# Save/load, replays, save migration

The serializer behind saves, replays and seek checkpoints, the file formats, migration of old saves, and the client's
recording, save lists and replay viewer. Multiplayer saves are in [multiplayer.md](multiplayer.md).

**Contracts of this area**
- One serializer for everything: it walks every instance field reachable from the `Simulation` and every per-world
  `ConditionalWeakTable<SimWorld, T>`.
- Sim state must be saveable (CLAUDE.md hard rule 9): plain fields, arrays and collections, no static mutable state,
  no stored delegates; caches get `[NoSave]`. `SaveCoverageTests` and `PersistenceTests` enforce it.
- Save → load → continue gives the same `StateHash` as the uninterrupted game.
- Saves survive layout and blueprint changes (migration). Replays stay bound to the exact sim build, blueprints and
  math version.
- A capture happens only on the sim thread or with the runner stopped.

Files and top-level types of this area: [index/persistence.txt](index/persistence.txt) (generated).

## Save/load & replays (`Rts.Sim/Persistence`)
- One serializer for saves, replays and seek checkpoints: `WorldState.Capture(sim)` writes the whole object graph
  reachable from the `Simulation` plus every static `ConditionalWeakTable<SimWorld, T>` of Rts.Sim (per-world system
  state, discovered automatically) — every instance field (private/readonly too) through generated IL (`Val.cs`,
  `Codecs.cs`, `GraphIO.cs`), object identity/shared references kept, unmanaged arrays block-copied. The immutable
  `BlueprintDb` and `Terrain` are not stored: references into them become ids (`ExternalSet`); the terrain is
  regenerated from `GameInfo.Map` (embedded in the file when there are no generator settings). `WorldState.Restore`
  rebuilds the identical graph: save → load → continue gives the same `StateHash` and the same bytes as the
  uninterrupted game (`PersistenceTests`, 1 and 4 threads).
- Compatibility: every type is written once with its name + field-layout signature; a different layout or changed
  blueprints are migrated field by field (section "Save migration (11a-2: files)"), a different terrain or a change
  that cannot be migrated → `IncompatibleSaveException` naming the type / field / blueprint id; damaged files → `SaveFormatException` (magic, version, payload length
  + hash, bounds-checked reads); another math version (`GameInfo.MathVersion`, 10a-1) → replays refused, see
  "Deterministic math (cross-OS)". Migrations (`SaveMigrations.cs`): the container version (`SaveContainer.Version`,
  now 2; files ≥ `MinVersion` load) is upgraded on load one step at a time: a header-JSON step (also for file lists)
  and a payload step per version (v1→v2: players without a faction get `FactionDb.DefaultFaction`); `GameInfo.FileVersion`
  = the version read. To change the format: bump the version, add a `Steps` entry + a `case`, extend
  `SaveMigrationTests` (writes old-version files from a current game with `SaveContainer.EncodeRaw`). The payload's
  field layouts are migrated per type (11a-2, next section); replays still need the exact sim build and blueprints.
- Files (`SaveFiles.cs`, `SaveStore.cs`): magic + version + JSON header (`GameInfo`: map settings, players, local
  army, sandbox, opaque client config, length, winner, build) + Brotli payload. `saves/*.rtssave` = state + the replay
  so far; `replays/*.rtsreplay` (newest 50 kept). Menu lists read only the headers.
- Replays (`Replay.cs`, `ReplayPlayer.cs`): `SimRunner.Hooks = ReplayRecorder` stores the initial state (first
  tick), each tick's command list and a `StateHash` every 10 ticks (≈0.02 ms/tick). `ReplayPlayer` (as the runner's
  hooks or headless) re-simulates from the initial state with the recorded commands (incoming ones are ignored) and
  compares the hashes → `DesyncTick`; `SeekTo` backwards restores a compressed checkpoint (every 600 ticks) and
  re-simulates. A loaded save keeps recording into its replay, so a replay always covers the whole game.
  Headless check: `Rts.Bench -- replay <file>`; timings/sizes: `Rts.Bench -- savebench`.
