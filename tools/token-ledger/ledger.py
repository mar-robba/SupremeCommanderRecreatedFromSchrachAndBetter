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
Standard library only. Purposes, their areas and waves, and the lines each module merged: tools/token-ledger/purposes.json.
"""
import csv, datetime, fcntl, glob, io, json, os, re, subprocess, sys, time
from collections import Counter, defaultdict

try:
    from zoneinfo import ZoneInfo
    LOCAL_TZ = ZoneInfo('Europe/Rome')  # only used to group rows by day
except Exception:  # no time zone database on this machine
    LOCAL_TZ = datetime.timezone(datetime.timedelta(hours=1))

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

# USD per million tokens: input, cache write 5 min, cache write 1 h, cache read, output (API list prices).
PRICES = {
    'claude-opus-5-5': (4, 5, 8, 0.20, 20),
    'claude-sonnet-5-5': (2, 2.5, 4, 0.10, 10),
    'claude-haiku-5-5': (0.10, 0.125, 0.20, 0.01, 0.50),
    'claude-fable-5-1': (10, 12.5, 20, 0.25, 50),
    'claude-fable-5-2': (8, 10, 16, 0.20, 40),  # cache writes = input x 1.25 (5 min) and x 2 (1 h), as for the others
}
DEFAULT_PRICE = PRICES['claude-opus-5-5']  # a model missing above is priced like this and named in summary.org
UNCLASSIFIED = 'Da classificare'

FIELDS = ['chiave', 'giorno', 'sessione', 'agente', 'padre', 'tipo', 'scopo', 'area', 'descrizione', 'inizio', 'fine',
          'modelli', 'chiamate', 'input', 'cache_scrittura', 'cache_lettura', 'output', 'output_fonte', 'usd']


def project_key(path):
    return re.sub(r'[^A-Za-z0-9]', '-', path)


# Transcript folders of earlier checkout paths (the disk was mounted at "/run/media/marco/Volume D" until 2026-10-22).
LEGACY_KEYS = ['-run-media-marco-Volume-D-RTS']


def transcript_dirs():
    dirs = []
    for prefix in [project_key(REPO)] + LEGACY_KEYS:
        dirs += [d for d in glob.glob(os.path.join(PROJECTS, prefix + '*')) if os.path.isdir(d)]
    return sorted(set(dirs))


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
    if any(s in d for s in ('fresh', 'resume', 'finish', 'agent g2', 'agent h2', 'agent i2', 'agent f2')):
        return 'ripresa'
    if re.search(r'\b(coordinator|agent|part) ([2-9]|\d\d)\b', d):  # "coordinator 3", "navy agent 2", "part 4": a successor
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


def valid_area(area, pur):
    """The area as spelled in purposes.json 'aree_valide' (case does not matter), or '' if it is not one of them."""
    for a in pur.get('aree_valide', []):
        if a.lower() == (area or '').strip().lower():
            return a
    return ''


def classify(sid, aid, desc, ts, tags, pur):
    """-> (scopo, area) of a session or agent: tag > [scopo] in the description > rule > default."""
    scopo = agent_scopo(desc, pur) if aid else session_scopo(sid, ts, pur)
    area = ''
    t = find_tag(tags, aid or sid)
    if t:
        scopo, area = t['scopo'], valid_area(t['area'], pur)
    return scopo, area or pur['aree'].get(scopo, UNCLASSIFIED)


def row_key(sid, aid, scopo, day):
    return '%s/%s/%s/%s' % (sid, aid or 'main', scopo, day)


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
    try:  # never let a broken purposes.json or ledger.csv stop a session from starting
        areas = ', '.join(load_purposes().get('aree_valide', []))
        todo = sorted({r['scopo'] for r in read_ledger().values() if r['area'] == UNCLASSIFIED})
    except Exception:
        areas, todo = 'vedi docs/tokens/README.org', []
    msg = ('Registro token (docs/tokens/): questa sessione è %s. Quando lo scopo è chiaro, registralo con '
           'python3 tools/token-ledger/ledger.py tag %s "SCOPO" --area AREA (aree: %s). '
           'Agli agenti dai una descrizione che inizia con [SCOPO].' % (sid[:8], sid[:8], areas))
    if todo:
        msg += (' Scopi ancora senza area nel registro: %s. Aggiungili ad "aree" (e a "onde", "righe" dopo il merge) in '
                'tools/token-ledger/purposes.json, poi ledger.py update.' % '; '.join(todo))
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
        if aid:
            desc = desc_of.get(aid, '')
            kind = agent_kind(desc)
        else:
            desc = parsed[path].get('prompt') or ''
            kind = 'sessione'
        scopo, area = classify(sid, aid, desc, ts, tags, pur)
        day = local_day(ts)
        key = row_key(sid, aid, scopo, day)
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


def keep_row(rows, r, tags, pur):
    """Add a ledger row whose transcript is gone. Its tokens and dollars stay as they were; its purpose and area
    follow today's tags and rules, so a purpose can still be renamed, split or given an area after 30 days."""
    r = dict(r)
    if r['agente']:
        r['tipo'] = agent_kind(r['descrizione'])
    r['scopo'], r['area'] = classify(r['sessione'], r['agente'], r['descrizione'], r['inizio'], tags, pur)
    r['chiave'] = row_key(r['sessione'], r['agente'], r['scopo'], r['giorno'])
    old = rows.get(r['chiave'])
    if old is None:
        rows[r['chiave']] = r
        return
    for k in ('chiamate', 'input', 'cache_scrittura', 'cache_lettura', 'output'):  # two rows became one purpose
        old[k] = int(old[k]) + int(r[k])
    old['usd'] = '%.2f' % (float(old['usd']) + float(r['usd']))
    old['inizio'], old['fine'] = min(old['inizio'], r['inizio']), max(old['fine'], r['fine'])
    old['modelli'] = '+'.join(sorted(set(old['modelli'].split('+')) | set(r['modelli'].split('+'))))
    f = {old['output_fonte'], r['output_fonte']}
    old['output_fonte'] = 'stimato' if 'stimato' in f else 'calibrato' if 'calibrato' in f else 'esatto'


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
    g = defaultdict(lambda: dict(n=0, calls=0, tok=0, out=0, usd=0.0, last='', agents=set()))
    for r in rows:
        e = g[key(r)]
        e['n'] += 1
        e['calls'] += int(r['chiamate'])
        e['tok'] += tok(r)
        e['out'] += int(r['output'])
        e['usd'] += float(r['usd'])
        e['last'] = max(e['last'], r['fine'])
        if r['agente']:
            e['agents'].add(r['agente'])
    for e in g.values():
        e['agents'] = len(e['agents'])
    return g


