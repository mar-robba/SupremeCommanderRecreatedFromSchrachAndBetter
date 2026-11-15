# Architecture — the map

Goal: a Supreme Commander 1–style RTS: huge maps (5–20 km), thousands of units, strategic zoom, flow economy,
T1–T3 + experimentals, land/air/naval, deterministic lockstep simulation, multithreaded.

This page is the top level: what the parts are, how they talk, and which file describes each part. It is short on
purpose. Read it whole, then read only the area files of the code you touch.

| Level | Where | Holds |
|---|---|---|
| 0 | [CLAUDE.md](../CLAUDE.md) | Commands and the hard rules. Always in context. |
| 1 | this page | Projects, threads, tick pipeline, cross-area contracts, the table of areas. |
| 2 | [architecture/](architecture/)`<area>.md` | One area: its contracts first, then how each system works. |
| 3 | [architecture/index/](architecture/index/)`<area>.txt` | Generated: every source file of the area and its top-level types. |
| 4 | the code | Only the files you change. |

## Projects

<!-- arch:projects -->
- `src/Rts.Client` → Rts.Sim, Rts.Net; packages: Raylib-cs
- `src/Rts.Net` → Rts.Sim
- `src/Rts.Sim` → no project
- `tools/Rts.Bench` → Rts.Sim, Rts.Net
<!-- /arch:projects -->

`Rts.Sim` is the deterministic simulation and has no rendering dependency. `Rts.Net` is the lockstep multiplayer and
has no raylib either. `Rts.Client` is the raylib renderer, UI and input (Raylib-cs 8.1 = raylib 6.0, OpenGL 3.3 core,
GLSL 330). Units are data: `data/blueprints/units/*.json`, factions `data/factions/*.json`. The list above is
generated from the project files.

## Threads
```
Main thread (raylib)                    Sim thread (SimRunner)               Worker pool
---------------------                   ----------------------               -----------
input -> Command ----ConcurrentQueue--> drain, sort (army, seq)
                                        Simulation.Step(commands)  ------->  Par.For chunks of systems
render <---- SimSnapshot (pooled) ----- SnapshotWriter.Write
interpolates prev/cur by time
```
- Sim runs at 10 ticks/s × game speed. The renderer interpolates between the last two snapshots (one tick of
  latency, as in SupCom) so motion is smooth at any FPS.
- Snapshots are pooled: `TakeLatest()` and then `Return()` (the `SnapshotInterpolator` handles this).

## Tick pipeline (`Simulation.Step`, fixed order)

One row per call in `Simulation.Step`, in the code's order (a test compares the first column with the code).

