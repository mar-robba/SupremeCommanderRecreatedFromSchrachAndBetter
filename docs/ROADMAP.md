# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## Waves at a glance
A **wave** is a batch of modules built by agents in parallel and merged before the next one (WORKFLOW "Waves").
The **Phase 0-4** sections further down are not waves: they are the feature chapters of the first plan, and each
feature there names the wave that delivered it. Update this table at every merge (WORKFLOW "Lifecycle of a wave"
step 7). Briefs and reports of every module: [HISTORY.md](HISTORY.md).

| Wave | Modules | State |
|---|---|---|
| Foundation | Solution, deterministic sim core, threaded runner, blueprints, map generator, orders, movement, basic client | ✅ 2026-10-10 |
| 1 | Combat, economy & construction, pathfinding & movement, client visuals, UI | ✅ 2026-10-10 |
| 2 ("Phase 2 work", never numbered at the time) | Fog of war & radar, first skirmish AI, map presets, menus / setup / end screens, audio | ✅ 2026-10-11 |
| 3 | (a) T2/T3 tech + upgrades, (b) shields & strategic weapons, (c) naval, (d) skirmish AI, (e) sandbox | ✅ 2026-10-12 |
| 4 | (d) AI finish, (f) SupCom polish, (g) experimentals + commander enhancements + SCUs, (h) transports, (i) save/load + replays | ✅ 2026-10-14 |
| 5 | (j) faction infrastructure, (m) scale/perf, (n) UX polish, (o) lockstep multiplayer, (l) Cybran faction, (k) AI uses everything + (k-navy) | ✅ 2026-10-16 → 10-24 |
| 6 | (6a) commander & engineers, (6b) game options & team play | ✅ 2026-10-24 |
| 7 | (7a) release package + settings + autosave, (7b) orders & selection UX | ✅ 2026-10-24 (7a), 2026-11-11 (7b) |
| 8 | (8a) missing units & walls, (8b-1) map props, (8b-2) map files + map pack + map picker, (8b-3) maps in multiplayer + biomes | ✅ 2026-11-11 (8a), 2026-11-12 (8b) |
| 9 | (9b-1) death animations + ACU warp-in, (9b-2) terrain self-shadows + unit LOD + minimap options; (9a) AI tuning removed by the owner 2026-11-11 | ✅ 2026-11-12 |
| 10 | (10a-1) deterministic math ✅, (10b-1) keybinding editor ✅, (10b-2) selection overlays + client leftovers ✅, (10a-2) observers + vote to drop + lobby pings + chat sounds ✅ (all 2026-11-12), (10a-3) multiplayer save/load ✅ 2026-11-13 | ✅ 2026-11-13 |
| 11 | Planned after the owner's answers of 2026-11-13 ("Questions for the owner"): pathing (G12) + save migration (G41) as one pair, voice announcer (G24); modules (11a-1) pathing, (11a-2) save migration, (11b-1) voice announcer ✅ 2026-11-13, briefs in HISTORY "Wave 11". Cut: campaign, map editor, Italian localization, online outside the LAN. Parked: tutorial, mod support, FA units, split screen, reconnect | in progress |

Where each wave is written: waves 6-11 with their gaps and "later" items in "Wave 6+ plan" → "Proposed waves" below;
what waves 1-5 delivered, feature by feature, in the Phase sections.

## ▶ NEXT STEPS (updated 2026-11-12 22:00 — read this first in a new session)
main has waves 1-9: (j) factions, (l) Cybran, (m) scale/perf, (n) UX polish, (o) lockstep MP, (k) AI
uses everything + (k-navy), (6a) commander & engineers, (6b) game options & team play, (7a) release + settings +
autosave (all merged by 2026-10-24), (7b) orders & selection UX and (8a) missing units & walls (both merged
2026-11-11), (8b-1) map props: trees and rocks, (9b-1) death animations + ACU warp-in, (8b-2) map files + map
pack + map picker, (9b-2) terrain self-shadows + unit LOD + minimap options and (8b-3) maps in multiplayer +
biomes (all five merged 2026-11-12). **Waves 8 and 9 are complete** (9a was removed by the owner). Wave 10:
(10b-1) keybinding editor, (10a-1) deterministic math, (10b-2) selection overlays and (10a-2) observers + vote to
drop merged 2026-11-12, (10a-3) multiplayer save/load merged 2026-11-13: **wave 10 is complete**. Wave 11: (11b-1)
voice announcer merged 2026-11-13; left: (11a-1) pathing, (11a-2) save migration.
Tests: 615 sim + 371 client + 43 net.
Reference match `Rts.Bench -- ai 30 256 42 hard,normal` on main = "WINNER: Player (Hard) after 12:00", hash
**C87C46C4EA726A82** (agents that must not change sim results compare against it; before 10a-1, which made the sim
math software-deterministic, it was "after 12:18", FB269C29202773C1; before 8b-1 "after 15:35", BBD1FF3C4E116A4A).
Bench `2000 300` hash **2F09CD7D01AF287E** (6A8312040EFA67F2 before 10a-1). Replays made before 10a-1 and saves on
generated maps made before 10a-1 refuse to open with a clear message (`GameInfo.MathVersion`); `Msg.ProtocolVersion` 6 (10a-3; 5 = 10a-2).
Each agent keeps `<worktree>/.claude/handoff.md` and
writes `<worktree>/.claude/agent-report.md` when done (never `git add -f` them: `.claude/` is ignored on purpose).
Briefs and reports: HISTORY "Wave 7", "Wave 8", "Wave 9", "Wave 10". Every prompt carries a `CTX-TAG:` line (AGENT_RULES).

**ON RESUME** (2026-11-13 04:15, written by orchestrator session 5473885b on Fable 5.2, at 250k of context, for the next session):
- **(8b-3) is merged and verified** (e4371c2, 2026-11-12; HISTORY report). Waves 8 and 9 are complete.
- **Wave 10 is re-cut into five modules** (HISTORY "Wave 10": briefs, order and pairs; purposes and waves are in
  `purposes.json`): pair 1 = **10a-1** deterministic math (G34, changes the reference hashes) + **10b-1** keybinding
  editor (G19, client only); pair 2 = **10a-2** observers + vote to drop + lobby pings + chat sounds + **10b-2**
  selection overlays + client leftovers (G20, G11 client items); then **10a-3** MP save/load (G36) with a wave 11
  module. One module agent each (`model: "opus"`, `isolation: "worktree"`, cap 200k, no sub-agents while two run).
- **Pair 1 is merged and verified**: (10b-1) keybinding editor (2af6abf) and (10a-1) deterministic math (f3a50a6),
  2026-11-12 (HISTORY reports). Both modules took two agents each (checkpoint at 170-175k after 15-21 minutes, then a
  fresh agent). Pair 1 ran in the half-used 15:40 window at the owner's request; that window was at ~30 $ at 18:50.
