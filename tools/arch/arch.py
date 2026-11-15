#!/usr/bin/env python3
"""Keeps the architecture docs and the code in step (docs/ARCHITECTURE.md "Keeping these docs true").

Usage: python3 -I tools/arch/arch.py <command>
  check                    the checks on the working tree (also ArchitectureDocsTests); exit 1 = problems
  sync                     rewrite the generated parts: docs/architecture/index/*.txt, the project list
  where <file>...          the area of a source file and the docs that name it
  review [base] [head]     a branch's code changes against its docs (default: main HEAD); exit 1 = undeclared
  hook commit-msg <file>   the git commit-msg hook (tools/git-hooks/commit-msg)
  hook pretool             Claude Code PreToolUse hook: refuses git commands that skip the hooks
  install                  point this repository's git at tools/git-hooks

Standard library only, no build needed. The repository is the one this file lives in (a worktree checks itself).
"""
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MAP = 'docs/ARCHITECTURE.md'
AREA_DIR = 'docs/architecture'
INDEX_DIR = AREA_DIR + '/index'
UNDOC = INDEX_DIR + '/undocumented.txt'
NOT_CODE = INDEX_DIR + '/not-code.txt'
CODE_ROOTS = ('src', 'tools/Rts.Bench')      # the source files that must belong to an area
IDENT_ROOTS = ('src', 'tools', 'tests')      # where an identifier named by the docs may live (.cs)
SKIP_DIRS = {'bin', 'obj', '.git', '.claude', '__pycache__'}
TOP_NAMES = {'src', 'tools', 'tests', 'data', 'docs', 'Rts.Sim', 'Rts.Client', 'Rts.Net', 'Rts.Bench'}
MAIN = 'main'
HELP = 'See docs/ARCHITECTURE.md "Keeping these docs true".'


# ---------------------------------------------------------------------------------------------------- files

def read(rel):
    return (ROOT / rel).read_text(encoding='utf-8')


def walk(top, exts):
    out = []
    for dp, dn, fn in os.walk(ROOT / top):
        dn[:] = sorted(d for d in dn if d not in SKIP_DIRS)
        for f in sorted(fn):
            if f.endswith(exts):
                out.append(os.path.relpath(os.path.join(dp, f), ROOT).replace(os.sep, '/'))
    return out


def git(*args, ok=(0,)):
    r = subprocess.run(['git', *args], cwd=ROOT, capture_output=True, text=True)
    if r.returncode not in ok:
        raise RuntimeError('git %s: %s' % (' '.join(args), r.stderr.strip()))
    return r.stdout


def repo_paths():
    """Every file of the repository that is not ignored (tracked or new)."""
    try:
        return [p for p in git('ls-files', '-co', '--exclude-standard').split('\n') if p and (ROOT / p).exists()]
    except (RuntimeError, OSError):
        return walk('.', ('',))


def source_files():
    out = []
    for top in CODE_ROOTS:
        out.extend(walk(top, ('.cs',)))
    return sorted(out)


def stem(path):
    return os.path.basename(path)[:-3]


# ---------------------------------------------------------------------------------------------------- the map

def block(text, name):
    m = re.search(r'<!-- arch:%s -->\n(.*?)<!-- /arch:%s -->' % (name, name), text, re.S)
    return m.group(1) if m else None


class Area:
    def __init__(self, ident, doc, patterns):
        self.id, self.doc, self.patterns = ident, doc, patterns


def load_areas(map_text):
    areas = []
    for line in (block(map_text, 'areas') or '').splitlines():
        m = re.match(r'\|\s*\[[^\]]+\]\((architecture/([\w\-]+)\.md)\)\s*\|([^|]*)\|', line)
        if m:
            areas.append(Area(m.group(2), 'docs/' + m.group(1), re.findall(r'`([^`]+)`', m.group(3))))
    return areas


def pattern_rx(pat):
    if pat.endswith('/'):
        return re.compile(re.escape(pat) + '.*')
    return re.compile('[^/]*'.join(re.escape(x) for x in pat.split('*')))


