"""SPEC-SPELL-120-308 L0: static contract checks of the spell table work (no Unity, no compile).

(The Spec calls this tool spell120_static308.py; that name belongs to the VFX static census of the sweep workflow, so the
table / contract half lives here.)

  table      canonical CSV (sha, 120 rows, category counts, grammar grid), rule sheets, one handler or a declared misfire per glyph
  handlers   rule-sheet ids <-> effect classes (HandlerId constants, SpellParamSpec declarations, installer registrations)
  clauses    clauses308.csv: every clause quotes the canonical text; L2 clauses that the runner covers must be ok
  patterns   no vocabulary glyph literal in the resolver, the wiring and new files; no Vfx120 / rendering / drawing reference
             in handler and rule files; no mutable static field in new files; no Gain / Heal inside a player-hit hook;
             groggy only from its allowed call sites; balance literals in handler files
  fix pass   (2026-10-04) a single shot behind a final never plans the lock-on judgement itself; an enemy / world dependent
             row behind a main-game final is TEST-only; the handler route has one refund, behind the unscheduling, and hands
             the handlers the guarded presenter; the interim presenter does not stretch a body; effects are registered one
             by one and the registry does not throw; no enemy profile carries a defence yet
  stage      every staged file sits in a folder its assembly already compiles; nothing under Drawing/ or HUD; the live
             file a staged copy was taken from has not changed since (Original/*.orig)
  data       Rules308_Base.csv reproduces from the book the main scene reads; the four books of the copy chain agree;
             after deploy: the imported table of the one target book (the Architecture296 copy W_Demo_Main reads) equals the
             table the sheets build, and no protected book (the two tree copies, the original book) carries a table

Read-only against the project. Writes Art/SpellVFX120/Spell120_308/contract308_report.json.

usage: python Tools/SpellVFX120/spell120_contract308.py [--final] [--live] [--stage <dir>] [--cases <dir>] [--out <dir>]
  --stage  another stage folder (a work package's private copy); --cases / --out as in spell120_pure308.py
  --final  end of the build phase: no glyph may wait for a rule sheet, every L2 clause of a built package must be covered
  --live   check the deployed files under Oheangbu/Assets instead of the stage
"""
import argparse, csv, hashlib, io, json, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import spell120_table308 as table308

ROOT = table308.repo_root(__file__)
sys.path.append(str(ROOT / 'Tools/SpellVFX120'))     # a staged copy of this tool finds the tools it does not carry
import spell120_base308 as base308

PROJECT = ROOT / 'Oheangbu'
SCRIPTS = PROJECT / 'Assets/_Project/Scripts'
STAGE = ROOT / 'Tools/Unity/Stage308_spell'
OUT = ROOT / 'Art/SpellVFX120/Spell120_308'
CASES = ROOT / 'Tools/SpellVFX120/Pure308'
ASSEMBLIES = ['Oheangbu.Core', 'Oheangbu.Data', 'Oheangbu.Combat', 'Oheangbu.Drawing', 'Oheangbu.BrushRender', 'Oheangbu.Presentation',
              'Oheangbu.Spellcraft', 'Oheangbu.App', 'Oheangbu.EditorTools', 'Oheangbu.EditorTools.WorldMacro']
VOCAB = {table308.letter_at(i) for i in range(1, 121)}
CHAIN = ['Oheangbu/Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset',
         'Oheangbu/Assets/_Project/Art/World/Reworld292/Data/607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset',
         'Oheangbu/Assets/_Project/Art/World/Watershed295/Data/17a09b6fa2e231f41a18e15962f1a2c4_607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset',
         'Oheangbu/Assets/_Project/Art/World/Architecture296/Data/dde80d292e16ddb48a9102e5bcae089d_17a09b6fa2e231f41a18e15962f1a2c4_607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset']
MAIN_BOOK = CHAIN[3]     # the copy W_Demo_Main reads: the one import target (SpellImportGuard308 protects the other three)
# files that hold the old glyph list on purpose, or clause ids (glyph-number) of the checklist
ORACLE = {'SpellOracle308.cs'}
CLAUSE_ID_FILES = re.compile(r'^(Spell120Checks308(\.WP\d\d)?\.cs|Cases_\w+\.cs)$')
CLAUSE_ID = re.compile(r'^.-\d+$')
# call sites that may raise groggy (parry path, harmony detonation, counter sword strike)
GROGGY_ALLOWED = {'AddFromParry(': {'Combat/GroggyMeter.cs', 'Combat/Enemy/EnemyVitals.cs'},
                  'AddGroggy(': {'Combat/Enemy/EnemyVitals.cs', 'App/InstallEffect308.cs', 'App/SwordFormEffect308.cs'},
                  '.AddParry(': {'App/CombatLoopWiring.cs'},
                  'Groggy.Add(': {'Combat/Enemy/EnemyVitals.cs'}}
# numbers a handler file may spell out: nothing that tunes the game (those are row parameters)
LITERAL_ALLOWED = {'0', '1', '0f', '1f', '2', '2f', '.5f', '0.5f', '.4f', '0.4f', '.001f', '0.001f', '.0001f', '1e-4f', '1e-3f'}


class Report:
    def __init__(self):
        self.items, self.facts, self.failed = [], {}, 0

    def check(self, group, ok, text, detail=''):
        self.items.append({'group': group, 'ok': bool(ok), 'text': text, 'detail': detail})
        if not ok:
            self.failed += 1
            print('FAIL [%s] %s%s' % (group, text, (' :: ' + detail) if detail else ''))
        return ok

    def note(self, key, value):
        self.facts[key] = value


