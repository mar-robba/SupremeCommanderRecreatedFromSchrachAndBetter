# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (updated 2026-10-22 21:55 — read this first in a new session)
main has waves 1-4 + (j) factions (merged 2026-10-15) + (n) UX polish, (o) lockstep MP and (m) scale/perf (merged
2026-10-16). Tests: 393 sim + 213 client + 21 net (`tests/Rts.Net.Tests`).
`Rts.Bench -- ai 30 256 42 hard,normal` on main = "WINNER: Player (Hard) after 11:30", hash EDCA8A729F29736E
(the reference for agents that must not change sim results).
Agent ids: `~/.claude/projects/-run-media-marco-RTS/orchestrator/agents-wave5.md` (ids are valid only in
the orchestrator session that started them). Each agent keeps `<worktree>/.claude/handoff.md` and writes
`<worktree>/.claude/agent-report.md` when done. Briefs: HISTORY.md "Wave 5".

**ON RESUME:**
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
    80/96). Left: fix the stalemates, navy + transports (`AiNavy`/`AiTransport` are stubs), T1 artillery balance,
    `AiTechTests`, docs, report. Client/Net tests not yet run on the branch.
    (m) measured one AI think at ~11 ms with 8 AIs on 1024: (k) should keep it down.
  - Both worktrees were clean at the restart. Merge (l) first; then (k) merges
    main and runs Cybran vs UEF matches.
  History: weekly limit 2026-10-14 08:14; session limits 2026-10-15 11:21 and 15:09, 2026-10-16 11:43 (reset 15:40).
- When an agent finishes (report + clean tree): merge it into main (`git merge --no-ff <branch>`), build, both tests,
  a screenshot, the AI match above (same hash unless the agent documents why it changes: (k) and (l) will change it),
  then `git worktree unlock` + `git worktree remove` + `git branch -d`, report → HISTORY.
- If an agent got cut off by a usage limit: commit its WIP in its worktree, then start a fresh agent in the same
  worktree (absolute path, no isolation) with its brief + "continue from .claude/handoff.md".
- Owner question (2026-10-15) "would Rust have been better than .NET?": answered, stay on .NET (WORKFLOW "Owner decisions").
- Housekeeping: a stray `.gitignore` rewrite patch is in `orchestrator/stray-main-gitignore.patch`.

**Next waves (owner: "everything must be done"):**
- **Wave 5** (after f + j are merged), 5 parallel agents:
  1. AI uses everything: T2/T3 upgrades, adjacency, enhancements, navy (Archipelago), transports, shields/TML/nukes/
     anti-nuke, experimentals. Faction-agnostic via roles. Balance pass.
  2. Cybran faction (`data/factions/cybran.json`): full T1–T3, Monkeylord, Soul Ripper, Scathis; stealth/cloak, EMP,
     amphibious walkers; distinct models.
  3. ✅ (merged 2026-10-16) Scale/perf: 1024 maps with 4–8 players, 5k+ units at 60 FPS (LOD, instancing, intel cost, async nav build).
  4. ✅ (merged 2026-10-16) UX polish: the follow-ups above; fire states (hold fire / return fire); Load in the pause menu; viewer F7; save
     migrations.
  5. ✅ (merged 2026-10-16) Lockstep multiplayer: LAN/Internet lobby on top of the replay command log and desync hashes.

Shared contracts (still valid):
- `BuiltBy`: a blueprint adds itself to builders' lists; unknown ids are ignored.
- `UpgradesTo`: in-place upgrade.
- Enums are append-only; edits to shared files stay minimal and additive.
- New models go in new partial files, with only a `case` line in UnitModels.cs.
- Sim state must stay serializable for agent (i): plain fields/arrays/lists, no stored delegates, no static mutable state.

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
- [ ] Second faction (Cybran-like) with distinct visuals and units
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