class Owners:
    """Which area a source path belongs to: the most specific matching pattern of the areas table."""

    def __init__(self, areas):
        self.rules = [(len(p.replace('*', '')), pattern_rx(p), p, a) for a in areas for p in a.patterns]

    def of(self, path):
        """(area or None, tie) where tie = two areas claim the path with equally specific patterns."""
        hits = sorted(((s, a) for s, rx, _, a in self.rules if rx.fullmatch(path)), key=lambda h: -h[0])
        if not hits:
            return None, False
        best = [a for s, a in hits if s == hits[0][0]]
        return best[0], len({a.id for a in best}) > 1

    def unused(self, files):
        return [(a.id, p) for _, rx, p, a in self.rules if not any(rx.fullmatch(f) for f in files)]


# ---------------------------------------------------------------------------------------------------- code facts

TYPE_RX = re.compile(
    r'^([ \t]*)(?:\[[^\]\n]*\][ \t]*)*'
    r'(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|ref|unsafe|file|new)[ \t]+)*'
    r'(class|struct|interface|enum|record(?:[ \t]+(?:struct|class))?)[ \t]+([A-Za-z_]\w*)', re.M)


def enum_values(text, pos):
    i = text.find('{', pos)
    j = text.find('}', i)
    if i < 0 or j < 0:
        return []
    body = re.sub(r'/\*.*?\*/', '', text[i + 1:j], flags=re.S)
    body = re.sub(r'//[^\n]*', '', body)
    body = re.sub(r'\[[^\]]*\]', '', body)
    return [m.group(1) for m in (re.match(r'\s*([A-Za-z_]\w*)', part) for part in body.split(',')) if m]


def top_types(text):
    """Top-level type declarations of a C# file, in order; an enum comes with its values."""
    found = [(len(m.group(1).expandtabs(4)), m) for m in TYPE_RX.finditer(text)]
    if not found:
        return []
    top = min(indent for indent, _ in found)
    out = []
    for indent, m in found:
        if indent != top:
            continue
        name = m.group(3)
        if m.group(2) == 'enum':
            name += '{' + ' '.join(enum_values(text, m.end())) + '}'
        if name not in out:
            out.append(name)
    return out


def summary(text):
    """First sentence of the doc comment above a file's first top-level type ('' if it has none)."""
    found = [(len(m.group(1).expandtabs(4)), m.start()) for m in TYPE_RX.finditer(text)]
    head = text[:min((pos for indent, pos in found if indent == min(i for i, _ in found)), default=0)] if found else text
    blocks = re.findall(r'///[ \t]*<summary>(.*?)</summary>', head, re.S)
    if not blocks:
        return ''
    s = re.sub(r'\n[ \t]*///', ' ', blocks[-1])
    s = re.sub(r'<see(?:also)? cref="([^"]+)"[ \t]*/>', r'\1', s)
    s = re.sub(r'<[^>]+>', '', s)
    for a, b in (('&amp;', '&'), ('&lt;', '<'), ('&gt;', '>'), ('&quot;', '"')):
        s = s.replace(a, b)
    s = re.split(r'(?<=[.:;])\s', ' '.join(s.split()), maxsplit=1)[0]
    return s if len(s) <= 170 else s[:167].rstrip() + '...'


def index_text(area, files):
    lines = ['# Generated from the code by `python3 -I tools/arch/arch.py sync`. Do not edit.',
             '# Area %s: each source file, its top-level types (an enum with its values) and the first sentence' % area.id,
             '# of its doc comment.']
    for f in files:
        text = read(f)
        what = summary(text)
        lines.append('%s: %s%s' % (f, ', '.join(top_types(text)), ' -- ' + what if what else ''))
    return '\n'.join(lines) + '\n'


def projects_text():
    lines = []
    for top in ('src', 'tools'):
        for proj in walk(top, ('.csproj',)):
            text = read(proj)
            refs = [os.path.basename(r.replace('\\', '/'))[:-7] for r in re.findall(r'<ProjectReference Include="([^"]+)"', text)]
            pkgs = re.findall(r'<PackageReference Include="([^"]+)"', text)
            line = '- `%s` → %s' % (os.path.dirname(proj), ', '.join(refs) if refs else 'no project')
            if pkgs:
                line += '; packages: ' + ', '.join(pkgs)
            lines.append(line)
    return '\n'.join(lines) + '\n'


