#!/bin/bash
# Runs many headless AI-vs-AI games in parallel and summarizes them (see docs/AI_TUNING.md).
#   SPECS="$(python3 -I tools/ai-lab/specs.py normal,normal 1-23,42)" tools/ai-lab/matrix.sh <tag> <minutes> [parallel]
# Each game: Rts.Bench ai <minutes> <map> <seed> <difficulties> 1 <players> <preset> (1 sim thread per game).
# Logs: <worktree>/.claude/ai-lab/runs/<tag>/<game>.txt (gitignored). AI_CFG / AI_CFG_<Difficulty> pass through.
# BIN = the bench binary; default: this worktree's Release build (dotnet build -c Release tools/Rts.Bench first).
set -e
TAG=$1; MIN=$2; PAR=${3:-12}
if [ -z "$TAG" ] || [ -z "$MIN" ] || [ -z "$SPECS" ]; then
  echo "usage: SPECS=\"preset:seed:diffs:players:map ...\" $0 <tag> <minutes> [parallel]" >&2; exit 2
fi
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(git -C "$HERE" rev-parse --show-toplevel)
export BIN=${BIN:-"$ROOT/tools/Rts.Bench/bin/Release/net10.0/Rts.Bench"}
export OUT="$ROOT/.claude/ai-lab/runs/$TAG" MIN
mkdir -p "$OUT"
run1() {
  IFS=: read -r preset seed diffs players map <<< "$1"
  name="${preset}_${seed}_${diffs//,/v}_${players}p_${map}"
  timeout 2400 "$BIN" ai "$MIN" "$map" "$seed" "$diffs" 1 "$players" "$preset" > "$OUT/$name.txt" 2>&1 || true
}
export -f run1
printf "%s\n" $SPECS | xargs -P "$PAR" -I{} bash -c 'run1 "$@"' _ {}
python3 -I "$HERE/summ.py" "$OUT"
