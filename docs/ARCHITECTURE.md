# Architecture

Goal: Supreme Commander 1–style RTS: huge maps (5–20 km), thousands of units, strategic zoom, flow economy,
T1–T3 + experimentals, land/air/naval, deterministic lockstep-ready simulation, multithreaded.

## Threads
```
Main thread (raylib)                    Sim thread (SimRunner)               Worker pool
---------------------                   ----------------------               -----------
input -> Command ----ConcurrentQueue--> drain, sort (army, seq)
                                        Simulation.Step(commands)  ------->  Par.For chunks of systems
render <---- SimSnapshot (pooled) ----- SnapshotWriter.Write
interpolates prev/cur by time
```
- Sim runs at 10 ticks/s × game speed. The renderer interpolates between the last two snapshots (one tick of
  latency, as in SupCom) so motion is smooth at any FPS.
- Snapshots are pooled: `TakeLatest()` and then `Return()` (the `SnapshotInterpolator` handles this).

## Tick pipeline (`Simulation.Step`, fixed order)
1. `CommandSystem`: validate and apply player commands (orders, factory queues, debug spawn); then
   `AiSystem` (skirmish AI, `Rts.Sim/AI/`) lets AI armies think and its commands go through `CommandSystem` too.
2. Rebuild `AliveList`s, then `SpatialGrid.Rebuild` (counting sort, 8-unit cells).
3. `IntelSystem`: fog of war, radar, sonar, omni per team (see Intel model).
4. `EconomySystem`: sum the production and the requests from the previous tick, compute efficiency, update storage.
5. `OrderSystem` (parallel): the front order of each mobile unit becomes a movement goal and forced targets.
6. `ConstructionSystem`: engineers build, repair, reclaim and assist (see Economy model).
7. `FactorySystem`: build queues, unit release, rally orders.
8. `PathSystem`: resolve path requests (HPA*-style sector regions + cached region goal fields + corridor A* +
   string pulling; formation members splice a shared leader path; deterministic per-tick solve cap).
   See `Pathfinding/PathService.cs` for the design.
9. `MovementSystem` (parallel, double-buffered `ScratchPos`): waypoint following, turn rate, priority separation
   (movers push idle friends), formation pace, stuck → repath, air orbiting, terrain/structure collision.
10. `TargetingSystem`, `WeaponSystem`, `ProjectileSystem` (combat, see below; code in `Rts.Sim/Combat/`).
11. `DamageSystem`: apply damage events in order and kill units.
12. `CleanupSystem`: free dead slots, assassination rule (commander dies -> army defeated), victory.

## Data model (`Rts.Sim/World`)
- `UnitStore`: fixed-capacity SoA. The core arrays are `Pos`, `Heading`, `Health`, `Bp`, `Army`, `Flags` and
  `BuildFraction`. Module arrays of structs: `Move`, `Weapons` (unit × `MaxWeapons`), `Orders` (`OrderQueue`),
  `Builder`, `Factory` and `Econ`. `UnitHandle(Index, Generation)` detects recycled slots; use `IsActive(h)`.
- `ProjectileStore`: SoA, free list.
- `Terrain` (immutable): heightmap at the corners of 1×1 cells, water level, `PassFlags` per cell, deposits,
  start positions. `OccupancyGrid` (mutable) marks the cells covered by structures.
- `BlueprintDb`: JSON blueprints. Categories become `UnitCategory` flags; `Builds` becomes `BuildIndices`.
- New per-unit state: add a field to the module struct in `World/Components.cs` and initialize it in your
  system's `InitUnit` (called from `UnitLifecycle.OnSpawn`).

## Economy model (SupCom flow economy) — `Rts.Sim/Economy/`
- **Production/upkeep** come from blueprints: complete, non-`Paused` units produce `ProductionMass/Energy` and pay
  `MaintenanceEnergy`. Units with upkeep (mass extractors) have their production scaled by energy efficiency.
  Storage max = sum of `StorageMass/Energy` of complete units (the commander gives 650/4000; games start full).
