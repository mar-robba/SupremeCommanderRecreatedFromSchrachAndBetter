# Handoff — agent (k) wave 5: AI uses everything

**2026-10-22 (orchestrator): the machine rebooted, so every `/tmp/claude-1000/...scratchpad` path below is GONE**
(specs nvn/hvn/hvh.txt, cmp.py, s2.txt, the main `base` bench build). Run logs in `.claude/ai-lab/runs/` survived.
Regenerate specs with `tools/ai-lab/specs.py` and keep ALL lab files under `.claude/ai-lab/` (survives reboots).
k5 (16:21-16:26) was cut by the session limit after 5 minutes: nothing done since CHECKPOINT 4.

Brief: docs/HISTORY.md "### (k) AI uses everything" + docs/AGENT_RULES.md (cap now 150k, CHECKPOINT ~130k).
Worktree = this dir, branch `worktree-agent-ac4c927c54a66ca1d`; main merged at ce1fd64. Always `bash tools/agents/dn.sh`.
(l) Cybran in parallel: new roles append-only in FactionRoles.All AND Optional (none needed so far); no cybran files.

## k6 CHECKPOINT (2026-10-22, coordinator 3, context cap) — the stall fix, evidence chain
- Lab (all under .claude/ai-lab/, gitignored): specs/{nvn,hvn,hvh}.txt; base/ = main + val= + spend tracker,
  basebin/Rts.Bench (apphost, main baseline); kbin/ = this branch (rebuilt at the last commit, defaults below);
  kbin2/kbin3 = stale experiment builds (ignore). cmp.py (`python3 -I .claude/ai-lab/cmp.py runs/A runs/B [--spent|--mil]
  [--marks 8,12] [--diff Hard]`); `bash .claude/ai-lab/sweep.sh <pfx> <specs> <bin> cfg...` (cfg "-" = none, tag
  becomes <pfx>__; DIFF=Hard env for h2h). One NvN matrix ~2-3 min. Nothing is running now.
- Bench (committed): SpendTracker.cs `spent=army/eng/mex/mexUp/fac/facUp/power/store/def/other/enh` (k mass),
  status `bud=FactorySpend/EcoSpend/FactoryBudget/UpgradeSpend`.
- Code (committed): AiEconomy.Budget.cs (army-first: FactoryBudget floor ArmyShare*budget/4, EcoRoom gates
  upgrades/enhancements/assist-upgrade/projects, first upgrade may overshoot room by 0.25*income), capacity rule in
  WantLandFactory (CapacityTime/Margin), Floating also by stored mass (FloatMass/FloatSeconds), knobs MaxUnitTier,
  ArtilleryTier, Pressure* (OFF), SiegeTime (OFF = 0; AiMilitary.SiegePush + AiGroup.BestDist/ProgressTick/ProgressTarget).
- NvN finished/96 (main b3_nvn 80): old k 48 (k5_) | budget a1__ 57 (ArmyShare .4/.6/.7: 47/53/56) | +capacity+float
  b1__ 58 | MaxUnitTier=1 65, =2 60, +no enh 68 (c1_*) | ArtilleryTier=1 65, =2 59, no shields 58 (c2_*) |
  pressure 0.85 d1__ 40 (worse -> off). All in docs/AI_TUNING.md "Tech AI stalemates".
- Diagnosis: (1) eco/tech took ~60 % of income (fixed partly by the budget); (2) lopsided games don't end: the
  winning army stops 40-70 from the enemy base because long-range units (MML r60, T3 arty r80) always have a target
  (attack-move stops), stuck check needs local<=0 (AiMilitary.UpdateAttackingGroup). Losing tech AIs keep upgrading.
