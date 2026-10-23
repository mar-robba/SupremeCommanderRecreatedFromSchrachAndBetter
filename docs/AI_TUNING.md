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
- 45-minute work (wave 5k) uses `run.py` with fixed game lists in `tools/ai-lab/specs/` (`nvn` 96 Normal mirrors,
  `hvn` Hard v Normal both seats, `hvh`, `cvu` Cybran v UEF both seats; spec field 6 = factions):
  `MIN=45 bash tools/ai-lab/specsweep.sh <prefix> tools/ai-lab/specs/nvn.txt <bin> - "Field=v"` (one status line per
  cfg; `DIFF=Hard` for head-to-heads), `bash tools/ai-lab/tc.sh <prefix> <specs> <bin> [tech cfg]` (goal (c): tech vs
  T1-only, both seats), `python3 -I tools/ai-lab/cvu.py <run dir>` (wins per faction), `cmp.py <run dirs> [--diff D]`
  (mean status numbers per minute mark, one row per run), `lop.py <run dir>` (lists the lopsided-stalled games with
  both sides' state). Run a copy of the binary (+ `data/` next to its parent dirs) so builds don't change it mid-run.
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

## Cybran vs UEF (wave 5l, 2026-10-16)

Both AIs play any faction through the roles (`cybran.json`); no AI code change was needed. 30 min, 256 maps,
Hard vs Hard, seeds 1–3, plains / canyon / lakes, each faction on both sides (18 games):
`Rts.Bench -- ai 30 256 <seed> hard,hard 2 2 <preset> cybran,uef` (and `uef,cybran`).
- First run: Cybran builders (engineers, SCU, ACU) were 0.1 faster than UEF ones → more mex: Cybran 6 / UEF 4 / 3 draws
  (plains + canyon only). Builder speeds set equal to UEF (combat units keep their small Cybran speed edge).
- After: **Cybran 8 / UEF 5 / 5 draws** (the lakes draws are T1 stalemates). Not significant at this size; re-measure
  with `tools/ai-lab` after the "AI uses everything" agent, when the AIs reach T2/T3 (today both field only T1:
  `AI_ROSTER=1` prints the max alive units per blueprint per army; Cybran lean on the Medusa, like UEF on artillery).
- 2026-10-23 recheck before the merge (client-only changes since): same games (seed 1 plains cybran,uef → UEF 18:07;
  seed 2 canyon uef,cybran → Cybran 13:14). New seeds: 4 plains cybran,uef and 5 canyon uef,cybran are draws at
  30:00; seed 4 with a 60 min limit → UEF 37:55.

## Tech AI stalemates (wave 5k, 2026-10-16 → 22)

The tech AI (upgrades, T2/T3 units, enhancements, projects) first ended far fewer games than the T1 AI on main:
NvN 48/96 finished (main 80/96), Hard v Normal 76-17-99 (main 177-7-8), Hard v Hard 36/96 (main 63/96).

### Where the mass goes: `spent=`

The bench status line ends with `spent=army/eng/mex/mexUp/fac/facUp/power/store/def/other/enh`: cumulative mass
(thousands) each army put into construction and upgrades, by what was built (build progress × cost, from
`tools/Rts.Bench/SpendTracker.cs`). `.claude/ai-lab/cmp.py`-style comparisons (mean per minute mark over the alive
armies of a matrix) showed the cause at once. NvN, minute 16, main vs tech AI: army 15.3k vs 9.0k; the tech AI's
extra went to commander enhancements 3.9k, extractor upgrades 2.3k, factory upgrades 1.8k and power +2.1k
(enhancements cost ~20 energy per mass). From minute 16 to 20 the tech AI put ~60 % of its income into eco/tech,
and its armies stayed half the size (27-35 land units vs 59) although its mass income was higher (37-58 vs 28).

### Experiments (NvN finished / 96; noise about ±6)

| Change | Finished |
|---|---|
| main (T1 AI) | 80 |
| tech AI as it was | 48 |
| MaxTier=1 + no enhancements/shields/TML/strategic | 86 |
| MaxTier=2 | 37 |
| EnhanceTime=99999 (no enhancements) | 53 |
| Shields + TML off | 47 |
| UpgradeShare=0.2 / later tier times | 48 / 43 |
| MaxMexUpgrades=1 | 43 |
| PowerMargin=0.5 | 45 |
| SpendRatio=1.5 | 48 |
| army-first budget (`AiEconomy.Budget.cs`, ArmyShare 0.5) | 57 |
| … ArmyShare 0.4 / 0.6 / 0.7 | 47 / 53 / 56 |
| + factory capacity rule, floating by absolute stored mass | 58 (capacity off 57, margin 1.6: 60) |
| + MaxUnitTier=1 (T1 combat units only, tech eco kept) / MaxUnitTier=2 | 65 / 60 |
| + MaxUnitTier=1 and no enhancements | 68 |
| + ArtilleryTier=1 (T1 artillery instead of T2 missile launchers / T3 artillery) / ArtilleryTier=2 | 65 / 59 |
| + ShieldTime=99999 (no base shields) | 58 |
| + "pressure" rule (ArmyShare 0.85 when own land strength < 0.6 × enemy or base threatened) | 40 (worse: off) |
| baseline re-run (2026-10-22, same defaults) | 58 |
| + siege push SiegeTime=60 / 30 / 60 with ArtilleryTier=1 | 55 / 44 / 58 (no help: off) |
| + snipe with indirect-fire members too (SnipeIndirect) / also CommanderSnipe from the start | 57 / 44 |
| + EnhanceTime=99999 (re-run on the budget baseline) | 57 |
| + attrition fall-back (`AiMilitary.FallBack`: no progress for StallTime s while weaker than required → gather again) StallTime=40 / 60 / 90 / 60 with margin 0.7 | 54 / 55 / 53 / 56 (off) |
| + LeadArmyShare 0.8 (army share while leading: own land strength > LeadRatio × enemy) / with LeadRatio 1.2 / 0.95 with 1.2 | 57 / 66 / 63 |

Findings beyond the budget:
- With T1 units only, army spending matches main by minute 20 (21.3k) and land counts are higher, yet only 65-68/96
  end: the tech economy (upgraded extractors near home, high-HP structures) lets the losing side rebuild.
- Many stalled games are lopsided (one side 10-18k army value and 2-4× the income at minute 28). Traced
  (highlands seed 2, `AI_DUMP=1200:1:1`): the winning army sits 40-70 from the enemy base for 10+ minutes. Its long
  range units (T2 missile launchers range 60, T1 artillery 30) keep a target in range (fresh units, engineers), and
  attack-move stops while there is a target, so the army never walks in. The stuck check only fires while nothing
  shoots at the group. Hence ArtilleryTier=1 helping, and the siege push (`AiMilitary.SiegePush`, `SiegeTime`).
- Making the losing side all-in on army (pressure rule) prolongs games.

### New goals (2026-10-23) and the lopsided metric

The 30-minute NvN count was the wrong yardstick (main's 80/96 is T1-only play; mirror games with T2/T3, shields and
point defense naturally last longer, in SupCom too). New goals: (a) lopsided games must end; (b) finished games at a
45-minute cap (NvN >= 80/96, HvH >= 85 %, Hard's win share vs Normal >= main's); (c) the tech AI beats a T1-only AI
(MaxTier=1, MaxUnitTier=1, no enhancements/shields/TML/strategic) in >= 65 % of decided games, both seats.

`tools/ai-lab/run.py` prints `lopsided stalled X/Y`: Y = games where one army's land value (bench `lv=`, mass of
complete mobile land combat units; older logs: `val=` army value) was >= 2x the other's and >= 2k for 5 status
minutes in a row; X = those of them still running 10 minutes after that (games where the cap came first don't
count). Retro on 30-minute runs: main 1/61, tech AI defaults 6/51. `.claude/ai-lab/tc.sh`-style head-to-heads use the
bench's per-army override (`AI_CFG0` / `AI_CFG1`).

