"""SPEC-SPELL-120-308 WP-01A: the offline importer (python mirror of Spellcraft/SpellTableBuilder308.cs).

Canonical vocabulary CSV + rule sheets (Rules308_*.csv) + Unlock308.csv -> the 120-row dispatch table.
The C# builder (editor importer and the offline runner) follows the same rules; both must print the same table hash.
Read-only against the project. Writes table308.json / table308.txt under Art/SpellVFX120/Spell120_308/ (unless --check).

usage: python Tools/SpellVFX120/spell120_table308.py [--sheets <dir>] [--strict] [--check] [--quiet]
                                                    [--only <packages>] [--skip <glyphs>] [--selection [<file>]]
  --sheets  folder holding Rules308_*.csv and Unlock308.csv (default: the stage folder when it has sheets, else Assets)
  --strict  an assigned glyph without a rule row is an error (end of the build phase; today 40 glyphs wait for their package)
  --check   build and report only, write nothing
  --only / --skip / --selection   the deployment switch of the editor importer (spell308-import:only=..:skip=..), mirrored:
            --only WP00+WP03 = Rules308_Base plus those package sheets (base = the legacy 36 alone), --skip <glyphs> = those
            glyphs keep their Base row, --selection = the selection the last editor import recorded
            (Art/SpellVFX120/Spell120_308/Import/selection308.json). A selected build never writes table308.json / .txt:
            those two are the table of every sheet.
"""
import argparse, csv, hashlib, io, json, re, sys
from decimal import Decimal, InvalidOperation
from pathlib import Path


def repo_root(start):
    """The repository root above a tool file: the folder that holds Oheangbu/Assets and Tools/SpellVFX120. The tools sit in
    Tools/SpellVFX120 (two levels down); a staged copy of a tool, deeper under Tools/Unity/<stage>, finds the same root."""
    here = Path(start).resolve()
    for folder in here.parents:
        if (folder / 'Oheangbu' / 'Assets').is_dir() and (folder / 'Tools' / 'SpellVFX120').is_dir():
            return folder
    return here.parents[2]


ROOT = repo_root(__file__)
CSV_PATH = ROOT / 'Docs' / '오행부_작도어휘_v0_1.csv'
STAGE = ROOT / 'Tools/Unity/Stage308_spell'
STAGE_SHEETS = STAGE / '_ProjectAssets/Data/Spells'
LIVE_SHEETS = ROOT / 'Oheangbu/Assets/_Project/Data/Spells'
OUT = ROOT / 'Art/SpellVFX120/Spell120_308'
EXPECTED_CSV_SHA = '467f1dc904cf261bf8a1b04213fcc46b04080037ee5caf6ec1252eb4b11bc23f'  # raw file bytes

CANONICAL_HEADER = '글자,초성,속성,중성,프레임,종성,분류,효과,상태,비고'
RULE_COLUMNS = ['글자', 'Mode', 'Handler', 'Pending', 'Kind', 'Gate', 'Feature', 'Inherit', 'BasePower', 'SpeedMul', 'AreaShape',
                'AreaAngle', 'AreaRadius', 'AreaLength', 'AreaSpeed', 'AreaDelay', 'Shots', 'Interval', 'Scatter', 'Params']
