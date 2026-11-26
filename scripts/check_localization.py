#!/usr/bin/env python3
"""Checks LinguaBridge interface translations.

Collects every English UI string (L("..."), LF("..."), label tables, tour texts) and reports the ones missing
from src/LinguaBridge/Localization/<lang>.json, entries no longer used, and placeholders ({0}, {target}) that
differ. Usage: python3 scripts/check_localization.py   (exit code 1 when something needs fixing)
"""
import json
import os
import re
import sys

def scan(src):
    """Yield (start, end, kind) for every literal; kind is 'reg', 'interp', 'verbatim', 'char' or 'comment'."""
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith('//', i):
            j = src.find('\n', i); j = n if j < 0 else j
            yield (i, j, 'comment'); i = j; continue
        if src.startswith('/*', i):
            j = src.find('*/', i) + 2
            yield (i, j, 'comment'); i = j; continue
        if c == "'":
            j = i + 1
            while src[j] != "'":
                j += 2 if src[j] == '\\' else 1
            yield (i, j + 1, 'char'); i = j + 1; continue
        if c in '$@' and i + 1 < n and src[i+1] in '"$@':
            # interpolated / verbatim (with possibly nested braces); skip to matching quote
            k = i
            while src[k] in '$@': k += 1
            verb = '@' in src[i:k]
            j = k + 1; depth = 0
            while True:
                ch = src[j]
                if verb and ch == '"' and src[j+1:j+2] == '"': j += 2; continue
                if not verb and ch == '\\': j += 2; continue
                if ch == '{':
                    if src[j+1:j+2] == '{': j += 2; continue
                    depth += 1
                elif ch == '}':
                    if depth == 0 and src[j+1:j+2] == '}': j += 2; continue
                    depth = max(0, depth - 1)
                elif ch == '"' and depth == 0:
                    break
                elif ch == '"' and depth > 0:
                    # nested string inside interpolation hole
                    j += 1
                    while src[j] != '"':
                        j += 2 if src[j] == '\\' else 1
                j += 1
            yield (i, j + 1, 'interp' if '$' in src[i:k] else 'verbatim'); i = j + 1; continue
        if c == '"':
            j = i + 1
            while src[j] != '"':
                j += 2 if src[j] == '\\' else 1
            yield (i, j + 1, 'reg'); i = j + 1; continue
        i += 1

ESCAPES = {'n': '\n', 'r': '\r', 't': '\t', '0': '\0', '"': '"', "'": "'", '\\': '\\'}

def unescape(lit):
    body = lit[1:-1]
    if '\\' not in body:
        return body
    return re.sub(r'\\(u[0-9a-fA-F]{4}|.)',
                  lambda m: chr(int(m.group(1)[1:], 16)) if len(m.group(1)) == 5 else ESCAPES.get(m.group(1), m.group(1)), body)

def groups(src):
    """Concatenated regular-literal expressions: list of (start, end, value)."""
    toks = [t for t in scan(src)]
    out = []
    k = 0
    while k < len(toks):
        s, e, kind = toks[k]
        if kind != 'reg':
            k += 1; continue
        start, end = s, e
        val = unescape(src[s:e])
        while k + 1 < len(toks):
            ns, ne, nk = toks[k+1]
            between = src[end:ns]
            if nk == 'reg' and re.fullmatch(r'\s*\+\s*', between):
                val += unescape(src[ns:ne]); end = ne; k += 1
            else:
                break
        out.append((start, end, val))
        k += 1
    return out


root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'LinguaBridge')
keys = []
def add(v):
    if v not in keys: keys.append(v)

def file(p): return open(os.path.join(root, p)).read()

SOURCES = sorted(os.path.relpath(os.path.join(d, f), root)
                 for d, _, fs in os.walk(root) if '/bin' not in d and '/obj' not in d
                 for f in fs if f.endswith('.cs'))

# 1. L("...") / LF("...") in every source file
for p in SOURCES:
    src = file(p)
    for s, e, v in groups(src):
        pre = src[max(0, s-4):s]
        if pre.endswith('L(') or pre.endswith('LF('):
            add(v)

# Engine statuses stored in English and shown through L() (DeepL keys, AI providers).
for v in ["not used yet", "OK", "invalid API key", "too many requests", "monthly quota used up", "empty response",
          "network error", "timed out"]: add(v)