def code_pipeline():
    """The calls of Simulation.Step, in order, as the keys of the map's pipeline table."""
    m = re.search(r'public void Step\(IReadOnlyList<Command> commands\)\s*\{(.*?)\n    \}', read('src/Rts.Sim/Simulation.cs'), re.S)
    if not m:
        return None
    keys = []
    for line in m.group(1).splitlines():
        c = re.match(r'([A-Za-z_][\w.]*)\(.*\);', line.strip())
        if not c:
            continue
        parts = c.group(1).split('.')
        if parts[0] in ('pf', 'Stopwatch'):
            continue
        keys.append(c.group(1) if parts[0] == 'w' else '.'.join(parts[-2:]))
    return keys


def doc_pipeline(map_text):
    keys = []
    for line in (block(map_text, 'pipeline') or '').splitlines():
        m = re.match(r'\|\s*`([^`]+)`', line)
        if m:
            keys.append(m.group(1))
    return keys


# ---------------------------------------------------------------------------------------------------- doc facts

INLINE_RX = re.compile(r'`([^`\n]+)`')
NAME_RX = re.compile(r'[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*')
WORD_RX = re.compile(r'[A-Za-z_]\w*')
PATHISH_RX = re.compile(r'[\w.*/<>{},\-]+')
FILE_EXT_RX = re.compile(r'\.(cs|json|py|sh|md|org|txt|csproj|slnx)$')


def arch_docs():
    return [MAP] + sorted(p for p in walk(AREA_DIR, ('.md',)) if '/index/' not in p)


def without_fences(text):
    return re.sub(r'^```.*?^```', lambda m: '\n' * m.group(0).count('\n'), text, flags=re.S | re.M)


def names_in(text):
    """Every dotted name of a text with its prefixes: `UnitRenderer.Cargo.cs` gives UnitRenderer, UnitRenderer.Cargo."""
    out = set()
    for m in NAME_RX.finditer(text):
        parts = m.group(0).split('.')
        for i in range(1, len(parts) + 1):
            out.add('.'.join(parts[:i]))
    return out


def path_tokens(span):
    """The path(s) an inline code span stands for (braces and <placeholders> expanded), or [] if it is no path."""
    s = span.strip().rstrip('.,;:')
    if s.startswith('-') or not PATHISH_RX.fullmatch(s):
        return []
    if '/' not in s and not FILE_EXT_RX.search(s):
        return []
    s = re.sub(r'<[^<>]*>', '*', s)
    m = re.fullmatch(r'(.*)\{([^{}]*)\}(.*)', s)
    return [m.group(1) + x + m.group(3) for x in m.group(2).split(',')] if m else [s]


def glob_rx(tok):
    return re.compile(r'(?:.*/)?' + '[^/]*'.join(re.escape(x) for x in tok.split('*')))


class Tree:
    """The repository's files and folders, for resolving what the docs cite."""

    def __init__(self):
        self.files = repo_paths()
        self.dirs = set()
        for p in self.files:
            while '/' in p:
                p = p.rsplit('/', 1)[0]
                self.dirs.add(p)
        self.src_dirs = {d.split('/')[2] for d in self.dirs if d.startswith('src/') and d.count('/') >= 2}

    def is_code_path(self, tok):
        first = tok.split('/')[0]
        return first in TOP_NAMES or first in self.src_dirs or ('/' not in tok and tok.endswith('.cs'))

    def files_matching(self, tok, among=None):
        """Files a cited path stands for: the path as a suffix, with `*`, with `.cs` added, or as `<X>.<name>.cs`."""
        among = self.files if among is None else among
        t = tok[2:] if tok.startswith('./') else tok
        if t.endswith('/'):
            return []
        for cand in (t, t + '.cs', t + '*.cs' if t.endswith('*') else None):
            if cand:
                rx = glob_rx(cand)
                hit = [p for p in among if rx.fullmatch(p)]
                if hit:
                    return hit
        if '/' not in t and t.endswith('.cs'):
            rx = re.compile(r'(?:.*/)?[^/]*\.' + '[^/]*'.join(re.escape(x) for x in t.split('*')))
            return [p for p in among if rx.fullmatch(p)]
        folder, _, last = t.rpartition('/')
        if folder and '.' in last and not FILE_EXT_RX.search(last):      # `Input/PlacementLine.Layout`: a member
            rx = glob_rx(folder + '/' + last.split('.')[0] + '.cs')
            return [p for p in among if rx.fullmatch(p)]
        return []

    def exists(self, tok):
        if self.files_matching(tok):
            return True
        rx = glob_rx((tok[2:] if tok.startswith('./') else tok).rstrip('/'))
        return any(rx.fullmatch(d) for d in self.dirs)


