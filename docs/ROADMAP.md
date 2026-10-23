# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (updated 2026-10-23 13:50 — read this first in a new session)
main has waves 1-4 + (j) factions (merged 2026-10-15) + (n) UX polish, (o) lockstep MP and (m) scale/perf (merged
2026-10-16) + (l) Cybran faction (merged 2026-10-23). Tests: 416 sim + 227 client + 21 net (`tests/Rts.Net.Tests`).
`Rts.Bench -- ai 30 256 42 hard,normal` on main = "WINNER: Player (Hard) after 11:30", hash EC8B7A06DF52317E
(the reference for agents that must not change sim results; before the Cybran data it was EDCA8A729F29736E, same game).
After the land balance pass (AI_TUNING "Unit balance" AFTER) it is "WINNER: AI 1 (Normal) after 15:50", hash 9724216772093A2E.
With the (k) AI defaults after the balance (a37b799) it is "WINNER: Player (Hard) after 15:58", hash 74E2EB7D333190A4.
Agent ids: `~/.claude/projects/-run-media-marco-RTS/orchestrator/agents-wave5.md` (ids are valid only in
the orchestrator session that started them). Each agent keeps `<worktree>/.claude/handoff.md` and writes
`<worktree>/.claude/agent-report.md` when done. Briefs: HISTORY.md "Wave 5".

**ON RESUME:**
- **2026-10-23 ~20:20 — orchestrator restart (session 1d992d8d ended at 244k). Do this first:**
  1. Both running agents CHECKPOINTed cleanly (trees clean, handoff notes current). Start a **fresh coordinator in each worktree** (absolute path, no isolation;
     prompt = the brief below + "continue from `.claude/handoff.md` and `.claude/notes/`"), at most 2 active agents;
     coordinators run ONE sub-agent at a time and wait while it works. Agent tags: `[IA usa tutto (5k)] ...`,
     `[Comandante e ingegneri (6a)] ...`. Context watcher: `agent_ctx.py --watch 135000 <id>=<cap-15k> ...`.
  2. **(k) AI uses everything** — worktree `.claude/worktrees/agent-ac4c927c54a66ca1d` (branch
     `worktree-agent-ac4c927c54a66ca1d`), class high, cap 250k (CHECKPOINT 230k). Goals (orchestrator decision
     2026-10-23): (a) lopsided games end, (b) finished games at a 45-min cap: NvN >= 80/96, HvH >= 85 %, Hard v Normal
     win share >= main's (main 177-7-8), (c) tech beats a T1-only config >= 65 %; timeboxed (record numbers, pick best
     defaults, move on). Done (CHECKPOINT 79f4ece, tests Sim 419/Client 227/Net 21): duel tool, unit balance pass
     (7650459), new Normal defaults, new Hard preset (focus fire off), branch reference hash 74E2EB7D333190A4 (Hard
     15:58). Numbers at 45 min: (c) 64 % (112-61), NvN 70/96, lopsided stalled 25/77, HvH 68/96, Hard v Normal 92-55.
     **Next: Cybran/UEF parity first (51-21; `duelmatrix uef cybran` role by role), then Hard's edge, stalls
     documented if no lever helps.** Then: AiTechTests timings, docs, lab
     scripts in tools/ai-lab, Client/Net tests, perf, final merge main, report with "Handoff review". Coordinators
     8/9 reached 240k in ~40 min: keep outputs tiny. Its branch tracks no `.claude/` files any more.
  3. **(k-navy) DONE** — branch `wave5-ai-navy` (worktree `.claude/worktrees/ai-navy`, 0d0f4b0, report in its
     `.claude/agent-report.md`). Merge into main AFTER (k): first `git merge` the final (k) branch into it (conflicts
     only in AI_TUNING.md), all tests, then merge. Copy both reports into HISTORY, remove worktrees + branches.
  4. **(6a) Commander & engineers** — worktree `.claude/worktrees/wave6a` (branch `wave6a-commander`, from main
     03bda94), class medium, cap 200k, sub-agents low 150k. CHECKPOINT before any code: its handoff has the G3
     design checked against the code and the piece order (G3, G1, G2, G7). Brief: "Wave 6+ plan" section, row 6a (G1 Overcharge,
     G2 commander death blast in `Commander/` not CleanupSystem, G3 engineer patrol automation in
     `Economy/EngineerPatrol.cs`, G7 teleporter / UEF drones / SCU jammer if time); both factions via roles; tests,
     screenshots, docs, new reference hash documented, report.
  5. When (k) is merged: start **6b game options & team play** (brief in "Wave 6+ plan").
  6. **Owner questions pending** (asked 2026-10-23 ~19:05, see "Questions for the owner" in the Wave 6+ plan:
     campaign, map editor, mods, Italian localization, release packaging, online play, voice announcer, FA units,
     split screen). Ask again if unanswered; record answers in WORKFLOW "Owner decisions".
  7. New worktrees need `git config --global --add safe.directory <path>` (disk mounted as root, WORKFLOW
     "Incidents"). Agent ids of this session: `~/.claude/projects/-run-media-marco-RTS/orchestrator/agents-wave5.md`.
