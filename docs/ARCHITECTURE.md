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
   Then `ExperimentalSystem` (sequential): crush, delayed death blasts, mobile-factory carry, energy-per-shot charging.
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
  A new unit can add itself to existing build lists with `BuiltBy: [builder ids]` (appended to those builders'
  `BuildIndices` in load order; unknown builder ids are ignored). `UpgradesTo: "<id>"` declares an in-place
  structure upgrade (resolved to `UpgradesToIndex`, inverse `UpgradesFromIndex`; strict).
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

## Tech tiers & upgrades — `Rts.Sim/Economy/UpgradeSystem.cs`
- **Tiers** are just blueprint data: `Tech` 1–3, the `TECH1..3` categories, and build lists. T2/T3 engineers and
  factories list every lower-tier option plus their own (`Builds`, or `BuiltBy` in the built blueprint); higher tiers come from new builders
  or from in-place upgrades. Blueprint files load in filename order, so `uef_t2_*`/`uef_t3_*` sort after T1 and
  T1 indices stay stable.
- **In-place upgrade** (`UpgradesTo: "<id>"`, chains: land/air factory T1→T2→T3, mex T1→T2→T3, radar T1→T2→omni):
  `UpgradeCommand` sets `UnitStore.Upgrade[i]` (`UpgradeState`: target bp, fraction, per-tick reservation). The
  structure builds the TARGET blueprint's BuildTime/cost with its own BuildRate (mex/radar: `SelfRate` = 10 × tech)
  through the flow economy (`ConstructionSystem.Work` with `BuilderMode.Upgrade`: pay first, then progress).
  Engineers assist with Guard (or Repair at full HP). Factories pause their queue meanwhile (FactorySystem spends the
  build power on the upgrade instead); everything else keeps working as the old blueprint (a mex keeps producing).
  `CancelUpgradeCommand` refunds cost × fraction.
- **Completion** (sequential): same slot and `UnitHandle` (selection, AI references, queue, rally survive), `Bp`
  swapped, health keeps its fraction, footprint re-stamped if it changed, weapon states cleared and modules
  re-initialized via `UnitLifecycle.OnSpawn`. `StateHash` mixes upgrade state only while one is active.
- **Snapshot/UI**: `UnitView.UpgradeBp` (-1 = none) / `UpgradeFrac`; assisting units report it as `WorkProgress`.
  Client: upgrade tile + U hotkey (`UI/UpgradeUi.cs`, `UpgradePanel.cs`, factory panel), tier pills (T) in the build
  menu and factory panel, the target model as a growing hologram + `Rendering/UpgradeOverlay.cs` bar.
  `--tech-demo` spawns a showcase scene (`UI/TechDemo.cs`). Sandbox/debug: `DebugSpawnCommand` spawns any blueprint
  complete (covered for every T2/T3 blueprint by `UpgradeTests`).

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
- Line of fire (`Combat/LineOfFire.cs`, used by `TargetingSystem`): a NON-forced target is only acquired/kept if the
  weapon can hit it from where the unit stands: straight shots and beams need a clear muzzle→target line over the
  terrain/water surface (same grazing tolerance as projectiles), shells need an arc solution (`Ballistics.SolveArc`,
  as `WeaponSystem` uses). Homing/missiles, bombs, flak, torpedoes and silo weapons are not restricted; forced
  targets (Attack orders, TML/nukes, attack-ground) skip the check. Effect: units below a cliff no longer stop
  on attack-move to shoot into the rock; they walk up the ramp.

## Shields & strategic weapons (`Rts.Sim/Strategic`)
- `StrategicSystem.Update` is one sequential pipeline step (after movement, before targeting): interceptor prep →
  silo stockpiles + Launch orders (`SiloSystem`) → shields + `ShieldIndex` (`ShieldSystem`) → missile defenses
  (`MissileSystem`). Per-unit state lives in `StrategicState.Of(world)`; blueprint data in `UnitBlueprint.Strategic.cs`
  (`Shield`, `Silo`, `MissileDefense`). Zero cost when no strategic unit exists.
