#!/usr/bin/env python3
"""Token ledger: how many tokens each Claude Code session and agent of this project used, and for which purpose.

    ledger.py update [--quiet]   read the transcripts + exact counters, rewrite docs/tokens/{ledger.csv,summary.org}
    ledger.py snapshot [--then-update]   save Claude Code's exact counter of the last session (fast, for SessionEnd)
    ledger.py report [--days N]  print the totals per area and purpose (all time, or the last N days)
    ledger.py tag ID SCOPO [--area AREA]   record the purpose of a session or agent (id or id prefix)
    ledger.py hello              SessionStart hook: tells the model its session id and how to tag it

Sources (see docs/tokens/README.org):
- transcripts in ~/.claude/projects/<project>/ : one usage record per model call. Input tokens are exact; the output
  count is often only the partial value written when the stream starts, so it is estimated from the visible text;
- ~/.claude.json projects[<repo>].last* : Claude Code's exact counter, kept for the LAST session only. `snapshot`
  copies it into docs/tokens/counters.jsonl, and `update` uses it to calibrate that session's output estimate.
The ledger keeps rows whose transcripts were deleted (Claude Code removes transcripts after cleanupPeriodDays).
Standard library only. Purposes: tools/token-ledger/purposes.json.
"""
import csv, datetime, fcntl, glob, io, json, os, re, subprocess, sys, time
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT_DIR = os.path.join(REPO, 'docs', 'tokens')
LEDGER = os.path.join(OUT_DIR, 'ledger.csv')
COUNTERS = os.path.join(OUT_DIR, 'counters.jsonl')
TAGS = os.path.join(OUT_DIR, 'tags.csv')
SUMMARY = os.path.join(OUT_DIR, 'summary.org')
PURPOSES = os.path.join(HERE, 'purposes.json')
CLAUDE_HOME = os.path.expanduser('~/.claude')
PROJECTS = os.path.join(CLAUDE_HOME, 'projects')
CACHE_DIR = os.path.join(os.environ.get('XDG_CACHE_HOME', os.path.expanduser('~/.cache')), 'rts-token-ledger')
CACHE = os.path.join(CACHE_DIR, 'cache.json')
LOCK = os.path.join(CACHE_DIR, 'lock')
CACHE_VERSION = 2
LOCAL_TZ = datetime.timezone(datetime.timedelta(hours=2))  # Europe/Rome (summer); only used to group rows by day

# USD per million tokens: input, cache write 5 min, cache write 1 h, cache read, output (API list prices).
PRICES = {
    'claude-opus-5-5': (4, 5, 8, 0.20, 20),
    'claude-sonnet-5-5': (2, 2.5, 4, 0.20, 10),
    'claude-haiku-5-5': (0.10, 0.125, 0.20, 0.01, 0.50),
    'claude-fable-5-1': (10, 12.5, 20, 0.25, 50),
}
DEFAULT_PRICE = PRICES['claude-opus-5-5']

FIELDS = ['chiave', 'giorno', 'sessione', 'agente', 'padre', 'tipo', 'scopo', 'area', 'descrizione', 'inizio', 'fine',
          'modelli', 'chiamate', 'input', 'cache_scrittura', 'cache_lettura', 'output', 'output_fonte', 'usd']


def project_key(path):
    return re.sub(r'[^A-Za-z0-9]', '-', path)


def transcript_dirs():
    prefix = project_key(REPO)
    return sorted(d for d in glob.glob(os.path.join(PROJECTS, prefix + '*')) if os.path.isdir(d))


# ---------------------------------------------------------------- parsing (cached per file by size + mtime)

def first_prompt(rec):
    c = rec.get('message', {}).get('content')
    texts = [c] if isinstance(c, str) else [b.get('text', '') for b in c or [] if isinstance(b, dict) and b.get('type') == 'text']
    for t in texts:
        t = t.strip()
        if t and not t.startswith('<') and not t.startswith('[Request interrupted'):
            return ' '.join(t.split())[:100]
    return None


