# Tools: Rts.Bench

`tools/Rts.Bench` is the headless console tool: benchmark, AI matches, unit duels, multiplayer self-test, replay
check, save timings, map export. It references Rts.Sim and Rts.Net, never the client. Commands and their options are
in CLAUDE.md "Commands"; how to read the numbers is in [DEV_GUIDE.md](../DEV_GUIDE.md) and
[AI_TUNING.md](../AI_TUNING.md).

**Contracts of this area**
- The sim never reads the environment: bench-only switches (`AI_CFG`, `AI_TRACE`, `BENCH_WARMUP`) are read here.
- A mode that checks something exits with a non-zero code on failure (desync, a refused map).

## Files

| File | What it runs |
|---|---|
| `Program.cs` | The entry point and the default benchmark: N units per side attack-move across the map. |
| `StageStats.cs` | Per-stage tick cost and allocations for the bench output. |
| `ParBench.cs` | `parbench`: the overhead of one `Par.For` call. |
| `AiMatch.cs` | `ai` / `aipassive`: a headless AI match. |
| `AiRoster.cs`, `KillTracker.cs`, `SpendTracker.cs` | AI match diagnostics: units fielded, kills per blueprint, mass spent by purpose. |
| `Duel.cs` | `duel` / `duelmatrix`: unit duels for balance. |
| `MpSelfTest.cs`, `MpSelfTest.Save.cs` | `mp`: the multiplayer self-test and its `save=TICK` step. |
| `ReplayBench.cs` | `replay` and `savebench`. |
| `MapExport.cs` | `mapexport`: writes and checks map files. |

Files and top-level types of this area: [index/tools.txt](index/tools.txt) (generated).