def per_1000(usd, lines):
    return it(1000 * usd / lines) if lines else '—'


def wave_table(rows, pur, tot):
    """One row per wave: the agents of its modules (coordinators, sub-agents, resumes, merges). Main sessions and
    purposes without a wave in purposes.json 'onde' go to the last row. Lines come from purposes.json 'righe'
    (written after each merge); a wave gets its $ per 1000 lines only when all its modules are merged."""
    onde, righe = pur.get('onde', {}), pur.get('righe', {})
    g = group(rows, lambda r: onde.get(r['scopo'], ''))
    scopi = defaultdict(set)
    for r in rows:
        scopi[onde.get(r['scopo'], '')].add(r['scopo'])
    body = []
    for w, e in sorted(g.items(), key=lambda x: (x[0] == '', int(x[0]) if x[0].isdigit() else 0)):
        done = w != '' and all(s in righe for s in scopi[w])
        lines = sum(righe[s] for s in scopi[w]) if done else 0
        name = 'Fuori onda' if not w else 'Fondazione' if w == '0' else 'Onda ' + w
        body.append([name, str(len(scopi[w])) if w else '—', str(e['agents']), it(e['calls'], 0),
                     it(e['tok'] / 1e6), it(e['tok'] / 1e3 / e['calls'], 0) if e['calls'] else '—',
                     it(e['usd'] / e['calls'], 3) if e['calls'] else '—', it(e['usd']), it(100 * e['usd'] / tot),
                     it(lines, 0) if done else '—' if not w else 'in corso', per_1000(e['usd'], lines),
                     it(1000 * e['calls'] / lines, 0) if lines else '—', local_day(e['last'] + 'Z') if e['last'] else ''])
    return org_table(['Onda', 'Moduli', 'Agenti', 'Chiamate', 'Token (M)', 'k token / chiamata', '$ / chiamata', '$',
                      '% costo', 'Righe', '$ / 1000 righe', 'Chiamate / 1000 righe', 'Ultima attività'], body)


def summary_tables(rows, pur, days=None):
    if days:
        cut = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=days)).strftime('%Y-%m-%dT%H:%M')
        rows = [r for r in rows if r['fine'] >= cut]
    tot = sum(float(r['usd']) for r in rows) or 1.0
    onde, righe = pur.get('onde', {}), pur.get('righe', {})
    def table(g, title, by_purpose=False):
        body = []
        for k, e in sorted(g.items(), key=lambda x: -x[1]['usd']):
            row = [k, str(e['agents']), it(e['tok'] / 1e6), it(e['out'] / 1e3, 0), it(e['usd']), it(100 * e['usd'] / tot)]
            if by_purpose:
                row += [onde.get(k, '—'), it(righe[k], 0) if k in righe else '—', per_1000(e['usd'], righe.get(k))]
            body.append(row + [local_day(e['last'] + 'Z') if e['last'] else ''])
        head = [title, 'Agenti', 'Token (M)', 'Output (k)', '$', '% costo']
        return org_table(head + (['Onda', 'Righe', '$ / 1000 righe'] if by_purpose else []) + ['Ultima attività'], body)
    return (rows, tot, table(group(rows, lambda r: r['area']), 'Area'),
            table(group(rows, lambda r: r['scopo']), 'Scopo', True), wave_table(rows, pur, tot))


