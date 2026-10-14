"""Head-to-head result of one difficulty in a matrix run: wins / losses / games without a winner, mean end time,
and the mean mass income of that difficulty vs the others at minutes 3/5/8 (to see whether its economy is ahead).
  python3 -I tools/ai-lab/h2h.py <run dir> <Difficulty>
"""
import collections
import glob
import os
import re
import sys

d = sys.argv[1]
X = sys.argv[2].capitalize()
w = l = n = 0
times = []
inc = collections.defaultdict(list)
for f in sorted(glob.glob(os.path.join(d, "*.txt"))):
    s = open(f).read()
    md = re.search(r"\((\w+) vs (\w+)\)", s)
    if md:
        diffs = [md.group(1), md.group(2)]
        for line in s.splitlines():
            mm = re.match(r"\[(\d+):00\]", line)
            if not mm or mm.group(1) not in ("3", "5", "8"):
                continue
            for k, part in enumerate(line.split(" | ")[1:3]):
                mi = re.search(r"M ([\d.]+)/", part)
                if mi:
                    inc[(diffs[k] == X, mm.group(1))].append(float(mi.group(1)))
    m = re.search(r"^WINNER: .*\((\w+)\) after (\d+):(\d+)", s, re.M)
    if not m:
        n += 1
        continue
    times.append(int(m.group(2)) + int(m.group(3)) / 60)
    if m.group(1) == X:
        w += 1
    else:
        l += 1


def avg(k):
    return sum(inc[k]) / max(1, len(inc[k]))


mine = "/".join(f"{avg((True, t)):.1f}" for t in ("3", "5", "8"))
other = "/".join(f"{avg((False, t)):.1f}" for t in ("3", "5", "8"))
print(f"{X}: won {w} lost {l} none {n} (mean end {sum(times) / max(1, len(times)):.1f} min) "
      f"M@3/5/8 {X} {mine} other {other}")
