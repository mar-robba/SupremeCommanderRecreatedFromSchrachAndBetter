# Sandbox mode

Spawn anything, cheats, nukes, any-army control: the sim commands, the side panel, the console and the `--exec` script.

**Contracts of this area**
- Sandbox actions are `SandboxCommand`s, applied at the start of a tick like every command (deterministic,
  replay-safe) and ignored in a normal game.
- The sandbox nuke is a real strategic missile launched through `StrategicApi`.
- `SandboxConsoleParser` and `BlueprintMatcher` are pure (no raylib) and unit-tested.

Files and top-level types of this area: [index/sandbox.txt](index/sandbox.txt) (generated).

## Sandbox mode (`Rts.Sim/Systems/SandboxSystem`, `Rts.Client/Sandbox`)
- Sim: `SandboxSystem.Enable(world)` (called by `GameSession.CreateSimulation` when `GameConfig.Sandbox`) turns the
  `SandboxCommand`s on (`Orders/SandboxCommands.cs`; ignored in a normal game): batched spawns (250 units/tick, square
  spiral, structures on free footprints), per-army toggles (infinite resources, instant build, build anywhere),
  AI on/off + difficulty at runtime, victory on/off (off = `WinningTeam` stays -1), unit actions (kill, delete, heal,
  damage, change owner, finish), clear area, nuke. `SandboxSystem.Nuke` launches a REAL strategic missile
  (`StrategicApi.LaunchNuke`, free) from the army's nearest strategic silo, else its commander, else the map edge
  nearest its start position (`NukeLaunchSite`): same flight, global launch warning, anti-nuke interception, shield
  knock-down and crater as a silo launch. `SandboxNukeCommand.Instant` (`nuke instant`) = `NukeInstant`: the same
  strategic detonation right away (`StrategicApi.Detonate`, rings 30/70 000 + 40/500), no missile. Instant build also
  fills silo stockpiles (`SiloSystem.InstantStockpile`), so with money + instabuild a silo nuke is ready at once. Like every command
  they are applied at the start of a tick: deterministic and replay-safe. `SimSnapshot.Sandbox` = immutable `SandboxStatus`.
- Client: `SandboxController` (owned by `GameSession`, null outside sandbox) = side panel (`SandboxPanel`, F9: army
  selector, cheats, AI, speed, selection actions, NUKE (missile) / Blast (instant) / clear tools, spawn list of every
  blueprint grouped by category/tech — shields, silos, missile defense and artillery installations in a "Strategic &
  shields" group — with search) + console (`SandboxConsole`, F10 or the key left of 1) + map tools + `--exec` script.
  Panel and console both produce `ConsoleAction`s (`ConsoleActions.cs`) run by `SandboxController.Execute`.
- Any-army control: in sandbox the player can select and order units of every army (`Input/ArmyCommands.cs` splits
  each command into one per owning army, since the sim checks every unit against the command's Army). Panel
  actions work on enemy selections; `--select any:<id>` selects another army's unit at start. Change owner uses
  `UnitTransfer` (same as capture).
  `SandboxConsoleParser` / `BlueprintMatcher` are pure (no raylib) and unit-tested in `Rts.Client.Tests`.
  While a sandbox text field has focus: `SelectionController.InputBlocked`, camera input off, debug/fog/build hotkeys
  skipped; F9/F10 effect debug keys are disabled in sandbox (`EffectsRenderer.DebugKeysEnabled`).
  `PlacementGrid.IgnoreDeposits` mirrors the local army's build-anywhere. `InGameScreen` skips the end of game unless
  victory is on. Entry points: main menu "Sandbox" (skirmish setup with an "Enemy AI" switch, default off), `--sandbox`,
  `--exec "cmd; cmd"` (implies `--sandbox`; `wait <frames>` for screenshots).
- Nuke visuals (ONE effect for every nuke: silo missiles, sandbox missiles, sandbox instant blasts): `EffectKind.Nuke`
  → `Rendering/NukeFx.cs` (self-contained, owned by `EffectsRenderer`: `Spawn` on the event, `Update`/`Build` in the
  particle pass, `DrawOverlay` = 2D screen flash right after `EndMode3D`, strength by on-screen/camera distance, none
  for far off-screen blasts): flash, draped shockwave/dust rings, fireball, mushroom cloud (private sorted smoke-puff
  pool), sparks/debris, scorch decal (90 s). Sound: ONE map-wide synthesized `SoundId.NukeExplosion`
  (`GameAudio.Strategic.cs` `PlayNuke`, ≈1/distance with a floor).