def write_summary(rows, counters, ratio, pur):
    rows = list(rows.values())
    rows, tot, by_area, by_scopo, by_onda = summary_tables(rows, pur)
    last = max((r['fine'] for r in rows), default='')
    days = group(rows, lambda r: r['giorno'])
    day_rows = [[d, it(e['tok'] / 1e6), it(e['usd']), str(e['agents'])] for d, e in sorted(days.items())]
    recent = sorted(rows, key=lambda r: (r['fine'], r['chiave']), reverse=True)[:20]
    recent_rows = [[r['fine'][:16].replace('T', ' '), r['tipo'], r['scopo'], (r['descrizione'] or '')[:50],
                    it(tok(r) / 1e6), r['usd'], r['output_fonte']] for r in recent]
    todo = group([r for r in rows if r['area'] == UNCLASSIFIED], lambda r: r['scopo'])
    fonti = Counter(r['output_fonte'] for r in rows)
    unpriced = sorted({m for r in rows for m in r['modelli'].split('+') if m and 'claude-' + m not in PRICES})
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
           '']
    if unpriced:
        out += ['*Modelli senza prezzo* in ~ledger.py~ (contati al prezzo di Opus 5.5, da aggiungere a ~PRICES~): %s.'
                % ', '.join(unpriced), '']
    out += ['* Per area', by_area, '',
           '* Per onda',
           'Il lavoro degli agenti di ogni onda (coordinatori, sotto-agenti, riprese, merge). "Fuori onda": sessioni',
           'principali (orchestratore, documentazione) e pianificazione. Una chiamata è un passo di un agente (leggere un',
           'file, lanciare un test, scrivere codice); "k token / chiamata" è il contesto medio riletto a ogni passo.',
           'Righe = righe aggiunte in ~src/~, ~tests/~, ~tools/~, ~data/~ dal merge del modulo (tabella ~righe~ di',
           '~purposes.json~); "in corso" = almeno un modulo dell\'onda non è ancora fuso in ~main~. Lettura dei numeri:',
           'capitolo "Costi" di [[file:../LESSONS.org][LESSONS.org]].',
           '', by_onda, '',
           '* Per scopo', by_scopo, '',
           '* Per giorno', org_table(['Giorno', 'Token (M)', '$', 'Agenti'], day_rows), '',
           '* Ultime attività', org_table(['Fine (UTC)', 'Tipo', 'Scopo', 'Descrizione', 'Token (M)', '$', 'Output'],
                                         recent_rows), '']
    if todo:
        out += ['* Da classificare',
                'Scopi senza area: aggiungerli alla tabella ~aree~ di ~tools/token-ledger/purposes.json~ (e a ~onde~; a',
                '~righe~ dopo il merge), poi ~ledger.py update~. Un agente senza scopo si etichetta con',
                '~ledger.py tag ID SCOPO --area AREA~; ai prossimi dare una descrizione che inizi con ~[scopo]~.', '']
        out += ['- %s: %d agenti, %s $' % (s, e['agents'], it(e['usd'])) for s, e in sorted(todo.items())] + ['']
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
    tags = read_tags()
    for k, r in old.items():  # rows whose transcript is gone stay in the ledger
        if (r['sessione'], r['agente']) not in live:
            keep_row(rows, r, tags, pur)
    os.makedirs(OUT_DIR, exist_ok=True)
    write_ledger(rows)
    write_summary(read_ledger(), counters, ratio, pur)
    if not quiet:
        print('%d righe, %d trascrizioni lette -> %s' % (len(rows), len(parsed), os.path.relpath(LEDGER, REPO)))


def report(days):
    rows = list(read_ledger().values())
    if not rows:
        print('Registro vuoto: eseguire prima "ledger.py update".')
        return
    rows, tot, by_area, by_scopo, by_onda = summary_tables(rows, load_purposes(), days)
    print('Periodo: %s. Totale %s M token, %s $ (equivalente API).\n' % (
        'ultimi %d giorni' % days if days else 'tutto', it(sum(tok(r) for r in rows) / 1e6), it(tot)))
    print(by_area + '\n\n' + by_onda + '\n\n' + by_scopo)


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
        pur = load_purposes()
        if area and not valid_area(area, pur):
            print('Area sconosciuta: "%s". Aree: %s.' % (area, ', '.join(pur.get('aree_valide', []))))
            return 2
        area = valid_area(area, pur)
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