| NvN, 45-minute cap | Finished (by 30) | Lopsided stalled |
|---|---|---|
| tech AI defaults (2026-10-23) | 65 (59) | 24/69 |
| + creeping advance (`AiMilitary.Advance`: every AdvanceInterval s a group >= AdvanceMargin × the known enemy at its objective/front moves its stopped members forward) 15 s / margin 1.3 / 10 s step 30 | 68 (49) / 58 (47) / 62 (51) | 31/74 / 33/73 / 34/70 (worse: off) |
| + ArtilleryTier=1 (artillery role = T1 artillery only: no T2 missile launchers, no T3 mobile artillery) | 78 (65) | 13/68 |
| + LeadArmyShare 0.8, LeadRatio 1.2 | 72 (66) | 21/76 |
| + ShieldTime=99999 (no base shields) | 73 (58) | 21/69 |
| + ArtilleryShare 0.15 (Normal default 0.25) | 69 (58) | 18/65 |
| + AaGroundFactor 0.3 (anti-air-only members count 30 % in group strength) | 64 (53) | 27/71 |
| + AaShareMin 0.05, AaShareMax 0.25 (mobile AA share of the mix; default 0.12-0.4) | 64 (54) | 22/68 |
| + StallTime=60, StallMargin=10 (fall back and gather whenever an attack stops closing in for 60 s) | 63 (55) | 25/71 |
| + Reinforce=false (fresh units wait for the next wave) | 40 (27) | 60/80 (much worse) |
| + follow mode (Advance with AdvanceStep 0: only indirect-fire members stopped far behind the front move up to it) margin 0 / 1.5 | 69 (58) / 66 (55) | 24/68 / 25/72 (no help) |
| + ArtilleryTier=2 (T2 missile launchers, no T3 mobile artillery) | 66 (60) | 27/71 |
| ArtilleryTier=1 again (noise check, with a no-op override) | 80 (60) | 14/73 |
| ArtilleryTier=1 + LeadArmyShare 0.8, LeadRatio 1.2 | 80 (65) | 22/77 (no gain on stalls) |

