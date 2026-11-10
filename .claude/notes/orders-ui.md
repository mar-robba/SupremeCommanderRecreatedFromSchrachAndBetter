# (7b) orders UI piece — state (CHECKPOINT at ~129k, agent 7b-orders-ui-2)
Commits: 4267983 (sim fix + tests), 6f2a0f3 (client, builds 0 warnings; NOT yet run/tested/screenshotted).
## Step 0 test fix (done, QueueEditTests 11/11)
Cause 1 (real bug): LineOffsets ordered each row by position along the line; a group standing perpendicular to the
line ties → slots by index → crossing paths, units stall each other → "crowded destination" arrival 4 u short.
Fix: row order by side position across the travel direction (centroid→center), blended with dir (0.25).
Cause 2 (setup): seed 3 has a cliff at (100,80) (slot unreachable). Test line moved to x=114, z 62..86, tolerance ±2.
New pure overload `QueueEditSystem.LineOffsets(pos, radius, structure, ids, from, to, facing, center)`; new test
`LineSlotsDoNotMakePathsCross` (3 cases).
## Client files (all new except the hooks)
Input/OrderEditMath.cs (pure: IsDrag 8px, FacingFromDrag, Cluster, Nearest), Input/FormationDrag.cs, Input/WaypointEditor.cs,
Input/OrdersUiDemo.cs (screenshot hooks), Rendering/OrderMarkers.cs (Draw3D/Draw2D).
Shared edits: SelectionController (Formation/Waypoints fields+ctor; Update: Refresh, TryBeginDrag on left press,
right press → Formation.Begin in Normal/AttackMove, click handled on release); Hotkeys (DeleteWaypoint = Delete, Units,
Orders; in All; 2 mouse lines "Right drag", "Drag a waypoint"); GameApp ClientOptions UiFormation/UiWaypoints + 2 case
lines; GameSession: _ordersDemo field + ctor line + Update line, OrderMarkers.Draw3D + Draw2D lines.
CLI: `--ui-formation x1,z1:x2,z2` (8 tanks spawned 14 u in front, preview frozen), `--ui-waypoints x,z` (6 tanks,
4 queued waypoints, 2nd shown dragged). Both print "[ui-orders] ... tanks at x,z"; need cheats (CLI default on).
## TODO (next agent, in order)
1. tests/Rts.Client.Tests/OrderEditMathTests.cs: FacingFromDrag (left→right = away from camera, right→left = towards),
   Cluster (eps 0.05 merge, distinct units, Ferry drop separate), IsDrag threshold, Nearest. Run HotkeyTableTests.
2. Screenshots 960x540 (max 3): screenshots/7b_formation_drag.png, 7b_waypoint_edit.png; find open ground near the
   demo start (run once, read the printed tank position), aim with --camera x,z,dist.
3. Full suites Sim/Client/Net; then write the final short report (cmds in the brief).
Pitfall: `sed -i` on this volume warns "Operazione non permessa" (perms) but still writes; prefer python edits.
## Part A run (agent 7b-sel-4, 2026-11-10): hooks verified, nothing broken
- Both hooks work as committed; pick radius 22 (7ad9342) kept: "[ui-orders] selected 8 tanks" / "6 tanks" (new print).
- Demo map: local start 38,38 (256 map), flat plateau x 14..62, z 14..62; demo army stands at ~40..66 on both axes.
  `--demo --ui-formation 14,22:34,22 --camera 24,29,45` -> screenshots/7b_formation_drag.png: green line, 8 slot rings
  each with a white facing arrow, label "Line formation · 8 units", the 8 selected tanks below.
  `--demo --ui-waypoints 8,16 --camera 26,35,62` -> screenshots/7b_waypoint_edit.png: 4 markers (3 green, 1 red
  attack-move), 2nd one white-ringed with its ghost + "Release: move waypoint" (a lower camera hides wp 3 behind the HUD).
- Screenshots are NOT committed: `screenshots/` is gitignored and the repo tracks no PNG at all (WORKFLOW: local only).
- Possible polish (not done): marker rings are 0.7 world units (about 12 px at distance 62) and each unit draws its
  own order line to its own slot, so the attack-move marker is hard to spot among the red lines when zoomed out.