def parse_file(path):
    """-> {'msgs': [[id, ts, model, in, cw, cw1h, cr, out, final, chars]], 'first': ts, 'last': ts, 'prompt': str}"""
    msgs, order, prompt, first, last = {}, [], None, None, None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            is_assistant = '"assistant"' in line
            if not is_assistant and (prompt is not None or '"user"' not in line):
                continue
            try:
                d = json.loads(line)
            except ValueError:
                continue
            ts = d.get('timestamp')
            if ts:
                first = first or ts
                last = ts
            if d.get('type') == 'user' and prompt is None and not d.get('isMeta'):
                prompt = first_prompt(d)
            if d.get('type') != 'assistant':
                continue
            m = d.get('message') or {}
            mid, u = m.get('id'), m.get('usage')
            if not mid or not u or m.get('model') == '<synthetic>':
                continue
            e = msgs.get(mid)
            if e is None:
                e = msgs[mid] = dict(ts=ts or '', model=m.get('model') or '?', u=u, out=0, final=False, chars=0, seen=set())
                order.append(mid)
            e['u'] = u
            e['out'] = max(e['out'], u.get('output_tokens') or 0)
            if 'iterations' in u or 'output_tokens_details' in u:
                e['final'] = True
            for b in m.get('content') or []:
                if not isinstance(b, dict):
                    continue
                key = json.dumps(b, sort_keys=True)[:300]
                if key in e['seen']:
                    continue
                e['seen'].add(key)
                if b.get('type') == 'text':
                    e['chars'] += len(b.get('text') or '')
                elif b.get('type') == 'tool_use':
                    e['chars'] += len(json.dumps(b.get('input') or {}, ensure_ascii=False))
    out = []
    for mid in order:
        e = msgs[mid]
        u = e['u']
        cc = u.get('cache_creation') or {}
        out.append([mid, e['ts'], e['model'], u.get('input_tokens') or 0, u.get('cache_creation_input_tokens') or 0,
                    cc.get('ephemeral_1h_input_tokens') or 0, u.get('cache_read_input_tokens') or 0, e['out'],
                    e['final'], e['chars']])
    return dict(msgs=out, first=first, last=last, prompt=prompt)


def load_transcripts():
    try:
        cache = json.load(open(CACHE))
        if cache.get('version') != CACHE_VERSION:
            cache = {}
    except (OSError, ValueError):
        cache = {}
    files = cache.get('files', {})
    new_files, parsed = {}, {}
    for d in transcript_dirs():
        for path in sorted(glob.glob(os.path.join(d, '*.jsonl')) + glob.glob(os.path.join(d, '*', 'subagents', '*.jsonl'))):
            st = os.stat(path)
            sig = [st.st_size, int(st.st_mtime)]
            c = files.get(path)
            if not c or c['sig'] != sig:
                c = dict(sig=sig, data=parse_file(path))
            new_files[path] = c
            parsed[path] = c['data']
    os.makedirs(CACHE_DIR, exist_ok=True)
    tmp = CACHE + '.tmp'
    with open(tmp, 'w') as f:
        json.dump(dict(version=CACHE_VERSION, files=new_files), f)
    os.replace(tmp, CACHE)
    return parsed


# ---------------------------------------------------------------- exact counters (~/.claude.json)

def read_counters():
    rows = []
    try:
        for line in open(COUNTERS, encoding='utf-8'):
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    except OSError:
        pass
    return rows


def snapshot():
    """Copy Claude Code's counter of the last session of this project into counters.jsonl (keeps the largest)."""
    try:
        cfg = json.load(open(os.path.expanduser('~/.claude.json')))
    except (OSError, ValueError):
        return False
    p = cfg.get('projects', {}).get(REPO) or {}
    sid = p.get('lastSessionId')
    if not sid or not p.get('lastModelUsage'):
        return False
    rec = dict(sessione=sid, costo_usd=round(p.get('lastCost') or 0, 6),
               input=p.get('lastTotalInputTokens') or 0, output=p.get('lastTotalOutputTokens') or 0,
               cache_scrittura=p.get('lastTotalCacheCreationInputTokens') or 0,
               cache_lettura=p.get('lastTotalCacheReadInputTokens') or 0,
               durata_s=round((p.get('lastDuration') or 0) / 1000),
               modelli={m: {k: v.get(k, 0) for k in ('inputTokens', 'outputTokens', 'thinkingTokens', 'cacheReadInputTokens',
                                                   'cacheCreationInputTokens', 'costUSD')}
                        for m, v in sorted((p.get('lastModelUsage') or {}).items())})
    size = lambda r: r['input'] + r['output'] + r['cache_scrittura'] + r['cache_lettura']
    rows = read_counters()
    for i, r in enumerate(rows):
        if r['sessione'] == sid:
            if size(rec) <= size(r):
                return False
            rows[i] = rec
            break
    else:
        rows.append(rec)
    os.makedirs(OUT_DIR, exist_ok=True)
    write_text(COUNTERS, ''.join(json.dumps(r, ensure_ascii=False, sort_keys=True) + '\n' for r in rows))
    return True


