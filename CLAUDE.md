# RTS — a Supreme Commander 1–like game (C# / .NET 10 / raylib)

The owner does not write code: AI agents build everything. Talk to the owner in **Italian**.
Read `docs/ARCHITECTURE.md` before changing the sim, and `docs/ROADMAP.md` to see what is done / in progress.
Docs index: `docs/README.md` (orchestrators: `docs/WORKFLOW.md`). All durable knowledge (design, decisions, lessons,
numbers, plans) goes in `docs/`, organized as that index says, never only in private agent memory or scratch files.

## Commands (run from repo root)
- Build all: `dotnet build Rts.slnx`
- Tests: `dotnet test tests/Rts.Sim.Tests` and `dotnet test tests/Rts.Client.Tests` (headless, must stay green)
- Benchmark (headless sim, Release): `dotnet run -c Release --project tools/Rts.Bench -- <unitsPerSide> <ticks> [threads]`
- AI vs AI match (headless): `dotnet run -c Release --project tools/Rts.Bench -- ai [maxMinutes] [mapSize] [seed] [normal,hard] [threads] [players] [preset]`
- Play: `dotnet run --project src/Rts.Client` (main menu), or straight into a game:
  `dotnet run --project src/Rts.Client -- [--demo] [--map 256|512|1024] [--seed N] [--players N] [--preset plains|lakes|highlands|canyon|archipelago] [--water 0..1]`
  (`--menu` / `--setup` / `--settings` open a menu screen, e.g. for screenshots; `--pause-menu` / `--endscreen` show overlays;
  `--autostart` with `--setup` starts the game when the preview is ready; `--nofog`, `--nosound` see `Program.cs`;
  visual test scenes: `--tech-demo`, `--scene strategic[:shields|:tml|:nuke]`, `--scene experimental[:mavor|:fog]` (Fatboy, Mavor, gateway+SCUs, enhanced commanders), `--scene transport` (C-6/C-14 loading, ferry, air staging, Atlantis) (scene positions are printed; aim with `--camera`))
- Sandbox (spawn anything, cheats, nukes; panel F9, console F10): `dotnet run --project src/Rts.Client -- --sandbox [--exec "spawn tank 100 enemy at 140,128; wait 30; nuke 140,128"]`
  (`--exec` runs console commands at start and implies `--sandbox`; combine with the screenshot options below;
  `nuke x,z` fires a real strategic missile (anti-nukes can intercept it), `nuke instant x,z` = the blast at once;
  multi-word unit names use `_`, e.g. `spawn strategic_missile_launcher me at 100,128`)
- Saves & replays (folders `saves/`, `replays/`; every game is recorded; F5 quicksave, F6 quickload, Esc → Save Game):
  `dotnet run --project src/Rts.Client -- --saves | --replays | --load FILE | --replay FILE [--seek TICK]`
  (`--save-at N` quicksaves at frame N, `--save-dialog` opens the save dialog; `--screenshot` runs write no replay unless `--write-replay`).
  Headless replay check (exit 1 = desync): `dotnet run -c Release --project tools/Rts.Bench -- replay <file>`;
  save/load timings: `... -- savebench [minutes] [mapSize] [players] [preset] [seed] [extraUnitsPerArmy] [threads]`
- **Visual check (agents must use this after client changes):**
  `dotnet run --project src/Rts.Client -- --demo --screenshot screenshots/<name>.png --frames 120 --size 1280x720 [--camera x,z,dist]`
  then Read the PNG to look at it. A display is available (X11/XWayland).

## Projects
- `src/Rts.Sim` — deterministic simulation, NO rendering deps. Systems in `Systems/`, data model in `World/`, `Data/`, `Map/`.
- `src/Rts.Client` — raylib renderer/UI/input (Raylib-cs 8.1 = raylib 6.0, OpenGL 3.3+ core, GLSL 330).
- `tests/Rts.Sim.Tests` — xUnit (test parallelization disabled because `Par.MaxThreads` is global).
- `tools/Rts.Bench` — perf benchmark.
- `data/blueprints/units/*.json` — unit definitions (like SupCom .bp). Add units here, not in code.

## Hard rules
1. **Determinism**: sim code never uses `System.Random`, `DateTime`, `Dictionary` iteration order, `HashSet` order,
   or float reductions whose order depends on threads. Use `DetRandom` (`world.RandomFor(unit, salt)` in parallel code).
   The test `SimulationIsDeterministicAcrossThreadCounts` must pass.
2. **Threading**: the sim runs on its own thread (`SimRunner`). Parallel systems use `Par.For` over `Units.AliveList`
   and may only write the data of the unit they iterate; cross-unit effects go to `ChunkedBuffer<T>` and are merged in
   chunk order, or happen in a sequential phase. Spawning/killing units only in sequential code.
3. **Client never touches `SimWorld`** while the runner is running: read `SimSnapshot`s, write `Command`s.
   (Terrain and BlueprintDb are immutable and may be read.)
4. Coordinates: sim uses `Vector2(x, z)` on the ground plane; 3D is `(x, y-up, z)`. Heading `h` points along
   `(cos h, sin h)`. 1 world unit ≈ 1 SupCom ogrid. Sim tick = 0.1 s (10 Hz). Rates are per second.
5. raylib matrices: build with System.Numerics then **`Matrix4x4.Transpose`** before passing to `DrawMesh`.
6. Keep each module in its own files; when several agents work in parallel only edit files of your area,
   and keep edits to shared files (`GameApp.cs`, `SimWorld.cs`, `UnitStore.cs`, `Components.cs`,
   `SimSnapshot.cs`, `Simulation.cs`) minimal and additive.
7. Every feature: build, run tests, add tests for sim logic, screenshot for visuals. Update `docs/ROADMAP.md`.
8. Don't install system packages/toolchains (the owner refused a rustup install). NuGet packages are fine.
9. **Saveable state** (save/load + replays, see ARCHITECTURE "Save/load & replays"): sim state = plain fields, arrays,
   List/Dictionary/HashSet, per-world `ConditionalWeakTable<SimWorld,T>`; no static mutable state, no stored lambdas/delegates.
   Caches/timings get `[NoSave]` (rebuild lazily or in `IAfterLoad`). `SaveCoverageTests` + `PersistenceTests` must pass.