- **Answered 2026-11-12 20:53** (owner's question on LESSONS.org): yes, its error entries stopped at 2026-10-14;
  no decision stopped them (the 2026-11-10 cadence change was only for "Costi"), the lifecycle simply had no step for
  them. Seven missing entries written (2026-10-16 → 11-12), the Hard-preset entry corrected (it was an exploit), and
  WORKFLOW "Lifecycle of a wave" step 7 now adds a LESSONS entry at every merge that shows a design error.
  `LESSONS.pdf`/`.tex` are the owner's Emacs export of 2026-10-15 (stale).
- **Pair 2 is merged and verified** (2026-11-12): (10b-2) selection overlays 57d4e20 and (10a-2) observers + vote
  to drop + lobby pings + chat sounds e9e8f8d (HISTORY reports, design errors in LESSONS, worktrees and branches
  removed). Each needed a third agent on Opus 5.5 (14 and 18 minutes, started 21:12).
- **Usage: the owner says it is fine** ("con gli usi siamo a posto", 2026-11-12 ~21:55, after the weekly-limit
  message of 21:02 that cut both agents; Opus 5.5 calls worked again from 21:08). Don't ask again: go on with the
  next module. The 5-hour window began about 20:45 and has had about 50 agent-minutes (next fresh window about
  01:45 on 2026-11-13, an estimate); if an agent is cut, the usual resume applies (WIP commit, fresh agent).
- **(11b-1) voice announcer is merged and verified** (556afff, 2026-11-13 04:03; HISTORY report, two design errors
  in LESSONS, worktree and branch removed): one agent, 16 minutes. 19 Italian lines spoken by `espeak`, generated by
  `tools/voice/generate.py` from `data/audio/voice/lines.json`. **Nobody has heard it yet**: the owner judges it with
  `dotnet run --project src/Rts.Client -- --voice-test` (told 2026-11-13 04:00); a wording or the voice can be
  changed and regenerated (DEV_GUIDE "Voice announcer").
- **(10a-3) multiplayer save/load is merged and verified** (7a84828, 2026-11-13 04:11; HISTORY report, four design
  errors in LESSONS, worktree and branch removed): four agents (one cut by the 5-hour limit of 2026-11-12 22:01
  after 2 minutes), 2,141 lines. Left open by its report (ROADMAP 10a-3 "later" lines): the map panel and the game
  options look editable in a load lobby (the net layer ignores the changes); a client joining a load lobby was
  tested only headless; saves on a map file, a save after a resign and the MP autosave have no test.
- **Next: the wave 11 pair, (11a-1) pathing + (11a-2) save migration** (briefs in HISTORY "Wave 11", purposes in
  `purposes.json`; both sim-side with disjoint files: `Pathfinding` + `MovementSystem` against `Persistence`).
  11a-1 changes the reference hashes and the golden values of `CrossOsDeterminismTests`; 11a-2 must change none: merge
  11a-2 first, or check the second one against the first one's new values. One module agent each: `model: "opus"`,
  `isolation: "worktree"`, cap 200k, CHECKPOINT 170k, no sub-agents while two run, rules block from AGENT_RULES, a
  `CTX-TAG:` line. The prompts of this session are the model (scratchpad of session 5473885b, `prompt-10a3.md`; lost
  at a reboot): brief, state with file sizes, design guidance, ordered TODO, rules. Watcher:
  `python3 -I tools/agents/agent_ctx.py --watch 999999 <id1>=162000 <id2>=162000` in the background; stop it with
  `for p in $(pgrep -f "agent_ctx.py --watc[h]"); do kill $p; done` (the `[h]` keeps the shell's own command line
  out of the match; `pkill -f` with the plain text kills the shell that runs it).
  **What worked on 2026-11-13:** an agent at 162-166k whose tree is clean and whose docs are committed is not told
  to CHECKPOINT but "run only the final checks and write the report, no more reading, stop before 185k, CHECKPOINT
  if a check fails for a cause that is not obvious": 11b-1 and 10a-3 both finished that way at 172k, without a
  successor. An agent at 165k with features still to write gets CHECKPOINT at once. Agents now reach 165k in 10-11
  minutes (they read about 100k in the first 3).
  Merge checks: `bash tools/agents/merge-check.sh <log> <screenshot args>`; for `Rts.Net` or save work also
  `Rts.Bench -- mp 3 600 256 1 0 save=300` and `... obs=1 save=250` (the script of this session,
  `check-10a3.sh` in its scratchpad, chains them). A successor that only runs the final checks is a trivial task:
  `model: "sonnet"`.
- **Usage on 2026-11-13:** the 5-hour window after the 02:00 reset began with the first call of another session of
  the owner (ed4bc96a, the one that recorded the answers; some time before 03:36). By 04:12 it held 43 agent-minutes
  on Opus 5.5 (three module agents) plus this orchestrator on Fable 5.2 at 150-250k of context, with no limit
  message. A fresh window begins between about 07:00 and 08:40 (estimate; only a limit message tells). The owner said
  "continua" at 03:36 with the window already begun; start the next pair when the owner says so or in a fresh window.
- **Three sessions of the owner were open on this project on 2026-11-13** (terminals pts/0 = the orchestrator, pts/1
  = session 3fb9c6bc waiting on a dialog since 2026-11-12, pts/5 = ed4bc96a, docs). Only one may orchestrate: check
  `~/.claude/sessions/*.json` (status, `updatedAt`) and `git status` for uncommitted edits of another session
  before editing ROADMAP or WORKFLOW (on 2026-11-13 the owner's answers were uncommitted edits of ed4bc96a; the
  orchestrator committed them with its own, 05331d0).
- **Owner, 2026-11-12 (session 4ebba5f4):** trivial agent tasks on Sonnet 5.5, effort by the task's complexity
  (WORKFLOW "Owner decisions"). The owner asked why ROADMAP shows no waves after 4, meaning: where is the plan of
  the whole project (answer: "Wave 6+ plan" below). The orchestrator added the "Waves at a glance" table above and
  made the wave lines headings without being asked (5e8a06d, 8013ae2); the owner said nothing had to change, and
  to the offer of a revert answered **"non toccare il roadmap"**: the file stays as it is now. Keep the table's
  wave 10 row and these NEXT STEPS lines current, and change nothing else of this file's structure unless the
  owner asks. **Don't edit docs in answer to a question.**
- **Stale, seen 2026-11-12 and left as it is** (the owner wants no unasked ROADMAP changes; ask first): gap table
  rows without `[x]` although delivered: G1-G3 and G7 (6a), G10, G26, G27 (8b).
- **Start a module only in a fresh usage window, and not on the hour:** the window reset at 15:40 on 2026-11-12,
  not 15:00 (WORKFLOW "When a window resets"); the 15:07 start was cut after 28 minutes. 8b-3 then used about
  8 $ of the 15:40 window by 16:20 (5 agents).
- **Lava biome follow-ups** (HISTORY 8b-3 "Known issues"): warm/grey units up close, ash-crown trees read as grey
  mushrooms from above; pack maps are all temperate (`mapexport --biome` can set them without changing hashes).
- **Watch the agents' context from the start:** module agents reach 200k in about 19 minutes and do not stop by
  themselves (WORKFLOW "Context limits and handoffs"): watcher at 170k, CHECKPOINT message at once, then a fresh
  agent in the same worktree.
- If an agent is cut off (usage limit or restart): commit WIP in its worktree, then a fresh agent there
  (`model: "opus"`, no coordinator) with its brief (HISTORY) + "continue from `.claude/handoff.md` and
  `.claude/notes/`" (the prompts of 2026-11-11 are the model: brief, state, ordered TODO, rules block). Don't resume
  by SendMessage an agent above 100k. Context watcher: `agent_ctx.py --watch 135000 <id>=185000 ...` (stop it by
  its process id: `pkill -f` with that text also kills the shell that runs the command).
- **Process rules changed by the owner (2026-11-10; WORKFLOW "Owner decisions" and "Review of the process
  rules"):** every Agent call carries `model: "opus"`; one agent per module of about 1,500 lines, cap 200k, no
  coordinator (sub-agents only for separable content: blueprints, models, tests); at most 2 agents at a time,
  sub-agents counted, and no start when other sessions of the owner have used part of the window; LESSONS "Costi"
  every 2-3 waves (next: after wave 11). Briefs written before this (8b and wave 9 in HISTORY) must be re-cut to
  that module size before they start.
- **Owner to decide:** install the packaged game + desktop shortcut (7a, `tools/package/`; the tar.gz must be unpacked
  on the home disk: the data partition has no exec bits).
- **After each merge:** report into HISTORY, a fragmentation row in WORKFLOW, the module's merged lines into
  `tools/token-ledger/purposes.json` `righe` + `ledger.py update` (WORKFLOW "Lifecycle of a wave" step 7). The
  "Costi" chapter of LESSONS was re-aligned after wave 9 (e43f997) and is next due after wave 11 (cost per wave so
  far: LESSONS "Per onda").
- **Owner questions answered 2026-11-13** (asked 2026-10-23 and 2026-10-24; "Questions for the owner" in the Wave 6+
  plan). Answers and the open ones are there; recorded in WORKFLOW "Owner decisions". Voice (Q7): answered later the same day,
  Italian, made with `espeak`. Still open: split screen (Q9; explained to the owner on 2026-11-13, no answer yet),
  reconnect (G37, not answered).
- **Improving the AI is no longer a goal of the project (owner, 2026-11-11):** the skirmish AI stays as merged in
  wave 5 ((k) + (k-navy)); its unmet goals (stalls, finished games, tech v T1-only) stay unmet. Removed from the
  plan: wave 9a, gap rows G30-G33 and every "later: AI ..." line below. The AI docs stay as they are (ARCHITECTURE
  "Skirmish AI", AI_TUNING). Plan no AI work and write no "later: AI uses X" lines for new features: new units and
  mechanics stay things the AI ignores. Still required: the AI must not break (AI tests green, the reference match
  `Rts.Bench -- ai 30 256 42 hard,normal` as the regression check of every merge).
- Worktrees: git trusts every folder under `.claude/worktrees/` since 2026-11-12 (one `safe.directory` entry with `/*`, WORKFLOW "Incidents"); a worktree elsewhere still needs its own entry.
  History of usage limits: weekly 2026-10-14 08:14 and 2026-11-12 ~21:05 (reset 2026-11-17 15:00, after ~17 min of pair 2); sessions
  2026-11-12 22:01 (reset 02:00; the window began about 20:45 and held about 65 agent-minutes on Opus 5.5 plus two orchestrator sessions on Fable 5.2), 2026-10-15 11:21 and 15:09, 2026-10-16 11:43 and 16:26,
  2026-10-22 ~23:00, 2026-10-23 14:05 and 20:14, 2026-10-24 ~01:53 (reset 05:30) and ~06:30 (reset 10:30; 6b and 7a resumed by SendMessage).
  2026-10-24 11:25 (reset 15:30; 7b and 8a cut; at 16:35 the account answered "subscription access disabled", work resumed 2026-11-10).
  2026-11-10 14:08 (reset 18:00): both coordinators and their two sub-agents cut after 10 minutes of work; other
  Claude sessions of the owner were open on the same account, so the window was probably nearly used up already.
- When an agent finishes (report + clean tree): merge it into main (`git merge --no-ff <branch>`), build, both tests,
  a screenshot, the AI match above (same hash unless the agent documents why it changes: (k) and (l) will change it),
  then `git worktree unlock` + `git worktree remove` + `git branch -d`, report → HISTORY.
- If an agent got cut off by a usage limit: commit its WIP in its worktree, then start a fresh agent in the same
  worktree (absolute path, no isolation) with its brief + "continue from .claude/handoff.md".
- Owner question (2026-10-15) "would Rust have been better than .NET?": answered, stay on .NET (WORKFLOW "Owner decisions").
- Housekeeping: a stray `.gitignore` rewrite patch is in `orchestrator/stray-main-gitignore.patch`.

**Next waves (owner: "everything must be done"; 2026-10-23: two factions, UEF + Cybran, are enough, no further factions):**
- **Wave 5** (after f + j are merged), 5 parallel agents:
  1. ✅ (merged 2026-10-24, with k-navy) AI uses everything: T2/T3 upgrades, adjacency, enhancements, navy (Archipelago), transports, shields/TML/nukes/
     anti-nuke, experimentals. Faction-agnostic via roles. Balance pass.
  2. ✅ (merged 2026-10-23) Cybran faction (`data/factions/cybran.json`): full T1–T3, Monkeylord, Soul Ripper, Scathis; stealth/cloak, EMP,
     amphibious walkers; distinct models.
  3. ✅ (merged 2026-10-16) Scale/perf: 1024 maps with 4–8 players, 5k+ units at 60 FPS (LOD, instancing, intel cost, async nav build).
  4. ✅ (merged 2026-10-16) UX polish: the follow-ups above; fire states (hold fire / return fire); Load in the pause menu; viewer F7; save
     migrations.
  5. ✅ (merged 2026-10-16) Lockstep multiplayer: LAN/Internet lobby on top of the replay command log and desync hashes.
- **Wave 6+**: gap table, waves 6-10 (2 agents each) and owner questions in "Wave 6+ plan (audit 2026-10-23)" below.

Shared contracts (still valid):
- `BuiltBy`: a blueprint adds itself to builders' lists; unknown ids are ignored.
- `UpgradesTo`: in-place upgrade.
- Enums are append-only; edits to shared files stay minimal and additive.
- New models go in new partial files, with only a `case` line in UnitModels.cs.
- Sim state must stay serializable for agent (i): plain fields/arrays/lists, no stored delegates, no static mutable state.

## Wave 6+ plan (audit 2026-10-23)
Audit of main (bfbb5f4) against Supreme Commander 1 (vanilla; FA = Forged Alliance only), for the two factions the
owner wants (UEF + Cybran). Not listed: what (k) "AI uses everything" and (k-navy) deliver. Checked by grep: rosters
(158 blueprints), hotkey table, setup/lobby/settings screens, victory code, alerts, save paths. Already there (no gap):
teams + shared team vision, team victory, per-army unit cap in the sim (1000, not shown or settable), terrain line of
fire (the Phase 1 "terrain blocking direct fire" later line is stale), reclaim of living units, strategic zoom, chat.
Size: S < 1k lines, M ~1-3k (one agent-window), L = several windows. Prio: 1 = needed for a complete SupCom1
experience, 2 = part of SupCom1 but the game is playable without it, 3 = nice to have.

| # | Gap | SC1/FA | Size | Area / files | Depends on | Prio |
|---|---|---|---|---|---|---|
| G1 | ACU **Overcharge** (energy-fed shot, damage from stored energy; order button + key) | SC1 | M | `Rts.Sim/Combat`, `Commander`, commander blueprints, OrdersPanel | — | 1 |
| G2 | **Commander death explosion** (nuke-like blast, hurts everyone near) | SC1 | S | `Commander` (death hook), Experimental death blast, NukeFx | — | 1 |
| G3 | **Engineer patrol automation**: patrolling engineers/ACU/SCU reclaim wrecks, repair, assist builds in range | SC1 | S-M | `Economy/ConstructionSystem`, OrderSystem Patrol case | — | 1 |
| G4 | [x] 2026-10-24 (6b) **Game options**: victory assassination / supremacy / annihilation / sandbox; unit cap 250-1000 (+ HUD "units 123/500"); start resources; fog explored/none; game speed normal/fast/adjustable; cheats; prebuilt units | SC1 | M | GameSetup, CleanupSystem, Army; SkirmishSetupScreen, LobbyScreen, NetGameConfig, `Rts.Net/Lobby` codec; Hud | — | 1 |
| G5 | [x] 2026-10-24 (6b) **Team play**: give units and mass/energy to allies, allied overflow sharing, allies-only chat | SC1 | M | UnitTransfer, new `Economy/EconomyShare.cs`, new `Orders/DiplomacyCommands.cs`, new `UI/DiplomacyPanel` | G4 | 1 |
| G6 | [x] 2026-10-24 (6b) Score history (income, units, kills every 30 s) + end-screen graphs, score column in the players panel | SC1 | S | ArmyStats (saveable), EndScreen | G4 | 3 |
| G7 | Missing enhancements: Personal Teleporter (both ACUs), UEF Engineering Drones (C-D1/C-D2), UEF SCU Radar Jammer | SC1 | M | `Commander`, blueprints, client models/fx | — | 2 |
| G8 | [x] 2026-11-11 (8a) Missing units: UEF Mech Marine (T1 bot); T3 strategic missile subs (UEF Ace, Cybran; nuke + TML from a submerged hull); Cybran T3 aircraft carrier; T3 sonar platforms; UEF T2 stealth field generator (verify vanilla); Salem destroyer walks on land | SC1 | M | `data/blueprints`, faction json (optional roles), `Naval`, `Strategic` (mobile silo), `Transport` (carrier), models | — | 2 |
| G9 | [x] 2026-11-11 (8a) **Walls** (T1 wall sections, both factions: block movement and shots, Shift-drag lines) | SC1 | S-M | data, Pathfinding blocked cells, Combat projectile hits | — | 2 |
| G10 | Map props: trees (energy) and rocks (mass) to reclaim; trees fall / burn | SC1 | M | PropStore kinds, `Map/` generator, PropRenderer | — | 2 |
| G11 | Leftovers: swap an occupied enhancement slot in one click, mobile TML moves into range, shield HP kept on enhancement swap, Mavor arc/priority, crush animation, emergency drop, staging repair cost, fog wrecks/deposits, sandbox selects enemies | SC1 | M (many S) | various | — | 3 |
| G12 | Pathing: exact unit clearance, per-unit MaxSlope, ships steering in narrow channels | SC1 | M | Pathfinding, MovementSystem | — | 3 |
| G13 | FA-only units for UEF/Cybran (Broadsword, Wailer, Continental, Novax, Megalith, Brick, Hoplite, Kennel/Hive); some FA units exist (Percival, Janus, Ravager) | FA | L | data + models | owner Q8 | 3 |
| G14 | [x] 2026-11-11 (7b) Selection/camera keys: all of a type on the map, all land / air / naval / engineers, ACU (select + center), camera bookmarks, jump to last alert | SC1 | S | Hotkeys (new entries), SelectionController, new `Input/SelectionFilters.cs` | — | 2 |
| G15 | [x] 2026-11-11 (7b) Formation move with facing (right-drag), waypoint editing (drag / delete queued order markers), ferry route editing | SC1 | M | Input, new `Orders/QueueEditCommands.cs` (+ one CommandSystem line) | — | 2 |
| G16 | [x] 2026-11-11 (7b) Notifications: minimap flash where attacked, structure lost, construction/upgrade done, unit cap reached, ally under attack | SC1 | S | GameAudio alerts, Minimap, Toasts | — | 2 |
| G17 | [x] 2026-11-11 (7b) Map pings / markers (alert, move, attack; map + minimap; allies and MP; in replays) | SC1 | M | new `UI/Pings`, sim command (additive) | — | 2 |
| G18 | [x] 2026-10-24 (7a) Settings: graphics (shadows, particles, terrain LOD, vsync, fps cap, UI scale), gameplay (edge scroll, camera/zoom speed); Settings from the pause menu (today: fullscreen, resolution, 3 volumes) | SC1 | M | SettingsScreen, AppSettings, renderer flags, InGameScreen | — | 2 |
| G19 | [x] 2026-11-12 (10b-1) Keybinding editor (rebind any Hotkeys entry, reuse the clash check, saved) | SC1 | M | Hotkeys.cs (overrides), new `Menus/KeyBindingsScreen` | G14, G18 | 2 |
| G20 | [x] 2026-11-12 (10b-2) Range/intel overlays for the selection (weapon, radar, shield; today only while placing) | SC1 | S | Rendering overlay | — | 3 |
| G21 | Split screen (two views, one strategic) / second monitor: parked, the owner asked what it means (Q9) | SC1 | L | GameSession, RtsCamera, FrameView, renderers | owner Q9 | parked |
| G22 | [x] 2026-11-12 (9b-2) Minimap options (resize, zoom, hide/show resources and units) | SC1 | S | Minimap | — | 3 |
| G23 | [x] 2026-11-12 (9b-1, 9b-2) Visuals: terrain self-shadows, low-poly unit LOD, death animations (aircraft spiral down, ships list and sink, structures collapse), ACU warp-in at start | SC1 | M | Rendering (+ effect event if needed) | — | 2 |
| G24 | [x] 2026-11-13 (11b-1) Voice announcer ("Comandante sotto attacco", "Lancio nucleare rilevato", 19 lines): Italian voice made with `espeak` (owner, 2026-11-13), files in `data/audio/voice/it` | SC1 | M | Audio + voice assets | owner Q7 | 11 |
| G25 | cut 2026-11-13 (owner Q4: no Italian localization, so no accent font) | — | S | — | — | — |
| G26 | Map files + curated map pack: save/load maps (heightmap, water, deposits, starts, props), 8-12 named maps with thumbnails, map picker in setup/lobby (host sends the map) | SC1 | M-L | `Rts.Sim/Map`, Menus setup/lobby, `Rts.Net` transfer | G10 | 2 |
| G27 | Terrain biomes (temperate, desert, tundra, lava, tropical) | SC1 | S-M | TerrainRenderer palettes + generator option | — | 3 |
| G28 | cut 2026-11-13 (owner Q4: no Italian localization) | — | — | — | owner Q4 | — |
| G29 | cut 2026-11-13 (owner Q2: no map editor; map files + pack + seeds cover variety) | — | — | — | owner Q2 | — |
| G30-G33 | removed from the plan 2026-11-11 (owner: improving the AI is no longer a goal; the AI of wave 5 stays). They were: AI personalities, AI team play, AI use of the new mechanics, AI think cost on 1024 | — | — | — | — | — |
| G34 | [x] 2026-11-12 (10a-1) Cross-OS determinism: software trig/exp (`DMath`) in all sim code + golden-value tests (Linux↔Windows games) | — | M | `Rts.Sim` wide (mechanical) | — | 2 (1 if Windows players) |
| G35 | [x] 2026-11-12 (10a-2) Observers (lobby slot, no fog, sends nothing) | SC1 | S | `Rts.Net/Lobby`, Net menus | — | 2 |
| G36 | [x] 2026-11-13 (10a-3) Multiplayer save/load (host saves, all reload with the same lobby) | SC1 | M | `Rts.Net`, Persistence, Net menus | — | 2 |
| G37 | Reconnect to a running game (host streams a save + command tail) | FAF | M | `Rts.Net` | G36 | 3 |
| G38 | cut 2026-11-13 (owner Q6: online only on the LAN; NAT traversal and relay server out of the plan) | SC1 | — | — | owner Q6 | — |
| G39 | [x] 2026-11-12 (10a-2) Vote to drop, per-player lobby pings, chat sounds (host migration: later, below) | — | S-M | `Rts.Net`, Net menus | — | 3 |
| G40 | [x] 2026-10-24 (7a) Autosave (every 5 min, rolling 3) | SC1 | S | client Persistence | — | 2 |
| G41 | Saves survive sim layout changes (field-level migration) | — | M | Persistence | — | 3 |
| G42 | Interactive tutorial (on a small scripted-scenario system: triggers, objectives, messages): parked, not answered (owner Q1) | SC1 | M | new `Rts.Sim/Scenario`, client | owner Q1 | parked |
| G43 | cut 2026-11-13 (owner Q1: no campaign / story) | SC1 | — | — | — | — |
| G44 | Mod support (`mods/<name>/` blueprint/faction overlays, enable list, MP mod hash): parked, estimate S-M (owner Q3 asked how hard it is) | SC1 | S-M | BlueprintDb loader, menus, lobby | G45 | parked |
| G45 | [x] 2026-10-24 (7a) **Release packaging**: self-contained Release build (linux-x64; win-x64 zip optional), data next to the exe, saves/replays/settings/screenshots in the user folder (today `saves/` = current dir), icon, `.desktop` launcher + desktop shortcut, version in the main menu, crash log | — | S-M | new `tools/package/`, csproj, SaveStore dirs, Program.cs | — | 1 |
- [ ] later (7b): G20 range/intel overlays for the selection (done in 10b-2); bookmarks are per session (not saved) with
  no on-screen list; class filters (Ctrl+Shift+L/A/N/E) have no "add" variant; Ctrl+A/Z/C, Alt+1-5, Backspace, Y and
  Alt+click pings were tested by unit tests and screenshot hooks only, not by hand in a game window (a desktop may grab
  Alt+digit / Alt+click); "structure lost" also fires on self-destruct (fixed in 10b-2); wall-sized structures toast "×N".
- [ ] later (8a): wall sections are separate 1×1 blocks (no joined corner/segment meshes); walls stop only direct
  shots of ENEMY armies and only up to their box height (arcing shells and tall far shots pass); the Salem's legs are
  client-only (no gait sound, far lower legs look like masts in steep top views); Shift-drag wall lines were checked
  in code, not by hand in a game window.

### Proposed waves (2 agents each, disjoint files, one usage window each; start after (k) and (k-navy) merge)
Free keys today: O, I, J, W, F2-F4, F11, F12 (`HotkeyTableTests`). Each agent: AGENT_RULES block, cap 150k.
Agents that change sim results document the new reference hash of `Rts.Bench -- ai 30 256 42 hard,normal`.

#### Wave 6: core SupCom mechanics (P1) ✅
- 6a Commander & engineers (G1, G2, G3; G7 if time). Overcharge (energy cost, damage from storage, ACU only, key O,
  order button, fx/sound), commander death blast (hook in `Commander/`, NOT CleanupSystem: 6b owns it), patrol
  automation in a new `Economy/EngineerPatrol.cs` (+ the Patrol case), then teleporter + UEF drones + SCU jammer. Owns
  `Rts.Sim/Commander`, `Combat` (new OverchargeSystem file), ConstructionSystem, commander/SCU blueprints, client
  order button + model partials. Tests: OC damage/energy, blast radius, patrol reclaim/repair/assist, teleport, save
  coverage. Screenshots: OC shot, death blast, teleport.
- 6b Game options & team play (G4, G5; G6 if time). Victory modes, unit cap option + HUD counter + "cap reached",
  start resources, fog, speed lock, cheats, prebuilt units in setup AND lobby (lobby codec in `Rts.Net`); give
  units/resources, allied overflow, allies-only chat, diplomacy panel; score graphs. Owns GameSetup, CleanupSystem,
  Army (append), UnitTransfer, new EconomyShare/DiplomacyCommands, setup/lobby screens, NetGameConfig, Hud,
  EndScreen. Tests: each victory mode, cap, transfers, overflow, codec round trip, MP loopback with options.

#### Wave 7: something the owner can launch + everyday UX ✅
- 7a Release, settings, autosave (G45, G18, G40). `tools/package/` script (dotnet publish Release self-contained,
  data copied, icon, `.desktop` + desktop shortcut, Windows zip optional), user-folder paths for saves/replays/
  settings/screenshots, version label, crash log; graphics + gameplay settings and Settings in the pause menu;
  autosave. Owns tools/package, csproj, SaveStore dirs, Program.cs, SettingsScreen, AppSettings, InGameScreen, menus.
  Tests: paths + settings round trip; launch the packaged build from the shortcut (screenshot); DEV_GUIDE section.
- 7b Orders & selection UX (G14, G15, G16, G17; G20 if time). Selection filters, ACU key, camera bookmarks, jump to
  alert; right-drag formation facing; drag/delete waypoints, ferry route editing; notification pass with minimap
  flashes; pings (sim command, replays + MP). Owns `Input/`, Hotkeys (new entries), SelectionController, Minimap,
  GameAudio alerts, new overlay, new sim `Orders/QueueEditCommands.cs` + ping command (additive). Tests: hotkey
  table, queue-edit + ping sim tests, ping in replay/MP loopback. Screenshots.

#### Wave 8: content (P2) ✅
- 8a Missing units & walls (G8, G9; Atlantis dive from G11). Mech Marine, both strategic missile subs (submerged
  launch), Cybran carrier (reuse the Atlantis cargo/factory), T3 sonar platforms, UEF stealth field generator, Salem
  on land, wall sections. Owns `data/blueprints`, faction json (optional roles: AI ignores what it doesn't know),
  `Naval`, `Strategic`, `Transport`, Pathfinding blocked cells, model partials + `case` lines. Tests: roster tests,
  submerged launch, walls block paths/shots, carrier; `--scene` group; screenshots.
- 8b Maps (G10, G26, G27; re-cut 2026-11-11 into the modules 8b-1, 8b-2, 8b-3: HISTORY "(8b) Maps"). Trees/rocks props (reclaim, fall/burn), map file format + 8-12 curated named maps
  with thumbnails, map picker in setup/lobby, host sends the map in MP; biomes. Owns PropStore (additive kinds),
  `Map/` (new MapFile, props placement), PropRenderer, TerrainRenderer palettes, map picker UI, `Rts.Net` map
  transfer. Tests: file round trip, props reclaim, MP map hash, MapScaleTests; screenshots per biome.

#### Wave 9: visual polish ✅
- 9a AI: removed from the plan (owner, 2026-11-11: improving the AI is no longer a goal).
- 9b Visual & audio polish (G23, G22; G24/G25 if the owner says yes; re-cut 2026-11-11 into 9b-1 and 9b-2:
  HISTORY "Wave 9"). Terrain shadows, unit LOD, death animations,
  ACU warp-in, minimap options. Owns `Rendering/`, `Audio/`, Minimap. Perf: `--perf-log` before/after; screenshots.
  - [x] 9b-1 Death animations + ACU warp-in (2026-11-12): aircraft spiral down and crash on their wreck, ships list
    and sink, structures collapse, land experimentals and commanders get longer sequences, ACU warp-in at game start
    (flash, rings, hologram rise, sound); client only, hashes unchanged; `--scene deaths`, `DeathAnimTests`.
  - [ ] later: a sound/particle for wrecks settling after a ship sinks in shallow water; death sequences for
    units dying inside transports; per-model break-apart (parts flying off).
  - [x] 9b-2 Terrain self-shadows, unit LOD, minimap options (2026-11-12; ARCHITECTURE "Terrain self-shadows, unit
    LOD, minimap options"): shadows baked from the heightmap with the unit shadow map's sun (ground in the terrain
    vertex colours, units/aircraft by height), low-poly far units by vertex clustering (tank 296 → 82 tris), minimap
    size 75-175 %, wheel zoom 1-8×, resources/units/trees layers and hover buttons; Graphics rows + a "Minimap"
    settings tab; `--terrain-shadows`, `--unit-lod`, `--sun-elevation`, `--minimap`; client only, hashes unchanged.
  - [ ] later: the sun is fixed (a moving or per-map sun needs a re-bake: ~30 ms on 1024 maps in Release); trees, rocks and
    wrecks are not darkened by terrain shadows (props have their own shader); LOD meshes are built lazily on a
    model's first far frame (a small hitch the first time); a hotkey for minimap zoom.

#### Wave 10: multiplayer robustness + keybindings (in progress)
(re-cut 2026-11-12 into 10a-1, 10b-1, 10a-2, 10b-2, 10a-3: HISTORY "Wave 10")
- 10a Multiplayer (G34, G35, G36; G39 if time). `DMath` software trig/exp across the sim (mechanical, one hash
  change, golden-value tests), observers, MP save/load, vote to drop, lobby pings, chat sounds. Owns `Rts.Sim` math
  call sites, `Rts.Net`, Net menus. Tests: Net tests, `Rts.Bench -- mp`, replays still verify.
  - [x] 10a-1 Deterministic math (2026-11-12; ARCHITECTURE "Deterministic math (cross-OS)"): software
    Sin/Cos/SinCos/Atan/Atan2/Asin/Acos/Exp/Log/Log2/Pow in `DMath` (max 2 ulp, Pow 4 beyond |y·log2 x| > 16), every
    sim call site and `Vector*.Lerp` routed through it (grep test), golden 600-tick game hash test, `GameInfo.MathVersion`
    (old replays and old generated-map saves refused with a clear message), `Msg.ProtocolVersion` 4; cost < 1 % of the
    tick. Reference hashes changed (bench 2F09CD7D01AF287E; `ai 30 256 42` Player (Hard) after 12:00, C87C46C4EA726A82).
  - [ ] later: run the sim tests (golden test) and `Rts.Bench -- replay` of one Linux-made file on Windows and on
    ARM64; `Vector2/3` dot/length intrinsic paths are still unverified across ISAs (could be written out like Lerp);
    old saves on generated maps cannot load (would need the old MathF generator or the terrain embedded in saves).
  - [x] 10a-2 Observers, vote to drop, lobby pings, chat sounds (2026-11-12; ARCHITECTURE "Observers, drop votes, lobby
    pings (10a-2)"): observer list in the lobby (any number up to 16, Observe/Play, host moves/kicks, a full game puts
    joiners there), observers get every turn and send none, no fog + follow any army's view; a defeated player resigns
    and stays as an observer; "Waiting for X" banner: 5 s countdown then a Drop button, strict majority of the remaining
    players drops X (host can't be voted out); per-person pings in the lobby; UI cues for chat, join/leave, ready, start;
    `Msg.ProtocolVersion` 5; `--observe`, `--net-vote-demo`, `Rts.Bench -- mp ... obs=N`.
  - [ ] later: host migration (the host can't be dropped by vote; its loss still ends the game for everybody);
    observers joining a game already running (needs G37 catch-up); an observer that loads late only catches up at the
    normal sim speed (no fast-forward); dedicated sounds for chat/join (today the click/confirm/error UI cues).
  - [x] 10a-3 Multiplayer save/load (2026-11-13; ARCHITECTURE "Multiplayer save/load (10a-3)"): any player asks (Esc →
    Save Game, F5), the host orders a tick, every peer writes the same save (id + state hash) with the lobby; Multiplayer
    → Load Saved Game / `--host --load-mp FILE` opens a load lobby (saved slots claimed by name, Take, "AI plays" for a
    missing player, fixed faction/team/colour); a missing or changed copy is sent by the host in chunks; lockstep resumes
    at the saved tick; host autosave `mp_autosave_1..3`; protocol 6; `Rts.Bench -- mp ... save=TICK`.
  - [ ] later: in the load lobby the map panel and game options still look editable for the host (the net layer
    ignores the changes: show them read-only); a GUI client joining a load lobby, a save on a map file and a save while
    a peer resigned are tested headless only or not at all; a save from another build of `Rts.Sim.dll` can't be resumed
    (by design, no migration: 11a-2).
- 10b Keybindings & client leftovers (G19; G21 if the owner wants it, else G11 client items and G20/G22 leftovers).
  Owns Hotkeys.cs, new KeyBindingsScreen, the listed client files. Tests: rebinding + clash tests; screenshots.
  - [x] 2026-11-12 (10b-1) Keybinding editor: Settings → Keys (capture with modifiers, rows rebind whole, clash
    refuse/swap, reset one/all), saved in settings.json, camera/audio/dialog/panel keys moved into the table (ARCHITECTURE "Keybindings").
  - [ ] later: bind a second (alternative) key per entry from the editor; a warning when Shift+K is bound while K is
    another entry's key (Shift is free for most keys, both would fire); a search field in the Keys tab; ReplayScreen keys ignore modifiers.
  - [x] 2026-11-12 (10b-2) Selection overlays and client leftovers (ARCHITECTURE "Selection overlays"): range / intel
    rings of the selection (key I, setting "Range rings", Shift = allied structures, merged + capped at 128), wrecks
    and deposits remembered under the fog (never-explored ones hidden), enhancement swap / chain in one click, minimap
    zoom keys Ctrl+= / Ctrl+-, no "Structure lost" toast on self-destruct, unit panel no longer allocates with big
    selections (500 selected: 208 → 28 KB/frame), `--perf-log` lists the stages that allocate.
  - [ ] later (10b-2): camera bookmarks saved per map/seed + on-screen list; "add" variants of the class filters
    (Shift is taken by Ctrl+Shift+L/A/N/E: needs another modifier); sandbox F9 "select own only" option (enemies are
    already selectable there); joined wall corner/segment meshes; crush animation; the snapshot still carries every
    wreck / deposit (fog memory is client-side only, a modified client sees them); fog memory is not saved (after a
    load, fogged wrecks reappear when seen); the enhancement chain queue is client-side and not saved (a load stops
    after the current step); ~22 KB/frame left with 500 selected (input + placement ghosts walk the selection).

#### Wave 11 (to do; the owner's answers of 2026-11-13)
- **Planned:** G12 pathing + G41 save migration (one pair, sim-side, no UI work), G24 voice announcer (the owner said
  yes; Italian, made with `espeak`: owner, 2026-11-13). Pair them so the sim work and the audio work do not share files.
- **Cut from the plan (owner, 2026-11-13):** campaign (G43), map editor (G29), Italian localization (G28, G25), online
  outside the LAN / relay server (G38; LAN and Tailscale/ZeroTier stay the way to play with friends).
- **Parked, not planned until the owner asks:** interactive tutorial (G42; the owner did not answer it), mod support
  (G44), FA units (G13; "not before the SupCom1 list G1-G45 is done", the owner did not object), split screen / second
  monitor (G21; the owner asked what it means, see Q9), reconnect (G37; not answered).
- **Mod support estimate (owner asked, for later):** S-M, after G45 (done). Work: an overlay loader on `BlueprintDb` for
  `mods/<name>/` JSON, an enable list in the menus, a mod hash in the lobby so every MP player loads the same set, and
  saves that record their mods. No script mods.

### Questions for the owner
1. **Campaign / story missions?** Recommendation: not now. First a short interactive tutorial (G42, one window) on a
   small scripted-scenario system that a campaign could reuse later; a full SupCom1-style campaign (2 × 6 missions,
   handmade maps, dialogue) costs many windows.
2. **Map editor?** Recommendation: no full editor; map files + a curated map pack (wave 8) and seed browsing cover
   play variety; terrain brushes in the sandbox later if you want to shape maps.
3. **Mod support?** Recommendation: yes but small (S-M), after packaging: `mods/<name>/` folders that add or override
   unit/faction JSON, an enable list, the same mods checked in MP. No script mods.
4. **Italian localization?** Recommendation: yes, as its own agent in wave 11 when the UI has settled (every UI
   string goes to a table, plus a font with accents); English stays available, docs stay English.
5. **How big a release step?** Recommendation (wave 7): a Linux Release folder + desktop shortcut + icon, saves in
   your user folder; a Windows zip as a cheap extra; no installer, Steam or itch.io for now.
6. **Online play outside the LAN / with Windows friends?** Recommendation: today Tailscale or ZeroTier (no code, acts
   as a LAN) or port forwarding; `DMath` (wave 10) only matters if Windows players join; a relay server needs a
   hosted machine (running cost): skip unless you want games with strangers.
7. **Voice announcer?** Recommendation: keep beeps + on-screen messages (all audio is synthesized, no asset files);
   a voice would mean the first recorded/generated audio assets (Italian or English?): your taste.
8. **Forged Alliance units for UEF/Cybran** (Broadsword, Wailer, Continental, Novax, Megalith, Brick...)?
   Recommendation: not until the SupCom1 list (G1-G45) is done.
9. **Split screen / second monitor?** Recommendation: skip (L cost, little use on one screen) unless you play on two
   monitors.

**Answers recorded 2026-11-13** (WORKFLOW "Owner decisions"; the list above keeps the original recommendations):
1. Campaign / story: no, cut (G43). Tutorial (G42): not answered, parked.
2. Map editor: no, cut (G29).
3. Mod support: not in the plan now; the owner asked how hard it is later. Estimate S-M, after G45 (see Wave 11).
4. Italian localization: no, cut (G28, G25).
5. Release step: the owner does not care; no further release work planned (G45 stays as delivered).
6. Online outside the LAN / with Windows friends: LAN only, cut (G38).
7. Voice announcer: yes (G24, Wave 11). Language: Italian; voice: `espeak`, already on the machine (both answered
   later on 2026-11-13: "usa espeak", "e la lingua italiano"). The on-screen texts stay English.
8. FA units (G13): no separate answer; the recommendation holds, parked until G1-G45 is done.
9. Split screen / second monitor (G21): the owner asked what it means; not decided, parked until explained.

## Phase 0 — Foundation ✅ (2026-10-10)
- [x] .NET 10 solution: Sim / Client / Tests / Bench
- [x] Deterministic sim core: UnitStore SoA, handles, DetRandom, Par (deterministic parallel chunks)
- [x] Threaded SimRunner (10 Hz), pooled snapshots, client interpolation
- [x] Blueprint JSON loader, 19 UEF T1 blueprints
- [x] Procedural symmetric map generator (deposits, start plateaus)
- [x] Commands/orders: move, attack-move, patrol, attack, guard, stop, formations, shift-queue
- [x] Basic movement (steering, turn rate, separation, terrain passability)
- [x] Client: terrain chunks, units as boxes, strategic icons, selection, orders, HUD, screenshot mode
- [x] Tests: determinism across thread counts, movement; benchmark (6k units ≈ 1.7 ms/tick)

## Phase 1 — Core gameplay (wave 1, parallel agents) ✅ merged 2026-10-10
- [x] **Combat** (merged 2026-10-10): targeting with priorities/layers/staggered scans, turrets, lead aiming,
      ballistic artillery, bombs, beams, homing missiles, flak, layer-filtered area damage (bench 6k units ≈ 3 ms/tick)
      - [ ] later: terrain blocking direct fire (EnergyPerShot: wave 4g; veterancy, idle aircraft engage: wave 4f)
- [x] **Economy & construction** (merged 2026-10-10): flow economy with efficiency/stall, engineers
      build/assist/repair/reclaim, factories with queue/repeat/rally/refund, deposits, storage, wrecks (PropStore)
      - [ ] later: reclaim energy from units (capture, pause, adjacency, factory drive-out: wave 4f)
- [x] **Pathfinding & movement** (merged 2026-10-10): HPA*-style sector regions + portals, cached goal fields,
      corridor A* + string pulling, formation path sharing, per-tick solve cap, air orbit, naval/hover/amphibious,
      priority separation, stuck → repath (1024 map, 6k units ≈ 2.5 ms/tick)
      - [ ] later: exact unit clearance, per-unit MaxSlope, air landing/refuel, async nav build (first tick ~180 ms on 1024)
- [x] **Client visuals** (merged 2026-10-10): procedural models for all shapes, instancing + culling, unit shadows,
      team colors, construction hologram, projectiles, particles/decals, role icons, sky, water shader, smooth camera
      (6k units: 170–360 FPS uncapped on Radeon 780M)
      - [ ] later: terrain shadows, effects scaled by game speed, wreck (PropView) rendering
- [x] **UI** (merged 2026-10-10): build menu with tabs/tooltips/hotkeys, placement ghost (client-side validity,
      deposit snap), factory panel (queue, progress, repeat), unit panel, order buttons, minimap, stall warning
      - [ ] later: kills in snapshot, structure rotation in sim, drag-to-place lines, custom font

## Phase 2 — A playable skirmish
- [x] Fog of war + radar (merged 2026-10-11): per-team bitset grids (vision/radar/sonar/omni/explored), parallel
      deterministic, ghosts of last-seen structures, fog-filtered snapshots, radar blips, client fog shader + minimap, F7/--nofog
      - [ ] later: fog wrecks/deposits, drop Attack orders on targets lost in fog, intel cost < 1 ms on 1024
- [x] Skirmish AI (merged 2026-10-11) — `src/Rts.Sim/AI`: deterministic in-sim AI issuing Commands; perception
      layer + threat maps, opening build order, expansion, power balance, air factory, PD/AA, waves to reachable targets,
      retreat/defense, Easy/Normal/Hard; ~0.01 ms/tick per AI; `Rts.Bench ai` headless matches
      - [x] wave 4a (AI resume): fog-fair perception (RespectIntel=true: ghosts, threat memory, staleness, commander
            last seen), land/air scouts + radar, staging on the walking route, reinforcement batches, regroup,
            recall when the base is losing, no army retreat, late-game push + snipes, terrain line of fire for
            non-forced targets (sim: `Combat/LineOfFire.cs`), Hard: artillery-heavy mix + micro (focus fire,
            pull-back, raids, snipes); ~0.02-0.03 ms/tick per AI. Bench 30 min/256/4 land presets: NvN ends by
            commander kill in ~86 %, Hard beats Normal in 86 % (166-8-18 of 192), Easy loses 23/24; 4p FFAs on 512 end
      - [ ] later: Highlands/Canyon NvN stalemates (equal armies trading), Hard vs Hard ends only ~69 %; T1 artillery
            looks too cost-efficient (balance); AI uses T2/T3, navy, strategic weapons, experimentals, transports
            (navy + drops done 2026-10-23, k-navy: Archipelago 256 NvN ends 2/6 by 30 min, was 0/6; k-navy 3: LZ and
            landed objectives avoid the enemy commander: 7/24 by 30 min after the land balance pass, 4/24 without)
      - [ ] later (k-navy): island drops still lose to big home armies on small islands (synchronized waves, pausing
            contested LZs and LZ freezing were tried and hurt, AI_TUNING "Navy and transports"); Hard builds few land
            units on islands (drops 3x fewer than Normal, loses 7 of 8 finished games to Normal); drops on land maps
            with cliffs. Engineer ferries not needed (engineers are amphibious).
- [x] Better map generator (merged 2026-10-11): presets Plains/Lakes/Highlands/Canyon/Archipelago, guaranteed land connectivity, tests
- [x] Main menu / game setup screen, victory/defeat screens (merged 2026-10-11):
      GameSession owns fog/wrecks/audio/cursor hints; setup AI slots → `Army.IsAI` + `AiDifficulty` (in-sim AI);
      fog ViewArmy = local human (defeated player "Keep Watching" = fog off); settings: master/effects/music
      volumes (saved) on an app-wide `AudioSystem` (menus: clicks + music); end screen `ArmyStats`
      - [x] AI on new presets: builds and fights everywhere; most NvN games end by commander kill (see Skirmish AI)
      - [x] AI uses everything (wave 5 k, 2026-10-24): T2/T3 tech with budget/pressure switch, shields, TML, strategic
            projects, enhancements, SCUs, air T2/T3; duel tool (`Rts.Bench duel/duelmatrix`), land balance pass, Cybran/UEF
            parity (UEF T1 tank shells 30 -> 40: CvU 51-21 -> 39-35), Normal defaults + Hard preset (HvN 102-52 at 45 min),
            lab scripts `tools/ai-lab`. Numbers: AI_TUNING "Final numbers of agent (k)".
- [x] Audio (merged 2026-10-11): 55 procedural SFX + ambient wind + generative music, positional, voice-limited,
      graceful without audio device (`--nosound`); wreck rendering; context orders (reclaim/repair/assist);
      kills + veterancy stars in unit panel; effects follow game speed
      - [ ] later: a human listening pass on the sounds (volume settings: menus branch)

- [x] Sandbox mode (agent e, wave 3): main menu "Sandbox" / `--sandbox` / `--exec "cmds"`; side panel (F9) with every
      blueprint (grouped, searchable, 1/10/50/100 per click, any army, construction sites), cheats (infinite resources,
      instant build, build anywhere, reveal map), AI on/off + difficulty per army, victory on/off (default: never ends),
      speed/pause, selection kill/delete/heal/damage/give/finish, clear area, nuke (`NukeFx`: flash, shockwave, fireball,
      mushroom cloud, scorch, deep boom; wrecks vaporized); console (F10 / `) with fuzzy unit names, history, Tab
      completion, help overlay. Sim: `SandboxSystem` + 16 tests; client parser tests (`SandboxConsoleTests`).
- [x] Sandbox × strategic (merge, 2026-10-12): `nuke x,z` / panel NUKE launch a REAL strategic missile
      (`StrategicApi.LaunchNuke`, from the nearest own nuke silo, else the commander, else the map edge: launch
      warning, anti-nukes intercept it, shields knocked down); `nuke instant x,z` / panel Blast = the detonation at once;
      instant build fills silo stockpiles; "Strategic & shields" spawn group; one `EffectKind.Nuke` → `NukeFx` + one
      sound for every nuke; real nukes vaporize wrecks in the crater too. Tests: `SandboxNukeTests`.
      - [ ] later: select enemy units for the actions (box-select only picks own units)

## Phase 3 — SupCom depth
- [x] T2 and T3 tech (wave 3, agent a, 2026-10-12): in-place upgrades (`UpgradeSystem`: land/air factory, mex,
      radar→omni; own build power + engineer assist, factory queue pauses, mex keeps producing, cancel refunds, same
      handle), 30 UEF T2/T3 blueprints (engineers, factories, mex, pgen, mass fab, PD, flak, SAM, omni, Pillar,
      Riptide, Brimstone MML, Sky Boxer, Titan, Percival, Demolisher, gunship, Janus, Wasp, Ambassador, spy plane),
      T2/T3 models, upgrade tile/U hotkey/progress bars/hologram, build-menu tier pills, `--tech-demo`
- [x] Shields, tactical missiles, artillery structures, strategic nukes + anti-nuke (wave 3, agent b, 2026-10-12):
      `Rts.Sim/Strategic` (shield domes absorb outside shots/splash, collapse + recharge, drop on energy stall; silo
      stockpiles + `OrderType.Launch`; TML cruise missiles, TMD; nuke high arc + two-ring blast that knocks shields
      down, anti-nuke), 9 UEF blueprints (T2/T3 shield, Parashield mobile shield, TML, TMD, nuke silo, anti-nuke,
      T2/T3 artillery), `StrategicApi.LaunchNuke` (used by the sandbox nuke), dome shader + ripples, missile trails,
      nuke explosion (`NukeFx`, shared with the sandbox) + flash + sound, launch mode (L), global launch warning,
      `--scene strategic`
- [x] Naval (wave 3, agent c, 2026-10-12): naval factories T1→T2→T3 (in-place upgrades), frigate, attack sub,
      destroyer (guns + torpedoes + torpedo defense), cruiser (AA + missiles), battleship, T2 torpedo bomber, T1/T2
      sonar, torpedo launcher; water placement (`NavalRules`, client ghost), torpedoes (run under water, home, die on
      shore), rate-limited torpedo defense, subs only seen by sonar/omni and only hit by torpedoes; Archipelago sea
      lanes + harbor beaches (playable: coast reachable, battleship lanes between starts); client: ship models, wakes,
      torpedo bubble trails, dim submerged subs, sonar ring, `--scene naval`
      - [x] AI builds and uses navy + transport drops on island maps (wave 5 k-navy, 2026-10-23; AI_TUNING "Navy and
            transports")
      - [ ] later: ships steering around each other in narrow channels
            (they only push apart now)
- [x] Experimentals (2026-10-13, wave 4g): data-driven `Experimental` block (crush, delayed death blast, big wreck), Tech 4 + "EXP"
      build-menu tier, built by T3 engineers / SCUs / ACU with T3 Engineering, mobile sites shared by assisting engineers;
      **Fatboy** (mobile factory building T1–T3 land units while moving, bubble shield, gauss cannons, riot gun, flak) and
      **Mavor** (strategic artillery, range 1500, 20000 energy per shot via `EnergyPerShot`); "Experimental detected" alert + sound
      - [ ] later: Monkeylord/Czar-like experimentals (2nd faction), crush animation, shield HP kept on enhancement swap,
        Mavor's low arc can hit hills at short range (higher/medium arc), Mavor target priority (it shoots any visible enemy)
- [x] Commander enhancements (2026-10-13, wave 4g): slots LeftArm/RightArm/Back with prerequisites (blueprint variants per slot
      combination), built on itself over time like an upgrade (drain, assist with Guard, cancel refunds, any order interrupts),
      remove = instant without refund; ACU: Heavy Antimatter Cannon, Zephyr Amplifier, Advanced/T3 Engineering, Resource Allocation,
      Personal Shield → Shield Generator Field, Tactical Missile Pack; enhancement panel, model attachments
      - [ ] later: Stop cancels the enhancement, replace an occupied slot in one click, mobile TML moves into range
- [x] Quantum Gateway + Support Commanders (2026-10-13, wave 4g): T3 gateway builds SCUs (T3 engineer + cannon, own enhancements:
      High Explosive Ordnance, Rapid Fabricator, Resource Allocation, Personal/Bubble Shield); SCU death never defeats the army
- [x] Commander kit & engineer automation (2026-10-24, wave 6a; ARCHITECTURE "Commander kit & engineer automation"):
      G1 ACU Overcharge (energy-fed shot, damage from stored energy, structures x0.5, ACUs capped; key O + orders
      button, fx/sounds), G2 commander death blast (two-ring nuke-like blast 1.5 s after death, smaller ACU mushroom),
      G3 patrol automation (patrolling engineers/ACU/SCU repair > assist > reclaim props near the route), G7 Personal
      Teleporter enhancement on both ACUs (10 s charge, 5000 E + 50 E per unit of distance; key J + button, arc/cost
      overlay, charge/arrival fx and sounds, back-pack model); console `overcharge`, `kill r at x,z`, `teleport x,z`
      Reference match `ai 30 256 42 hard,normal` unchanged in play (Hard 15:35, identical stats), new hash
      BF85B7DBBF03D949 (the new commander state is mixed into the hash)
      - [ ] later: UEF Engineering Drones (C-D1/C-D2 ACU enhancement), UEF SCU Radar Jammer enhancement (rest of G7)
      - [ ] later: Overcharge reload/charge in the snapshot (button
            shows ready from stored energy only); patrol test for the unreachable-target cooldown (needs a cliff map)
- [x] Game options & team play (2026-10-24, wave 6b; ARCHITECTURE "Game options & team play"): G4 options in the
      skirmish setup AND the lobby (victory assassination/supremacy/annihilation/sandbox, unit cap 250-1000 + HUD
      "units n/cap" counter with amber/red + toast, start resources low/normal/high (bank), fog normal/explored/none,
      speed adjustable/1x/2x locked, cheats (F9/F10 sandbox tools in a normal game), prebuilt base), `--options SPEC`,
      lobby codec (protocol 2); G5 give units / mass / energy to allies, allied overflow sharing, allies-only chat
      (Shift+Enter), F4 diplomacy panel, `--teams`, `--diplomacy`; G6 score history every 30 s (saved), end-screen line
      chart per army (score, incomes, units, kills, mass collected), score column in the diplomacy panel.
      Defaults = old behavior: reference match unchanged (Hard 15:35, hash BF85B7DBBF03D949)
      - [ ] later: share-until-death / full share of a defeated ally's units (SupCom "share conditions"), alliances
            changed during the game (today teams are fixed by the lobby), gifting units beyond the receiver's cap
      - [ ] later: save field migrations for the new 6b fields (`SimWorld.Options`, `Army.UnitCap/StartBank*`,
            `ArmyStats` gifts + `History`) so pre-6b saves load (header `GameInfo.Options = null` = defaults), allies-only chat in
            the lobby and in single player (AI allies), score graph hover/values, frame-timing text overlaps the
            diplomacy header in screenshot mode
- [x] Release, settings, autosave (2026-10-24, wave 7a; ARCHITECTURE "Client" user folders/settings bullets, "Save/load"
      autosave bullet, DEV_GUIDE "Packaging"): G45 `bash tools/package/package.sh [linux|win|all]` → self-contained
      `release/RtsGame-0.7.0-linux-x64.tar.gz` (36 MB) / `-win-x64.zip` (37 MB) with data/, `release.txt` marker
      (user folders `~/.local/share/RtsGame`), procedural icon, `.desktop` + `install-desktop.sh` (tested with a
      temporary HOME only), version label in the main menu, crash log `~/.local/share/RtsGame/crash.log`. G18 settings
      tabs Audio & Display / Graphics (shadows, particles, terrain detail, VSync, FPS cap, UI scale) / Gameplay (edge
      scroll, pan/zoom speed, autosave), all live, Settings in the pause menu. G40 autosave every 5 min of game time,
      rolling `autosave_1..3`, not in replays/sandbox/MP/screenshot runs (`--autosave-every TICKS` for tests).
      - [ ] later: install the desktop shortcut on the owner's machine (ask first); F12 screenshots go to the cwd
            (raylib built-in capture; the launcher sets `Path=` to the user screenshots folder); Settings and autosave
            in multiplayer (needs an in-screen overlay: leaving NetInGameScreen stalls lockstep); live effect of
            shadows/particles/UI scale only unit-tested (screenshot runs use defaults); signed/AppImage/Flatpak builds
- [x] Transports/ferry, air staging, engineer assist chains (wave 4, agent h, 2026-10-13): one cargo mechanic
      (`Rts.Sim/Transport`; carried units off the map, untargetable, die with the carrier); C-6 (T1, 6 slots) and C-14
      (T2, 12 slots, AA + riot gun) VTOL transports; orders Load / Unload / Transport-to (cargo split among the
      transports) / Ferry (beacon → drop, repeats) / Dock; aircraft fuel (half speed when empty) + auto-return; T2 Air
      Staging (repairs/refuels, releases when done); Atlantis experimental carrier (builds, carries and launches
      aircraft); engineer assist chains (guard a guard → helps the root builder; guards repair first); client:
      models, cargo hanging under transports, context clicks, order buttons, cargo counter, fuel bar, ferry lines,
      `--scene transport`
      - [x] Atlantis dive (Sub layer + `StartSurfaced`, no dock/launch/SAM while submerged; AtlantisDiveTests)
      - [ ] later: staging repair cost, T3 heavy transport, emergency drop, gunship VTOL hover
      - [x] merged with wave 4f polish (2026-10-14): shared hotkeys E (transport-to with transports selected, else
            capture) and D (subs dive + aircraft dock); captured carriers take their cargo along (not when carrying a
            commander); pausable Atlantis factory; boarding ends the factory roll-out
- [x] Faction infrastructure (2026-10-14, agent j): `data/factions/<id>.json` (commander, color, roles → blueprint ids,
      70 required + 15 optional roles), `FactionDb` on `BlueprintDb.Factions`, `UnitBlueprint.Faction`, per-army
      `Army.Faction` from the setup (random = from the seed), faction commander; AI + strategic code use roles (no
      `uef_*` ids in the sim); client: FACTION column in the skirmish setup, `--faction`/`--factions`, sandbox FACTION
      filter, saved in client data/save headers; bench `[factions]` arg; ARCHITECTURE "Factions" contract
- [x] Second faction: Cybran (2026-10-23, wave 5l; ARCHITECTURE "Cybran faction & stealth/EMP"): `cybran.json` + 80
      `cybran_*` blueprints (full SupCom1 roster, ACU/SCU enhancements, Monkeylord, Soul Ripper, Scathis, ED1→ED5
      shield chain); sim: radar/sonar stealth, cloak, stealth fields (omni beats them), EMP stun (Medusa, SCU),
      seabed walkers (Wagner, Monkeylord); distinct `cy_*` models for every unit; status visuals (own cloaked units
      translucent, stun sparks, stealth-field ring); `--scene cybran[:group]`; Cybran vs UEF AI 8/5/5 (AI_TUNING)
- [x] SupCom polish (2026-10-14, wave 4f): collapsed/paused shields pay no upkeep, recharge only without stall; domes
      absorb bombs/shells from above and bombs released inside; ship hull separation (subs vs subs, surfaced subs vs
      ships); sub dive/surface (D); adjacency bonus (pgens cut energy use, storages boost mex/pgens; placement lines);
      veterancy (+10% HP and regen per level); pause (H: factories, engineers, upgrades, enhancements, silos, fabs,
      shields, radar); capture (E / right-click enemy with engineers); units drive out of factories, aircraft take off
      from the pad; Attack orders dropped when the target is lost in fog; idle aircraft engage; Shift+drag structure
      lines; sandbox selects/orders any army (`--select any:<id>`); default cameras for `--scene strategic[:x]`/naval
      - [ ] later: the Fatboy drops units at its back without a drive-out
        (done in wave 5n: factory exits skip sides with parked units; fire states, captured targets held)
- [x] Mass fabricators (T2/T3), adjacency bonus (wave 4f)

- [x] Map props: trees and rocks (2026-11-12, module 8b-1; ARCHITECTURE "Map props"): `PropStore` kinds (wreck /
      tree / rock) + unsaved spatial grid; `Map/MapProps` places symmetric forests and rock fields on every preset
      (4 players: 256 = 460, 512 = 2660, 1024 = 10816 props); engineers/AI reclaim them (trees energy, rocks mass);
      trees fall under moving units, burn in area damage (burnt = less value, spreads), nukes clear forests;
      `PropRenderer.Nature` (instanced, near/far LOD, fall/burn looks); reclaim tooltip; `--scene props[:fire|:rocks]`
      - [ ] later: trees cast no shadows (the shadow pass is in UnitRenderer); a fallen tree that ignites replays its
        fall animation (needs a FallTick field); trees on the minimap; old saves/replays refused (layout change)
- [x] Map files, map pack, map picker (2026-11-12, module 8b-2; ARCHITECTURE "Map files"): versioned `*.rtsmap`
      (`Map/MapFile`: heights, water, deposits, starts, props, name, content hash; bit-exact round trip, Brotli),
      `MapLibrary` + `MapChecks` (generator guarantees); `Rts.Bench -- mapexport` (+ `--pack`, `--scan`); 10 named
      maps in `data/maps` (2-8 players, 256/512/1024, 2373 KB, spec `tools/maps/pack.txt`); fewer players than starts
      = spread subset; skirmish setup "Map" picker (Random map keeps the generator rows) + `--mapfile NAME`; saves and
      replays record map id + hash and refuse a missing or different file; generated maps unchanged
      - [ ] later: saves of a map loaded from a path outside data/maps reload only if the file is copied there; map
        files in the lobby / multiplayer (8b-3); a user maps folder (release mode); a map editor
- [x] Maps in multiplayer + biomes (2026-11-12, module 8b-3; ARCHITECTURE "Maps in multiplayer and biomes"): the host
      picks a map file in the lobby (Map + Biome rows); clients with the same file (id + content hash, data/maps or
      `downloaded_maps`) use it, the others download it from the host (32 KB chunks, 256 KB window, progress line,
      Ready off until verified; hash checked off-thread), net protocol 3 (old peers refused); net replays record the
      map; `Rts.Bench -- mp ... map=ID biome=X`; biomes temperate / desert / tundra / lava / tropical (map file header
      byte, generator/skirmish setting, `--biome`, setup + lobby rows; terrain, water, sky, minimap and preview
      palettes, biome trees and rocks; no effect on the sim); `mapexport --biome B`; 884 KB map in ~120 ms on loopback
      - [ ] later: resume a broken download (it restarts from zero); biomes for the pack maps (all Temperate now);
        a lobby map browser with thumbnails; a client-side cache limit for `downloaded_maps`

## Phase 4 — Scale and polish
- [x] 1024 maps with 4–8 players, 5k+ units at 60 FPS (wave 5m, 2026-10-16; ARCHITECTURE "Performance & scale"):
      Par worker pool without allocations, stage profiler (bench + client `--perf-log`), JIT warm-up thread + loops
      optimized at once (tick spikes gone: 8 AIs on 1024 max 12 ms, was 217), nav grids at load + parallel graph
      builds, goal-field array reuse (AI games 10 KB/tick, was ~125), intel 0.33 ms with 8 armies; client terrain
      chunk LOD + skirts (GPU 8.2 → 2.8 ms whole 1024 map), instanced particles, deposit/minimap culling (4.8k units
      in battle: CPU 1.6 ms/frame); 6/8-player maps on 512/1024 checked fair for every preset (`MapScaleTests`).
      - [ ] later: a region graph built mid-game (first unit of a new layer/size) costs ~14 ms on 1024; low-poly unit
            LOD (units:gather 0.8 ms + GPU 1.8 ms at 3.3k units drawn); snapshot build ~0.3 ms avg
- [x] Save/load, replays, desync detection (wave 4, agent i, 2026-10-14): automatic deep serializer of the whole sim
      (`Rts.Sim/Persistence`: IL codecs, shared refs, blueprint/terrain by id, per-type layout check, Brotli files;
      `[NoSave]` + `IAfterLoad` for caches), save → load → continue = identical state/hash (1 and 4 threads, test with
      every blueprint, AIs, missiles, nukes, upgrades, enhancements), coverage guard test (`SaveCoverageTests`).
      Replays = initial state + command log + `StateHash` every 10 ticks (≈0.01 ms/tick); `ReplayPlayer` with desync
      detection and backward seek (checkpoints). Client: every game recorded (written at the end and on quit), pause
      menu Save Game + F5/F6 quicksave/quickload, main menu Load Game / Replays lists, replay viewer (pause, 0.5×–16×,
      timeline seek, fog per army or all, DESYNC banner), sandbox setups saveable, clear errors for damaged/incompatible
      files; `Rts.Bench replay <file>` / `savebench`. Late game (512 map, 4 AIs, 25 min, 6.6k units): save 1.8 MB,
      capture 86 ms + background encode 99 ms, load 0.3 s; replay 165 KB, headless verify ≈150× real time.
      - [x] wave 5n: container version + migration steps (`SaveMigrations`, v2), pause menu "Load Game", F7 updates
            the replay view selector
      - [ ] later: saves don't survive sim LAYOUT changes (no field-level migration: the layout check refuses them);
            seek checkpoints cost a short hitch every 60 s of game time; multiplayer reuse (autosave: done wave 7a)
- [x] Lockstep multiplayer (2026-10-16, wave 5 o): new `src/Rts.Net` (no raylib). TCP length-framed protocol, host
      relays turns (star), LAN discovery (UDP 47625), join by ip:port; lobby with 8 slots (human/AI/open/closed),
      faction, team, color, AI level, ready, host map options + live preview, chat, kick; one turn per player per
      tick, input delay adaptive to ping (1..30 ticks), sim holds until every turn is in ("Waiting for X"); speed and
      pause negotiated; StateHash at load + every 10 ticks → desync banner + replay dump; drop after 10 s silence →
      AI takeover or defeat (host option), host lost → client continues offline; every MP game writes a normal
      replay. Client: Multiplayer menu (Host / Join / LAN list), lobby, net loading, in-game chat (Enter), players +
      pings panel, Esc menu without pause; CLI `--host [port]`, `--join ip:port`, `--name`, `--autostart`.
      Tests: Rts.Net.Tests (21: framing, codecs, lobby, factions, 2–4 peer loopback matches with random commands,
      drops, desync, speed, LAN), `Rts.Bench -- mp` (4 peers 1200 ticks in 1.3 s, all hashes equal; drop variant),
      two windows on one machine. Float determinism review in ARCHITECTURE "Multiplayer".
      - [ ] later: software trig in `DMath` (cross-OS games can desync: libm vs UCRT), reconnecting, observers,
            host migration, saving/loading MP games, lobby pings per player, chat sounds, NAT traversal/relay server
            (today: port forwarding), "Waiting for X" → vote to drop
- [x] Voice announcer (2026-11-13, wave 11b-1, gap G24; ARCHITECTURE "Voice announcer (11b-1: files)", DEV_GUIDE "Voice
      announcer"): 19 spoken Italian lines (on-screen text stays English) on top of the beeps and toasts: commander / base /
      units / ally under attack, structure lost, nuclear launch detected / strategic missile launched, tactical launch
      detected, enemy experimental detected, enemy / allied commander destroyed, enemy / ally defeated, experimental
      complete, enhancement complete, upgrade complete, mass / energy low, unit cap. Line table `data/audio/voice/lines.json`
      (text, priority, cooldown, maxWait) read by the game and by the generator `tools/voice/generate.py` (espeak `it+m3` +
      ffmpeg trim, band-pass, echo, loudness; 22050 Hz mono WAV, 1.5 MB). Queue: one line at a time, priority first, stale
      lines dropped, per-line cooldowns. Settings → Audio: Voice on/off + Voice volume. `--voice-test` plays every line.
      Tests: `VoiceTests` (10).
      - [ ] later: voice lines for victory / defeat, own commander lost, allied pings; a second language (the layout is
            per language: add `text.<lang>` + `languages`, regenerate, pick the language in settings); a nicer voice
            (espeak only; mbrola voices are listed but the mbrola binary is not installed)
