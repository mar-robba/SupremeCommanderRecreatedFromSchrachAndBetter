# Rules block for agent prompts

The orchestrator pastes the block below into every agent prompt, after the agent's own brief (goal, must-haves,
files it owns). Fill in the `<...>` parts. Why these rules exist: [WORKFLOW.md](WORKFLOW.md).
Start every agent with `model: "opus"` in the Agent call, and run at most 2 agents at a time, sub-agents counted
(WORKFLOW "Owner decisions", 2026-11-10): so `<S>` below is 1 for an agent that runs alone and 0 when two module
agents run.

---

## Rules for this job (read carefully)
- Project: a Supreme Commander 1-like RTS in C#/.NET 10 + Raylib-cs. Main checkout: `/run/media/marco/RTS` (until 2026-10-22 `/run/media/marco/Volume D/RTS`).
  CLAUDE.md is already in your context. Read ONLY what your job needs, by section (`grep -n '^## ' <file>`, then
  read that range): reading all of docs/ costs ~50k tokens, a third of your cap. A resumed agent reads its
  `.claude/handoff.md` and notes first, then only the sections they point to. A new agent reads its brief, the
  docs/ARCHITECTURE.md sections of its area plus "Factions" and "Save/load & replays" (every new sim field must be
  saveable), and docs/DEV_GUIDE.md "Building in a shared worktree" and "Sim pitfalls" before its first build. Never read docs/HISTORY.md, docs/ROADMAP.md
  or docs/ARCHITECTURE.md whole.
- You are one of `<N>` agents working in parallel (wave `<W>`), each in its own git worktree and branch:
  `<one line per agent: (letter) area (main folders)>`.
  Stay in your area. Keep edits to shared files minimal and additive. Shared files: GameApp.cs, GameSession.cs,
  SimWorld.cs, UnitStore.cs, Components.cs, SimSnapshot.cs, Simulation.cs, OrderSystem.cs, CommandSystem.cs,
  UnitModels.cs, Hud.cs and the HUD help lines, Program.cs (CLI options: add lines, never reorder),
  docs/ARCHITECTURE.md (add your own section), docs/ROADMAP.md (your lines only).
  - Enums are append-only.
  - Put new code in new files (partial classes are fine).
  - New models go in new partial files, with only a `case` line in UnitModels.cs.
  - Contracts that still hold: `BuiltBy` and `UpgradesTo` (ARCHITECTURE "Data model"); factions/roles
    (ARCHITECTURE "Factions": sim/AI code never hard-codes `uef_*` ids, it asks the faction's roles).
  - New hotkeys: check the HUD help for free keys and list every new key in your report (keys clash at merge).
- `CTX-TAG: <unique tag>` (see the context cap bullet).
- Context cap: HARD limit of `<CAP>`k tokens of context (default: a module agent 200k, a sub-agent 150k; the
  orchestrator sizes it per task, WORKFLOW "Sizing an agent's context cap": `Fragmentation sensitivity: <class>,
  because <reason>`). Stay lean:
  - don't read huge files whole (grep, then read ranges);
  - read few screenshots (at most ~6 in your whole run; prefer `--size 960x540`);
  - pipe long outputs through tail/grep.
  At `<CAP-20>`k: commit, update your handoff note, and end your turn with the word CHECKPOINT. A fresh agent will
  continue from your note.
  - Measure yourself: this prompt has a line `CTX-TAG: <tag>`. Run
    `python3 -I /run/media/marco/RTS/tools/agents/agent_ctx.py --self <tag>` after every milestone and about every
    10 tool calls (more often after screenshots or big reads). You can't feel your context: every sub-agent of
    wave 6 ran past its CHECKPOINT until the orchestrator stopped it.
  - Give each sub-agent you start its own unique `CTX-TAG: <tag>` line in its prompt (e.g. `6b-client-2`).
- Handoff note: keep `<your worktree>/.claude/handoff.md` up to date at every milestone: done / TODO in order /
  known bugs / key commands / bench numbers. Commit WIP at every milestone. Usage limits can stop you at any moment.
  Keep it SHORT (under ~60 lines): only what the next agent needs to continue (state + commits, TODO in order,
  decisions and pitfalls), not a diary.
  The orchestrator then commits your leftovers as "WIP" and starts a fresh agent from your note.
- Sub-agents: do your module yourself; there is no coordinator role. You may start a sub-agent (general-purpose)
  only for separable content with a written contract (blueprints, unit models, tests), never to split a feature
  into sim and client, and at most `<S>` at a time (the project runs at most 2 agents at once, sub-agents counted;
  each sub-agent pays its own start-up reading). Rules:
  - pass `model: "opus"` in every Agent call (a sub-agent otherwise inherits the session's model, and on Fable 5.2
    a usage window holds a fifth of the work);
  - they work in YOUR worktree (give them the absolute path; no isolation flag) on disjoint files;
  - only ONE `dotnet build`/`dotnet test` may run at a time per worktree: everyone runs dotnet through
    `bash tools/agents/dn.sh <dotnet args>` (call it through `bash`: the volume is mounted without exec bits and git has `core.fileMode=false`);
  - they commit only their own files (`git commit -m "..." -- <files>`);
  - size each one's cap with WORKFLOW "Sizing an agent's context cap" (default 150k; more only when splitting the
    piece would hurt its result) and write the class, reason and cap in its prompt; ask for short reports;
  - when a sub-agent finishes or stops (CHECKPOINT), it first writes the essentials of its piece (done + commits,
    what is left, decisions/pitfalls; under ~30 lines) to `<your worktree>/.claude/notes/<piece>.md`, and you add a
    one-line pointer to it in your handoff note, so the work can continue after a restart;
  - start their description with your purpose tag in brackets, e.g. `[Fazione Cybran (5l)] client models`: the
    token ledger files their cost under that purpose (docs/tokens/README.org).
