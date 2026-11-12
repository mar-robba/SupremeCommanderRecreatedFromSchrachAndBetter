# How the project is built (owner + orchestrator + agents)

The owner does not write code. Everything is written by Claude agents. One Claude session, the **orchestrator**,
plans the work, writes the agent prompts, starts the agents, merges their branches and checks the result. Each
**agent** owns one module (e.g. "naval", "save/load"), cut to the size of one agent, and works in its own git
worktree. It starts **sub-agents** only for separable content with a written contract ("Sub-agents").

This file is the process. The state of the work is in [ROADMAP.md](ROADMAP.md), what was delivered is in
[HISTORY.md](HISTORY.md), and the rules every agent gets are in [AGENT_RULES.md](AGENT_RULES.md).

## Owner decisions

These are the owner's instructions so far. They are binding. Add new ones here, with the date.

| Date | Decision |
|---|---|
| 2026-10-10 | Build a large-scale, multithreaded RTS like Supreme Commander 1. The owner doesn't code: AI agents build everything. The orchestrator may start as many agents as it needs (since 2026-11-10: at most 2 at a time). When the orchestrator's context gets full, continue in a new session. |
| 2026-10-10 | No new system toolchains (the owner refused a rustup install). The project uses the installed .NET 10 SDK. NuGet packages are fine. If a system package is really needed, ask first (the owner can run `! sudo apt install ...`). Also CLAUDE.md hard rule 8. |
| 2026-10-12 | Hard limit: **300k tokens of context per agent** (quality drops with very large contexts). See "Context limits and handoffs". |
| 2026-10-12 | Agents may start their own sub-agents (rules in AGENT_RULES.md). Since 2026-11-10: only for separable content. |
| 2026-10-12 | "Giudica contando che tutto deve essere fatto": everything must be done. The orchestrator chooses the next waves itself, aiming at the full SupCom feature set. |
| 2026-10-14 | The owner restarts the orchestrator at about 200k context, so the orchestrator keeps its own context lean and prepares its handoff (ROADMAP "NEXT STEPS") early. |
| 2026-10-14 | All durable documentation goes in `docs/`, organized (see [README.md](README.md)), never only in the agents' private memory. Only small transient working notes stay private. |
| 2026-10-15 | The owner clarified that they *can* install and uninstall software on this machine (the earlier "not technical" was not accurate). System packages and toolchains still need the owner's OK first: the orchestrator asks, and the owner installs. The owner asked whether Rust would have been a better choice than .NET for efficiency; the orchestrator's answer (stay on .NET: the sim uses ~4 % of its tick budget, the bottleneck is the GPU, a rewrite of ~53k lines would add no features; cross-machine float determinism is a risk in both languages and is handled in C#, see agent (o)). The owner read it and agreed to remove the question note from ARCHITECTURE.md. |
| 2026-10-15 | Track how many tokens go to each purpose, for this project only: the token ledger in [tokens/](tokens/README.org) is filled by project hooks; agents get a `[purpose]` tag in their description; sessions are tagged with `ledger.py tag`. |
| 2026-10-16 | Context cap lowered: **150k tokens per agent and sub-agent** (CHECKPOINT at ~130k). Every agent and sub-agent, when it finishes or stops, saves only the essentials of its work (done + commits, what is left, decisions/pitfalls) in a short note in its worktree (`.claude/handoff.md`, sub-agents `.claude/notes/<piece>.md`), so the work continues after a restart of the orchestrator. |
| 2026-10-16 | The 150k cap is the **default, not a fixed size**: before starting an agent or sub-agent, judge from the task's requirements how much splitting it across fresh agents would hurt the *result* (not the token cost); when it would hurt a lot, give that agent a proportionally higher cap (up to 300k). Keep evaluating how much the small-context approach harms the success of the work. See "Sizing an agent's context cap" and "Fragmentation: does a small cap hurt the result?". |
| 2026-10-23 | **Two factions are enough** (UEF + Cybran): don't create further factions (no Aeon/Seraphim). "Everything must be done" means the full feature set for these two. |
| 2026-11-10 | The owner asked for a judgment of these rules and approved its four changes ("effettua le modifiche consigliate"; "Review of the process rules (2026-11-10)"): (1) agents and sub-agents run on **Opus 5.5** (`model: "opus"` in every Agent call); Fable 5.2 only for the orchestrator and for pieces of class high or critical. (2) **One agent per module, no coordinator**: modules of about 1,500 lines, cap 200k; sub-agents only for separable content with a written contract (blueprints, models, tests). (3) **At most 2 agents at a time, sub-agents counted**, and no start when other sessions of the owner have already used part of the usage window. (4) LESSONS "Costi" is brought up to date **every 2-3 waves** or when the owner asks, not after every wave. |
| 2026-11-11 | **Improving the AI is no longer a goal of the project** ("l'ai non deve essere più un obbiettivo del progetto, terremo quella già fatta"; then "mantieni la doc sulla ai soltanto togli dal programma l'obbiettivo di migliorarla"). The skirmish AI stays as merged in wave 5. Removed from ROADMAP: wave 9a, gap rows G30-G33, the "later: AI ..." lines. The AI docs (ARCHITECTURE "Skirmish AI", AI_TUNING) stay unchanged. Agents plan no AI work and write no "later: AI uses X" lines; the AI only has to keep working (AI tests, the reference match at every merge). |
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
   finish within one usage window. Cut the work into modules of about 1,500 lines (the size of 7a), each for one
   agent with a 200k cap and no coordinator (owner, 2026-11-10); what needs more becomes two modules, one after the
   other. Choose areas whose files barely overlap. Write the contracts they share (they
   become ARCHITECTURE sections): enums are append-only; `BuiltBy` / `UpgradesTo` in blueprints; faction roles
   instead of hard-coded `uef_*` ids; all sim state saveable.
2. **Prompt.** Each agent gets its area brief (goal, must-haves, files it owns, what to leave to others) plus the
   rules block from [AGENT_RULES.md](AGENT_RULES.md), with the wave's agent list filled in. Write the briefs
   in [HISTORY.md](HISTORY.md), in the wave's section, so the owner can read what is being built.
3. **Start.** Give every agent (and every sub-agent) a description that starts with its purpose tag, e.g.
   `[Fazione Cybran (5l)] client models`, so the token ledger files its cost under that purpose
   ([tokens/README.org](tokens/README.org)). A new purpose also needs its area and its wave in
   `tools/token-ledger/purposes.json` (`aree`, `onde`), or its cost shows up as "Da classificare" (it happened to
   waves 6-8 until 2026-11-10; the session-start hook now names the purposes without an area). Every Agent call carries
   `model: "opus"` (owner, 2026-11-10: agents otherwise inherit the session's model, and a Fable 5.2 window holds a
   fifth of the work); `model: "fable"` only for a piece of class high or critical, planned for a window of its own.
   As a safety net `.claude/settings.json` sets `"env": {"CLAUDE_CODE_SUBAGENT_MODEL": "opus"}`, the default model
   of sub-agents in sessions started after 2026-11-11 (the variable exists in Claude Code 2.1.322; its effect was
   not checked with a real agent, and the file is local, not in git). Keep the explicit `model` in the call.
   At most 2 agents run at a time, sub-agents counted, and none starts when other sessions of the owner have already
   used part of the window ("Usage limits"). One agent per module with `isolation: "worktree"`: it gets its own
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
   keep it current): `summary.org` then shows the module's and the wave's cost per 1000 lines. Every 2-3 waves, or
   when the owner asks (owner, 2026-11-10; before: after every wave), bring the whole "Costi" chapter of
   [LESSONS.org](LESSONS.org) up to date with `summary.org` (its tables, not only a new paragraph: from 2026-10-15
   to 2026-11-10 the main tables stayed at the wave 4 numbers); its first lines say when it is next due. **At every
   merge**, turn the report's "Design errors found" section (AGENT_RULES "Final report", mandatory since
   2026-11-12; every prompt's "Final report" line must name it) and any design error the merge checks show into
   dated entries of [LESSONS.org](LESSONS.org) (in Italian: Errore, Soluzione, commit); "none" = nothing to add. This step was missing from 2026-10-16 to 2026-11-12: those errors went
   only into HISTORY "Known issues" and DEV_GUIDE pitfalls, and the owner noticed (entries written 2026-11-12).
   Then remove the worktree and the branch: `git worktree remove <path>` + `git branch -d <branch>`.
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
  when splitting would hurt the result (next section); a module agent, which since 2026-11-10 does its module alone,
  is class medium (200k). Every prompt states the agent's cap. About 20k below it the agent commits, updates its
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
- **How fast a module agent fills its cap** (2026-11-12, 8b-1 and 9b-1 on Opus 5.5): 37k at its first call, 124k
  after 6 minutes of reading, 180-200k after 19 minutes; neither stopped by itself at its 180k checkpoint. A message
  is read only at the agent's next tool round, and the watcher polls once a minute, so for a 200k cap start the
  watcher at 170k (`<id>=170000`), not 185k, and send the CHECKPOINT message at once. A module of about 1,500 lines
  took two agents (36 and 43 minutes in all), with nothing lost at the hand-over.
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
| medium | 200k (180k) | A module agent working alone (the default since 2026-11-10). One feature across several files or modules whose design choices must stay consistent; integrating other agents' pieces; a merge with a few conflicts. |
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
| 2026-11-11 | (7b) Orders & selection UX | coordinators medium (200k), pieces low (150k); last attempt: one module agent medium (200k), no coordinator, on Opus 5.5 | coordinator 1 cut by a usage limit (2026-10-24); coordinator 2 on Fable 5.2 cut twice (71k, 109k) and its ping sub-agent cut at 78k with no commit and no note (dead); the module agent finished at 176k in 19 minutes (80 calls) | medium: the ping piece was rebuilt from zero; the module agent was at about 100k after 30 calls of reading the notes and the existing code, so it dropped the in-game try of the keys and G20 (left in the plan). No wrong or redone work at the seams; it also skipped the interleaved perf check (done at the merge, no regression). |
| 2026-11-11 | (8a) Missing units & walls | coordinators medium (200k), pieces low (150k); from 2026-11-11 module agents medium (200k), no coordinator, on Opus 5.5, client sub-agents low (150k) | coordinators 1-3 cut by usage limits (2026-10-24, 2026-11-10); module agent 1 CHECKPOINT at 173k after 23 minutes (data, Salem, subs, carrier, walls in the sim); module agent 2 cut by a usage limit after 6 minutes, resumed in a later window, finished at 142k; its first client sub-agent read to 116k before working and stopped at 131k after the models, the second finished at 123k | low: nothing was redone. The note named three files for the replay bug, the cause was a fourth (PropStore saved a lazy alive-list cache): comparing the two states field by field found it in one run, bisecting by file would not have. Gaps in the older notes: "Atlantis dive already done" and "line placement already exists" were missing, the naval sub-agent left no note (its WIP was read from the diff). One seam bug, found by the second client sub-agent: the carrier deck spots were never used. |
| 2026-11-12 | (8b-1) Map props | module agent medium (200k), no coordinator, Opus 5.5 | 2 attempts, 0 dead: agent 1 CHECKPOINT at 186k after 19 minutes (sim part written, building, 19 tests; it stopped when the orchestrator messaged it at 178k), agent 2 finished at 172k in 17 minutes | low: the note was accurate; the full sim suite had not been run before the hand-over, so the one failing test (not the one the note named) was diagnosed from scratch; nothing broken at the seam. |
| 2026-11-12 | (8b-2) Map files, map pack, map picker | module agent medium (200k), no coordinator, Opus 5.5 | 3 attempts, 1 dead: the first start died on its first call (network error, ECONNRESET, nothing written); agent 1 CHECKPOINT at 166k after 15 minutes (sim, pack, picker done; stopped by the orchestrator's message at 166k, sent when the other agent passed 170k); agent 2 finished at 112k in 10 minutes | none: the note was complete; agent 2 found two more screens with the same gap the note named and fixed them. |
| 2026-11-12 | (9b-2) Terrain self-shadows, unit LOD, minimap options | module agent medium (200k), no coordinator, Opus 5.5 | 4 attempts, 1 dead: the first start died on its first call (network error); agent 1 CHECKPOINT at 185k after 15 minutes, before its first build (the watcher fired at 172k, the message landed one round later); agent 2 CHECKPOINT at 170k after 20 minutes; agent 3 finished at 145k in 23 minutes | low: the notes were accurate and their hook points matched the code; each successor found 2-4 facts the note lacked (shader edge cases, a test that breaks on a new settings field, a perf baseline from a non-interleaved batch, a stale main copy for perf runs). The perf problem left by agent 2 was named with its numbers, so agent 3 measured it first and fixed it inside its time box. |
| 2026-11-12 | (9b-1) Death animations + ACU warp-in | module agent medium (200k), no coordinator, Opus 5.5 | 2 attempts, 0 dead: agent 1 reached 213k in 20 minutes (30k past its checkpoint, stopped by the orchestrator's message) with most code written, never built, and its last patch saved to a file instead of applied; agent 2 finished at 164k in 23 minutes | low: the note was accurate and the pending patch applied almost verbatim (one compile error); the scene class was written from scratch; one fact was missing from the note (`wait N` in `--exec` counts frames). |
| 2026-11-12 | (8b-3) Maps in multiplayer + biomes | module agents medium (200k), no coordinator, Opus 5.5; biome-visuals sub-agents low (150k, then 120k) | 5 agents, 0 dead: agent 1 cut by a usage limit at 162k after ~28 minutes (its last edits committed unbuilt by the orchestrator); agent 2 CHECKPOINT at 171k after 14 minutes (watcher at 170k); agent 3 finished at 137k in 15 minutes (one network drop at its start, resumed); sub-agent 1 stopped at its 130k checkpoint, sub-agent 2 finished at 97k (one network drop, resumed) | low: no redone work. One seam bug: the unbuilt WIP broke a sim test that agent 2 did not run (it ran only net and its own tests); agent 3 found it in the full suite. The separable visuals piece split cleanly into "build" and "check and tune" sub-agents. |
| 2026-11-12 | (10b-1) Keybinding editor | module agent medium (200k), no coordinator, Opus 5.5 | 2 agents, 0 dead: agent 1 CHECKPOINT at 172k after 15 minutes (watcher at 170k) with the feature, tests and docs done and the sim tests unrun; agent 2 (low, 150k) ran the checks and wrote the report, 92k in 6 minutes | none: the note was enough; it lacked one command and the normal allocation numbers, so agent 2 built main to compare. |
| 2026-11-12 | (10a-1) Deterministic math across the sim | module agent medium (200k), no coordinator, Opus 5.5 | 2 agents, 0 dead: agent 1 CHECKPOINT at 175k after 21 minutes (watcher at 170k) with the math, the call sites and the protocol bump committed and the full suites unrun; agent 2 finished at 137k in 17 minutes | low: no redone work; agent 2 found one seam the note lacked (replay writes kept the old header after loading an old save) and a screenshot pitfall. |

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
  So run **at most 2 agents at a time, sub-agents counted** (owner rule since 2026-11-10; until then waves 6-8 ran
  2 coordinators plus their sub-agents), and size each brief to finish within one window. Other sessions of the
  owner use the same window: when part of it is already gone, wait for the reset.
- **On Fable 5.2 a window is about 5 times smaller** (measured 2026-11-10 from the transcripts' usage fields; all
  numbers above are for Opus 5.5). The session and its agents ran on Fable 5.2 (the owner's `/model` choice; agents
  inherit it): the 13:00 window ended after 129 calls and 10M tokens of this project (plus 2.3M of another session
  of the owner), the 18:00 window after 93 calls and 7.7M (plus 1.9M elsewhere). That was 8-10 minutes of
  2 coordinators + 2-3 sub-agents each time, and every resume first pays the re-read of each agent's context.
  Per window (`windows.py`, lines added by the work done in the window): the last five Opus 5.5 windows (Oct 23-24)
  each held 480-670 calls, about 30 $ at list price and 800-4,000 lines (2,500 on average); the two Fable 5.2 windows
  95-128 calls, about 12 $ and 200 and 830 lines. So a Fable window holds half the list-price dollars and, at twice
  the price per token, a fifth of the work (LESSONS "Costi", "Fable 5.2 costa il doppio per passo").
  Decided by the owner on 2026-11-10: agents on Opus 5.5 (`model: "opus"` in every Agent call; about 5 times more
  agent work per window, as in waves 1-7a). Fable 5.2 stays for the orchestrator and for pieces of class high or
  critical. Check again at the merge of 7b and 8a whether the work done on Fable 5.2 was much better.

**When a window resets.** Only the limit message ("resets 12am") or the owner's `/usage` tells it; `windows.py`
knows the resets of the windows that were hit, not of the current one. On 2026-11-11 the orchestrator worked out
"previous reset 18:00 + 5 h = 23:00" (first call of the project 18:09) and scheduled the next agents for 23:04 with
CronCreate; the window in fact reset at 00:00, and at 23:02 it had 1 $ left. So schedule a start for the reset time
of a limit message, or one hour later than the estimate, and let the job first make one small call: if the limit
message comes, its time is the real reset. A CronCreate job survives `/clear` (same process) but not a restart of
Claude Code, and fires only while the session is idle. Its prompt must be self-contained: a `/clear` keeps the job but drops the
transcript, so a ready agent prompt goes in a file the job names (2026-11-12: the 8b-3 prompt in the scratchpad of
session e3d118ad). A job runs on the session's model: on 2026-11-12 the 06:07 job fired in a session on Fable 5.2
whose usage had run out and did nothing, unseen until the owner came back at 10:40.
The same mistake again on 2026-11-12: the window was put at 10:00-15:00 (on the hour, as `windows.py` prints it),
but the first calls were 10:34-10:41 and it reset at 15:40; the 15:07 job passed its checks (no hit yet) and its
module agent was cut after 28 minutes. Windows do not start on the hour: the estimate is the first call after the
previous reset + 5 h, and the job's "one small call" check does not help while the old window still has some usage.

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

Since 2026-11-10 a module agent does its module itself: there is no coordinator, which cost a third of 6a, 6b and
7a. It starts a sub-agent (general-purpose, `model: "opus"`) only for separable content with a written contract
(blueprints, unit models, tests), not to split a feature into sim and client. Sub-agents count toward the limit of
2 agents at a time, so the orchestrator writes in the prompt how many the agent may run at once: 1 when it runs
alone, 0 when two module agents run. Rules for a sub-agent:
- they work in the parent's worktree (absolute path, no isolation flag), on disjoint files;
- only one `dotnet build`/`dotnet test` runs at a time per worktree: use `tools/agents/dn.sh` (see DEV_GUIDE);
- they commit only their own files: `git add <files> && git commit -m "..." -- <files>`;
- a cap sized like any agent's ("Sizing an agent's context cap", default 150k); short reports; before finishing or stopping, the essentials go to `.claude/notes/<piece>.md`.

Sub-agents cannot write outside their worktree, e.g. the orchestrator's private folder. That's why reports go to
`<worktree>/.claude/agent-report.md`.

## Review of the process rules (2026-11-10)

The owner asked whether the rules in "Owner decisions" and this way of orchestrating are right or harmful. The
answer, checked against the token ledger (numbers: [LESSONS.org](LESSONS.org) "Costi"; harm to the result: the
fragmentation table above): **the framework is right. Three choices cost throughput (work done per usage window).
None of them measurably hurt the result, except in the AI tuning of (k).** The table judges the rules as they stood
before the changes at the end of this section.

| Rule | Verdict | Evidence |
|---|---|---|
| Durable knowledge in `docs/`, handoff notes, WIP commits (2026-10-14, 2026-10-16) | Right, the most valuable one. | At least 25 session limits, one weekly limit and a 17-day account block: each time the work went on from ROADMAP "NEXT STEPS" and the notes, and no committed work was lost. |
| Context cap sized by the task (2026-10-16) | Right. | The context re-read per step went from 209k to 93k tokens and the cost of a step from 0.117 $ to 0.049 $. In the fragmentation table the harm is none or low in 7 rows of 8, and medium for the (k) stall diagnosis and for (k) as a whole. |
| Token ledger (2026-10-15) | Right. | Documentation and cost analysis are 4.5 % of the spend (32 $ of 718 $), and nothing in this review could be judged without it. |
| Agents may start sub-agents (2026-10-12) | Harmful as a general rule. | Point 1. |
| As many agents as needed, in parallel (2026-10-10) | Harmful under a subscription. | Point 2. |
| Agents inherit the session's model (not a written rule: the owner's `/model` default) | The most harmful one today. | Point 3. |

The weak points:

1. **The relay between agents spent what the cap saved.** A step got 2.4 times cheaper, but 1000 lines took 28
   steps in wave 1 and 177 in wave 6, and cost 3.3 $ and 8.6 $. Part of that is the project growing (more code to
   read, more checks); how much the relay alone weighs is not measured. What is measured:
   - resumes and successors are 28 % of the total spend;
   - a module went through 1 agent in wave 1 and through 9-11 in waves 5 and 6;
   - the coordinator is a third of the cost of 6a, 6b and 7a.

   The coordinator layer follows from two rules together: with a 150k cap a module of 2,500-3,500 lines does not
   fit in one agent. Fewer hands cost less: 7a (4 agents, 1,515 lines) 6.9 $ per 1000 lines, 6b (5 agents) 7.2 $,
   6a (13 agents) 9.6 $. Sub-agents pay off on separable content with a written contract: Cybran, 20 agents, 4.9 $.
   So the cap stands on the quality of the result and on cheaper cuts, not on savings: per line it was
   cost-neutral (wave 6 8.6 $, wave 5 8.2 $).
2. **Parallelism gains nothing under a usage limit.** A window holds a fixed amount of work ("Usage limits"), so
   more agents only end it sooner, and every agent that is cut needs a resume. Twelve agents resumed together used
   a window in 9 minutes (2026-10-15), and resumes were 57 % of the cost of wave 5. "Usage limits" already says "at
   most 2 agents at a time, without sub-agents", but waves 6-8 ran 2 coordinators plus their sub-agents (4-5 agents
   at a time on 2026-11-10): the written rule and the practice disagreed (change 3 below).