class Docs:
    """What the architecture docs say: the names they use and the files they cite."""

    def __init__(self, tree, sources, loader=read, only=None):
        self.tree, self.sources = tree, sources
        self.text = {}
        for d in (arch_docs() if only is None else only):
            try:
                self.text[d] = loader(d)
            except (OSError, RuntimeError):
                pass
        self.names = {d: names_in(t) for d, t in self.text.items()}
        self.cited = {}       # doc -> source files matched by the paths it cites
        self.spans = {}       # doc -> [(line number, inline code span)]
        for d, t in self.text.items():
            files, spans = set(), []
            for n, line in enumerate(without_fences(t).split('\n'), 1):
                for m in INLINE_RX.finditer(line):
                    spans.append((n, m.group(1)))
                    for tok in path_tokens(m.group(1)):
                        if tree.is_code_path(tok):
                            files.update(tree.files_matching(tok, sources))
            self.cited[d], self.spans[d] = files, spans

    def naming(self, path):
        """The docs that name a source file: by its file name (`Walls.cs`, `Walls`) or by a path pattern."""
        s = stem(path)
        return [d for d in self.text if s in self.names[d] or path in self.cited[d]]


def code_identifiers():
    words = set()
    for top in IDENT_ROOTS:
        for f in walk(top, ('.cs',)):
            words.update(WORD_RX.findall(read(f)))
    for top, ext in (('data', '.json'), ('src', '.csproj'), ('tools', '.csproj'), ('tests', '.csproj')):
        for f in walk(top, (ext,)):
            words.update(WORD_RX.findall(read(f)))
    return words


def looks_like_code(word):
    return bool(re.search(r'[a-z0-9][A-Z]', word) or re.match(r'[A-Z]{2,}[a-z]', word))


def list_file(rel):
    try:
        return [x.strip() for x in read(rel).split('\n') if x.strip() and not x.startswith('#')]
    except OSError:
        return []


# ---------------------------------------------------------------------------------------------------- check

class State:
    def __init__(self):
        self.map_text = read(MAP)
        self.areas = load_areas(self.map_text)
        self.owners = Owners(self.areas)
        self.sources = source_files()
        self.tree = Tree()
        self.docs = Docs(self.tree, self.sources)

    def files_of(self, area):
        return [f for f in self.sources if self.owners.of(f)[0] is area]

    def undocumented(self):
        return [f for f in self.sources if not self.docs.naming(f)]


