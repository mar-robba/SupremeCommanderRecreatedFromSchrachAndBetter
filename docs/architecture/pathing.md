# Pathing and movement

How units find a route and move along it: clearance grids, slope classes, region graphs and goal fields
(`Pathfinding/`) and the per-tick movement step (`MovementSystem`). The map's pipeline rows for `PathSystem` and
`MovementSystem` summarize the algorithms; the design notes are in `Pathfinding/PathService.cs`.

**Contracts of this area**
- One question, one function: whether a unit fits somewhere is `PathService.Fits` / `NavGrid.Fits`, for the path
  search, the movement step, `FindNearestPassable` and teleport landing alike.
- A movement step never traps a unit: it is accepted iff `Clear(new) >= min(req, Clear(here))`.
- Grids and graphs are pure functions of terrain + occupancy, so building them early or lazily gives the same result.
- The number of path solves per tick is capped deterministically.
- Mobile `Radius` stays ≤ 3: it sizes every unit's separation query ([commander.md](commander.md)).

Files and top-level types of this area: [index/pathing.txt](index/pathing.txt) (generated).

## Pathing: clearance, slope classes, channels (11a-1: `Pathfinding/NavGrid.cs`, `Pathfinding/PathService.Classes.cs`, `PathService.cs`, `PathSystem.cs`, `Systems/MovementSystem.cs`, `Naval/NavalMotion.cs`)

Gap G12: units path and move by their real radius and their own slope limit; oncoming ships pass in channels.
- **Clearance field** (`NavGrid.Clear`): squared Euclidean distance in half cells from a cell center to the nearest
  blocked cell (terrain for the grid's layer + slope, structures, map border), `ax^2 + az^2` with `ax = max(0, 2|dx| - 1)`,
  capped at `MaxClearance` = 34. Computed separably (row distance, then column combine) inside a window of
  `Reach` = 4 cells, the same window as the old Chebyshev classes, so the nav build and `RecomputeAround` cost the same.
- **One question, one function**: `NavGrid.ClearanceFor(radius)` = the smallest value Clear can take that is
  >= (2 (r - 0.25))^2 (the disc may overlap a blocked cell by at most 0.25; a W-cell gap admits r when W >= 2r + 0.5,
  never when W < 2r - 0.5); r < 0.75 -> 1 = any free cell (unchanged for small units). Values: 0.8 -> 2, ACU 1.2 -> 9,
  3.0 -> 34. `PathService.Fits(bp, p)` / `NavGrid.Fits(p, req)` is used by the path search (graph per requirement),
  `MovementSystem` (after separation, before a position is accepted), `FindNearestPassable` and teleport landing.
- **Never trapped**: the movement step is accepted iff `Clear(new) >= min(req, Clear(here))`: a unit that already
  does not fit (left a factory, a structure built next to it, an old save) can always move to a cell that fits it at
  least as well; on a blocked cell (Clear 0) it walks out freely as before. Idle units that do not fit walk to the
  nearest fitting cell unless they are building from where they stand. Builder goals next to a structure resolve to
  the nearest reachable fitting cell (`PathToNearest`) and the arrive radius shrinks by that gap (as ships near shores).
- **Slope classes**: `Movement.MaxSlope` (default 0.75 = `Terrain.DefaultMaxSlope`) grouped into at most 3 classes
  (`BuildSlopeClasses`: the distinct values >= 0.75 of mobile Land/Hover/Amphibious blueprints; class 0 is always 0.75,
  which the map generator guarantees connectivity for, so nothing is stricter: Fatboy/Scathis/Monkeylord's 0.6/0.7
  were removed). Today: 0.75 and 1.2 (bots, both ACUs and SCUs, Monkeylord). Grid slot = layer * 3 + class, built only
  for slots a blueprint uses; graphs per (slot, requirement) lazily as before. The three AI callers
  `GraphFor(layer, ClearanceFor(r))` get slope class 0.
- **Saved nav format**: `PathService.NavFormat` (saved int, `CurrentNavFormat` = 1). `AfterLoad` drops grids, graphs,
  goal fields and unit paths (units re-path) when the format or array sizes differ, e.g. a save from before 11a-1
  (format 0 by default once the save layout tolerates new fields).
- **Channels** (`NavalMotion.HullVisitor.HeadOn`): a moving hull ahead, coming the other way (headings dot < -0.5)
  on a colliding line, makes the ship steer to its own right (both do: they pass port to port) and the higher unit
  index also brakes, so in a one-lane channel it queues. Query radius + 6 cells. Before, oncoming hulls drove through
  each other (closest centers 0.03-0.86 in `NavalChannelTests`); now 1.53-3.91, no deadlock.
- Reference values (old -> new): GoldenStateHash 0xD273E007297C55F1 -> 0xED3AD081E0A9502E (GoldenTerrainHash unchanged);
  AI match `ai 30 256 42 hard,normal` C87C46C4EA726A82 "WINNER: Player (Hard) after 12:00" -> 31A42EB6FA4BA170 "after
  16:01"; bench `2000 300` hash 2F09CD7D01AF287E unchanged (only radius < 0.75 units: same cells as before), avg
  2.16 -> 2.06 ms/tick; nav build 1024: setup 483 -> 518 ms, first tick 150.2 -> 150.6 ms (Path 51.3 -> 53.3);
  savebench 8 256 2: file 714 -> 754 KB, load cold 522 -> 493 / warm 169 -> 127 ms. Map pack: 10 written, 0 refused.
- Tests: `PathClearanceTests` (requirement values and cap, gaps, Fatboy/battleship not pushed into a cliff/shore,
  slope walker vs tank, slope classes, nav-format reset, save/load 1 + 4 threads), `NavalChannelTests` (5 channels).
  `AiTechTests.AiReachesTech2OnFixedSeed` moved to seed 4 (games diverge; measured: no builder stuck).
