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
| 2026-10-16 | The 150k cap is the **default, not a fixed size**: before starting an agent or sub-agent, judge from the task's requirements how much splitting it across fresh agents would hurt the *result* (not the token cost); when it would hurt a lot, give that agent a proportionally higher cap (up to 300k). Keep evaluating how much the small-context approach harms the success of the work. See "Sizing an agent's context cap" and "Fragmentation: does a small cap hurt the result?". |
| 2026-10-23 | **Two factions are enough** (UEF + Cybran): don't create further factions (no Aeon/Seraphim). "Everything must be done" means the full feature set for these two. |
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
   ([tokens/README.org](tokens/README.org)). A new purpose also needs its area and its wave in
   `tools/token-ledger/purposes.json` (`aree`, `onde`), or its cost shows up as "Da classificare" (it happened to
   waves 6-8 until 2026-11-10; the session-start hook now names the purposes without an area). One agent per area with `isolation: "worktree"`: it gets its own
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
   "NEXT STEPS". Write the lines the module added into `purposes.json` (`righe`; the command is in
   [tokens/README.org](tokens/README.org) "Onde e righe"), run `ledger.py update` and commit `docs/tokens/` (the hooks
   keep it current): `summary.org` then shows the module's and the wave's cost per 1000 lines. After the last merge of
   a wave, bring the whole "Costi" chapter of [LESSONS.org](LESSONS.org) up to date with `summary.org` (its tables,
   not only a new paragraph: from 2026-10-15 to 2026-11-10 the main tables stayed at the wave 4 numbers). Then remove the worktree and the branch: `git worktree remove <path>` + `git branch -d <branch>`.
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

- **Cap: 150k tokens of context per agent and sub-agent by default** (owner, 2026-10-16; it was 300k), raised per task
  when splitting would hurt the result (next section). Every prompt states the agent's cap. About 20k below it the agent commits, updates its
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
- **Self-measure** (2026-10-24): every prompt carries a unique `CTX-TAG: <tag>` line and the agent runs
  `agent_ctx.py --self <tag>` itself (AGENT_RULES), because agents can't feel their own context: on 2026-10-24 all five
  wave 6 sub-agents ran 10-45k past their CHECKPOINT until the orchestrator messaged them.
- **Measuring context**: `python3 -I tools/agents/agent_ctx.py <agentId>...` prints each agent's exact context,
  taken from the API usage fields of its transcript. It never reads the transcript text. File size is useless,
  because screenshots inflate it. Background alert:
  `python3 -I tools/agents/agent_ctx.py --watch 135000 [id=tokens ...] [ignored ids]` (`id=tokens`: own threshold of an agent with a raised cap). It covers every agent and sub-agent active
  in the last 5 minutes, and exits with a message when one passes the threshold.
- The orchestrator's own context: the owner restarts it at about 200k. Keep it lean:
  - delegate conflict-heavy merges;
  - don't read big files or many screenshots;
  - pipe long outputs through `tail`/`grep`;
  - keep ROADMAP "NEXT STEPS" current, so a restart costs nothing.

## Sizing an agent's context cap

Owner rule (2026-10-16): the cap follows the task. Before writing an agent or sub-agent prompt, the parent judges
its **fragmentation sensitivity**: how much the task's success depends on reasoning that a short handoff note
cannot carry to a fresh agent.

| Class | Cap (CHECKPOINT) | Typical tasks |
|---|---|---|
| low | 150k (130k) | Separable pieces with a written contract: data files, one model/test/doc file per piece, a well-specified feature in 1-3 files. The note carries everything. |
| medium | 200k (180k) | One feature across several files or modules whose design choices must stay consistent; integrating other agents' pieces; a merge with a few conflicts. |
| high | 250k (230k) | The result rests on a chain of evidence built during the work: diagnosing a non-obvious bug, AI/balance tuning that compares many runs, a performance investigation, a cross-cutting refactor, a conflict-heavy merge. |
| critical | 300k (280k), the maximum | Searches where a restart means starting the search again: desync/determinism hunts. Above 300k quality drops (owner, 2026-10-12). |