<!-- arch:pipeline -->
| Call | Area | What it does |
|---|---|---|
| `EmpStun.Expire` | [combat](architecture/combat.md) | Clears the EMP stuns that ran out. |
| `CommandSystem.Run` | [orders](architecture/orders.md) | Validates and applies the players' commands (orders, factory queues, debug spawn). |
| `AiSystem.Run` | [ai](architecture/ai.md) | The skirmish AI armies think and produce normal commands. |
| `CommandSystem.Run` | [orders](architecture/orders.md) | Applies the AI's commands through the same path. |
| `SandboxSystem.BeginTick` | [sandbox](architecture/sandbox.md) | Sandbox only: batched spawns, infinite resources, instant build. |
| `TransportSystem.Run` | [transport](architecture/transport.md) | Sequential: load / unload / ferry / dock orders, fuel, carriers carry their cargo. Before the rebuild, so units that board leave the map the same tick. |
| `w.Units.RebuildAliveList` | [core](architecture/core.md) | The list of living units (carried units are skipped). |
| `w.Projectiles.RebuildAliveList` | [core](architecture/core.md) | The list of living projectiles. |
| `w.Grid.Rebuild` | [core](architecture/core.md) | `SpatialGrid`: counting sort, 8-unit cells. |
| `IntelSystem.Run` | [intel](architecture/intel.md) | Stealth mask first, then fog of war, radar, sonar, omni per team. |
| `AdjacencySystem.Run` | [economy](architecture/economy.md) | Adjacency bonuses, recomputed only when structures change. |
| `EconomySystem.Run` | [economy](architecture/economy.md) | Sums the production and the requests of the previous tick, computes efficiency, updates storage. |
| `OrderSystem.Run` | [orders](architecture/orders.md) | Parallel: the front order of each mobile unit becomes a movement goal and forced targets. |
| `ConstructionSystem.Run` | [economy](architecture/economy.md) | Engineers build, repair, reclaim, assist, capture; patrol automation. |
| `TeleportSystem.Run` | [commander](architecture/commander.md) | Personal teleporter: charge, then jump. |
| `FactorySystem.Run` | [economy](architecture/economy.md) | Build queues, unit release, rally orders. |
| `PathSystem.Run` | [pathing](architecture/pathing.md) | Resolves path requests: HPA*-style sector regions + cached region goal fields + corridor A* + string pulling; formation members splice a shared leader path; deterministic per-tick solve cap. Design notes in `Pathfinding/PathService.cs`. |
| `MovementSystem.Run` | [pathing](architecture/pathing.md) | Parallel, double-buffered `ScratchPos`: waypoint following, turn rate, priority separation (movers push idle friends), formation pace, stuck → repath, air orbiting, terrain / structure collision. |
| `ExperimentalSystem.Run` | [commander](architecture/commander.md) | Sequential: crush, delayed death blasts, mobile-factory carry, energy-per-shot charging. |
| `PropSystem.Run` | [map](architecture/map.md) | Trees knocked over by ground units, burning and burnt trees. |
| `CommanderDeathBlast.Run` | [commander](architecture/commander.md) | Fires queued commander death blasts into the strategic detonation queue. |
| `StrategicSystem.Run` | [strategic](architecture/strategic.md) | Sequential: silos and launches, shields and the shield index, missile defense. |
| `OverchargeSystem.Run` | [combat](architecture/combat.md) | Arms the next ACU shot from stored energy. |
| `TargetingSystem.Run` | [combat](architecture/combat.md) | Each weapon acquires or keeps a target its army detects. |
| `WeaponSystem.Run` | [combat](architecture/combat.md) | Aiming, firing, beams. |
| `ProjectileSystem.Run` | [combat](architecture/combat.md) | Projectile flight and hits (shields, walls, units, ground). |
| `DamageSystem.Run` | [combat](architecture/combat.md) | Applies the damage events in order and kills units. |
| `CleanupSystem.Run` | [game-options](architecture/game-options.md) | Frees dead slots, applies the victory rule, records score samples. |
<!-- /arch:pipeline -->

## Contracts between areas

These hold everywhere. The details are in the area named.

- **Determinism** ([core](architecture/core.md), CLAUDE.md hard rule 1): the same commands give the same `StateHash`
  on every machine and thread count. `DetRandom` and `DMath` only; no result may depend on `Dictionary` / `HashSet`
  order or on thread timing.
- **Threading** ([core](architecture/core.md), hard rule 2): parallel systems write only the unit they iterate;
  cross-unit effects are buffered and merged in chunk order, or happen in a sequential step.
- **Commands in, snapshots out**: the only way into the sim is a `Command` applied at the start of a tick
  ([orders](architecture/orders.md)); the only way out is a `SimSnapshot`, fog-filtered for the view army
  ([intel](architecture/intel.md)). The client never touches `SimWorld` (hard rule 3). Replays and lockstep
  multiplayer are command lists, so anything that changes the sim outside a command breaks both.
- **Saveable state** ([persistence](architecture/persistence.md), hard rule 9): sim state is plain fields, arrays and
  collections; caches are `[NoSave]`.
- **Data, not code** ([factions](architecture/factions.md), hard rule 10): units, abilities and factions are JSON;
  sim and AI code asks faction roles and never names a unit id.
- **Stable numbers**: enums are append-only (saves, replays and the wire store their values); a file format or
  protocol change bumps its version and keeps reading the old one ([persistence](architecture/persistence.md),
  [map](architecture/map.md), [multiplayer](architecture/multiplayer.md)).
- **Blueprint swaps**: upgrades and enhancements replace a unit's blueprint in place, same `UnitHandle`
  ([economy](architecture/economy.md), [commander](architecture/commander.md)); every module's `InitUnit` is
  idempotent.