UNLOCK_COLUMNS = ['종성', 'LedgerId', 'EvidenceId', 'GrantedInMain']
CATEGORY_NAMES = ['공격', '상합 설치', '패링', '버프', '소환', '방벽', '필드', '공백']
CATEGORIES = ['Attack', 'Install', 'Parry', 'Buff', 'Summon', 'Ward', 'Field', 'Blank']
CATEGORY_COUNTS = [50, 5, 5, 25, 5, 5, 5, 20]
ELEMENTS = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
ELEMENT_NAMES = '목화토금수'
INITIALS, MEDIALS = 'ㄱㄴㅁㅅㅇ', 'ㅏㅓㅗㅜ'
MEDIAL_NAMES = ['A', 'Eo', 'O', 'U']
FINALS = ['None', 'Giyeok', 'Nieun', 'Mieum', 'Siot', 'Ieung']
KINDS = ['AttackSingle', 'AttackArea', 'Parry', 'Summon', 'Field', 'Buff', 'Ward', 'Install']
SHAPES = ['None', 'Cone', 'Circle', 'Path', 'Volley']
FEATURES = {'': 'None', 'book': 'Book', 'ward': 'Ward', 'buff.g': 'BuffG', 'guk': 'Guk', 'giyeok': 'Giyeok', 'mum': 'Mum'}
DEFAULT_KIND = {'Attack': 'AttackSingle', 'Install': 'Install', 'Parry': 'Parry', 'Buff': 'Buff', 'Summon': 'Summon', 'Ward': 'Ward',
                'Field': 'Field', 'Blank': 'AttackSingle'}
LEAD, VOWEL, TAIL = [0, 2, 6, 9, 11], [0, 4, 8, 13], [0, 1, 4, 16, 19, 21]
# key -> (min, max). Keys the built-in paths read; handlers declare their own (SpellParamSpec in the handler source).
CORE_KEYS = {'cost': (0, 10), 'area.spikes': (0, 1), 'area.seed': (0, 1), 'trace.pierce': (0, 1), 'lift.height': (.1, 20),
             'lift.rise': (.01, 20), 'lift.descent': (.01, 20), 'lift.hold': (0, 600), 'spike.count': (1, 64), 'spike.fill': (.01, 1),
             'spike.window': (0, 10), 'spike.gap': (0, 10), 'spike.inner': (0, 1)}
NUMBERS = [('BasePower', 'power', 0, 1000, False), ('SpeedMul', 'speed', 0, 10, False), ('AreaAngle', 'angle', 0, 180, False),
           ('AreaRadius', 'radius', 0, 100, False), ('AreaLength', 'length', 0, 200, False), ('AreaSpeed', 'aspeed', 0, 200, False),
           ('AreaDelay', 'delay', 0, 30, False), ('Shots', 'shots', 0, 200, True), ('Interval', 'interval', 0, 10, False),
           ('Scatter', 'scatter', 0, 1, True)]
# What the importer never writes (mirror of Editor/WorldMacro/SpellImportGuard308.cs; spell120_guard308.py compares the two).
# A protected book or summon profile is never an import target: it is "SKIPPED (protected)".
#   trees  a folder or file name that starts with one of these names
#   files  a file name, wherever the file sits
#   paths  an asset path, letter case ignored: an entry that ends with '/' is a folder (everything under it), any other entry
#          is that one file. Neither sits in a protected tree; the protected scene W_Demo_Compact.unity reads both.
PROTECTED_TREES = ('Watershed295', 'Reworld292', 'MountainTrail285')
PROTECTED_FILES = ('W_Demo_Compact.unity', '03_Content.asset')
PROTECTED_PATHS = ('Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset', 'Assets/_Project/Art/Demo/Summons/')
SKIP_LABEL = 'SKIPPED (protected)'


def protected_path(path):
    parts = str(path or '').replace(chr(92), '/').split('/')
    if not parts[-1]:
        return True
    if any(part.lower().startswith(tree.lower()) for part in parts for tree in PROTECTED_TREES) or \
            any(parts[-1].lower() == name.lower() for name in PROTECTED_FILES):
        return True
    # the paths, against the path without its empty, '.' and '..' segments and from every folder boundary on (the C#
    # OnProtectedPath): 'Oheangbu/Assets/...' and an absolute path are judged like the asset path itself
    kept = []
    for part in parts:
        if part in ('', '.'):
            continue
        if part == '..':
            if kept:
                kept.pop()
            continue
        kept.append(part)
    text = ('/' + '/'.join(kept) + '/').lower()
    return any(('/' + entry.lower()) in text if entry.endswith('/') else text.endswith('/' + entry.lower() + '/') for entry in PROTECTED_PATHS)


KEY_RE = re.compile(r'^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)*$')
ID_RE = re.compile(r'^[a-z0-9_]+$')


