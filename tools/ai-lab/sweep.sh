#!/bin/bash
# Tries several AiConfig overrides on the same game list (see docs/AI_TUNING.md).
#   SPECS="$(python3 -I tools/ai-lab/specs.py normal,normal 1-23,42)" tools/ai-lab/sweep.sh <tagprefix> "cfg" ...
#   DIFF=Hard SPECS="$(python3 -I tools/ai-lab/specs.py hard,normal 1-24 --both-sides)" tools/ai-lab/sweep.sh H7 - "ArtilleryShare=0.6"
# cfg = an AI_CFG string ("Field=value,Field=value"), "-" = no override (the baseline).
# Without DIFF: the cfg applies to every AI; prints how many games ended and which didn't.
# With DIFF=<Difficulty>: the cfg applies to that difficulty only (AI_CFG_<Difficulty>); prints its head-to-head.
# Env: MIN (game minutes, default 30), PAR (parallel games, default 14).
set -e
PFX=$1; shift
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(git -C "$HERE" rev-parse --show-toplevel)
for c in "$@"; do
  tag="${PFX}_$(echo "$c" | sed 's/[^A-Za-z0-9.]/_/g')"
  cc=$c; [ "$cc" = "-" ] && cc=""
  if [ -n "$DIFF" ]; then
    env "AI_CFG_$DIFF=$cc" "$HERE/matrix.sh" "$tag" "${MIN:-30}" "${PAR:-14}" > /dev/null
    echo "$c => $(python3 -I "$HERE/h2h.py" "$ROOT/.claude/ai-lab/runs/$tag" "$DIFF")"
  else
    r=$(AI_CFG="$cc" "$HERE/matrix.sh" "$tag" "${MIN:-30}" "${PAR:-14}" | grep -E "^finished")
    none=$(grep -L "^WINNER" "$ROOT/.claude/ai-lab/runs/$tag"/*.txt | xargs -r -d '\n' -n1 basename | sed 's/\.txt$//' | paste -sd' ')
    echo "$c => $r | no winner: $none"
  fi
done
