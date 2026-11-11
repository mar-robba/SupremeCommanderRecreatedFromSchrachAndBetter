# (7b) piece 4: pings UI + notification pass + jump to last alert (agent 7b-mod-1, 2026-11-11)
Status: IN PROGRESS (design fixed, code being written).
## Design
- `UI/AlertLog.cs` (pure, no raylib): `AlertKind` (UnderAttack, CommanderUnderAttack, AllyUnderAttack, StructureLost,
  ConstructionDone, UpgradeDone, UnitCapReached, Ping), entries (kind, pos, time, text); per-area cooldown for
  flashes, per-kind cooldown for toast + sound; `NextJump` cycles newest -> older (Backspace).
- `UI/AlertMonitor.cs`: per new snapshot tick: own/ally health drops (prev->cur), own UnitDeath of complete
  structures, own BuildComplete of structures (upgrade = same handle had another bp in prev), unit cap reached.
  Replaces GameAudio.CheckUnderAttack (that one only ran with an audio device); toast + sound via
  `GameAudio.Notify(kind, text, color)` (Audio/GameAudio.Alerts.cs).
- Pings: `Input/PingInput.cs` (Alt+left alert, Alt+right move, Alt+Shift+left attack, on map + minimap; Y = alert at
  cursor) -> `MapPingCommand`; `UI/PingMarkers.cs` draws snapshot `Pings` on map (screen space) + minimap; new
  ping of an ally -> sound + alert log entry + toast.
- Owners: `SelectionController.Alerts` (AlertLog) + `.Pings` (PingInput) so Minimap and hotkeys reach them;
  GameSession polls the monitor every frame (also when not interactive).