- NEXT (in order): a) sweep kbin "SiegeTime=60" "SiegeTime=30" "SiegeTime=60,ArtilleryTier=1" "ArtilleryTier=1" on
  NvN; b) if siege helps, also make Engage/attack-move closing for indirect units (MML/arty follow tanks), or keep
  ArtilleryTier lower / MML share small; c) no factory upgrades while units are T1-only? (facU 4-9k wasted when
  MaxUnitTier=1); d) T3 dps buff within SupCom numbers (T3 dps/mass ~half of T1); e) then HvN (DIFF=Hard, hvn specs)
  + HvH vs main (base_hvn 177-7-8, base_hvh 63/96) and set Normal/Hard defaults; then navy sub-agent (notes/navy.md).

## CHECKPOINT 4 (coordinator 2, context cap hit) — findings since ab30ce2
- e4e257d: AiConfig is `partial` (navy/transport knobs go in AiConfig.Navy.cs / .Transport.cs); bench status line
  ends with `val=<army>k/<defenses+shields>k/<other structures>k` (mass value of complete units).
- NAVY SUB-AGENT IS RUNNING (started by coordinator 2) in this worktree: files AiNavy*.cs, AiConfig.Navy.cs,
  tests/Rts.Sim.Tests/AiNavyTests.cs; gate `NavyEnabled=false` until its final commit (flips it true); builds its
  bench to .claude/navybin, run tags navy_*; writes .claude/notes/navy.md. Don't edit its files; wait for its note.
- k2 matrix (ab30ce2): NvN 48/96, HvN 76-17-99, HvH 36/96 (= k1). k3_nvn (e4e257d) 48/96. base2_nvn (main+val=) 80/96.
- s1 NvN sweep (AI_CFG): MaxTier=1+Enhance/Shield/Tml/Strategic off -> **86/96**; EnhanceTime=99999 53; Shield+Tml
  off 47; UpgradeShare=0.2 48; later MexT2/LandT2/LandT3 times 43. => cause is T2/T3 itself, not projects/timing.
- s2_1 = AI_CFG MaxTier=2 (done): finished 37/96 (canyon 3/24 highlands 7/24 lakes 13/24 plains 14/24), mean 18.3 min; full output in /tmp/claude-1000/-run-media-marco-Volume-D-RTS/0e69024f-c17b-4125-928c-f021a6055ab1/scratchpad/s2.txt, logs .claude/ai-lab/runs/s2_1.
- Symptoms (k2 vs base, NvN means): land units half (27-35 vs 59) with higher mass income (37-58 vs 28); pgens 42 vs
  15 at min 20 (ProjectedEnergy over-estimates: engineers BR*4, T3 factories x2.2); fac stuck at 7; mass piles up
  2-10k at min 25-30 (Floating needs MassRatio>0.75, broken with big storage); waves similar.
- Lanchester value dps*hp/mass^2 (UEF): T1 tank 1.7, T1 arty 3.9, T2 heavy tank 2.4, T3 assault 1.1, T3 siege 0.7,
  T3 arty 0.2, MML2 0.5 => T3 land units far less cost-efficient than T1/T2 (balance pass, TODO 3).
- Next: compare val= k3_nvn vs base2_nvn at min 8-20 (extend /tmp/claude-1000/-run-media-marco-Volume-D-RTS/0e69024f-c17b-4125-928c-f021a6055ab1/scratchpad/cmp.py to parse val=); then try, one at a time:
  Floating by absolute stored mass (more factories), cap ProjectedEnergy/pgens, prefer T2 heavy tanks over T3 in
  ChooseLand (or buff T3 blueprints), maybe no factory upgrade while few factories. Measure each with sweep.
- Tools: specs copied to /tmp/claude-1000/-run-media-marco-Volume-D-RTS/0e69024f-c17b-4125-928c-f021a6055ab1/scratchpad/{nvn,hvn,hvh}.txt; `bash /tmp/claude-1000/-run-media-marco-Volume-D-RTS/0e69024f-c17b-4125-928c-f021a6055ab1/scratchpad/sweep.sh <prefix> <specs> <diff|-> - "Cfg=.."` (one
  run.py per config, prints finished/wins/tech); main bench with val=: `--bin /tmp/claude-1000/-run-media-marco-Volume-D-RTS/0e69024f-c17b-4125-928c-f021a6055ab1/scratchpad/base/tools/Rts.Bench/bin/Release/net10.0/Rts.Bench`
  (apphost, NOT .dll). Never `pkill -f "Rts.Bench ai"` (kills your own shell).

