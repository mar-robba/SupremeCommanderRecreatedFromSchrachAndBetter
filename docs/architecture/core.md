# Sim core: world, determinism, performance

The skeleton of the deterministic simulation: the world and its stores, the worker pool, the deterministic math and
the snapshot the client reads. Threads and the tick pipeline are in the map ([ARCHITECTURE.md](../ARCHITECTURE.md)).

**Contracts of this area**
- The same commands give the same `StateHash` on every machine and with every thread count. Randomness only through
  `DetRandom`, every transcendental through `DMath`, no result that depends on `Dictionary` / `HashSet` order or on
  which thread summed first (CLAUDE.md hard rules 1 and 2).
- Parallel systems (`Par.For` over an `AliveList`) write only the unit they iterate. Effects on other units go
  through a `ChunkedBuffer<T>` merged in chunk order, or happen in a sequential step. Units are spawned and killed
  only in sequential code.
- A unit is referenced across ticks only by `UnitHandle` (index + generation).
- The client never touches `SimWorld` while the runner runs: it reads pooled `SimSnapshot`s and sends `Command`s.
- New per-unit state is a field in a module struct of `World/Components.cs`, set in the system's `InitUnit`, and
  saveable ([persistence.md](persistence.md) "Keeping new state saveable").

Files and top-level types of this area: [index/core.txt](index/core.txt) (generated).

## Data model (`Rts.Sim/World`)
- `UnitStore`: fixed-capacity SoA. The core arrays are `Pos`, `Heading`, `Health`, `Bp`, `Army`, `Flags` and
  `BuildFraction`. Module arrays of structs: `Move`, `Weapons` (unit × `MaxWeapons`), `Orders` (`OrderQueue`),
  `Builder`, `Factory` and `Econ`. `UnitHandle(Index, Generation)` detects recycled slots; use `IsActive(h)`.
- `ProjectileStore`: SoA, free list.
- `Terrain` (immutable): heightmap at the corners of 1×1 cells, water level, `PassFlags` per cell, deposits,
  start positions. `OccupancyGrid` (mutable) marks the cells covered by structures.
- `BlueprintDb`: JSON blueprints. Categories become `UnitCategory` flags; `Builds` becomes `BuildIndices`.
  A new unit can add itself to existing build lists with `BuiltBy: [builder ids]` (appended to those builders'
  `BuildIndices` in load order; unknown builder ids are ignored). `UpgradesTo: "<id>"` declares an in-place
  structure upgrade (resolved to `UpgradesToIndex`, inverse `UpgradesFromIndex`; strict).
- New per-unit state: add a field to the module struct in `World/Components.cs` and initialize it in your
  system's `InitUnit` (called from `UnitLifecycle.OnSpawn`).

## Performance & scale (wave 5m)

Goal: 1024 maps, 8 armies, 5k+ units at 60 FPS on the Radeon 780M, sim tick < 10 ms. Numbers: DEV_GUIDE "Reference
numbers"; how to measure: DEV_GUIDE "Perf check" and "Profiling".
- **Worker pool** (`Core/ParPool.cs`, `Par.cs`): persistent threads, the caller participates, no allocation per call.
  A call made while the pool is busy (nested, or from a second thread such as the warm-up or a loading thread) runs
  inline on the caller with the same chunks, so results never depend on it. Hot call sites pass static lambdas
  with the world as an argument (`Par.For(n, chunk, w, static (w, c, a, b) => ...)`): a capturing lambda allocates.
- **Profiling**: `Simulation.Profile` (`Core/SimProfile.cs`) times every tick stage (and, opt-in, its allocations);
  `Rts.Bench` prints stage avg/max, KB/tick, the first tick and the worst tick with their stage breakdown. Client:
  `FrameProfiler` (CPU laps per frame stage, `FrameProfiler.Sub("x")` splits a stage from inside a module; GPU timer
  queries per stage) printed by `--screenshot` runs and once per second by `--perf-log`.
- **JIT**: tier-0 code + on-stack replacement made the first ~30 s of a game spiky (10–30 ms ticks in loops that
  normally cost 0.05 ms). `Rts.Client` and `Rts.Bench` set `TieredCompilationQuickJitForLoops=false` (methods with
  loops are optimized at once), and the client starts `SimWarmup` at startup: a throwaway 2-AI skirmish with a small
  battle on a background thread, so the real game's first tick runs compiled code (first tick ~200 → ~15 ms on 1024).
  The warm-up world is separate; `ScalePerfTests` checks a game's hash is unchanged while it runs.
- **Navigation build**: `GameSetup.CreateSkirmish` builds the clearance grids (`PathService.EnsureGrids`, terrain pass
  parallel) at load instead of in the first tick; region graphs are still built on first use (they are saved state,
  so building all 16 up front would bloat saves) and several missing graphs are built in parallel
  (`PathService.EnsureGraphs`). Early vs lazy building gives identical results: grids and graphs are pure functions
  of terrain + occupancy, and later structures update them incrementally with the same functions.
