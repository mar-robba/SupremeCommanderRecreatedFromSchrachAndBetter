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
   Then `TransportSystem` (sequential, `Rts.Sim/Transport/`): load/unload/ferry/dock orders, fuel, carriers carry
   their cargo (see Transports & cargo) — before the rebuild, so units that board leave the map the same tick.
2. Rebuild `AliveList`s (carried units are skipped), then `SpatialGrid.Rebuild` (counting sort, 8-unit cells).
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

## Factions (`Rts.Sim/Data/FactionDb.cs`, `data/factions/*.json`) — contract for faction/AI agents
- A faction = `data/factions/<id>.json`: `Id` (lowercase, = file name = the blueprint id prefix of its units, e.g.
  `cybran` → `cybran_t1_tank`), `Name`, `Commander` (blueprint id of the starting commander), `Color` ("#RRGGBB" UI
  accent), `Description`, `Roles` {role → blueprint id}. Loaded with the blueprints into `BlueprintDb.Factions`
  (`FactionDb`: file order = ordinal id order, ids case-insensitive, immutable, readable from any thread). Loading
  throws `InvalidDataException` for an unknown role name, a role/commander pointing to a missing blueprint, a bad
  color, duplicate ids or the reserved id `random`. `uef.json` is the reference file.
- `UnitBlueprint.Faction` = JSON `"Faction"`, else the id prefix before the first `_` (`BlueprintDb.FactionOfId`).
- `SkirmishSettings.Factions[army]` (null/"" = `uef`, `random` = `FactionDb.Resolve(choice, seed, army)`, the same on
  every machine; unknown id → `InvalidOperationException`) → `Army.Faction` (saved with the game, so loads and replays
  keep it) → the army starts with its faction's `Commander`. Exposed as `ArmyView.Faction` (snapshot) and
  `PlayerInfo.Faction` (save/replay header).
- **Rule: sim/AI code never hard-codes unit ids** (`"uef_..."`): it asks a role of the army's faction:
  `w.Blueprints.Factions.RoleBlueprint(w.Armies[a].Faction, FactionRoles.TankT1)` → blueprint index, -1 = none
  (`FactionBlueprint.Role(role)` on a resolved faction). `grep -rn '"uef_' src/Rts.Sim` must only find doc comments.
  Tests, client visual scenes (`--scene ...`) and data files may use ids. Example: `AiBrain` resolves its `Bp*` fields
  from roles once per brain; `StrategicApi.NukeSiloOf(w, army)` = the faction's `nuke_silo` (else the first faction
  that has one).
- Roles are `FactionRoles` constants. **Required** roles must be filled by every shipped faction (AI/sim code may rely
  on them); **Optional** roles (`FactionRoles.Optional`) are faction-specific units, code using them must handle -1.
  Tests (`FactionTests`): `EveryFactionFillsEveryRequiredRole`, `EveryFactionBlueprintHasARole` (every non-variant
  blueprint of a faction is its commander or some role, so the AI can find it), `RoleListsAreConsistent`.
  - Required: engineer_t1, engineer_t2, engineer_t3, scu, scout_t1, tank_t1, artillery_t1, mobile_aa_t1,
    amphibious_t2, heavy_tank_t2, mml_t2, mobile_aa_t2, assault_t3, artillery_t3, air_scout_t1, interceptor_t1,
    bomber_t1, transport_t1, gunship_t2, torpedo_bomber_t2, transport_t2, air_staging, air_superiority_t3,
    spy_plane_t3, strat_bomber_t3, frigate_t1, sub_t1, destroyer_t2, cruiser_t2, battleship_t3, land_factory_t1,
    land_factory_t2, land_factory_t3, air_factory_t1, air_factory_t2, air_factory_t3, naval_factory_t1,
    naval_factory_t2, naval_factory_t3, quantum_gateway, mex_t1, mex_t2, mex_t3, pgen_t1, pgen_t2, pgen_t3, hydro,
    mass_fab_t2, mass_fab_t3, mass_storage, energy_storage, pd_t1, pd_t2, aa_t1, aa_t2, aa_t3, torpedo_t1, shield_t2,
    tml, tmd, artillery_installation_t2, artillery_installation_t3, nuke_silo, anti_nuke, radar_t1, radar_t2, omni_t3,
    sonar_t1, sonar_t2, exp_land.
  - Optional: mobile_shield_t2, mobile_stealth_t2, siege_t3, sniper_t3, fighter_bomber_t2, gunship_t3, carrier_t3,
    pd_t3, torpedo_t2, shield_t3, stealth_field_t2, exp_land_2, exp_air, exp_naval, exp_artillery.
- **New faction** (e.g. Cybran): add `data/factions/cybran.json` + `cybran_*` blueprints in `data/blueprints/units`
  (build lists via `Builds`/`BuiltBy` inside the faction); fill every required role and the optional ones it has; a
  unit that fits no role gets a new (optional) role. Nothing else: the client lists it automatically (setup FACTION
  column, sandbox FACTION filter, `--faction cybran`), the AI plays it through roles.
- **AI agents** ask roles, never ids; prefer required roles, fall back when an optional role is -1 (e.g. no mobile
  shield → skip it, no `exp_air` → build `exp_land`).
- **New role**: a constant in `FactionRoles` + an entry in `FactionRoles.All` (+ `Optional` if not every faction has
  one) + the role in every `data/factions/*.json` + the lists above. Role names are lowercase `<kind>_t<tier>`.
