# (7b) Orders & selection UX — handoff (coordinator 1, CTX 7b-coord-1)
Worktree `/run/media/marco/RTS/.claude/worktrees/wave7b`, branch `wave7b-orders` (from main de11d52, merged).
Brief: ROADMAP "Wave 6+ plan" row 7b; G14 G15 G16 G17 (+G20 if time). One sub-agent at a time.

## Baselines (main == branch start)
- `Rts.Bench -- 2000 300` (BENCH_WARMUP=1): total 0.9s, hash F74F5FED38AC5D83
- `Rts.Bench -- ai 30 256 42 hard,normal` must stay "WINNER: Player (Hard) after 15:35", hash BF85B7DBBF03D949

## Design (one design across sim + client)
- Sim commands (new files `src/Rts.Sim/Orders/QueueEditCommands.cs`, `PingCommands.cs`; one `case` each in CommandSystem):
  - `MoveWaypointCommand(Army, Units, From, To)`: every queued order of the units whose group point `Pos`≈From
    (position orders: Move/AttackMove/Patrol/Ferry/Unload/Teleport/Launch? see notes/sim.md) gets Pos=To;
    Ferry `Aux`≈From → Aux=To (ferry route editing); patrol Aux (= previous order's Destination) follows.
  - `DeleteWaypointCommand(Army, Units, At)`: removes those orders.
  - `FormationMoveCommand(Army, Units, OrderType Move|AttackMove, From, To, Facing, Queue)`: line formation across
    the drag, facing given by the client (normalized in sim).
  - `MapPingCommand(Army, PingKind Alert|Move|Attack, Pos)`: throttled per army, kept ~10 s, snapshot `Pings`
    (client draws only allies' pings).
- Snapshot additions: `OrderView` gets the raw group point (for markers), `Pings` array.
- Client: markers clustered by group point; left-drag a marker = MoveWaypoint, Delete over marker = delete.
- Keys (proposed; check HotkeyTableTests): Ctrl+Z / Ctrl+Shift+Z same type on screen / map; Ctrl+A all mobile
  combat; Ctrl+Shift+L/A/N/E land/air/naval/engineers; Ctrl+C commander + center; Alt+1..5 recall camera
  bookmark, Ctrl+Alt+1..5 save; Backspace jump to last alert; pings: Alt+left = alert, Alt+right = move,
  Alt+Shift+left = attack (map + minimap), key Y = alert ping at the cursor.

## Pieces (status) — coordinator 2 (CTX 7b-coord-2) from 2026-11-10
0. [x] 2026-11-10 `git merge main` (7a in; clean, no conflicts, 5a89b0a). Build 0 errors; Sim 522, Client 288, Net 27 green.
1. [x] sim commands + tests (24190b3, 299cd87, 4267983) -> notes/sim.md (contract). QueueEditTests 11/11.
2. [ ] selection filters, commander key, camera bookmarks -> sub-agent 7b-sel-4 (Part B), notes/selection.md
3. [~] formation drag + waypoint editing UI: code 6f2a0f3, OrderEditMathTests 8b671fd; missing: run the hooks +
   screenshots (7b-sel-4 Part A). 7ad9342 WIP = OrdersUiDemo pick radius 14->22 (to confirm). notes/orders-ui.md
4. [ ] pings UI + notification pass (AlertLog, minimap flash, toasts) + jump to last alert + ping replay test
   -> sub-agent 7b-ping-1, notes/pings.md
5. [ ] G20 range/intel overlays (if time) -> notes/overlays.md
6. [ ] finish: merge main, build, 3 suites, bench 2000 300, ai ref match, screenshots, docs (ARCHITECTURE section,
   ROADMAP G14-G17 rows `[x] 2026-11-10 (7b)` + later items, CLAUDE.md CLI lines), `.claude/agent-report.md`

Sub-agent rules file: .claude/notes/common.md (prompts point to it). One sub-agent at a time. `.claude/` is gitignored:
notes are tracked with `git add -f`.
