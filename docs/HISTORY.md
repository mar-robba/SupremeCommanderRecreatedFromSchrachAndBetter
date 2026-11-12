# Development history

What each wave and each agent delivered, taken from the agents' final reports at merge time. Each section has: what
was delivered, how to try it, the tests added, the numbers measured, and the known issues **at merge time**. The
follow-ups that are still open are tracked in [ROADMAP.md](ROADMAP.md); how each system works is in
[ARCHITECTURE.md](ARCHITECTURE.md). The foundation, wave 1 and the Phase 2 features (2026-10-10/11) are summarized
in the ROADMAP phases. Their agents left no reports.

How the waves are organized: [WORKFLOW.md](WORKFLOW.md). After each merge, the orchestrator adds the agent's report
here.

- [Wave 3 (2026-10-11 → 12)](#wave-3)
  - [(a) T2/T3 tech and upgrades](#a-t2t3-tech-and-upgrades)
  - [(b) Shields and strategic weapons](#b-shields-and-strategic-weapons)
  - [(c) Naval](#c-naval)
  - [(e) Sandbox mode](#e-sandbox-mode)
- [Wave 4 (2026-10-12 → 14)](#wave-4)
  - [(d) Skirmish AI](#d-skirmish-ai)
  - [(g) Experimentals, commander enhancements, SCUs](#g-experimentals-commander-enhancements-scus)
  - [(h) Transports, cargo, air staging, Atlantis, assist chains](#h-transports-cargo-air-staging-atlantis-assist-chains)
  - [(i) Save/load, replays, desync detection](#i-saveload-replays-desync-detection)
  - [(f) SupCom polish (+ merge with h)](#f-supcom-polish--merge-with-h)
- [Pre-wave 5: (j) faction infrastructure](#pre-wave-5)
- [Wave 5 (in progress): the briefs](#wave-5-in-progress)
- [Wave 10: the briefs (re-cut 2026-11-12)](#wave-10-multiplayer-robustness--keybindings-briefs-re-cut-2026-11-12)

---

## Wave 3

Five agents: (a) T2/T3 tech, (b) strategic, (c) naval, (d) skirmish AI (it continued into wave 4, see there),
(e) sandbox. Merged into main on 2026-10-12, except (d). After wave 3: 229 sim tests + 158 client tests.

### (a) T2/T3 tech and upgrades
*2026-10-12 · ARCHITECTURE "Tech tiers & upgrades"*

- **Delivered**:
  - the in-place upgrade mechanism (`UpgradeSystem`): the structure builds its own upgrade, engineers can assist,
    a factory's queue pauses, a mex keeps producing, Cancel refunds, the unit keeps its handle;
  - upgrade chains: land/air factory T1→T2→T3, mex T1→T2→T3, radar T1→T2→omni;
  - 30 UEF T2/T3 blueprints: engineers, factories, mex, pgen, mass fab, PD (Triad/Ravager), flak, SAM, omni,
    Pillar, Riptide, Brimstone MML, Sky Boxer, Titan, Percival, Demolisher, gunship, Janus, Wasp, Ambassador, spy
    plane;
  - client: T2/T3 models, upgrade tile + U hotkey, upgrade progress bars and hologram, build-menu tier pills (T).
- **Try it**: `dotnet run --project src/Rts.Client -- --tech-demo` (prints the row positions; e.g.
  `--camera 40,58,48` for the factories, `--camera 40,82,34` for the units).
- **Tests**: `UpgradeTests` (12): cost, handle, health fraction, mex producing, factory pause/resume, cancel refund,
  assist speed, rejections, radar range, T2 engineer builds, factory tiers, blueprint sanity, determinism 1 vs 8
  threads, every tech blueprint spawns.
- **Numbers**: bench `3000 300` avg 4.07–4.86 ms/tick (baseline 4.46, 5118 units). `ai 20 256 42`: Player wins at
  19:49 with the same hash as before (the AI is untouched).

### (b) Shields and strategic weapons
*2026-10-12 · ARCHITECTURE "Shields & strategic weapons"*

- **Delivered**:
  - 9 blueprints: T2/T3 shield generators, Parashield mobile shield, TML, TMD, nuke silo, anti-nuke, T2/T3
    artillery installations;
  - shield domes: they absorb shots entering from outside; shots fired from inside pass out; outside splash doesn't
    reach units inside; beams are blocked; an energy stall collapses them; recharge delay;
  - silo stockpiles paid up front, `OrderType.Launch`;
  - TML cruise missiles; nuke on a high arc with a two-ring blast that knocks shields down; TMD and anti-nuke
    interceptors;
  - `StrategicApi.LaunchNuke` (also used by the sandbox);
  - client: dome shader with hit ripples, missile trails, launch mode (L), global launch warning + klaxon.
- **Try it**: `--scene strategic[:shields|:tml|:nuke]` (add `--nofog`).
- **Tests**: `StrategicTests` (shields, TML/TMD, nuke, anti-nuke, LaunchNuke API, nuke through fog, build lists,
  determinism), `StrategicUiTests`. Sim 189 + client 95 green.
- **Numbers**: bench `3000 300` avg 3.66 ms/tick (before: ~3.9–4.2). `ai 20 256 42`: Player wins at 19:49.
- **Known issues at merge**:
  - collapsed shields still paid upkeep (fixed in 4f);
  - bombers flew above domes (fixed in 4f);
  - the nuke silo draws 1000 E/s while building;
  - TMD/anti-nuke weapons have `TargetLayers ["Air"]` only to pass the data test (they are manual and never
    auto-fire);
  - the AI doesn't use strategic units.

### (c) Naval
*2026-10-12 · ARCHITECTURE "Naval", "Map generator"*

- **Delivered**:
  - 12 blueprints: naval factories T1–T3 (upgrade chain), frigate, attack sub, destroyer, cruiser, battleship,
    T2 torpedo bomber, T1/T2 sonar, torpedo launcher;
  - torpedoes: they run under water, home on the target, die near the shore;
  - torpedo defense: destroys one torpedo per cooldown;
  - subs: only seen by sonar/omni, only hit by torpedoes;
  - Archipelago: sea lanes wide enough for a battleship between all starts, and harbor beaches from each start
    plateau down to the sea;
  - client: ship models, wakes, torpedo bubble trails, dim submerged subs, sonar ring.
- **Try it**: `--scene naval` (`--select uef_t1_naval_factory` for the factory menu).
- **Tests**: `NavalTests`, including island size, a naval site near every start, a battleship lane between all
  starts, the commander builds a naval factory, `NavalCombatIsDeterministicAcrossThreadCounts`. Sim 190 + client
  130 green.
- **Numbers**: bench `3000 300` avg 4.82 ms/tick on a loaded machine (main 7.27 in the same conditions: no
  regression).
- **Known issues at merge**:
  - the AI builds no navy;
  - `--gallery` shows naval shapes on land;
  - the T1 sonar ring is off-screen at normal zoom;
  - ships could overlap (fixed in 4f: hull separation).

### (e) Sandbox mode
*2026-10-12 · ARCHITECTURE "Sandbox mode"*

- **Delivered**:
  - main menu "Sandbox", `--sandbox`, `--exec "commands"`;
  - side panel (F9) with every blueprint (grouped, searchable, 1/10/50/100 per click, any army, construction sites);
  - cheats: infinite resources, instant build, build anywhere, reveal map;
  - AI on/off and difficulty per army, victory on/off (by default the game never ends), speed/pause;
  - selection actions: kill/delete/heal/damage/give/finish, clear area;
  - nuke effect (`NukeFx`: flash, shockwave, fireball, mushroom cloud, scorch, deep boom; wrecks vaporized);
  - console (F10 or `) with fuzzy unit names, history, Tab completion, help overlay.
- **Console commands** (armies: `me`, `enemy`, `enemy2`, `p2`, `armyN`, `all`): `spawn <unit> [count] [army]
  [at x,z] [site]`, `nuke [x,z]`, `money / instabuild / buildanywhere [army|all] [on|off]`, `fog [on|off]`,
  `reveal`, `ai <army|all> on|off [easy|normal|hard]`, `kill`, `delete`, `heal`, `damage [pct]`,
  `give/owner <army>`, `complete`, `clear [radius] [at x,z]`, `speed <x>` (0.125..8), `pause [on|off]`,
  `victory on|off`, `camera x,z [dist]`, `wait <frames>`, `panel [on|off]`, `console [on|off]`, `cls`, `help`.
- **Try it**: `--sandbox --exec "spawn tank 100 enemy at 140,128; wait 30; nuke 140,128"`.
- **Tests**: 16 sim sandbox tests, `SandboxConsoleTests` (parser), later `SandboxNukeTests`. Sim 171 + client 118.
- **Known issues at merge**:
  - box-select picked only own units (fixed in 4f: any-army selection);
  - the perf overlay covers the end of the console header;
  - the F1/F2 debug spawn is active outside the sandbox too.
  - At the merge with (b), `nuke` became a real strategic missile and `nuke instant` the blast alone; both use one
    `EffectKind.Nuke`.

---

## Wave 4

Started 2026-10-12 evening, merged 2026-10-13/14 in the order g, i, h, d, f. Several agents hit the 300k context
cap and were replaced by fresh agents in the same worktree (see WORKFLOW). After the f merge: 342 sim tests + 193
client tests.

### (d) Skirmish AI
*finished 2026-10-14 · ARCHITECTURE "Skirmish AI" · experiments: [AI_TUNING.md](AI_TUNING.md)*

- **Delivered**:
  - fog-fair AI: ghosts of seen structures, threat memory, scouts, radar; no difficulty cheats;
  - staged attacks along the walking route, reinforcement batches, regroup, base recall, late-game push and
    commander snipes;
  - Hard's edge comes from a stronger opening (5 engineers, `MexChain 1`), an artillery-heavy mix
    (`ArtilleryShare 0.55`, the biggest lever) and micro (focus fire, pull-back, raids, snipes);
  - AI state survives save/load.
- **Sim change outside the AI**: `Combat/LineOfFire.cs`. Non-forced targets need a terrain line of fire; shells
  need an arc solution. Missiles, bombs, flak, torpedoes, silo weapons and forced targets are exempt.
- **Try it**: `dotnet run -c Release --project tools/Rts.Bench -- ai 30 256 42 hard,normal`; watch one in the
  client with `--setup --autostart --preset highlands --seed 7`.
- **Tests**: `AiIntelTests`, `LineOfFireTests`, `AiMicroTests` (focus fire, difficulty knobs), `AiSaveLoadTests`
  (save mid-battle, identical continuation). Sim 264 + client 168.
- **Numbers**: NvN 83/96 games end by commander kill; Hard v Normal 166-8-18 / 192; Easy loses 23/24; 4-player FFAs
  on 512 end in 23–30 min. AI cost 0.02–0.03 ms/tick per AI. Details in AI_TUNING.
- **Known issues at merge**:
  - some Highlands/Canyon NvN games stalemate; only 69% of Hard v Hard games end;
  - T1 mobile artillery looks too cost-efficient;
  - one-off 50–110 ms spike on the first AI think (JIT);
  - no T2/T3, navy or strategic weapons in the AI yet.

### (g) Experimentals, commander enhancements, SCUs
*2026-10-13 · ARCHITECTURE "Experimentals & commanders"*

- **Delivered**:
  - Experimentals (Tech 4, "EXP" build tier) are built by T3 engineers, SCUs and an ACU with T3 Engineering;
    assisting engineers share one mobile site;
  - a generic `Experimental` blueprint block: crushing small enemy land units, a delayed death blast, a big wreck;
  - an "Experimental detected" banner + klaxon (fog-aware);
  - **Fatboy**: builds T1–T3 land units while moving (they exit at its back), bubble shield, gauss cannons, riot
    gun, flak;
  - **Mavor**: range 1500, 20000 energy per shot (`EnergyPerShot`, charged before firing);
  - **ACU enhancements** (8; slots, prerequisites, removable): blueprint variants built like upgrades (drain,
    assist with Guard, cancel refunds, a new order interrupts); enhancement panel and model attachments;
  - **Quantum Gateway** builds SCUs (with their own enhancements). An SCU's death never defeats the army.
- **Design choices worth knowing**:
  - each enhancement combination is a generated blueprint variant (`base+Enh1+Enh2`), so installing one just swaps
    the unit's blueprint and every system reads the new data for free;
  - enhancement progress reuses `UpgradeState`, so there is no new order type;
  - no new values in shared enums;
  - the Fatboy's `Radius` stays ≤ 3.0 (see DEV_GUIDE "Sim pitfalls").
- **Try it**: `--scene experimental[:mavor|:fog]`; sandbox `--exec "spawn uef_x_fatboy me at 120,128; spawn
  uef_commander+T3Engineering+HeavyAntimatterCannon+ShieldGeneratorField me at 110,128"`.
- **Tests**: `ExperimentalTests` (7), `EnhancementTests` (6). Sim 242 + client 163.
- **Numbers**: bench `2000 300` same as main.
- **Known issues at merge**:
  - the Mavor's low arc can hit hills at short range, and it shoots any visible enemy;
  - Stop doesn't cancel an enhancement;
  - an occupied slot must be cleared (right-click) before installing another enhancement;
  - shield HP refills on any enhancement swap;
  - a mobile TML doesn't move into range;
  - `--select` can't pre-select variant ids.

### (h) Transports, cargo, air staging, Atlantis, assist chains
*2026-10-13 · ARCHITECTURE "Transports & cargo"*

- **Delivered**:
  - one cargo mechanic: carried units are off the map, untargetable, and die with the carrier;
  - C-6 (T1, 6 slots) and C-14 (T2, 12 slots, AA + riot gun) VTOL transports;
  - orders Load / Unload / Transport-to (cargo split across the selected transports) / Ferry (pickup beacon → drop
    point, repeating) / Dock;
  - aircraft fuel (half speed when empty); damaged or empty idle aircraft return to the T2 Air Staging, which
    repairs, refuels and releases them;
  - the Atlantis (T4): builds, carries and launches aircraft; can only be started on water;
  - assist chains resolve to the root builder; a guard repairs its damaged guarded unit first;
  - client: 4 models, cargo hanging under transports, parked aircraft, right-click context, cargo/fuel bars, ferry
    lines.
- **Hotkeys**: U Unload/Launch, E Transport-to, F Ferry, D Dock. E and D clashed with (f); the merge made them
  context-dependent.
- **Try it**: `--scene transport`; sandbox `--exec "spawn uef_t1_transport me at 130,128; spawn tank 4 me at
  134,128"`.
- **Tests**: `TransportTests`, `AssistChainTests`, `AtlantisCanOnlyBeStartedOnWater`,
  `SaveLoadWithCargoContinuesIdentically`. Sim 271 + client 177.
- **Numbers**: bench `2000 300` 3.6–4.1 ms/tick vs main 4.6 (no regression).
- **Known issues at merge**:
  - the Atlantis can't dive;
  - the AI doesn't use transports;
  - the order buttons are cramped with a loaded transport selected;
  - the cargo/fuel rows can cut off the unit panel stats;
  - minimap clicks don't work with the new order modes;
  - the C-14 hasn't been checked close up.

### (i) Save/load, replays, desync detection
*2026-10-14 · ARCHITECTURE "Save/load & replays" · CLAUDE.md hard rule 9*

- **Delivered**:
  - an automatic deep serializer saves the whole sim: save → load → continue gives identical hashes and bytes at 1
    and 4 threads;
  - every game is recorded (commands + a hash every 10 ticks), written at the end and on quit;
  - Save Game dialog, F5/F6 quicksave/quickload (also in the sandbox); the Load Game and Replays lists say why a
    file is unusable;
  - replay viewer: pause, 0.5–16×, seek, fog per army, DESYNC banner.
- **Try it**: `--saves`, `--replays`, `--load FILE`, `--replay FILE [--seek T]`; headless
  `Rts.Bench -- replay <file>` (exit code 1 = desync); `Rts.Bench -- savebench 25 512 4 plains 42 1500`.
- **Tests**:
  - `SaveCoverageTests`: a guard that names the unsaveable field and the fix;
  - `PersistenceTests`: a rich scenario (4 AIs, every blueprint, silo launches, a nuke, upgrades), saved at tick
    1100 and run to 2200, with identical hashes and bytes;
  - `ReplayTests` (5): re-simulation, tampered command = desync, a save keeps the replay, damaged files fail
    cleanly;
  - `SaveLoadUiTests` (5). Sim 253 + client 168.
- **Numbers** (512 map, 4 AIs, 25 min, 6641 units): save file 1.8 MB (raw 29.5 MB); capture 86 ms on the sim
  thread; encode 99 ms in the background; load 0.3 s; replay 165 KB; headless verify 1493 ticks/s, all 1505
  hashes OK; recording 0.009 ms/tick.
- **Known issues at merge**:
  - no migrations: any change to the saved data layout makes older files "incompatible";
  - no Load Game in the pause menu;
  - F7 doesn't update the viewer's view selector;
  - seek checkpoints cause a short hitch every 60 s of game time.

### (f) SupCom polish (+ merge with h)
*2026-10-14 · ARCHITECTURE "Economy", "Combat", "Shields", "Naval", "Client"*

- **Delivered**:
  - shields: no upkeep when collapsed or paused; recharge only without an energy stall; domes absorb bombs and
    shells from above, and bombs released inside an enemy dome;
  - ships: hull separation (subs collide only with subs, surfaced subs with ships); sub dive/surface (D);
  - adjacency bonus: pgens cut their neighbors' energy use, storages boost mex/pgens; placement preview lines;
  - veterancy: +10% max HP per level, plus regen;
  - pause (H): factories, engineers, upgrades, enhancements, silos, fabs, shields, radar;
  - capture (E, or right-click an enemy with engineers);
  - units drive out of factories; aircraft take off from the pad;
  - Attack orders are dropped when the target is lost in fog; idle aircraft engage;
  - Shift+drag structure lines;
  - the sandbox can select and order any army (`--select any:<id>`);
  - default cameras for the `--scene` variants.
- **Merge with (h)** (done by a merge agent): shared hotkeys (`Input/SharedHotkeys.cs`):
  - E = transport-to when transports are selected, else capture;
  - D = dive for subs AND dock for aircraft.
  Right-click priority:
  - enemy → capture (builders) / attack;
  - own unit → Load / Pick up / Dock before repair/assist;
  - ground → reclaim, then Unload, then move.
  A captured carrier takes its cargo along (not when carrying a commander). The Atlantis is pausable. Boarding ends
  a factory roll-out.
- **Try it**: `--sandbox --nofog --exec "spawn tank 8 enemy at 140,128" --select any:uef_t1_tank`;
  `--ui-place <id>@x,z`.
- **Tests**: `ShieldPolishTests`, `NavalPolishTests`, `SubmarineTests`, `AdjacencyTests`, `AdjacencyPreviewTests`,
  `VeterancyTests`, `PauseTests`, `CaptureTests`, `FogAttackTests`, `FactoryExitTests`, `PlacementLineTests`,
  `SharedHotkeysTests`, `TransportPolishIntegrationTests`. After the merge: sim 342 + client 193.
- **Numbers**: bench `2000 300`: branch 3.98 vs main 4.14 ms/tick; after the merge with h: 4.36.
- **Known issues at merge**:
  - units leaving a factory don't avoid units parked at the exit (they repath after 8 s);
  - own units keep shooting a unit being captured (no hold-fire state);
  - the AI doesn't use capture/pause/adjacency/dive;
  - the client capture cursor doesn't know that a transport carries a commander (the sim rejects it);
  - foreign-army selections in the sandbox get no transport context orders;
  - the order panel is cramped with many buttons.

---

## Pre-wave 5

### (j) Faction infrastructure
*merged 2026-10-15*

Goal: make a second faction possible:
- `data/factions/<id>.json` with `Roles` (role → blueprint id), a `FactionDb`, `UnitBlueprint.Faction`,
  `Army.Faction`;
- the AI and `StrategicApi` ask for roles instead of hard-coded `uef_*` ids;
- a faction selector in setup + `--faction` / `--factions`; a faction filter in the sandbox;
- an ARCHITECTURE "Factions" contract.

**Delivered** (merged 2026-10-15; two agents, the second cut by the weekly usage limit before its report, so the
orchestrator finished the checks):
- `FactionDb` + `data/factions/uef.json`: 70 required + 15 optional roles (`FactionRoles`), every UEF unit mapped
  (incl. transports, air staging, Atlantis); loading rejects unknown roles, missing blueprints, bad colors, duplicate
  ids and the reserved id `random`.
- `SkirmishSettings.Factions` → `Army.Faction` (saved; `random` resolved from the seed, the same on every machine);
  `ArmyView.Faction`, `PlayerInfo.Faction`; the army starts with its faction's `Commander`.
- The AI (`AiBrain` `Bp*` fields) and `StrategicApi.NukeSiloOf` resolve units by role; `"uef_` no longer appears in
  sim code.
- Client: FACTION column in the setup screen (validated in `GameConfig`), `--faction ID` / `--factions a,b,...`,
  sandbox FACTION filter, `ClientGameData`; `Rts.Bench ai ... [factions]`.
- Docs: ARCHITECTURE "Factions" (the contract for (k) and (l)), CLAUDE.md rule 10 + CLI lines.
- Tests: `FactionTests` (sim), `FactionSetupTests` (client). At merge: 355 sim + 199 client tests green.
- Check: `Rts.Bench -- ai 30 256 42 hard,normal` = main exactly (WINNER Hard after 11:30, hash EDCA8A729F29736E).
- Known: only UEF exists, so the selector offers UEF / Random until (l) adds Cybran.

## Wave 5 (merged 2026-10-16 → 10-24)

Started 2026-10-14 ~00:45: (m), (n) and (o) started after the (f) merge. (k) and (l) start after (j) is merged,
because they depend on faction roles. Below are the briefs the agents received: each prompt = brief +
[AGENT_RULES.md](AGENT_RULES.md). The reports will be added under each brief when the agents are merged.

### (k) AI uses everything — `src/Rts.Sim/AI` (+ balance in `data/blueprints`)
*brief; starts after (j)*

The skirmish AI (wave 4d) only plays T1 land plus some air/PD/AA. Make it use everything, faction-agnostic through
the faction roles. New roles are append-only; list them in the report so the Cybran agent fills them too.
- **Tech**:
  - factory and mex upgrades T2/T3, higher-tier engineers, T2/T3 power, mass fabs late, storage;
  - adjacency-aware placement (mex + storage, pgens around factories/fabs).
- **Commanders**: commander enhancements; SCUs from the Quantum Gateway (late).
- **Navy**:
  - naval factories on Lakes/Archipelago when the enemy is reachable by water or not by land;
  - frigates, subs, destroyers, cruisers, battleships;
  - anti-naval defense.
- **Transports**: drops on islands (Archipelago) and harass drops; air staging + fuel.
- **Defense**:
  - shields over the base core;
  - T2/T3 PD/AA;
  - TMD vs enemy TML;
  - anti-nuke when an enemy SML is seen (intel-fair).
- **Strategic**:
  - TML on enemy defenses;
  - T2/T3 artillery;
  - SML + nukes on the best-value cluster;
  - experimentals when the economy allows.
- **Cheap extras**: capture, pause and dive where they are cheap to add.
- **Balance pass**:
  - T1 mobile artillery is too cost-efficient; rebalance Hard's mix accordingly;
  - fix the Highlands/Canyon NvN stalemates;
  - Hard vs Hard should end in more than 85% of games.
- **Constraints**: AI cost below 0.1 ms/tick per AI; determinism.
- **Validation**:
  - `Rts.Bench ai` matrices (see AI_TUNING): land presets × seeds, Lakes/Archipelago 512, 4-player FFA on 512;
  - a summary line in the bench output: max tier, number of experimentals, nukes, navy;
  - tests: T2 by minute X on a fixed seed; navy on Archipelago; anti-nuke after seeing an SML; save/load
    mid-game still identical.

**Delivered** (merged 2026-10-24, 344e2b5; eleven coordinators from 2026-10-14 to 10-24, cut by usage limits and the
context cap many times; navy + transports split off to (k-navy), merged after it). Final report of coordinator 11:
- **Works:** the AI plays T2/T3 (upgrades, enhancements, shields, TML, strategic projects, SCUs, air T2/T3) through
  faction roles, with a tech budget plus a pressure switch; duel tool (`Rts.Bench duel` / `duelmatrix`); land balance
  pass (AI_TUNING "Unit balance"); Cybran/UEF parity; Normal defaults and Hard preset; lab scripts in `tools/ai-lab`.
- **Last run:** UEF T1 tank MuzzleVelocity 30 -> 40, hp 305 (26d271f): Cybran v UEF 51-21 -> 39-35 (96 games,
  45 min). Hard preset: artillery 0.05, 8/18 land factories, army share 0.6 (d52b553); 11 Hard levers measured, a
  threat-weighted focus fire was reverted.
- **Try:** `MIN=45 DIFF=Hard bash tools/ai-lab/specsweep.sh <tag> tools/ai-lab/specs/hvn.txt <bin> -`;
  `Rts.Bench duelmatrix uef cybran`.
- **Numbers (45-min cap):** NvN 64/96 finished (21/69 lopsided games stalled; goal 80/96); HvH 76/96 (goal 85 %);
  Hard v Normal 102-52 (154/192 finished); tech v T1-only 60 % (goal 65 %); Cybran v UEF 39-35. Reference match
  `ai 30 256 42 hard,normal`: Hard wins at 15:35, hash **4E3F22FD0F8EED11** (verified on main after the merge). Perf:
  `2000 300` avg 1.75-1.97 ms vs main 1.72-1.80 (noise); AI 0.017 ms/tick.
- **Tests:** Sim 419, Client 227, Net 21 green. Tests read the tank's hp from its blueprint; AiTechTests uses seed 6.
- **Hotkeys:** none. **Shared files:** none (data, AiConfig, tests, docs).
- **Known issues:** goals (a) stalls, (b) finished games and (c) tech v T1-only are not met (AI_TUNING "Hard's edge",
  "Final numbers of agent (k)"): the stalls need an AI decision change, not more config values (wave 9a). Hard is
  about 2:1 against Normal; main's old 96 % came from the T1 artillery exploit. Late games lean Cybran (UEF splits its
  T2/T3 mass across shields, siege bots and fighter-bombers).
- **Handoff review:** the note was good. It lacked that blueprint data is read live from the worktree (data tests
  need no rebuild); `pgrep -f` wait loops matched themselves (wait on a result line instead); 45-min matrices take
  8-12 min, not 2-3.5.

### (k-navy) Navy + transports AI — `AiNavy*`, `AiTransport*`, `AiConfig.Navy.cs`
*brief (2026-10-23, split off from (k)): naval factories and fleets on water maps, island drops with transports
(landing zones, ferry, air staging), harass drops; own worktree branched from the (k) branch, merged after it.*

**Delivered** (merged 2026-10-24 after (k); three agents: k-navy 1 stopped at 193k after 11.5 min with the navy
written, k-navy 2 wrote transports and tests, k-navy 3 finished). Report of k-navy 3:
- **Works:** naval factories and fleets when the enemy is reachable by water; island mode (no land path to any enemy
  start): air factory before the mass-stall gate, transports queued behind interceptors, landing zones scored by
  anti-air along the flight and kept away from the enemy commander's last seen position (`DropCommanderAvoid=3`);
  Hard landed groups raid extractors first (`DropMexValueHard=80`). Engineers are amphibious, so no engineer ferries.
- **Tried and reverted** (AI_TUNING "Navy and transports"): synchronized drop waves with a strength gate, pausing the
  ferry while the landing zone is contested, freezing the landing zone: worse or noise.
- **Numbers:** Archipelago 256 NvN 30 min 7/24 finished (4/24 without avoidance; 0/6 before the agent), 512 1/12.
  Land games unchanged: reference match `ai 30 256 42 hard,normal` still 4E3F22FD0F8EED11 after the merge. AI cost
  0.007 ms/tick (256), 0.014 (512), no per-think allocations.
- **Tests:** Sim 423 (+4: AiNavy/AiTransport tests), Client 227, Net 21. **Shared files:** none.
- **Known issues:** Hard on Archipelago builds few land units and drops 3x less than Normal (loses 7 of 8 finished
  games to Normal: economy/production); small-island drops still die fast to home armies.
- **Handoff review:** k-navy 1's note missed that air factories never got built on island maps and that engineer jobs
  after the mass-stall gate never ran (k-navy 2 found both); k-navy 2's match runner lived only in the scratchpad
  (lost, rewritten); "re-measure after the k merge" didn't say (k) kept moving (two more merges).

### (l) Cybran faction — `data/factions/cybran.json`, `cybran_*` blueprints, new sim features, models
*brief; starts after (j)*

A full SupCom1 Cybran roster, mirroring UEF role by role, with SupCom1 names and balance close to the UEF
equivalents.
- **Commanders and engineers**: ACU with enhancements, SCU, engineers T1–T3.
- **Structures**:
  - economy;
  - land/air/naval factories T1–T3;
  - PD/AA/TMD/torpedo defense;
  - shields (the ED1–ED5 upgrade chain);
  - radar/sonar, stealth field generator;
  - TML, SML, anti-nuke, T2/T3 artillery.
- **Land**: Mole, Hunter, Mantis, Medusa, Sky Slammer, Rhino, Wagner (amphibious), Viper, Banger, Deceiver (mobile
  stealth), Loyalist, Trebuchet.
- **Air**: Hummingbird, Prowler, Zeus, a gunship, Skyhook transport, Gemini ASF, Revenant strategic bomber, Spook.
- **Navy**: Trident, Sliver, Salem, Siren, Galaxy.
- **Experimentals**: Monkeylord (spider, heavy laser, walks under water), Soul Ripper (heavy gunship), Scathis
  (mobile rapid artillery).
- **New generic sim features** (data-driven, reusable by other factions):
  - stealth: radar stealth, sonar stealth, stealth fields;
  - cloak: hidden from vision unless omni, with energy upkeep; ghosts and blips must respect it;
  - EMP stun: no move, fire or build for N seconds;
  - amphibious walkers on the seabed;
  - the Cybran shield upgrade chain.
- **Integration**: players and the AI can pick Cybran (every role in `cybran.json` filled).
- **Look**: distinct procedural models (angular, spidery legs, red/dark accents in team color), icons; the sandbox
  can spawn every unit.
- **Tests**:
  - every Cybran blueprint loads and is buildable through `BuiltBy`; every role is filled;
  - stealth/cloak hide from radar/vision but not from omni;
  - EMP stun; an amphibious walker crosses water;
  - a Cybran vs UEF AI match runs to a result.
  Screenshots with `--scene cybran`.

**Delivered** (merged 2026-10-23; four coordinators and about ten sub-agents, cut by usage limits on 10-15, 10-16
and 10-22): the second faction, playable by humans and the AI.
- Data: `data/factions/cybran.json` plus 80 `cybran_*` blueprints (the full SupCom1 roster, experimentals Monkeylord,
  Soul Ripper and Scathis, the ED1→ED5 shield upgrade chain). New optional roles: `light_assault_t1`,
  `shield_ed2_t2` … `shield_ed5_t3`.
- Sim: radar and sonar stealth, cloak, stealth fields (active while complete, not paused, energy not stalled; upkeep
  `MaintenanceEnergy`), EMP stun (`StunDuration/StunRadius/StunMaxTech`; commanders and aircraft immune), seabed
  walkers (`Amphibious`, only torpedoes or `FiresSubmerged` weapons reach them), `UnitView.Status` flags in the
  snapshot.
- Client: distinct `cy_*` models for every unit (angular, spidery legs, red/dark accents in team colour), status
  visuals (cloaked = translucent shimmer, stunned = blue-white sparks, stealth field = violet ring on the terrain),
  `--scene cybran[:land|:air|:naval|:structures1..3|:experimental]`.
- Tests: `CybranRosterTests`, `StealthTests`, `EmpAmphibiousShieldTests`, `CybranModelTests`, `CybranSceneTests`,
  `StatusFxTests`. At merge: 416 sim + 227 client + 21 net, all green.
- Numbers: `ai 30 256 42 hard,normal` plays the same game as before (Hard 11:30), hash EC8B7A06DF52317E instead of
  EDCA8A729F29736E, because the `cybran_*` files shift the blueprint index (DEV_GUIDE "Sim pitfalls"). Cybran vs UEF,
  Hard vs Hard on 256, 18 games: Cybran 8, UEF 5, 5 draws. Bench `2000 300`: no regression.
- Still open: the AI doesn't use stealth, cloak or EMP on purpose (both AIs field only T1 until (k) merges); a
  cloaked unit upgrading in place loses its upgrade hologram; the EMP sparks look pale on light hulls.
- Handoff review: the notes were good (the C3a list was exact). They lacked that the parked planes are drawn but tiny
  at the default air camera, and that a bench copied outside the repo needs `data/` next to it.

### (m) Scale/perf — renderer, sim hot paths, map/setup scale
*brief; started 2026-10-14*

Goal: 1024 maps with 4–8 players and 5k+ units at 60 FPS on the Radeon 780M, sim tick below 10 ms in the worst
case.
- **Measure first**: Rts.Bench, `--demo` with many units, an FPS/frame-time overlay or log.
- **Client**:
  - unit LOD (low-poly far models, icons at strategic zoom);
  - GPU instancing for every unit/projectile/effect draw;
  - frustum culling, terrain chunk LOD;
  - fewer allocations per frame;
  - cheaper snapshot interpolation and minimap/fog texture updates.
- **Sim**:
  - intel below 1 ms on 1024 with 8 armies;
  - async/incremental navigation build (first tick ~180 ms on 1024);
  - snapshot building;
  - ~0 GC allocations per tick;
  - AI cost with 8 AIs; pathing bursts.
- **Maps and setup**: 8-player starts on 512/1024 for every preset (symmetry, fairness); the setup screen supports
  8 slots.
- **Deliverable**: a before/after perf table. Sim results must not change (identical AI match hashes) unless a
  documented fix requires it.

**Delivered** (merged 2026-10-16; three agents and three sub-agents, cut twice by usage limits): a 1024 map with
8 players and 4.8k units runs at 60 FPS; the worst sim tick went from 217 ms to about 12 ms.
- Sim: the `Par` worker pool no longer allocates; per-stage profiler (`Rts.Bench`, `BENCH_ALLOC=1`,
  `BENCH_WARMUP=1`); nav grids built at load and missing region graphs built in parallel; `SimWarmup` compiles the sim
  code on a background thread when the client starts; `QuickJitForLoops=false` in the client and the bench (the
  10–30 ms tick spikes were loops running unoptimized JIT code, not GC); goal-field arrays reused.
- Client: terrain chunk LOD with skirts (no cracks), instanced particles, deposit markers culled, a minimap dot per
  pixel, `--perf-log`, `--terrain-lod 0..3`.
- Maps: 6- and 8-player maps on 512 and 1024, fair for every preset.
- Tests: `MapScaleTests`, `ScalePerfTests` (the warm-up thread leaves a game's hash unchanged). At merge: 393 sim +
  213 client + 21 net tests green. `ai 30 256 42 hard,normal` = main exactly (hash EDCA8A729F29736E), other bench
  hashes unchanged.

| (same machine, interleaved runs) | main | m |
|---|---|---|
| `2000 300` avg / p95 / max ms | 3.5–4.5 / 5.8–9.2 / 25 | 1.7–2.1 / 2.4–3.4 / 9.5 |
| `700 400 0 1024 8` avg / p95 / max | 3.9–4.6 / 10.6–12.3 / 34 | 2.4 / 4.5–4.9 / 12–16 (warm-up: 2.0 / 3.8 / 8.5) |
| first tick on 1024 | ~210 ms | 15 ms (with warm-up) |
| intel, 8 armies on 1024 | 1.1–1.35 ms | 0.33 ms |
| 8 AIs on 1024, 40 min: avg / p95 / max, alloc | 1.64 / 3.68 / 217, ~125 KB/tick (main 8146fdd) | 0.96 / 2.21 / 12.1, 10 KB/tick |
| client, 4.8k units: CPU (close view) / GPU (whole map) | 3.2 / 8.2 ms | 1.6 / 2.3–2.8 ms |

- Still open: one AI think costs about 11 ms with 8 AIs on 1024 (passed to (k)); a new region graph mid-game costs
  about 14 ms on 1024; no low-poly LOD for unit models; without the warm-up (the bench default) the first tick
  still costs 100–145 ms.

### (n) UX polish — client UI/input + small sim fixes
*brief; started 2026-10-14*

- **Fire states** per unit: Fire at will / Return fire / Hold fire (saveable sim order flag, UI toggle +
  hotkey). Units capturing a target hold fire on it.
- **Transports**:
  - order buttons are cramped with a loaded transport selected;
  - the cargo/fuel rows cut off the unit panel;
  - minimap clicks must work in every order mode;
  - Atlantis dive.
- **Experimentals**:
  - the Mavor picks a high arc when the low one is blocked;
  - Stop cancels an enhancement;
  - shield HP doesn't refill on an enhancement change.
- **Save/replays**:
  - Load in the pause menu;
  - F7 updates the viewer's view selector;
  - save migrations (format version + migration hook + a test that loads an older save).
- **Factory exit**: units leaving a factory avoid parked units.
- **General**:
  - double-click selects every visible unit of that type; Ctrl+Shift+click;
  - double-tapping a control group centers the camera;
  - tooltips show hotkeys;
  - a better unit panel for mixed selections;
  - a help overlay (F1) generated from one hotkey table, with no clashes;
  - idle engineer/factory buttons;
  - any obvious UX bug found while testing.

**Delivered** (merged 2026-10-16; four agents: the first three were cut by usage limits or the context cap, each
continued from the previous one's handoff note):
- Sim:
  - fire states (Fire at will / Return fire / Hold fire); units don't auto-shoot a unit their army is capturing;
  - Atlantis dive; the Mavor lobs over ridges on the high arc (`HighArcWhenBlocked`);
  - Stop cancels an enhancement (with refund); shield HP and up/down state are kept when the blueprint changes;
  - factory exit skips sides with a parked unit;
  - save migrations: container v2, `SaveMigrations` header/payload steps (v1: default faction),
    `GameInfo.FileVersion`. Limit: no field-level migration (a changed sim layout still refuses the file).
- Client:
  - `Input/Hotkeys.cs` = the one key table (`HotkeyTableTests`: no clashes, every polled key listed); the F1 help
    overlay is generated from it (`--help-overlay`); tooltips show keys;
  - the orders panel adds columns (max 3 rows); compact unit panel for transports/aircraft; mixed selection panel
    (idle/fire-state counts, smaller type buttons, Ctrl+click type);
  - minimap clicks work in every order mode; idle engineer/factory buttons;
  - pause menu "Load Game" (Back returns to the game); replay F7 syncs the view selector.
- New hotkeys: Q / Shift+Q fire state · F1 help overlay (debug spawns moved to Ctrl+F1 / Ctrl+F2) · "." next idle
  engineer (Shift: all) · "," next idle factory (Shift: all) · double-tap 0-9 centers the camera on the group ·
  Shift+double-click / Ctrl+Shift+click add that type on screen · Ctrl+click a unit panel type button adds that type.
- Tests: `FireStateTests`, `AtlantisDiveTests`, `MavorHighArcTests`, `FactoryExitTests`, `SaveMigrationTests`,
  `HotkeyTableTests` and client selection tests. At merge: 376 sim + 211 client tests green.
- Check: `Rts.Bench -- ai 30 256 42 hard,normal` = main exactly (WINNER Hard after 11:30, hash EDCA8A729F29736E).
- Not done: a capture cursor hint for transports carrying a commander (unclear, low value).

### (o) Lockstep multiplayer — new `src/Rts.Net` (no raylib) + client lobby
*brief; started 2026-10-14*

Deterministic lockstep on top of the replay command log and the desync hashes.
- **Transport**: TCP with a framed protocol; host/join by IP:port; LAN discovery by UDP broadcast; Internet play
  with port forwarding (document it).
- **Lobby**:
  - slots (human/AI/closed), faction, team, color;
  - host-owned map options; ready flags; chat;
  - menus Multiplayer → Host / Join / LAN games;
  - CLI `--host [port]`, `--join ip:port`, `--name N`.
- **Lockstep**:
  - commands scheduled for tick T + delay (adaptive to ping);
  - turn messages per tick per player; the sim advances only when every turn for a tick is in;
  - game speed and pause negotiated;
  - a desync hash exchange every N ticks; on a desync, a message + a replay dump.
- **Disconnects**: a timeout drops the player; the AI takes over its army (or it is defeated, a host option).
  Reconnecting is "later".
- **Replays**: every MP game writes a normal replay.
- **Tests**:
  - a headless 2–4 peer loopback match with random commands and identical hashes;
  - protocol framing; drop handling;
  - `Rts.Bench -- mp` self-test;
  - two client windows on this machine (`--host` + `--join 127.0.0.1:port`).

**Delivered** (merged 2026-10-16; three agents, the first two cut by usage limits):
- `src/Rts.Net` (no raylib): TCP framed protocol, the host relays every turn; LAN discovery on UDP 47625; lobby with
  8 slots (human / AI / open / closed): faction, team, color, AI level, ready flags, host map options, chat, kick.
- Lockstep: one turn per player per tick, input delay adapted to ping; speed and pause negotiated with the host;
  `StateHash` checked at load and every 10 ticks (a desync shows a banner and writes the replay so far); a dropped
  player goes to the AI or is defeated (host option); a client that loses the host continues offline. Lobby
  factions go into the skirmish settings; the host refuses unknown faction ids.
- Client: main menu Multiplayer (name, Host, Join, LAN list), lobby with map preview, loading screen; in game: chat
  (Enter, in `Hotkeys.cs`), players/pings panel, "Waiting for X" banner, an Esc menu that doesn't pause, observer
  mode after defeat. Space / + / − become requests to the host. Every MP game writes a normal replay.
- How to try: two windows, `--host 47704 --name A --autostart` and `--join 127.0.0.1:47704 --name B --autostart`;
  against AI only `--host --autostart --players 3`; headless `Rts.Bench -- mp 4 1200` (or `mp 3 1200 256 1 0 drop`).
- Sim fix: the static `_tmp` scratch lists in ConstructionSystem, MissileSystem and DamageSystem are now
  `[ThreadStatic]` (several sims in one process raced on them: the loopback test failed about 1 run in 3).
- Docs: ARCHITECTURE "Multiplayer" (with the float determinism review), DEV_GUIDE (port forwarding), CLAUDE.md.
- Tests: `Rts.Net.Tests` (21: framing, codec, lobby, loopback 2/3/4 peers + AI with random commands, drops, desync,
  speed/pause, LAN), `NetGameConfigTests`. At merge: 376 sim + 213 client + 21 net tests green.
- Numbers: `mp` 4 peers × 1200 ticks in 1.3 s, all hashes equal; bench 2000 300 main 4.07 ms vs branch 3.84 ms;
  `ai 30 256 42 hard,normal` = main exactly (hash EDCA8A729F29736E).
- Known issues: games between different operating systems can desync (the sim's trig uses each OS's C math
  library); no reconnect, MP observers, MP saves or NAT traversal; after game over the end screen stops the local
  sim, so a peer that keeps playing waits.

## Wave 6 (started 2026-10-23 20:10)

Two agents with disjoint files, from the audit "Wave 6+ plan" in [ROADMAP.md](ROADMAP.md) (gap table G1-G45). Each
prompt = brief + [AGENT_RULES.md](AGENT_RULES.md); coordinators cap 200k (medium), one sub-agent at a time (low 150k).

### (6a) Commander & engineers — `Rts.Sim/Commander`, `Combat/OverchargeSystem.cs`, `Economy/EngineerPatrol.cs`
*brief* (gap rows G1, G2, G3; G7 if time): Overcharge (energy-fed ACU shot, damage from stored energy, key O, order
button, fx/sound); commander death blast (hook in `Commander/`, not CleanupSystem: 6b owns it); engineer patrol
automation (patrolling builders repair > assist > reclaim near the route); then personal teleporter (both ACUs), UEF
engineering drones, UEF SCU radar jammer. Owns `Rts.Sim/Commander`, the new OverchargeSystem, ConstructionSystem,
commander/SCU blueprints, client order button + model partials. Both factions through roles. Tests: OC damage/energy,
blast radius, patrol reclaim/repair/assist, teleport, save coverage. Screenshots: OC shot, death blast, teleport.

**Delivered** (merged 2026-10-24; four coordinators: coordinator 1 designed G3 and stopped at the orchestrator restart,
coordinator 2 was cut by a usage limit at once, coordinator 3 built G3/G1/G2 and the teleporter sim, coordinator 4
finished). Report of coordinator 4:
- **G1 Overcharge:** the commander's shot spends stored energy (1000-5000) as damage; structures take half, enemy
  commanders at most 400. Key O + orders button, fx and sounds.
- **G2 commander death blast:** two-ring nuke-like blast 1.5 s after the commander dies; hurts own units too, removes
  wrecks, smaller mushroom.
- **G3 patrol:** patrolling engineers, commanders and SCUs repair damaged allies first, then assist allied buildings,
  then reclaim wrecks near the route.
- **G7 Personal Teleporter** (both commanders): 10 s charge, 5000 E + 50 E per unit of distance; key J + button, arc
  and cost overlay, fx, sounds, back-pack model.
- **Try:** `--sandbox --exec "money me on; spawn uef_commander+PersonalTeleporter me at 100,128; wait 10; teleport me
  150,128"`; console `overcharge [army|all] [on|off]`, `kill [r] at x,z`, `teleport [army|all] [at] x,z`.
- **Tests:** Sim 462 (Overcharge, CommanderDeathBlast 7, EngineerPatrol 6, Teleport 14), Client 251 (OverchargeUi 7,
  TeleportUi 9, snap 1), Net 21. **Hotkeys:** O, J.
- **Numbers:** reference match plays exactly like before (Hard at 15:35, same stats); hash **BF85B7DBBF03D949**
  because the new commander state is hashed (verified on main after the merge). Perf `2000 300`: 1.78 vs 1.74 ms (noise).
- **Shared files (additive):** Simulation, UnitLifecycle, CommandSystem, OrderSystem, ConstructionSystem,
  WeaponSystem, Components, SimSnapshot/SnapshotWriter, IntelSnapshotWriter, SimEvents, Order, EnhancementCatalog;
  client Hotkeys, SelectionController, OrdersPanel, GameSession, GameApp, EffectsRenderer, UnitRenderer, SoundBank,
  sandbox console.
- **Left (ROADMAP later):** UEF Engineering Drones, UEF SCU Radar Jammer; the AI does not use Overcharge, the
  teleporter or patrols (wave 9a, G32).
- **Handoff review:** the notes gave exact files and lines per step, so two small sub-agents finished the teleporter
  client without rereading code. The handoff left the build broken (the WIP needed 4 lines in shared files). Seam bug:
  fog hid the teleport arrival effect from its own player (fixed, with a test). Every earlier 6a sub-agent ran past
  its CHECKPOINT; what worked: pieces of ~4 steps, exact line pointers, 1-3 screenshots, a commit per milestone, and
  a context warning mid-run (hence `agent_ctx.py --self`).

### (6b) Game options & team play — GameSetup, CleanupSystem, setup/lobby screens, `Rts.Net` lobby codec
*brief* (gap rows G4, G5; G6 if time; started 2026-10-24 06:00): options in the skirmish setup AND the MP lobby (host
decides, sent through the lobby codec, saved in saves/replays): victory assassination / supremacy / annihilation /
sandbox, unit cap 250-1000 with a HUD counter and "cap reached", start resources, fog normal/explored/none, game speed
lock, cheats on/off, prebuilt bases (via roles). Team play through commands (replays + MP): give units and mass/energy
to allies, allied overflow sharing, allies-only chat, a diplomacy panel. G6: score history + end-screen graphs.
Defaults keep today's behavior (reference hash unchanged). Owns GameSetup, CleanupSystem, Army/ArmyStats (append),
UnitTransfer, new EconomyShare/DiplomacyCommands, setup/lobby/end screens, NetGameConfig, Hud (additive), new
DiplomacyPanel, the `Rts.Net` lobby codec. Tests: each victory mode, cap, transfers, overflow, codec round trip, MP
loopback with options.

**Delivered** (merged 2026-10-24; one coordinator, resumed once after a usage limit; sub-agents: sim, client-1 net
codec + session + HUD, client-2 setup/lobby UI + diplomacy (cut by the usage limit, note written on resume), finish).
Report:
- **G4 options** in the skirmish setup and the lobby: victory assassination / supremacy / annihilation / sandbox;
  unit cap 250-1000 with a HUD "units n/cap" counter and a "Unit cap reached" alert; start resources low / normal /
  high (high adds a refill bank); fog normal / explored / none; speed adjustable or locked at 1x / 2x; cheats;
  prebuilt base from faction roles. Lobby codec protocol 2; options saved in saves and replays (`SimWorld.Options`,
  `GameInfo.Options`).
- **G5 team play:** give units, mass or energy to an ally (commands), allied overflow sharing, allies-only chat
  (Shift+Enter), F4 diplomacy panel.
- **G6 score history:** a sample every 30 s (saved, not hashed), a line chart per army on the end screen, a score
  column in the diplomacy panel.
- **Try:** `--options victory=supremacy,cap=250,resources=high,fog=explored,speed=fast,cheats=off,prebuilt=on`;
  `--teams 1,1,2,2 --players 4 --diplomacy`; `--setup`; `--host --options ...`; bench `ai 12 256 7 hard,normal 1 2
  plains uef,cybran victory=supremacy,prebuilt=on,cap=250`.
- **Tests:** GameOptions 27, TeamPlay 8, GameOptionsNet 3, GameOptionsClient 3, DiplomacyPanel 3, AlliesChat 1,
  ScoreHistory 3, ScoreGraph 1; after the merge Sim 500, Client 258, Net 26.
- **Hotkeys:** F4 diplomacy; F9/F10 sandbox tools when cheats are on (the old FX debug keys only with `--fxtest`);
  Shift+Enter allies chat.
- **Numbers:** reference match unchanged (BF85B7DBBF03D949, verified on main after the merge). Perf `2000 300` main
  1.75-1.94 vs 1.77-1.94 ms. Smoke match with options: no exceptions.
- **Shared files (additive):** SimWorld (`Options`), SimSnapshot/SnapshotWriter (ArmyView UnitCap/Name/Score,
  Options), CommandSystem (3 cases), EconomySystem (overflow hook), MultiplayerSystem (Defeat); GameApp/Program (CLI),
  GameSession, Hud, Hotkeys, NetInGameScreen, Bench Program (9th arg).
- **Known issues:** in screenshot mode the frame-timing text draws over the diplomacy panel header; the HUD counter's
  amber/red colours only unit-tested; saves made before 6b untested (field migration = ROADMAP later; replays from
  before the (k) balance data already refuse to open: "unit blueprints changed").
- **Handoff review:** the notes were enough. Rediscovered: `HostSession.SetSlot` can't change a client's team (tests
  use `LobbyRequest`), and `dn.sh` in a fresh worktree needs `safe.directory`. No seam bugs.

## Wave 7 (started 2026-10-24)

From the "Wave 6+ plan" in [ROADMAP.md](ROADMAP.md). 7a started while 6b was still running (disjoint files); 7b
starts after 6b merges (Hud, Input). Owner question 5 (release size) was unanswered: the recommendation holds.

### (7a) Release, settings, autosave — `tools/package/`, Program.cs, SettingsScreen, AppSettings, pause menu
*brief* (gap rows G45, G18, G40): a `tools/package/` script that builds a self-contained Linux Release folder
(dotnet publish, data next to the exe, icon, version label in the main menu, crash log file) plus a `.desktop`
launcher/desktop-shortcut installer (tested with a temporary HOME; installing on the owner's real desktop is the
owner's choice) and an optional Windows zip; the packaged build keeps saves/replays/settings/screenshots in the user
folder (XDG), runs from the repo keep today's folders. Settings: graphics (shadows, particles, terrain LOD, vsync,
fps cap, UI scale) and gameplay (edge scroll, camera/zoom speed), reachable from the pause menu. Autosave every
5 minutes, rolling 3. Tests: path resolution, settings round trip, autosave rotation; screenshots of the settings
screens and the packaged build running; DEV_GUIDE "Packaging" section.

**Delivered** (merged 2026-10-24; one coordinator, resumed once after a usage limit; 132k at the end). Report:
- **G45 packaging:** `bash tools/package/package.sh [linux|win|all] [-o DIR]` builds self-contained
  `release/RtsGame-0.7.0-linux-x64.tar.gz` (36 MB) and `-win-x64.zip` (37 MB) with `data/`, a `release.txt` marker,
  a procedural icon, `rtsgame.desktop` and `install-desktop.sh`. The marker switches saves, replays and screenshots
  to `~/.local/share/RtsGame` (`UserDirs`); without it (dev runs, tests, Bench) today's folders. Settings in
  `~/.config/RtsGame`. Version label `v0.7.0 (hash)` in the main menu. Crash log `~/.local/share/RtsGame/crash.log`
  (main, unhandled and sim-thread faults; notice on the next start). The desktop installer was tested only with a
  temporary HOME (the owner decides about their desktop).
- **G18 settings:** three tabs; Graphics: shadows, particles, terrain detail, VSync, FPS cap, UI scale; Gameplay:
  edge scroll, pan and zoom speed, autosave. Applied live; `--novsync` / `--terrain-lod` still override. Settings
  button in the pause menu.
- **G40 autosave:** every 5 min of game time into `autosave_1..3` (empty slot first, then the oldest); skipped in
  replays, sandbox, multiplayer, after the game end and in screenshot runs; "Autosave N" in Load.
- **Try:** `--settings-tab graphics|gameplay`, `--autosave-every TICKS`, `--export-icon FILE.png`.
- **Tests:** UserDirs 7 (Sim), CrashLog 9, AppSettings 6, Autosave (Client); after the merge Sim 507, Client 279,
  Net 26. **Hotkeys:** none.
- **Numbers:** reference match unchanged (BF85B7DBBF03D949, verified on main after the merge). Perf `2000 300`
  1.75-1.88 vs main 1.74-1.96 ms. Publish takes 146 s (linux), 86 s (win).
- **Shared files:** additive lines in GameApp.cs, GameSession.cs (fault log), Program.cs, Ui.cs; 3 lines in RtsCamera.cs.
- **Known issues:** F12 screenshots land in the current folder (raylib built-in; the launcher sets `Path=`);
  multiplayer has no Settings or autosave; the live effect of shadows/particles/UI scale is unit-tested only.

### (7b) Orders & selection UX — `Rts.Client/Input`, Hotkeys, SelectionController, Minimap, alerts, pings
*brief* (gap rows G14, G15, G16, G17; G20 if time; started 2026-10-24 after the 6b merge): selection filters (all of
a type on the map, all land / air / naval / engineers), commander key (select + center), camera bookmarks, jump to the
last alert; right-drag formation move with facing; waypoint editing (drag / delete queued order markers) and ferry
route editing through a new sim `Orders/QueueEditCommands.cs`; notification pass (minimap flash where attacked,
structure lost, construction/upgrade done, ally under attack); map pings (alert / move / attack, map + minimap,
allies and MP, in replays) as a sim command; G20 range/intel overlays for the selection. Tests: hotkey table,
queue-edit and ping sim tests, ping in a replay and an MP loopback. Screenshots.

**Delivered** (merged 2026-11-11, b8b9062; G14-G17, without G20). Three attempts: coordinator 1 on 2026-10-24 (cut by
a usage limit after the sim commands), coordinator 2 on 2026-11-10 on Fable 5.2 (cut twice after 8-10 minutes: main
merged, selection keys, orders UI), then one module agent on Opus 5.5 without coordinator or sub-agents on
2026-11-11 (19 minutes, 80 calls, 176k at the end: pings UI, notifications, finish). Report:
- **Orders (G15):** sim commands `MoveWaypoint`, `DeleteWaypoint` and `FormationMove` (`Orders/QueueEditCommands.cs`;
  replays and lockstep carry them). Right-drag = line formation with a facing; left-drag a queued order or ferry
  marker moves it, Del over a marker deletes it.
- **Selection and camera (G14):** same types on screen / on the map, all combat, land / air / naval / engineers,
  commander (select + center), five camera bookmarks.
- **Pings (G17):** `MapPingCommand` (alert / move / attack), shown on map and minimap to allies and observers, kept
  in replays, sent in multiplayer.
- **Notifications (G16):** new `AlertMonitor`: own units, the commander or an ally under attack, structure lost,
  construction or upgrade complete (repeats as xN), unit cap reached; each with a toast, an alert sound and a minimap
  flash. It works without an audio device (the old under-attack alert did not). Backspace jumps to the last alert,
  again to the one before.
- **Try:** `--demo --ui-pings 120,120 --camera 128,128,70 --frames 440`; `--ui-formation 14,22:34,22 --camera
  24,29,45`; `--ui-waypoints 8,16 --camera 26,35,62`.
- **Tests:** QueueEditTests 11, PingTests 4 (one in a replay), PingNetTests (MP loopback), SelectionFilterTests 9,
  AlertTests 9, OrderEditMathTests. After the merge: Sim 522, Client 309, Net 27, all green.
- **Hotkeys:** Ctrl+Z / Ctrl+Shift+Z same types on screen / map; Ctrl+A combat units; Ctrl+Shift+L / A / N / E land /
  air / naval / engineers; Ctrl+C commander; Alt+1-5 recall a bookmark, Ctrl+Alt+1-5 save it; Backspace last alert;
  Y alert ping at the cursor; Del delete the waypoint under the cursor. Mouse: right-drag formation, left-drag a
  marker, Alt+left / Alt+Shift+left / Alt+right = alert / attack / move ping.
- **Numbers:** reference match unchanged on main after the merge ("WINNER: Player (Hard) after 15:35", hash
  BF85B7DBBF03D949). Bench `2000 300`, three interleaved runs while the 8a agent was working: main before the merge
  2.42-2.97 ms/tick, after it 2.14-2.58, same hash F74F5FED38AC5D83 (no regression; the agent had not done this check).
  2,648 lines added in src, tests, tools and data. Screenshot `screenshots/7b_pings_main.png` (three pings on map and
  minimap, "Units under attack" toast).
- **Shared files:** CommandSystem (4 cases, a parameter on ApplyIssueOrder), OrderSystem and MovementSystem (one line
  each: turn to the facing on arrival), Order.cs (`OrderQueue.RemoveAt`), SimSnapshot / SnapshotWriter (raw waypoint
  point and `Aux` on orders, ping list), GameSession, GameApp (3 CLI options), SelectionController, Hotkeys, Minimap,
  GameAudio (under-attack check moved to `AlertMonitor`), SoundBank (+3 sounds at the end of the enum),
  UnitCapCounter (its toast moved to `AlertMonitor`).
- **Not done / known issues:** G20 range overlays not started; the new keys were never pressed by hand in a game
  window (unit tests and screenshot hooks only); bookmarks are not saved; "structure lost" also fires on
  self-destruct; two toast stacks share the top centre of the screen; the AI neither sends nor reads pings (ROADMAP
  "later (7b)").
- **Handoff review:** the ping sub-agent cut on 2026-11-10 had left no commit and no note, so that piece was rebuilt
  after a grep of GameAudio, Toasts and Minimap. The module agent was at about 100k after 30 calls (4 minutes) of
  reading the notes and the existing code, half of its 200k cap, so it dropped the in-game try of the keys and G20.

## Wave 8 (started 2026-10-24)

### (8a) Missing units & walls — `data/blueprints`, `Naval`, `Strategic`, `Transport`, pathfinding blocked cells
*brief* (gap rows G8, G9; Atlantis dive from G11; started while 7b runs, disjoint files): the SupCom1 vanilla units
still missing for UEF and Cybran: UEF Mech Marine (T1 bot), T3 strategic missile submarines for both factions (nuke +
tactical missiles launched from a submerged hull), Cybran T3 aircraft carrier (reuse the Atlantis cargo/factory
code), T3 sonar platforms, the UEF T2 stealth field generator if it is vanilla SC1, the Cybran Salem destroyer
walking on land; T1 wall sections for both factions (block movement and direct shots, Shift-drag line placement);
Atlantis dive. Faction json roles stay optional (the AI ignores what it doesn't know). Tests: roster tests,
submerged launch, walls block paths and shots, carrier; a `--scene` group; screenshots.

**Delivered** (merged 2026-11-11, fce92ee; G8 and G9). Attempts: coordinators 1-3 on 2026-10-24 and 2026-11-10, each
cut by a usage limit; then two module agents on Opus 5.5 without coordinator on 2026-11-11: 8a-mod-1 (13:19-13:42,
CHECKPOINT at 173k) and 8a-mod-2 (started 13:44, cut by the usage limit at 13:50, resumed 19:15, finished 19:34 at
142k: main with 7b merged, the replay bug, docs), with two client sub-agents one after the other (the first read to
116k before working and stopped at 131k after the models; the second did the Salem legs, the scene and the
screenshots and finished at 123k). Report:
- **Units (G8):** 9 new blueprints. UEF Mech Marine (T1 bot); UEF T2 stealth field generator; T3 sonars for both
  factions (upgrades of the T2 ones); T3 strategic missile subs for both factions (they build and launch the nuke
  submerged and on the move, cruise missiles fire submerged); Cybran T3 carrier (the Atlantis code on a ship: builds,
  docks, repairs and launches 16 aircraft). The Salem destroyer walks on land at 35 % speed (torpedoes held ashore);
  the client lifts the hull and draws 4 legs. Models for every new shape. The faction roles `nuke_sub_t3`,
  `sonar_t3`, `wall_t1` are optional; the AI does not build these units. The Atlantis dive of the brief was already
  done before 8a (AtlantisDiveTests).
- **Walls (G9):** T1 wall sections for both factions (`Combat/Walls.cs`): they block paths, absorb enemy direct shots
  and beams up to their height, and are never auto-targeted. Shift-drag line placement already existed.
- **Try:** `--scene units8a[:land|:walls|:naval|:salem]`; sandbox `spawn uef_t1_wall me at x,z`.
- **Tests:** MissingUnitsRosterTests, WalkingShipTests 10, MissileSubTests 8, CarrierTests 6, WallTests 11, client
  WalkingShipRenderTests 6. After the merge: Sim 569, Client 315, Net 27, all green.
- **Hotkeys:** none.
- **Replay bug found on the way:** `PropStore`'s alive list was a lazy cache that got saved. The slots past its count
  depended on who had read `AliveList` and when, not on the game. The Salem hooks changed the course of the game,
  and that exposed it. The cache is now `[NoSave]` and rebuilt when read (DEV_GUIDE pitfall).
- **Numbers (checked on main after the merge):** reference match "WINNER: Player (Hard) after 15:35", hash
  BBD1FF3C4E116A4A (the same game as BF85B7DBBF03D949: the new blueprint files shift the blueprint indices, AI_TUNING
  note). Bench `2000 300`, four interleaved runs: main before the merge (c28d102) 1.92-1.97 ms/tick, after it
  1.90-2.10 (2.10 on the first run, then 1.90-1.97); bench hash EAED77303695A943 (was F74F5FED38AC5D83; same units,
  projectiles and path solves). 2,250 lines added in src, tests, tools and data. Screenshots
  `screenshots/8a_merge_{land,walls,naval,salem}.png` (walls and salem looked at: the two wall lines with units on
  both sides; the Salem on land on four legs). The first check run after the merge was void: the build died with the
  CLR flake of DEV_GUIDE "Profiling" and the tests ran on stale binaries (414 of 522 "failed"); the second run was clean.
- **Shared files:** one line each in GameApp.cs, GameSession.cs, UnitModels.cs (3 cases), UnitModels.Cybran.cs,
  UnitRenderer.cs, UnitRenderer.Cargo.cs; small hooks in MovementSystem (land speed), Combat (Projectile / Targeting /
  Weapon systems, CombatCatalog), PropStore. Nothing in `Input/`.
- **Not done / known issues:** saves and replays from before 8a refuse to open (new blueprints); the rest is in
  ROADMAP "later (8a)".
- **Handoff review:** the note described the replay bug accurately and named three suspect files; the cause was in a
  fourth (PropStore). Bisecting by file would not have found it; comparing the two states field by field with
  reflection found it in one run. One client sub-agent stopped at its context limit; the next one found that the
  carrier deck spots were never used (`BaseShape` keeps "cy_carrier") and fixed it.

### (8b) Maps — three modules, one after the other (re-cut 2026-11-11)
The brief of 2026-10-23 (ROADMAP "Wave 8": G10, G26, G27 in one agent) is cut into three modules of about 1,500
lines, each for one module agent on Opus 5.5 (cap 200k, no coordinator; WORKFLOW "Lifecycle of a wave"). Each starts
from main after the one before it is merged. None is started yet.

*brief (8b-1) Map props: trees and rocks* (G10; ledger purpose "Alberi e rocce (8b-1)"): the generator places forests
(trees: energy, a little mass) and rock fields (rocks: mass) on every preset, deterministic per seed and symmetric
like the terrain (`MapSymmetry`), never on start plateaus, deposits, water or steep ground; density scales with the
map size (a 1024 map has thousands, so `PropStore` capacity, its linear `FindNearest` and the snapshot must be
sized and measured). `PropStore` gets a prop kind (wreck / tree / rock, additive arrays, saveable). Engineers
reclaim them like wrecks (single order, area reclaim on patrol). Trees do not block movement: they fall when a unit
walks over them and burn in area damage (a burnt tree is worth less; a nuke clears the forest). Client: instanced
trees and rocks in `PropRenderer` with a far LOD, fall and burn effects, reclaim value in the tooltip. Owns
`World/PropStore.cs`, new `Map/MapProps.cs` (+ one call in the generator), new sim file for fall/burn, `PropRenderer*`.
Leaves alone: map files, setup screens, `TerrainRenderer` (8b-2, 8b-3, 9b-2). The reference match will change (the
AI reclaims props): document the new hash and that the game still ends with a winner. Tests: placement
deterministic, symmetric and off starts / deposits / water; reclaim values; fall and burn; save/load and a replay
with props; `MapScaleTests` at 1024. Perf: interleaved bench `2000 300` and `--perf-log` on a 1024 map before and
after. A `--scene` group and screenshots.

*brief (8b-2) Map files, map pack, map picker* (G26 without multiplayer; purpose "File mappa e scelta mappa (8b-2)";
after 8b-1): a versioned map file (`Map/MapFile.cs`: heightmap, water level, deposits, start positions, props, name,
players, a content hash) with exact save / load round trip; a headless command that exports generated maps
(`Rts.Bench -- mapexport ...`); 8-12 named maps for 2-8 players and the three sizes in `data/maps/`, chosen from
seeds and checked by the generator's guarantees (starts connected, deposits buildable); the skirmish setup gets a
map picker (name, size, players, thumbnail drawn from the file as `MapPreview` does; "random map" keeps today's
generator options) and the client a `--mapfile NAME` option. Saves and replays record the map by name + hash and
refuse a different file. Owns `Map/MapFile.cs`, `GameSetup` / `Persistence/GameInfo` (additive), `Menus`
setup screen and `GameConfig`, the export command. Leaves alone: `Rts.Net` and the lobby (8b-3). Sim results on
generated maps must not change (same reference hash as after 8b-1). Tests: file round trip, every pack map valid,
a game + replay + save on a file map. Screenshots of the picker.

*brief (8b-3) Maps in multiplayer + biomes* (G26 lobby part, G27; purpose "Mappe in rete e biomi (8b-3)"; after
8b-2 and 9b-2): the host picks a map file in the lobby; clients that have the same file (name + hash) use it, the
others receive it from the host in chunks with a progress line, and check the hash; lobby codec version bump;
`Rts.Bench -- mp` on a file map. Biomes (temperate, desert, tundra, lava, tropical): a generator / map file field,
`TerrainRenderer` palettes, tree and rock looks per biome, a setup and lobby option, no effect on sim results.
Owns `Rts.Net` lobby + transfer, the lobby screen, `TerrainRenderer` palettes. Tests: codec round trip, transfer in
an MP loopback (file present and missing), hash mismatch refused. Screenshots: one per biome, the lobby picker.

### (8b-1) Map props: trees and rocks — report (merged 2026-11-12, dd28b4a)
Two module agents on Opus 5.5, one after the other in the same worktree (the first stopped at its context cap after
the sim part, 19 minutes; the second finished in 17 minutes). 1,393 lines in `src` and `tests`.

**What works.** Every map preset gets forests and rock fields, symmetric like the terrain, none on start plateaus,
deposits, water or steep ground. Engineers and the AI reclaim them: trees give energy (and a little mass), rocks
give mass. Trees fall when units drive over them and burn in area damage; fire spreads; a burnt tree is worth less
(energy x0.25, mass x0.5); nukes knock down and set fire to forests. Client: instanced trees and rocks with a far
LOD, the fall animation, burning crowns with flames and smoke, charred trunks; the cursor shows "Reclaim tree +N
energy" / "Reclaim rock +N mass"; `PickProp` only checks props near the ground point under the mouse.
How it works: ARCHITECTURE "Map props".

**How to try it.** `dotnet run --project src/Rts.Client -- --scene props[:fire|:rocks]` (prints the positions), or
any skirmish.

**Tests.** `MapPropsTests` (19, sim: placement, symmetry, density, grid against a full scan, reclaim, patrol
reclaim, fall, burn, nuke, save/load while burning), `PropsSceneTests` (2, client). `AiVersusAiReachesCombat` failed
on every run with props (on seed 42 the rocks shift the two mirrored AI economies so that neither attacks within
8 minutes: a timing edge, not an AI bug), so that test now clears the props; six tests that count wrecks call the
new `TestUtil.ClearMapProps`. No new hotkeys.

**Shared files** (a few lines each): Terrain, MapBuilder, GameSetup, SimWorld, Simulation, DamageSystem,
MissileSystem, ConstructionSystem, AiEconomy, SimSnapshot, SnapshotWriter, GameApp, GameSession, ContextOrders,
CLAUDE.md.

**Numbers.**
- Props with 4 players on Plains: 256 map = 460, 512 = 2,660, 1024 = 10,816.
- Bench `2000 300`, 5 interleaved runs: main 2.29 ms per tick, branch 2.43 ms (+6 %).
- `--perf-log` on a 1024 demo: main 2.32 ms per frame (CPU 0.61), branch 2.30 ms (CPU 0.68); the props stage costs 0.15 ms.
- Reference match `ai 30 256 42 hard,normal`: before "Hard wins after 15:35", BBD1FF3C4E116A4A; now "Hard wins
  after 12:18", **FB269C29202773C1**. Bench `2000 300` hash: EAED77303695A943 → **6A8312040EFA67F2**.
- A replay of a 256 demo re-simulates in sync; `savebench 4 1024 4`: load 926 ms cold, 413 ms warm, same state hash.

**Known issues.** Trees cast no shadows (the shadow pass is in `UnitRenderer`, outside this module); a tree that
has fallen and then catches fire plays its fall again (needs its own fall tick); trees are not on the minimap;
saves and replays made before this merge do not load (the saved prop layout changed).

**Handoff review** (second agent). The first agent's note was accurate for the sim part. It had not run the full
sim suite, so the test that really failed (`AiVersusAiReachesCombat`, not the one the note named) was diagnosed from
scratch. Nothing was broken between the two agents' work.

**Merge checks on main** (orchestrator, 2026-11-12 00:42-00:47): build 0 errors; tests 588 sim + 317 client + 27
net green; reference match and bench give the two new hashes above; screenshot of `--scene props:fire` (standing,
fallen and burning trees) looked right. The branch already contained main, so the merge changed nothing else.

### (8b-2) Map files, map pack, map picker — report (merged 2026-11-12, 9528229)
Two module agents on Opus 5.5, one after the other in the same worktree (a first start at 10:52 died on its first
call from a network error and wrote nothing). Agent 1 wrote the sim side, the pack and the client picker in 15
minutes and stopped at 166k when the orchestrator asked; agent 2 ran the suites and the reference checks, fixed
three screens and wrote the docs in 10 minutes (112k). 1,037 lines in `src`, `tests` and `tools`, plus 2.4 MB of
map files. Generated maps play exactly as before (same reference hashes).

**What works.** A versioned `*.rtsmap` file (`Rts.Sim/Map/MapFile.cs`: heights, water, deposits, starts, props,
name, content hash; Brotli, bit-exact round trip; heights rounded to 1/256 in the pack). `MapLibrary` finds the
maps, `MapChecks` tests the generator's guarantees (starts connected, deposits buildable) through faction roles.
`Rts.Bench -- mapexport` writes one map, `--pack` the whole pack (`tools/maps/pack.txt`), `--scan` finds seeds that
pass the checks. 10 named maps in `data/maps` for 2-8 players and the three sizes. A game with fewer players than
the map has starts uses a well-spread subset of them. The skirmish setup has a "Map" row ("Random map" keeps the
generator rows; a file map shows its thumbnail with the starts the game will use); the loading screen, the
"separate islands" warning and the screenshot info follow the file. Saves and replays store the map id and hash and
refuse a missing or different file. How it works: ARCHITECTURE "Map files".

**How to try it.** `dotnet run --project src/Rts.Client -- --setup --mapfile coral_strait`, or
`-- --mapfile great_lakes --players 8`. No new hotkeys.

**Tests.** `MapFileTests` (11, sim: exact and rounded round trip, damaged files, a game on an exact file equals the
generated game, spread starts, every pack map passes the checks, a pinned hash, save/load and replay on a file map,
missing or different file refused); `GameConfigTests.MapFilePickerChecksPlayersAndStartsTheGameOnTheFile` (client).

**Shared files** (additive): GameApp.cs (3 lines), AppHost (1 line), Menus (GameConfig, SkirmishSetupScreen,
LoadingScreen, MapPreview), Persistence (GameInfo, SaveGame), CLAUDE.md (2 lines).

**Numbers.**
- Map sizes (KB): twin_meadows 64, red_gorge 60, four_ponds 54, three_terraces 54 (256); coral_strait 210,
  highland_cross 186, six_fields 204, mesa_pentagon 203 (512); great_lakes 701, badlands 637 (1024). Pack 2,373 KB.
- Bench `2000 300` tick, interleaved, both copies on tmpfs: main 2.53 ms, branch 2.57 ms (+1.6 %, noise). With the
  branch on the repo volume and main on tmpfs the branch looked 10 % slower: the perf check depends on the
  filesystem (now in DEV_GUIDE "Perf check").
- Reference match and bench: the hashes of main (FB269C29202773C1, 6A8312040EFA67F2).

**Known issues / later** (ROADMAP): a save made on a map loaded from a path outside `data/maps` reloads only if the
file is copied there; map files in the lobby (8b-3); a user maps folder; a map editor.

**Handoff review** (second agent). The note was complete and accurate. Rediscovered: the filesystem bias of the
perf check. The note listed only the island warning as a gap; the loading screen and the screenshot info had the
same problem and were fixed too. Nothing broken at the seam. `tools/agents/dn.sh` fails in a worktree under the
scratchpad (git `safe.directory`), so the main copy for the perf runs was built with dotnet directly.

**Merge checks on main** (orchestrator, 2026-11-12 11:23-11:27): build 0 errors; tests 599 sim + 332 client + 27
net green; reference match "Hard wins after 12:18", FB269C29202773C1; bench 6A8312040EFA67F2; screenshot of
`--setup --mapfile great_lakes --players 4` (thumbnail with the four starts in use numbered, the other four as
white slots) looked right.

### (8b-3) Maps in multiplayer + biomes — report (merged 2026-11-12, e4371c2)
Three module agents and two sub-agents on Opus 5.5, in one worktree, 15:07-16:20. Agent 1 (started by a scheduled
job) wrote the biome field and the map library side, then a usage limit cut it at 162k at about 15:35 (the window
reset at 15:40, not at 15:00 as `windows.py` estimated: the job started in the tail of the old window); its last
edits were committed unbuilt by the orchestrator. Agent 2 wrote the net protocol, the transfer, the lobby, the bench
option and the tests in 14 minutes (CHECKPOINT at 171k). Agent 3 ran the suites, fixed one test, took the lobby
screenshots, added `mapexport --biome` and biome-coloured previews, and wrote the docs in 15 minutes (137k). The
biome visuals were a separate piece: sub-agent 1 (palettes, shaders, trees and rocks, `--biome`, stopped at its
130k checkpoint after one screenshot), sub-agent 2 (24 shots in six 2x2 grids, tuning, minimap, 97k). Two network
drops (ECONNRESET) stopped agent 3 and sub-agent 2 once each; both resumed by message. 1,563 lines in `src`,
`tests` and `tools`.

**What works.** The host picks "Random map" or a map file and a biome in the lobby; the lobby state carries the
map id, content hash, name, starts, size and biome (net protocol 3: older peers are refused with both versions
named). A client that has the file (id + hash, in `data/maps` or `downloaded_maps`) uses it; the others request
it: 32 KB chunks, at most 256 KB beyond the client's acknowledged bytes, a progress line in the map panel and the
bottom bar, the file decoded and hash-checked off-thread, stored in `downloaded_maps` (release: the user data
folder), and Ready stays off until then (the host also refuses it); a wrong file is refused. Net replays record the
map. Biomes (temperate, desert, tundra, lava, tropical; append-only enum `Rts.Sim/Map/Biome.cs`) are an optional
trailing byte of the map file header (old files read as temperate, content hashes unchanged), a generator /
skirmish / lobby setting and a `GameInfo` field; the client draws per-biome ground, cliff, water (lava glows with a
drifting crust), sky, minimap and preview palettes and per-biome trees and rocks (cacti, snowy conifers, birches,
charred trees, palms; sand, snow, ash or glowing cracks on rocks). The sim is the same on every biome. How it works:
ARCHITECTURE "Maps in multiplayer and biomes".

**How to try it.** `dotnet run --project src/Rts.Client -- --setup --biome desert`; `-- --demo --preset lakes
--biome lava`; `-- --host --mapfile great_lakes`; a throttled transfer by hand: DEV_GUIDE (`RTS_MAP_UPLOAD_KBPS`,
`RTS_USER_DIR`); headless `Rts.Bench -- mp 2 600 256 0 0 0 map=great_lakes biome=tundra`. No new hotkeys.

**Tests.** `MapTransferTests` (6, net: codec round trip, map present on both sides, missing map downloaded and
used, corrupted upload refused, old protocol refused, an 884 KB map in loopback), `BiomeMapTests` (4, sim: header
without the biome byte, header rewrite keeps the hash, biome in `GameInfo`, same sim state on every biome),
`BiomeVisualsTests` (client: a palette per biome, temperate = the old shader and minimap constants);
`MapFileTests.MissingOrDifferentMapFileIsRefused` changed: a save now also finds its map by content hash, so a
renamed copy loads.

**Shared files** (additive): NetSession / HostSession / ClientSession / LobbyScreen made partial (new `*.Maps.cs`),
`ClientSession.Join` optional map folder, GameApp (`--biome`), GameSession, AppHost, GameConfig, ClientGameData,
Program, Shaders (terrain and water colours became uniforms, `Atmosphere` colours settable), MapPreview (optional
biome), SkirmishSetupScreen (Biome row, preview 0.48 → 0.44 of the panel), CLAUDE.md (3 lines).

**Numbers.**
- Bench `2000 300` tick, 3 interleaved runs: main 1.75-1.95 ms, branch 1.79-1.83 ms (no regression).
- Transfer in loopback: an 884 KB map (1024, 8 players) in about 120 ms; great_lakes is 718 KB. A 20 KB/s
  throttled upload shows 41 % in the lobby screenshot.
- Reference match and bench: the hashes of main (FB269C29202773C1, 6A8312040EFA67F2).

**Known issues / later** (ROADMAP): an interrupted download starts again from zero; all pack maps are temperate;
the players list says "Not ready" while a client downloads (the progress is only in the map panel and the bottom
bar); on lava, units look warm and grey up close and the ash-crown trees read as grey mushrooms from above; the
retouched snowy conifers were not re-checked up close.

**Handoff review** (orchestrator; the agents wrote none). The notes were accurate and every successor continued
without redoing work. One seam bug: the unbuilt WIP the orchestrator committed after the usage limit (save finds its
map by hash) broke `MapFileTests`, and agent 2 ran only the net tests and its own sim tests, so agent 3 found it in
the full suite (as in 8b-1: run the full suites before a hand-over). Sub-agent 1 left the missing per-biome checks
and the minimap as named open items, which made sub-agent 2 a short, self-contained piece.

**Merge checks on main** (orchestrator, 2026-11-12 16:21-16:24, `tools/agents/merge-check.sh`): build 0 errors;
tests 603 sim + 343 client + 33 net green; reference match "Hard wins after 12:18", FB269C29202773C1; bench
6A8312040EFA67F2; screenshot `--demo --biome desert` (sand plateau with dark rock cliffs, cacti and sandstone
rocks, units readable, minimap in desert colours) looked right.

## Wave 9: visual polish (briefs re-cut 2026-11-11; 9b-1 and 9b-2 merged 2026-11-12)
9a (AI) was removed from the plan by the owner on 2026-11-11. 9b is cut into two modules of about 1,500 lines, one
module agent each as in (8b). Their files are disjoint from 8b-1 and 8b-2, so the pairs 8b-1 + 9b-1 and
8b-2 + 9b-2 can run together (at most 2 agents at a time).

*brief (9b-1) Death animations + ACU warp-in* (G23 first half; purpose "Animazioni di morte e warp-in (9b-1)"):
aircraft spiral down and crash where they fall, ships list and sink, structures collapse into their wreck, big
units get a longer death sequence; the ACUs warp in at the start of a game (flash, rings, sound). Client only
(`Rendering/` unit and effects renderers, `Audio/`); if an effect event needs one more field it is additive and the
sim results stay the same (reference hash unchanged). Leaves alone: `PropRenderer` (8b-1), `TerrainRenderer` and
`Minimap` (9b-2). Tests where there is logic (animation timing, the sink / fall paths); `--perf-log` on the 5k-unit
scene of DEV_GUIDE before and after; screenshots of each death kind and of the warp-in.

*brief (9b-2) Terrain self-shadows, unit LOD, minimap options* (G23 second half, G22; purpose "Ombre terreno, LOD,
minimappa (9b-2)"; after 9b-1): terrain self-shadows (baked from the heightmap, sun direction of the unit shadows),
a low-poly LOD for far units (measured on the 5k-unit scene), minimap resize / zoom / show-hide resources and units,
saved in the settings. Owns `TerrainRenderer*`, the LOD part of the unit renderer, `Minimap`, settings entries.
G24 (voice announcer) and G25 (TTF font) only if the owner says yes ("Questions for the owner"). `--perf-log` before
and after; screenshots.

### (9b-1) Death animations + ACU warp-in — report (merged 2026-11-12, 3a000b0)
Two module agents on Opus 5.5, one after the other in the same worktree (the first wrote most of the code and
stopped at 213k of context, 30k past its checkpoint, before the first build; the second made it compile and finished
in 23 minutes). 1,557 lines in `src` and `tests`. Client only: no sim change, same reference hashes as main.

**What works.** Aircraft spiral down and crash where their wreck is (the wreck stays hidden until the impact, a
smaller burst plays in mid-air); surface ships list, sink bow-up and fade out under the water; structures shake
and collapse while their wreck fades in; land experimentals stagger and fall over; commanders kneel until their
death blast. At game start the ACUs warp in: flash, beam, rings, the model rising bottom-up as a hologram, a sound.
Five new sounds (AircraftFall, AircraftCrash, ShipSink, StructureCrumble, WarpIn). Limits: at most 64 sequences at
once, none off-screen or under fog; land units other than experimentals and commanders get no sequence (keeps 5k
battles cheap); the warp-in plays only when the first snapshot is at tick 5 or earlier, so never after a load or a
replay seek. The wreck hiding (`PropRenderer.DeathFx.cs`) acts on wrecks only, never on 8b-1's trees and rocks.
How it works: ARCHITECTURE, the death animations section.

**How to try it.** `dotnet run --project src/Rts.Client -- --scene deaths[:air|:naval|:structures|:big|:acu|:warp][:N]`
(N = ticks after the kill at which the game pauses; the scene prints each group's position and a `--camera`).
No new hotkeys.

**Tests.** `DeathAnimTests` (6, client): the sequence each unit kind gets, the aircraft path ending on the wreck,
the ship sinking and fading, wreck visibility, shader packing, the warp-in tick rule.

**Shared files** (additive): GameSession.cs (scene line, one wiring line), GameApp.cs (scene line), EffectsRenderer.cs
(3 hook lines), PropRenderer.cs (1 line), the unit shader in Shaders.cs (the instance w value darkens and dithers
dying units; living units send 0), SoundId (5 values appended), GameAudio.cs (drains the new sound queue), CLAUDE.md.

**Numbers** (`--perf-log`, Release, before → after; taken before the merge of 8b-1).
- 5k-unit scene, 900 frames: CPU 2.49 → 2.38 ms, GPU 5.90 → 5.79 ms; 2400 frames: CPU 2.46 → 2.17 / 2.51, GPU 5.94 → 5.92 / 5.87
  (one run had a single 1 s frame that did not repeat: most likely machine load).
- Scene with many deaths (5k units + 600 interceptors + 400 generators, a nuke): CPU 3.73 → 3.67 ms, GPU 10.19 →
  10.07 ms. The nuke goes off at about tick 13, so that run mostly exercised structure collapses (capped at 64).
- Reference match and bench `2000 300`: the hashes of main (FB269C29202773C1, 6A8312040EFA67F2).

**Known issues.** No sequence for units that die inside a transport; models do not break into pieces; a
commander's death blast whites out the sequences near it (the scene keeps the ACU apart for that reason).

**Handoff review** (second agent). The note was accurate and the pending patch applied almost verbatim: one compile
error (`bp.DeathDelay` lives in `bp.Experimental`). The note did not say that `wait N` in `--exec` counts frames,
not ticks. The scene class had to be written from scratch; its warp variant must not step the sim before the start.

**Merge checks on main** (orchestrator, 2026-11-12 00:48-00:52): the first build died with the CLR flake of
DEV_GUIDE and passed when run again (0 warnings, 0 errors); tests 588 sim + 331 client + 27 net green; reference
match and bench give the hashes of main; screenshots of `--scene deaths:air` (a Soul Ripper burning on its way
down), `:naval` and `:warp` (the commander rising as a blue hologram inside a ring) looked right.

### (9b-2) Terrain self-shadows, unit LOD, minimap options — report (merged 2026-11-12, af7f792)
Three module agents on Opus 5.5, one after the other in the same worktree (a first start at 10:52 died on its
first call from a network error and wrote nothing). Agent 1 wrote the shadow bake, the mesh simplifier and the LOD
maths and stopped at 185k (11:12) before the first build; agent 2 built it, wired the LOD, the settings and the
minimap and stopped at 170k (11:35) with the GPU at the strategic camera 15 % over main; agent 3 removed that cost
and finished in 23 minutes (145k). 1,082 lines in `src` and `tests`. Client only: no sim change, same bench hash.

**What works.** Terrain self-shadows baked once per map from the heightmap with the sun of the unit shadows (about
30 ms in Release, 150 ms in Debug on a 1024 map), soft edges; the ground's lit value lives in the terrain chunk
vertex colours (every terrain LOD level, coarse vertices average the area they cover), so the ground reads no
texture; units read the shadow line from a texture once and darken in the shadow of a ridge, aircraft above it
stay lit. Far units switch to low-poly meshes (vertex clustering that keeps the team-colour / metal / glow tags; a
tank 296 → 82 triangles) by projected size with hysteresis; death fade and team colour are kept, holograms, cloak,
submerged and cargo use the full meshes. Minimap: size 75-175 % (grows upwards), mouse-wheel zoom 1-8x around the
camera, layers for resources, units and trees, small buttons while the mouse is over it. Settings: two Graphics
rows (terrain shadows, unit LOD) and a fourth tab "Minimap", all saved. How it works: ARCHITECTURE, the 9b-2 section.

**How to try it.** `--terrain-shadows on|off`, `--unit-lod on|off|PIXELS` (a number forces the LOD up close),
`--sun-elevation 20` (low sun for screenshots), `--minimap size=1.5,zoom=3,resources=off,units=on,trees=on,buttons=on`,
`--settings --settings-tab minimap`. No new hotkeys.

**Tests.** `TerrainShadowLodMinimapTests` (7, client: bake on flat ground and behind a ridge, blur, mesh simplifier,
LOD hysteresis, minimap maths, settings round trip, ground lit value and footprint mean of the terrain LOD
vertices); `AppSettingsTests` data extended.

**Shared files** (additive): GameApp.cs (one line in the CLI exclusion list + cases), Hud.cs (one line),
Shaders.cs (the unit and terrain shaders include the self-shadow code; death-fade lines untouched), PerfLog.cs,
AppSettings, GraphicsSettings, SettingsScreen, TerrainRenderer.cs (bake call), CLAUDE.md; ROADMAP G22 and G23 done.

**Numbers** (5k-unit scene of DEV_GUIDE "Profiling", Debug, 960x540, interleaved, both copies on the repo volume;
main → branch).
- Camera 110: GPU 4.39 → 3.47 ms, CPU 4.77 → 5.08.
- Camera 200: GPU 6.26 → 4.31 ms, CPU 6.69 → 7.15 (Release CPU 4.37 → 4.22); 4,794 LOD units = 393k triangles
  instead of about 1.42M.
- Camera 700: GPU 3.13 → 3.10 ms, CPU 4.35 → 4.23 (unit meshes are hidden beyond distance 260, icons only). Before
  agent 3's fix the ground's per-pixel shadow read cost 0.55 ms here (+15 %).
- Bench `2000 300` hash 6A8312040EFA67F2 (main).

**Known issues / later** (ROADMAP): the sun is fixed; trees, rocks and wrecks are not darkened by terrain shadows
(own shader); LOD meshes are built the first time a model is seen from far, a small hitch; in Debug the unit
gather stage costs 5-7 % more CPU at cameras 110 and 200 (Release: same as main).

**Handoff review.** Agent 2: the first note was accurate and its hook points matched the code; it lacked three
facts (the first ray sample darkened flat ground under a low sun, the raw shadow had stair-step edges, a new
settings field breaks the `AppSettingsTests` data). Agent 3: the note's "shadows off" figure came from another
batch of runs, not an interleaved comparison; the main copy for perf runs was at a main from before 8b-2; the bake
time was understated for Debug; docs and comments claimed props get terrain shadows (they don't); a duplicate "2x"
zoom label no screenshot had caught. Nothing broken at the seams.

**Merge checks on main** (orchestrator, 2026-11-12 11:57-12:01): build and tests 599 sim + 339 client + 27 net
green; reference match "Hard wins after 12:18", FB269C29202773C1; bench 6A8312040EFA67F2; screenshot of
`--demo --preset canyon --sun-elevation 22 --minimap size=1.5,zoom=2` (the canyon walls cast soft shadows on the
plateau, units in the sun keep their own shadows, the minimap larger and zoomed 2x) looked right.

## Wave 10: multiplayer robustness + keybindings (briefs re-cut 2026-11-12)
The two wave 10 briefs of 2026-10-23 (ROADMAP "Wave 10": 10a multiplayer, 10b keybindings) are cut into five
modules of up to about 1,500 lines, one module agent each on Opus 5.5 (cap 200k, no coordinator; WORKFLOW
"Lifecycle of a wave"). At most 2 run at a time, so they go in pairs with disjoint files:
1. **10a-1 + 10b-1** (sim math + client input): the first pair.
2. **10a-2 + 10b-2** (`Rts.Net` + client rendering/UI), after the first pair is merged (10b-2 adds hotkeys to the
   table that 10b-1 makes rebindable).
3. **10a-3** after 10a-2 (both own `Rts.Net` and the Net menus), with a wave 11 module as its pair.
Host migration (G39) and reconnect (G37) are not in wave 10: they need MP save/load (10a-3) first.
Sizes measured before the cut: `Rts.Sim` has 38 direct `MathF`/`Math` trig/pow calls in 12 files next to the
existing `Core/DMath.cs` wrappers (which still call `MathF`); `Rts.Net` is about 3,000 lines; the hotkey table
(`Input/Hotkeys.cs`) is read-only (`readonly KeyboardKey[] Keys`), and about 40 key checks outside it call raylib
directly (camera, selection, text fields, panels).

*brief (10a-1) Deterministic math across the sim* (G34; ledger purpose "Matematica deterministica (10a-1)"): today
the sim's trig goes through `MathF`, whose results are not guaranteed to be the same bits on every OS, CPU and
.NET runtime, so a Linux player and a Windows player can desync. Make every transcendental function the sim uses
pure IEEE float arithmetic: `DMath.Sin/Cos/SinCos/Atan2/Atan/Asin/Acos/Exp/Log/Pow` (range reduction +
polynomials, only `+ - * /` and `MathF.Sqrt`, which IEEE rounds exactly; no FMA, no `double` shortcuts, no table
whose content depends on the runtime), accurate to a few ulp, and route all 38 call sites and `DMath.Dir` /
`HeadingOf` through them, map generator included. Audit and fix the other cross-machine risks in `Rts.Sim` and
`Rts.Net`: `System.Numerics` helpers that call `MathF` inside (`Matrix3x2/4x4.CreateRotation*`, `Quaternion`,
`Vector2.Transform` with a rotation), `double` `Math.*` calls, culture-dependent parsing/formatting or string
comparison (`float.Parse`, `ToString()` without `InvariantCulture`, `string.Compare`, `OrderBy` on strings without
`StringComparer.Ordinal`), `HashCode.Combine` / `string.GetHashCode` (randomized per process) in anything that
feeds the sim. Add an analyzer-style test that fails if `MathF.Sin|Cos|Tan|Atan|Asin|Acos|Exp|Log|Pow` appears in
`src/Rts.Sim` outside `DMath.cs` (a source grep test is enough). Golden-value tests: a table of inputs →
expected bit patterns (`BitConverter.SingleToUInt32Bits`) for each function (edge cases: 0, ±π, huge angles, NaN,
denormals), plus a short fixed-seed headless game (e.g. 2 armies, 600 ticks) whose state hash is written in the
test: whoever runs the tests on another OS checks cross-OS determinism. Bump `Msg.ProtocolVersion` (peers with old
math must not join) and make replays recorded before this change refuse with a clear message instead of
desyncing (check what `GameInfo.CurrentSimBuild` already gives). The reference match and the bench hash WILL change:
document the new hashes (ROADMAP "NEXT STEPS" is the orchestrator's: put them in your report) and that the game
still ends with a winner. Perf: interleaved bench `2000 300` before/after (the software trig must not cost more than
2 % of the sim tick; `DMath.Dir` is hot). Owns `Core/DMath.cs`, the math call sites in `Rts.Sim`, `Msg.ProtocolVersion`,
new tests. Leaves alone: the client (it may keep `MathF`), `Input/`, menus (10b-1). DEV_GUIDE "Sim pitfalls": a line
on the new rule. Fragmentation: medium (mechanical, but the golden tests and the hash change belong together).

*brief (10b-1) Keybinding editor* (G19; purpose "Editor dei tasti (10b-1)"; with 10a-1): every entry of the hotkey
table can be rebound by the player and the choice is saved. `Hotkeys` gets a stable id per entry (its field name)
and a mutable override layer (keys + modifiers per id; rows of slots such as Z X C V B N and 0-9 rebind as a
whole row or stay fixed: decide and document), loaded from and saved to the settings file next to the other
`AppSettings` (an unknown id or key in the file is ignored, never a crash). A new `Menus/KeyBindingsScreen`
(a "Keys" tab in Settings, also from the pause menu): the table by help group, click an entry → "press a key"
capture with Shift/Ctrl/Alt (Esc cancels), the existing clash check (`Hotkeys.Overlaps`, `Share` tags; the logic of
`HotkeyTableTests`) refuses or asks to swap a clashing key, reset one entry / reset all. The help overlay (F1), the
order buttons' tooltips and the HUD help lines show the bound keys (they use `Hotkey.Label`). The direct
`IsKeyPressed` checks outside the table (about 40: `RtsCamera`, `SelectionController`, panels, `GameAudio`, …):
move into the table the ones that are game actions so they rebind too; text fields, menus and debug keys stay
fixed (list them in your ARCHITECTURE section). A `--settings-tab keys` screenshot hook. Owns `Input/Hotkeys.cs`,
`Input/SharedHotkeys.cs`, the new screen, the settings tab, the moved key checks (small edits in their files).
Leaves alone: `Rts.Sim` (10a-1), `Rendering/`, Minimap. Tests: override round trip through the settings file,
clash refused, reset, a rebound key fires its action and the old key no longer does, every table entry has a
unique id. Client only, hashes unchanged. Screenshots of the screen and of the help overlay with a rebound key.

*brief (10a-2) Observers, vote to drop, lobby pings, chat sounds* (G35, G39 without host migration; purpose
"Osservatori e voto di espulsione (10a-2)"; after 10a-1): an observer slot type in the lobby (any number of
observers next to the player slots, codec + protocol bump): an observer gets every turn, sends none (the game never
waits for it), sees the whole map without fog, can follow any army's view and use the players panel, chat with
everyone, and leaves without dropping anyone; a player who is defeated can stay as an observer. Vote to drop: when
the sim waits for a player longer than a few seconds, the "Waiting for X…" banner shows a countdown and a Drop
button for every other player; X is dropped (with today's `OnDrop` action) when a majority of the remaining players
vote, or by the host after the timeout as today. Per-player pings in the lobby slots (the in-game ping list
exists), and sounds for chat messages, player joined / left, ready and the game start countdown (reuse `Audio/`
cues; no new assets needed). Owns `Rts.Net` (lobby codec, `HostSession`, `ClientSession`, `NetGame`), the lobby and
Net in-game screens. Leaves alone: saves (10a-3), `Rendering/`, `UI/` panels (10b-2). Tests: codec round trip
with observers, a loopback game with an observer (game never waits, observer state hash = players'), vote majority
and tie, `Rts.Bench -- mp` with an observer (add an option). Screenshots: lobby with an observer and pings, the
drop vote banner.

*brief (10b-2) Selection overlays and client leftovers* (G20, the client items of G11, leftovers of 7b / 8a / 9b;
purpose "Overlay di portata e rifiniture client (10b-2)"; after 10b-1): range and intel rings for the selected
units and structures (weapon ranges, min range, radar / sonar / omni, shield radius, stealth / jammer field; today
only while placing), a toggle key and a settings entry, readable with 500 units selected (merge equal rings, cap the
count, no per-frame allocation); for the selection only, plus allied structures on Shift. G11 client items: wrecks
and mass/hydrocarbon deposits seen once stay drawn under the fog as last seen; an occupied enhancement slot swaps in
one click (the client queues remove + install; if the sim refuses that order, a small additive command is
allowed); the sandbox selects enemy units (F9 panel option); crush animation when a big walker steps on small
units if it fits. Leftovers: a minimap zoom hotkey (9b-2), camera bookmarks saved per map file / seed and listed on
screen, "add" variants (Shift) of the class filters (7b), joined wall corner / segment meshes (8a), the "structure
lost" toast skips self-destruct (7b). Owns `Rendering/` overlay files (new), `Minimap`, `UI/` panels, the new
hotkeys (rebindable through 10b-1's table). Leaves alone: `Rts.Net` and the Net menus (10a-2). Client only unless
the enhancement swap needs its command (then additive, hash unchanged when unused). Tests: overlay ring merging,
fog memory of wrecks / deposits, bookmark save round trip. `--perf-log` with 500 units selected; screenshots.

*brief (10a-3) Multiplayer save/load* (G36; purpose "Salvataggi in multiplayer (10a-3)"; after 10a-2): in a
multiplayer game any player can ask to save, the host decides; every peer writes the same save (state at the same
tick, the lobby: players, slots, teams, observers, options, map) into its `saves/`, marked as a multiplayer save.
Loading: the host picks a multiplayer save in a "Load game" lobby; the lobby shows the saved slots and each joining
player takes a saved slot (by name by default); peers that lack the file (or have a different hash) receive it from
the host in chunks like the map transfer of 8b-3, check the hash, all load and resume lockstep at the saved tick;
missing players can become AI (`OnDrop` action) or block the start. Autosave in MP: same tick on every peer, host
setting. The replay of a loaded game continues (or a new replay starts from the save: decide and document). Owns
`Rts.Net` save transfer + resume, the Net menus' save/load parts, the multiplayer part of client `Persistence`.
Tests: save in a loopback game → load → both peers' state hashes equal at the tick and N ticks later; a missing
file is transferred; a changed file is refused; `Rts.Bench -- mp` gets a save/load step. Screenshots: the load
lobby with saved slots.

### (10b-1) Keybinding editor — report (merged 2026-11-12, 2af6abf)
Two agents: the module agent stopped at its checkpoint (172k, 15 minutes) with the feature done, a fresh agent
ran the checks and wrote the report (92k, 6 minutes). 754 lines.

**What works**: every `Hotkeys` entry can be rebound. Each entry's id is its field name; the binding is an
override layer on the table (the defaults never change), saved in `AppSettings.KeyBindings` as `"v1;Id=Keys;..."`
(unknown ids, bad keys and clashing entries are skipped, never a crash). The Settings tab "Keys" (also from the
pause menu's Settings): click an entry, press a key with Shift/Ctrl/Alt (Esc cancels); a row of slots (Z X C V B N,
0-9) rebinds key by key as a whole row; a clashing key is refused with an offer to swap; right click or "Reset"
restores one entry, "Reset all" everything. Shared keys (D, E, U, M) act by selection only while both entries keep
the same key; once split, each key does its own action.
**How to try it**: `--settings-tab keys`, `--keys-capture Stop` (opens the capture prompt), `--bind
"Stop=F11,Patrol=Ctrl+G"` (bindings for that run only; screenshot runs never read settings.json), with
`--help-overlay` to see them in the F1 help.
**Moved into the table** (so they rebind): camera pan / rotate / reset, mute / music, the self-destruct dialog keys,
build and factory slot rows, tier, category, repeat, rotate structure, fog, perf overlay, debug F9/F10, sandbox
panel / console, upgrade, F5 / F6. **Stay fixed**: Esc, modifier reads, text fields, the sandbox console history,
menu keys; the replay viewer follows rebound keys but ignores modifiers. No new hotkeys.
**Tests**: 6 in `KeyBindingTests` (round trip, clash, swap, reset, rebound key fires / old key does not, unique
ids, corrupt entry ignored). **Shared files**: GameApp.cs (2 options), GameSession.cs (1 line), Hud.cs (dialog keys),
SettingsScreen (Keys tab), AppSettings (1 field), AppSettingsTests, HotkeyTableTests.
**Measured**: sim 603, client 349, net 33 green; bench `2000 300` hash 6A8312040EFA67F2 unchanged; `--perf-log
--demo` 1.7-2.1 KB/frame, the same as a main build (no allocations from the binding lookups).
**Known issues**: held camera keys now need Ctrl released, so Ctrl+arrows no longer pan (noted in ARCHITECTURE
"Keybindings"); later (ROADMAP G19): a second key per entry, a Shift+K warning, a search field in the Keys tab,
ReplayScreen keys ignore modifiers. In `--demo` the perf overlay draws over the help overlay (older).
**Handoff review** (second agent): the note was enough; it lacked the exact `--help-overlay` command and the normal
allocation numbers, so it built main in a temporary worktree to compare. Nothing was redone.
**Merge checks on main** (orchestrator, 2026-11-12 18:46-18:50): build and tests 603 sim + 349 client + 33 net
green; reference match "Hard wins after 12:18", FB269C29202773C1; bench 6A8312040EFA67F2; screenshot of
`--settings --settings-tab keys --bind "Stop=F11"` (the Keys tab by group, Stop shown as F11 with its Reset button)
looked right.

### (10a-1) Deterministic math across the sim — report (merged 2026-11-12, f3a50a6)
Two agents: the module agent stopped at its checkpoint (175k, 21 minutes) with the software math, the call sites
and the protocol bump done and the full suites unrun; a fresh agent finished it (137k, 17 minutes). 1,104 lines.

**What works**: every trig / exp / log / pow call of the sim goes through software `DMath` (`Core/DMath.Trig.cs`,
`DMath.Inverse.cs`, `DMath.ExpLog.cs`: float `+ - * /`, `Sqrt`, bit casts and integer math only; no FMA, no
`double`), so Linux and Windows peers compute the same bits. All 12 sim files are covered, map generator included
(`MapSymmetry` used `double Math.Cos/Sin`), and `Vector2/3.Lerp` became `DMath.Lerp` (it may use FMA on some CPUs).
A source grep test bans `MathF`/`Math` transcendentals, `Vector*.Lerp` and `MultiplyAddEstimate` in `src/Rts.Sim`
outside `DMath*.cs`. Save and replay headers record `GameInfo.MathVersion` (missing = 0 = old math): old replays
are refused ("recorded by an older game version…"; the replay list tags them "OLD VERSION"; `Rts.Bench -- replay`
exits with 2); old saves on generated maps show "Cannot load this game" (the regenerated terrain differs); other
old saves (map files, embedded terrain) load and play on, with the replay log restarted at the loaded tick.
`Msg.ProtocolVersion` 3 → 4. Audit: nothing in the sim state depends on hash order or culture; fixed
`MultiplayerSystem` `EndsWith` (Ordinal), `FactionDb` hex `TryParse` (InvariantCulture), `MapSymmetry` double math.
**How to check**: `dotnet test tests/Rts.Sim.Tests --filter "CrossOs|DeterministicMath"`; on another OS or CPU a
failing golden test = a cross-OS desync.
**Tests**: `DeterministicMathTests` (accuracy sweeps against double `Math`, golden bit patterns, IEEE edge cases,
the grep ban); `CrossOsDeterminismTests` (golden game: AI vs AI plus a brawl, 600 ticks, 2 threads, terrain hash
C870F62B6FC7C28D, state hash D273E007297C55F1; header field, replay refusal, old-save cases). No existing expected
value changed. **Shared files**: `GameInfo.cs` (field + one line), `SaveFiles.cs` (version checks), `SaveText.cs`
(tag), `Messages.cs`.
**Measured**: max error 2 ulp (Sin, Cos, Atan, Asin, Atan2, Log2), 1 ulp (Acos, Exp, Log), Pow 1 ulp (4 when
|y·log2 x| > 16). Bench `2000 300` interleaved: 1 thread 4.54 → 4.57 ms (+0.7 %), 15 threads 1.755 → 1.78 ms
(+1.6 %, noise level). Bench hash 6A8312040EFA67F2 → **2F09CD7D01AF287E**; reference match "Hard wins after 12:18"
FB269C29202773C1 → **"after 12:00", C87C46C4EA726A82**. `Rts.Bench -- mp` in sync; a replay recorded on the build
verifies 180/180.
**Known issues / follow-ups**: not run on Windows or ARM64 yet (run the golden test and `Rts.Bench -- replay` of a
Linux replay there); `Vector2/3` dot and length not checked across CPU types; old saves on generated maps cannot be
loaded.
**Handoff review** (second agent): the note was enough; it missed that replay writes copy the stored header without
refreshing it (a loaded old save would have marked its new replays as old math: loading now resets the version),
and that `--screenshot` needs the `screenshots/` folder (raylib says "saved" but writes nothing without it).
**Merge checks on main** (orchestrator, 2026-11-12 19:02-19:06; one doc conflict in ARCHITECTURE, both sections
kept): build and tests 614 sim + 349 client + 33 net green; reference match "Hard wins after 12:00",
C87C46C4EA726A82; bench 2F09CD7D01AF287E (both as the report says); `Rts.Bench -- mp` 1200 ticks, 120 hash checks,
no desync; `--demo` screenshot unchanged.

### (10b-2) Selection overlays and client leftovers — report (merged 2026-11-12, 57d4e20)
Three agents: the first stopped at its checkpoint (170k, 14 minutes) with the rings and the fog memory done, the
second was cut by the weekly-limit message before it changed anything, the third finished (147k, 14 minutes,
81 calls). 1,019 lines.

Branch `worktree-agent-ad3fa042eecd3d963`, head after merge of main 6df4f11 (main moved with docs only).

**Works**
- G20 range/intel rings of the selection (key I, setting "Range rings", Shift = allied structures; merged, capped at 128, no per-frame allocation).
- Wrecks and deposits remembered under the fog; never-explored ones are hidden.
- Enhancement swap/chain in one click: an amber "swap" button sends remove + the first step, and `UI/EnhancementChain.cs` sends the later steps. No sim change.
- Minimap zoom keys Ctrl+= / Ctrl+-.
- No "Structure lost" toast for self-destructed structures.
- Unit panel with 500 selected: 208 → 28 KB/frame. The cause was the LINQ grouping in `UnitPanel`, already on main. `--perf-log` now lists the stages that allocate.

**Try:** `--exec "spawn tank 500 me at 128,128" --select "everything@500" --perf-log`, then press I. For the swap: select an ACU and click T3E.

**Tests:** 7 ring/fog + 4 chain + 1 alert (client), 1 swap (sim). Client 361, sim 615, net 33: all green. `Rts.slnx` builds.

**Bench `2000 300`:** hash 2F09CD7D01AF287E (unchanged), tick 2.1–2.4 ms vs main 2.0–2.1 ms (noise). AI match not rerun (no sim code changed).

**New hotkeys:** I, Ctrl+= / Ctrl+KpAdd, Ctrl+- / Ctrl+KpSub.

**Shared-file edits:** GameSession (2 lines), Hud (profiler laps), SelectionController (note self-destruct), Hotkeys (3 entries), AppSettings/SettingsScreen/GraphicsSettings, CLAUDE.md, docs.

**Left (ROADMAP later lines):** saved bookmarks + list, Shift "add" class filters (Shift is already used), sandbox own-only option, joined walls, crush animation, ~22 KB/frame from input + placement with 500 selected.

**Design errors found**
- Props (wrecks, deposits) reach the client without intel filtering, so they were drawn in fog never explored. The client now hides them (a926ff4), but the snapshot still carries them: open.
- `UnitPanel` regrouped the selection with LINQ every frame (~180 KB/frame at 500 units): a per-frame UI path built on allocating helpers. Fixed in e85c944.
- The sim installs one enhancement step per command, so multi-step swaps need a client-side queue. That queue is not saved and not visible to other players: open (fine for now).

**Handoff review**
The note was accurate and the plan for the swap was right. It lacked a way to find which stage allocates, so I added per-stage allocations to `--perf-log`. The swap screenshot spawned a plain ACU: the `+ResourceAllocation` spawn suffix did not apply, so the occupied-slot path is covered only by tests.

**Merge checks on main** (orchestrator, 2026-11-12 21:26-21:30): build and tests 615 sim + 361 client + 33 net
green; reference match "Player (Hard) after 12:00", C87C46C4EA726A82; bench 2F09CD7D01AF287E; screenshot of
`--sandbox --exec "spawn tank 8 me at 128,128" --select "everything@8"` (one merged red weapon ring around the
group, the enhancement panel with amber T3E and SGF buttons) looked right.

### (10a-2) Observers, vote to drop, lobby pings, chat sounds — report (merged 2026-11-12, e9e8f8d)
Three agents: the first stopped at its checkpoint (170k, 11 minutes) with the whole `Rts.Net` part done, the second
was cut by the weekly-limit message after a minute or two (one test edit, sound), the third built every client
screen and finished (154k, 18 minutes, 91 calls). 1,035 lines.

**Works.** Protocol 5. Lobby: observers row (up to 16) with pings, Observe/Play buttons, host moves anybody ("Obs" / "Play") and kicks observers; a full game seats joiners as observers; ping next to each slot name. In game an observer gets every turn, sends none, never holds the game, has no fog and clicks a players-panel row to follow an army (top row = all); chat works; leaving drops nobody. A defeated player resigns (the others drop it as defeated) and stays as an observer. "Waiting for X": 5 s countdown, then "Drop X" for the other players; a strict majority of the remaining players drops X (OnDrop action; the host can't be voted out). Sounds: chat = click, join/ready/start = confirm, left = error.

**Try.** `--host 47704 --autostart` + `--join 127.0.0.1:47704 --observe --autostart` + a second `--join`; `--host 47702 --players 2 --autostart --net-vote-demo`; `Rts.Bench -- mp 3 600 256 0 0 obs=2`.

**Tests.** `ObserverTests` (5: codec; lobby moves, full game, kick, pings; late observer in sync; resign; vote rules); `LoopbackMatchTests` full lobby now gives an observer.

**Hotkeys.** None. CLI: `--observe`, `--net-vote-demo`, Bench `obs=N`.

**Shared files.** GameApp.cs (2 options), AppHost.MenuGameOptions (1 line), MultiplayerScreen (observe on join), CLAUDE.md, ROADMAP, DEV_GUIDE, ARCHITECTURE (own section).

**Numbers.** 614 sim + 349 client + 38 net (+5) green after merging main. Bench `2000 300` 2F09CD7D01AF287E, 2.30 ms/tick vs main 2.91. AI `ai 30 256 42 hard,normal` Hard after 12:00, C87C46C4EA726A82. mp `obs=2`, `1 AI obs=1`, `drop`: in sync.

**Follow-ups.** Host migration; joining a running game (G37); late observer catches up only at normal speed; dedicated sounds; vote banner seen only via the demo hook (logic covered by tests).

**Design errors found.**
- `NetLoadingScreen` dropped an early `Go` (`when _session != null`). That is fine for players but wrong for observers who are still loading. Fixed (`_goSeen`), commit ae142bb.
- Resign took effect only at the next sim step, so a held game never resigned. Fixed in Hold (e2675f2).
- A full game refused joiners. Fixed: they become observers (88aa2f0).
- `GameSession` uses `localArmy` as an index (`StartPositions[localArmy % n]`), so "no army" (-1) can't be expressed. Observers start on army 0. Open.

**Handoff review.** The note was precise. I rediscovered how UI sounds play (`AudioSystem.MenuUpdate` in menus, `GameAudio` in game) and that `MenuGameOptions` copies fields one by one (a new option needs a line there).

**Merge checks on main** (orchestrator, 2026-11-12 21:32-21:36; one conflict, ARCHITECTURE.md: both new sections
kept): build and tests 615 sim + 361 client + 38 net green; reference match "Player (Hard) after 12:00",
C87C46C4EA726A82; bench 2F09CD7D01AF287E; `Rts.Bench -- mp 3 600 256 0 0 obs=2` and `mp 2 600 256 1 0 obs=1` in
sync (60 hash checks each); screenshot of `--host 47702 --players 2 --autostart --net-vote-demo` ("Waiting for
Bob…", the "Drop Bob" button, "1/2 votes to drop Bob") looked right.
