"""Cybran vs UEF result of a run dir made from specs/cvu.txt: wins per faction (seat from the file name).
  python3 -I tools/ai-lab/cvu.py .claude/ai-lab/runs/<tag>"""
import glob, os, re, sys
w = {"cybran": 0, "uef": 0}; n = 0
for f in glob.glob(os.path.join(sys.argv[1], "*.txt")):
    fac = os.path.basename(f)[:-4].split("_")[-1].split("v")
    m = re.search(r"^WINNER: (Player|AI 1)", open(f).read(), re.M)
    if not m: n += 1; continue
    w[fac[0] if m.group(1) == "Player" else fac[1]] += 1
print(f"cybran {w['cybran']} uef {w['uef']} none {n}")