# ---------------------------------------------------------------- ledger

def load_purposes():
    return json.load(open(PURPOSES, encoding='utf-8'))


def agent_kind(desc):
    d = desc.lower()
    if d.startswith('merge') or '] merge' in d:
        return 'merge'
    if any(s in d for s in ('fresh', 'resume', 'part 2', 'part 3', 'finish', 'agent g2', 'agent h2', 'agent i2', 'agent f2')):
        return 'ripresa'
    return 'agente'


def agent_scopo(desc, pur):
    m = re.match(r'\s*\[([^\]]+)\]', desc)
    if m:
        return m.group(1).strip()
    for pattern, scopo in pur['regole']:
        if pattern in desc:
            return scopo
    return pur['default_agente']


def session_scopo(sid, ts, pur):
    for o in pur.get('sessioni', []):
        if sid.startswith(o['sessione']) and ('prima_di' not in o or ts < o['prima_di']):
            return o['scopo']
    return pur['default_sessione']


def read_tags():
    try:
        with open(TAGS, encoding='utf-8', newline='') as f:
            return list(csv.DictReader(f))
    except OSError:
        return []


def find_tag(tags, ident):
    """The last tag whose id is a prefix of ident (a session uuid or an agent id)."""
    hit = None
    for t in tags:
        if t['id'] and ident.startswith(t['id']):
            hit = t
    return hit


def tag(ident, scopo, area):
    tags = [t for t in read_tags() if t['id'] != ident]
    tags.append(dict(id=ident, scopo=scopo, area=area or '', data=datetime.date.today().isoformat()))
    os.makedirs(OUT_DIR, exist_ok=True)
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=['id', 'scopo', 'area', 'data'], lineterminator='\n')
    w.writeheader()
    w.writerows(tags)
    write_text(TAGS, buf.getvalue())


def hello():
    """SessionStart: one short line of context, so the model can tag what the session is for."""
    try:
        sid = json.load(sys.stdin).get('session_id', '')
    except ValueError:
        sid = ''
    if not sid:
        return
    msg = ('Registro token (docs/tokens/): questa sessione è %s. Quando lo scopo è chiaro, registralo con '
           'python3 tools/token-ledger/ledger.py tag %s "SCOPO" --area AREA (aree: vedi docs/tokens/summary.org). '
           'Agli agenti dai una descrizione che inizia con [SCOPO].' % (sid[:8], sid[:8]))
    print(json.dumps({'hookSpecificOutput': {'hookEventName': 'SessionStart', 'additionalContext': msg}}))


def price(model):
    return PRICES.get(model, DEFAULT_PRICE)


