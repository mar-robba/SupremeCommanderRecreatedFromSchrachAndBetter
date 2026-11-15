# Economy, construction, factories, upgrades

The SupCom flow economy and everything that spends it: construction, factories, in-place upgrades, adjacency, pause,
capture, wrecks and reclaim, engineer automation.

**Contracts of this area**
- Pay first, then progress. A consumer sets its request in tick T; `EconomySystem` grants `request × Efficiency` in
  tick T+1; the work applied is scaled by the same `Econ[i].Efficiency`, so resources spent always equal work done.
- Every upkeep consumer (radar, shields, energy weapons) reads `Econ[i].Efficiency`.
- Per-army sums are per-chunk partial sums merged in chunk order (determinism).
- An in-place upgrade keeps the slot and the `UnitHandle`, so selections, AI references and queues survive.

Files and top-level types of this area: [index/economy.txt](index/economy.txt) (generated).

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

## Assist chains (from "Transports & cargo")

- Assist chains (`Economy/AssistChain.cs`): an engineer guarding an engineer that guards a builder… helps the root
  builder (depth 8, cycle-safe); a guard repairs its damaged guarded unit first.

## Engineer patrol automation (from "Commander kit & engineer automation", wave 6a)

- **Engineer patrol automation (G3)**: mobile builders on a Patrol order (`ConstructionSystem` case Patrol ->
  `EngineerPatrol.Exec`) scan every 5 ticks (staggered `(Tick + i) % 5`, radius `ScanRadius` 12 + half size) and do,
  in priority: repair damaged allies (not flying aircraft, not carried) > assist unfinished allied structures >
  reclaim props (never units). The job is kept in the patrol Order itself (`State` 1 repair / 2 assist / 3 reclaim,
  `Target`, `Spawned` = prop, `BlueprintIndex` = no-scan-before tick after an unreachable job, 5 s); OrderSystem's
  Patrol case returns early while `State != 0` so the route does not advance. Leash 1.5 x scan range. Unreachable =
  the builder `ArrivedAt` its goal while still out of range (not checked on the job's first tick). Prop draining is the
  shared `ConstructionSystem.ReclaimProp` (same float ops as ExecReclaim).