- 2026-10-23 18:50 (session 1d992d8d, orchestrator near its context limit): state for a restart.
  - (k) coordinator 9 running in `agent-ac4c927c54a66ca1d`: unit balance pass done (7650459, duel tool f71a222,
    AI_TUNING "Unit balance"), now re-measuring goals (a)(b)(c). When it finishes: merge it FIRST.
  - (k-navy) DONE: branch `wave5-ai-navy` (0d0f4b0, worktree `.claude/worktrees/ai-navy`, report in its
    `.claude/agent-report.md`), contains (k) up to 9f0b1e5. Merge it AFTER (k) (it may need `git merge` of the final
    (k) first: conflicts only in AI_TUNING.md). Then HISTORY reports for both, remove worktrees/branches.
  - Wave 6 audit merged (820b2e4): "Wave 6+ plan" below. Its 9 owner questions were asked on 2026-10-23 ~19:05;
    record the answers in WORKFLOW "Owner decisions" and adjust the plan.
  - 2026-10-23 20:10: wave 6a (commander & engineers: Overcharge, death blast, patrol automation, then teleport/drones/
    jammer) started early, in parallel with (k) (disjoint files): coordinator in `.claude/worktrees/wave6a` (branch
    `wave6a-commander`). Start 6b (game options & team play) when (k) finishes. (k) was told: Cybran v UEF 51-21 and
    Hard v Normal 92-55 after the balance pass must be fixed.
  - New worktrees need `git config --global --add safe.directory <path>` (disk mounted as root).
- 2026-10-23 18:25: (k-navy) 2 stopped at its cap with navy + transports written, tested (Sim 423) and documented,
  (k) and main merged in (d2f9b24); k-navy 3 finishes it (re-measure, landing-zone weakness, Hard harass drops, perf,
  report). Its branch merges into main AFTER (k). (k) coordinator 9 runs the unit balance sub-agent.
- 2026-10-23 13:52: (k) coordinator 8 stopped at its cap (CHECKPOINT cf3767b): a T1-only AI beats the tech AI 22-6,
  best lever ArtilleryTier=1 (NvN 45 min 78/96). Coordinator 9 started with the orchestrator's direction: **balance
  first** (bench duel tool, then T1 artillery down / T2-T3 up to SupCom-like mass efficiency, UEF and Cybran), then
  goals (a)(b)(c) again.
- 2026-10-23 13:55: (l) Cybran merged into main (f727ced). Running: (k) coordinator 8 in `agent-ac4c927c54a66ca1d`
  and the new (k-navy) agent in `.claude/worktrees/ai-navy` (branch `wave5-ai-navy`, from the (k) branch at 701ea45):
  navy + transports AI (owns AiNavy*, AiTransport*, AiConfig.Navy.cs, their tests); it merges the (k) branch into its
  own at the end. Merge order: (k) into main, then `wave5-ai-navy` (or navy into (k) first). If cut off: fresh agent
  in the same worktree from its `.claude/handoff.md`.