def build_rows(parsed, counters, pur):
    root_len = len(PROJECTS.rstrip('/').split('/'))
    meta, info = {}, {}
    for path in parsed:
        parts = path.split('/')
        sub = '/subagents/' in path
        sid = parts[root_len + 1] if sub else os.path.basename(path)[:-6]
        aid = os.path.basename(path)[6:-6] if sub else ''
        info[path] = (sid, aid)
        mf = path[:-6] + '.meta.json'
        if sub and os.path.exists(mf):
            try:
                m = json.load(open(mf))
            except ValueError:
                continue
            old = meta.setdefault(aid, {})
            for k, v in m.items():
                if v and not old.get(k):
                    old[k] = v
    # each model call counts once: a message id found in several files belongs to the agent file
    owner = {}
    for path, data in parsed.items():
        for m in data['msgs']:
            if m[0] not in owner or '/subagents/' in path:
                owner[m[0]] = path
    # output estimate for calls whose count was cut: visible chars / 4 * ratio measured on the complete calls
    fin_out = sum(m[7] for p, d in parsed.items() for m in d['msgs'] if m[8] and owner[m[0]] == p)
    fin_chars = sum(m[9] for p, d in parsed.items() for m in d['msgs'] if m[8] and owner[m[0]] == p)
    ratio = fin_out / (fin_chars / 4) if fin_chars else 2.5
    calls = []  # [path, sid, aid, ts, model, in, cw, cw1h, cr, out_exact_or_None, est]
    for path, data in parsed.items():
        sid, aid = info[path]
        for m in data['msgs']:
            if owner[m[0]] != path:
                continue
            est = 0 if m[8] else max(m[7], m[9] / 4 * ratio)
            calls.append([path, sid, aid, m[1], m[2], m[3], m[4], m[5], m[6], m[7] if m[8] else None, est])
    # calibration with Claude Code's exact counter, when it covers the same calls as the transcripts
    calibrated = set()
    by_sid = defaultdict(list)
    for c in calls:
        by_sid[c[1]].append(c)
    for rec in counters:
        group = by_sid.get(rec['sessione'])
        if not group:
            continue
        main = [c for c in group if not c[2]]
        for scope in (main, group):
            cr = sum(c[8] for c in scope)
            if rec['cache_lettura'] and abs(cr - rec['cache_lettura']) <= 0.02 * rec['cache_lettura']:
                break
        else:
            continue
        for model, mu in rec.get('modelli', {}).items():
            mc = [c for c in scope if c[4] == model]
            exact = sum(c[9] for c in mc if c[9] is not None)
            est = sum(c[10] for c in mc if c[9] is None)
            rest = mu.get('outputTokens', 0) - exact
            if est > 0 and rest >= 0:
                for c in mc:
                    if c[9] is None:
                        c[10] *= rest / est
                        calibrated.add(id(c))
    rows = {}
    tags = read_tags()
    desc_of = {a: m.get('description', '') for a, m in meta.items()}
    for c in calls:
        path, sid, aid, ts, model = c[:5]
        tag = find_tag(tags, aid or sid)
        if aid:
            desc = desc_of.get(aid, '')
            scopo, kind = agent_scopo(desc, pur), agent_kind(desc)
        else:
            desc = parsed[path].get('prompt') or ''
            scopo, kind = session_scopo(sid, ts, pur), 'sessione'
        area = pur['aree'].get(scopo, 'Da classificare')
        if tag:
            scopo = tag['scopo']
            area = tag['area'] or pur['aree'].get(scopo, 'Da classificare')
        day = local_day(ts)
        key = '%s/%s/%s/%s' % (sid, aid or 'main', scopo, day)
        r = rows.get(key)
        if r is None:
            r = rows[key] = dict(chiave=key, giorno=day, sessione=sid, agente=aid,
                                 padre=(meta.get(aid) or {}).get('parentAgentId', ''),
                                 tipo=kind, scopo=scopo, area=area, descrizione=desc,
                                 inizio=ts, fine=ts, modelli=set(), chiamate=0, input=0, cache_scrittura=0,
                                 cache_lettura=0, output=0.0, fonti=set(), usd=0.0)
        r['inizio'], r['fine'] = min(r['inizio'], ts), max(r['fine'], ts)
        r['modelli'].add(model.replace('claude-', ''))
        out = c[9] if c[9] is not None else c[10]
        r['fonti'].add('esatto' if c[9] is not None else 'calibrato' if id(c) in calibrated else 'stimato')
        r['chiamate'] += 1
        r['input'] += c[5]
        r['cache_scrittura'] += c[6]
        r['cache_lettura'] += c[8]
        r['output'] += out
        p = price(model)
        r['usd'] += (c[5] * p[0] + (c[6] - c[7]) * p[1] + c[7] * p[2] + c[8] * p[3] + out * p[4]) / 1e6
    for r in rows.values():
        f = r.pop('fonti')
        r['output_fonte'] = 'stimato' if 'stimato' in f else 'calibrato' if 'calibrato' in f else 'esatto'
        r['modelli'] = '+'.join(sorted(r['modelli']))
        r['output'] = int(round(r['output']))
        r['usd'] = '%.2f' % r['usd']
        r['inizio'], r['fine'] = r['inizio'][:16], r['fine'][:16]
    return rows, ratio


