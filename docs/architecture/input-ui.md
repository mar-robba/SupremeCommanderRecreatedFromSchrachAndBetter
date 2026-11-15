# Input, HUD and UI

Selection and orders from mouse and keyboard, the hotkey table and its rebinding, the HUD panels, the minimap,
notifications and pings.

**Contracts of this area**
- `Input/Hotkeys.cs` is the one key table. Input code never reads a raw key for a game action; `HotkeyTableTests`
  fails on clashes and on keys polled outside the table.
- Input changes the game only by sending `Command`s.
- Pure helpers (`SelectionMath`, `SharedHotkeys`, `MinimapView`) make no raylib calls and are unit-tested.
- Every map ↔ screen conversion on the minimap goes through `MinimapView`.

Files and top-level types of this area: [index/input-ui.txt](index/input-ui.txt) (generated).

## Selection, hotkeys, gameplay toggles (from "Client")

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

## Order input and notifications (from "Orders & selection UX", wave 7b)

- **Client input:** `SelectionFilters` (Ctrl+Z / Ctrl+Shift+Z same types, Ctrl+A combat, Ctrl+Shift+L/A/N/E, Ctrl+C
  commander), `CameraBookmarks` (Alt / Ctrl+Alt+1-5, per session), `FormationDrag` (right-drag), `WaypointEditor`
  (left-drag a marker, Del), `PingInput` (Alt+left / Alt+Shift+left / Alt+right on map and minimap, Y; Backspace =
  `AlertLog.NextJump`). All keys are entries of `Hotkeys`.
- **Notifications:** `AlertMonitor.Poll` (GameSession, every frame, with or without an audio device) compares the
  previous and current snapshot + new effects: own / allied health drops, own complete structure `UnitDeath`, own
  structure `BuildComplete` (an upgrade keeps the unit handle, so "same handle, other blueprint in prev" = upgrade done),
  unit cap reached, new pings. Every alert with a position goes to `SelectionController.Alerts` (`AlertLog`: merges a
  kind within 30 u / 8 s, minimap flash 4 s, Backspace cycle); toasts + sounds go through `GameAudio.Notify` with
  per-kind cooldowns (own attack: only off screen, 25 s; commander 20 s; ally 30 s). `PingMarkers` draws pings on the
  map (screen space) and pings + flashes on the minimap. Screenshot hooks: `--ui-formation`, `--ui-waypoints`, `--ui-pings`.

## Minimap options (from "Terrain self-shadows, unit LOD, minimap options", 9b-2)

- Minimap: `MinimapView` (pure) = size steps 75-175 %, zoom steps 1-8× (wheel over the minimap), `Virtual` = the
  whole map's screen rectangle (zoom centred on the camera focus, clamped to cover the panel); every map↔screen
  conversion (dots, fog, pings, clicks) goes through it, drawn under a scissor; view frozen while dragging.
  `Minimap.Options.cs`: hover buttons (− + size, Z− Z+ zoom, R resources, U units, T trees) and the tree layer.
  Settings tab "Minimap" (size, zoom, resources, units, trees) saved in `AppSettings`; `--minimap
  size=1.5,zoom=3,resources=off,units=on,trees=on,buttons=on` overrides for one run.

## Keybindings (10b-1, client only: `Input/Hotkeys.cs`, `Input/Hotkeys.Bindings.cs`, `Menus/KeyBindingsScreen.cs`)
- **Override layer**: `Hotkey.DefaultKeys/DefaultMods` = the table (never changed); `Keys/Mods/Label` = the effective
  binding (`SetBinding`, `Label` cached, no per-frame allocation). `Hotkey.Id` = the field name in `Hotkeys` (assigned
  by reflection after `Groups`; `Hotkeys.ById`). The table is kept clash-free: `TryBind` refuses a clash (same key +
  mods, overlapping scopes, no common `Share` tag = the old `Clashes` logic), `TrySwap` gives the clashing entries the
  old binding (refused when one is fixed, has another key count, or a third entry would clash), `Reset` (= TryBind to
  the default, refused/swappable the same way), `ResetAll`. `Hotkeys.Revision` counts changes.