for p in ['Translation/LlmTranslator.cs']:
    for m in re.finditer(r'new LlmFailure\(\s*"((?:[^"\\]|\\.)*)"', file(p)):
        add(m.group(1))
# Language names (English, shown through L()).
for m in re.finditer(r'new Language\("[A-Z]{2}",\s*"([^"]+)"', file('Translation/Languages.cs')):
    add(m.group(1))

ID = re.compile(r'^[a-z0-9_.\-]+$')
def ok(v):
    return re.search(r'[A-Za-z]', v) and not ID.match(v) and not v.startswith('/') and not v.startswith('##') \
        and not (' ' not in v and '.' in v)

# 2. static arrays shown through L()
def block(src, name):
    i = src.index(name); j = src.index('};', i)
    return src[i:j]
mw = file('Windows/MainWindow.cs'); sw = file('Windows/SetupWizard.cs'); cw = file('Windows/ConfigWindow.cs')
for src, name, step in [(mw,'SendChannels =',1),(sw,'ChannelPresets =',1),(sw,'StepNames =',1),(cw,'ChannelGroups =',1),(cw,'Formalities =',2),
                        (cw,'OutputChannels =',1)]:
    b = block(src, name)
    vals = [v for _, _, v in groups(b)]
    if step == 2: vals = vals[0::2]
    for v in vals:
        if ok(v) or name == 'StepNames =': add(v)
for src, name in [(sw,'CheatSheet ='),(cw,'Commands =')]:
    vals = [v for _, _, v in groups(block(src, name))]
    for v in vals[1::2]: add(v)
for v in ["unspecified", "male", "female", "non-binary"]: add(v)
for v in ["Engine","Languages","Chat","Roleplay","AI","Glossary","Help"]: add(v)
# history channel / sender names
for v in ["Tell","Tell (out)","Party","FC","Emote","Say","Shout","Yell","Alliance","NoviceNetwork","PvPTeam","Echo",
          "clipboard","Clipboard","Party Finder","Listing","You","current"]: add(v)

# 3. Tour content: titles, bodies, try-its, tour names
tc = file('Tutorial/TourContent.cs')
for s, e, v in groups(tc):
    line = tc[tc.rfind('\n',0,s)+1:s].strip()
    if line.startswith('//') or line.startswith('///'): continue
    if not ok(v): continue
    if v in ('main', 'settings'): continue
    if re.fullmatch(r'[A-Za-z]+', v) and v in ["Engine","Languages","Chat","Roleplay","AI","Glossary","Help"]: continue
    add(v)

# 4. User-visible text that never goes through L(): literals passed straight to the chat / status line / errors.
#    Lines that only show codes or numbers say so with a "// loc-ok" comment.
LITERAL = re.compile(r'\b(SetStatus|PrintError|ChatGui\.Print|TranslationException|EnginePausedException|Ui\.Status|Ui\.Badge)\(\s*\$?"')
bad = False
for p in SOURCES:
    for n, line in enumerate(file(p).split('\n'), 1):
        if LITERAL.search(line) and not line.strip().startswith('//') and 'loc-ok' not in line:
            print(f'untranslated literal: {p}:{n}: {line.strip()[:110]}')
            bad = True

for name in sorted(os.listdir(os.path.join(root, 'Localization'))):
    if not name.endswith('.json'):
        continue
    table = json.load(open(os.path.join(root, 'Localization', name), encoding='utf-8'))
    missing = [k for k in keys if k not in table]
    unused = [k for k in table if k not in keys]
    ph = lambda s: sorted(re.findall(r'\{[^{}]*\}', s))
    wrong = [k for k in keys if k in table and ph(k) != ph(table[k])]
    print(f'{name}: {len(table)} entries, {len(missing)} missing, {len(unused)} unused, {len(wrong)} placeholder mismatches')
    for k in missing: print('  missing:', json.dumps(k, ensure_ascii=False)[:100])
    for k in unused: print('  unused: ', json.dumps(k, ensure_ascii=False)[:100])
    for k in wrong: print('  placeholders:', json.dumps(k, ensure_ascii=False)[:100])
    bad |= bool(missing or wrong)
sys.exit(1 if bad else 0)