- **Allocations**: ~0 per tick in combat; AI games ~10 KB/tick on 1024 (was ~125), mostly new goal fields. Goal
  fields reuse the Dist/Next arrays of evicted fields (`PathService.RentFieldArray`/`RecycleField`, safe because
  fields are read only during one tick's solves and evicted in sequential phases) and a per-thread Dijkstra heap.
- **Intel**: < 0.4 ms avg on 1024 with 8 armies (was ~1.1).
- **Client**: units are instanced (2 draw calls); particles are GPU-instanced (`ParticleInstances.cs`, one 52-byte
  instance per particle, the vertex shader builds billboard / ground quad / streak); terrain chunks (64×64) have
  LOD meshes at vertex strides 1/2/4/8 (`TerrainRenderer.Lod.cs`): a chunk uses the coarsest stride whose cell
  stays ≤ 5 px on screen, and every inner chunk edge has a skirt (depth = 2 × the largest stride error on that edge)
  so neighbours at different strides leave no cracks; `--terrain-lod N` forces a level. Deposit markers are
  frustum-culled and drawn as one flat square beyond 260 units; the minimap draws one dot per pixel and color.
- **Maps**: 6–8 players on 512/1024 for every preset are rotationally symmetric (non-quarter-turn counts carve one
  corridor profile and rasterize every rotated copy with it) and fair: `MapScaleTests` checks plateau heights,
  terrain symmetry, the nearest deposits of each start and the ground distances between starts.

## Deterministic math (cross-OS) (10a-1: `Rts.Sim/Core/DMath*.cs`)
- Why: `MathF.Sin/Atan2/Pow/...` call the OS C runtime (glibc, Windows UCRT, macOS libSystem), which may differ in the
  last bit, so Linux and Windows lockstep peers could desync. Every transcendental the sim uses is now computed in
  software from IEEE float `+ − × ÷` and bit casts only, bit-identical wherever .NET runs (x64 SSE2, ARM64 NEON; RyuJIT
  never contracts to FMA). `MathF.Sqrt` stays (IEEE correctly rounded everywhere).
- Functions: `Sin`, `Cos`, `SinCos` (`DMath.Trig.cs`: Cody-Waite 4-part π/2 reduction for |x| < 6000, Payne-Hanek with
  `UInt128` above, minimax polynomials); `Atan`, `Atan2`, `Asin`, `Acos` (`DMath.Inverse.cs`); `Exp`, `Log`, `Log2`,
  `Pow` (`DMath.ExpLog.cs`; pow keeps y·log2 x exact with Fast2Sum/TwoProduct). `Dir`/`HeadingOf` use SinCos/Atan2.
  `DMath.Lerp` (float, Vector2, Vector3) = `a + (b − a)·t` written out: `Vector2/3.Lerp` may use
  `MultiplyAddEstimate` (an FMA on some CPUs). NaN in → the constant `float.NaN` out (NaN payloads differ x86/ARM).
- Accuracy vs double `Math` (sweeps in `DeterministicMathTests`, plus a golden-bits table and IEEE edge cases ±0, ±∞,
  NaN, subnormals): max ulp Sin 2, Cos 2, Atan 2, Asin 2, Acos 1, Atan2 2, Exp 1, Log 1, Log2 2, Pow 1 (4 when
  |y·log2 x| > 16). Cost < 1 % of the tick (bench `2000 300` interleaved: 1 thread 4.54 → 4.57 ms, 15 threads
  1.76 → 1.78 ms).
- Guard: `DeterministicMathTests.SimSourceUsesNoMathFTranscendentals` greps `src/Rts.Sim` (DMath*.cs excepted) for
  `Math(F).Sin|Cos|Tan|SinCos|Atan|Atan2|Asin|Acos|Sinh|Cosh|Tanh|Exp|Log|Log2|Log10|Pow|Cbrt|FusedMultiplyAdd(`,
  `Vector2/3/4.Lerp(` and `MultiplyAddEstimate`. A function DMath lacks: add it there with golden-bit tests.
- **Cross-OS check**: `CrossOsDeterminismTests.GoldenGameHashIsTheSameOnEveryMachine` (AI Hard UEF vs AI Normal
  Cybran, generated 256 map seed 20261112, a 60-unit brawl in the middle, 600 ticks, 2 threads): the terrain hash and
  `StateHash` literals were written on Linux x64. Red on another OS/CPU = a cross-OS desync; when the sim changes on
  purpose, update the literals (the message prints them) and say why in the commit.
- Versioning: `DMath.Version` (now 1; bump when any DMath result changes) is written in every save/replay header as
  `GameInfo.MathVersion` (no field = 0 = the old MathF sim). Replays of another version are refused on open
  (`SaveGame.CheckReplayMath`, list tag "OLD VERSION", `Rts.Bench -- replay` exit 2). Old saves on generated maps:
  the generator uses DMath (MapSymmetry, noise), the regenerated terrain differs, the TerrainHash check refuses them
  with a clear message; old saves on map files or embedded terrain load and play on, but their replay log is dropped
  (recording restarts at the loaded tick). `Msg.ProtocolVersion` 4 keeps old builds out of multiplayer.
- Audit (2026-11-12) beyond trig, all OK: no `HashCode`/`GetHashCode` in Sim/Net; sorts use ordinal or numeric keys;
  `Codecs` sorts `ConcurrentDictionary` keys with `Comparer<K>.Default` (culture for strings) but no such dictionary
  is sim state; AI `$"..."` floats only in trace strings; JSON parsing is invariant; float→int casts saturate alike
  on .NET 9+; Rts.Net's port `int.TryParse` is UI only. Fixed: `MultiplayerSystem` `EndsWith` → Ordinal, `FactionDb`
  hex `TryParse` → InvariantCulture, `MapSymmetry` double `Math.Cos/Sin` → DMath.
- Reference numbers changed with the math (2026-11-12): bench `2000 300` hash 6A8312040EFA67F2 → 2F09CD7D01AF287E;
  `ai 30 256 42 hard,normal` "Player (Hard) after 12:18" (FB269C29202773C1) → "after 12:00" (C87C46C4EA726A82).