- Client: setup screen FACTION column per slot (`GameConfig.FactionChoices` = every faction, then Random; unknown ids
  block the start via `GameConfig.Problem(factions)`), `--faction ID` (local player) / `--factions a,b,...` (slots in
  order), sandbox spawn panel FACTION filter (`SandboxPanel.MatchesFaction`), slot factions kept in `ClientGameData`
  and rebuilt from save headers.

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
  gets a copy of the factory's order queue (rally) or a short move. Repeat re-appends; Dequeue of the item in progress
  cancels it with a refund; factory death kills the unfinished unit.
  **Drive-out** (`FactoryExitMotion`): a finished surface unit starts at the factory center and, while
  `Move.LeaveTime > 0` (cap 8 s), MovementSystem steers it straight to `Move.FactoryExit` (front, else first free side; a side with a parked unit
  — no move goal — on its exit lane is used only if every open side has one)
  across the factory's own blocked footprint; then it repaths to its rally. Aircraft take off from the pad (spawned
  at ground height, they climb). Mobile factories (Fatboy) drop units straight at their back (no drive-out, no rally).
- **Adjacency** (`AdjacencyRules` values, `AdjacencySystem`, sequential, right before EconomySystem): writes
  `Econ[i].AdjMassProd/AdjEnergyProd/AdjEnergyUse` per structure. Power generators cut the energy use of touching
  structures (T1 pgen 10% / hydro 12.5% / T2 15% / T3 20% per full side, total cap 75%); mass storage → extractors
  +12.5% mass, energy storage → pgens +12.5% energy per side; all scaled by shared edge / receiver side length. Recompute
  only on change: candidate list rebuilt when `OccupancyGrid.Version` changes, a hash of (index, gen, bp, army, active,
  complete) decides a full recompute (build complete, death, capture, upgrade swap). The per-world state is a pure cache.
- **Pause** (`SetPausedCommand` → `PauseSystem`, `UnitFlags.Paused`): factories, engineers/ACU (incl. enhancements),
  upgrades, silos, mass fabricators, shields and radar/sonar stop and request nothing; paused units still move and
  fight. A paused upgrading structure keeps its old production.
- **Capture** (`Order.CaptureUnit`, `CaptureSystem`, `UnitTransfer`): an engineer/ACU in build range works for
  `BuildTime / BuildRate / 2` seconds paying the target's `BuildCostEnergy` (flow economy, `BuilderMode.Capture`).
  Progress lives on the target (`Builder.CaptureProgress`, several captors add up with a per-tick reservation; reset
  after 2 s without work). At 100% `UnitTransfer` moves the unit to the captor's army (orders/targets/pause cleared;
  also used by the sandbox "give") and emits `EffectKind.Captured`. Targets: complete, visible enemy non-commanders.
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
- **Veterancy** (`Combat/Veterancy.cs`, kills counted in `SimWorld` via `Veterancy.AddKill`): a level every
  3 × tech kills (commanders/experimentals 20), max 5. Each level: +10% max health (healed by that much on level-up)
  and regen +1/+3/+6 HP/s (T1/T2/T3; commander 10, experimental 25). Always read max health through
  `Veterancy.MaxHealth(w, i)` (damage, repair, snapshot, upgrade/enhancement swaps keep the health fraction).
- Attack orders on a target the army no longer detects (lost in fog) are dropped (OrderSystem). Idle aircraft with
  no orders engage what their weapons acquire (leashed chase) and return to their orbit point.
- Line of fire (`Combat/LineOfFire.cs`, used by `TargetingSystem`): a NON-forced target is only acquired/kept if the
  weapon can hit it from where the unit stands: straight shots and beams need a clear muzzle→target line over the
  terrain/water surface (same grazing tolerance as projectiles), shells need an arc solution (`Ballistics.SolveArc`,
  as `WeaponSystem` uses). Homing/missiles, bombs, flak, torpedoes and silo weapons are not restricted; forced
  targets (Attack orders, TML/nukes, attack-ground) skip the check. Effect: units below a cliff no longer stop
  on attack-move to shoot into the rock; they walk up the ramp. `WeaponBlueprint.HighArcWhenBlocked` (Mavor): when
  `LineOfFire.IsArcClear` finds terrain on the low arc, `WeaponSystem` lobs the shell on the high arc instead.
- **Fire states** (`Combat/FireStates.cs`, `SetFireStateCommand`, `UnitStore.FireStates`, snapshot `UnitView.FireState`):
  Fire at will (default) / Return fire (acquires targets only while hit in the last 5 s) / Hold fire (never acquires;
  Attack orders and attack-ground still fire). `TargetingSystem` reads it; a unit being captured by an army (or an
  ally) is never auto-acquired by that army's units.

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
  muzzle→target line. Nukes ignore domes and knock every shield in their outer ring down. Bombs and shells falling
  onto a dome from above are absorbed too, and a bomb released by an enemy bomber INSIDE a dome hits the shield.
  Collapsed or paused shields pay no upkeep (EconomySystem); recharge only starts while the army's energy efficiency
  is >= 0.95.
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
  `StrategicSystem.InitUnit` is idempotent and keeps the silo stock and the shield HP/up-down state, clamped), so every
  system just reads the new blueprint.
  `BuildListFrom` merges other builders' final build lists (SCU/T3 Engineering from `uef_t3_engineer`, Fatboy from the T3
  land factory). Building one reuses `UpgradeState`/`UpgradeSystem`: `EnhanceCommand` stops the unit and targets the variant,
  `UpgradeSystem.Cost` returns the enhancement's cost/time, the unit works on itself with its BuildRate, engineers assist
  with Guard, `CancelUpgradeCommand` refunds, any new order or Stop cancels it (with refund); `RemoveEnhancementCommand` clears a slot at once
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
- Dive/surface (`SetSubmergedCommand` → `Naval/Submarines.cs`, `UnitFlags.Surfaced`): depth changes smoothly;
  the layer (`OrderSystem.LayerOf`) switches halfway. A surfaced sub is a Water-layer unit: seen by vision/radar,
  hit by surface guns, collides with ships. Every `Movement.Layer: "Sub"` unit can dive; `StartSurfaced` divers
  (the Atlantis) spawn at the surface. Submerged, only torpedo weapons work (`Submarines.FiresSubmerged`: Targeting
  and WeaponSystem hold the SAMs/guns). A blueprint swap (InitUnit re-run) keeps the dive state and depth.
