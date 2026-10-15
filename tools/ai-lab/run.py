"""Python version of matrix.sh (for shells where running bash scripts is not allowed), with a compact summary.

  python3 -I tools/ai-lab/run.py <tag> <minutes> <parallel> <specs...|@file> [--bin PATH] [--quiet] [--diff D]

Specs are the strings printed by specs.py (preset:seed:difficulties:players:mapSize); pass "@-" to read them from
stdin or "@file" from a file. Each game runs `Rts.Bench ai <minutes> <map> <seed> <diffs> 1 <players> <preset>` and
writes <worktree>/.claude/ai-lab/runs/<tag>/<game>.txt (AI_CFG / AI_CFG_<Difficulty> env vars pass through).
Summary: games finished (commander kill) per preset, mean end time, wins per difficulty (with --diff D: wins/losses/
none of D like h2h.py), and the mean of the bench "tech:" summary lines (max tier, experimentals, nukes, navy).
"""
import concurrent.futures
import glob
import os
import re
import subprocess
import sys
from collections import defaultdict


def main():
    args = sys.argv[1:]
    quiet = "--quiet" in args
    if quiet:
        args.remove("--quiet")
    bin_path = None
    if "--bin" in args:
        k = args.index("--bin")
        bin_path = args[k + 1]
        del args[k:k + 2]
    diff = None
    if "--diff" in args:
        k = args.index("--diff")
        diff = args[k + 1].capitalize()
        del args[k:k + 2]
    tag, minutes, par = args[0], args[1], int(args[2])
    specs = []
    for s in args[3:]:
        if s == "@-":
            specs += sys.stdin.read().split()
        elif s.startswith("@"):
            specs += open(s[1:]).read().split()
        else:
            specs += s.split()
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(os.path.dirname(here))
    bin_path = bin_path or os.path.join(root, "tools", "Rts.Bench", "bin", "Release", "net10.0", "Rts.Bench")
    out = os.path.join(root, ".claude", "ai-lab", "runs", tag)
    os.makedirs(out, exist_ok=True)

    def run1(spec):
        preset, seed, diffs, players, size = spec.split(":")
        name = f"{preset}_{seed}_{diffs.replace(',', 'v')}_{players}p_{size}"
        with open(os.path.join(out, name + ".txt"), "w") as f:
            try:
                subprocess.run([bin_path, "ai", minutes, size, seed, diffs, "1", players, preset],
                               stdout=f, stderr=subprocess.STDOUT, timeout=2400)
            except subprocess.TimeoutExpired:
                f.write("\nTIMEOUT\n")
        return name

    with concurrent.futures.ThreadPoolExecutor(par) as ex:
        list(ex.map(run1, specs))
    summarize(out, quiet, diff)


def summarize(out, quiet=False, diff=None):
    fin = defaultdict(int)
    tot = defaultdict(int)
    times = []
    wins = defaultdict(int)
    w = l = n = 0
    tech = defaultdict(list)
    errors = 0
    for f in sorted(glob.glob(os.path.join(out, "*.txt"))):
        s = open(f).read()
        preset = os.path.basename(f).split("_")[0]
        tot[preset] += 1
        m = re.search(r"^WINNER: (.*) after (\d+):(\d+)", s, re.M)
        md = re.search(r"^WINNER: .*\((\w+)\) after", s, re.M)
        if m:
            fin[preset] += 1
            times.append(int(m.group(2)) + int(m.group(3)) / 60)
            if md:
                wins[md.group(1)] += 1
            line = f"{m.group(1):18s} {m.group(2)}:{m.group(3)}"
        elif re.search(r"^NO WINNER", s, re.M):
            line = "NO WINNER"
        else:
            errors += 1
            line = "ERROR " + s[-160:].replace("\n", " ")
        if diff is not None:
            if not m:
                n += 1
            elif md and md.group(1) == diff:
                w += 1
            else:
                l += 1
        for t in re.finditer(r"^  (\S.*?) tech: (.*)$", s, re.M):
            for kv in re.finditer(r"(\w+)=([\d.]+)", t.group(2)):
                tech[kv.group(1)].append(float(kv.group(2)))
        if not quiet:
            print(f"{os.path.basename(f)[:-4]:40s} {line}")
    ft = sum(fin.values())
    tt = sum(tot.values())
    per = " ".join(f"{p} {fin[p]}/{tot[p]}" for p in sorted(tot))
    print(f"finished {ft}/{tt} ({per})" + (f", mean {sum(times) / len(times):.1f} min" if times else "")
          + (f", errors {errors}" if errors else ""))
    if wins:
        print("wins: " + ", ".join(f"{k} {v}" for k, v in sorted(wins.items())))
    if diff is not None:
        print(f"{diff}: won {w} lost {l} none {n}")
    if tech:
        print("tech (mean per AI): " + " ".join(f"{k}={sum(v) / len(v):.2f}" for k, v in tech.items()))


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--summary":
        summarize(sys.argv[2], quiet=True)
    else:
        main()