## Unit balance

Tool: `Rts.Bench -- duel <unitA> <unitB> [mass] [seed] [mapSize]` and `duelmatrix <factionA> [factionB] [masses] [seeds]`
(`tools/Rts.Bench/Duel.cs`, ~5 s per matrix on 16 threads, deterministic). Method: equal mass of each unit type
(count = round(mass / mass cost), at least 1) in rows on flat ground, front rows just out of the longer weapon range,
both groups attack-move at each other in the real sim (accuracy, area damage, turning, veterancy; no fog, no AI);
idle units re-attack-move to the enemy centroid every 5 s. **shareA** = A's share of the surviving value
(mass x hp/maxhp): 1.00 = A wins at full hp, 0.00 = B wins. **margin** = A's surviving fraction minus B's (-1..+1;
fair when the counts round unevenly). Each cell = 3 seeds x both sides (A west and A east). `*` = a side's real mass is
>20% off the budget (one UEF T3 siege bot = 1280 mass at budget 600 or 2000). Mobile AA (no ground weapon) is skipped.

### BEFORE (2026-10-23, blueprints at fd3eaca)

Role average per mass: mean shareA of the row unit vs every unit of the other side (margin in brackets).

| uef vs uef | tank1 | art1 | hvy2 | amph2 | mml2 | asl3 | sieg3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.33 (-0.26) | 0.75 (+0.26) | 0.63 (+0.21) | 0.48 (-0.11) | 0.08 (-0.37) | 0.31 (-0.28) | 0.94\* (+0.53) | 0.48 (+0.01) |
| 2000 | 0.33 (-0.22) | 0.94 (+0.42) | 0.71 (+0.21) | 0.21 (-0.26) | 0.25 (-0.15) | 0.31 (-0.22) | 0.67\* (+0.01) | 0.58 (+0.21) |
| 6000 | 0.29 (-0.24) | 0.94 (+0.44) | 0.71 (+0.23) | 0.48 (-0.12) | 0.54 (+0.10) | 0.21 (-0.28) | 0.10 (-0.40) | 0.73 (+0.28) |

| cybran vs cybran | tank1 | lgt1 | art1 | hvy2 | amph2 | mml2 | asl3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.25 (-0.22) | 0.42 (-0.10) | 0.71 (+0.26) | 0.94 (+0.45) | 0.65 (+0.06) | 0.46 (-0.05) | 0.19 (-0.31) | 0.40 (-0.10) |
| 2000 | 0.38 (-0.20) | 0.60 (+0.02) | 0.94 (+0.43) | 0.79 (+0.32) | 0.27 (-0.25) | 0.31 (-0.08) | 0.29 (-0.23) | 0.42 (-0.01) |
| 6000 | 0.33 (-0.22) | 0.21 (-0.27) | 0.94 (+0.45) | 0.77 (+0.26) | 0.35 (-0.23) | 0.48 (+0.07) | 0.46 (-0.16) | 0.46 (+0.09) |

| uef unit vs all cybran | tank1 | art1 | hvy2 | amph2 | mml2 | asl3 | sieg3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.25 (-0.25) | 0.77 (+0.29) | 0.79 (+0.29) | 0.52 (-0.06) | 0.10 (-0.32) | 0.31 (-0.28) | 1.00\* (+0.58) | 0.48 (+0.03) |
| 2000 | 0.35 (-0.24) | 0.96 (+0.41) | 0.63 (+0.19) | 0.19 (-0.30) | 0.48 (-0.01) | 0.35 (-0.18) | 0.71\* (+0.02) | 0.58 (+0.13) |
| 6000 | 0.21 (-0.27) | 0.96 (+0.45) | 0.67 (+0.20) | 0.33 (-0.17) | 0.42 (+0.08) | 0.13 (-0.31) | 0.02 (-0.45) | 0.54 (+0.19) |

