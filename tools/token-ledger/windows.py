"""Usage-limit windows: how much the account used in each 5-hour session window that ended with a limit, and when
the weekly limit came. Reads the Claude Code transcripts of this project (main sessions + sub-agents, current and
earlier checkout paths).

    python3 -I tools/token-ledger/windows.py [SINCE]     (SINCE = ISO local time for the final total)

Each "session hit" line: when the limit stopped the work, the reset time printed by Claude Code, the window start
(= reset - 5 h), the time of its first model call and the calls and tokens from window start to the stop, per model:
cache reads, cache writes, output, and dollars at list price (same PRICES as ledger.py; output in transcripts is
complete only from 2026-10-23, earlier windows undercount it, so compare them on "input-side $"). Times are
Europe/Rome. Other projects of the owner share the same limit but are not read here. See docs/WORKFLOW.md
"Usage limits".
"""
import datetime as dt
import glob
import json
import os
import re
import sys
from zoneinfo import ZoneInfo

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ledger import PRICES, DEFAULT_PRICE  # noqa: E402  (input, cache write 5 min, cache write 1 h, cache read, output)

ROME = ZoneInfo('Europe/Rome')
BASES = [os.path.expanduser('~/.claude/projects/' + p + '/') for p in ('-run-media-marco-RTS', '-run-media-marco-Volume-D-RTS')]
HIT = [re.compile(r"You've hit your (session|weekly) limit[^\"]{0,10}resets ([0-9:apm]+)"),  # until 2026-10
       re.compile(r"hit your (5-hour|weekly) limit[^\"]{0,6}resets ([0-9:apm]+)")]          # from 2026-11

calls, hits = {}, []
for base in BASES:
    for f in glob.glob(base + '*.jsonl') + glob.glob(base + '*/subagents/*.jsonl'):
        for line in open(f, errors='ignore'):
            try:
                d = json.loads(line)
            except ValueError:
                continue
            ts = d.get('timestamp')
            if not ts:
                continue
            t = dt.datetime.fromisoformat(ts.replace('Z', '+00:00')).astimezone(ROME)
            m = d.get('message')
            if isinstance(m, dict) and m.get('role') == 'assistant' and m.get('usage') and m.get('id'):
                u = m['usage']
                rec = (t, m.get('model') or '?', u.get('input_tokens', 0), u.get('cache_creation_input_tokens', 0),
                       (u.get('cache_creation') or {}).get('ephemeral_1h_input_tokens', 0),
                       u.get('cache_read_input_tokens', 0), u.get('output_tokens', 0))
                old = calls.get(m['id'])
                if old is None or rec[6] > old[6]:  # a streamed message is logged several times: keep the final count
                    calls[m['id']] = rec
            if d.get('type') == 'assistant':
                for rx in HIT:
                    mm = rx.search(line)
                    if mm:
                        hits.append((t, 'weekly' if mm.group(1) == 'weekly' else 'session', mm.group(2)))
                        break
recs = sorted(c for c in calls.values() if c[1] != '<synthetic>')


def usd(r):
    p = PRICES.get(r[1], DEFAULT_PRICE)
    inside = r[2] * p[0] + (r[3] - r[4]) * p[1] + r[4] * p[2] + r[5] * p[3]
    return (inside + r[6] * p[4]) / 1e6, inside / 1e6


first = {}
for t, k, r in sorted(hits):
    first.setdefault((k, r, t.date()), t)
for (k, r, _), t in sorted(first.items(), key=lambda x: x[1]):
    if k == 'weekly':
        print(f"weekly hit {t:%m-%d %H:%M} resets {r}")
        continue
    mt = re.match(r"(\d+)(?::(\d+))?(am|pm)", r)
    reset = t.replace(hour=int(mt.group(1)) % 12 + (12 if mt.group(3) == 'pm' else 0), minute=int(mt.group(2) or 0),
                      second=0, microsecond=0)
    if reset <= t:
        reset += dt.timedelta(days=1)  # a reset after midnight is the next day
    start = reset - dt.timedelta(hours=5)
    sel = [x for x in recs if start <= x[0] <= t]
    if not sel:
        continue
    print(f"session hit {t:%m-%d %H:%M} resets {r}: window from {start:%H:%M}, first call {sel[0][0]:%H:%M}, "
          f"calls={len(sel)}")
    for model in sorted({x[1] for x in sel}):
        s = [x for x in sel if x[1] == model]
        total = sum(usd(x)[0] for x in s)
        inside = sum(usd(x)[1] for x in s)
        print(f"    {model:18s} calls={len(s):4d} cacheRead={sum(x[5] for x in s) / 1e6:5.1f}M "
              f"cacheWrite={sum(x[3] for x in s) / 1e6:4.2f}M output={sum(x[6] for x in s) / 1e3:4.0f}k "
              f"$={total:5.1f} (input-side {inside:4.1f})")

since = dt.datetime.fromisoformat(sys.argv[1] if len(sys.argv) > 1 else '2026-11-05T10:00').replace(tzinfo=ROME)
sel = [x for x in recs if x[0] >= since]
print(f"since {since:%m-%d %H:%M}: calls={len(sel)} tokens={sum(x[2] + x[3] + x[5] for x in sel) / 1e6:.1f}M "
      f"$={sum(usd(x)[0] for x in sel):.1f}")