def check(st=None):
    """Returns the list of problems (empty = the docs and the code agree)."""
    st = st or State()
    out = []
    if not st.areas:
        return ['%s: the areas table (between <!-- arch:areas --> markers) is missing or empty.' % MAP]

    # 1. areas: every doc exists, every source file has exactly one owner, no dead pattern
    listed = {a.doc for a in st.areas}
    for a in st.areas:
        if not (ROOT / a.doc).exists():
            out.append('%s: the areas table lists %s, which does not exist.' % (MAP, a.doc))
    for d in arch_docs():
        if d != MAP and d not in listed:
            out.append('%s is not in the areas table of %s: add its row.' % (d, MAP))
    for f in st.sources:
        area, tie = st.owners.of(f)
        if area is None:
            out.append('%s belongs to no area: add a pattern for it to a row of the areas table in %s '
                       '(or a new area file + row).' % (f, MAP))
        elif tie:
            out.append('%s is claimed by two areas with equally specific patterns: make one more specific.' % f)
    for area_id, pat in st.owners.unused(st.sources):
        out.append('%s: pattern `%s` of area %s matches no file: fix or remove it.' % (MAP, pat, area_id))

    # 2. every source file is named in some architecture doc
    undoc = set(st.undocumented())
    baseline = set(list_file(UNDOC))
    for f in sorted(undoc - baseline):
        area = st.owners.of(f)[0]
        out.append('%s is not described in the architecture docs: name it in %s and say what it holds.'
                   % (f, area.doc if area else 'its area file'))
    if baseline - undoc:
        out.append('%s lists files that are documented now or gone (%s): run `python3 -I tools/arch/arch.py sync`.'
                   % (UNDOC, ', '.join(sorted(baseline - undoc)[:4])))

    # 3. what the docs cite exists
    words = code_identifiers()
    not_code = set(list_file(NOT_CODE))
    for d in st.docs.text:
        seen = set()
        for n, span in st.docs.spans[d]:
            toks = [t for t in path_tokens(span) if st.tree.is_code_path(t)]
            if toks:
                for t in toks:
                    if not st.tree.exists(t) and t not in seen:
                        seen.add(t)
                        out.append('%s:%d cites `%s`, which does not exist: fix the path or the text.' % (d, n, t))
                continue
            if span.lstrip().startswith('-'):
                continue
            for w in WORD_RX.findall(span):
                if looks_like_code(w) and w not in words and w not in not_code and w not in seen:
                    seen.add(w)
                    out.append('%s:%d names `%s`, which is nowhere in the code or data: update the text '
                               '(a word that is not code goes in %s).' % (d, n, w, NOT_CODE))

    # 4. the pipeline table = Simulation.Step
    code, doc = code_pipeline(), doc_pipeline(st.map_text)
    if code is None:
        out.append('tools/arch/arch.py cannot find Simulation.Step in src/Rts.Sim/Simulation.cs: update code_pipeline().')
    elif code != doc:
        missing = [k for k in code if k not in doc]
        extra = [k for k in doc if k not in code]
        detail = ('; '.join(x for x in ('not in the table: ' + ', '.join(missing) if missing else '',
                                       'not in the code: ' + ', '.join(extra) if extra else '') if x)
                  or 'same calls, another order')
        out.append('%s: the pipeline table differs from Simulation.Step (%s). The table has one row per call, in '
                   'the code\'s order: %s' % (MAP, detail, ' > '.join(code)))

    # 5. generated parts are current
    stale = []
    for a in st.areas:
        rel = '%s/%s.txt' % (INDEX_DIR, a.id)
        if not (ROOT / rel).exists() or read(rel) != index_text(a, st.files_of(a)):
            stale.append(rel)
    if block(st.map_text, 'projects') != projects_text():
        stale.append(MAP + ' (project list)')
    if stale:
        out.append('Generated parts are stale (%s): run `python3 -I tools/arch/arch.py sync` and commit the result.'
                   % ', '.join(stale[:6]))
    return out


def sync(init_undocumented=False):
    st = State()
    os.makedirs(ROOT / INDEX_DIR, exist_ok=True)
    keep = {'undocumented.txt', 'not-code.txt'}
    for a in st.areas:
        keep.add(a.id + '.txt')
        (ROOT / INDEX_DIR / (a.id + '.txt')).write_text(index_text(a, st.files_of(a)), encoding='utf-8')
    for f in os.listdir(ROOT / INDEX_DIR):
        if f not in keep:
            os.remove(ROOT / INDEX_DIR / f)
    text = st.map_text
    if block(text, 'projects') is not None:
        text = re.sub(r'(<!-- arch:projects -->\n).*?(<!-- /arch:projects -->)',
                      lambda m: m.group(1) + projects_text() + m.group(2), text, flags=re.S)
        if text != st.map_text:
            (ROOT / MAP).write_text(text, encoding='utf-8')
    undoc = st.undocumented()
    if not init_undocumented:      # the list may only shrink: sync never adds a file to it
        old = set(list_file(UNDOC))
        undoc = [f for f in undoc if f in old]
    head = ['# Source files no architecture doc named when the gate was introduced (2026-11-15).',
            '# This list may only shrink: describe a file in its area doc, then run `arch.py sync`.',
            '# A new file never goes here (arch.py review refuses a branch that adds lines).']
    (ROOT / UNDOC).write_text('\n'.join(head + undoc) + '\n', encoding='utf-8')
    if not (ROOT / NOT_CODE).exists():
        (ROOT / NOT_CODE).write_text('# Words the architecture docs write in `code style` that are not identifiers '
                                     'of the code (one per line).\n', encoding='utf-8')
    print('sync: %d areas, %d source files, %d undocumented' % (len(st.areas), len(st.sources), len(undoc)))


