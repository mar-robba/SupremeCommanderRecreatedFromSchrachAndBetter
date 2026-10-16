# Rules block for agent prompts

The orchestrator pastes the block below into every agent prompt, after the agent's own brief (goal, must-haves,
files it owns). Fill in the `<...>` parts. Why these rules exist: [WORKFLOW.md](WORKFLOW.md).

---

## Rules for this job (read carefully)
- Project: a Supreme Commander 1-like RTS in C#/.NET 10 + Raylib-cs. Main checkout: `/run/media/marco/Volume D/RTS`.
  First read CLAUDE.md, docs/README.md and docs/ROADMAP.md. Read the docs/ARCHITECTURE.md sections for your area
  in full and skim the rest, especially "Factions" and "Save/load & replays": every new sim field must be saveable.
  Read docs/DEV_GUIDE.md before your first build.
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
- Context cap: HARD limit of 300k tokens of context. Stay lean:
  - don't read huge files whole (grep, then read ranges);
  - read few screenshots (at most ~6 in your whole run; prefer `--size 960x540`);
  - pipe long outputs through tail/grep.
  At ~250k: commit, update your handoff note, and end your turn with the word CHECKPOINT. A fresh agent will
  continue from your note.
- Handoff note: keep `<your worktree>/.claude/handoff.md` up to date at every milestone: done / TODO in order /
  known bugs / key commands / bench numbers. Commit WIP at every milestone. Usage limits can stop you at any moment.
  The orchestrator then commits your leftovers as "WIP" and starts a fresh agent from your note.
- Sub-agents: you MAY start sub-agents (general-purpose) for well-separated pieces (e.g. sim vs client). Rules:
  - they work in YOUR worktree (give them the absolute path; no isolation flag) on disjoint files;
  - only ONE `dotnet build`/`dotnet test` may run at a time per worktree: everyone runs dotnet through
    `bash tools/agents/dn.sh <dotnet args>` (call it through `bash`: the volume is mounted without exec bits and git has `core.fileMode=false`);
  - they commit only their own files (`git commit -m "..." -- <files>`);
  - give them the same 300k cap and ask for short reports;
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
- Final report: write `<your worktree>/.claude/agent-report.md`, under 300 words:
  - what works;
  - how to try it (CLI / sandbox commands);
  - tests added;
  - new hotkeys;
  - shared-file edits;
  - measured numbers (bench, sizes, win rates…);
  - known issues and follow-ups.
  Return the same short text as your final message. The orchestrator copies it into docs/HISTORY.md at merge.
