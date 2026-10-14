# Skirmish AI tuning: method and results

How the AI is measured, and every tuning result so far. How the AI works is in ARCHITECTURE "Skirmish AI". Read this
before changing the AI or unit balance, so you don't repeat experiments that already failed.

## Method: many games, never one

One AI-vs-AI game says almost nothing: the same change can win or lose a single match by chance. The noise
between two runs of 96 games is still about ±6 wins. So:
- judge a change on **at least 96 games**, and on 192 for Hard-vs-Normal head-to-heads;
- play every head-to-head **from both sides** (difficulties swapped), so the start position doesn't bias it;
- keep the game list fixed while comparing configs, and change one thing at a time;
- record a baseline on main before your change (results change at every merge).

## Tools (`tools/ai-lab/`)

Build the bench first: `dotnet build -c Release tools/Rts.Bench`. Then, from the worktree:

```bash
# game list: preset:seed:difficulties:players:mapSize, separated by spaces
SPECS="$(python3 -I tools/ai-lab/specs.py normal,normal 1-23,42)"
SPECS="$SPECS" tools/ai-lab/matrix.sh base 30 14         # 96 games x 30 min, 14 in parallel: ~60 s wall
python3 -I tools/ai-lab/stats.py .claude/ai-lab/runs/base # economy/army averages per difficulty at minute marks
# config sweep: "-" = baseline, each other argument = one AI_CFG override string
SPECS="$SPECS" tools/ai-lab/sweep.sh S1 - "MaxRaids=2" "FirstWave=14"
# head-to-head sweep for one difficulty (AI_CFG_Hard), 192 games each
DIFF=Hard SPECS="$(python3 -I tools/ai-lab/specs.py hard,normal 1-24 --both-sides)" \
  tools/ai-lab/sweep.sh H7 - "ArtilleryShare=0.6"
```

- `matrix.sh` writes one log per game to `.claude/ai-lab/runs/<tag>/` and prints the winner and end time of each
  game, plus "finished X/Y" (how many games ended by a commander kill).
- `h2h.py <run dir> <Difficulty>`: wins / losses / no winner for that difficulty, mean end time, and its mass income
  vs the opponent at minutes 3/5/8.
- `stats.py <run dir> [marks]`: mean mex, land units, engineers, factories, losses and mass income per difficulty.
- Debugging one game: `AI_TRACE=1 AI_GROUPS=1 AI_STATUS_SECONDS=30 tools/Rts.Bench/bin/Release/net10.0/Rts.Bench ai
  30 256 7 normal,normal 1 2 highlands`. All switches: ARCHITECTURE "Skirmish AI".

### Standard matrices

| Name | Games | Command |
|---|---|---|
| NvN | 96 (4 land presets × 24 seeds) | `specs.py normal,normal 1-23,42` |
| Hard v Normal | 192 (4 presets × 24 seeds × both sides) | `specs.py hard,normal 1-24 --both-sides` |
| Easy v Normal | 48 | `specs.py easy,normal 3,5,7,9,11,42 --both-sides` |
| FFA | 3 (4 players, 512 map, give them 60 min) | `specs.py normal,normal,normal,normal 42,7,3 --presets plains --players 4 --map 512` |
| Quick check | 8 | `specs.py normal,normal 7,42` |

The land presets are plains, lakes, highlands and canyon. Archipelago needs a navy, which the AI doesn't build yet.
Check it separately for crashes and command spam.

### Reading the status lines

One line per game minute, one block per army:
`n=` units, `mex=`, `pg=` power generators, `fac=` factories, `def=` defenses, `eng=` engineers, `land=`/`air=`
units, `M income/demand/stored`, `E income/demand/stored`, `eff mass/energy` efficiency, `lost=` units lost,
`acu=` commander health, `pool=` idle army units, `defend=`, `attack=units/groups`, `waves=` attacks launched.
At the end: `WINNER: <army> (<difficulty>) after mm:ss`, or `NO WINNER`, then build/loss/mass statistics, the AI
cost per tick and the state hash.

## Results so far (wave 3d/4d, 2026-10-12 → 14)

### Key insight

Games end **only** by commander death. Commanders stay at home, protected by point defenses and fresh units. Field
trades between equal AIs are even (about 600 field deaths each). To finish games, the AI must go for the
commander: a late-game push toward the commander's last known position, and snipes.

### What worked

| Change | Result |
|---|---|
| Staging along the walking route, reinforcements in batches, regroup, recall when the base is losing, late-game push + snipes | NvN games ended by commander kill: 83/96 (86%), mean 18.6 min |
| Hard opening: `MexChain 1` (was 2), `EarlyEngineers 5`, `RetreatFraction 0` | Hard v Normal 53-29-14 / 96 (old Hard: 24-14-10 / 48) |
| + Hard focus fire (`FocusFire`, `FocusHorizon` 3 s) | 63-24-9 / 96; on 192 games: 110-59-23 |
| `ArtilleryShare` 0.4 for Hard (default 0.25) | 150-27-15 / 192 |
| **`ArtilleryShare` 0.55 for Hard** (adopted) | **166-8-18 / 192**: by far the biggest lever |

### What did NOT help (don't retry without a new reason)

- After defending, sending the army back to its saved attack objective (the "sortie" change). Results got worse:
  piecemeal sortie pressure was what ended the Highlands/Canyon games. Reverted.
- For Hard: `FirstWave 14`, `FirstAttackTime 270`, `EnemyArmyShare 0.7`, no raids, `MaxRaids 2`,
  `PullBackHealth 0.35/0.45`, `MaxEngineers 10`.
- `FocusHorizon` 1.5 or 6 instead of 3: inside the noise.
- `ArtilleryShare 0.15` for Hard: 69-105, much worse.

### Final numbers at merge (30 min, 256 maps)

- NvN: 83/96 games end by commander kill (86%). Highlands seed 7 stalemates.
- Hard v Normal: 166 won, 8 lost, 18 no winner (of 192).
- Easy v Normal: Easy lost 23 of 24.
- Hard v Hard: only ~69% of games end.
- FFA 4 players on 512: 3/3 ended (22:55, 27:28, 29:43).
- AI cost: 0.02–0.03 ms/tick per AI (worst 0.058).

### Open problems

- Some Highlands/Canyon NvN games stalemate: equal armies trade forever.
- T1 mobile artillery looks too cost-efficient. Hard's edge relies on it, so a balance pass will move these numbers.
  Re-tune after it.
- The AI doesn't use T2/T3 upgrades, navy, strategic weapons, experimentals, transports, capture, pause or
  adjacency yet (wave 5, agent "AI uses everything").
