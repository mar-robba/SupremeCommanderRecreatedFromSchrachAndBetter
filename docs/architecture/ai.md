# Skirmish AI

The in-sim skirmish opponent. Measurements, test matrices and every tuning result: [AI_TUNING.md](../AI_TUNING.md).

**Contracts of this area**
- The AI runs inside the deterministic sim and acts only through normal `Command`s, like a player.
- It sees enemies only through `AiPerception` (fog-fair) and gets no resource or vision bonus at any difficulty.
- It asks faction roles, never unit ids.
- All AI state is plain saveable fields (`AiSaveLoadTests`).
- Owner decision 2026-11-11: improving the AI is no longer a goal of the project. It only has to keep working (AI
  tests, the reference match at every merge).

Files and top-level types of this area: [index/ai.txt](index/ai.txt) (generated).

## Skirmish AI (`Rts.Sim/AI`)
- Runs inside the deterministic sim (sequential, AliveList order, DetRandom, no Dictionary/HashSet iteration) for
  armies with `Army.IsAI && !Defeated`, and acts ONLY through normal `Command`s (`AiCommands`), like a player.
  State per world via ConditionalWeakTable (`AiSystem.StateOf(w)`): one `AiBrain` per army + per-unit-slot
  `AiUnitInfo` (role/task, generation-checked).
  All AI state is plain fields/lists in that table, so it is saved and restored with the game (test
  `AiSaveLoadTests`: save mid-battle, load, identical continuation); only the `AiSystem.Trace` hook is `[NoSave]`.
- `AiBrain` thinks on a staggered schedule (economy every `EconInterval`, military every `MilitaryInterval` ticks,
  offset by army index); difficulty (`Army.AiDifficulty`: Easy/Normal/Hard) selects an `AiConfig`. No difficulty
  cheats (no resource bonus, no map vision): Hard is better through faster reactions, more factories, raids and
  commander snipes (until the 2026-10-23 unit balance also an artillery-heavy mix, pull-back and focus fire; see below).
  Cost ≈ 0.02-0.03 ms/tick per AI (worst game ≈ 0.05).
- `AiPerception` (fog-fair, `RespectIntel=true`): the only window on enemies. Enemy units count only while seen or on
  radar (radar blips = anonymous, assumed one tank), structures are remembered as ghosts until seen dead, threat maps
  = current threat + decaying memory, per-area staleness (when did we last look), enemy commander last seen (only
  when actually visible). `AiScouting`: land/air scouts to stale areas, radar at base then expansions.
- `AiEconomy`: commander opening (factory, mexes, pgens, hydro), engineer jobs (finish orphan sites, power when
  income < max(smoothed demand, projected spending), mexes nearest the base first + chains, hydro, factories when
  mass income/floating allows (more land factories while floating), air factory mid-game, PD/AA, storage/radar,
  repair, reclaim, assist factories), task deadlines (stuck → reassign, deposit blacklist), engineers flee
  threats; factory queues (engineer target, scouts, tank/artillery/mobile-AA mix by enemy air; interceptors/bombers).
- `AiPlacement`: spiral search with `OccupancyGrid.CanPlace` + free margin (3 factories / 2 others, relaxed pass),
  keeps deposits and the rally point clear, land-reachability for factories, reservations, failure cooldown.
- `AiMilitary`:
  - Pool at the rally point → one main army (`AiGroup`, Assemble/Attack/Retreat). Objective = best reachable known
    enemy structure (value by type − distance, outgunning defenses avoided, enemy base avoided before `PushTime`,
    base/clusters preferred when pushing), else the enemy commander's last known position, else stale enemy starts;
    unreachable/stuck targets blacklisted 150 s.
  - Staging point `StageDistance` short of the objective along the walking route (`AiBrain.SolveRoute`), outside
    known defenses. Launch when gathered and strength ≥ `RequiredStrength` (AttackMargin × (defenses at the target +
    EnemyArmyShare × rest of the estimated enemy army)), or when at `MaxArmy`, or after `AssembleTimeout`.
  - Attacking: reinforcements walk in batches to fixed points (staging, then the target) and merge near the army
    anchor; strung-out armies regroup; idle members engage the nearest known enemy; position targets end after
    `PositionTargetTime` when dominating; next objective nearby → keep going, far → stage again. No difficulty
    retreats a beaten army (matrix: retreating lost more games than fighting on); the army is recalled when the base
    is losing (not when finishing the enemy). Late game (`LateGameTime`): looser launch rule, snipes for everyone.
  - Defense: pool + defenders vs base/expansion threats, nearby army members help vs real base attacks; sorties to
    an attacked expansion; clearing groups for enemies on our deposits; commander retreats when hurt.
  - Hard: raids on weakly defended enemy extractors, commander snipe (direct-fire members focus a visible enemy
    commander near the army), pull-back of units under 25 % health, focus fire (`PickFocusTarget`: each direct-fire
    member takes the weakest identified enemy in range after the damage already assigned, FocusHorizon = 3 s).
  - Air: interceptor patrols, bomber groups on raid targets; air scouts.
  - Navy (`AiNavy.cs`, `AiNavy.Fleet.cs`, wave 5 k-navy): `Decide` once (no DeepWater = nothing, so dry maps are
    unchanged) looks for a coast site near the base whose water (class-2 graph) reaches an enemy base; `Priority` =
    no land path to any enemy start (the navy gets the factory budget before the land factories). Engineers build
    naval factories, torpedo launchers (after enemy ships came near) and sonar; ships gather at `Home` (defend the
    coast), sail at MinFleet + strength (or timeout with 0.7x the required strength), attack enemy ships > coastal
    structures by value > stale enemy starts, retreat when beaten. Fleet members: Role Naval, Group 1000001.
  - Transports (`AiTransport.cs`, `AiTransport.Drop.cs`): island mode = no enemy start reachable on foot. Builds
    transports (hook `ManageFactories` before the land factories; an air factory job before the mass-stall gate),
    picks a landing zone on the target's island (rings 30-100 x 16 around the enemy start, scored by ground/air
    threat, anti-air along the flight, enemy defense radius, nearness to enemy structures and to the enemy commander's
    last seen position (it kills most drop units), bad-LZ memory), runs a
    ferry route (`TransportOrders.Ferry(beacon, LZ)`) and sends idle Pool/army units to the beacon (Role Transport,
    Group 1000002: the military code ignores them; released to Pool if the base is attacked). Landed units gather
    until DropWave units and 0.7x the enemy land estimate (or DropGatherTime), then attack-move the best known
    structure on that island (factories first, away from the enemy commander; Hard: extractors first). Above DropIdleCap idle land units the land/air factories get no budget. Air staging
    (T2) + Dock for low-fuel transports. Knobs: `AiConfig.Navy.cs` (Drop*, fleet), `MaxTransports` (AiConfig.cs).
  - T2/T3/strategic weapons: see the wave 5k AI_TUNING sections.
