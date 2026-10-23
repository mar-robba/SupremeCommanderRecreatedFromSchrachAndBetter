# Handoff — agent (k) wave 5: AI uses everything (coordinator 8 CHECKPOINT, 2026-10-23)

Repo /run/media/marco/RTS (old "Volume D" paths are stale). Worktree = this dir, branch
`worktree-agent-ac4c927c54a66ca1d`. Always `bash tools/agents/dn.sh <dotnet args>`. main (with Cybran 79b5e37) merged
at 6503874. Sim tests: 396 green (sub-agent run incl. AiTechTests). Cap 250k (CHECKPOINT 230k).
Navy/transports = separate agent (worktree .claude/worktrees/ai-navy, branch wave5-ai-navy, from 701ea45): it owns
AiNavy*.cs, AiTransport*.cs, AiConfig.Navy.cs (+1 hook in AiEconomy.ManageFactories). Don't edit those; commit often.

## Goals (orchestrator 2026-10-23)
(a) lopsided games end: run.py `lopsided stalled X/Y` -> ~0. (b) 45-min cap: NvN >= 80/96, HvH >= 85 %, Hard win
share vs Normal >= main (177-7-8 at 30). (c) tech beats T1-only (tc.sh) >= 65 % of decided games, both seats.
Then: T1 artillery balance + Hard mix; docs (AI_TUNING, ARCHITECTURE "Skirmish AI", ROADMAP, DEV_GUIDE); lab scripts
(cmp.py, lop.py, tc.sh, cvu.py, sweep.sh with MIN) into tools/ai-lab + AI_TUNING Tools lines; Client/Net tests;
perf check; final merge main; .claude/agent-report.md with "Handoff review".

## Numbers (NvN 45 min, 96 games; all rows in docs/AI_TUNING.md "New goals" table)
- defaults: 65 finished (59 by 30), stalled 24/69. **ArtilleryTier=1: 78 (65), 13/68 = best lever** (no MML/T3 arty).
- LeadArmyShare .8+LeadRatio 1.2: 72 (66), 21/76. ShieldTime off 73/21. ArtilleryShare .15: 69/18. ArtilleryTier=2: 66/27.
- Ruled out (worse/no help): Advance (creeping push) 31-34 stalled; follow mode 24-25; AaGroundFactor .3; AaShare
  .05-.25; StallTime 60 + StallMargin 10; Reinforce=false (40, 60/80!). Earlier: siege push, pressure rule.
- (c) partial (plains only, crashed run): T1-only beat tech 22-6 -> tech loses badly to T1 spam. Suspects: early
  factory upgrades (factory idle while upgrading), enhancements, mex upgrades at 5.5 min. Try on the tech seat only
  (tc.sh 4th arg goes to the tech seat): EnhanceTime=99999, LandT2Time later, ArmyShare .6, ArtilleryTier=1.

## Running at CHECKPOINT (background, may die with me): chain writing .claude/ai-lab/e2.out then tc.out
 e2 (kbin6): "ArtilleryTier=1,ScoutRebuildTime=91" (noise check), "+LeadArmyShare=0.8,LeadRatio=1.2",
 "+ReinforceBatch=3,ReinforceWait=20"; then tc0 (defaults) and tc1 (ArtilleryTier=1) tech-vs-T1, 45 min.
 If incomplete: `MIN=45 bash .claude/ai-lab/sweep.sh e2 .claude/ai-lab/specs/nvn.txt .claude/ai-lab/kbin6/Rts.Bench cfg...`
 and `MIN=45 bash .claude/ai-lab/tc.sh tc0 .claude/ai-lab/specs/nvn.txt .claude/ai-lab/kbin6/Rts.Bench [cfg]`.

## NEXT (in order)
1. Read e2/tc results, record in AI_TUNING. Likely set ArtilleryTier=1 as Normal/Hard default (+LeadArmyShare if it
   adds); then HvN (specs/hvn.txt, DIFF=Hard) and HvH at 45 min; Cybran v UEF: `python3 -I tools/ai-lab/run.py cvu 45 14
   @.claude/ai-lab/specs/cvu.txt --quiet --bin .claude/ai-lab/kbin6/Rts.Bench` then `python3 -I .claude/ai-lab/cvu.py .claude/ai-lab/runs/cvu`.
2. Goal (c) levers on the tech seat. 3. Balance: T1 arty (36 m, 100 dmg/4 s, r30, spread 1.5) too efficient; a
   bench `duel` mode would help (DataPaths finds data/ above the binary: copy a bin + data dir to experiment safely).
4. Re-check AiTechTests (seed-2 timings) after default changes. 5. Docs/scripts/report (see Goals).

## Lab (.claude/ai-lab/, gitignored)
specs/{nvn,hvn,hvh,cvu}.txt; kbin6 = current branch build (rebuild after every merge: old bins CRASH on new data);
basebin = old main; cmp.py; lop.py <run> (lists stalled games); tc.sh; cvu.py; sweep.sh (MIN env). run.py (committed)
prints `by 30 min`, `lopsided stalled`, supports `:factions` spec field. Bench: `lv=` land value, AI_BASE=<s>:<army>
base dump, AI_DUMP=<s>:<army>:<group>, AI_TRACE=1 AI_GROUPS=1.

## Diagnosis (plains 19 / highlands 21)
Leader 5-10x army sits at the enemy base edge for 10+ min: front tanks hold on factories, MML/T3 arty hold at max
range on PD/shields; ~50 % of the army walks in reinforcement batches; defender's fresh units + PD + enhanced ACU
win the attrition; defender eco = upgraded mexes at home (starving impossible). Leader never builds strategic
projects (MassRatio < .05 / EcoRoom gate; 12 land factories instead).

## Pitfalls
- Upgraded/enhanced units change Bp: count by category/tier. Any order cancels a commander enhancement.
- No per-think allocations. zsh: quote globs. Foreground waits >600 s go to background. Never pkill "Rts.Bench ai".
