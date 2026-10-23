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

#### After the unit balance pass (7650459, "Unit balance" below)

| 45-minute cap, post-balance | Result |
|---|---|
| NvN defaults | 66 (52) finished, stalled 31/79 |
| NvN ArtilleryTier=1 | 69 (59), stalled 29/85 (the pre-balance 78/13 gain is gone: T1 artillery no longer wins fights) |
| (c) tech vs T1-only, defaults | tech 92 - 88 T1-only (51 %; pre-balance plains 6-22). Plains 21-27, lakes 23-22, highlands 19-24, canyon 29-15 |
| (c) ArtilleryTier=1 on the tech seat | 96 - 85 (53 %) |

When the games end decides it: the T1-only AI wins early (81 of its 88 wins before minute 25, 25 before minute 15), the
tech AI wins late (58 of 92 after minute 25). The tech AI is weakest between ~5 and ~20 minutes, while extractor and
factory upgrades and enhancements take mass from the army.

Levers for the tech seat of (c) (192 games each, tech share of decided games): PressureArmyShare 0.85 + PressureRatio
0.8 63 %; MexT2Time 480 + LandT2Time 660 53 %; EnhanceTime 900 58 %; ArmyShare 0.6 55 %; UpgradeShare 0.2 48 %;
**all of the first three together 74 % (66-23 and 69-24 by seat; plains 25-21, lakes 36-11, highlands 33-10,
canyon 41-5)** -> made the Normal defaults (`AiConfig`: MexT2Time 480, LandT2Time 660, EnhanceTime 900,
PressureArmyShare 0.85, PressureRatio 0.8). NvN 45 with them: 70 (49) finished, stalled 25/77 (without the pressure
switch 63/30; + ArtilleryTier=1 67/31, so ArtilleryTier stays 3).

Hard v Normal (hvn, 192 games, 45 min; wins Hard-Normal) with those Normal defaults: the old Hard preset lost 18-81.
ArtilleryShare 0.25 49-75, 0.4 44-77; + Normal's tech times 60-71; + Normal's upgrade/project values 60-69; + ArtilleryShare
0.1 69-75 (= "C"). On top of C: raids off 58-88 (raids help), PullBackHealth 0 67-77, **FocusFire off 90-48**, Normal
attack timing (FirstAttackTime 240, AttackMargin 1.3, AssembleTimeout 150) 79-67; FocusFire + pull-back off + Normal
attack timing + Normal engineers (10/4), 1 radar, no air scout, 3 expansion PDs: **92-55, stalled 42/129** -> the new
Hard preset. At minute 8 the old Hard had 40 land units vs Normal's 53 at the same income (mass demand 41 vs 30: too
many commitments) and lost extractors. Focus fire hurts since the balance pass (not investigated: overkill or units
stopping to re-target?). Main's 177-7-8 (30 min) was T1-only play with the old T1 artillery; not reached.

Final check with the new Normal defaults and Hard preset (a37b799, 45 min): NvN 70 (49), stalled 25/77; HvN 92-55
(147/192 finished, stalled 42/129); HvH 68/96 (57 by 30), stalled 29/64; Cybran v UEF (cvu, 96) **Cybran 51 - UEF 21,
24 draws** (pre-balance 8-5 in 18: the balance pass broke faction parity, to fix); (c) tech vs T1-only 112-61 = 64 %
(the T1-only seat now also has the pressure switch). Reference match `ai 30 256 42 hard,normal`: Hard at 15:58, hash
74E2EB7D333190A4.

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

### AFTER (2026-10-23, balance pass)

Same tool, masses and seeds (blueprints of this pass; unit table: `duelmatrix uef cybran`).

| uef vs uef | tank1 | art1 | hvy2 | amph2 | mml2 | asl3 | sieg3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.50 (-0.02) | 0.23 (-0.40) | 0.69 (+0.21) | 0.81 (+0.28) | 0.10 (-0.41) | 0.44\* (-0.07) | 0.94\* (+0.64) | 0.29 (-0.23) |
| 2000 | 0.40 (-0.13) | 0.40 (-0.18) | 0.67 (+0.13) | 0.58 (+0.12) | 0.10 (-0.36) | 0.94 (+0.38) | 0.65 (+0.15) | 0.27 (-0.12) |
| 6000 | 0.42 (-0.20) | 0.31 (-0.19) | 0.73 (+0.14) | 0.75 (+0.20) | 0.06 (-0.28) | 0.71 (+0.18) | 0.73 (+0.17) | 0.29 (-0.01) |