def letter_at(index):
    i = index - 1
    return chr(0xAC00 + (LEAD[i // 24] * 21 + VOWEL[i % 24 // 6]) * 28 + TAIL[i % 6])


def index_of(letter):
    if len(letter) != 1:
        return 0
    code = ord(letter) - 0xAC00
    if code < 0 or code >= 19 * 21 * 28:
        return 0
    lead, vowel, tail = code // (21 * 28), code // 28 % 21, code % 28
    if lead not in LEAD or vowel not in VOWEL or tail not in TAIL:
        return 0
    return LEAD.index(lead) * 24 + VOWEL.index(vowel) * 6 + TAIL.index(tail) + 1


def cell_of(index):
    """(initial 0..4, medial 0..3, final 0..5)"""
    i = index - 1
    return i // 24, i % 24 // 6, i % 6


def category_of(initial, medial, final):
    if medial == 0:
        return 'Install' if final == 3 else 'Attack'
    if medial == 1:
        return 'Parry' if final == 0 else 'Buff'
    if medial == 2:
        return 'Summon' if final == 3 else 'Attack'
    if final == 0:
        return 'Ward'
    return 'Field' if final == initial + 1 else 'Blank'


def kind_fits(category, kind):
    if category == 'Attack':
        return kind in ('AttackSingle', 'AttackArea')
    return category != 'Blank' and kind == DEFAULT_KIND[category]


def core_handler(kind, shape):
    """The built-in handler id of a kind and shape (the C# SpellTableBuilder308.CoreHandler)."""
    named = {'Parry': 'core.parry', 'Summon': 'core.summon', 'Field': 'core.field', 'Buff': 'core.buff', 'Ward': 'core.ward', 'Install': 'core.install'}
    if kind in named:
        return named[kind]
    return {'Cone': 'core.cone', 'Circle': 'core.circle', 'Path': 'core.path', 'Volley': 'core.volley'}.get(shape, 'core.single')


def takeover_declared(row):
    """WP-11 handover (the C# SpellTakeover308.Declared): a book row that names a handler other than the built-in of its shape."""
    return row['feature'] == 'Book' and bool(row['handler']) and row['handler'] != core_handler(row['kind'], row['shape'])


def normalise(text):
    if text.startswith(chr(0xFEFF)):
        text = text[1:]
    return text.replace('\r\n', '\n').replace('\r', '\n')


def sha(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def parse_csv(text):
    rows = []
    for row in csv.reader(io.StringIO(normalise(text))):
        if not row or (row[0][:1] == '#'):
            continue
        rows.append(row)
    return rows


def try_decimal(text):
    """Plain decimals, at most six significant digits (the C# TryDecimal)."""
    if not text:
        return None
    digits, leading = 0, True
    for i, c in enumerate(text):
        if c == '-' and i == 0:
            continue
        if c == '.':
            continue
        if not ('0' <= c <= '9'):
            return None
        if leading and c == '0':
            continue
        leading = False
        digits += 1
    if digits > 6 or text.count('.') > 1 or text in ('-', '.', '-.'):
        return None
    try:
        return Decimal(text)
    except InvalidOperation:
        return None


def dec_text(value):
    text = format(Decimal(value), 'f')
    if '.' in text:
        text = text.rstrip('0').rstrip('.')
    return '0' if text in ('-0', '') else text


def read_sheets(folder):
    folder = Path(folder)
    sheets = sorted(((p.stem, p.read_text(encoding='utf-8')) for p in folder.glob('Rules308_*.csv')), key=lambda s: s[0].encode('utf-8'))
    unlock = (folder / 'Unlock308.csv').read_text(encoding='utf-8') if (folder / 'Unlock308.csv').exists() else ''
    return sheets, unlock


def default_sheets():
    return STAGE_SHEETS if any(STAGE_SHEETS.glob('Rules308_*.csv')) else LIVE_SHEETS


# ---- the deployment switch (the C# SpellTable308.Selection of the editor importer) ----
BASE_SHEET, SHEET_PREFIX = 'Rules308_Base', 'Rules308_'
SELECTION_FILE = OUT / 'Import' / 'selection308.json'


def selection(only=None, skip='', every=None):
    """{'all', 'only' (full sheet names, sorted), 'skip' (glyphs)}. only = None means every sheet."""
    names = []
    for token in re.split(r'[+,]', only or ''):
        name = token.strip()
        if not name or name.lower() == 'base' or name == BASE_SHEET:
            continue
        if name.startswith(SHEET_PREFIX):
            name = name[len(SHEET_PREFIX):]
        if name[0].isdigit():
            name = 'WP' + name
        name = SHEET_PREFIX + name.upper()
        if name not in names:
            names.append(name)
    glyphs = ''
    for ch in skip or '':
        if ch.isspace() or ch in '+,':
            continue
        if index_of(ch) == 0:
            raise ValueError('skip names a character outside the 120-glyph grid: U+%04X' % ord(ch))
        if ch not in glyphs:
            glyphs += ch
    return {'all': (only is None) if every is None else bool(every), 'only': sorted(names, key=lambda n: n.encode('utf-8')), 'skip': glyphs}


def read_selection(path=None):
    """The selection the last editor import recorded, or None (no import recorded one, or the file cannot be read)."""
    path = Path(path) if path else SELECTION_FILE
    if not path.exists():
        return None
    try:
        data = json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return None
    return {'all': bool(data.get('all', True)), 'only': sorted(data.get('only') or [], key=lambda n: n.encode('utf-8')), 'skip': data.get('skip') or ''}


def selection_text(choice):
    if choice is None:
        return 'every sheet'
    head = 'every sheet' if choice['all'] else 'only base (the legacy 36)' if not choice['only'] else \
        'only ' + '+'.join(n[len(SHEET_PREFIX):] for n in choice['only'])
    return head + (', skip ' + choice['skip'] if choice['skip'] else '')


def without_rows(text, skip):
    """A package sheet without the rows of the skipped glyphs (the C# WithoutRows: a rule row starts with its glyph and a comma)."""
    if not skip:
        return text
    kept = []
    for line in text.split(chr(10)):      # lines end at a line feed only, as in the C# (a carriage return stays inside its line)
        first = 1 if line[:1] == chr(0xFEFF) else 0
        if len(line) > first + 1 and line[first + 1] == ',' and line[first] in skip:
            continue
        kept.append(line)
    return chr(10).join(kept)


def select(sheets, choice):
    """The sheets a selection carries: Base always, the named packages (or all), the skipped glyphs' rows taken out."""
    if choice is None:
        return sheets
    picked = []
    for name, text in sheets:
        if not (choice['all'] or name == BASE_SHEET or name in choice['only']):
            continue
        picked.append((name, text if name == BASE_SHEET else without_rows(text, choice['skip'])))
    return picked


def build(canonical_text, sheets, unlock_text, catalog=None, strict=False):
    """catalog: {handler id: {key: (min, max, required)}} or None. Returns a dict (rows, unlocks, errors, notes, missing, hashes)."""
    errors, notes, missing = [], [], []
    sheets = sorted(sheets, key=lambda s: s[0].encode('utf-8'))
    source = 'csv\n' + sha(normalise(canonical_text)) + '\n'
    for name, text in sheets:
        source += name + '\n' + sha(normalise(text)) + '\n'
    source += 'unlock\n' + sha(normalise(unlock_text))
    source_hash = sha(source)

    canonical = parse_csv(canonical_text)
    if not canonical or ','.join(canonical[0]) != CANONICAL_HEADER:
        errors.append('canonical CSV: unexpected header')
    if len(canonical) != 121:
        errors.append('canonical CSV: %d rows, expected 120' % (len(canonical) - 1))
    counts = [0] * 8
    categories, effects = [], []
    for i in range(120):
        initial, medial, final = cell_of(i + 1)
        expected = letter_at(i + 1)
        categories.append(category_of(initial, medial, final))
        effects.append('')
        if i + 1 >= len(canonical):
            continue
        cells = canonical[i + 1]
        if len(cells) < 7:
            errors.append('canonical CSV row %d: too few cells' % (i + 1)); continue
        if cells[0] != expected:
            errors.append("canonical CSV row %d: glyph '%s' is not grid cell %d" % (i + 1, cells[0], i + 1)); continue
        effects[i] = cells[7] if len(cells) > 7 else ''
        if cells[1] != INITIALS[initial]:
            errors.append(cells[0] + ': initial column disagrees with the glyph')
        if cells[2] != ELEMENT_NAMES[initial]:
            errors.append(cells[0] + ': element column disagrees with the initial')
        if cells[3] != MEDIALS[medial]:
            errors.append(cells[0] + ': medial column disagrees with the glyph')
        final_jamo = INITIALS.find(cells[5]) if len(cells[5]) == 1 else -1
        if final_jamo + 1 != final:
            errors.append(cells[0] + ': final column disagrees with the glyph')
        if cells[6] not in CATEGORY_NAMES:
            errors.append("%s: unknown category '%s'" % (cells[0], cells[6]))
        else:
            c = CATEGORY_NAMES.index(cells[6]); counts[c] += 1
            if CATEGORIES[c] != categories[i]:
                errors.append('%s: CSV category %s but the grammar grid says %s' % (cells[0], CATEGORIES[c], categories[i]))
    if len(canonical) == 121:
        for c in range(8):
            if counts[c] != CATEGORY_COUNTS[c]:
                errors.append('category %s: %d rows, expected %d' % (CATEGORIES[c], counts[c], CATEGORY_COUNTS[c]))

    drafts = [None] * 120
    for name, text in sheets:
        rows = parse_csv(text)
        if not rows:
            errors.append(name + ': empty sheet'); continue
        header, column = True, {}
        for at, title in enumerate(rows[0]):
            if title not in RULE_COLUMNS or title in column:
                errors.append("%s: unknown or repeated column '%s'" % (name, title)); header = False
            else:
                column[title] = at
        if RULE_COLUMNS[0] not in column:
            errors.append(name + ': no glyph column'); header = False
        if not header:
            continue
        for r in range(1, len(rows)):
            cells = rows[r]

            def cell(title):
                at = column.get(title)
                return cells[at].strip() if at is not None and at < len(cells) else ''
            letter = cell(RULE_COLUMNS[0])
            where = '%s row %d (%s)' % (name, r, letter)
            index = index_of(letter)
            if index == 0:
                errors.append(where + ': not a vocabulary glyph'); continue
            if categories[index - 1] == 'Blank':
                errors.append(where + ': a blank glyph cannot have a rule row'); continue
            draft = {'letter': letter, 'sheet': name, 'mode': cell('Mode') or 'new', 'handler': cell('Handler'), 'pending': cell('Pending'),
                     'kind': cell('Kind'), 'gate': cell('Gate'), 'feature': cell('Feature'), 'inherit': cell('Inherit'),
                     'shape': cell('AreaShape'), 'params': {}, 'replaced': False}
            fine = True
            for title, key, low, high, whole in NUMBERS:
                text_value = cell(title)
                draft[key] = None
                if not text_value:
                    continue
                value = try_decimal(text_value)
                if value is None or value < low or value > high or (whole and value != value.to_integral_value()):
                    errors.append("%s: %s '%s' is outside %s..%s" % (where, title, text_value, low, high)); fine = False; continue
                draft[key] = value
            for pair in [p for p in cell('Params').split(';') if p]:
                eq = pair.find('=')
                key = pair[:eq].strip() if eq > 0 else ''
                value = try_decimal(pair[eq + 1:].strip())
                if not KEY_RE.match(key) or value is None or key in draft['params']:
                    errors.append("%s: bad or repeated parameter '%s'" % (where, pair.strip())); fine = False; continue
                draft['params'][key] = value
            if draft['mode'] not in ('new', 'replace'):
                errors.append(where + ': Mode must be new or replace'); fine = False
            if (not draft['handler']) == (not draft['pending']):
                errors.append(where + ': exactly one of Handler and Pending must be filled'); fine = False
            if draft['feature'] and (draft['feature'] not in FEATURES or not draft['handler']):
                errors.append("%s: unknown Feature '%s' or Feature without Handler" % (where, draft['feature'])); fine = False
            if draft['gate'] not in ('', 'final', 'open', 'test'):
                errors.append(where + ': Gate must be final, open or test'); fine = False
            if draft['gate'] == 'open' and categories[index - 1] != 'Summon':
                errors.append(where + ': Gate open is the summon exception only (a final consonant opens through Unlock308.csv)'); fine = False
            # Gate test narrows a row to its final's TEST unlock (the C# SpellUnlockPolicy308.RuleFor)
            if draft['gate'] == 'test' and (cell_of(index)[2] == 0 or not draft['handler'] or draft['feature']):
                errors.append(where + ': Gate test needs a handler row with a final consonant and without a legacy feature'); fine = False
            if draft['inherit'] not in ('', 'base'):
                errors.append(where + ': Inherit must be empty or base'); fine = False
            if draft['kind'] and draft['kind'] not in KINDS:
                errors.append("%s: unknown Kind '%s'" % (where, draft['kind'])); fine = False
            if draft['shape'] and draft['shape'] not in SHAPES:
                errors.append("%s: unknown AreaShape '%s'" % (where, draft['shape'])); fine = False
            if not fine:
                continue
            existing = drafts[index - 1]
            if draft['mode'] == 'new':
                if existing is not None:
                    errors.append('%s: the glyph already has a row in %s (use Mode replace)' % (where, existing['sheet'])); continue
            else:
                if existing is None:
                    errors.append(where + ': replace needs an existing row'); continue
                if existing['replaced']:
                    errors.append('%s: the glyph was already replaced by %s' % (where, existing['sheet'])); continue
                draft['replaced'] = True
                notes.append('%s: %s row replaced by %s' % (letter, existing['sheet'], name))
            drafts[index - 1] = draft

    rows_out, lines = [], []
    for i in range(120):
        initial, medial, final = cell_of(i + 1)
        letter, category, draft = letter_at(i + 1), categories[i], drafts[i]
        row = {'index': i + 1, 'letter': letter, 'element': ELEMENTS[initial], 'medial': MEDIAL_NAMES[medial], 'final': FINALS[final],
               'category': category, 'kind': DEFAULT_KIND[category], 'handler': '', 'pending': '', 'gate': 'Final', 'feature': 'None',
               'shape': 'None', 'params': {}, 'sheet': '', 'effect': effects[i]}
        values = {key: Decimal(0) for _, key, _, _, _ in NUMBERS}
        if draft is None:
            if category != 'Blank':
                missing.append(letter)
                if strict:
                    errors.append(letter + ': assigned glyph without a rule row')
        else:
            row.update(handler=draft['handler'], pending=draft['pending'],
                       gate='Open' if draft['gate'] == 'open' else 'Test' if draft['gate'] == 'test' else 'Final',
                       feature=FEATURES[draft['feature']], sheet=draft['sheet'])
            parent = None
            if draft['inherit'] == 'base':
                base_index = i + 1 - final
                parent = drafts[base_index - 1] if base_index != i + 1 else None
                if parent is None or not parent['handler'] or parent['inherit']:
                    errors.append(letter + ': Inherit base needs a handled base row that does not inherit itself'); parent = None
            for _, key, _, _, _ in NUMBERS:
                own = draft[key]
                values[key] = own if own is not None else (parent[key] if parent is not None and parent[key] is not None else Decimal(0))
            row['shape'] = draft['shape'] or (parent['shape'] if parent is not None and parent['shape'] else 'None')
            kind = draft['kind'] or (parent['kind'] if parent is not None else '')
            if kind:
                row['kind'] = kind
            if not kind_fits(category, row['kind']):
                errors.append('%s: Kind %s does not fit category %s' % (letter, row['kind'], category))
            declared = None
            # WP-11: a book row that declares a handover (SpellTakeover308.Declared) carries its handler's parameters like a new row
            handled = row['feature'] == 'None' or takeover_declared(row)
            registered = handled and row['handler'] and catalog is not None
            if registered:
                declared = catalog.get(row['handler'])
                if declared is None:
                    errors.append("%s: handler '%s' is not registered" % (letter, row['handler']))
            must_know = not handled or not row['handler'] or catalog is not None
            for key in sorted(draft['params'], key=lambda k: k.encode('utf-8')):
                value = draft['params'][key]
                spec = CORE_KEYS.get(key) or (declared.get(key)[:2] if declared and key in declared else None)
                if spec is None and must_know:
                    errors.append("%s: unknown parameter '%s'" % (letter, key))
                if spec is not None and not (Decimal(str(spec[0])) <= value <= Decimal(str(spec[1]))):
                    errors.append('%s: %s=%s is outside %s..%s' % (letter, key, dec_text(value), spec[0], spec[1]))
                row['params'][key] = dec_text(value)
            if declared:
                for key, spec in declared.items():
                    if spec[2] and key not in draft['params']:
                        errors.append("%s: required parameter '%s' of %s is missing" % (letter, key, row['handler']))
        for _, key, _, _, _ in NUMBERS:
            row[key] = dec_text(values[key])
        rows_out.append(row)
        lines.append('|'.join([str(row['index']), letter, row['element'], row['medial'], row['final'], category, row['kind'], row['handler'],
                               row['pending'], row['gate'], row['feature'], row['power'], row['speed'], row['shape'], row['angle'],
                               row['radius'], row['length'], row['aspeed'], row['delay'], row['shots'], row['interval'], row['scatter'],
                               ';'.join(k + '=' + v for k, v in row['params'].items())]))

    unlocks = []
    unlock_rows = parse_csv(unlock_text)
    if not unlock_rows or ','.join(unlock_rows[0]) != ','.join(UNLOCK_COLUMNS):
        errors.append('unlock sheet: unexpected header')
    for r in range(1, len(unlock_rows)):
        cells = [c.strip() for c in unlock_rows[r]]
        ok = len(cells) == 4 and len(cells[0]) == 1 and cells[0] in INITIALS and ID_RE.match(cells[1]) and \
            (not cells[2] or ID_RE.match(cells[2])) and cells[3] in ('0', '1')
        if not ok:
            errors.append('unlock sheet row %d: bad final, id or GrantedInMain' % r); continue
        final = INITIALS.index(cells[0]) + 1
        if any(u['final'] == FINALS[final] for u in unlocks):
            errors.append('unlock sheet row %d: final repeated' % r); continue
        unlocks.append({'final': FINALS[final], 'order': final, 'ledger': cells[1], 'evidence': cells[2], 'granted': cells[3] == '1'})
    if len(unlocks) != 5:
        errors.append('unlock sheet: %d finals, expected 5' % len(unlocks))
    unlocks.sort(key=lambda u: u['order'])
    for u in unlocks:
        lines.append('U|%s|%s|%s|%s' % (u['final'], u['ledger'], u['evidence'], '1' if u['granted'] else '0'))
    text = '\n'.join(lines)
    return {'rows': rows_out, 'unlocks': unlocks, 'errors': errors, 'notes': notes, 'missing': missing,
            'sourceHash': source_hash, 'tableHash': sha(text), 'canonical': text}


def load(sheets_dir=None, catalog=None, strict=False, choice=None):
    sheets_dir = Path(sheets_dir) if sheets_dir else default_sheets()
    sheets, unlock = read_sheets(sheets_dir)
    sheets = select(sheets, choice)
    table = build(CSV_PATH.read_text(encoding='utf-8'), sheets, unlock, catalog, strict)
    table['sheets'] = [name for name, _ in sheets]
    table['sheetsDir'] = sheets_dir.relative_to(ROOT).as_posix() if sheets_dir.is_relative_to(ROOT) else str(sheets_dir)
    table['csvSha256'] = hashlib.sha256(CSV_PATH.read_bytes()).hexdigest()
    if table['csvSha256'] != EXPECTED_CSV_SHA:
        table['errors'].append('canonical CSV sha256 changed: ' + table['csvSha256'])
    return table


def summary(table):
    rows = table['rows']
    legacy = sum(1 for r in rows if r['feature'] != 'None')
    handled = sum(1 for r in rows if r['handler'] and r['feature'] == 'None')
    reserved = sum(1 for r in rows if r['pending'])
    blank = sum(1 for r in rows if r['category'] == 'Blank')
    return {'rows': len(rows), 'legacy': legacy, 'registeredHandlerRows': handled, 'reserved': reserved, 'blank': blank,
            'waitingForSheet': len(table['missing']), 'handedOver': sum(1 for r in rows if takeover_declared(r)),
            'testOnly': sum(1 for r in rows if r['gate'] == 'Test')}


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser()
    ap.add_argument('--sheets'); ap.add_argument('--strict', action='store_true'); ap.add_argument('--check', action='store_true')
    ap.add_argument('--quiet', action='store_true')
    ap.add_argument('--only'); ap.add_argument('--skip', default=''); ap.add_argument('--selection', nargs='?', const='')
    a = ap.parse_args()
    choice = None
    if a.selection is not None:
        choice = read_selection(a.selection or None)
        print('selection (%s): %s' % (a.selection or SELECTION_FILE.relative_to(ROOT).as_posix(), selection_text(choice) if choice else 'none recorded: every sheet'))
    elif a.only is not None or a.skip:
        choice = selection(a.only, a.skip)
        print('selection (argument): ' + selection_text(choice))
    if choice is not None:
        a.check = True      # table308.json / .txt are the table of every sheet; a selected build only reports
    table = load(a.sheets, None, a.strict, choice)
    info = summary(table)
    print('sheets %s: %s' % (table['sheetsDir'], ', '.join(table['sheets'])))
    print('rows %(rows)d = legacy %(legacy)d + registered %(registeredHandlerRows)d + reserved %(reserved)d + blank %(blank)d + waiting for a sheet %(waitingForSheet)d' % info)
    if info['handedOver']:
        print('handed over to a handler when the registry has it (book rows, WP-11): %d  %s' % (
            info['handedOver'], ' '.join(r['letter'] + ':' + r['handler'] for r in table['rows'] if takeover_declared(r))))
    if info['testOnly']:
        print('TEST-unlock only, never opened by the main-game grant of their final (Gate test): %d  %s' % (
            info['testOnly'], ' '.join(r['letter'] + ':' + r['handler'] for r in table['rows'] if r['gate'] == 'Test')))
    print('sourceHash', table['sourceHash'])
    print('tableHash ', table['tableHash'])
    for note in table['notes']:
        if not a.quiet:
            print('note:', note)
    for error in table['errors']:
        print('ERROR:', error)
    if not a.check:
        OUT.mkdir(parents=True, exist_ok=True)
        payload = {k: table[k] for k in ('sheetsDir', 'sheets', 'csvSha256', 'sourceHash', 'tableHash', 'errors', 'notes', 'missing', 'unlocks', 'rows')}
        payload['summary'] = info
        (OUT / 'table308.json').write_bytes((json.dumps(payload, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
        (OUT / 'table308.txt').write_bytes((table['canonical'] + '\n').encode('utf-8'))
        print('wrote', (OUT / 'table308.json').relative_to(ROOT).as_posix(), 'sha256', hashlib.sha256((OUT / 'table308.json').read_bytes()).hexdigest())
        print('wrote', (OUT / 'table308.txt').relative_to(ROOT).as_posix(), 'sha256', hashlib.sha256((OUT / 'table308.txt').read_bytes()).hexdigest())
    sys.exit(1 if table['errors'] else 0)


if __name__ == '__main__':
    main()