## Areas

Every source file under `src/` and `tools/Rts.Bench/` belongs to exactly one area: the most specific pattern that
matches it (a path ending in `/` covers the folder and its subfolders; `*` stays inside one folder). This table is
what `tools/arch/arch.py` reads.

<!-- arch:areas -->
| Area | Code | Holds |
|---|---|---|
| [core](architecture/core.md) | `src/Rts.Sim/*.cs` `src/Rts.Sim/Core/` `src/Rts.Sim/World/` `src/Rts.Sim/Snapshot/` `src/Rts.Sim/Systems/` | World and stores, worker pool, deterministic math, snapshot, performance. |
| [factions](architecture/factions.md) | `src/Rts.Sim/Data/` | Blueprints, the faction and role contract, unit rosters. |
| [economy](architecture/economy.md) | `src/Rts.Sim/Economy/` `src/Rts.Sim/World/UpgradeState.cs` `src/Rts.Sim/Orders/UpgradeCommands.cs` | Flow economy, construction, factories, upgrades, adjacency, capture, reclaim. |
| [orders](architecture/orders.md) | `src/Rts.Sim/Orders/` `src/Rts.Sim/Systems/CommandSystem.cs` `src/Rts.Sim/Systems/OrderSystem.cs` | Commands, order queues, queue editing, pings. |
| [pathing](architecture/pathing.md) | `src/Rts.Sim/Pathfinding/` `src/Rts.Sim/Systems/MovementSystem.cs` | Clearance, slope classes, path search, movement. |
| [combat](architecture/combat.md) | `src/Rts.Sim/Combat/` `src/Rts.Sim/Systems/DamageSystem.cs` `src/Rts.Sim/Systems/IdleAirEngage.cs` `src/Rts.Sim/World/ProjectileStore.cs` `src/Rts.Sim/Orders/FireStateCommands.cs` `src/Rts.Sim/Orders/OverchargeCommands.cs` | Targeting, weapons, projectiles, damage, veterancy, EMP, Overcharge, walls. |
| [intel](architecture/intel.md) | `src/Rts.Sim/Intel/` | Fog of war, radar / sonar / omni, stealth, snapshot filter. |
| [strategic](architecture/strategic.md) | `src/Rts.Sim/Strategic/` `src/Rts.Sim/Snapshot/StrategicViews.cs` | Shields, silos, missiles, missile defense. |
| [commander](architecture/commander.md) | `src/Rts.Sim/Experimental/` `src/Rts.Sim/Commander/` `src/Rts.Sim/Orders/EnhancementCommands.cs` | Experimentals, enhancements, death blast, teleporter. |
| [naval](architecture/naval.md) | `src/Rts.Sim/Naval/` | Water structures, torpedoes, subs, hulls, walking ships. |
| [transport](architecture/transport.md) | `src/Rts.Sim/Transport/` `src/Rts.Sim/Systems/MovementSystem.Vtol.cs` `src/Rts.Sim/Snapshot/TransportViews.cs` | Transports, carriers, ferries, docking, fuel. |
| [ai](architecture/ai.md) | `src/Rts.Sim/AI/` | The skirmish AI. |
| [map](architecture/map.md) | `src/Rts.Sim/Map/` `src/Rts.Sim/World/PropStore.cs` `src/Rts.Sim/World/PropSystem.cs` | Generator, map files, props, biomes. |
| [persistence](architecture/persistence.md) | `src/Rts.Sim/Persistence/` `src/Rts.Client/Persistence/` | Saves, replays, migration, recording. |
| [game-options](architecture/game-options.md) | `src/Rts.Sim/GameOptions.cs` `src/Rts.Sim/PrebuiltBase.cs` `src/Rts.Sim/Economy/DiplomacySystem.cs` `src/Rts.Sim/Economy/EconomyShare.cs` `src/Rts.Sim/Orders/DiplomacyCommands.cs` `src/Rts.Sim/World/ArmyStats.cs` `src/Rts.Sim/Systems/CleanupSystem.cs` | Game options, victory, teams, score. |
| [sandbox](architecture/sandbox.md) | `src/Rts.Sim/Systems/Sandbox*.cs` `src/Rts.Sim/Orders/SandboxCommands.cs` `src/Rts.Client/Sandbox/` | Sandbox commands, panel, console. |
| [multiplayer](architecture/multiplayer.md) | `src/Rts.Net/` `src/Rts.Sim/Orders/MultiplayerCommands.cs` `src/Rts.Sim/Systems/MultiplayerSystem.cs` `src/Rts.Sim/Persistence/MultiplayerSaveInfo.cs` `src/Rts.Client/Menus/Lobby*.cs` `src/Rts.Client/Menus/Net*.cs` `src/Rts.Client/Menus/Multiplayer*.cs` | Lobby, lockstep, transfers, observers, net screens. |
| [client](architecture/client.md) | `src/Rts.Client/*.cs` `src/Rts.Client/Menus/` `src/Rts.Sim/Persistence/UserDirs.cs` | App, session, camera, screens, settings, scenes. |
| [rendering](architecture/rendering.md) | `src/Rts.Client/Rendering/` | Terrain, units, effects, fog, props, shadows, LOD. |
| [input-ui](architecture/input-ui.md) | `src/Rts.Client/Input/` `src/Rts.Client/UI/` | Selection, orders input, hotkeys, HUD, minimap, alerts. |
| [audio](architecture/audio.md) | `src/Rts.Client/Audio/` | Sound synthesis, alerts, voice announcer. |
| [tools](architecture/tools.md) | `tools/Rts.Bench/` | The headless bench tool. |
<!-- /arch:areas -->