- The hard rules in CLAUDE.md apply:
  - determinism and threading;
  - the client reads only snapshots;
  - Transpose raylib matrices;
  - saveable sim state;
  - no system package installs (NuGet is fine).
- FIRST STEP: your worktree may have been created from `origin/main`, which is stale. Run `git merge main` (the
  LOCAL main branch) before anything else, and again before finishing. main moves while you work; resolve
  conflicts keeping both sides.
- Documentation: lasting knowledge goes in `docs/`, not only in your handoff note:
  - how your system works → your ARCHITECTURE section;
  - pitfalls, tools and tricks you discovered → DEV_GUIDE.md;
  - AI/balance experiments and their results → AI_TUNING.md.
  See the table in docs/README.md.
- Before finishing:
  1. `git merge main`.
  2. `dotnet build Rts.slnx`; both test projects green, including SimulationIsDeterministicAcrossThreadCounts,
     SaveCoverageTests and PersistenceTests.
  3. Perf check with Rts.Bench (`2000 300` vs a main build; no regression > 10%; see DEV_GUIDE "Perf check").
  4. Screenshots of the visible features.
  5. Docs: your ARCHITECTURE section, CLAUDE.md command lines for new CLI options, and ROADMAP lines ([x] with the
     date, plus "- [ ] later:" items for what you leave out). Don't rewrite the ROADMAP "NEXT STEPS" section: the
     orchestrator owns it.
  6. Commit. The tree must be clean.
- Final report: write `<your worktree>/.claude/agent-report.md`, under 350 words:
  - what works;
  - how to try it (CLI / sandbox commands);
  - tests added;
  - new hotkeys;
  - shared-file edits;
  - measured numbers (bench, sizes, win rates…);
  - known issues and follow-ups;
  - **"Design errors found"** (always present; write "none" if there were none): every bug or limit whose cause is
    a design or architecture choice, in your code or in older code: a regression found on the way, an exploit, a
    wrong assumption, a risk you left open. One or two lines each: the error, why it happened, the fix (or "open"),
    the commit. The orchestrator turns them into LESSONS.org entries at merge (WORKFLOW "Lifecycle of a wave" 7);
  - if you continued from a handoff note: "Handoff review", 2-4 lines: what the note lacked, what you had to
    rediscover, what you redid or re-tried that was already done or ruled out, what was broken at the seam.
  Return the same short text as your final message. The orchestrator copies it into docs/HISTORY.md at merge.
