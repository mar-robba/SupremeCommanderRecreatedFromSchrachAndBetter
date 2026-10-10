# Roadmap

Legend: [x] done · [~] in progress (owner) · [ ] todo. Keep this file updated when you finish work.

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

## Phase 1 — Core gameplay (wave 1, parallel agents)
- [ ] **Combat**: targeting (priorities, layers, visibility), turrets, direct/ballistic projectiles, beams,
      area damage, air-to-air, bombers, veterancy, wrecks (mass value) on death
- [x] **Economy & construction** (merged 2026-10-10): flow economy with efficiency/stall, engineers
      build/assist/repair/reclaim, factories with queue/repeat/rally/refund, deposits, storage, wrecks (PropStore)
      - [ ] later: Capture, pause toggle, adjacency bonus, units drive out of the factory, reclaim energy from units
- [ ] **Pathfinding & movement**: grid/hierarchical A*, structure-aware, path cache, group movement keeping
      formation speed, air movement (circling, landing), naval layer, stuck recovery
- [ ] **Client visuals**: procedural meshes per Visual.Shape, GPU instancing, team colors, projectiles,
      explosions/muzzle flashes from EffectEvents, water shader, better strategic icons, camera tuning
- [ ] **UI**: build menu (ACU/engineers), placement ghost with validity, factory queue panel, minimap,
      unit info panel, tooltips, order buttons

## Phase 2 — A playable skirmish
- [ ] Fog of war + radar (IntelSystem, per-army visibility grids, client fog overlay)
- [ ] Skirmish AI (build order, economy scaling, attack waves, defense) — `src/Rts.Sim/AI`
- [ ] Better map generator (rivers/lakes, cliffs, ramps, naval maps, more players)
- [ ] Main menu / game setup screen, victory/defeat screens
- [ ] Audio (raylib audio: weapons, explosions, UI)

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