- Threading: `SaveGame.Capture` only on the sim thread (a `SimRunner.Post` job; jobs also run while paused) or with
  the runner stopped; `SaveGame.Encode` (compression) and file writes run on a background task.
- Client (`Rts.Client/Persistence`, `Menus/{SaveListScreen,FileLoadingScreen,ReplayScreen,InGameSaveUi,MessageBox}`):
  `GameRecording` (owned by `GameSession`) sets the recorder as runner hooks for every game, captures saves through a
  runner job and writes files on a worker (toasts); the replay is written when a team wins and again on quit/restart
  (same file; games under 5 s are dropped; app exit waits for pending writes). `ClientGameData` = the client config
  stored in `GameInfo.ClientData` (rebuilds names/colors/sandbox on load). Pause menu "Save Game" dialog, F5/F6
  quicksave/quickload; main menu "Load Game" / "Replays" lists (header only; damaged/incompatible files greyed with
  the reason; load errors in a message box). Replay viewer = `GameSession` over `ReplayPlayer.Sim` with
  `GameContext.ReadOnly` (no order/build/enhancement UI, commands ignored by the player anyway): play/pause, speed
  0.5×–16×, timeline seek (`ReplaySeeker`: runner job `SeekTo` + `ReplaceSimulation`), fog view per army or all,
  DESYNC banner, other-build warning.
- Autosave (wave 7a, `Rts.Client/Persistence/Autosave.cs`, pure + `AutosaveTests`; wired in `InGameSaveUi.AutosaveStep`
  from `InGameScreen`'s Playing state): every 3000 ticks of GAME time (5 min; counted from the first snapshot tick seen,
  so a loaded game waits a full interval and a paused game never autosaves) the normal save path writes `autosave_N`
  (N = 1..3: first empty slot, else the oldest file, never the slot this game wrote last; other saves untouched),
  toast "Autosaved", listed as "Autosave N". Only with the setting on (`AppSettings.Autosave`), a recording, not
  replay/sandbox/scene/multiplayer (`NetInGameScreen` has none), not after a defeat or the end of the game, not in
  `--screenshot` runs unless `--autosave-every TICKS` (test option: interval, forces it on).
- **Keeping new state saveable** (rule for every feature): sim state lives in plain fields of sim classes/structs,
  arrays, `List`/`Dictionary`/`HashSet`/`Queue`/`Stack`, or a per-world `ConditionalWeakTable<SimWorld, T>`.
  No static mutable state, no stored lambdas/delegates (store an enum/id and dispatch in code; a cached delegate to
  your own method = `[NoSave] Action? _f;` + `_f ??= Method`), no framework interfaces (`IList<T>`, `IEnumerable<T>`)
  or `object` as field types. Caches, scratch buffers and wall-clock timings: `[NoSave]` (auto-properties
  `[field: NoSave]`), rebuilt lazily or in `IAfterLoad.AfterLoad()`; never `[NoSave]` anything that affects future
  ticks. `SaveCoverageTests` walks every reachable field type and fails with the field path and the fix;
  `PersistenceTests.SaveLoadContinueMatchesUninterruptedGame` proves the round trip on a rich game (AIs, every
  blueprint spawned, missiles, nukes, upgrades) — extend that scenario if your feature has state it doesn't reach.

