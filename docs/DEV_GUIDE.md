# Developer guide: practical know-how

Things learned while building the project that are not about how a system works (that is
[ARCHITECTURE.md](ARCHITECTURE.md)) but about how to work on it without losing hours. The basic commands are in
[CLAUDE.md](../CLAUDE.md).

## Building in a shared worktree

- **Only one `dotnet build`/`dotnet test` at a time per worktree.** Two builds in the same folder corrupt each
  other's `obj/` output. When several agents share a worktree, everyone runs dotnet through the lock wrapper:
  `bash tools/agents/dn.sh build Rts.slnx`, `bash tools/agents/dn.sh test tests/Rts.Sim.Tests --filter "..."` (always through `bash`: in worktrees the file may not be executable).
  Run it from inside the worktree; the lock is `<worktree>/.claude/build.lock`.
- **Why the wrapper uses `flock -o` and turns MSBuild node reuse off:** with a plain `flock`, the reused MSBuild
  nodes and the compiler server inherited the lock's file descriptor and kept the lock held forever after the build
  ended. In wave 4, four orphaned nodes blocked every build. The permission checker also tends to block a raw
  `flock ... dotnet` line; the script is accepted.
- If a build hangs on the compiler server: `dotnet build-server shutdown --vbcscompiler`.
- A second builder in the same worktree that must not wait for the lock can use its own output folder:
  `dotnet build ... --artifacts-path <worktree>/.claude/art`.
- Without the wrapper, add `-nr:false` (`-nodeReuse:false`) to builds in a worktree that others use.

## Tests

- `dotnet test tests/Rts.Sim.Tests` and `dotnet test tests/Rts.Client.Tests` must stay green. The tests that
  matter most after any sim change:
  - `SimulationIsDeterministicAcrossThreadCounts` (determinism);
  - `SaveCoverageTests` (every sim field can be saved; a failure names the field and the fix);
  - `PersistenceTests` (save → load → continue gives identical bytes and hashes).
- **The test output on this machine is in Italian**: "Superati" = passed, "Non superati" = failed,
  "Messaggio di errore" = error message, "Totale test" = total. Grep for those words, not the English ones.
- Run one class: `--filter "FullyQualifiedName~TransportTests"`. Several: `"FullyQualifiedName~A|FullyQualifiedName~B"`.
- Test parallelization is off in the sim tests, because `Par.MaxThreads` is global.

## Shell quirks (zsh on this machine)

- **The repo path has a space** (`/run/media/marco/Volume D/RTS`): always quote paths. With `xargs` use `-d '\n'`.
- **zsh does not word-split unquoted variables**: with `ids="a b"`, `cmd $ids` passes ONE argument. Use an array or
  `${=ids}`.
- `tr` is aliased to `tree` in this shell. Use `sed`/`paste` instead.
- Long one-liners that mix `&&`, `cd` and inline Python with f-strings are often blocked by the permission
  checker. Put the script in a file (scratchpad) and pass the repo path as an argument. Use `python3 -I`.
- `jq -c . data/blueprints/units/<id>.json` prints a blueprint compactly.
- Scratchpads (`/tmp/claude-1000/...`) are wiped on reboot. A script worth keeping goes in `tools/`.

## Sim pitfalls (each one cost real debugging time)

- **Blueprint indices are not stable.** Blueprint files load in filename order, so adding a file shifts every later
  index (`uef_t1_transport` shifted all T2/T3 units by one). Never hard-code a blueprint index; look blueprints up by
  id, or by faction role (ARCHITECTURE "Factions").
- **New units join `Units.AliveList` on the next tick.** Systems that iterate the AliveList don't see a unit spawned
  this tick. Tests: call `sim.Step()` once after spawning before you expect other systems to react.
- **`UnitLifecycle.OnSpawn` runs again when a unit's blueprint is swapped** (upgrades, commander enhancements).
  Every module's `InitUnit` must be idempotent: check whether the unit is already registered, and keep its state
  (e.g. the silo stockpile).
- **Enhancement variants are blueprints too.** Anything that iterates `db.All` (sandbox lists, demo spawners, tests)
  sees `uef_commander+HeavyAntimatterCannon+...`. Filter with `bp.IsVariant`.
