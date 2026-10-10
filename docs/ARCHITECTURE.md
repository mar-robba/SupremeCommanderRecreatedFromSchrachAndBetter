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
1. `CommandSystem`: validate and apply player commands (orders, factory queues, debug spawn).
2. Rebuild `AliveList`s, then `SpatialGrid.Rebuild` (counting sort, 8-unit cells).
3. `IntelSystem`: fog of war / radar (stub: everything is visible).
4. `EconomySystem`: sum the production and the requests from the previous tick, compute efficiency, update storage.
5. `OrderSystem` (parallel): the front order of each mobile unit becomes a movement goal and forced targets.
6. `ConstructionSystem`: engineers build, repair, reclaim and assist (stub).
7. `FactorySystem`: build queues, unit release, rally orders (stub).
8. `PathSystem`: resolve path requests (stub: straight lines).
9. `MovementSystem` (parallel, double-buffered `ScratchPos`): steering, turn rate, separation, terrain.
10. `TargetingSystem`, `WeaponSystem`, `ProjectileSystem` (stubs; combat).
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

## Economy model (SupCom flow economy, to implement)
- Each unit sets `Econ[i].MassProduction/EnergyProduction` (only when complete and not paused) and
  `MassRequest/EnergyRequest` (per second) for consumption: build costs while building
  (cost × buildRate / buildTime), upkeep such as mex/radar/shields.
- `EconomySystem` sums these per army and computes `efficiency = min(1, (stored + income·dt) / (demand·dt))` for
  each resource. Storage is clamped to [0, max], where max = sum of `Storage*` over complete units.
- Consumers scale their progress by the efficiency (min of mass and energy for builders).

## Client (`Rts.Client`)
- `GameApp` main loop · `RtsCamera` (zoom to cursor, strategic view) · `SnapshotInterpolator`
- `Rendering/`: `TerrainRenderer` (chunk meshes, baked lighting, picking), `UnitRenderer`, `IconRenderer`
  (strategic icons when zoomed out), `Shaders`.
- `Input/SelectionController`: selection, control groups, right-click orders, attack-move and patrol modes.
- `UI/Hud`: resources, selection summary, help.