def read_ledger():
    try:
        with open(LEDGER, encoding='utf-8', newline='') as f:
            return {r['chiave']: r for r in csv.DictReader(f)}
    except OSError:
        return {}


def write_text(path, text):
    try:
        if open(path, encoding='utf-8').read() == text:
            return
    except OSError:
        pass
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    os.replace(tmp, path)


def write_ledger(rows):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=FIELDS, lineterminator='\n')
    w.writeheader()
    for r in sorted(rows.values(), key=lambda r: (r['inizio'], r['chiave'])):
        w.writerow({k: r.get(k, '') for k in FIELDS})
    write_text(LEDGER, buf.getvalue())


# ---------------------------------------------------------------- summary

def it(x, d=1):
    return ('{:,.%df}' % d).format(x).replace(',', 'X').replace('.', ',').replace('X', '.')


def tok(r):
    return sum(int(r[k]) for k in ('input', 'cache_scrittura', 'cache_lettura', 'output'))


def local_day(ts):
    if not ts:
        return ''
    t = datetime.datetime.fromisoformat(ts.replace('Z', '+00:00'))
    if t.tzinfo is None:
        t = t.replace(tzinfo=datetime.timezone.utc)
    return t.astimezone(LOCAL_TZ).strftime('%Y-%m-%d')


def org_table(header, rows):
    rows = [header] + rows
    w = [max(len(r[i]) for r in rows) for i in range(len(header))]
    line = lambda r: '| ' + ' | '.join(c.ljust(w[i]) for i, c in enumerate(r)) + ' |'
    sep = '|' + '+'.join('-' * (x + 2) for x in w) + '|'
    return '\n'.join([line(rows[0]), sep] + [line(r) for r in rows[1:]])


def group(rows, key):
    g = defaultdict(lambda: dict(n=0, tok=0, out=0, usd=0.0, last='', agents=set()))
    for r in rows:
        e = g[key(r)]
        e['n'] += 1
        e['tok'] += tok(r)
        e['out'] += int(r['output'])
        e['usd'] += float(r['usd'])
        e['last'] = max(e['last'], r['fine'])
        if r['agente']:
            e['agents'].add(r['agente'])
    for e in g.values():
        e['agents'] = len(e['agents'])
    return g


def summary_tables(rows, days=None):
    if days:
        cut = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=days)).strftime('%Y-%m-%dT%H:%M')
        rows = [r for r in rows if r['fine'] >= cut]
    tot = sum(float(r['usd']) for r in rows) or 1.0
    def table(g, title):
        body = [[k, str(e['agents']), it(e['tok'] / 1e6), it(e['out'] / 1e3, 0), it(e['usd']), it(100 * e['usd'] / tot),
                 local_day(e['last'] + 'Z') if e['last'] else ''] for k, e in sorted(g.items(), key=lambda x: -x[1]['usd'])]
        return org_table([title, 'Agenti', 'Token (M)', 'Output (k)', '$', '% costo', 'Ultima attività'], body)
    return rows, tot, table(group(rows, lambda r: r['area']), 'Area'), table(group(rows, lambda r: r['scopo']), 'Scopo')