Proportional check: estimate **S**, the start-up reading (system prompt ≈ 35k + the files and sections the agent
must read, bytes / 4), and **W**, what it must hold at once (the code it edits, the logs or run outputs it
compares). If S + W + 40k exceeds the class cap, take the next class. If S alone is over ~60k, first trim the
reading list: that is a prompt problem, not a cap problem (both "dead" attempts of 2026-10-16 below).
- The parent writes in the prompt: `Fragmentation sensitivity: <class>, because <reason>. Your cap: <cap>k,
  CHECKPOINT at <cap-20>k.` and the same line in its handoff note.
- A raised cap belongs to the piece that needs it: when the diagnosis is done and the rest is routine, the next
  agent gets its own class again.
- The orchestrator gives the context watcher the raised threshold: `agent_ctx.py --watch 135000 <id>=<cap-15k>`.
- Bigger contexts cost more per call (see "Usage limits"); the owner accepts that when the result needs it.

## Fragmentation: does a small cap hurt the result?

Owner request (2026-10-16): keep evaluating how much many small contexts harm the *success* of the work.
- **Countable symptoms**: `python3 -I tools/agents/handoffs.py [--since DATE] [--agents]` gives per purpose the
  attempts, the CHECKPOINT hand-overs and the **dead** attempts (stopped at the cap having worked only in its last
  15 %: read until the cap, handed over nearly nothing).
- **The result itself**: every agent that continued from a handoff adds a "Handoff review" to its report: what
  the note lacked, what it had to rediscover, what it redid or re-tried that its predecessor had already done or
  ruled out, what it found broken at the seam. At each merge the orchestrator copies it into HISTORY and adds a row
  below.
- About every two waves the orchestrator reads the table: classes whose pieces show major harm get a bigger cap
  (or that kind of task moves up a class); classes that never show harm stay at 150k.

| Date | Piece | Class (cap used) | Attempts / dead | Harm to the result |
|---|---|---|---|---|
| 2026-10-16 | (k) AI stall diagnosis | high (150k at the time) | k3 202k (old cap), k4 135k, k5 / navy sub-agent dead at 164k | medium: k4 found the cause (T2/T3 play itself: half the land army, 42 pgens vs 15, unspent mass) but stopped at the cap before any fix, so a third agent re-reads the evidence. Navy: read until the cap, no code. k5 raised to 250k. |
| 2026-10-16 | (l) client models | low (150k) | C dead at 152k, C1 started at 70k | none on the result so far; the dead attempt came from a too-wide reading list (fixed in AGENT_RULES), not from the cap. |
| 2026-10-23 | (k-navy) navy + transports | medium (200k) | agent 1 reached 193k in 11.5 min (read whole AI and transport files), stopped with the navy written but untested | low so far: its note lists the verified APIs; agent 2 started with a "grep, read ranges, never whole big files" rule. (k) coordinator 8 reached 240k in 38 min (matrix outputs). |
| 2026-10-23 | (l) Cybran C1b–C3b + integration | coordinators medium (200k), pieces low (150k) | coordinator 10-22 cut by usage limit at 107k, fresh one finished at 110k; C1b reached 151k before writing code (big model files), C3a cut by usage limit; C3b 135k | low: every piece finished from its note; the only rediscovery was cosmetic (parked planes looked missing at the default camera). Model pieces read ~110k of code first: give them the helper signatures in the prompt. |
| 2026-10-24 | (k) AI uses everything, end | high (250k) | k9 CHECKPOINT at 240k; k10 cut by a usage limit at 41k (only merged main); k11 finished at 191k | low: k11 continued from the note without redoing work (only gaps: data is read live, matrices take 8-12 min). Over the whole of (k) (11 coordinators) the harm was medium: the stall diagnosis was re-read twice (2026-10-16 row) and the goals stayed unmet; the remaining stall problem is an AI decision change, not a context problem. |
| 2026-10-24 | (6a) Commander & engineers | coordinators medium (200k), pieces low (150k; one client piece 180k) | coordinator 2 cut by a usage limit at 33k; coordinator 3 CHECKPOINT at 163k, coordinator 4 finished at 132k; 5 sub-agents ran 10-45k past their CHECKPOINT (stopped by the orchestrator) | low: every piece finished from its note; one seam bug (fog hid the own teleport fx), a broken build at a handoff (WIP needed 4 shared lines). Agents can't feel their context: `agent_ctx.py --self` + CTX-TAG added. |
| 2026-10-24 | (6b) Game options & team play | coordinator medium (200k), pieces low (150k), finish piece medium (200k) | one coordinator to the end (171k), resumed once after a usage limit; client-2 cut by the limit at 123k (note written on resume); the sim piece ran past its CHECKPOINT before `--self` existed | none: no rediscovered work beyond two API facts, no seam bugs. |
| 2026-10-24 | (7a) Release, settings, autosave | coordinator medium (200k), pieces low (150k) | one coordinator (132k), resumed once after a usage limit (its packaging sub-agent cut at 61k with nothing committed, resumed) | none reported. |

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
- **On Fable 5.2 a window is about 5 times smaller** (measured 2026-11-10 from the transcripts' usage fields; all
  numbers above are for Opus 5.5). The session and its agents ran on Fable 5.2 (the owner's `/model` choice; agents
  inherit it): the 13:00 window ended after 129 calls and 10M tokens of this project (plus 2.3M of another session
  of the owner), the 18:00 window after 93 calls and 7.7M (plus 1.9M elsewhere). That was 8-10 minutes of
  2 coordinators + 2-3 sub-agents each time, and every resume first pays the re-read of each agent's context.
  Open owner decision (asked 2026-11-10): agents on Opus 5.5 (`model: "opus"` in the Agent call; about 5 times more
  agent work per window, as in waves 1-7a) or on Fable 5.2 (stronger model, short windows).

