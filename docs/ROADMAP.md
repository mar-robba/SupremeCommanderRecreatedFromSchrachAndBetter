# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

## ▶ NEXT STEPS (updated 2026-10-12 evening — read this first in a new session)
main has all of wave 3 except the AI:
- maps + menus;
- (a) T2/T3 tech + upgrades (`--tech-demo`);
- (b) strategic: shields/TML/TMD/nukes/artillery, `--scene strategic[:shields|:tml|:nuke]`;
- (c) naval, `--scene naval`;
- (e) sandbox: `--sandbox`, `--exec`, F9 panel, F10 console. The sandbox `nuke` fires a real missile; `nuke instant` is the old blast.
Tests: 229 sim + 158 client.

**Pending:**
1. **(d) AI**: worktree `.claude/worktrees/agent-a1c38e069748a3b2b`, branch `worktree-agent-a1c38e069748a3b2b`.
   **Stopped by the owner on 2026-10-12: ask the owner before restarting it.**
   - Last state: WIP commit d05dca3, NOT fully tested. It has already merged main up to the sandbox, but not strategic/naval.
   - Handoff: `~/.claude/projects/-run-media-marco-Volume-D-RTS/orchestrator/handoff-d.md`.
   - Remaining work: final tests, `git merge main`, bench matrix (every 1v1 should end within 30 min; Hard should beat
     Normal in 3 of 4), docs.
2. Remove the naval worktree `.claude/worktrees/agent-a22a2b0fcf5a1b603` (already merged; it was left in case a sub-agent
   of the naval agent was still running): `git worktree remove ... && git branch -d worktree-agent-a22a2b0fcf5a1b603`.
3. Known follow-ups:
   - `--scene strategic` needs `--camera` to frame the action;
   - collapsed shields still pay upkeep;
   - bombers fly over shield domes;
   - ships can overlap;
   - box-select picks only own units (sandbox);
   - the AI uses no T2/T3/naval/strategic yet.

Agent workflow: see the memory file agent-context-handoff. In short: 300k context cap; fresh agent + handoff note; agents may use sub-agents.
Orchestrator tools (`agent_ctx.py`, `watch_agents.sh`) and handoff/report files are in
`~/.claude/projects/-run-media-marco-Volume-D-RTS/orchestrator/`.

**Next waves:**
- **Wave 4**: the AI uses T2/T3 upgrades, navy and strategic weapons; balance pass. Then the polish items above.
- **Phase 3/4**: experimentals, transports, 2nd faction, save/replays, multiplayer.

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
      - [ ] later: select enemy units for the actions (box-select only picks own units), save/load sandbox setups

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
      - [ ] later: collapsed shields still pay upkeep; AI builds/uses shields, TML, nukes (wave 4); bombers fly over domes
- [x] Naval (wave 3, agent c, 2026-10-12): naval factories T1→T2→T3 (in-place upgrades), frigate, attack sub,
      destroyer (guns + torpedoes + torpedo defense), cruiser (AA + missiles), battleship, T2 torpedo bomber, T1/T2
      sonar, torpedo launcher; water placement (`NavalRules`, client ghost), torpedoes (run under water, home, die on
      shore), rate-limited torpedo defense, subs only seen by sonar/omni and only hit by torpedoes; Archipelago sea
      lanes + harbor beaches (playable: coast reachable, battleship lanes between starts); client: ship models, wakes,
      torpedo bubble trails, dim submerged subs, sonar ring, `--scene naval`
      - [ ] later: AI builds and uses navy (wave 4), naval adjacency, sub surfacing toggle, ship-to-ship collision
            avoidance in narrow channels
- [ ] Experimentals (e.g. Fatboy-like, Monkeylord-like, Czar-like)
- [ ] Transports/ferry, air staging, engineer assist chains
- [ ] Second faction (Cybran-like) with distinct visuals and units
- [ ] Mass fabricators, adjacency bonus

## Phase 4 — Scale and polish
- [ ] 1024 maps with 4–8 players, 5k+ units at 60 FPS (instancing, LOD, culling)
- [ ] Save/load, replays (command logs)
- [ ] Lockstep multiplayer (deterministic math audit, desync detection via StateHash)