- Ship separation (`Naval/NavalMotion.cs`): hulls are capsules along the heading, pushed apart for all armies;
  submerged subs only collide with subs, surfaced subs with ships.
- Client: models in `Rendering/UnitModels.Naval.cs`; `EffectsRenderer.Naval.cs` (torpedo body + surface streak +
  bubble trail, splash/hit/interception bursts, ship wakes scaled by speed and hull), `UnitRenderer.Submerged.cs`
  (submerged units drawn after the water, see-through and blue-tinted), `PropRenderer.Naval.cs` (wrecks on deep
  water sink and fade), sonar ring in `BuildPlacement`. `--scene naval` (`NavalScene.cs`): scripted Archipelago
  fleet battle + naval factory (`--select uef_t1_naval_factory`) for screenshots and tests.

## Transports & cargo (`Rts.Sim/Transport`)
- One mechanic for land transports, air staging and the Atlantis: a **carrier** has `Cargo {Layer Land|Air,
  Capacity (slots), LoadRange, RepairRate, RefuelRate, KeepDocked, HoverHeight}` in its blueprint
  (`Data/UnitBlueprint.Transport.cs`). What a unit weighs as cargo is `TransportSize` (-1 = derived: land T1 = 1,
  T2 = 2, T3/commander = 4 slots; aircraft T1–T2 = 1, T3 = 2; structures, ships and experimentals can't be carried).
- **Carried units** get `UnitFlags.Carried`: they stay alive (handles valid, `IsActive` true) but `RebuildAliveList`
  skips them, so they are off the grid, untargetable, out of intel/economy and out of `snap.Units`. Their position
  follows the carrier each tick. When a unit boards, weapon locks and front Attack orders on it are dropped
  (`PurgeReferences`; Attack orders issued later are dropped by `DropAttackOnCarried`). A destroyed carrier kills
  its cargo, recursively (no emergency drop, like SupCom).
- State in `SimWorld.Cargo` (`CargoState`: `Carrier[]`, `Holds[]` lists, `Fuel[]`, `AirMode[]`, timers) — plain
  arrays/lists (save/load friendly).
- Orders (`TransportOrders` builds and issues them; `CommandSystem` hands Load/Unload/TransportTo/Ferry/Dock to it):
  `Load` (land unit → walk to the transport and board; transport → fly to the unit and pick it up), `Unload` (land
  transports fly there, land and drop the cargo in rings; air carriers launch their aircraft), `TransportTo`
  (issue-time only: cargo is split among the selected transports, biggest units first, into Load + Unload; no
  transports = Move), `Ferry` (transport: shuttles between a pickup beacon and a drop point forever, leaves 3 s after
  the last boarding or when full; land unit: walks to the beacon and boards a ferry transport), `Dock` (aircraft:
  to a given/nearest air carrier). Queued orders after the transport order resume after the drop.
- VTOL (`Systems/MovementSystem.Vtol.cs`): air units with land cargo hover and land (`AirMode`) to load/unload.
- Fuel: aircraft burn `FuelSeconds` (0 = derived 400/600/800 s by tech; < 0 = unlimited) while flying; out of fuel
  = half speed. Idle aircraft below 50% HP or out of fuel auto-dock at the nearest air carrier with room.
  Air Staging repairs/refuels for free and releases aircraft when full; `KeepDocked` carriers (the Atlantis) keep
  them until Unload (Launch) or until the aircraft gets new queued orders.
- Mobile factory (Atlantis): builds aircraft on deck (`SyncFactoryUnit` keeps the unit being built on the carrier)
  and carries them. Pausable like any factory (H). It can dive (`Movement.Layer: "Sub"` + `StartSurfaced`, see Naval):
  while submerged (`Submarines.IsSubmerged`) the deck is closed: no docking (aircraft wait above it; `FindAirCarrier`
  skips it), no launching (Unload waits, docked aircraft stay aboard), a finished aircraft goes straight into the hold
  with a Dock order in front of its orders (`TransportSystem.DockFinished`) and takes off after surfacing; with a
  full hold production waits (`WaitsForRoom`).
- Capture (`UnitTransfer`): a captured carrier's cargo changes army with it (hold order, recursive); carried units
  can't be captured and a carrier holding a commander can't be captured. Boarding ends a factory roll-out
  (`MoveState.LeaveTime = 0`). Air staging repairs veterans to their raised max health.
- Assist chains (`Economy/AssistChain.cs`): an engineer guarding an engineer that guards a builder… helps the root
  builder (depth 8, cycle-safe); a guard repairs its damaged guarded unit first.
- Snapshot (`Snapshot/TransportViews.cs`, written by `TransportSnapshotWriter`): `CargoViews` (carrier, used/capacity
  slots, landing) + `CargoUnits` (blueprint, health, fuel, size of each carried unit), `Ferries` (army, pickup, drop,
  transports), `UnitView.Fuel`.
- Client: models in `Rendering/UnitModels.Transport.cs`, cargo drawn under transports / on the pads and the deck in
  `Rendering/UnitRenderer.Cargo.cs`, ferry routes in `Rendering/TransportOverlay.cs`, orders/context clicks/cargo
  and fuel in the UI; `--scene transport` (`TransportScene.cs`) shows everything.

## Skirmish AI (`Rts.Sim/AI`)
- Runs inside the deterministic sim (sequential, AliveList order, DetRandom, no Dictionary/HashSet iteration) for
  armies with `Army.IsAI && !Defeated`, and acts ONLY through normal `Command`s (`AiCommands`), like a player.
  State per world via ConditionalWeakTable (`AiSystem.StateOf(w)`): one `AiBrain` per army + per-unit-slot
  `AiUnitInfo` (role/task, generation-checked).
  All AI state is plain fields/lists in that table, so it is saved and restored with the game (test
  `AiSaveLoadTests`: save mid-battle, load, identical continuation); only the `AiSystem.Trace` hook is `[NoSave]`.
- `AiBrain` thinks on a staggered schedule (economy every `EconInterval`, military every `MilitaryInterval` ticks,
  offset by army index); difficulty (`Army.AiDifficulty`: Easy/Normal/Hard) selects an `AiConfig`. No difficulty
  cheats (no resource bonus, no map vision): Hard is better through faster reactions, more factories, raids and
  commander snipes (until the 2026-10-23 unit balance also an artillery-heavy mix, pull-back and focus fire; see below).
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
- Tech and budget (wave 5k): `AiEconomy.Tech.cs` upgrades extractors and factories after `MexT2Time`/`MexT3Time`,
  `LandT2Time`/`LandT3Time`/`AirT2Time`/`AirT3Time` within `UpgradeShare` of the mass income (`MaxMexUpgrades`,
  `MaxFactoryUpgrades` at once), buys commander enhancements after `EnhanceTime`, builds tiered engineers/power/
  defenses, pauses mass fabricators when energy runs short, runs the quantum gateway (`MaxScus`) and the big projects
  (`ShieldTime`: base shields, TMD, artillery installations, storages; `TmlTime`; `StrategicTime`: nukes,
  experimentals; an anti-nuke as soon as an enemy launcher is seen). `AiStrategic.cs` aims the tactical/strategic
  launchers. `AiEconomy.Budget.cs`: the factories get at least `ArmyShare` (0.5) of the mass income and eco/tech
  commitments start only while the rest leaves room; **pressured** (after 5 min, own land strength below
  `PressureRatio` 0.8 x the enemy's estimate, or enemies at the base) the share is `PressureArmyShare` 0.85, so tech
  waits while the army is losing (tech vs a T1-only AI: 51 % -> 74 % of decided games). Unit choice by faction roles;
  caps `MaxTier` (structures/engineers), `MaxUnitTier` (combat units), `ArtilleryTier`. Off by default (AI_TUNING):
  `StallTime` fall-back, `AdvanceInterval` creeping advance, `LeadArmyShare`. Since the 2026-10-23 unit balance Hard
  no longer uses the 0.55 artillery share, focus fire or the unit pull-back (they lost to Normal); numbers and the
  open stall problem: AI_TUNING "New goals" and "Unit balance".
- Bench: `dotnet run -c Release --project tools/Rts.Bench -- ai [minutes] [map] [seed] [diff0,diff1,...] [threads] [players] [preset] [factions]`
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
- **Hotkeys**: `Input/Hotkeys.cs` is THE key table (key(s), modifiers, scope, help group/text, `Share` tags for keys
  shared on purpose: D, E, U, M; `Pressed()`, `Label`, `Clashes()`, `Mouse` help lines). Input code reads keys from it
  (never a raw `KeyboardKey` for a game action). Rule: a new key goes in the table; `HotkeyTableTests` fails on clashes
  and on keys polled in `src/Rts.Client` that are not in it. F1 / the top-bar "?" open `UI/HelpOverlay` (generated
  from the table; `--help-overlay` for screenshots). Tooltips show keys through `Hotkey.Label`.
- Selection/UX: fire state Q / Shift+Q (`Input/FireStateToggle.cs`, "Fire" order button, unit panel order line);
  `UI/IdleButtons` above the minimap (idle engineers ".", idle factories ","; Shift = all; `Input/SelectionMath`);
  double-click / Ctrl+click = that type on screen (Shift / Ctrl+Shift add); double-tapping a control group centers the
  camera; minimap left click gives the current order mode's order there (`SelectionController.IssueAtWorldPoint`);
  `OrdersPanel` adds columns (max 3 rows); the unit panel uses a compact header when ≥ 2 extra rows follow; a mixed
  selection shows idle/fire-state counts and type buttons (smaller when many; Ctrl+click = that type on screen).
- Pause menu "Load Game": `AppHost.Go(next, keepOld: true)` keeps the paused `InGameScreen` alive; `SaveListScreen`
  (`returnTo`) owns it: Back returns to it, loading a save disposes it.
- Gameplay toggles (unit panel buttons + hotkeys): pause H (`SetPausedCommand`), dive/surface D (subs), capture E
  (then click an enemy; engineers also capture with a context right-click on enemies, `CursorHint` shows it).
  D and E are shared with the transport orders (`Input/SharedHotkeys.cs`, pure): E = transport-to when transports are
  selected, else capture; D = subs dive/surface AND aircraft dock (a mixed selection does both); the Capture button
  shows no key while E means transport-to. Right-click priority (`ContextOrders.Evaluate`): enemy → capture (only
  engineers/commanders selected) or attack; own finished unit → Load / Pick up / Dock (transport rules, local army
  only) before the builder actions; ground → reclaim a wreck, else Unload (loaded transports), else move. The unit
  panel shows veterancy and adjacency bonuses and capture bars; `Input/AdjacencyPreview` draws lines to neighbours
  that would give/receive a bonus while placing a structure; `Input/PlacementLine`: Shift+drag lays a line of
  structures (`--ui-place id@x,z` for screenshots).

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
- Any-army control: in sandbox the player can select and order units of every army (`Input/ArmyCommands.cs` splits
  each command into one per owning army, since the sim checks every unit against the command's Army). Panel
  actions work on enemy selections; `--select any:<id>` selects another army's unit at start. Change owner uses
  `UnitTransfer` (same as capture).
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

## Save/load & replays (`Rts.Sim/Persistence`)
- One serializer for saves, replays and seek checkpoints: `WorldState.Capture(sim)` writes the whole object graph
  reachable from the `Simulation` plus every static `ConditionalWeakTable<SimWorld, T>` of Rts.Sim (per-world system
  state, discovered automatically) — every instance field (private/readonly too) through generated IL (`Val.cs`,
  `Codecs.cs`, `GraphIO.cs`), object identity/shared references kept, unmanaged arrays block-copied. The immutable
  `BlueprintDb` and `Terrain` are not stored: references into them become ids (`ExternalSet`); the terrain is
  regenerated from `GameInfo.Map` (embedded in the file when there are no generator settings). `WorldState.Restore`
  rebuilds the identical graph: save → load → continue gives the same `StateHash` and the same bytes as the
  uninterrupted game (`PersistenceTests`, 1 and 4 threads).
- Compatibility: every type is written once with its name + field-layout signature; a different layout, blueprint
  set or terrain → `IncompatibleSaveException`; damaged files → `SaveFormatException` (magic, version, payload length
  + hash, bounds-checked reads). Migrations (`SaveMigrations.cs`): the container version (`SaveContainer.Version`,
  now 2; files ≥ `MinVersion` load) is upgraded on load one step at a time: a header-JSON step (also for file lists)
  and a payload step per version (v1→v2: players without a faction get `FactionDb.DefaultFaction`); `GameInfo.FileVersion`
  = the version read. To change the format: bump the version, add a `Steps` entry + a `case`, extend
  `SaveMigrationTests` (writes old-version files from a current game with `SaveContainer.EncodeRaw`). Limit: the
  payload is still checked against this build's field layouts (no field-level migration yet).
- Files (`SaveFiles.cs`, `SaveStore.cs`): magic + version + JSON header (`GameInfo`: map settings, players, local
  army, sandbox, opaque client config, length, winner, build) + Brotli payload. `saves/*.rtssave` = state + the replay
  so far; `replays/*.rtsreplay` (newest 50 kept). Menu lists read only the headers.
- Replays (`Replay.cs`, `ReplayPlayer.cs`): `SimRunner.Hooks = ReplayRecorder` stores the initial state (first
  tick), each tick's command list and a `StateHash` every 10 ticks (≈0.02 ms/tick). `ReplayPlayer` (as the runner's
  hooks or headless) re-simulates from the initial state with the recorded commands (incoming ones are ignored) and
  compares the hashes → `DesyncTick`; `SeekTo` backwards restores a compressed checkpoint (every 600 ticks) and
  re-simulates. A loaded save keeps recording into its replay, so a replay always covers the whole game.
  Headless check: `Rts.Bench -- replay <file>`; timings/sizes: `Rts.Bench -- savebench`.
- Threading: `SaveGame.Capture` only on the sim thread (a `SimRunner.Post` job; jobs also run while paused) or with
  the runner stopped; `SaveGame.Encode` (compression) and file writes run on a background task.
- Client (`Rts.Client/Persistence`, `Menus/{SaveListScreen,FileLoadingScreen,ReplayScreen,InGameSaveUi,MessageBox}`):
  `GameRecording` (owned by `GameSession`) sets the recorder as runner hooks for every game, captures saves through a
  runner job and writes files on a worker (toasts); the replay is written when a team wins and again on quit/restart
  (same file; games under 5 s are dropped; app exit waits for pending writes). `ClientGameData` = the client config
  stored in `GameInfo.ClientData` (rebuilds names/colors/sandbox on load). Pause menu "Save Game" dialog, F5/F6
  quicksave/quickload; main menu "Load Game" / "Replays" lists (header only; damaged/incompatible files greyed with
  the reason; load errors in a message box). Replay viewer = `GameSession` over `ReplayPlayer.Sim` with
  `GameContext.ReadOnly` (no order/build/enhancement UI, commands ignored by the player anyway): play/pause, speed
  0.5×–16×, timeline seek (`ReplaySeeker`: runner job `SeekTo` + `ReplaceSimulation`), fog view per army or all,
  DESYNC banner, other-build warning.
- **Keeping new state saveable** (rule for every feature): sim state lives in plain fields of sim classes/structs,
  arrays, `List`/`Dictionary`/`HashSet`/`Queue`/`Stack`, or a per-world `ConditionalWeakTable<SimWorld, T>`.
  No static mutable state, no stored lambdas/delegates (store an enum/id and dispatch in code; a cached delegate to
  your own method = `[NoSave] Action? _f;` + `_f ??= Method`), no framework interfaces (`IList<T>`, `IEnumerable<T>`)
  or `object` as field types. Caches, scratch buffers and wall-clock timings: `[NoSave]` (auto-properties
  `[field: NoSave]`), rebuilt lazily or in `IAfterLoad.AfterLoad()`; never `[NoSave]` anything that affects future
  ticks. `SaveCoverageTests` walks every reachable field type and fails with the field path and the fix;
  `PersistenceTests.SaveLoadContinueMatchesUninterruptedGame` proves the round trip on a rich game (AIs, every
  blueprint spawned, missiles, nukes, upgrades) — extend that scenario if your feature has state it doesn't reach.

## Performance & scale (wave 5m)

Goal: 1024 maps, 8 armies, 5k+ units at 60 FPS on the Radeon 780M, sim tick < 10 ms. Numbers: DEV_GUIDE "Reference
numbers"; how to measure: DEV_GUIDE "Perf check" and "Profiling".
- **Worker pool** (`Core/ParPool.cs`, `Par.cs`): persistent threads, the caller participates, no allocation per call.
  A call made while the pool is busy (nested, or from a second thread such as the warm-up or a loading thread) runs
  inline on the caller with the same chunks, so results never depend on it. Hot call sites pass static lambdas
  with the world as an argument (`Par.For(n, chunk, w, static (w, c, a, b) => ...)`): a capturing lambda allocates.
- **Profiling**: `Simulation.Profile` (`Core/SimProfile.cs`) times every tick stage (and, opt-in, its allocations);
  `Rts.Bench` prints stage avg/max, KB/tick, the first tick and the worst tick with their stage breakdown. Client:
  `FrameProfiler` (CPU laps per frame stage, `FrameProfiler.Sub("x")` splits a stage from inside a module; GPU timer
  queries per stage) printed by `--screenshot` runs and once per second by `--perf-log`.
- **JIT**: tier-0 code + on-stack replacement made the first ~30 s of a game spiky (10–30 ms ticks in loops that
  normally cost 0.05 ms). `Rts.Client` and `Rts.Bench` set `TieredCompilationQuickJitForLoops=false` (methods with
  loops are optimized at once), and the client starts `SimWarmup` at startup: a throwaway 2-AI skirmish with a small
  battle on a background thread, so the real game's first tick runs compiled code (first tick ~200 → ~15 ms on 1024).
  The warm-up world is separate; `ScalePerfTests` checks a game's hash is unchanged while it runs.
- **Navigation build**: `GameSetup.CreateSkirmish` builds the clearance grids (`PathService.EnsureGrids`, terrain pass
  parallel) at load instead of in the first tick; region graphs are still built on first use (they are saved state,
  so building all 16 up front would bloat saves) and several missing graphs are built in parallel
  (`PathService.EnsureGraphs`). Early vs lazy building gives identical results: grids and graphs are pure functions
  of terrain + occupancy, and later structures update them incrementally with the same functions.
- **Allocations**: ~0 per tick in combat; AI games ~10 KB/tick on 1024 (was ~125), mostly new goal fields. Goal
  fields reuse the Dist/Next arrays of evicted fields (`PathService.RentFieldArray`/`RecycleField`, safe because
  fields are read only during one tick's solves and evicted in sequential phases) and a per-thread Dijkstra heap.
- **Intel**: < 0.4 ms avg on 1024 with 8 armies (was ~1.1).
- **Client**: units are instanced (2 draw calls); particles are GPU-instanced (`ParticleInstances.cs`, one 52-byte
  instance per particle, the vertex shader builds billboard / ground quad / streak); terrain chunks (64×64) have
  LOD meshes at vertex strides 1/2/4/8 (`TerrainRenderer.Lod.cs`): a chunk uses the coarsest stride whose cell
  stays ≤ 5 px on screen, and every inner chunk edge has a skirt (depth = 2 × the largest stride error on that edge)
  so neighbours at different strides leave no cracks; `--terrain-lod N` forces a level. Deposit markers are
  frustum-culled and drawn as one flat square beyond 260 units; the minimap draws one dot per pixel and color.
- **Maps**: 6–8 players on 512/1024 for every preset are rotationally symmetric (non-quarter-turn counts carve one
  corridor profile and rasterize every rotated copy with it) and fair: `MapScaleTests` checks plateau heights,
  terrain symmetry, the nearest deposits of each start and the ground distances between starts.

## Multiplayer (`src/Rts.Net`, no raylib; client screens in `Rts.Client/Menus`)
- **Model**: deterministic lockstep on top of the replay machinery. Every peer runs the full sim; only commands
  travel. Star topology: the host relays every turn (no peer-to-peer). TCP, length-framed binary messages
  (`Wire/Framing.cs`, `Wire/Messages.cs`: Hello, Welcome, Reject, Lobby, LobbyRequest, Chat, Start, Loaded, Go, Turn,
  Hash, Desync, Drop, Speed, Delay, Ping/Pong, Pings, Leave, Abort). Port 47624 TCP (game), 47625 UDP (LAN
  discovery: `LanAnnouncer` broadcasts once a second, `LanBrowser` lists the games; protocol + sim build compared).
  Joining needs the same `GameInfo.CurrentSimBuild` (the Rts.Sim assembly's MVID) and protocol version.
- **Lobby** (`Lobby/LobbyState.cs`): 8 slots (Open / Human / AI / Closed) with faction, team, color, AI difficulty,
  ready; `LobbyOptions` (map size, preset, water, seed, `OnDrop`). The host owns the state and applies requests
  (`HostSession.SetSlotKind/SetSlot/MoveHost/SetOptions/Kick`, clients send `LobbyRequest`); every change is broadcast
  and published as an immutable copy (`NetSession.Lobby`, any thread). UI threads read events with `TryGetEvent`.
- **Start**: `HostSession.StartGame` (checks `LobbyState.Problem()` and `FactionKnown`) broadcasts `GameStart` (lobby
  copy + initial delay). Army slots in slot order = armies 0..n-1; `NetGameSetup.ToSkirmish` maps slots to
  `SkirmishSettings` (AI flags, difficulty, teams, colors, names, factions; "random" resolved from the seed). Each peer
  builds the sim on a worker, reports `Loaded(StateHash)`; the host sends Go when every hash equals its own, else
  Abort (everybody back to the lobby).
- **Turns** (`Lockstep/LockstepHooks.cs`, the runner's `ITickHooks`, wrapping the replay recorder): every player
  sends exactly one turn per tick, contiguous. Local commands of the frame stepping to T+1 go into the turn for
  T+1+delay (ticks 1..delay are pre-sent empty at Go). `Hold()` = no Go yet, or some player's turn for tick+1 is
  missing (`WaitingFor`). `BeforeStep` merges the turns in player order, keeps only commands of that player's army,
  renumbers `Sequence`/`ExecuteTick` → identical command lists on every peer (and in every replay). The local copy is
  decoded from the bytes sent, so all peers step identical objects. Turn payload = `TurnEncoder`/`TurnDecoder`:
  the persistence graph writer with a per-sender type table (type names/layouts go over the wire once per game).
- **Delay**: sender-side, host-adaptive: `DelayFor(maxRtt, speed) = 1 + (maxRtt + 20 ms) / tickMs` (1..30 ticks),
  recomputed every 250 ms from the pings and broadcast (Delay). A larger delay leaves a gap filled with empty turns;
  a smaller one makes the sender wait until the ticks catch up (commands stay pending).
- **Speed/pause**: requests to the host (`NetGame.RequestSpeed`), broadcast (Speed) and applied to every runner
  (`NetGame.AttachRunner`); they don't affect determinism.
- **Desync detection**: every 10 ticks each client sends `StateHash` to the host; the host compares with its own →
  Desync broadcast (game continues; the client shows a banner and writes the replay so far: `GameRecording.WriteReplayNow`).
- **Disconnects**: a connection silent for `TimeoutSeconds` (10 s) or closed → the host picks lastTick = the last turn it
  relayed for that player, broadcasts Drop after those turns; every peer applies empty turns afterwards and a
  `PlayerLeftCommand(army, OnDrop)` at lastTick+1 (`Rts.Sim/Orders/MultiplayerCommands.cs`, `MultiplayerSystem`):
  AI takeover (`Army.IsAI`) or defeat. Host lost → a client continues offline (drops every other player after its
  last received turn; `NetGame.Offline`). Reconnecting is not supported.
- **Replays**: the MP game's `GameRecording` (`NetGameConfig.Recording`) is a normal skirmish replay of the merged
  command log (remote humans are non-AI armies whose commands come from the log): `Rts.Bench -- replay` verifies it.
- **Client** (`Menus/MultiplayerScreen` → `LobbyScreen` → `NetLoadingScreen` → `NetInGameScreen`): the screen that
  holds the session passes it on (`_handedOff`), any other exit calls `Leave()`. `GameSession` gets the lockstep
  hooks through `wrapHooks`, `NetKeys` (Space/+/− become requests; no debug spawns, no F7 fog reveal; Enter = chat, `Hotkeys.Chat`) and
  `KeyboardCaptured` (chat line open → game hotkeys blocked). No saving and no Restart in MP; Esc menu doesn't pause;
  local defeat → observer (the sim must keep running so the others don't wait); game over → end screen (runner
  stopped: the others may then wait if they continue).
- **Tests**: `tests/Rts.Net.Tests` (framing, codecs, lobby round trip, factions, loopback TCP matches of 2–4 peers with
  random commands → identical hashes + MP replay re-simulates, drops, desync, speed, LAN discovery);
  `Rts.Bench -- mp [peers] [ticks] [mapSize] [aiSlots] [threads] [drop]` (headless self-test, exit 1 on desync).
- **Float determinism review** (2026-10-16). Lockstep needs bit-identical floats on every machine:
  - Basic IEEE ops (+ − × ÷, sqrt, float↔int casts) are deterministic: RyuJIT uses scalar SSE2 on x64 and NEON on
    ARM64, no x87 and no implicit FMA contraction; .NET 9+ float→int casts saturate the same on x64 and ARM64. No
    `MathF.FusedMultiplyAdd`, no explicit SIMD, no `GetHashCode` in Rts.Sim (checked).
  - `System.Numerics.Vector2/3` (≈200 uses): same results on the same .NET + ISA; their intrinsic code paths could in
    principle round differently between x64 and ARM64 (dot products) — unverified.
  - **Risk**: transcendental functions (`MathF.Sin/Cos/Atan2/Pow/Log2`, ~40 calls in Rts.Sim: weapons, projectiles,
    missiles, movement, AI memory decay, map generation) call the OS C runtime (glibc libm, Windows UCRT, macOS
    libSystem), which may differ in the last bit → Linux vs Windows games can desync. Same OS + same build: identical.
  - Safety net: `Loaded` hashes at start, `StateHash` every 10 ticks, desync banner + replay dump.
  - later: route all sim trig through `DMath` with software implementations (polynomials using only + − × ÷), verify
    Linux↔Windows↔ARM64 with `Rts.Bench -- replay` of the same file on each machine.

## Cybran faction & stealth/EMP (wave 5l)

**Data.** `data/factions/cybran.json` + 80 `cybran_*` blueprints (SupCom1 roster: ACU/SCU with enhancements,
engineers T1–T3, land/air/naval T1–T3, Monkeylord, Soul Ripper, Scathis, full base set). Every required role is
filled; Cybran-only optional roles: `light_assault_t1` (Hunter), `mobile_stealth_t2` (Deceiver), `shield_ed2_t2` …
`shield_ed5_t3`. UEF-only roles stay empty, and `shield_t3` is empty (ED4/ED5 are upgrade-only, as in SupCom1).
Balance mirrors the UEF unit of the same role (builders same speed; combat units a little faster). Every
`Visual.Shape` starts with `cy_`; air factories reuse the land factory shapes. `CybranRosterTests` checks roles,
in-faction build lists, reachability from the commander, `UpgradesTo` chains and the Cybran specifics.

**Stealth, cloak, stealth fields** (`Intel/StealthSystem.cs`, `Intel/StealthState.cs`). Blueprint `Intel.RadarStealth`,
`Intel.SonarStealth`, `Intel.Cloak`, `Intel.StealthFieldRadius`; ACU/SCU enhancements set the same via `Effects.*`
(upkeep `Effects.MaintenanceEnergyAdd`).
- Radar stealth: enemy radar doesn't detect the unit; sonar stealth: enemy sonar doesn't. Cloak = no vision either
  (implies radar + sonar stealth, so no blip). Enemy **omni** beats all of them (blip on omni; full sight of a cloaked
  unit only inside omni). A field gives radar + sonar stealth to every unit of the generator's team within the radius.
- Active while complete, on the map (not carried), not Paused (intel toggle) and, with upkeep, while the army's energy
  efficiency ≥ `StealthSystem.MinPowerEfficiency` (energy stall switches it off).
- `StealthSystem.Run` (sequential, start of `IntelSystem.Run`) rebuilds a per-slot byte mask; `IntelService.StealthMask`
  is null while nothing is active (zero cost for intel queries), and without any stealth blueprint the update is one
  check. Ghosts respect it (`HiddenFromTeam`: no ghost of a cloaked unit; one in sight is forgotten). Snapshot: `UnitView.Status` (`UnitStatus` flags RadarStealth,
  SonarStealth, Cloaked, Stunned, StealthField) from `StealthSystem.StatusOf`.

**EMP stun** (`Combat/EmpStun.cs`). Weapon `StunDuration` (s), `StunRadius` (0 = DamageRadius, else only the unit hit),
`StunMaxTech` (default 3). Applied in the sequential impact phase (projectiles, beams) to enemy units on the weapon's
target layers; commanders, aircraft and units above `StunMaxTech` are immune; a shield dome protects the units inside
from an area stun fired from outside. `UnitFlags.Stunned` (bit 11): no move, acquire, fire, missile defense, build /
assist / repair / reclaim / upgrade / produce / stockpile; shields, intel, economy and stealth keep working.
`EmpStun.Expire` clears it at tick start; `EmpStunState` (until-tick + generation per slot) exists only after the first
stun. Users in data: Medusa (`cybran_t1_artillery`), SCU EMP array (`cybran_scu`).

**Seabed walkers** (`Naval/Submarines.cs`). `Movement.Layer: "Amphibious"` units (Wagner, Monkeylord) walk under
water; below the water level they hold every weapon except torpedo kinds and `FiresSubmerged` weapons, and are
targeted like Layer Sub (torpedoes only; sonar/omni detection).

**Shield chain.** ED1 → ED5 via `UpgradesTo` on the shield structures; no sim code: `UpgradeSystem.Complete` →
`UnitLifecycle.OnSpawn` → `StrategicSystem.InitUnit` brings the dome up at the new blueprint's MaxHealth.

**Client.** Models in `Rendering/UnitModels.Cybran*.cs` (`Cybran.cs` = shape dispatch `IsCybranShape`/`CybranShape`,
`BaseShape` maps `cy_air_staging`/naval shapes to the UEF names that cargo, icons and naval code check; `CybranAir`,
`CybranNaval`, `CybranStructures`, `CybranDefense`, `CybranExperimental`); one `case` line in `UnitModels.Build`.
Status visuals (`Rendering/StatusFx.cs` = pure logic, `UnitRenderer.Status.cs` = `DrawStatus`, called after
`DrawSubmerged`) from `UnitView.Status`: cloaked units skip the opaque pass and are drawn with a translucent fresnel
shimmer (no depth writes, no shadow; a cloaked unit under water stays a normal submerged unit); stunned units get
blue-white spark bolts; a stealth field shows a violet terrain-following ring at `bp.Intel.StealthFieldRadius`
(enhancements are blueprint variants, so `UnitView.Bp` carries their radius). Radar/sonar stealth alone has no visual.
`--scene cybran[:land|:air|:naval|:structures1..3|:experimental]` (`CybranScene.cs`) lays out every Cybran blueprint.
Tests: `CybranModelTests`, `CybranSceneTests`, `StatusFxTests`.

**AI.** No AI change: the AI plays Cybran through the roles (AI_TUNING "Cybran vs UEF"). The AI doesn't use
stealth/cloak/EMP deliberately yet (later).