| cybran vs cybran | tank1 | lgt1 | art1 | hvy2 | amph2 | mml2 | asl3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.50 (+0.00) | 0.40 (-0.18) | 0.15 (-0.50) | 0.77 (+0.31) | 0.73 (+0.28) | 0.29 (-0.20) | 0.94\* (+0.57) | 0.23 (-0.29) |
| 2000 | 0.50 (+0.03) | 0.44 (-0.05) | 0.31 (-0.37) | 0.88 (+0.28) | 0.65 (+0.20) | 0.08 (-0.28) | 0.85 (+0.37) | 0.29 (-0.18) |
| 6000 | 0.54 (-0.03) | 0.38 (-0.21) | 0.31 (-0.21) | 0.81 (+0.25) | 0.65 (+0.20) | 0.13 (-0.23) | 0.94 (+0.34) | 0.25 (-0.11) |

| uef unit vs all cybran | tank1 | art1 | hvy2 | amph2 | mml2 | asl3 | sieg3 | art3 |
|---|---|---|---|---|---|---|---|---|
| 600 | 0.44 (-0.06) | 0.27 (-0.41) | 0.63 (+0.20) | 0.69 (+0.26) | 0.13 (-0.43) | 0.50\* (-0.09) | 1.00\* (+0.67) | 0.23 (-0.28) |
| 2000 | 0.50 (-0.02) | 0.29 (-0.30) | 0.77 (+0.24) | 0.63 (+0.20) | 0.13 (-0.29) | 1.00 (+0.47) | 0.88 (+0.29) | 0.27 (-0.15) |
| 6000 | 0.48 (-0.07) | 0.29 (-0.17) | 0.77 (+0.25) | 0.85 (+0.32) | 0.10 (-0.21) | 0.77 (+0.28) | 0.85 (+0.28) | 0.33 (-0.09) |

Full shareA grids at mass 2000:

```
uef vs uef
  shareA tank1   art1   hvy2  amph2   mml2   asl3  sieg3   art3   avg
  tank1   0.50   1.00   0.00   0.00   1.00   0.00   0.00   0.67   0.40
  art1    0.00   0.50   0.00   0.00   1.00   0.00   0.67   1.00   0.40
  hvy2    1.00   1.00   0.50   1.00   1.00   0.00   0.33   0.50   0.67
  amph2   1.00   1.00   0.00   0.50   1.00   0.00   0.33   0.83   0.58
  mml2    0.00   0.00   0.00   0.00   0.50   0.00   0.00   0.33   0.10
  asl3    1.00   1.00   1.00   1.00   1.00   0.50   1.00   1.00   0.94
  sieg3   1.00   0.33   0.67   0.67   1.00   0.00   0.50   1.00   0.65
  art3    0.33   0.00   0.50   0.17   0.67   0.00   0.00   0.50   0.27
  B avg   0.40   0.40   0.67   0.58   0.10   0.94   0.65   0.27
cybran vs cybran
  shareA tank1   lgt1   art1   hvy2  amph2   mml2   asl3   art3   avg
  tank1   0.50   1.00   1.00   0.00   0.00   1.00   0.00   0.50   0.50
  lgt1    0.00   0.50   1.00   0.00   0.00   1.00   0.00   1.00   0.44
  art1    0.00   0.00   0.50   0.00   0.00   1.00   0.00   1.00   0.31
  hvy2    1.00   1.00   1.00   0.50   1.00   1.00   0.67   0.83   0.88
  amph2   1.00   1.00   1.00   0.00   0.50   1.00   0.00   0.67   0.65
  mml2    0.00   0.00   0.00   0.00   0.00   0.50   0.00   0.17   0.08
  asl3    1.00   1.00   1.00   0.33   1.00   1.00   0.50   1.00   0.85
  art3    0.50   0.00   0.00   0.17   0.33   0.83   0.00   0.50   0.29
  B avg   0.50   0.44   0.31   0.88   0.65   0.08   0.85   0.29
uef (rows) vs cybran (columns)
  shareA tank1   lgt1   art1   hvy2  amph2   mml2   asl3   art3   avg
  tank1   0.33   1.00   1.00   0.00   0.00   1.00   0.00   0.67   0.50
  art1    0.00   0.00   0.33   0.00   0.00   1.00   0.00   1.00   0.29
  hvy2    1.00   1.00   1.00   0.33   0.83   1.00   0.67   0.33   0.77
  amph2   1.00   1.00   1.00   0.00   0.00   1.00   0.00   1.00   0.63
  mml2    0.00   0.00   0.00   0.00   0.00   0.67   0.00   0.33   0.13
  asl3    1.00   1.00   1.00   1.00   1.00   1.00   1.00   1.00   1.00
  sieg3   1.00   1.00   0.67   1.00   0.67   1.00   0.67   1.00   0.88
  art3    0.33   0.00   0.00   0.33   0.17   0.83   0.00   0.50   0.27
  B avg   0.42   0.38   0.38   0.67   0.67   0.06   0.71   0.27
```

Blueprint changes (energy cost and build time scale with mass, so mass / build time stays as before: 0.20, UEF amph2
0.18, siege bot 0.256; with factory BuildRate 20/40/90 a T1/T2/T3 factory turns out about 4/8/18 mass per second):

