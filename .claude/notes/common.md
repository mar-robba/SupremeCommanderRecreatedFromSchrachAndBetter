# Common rules for (7b) sub-agents (read this whole file first; it is short)
- Project: SupCom-1-like RTS, C#/.NET 10 + Raylib-cs 8.1. Worktree (work ONLY here, absolute paths):
  `/run/media/marco/RTS/.claude/worktrees/wave7b`, branch `wave7b-orders`. Do NOT merge main unless your prompt says so.
- Rules block: `/run/media/marco/RTS/docs/AGENT_RULES.md` applies (read it), except: no merge/final docs/report unless asked.
  CLAUDE.md hard rules: sim determinism; client never touches SimWorld (reads `SimSnapshot`, sends `Command`s);
  `Matrix4x4.Transpose` for raylib; saveable sim state; enums append-only; shared files (GameSession.cs, GameApp.cs,
  Hud.cs, Hotkeys.cs, CommandSystem.cs, SimSnapshot.cs, Program.cs...) minimal + additive; new code in new files.
  (7a) is merged into main and main is merged here (2026-11-10). The only parallel agent now is (8a) Missing units &
  walls (data/blueprints, faction json, Rts.Sim/Naval, Strategic, Transport, pathfinding blocked cells, new unit
  models, a new `--scene`): stay out of those.
- Cap: 150k (class low), CHECKPOINT at 130k: measure `python3 -I /run/media/marco/RTS/tools/agents/agent_ctx.py --self <your CTX-TAG>`
  after every milestone and ~every 10 tool calls. Grep + read ranges, never whole big files. Pipe outputs through tail/grep.
  At most 3 screenshots, `--size 960x540` (`dotnet run --project src/Rts.Client -- --demo --screenshot screenshots/<n>.png
  --frames 120 --size 960x540 [--camera x,z,dist]`), Read them.
- dotnet ONLY via `bash tools/agents/dn.sh <args>` from the worktree (lock). Test output is Italian: grep
  `-E "Superato|Non superat|error"`. Suites: tests/Rts.Sim.Tests, tests/Rts.Client.Tests, tests/Rts.Net.Tests.
- Commit only your files after every milestone: `git commit -m "(7b) ..." -- <files>`. Prefer python edits over `sed -i`
  (this volume warns "Operazione non permessa").
- Design + piece status: `.claude/handoff.md`. Sim contract: `.claude/notes/sim.md`. Orders UI: `.claude/notes/orders-ui.md`.
- Hotkeys: `src/Rts.Client/Input/Hotkeys.cs` is THE key table (new entries only; add to its `All` list; mouse help lines
  near the end); `HotkeyTableTests` fails on clashes/raw keys. List every new key in your note.
- Finish: write `.claude/notes/<piece>.md` (under 30 lines: commits, files, keys, CLI options, shared-file edits one line
  each, tests, screenshots, pitfalls, TODO). Commit. Final message under 150 words. At 130k: commit + note + CHECKPOINT.