- **Don't add to `w.Events.AreaDamage` inside `OnDeath`**: DamageSystem is iterating that list. Queue the blast
  and emit it in a later tick (that's how experimental death blasts work).
- **Keep mobile units' `Radius` ≤ 3.0.** `PathService.MaxMobileRadius` (the largest radius of any mobile blueprint)
  sets the separation query size for EVERY unit. One big unit slows down the whole sim. Give a big unit a separate
  contact radius (e.g. `CrushRadius`) instead.
- **ConstructionSystem resets builders' resource requests every tick.** Another system that adds requests for a
  builder (e.g. a silo on a commander) must use `+=` after it, never `=`.
- **Blueprint sanity tests** (`UpgradeTests.EveryBlueprintLoadsWithTechCategoriesAndTargetLayers` and friends):
  - every weapon needs `TargetLayers`, even manual ones (TMD/anti-nuke use `["Air"]`; they never auto-fire anyway);
  - Tech 2/3 blueprints need the `Tech2`/`Tech3` category;
  - category `Experimental` ⇔ `Tech: 4`.
- **Weapon arcs**: the `IndirectFire`/`Artillery` categories give a weapon a high arc, with 25–40 s flights at long
  range. A long-range direct gun (the Mavor) must not have them.
- **Carried units are alive but not on the map**: `IsActive` is true, but they are not in the AliveList. Orders or
  weapon locks that point at them must be dropped (see ARCHITECTURE "Transports & cargo"). A unit that boards after
  the AliveList rebuild (e.g. an aircraft finished inside a dived Atlantis, in FactorySystem) stays in this tick's
  AliveList and grid: TargetingSystem skips `UnitFlags.Carried` shooters and targets for that reason.
- **Fresh spawn vs blueprint swap in `InitUnit`**: `UnitStore.Allocate` zeroes the module structs, so a field that
  `InitUnit` always sets to non-zero (`MoveState.OrbitDir`) tells a new unit from a re-init (MovementSystem uses it
  to keep a dived sub down).
- **Saveable state** (CLAUDE.md rule 9): static scratch lists and cached delegates get `[NoSave]` and are rebuilt
  lazily (`??=`). An unsupported framework type in sim state makes the serializer throw with a hint;
  `SaveCoverageTests` catches it first.
- **The sim never reads environment variables.** `AI_CFG` and the other `AI_*` switches are read by `Rts.Bench`
  only.

## Client pitfalls

- Unit models are built lazily per blueprint index (`UnitRenderer.ModelFor` → `UnitModels.Build`), so every
  enhancement variant gets its own mesh with its attachments.
- Sounds longer than 4 s fail `SynthTests` (the nuke explosion is 3.9 s played at a lower pitch to sound longer).
- The perf overlay is on in screenshots and covers the right end of the sandbox console header.

## Visual checks

- Command: CLAUDE.md "Visual check". Use `--size 960x540` to save context. Every screenshot you Read costs context,
  so take a few good ones (an agent's whole run: at most ~6).
- Test scenes put things where the camera can see them: `--scene strategic[:shields|:tml|:nuke]`, `--scene naval`,
  `--scene experimental[:mavor|:fog]`, `--scene transport`, `--tech-demo`. They print positions; aim with
  `--camera x,z,dist`.
- `--select <blueprint id>` (or `any:<id>` in the sandbox) pre-selects units; `--ui-place id@x,z` shows a placement
  ghost; the sandbox `--exec "spawn ...; wait 30; ..."` builds any situation.

## Perf check (before finishing a branch)

The machine is noisy when other agents run benchmarks at the same time, so compare **interleaved runs** of main and
your branch, not one run against an old number:
1. Build a main copy outside your worktree, e.g. `git worktree add --detach <scratch>/mainbench main`, then
   `dotnet build -c Release tools/Rts.Bench` in both trees.
2. Alternate 3–4 times, from each tree's root: `dotnet tools/Rts.Bench/bin/Release/net10.0/Rts.Bench.dll 2000 300`
   and compare the `tick ms` lines.
3. The budget is a regression of at most 10%. Remove the extra worktree afterwards (`git worktree remove`).

## Profiling

- Sim: `Rts.Bench` prints per-stage avg/max ms, KB allocated per tick, the first tick and the worst tick (overall
  and after tick 20) with their stage breakdown. `BENCH_ALLOC=1` adds allocations per stage (slower, use it to find
  allocations, not for timings); `BENCH_WARMUP=1` runs `SimWarmup` first (as the client does); 5th argument =
  armies (`... 700 400 0 1024 8`: 8 blobs on a 1024 map). `ai` matches print the same stage lines.
- Client: `--screenshot` runs print `perf:` (frame/CPU ms), `stages ms:` (CPU per frame stage, avg/max) and `gpu ms:`
  (GPU timer queries); `--perf-log` prints the same once per second while playing. Frames are vsync-capped at 60:
  compare CPU and GPU ms, not FPS (or add `--novsync`).
- Big client scene (5k units, 8 players, 1024): `--map 1024 --players 8 --nosound --nofog --exec "spawn tank 1200 me
  at 460,470; spawn tank 1200 enemy at 540,550; spawn tank 1200 enemy at 460,550; spawn tank 1200 me at 540,470"
  --frames 900 --size 960x540 --camera 500,510,110` (close) or `--camera 500,510,700` (whole battle, strategic zoom).
- Tick spikes that hit stages which are normally ~0 (Construction, Economy, Factory at 1–7 ms in the same tick) are
  JIT tiering, not GC: try `DOTNET_TieredCompilation=0` to confirm. Hence `TieredCompilationQuickJitForLoops=false`
  in the client and bench csproj (ARCHITECTURE "Performance & scale").
- `dotnet build` may die with `Internal CLR error (0x80131506)` once in a while: just run it again.

## Reference numbers

Measured at merge time (details in [HISTORY.md](HISTORY.md)). Machine: AMD Ryzen 7 8840U (16 threads), Radeon 780M
integrated GPU.

| What | Number | When |
|---|---|---|
| Sim, 6k units | ≈ 1.7 ms/tick (movement only) → ≈ 3 ms/tick with combat | wave 0–1 |
| Pathfinding, 1024 map, 6k units | ≈ 2.5 ms/tick; first navigation build ≈ 180 ms | wave 1 |
| Client, 6k units | 170–360 FPS uncapped | wave 1 |
| `Rts.Bench 3000 300` | avg ≈ 3.7–4.5 ms/tick | wave 3 |
| `Rts.Bench 2000 300` | avg ≈ 4.1–4.4 ms/tick (4.36 after the 4f merge) | wave 4 |
| Skirmish AI | 0.02–0.03 ms/tick per AI (worst ≈ 0.06); one-off 50–110 ms on the first think (JIT) | wave 4 |
| Save, 512 map, 4 AIs, 25 min, 6641 units | file 1.8 MB (raw 29.5 MB); capture 86 ms on the sim thread; encode 99 ms in the background; load 0.3 s | wave 4 |
| Replay of that game | 165 KB; headless verify 1493 ticks/s; recording 0.009 ms/tick | wave 4 |
| 96 AI games of 30 min (256 maps) | ≈ 60 s wall with `tools/ai-lab` | wave 4 |
| `Rts.Bench 2000 300` | avg 1.7–2.0 ms/tick, p95 2.4–2.9, max ≈ 10 (main before 5m, same runs: 3.5–4.5 / 5.8–9.2 / 25) | wave 5m |
| `Rts.Bench 700 400 0 1024 8` (8 armies, 4.6k units) | avg 2.4, p95 4.5–4.9, intel 0.33 (before: 3.9–4.6 / 10.6–12.3 / 1.1–1.35); with warm-up avg 2.0, p95 3.8, max 8.5, first tick 15 ms (before ≈ 210) | wave 5m |
| `ai 40 1024 42 hard 0 8 plains` (8 AIs, 40 min) | avg 0.96, p95 2.21, max 12.1 (one AI think), 10 KB/tick (before: 1.64 / 3.68 / 217 / ≈ 125 KB) | wave 5m |
| Client, 4.8k units in battle, 1024, 960×540 | close: CPU 1.6 ms, GPU 4.2 ms (before CPU 3.2); whole map: CPU 2.2 ms, GPU 2.8 ms (before 2.8 / 8.2: terrain 7.4 → 2.0 with chunk LOD) | wave 5m |

## Tools

| Tool | What for |
|---|---|
| `tools/Rts.Bench` | Headless benchmark, AI matches, replay verification, save/load timings (commands in CLAUDE.md). AI debug switches: ARCHITECTURE "Skirmish AI". |
| `tools/agents/dn.sh` | dotnet with the per-worktree build lock (above). |
| `tools/agents/agent_ctx.py` | Exact context size of sub-agents, and a background alert (WORKFLOW "Context limits and handoffs"). |
| `tools/ai-lab/` | Hundreds of headless AI games in parallel, config sweeps, head-to-head stats ([AI_TUNING.md](AI_TUNING.md)). |
| `tools/token-ledger/` | Token ledger: tokens per session/agent and purpose, kept up to date by project hooks ([tokens/README.org](tokens/README.org)); `windows.py`: tokens used in each usage-limit window ([WORKFLOW.md](WORKFLOW.md) "Usage limits"). |
