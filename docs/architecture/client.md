# Client shell: app, session, screens, settings

The raylib application around a game: options and window, the screen loop, one running game (`GameSession`), the
camera, the menus, the settings, user folders and release builds. Rendering, input / UI and audio have their own files.

**Contracts of this area**
- The client reads `SimSnapshot`s and writes `Command`s. It never touches `SimWorld` while the runner runs
  (CLAUDE.md hard rule 3); `Terrain` and `BlueprintDb` are immutable and may be read.
- Per-game client modules live in `GameSession`: a field, the constructor, `Frame`, `Dispose`.
- Screens are `IScreen`s run by `AppHost`.
- A setting's missing field means its default, which is the behavior before the setting existed.

Files and top-level types of this area: [index/client.txt](index/client.txt) (generated).

## Client (`Rts.Client`)
- `GameApp`: options + window + screen loop (`Menus/AppHost`, `IScreen`): `MainMenuScreen` → `SettingsScreen` /
  `SkirmishSetupScreen` (slots, map settings, live `MapPreview`) → `LoadingScreen` (sim built on a worker thread) →
  `InGameScreen` (pause menu on Esc, end screen with `ArmyStats`). CLI game arguments still start a game directly.
- `GameSession`: one running game (runner, camera, renderers incl. fog/wrecks, input, HUD, cursor hints,
  `GameAudio`); `Frame()` is the in-game frame. Add per-game client modules there (field + constructor + `Frame` +
  `Dispose`). `GameSession.CreateSimulation(cfg)`: `GameConfig` slots → `SkirmishSettings` (AI slots set
  `Army.IsAI` + `AiDifficulty`, so the in-sim skirmish AI drives them); fog `ViewArmy` = the human slot's army.
- `RtsCamera` (zoom to cursor, strategic view) · `SnapshotInterpolator`

- Pause menu "Load Game": `AppHost.Go(next, keepOld: true)` keeps the paused `InGameScreen` alive; `SaveListScreen`
  (`returnTo`) owns it: Back returns to it, loading a save disposes it.

- User folders & release builds (wave 7a; DEV_GUIDE "Packaging"): `Rts.Sim/Persistence/UserDirs` resolves the folders once
  at startup (`UserDirs.Apply()` in Program.cs → `SaveStore.SavesDir/ReplaysDir`). Rule: a `release.txt` marker next to
  the executable (written by `tools/package/package.sh`) = release mode → saves/replays/screenshots in
  `$XDG_DATA_HOME/RtsGame` (`%APPDATA%\RtsGame`); otherwise (dotnet run, tests, Bench) relative to the cwd as before;
  `RTS_USER_DIR` overrides. Settings always `$XDG_CONFIG_HOME/RtsGame/settings.json`; `data/` found by walking up from
  the exe. `CrashLog` (user data dir `crash.log`, main thread + unhandled + first sim-thread fault), `AppVersion.Label`
  (main menu), `AppIcon` (procedural window icon, `--export-icon`).

- Settings (wave 7a): `Menus/SettingsScreen` has three tabs (Audio & Display / Graphics / Gameplay; `--settings-tab
  audio|graphics|gameplay`), also opened from the pause menu (`returnTo` = the paused game, like the save list; not in
  multiplayer, where leaving `NetInGameScreen` would stall the lockstep turns). `AppSettings` fields: `Shadows`,
  `ParticleQuality` (0..2 → 0.35/0.65/1), `TerrainDetail` (-1 auto, 0..3 forced LOD), `VSync`, `FpsCap` (0/30/60/120/144),
  `UiScale` (0.8..1.5), `EdgeScroll`, `PanSpeed`/`ZoomSpeed` (0.5..2), `Autosave` (read by the autosave). `LoadFrom`
  clamps (`Normalize`); missing fields = defaults = the behavior before the settings existed. `Menus/GraphicsSettings`
  (`Init` after InitWindow, `Apply` on every change, all live) writes static switches read by the renderers:
  `ShadowMap.Enabled` (skips the unit depth pass, `shadowOn = 0`), `ParticleSystem.Quality` (folded into `Lod`),
  `TerrainRenderer.ForceLod`, `Ui.UserScale` (× automatic `Ui.S`), `CameraSettings` (read by `RtsCamera`), plus
  VSync (`SetWindowState/ClearWindowState`) and `SetTargetFPS` (only called once a cap is chosen). Precedence:
  `--novsync` and `--terrain-lod` win for that run (the screen shows a note); `--screenshot` runs use defaults.

The other bullets of the old "Client" section are in [rendering.md](rendering.md), [input-ui.md](input-ui.md) and
[audio.md](audio.md).
