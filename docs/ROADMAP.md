# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (handoff 2026-10-11 — read this first in a new session)
1. **Integrate the maps + menus branch** `worktree-agent-aee9b8d5b16b55e55` (worktree still at
   `.claude/worktrees/agent-aee9b8d5b16b55e55`, clean, commit 504efcb). It has map presets (Plains/Lakes/Highlands/
   Canyon/Archipelago, guaranteed land connectivity), main menu → setup with preview → game → pause → end screen with
   stats, and the in-game loop moved from GameApp.cs to the new GameSession.cs. It branched BEFORE the audio, fog and AI
   merges. Delegate to ONE agent working in that worktree:
   - `git merge main`;
   - move the audio/fog/wreck/context-order/cursor-hint wiring from the old GameApp loop into GameSession.cs;
   - keep all CLI options;
   - Army.cs: keep a single `AiDifficulty` enum;
   - wire the menu volume to GameAudio, the setup AI difficulty to Army.AiDifficulty, and the fog ViewArmy to the local human;
   - both test projects green, screenshots, commit.
   Then merge that branch into main.
2. **Wave 3** (4 parallel agents, worktrees):
   - (a) T2/T3 tech: in-place upgrades (factories, mex), T2/T3 land/air units, engineers.
   - (b) Strategic: shields, TML/TMD, nukes/anti-nuke, T3 artillery.
   - (c) Naval: naval factory, ships/subs, sonar, torpedoes; makes Archipelago playable.
   - (d) AI: RespectIntel=true, scouts/radar, balance. Since the fog merge, AI-vs-AI seed 42 has no winner in 30 min.
3. Then Phase 3/4 (experimentals, transports, 2nd faction, save/replays, multiplayer).

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
- [~] Better map generator — done on branch worktree-agent-aee9b8d5b16b55e55, main merged into it (2026-10-11),
      branch ready to merge: presets Plains/Lakes/Highlands/Canyon/Archipelago, guaranteed land connectivity, tests
- [~] Main menu / game setup screen, victory/defeat screens — same branch, merged with main's audio/fog/AI:
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
