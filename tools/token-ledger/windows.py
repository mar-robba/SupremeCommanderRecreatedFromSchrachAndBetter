"""Usage-limit windows: how many tokens the account used in each 5-hour session window that ended with a limit,
and in the weekly window. Reads the Claude Code transcripts of this project (main sessions + sub-agents).

    python3 -I tools/token-ledger/windows.py

Each "session hit" line: when the limit stopped the work, the reset time printed by Claude Code, and the tokens
(cache reads, cache writes, plain input; output is not reliable in transcripts) from window start (= reset - 5 h)
to the stop. Times are Europe/Rome (UTC+2 in October). See docs/WORKFLOW.md "Usage limits".
"""
import json,glob,re,datetime as dt,os,sys
base=os.path.expanduser('~/.claude/projects/-run-media-marco-Volume-D-RTS/')
files=glob.glob(base+'*.jsonl')+glob.glob(base+'*/subagents/*.jsonl')
recs=[];hits=[]
seen=set()
for f in files:
    for l in open(f):
        try: d=json.loads(l)
        except: continue
        ts=d.get('timestamp')
        if not ts: continue
        t=dt.datetime.fromisoformat(ts.replace('Z','+00:00'))
        m=d.get('message')
        if isinstance(m,dict):
            u=m.get('usage'); mid=m.get('id')
            if u and mid and (f,mid) not in seen and m.get('role')=='assistant':
                seen.add((f,mid))
                cr=u.get('cache_read_input_tokens',0); cw=u.get('cache_creation_input_tokens',0); inp=u.get('input_tokens',0); out=u.get('output_tokens',0)
                recs.append((t,cr,cw,inp,out))
        mm=re.search(r"You've hit your (session|weekly) limit[^\"]{0,10}resets ([0-9:apm]+)",l) if d.get('type')=='assistant' else None
        if mm: hits.append((t,mm.group(1),mm.group(2)))
recs.sort()
hits.sort()
# dedupe hits per (kind,reset) keeping earliest
first={}
for t,k,r in hits:
    key=(k,r,t.date())
    if key not in first: first[key]=t
rome=dt.timezone(dt.timedelta(hours=2))
for (k,r,day),t in sorted(first.items(), key=lambda x:x[1]):
    tl=t.astimezone(rome)
    if k=='session':
        mt=re.match(r"(\d+)(?::(\d+))?(am|pm)",r); h=int(mt.group(1))%12+(12 if mt.group(3)=='pm' else 0); mi=int(mt.group(2) or 0)
        reset=tl.replace(hour=h,minute=mi,second=0,microsecond=0)
        if reset<=tl: reset+=dt.timedelta(days=1)  # a reset after midnight is the next day
        start=reset-dt.timedelta(hours=5)
        sel=[x for x in recs if start<=x[0].astimezone(rome)<=tl]
        cr=sum(x[1] for x in sel);cw=sum(x[2] for x in sel);inp=sum(x[3] for x in sel);n=len(sel)
        usd=cr*0.2e-6+cw*5e-6+inp*4e-6
        print(f"{k} hit {tl:%m-%d %H:%M} resets {r}: window from {start:%H:%M}: calls={n} cacheRead={cr/1e6:.1f}M cacheWrite={cw/1e6:.1f}M input={inp/1e6:.2f}M  inputUSD={usd:.0f}")
    else:
        print(f"{k} hit {tl:%m-%d %H:%M} resets {r}")
def tot(a,b):
    sel=[x for x in recs if a<=x[0].astimezone(rome)<b]
    return len(sel), sum(x[1]+x[2]+x[3] for x in sel)/1e6, sum(x[1]*0.2e-6+x[2]*5e-6+x[3]*4e-6 for x in sel)
R=lambda s: dt.datetime.fromisoformat(s).replace(tzinfo=rome)
print("week to 10-14 10:10:", tot(R('2026-10-01T00:00'),R('2026-10-14T10:10')))
print("since the last weekly reset (or argv[1], ISO local time):", tot(R(sys.argv[1] if len(sys.argv)>1 else '2026-10-15T10:00'),R('2100-01-01T00:00')))