## State (last commit ab30ce2, compiles; 393 sim tests green at 1f125cb+1)
- AiRoster (roles by tier), AiEconomy.Tech.cs (upgrades, enhancements, tiered engineers/power/defense, projects:
  anti-nuke/TMD/shields/TML/arty/gateway/exp/nuke/fabs, storage+pgen adjacency, assist upgrades, capture),
  AiPlacement.FindAdjacentSite, AiStrategic.cs (TML/nuke fire), AiConfig tech knobs, AiStats tech counters,
  bench line `  <army> tech: tier=.. upgrades=.. enh=.. exp=.. nukes=.. tml=.. navy=.. drops=.. captures=..`.
- AiNavy.cs / AiTransport.cs are still STUBS (hooks wired: TryBuild, ManageFactories(ref float slot, float budget),
  ChooseAir, Think). AiEconomy.IssueBuild/SetTask/GoHome are public for them.

## TODO (in order)
1. Fix the stall (see CHECKPOINT 4 findings above; k1/k2 logs in .claude/ai-lab/runs). Likely levers: lower UpgradeShare / later times, fewer PD/shields,
   FactoryBudget weight for T2/T3 factories, attack strength (RequiredStrength) with tier units, LateGame push.
   Goal: Hard v Normal ≥ main, NvN ≥ 80/96, HvH > 85 %.
2. Navy (AiNavy.cs) via ONE sub-agent; then transports (AiTransport.cs) via one sub-agent. Briefs: hooks above,
   roster fields (NavalFactory[], Frigate1, Sub1, Destroyer2, Cruiser2, Battleship3, Torpedo1/2, Sonar1/2,
   Transport1/2, AirStaging), View (NavalFactories, NavalUnits, OwnNavalStrength, EnemyNavalStrength,
   EnemyNavalFactories, NavalTier), `NavalRules.FindSite(w.Occupancy, w.Terrain, bp, near, maxR, out site)`,
   TransportOrders.TransportTo, AiRole.Naval/Transport, Stats.Naval/Drops, Cfg.MaxNavalFactories/MaxTransports.
3. Balance pass (T1 arty 36m/25dps/r30 too efficient) + re-tune Hard ArtilleryShare; record in AI_TUNING.
4. Tests (tests/Rts.Sim.Tests/AiTechTests.cs): T2 by minute X fixed seed; anti-nuke after seeing an SML (spawn);
   navy on Archipelago; save/load mid-game (~min 12) identical (AiSaveLoadTests pattern). NewAiGame helper: AiTests.cs.
5. Docs (AI_TUNING, ARCHITECTURE Skirmish AI, ROADMAP, DEV_GUIDE run.py), final merge main, report.

## Key commands
- `bash tools/agents/dn.sh build -c Release tools/Rts.Bench`; one game:
  `dotnet tools/Rts.Bench/bin/Release/net10.0/Rts.Bench.dll ai 30 256 42 hard,normal` (main: Hard 11:30, EDCA8A729F29736E;
  k pre-fix: Hard 18:32, 8F710C89B7FBA273).
- Specs (scratchpad c9948f01.../scratchpad/{hvn,nvn,hvh}.txt; regenerate with tools/ai-lab/specs.py, see AI_TUNING).
- `python3 -I tools/ai-lab/run.py <tag> 30 12 @<specs> --quiet [--diff hard]` (~10 min for hvn+nvn+hvh; don't
  rebuild the Release bench while it runs). Baseline main bench: scratchpad base/tools/Rts.Bench/bin/Release/net10.0/.

## Pitfalls
- Upgraded/enhanced units change Bp: count by category/tier. Any order cancels a commander enhancement.
- No per-think allocations (`[..]` collection expressions allocate: use roster arrays).
- zsh: quote globs (`--include='*.cs'`); `tr` misbehaves, use paste. Foreground waits >600 s go to background.