- **Rules**: one key or alternatives (`alt: true`: = / keypad +, Enter / keypad Enter, T / Tab, F10 / `, dialog keys)
  rebind to ONE key + mods; a row of slots (arrows, PgUp/PgDn, 0-9, 1-5, Z X C V B N, Ctrl+Shift+L A N E) rebinds as a
  whole row (same number of distinct keys, one modifier set; the capture asks key 1 of N…); entries on Esc (`Menu`,
  `DialogCancel`) are fixed (Esc cancels the capture). Modifier semantics unchanged: Ctrl must match, Shift/Alt free
  unless bound. Held keys (`Down(i)`) follow the same rule, so the camera arrows / PgUp / PgDn now stop while Ctrl is
  held (before 10b-1 they ignored modifiers); exact Ctrl keeps a WASD pan from firing on Ctrl+A (select all combat units).
- **Shared keys** (D, E, U, M) resolve by selection only while both entries still have the same binding
  (`Hotkeys.SameBinding`): split, each key does its own action (UnitToggles capture, TransportInput unload vs upgrade,
  GameAudio mute vs move, OrdersPanel capture label).
- **Saved** in `AppSettings.KeyBindings` (string, only non-default entries): `"v1;Stop=Ctrl+F11;FactorySlots=Alt+Q W E R T Y"`
  (raylib key names, digits also "1"). `AppSettings.Load()` → `Hotkeys.LoadBindings` (reset + apply). `ApplyBindings`
  skips unknown ids, bad keys/mods, invalid counts and fixed entries, then undoes the later-applied entry of any clash
  pair (a saved swap loads because all are applied before the check). `--bind "Id=Spec,..."` applies on top for one
  run (screenshot runs never read settings.json); the Keys tab writes `ExportBindings()` on close only when it changed.
- **Screen**: Settings tab "Keys" (`--settings-tab keys`, also from the pause menu's Settings): the table by help
  group, scrollable; click = capture (`GetKeyPressed`, mods held at the first key), right click / "Reset" = default,
  "Reset all"; a clash opens a prompt (Swap / Cancel); `Capturing` keeps Esc from closing the settings. `--keys-capture ID`.
- **Input source**: `Hotkeys.Input` (`IKeyState`, raylib by default) so tests drive `Pressed/PressedIndex/Down` without a window.
- **Key checks moved into the table** (they rebind now): camera pan/rotate/reset (`RtsCamera`: `Pan.Down(i)`,
  `Rotate.Down(i)`), mute/music (`GameAudio`), self-destruct dialog (`Hud`: DialogConfirm/Cancel, SelfDestruct),
  build/factory slot rows, tiers, category, repeat (`BuildMenu`, `FactoryPanel`), Rotate90, Fog, Perf, debug F9/F10,
  sandbox panel/console, upgrade U, quicksave/quickload (`IsKeyPressed(Hotkeys.X.Key)` → `X.Pressed()`).
- **Fixed on purpose** (not in the editor): Esc everywhere (menus, placement cancel, help close, order-mode cancel);
  modifier reads (Shift = queue/add, Ctrl/Alt clicks); text fields (`SandboxTextField`, `NetTextField`, `MenuUi`
  number fields, chat line), the sandbox console's Up/Down/Tab history keys, menu keys (`MenuUi.Key`, Enter/Backspace
  in lists), `ReplayScreen` (follows the rebound keys, modifiers ignored), `PingInput` mouse+Alt combos.

## Enhancement chain, leftovers, screenshot options (from "Selection overlays", 10b-2)

- **Enhancement swap / chain** (G11): the sim installs an enhancement only into an empty slot or onto its exact
  prerequisite, and `RemoveEnhancementCommand` clears a slot's whole chain at once in the command phase. So a click on
  a not-installed enhancement (amber "swap" button when the slot is taken or a prerequisite is missing) sends, per
  unit, Remove (if the slot holds something outside the target's chain) + the chain's first step in the same tick;
  `EnhancementChain` keeps the later steps client side and sends each when the previous one is installed. A step
  cancelled (Stop, Cancel), refused, or not started within 20 ticks drops the rest. No sim change; not saved.
- Leftovers: minimap zoom keys `Hotkeys.MinimapZoomIn/Out` (Ctrl+= / Ctrl+-, one wheel step, saved); the "Structure
  lost" toast skips units the player self-destructed (`AlertLog.NoteSelfDestruct`, matched by handle in the previous
  snapshot); `UnitPanel` groups the selection without LINQ (was ~180 KB/frame with 500 selected).
- CLI for screenshots: `--select "<what>@N"` waits until N units match (sandbox `--exec` spawns arrive later),
  `--select everything` = units + structures.
