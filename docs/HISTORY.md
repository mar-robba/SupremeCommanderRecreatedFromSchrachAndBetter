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
*in progress (2026-10-14)*

Goal: make a second faction possible:
- `data/factions/<id>.json` with `Roles` (role → blueprint id), a `FactionDb`, `UnitBlueprint.Faction`,
  `Army.Faction`;
- the AI and `StrategicApi` ask for roles instead of hard-coded `uef_*` ids;
- a faction selector in setup + `--faction` / `--factions`; a faction filter in the sandbox;
- an ARCHITECTURE "Factions" contract.
Its report goes here when it is merged.

## Wave 5 (in progress)

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