def where(paths):
    st = State()
    for p in paths:
        p = os.path.relpath(os.path.abspath(p), ROOT).replace(os.sep, '/')
        area, _ = st.owners.of(p)
        named = [d for d in st.docs.naming(p) if not area or d != area.doc]
        print('%s\n  area: %s\n  also named in: %s' % (p, '%s (%s)' % (area.id, area.doc) if area else 'NONE',
                                                       ', '.join(named) or '-'))


# ---------------------------------------------------------------------------------------------------- the gate

ARCH_LINE_RX = re.compile(r'^Arch:[ \t]*(.+?)[ \t]+[-–—][ \t]+(\S.*)$', re.M)


def arch_lines(message, areas, problems):
    """`Arch: area, area - reason` lines of commit messages: {area id: reason}."""
    known = {a.id for a in areas}
    out = {}
    for m in ARCH_LINE_RX.finditer(message):
        reason = m.group(2).strip()
        for ident in re.split(r'[,\s]+', m.group(1).strip()):
            if ident not in known:
                problems.append('`Arch:` line names "%s", which is not an area (areas: %s).' % (ident, ', '.join(sorted(known))))
            elif len(reason) < 15:
                problems.append('`Arch: %s` needs a real reason (why the docs still hold), not "%s".' % (ident, reason))
            else:
                out[ident] = reason
    return out


def changed(diff_args):
    """(paths of changed files, deleted included) from a `git diff --name-status` call."""
    out = []
    for line in git('diff', '--name-status', '-M', *diff_args).split('\n'):
        f = line.split('\t')
        if len(f) >= 2:
            out.append(f[-1])
            if f[0].startswith('R'):
                out.append(f[1])
    return out


def real_changes(diff_args, docs):
    """The docs whose change in that diff is more than whitespace or blank lines (a blank line added to a doc
    does not describe anything)."""
    out = []
    for p in docs:
        d = git('diff', '-w', '--ignore-blank-lines', '--unified=0', *diff_args, '--', p)
        if any(l[:1] in '+-' and not l.startswith(('+++', '---')) for l in d.split('\n') if l):
            out.append(p)
    return out


def is_source(path):
    return path.endswith('.cs') and any(path.startswith(r + '/') for r in CODE_ROOTS)


def is_arch_doc(path):
    return path == MAP or (path.startswith(AREA_DIR + '/') and path.endswith('.md'))


