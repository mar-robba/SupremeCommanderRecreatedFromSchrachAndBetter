"""List the lopsided-stalled games of a run dir with the minute-of-event and both sides' state at event+10.
  python3 -I tools/ai-lab/lop.py <run dir> [stalled|ended]"""
import glob, os, re, sys, importlib.util
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('run', os.path.join(os.path.dirname(__file__), 'run.py'))
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
want = sys.argv[2] if len(sys.argv) > 2 else 'stalled'
for f in sorted(glob.glob(os.path.join(sys.argv[1], '*.txt'))):
    s = open(f).read()
    if m.lopsided(s) != want: continue
    rows = [l for l in s.splitlines() if re.match(r"\[\d+:00\]", l)]
    def st(l):
        out = []
        for p in l.split(' | ')[1:]:
            g = lambda k: (re.search(r'\b' + k + r'=([^ ]+)', p) or [None, '?'])[1]
            mi = re.search(r'M ([\d.]+)/', p)
            out.append(f"lv={g('lv')} land={g('land')} def={g('def')} M={mi.group(1) if mi else '?'} acu={g('acu')} val={g('val')}")
        return ' || '.join(out)
    end = re.search(r"^(WINNER: .*|NO WINNER.*)$", s, re.M).group(1)[:40]
    print(os.path.basename(f)[:-4], '|', end)
    for k in (15, 25, 35):
        if k < len(rows): print(f"   [{k}] {st(rows[k])}")