- **Shields** (ellipsoid domes; T2/T3 generators, `uef_t2_mobile_shield` follows its unit): HP, RegenRate after
  RegenDelay, RechargeTime after collapsing. Energy upkeep via the flow economy; `Econ.Efficiency` < 0.95 (stall)
  collapses them. `ShieldIndex` (32-unit grid, army mask per cell) is read in the parallel projectile phase: a
  projectile segment entering an enemy dome from OUTSIDE is absorbed at the surface and damages the shield; shots
  fired from inside pass out; area blasts outside a dome don't hurt units inside; beams are blocked on the
  muzzle→target line. Nukes ignore domes and knock every shield in their outer ring down.
- **Silos** (TML, nuke, anti-nuke): stockpile built pay-first like construction (MissileCost × dt/BuildTime per tick,
  scaled by efficiency, up to MaxStorage). `OrderType.Launch` (`Order.LaunchAt(pos)`) at the queue front fires one
  missile when stocked (TML: only within [MinRange, Range], else dropped); with an empty stockpile it waits.
  Silo/defense weapons are `WeaponKind.Manual`: never auto-fire. `SiloSystem.SetStock` for scenes/tests;
  `SiloSystem.InstantStockpile` = sandbox instant build (a free missile per tick up to MaxStorage).
- **Missiles** are ordinary projectiles registered in `StrategicState`: TML = scripted cruise path above the highest
  terrain, shield-stoppable; nuke = high arc anywhere, two-ring blast (`OuterDamage/OuterRadius`) queued by
  `MissileSystem.QueueDetonation` (+ the one `EffectKind.Nuke` effect) and applied at the start of DamageSystem; the
  next tick `StrategicSystem.Run` vaporizes props/wrecks in the inner ring (clean crater). TMD / anti-nuke fire homing interceptors at the nearest unchased enemy missile in Radius (rate
  limited; anti-nuke spends a stockpiled missile per shot).
- **Snapshot** (`Snapshot/StrategicViews.cs`): `Shields` (HP fraction, up, recharge fraction, last hit tick), `Silos` (kind, stock, progress),
  `Launches` + `LaunchSerial` (nuke launches, GLOBAL: not fog-filtered, drive the "STRATEGIC LAUNCH DETECTED"
  banner/klaxon); `EffectKind.Nuke` effects are also global (IntelSnapshotWriter never fog-filters them). Enemy missiles otherwise follow projectile fog rules.
- **Public entry point**: `StrategicApi.LaunchNuke(SimWorld w, int army, Vector2 from, Vector2 target,
  string siloBp = DefaultNukeSilo)` and `LaunchNuke(w, army, target)` (from the commander, else map center) launch
  a nuke without a silo (warning, interceptable, same blast); `StrategicApi.Detonate(w, army, center, rings…)` = the
  blast alone (no missile); `StrategicApi.NukeRings(db)` = the silo missile's rings (client previews). Sequential sim
  code only. The sandbox `nuke` command uses `LaunchNuke`, `nuke instant` uses `Detonate`.
- **Client**: `Rendering/ShieldRenderer.cs` (fresnel + hex dome shader, hit ripples, fade on collapse, fog-aware,
  shield HP bars), `Rendering/EffectsRenderer.Strategic.cs` (missile/nuke trails, interceptors, shield and
  launch/intercept bursts; the detonation itself is `NukeFx`, see Sandbox below), `Input/StrategicOrders.cs` (Launch mode: L hotkey /
  Launch button, picks one in-range launcher with most stock), `UI/StrategicHud.cs` (banner, target markers, range
  and blast rings, silo/shield bars), audio `GameAudio.Strategic.cs`. Visual test bed: `--scene strategic[:shields|:tml|:nuke]`.

