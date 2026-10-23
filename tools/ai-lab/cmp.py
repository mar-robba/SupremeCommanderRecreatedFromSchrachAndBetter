"""Compare matrix runs: mean status-line numbers per minute mark (all armies alive at that mark, optionally one
difficulty), one row per run.
  python3 -I tools/ai-lab/cmp.py <run dir> [<run dir> ...] [--marks 5,8,12,16,20,25] [--diff Hard]
"""
import glob
import os
import re
import sys
from collections import defaultdict

args = sys.argv[1:]
marks = ["5", "8", "12", "16", "20", "25"]
diff = None
if "--marks" in args:
    k = args.index("--marks"); marks = args[k + 1].split(","); del args[k:k + 2]
if "--diff" in args:
    k = args.index("--diff"); diff = args[k + 1].capitalize(); del args[k:k + 2]
KEYS = ["mex", "pg", "fac", "def", "eng", "land", "air"]
SP = ["sArmy", "sEng", "sMex", "sMexU", "sFac", "sFacU", "sPow", "sSto", "sDef", "sOth", "sEnh"]
COLS = KEYS + ["Mi", "Md", "Ms", "Ei", "Ed", "Es", "vA", "vD", "vE", "lost"]
if "--mil" in args:
    args.remove("--mil"); COLS = ["land", "air", "pool", "defend", "attack", "waves", "vA", "lost"]
if "--spent" in args:
    args.remove("--spent"); COLS = ["Mi"] + SP


def parse(d):
    acc = defaultdict(lambda: defaultdict(list))
    for f in glob.glob(os.path.join(d, "*.txt")):
        s = open(f).read()
        m = re.search(r"\((\w+)(?: \w+)? vs (\w+)(?: \w+)?\)", s)
        diffs = [m.group(1), m.group(2)] if m else []
        for line in s.splitlines():
            mm = re.match(r"\[(\d+):00\]", line)
            if not mm or mm.group(1) not in marks:
                continue
            for k, part in enumerate(line.split(" | ")[1:]):
                if "DEFEATED" in part or (diff and k < len(diffs) and diffs[k] != diff):
                    continue
                row = acc[mm.group(1)]
                for key in KEYS + ["lost", "pool", "defend", "attack", "waves"]:
                    x = re.search(r"\b" + key + r"=([\d.]+)", part)
                    if x: row[key].append(float(x.group(1)))
                x = re.search(r"M ([\d.]+)/([\d.]+)/([\d.]+)", part)
                if x: row["Mi"].append(float(x.group(1))); row["Md"].append(float(x.group(2))); row["Ms"].append(float(x.group(3)))
                x = re.search(r"E ([\d.]+)/([\d.]+)/([\d.]+)", part)
                if x: row["Ei"].append(float(x.group(1))); row["Ed"].append(float(x.group(2))); row["Es"].append(float(x.group(3)))
                x = re.search(r"val=([\d.]+)k/([\d.]+)k/([\d.]+)k", part)
                if x:
                    row["vA"].append(float(x.group(1))); row["vD"].append(float(x.group(2))); row["vE"].append(float(x.group(3)))
                x = re.search(r"spent=([\d./]+)", part)
                if x:
                    for c, v in zip(SP, x.group(1).split("/")): row[c].append(float(v))
    return acc


runs = [(os.path.basename(d.rstrip("/")), parse(d)) for d in args]
print(f"{'run':14s} {'min':>3s} " + " ".join(f"{c:>6s}" for c in COLS) + "   n")
for mk in marks:
    for name, acc in runs:
        row = acc.get(mk)
        if not row:
            continue
        n = max(len(v) for v in row.values())
        print(f"{name[:14]:14s} {mk:>3s} " + " ".join(
            f"{(sum(row[c]) / len(row[c]) if row[c] else float('nan')):6.1f}" for c in COLS) + f" {n:3d}")
    print()