def write_summary(rows, counters, ratio):
    rows = list(rows.values())
    rows, tot, by_area, by_scopo = summary_tables(rows)
    last = max((r['fine'] for r in rows), default='')
    days = group(rows, lambda r: r['giorno'])
    day_rows = [[d, it(e['tok'] / 1e6), it(e['usd']), str(e['agents'])] for d, e in sorted(days.items())]
    recent = sorted(rows, key=lambda r: (r['fine'], r['chiave']), reverse=True)[:20]
    recent_rows = [[r['fine'][:16].replace('T', ' '), r['tipo'], r['scopo'], (r['descrizione'] or '')[:50],
                    it(tok(r) / 1e6), r['usd'], r['output_fonte']] for r in recent]
    todo = sorted({(r['scopo'], r['descrizione'][:60]) for r in rows if r['area'] == 'Da classificare'})
    fonti = Counter(r['output_fonte'] for r in rows)
    out = ['#+TITLE: Registro dei token',
           '#+LANGUAGE: it',
           '#+STARTUP: showall',
           '',
           'Generato da ~tools/token-ledger/ledger.py~ (non modificare a mano). Come funziona: [[file:README.org][README.org]].',
           'Dati fino al %s UTC. Dollari = equivalente a listino API, non quanto paga l\'abbonamento.' % last.replace('T', ' '),
           '',
           '* Totale',
           '%s milioni di token, %s $, %d sessioni e %d agenti.' % (
               it(sum(tok(r) for r in rows) / 1e6), it(tot), len({r['sessione'] for r in rows if not r['agente']}),
               len({r['agente'] for r in rows if r['agente']})),
           'Output: %d righe esatte, %d calibrate sul contatore di Claude Code, %d stimate (testo visibile / 4 × %s).'
           % (fonti['esatto'], fonti['calibrato'], fonti['stimato'], it(ratio, 2)),
           '',
           '* Per area', by_area, '',
           '* Per scopo', by_scopo, '',
           '* Per giorno', org_table(['Giorno', 'Token (M)', '$', 'Agenti'], day_rows), '',
           '* Ultime attività', org_table(['Fine (UTC)', 'Tipo', 'Scopo', 'Descrizione', 'Token (M)', '$', 'Output'],
                                         recent_rows), '']
    if todo:
        out += ['* Da classificare',
                'Etichettarli con ~ledger.py tag ID SCOPO --area AREA~, aggiungere una regola o l\'area dello scopo in',
                '~tools/token-ledger/purposes.json~, oppure dare agli agenti una descrizione che inizi con ~[scopo]~.', '']
        out += ['- %s: %s' % (s, d) for s, d in todo] + ['']
    write_text(SUMMARY, '\n'.join(out))


# ---------------------------------------------------------------- commands

def update(quiet):
    os.makedirs(CACHE_DIR, exist_ok=True)
    lock = open(LOCK, 'w')
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        return  # another update is running and will see the same data
    snapshot()
    pur = load_purposes()
    parsed = load_transcripts()
    counters = read_counters()
    rows, ratio = build_rows(parsed, counters, pur)
    old = read_ledger()
    live = {(r['sessione'], r['agente']) for r in rows.values()}
    for k, r in old.items():  # rows whose transcript is gone stay in the ledger
        if k not in rows and (r['sessione'], r['agente']) not in live:
            rows[k] = r
    os.makedirs(OUT_DIR, exist_ok=True)
    write_ledger(rows)
    write_summary(read_ledger(), counters, ratio)
    if not quiet:
        print('%d righe, %d trascrizioni lette -> %s' % (len(rows), len(parsed), os.path.relpath(LEDGER, REPO)))


def report(days):
    rows = list(read_ledger().values())
    if not rows:
        print('Registro vuoto: eseguire prima "ledger.py update".')
        return
    rows, tot, by_area, by_scopo = summary_tables(rows, days)
    print('Periodo: %s. Totale %s M token, %s $ (equivalente API).\n' % (
        'ultimi %d giorni' % days if days else 'tutto', it(sum(tok(r) for r in rows) / 1e6), it(tot)))
    print(by_area + '\n\n' + by_scopo)


def main(argv):
    cmd = argv[1] if len(argv) > 1 else 'update'
    if cmd == 'update':
        update('--quiet' in argv)
    elif cmd == 'snapshot':
        snapshot()
        if '--then-update' in argv:  # detached: survives the end of the session, re-reads the counter once it is saved
            subprocess.Popen([sys.executable, os.path.abspath(__file__), 'delayed-update'], start_new_session=True,
                             stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    elif cmd == 'delayed-update':
        time.sleep(5)
        update(True)
    elif cmd == 'tag' and len(argv) >= 4:
        area = argv[argv.index('--area') + 1] if '--area' in argv else ''
        tag(argv[2], argv[3], area)
        update(True)
        print('%s -> %s%s' % (argv[2], argv[3], ' (%s)' % area if area else ''))
    elif cmd == 'hello':
        hello()
    elif cmd == 'report':
        days = int(argv[argv.index('--days') + 1]) if '--days' in argv else None
        report(days)
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
