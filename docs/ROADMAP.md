# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (updated 2026-10-11 — read this first in a new session)
Maps + menus are merged (main 1f1087f+). **Wave 3 is running: 4 agents in worktrees** (branches
`worktree-agent-*`; check `git worktree list`). When an agent finishes, the orchestrator merges its branch into main,
runs both test projects plus screenshots, then removes the worktree and branch. Merge order: (a) first, then (b), (c), (d).

Shared contracts (on main before wave 3 started):
- `BuiltBy: [builder ids]` in a blueprint adds it to those builders' build lists (unknown ids ignored), so agents add
  units WITHOUT editing other builders' files. `UpgradesTo: "<id>"` declares an in-place upgrade
  (`UpgradesToIndex` / `UpgradesFromIndex`); agent (a) implements the upgrade mechanism.
- Reserved ids: `uef_t2_engineer`, `uef_t3_engineer` (owned by a), `uef_t2_land_factory`, `uef_t3_land_factory`,
  `uef_t2_air_factory`, `uef_t3_air_factory`, `uef_t2_mass_extractor`, `uef_t3_mass_extractor` (a);
  `uef_t1_naval_factory`, `uef_t2_naval_factory`, `uef_t3_naval_factory` (c).
- New enum members (OrderType, CommandType, UnitCategory, snapshot fields) are appended at the END; shared files
  edits stay minimal and additive; new models go in new partial files with only a `case` line in UnitModels.cs.

Wave 3 agents:
- (a) T2/T3 tech: in-place upgrades (factories, mex), T2/T3 land/air units, T2/T3 engineers, T2/T3 power, UI for upgrades.
- (b) Strategic: shields, TML/TMD, nukes/anti-nuke (silo stockpile + launch order), T3 artillery.
- (c) Naval: naval factories T1–T3, ships/subs, sonar, torpedoes; makes Archipelago playable.
- (d) AI: RespectIntel=true, scouts/radar, fix Highlands/Canyon stalemates, Hard noticeably stronger, 4-player FFA.
Then: wave 4 = AI uses T2/T3/naval/strategic; Phase 3/4 (experimentals, transports, 2nd faction, save/replays, MP).

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
      - [ ] later: EnergyPerShot, veterancy bonuses, idle aircraft engaging, terrain blocking direct fire
- [x] **Economy & construction** (merged 2026-10-10): flow economy with efficiency/stall, engineers
      build/assist/repair/reclaim, factories with queue/repeat/rally/refund, deposits, storage, wrecks (PropStore)
      - [ ] later: Capture, pause toggle, adjacency bonus, units drive out of the factory, reclaim energy from units
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
      - [ ] next: switch to RespectIntel=true now that fog exists, use scouts/radar (after fog merge AI-vs-AI seed 42
            no longer ends within 30 min), less midgame mass stall, 4-player FFA finishing, Hard noticeably stronger
- [x] Better map generator (merged 2026-10-11): presets Plains/Lakes/Highlands/Canyon/Archipelago, guaranteed land connectivity, tests
- [x] Main menu / game setup screen, victory/defeat screens (merged 2026-10-11):
      GameSession owns fog/wrecks/audio/cursor hints; setup AI slots → `Army.IsAI` + `AiDifficulty` (in-sim AI);
      fog ViewArmy = local human (defeated player "Keep Watching" = fog off); settings: master/effects/music
      volumes (saved) on an app-wide `AudioSystem` (menus: clicks + music); end screen `ArmyStats`
      - [ ] AI on new presets: builds and fights everywhere, but Highlands/Canyon AI-vs-AI often end in a stalemate
            (bench: Plains s42 winner 19:49, Lakes s5 winner 13:06, Highlands s7 none in 40 min, Canyon s3 none in 30)
- [x] Audio (merged 2026-10-11): 55 procedural SFX + ambient wind + generative music, positional, voice-limited,
      graceful without audio device (`--nosound`); wreck rendering; context orders (reclaim/repair/assist);
      kills + veterancy stars in unit panel; effects follow game speed
      - [ ] later: a human listening pass on the sounds, veterancy bonuses in the sim (volume settings: menus branch)

## Phase 3 — SupCom depth
- [ ] T2 and T3 tech: upgrades of factories/mexes, T2/T3 units for land, air, naval
- [ ] Shields, tactical missiles, artillery structures, strategic nukes + anti-nuke
- [ ] Experimentals (e.g. Fatboy-like, Monkeylord-like, Czar-like)
- [ ] Transports/ferry, air staging, engineer assist chains
- [ ] Second faction (Cybran-like) with distinct visuals and units
- [ ] Mass fabricators, adjacency bonus

## Phase 4 — Scale and polish
- [ ] 1024 maps with 4–8 players, 5k+ units at 60 FPS (instancing, LOD, culling)
- [ ] Save/load, replays (command logs)
- [ ] Lockstep multiplayer (deterministic math audit, desync detection via StateHash)
