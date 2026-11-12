#!/usr/bin/env bash
# Merge checks on main: build, three test suites, reference AI match, bench hash, one screenshot.
# usage: bash merge-check.sh <log> [screenshot args...]
cd /run/media/marco/RTS || exit 1
LOG="$1"; shift
: > "$LOG"
echo "== build $(date +%T)" >> "$LOG"
bash tools/agents/dn.sh build Rts.slnx 2>&1 | grep -E "Avvisi|Errori| error " | tail -5 >> "$LOG"
for t in Rts.Sim.Tests Rts.Client.Tests Rts.Net.Tests; do
  echo "== test $t $(date +%T)" >> "$LOG"
  bash tools/agents/dn.sh test tests/$t 2>&1 | grep -E "Non superato|Superato!|Totale|error" | tail -6 >> "$LOG"
done
echo "== ai match $(date +%T)" >> "$LOG"
dotnet run -c Release --project tools/Rts.Bench -- ai 30 256 42 hard,normal 2>&1 | grep -iE "winner|hash" | tail -4 >> "$LOG"
echo "== bench $(date +%T)" >> "$LOG"
dotnet run -c Release --project tools/Rts.Bench -- 2000 300 2>&1 | grep -iE "hash|avg|tick" | tail -4 >> "$LOG"
if [ $# -gt 0 ]; then
  echo "== screenshot $(date +%T)" >> "$LOG"
  dotnet run --project src/Rts.Client -- "$@" 2>&1 | tail -2 >> "$LOG"
fi
echo "== done $(date +%T)" >> "$LOG"
