# How the project is built (owner + orchestrator + agents)

The owner does not write code. Everything is written by Claude agents. One Claude session, the **orchestrator**,
plans the work, writes the agent prompts, starts the agents, merges their branches and checks the result. Each
**agent** owns one area (e.g. "naval", "save/load") and works in its own git worktree. An agent may start
**sub-agents** for separate pieces of its area.

This file is the process. The state of the work is in [ROADMAP.md](ROADMAP.md), what was delivered is in
[HISTORY.md](HISTORY.md), and the rules every agent gets are in [AGENT_RULES.md](AGENT_RULES.md).

## Owner decisions

These are the owner's instructions so far. They are binding. Add new ones here, with the date.

| Date | Decision |
|---|---|
| 2026-10-10 | Build a large-scale, multithreaded RTS like Supreme Commander 1. The owner doesn't code: AI agents build everything. The orchestrator may start as many agents as it needs. When the orchestrator's context gets full, continue in a new session. |
| 2026-10-10 | No new system toolchains (the owner refused a rustup install). The project uses the installed .NET 10 SDK. NuGet packages are fine. If a system package is really needed, ask first (the owner can run `! sudo apt install ...`). Also CLAUDE.md hard rule 8. |
| 2026-10-12 | Hard limit: **300k tokens of context per agent** (quality drops with very large contexts). See "Context limits and handoffs". |
| 2026-10-12 | Agents may start their own sub-agents (rules in AGENT_RULES.md). |
| 2026-10-12 | "Giudica contando che tutto deve essere fatto": everything must be done. The orchestrator chooses the next waves itself, aiming at the full SupCom feature set. |
| 2026-10-14 | The owner restarts the orchestrator at about 200k context, so the orchestrator keeps its own context lean and prepares its handoff (ROADMAP "NEXT STEPS") early. |
| 2026-10-14 | All durable documentation goes in `docs/`, organized (see [README.md](README.md)), never only in the agents' private memory. Only small transient working notes stay private. |
| 2026-10-15 | The owner clarified that they *can* install and uninstall software on this machine (the earlier "not technical" was not accurate). System packages and toolchains still need the owner's OK first: the orchestrator asks, and the owner installs. The owner asked whether Rust would have been a better choice than .NET for efficiency; the orchestrator's answer (stay on .NET: the sim uses ~4 % of its tick budget, the bottleneck is the GPU, a rewrite of ~53k lines would add no features; cross-machine float determinism is a risk in both languages and is handled in C#, see agent (o)). The owner read it and agreed to remove the question note from ARCHITECTURE.md. |
| 2026-10-15 | Track how many tokens go to each purpose, for this project only: the token ledger in [tokens/](tokens/README.org) is filled by project hooks; agents get a `[purpose]` tag in their description; sessions are tagged with `ledger.py tag`. |
| 2026-10-16 | Context cap lowered: **150k tokens per agent and sub-agent** (CHECKPOINT at ~130k). Every agent and sub-agent, when it finishes or stops, saves only the essentials of its work (done + commits, what is left, decisions/pitfalls) in a short note in its worktree (`.claude/handoff.md`, sub-agents `.claude/notes/<piece>.md`), so the work continues after a restart of the orchestrator. |
| always | Talk to the owner in Italian. Code, comments and docs are in English. |

## Waves

The work goes in **waves**: the orchestrator starts several agents in parallel, each on a separate area, then
merges them all before the next wave.

| Wave | When | Areas |
|---|---|---|
| Foundation | 2026-10-10 | Solution, deterministic sim core, threaded runner, blueprints, map generator, orders, movement, basic client (orchestrator). |
| 1 | 2026-10-10 | Combat, economy & construction, pathfinding & movement, client visuals, UI. |
| Phase 2 work | 2026-10-11 | Fog of war & radar, first skirmish AI, map presets, menus/setup/end screens, audio. |
| 3 | 2026-10-11 → 12 | (a) T2/T3 tech + upgrades, (b) shields & strategic weapons, (c) naval, (d) skirmish AI (continued in wave 4), (e) sandbox. |
| 4 | 2026-10-12 → 14 | (d) AI finish, (f) SupCom polish, (g) experimentals + commander enhancements + SCUs, (h) transports & cargo, (i) save/load + replays. |
| pre-5 | 2026-10-13 → | (j) faction infrastructure (factions + roles, so a second faction can be added). |
| 5 | 2026-10-14 → | (m) scale/perf, (n) UX polish, (o) lockstep multiplayer; after (j): (k) AI uses everything, (l) Cybran faction. |

Details per agent, including the wave 5 briefs: [HISTORY.md](HISTORY.md). The current state: ROADMAP "NEXT STEPS".

## Lifecycle of a wave

1. **Plan.** Check what similar work cost in [tokens/summary.org](tokens/summary.org) and size the briefs to
   finish within one usage window. Choose areas whose files barely overlap. Write the contracts they share (they
   become ARCHITECTURE sections): enums are append-only; `BuiltBy` / `UpgradesTo` in blueprints; faction roles
   instead of hard-coded `uef_*` ids; all sim state saveable.
2. **Prompt.** Each agent gets its area brief (goal, must-haves, files it owns, what to leave to others) plus the
   rules block from [AGENT_RULES.md](AGENT_RULES.md), with the wave's agent list filled in. Write the briefs
   in [HISTORY.md](HISTORY.md), in the wave's section, so the owner can read what is being built.
3. **Start.** Give every agent (and every sub-agent) a description that starts with its purpose tag, e.g.
   `[Fazione Cybran (5l)] client models`, so the token ledger files its cost under that purpose
   ([tokens/README.org](tokens/README.org)). One agent per area with `isolation: "worktree"`: it gets its own
   branch and folder under `.claude/worktrees/`. The worktree may be created from `origin/main`, which is stale,
   so the agent's first step is `git merge main` (the local branch).
4. **While they run.** Watch their context size (below) and handle usage limits (below). Answer questions; don't
   do their work in the orchestrator's context.
5. **Finish.** The agent merges main again, builds, runs both test projects, benchmarks, takes screenshots, writes
   its ARCHITECTURE section and ROADMAP lines, commits (clean tree) and writes `<worktree>/.claude/agent-report.md`.
6. **Merge**, one branch at a time, in main: `git merge --no-ff <branch>`, then build, both test projects (including
   `SimulationIsDeterministicAcrossThreadCounts`, `SaveCoverageTests`, `PersistenceTests`) and a screenshot of the
   feature. If the merge has many conflicts, don't resolve them in the orchestrator: start a **merge agent** in the
   branch's worktree. It merges main into the branch there and resolves the conflicts. Then main merges the
   branch with no conflicts.
7. **After the merge.** Copy the agent's report into [HISTORY.md](HISTORY.md) (its own section) and update ROADMAP
   "NEXT STEPS". Commit `docs/tokens/` (the hooks keep it current); after the last merge of a wave, update the
   "Costi" chapter of [LESSONS.org](LESSONS.org) with the wave's numbers. Then remove the worktree and the branch: `git worktree remove <path>` + `git branch -d <branch>`.
   A locked worktree needs `git worktree unlock <path>` first.
8. **Cross-module bugs** often appear only after a merge (example: anti-air vs orbiting aircraft). Send them to the
   agent that owns the area. That agent runs `git merge main` first.

### Where merges conflict

These files conflicted in almost every wave:
- `GameApp.cs` and `GameSession.cs`: CLI options and scene wiring. Keep both sides.
- `OrderSystem.cs`, `Army.cs`, the HUD help lines.
- `ARCHITECTURE.md`: each agent adds its own section.
- Hotkeys: two agents pick the same free key. In wave 4, (f) used D = dive and E = capture, while (h) used
  D = dock and E = transport-to. The merge made them context-dependent (`Input/SharedHotkeys.cs`). Agents should
  list their new hotkeys in their report.
- Enum values: two agents add the same idea. Example: `EffectKind.Nuke` from both the sandbox and the strategic
  agent. Keep one value.

To keep conflicts small:
- shared files get only small, additive edits;
- new code goes in new files (partial classes);
- new models go in new partial files, with one `case` line in `UnitModels.cs`.

## Context limits and handoffs

- **Hard cap: 150k tokens of context per agent and sub-agent** (owner, 2026-10-16; it was 300k). Every prompt says it. At about 130k the agent commits, updates its
  handoff note and ends its turn with the word `CHECKPOINT`. The orchestrator then starts a **fresh agent in the
  same worktree**: absolute path in the prompt, no isolation flag, the original task plus "continue from the
  handoff note".
- **Handoff note**: `<worktree>/.claude/handoff.md`. It has: what is done, the TODO in order, known bugs, key
  commands and benchmark numbers; short (under ~60 lines), only what the next agent needs. The agent updates it at
  every milestone. A sub-agent writes the essentials of its piece to `<worktree>/.claude/notes/<piece>.md` when it
  finishes or stops, and its parent links it from the handoff note. It is a transient working note (the
  `.claude/` folder is gitignored). Its lasting parts end up in ARCHITECTURE, HISTORY or DEV_GUIDE.
- **Reading costs context**: all of `docs/` is ~190 kB, about 50k tokens; with the system prompt a fresh agent that
  reads everything starts near 70k. On 2026-10-16 a coordinator reached 126k and a client sub-agent 144k within
  10 minutes of starting. So prompts give a targeted reading list (AGENT_RULES first bullet): handoff and notes
  first, then only the needed sections; sub-agent prompts carry the contract lines they need instead of "read the docs".
- **Never resume a big agent** with SendMessage after it was cut off: it keeps its whole old transcript and grows
  without limit. Resume only agents well under 100k. Replace the others with a fresh agent and the handoff note.
- **Measuring context**: `python3 -I tools/agents/agent_ctx.py <agentId>...` prints each agent's exact context,
  taken from the API usage fields of its transcript. It never reads the transcript text. File size is useless,
  because screenshots inflate it. Background alert:
  `python3 -I tools/agents/agent_ctx.py --watch 135000 [ignored ids]`. It covers every agent and sub-agent active
  in the last 5 minutes, and exits with a message when one passes the threshold.
- The orchestrator's own context: the owner restarts it at about 200k. Keep it lean:
  - delegate conflict-heavy merges;
  - don't read big files or many screenshots;
  - pipe long outputs through `tail`/`grep`;
  - keep ROADMAP "NEXT STEPS" current, so a restart costs nothing.

## Usage limits

The account hits usage limits about every 2–5 hours, and the agents stop wherever they are. After the reset:
1. Commit the leftovers in every dirty worktree as `WIP` (the work is never lost, even if it doesn't compile).
2. In the same orchestrator session: resume each agent under ~200k context with SendMessage, and tell it which of
   its sub-agents were cut off (and their context sizes). Replace bigger ones with fresh agents plus their handoff
   note.
3. In a new orchestrator session, old agent ids don't carry over. A session resumed with `--resume` keeps them.
   Without the ids, start a fresh agent in the existing worktree path.

**How big a window is** (measured 2026-10-16 with `python3 -I tools/token-ledger/windows.py`, input-side tokens,
mostly cache reads, of every session and agent of the project):
- every 5-hour window ended at about **40–60M tokens** (250–650 model calls), at any hour of the day or night:
  steadiest measure, about **20–23 $** of input-side list price per window (cache reads 0.2 $/M, cache writes 5 $/M),
  so bigger contexts mean fewer calls per window. (A first version of the script claimed bigger evening windows:
  a bug with resets after midnight, fixed.) The weekly limit (resets Thursday 10:00) came at about **716M tokens**
  (about 280 $), so roughly 13 windows per week.
- On 2026-10-16, 2 agents (plus at times one sub-agent and the orchestrator) used the 10:40 window in 63 minutes
  (606 calls, 80M tokens, 22 $), and finished and merged three agents in it.
- On 2026-10-15 15:01, 12 agents resumed together (5 agents + 7 sub-agents, many with 200k contexts whose cache had
  expired) used the whole window in 9 minutes; on 11:00 the same five agents used it in 20 minutes.
- A window holds a fixed amount of work, whatever the number of agents: more agents in parallel only make the window
  end sooner, and each one is cut and needs a resume (about 1 $ of start-up reading each, plus its cache rewrite).
  So run **at most 2 agents at a time, without sub-agents**, and size each brief to finish within one window.

## Resuming (new orchestrator session)

1. Read ROADMAP "▶ NEXT STEPS", especially its "ON RESUME" list.
2. Make sure no other orchestrator session is running. It happened on 2026-10-14: a second session found main
   moving under it. Check `ps -eo etime,args | grep claude`, `git log --oneline -5` and the times of the recent
   session transcripts in `~/.claude/projects/-run-media-marco-Volume-D-RTS/`.
3. `git log --oneline -5`, `git worktree list`, `git branch`.
4. For each worktree: is it finished (report written, clean tree, final commit) or does it need a fresh agent
   (read its `.claude/handoff.md`)?
5. Continue with the merges and the next wave as the NEXT STEPS say.

## Sub-agents

An agent may start sub-agents (general-purpose) for well-separated pieces, e.g. sim vs client:
- they work in the parent's worktree (absolute path, no isolation flag), on disjoint files;
- only one `dotnet build`/`dotnet test` runs at a time per worktree: use `tools/agents/dn.sh` (see DEV_GUIDE);
- they commit only their own files: `git add <files> && git commit -m "..." -- <files>`;
- same 150k cap; short reports; before finishing or stopping, the essentials go to `.claude/notes/<piece>.md`.

Sub-agents cannot write outside their worktree, e.g. the orchestrator's private folder. That's why reports go to
`<worktree>/.claude/agent-report.md`.

## Where things live

| What | Where | Lasting? |
|---|---|---|
| Documentation (all of it) | `docs/` (this folder), `CLAUDE.md` | yes, in git |
| Agent/orchestrator tools | `tools/agents/` (build lock, context monitor), `tools/ai-lab/` (AI test matrices) | yes, in git |
| An agent's working notes and final report | `<worktree>/.claude/handoff.md`, `<worktree>/.claude/agent-report.md` | until the merge; then the report goes into HISTORY.md |
| Screenshots, saves, replays | `screenshots/`, `saves/`, `replays/` | local only (gitignored) |
| AI matrix logs | `<worktree>/.claude/ai-lab/runs/<tag>/` | local only |
| Token ledger (tokens per session/agent and purpose) | `docs/tokens/`, written by `tools/token-ledger/ledger.py` from the hooks in `.claude/settings.json` | yes, in git (the hooks file is local) |
| Session transcripts, private memory | `~/.claude/projects/-run-media-marco-Volume-D-RTS/` | private. Memory holds only pointers to these docs and how-to-work preferences. |
| Scratchpads | `/tmp/claude-1000/...` | **wiped on reboot**: never keep anything important there |

## Incidents worth remembering

- **Stray `.gitignore` rewrite (wave 4):** an unknown sub-agent rewrote main's `.gitignore`. It was reverted. Since
  then sub-agents commit only their own files. Before merging, check `git status` in main.
- **Locked build (wave 4):** four orphaned MSBuild nodes held the shared build lock forever, and every build in that
  worktree hung. The fix is `flock -o` with MSBuild node reuse off: `tools/agents/dn.sh`. Killing the orphaned
  processes was left to the owner.
- **Two orchestrators (2026-10-14):** a new session started while the previous orchestrator was still merging into
  main. Hence step 2 of "Resuming".