A feature that spans sim and client is described where its sim part lives, and the client areas point to it. Tests are
not in an area: each area file names the tests that prove its contracts.

**Finding an old section.** Code comments and older docs cite sections of this file by name (`ARCHITECTURE
"Map files"`). Each one is now a `##` heading in an area file, with its old title:
`grep -rn '^## .*Map files' docs/architecture/`.

## Keeping these docs true

The docs are worth reading only while they match the code, so the match is enforced. The tool is
`tools/arch/arch.py` (`python3 -I tools/arch/arch.py <command>`; details in [DEV_GUIDE.md](DEV_GUIDE.md)
"Architecture docs gate").

**1. Checks on the files (`check`, run by `ArchitectureDocsTests` in every test project).** The test suites are red when:
- a source file belongs to no area, or a pattern of the table matches nothing;
- a source file is not named in any architecture doc (the files that were undocumented on 2026-11-15 are listed in
  [architecture/index/undocumented.txt](architecture/index/undocumented.txt); the list may only shrink);
- these docs name a source file or a code identifier that no longer exists;
- the pipeline table above differs from `Simulation.Step`;
- the generated files (the indexes, the project list) are stale: run `sync`.

**2. The commit gate (git `commit-msg` hook, `tools/git-hooks/`).** A commit that changes source files must also
change the doc that describes them (the file's area doc, or any architecture doc that names the file:
`arch.py where <file>`), or say in its message why the docs still hold, one line per area:

    Arch: combat, pathing - bug fix in the separation query, no behavior described in the docs changes

A commit whose message starts with `WIP` is not checked. A merge is checked as a whole: every area whose code the
branch changed needs a doc change or an `Arch:` line somewhere in the branch (`arch.py review main <branch>` shows the
table). `--no-verify` is not allowed.

**3. What needs a doc change, not an `Arch:` line.** Change the area doc when the change alters what the doc says or
should say: a new system, pipeline step, command, order type, snapshot field or file format; a contract between
areas; where state lives or how it is saved; a rule other code relies on (ordering, thread safety, units of measure,
who may call what); a number the doc states. An `Arch:` line is for changes the docs do not describe: a bug fix that
restores the documented behavior, a refactor inside one file, performance work with the same results, tests, comments.

**4. Where to write.** In the area file, under the section of the system, or a new `##` section for a new system.
Contracts go in the "Contracts of this area" list at the top. A new area = a new file + a row in the table above. Do
not append a section per wave: describe the system as it is now (what was built when is in [HISTORY.md](HISTORY.md)).
A change to the projects, threads, pipeline or cross-area contracts also updates this page.
