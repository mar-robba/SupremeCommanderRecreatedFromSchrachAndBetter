# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (updated 2026-10-12 afternoon — read this first in a new session)
main has: maps + menus, and wave 3a (T2/T3 tech + in-place upgrades, `--tech-demo`). Tests: 155 sim + 69 client.

**Running (or finished, waiting to be merged): 4 agents in worktrees** under `.claude/worktrees/`.
Check each with `git -C <dir> log --oneline -3` and `git -C <dir> status --short`.
| area | worktree dir / branch `worktree-agent-<id>` | latest agent id (2026-10-12) |
|---|---|---|
| (b) strategic: shields, TML/TMD, nuke/anti-nuke, T2/T3 artillery, `--scene strategic` | `agent-ae5eb2c4f3916597c` | a9fccb82ae6e7beb1 |
| (c) naval: naval factories T1–T3, ships/subs, sonar, torpedoes, playable Archipelago, `--scene naval` | `agent-a22a2b0fcf5a1b603` | a128c58db97bd1e5a |
| (d) AI: RespectIntel, scouting/radar, finish games on Highlands/Canyon, Hard > Normal, FFA | `agent-a1c38e069748a3b2b` | acebc58040f7127e1 |
| (e) sandbox (owner request): `--sandbox`, `--exec`, menu button, F9 panel, F10 console, cheats | `agent-a1b89b5d8e6a88a18` | a112d86e1a631c547 |
Handoff notes (done / TODO): `~/.claude/projects/-run-media-marco-Volume-D-RTS/orchestrator/handoff-{b,c,d,e}.md`.
These are durable copies; the live ones are in the session scratchpad. If an agent is unfinished in a new session,
spawn a FRESH agent in the same worktree dir (absolute path, no isolation) with: the area scope (table above + Phase 3
lines below), its handoff note, the 300k-context rule, and permission to use sub-agents.

**Merge procedure** (orchestrator, in the main checkout; merge order b → c → e → d):
1. `git merge --no-ff worktree-agent-<id>`; conflicts are expected in shared files: resolve additively.
2. `dotnet build Rts.slnx`, then both test projects green.
3. One screenshot of the feature's scene. Then `git worktree remove` + `git branch -d`, and update this section.
After (b) and (e) are both merged:
- switch `SandboxSystem.Nuke` to (b)'s public nuke-launch entry point;
- unify the two nuke effects (`EffectKind.Nuke` exists on both branches).
This can be a small agent task.

Shared contracts (still valid): `BuiltBy` (a blueprint adds itself to builders' lists; unknown ids are ignored) and
`UpgradesTo` (in-place upgrade). Enums are append-only. Edits to shared files stay minimal and additive. New models go
in new partial files, with only a `case` line in UnitModels.cs.

Then:
- **Wave 4**: the AI uses T2/T3 upgrades, naval, shields, TML and nukes; balance pass.
- **Phase 3/4**: experimentals, transports, 2nd faction, save/replays, multiplayer.

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
- [x] T2 and T3 tech (wave 3, agent a, 2026-10-12): in-place upgrades (`UpgradeSystem`: land/air factory, mex,
      radar→omni; own build power + engineer assist, factory queue pauses, mex keeps producing, cancel refunds, same
      handle), 30 UEF T2/T3 blueprints (engineers, factories, mex, pgen, mass fab, PD, flak, SAM, omni, Pillar,
      Riptide, Brimstone MML, Sky Boxer, Titan, Percival, Demolisher, gunship, Janus, Wasp, Ambassador, spy plane),
      T2/T3 models, upgrade tile/U hotkey/progress bars/hologram, build-menu tier pills, `--tech-demo`
      - [ ] later: T2/T3 naval (agent c), AI uses T2/T3 + upgrades (wave 4), adjacency, T3 engineers build experimentals
- [ ] Shields, tactical missiles, artillery structures, strategic nukes + anti-nuke
- [ ] Experimentals (e.g. Fatboy-like, Monkeylord-like, Czar-like)
- [ ] Transports/ferry, air staging, engineer assist chains
- [ ] Second faction (Cybran-like) with distinct visuals and units
- [ ] Mass fabricators, adjacency bonus

## Phase 4 — Scale and polish
- [ ] 1024 maps with 4–8 players, 5k+ units at 60 FPS (instancing, LOD, culling)
- [ ] Save/load, replays (command logs)
- [ ] Lockstep multiplayer (deterministic math audit, desync detection via StateHash)