- 2026-10-23 13:15 (session 1d992d8d): the disk came back at `/run/media/marco/RTS` (WORKFLOW "Incidents"); the
  10-22 ~23:00 usage limit had cut both coordinators ((k) WIP committed as ceacaa1, (l) C3a WIP 606a2b7). Fresh
  coordinators started (ids in `orchestrator/agents-wave5.md`): (l) cap 200k: C3a leftovers, C3b status visuals,
  D integration + report. (k) cap 250k with **new stall goals** (orchestrator decision: the 30-min 80/96 target
  measured T1-only play): (a) lopsided games must end (winning army stops 40-70 from the base), (b) finished games
  at a 45-min cap: NvN >= 80/96, HvH >= 85 %, Hard v Normal >= main, (c) tech beats a T1-only config head-to-head
  (>= 65 %); timeboxed. Navy + transports AI go to a separate agent (new worktree from the (k) branch) after (l) merges.
- 2026-10-22 21:55 (session cb4473f4, after a reboot: every /tmp scratchpad is wiped): the 2026-10-16 16:26 session
  limit had cut both coordinators and the (l) client sub-agent C1; its WIP is committed as 8d78f74 (Cybran air models
  + client hooks; naval models missing, the client does NOT compile; state at the bottom of (l) `notes/C-client.md`).
  Fresh coordinators started in both worktrees (ids in `orchestrator/agents-wave5.md`): (l) cap 200k (medium:
  integration), (k) cap 250k (high: stall diagnosis by matrices). Same brief and TODO as below. If this session is gone
  too, start them again the same way (their handoff notes are current).
- 2026-10-16 16:07: new orchestrator session started a fresh coordinator in each worktree below ((l) and (k)); ids in
  `orchestrator/agents-wave5.md`. If this session is gone too, repeat the step below (their handoff notes are current).