- Results (bench, 30 min, 256 maps, 4 land presets): Normal vs Normal ends by commander kill in ~86 % of games
  (mean ≈ 18.6 min); Hard beats Normal in 86 % of games (166 won / 8 lost / 18 undecided of 192), Easy loses to
  Normal in 23 of 24; Hard vs Hard ends in ~69 %; 4-player FFAs on 512 end by ~30 min. Stalemates are equal armies
  trading in the middle of Highlands/Canyon maps. T1 mobile artillery is very cost-efficient (balance pass later).
- Tech and budget (wave 5k): `AiEconomy.Tech.cs` upgrades extractors and factories after `MexT2Time`/`MexT3Time`,
  `LandT2Time`/`LandT3Time`/`AirT2Time`/`AirT3Time` within `UpgradeShare` of the mass income (`MaxMexUpgrades`,
  `MaxFactoryUpgrades` at once), buys commander enhancements after `EnhanceTime`, builds tiered engineers/power/
  defenses, pauses mass fabricators when energy runs short, runs the quantum gateway (`MaxScus`) and the big projects
  (`ShieldTime`: base shields, TMD, artillery installations, storages; `TmlTime`; `StrategicTime`: nukes,
  experimentals; an anti-nuke as soon as an enemy launcher is seen). `AiStrategic.cs` aims the tactical/strategic
  launchers. `AiEconomy.Budget.cs`: the factories get at least `ArmyShare` (0.5) of the mass income and eco/tech
  commitments start only while the rest leaves room; **pressured** (after 5 min, own land strength below
  `PressureRatio` 0.8 x the enemy's estimate, or enemies at the base) the share is `PressureArmyShare` 0.85, so tech
  waits while the army is losing (tech vs a T1-only AI: 51 % -> 74 % of decided games). Unit choice by faction roles;
  caps `MaxTier` (structures/engineers), `MaxUnitTier` (combat units), `ArtilleryTier`. Off by default (AI_TUNING):
  `StallTime` fall-back, `AdvanceInterval` creeping advance, `LeadArmyShare`. Since the 2026-10-23 unit balance Hard
  no longer uses the 0.55 artillery share, focus fire or the unit pull-back (they lost to Normal); its edge is now 8
  land factories, raids, faster thinking, army share 0.6 and artillery 0.05 (about 2:1 vs Normal). Numbers and the
  open stall problem: AI_TUNING "New goals", "Hard's edge" and "Unit balance".
- Bench: `dotnet run -c Release --project tools/Rts.Bench -- ai [minutes] [map] [seed] [diff0,diff1,...] [threads] [players] [preset] [factions]`
  (`aipassive` = army 0 idle). Env: `AI_STATUS_SECONDS` (status lines), `AI_GROUPS=1`, `AI_TRACE=1` (decisions),
  `AI_DUMP`, `AI_DEPOSITS`, `AI_OMNI=1` (no fog), `AI_CFG="Field=value,..."` / `AI_CFG<army>` / `AI_CFG_<Difficulty>`
  (override `AiConfig` fields for sweeps; bench only, the sim never reads the environment).

## AI and the Cybran faction (from "Cybran faction & stealth/EMP", wave 5l)

**AI.** No AI change: the AI plays Cybran through the roles (AI_TUNING "Cybran vs UEF"). The AI doesn't use
stealth/cloak/EMP deliberately yet (later).