## Experimentals & commanders (`Rts.Sim/Experimental`, `Rts.Sim/Commander`)
- **Experimentals** = blueprints with category `Experimental` and `Tech` 4 (build menu tier "EXP"). Optional
  `Experimental` block: `CrushRadius`/`CrushDamage`/`CrushMaxTech`/`CrushAllies`, `DeathDamage`/`DeathRadius`/`DeathDelay`.
  `ExperimentalSystem` (sequential, after MovementSystem; per-world `ExperimentalState`, plain lists/arrays) registers
  units in `UnitLifecycle.OnSpawn` (idempotent) and:
  - crush: a moving crusher damages complete enemy land units (Tech ≤ CrushMaxTech, not commander/experimental) within the
    radius (`DamageEvent`s, grid query sorted by index);
  - death blast: `OnUnitDeath` queues a `PendingBlast`; after DeathDelay it becomes an `AreaDamageEvent` + big `Explosion`
    effect (can't add area damage inside OnDeath: DamageSystem is iterating it). The wreck is the normal one
    (BuildCostMass × WreckMassFraction);
  - mobile factories (Fatboy: a mobile unit with the `Factory` category; FactorySystem handles it): the unit under
    construction rides inside; `FactorySystem.Release` lets it out at the back and doesn't copy the factory's own orders;
  - `EnergyPerShot` weapons (Mavor): charge pay-first through the unit's EnergyRequest (structures own it, builders add to it);
    `WeaponSystem` fires only with a full charge (`WeaponEnergy.TryConsume`, parallel-safe: own slot).
  Engineers ordered to build the same mobile unit at the same spot assist one site (`ExperimentalSystem.FindMobileSite`).
  Keep mobile `Radius` ≤ 3 (it sizes every unit's separation query; use `CrushRadius` for contact).
- **Enhancements** (`Enhancements` in the blueprint: Id, Slot LeftArm/RightArm/Back, Prerequisite in the same slot,
  cost/time, Visual/Icon, `Effects`: weapon modifiers, added weapons, health/regen/build rate/production/upkeep/vision,
  `BuildListFrom`, Shield, Silo). `EnhancementCatalog` (at `BlueprintDb.FinalizeDb`) generates one blueprint VARIANT per slot
  combination (id `base+Enh1+Enh2`, JSON clone + effects, prerequisite effects stack; `IsVariant`, `Installed`,
  `EnhancementVariants`). Installing/removing swaps `Units.Bp` in place (same handle, health fraction kept, modules re-init;
  `StrategicSystem.InitUnit` is idempotent and keeps the silo stock), so every system just reads the new blueprint.
  `BuildListFrom` merges other builders' final build lists (SCU/T3 Engineering from `uef_t3_engineer`, Fatboy from the T3
  land factory). Building one reuses `UpgradeState`/`UpgradeSystem`: `EnhanceCommand` stops the unit and targets the variant,
  `UpgradeSystem.Cost` returns the enhancement's cost/time, the unit works on itself with its BuildRate, engineers assist
  with Guard, `CancelUpgradeCommand` refunds, any new order cancels it; `RemoveEnhancementCommand` clears a slot at once
  (no refund). The snapshot's `UpgradeBp`/`UpgradeFrac` carry the progress (`EnhancementCatalog.Added` = which one).
  Client: `UI/EnhancementPanel.cs`, model attachments in `Rendering/UnitModels.Experimental.cs` (from `Installed` + `Visual`).
- **Quantum Gateway / SCU**: a T3 factory building `uef_scu` (engineer + cannon with its own enhancements). The SCU has no
  `Commander` category: assassination only looks at `Army.Commander`.
- Client: `UI/ExperimentalAlert.cs` warns once per enemy experimental seen in the fog-filtered snapshot (banner + sound);
  `--scene experimental` shows them all. Sandbox lists hide variants (exact variant ids still spawn).

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
- Archipelago instead gets sea lanes (`MapBuilder.Archipelago.cs`): `EnsureSeaLanes` labels the deep water eroded to
  battleship clearance (r=3) and digs symmetric channels (coarse Dijkstra; only the cheapest one per round, so few
  land bridges are cut) or inlets until every start has the same big sea within reach; `ShapeBeaches` cuts a
  slope-limited "harbor beach" from each start plateau to its sea (the plateau core is kept) so builders can walk
  to the coast. Tests in `NavalTests` (island size, naval site near each start, battleship-sized sea lane).

## Naval (`Rts.Sim/Naval`)
- Water structures (Structure + `MinWaterDepth` > 0: naval factories T1–T3, sonar, torpedo launcher):
  `NavalRules.CellBuildable` (every footprint cell needs that much water) is used by `OccupancyGrid.CanPlace` and
  by the client ghost (`PlacementGrid`, `PlacementError.NeedsDeepWater`); `NavalRules.FindSite` = nearest valid
  site (AI/tests/scenes). They sit at the water surface. Builders on land walk as close as they can (an unreachable
  goal shrinks the arrive radius by the gap, `MovementSystem`), then build within range.
- Torpedoes (`Naval/Torpedoes.cs`): weapons with `ProjectileVisual` "torpedo" (`WeaponKind.Torpedo`,
  `ProjectileFlags.Torpedo`) drop into the water, run at `RunDepth`, home on the target, hit by a 2D swept circle
  on the Water/Sub layers and die when the sea floor gets close (shore/shallows). Torpedo defense ("antitorpedo",
  `WeaponKind.AntiTorpedo`, never targets units): `Torpedoes.Intercept` (sequential, before projectiles move)
  destroys one torpedo in range per weapon cooldown.
- Subs (`MotionLayer.Sub`, layer `LayerMask.Sub`) are visible only with sonar/omni coverage (`IntelService`), and
  only weapons with the Sub layer (torpedoes) can target them: land/air guns never acquire them.
- Client: models in `Rendering/UnitModels.Naval.cs`; `EffectsRenderer.Naval.cs` (torpedo body + surface streak +
  bubble trail, splash/hit/interception bursts, ship wakes scaled by speed and hull), `UnitRenderer.Submerged.cs`
  (submerged units drawn after the water, see-through and blue-tinted), `PropRenderer.Naval.cs` (wrecks on deep
  water sink and fade), sonar ring in `BuildPlacement`. `--scene naval` (`NavalScene.cs`): scripted Archipelago
  fleet battle + naval factory (`--select uef_t1_naval_factory`) for screenshots and tests.

## Skirmish AI (`Rts.Sim/AI`)
- Runs inside the deterministic sim (sequential, AliveList order, DetRandom, no Dictionary/HashSet iteration) for
  armies with `Army.IsAI && !Defeated`, and acts ONLY through normal `Command`s (`AiCommands`), like a player.
  State per world via ConditionalWeakTable (`AiSystem.StateOf(w)`): one `AiBrain` per army + per-unit-slot
  `AiUnitInfo` (role/task, generation-checked).
- `AiBrain` thinks on a staggered schedule (economy every `EconInterval`, military every `MilitaryInterval` ticks,
  offset by army index); difficulty (`Army.AiDifficulty`: Easy/Normal/Hard) selects an `AiConfig`. No difficulty
  cheats (no resource bonus, no map vision): Hard is better through faster reactions, a stronger opening (5 early
  engineers, mex chains), an artillery-heavy land mix (`ArtilleryShare` 0.55 vs 0.25: by far the biggest lever),
  raids, commander snipes and micro (damaged units pulled back for repair, focus fire).
  Cost ≈ 0.02-0.03 ms/tick per AI (worst game ≈ 0.05).
- `AiPerception` (fog-fair, `RespectIntel=true`): the only window on enemies. Enemy units count only while seen or on
  radar (radar blips = anonymous, assumed one tank), structures are remembered as ghosts until seen dead, threat maps
  = current threat + decaying memory, per-area staleness (when did we last look), enemy commander last seen (only
  when actually visible). `AiScouting`: land/air scouts to stale areas, radar at base then expansions.
- `AiEconomy`: commander opening (factory, mexes, pgens, hydro), engineer jobs (finish orphan sites, power when
  income < max(smoothed demand, projected spending), mexes nearest the base first + chains, hydro, factories when
  mass income/floating allows (more land factories while floating), air factory mid-game, PD/AA, storage/radar,
  repair, reclaim, assist factories), task deadlines (stuck → reassign, deposit blacklist), engineers flee
  threats; factory queues (engineer target, scouts, tank/artillery/mobile-AA mix by enemy air; interceptors/bombers).
- `AiPlacement`: spiral search with `OccupancyGrid.CanPlace` + free margin (3 factories / 2 others, relaxed pass),
  keeps deposits and the rally point clear, land-reachability for factories, reservations, failure cooldown.
- `AiMilitary`:
  - Pool at the rally point → one main army (`AiGroup`, Assemble/Attack/Retreat). Objective = best reachable known
    enemy structure (value by type − distance, outgunning defenses avoided, enemy base avoided before `PushTime`,
    base/clusters preferred when pushing), else the enemy commander's last known position, else stale enemy starts;
    unreachable/stuck targets blacklisted 150 s.
  - Staging point `StageDistance` short of the objective along the walking route (`AiBrain.SolveRoute`), outside
    known defenses. Launch when gathered and strength ≥ `RequiredStrength` (AttackMargin × (defenses at the target +
    EnemyArmyShare × rest of the estimated enemy army)), or when at `MaxArmy`, or after `AssembleTimeout`.
  - Attacking: reinforcements walk in batches to fixed points (staging, then the target) and merge near the army
    anchor; strung-out armies regroup; idle members engage the nearest known enemy; position targets end after
    `PositionTargetTime` when dominating; next objective nearby → keep going, far → stage again. No difficulty
    retreats a beaten army (matrix: retreating lost more games than fighting on); the army is recalled when the base
    is losing (not when finishing the enemy). Late game (`LateGameTime`): looser launch rule, snipes for everyone.
  - Defense: pool + defenders vs base/expansion threats, nearby army members help vs real base attacks; sorties to
    an attacked expansion; clearing groups for enemies on our deposits; commander retreats when hurt.
  - Hard: raids on weakly defended enemy extractors, commander snipe (direct-fire members focus a visible enemy
    commander near the army), pull-back of units under 25 % health, focus fire (`PickFocusTarget`: each direct-fire
    member takes the weakest identified enemy in range after the damage already assigned, FocusHorizon = 3 s).
  - Air: interceptor patrols, bomber groups on raid targets; air scouts.
  - Navy/T2/T3/strategic weapons are not used yet (wave 4); on Archipelago the AI builds land units that stay on
    their island (no crash, no command spam).
- Results (bench, 30 min, 256 maps, 4 land presets): Normal vs Normal ends by commander kill in ~86 % of games
  (mean ≈ 18.6 min); Hard beats Normal in 86 % of games (166 won / 8 lost / 18 undecided of 192), Easy loses to
  Normal in 23 of 24; Hard vs Hard ends in ~69 %; 4-player FFAs on 512 end by ~30 min. Stalemates are equal armies
  trading in the middle of Highlands/Canyon maps. T1 mobile artillery is very cost-efficient (balance pass later).
- Bench: `dotnet run -c Release --project tools/Rts.Bench -- ai [minutes] [map] [seed] [diff0,diff1,...] [threads] [players] [preset]`
  (`aipassive` = army 0 idle). Env: `AI_STATUS_SECONDS` (status lines), `AI_GROUPS=1`, `AI_TRACE=1` (decisions),
  `AI_DUMP`, `AI_DEPOSITS`, `AI_OMNI=1` (no fog), `AI_CFG="Field=value,..."` / `AI_CFG<army>` / `AI_CFG_<Difficulty>`
  (override `AiConfig` fields for sweeps; bench only, the sim never reads the environment).

## Client (`Rts.Client`)
- `GameApp`: options + window + screen loop (`Menus/AppHost`, `IScreen`): `MainMenuScreen` → `SettingsScreen` /
  `SkirmishSetupScreen` (slots, map settings, live `MapPreview`) → `LoadingScreen` (sim built on a worker thread) →
  `InGameScreen` (pause menu on Esc, end screen with `ArmyStats`). CLI game arguments still start a game directly.
- `GameSession`: one running game (runner, camera, renderers incl. fog/wrecks, input, HUD, cursor hints,
  `GameAudio`); `Frame()` is the in-game frame. Add per-game client modules there (field + constructor + `Frame` +
  `Dispose`). `GameSession.CreateSimulation(cfg)`: `GameConfig` slots → `SkirmishSettings` (AI slots set
  `Army.IsAI` + `AiDifficulty`, so the in-sim skirmish AI drives them); fog `ViewArmy` = the human slot's army.
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
  `AudioSystem` (app-wide, created once by `GameApp`): device + sound bank (synthesized once) + ambient stream +
  `AudioSettings` volumes; each game's `GameAudio` drives it, menus call `MenuUpdate` (clicks + music). Volumes
  come from `AppSettings` (settings screen, saved) unless `--volume/--sfx-volume/--music-volume` are given.
- `Rendering/FogRenderer`: fog of war on the client. Uploads `SimSnapshot.Fog` into two R8 textures (previous/current
  tick, bilinear) sampled by terrain/water/unit shaders via `Shaders.FogOfWarFs` (unexplored dark, explored
  dimmed/desaturated, 5-tap soft edges, blended by the interpolation alpha); radar blips as army-colored ring sprites;
  minimap fog/ghosts/blips; ghost structures are drawn greyed by `UnitRenderer`/`IconRenderer`. F7 / `--nofog`: reveal
  all. Picking/selection need no special code: unseen enemies are simply not in the snapshot.
- `Input/SelectionController`: selection, control groups, right-click orders, attack-move and patrol modes;
  `Input/ContextOrders` (engineers: wreck → reclaim, damaged/unfinished → repair, factory → assist) + `UI/CursorHint`.
- `UI/Hud`: resources, selection summary, help.

## Sandbox mode (`Rts.Sim/Systems/SandboxSystem`, `Rts.Client/Sandbox`)
- Sim: `SandboxSystem.Enable(world)` (called by `GameSession.CreateSimulation` when `GameConfig.Sandbox`) turns the
  `SandboxCommand`s on (`Orders/SandboxCommands.cs`; ignored in a normal game): batched spawns (250 units/tick, square
  spiral, structures on free footprints), per-army toggles (infinite resources, instant build, build anywhere),
  AI on/off + difficulty at runtime, victory on/off (off = `WinningTeam` stays -1), unit actions (kill, delete, heal,
  damage, change owner, finish), clear area, nuke. `SandboxSystem.Nuke` launches a REAL strategic missile
  (`StrategicApi.LaunchNuke`, free) from the army's nearest strategic silo, else its commander, else the map edge
  nearest its start position (`NukeLaunchSite`): same flight, global launch warning, anti-nuke interception, shield
  knock-down and crater as a silo launch. `SandboxNukeCommand.Instant` (`nuke instant`) = `NukeInstant`: the same
  strategic detonation right away (`StrategicApi.Detonate`, rings 30/70 000 + 40/500), no missile. Instant build also
  fills silo stockpiles (`SiloSystem.InstantStockpile`), so with money + instabuild a silo nuke is ready at once. Like every command
  they are applied at the start of a tick: deterministic and replay-safe. `SimSnapshot.Sandbox` = immutable `SandboxStatus`.
- Client: `SandboxController` (owned by `GameSession`, null outside sandbox) = side panel (`SandboxPanel`, F9: army
  selector, cheats, AI, speed, selection actions, NUKE (missile) / Blast (instant) / clear tools, spawn list of every
  blueprint grouped by category/tech — shields, silos, missile defense and artillery installations in a "Strategic &
  shields" group — with search) + console (`SandboxConsole`, F10 or the key left of 1) + map tools + `--exec` script.
  Panel and console both produce `ConsoleAction`s (`ConsoleActions.cs`) run by `SandboxController.Execute`.
  `SandboxConsoleParser` / `BlueprintMatcher` are pure (no raylib) and unit-tested in `Rts.Client.Tests`.
  While a sandbox text field has focus: `SelectionController.InputBlocked`, camera input off, debug/fog/build hotkeys
  skipped; F9/F10 effect debug keys are disabled in sandbox (`EffectsRenderer.DebugKeysEnabled`).
  `PlacementGrid.IgnoreDeposits` mirrors the local army's build-anywhere. `InGameScreen` skips the end of game unless
  victory is on. Entry points: main menu "Sandbox" (skirmish setup with an "Enemy AI" switch, default off), `--sandbox`,
  `--exec "cmd; cmd"` (implies `--sandbox`; `wait <frames>` for screenshots).
- Nuke visuals (ONE effect for every nuke: silo missiles, sandbox missiles, sandbox instant blasts): `EffectKind.Nuke`
  → `Rendering/NukeFx.cs` (self-contained, owned by `EffectsRenderer`: `Spawn` on the event, `Update`/`Build` in the
  particle pass, `DrawOverlay` = 2D screen flash right after `EndMode3D`, strength by on-screen/camera distance, none
  for far off-screen blasts): flash, draped shockwave/dust rings, fireball, mushroom cloud (private sorted smoke-puff
  pool), sparks/debris, scorch decal (90 s). Sound: ONE map-wide synthesized `SoundId.NukeExplosion`
  (`GameAudio.Strategic.cs` `PlayNuke`, ≈1/distance with a floor).