def strip_comments(source):
    """C# source with comments blanked and string / char literals kept. Returns (code, [literal tokens])."""
    out, literals, i, n = [], [], 0, len(source)
    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ''
        if c == '/' and nxt == '/':
            j = source.find('\n', i)
            i = n if j < 0 else j
        elif c == '/' and nxt == '*':
            j = source.find('*/', i + 2)
            i = n if j < 0 else j + 2
        elif c == '@' and nxt == '"':
            j = i + 2
            while j < n:
                if source[j] == '"':
                    if j + 1 < n and source[j + 1] == '"':
                        j += 2; continue
                    break
                j += 1
            literals.append(source[i + 2:j]); out.append('""'); i = j + 1
        elif c == '"':
            j = i + 1
            while j < n and source[j] != '"':
                j += 2 if source[j] == chr(92) else 1
            literals.append(source[i + 1:j]); out.append('""'); i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and source[j] != "'":
                j += 2 if source[j] == chr(92) else 1
            literals.append(source[i + 1:j]); out.append("' '"); i = j + 1
        else:
            out.append(c); i += 1
    return ''.join(out), literals


def read(path):
    return path.read_text(encoding='utf-8-sig')


def stage_files():
    return sorted(p for p in STAGE.rglob('*.cs') if 'Original' not in p.parts and '_ProjectAssets' not in p.parts)


def method_body(code, name):
    """Text of the first method called name( ... ) { ... } in comment-stripped code ('' when absent)."""
    at = code.find(name + '(')
    while at >= 0:
        brace = code.find('{', at)
        semi = code.find(';', at)
        if brace >= 0 and (semi < 0 or brace < semi):
            depth, j = 0, brace
            while j < len(code):
                if code[j] == '{':
                    depth += 1
                elif code[j] == '}':
                    depth -= 1
                    if depth == 0:
                        return code[brace:j + 1]
                j += 1
        at = code.find(name + '(', at + 1)
    return ''


def class_chain(files, path):
    """Comment-stripped code of an effect file plus the files of the base classes its classes derive from (within the stage)."""
    owner = {}
    for p in files:
        for m in re.finditer(r'\bclass\s+(\w+)', strip_comments(read(p))[0]):
            owner.setdefault(m.group(1), p)
    seen, todo, code = set(), [path], ''
    while todo:
        p = todo.pop()
        if p in seen:
            continue
        seen.add(p)
        text = strip_comments(read(p))[0]
        code += text
        for m in re.finditer(r'\bclass\s+\w+\s*:\s*([\w.]+)', text):
            base = m.group(1).split('.')[-1]
            if base in owner:
                todo.append(owner[base])
    return code, sorted(x.name for x in seen)


def follows_base(row):
    """The C# PrimaryShotRule308.FollowsBase: a handler single shot behind a final consonant."""
    return row['final'] != 'None' and bool(row['handler']) and row['feature'] == 'None' and row['kind'] == 'AttackSingle' and row['shape'] == 'None'