def gate(code_paths, doc_paths, message, loader=read):
    """Returns (problems, report lines): every changed source file needs a changed doc that describes it
    (its area doc or a doc naming it) or an `Arch:` line for its area."""
    areas = load_areas(loader(MAP))
    owners = Owners(areas)
    problems = []
    declared = arch_lines(message, areas, problems)
    docs = Docs(Tree(), source_files(), loader, only=sorted(set(doc_paths)))
    by_area, report = {}, []
    for p in sorted(set(code_paths)):
        area, _ = owners.of(p)
        key = area.id if area else '(no area)'
        entry = by_area.setdefault(key, {'area': area, 'files': [], 'open': [], 'docs': set()})
        entry['files'].append(p)
        covering = [d for d in docs.text if (area and d == area.doc) or stem(p) in docs.names[d] or p in docs.cited[d]]
        entry['docs'].update(covering)
        if not covering and key not in declared:
            entry['open'].append(p)
    for key in sorted(by_area):
        e = by_area[key]
        how = ('docs changed: ' + ', '.join(sorted(os.path.basename(d) for d in e['docs']))) if e['docs'] else ''
        if key in declared:
            how = (how + '; ' if how else '') + 'Arch: ' + declared[key]
        report.append('  %-13s %2d file(s)  %s' % (key, len(e['files']), how or 'NOTHING'))
        if e['open']:
            doc = e['area'].doc if e['area'] else 'an area doc (the file has no area yet)'
            problems.append('area %s: %s changed, but neither %s nor any doc naming %s did, and the message has no '
                            '`Arch: %s - <why the docs still hold>` line.'
                            % (key, ', '.join(e['open'][:5]) + (' …' if len(e['open']) > 5 else ''), doc,
                               'it' if len(e['open']) == 1 else 'them', key))
    return problems, report


def undocumented_growth(base_text, head_text):
    lines = lambda t: {x.strip() for x in t.split('\n') if x.strip() and not x.startswith('#')}
    return sorted(lines(head_text) - lines(base_text))


def show(rev, rel):
    return git('show', '%s:%s' % (rev, rel))


def review(base, head):
    """A branch as a whole: what `git merge --no-ff <head>` into <base> would bring."""
    mb = git('merge-base', base, head).strip()
    paths = changed([mb, head])
    message = git('log', '--format=%B', '%s..%s' % (mb, head))
    loader = read if head == 'HEAD' and not git('status', '--porcelain', '--', 'docs').strip() else (lambda rel: show(head, rel))
    problems, report = gate([p for p in paths if is_source(p)],
                            real_changes([mb, head], [p for p in paths if is_arch_doc(p)]), message, loader)
    try:
        grown = undocumented_growth(show(mb, UNDOC), loader(UNDOC))
        if grown:
            problems.append('%s grew (%s): that list may only shrink; describe the files instead.' % (UNDOC, ', '.join(grown[:4])))
    except (RuntimeError, OSError):
        pass
    print('arch review %s..%s (%d source files changed)' % (mb[:8], head, len([p for p in paths if is_source(p)])))
    print('\n'.join(report) if report else '  no source file changed')
    return problems


def fail(title, problems):
    sys.stderr.write('\n%s\n' % title)
    for p in problems:
        sys.stderr.write('  - %s\n' % p)
    sys.stderr.write(HELP + '\n\n')
    return 1


def hook_commit_msg(msg_file):
    message = '\n'.join(l for l in Path(msg_file).read_text(encoding='utf-8', errors='replace').split('\n')
                        if not l.startswith('#'))
    if message.strip().upper().startswith('WIP'):
        return 0                    # work in progress: checked when the branch is merged
    branch = git('rev-parse', '--abbrev-ref', 'HEAD').strip()
    has_main = git('rev-parse', '-q', '--verify', MAIN, ok=(0, 1)).strip() != ''
    merge_head = git('rev-parse', '-q', '--verify', 'MERGE_HEAD', ok=(0, 1)).strip()
    problems = []
    if merge_head:
        if branch != MAIN and has_main and subprocess.run(
                ['git', 'merge-base', '--is-ancestor', merge_head, MAIN], cwd=ROOT).returncode == 0:
            return 0                # taking main into a branch: those commits were checked on main
        mb = git('merge-base', 'HEAD', merge_head).strip()
        paths = changed([mb, merge_head])
        doc_paths = real_changes([mb, merge_head], [p for p in paths if is_arch_doc(p)])
        message += '\n' + git('log', '--format=%B', '%s..%s' % (mb, merge_head))
        title = 'arch gate: the branch being merged changes code without its architecture docs.'
        try:
            grown = undocumented_growth(show(mb, UNDOC), read(UNDOC))
            if grown:
                problems.append('%s grew (%s): that list may only shrink.' % (UNDOC, ', '.join(grown[:4])))
        except (RuntimeError, OSError):
            pass
    else:
        paths = changed(['--cached'])
        doc_paths = real_changes(['--cached'], [p for p in paths if is_arch_doc(p)])
        if branch != MAIN and has_main:      # docs and Arch: lines of earlier commits of this branch count
            mb = git('merge-base', 'HEAD', MAIN).strip()
            doc_paths += real_changes([mb, 'HEAD'], [p for p in changed([mb, 'HEAD']) if is_arch_doc(p)])
            message += '\n' + git('log', '--format=%B', '%s..HEAD' % mb)
        title = 'arch gate: this commit changes code without its architecture docs.'
    code = [p for p in paths if is_source(p)]
    if not code and not merge_head:
        return 0
    more, _ = gate(code, doc_paths, message)
    problems += more
    if problems:
        return fail(title, problems + [
            'Fix: change the doc that describes the code (and `git add` it), or add to the commit message one line '
            'per area: `Arch: <area> - <why the docs still hold>`. `python3 -I tools/arch/arch.py where <file>` '
            'shows a file\'s docs.'])
    if branch == MAIN or merge_head:         # main must also pass the checks on the files
        tree_problems = check()
        if tree_problems:
            return fail('arch gate: the architecture docs do not match the code (same checks as ArchitectureDocsTests).',
                        tree_problems)
    return 0


