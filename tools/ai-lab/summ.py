"""Summary of a matrix run: one line per game (winner and end time) + how many games ended.
  python3 -I tools/ai-lab/summ.py <run dir>
"""
import glob
import os
import re
import sys

d = sys.argv[1]
fin = 0
tot = 0
times = []
for f in sorted(glob.glob(os.path.join(d, "*.txt"))):
    s = open(f).read()
    tot += 1
    m = re.search(r"^WINNER: (.*) after (\d+):(\d+)", s, re.M)
    if m:
        fin += 1
        times.append(int(m.group(2)) + int(m.group(3)) / 60)
        r = f"{m.group(1):18s} {m.group(2)}:{m.group(3)}"
    else:
        m2 = re.search(r"^NO WINNER.*", s, re.M)
        r = m2.group(0)[:40] if m2 else "ERROR/" + s[-200:].replace("\n", " ")[:80]
    print(f"{os.path.basename(f)[:-4]:40s} {r}")
print(f"finished {fin}/{tot}" + (f", mean {sum(times) / len(times):.1f} min" if times else ""))
