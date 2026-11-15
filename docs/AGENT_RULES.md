# Rules block for agent prompts

The orchestrator pastes the block below into every agent prompt, after the agent's own brief (goal, must-haves,
files it owns). Fill in the `<...>` parts. Why these rules exist: [WORKFLOW.md](WORKFLOW.md).
Start every agent with `model: "opus"` in the Agent call (`model: "sonnet"` for a trivial task: owner, 2026-11-12,
WORKFLOW "Lifecycle of a wave" step 3), and run at most 2 agents at a time, sub-agents counted
(WORKFLOW "Owner decisions", 2026-11-10): so `<S>` below is 1 for an agent that runs alone and 0 when two module
agents run.

---

## Rules for this job (read carefully)
- Project: a Supreme Commander 1-like RTS in C#/.NET 10 + Raylib-cs. Main checkout: `/run/media/marco/RTS` (until 2026-10-22 `/run/media/marco/Volume D/RTS`).
  CLAUDE.md is already in your context. Read ONLY what your job needs, by section (`grep -n '^## ' <file>`, then
  read that range): reading all of docs/ costs ~50k tokens, a third of your cap. A resumed agent reads its
  `.claude/handoff.md` and notes first, then only the sections they point to. A new agent reads its brief,
  docs/ARCHITECTURE.md (the map: one page, read it whole), the files in docs/architecture/ of the areas it changes
  (`python3 -I tools/arch/arch.py where <file>` names them) plus architecture/factions.md and the contracts at the top
  of architecture/persistence.md (every new sim field must be saveable), and docs/DEV_GUIDE.md "Building in a shared
  worktree" and "Sim pitfalls" before its first build. Never read docs/HISTORY.md or docs/ROADMAP.md whole.
- You are one of `<N>` agents working in parallel (wave `<W>`), each in its own git worktree and branch:
  `<one line per agent: (letter) area (main folders)>`.
  Stay in your area. Keep edits to shared files minimal and additive. Shared files: GameApp.cs, GameSession.cs,
  SimWorld.cs, UnitStore.cs, Components.cs, SimSnapshot.cs, Simulation.cs, OrderSystem.cs, CommandSystem.cs,
  UnitModels.cs, Hud.cs and the HUD help lines, Program.cs (CLI options: add lines, never reorder),
  docs/ARCHITECTURE.md (the map: change it only for a new area, pipeline step or cross-area contract),
  docs/architecture/*.md (the sections of your systems), docs/ROADMAP.md (your lines only).
  - Enums are append-only.
  - Put new code in new files (partial classes are fine).
  - New models go in new partial files, with only a `case` line in UnitModels.cs.
  - Contracts that still hold: the "Contracts between areas" of docs/ARCHITECTURE.md and the "Contracts of this
    area" list at the top of each area file, e.g. `BuiltBy` and `UpgradesTo` (architecture/core.md "Data model"),
    factions/roles (architecture/factions.md: sim/AI code never hard-codes `uef_*` ids, it asks the faction's roles).
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
    a usage window holds a fifth of the work); for a trivial piece (the prompt says exactly what to do, no design
    choice is left, a command tells whether it is right: data files from a table, running checks) pass
    `model: "sonnet"` instead, with `effort: "medium"` or `"high"` by the piece's complexity (owner, 2026-11-12:
    no Opus 5.5 at xhigh for very simple tasks);
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
  conflicts keeping both sides (a conflict in a generated `docs/architecture/index/*.txt`: run
  `python3 -I tools/arch/arch.py sync` instead).
- Architecture docs (owner, 2026-11-15: no code change that needs a change of the architecture docs may leave them
  unchanged; ARCHITECTURE "Keeping these docs true", DEV_GUIDE "Architecture docs gate"):
  - a change to what the docs describe (a system, pipeline step, command, order type, snapshot field, file format,
    contract between areas, where state lives or how it is saved, a rule other code relies on, a number they state)
    updates the area file in the same commit. Describe the system as it is now, in its own section; never append a
    section per wave or per agent;
  - a code commit that changes nothing the docs describe (bug fix restoring the documented behavior, refactor inside
    a file, perf with the same results, tests, comments) carries a line `Arch: <area> - <why the docs still hold>`;
    the git hook refuses the commit otherwise. A commit whose message starts with `WIP` passes and is checked at the
    merge;
  - never `--no-verify`, `git commit -n` or a change of `core.hooksPath` (a hook refuses them);
  - a new, renamed or removed source file, type or enum value: describe it in its area file, run
    `python3 -I tools/arch/arch.py sync` and commit the indexes. `ArchitectureDocsTests` (in every test suite) stays
    red until the docs match the code.
- Other documentation: lasting knowledge goes in `docs/`, not only in your handoff note:
  - pitfalls, tools and tricks you discovered → DEV_GUIDE.md;
  - AI/balance experiments and their results → AI_TUNING.md.
  See the table in docs/README.md.
- Before finishing:
  1. `git merge main`.
  2. `dotnet build Rts.slnx`; the three test projects green, including SimulationIsDeterministicAcrossThreadCounts,
     SaveCoverageTests, PersistenceTests and ArchitectureDocsTests; `python3 -I tools/arch/arch.py review main`
     passes (no area shows NOTHING).
  3. Perf check with Rts.Bench (`2000 300` vs a main build; no regression > 10%; see DEV_GUIDE "Perf check").
  4. Screenshots of the visible features.
  5. Docs: the area files of your systems (step 2 lists the areas you changed), CLAUDE.md command lines for new CLI options, and ROADMAP lines ([x] with the
     date, plus "- [ ] later:" items for what you leave out). Don't rewrite the ROADMAP "NEXT STEPS" section: the
     orchestrator owns it.
  6. Commit. The tree must be clean.
- Final report: write `<your worktree>/.claude/agent-report.md`, under 350 words:
  - what works;
  - how to try it (CLI / sandbox commands);
  - tests added;
  - new hotkeys;
  - shared-file edits;
  - the table printed by `arch.py review main` (areas changed, the docs changed or the `Arch:` reasons);
  - measured numbers (bench, sizes, win rates…);
  - known issues and follow-ups;
  - **"Design errors found"** (always present; write "none" if there were none): every bug or limit whose cause is
    a design or architecture choice, in your code or in older code: a regression found on the way, an exploit, a
    wrong assumption, a risk you left open. One or two lines each: the error, why it happened, the fix (or "open"),
    the commit. The orchestrator turns them into LESSONS.org entries at merge (WORKFLOW "Lifecycle of a wave" 7);
  - if you continued from a handoff note: "Handoff review", 2-4 lines: what the note lacked, what you had to
    rediscover, what you redid or re-tried that was already done or ruled out, what was broken at the seam.
  Return the same short text as your final message. The orchestrator copies it into docs/HISTORY.md at merge.
