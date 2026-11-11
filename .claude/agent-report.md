# (7b) Orders & selection UX — agent report (7b-mod-1, 2026-11-11)
**Works:** sim `MoveWaypoint/DeleteWaypoint/FormationMove` + `MapPingCommand` (replays, lockstep); right-drag line
formation with facing; drag / Del waypoint + ferry markers; selection filters, commander key, camera bookmarks; map pings
on map + minimap (allies, observers, replays); notifications via new `AlertMonitor` (own/ally under attack, commander,
structure lost, construction/upgrade complete ×N, unit cap) with toasts, alert sounds, minimap flashes; Backspace jump.
**Try:** `--demo --ui-pings 120,120 --camera 128,128,70 --frames 440`; `--ui-formation 14,22:34,22 --camera 24,29,45`;
`--ui-waypoints 8,16 --camera 26,35,62`.
**Tests:** QueueEditTests (11), PingTests (4, incl. replay), PingNetTests (MP loopback), SelectionFilterTests (9),
AlertTests (9), OrderEditMath. Sim 522, Client 309, Net 27 green.
**Keys (all):** Ctrl+Z / Ctrl+Shift+Z same types screen/map; Ctrl+A combat; Ctrl+Shift+L/A/N/E land/air/naval/engineers;
Ctrl+C commander; Alt+1-5 recall, Ctrl+Alt+1-5 save bookmark; Backspace last alert; Y alert ping; Del delete waypoint;
mouse: right-drag formation, left-drag waypoint, Alt+left / Alt+Shift+left / Alt+right = alert / attack / move ping.
**Shared edits:** CommandSystem (4 cases, ApplyIssueOrder param), OrderSystem/MovementSystem (arrival turning, 1 line
each), Order.cs RemoveAt, SimSnapshot/SnapshotWriter (OrderView Point/Aux, Pings), GameSession (alerts, pings, demo),
GameApp (3 options), SelectionController, Hotkeys, Minimap, GameAudio (CheckUnderAttack moved out), SoundBank enum +3,
UnitCapCounter (toast moved).
**Numbers:** ai 30 256 42 = WINNER Player (Hard) 15:35, hash BF85B7DBBF03D949; bench 2000 300 hash F74F5FED38AC5D83,
2.46-2.83 ms/tick (1.98 earlier; machine shared with 8a, no interleaved main run; this session changed no sim code).
**Not done:** G20 overlays; keys never pressed by hand in a game window; bookmarks not saved; see ROADMAP "later (7b)".
**Handoff review:** the cut 7b-ping-1 left nothing, so piece 4 was redone from a grep of GameAudio/Toasts/Minimap;
the old under-attack alert only ran with an audio device (now fixed); two toast stacks (Persistence.Toasts and
GameAudio) still share the top center. Context: ~115k used before any work (prompt + notes), so piece 3/5 were dropped.