- **Requests**: consumers set `Econ[i].MassRequest/EnergyRequest` (per second) during tick T. `EconomySystem` (tick
  T+1, before consumers) sums everything per army (per-chunk partial sums merged in chunk order), computes
  `efficiency = min(1, (stored + income·dt) / (demand·dt))` per resource, writes `Econ[i].Efficiency` (min over the
  resources that unit requested, 1 if none) and deducts exactly `request × Econ[i].Efficiency`.
- **Pay first, then progress**: builders store the requested work in `Builder[i].PendingWork`; ConstructionSystem
  applies it at T+1 scaled by `Econ[i].Efficiency`, so resources spent always equal work done. Assisting builders
  reserve work on the target (`Builder[target].ReservedWork`) so they never overshoot. Any upkeep consumer
  (radar, shields, energy weapons) should read `Econ[i].Efficiency`.
- **Construction** (`ConstructionSystem`, sequential): front Build/Repair/Reclaim order of engineers/commander, and
  Guard = assist (factory's current unit, an engineer's site, finish a site, or repair). Build range = 5 + target
  half size (+2 commander). Mex/hydro sites snap to the nearest deposit within 4. Same unfinished allied structure
  already at the site → assist; otherwise invalid placement → order dropped. Own units are pushed out of a new
  footprint. `Order.State/Spawned` hold the site handle (Build) or the prop handle (Reclaim of a prop).
- **Factories** (`FactorySystem`): front of `Factory.Queue` is spawned inside as `UnderConstruction`; on completion it
  is moved to the exit (factory heading) and gets a copy of the factory's order queue (rally) or a short move.
  Repeat re-appends; Dequeue of the item in progress cancels it with a refund; factory death kills the unfinished unit.
- **Wrecks/props** (`World/PropStore`, `SimWorld.Props`): complete units leave a wreck worth
  `BuildCostMass × WreckMassFraction` (unfinished or reclaimed units leave none). Reclaim speed = 2 mass/s per point
  of BuildRate; reclaimed resources arrive as income next tick (`Econ[i].ReclaimMass/Energy`). New structures absorb
  props under their footprint. The client sees them as `SimSnapshot.Props` (`PropView`); to reclaim one, send
  `Order.ReclaimAt(UnitHandle.None, pos)`.

## Intel model (fog of war, `Rts.Sim/Intel`)
- Intel is per TEAM (allies share everything). `IntelService` (`world.Intel`) keeps per team bitsets over a grid of
  2×2-unit cells: vision, radar, sonar, omni, plus an explored byte grid (0 unexplored / 128 explored / 255 visible).
- `IntelSystem` recomputes everything each tick: sequential gather of complete units (structures and aircraft
  included) counting-sorted by (team, row), then PARALLEL work items = (team, band of 16 rows), heaviest first; each
  item writes only its own rows (deterministic, no atomics). Coverage is padded by half a cell diagonal: a unit within
  VisionRadius of a viewer is always visible. Radar/sonar/omni with energy upkeep are off when paused or stalled.
- Queries (thread-safe): `IsVisible(army, unit)` (vision; submerged units also need sonar/omni), `IsDetected`
  (vision, or radar for land/air/surface, sonar for surface/sub, omni for all), `IsBlip`, `IsPositionVisible/
  Explored/OnRadar`, `GetKnownStructures` (ghosts: last-seen enemy structures, forgotten when re-scouted dead).
  Own/allied units are always visible; `Intel.Enabled = false` reveals everything.