3. **The model weighs more than any rule.** On Fable 5.2 a usage window holds about a fifth of the agent work of
   an Opus 5.5 window ("Usage limits"; LESSONS "Fable 5.2 costa il doppio per passo").

Two more observations:
- **Tuning does not work as a relay.** (k) took 11 coordinators one after another and 76.6 $ (19.6 $ per 1000
  lines, the dearest module) and left its goals unmet. The raised cap of 2026-10-16 reduced the re-reading but did
  not change the outcome: the next step there would have been an AI design change (wave 9a, removed from the plan by the owner on
  2026-11-11: improving the AI is no longer a goal), then few measuring rounds that
  re-read only summaries.
- **Bookkeeping** is cheap over the whole project (the 4.5 % above), but on 2026-11-10 it was 12 $ of the day's
  39 $ (three waves of cost tables brought up to date, on Fable 5.2), against 18 $ of module work.

**Changes. The owner approved all four on 2026-11-10 ("effettua le modifiche consigliate"). They are binding: see
"Owner decisions", "Lifecycle of a wave" steps 1, 3 and 7, "Usage limits", "Sub-agents" and AGENT_RULES.** In
order of effect:
1. Agents on Opus 5.5 (`model: "opus"` in the Agent call). Fable 5.2 for the orchestrator and for pieces of class
   high or critical (diagnosis, desync hunts). Check again at the merge of 7b and 8a whether the work done on
   Fable 5.2 was much better.
2. One agent per module, no coordinator: modules of about 1,500 lines (the size of 7a), cap 200k. Sub-agents only
   for separable content with a written contract (blueprints, models, tests). The price: more and smaller merges
   for the orchestrator.
3. At most 2 agents at a time, sub-agents counted, and no start when other sessions of the owner have already
   used part of the window (as on 2026-11-10 14:08).
4. Bring LESSONS "Costi" up to date every 2-3 waves instead of after every wave (`summary.org` is always
   current). This was the weakest of the four: earlier on 2026-11-10 the owner had asked for these tables to be
   kept current, and chose this cadence with the other three.

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
- **Isolation worktrees refused (2026-11-12):** both Agent calls with `isolation: "worktree"` failed with "git could
  not be run to resolve it": Claude Code creates the worktree and then runs git in it, which git refused for the
  same ownership reason, before any agent could add its entry. Fixed for good with one prefix entry (git 2.46+):
  `git config --global --add safe.directory '/run/media/marco/RTS/.claude/worktrees/*'`. The two empty worktrees
  of the failed calls stay behind, locked: `git worktree unlock`, `git worktree remove`, `git branch -d`.