def hook_pretool():
    """Claude Code PreToolUse (Bash): refuse git commands that would skip the commit gate."""
    try:
        cmd = json.load(sys.stdin).get('tool_input', {}).get('command', '') or ''
    except (ValueError, AttributeError):
        return 0
    if not re.search(r'\bgit\b', cmd):
        return 0
    why = None
    if re.search(r'\bgit\b[^|;&\n]*\s(commit|merge|rebase|cherry-pick|am|revert|pull)\b[^\n]*--no-verify\b', cmd):
        why = '--no-verify skips the architecture docs gate'
    else:
        m = re.search(r'\bgit\b[^|;&\n"\']*\scommit\b([^|;&\n"\'<]*)', cmd)
        if m and re.search(r'\s-[a-zA-Z]*n[a-zA-Z]*(\s|$)', m.group(1).split(' -m')[0] + ' '):
            why = '`git commit -n` skips the architecture docs gate'
        elif re.search(r'core\.hooksPath', cmd) and 'tools/git-hooks' not in cmd and not re.search(r'--get\b', cmd):
            why = 'core.hooksPath must stay tools/git-hooks (the architecture docs gate)'
    if why:
        sys.stderr.write('Refused: %s. Update the architecture docs or add an `Arch: <area> - <reason>` line to the '
                         'commit message; a commit that starts with WIP is not checked. %s\n' % (why, HELP))
        return 2
    return 0


def install(quiet=False):
    if git('config', '--get', 'core.hooksPath', ok=(0, 1)).strip() != 'tools/git-hooks':
        git('config', 'core.hooksPath', 'tools/git-hooks')
        if not quiet:
            print('git core.hooksPath = tools/git-hooks')
    return 0


def main(argv):
    cmd = argv[1] if len(argv) > 1 else ''
    if cmd == 'check':
        problems = check()
        if problems:
            print('The architecture docs do not match the code (%d problem%s):' % (len(problems), '' if len(problems) == 1 else 's'))
            for p in problems:
                print('  - ' + p)
            print(HELP)
            return 1
        print('architecture docs: ok')
        return 0
    if cmd == 'sync':
        sync('--init-undocumented' in argv)
        return 0
    if cmd == 'where' and len(argv) > 2:
        where(argv[2:])
        return 0
    if cmd == 'review':
        problems = review(argv[2] if len(argv) > 2 else MAIN, argv[3] if len(argv) > 3 else 'HEAD')
        return fail('arch review: code changed without its architecture docs.', problems) if problems else 0
    if cmd == 'hook' and len(argv) > 3 and argv[2] == 'commit-msg':
        return hook_commit_msg(argv[3])
    if cmd == 'hook' and len(argv) > 2 and argv[2] == 'pretool':
        return hook_pretool()
    if cmd == 'install':
        return install('--quiet' in argv)
    print(__doc__)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv))