Full shareA grids at mass 2000 (row = unit A, column = unit B; `B avg` = the column unit's mean share vs all rows):

```
uef vs uef
  shareA tank1   art1   hvy2  amph2   mml2   asl3  sieg3   art3   avg
  tank1   0.50   0.00   0.00   1.00   0.50   0.67   0.00*  0.00   0.33
  art1    1.00   0.50   1.00   1.00   1.00   1.00   1.00*  1.00   0.94
  hvy2    1.00   0.00   0.50   1.00   1.00   1.00   1.00*  0.17   0.71
  amph2   0.00   0.00   0.00   0.50   0.50   0.00   0.17*  0.50   0.21
  mml2    0.50   0.00   0.00   0.50   0.50   0.50   0.00*  0.00   0.25
  asl3    0.33   0.00   0.00   1.00   0.50   0.50   0.00*  0.17   0.31
  sieg3   1.00*  0.00*  0.00*  0.83*  1.00*  1.00*  0.50*  1.00*  0.67
  art3    1.00   0.00   0.83   0.50   1.00   0.83   0.00*  0.50   0.58
  B avg   0.33   0.94   0.71   0.21   0.25   0.31   0.67   0.58
cybran vs cybran
  shareA tank1   lgt1   art1   hvy2  amph2   mml2   asl3   art3   avg
  tank1   0.50   0.00   0.00   0.00   0.67   0.17   1.00   0.67   0.38
  lgt1    1.00   0.50   0.00   0.00   1.00   0.67   0.67   1.00   0.60
  art1    1.00   1.00   0.50   1.00   1.00   1.00   1.00   1.00   0.94
  hvy2    1.00   1.00   0.00   0.50   1.00   1.00   1.00   0.83   0.79
  amph2   0.33   0.00   0.00   0.00   0.50   0.50   0.83   0.00   0.27
  mml2    0.83   0.33   0.00   0.00   0.50   0.50   0.33   0.00   0.31
  asl3    0.00   0.33   0.00   0.00   0.17   0.67   0.50   0.67   0.29
  art3    0.33   0.00   0.00   0.17   1.00   1.00   0.33   0.50   0.42
  B avg   0.38   0.60   0.94   0.79   0.27   0.31   0.29   0.42
uef (rows) vs cybran (columns)
  shareA tank1   lgt1   art1   hvy2  amph2   mml2   asl3   art3   avg
  tank1   0.33   0.33   0.00   0.00   1.00   0.00   1.00   0.17   0.35
  art1    1.00   1.00   0.67   1.00   1.00   1.00   1.00   1.00   0.96
  hvy2    1.00   1.00   0.00   0.00   1.00   1.00   1.00   0.00   0.63
  amph2   0.00   0.00   0.00   0.00   0.00   0.33   0.50   0.67   0.19
  mml2    1.00   0.33   0.00   0.00   0.83   0.83   0.67   0.17   0.48
  asl3    0.00   0.83   0.00   0.00   0.00   0.67   0.50   0.83   0.35
  sieg3   1.00*  0.67*  0.00*  0.00*  1.00*  1.00*  1.00*  1.00*  0.71
  art3    0.33   0.00   0.17   0.67   1.00   1.00   0.83   0.67   0.58
  B avg   0.42   0.48   0.90   0.79   0.27   0.27   0.19   0.44
```

Findings: (1) **T1 artillery beats every land unit at equal mass** from 2000 up (row 0.94-0.96, keeps ~40% of its mass
more than the enemy); hp x dps / mass^2 is 3.9 (UEF) / 3.3 (Cybran) against 1.7 for the T1 tanks, and it wins 1.00 vs T1
tanks at every mass. (2) **T1 tanks are the weakest per mass** (0.21-0.38). (3) **T2 heavy tanks are strong** (0.63-0.94),
the T2 amphibious tanks weak from 2000 up (0.19-0.48). (4) **T3 assault bots lose per mass** to T1/T2 (0.13-0.46; UEF asl3 vs tank1
0.33, Cybran 0.00 at 2000), and the UEF siege bot falls to 0.10 at 6000 (5 units vs swarms); only T3 mobile artillery
holds up at big mass (0.73 UEF at 6000).