## Resuming (new orchestrator session)

1. Read ROADMAP "▶ NEXT STEPS", especially its "ON RESUME" list.
2. Make sure no other orchestrator session is running. It happened on 2026-10-14: a second session found main
   moving under it. Check `ps -eo etime,args | grep claude`, `git log --oneline -5` and the times of the recent
   session transcripts in `~/.claude/projects/-run-media-marco-RTS/` (before 2026-10-23: `-run-media-marco-Volume-D-RTS`).
3. `git log --oneline -5`, `git worktree list`, `git branch`.
4. For each worktree: is it finished (report written, clean tree, final commit) or does it need a fresh agent
   (read its `.claude/handoff.md`)?
5. Continue with the merges and the next wave as the NEXT STEPS say.

## Sub-agents

An agent may start sub-agents (general-purpose) for well-separated pieces, e.g. sim vs client:
- they work in the parent's worktree (absolute path, no isolation flag), on disjoint files;
- only one `dotnet build`/`dotnet test` runs at a time per worktree: use `tools/agents/dn.sh` (see DEV_GUIDE);
- they commit only their own files: `git add <files> && git commit -m "..." -- <files>`;
- a cap sized like any agent's ("Sizing an agent's context cap", default 150k); short reports; before finishing or stopping, the essentials go to `.claude/notes/<piece>.md`.

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
| Session transcripts, private memory | `~/.claude/projects/-run-media-marco-RTS/` (older sessions: `-run-media-marco-Volume-D-RTS/`) | private. Memory holds only pointers to these docs and how-to-work preferences. |
| Scratchpads | `/tmp/claude-1000/...` | **wiped on reboot**: never keep anything important there |

## Incidents worth remembering

- **Stray `.gitignore` rewrite (wave 4):** an unknown sub-agent rewrote main's `.gitignore`. It was reverted. Since
  then sub-agents commit only their own files. Before merging, check `git status` in main.
- **Locked build (wave 4):** four orphaned MSBuild nodes held the shared build lock forever, and every build in that
  worktree hung. The fix is `flock -o` with MSBuild node reuse off: `tools/agents/dn.sh`. Killing the orphaned
  processes was left to the owner.
- **Two orchestrators (2026-10-14):** a new session started while the previous orchestrator was still merging into
  main. Hence step 2 of "Resuming".
- **Disk remounted (2026-10-23):** the data partition, mounted until then at `/run/media/marco/Volume D` (owner marco),
  came up at `/run/media/marco` as root (`fuseblk`, `user_id=0`), so the repo moved to `/run/media/marco/RTS`. Git
  refused it ("dubious ownership": fixed with `git config --global --add safe.directory <path>` for main and each
  worktree), the worktrees pointed at the old path (`git worktree repair <new paths>` from main), and Claude Code
  filed the transcripts under a new project key (`-run-media-marco-RTS`; the ledger and `handoffs.py` also read the
  old folder). New worktrees need their own `safe.directory` entry.
