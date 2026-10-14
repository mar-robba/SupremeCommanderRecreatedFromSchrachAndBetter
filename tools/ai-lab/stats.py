"""Average economy/army numbers per difficulty at given minute marks of a matrix run (2-player games).
  python3 -I tools/ai-lab/stats.py <run dir> [2:00 3:00 ...]
"""
import collections
import glob
import os
import re
import sys

d = sys.argv[1]
marks = sys.argv[2:] or ["2:00", "3:00", "4:00", "5:00", "7:00", "10:00"]
acc = collections.defaultdict(lambda: collections.defaultdict(list))
for f in glob.glob(os.path.join(d, "*.txt")):
    s = open(f).read()
    m = re.search(r"\((\w+) vs (\w+)\)", s)
    if not m:
        continue
    diffs = [m.group(1), m.group(2)]
    for line in s.splitlines():
        mm = re.match(r"\[(\d+:\d+)\]", line)
        if not mm or mm.group(1) not in marks:
            continue
        for k, part in enumerate(line.split(" | ")[1:3]):
            row = acc[(diffs[k], mm.group(1))]
            for key in ("mex", "land", "eng", "fac", "lost"):
                row[key].append(float(re.search(key + r"=([\d.]+)", part).group(1)))
            row["Minc"].append(float(re.search(r"M ([\d.]+)/", part).group(1)))
for key in sorted(acc, key=lambda k: (k[0], int(k[1].split(":")[0]))):
    v = acc[key]
    print(f"{key[0]:7s} {key[1]:>6s} " + " ".join(f"{n}={sum(x) / len(x):6.1f}" for n, x in v.items())
          + f"  (n={len(v['mex'])})")