- Targeting only acquires and keeps targets the army detects (`CombatState.CanSee`), forced Attack targets included.
- Snapshot: `SimRunner.ViewArmy` (client sets the local army; -1 = no fog). `IntelSnapshotWriter` filters the snapshot:
  `Units` only has units the view army sees (enemy orders/queues stripped), radar contacts go to `Blips` (position,
  army, velocity; no type/handle so they can't be selected or targeted as units), `Ghosts`, enemy projectiles and
  effects only in vision, and the explored grid in `Fog` (`FogCols × FogRows`, `FogVersion`).

## Combat model (`Rts.Sim/Combat`)
- `CombatState.Of(world)` (attached via ConditionalWeakTable): per-tick cache of each unit's layer and a per-grid-cell
  army bitmask, so scans/collisions skip cells without enemies. `CombatCatalog`: per-blueprint weapon kinds
  (Direct, Homing = "missile", Ballistic, Bomb = ballistic + MuzzleVelocity 0, Beam = MuzzleVelocity 0).
- Targeting priority: units that can shoot back at the shooter's layer, then other armed units, unarmed mobile,
  armed structures, other structures; then distance, then index. Attack orders force targets; attack-ground
  uses `WeaponState.HasGroundTarget`. Aircraft on attack-move/patrol chase their engaged target (OrderSystem).
- Weapons lead moving targets (`MoveState.Velocity`); artillery (IndirectFire) uses the high ballistic arc;
  bombs are released when the free-fall impact point lands on the predicted target position.
- Projectiles: straight shots hit the first enemy collision sphere on their path, shells/bombs explode on the
  ground, flak (`ProjectileFlags.ProximityFuse`) bursts near aircraft. Area damage uses
  `AreaDamageEvent.Layers` (= weapon TargetMask) so flak only hurts aircraft and bombs don't hit planes.

## Map generator (`Rts.Sim/Map`)
- `MapGenerator.Generate(MapGenSettings)` → immutable `Terrain`. Presets (`MapPreset`): Plains, Lakes, Highlands
  (terraces, cliffs, natural + carved ramps), Canyon (valley network in mesas), Archipelago (islands, needs naval).
  `WaterAmount` 0..1 (negative = preset default). Stages in `MapBuilder*.cs`: symmetric noise fields
  (`MapSymmetry`: N-fold rotation about the center, sector blending), preset relief, lakes (percentile of a lake
  field), flat start plateaus, connectivity repair (`MapConnectivity`: land components with clearance 2, coarse
  Dijkstra, slope-limited corridor carving in all N rotations), deposits (base / expansion clusters / scattered,
  validated on flat main-component land in every rotated copy), final flood-fill verification.
- Guarantee (tests in `MapGeneratorTests`): on every preset except Archipelago all starts are connected over
  `Terrain.CanTraverse(Land)`; deposits are buildable; start areas are flat; deterministic per seed; 1024 < 0.5 s.

## Skirmish AI (`Rts.Sim/AI`)
- Runs inside the deterministic sim (sequential, AliveList order, DetRandom, no Dictionary/HashSet iteration) for
  armies with `Army.IsAI && !Defeated`, and acts ONLY through normal `Command`s (`AiCommands`), like a player.
  State per world via ConditionalWeakTable (`AiSystem.StateOf(w)`): one `AiBrain` per army + per-unit-slot
  `AiUnitInfo` (role/task, generation-checked).
- `AiBrain` thinks on a staggered schedule (economy every `EconInterval`, military every `MilitaryInterval` ticks,
  offset by army index); difficulty (`Army.AiDifficulty`: Easy/Normal/Hard) selects an `AiConfig` (think speed,
  caps, opening, wave timing/size, retreat/pull-back micro, reinforcement). Cost ≈ 0.01 ms/tick per AI.
- `AiPerception`: the only window on enemies (scan → `AiView` lists, surface/anti-air threat maps, remembered enemy
  structures). `RespectIntel=false` = omniscient for now; set true to obey `IntelService` (fog of war).
- `AiEconomy`: commander opening (factory, mexes, pgens, hydro), engineer jobs (finish orphan sites, power when
  income < max(smoothed demand, projected spending), mexes nearest the base first + chains, hydro, factories when
  mass income/floating allows, air factory mid-game, PD/AA, storage/radar, repair, reclaim, assist factories),
  task deadlines (stuck → reassign, deposit blacklist), engineers flee threats; factory queues (engineer target,
  scouts, tank/artillery/mobile-AA mix by enemy air; interceptors/bombers).
- `AiPlacement`: spiral search with `OccupancyGrid.CanPlace` + free margin (3 factories / 2 others, relaxed pass),
  keeps deposits and the rally point clear, land-reachability for factories, reservations, failure cooldown.
- `AiMilitary`: rally pool → attack waves (growing size) to the best reachable known enemy structure
  (`PathService.CanReach`), retarget/stuck handling, retreat when beaten, base/expansion defense pulls nearby units,
  clearing enemies off our deposits, commander retreats when hurt, interceptor patrols, bomber raids, scouts.
- Bench: `dotnet run -c Release --project tools/Rts.Bench -- ai [minutes] [map] [seed] [diff0,diff1] [threads] [players]`
  (`aipassive` = army 0 idle; env `AI_STATUS_SECONDS`, `AI_GROUPS=1` for more output).

## Client (`Rts.Client`)
- `GameApp`: options + window + screen loop (`Menus/AppHost`, `IScreen`): `MainMenuScreen` → `SettingsScreen` /
  `SkirmishSetupScreen` (slots, map settings, live `MapPreview`) → `LoadingScreen` (sim built on a worker thread) →
  `InGameScreen` (pause menu on Esc, end screen with `ArmyStats`). CLI game arguments still start a game directly.
- `GameSession`: one running game (runner, camera, renderers, input, HUD); `Frame()` is the in-game frame.
  Add per-game client modules there (field + constructor + `Frame` + `Dispose`).
- `RtsCamera` (zoom to cursor, strategic view) · `SnapshotInterpolator`
- `Rendering/`: `TerrainRenderer` (chunk meshes + per-pixel terrain shader, sky, animated water, picking),
  `UnitRenderer` (GPU-instanced procedural models from `UnitModels`/`MeshBuilder`, one draw per blueprint mesh,
  team color per instance, turrets, construction holograms, sun `ShadowMap`), `EffectsRenderer` (projectiles +
  pooled `ParticleSystem` for `EffectEvent`s, `QuadBatch` quads), `IconRenderer` + `IconAtlas` (procedural
  strategic icons, crossfade with meshes), `Shaders` (GLSL 330 sources, `Atmosphere` lighting/haze),
  `FrameView` (per-frame matrices, frustum culling), `PerfOverlay` (frame time, F8).
  Unit model vertex colors: rgb = albedo, alpha = material tag (255 metal, 0 team color, 128 glow).
  Debug: `--gallery` (all shapes), `--fxtest`/F9 (fake battle effects), F10 (explosion), `--stress N`.
  `PropRenderer` (wrecks from `SimSnapshot.Props`: the unit's model with a burnt-metal shader, sunk/tilted, embers +
  smoke when fresh, sinking while reclaimed; gray dots in the strategic view). Effects/wreck animations run in game
  time (`EffectsRenderer.GameDt`: frozen while paused, faster at 2x).
- `Audio/`: everything synthesized at startup (no asset files). `Synth` (DSP layers) + `SoundBank` (recipes, variants,
  gains/voice limits) → raylib Sounds with alias voice pools (`AudioEngine`); `AmbientSynth` renders wind, generative
  music and reclaim/build hums into a real-time AudioStream; `GameAudio` maps snapshot `EffectEvent`s (incl.
  `WeaponIndex`) to positional one-shots (listener = camera focus, zoom ducking, pan, per-frame merging) and raises
  alerts (under attack, stall, unit ready). `UiSounds` = click/confirm/error requests. `--nosound`; no device = silent.
- `Rendering/FogRenderer`: fog of war on the client. Uploads `SimSnapshot.Fog` into two R8 textures (previous/current
  tick, bilinear) sampled by terrain/water/unit shaders via `Shaders.FogOfWarFs` (unexplored dark, explored
  dimmed/desaturated, 5-tap soft edges, blended by the interpolation alpha); radar blips as army-colored ring sprites;
  minimap fog/ghosts/blips; ghost structures are drawn greyed by `UnitRenderer`/`IconRenderer`. F7 / `--nofog`: reveal
  all. Picking/selection need no special code: unseen enemies are simply not in the snapshot.
- `Input/SelectionController`: selection, control groups, right-click orders, attack-move and patrol modes;
  `Input/ContextOrders` (engineers: wreck → reclaim, damaged/unfinished → repair, factory → assist) + `UI/CursorHint`.
- `UI/Hud`: resources, selection summary, help.