## Save migration (11a-2: files `Persistence/SaveLayout.cs`, `SaveMigrationCodecs.cs`, `SaveBlueprintIndices.cs`, `WorldState.cs`)
Saves survive sim layout changes and blueprint edits; replays do not (they stay bound to the exact build).
- **Format.** The graph stream is unchanged: each type is written once as (stable name, signature); the signature
  already lists every saved field as `name:type` (structs inline, `#rawN` for raw blocks), so `SaveLayout` parses it,
  no extra table. `WorldState.FormatVersion` 2 adds after the old header: the blueprint table (count, then id +
  per-blueprint hash `WorldState.BlueprintHashes`) and the offset of an external-key table written after the graph
  (saved external id → stable path, `ExternalSet.PathOf`: `#0` db, `#1` terrain, `bp:ID/Field[i]…`, `empty:T[]` for
  `Array.Empty` shared by the sim's `= []` fields). File size +1.4 % (714 → 724 KB on `savebench 8 256 2`).
- **Fast path.** `GraphReader.ReadType`: a type whose saved signature equals this build's (and holds no blueprint index
  to translate) reads through the generated codecs, at today's speed. Any other type gets a read plan
  (`MigrationContext.CodecFor`): saved field → current field by name (then `SaveRenames.Fields`), field gone → its
  bytes skipped (`SkipPlan`; a gone class/array is read as a `DroppedObject` so object numbering stays right), field new
  → the parameterless constructor's / initializer's value, retyped → `SaveLayout.CanConvert` (integer widening,
  float → double, enum ↔ its underlying integer) else refused `Type.Field: saved X, now Y`. Structs in arrays, lists and
  dictionary values are read element by element. Type renames: `SaveRenames.Types` (old stable name → new), also
  inside generic arguments. After loading, `ISaveMigrated.OnMigrated(info)` runs on every migrated object
  (`info.Defaulted` / `Dropped`), before `IAfterLoad`.
- **Gone types.** An object of a type that no longer exists may only land where a plan drops it (a dropped field, or a
  reference field read by a plan → null, reported); anywhere else (inside an unchanged container) the load is refused
  naming the type (`GraphReader.ReadRef` + `AcceptDropped`).
- **Per-world state** (`WorldState.Tables`): a saved key this build lacks is read with `Skipping` and dropped
  (reported); a new key starts fresh.
- **Blueprints.** Ids differ → every saved blueprint index is translated by id (`MigrationContext.SetBlueprintMap`).
  Chosen way: a central field table, not a typed wrapper: `BlueprintIndexFields.Indices` (34 fields incl.
  `FactoryState.Queue`) + `IndexTypes` (every int field of `AiRoster`); `RemapPlan` translates them. A blueprint the
  save uses that is gone → refused with its id; an unused gone id is fine. Any blueprint differs: `Derived` types
  (`CombatCatalog`) are rebuilt from the new db, `DerivedTables` (torpedo `DefenseTable`) dropped and rebuilt on use,
  `PerBlueprint` arrays (`AiView.Count/Complete/Queued`, `AiPlacement._failUntil`, `IntelService._canSubmerge`)
  reordered by id. Health above the new maximum (veterancy included) is clamped and reported. A weapon count change is
  safe (per-unit weapon state is `MaxWeapons` wide, loops bound by `bp.Weapons.Length`). `SaveCoverageTests`:
  `EveryBlueprintIndexFieldIsRemapped` (name heuristic `Bp`/`Blueprint` on int-like fields: list it in `Indices` or
  `NotIndices`) and `BlueprintDerivedStateNamesStillExist` (every name in the tables resolves).
- **Report.** `LoadedGame.Migration` (`SaveMigrationReport.Lines`): renames, dropped / defaulted fields, conversions,
  blueprints changed, rebuilt state, clamps. `Migrated` → the replay log restarts at the loaded tick (`Replay = null`),
  like an old-math save; replays stay strict (exact sim build and blueprints; `ReplayPlayer` refuses a migrated
  initial state, the replay menus keep the blueprint-hash check). Menus check a file header with
  `SaveGame.BlueprintMismatch` (replays only), the loader decides for saves.
- **Older saves** (state format 1, before 11a-2): field layouts migrate the same way, but they have no blueprint table:
  any blueprint change refuses them ("older save without a blueprint table").
- **Proof.** `SaveLayoutMigrationTests` (synthetic old/new types through a temporary rename), `SaveBlueprintMigrationTests`
  (reordered / removed / changed blueprints on the rich scenario), `SaveFixtureTests` (a real save of 2026-11-13,
  written before 11a-1, loads and plays 200 ticks after 11a-1 changed `Pathfinding` and 11 blueprints).
  Its load needed an `ISaveMigrated` hook: a new version field takes its initializer's value on an old save (`PathService.NavFormat`).
