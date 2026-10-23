#!/bin/bash
# Config sweep over a fixed game-list file with run.py (status lines: finished, by 30 min, lopsided stalled).
#   MIN=45 bash tools/ai-lab/specsweep.sh <prefix> tools/ai-lab/specs/nvn.txt <Rts.Bench binary> "-" "Field=v,Field=v" ...
# cfg "-" = no override. DIFF=Hard -> the cfg goes to AI_CFG_Hard only and the head-to-head line is printed too.
# Env: MIN (game minutes, default 30), PAR (parallel games, default 14). Copy the binary + data/ elsewhere first
# if you keep building (DataPaths finds data/ above the binary).
ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel); cd "$ROOT"
P=$1; S=$2; BIN=$3; shift 3
for c in "$@"; do
  tag="${P}_$(echo "$c" | sed 's/[^A-Za-z0-9.]/_/g')"; cc=$c; [ "$cc" = "-" ] && cc=""
  [ ${#tag} -gt 100 ] && tag="${P}_$(echo -n "$c" | md5sum | cut -c1-10)" && echo "$tag = $c"   # long cfgs: hashed dir name
  if [ -n "$DIFF" ]; then
    env "AI_CFG_$DIFF=$cc" python3 -I tools/ai-lab/run.py $tag ${MIN:-30} ${PAR:-14} @$S --quiet --bin $BIN --diff $DIFF | head -2 | paste -sd' ' | sed "s|^|$tag: |"
  else
    AI_CFG="$cc" python3 -I tools/ai-lab/run.py $tag ${MIN:-30} ${PAR:-14} @$S --quiet --bin $BIN | head -1 | sed "s|^|$tag: |"
  fi
done