def handler_catalog(files):
    """{handler id: (file, {key: (min, max, required)})} from effect sources: const HandlerId + new SpellParamSpec(...)."""
    catalog = {}
    for path in files:
        if not path.name.endswith('Effect308.cs'):
            continue
        source = read(path)
        specs = {}
        for m in re.finditer(r'new\s+SpellParamSpec\(\s*"([^"]+)"\s*,\s*([-\d.eE]+)f?\s*,\s*([-\d.eE]+)f?\s*(?:,\s*(?:required\s*:\s*)?(true|false))?', source):
            specs[m.group(1)] = (float(m.group(2)), float(m.group(3)), m.group(4) != 'false')
        for m in re.finditer(r'const\s+string\s+HandlerId\s*=\s*"([^"]+)"', source):
            catalog[m.group(1)] = (path, specs)
    return catalog


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('--final', action='store_true'); ap.add_argument('--live', action='store_true')
    ap.add_argument('--stage'); ap.add_argument('--cases'); ap.add_argument('--out')
    a = ap.parse_args()
    global STAGE, CASES
    if a.stage:
        STAGE = Path(a.stage).resolve()
    if a.cases:
        CASES = Path(a.cases).resolve()
    reports = Path(a.out).resolve() if a.out else OUT
    r = Report()
    root = SCRIPTS if a.live else STAGE
    # --live: the stage is the manifest (other tracks also have *308*.cs files under Assets; only this work's files are judged)
    files = [SCRIPTS / p.relative_to(STAGE) for p in stage_files() if (SCRIPTS / p.relative_to(STAGE)).exists()] if a.live else stage_files()
    new_files = [p for p in files if '308' in p.name]
    staged_sheets = STAGE / '_ProjectAssets/Data/Spells'
    sheets_dir = table308.LIVE_SHEETS if a.live else staged_sheets if any(staged_sheets.glob('Rules308_*.csv')) else table308.default_sheets()

    # ---- table ----
    catalog = handler_catalog(files)
    table = table308.load(sheets_dir, {k: v[1] for k, v in catalog.items()}, a.final)
    info = table308.summary(table)
    r.note('sheets', table['sheets']); r.note('summary', info); r.note('tableHash', table['tableHash']); r.note('sourceHash', table['sourceHash'])
    r.check('table', not table['errors'], 'the table builds (CSV sha, 120 rows, counts, grammar grid, sheet rules, handler parameters)', ' | '.join(table['errors'][:8]))
    again = table308.load(sheets_dir, {k: v[1] for k, v in catalog.items()}, a.final)
    r.check('table', again['tableHash'] == table['tableHash'] and again['canonical'] == table['canonical'], 'two builds give the same table')
    r.check('table', info['legacy'] == 36, '36 legacy rows', str(info['legacy']))
    r.check('table', info['blank'] == 20 and all(not x['handler'] and not x['pending'] for x in table['rows'] if x['category'] == 'Blank'), '20 blank rows without a handler')
    r.check('table', info['legacy'] + info['registeredHandlerRows'] + info['reserved'] + info['waitingForSheet'] == 100,
            'every assigned glyph is legacy, registered, reserved or waiting', str(info))
    if a.final:
        r.check('table', info['waitingForSheet'] == 0, 'no glyph waits for a rule sheet (--final)', ''.join(table['missing']))
    plan = {row['글자']: row for row in csv.DictReader(io.StringIO((OUT / 'handler_map308.csv').read_text(encoding='utf-8-sig')))}
    wrong = []
    for row in table['rows']:
        want = plan[row['letter']]
        expect = want['WP-11 교체 처리기'] or want['처리기']
        # WP-11 handover: a legacy (book) row may name the plan's replacement handler; it keeps its legacy feature
        if row['feature'] != 'None' and row['handler'] not in {want['처리기'], want['WP-11 교체 처리기']} - {''}:
            wrong.append(row['letter'] + ':' + row['handler'])
        elif row['feature'] == 'None' and row['handler'] and row['handler'] != expect:
            wrong.append(row['letter'] + ':' + row['handler'])
        elif row['pending'] and row['pending'] != want['묶음']:
            wrong.append(row['letter'] + ':pending ' + row['pending'])
    r.check('table', not wrong, 'handler ids and reserved packages equal the plan sheet (handler_map308.csv)', ' '.join(wrong[:12]))

    # ---- handlers ----
    handed = [x for x in table['rows'] if table308.takeover_declared(x)]
    r.note('handedOverRows', {x['letter']: x['handler'] for x in handed})
    r.check('table', all(x['handler'] == plan[x['letter']]['WP-11 교체 처리기'] for x in handed),
            'a book row is handed over to a handler only where the plan sheet names a replacement handler (WP-11)',
            ' '.join(x['letter'] + ':' + x['handler'] for x in handed if x['handler'] != plan[x['letter']]['WP-11 교체 처리기']))
    used = {x['handler'] for x in table['rows'] if x['handler'] and (x['feature'] == 'None' or table308.takeover_declared(x))}
    r.note('handlerClasses', sorted(catalog)); r.note('handlerIdsUsedByRows', sorted(used))
    r.check('handlers', all(h in catalog for h in used), 'every registered-handler row names an effect class', ' '.join(sorted(used - set(catalog))))
    installers = ''.join(strip_comments(read(p))[0] for p in files if p.name.startswith('SpellEffectInstaller308'))
    missing_registration = []
    for handler, (path, _) in catalog.items():
        for cls in re.findall(r'class\s+(\w+Effect308)\b', strip_comments(read(path))[0]):
            if 'abstract class ' + cls in read(path):
                continue
            if 'Register<' + cls + '>' not in installers:
                missing_registration.append(cls)
    r.check('handlers', not missing_registration, 'every effect class is registered in SpellEffectInstaller308*.cs', ' '.join(sorted(set(missing_registration))))

    # ---- clauses ----
    clauses = list(csv.DictReader(io.StringIO((OUT / 'clauses308.csv').read_text(encoding='utf-8-sig'))))
    effect = {x['letter']: x['effect'] for x in table['rows']}
    unquoted = [c['절 ID'] for c in clauses if c['CSV 원문 조각'] not in effect.get(c['글자'], '') and c['검사 층'] != '보류' or not c['검사 층']]
    unquoted = [c['절 ID'] for c in clauses if c['글자'] in effect and c['CSV 원문 조각'] and c['CSV 원문 조각'] not in effect[c['글자']]]
    r.check('clauses', not unquoted, 'every clause quotes a literal fragment of the canonical effect text (%d clauses)' % len(clauses), ' '.join(unquoted[:10]))
    r.check('clauses', all(c['검사 층'] for c in clauses), 'every clause is bound to a layer (L2, L3, existing check, Play or held)')
    pure = reports / 'pure308_report.json'
    covered, failed_clauses = {}, []
    if pure.exists():
        covered = json.loads(pure.read_text(encoding='utf-8')).get('clauses', {})
        failed_clauses = [k for k, v in covered.items() if v.get('failed')]
    r.check('clauses', pure.exists() and not failed_clauses, 'the runner report exists and no covered clause failed', ' '.join(failed_clauses[:10]))
    l2 = [c['절 ID'] for c in clauses if c['검사 층'] == 'L2']
    l2_covered = [c for c in l2 if c in covered]
    check_sources = ''.join(read(p) for p in files if p.name.startswith('Spell120Checks308'))
    l3 = [c['절 ID'] for c in clauses if c['검사 층'] == 'L3']
    l3_written = [c for c in l3 if '"' + c + '"' in check_sources or (c.split('-')[1] and '+ "-' + c.split('-')[1] + '"' in check_sources and c[0] in '곰놈몸솜옴')]
    r.note('clauses', {'total': len(clauses), 'L2': len(l2), 'L2 covered by the runner': len(l2_covered), 'L3': len(l3), 'L3 written in Spell120Checks308': len(l3_written),
                       'runner clause ids': sorted(covered)})
    if a.final:
        built = {x['letter'] for x in table['rows'] if x['handler']}
        open_l2 = [c['절 ID'] for c in clauses if c['검사 층'] == 'L2' and c['글자'] in built and c['절 ID'] not in covered]
        r.check('clauses', not open_l2, 'every L2 clause of a built glyph is covered by the runner (--final)', ' '.join(open_l2[:20]))

    # ---- patterns ----
    glyph_hits, render_hits, static_hits, reward_hits, literal_hits = [], [], [], [], []
    scan = [p for p in files if p.name in ('SpellResolver.cs',) or p.name.startswith('CombatLoopWiring') or p in new_files]
    for path in sorted(set(scan)) + sorted(CASES.glob('*.cs')):
        code, literals = strip_comments(read(path))
        rel = path.name
        if rel in ORACLE:
            continue
        for literal in literals:
            # a literal made only of vocabulary glyphs names a spell ("곰", '고', a glyph list). Mixed words (CSV column and
            # category names) are not spell names; clause ids (glyph-number) are allowed in check and case files only.
            if literal and all(ch in VOCAB for ch in literal):
                glyph_hits.append(rel + ':' + literal)
            elif CLAUSE_ID.match(literal) and literal[0] in VOCAB and not CLAUSE_ID_FILES.match(rel):
                glyph_hits.append(rel + ':' + literal)
        if path in new_files:
            for line in code.splitlines():
                s = line.strip()
                if re.match(r'^(public|private|internal|protected)?\s*(static)\s', s) and s.endswith(';') and '(' not in s.split('=')[0] and \
                        not re.search(r'\b(readonly|const|class|event|delegate)\b', s) and '=>' not in s:
                    static_hits.append(rel + ':' + s[:70])
                # a static readonly collection is still mutable state that survives Play (domain reload is off)
                elif re.search(r'\bstatic\s+(readonly\s+)?(System\.Collections\.Generic\.)?(List|Dictionary|HashSet|Queue|Stack|SortedDictionary|SortedList|LinkedList)\s*<', s) \
                        and '(' not in s.split('=')[0] and '=>' not in s.split('=')[0]:
                    static_hits.append(rel + ':' + s[:70])
        if rel.endswith('Effect308.cs') or rel.endswith('Rule308.cs'):
            for token in ('Vfx120', 'UnityEngine.Rendering', 'Oheangbu.Drawing', 'GameObject.Find', 'Resources.Load'):
                if token in code:
                    render_hits.append(rel + ':' + token)
        if 'IPlayerHitHook' in code and not rel.startswith(('SpellEffect308', 'SpellEffectRegistry308', 'Spell120Checks308')):
            body = method_body(code, 'OnPlayerHit')
            if '.Gain(' in body or '.Heal(' in body or '.Restore(' in body:
                reward_hits.append(rel)
        if rel.endswith('Effect308.cs'):
            for line in code.splitlines():
                if 'SpellParamSpec(' in line:
                    continue
                for number in re.findall(r'(?<![\w.])(\d*\.\d+f?|\d+f|\d+)(?![\w.])', line):
                    if number not in LITERAL_ALLOWED:
                        literal_hits.append(rel + ':' + number)
    r.check('patterns', not glyph_hits, 'no vocabulary glyph literal in the resolver, the wiring and new files (A3)', ' '.join(glyph_hits[:12]))
    r.check('patterns', not render_hits, 'handler and rule files reference no Vfx120 / rendering / drawing type (A9)', ' '.join(render_hits[:12]))
    r.check('patterns', not static_hits, 'no mutable static field in new files (A0)', ' | '.join(static_hits[:8]))
    r.check('patterns', not reward_hits, 'no Gain / Heal / Restore inside a player-hit hook (A10)', ' '.join(reward_hits))
    r.check('patterns', not literal_hits, 'handler files spell out no balance number (A7)', ' '.join(literal_hits[:12]))
    groggy_hits = []
    runtime = {}
    for path in SCRIPTS.rglob('*.cs'):
        rel = path.relative_to(SCRIPTS).as_posix()
        if not rel.startswith('Editor/'):
            runtime[rel] = path
    if not a.live:
        for path in files:
            rel = path.relative_to(STAGE).as_posix()
            if not rel.startswith('Editor/'):
                runtime[rel] = path
    for rel, path in runtime.items():
        try:
            code = strip_comments(read(path))[0]
        except (OSError, UnicodeDecodeError):
            continue
        for token, allowed in GROGGY_ALLOWED.items():
            if token in code and rel not in allowed:
                groggy_hits.append(rel + ':' + token)
    r.check('patterns', not groggy_hits, 'groggy rises only from its allowed call sites: parry, harmony, counter sword strike (A10)', ' '.join(groggy_hits[:12]))

    # ---- fix pass 2026-10-04 (decisions a - d of the main agent, review B robustness) ----
    by_name = {p.name: p for p in files}

    def code_of(name):
        return strip_comments(read(by_name[name]))[0] if name in by_name else ''
    # b. ballistics belong to the initial consonant: a single shot behind a final whose base glyph is handed over to a
    #    handler flies its first stage through that handler (SpellPrimary308), never through the wiring's lock-on plan
    by_letter = {x['letter']: x for x in table['rows']}
    staged, back_on_lock_on, no_stage = [], [], []
    for row in table['rows']:
        if not follows_base(row):
            continue
        base = by_letter[table308.letter_at(row['index'] - table308.FINALS.index(row['final']))]
        if not table308.takeover_declared(base):
            continue
        staged.append(row['letter'])
        if row['handler'] not in catalog:
            back_on_lock_on.append(row['letter'] + ':no class'); continue
        code, chain = class_chain(files, catalog[row['handler']][0])
        if '.PlanSingle(' in code:
            back_on_lock_on.append('%s:%s calls PlanSingle (%s)' % (row['letter'], row['handler'], ' '.join(chain)))
        if 'SpellPrimary308.Plan(' not in code and 'DerivedHits308.Primary(' not in code:
            back_on_lock_on.append('%s:%s does not take SpellPrimary308.Plan' % (row['letter'], row['handler']))
        if base['handler'] not in catalog or 'ISpellPrimaryStage308' not in strip_comments(read(catalog[base['handler']][0]))[0]:
            no_stage.append(base['letter'] + ':' + base['handler'])
    r.note('primaryStageRows', staged)
    r.check('fix', len(staged) == 10 and not back_on_lock_on and not no_stage and 'SpellPrimary308.Plan(' in code_of('DerivedHits308.cs'),
            'the ten single shots behind a final take their first stage from the base glyph\'s handler: no handler of theirs plans the lock-on single judgement',
            ' | '.join(back_on_lock_on + no_stage) or ''.join(staged))
    wiring = code_of('CombatLoopWiring.Spell308.cs')
    r.check('fix', 'PrimaryShotRule308.StageHandler(' in wiring and 'ISpellPrimaryStage308' in wiring and re.search(r'new SpellCastContext\([^;]*primary, ballistics\)', wiring) is not None,
            'the wiring hands every handler cast the first-stage handler of its body (PrimaryShotRule308)')
    # a. an enemy / world dependent row behind a final the main game grants stays TEST-only
    granted = {u['final'] for u in table['unlocks'] if u['granted']}
    world = [x for x in table['rows'] if x['handler'] and x['feature'] == 'None' and x['final'] in granted and plan[x['letter']]['묶음'] in ('WP-12', 'WP-13')]
    r.note('testOnlyRows', [x['letter'] for x in table['rows'] if x['gate'] == 'Test'])
    r.check('fix', world and all(x['gate'] == 'Test' for x in world) and all(x in world for x in table['rows'] if x['gate'] == 'Test') and
            'SpellUnlockPolicy308.RuleFor(row, rule)' in wiring,
            'every enemy / world dependent row (plan packages WP-12, WP-13) behind a main-game final is Gate test, nothing else is, and the wiring judges a row by its own rule',
            ' '.join(x['letter'] + ':' + x['gate'] for x in world))
    # c. one refund on the handler route, behind the unscheduling; handlers only ever see the guarded presenter
    withdraw = method_body(wiring, 'Withdraw')
    guard = code_of('SpellEffectPresenterGuard308.cs')
    naked = [l.strip()[:60] for l in guard.splitlines() if re.search(r'_inner\.(Begin|Cue|End|EndAll)\(', l) and 'try' not in l]
    r.check('fix', wiring.count('.Restore(') == 1 and '.Restore(' in withdraw and 0 <= withdraw.find('Unschedule308(') < withdraw.find('.Recall(') < withdraw.find('.Restore(') and
            'SpellDispatchRule308.Run(ref steps)' in wiring and 'catch' not in method_body(wiring, 'CastRegistered308'),
            'the handler route has one refund (Withdraw), after the cast\'s scheduled hits and its held first-stage shot were taken back; the order is SpellDispatchRule308')
    r.check('fix', 'Fx308 => _fxGuard308;' in wiring and '_fxGuard308.Hold()' in wiring and '_fxGuard308.Release(true)' in wiring and '_fxGuard308.Release(false)' in wiring and
            wiring.count('_fx308') == 2 and guard and not naked,
            'handlers are handed the presenter guard only: held back while Commit runs, every call into the presenter inside its own guard', ' | '.join(naked))
    # d. the interim presenter never stretches a catalogue body and leaves nothing behind
    presenter = code_of('Vfx120SpellPresenter308.cs')
    r.check('fix', 'request.Duration;' not in presenter and 'SpellCueLife308.Seconds(request.Duration' in presenter and 'catch (Exception' in method_body(presenter, 'Begin') and
            'Release(live)' in method_body(presenter, 'Begin') and 'stack.Pop()' in method_body(presenter, 'EndAll'),
            'the interim presenter gives a body its cue life only (never the requested duration), destroys a half-made body, and destroys every profile copy on EndAll')
    # review C (2026-10-04): the same decisions, at the places the first pass left to a string that survives a sabotage
    # a. every answer to "is this final unlocked" passes through the rule it was handed (GrantedInMain of that rule), and
    #    the importer's normal-save report judges a row by its own rule
    # two files are called SpellTable308.cs (the table types and the editor importer): the importer is named by its folder
    importer_file = root / 'Editor/WorldMacro/SpellTable308.cs'
    session = code_of('WorldMacroPlaytestSession.Spells308.cs'); fixture = code_of('SpellFixture308.cs')
    importer = strip_comments(read(importer_file))[0] if importer_file.exists() else ''
    r.check('fix', 'SpellUnlockPolicy308.Unlocked(DemoCampaignActive, rule.GrantedInMain, ledger, evidence, test, Spell308IsolatedStore)' in session and
            'SpellUnlockPolicy308.TestUnlockAccepted(enabled, Application.isPlaying, Spell308IsolatedStore)' in session and
            'store.FilePath.EndsWith(Content.SaveSlot + Spell308TestSuffix +' in session and
            'SpellUnlockPolicy308.Unlocked(true, rule.GrantedInMain,' in fixture and
            'SpellUnlockPolicy308.RuleFor(row, rule).GrantedInMain' in method_body(importer, 'ReachOf') and
            'SpellGateMode.Test' in method_body(importer, 'NormalSaveChanges'),
            'the session, the fixture and the importer\'s normal-save report all judge a final by the rule they are handed (a TEST-only row stays locked behind a main-game proof)')
    # b. the first stage really is the base glyph's handler: the helper launches it, the wiring finds it
    primary = code_of('SpellEffectPrimary308.cs')
    r.check('fix', 'ctx.Primary.Launch(' in method_body(primary, 'Plan') and 'as ISpellPrimaryStage308' in method_body(wiring, 'PrimaryStage308') and
            'PrimaryShotRule308.BallisticsLetter(row)' in method_body(wiring, 'PrimaryStage308') and
            all('ISpellPrimaryStage308' in code_of(n) and 'SpellPrimaryShot308 Launch(' in code_of(n) for n in ('RangedSingleEffect308.cs', 'BallisticEffect308.cs', 'HomingEffect308.cs')),
            'SpellPrimary308.Plan launches the stage the wiring found for the body; the three base handlers are first stages')
    # ... and each of those stages keeps the clause that makes it miss (their Unity halves are not run offline)
    ranged_fx, fall_fx, guided_fx = code_of('RangedSingleEffect308.cs'), code_of('BallisticEffect308.cs'), code_of('HomingEffect308.cs')
    r.check('fix', '!RangedSingleRule308.Reaches(host.PlayerPosition, target.transform.position, ballistics.F(' in method_body(ranged_fx, 'SpellPrimaryShot308 Launch') and
            'return SpellPrimaryShot308.Nothing;' in method_body(ranged_fx, 'SpellPrimaryShot308 Launch') and
            'Vector3 fallPoint = target.transform.position;' in method_body(fall_fx, 'SpellPrimaryShot308 Launch') and
            'BallisticRule308.Lands(rock.FallPoint, target.transform.position, rock.Radius)' in method_body(fall_fx, 'void Tick') and
            'if (!there) { Misses++; continue; }' in method_body(fall_fx, 'void Tick') and
            'HomingRule308.Step(ref shot.State' in method_body(guided_fx, 'void Tick') and
            'if (status == HomingStatus308.Missed) { Misses++; continue; }' in method_body(guided_fx, 'void Tick'),
            'the three first stages keep their miss: beyond the range nothing is launched, the rock is judged around the spot it was thrown at, a guided shot that lost its target does not land')
    # c. the re-broadcasts presentation listens to cannot undo or cut short a judged cast on the handler route
    main = code_of('CombatLoopWiring.cs')
    planners = [method_body(wiring, 'ISpellCastHost.' + n) for n in ('PlanSingle', 'PlanCone', 'PlanCircle', 'PlanPath', 'PlanVolley')]
    judged = method_body(wiring, 'AcceptJudged308')
    r.check('fix', all('MutePlans308()' in b and 'finally { TellPlan308(listeners, plan); }' in b for b in planners) and
            'catch (Exception' in method_body(wiring, 'TellPlan308') and 'CastPlanned = listeners;' in method_body(wiring, 'TellPlan308') and
            'Accept() => _wiring.AcceptJudged308(' in wiring and 'catch (Exception' in judged and
            0 <= judged.find('GetInvocationList()') < judged.find('_spells308?.OnCastAccepted(') and
            'catch (Exception' in method_body(wiring, 'ISpellCastHost.PresentationOwned') and
            'if (!show) { _waiting.Clear(); return; }' in method_body(guard, 'Release') and
            re.search(r'try \{ EnemyDamageResolved\?\.Invoke\(result\); \}\s*finally \{ NotifyEnemyHit308\(result\); \}', main) is not None,
            'a CastPlanned / CastAccepted / EnemyDamageResolved listener that throws cannot withdraw a handler cast, skip its accept hooks or skip the hit hooks')
    r.check('fix', '_effect.Clear(SpellClearReason.Faulted)' in withdraw and withdraw.find('Unschedule308(') < withdraw.find('_effect.Clear(SpellClearReason.Faulted)') < withdraw.find('.Restore('),
            'a handler that threw inside Commit is reset before its ink comes back (no state of the withdrawn cast stays on)')
    # M4. what a modifier handler hung on an enemy is taken back when the fight is cleared (death, rest, disable)
    modifier_clear = method_body(code_of('ModifierEffect308.cs'), 'void Clear')
    r.check('fix', 'AtDrop(_pending[i].Enemy' in modifier_clear and 'AtClear(_carriers[i].Enemy)' in modifier_clear,
            'ModifierEffect308.Clear takes back what it set up for a hit on its way and what an enemy still carries')
    # WP-14: the sword form is a rule state. It is on before anything is shown, off before anything is told, and every
    # call it makes into the presenter or the input seam is guarded.
    sword = code_of('SwordFormEffect308.cs')
    commit, ending, clearing = method_body(sword, 'Commit'), method_body(sword, 'void End'), method_body(sword, 'void Clear')
    unguarded = [n for n in ('Cue', 'HoldSeam', 'DropSeam') if 'catch (Exception' not in method_body(sword, 'void ' + n)]
    r.check('fix', 0 <= commit.find('_active = true;') < commit.find('try') < commit.find('ctx.Fx.Begin(') and commit.rstrip().endswith('return true;\n        }') and
            'return false' not in commit and 0 <= ending.find('_active = false;') < ending.find('DropSeam(') < ending.find('fx.Cue(') and 'catch (Exception' in ending and
            '_active = false;' in clearing and 'DropSeam(false)' in clearing and 'StopListening()' in clearing and not unguarded and
            'IPlayerHitHook' not in sword and '.Gain(' not in sword and '.Heal(' not in sword,
            'the sword form never fails after it is on, ends as a rule state first, drops its input seam on every end, and has no player-hit hook and no gain', ' '.join(unguarded))
    seam = code_of('SwordFormSeam308.cs')
    r.check('fix', 'Oheangbu.Drawing' not in sword and '_drawingInput.EntryAllowed = _swordGate308' in seam and 'if (_swordHeld308) return false;' in method_body(seam, 'SwordDrawGate308') and
            not any(t in seam for t in ('InterruptLetter', 'HangulComposer', 'JamoTemplate', 'StrokeData', 'DrawnLetter', 'Recogni')),
            'the sword seam only shuts the draw mode\'s entry gate (no stroke is sampled while the form is held); it touches no recognition type')
    # M1. effects are built one by one; nothing in the registry or the installer can stop the scope
    installer_code = ''.join(strip_comments(read(p))[0] for p in files if p.name.startswith('SpellEffectInstaller308'))
    registry = code_of('SpellEffectRegistry308.cs')
    ids = {}
    for path in files:
        if path.name.endswith('Effect308.cs'):
            for m in re.finditer(r'const\s+string\s+HandlerId\s*=\s*"([^"]+)"', read(path)):
                ids.setdefault(m.group(1), []).append(path.name)
    shared = ['%s: %s' % (k, ' '.join(v)) for k, v in sorted(ids.items()) if len(v) > 1]
    r.check('fix', 'As<ISpellEffect>' not in installer_code and 'IEnumerable<ISpellEffect>>' not in installer_code and 'throw' not in registry and
            'Register<SpellEffectRegistry308>(effects.Build' in installer_code and not shared,
            'effect classes are registered by their own type and resolved one by one; the registry never throws; no two classes share a handler id', ' | '.join(shared))
    # WP-07: defence is data with default 0 and no enemy profile carries one yet, so every existing enemy takes the old damage
    profile_guid = (SCRIPTS / 'Combat/Enemy/EnemyVitalsProfileSO.cs.meta').read_text(encoding='utf-8').split('guid: ')[1].split()[0]
    profiles, armoured = 0, []
    for path in (PROJECT / "Assets/_Project").rglob('*.asset'):
        try:
            if path.stat().st_size > 200_000:
                continue
            text = path.read_text(encoding='utf-8', errors='ignore')
        except OSError:
            continue
        if profile_guid not in text[:3000]:
            continue
        profiles += 1
        m = re.search(r'^  _defence: (\S+)', text, re.M)
        if m and float(m.group(1)) != 0:
            armoured.append(path.name)
    r.note('enemyProfiles', profiles)
    r.check('fix', profiles > 0 and not armoured, 'no enemy profile asset carries a defence (%d profiles): with defence 0 the intake returns every hit untouched' % profiles, ' '.join(armoured))

    # ---- stage layout (the offline compile silently drops a staged file in a folder its assembly does not know) ----
    if not a.live:
        folders = set()
        ns = '{http://schemas.microsoft.com/developer/msbuild/2003}'
        for name in ASSEMBLIES:
            for e in ET.parse(PROJECT / (name + '.csproj')).getroot().iter(ns + 'Compile'):
                folders.add(Path(e.get('Include').replace(chr(92), '/')).parent.as_posix().lower())
        homeless = [p.relative_to(STAGE).as_posix() for p in files
                    if ('assets/_project/scripts/' + p.relative_to(STAGE).parent.as_posix()).lower().rstrip('/') not in folders]
        r.check('stage', not homeless, 'every staged file sits in a folder its assembly compiles', ' '.join(homeless))
        forbidden = [p.relative_to(STAGE).as_posix() for p in files if p.relative_to(STAGE).as_posix().startswith('Drawing/') or 'Hud' in p.name]
        r.check('stage', not forbidden, 'no staged file under Scripts/Drawing/ and no HUD file (A0)', ' '.join(forbidden))
        drift, replaced = [], 0
        deployed = all((SCRIPTS / p.relative_to(STAGE)).exists() and (SCRIPTS / p.relative_to(STAGE)).read_bytes() == p.read_bytes() for p in files)
        for orig in sorted((STAGE / 'Original').rglob('*.orig')):
            rel = orig.relative_to(STAGE / 'Original').as_posix()[:-5]
            replaced += 1
            # before the deploy the live file is the .orig; after it, it is the staged copy. Anything else is drift.
            if not (SCRIPTS / rel).exists() or ((SCRIPTS / rel).read_bytes() != orig.read_bytes() and
                                                 not ((STAGE / rel).exists() and (SCRIPTS / rel).read_bytes() == (STAGE / rel).read_bytes())):
                drift.append(rel)
            if not (STAGE / rel).exists():
                drift.append(rel + ' (no staged copy)')
        # (after the deploy a new file exists live as the staged copy itself: that is not a replaced file)
        existing_without_orig = [p.relative_to(STAGE).as_posix() for p in files
                                 if (SCRIPTS / p.relative_to(STAGE)).exists() and not (STAGE / 'Original' / (p.relative_to(STAGE).as_posix() + '.orig')).exists()
                                 and (SCRIPTS / p.relative_to(STAGE)).read_bytes() != p.read_bytes()]
        r.note('stage', {'files': len(files), 'replaceLive': replaced, 'new': len(files) - replaced})
        r.check('stage', not drift, 'the live files have not changed since the stage copied them (%d files)' % replaced + (' [deployed: live = stage]' if deployed else ''), ' '.join(drift))
        r.check('stage', not existing_without_orig, 'every staged file that replaces a live file has its Original/*.orig', ' '.join(existing_without_orig))

    # ---- data ----
    base_text, _ = base308.build()
    base_file = Path(sheets_dir) / 'Rules308_Base.csv'
    r.check('data', base_file.exists() and base_file.read_bytes() == (chr(0xFEFF) + base_text).encode('utf-8'),
            'Rules308_Base.csv reproduces from the book the main scene reads (spell120_base308.py)')
    entries = [base308.book_entries(ROOT / p) for p in CHAIN]
    r.check('data', all(e == entries[0] for e in entries) and len(entries[0]) == 20, 'the four books of the copy chain carry the same 20 entries')
    imported = []
    for p in CHAIN:
        text = (ROOT / p).read_text(encoding='utf-8')
        m = re.search(r'^  _tableHash: (\S*)', text, re.M)
        imported.append(m.group(1) if m else '')
    r.note('importedTableHash', dict(zip([Path(p).parent.parent.name for p in CHAIN], imported)))
    # a protected book is never an import target (SpellImportGuard308: the two copies inside protected trees, and since the
    # second guard pass the original book, which the protected scene W_Demo_Compact reads): it carries no table, ever.
    # The one target left is the Architecture296 copy.
    skipped = [p for p in CHAIN if table308.protected_path(p)]
    r.note('importTargets', {'targets': [Path(p).parent.parent.name for p in CHAIN if p not in skipped], 'skipped (protected)': [Path(p).parent.parent.name for p in skipped]})
    if [p for p in CHAIN if p not in skipped] != [MAIN_BOOK]:
        r.check('data', False, 'the one import target of the copy chain is the Architecture296 copy (the original book and the two tree copies are protected)',
                str([Path(p).parent.parent.name for p in CHAIN if p not in skipped]))
    imported_protected = [h for p, h in zip(CHAIN, imported) if p in skipped]
    imported = [h for p, h in zip(CHAIN, imported) if p not in skipped]
    if any(imported_protected):
        r.check('data', False, 'no protected book carries an imported table (the two tree copies, the original book)', str(imported_protected))
    if any(imported):
        # the editor import is a switch (spell308-import:only=..:skip=..): the books carry the table of the selection the
        # last import recorded, which is the whole folder only when no narrower selection is in force
        choice = table308.read_selection()
        expected = table308.load(sheets_dir, {k: v[1] for k, v in catalog.items()}, False, choice)['tableHash'] if choice else table['tableHash']
        r.note('importSelection', table308.selection_text(choice) + ('' if choice else ' (no selection recorded)'))
        r.check('data', all(h == expected for h in imported),
                'after deploy: the %d target book(s) (the unprotected of the four: the Architecture296 copy) carry the table the selected sheets build now (A8; selection: %s)' % (len(imported), table308.selection_text(choice)), str(imported))
    else:
        r.note('deploy', 'no book carries an imported table yet (expected before spell308-import)')
    summon = Path(sheets_dir) / 'SummonPower308.csv'
    rows = table308.parse_csv(summon.read_text(encoding='utf-8')) if summon.exists() else []
    summons = {x['letter'] for x in table['rows'] if x['category'] == 'Summon'}
    r.check('data', len(rows) == 6 and {x[0] for x in rows[1:]} == summons and all(table308.try_decimal(x[1]) is not None and table308.try_decimal(x[1]) > 0 for x in rows[1:]),
            'SummonPower308.csv gives the five summon glyphs a positive base power')

    payload = {'status': 'COMPLETE' if r.failed == 0 else 'FAILED', 'mode': ('live' if a.live else 'stage') + (' final' if a.final else ' foundation'),
               'checks': len(r.items), 'failed': r.failed, 'facts': r.facts, 'items': r.items}
    reports.mkdir(parents=True, exist_ok=True)
    (reports / 'contract308_report.json').write_bytes((json.dumps(payload, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    print('contract308 (%s): %d checks, %d failed' % (payload['mode'], len(r.items), r.failed))
    print('  rows %s' % info)
    print('  clauses: %s' % {k: v for k, v in r.facts['clauses'].items() if k != 'runner clause ids'})
    sys.exit(0 if r.failed == 0 else 1)


if __name__ == '__main__':
    main()