| unit | changes (old -> new) | eff (hp x dps / mass^2) |
|---|---|---|
| uef_t1_artillery | mass 36 -> 40 (energy 180 -> 200, build time 180 -> 200), MaxHealth 200 -> 165, MuzzleVelocity 20 -> 45, Spread 1.5 -> 3 | 3.86 -> 2.58 |
| cybran_t1_artillery | mass 36 -> 40 (200 / 200), MaxHealth 190 -> 150, MuzzleVelocity 20 -> 45, Spread 1.5 -> 3 | 3.30 -> 2.11 |
| cybran_t1_light_bot | MaxHealth 140 -> 120, Damage 7 -> 6 (dps 10.5 -> 9) | 2.17 -> 1.60 |
| uef_t2_heavy_tank | MaxHealth 1550 -> 1250 | 2.37 -> 1.91 |
| cybran_t2_heavy_tank | MaxHealth 1200 -> 880 | 2.49 -> 1.83 |
| uef_t2_amphibious_tank | MaxHealth 900 -> 1100, Damage 8 -> 9 (dps 40 -> 45) | 1.41 -> 1.93 |
| cybran_t2_amphibious_tank | MaxHealth 1000 -> 1150, Damage 40 -> 44 | 1.47 -> 1.86 |
| uef_t2_mobile_missile | MaxHealth 500 -> 450, DamageRadius 1.5 -> 1 | 0.51 -> 0.46 |
| cybran_t2_mobile_missile | MaxHealth 450 -> 400, DamageRadius 1.5 -> 1 | 0.47 -> 0.42 |
| uef_t3_assault_bot | mass 480 -> 420 (5400 -> 4725, 2400 -> 2100), MaxHealth 2600 -> 2800, Damage 50 -> 54 (dps 100 -> 108) | 1.13 -> 1.71 |
| cybran_t3_assault_bot | mass 450 -> 400 (5000 -> 4450, 2200 -> 1960), MaxHealth 2500 -> 2550, Damage 52 -> 54 (dps 104 -> 108) | 1.28 -> 1.72 |
| uef_t3_heavy_bot | mass 1280 -> 960 (12800 -> 9600, 5000 -> 3750), Damage 450 -> 600 (dps 180 -> 240) | 0.71 -> 1.69 |
| uef_t3_mobile_artillery | MaxHealth 1200 -> 950, MuzzleVelocity 32 -> 40, Spread 3 -> 5.5 | 0.21 -> 0.17 |
| cybran_t3_mobile_artillery | MuzzleVelocity 32 -> 40, Spread 3 -> 5.5 | 0.19 (same) |

T1 tanks/bots unchanged (UEF 1.72, Cybran 1.73). Targets met: art1 vs tank1 0.00 at 2000 and 6000 in all three
matrices (0.00-0.17 at 600), art1 row 0.15-0.40; hvy2/amph2/asl3/sieg3 vs tank1 (and lgt1) 1.00 at 2000/6000 (asl3
0.00 at 600: one 420-mass bot vs 11 tanks); hvy2 vs asl3 about even (0.00-0.67), sieg3 > hvy2; mml2 and art3 vs
tank1/hvy2/asl3 <= 0.50 at 2000/6000; cross-faction same-role margins within +-0.1 at 2000/6000
except amph2 (-0.13/+0.14) and asl3 (+0.18 at 2000: 5 UEF bots = 2100 mass vs 2000).

Findings: (1) **Shells lead moving targets perfectly** (WeaponSystem solves the arc 3 times with the target's velocity
and turn rate), but in a duel they mostly land on units that already stopped at their own range, so aiming at the
current position instead changed nothing (tried and dropped; no new blueprint field). **Flight time is the lever**:
indirect fire takes the high arc, so a higher MuzzleVelocity means a longer flight (art1 20 -> 45: about 3.7 s -> 9 s
at range 30, apex ~100): volleys fired at approaching units land where they were going, and shells keep falling on
targets that already died. Spread barely matters against clumps, and cost alone did not do it (art1 at twice the cost
still won at 6000). (2) **Range and footprint win big fights**: at 6000 only the front rows of a T1 blob reach the
enemy while longer-ranged or fewer, bigger units all fire, so higher tiers win at equal eff (hvy2 1.91 beats tank1 1.72
by margin +0.4..+0.5; asl3 1.71 ~ hvy2 1.91). (3) Open: higher tiers beat T1 clearly (margins +0.4..+0.7, not
"slightly"); MML and T3 artillery are weak in the open (rows 0.06-0.33: siege only); UEF asl3 wins every 2000 cell
(5 x 420 = 2100 mass). shareA stays nearly binary, so same-role cross cells read 0.00/1.00 even at margin +-0.05.
(4) The reference match `ai 30 256 42 hard,normal` now ends "WINNER: AI 1 (Normal) after 15:50" (was Hard at 11:30),
hash 9724216772093A2E: check AI matches before trusting the AI on these numbers.