- 2026-10-16 16:10: the owner restarted the orchestrator. The last two wave 5 agents were told to CHECKPOINT
  (commit + short handoff) and stop with the old session; their ids are gone. **Start a fresh agent in each
  worktree** (absolute path, no isolation; brief in HISTORY "Wave 5" + "continue from `.claude/handoff.md` and the
  notes in `.claude/notes/`"), at most 2 at a time (WORKFLOW "Usage limits"), each a coordinator running ONE sub-agent
  at a time, everyone under the 150k context cap (owner rule 2026-10-16, AGENT_RULES):
  - (l) Cybran in `agent-a9df3acf3524c656f`: sim done (stealth/cloak/field, EMP, seabed walkers; shield chain: see
    `notes/A-sim.md`), data done (80 blueprints + cybran.json + CybranRosterTests, `notes/B-data.md`; 5c23700, all
    tests green: 399 sim + 213 client + 21 net). Next: sub-agent C client (remaining air/naval/experimental/structure
    models: remove the `#if CYBRAN_MODELS_FULL` guard in `UnitModels.Cybran.cs`, `case` lines, icons, status
    visuals, `--scene cybran`), then D integration (Cybran vs UEF AI matches, perf, docs, final merge, report).
  - (k) AI uses everything in `agent-ac4c927c54a66ca1d` (CHECKPOINT ab30ce2, compiles, clean): tech (T2/T3
    upgrades, enhancements, adjacency), projects (shields, TMD, anti-nuke, TML, artillery, gateway/SCUs,
    experimentals, nukes, mass fabs), `AiStrategic`, captures, a `tech:` summary line in the bench. **Problem first:
    tech makes games stall** (Hard v Normal 77-16 with 99 unfinished of 192, main 177-7-8; NvN finished 45/96 vs
    80/96). Left: fix the stalemates, navy + transports (`AiNavy`/`AiTransport` are stubs), T1 artillery balance (done: land balance pass, AI_TUNING "Unit balance" AFTER),
    `AiTechTests`, docs, report. Client/Net tests not yet run on the branch.
    (m) measured one AI think at ~11 ms with 8 AIs on 1024: (k) should keep it down.
  - Both worktrees were clean at the restart. Merge (l) first; then (k) merges
    main and runs Cybran vs UEF matches.
  History: weekly limit 2026-10-14 08:14; session limits 2026-10-15 11:21 and 15:09, 2026-10-16 11:43 (reset 15:40), 2026-10-16 16:26, 2026-10-22 ~23:00, 2026-10-23 14:05 (reset 18:00; agents resumed by SendMessage).
- When an agent finishes (report + clean tree): merge it into main (`git merge --no-ff <branch>`), build, both tests,
  a screenshot, the AI match above (same hash unless the agent documents why it changes: (k) and (l) will change it),
  then `git worktree unlock` + `git worktree remove` + `git branch -d`, report → HISTORY.
- If an agent got cut off by a usage limit: commit its WIP in its worktree, then start a fresh agent in the same
  worktree (absolute path, no isolation) with its brief + "continue from .claude/handoff.md".
- Owner question (2026-10-15) "would Rust have been better than .NET?": answered, stay on .NET (WORKFLOW "Owner decisions").
- Housekeeping: a stray `.gitignore` rewrite patch is in `orchestrator/stray-main-gitignore.patch`.

**Next waves (owner: "everything must be done"; 2026-10-23: two factions, UEF + Cybran, are enough, no further factions):**
- **Wave 5** (after f + j are merged), 5 parallel agents:
  1. AI uses everything: T2/T3 upgrades, adjacency, enhancements, navy (Archipelago), transports, shields/TML/nukes/
     anti-nuke, experimentals. Faction-agnostic via roles. Balance pass.
  2. Cybran faction (`data/factions/cybran.json`): full T1–T3, Monkeylord, Soul Ripper, Scathis; stealth/cloak, EMP,
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
| G4 | **Game options**: victory assassination / supremacy / annihilation / sandbox; unit cap 250-1000 (+ HUD "units 123/500"); start resources; fog explored/none; game speed normal/fast/adjustable; cheats; prebuilt units | SC1 | M | GameSetup, CleanupSystem, Army; SkirmishSetupScreen, LobbyScreen, NetGameConfig, `Rts.Net/Lobby` codec; Hud | — | 1 |
| G5 | **Team play**: give units and mass/energy to allies, allied overflow sharing, allies-only chat | SC1 | M | UnitTransfer, new `Economy/EconomyShare.cs`, new `Orders/DiplomacyCommands.cs`, new `UI/DiplomacyPanel` | G4 | 1 |
| G6 | Score history (income, units, kills every 30 s) + end-screen graphs, score column in the players panel | SC1 | S | ArmyStats (saveable), EndScreen | G4 | 3 |
| G7 | Missing enhancements: Personal Teleporter (both ACUs), UEF Engineering Drones (C-D1/C-D2), UEF SCU Radar Jammer | SC1 | M | `Commander`, blueprints, client models/fx | — | 2 |
| G8 | Missing units: UEF Mech Marine (T1 bot); T3 strategic missile subs (UEF Ace, Cybran; nuke + TML from a submerged hull); Cybran T3 aircraft carrier; T3 sonar platforms; UEF T2 stealth field generator (verify vanilla); Salem destroyer walks on land | SC1 | M | `data/blueprints`, faction json (optional roles), `Naval`, `Strategic` (mobile silo), `Transport` (carrier), models | — | 2 |
| G9 | **Walls** (T1 wall sections, both factions: block movement and shots, Shift-drag lines) | SC1 | S-M | data, Pathfinding blocked cells, Combat projectile hits | — | 2 |
| G10 | Map props: trees (energy) and rocks (mass) to reclaim; trees fall / burn | SC1 | M | PropStore kinds, `Map/` generator, PropRenderer | — | 2 |
| G11 | Leftovers: Atlantis dive (Sub layer), swap an occupied enhancement slot in one click, mobile TML moves into range, shield HP kept on enhancement swap, Mavor arc/priority, crush animation, emergency drop, staging repair cost, fog wrecks/deposits, sandbox selects enemies | SC1 | M (many S) | various | — | 3 |
| G12 | Pathing: exact unit clearance, per-unit MaxSlope, ships steering in narrow channels | SC1 | M | Pathfinding, MovementSystem | — | 3 |
| G13 | FA-only units for UEF/Cybran (Broadsword, Wailer, Continental, Novax, Megalith, Brick, Hoplite, Kennel/Hive); some FA units exist (Percival, Janus, Ravager) | FA | L | data + models | owner Q8 | 3 |
| G14 | Selection/camera keys: all of a type on the map, all land / air / naval / engineers, ACU (select + center), camera bookmarks, jump to last alert | SC1 | S | Hotkeys (new entries), SelectionController, new `Input/SelectionFilters.cs` | — | 2 |
| G15 | Formation move with facing (right-drag), waypoint editing (drag / delete queued order markers), ferry route editing | SC1 | M | Input, new `Orders/QueueEditCommands.cs` (+ one CommandSystem line) | — | 2 |
| G16 | Notifications: minimap flash where attacked, structure lost, construction/upgrade done, unit cap reached, ally under attack | SC1 | S | GameAudio alerts, Minimap, Toasts | — | 2 |
| G17 | Map pings / markers (alert, move, attack; map + minimap; allies and MP; in replays) | SC1 | M | new `UI/Pings`, sim command (additive) | — | 2 |
| G18 | Settings: graphics (shadows, particles, terrain LOD, vsync, fps cap, UI scale), gameplay (edge scroll, camera/zoom speed); Settings from the pause menu (today: fullscreen, resolution, 3 volumes) | SC1 | M | SettingsScreen, AppSettings, renderer flags, InGameScreen | — | 2 |
| G19 | Keybinding editor (rebind any Hotkeys entry, reuse the clash check, saved) | SC1 | M | Hotkeys.cs (overrides), new `Menus/KeyBindingsScreen` | G14, G18 | 2 |
| G20 | Range/intel overlays for the selection (weapon, radar, shield; today only while placing) | SC1 | S | Rendering overlay | — | 3 |
| G21 | Split screen (two views, one strategic) / second monitor | SC1 | L | GameSession, RtsCamera, FrameView, renderers | owner Q9 | 3 |
| G22 | Minimap options (resize, zoom, hide/show resources and units) | SC1 | S | Minimap | — | 3 |
| G23 | Visuals: terrain self-shadows, low-poly unit LOD, death animations (aircraft spiral down, ships list and sink, structures collapse), ACU warp-in at start | SC1 | M | Rendering (+ effect event if needed) | — | 2 |
| G24 | Voice announcer ("Commander under attack", "Nuclear launch detected") | SC1 | M | Audio + voice assets | owner Q7 | 3 |
| G25 | TTF font (raylib's default font has no è/à) | — | S | Ui text | G28 | 2 if G28 |
| G26 | Map files + curated map pack: save/load maps (heightmap, water, deposits, starts, props), 8-12 named maps with thumbnails, map picker in setup/lobby (host sends the map) | SC1 | M-L | `Rts.Sim/Map`, Menus setup/lobby, `Rts.Net` transfer | G10 | 2 |
| G27 | Terrain biomes (temperate, desert, tundra, lava, tropical) | SC1 | S-M | TerrainRenderer palettes + generator option | — | 3 |
| G28 | Italian localization (string table, language setting, UI/tooltips/help/unit names) | — | M-L | whole client + blueprint names | owner Q4, G25 | Q |
| G29 | Map editor (SupCom1 had none in game) | — | L | — | owner Q2 | 3 |
| G30 | AI personalities (Rush / Turtle / Tech / Horde / Adaptive), per slot | SC1 | M | `Rts.Sim/AI` presets, AiDifficulty append, AI LEVEL column | (k) | 2 |
| G31 | AI team play: allied AIs attack together, defend an ally, answer pings, share overflow | SC1 | M | AI | G5, G17, (k) | 2 |
| G32 | AI uses the new mechanics: overcharge, teleport snipes, stealth/cloak/EMP on purpose, capture, missile subs, carrier, walls | SC1 | M | AI | G1, G7, G8, (k) | 2 |
| G33 | AI think ~11 ms on 1024 with 8 AIs; mid-game region graph ~14 ms | — | S-M | AI, Pathfinding | (k) | 2 |
| G34 | Cross-OS determinism: software trig/exp (`DMath`) in all sim code + golden-value tests (Linux↔Windows games) | — | M | `Rts.Sim` wide (mechanical) | — | 2 (1 if Windows players) |
| G35 | Observers (lobby slot, no fog, sends nothing) | SC1 | S | `Rts.Net/Lobby`, Net menus | — | 2 |
| G36 | Multiplayer save/load (host saves, all reload with the same lobby) | SC1 | M | `Rts.Net`, Persistence, Net menus | — | 2 |
| G37 | Reconnect to a running game (host streams a save + command tail) | FAF | M | `Rts.Net` | G36 | 3 |
| G38 | NAT traversal / relay server (today: port forwarding) | SC1 | L | `Rts.Net` + a hosted server | owner Q6 | 3 |
| G39 | Vote to drop, host migration, per-player lobby pings, chat sounds | — | S-M | `Rts.Net`, Net menus | — | 3 |
| G40 | Autosave (every 5 min, rolling 3) | SC1 | S | client Persistence | — | 2 |
| G41 | Saves survive sim layout changes (field-level migration) | — | M | Persistence | — | 3 |
| G42 | Interactive tutorial (on a small scripted-scenario system: triggers, objectives, messages) | SC1 | M | new `Rts.Sim/Scenario`, client | owner Q1 | Q |
| G43 | Campaign (UEF + Cybran operations: scripted missions, dialogue, handmade maps) | SC1 | XL | scenario system, maps, AI scripts | G42, G26 | Q |
| G44 | Mod support (`mods/<name>/` blueprint/faction overlays, enable list, MP mod hash) | SC1 | S-M | BlueprintDb loader, menus, lobby | G45 | Q |
| G45 | **Release packaging**: self-contained Release build (linux-x64; win-x64 zip optional), data next to the exe, saves/replays/settings/screenshots in the user folder (today `saves/` = current dir), icon, `.desktop` launcher + desktop shortcut, version in the main menu, crash log | — | S-M | new `tools/package/`, csproj, SaveStore dirs, Program.cs | — | 1 |

### Proposed waves (2 agents each, disjoint files, one usage window each; start after (k) and (k-navy) merge)
Free keys today: O, I, J, W, F2-F4, F11, F12 (`HotkeyTableTests`). Each agent: AGENT_RULES block, cap 150k.
Agents that change sim results document the new reference hash of `Rts.Bench -- ai 30 256 42 hard,normal`.

**Wave 6: core SupCom mechanics (P1)**
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

**Wave 7: something the owner can launch + everyday UX**
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

**Wave 8: content (P2)**
- 8a Missing units & walls (G8, G9; Atlantis dive from G11). Mech Marine, both strategic missile subs (submerged
  launch), Cybran carrier (reuse the Atlantis cargo/factory), T3 sonar platforms, UEF stealth field generator, Salem
  on land, wall sections. Owns `data/blueprints`, faction json (optional roles: AI ignores what it doesn't know),
  `Naval`, `Strategic`, `Transport`, Pathfinding blocked cells, model partials + `case` lines. Tests: roster tests,
  submerged launch, walls block paths/shots, carrier; `--scene` group; screenshots.
- 8b Maps (G10, G26; G27 if time). Trees/rocks props (reclaim, fall/burn), map file format + 8-12 curated named maps
  with thumbnails, map picker in setup/lobby, host sends the map in MP; biomes. Owns PropStore (additive kinds),
  `Map/` (new MapFile, props placement), PropRenderer, TerrainRenderer palettes, map picker UI, `Rts.Net` map
  transfer. Tests: file round trip, props reclaim, MP map hash, MapScaleTests; screenshots per biome.

**Wave 9: AI on top of the new features + visual polish**
- 9a AI (G30, G31, G32, G33). Personalities as AiConfig presets (setup/lobby column), allied coordination with AIs
  and humans (pings, defend ally, share), uses overcharge/teleport/stealth/cloak/EMP/capture/new units, think cost
  on 1024 8p. Owns `Rts.Sim/AI`, AI_TUNING, the AI LEVEL column. Tests: AI tests + standard matrices (AI_TUNING).
- 9b Visual & audio polish (G23, G22; G24/G25 if the owner says yes). Terrain shadows, unit LOD, death animations,
  ACU warp-in, minimap options. Owns `Rendering/`, `Audio/`, Minimap. Perf: `--perf-log` before/after; screenshots.

**Wave 10: multiplayer robustness + keybindings**
- 10a Multiplayer (G34, G35, G36; G39 if time). `DMath` software trig/exp across the sim (mechanical, one hash
  change, golden-value tests), observers, MP save/load, vote to drop, lobby pings, chat sounds. Owns `Rts.Sim` math
  call sites, `Rts.Net`, Net menus. Tests: Net tests, `Rts.Bench -- mp`, replays still verify.
- 10b Keybindings & client leftovers (G19; G21 if the owner wants it, else G11 client items and G20/G22 leftovers).
  Owns Hotkeys.cs, new KeyBindingsScreen, the listed client files. Tests: rebinding + clash tests; screenshots.

**Wave 11+ (depends on the owner's answers)**: Italian localization (G28 + G25: touches every UI file, so pair it
with a sim-only agent: G12 pathing + G41 save migration); tutorial/scenario system (G42) then campaign (G43); mod
support (G44, after G45); FA units (G13); relay server (G38); reconnect (G37).

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
- [x] Better map generator (merged 2026-10-11): presets Plains/Lakes/Highlands/Canyon/Archipelago, guaranteed land connectivity, tests
- [x] Main menu / game setup screen, victory/defeat screens (merged 2026-10-11):
      GameSession owns fog/wrecks/audio/cursor hints; setup AI slots → `Army.IsAI` + `AiDifficulty` (in-sim AI);
      fog ViewArmy = local human (defeated player "Keep Watching" = fog off); settings: master/effects/music
      volumes (saved) on an app-wide `AudioSystem` (menus: clicks + music); end screen `ArmyStats`
      - [x] AI on new presets: builds and fights everywhere; most NvN games end by commander kill (see Skirmish AI)
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
      - [ ] later: AI uses T2/T3 + upgrades (wave 4), adjacency, T3 engineers build experimentals
- [x] Shields, tactical missiles, artillery structures, strategic nukes + anti-nuke (wave 3, agent b, 2026-10-12):
      `Rts.Sim/Strategic` (shield domes absorb outside shots/splash, collapse + recharge, drop on energy stall; silo
      stockpiles + `OrderType.Launch`; TML cruise missiles, TMD; nuke high arc + two-ring blast that knocks shields
      down, anti-nuke), 9 UEF blueprints (T2/T3 shield, Parashield mobile shield, TML, TMD, nuke silo, anti-nuke,
      T2/T3 artillery), `StrategicApi.LaunchNuke` (used by the sandbox nuke), dome shader + ripples, missile trails,
      nuke explosion (`NukeFx`, shared with the sandbox) + flash + sound, launch mode (L), global launch warning,
      `--scene strategic`
      - [ ] later: AI builds/uses shields, TML, nukes (wave 4)
- [x] Naval (wave 3, agent c, 2026-10-12): naval factories T1→T2→T3 (in-place upgrades), frigate, attack sub,
      destroyer (guns + torpedoes + torpedo defense), cruiser (AA + missiles), battleship, T2 torpedo bomber, T1/T2
      sonar, torpedo launcher; water placement (`NavalRules`, client ghost), torpedoes (run under water, home, die on
      shore), rate-limited torpedo defense, subs only seen by sonar/omni and only hit by torpedoes; Archipelago sea
      lanes + harbor beaches (playable: coast reachable, battleship lanes between starts); client: ship models, wakes,
      torpedo bubble trails, dim submerged subs, sonar ring, `--scene naval`
      - [ ] later: AI builds and uses navy (wave 4), ships steering around each other in narrow channels
            (they only push apart now)
- [x] Experimentals (2026-10-13, wave 4g): data-driven `Experimental` block (crush, delayed death blast, big wreck), Tech 4 + "EXP"
      build-menu tier, built by T3 engineers / SCUs / ACU with T3 Engineering, mobile sites shared by assisting engineers;
      **Fatboy** (mobile factory building T1–T3 land units while moving, bubble shield, gauss cannons, riot gun, flak) and
      **Mavor** (strategic artillery, range 1500, 20000 energy per shot via `EnergyPerShot`); "Experimental detected" alert + sound
      - [ ] later: Monkeylord/Czar-like experimentals (2nd faction), Mavor/Fatboy AI use, crush animation, shield HP kept on enhancement swap,
        Mavor's low arc can hit hills at short range (higher/medium arc), Mavor target priority (it shoots any visible enemy)
- [x] Commander enhancements (2026-10-13, wave 4g): slots LeftArm/RightArm/Back with prerequisites (blueprint variants per slot
      combination), built on itself over time like an upgrade (drain, assist with Guard, cancel refunds, any order interrupts),
      remove = instant without refund; ACU: Heavy Antimatter Cannon, Zephyr Amplifier, Advanced/T3 Engineering, Resource Allocation,
      Personal Shield → Shield Generator Field, Tactical Missile Pack; enhancement panel, model attachments
      - [ ] later: Stop cancels the enhancement, replace an occupied slot in one click, mobile TML moves into range, AI uses enhancements
- [x] Quantum Gateway + Support Commanders (2026-10-13, wave 4g): T3 gateway builds SCUs (T3 engineer + cannon, own enhancements:
      High Explosive Ordnance, Rapid Fabricator, Resource Allocation, Personal/Bubble Shield); SCU death never defeats the army
- [x] Transports/ferry, air staging, engineer assist chains (wave 4, agent h, 2026-10-13): one cargo mechanic
      (`Rts.Sim/Transport`; carried units off the map, untargetable, die with the carrier); C-6 (T1, 6 slots) and C-14
      (T2, 12 slots, AA + riot gun) VTOL transports; orders Load / Unload / Transport-to (cargo split among the
      transports) / Ferry (beacon → drop, repeats) / Dock; aircraft fuel (half speed when empty) + auto-return; T2 Air
      Staging (repairs/refuels, releases when done); Atlantis experimental carrier (builds, carries and launches
      aircraft); engineer assist chains (guard a guard → helps the root builder; guards repair first); client:
      models, cargo hanging under transports, context clicks, order buttons, cargo counter, fuel bar, ferry lines,
      `--scene transport`
      - [ ] later: AI uses transports/ferries/air staging (wave 5), Atlantis dive (the sub toggle exists now: needs
            Sub layer + surfaced by default + no dock/launch/SAM while submerged), staging repair cost, T3 heavy
            transport, emergency drop, gunship VTOL hover
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
  - [ ] later: AI uses stealth/cloak/EMP deliberately (Deceiver escorts, cloaked ACU, Medusa vs groups); Cybran
        T2/T3 and experimentals in AI matches once (k) "AI uses everything" lands
- [x] SupCom polish (2026-10-14, wave 4f): collapsed/paused shields pay no upkeep, recharge only without stall; domes
      absorb bombs/shells from above and bombs released inside; ship hull separation (subs vs subs, surfaced subs vs
      ships); sub dive/surface (D); adjacency bonus (pgens cut energy use, storages boost mex/pgens; placement lines);
      veterancy (+10% HP and regen per level); pause (H: factories, engineers, upgrades, enhancements, silos, fabs,
      shields, radar); capture (E / right-click enemy with engineers); units drive out of factories, aircraft take off
      from the pad; Attack orders dropped when the target is lost in fog; idle aircraft engage; Shift+drag structure
      lines; sandbox selects/orders any army (`--select any:<id>`); default cameras for `--scene strategic[:x]`/naval
      - [ ] later: AI uses capture/pause/adjacency/dive; the Fatboy drops units at its back without a drive-out
        (done in wave 5n: factory exits skip sides with parked units; fire states, captured targets held)
- [x] Mass fabricators (T2/T3), adjacency bonus (wave 4f)

## Phase 4 — Scale and polish
- [x] 1024 maps with 4–8 players, 5k+ units at 60 FPS (wave 5m, 2026-10-16; ARCHITECTURE "Performance & scale"):
      Par worker pool without allocations, stage profiler (bench + client `--perf-log`), JIT warm-up thread + loops
      optimized at once (tick spikes gone: 8 AIs on 1024 max 12 ms, was 217), nav grids at load + parallel graph
      builds, goal-field array reuse (AI games 10 KB/tick, was ~125), intel 0.33 ms with 8 armies; client terrain
      chunk LOD + skirts (GPU 8.2 → 2.8 ms whole 1024 map), instanced particles, deposit/minimap culling (4.8k units
      in battle: CPU 1.6 ms/frame); 6/8-player maps on 512/1024 checked fair for every preset (`MapScaleTests`).
      - [ ] later: one AI think costs ~11 ms on 1024 with 8 AIs (tick 2104 of `ai 40 1024 42 hard 0 8 plains`);
            a region graph built mid-game (first unit of a new layer/size) costs ~14 ms on 1024; low-poly unit
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
            seek checkpoints cost a short hitch every 60 s of game time; autosave; multiplayer reuse (wave 6)
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
