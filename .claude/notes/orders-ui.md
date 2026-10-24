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
