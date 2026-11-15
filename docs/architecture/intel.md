# Intel: fog of war, radar, stealth

What each team sees: vision, radar, sonar, omni, the explored grid, ghosts of structures, stealth and cloak, and the
filter that keeps unseen things out of the client's snapshot.

**Contracts of this area**
- Intel is per team: allies share everything.
- `IntelSystem` recomputes everything each tick; its parallel work items write only their own rows.
- The snapshot is filtered by `IntelSnapshotWriter`: the client receives only what the view army sees, so picking and
  selection need no fog code. Global on purpose: nuke launches and `EffectKind.Nuke`.
- Queries are thread-safe. `IntelService.StealthMask` is null while no stealth is active (no cost for the queries).

Files and top-level types of this area: [index/intel.txt](index/intel.txt) (generated).

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

## Stealth, cloak, stealth fields (from "Cybran faction & stealth/EMP", wave 5l)

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
