# Handoff — agent (k) wave 5: AI uses everything (coordinator 8, started 2026-10-23)

Repo moved to /run/media/marco/RTS (old "Volume D" paths are stale). Worktree = this dir, branch
`worktree-agent-ac4c927c54a66ca1d`. Always `bash tools/agents/dn.sh <dotnet args>`. main merged at 0427eb6;
393 Sim tests green there. Cap 250k (CHECKPOINT 230k).

## Goals (orchestrator 2026-10-23, replaces "NvN 80/96 at 30 min")
(a) lopsided games must end: metric `lopsided stalled X/Y` (run.py summary; lv=/val= >= 2x and >= 2k for 5 status
    minutes, game not over 10 min later) -> ~0. (b) 45-min cap: NvN >= 80/96, HvH >= 85 %, Hard win share vs Normal
    >= main (177-7-8 at 30). (c) tech beats T1-only (MaxUnitTier=1, projects/enh off) >= 65 % of decided games, both
    seats (per-army override exists: env AI_CFG0 / AI_CFG1 in the bench).
Then: T1 artillery balance + Hard mix; AiTechTests; docs; tools/ai-lab scripts; final merge; report.
Navy/transports: a SEPARATE agent (orchestrator starts it later) owns AiNavy.cs, AiTransport.cs, AiConfig.Navy.cs:
don't edit those, keep .claude/notes/navy.md. (l) Cybran merges into main first: then merge main, run Cybran v UEF.

## Lab (.claude/ai-lab/, gitignored)
- specs/{nvn,hvn,hvh}.txt; basebin/ = main bench; kbin/ = this branch @0427eb6 + lv= (defaults);
  kbin2/ = + Advance (knobs default off). cmp.py (`--spent|--mil --marks --diff`), sweep.sh
  (`MIN=45 bash .claude/ai-lab/sweep.sh <pfx> <specs> <bin> cfg...`, cfg "-" = none, DIFF=Hard for h2h).
- run.py (committed, tools/ai-lab) now prints `by 30 min N` (when > 30 min runs) and `lopsided stalled X/Y`.
  Retro on 30-min runs: main b3_nvn 80/96 stalled 1/61; k f1__ (defaults) 58/96 stalled 6/51; i1 LeadArmyShare
  0.8+LeadRatio 1.2 66/96 stalled 9/63 (not yet in AI_TUNING).
- Trace: `AI_TRACE=1 AI_GROUPS=1 .claude/ai-lab/kbin/Rts.Bench ai 30 256 19 normal,normal 1 2 plains`;
  `AI_DUMP=<sec>:<army>:<group>` dumps members (orders, holding, weapon targets).

## Diagnosis (plains 19, k defaults): army g1 10x stronger, 10 min at the base edge
Front tanks hold (attack-move) on factories ~20 away; MMLs hold 60 away on a PD (only 9 MML kills by min 23);
reinforcement batches stream in; defender's MMLs (168 kills), PD (111), ACU (72) win the attrition; the group
centroid drifts back (183,200)->(142,153) as long-range members stop at max range. 650 attacker deaths in the base.

## Experiments this session
- Advance (AiMilitary.Advance.cs, AdvanceInterval/Margin/Step, AiGroup.AdvanceTick): dominant army steps the
  front forward and pulls stopped rear members up. Measuring (n45 baseline running).

## Pitfalls
- Upgraded/enhanced units change Bp: count by category/tier. Any order cancels a commander enhancement.
- No per-think allocations. zsh: quote globs. Foreground waits >600 s go to background. Never pkill "Rts.Bench ai".
