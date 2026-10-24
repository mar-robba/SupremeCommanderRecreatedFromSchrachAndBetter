#!/bin/bash
# Goal (c) of AI_TUNING: the tech AI vs a T1-only AI, both seats (per-army overrides AI_CFG0 / AI_CFG1).
#   MIN=45 bash tools/ai-lab/tc.sh <prefix> tools/ai-lab/specs/nvn.txt <Rts.Bench binary> [cfg for the tech side]
# Prints: tech won X, T1-only won Y, tech share of decided games.
ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel); cd "$ROOT"
P=$1; S=$2; BIN=$3; C=${4:-}
T1="MaxTier=1,MaxUnitTier=1,EnhanceTime=99999,ShieldTime=99999,TmlTime=99999,StrategicTime=99999"
R=.claude/ai-lab/runs
AI_CFG0="$C" AI_CFG1="$T1" python3 -I tools/ai-lab/run.py ${P}_t1seat1 ${MIN:-45} ${PAR:-14} @$S --quiet --bin $BIN > /dev/null
AI_CFG1="$C" AI_CFG0="$T1" python3 -I tools/ai-lab/run.py ${P}_t1seat0 ${MIN:-45} ${PAR:-14} @$S --quiet --bin $BIN > /dev/null
tw=$(( $(grep -l "^WINNER: Player" $R/${P}_t1seat1/*.txt | wc -l) + $(grep -l "^WINNER: AI 1" $R/${P}_t1seat0/*.txt | wc -l) ))
lw=$(( $(grep -l "^WINNER: AI 1" $R/${P}_t1seat1/*.txt | wc -l) + $(grep -l "^WINNER: Player" $R/${P}_t1seat0/*.txt | wc -l) ))
echo "$P [$C]: tech won $tw, T1-only won $lw, tech share $(( 100 * tw / (tw + lw > 0 ? tw + lw : 1) ))% of decided"
