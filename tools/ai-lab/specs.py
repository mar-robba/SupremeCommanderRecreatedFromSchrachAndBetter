"""Game lists for matrix.sh (see docs/AI_TUNING.md). One spec = preset:seed:difficulties:players:mapSize.

  python3 -I tools/ai-lab/specs.py <difficulties> <seeds> [--both-sides] [--presets a,b] [--players N] [--map N]

<seeds> is a list with ranges, e.g. "1-23,42". --both-sides also adds every game with the difficulties reversed
(e.g. hard,normal AND normal,hard), so the start position doesn't bias a head-to-head result.
The standard matrices:
  NvN 96 games:          specs.py normal,normal 1-23,42
  Hard v Normal 192:     specs.py hard,normal 1-24 --both-sides
  Easy v Normal 48:      specs.py easy,normal 3,5,7,9,11,42 --both-sides
  FFA 4p on 512:         specs.py normal,normal,normal,normal 42,7,3 --presets plains --players 4 --map 512
"""
import argparse


def seeds(text):
    out = []
    for part in text.split(","):
        a, _, b = part.partition("-")
        out.extend(range(int(a), int(b) + 1) if b else [int(a)])
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("difficulties")
    p.add_argument("seeds")
    p.add_argument("--both-sides", action="store_true")
    p.add_argument("--presets", default="plains,lakes,highlands,canyon")
    p.add_argument("--players", type=int, default=2)
    p.add_argument("--map", type=int, default=256)
    a = p.parse_args()
    diffs = [a.difficulties]
    reversed_diffs = ",".join(reversed(a.difficulties.split(",")))
    if a.both_sides and reversed_diffs != a.difficulties:
        diffs.append(reversed_diffs)
    print(" ".join(f"{preset}:{seed}:{d}:{a.players}:{a.map}"
                   for preset in a.presets.split(",") for seed in seeds(a.seeds) for d in diffs))


if __name__ == "__main__":
    main()
