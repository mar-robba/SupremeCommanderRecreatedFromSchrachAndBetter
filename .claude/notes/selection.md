# (7b) G14 selection filters, commander key, camera bookmarks (agent 7b-sel-5, 2026-11-10) — client only, no sim change
Commits: 67f3a2b (code + hotkeys), 7f942e9 (tests), + this note. Build Rts.slnx 0 errors; Rts.Client.Tests 297/297 green.
Files (new): `src/Rts.Client/Input/{SelectionFilters,CameraBookmarks}.cs`, `tests/Rts.Client.Tests/SelectionFilterTests.cs` (9 tests).
Keys (all scope Game = also in replays; entries in Hotkeys.cs + `All`):
- Ctrl+Z `SelectSameTypes`: ADD every unit of the selected (army, blueprint) types on screen (structures too).
- Ctrl+Shift+Z `SelectSameTypesMap`: same, whole map.
- Ctrl+A `SelectCombat`: REPLACE the selection with all own armed mobile units (no engineers, no commander).
- Ctrl+Shift+L / A / N / E `SelectDomain` (one entry, 4 keys): all own land / air / naval units / engineers
  (mobile, no commander; land/air/naval exclude engineers but include unarmed units: transports, scouts...).
- Ctrl+C `SelectCommander`: select the local commander(s) + center the camera (Shift: add; pressed again: centers again).
- Alt+1..5 `BookmarkRecall`: go to camera bookmark (x, z, zoom, yaw). Ctrl+Alt+1..5 `BookmarkSave`: save it (toast).
  Empty slot recalled: toast. Still free for the next piece: Backspace, Y, Alt+mouse buttons.
Shared-file edits:
- Hotkeys.cs: const `Al` (Alt), 7 entries (2 Camera, 5 Selection), `All` list, helper `Digits(first, last)`.
- SelectionController.cs: field `Bookmarks`; in HandleHotkeys 2 call lines (`SelectionFilters.HandleHotkeys`,
  `Bookmarks.HandleHotkeys`) + control groups (GroupSet / GroupRecall) skipped while Alt is held.
Rules: pure `SelectionFilters.Matches(filter, bp, army, localArmy, selectedTypes)`, `IsCommander`, `TypeKey`;
  class filters use `ctx.LocalArmy` only (also in the sandbox) and skip units under construction; a class filter with
  no match leaves the selection alone; a replace cancels the order mode / placement.
Pitfalls: `Hotkey.Pressed()` leaves undeclared Shift/Alt free, so the handler checks the Ctrl+Shift entries first and
  the plain Ctrl+Z / Ctrl+A only without Shift (so no "Shift = add" for those; only Ctrl+C has it). Build menu /
  factory slot keys (Z X C V B N) already return on Ctrl/Alt, so no double action.
NOT verified: nothing run in the game window (only build + unit tests); a desktop may grab Alt+digit / Ctrl+Alt+digit.
TODO: bookmarks are per session (not in saves), no on-screen list; no "add" variant for class filters; ROADMAP line for G14.
