"""SPEC-SPELL-120-308 L2: build and run the pure C# rule code outside Unity.

Sources compiled into one exe with Unity's Roslyn (references: netstandard.dll + UnityEngine.CoreModule.dll only):
  - every .cs whose first line is "// PURE308" (the stage copy wins over Assets/_Project/Scripts)
  - Core/Domain/*.cs, Combat/AreaGeometry.cs, Spellcraft/*.cs (stage copies win)
  - Tools/SpellVFX120/Pure308/*.cs (Program + Cases_*.cs)
The exe runs under dotnet. Usable inside: Vector3 / Mathf arithmetic and plain classes. Not usable (native calls throw):
Quaternion.AngleAxis, AnimationCurve, ScriptableObject / MonoBehaviour creation, Time, Debug, Physics.

What it proves: table build (and that the C# builder and the python mirror print the same table hash), resolve statuses,
the 36-glyph regression against the pre-#308 resolver over every spell book of the project, unlock policy, rule cases.
What it cannot see: anything that needs a Unity object (the effect classes' Unity half, the importer, the save flow).

Read-only against Oheangbu/Assets. Writes under Art/SpellVFX120/Spell120_308/ (pure308_report.json, Checks/equivalence308.*).

usage: python Tools/SpellVFX120/spell120_pure308.py [--sheets <dir>] [--stage <dir>] [--cases <dir>] [--out <dir>] [--live] [--keep]
  --live   compile the Assets copies only (after deploy); default prefers Tools/Unity/Stage308_spell
  --stage  another stage folder (a work package's private copy); its _ProjectAssets/Data/Spells sheets are used unless --sheets
  --cases  another folder of Program.cs + Cases_*.cs; --out another report folder (default Art/SpellVFX120/Spell120_308)
"""
import argparse, hashlib, json, os, re, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import spell120_table308 as table308

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / 'Oheangbu/Assets/_Project/Scripts'
ASSETS = ROOT / 'Oheangbu/Assets/_Project'
STAGE = ROOT / 'Tools/Unity/Stage308_spell'
CASES = ROOT / 'Tools/SpellVFX120/Pure308'
OUT = ROOT / 'Art/SpellVFX120/Spell120_308'
UNITY = Path('C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data')
CSC = UNITY / 'DotNetSdkRoslyn/csc.dll'
REFS = [UNITY / 'NetStandard/ref/2.1.0/netstandard.dll', UNITY / 'Managed/UnityEngine/UnityEngine.CoreModule.dll']
MAIN_BOOK_GUID = 'cf106cd4906cd294fb5743ccd65a2862'  # the Architecture296 copy W_Demo_Main reads
ENTRY_FIELDS = ['Kind', 'Element', 'BasePower', 'AreaShape', 'ProjectileSpeedMul', 'AreaAngle', 'AreaRadius', 'AreaLength', 'AreaSpeed',
                'AreaImpactDelay', 'VolleyShots', 'VolleyInterval', 'ScatterVolley']


def first_line(path):
    try:
        with open(path, encoding='utf-8-sig') as f:
            return f.readline().strip()
    except (OSError, UnicodeDecodeError):
        return ''


def sources(live, stage, cases):
    """relative path -> file. Fixed sets first, then every PURE308 file; the stage copy wins unless --live."""
    chosen = {}

    def add(rel, path):
        chosen.setdefault(rel.lower(), path)
    roots = ([] if live else [stage]) + [SCRIPTS]
    for root in roots:
        for sub, pattern in (('Core/Domain', '*.cs'), ('Combat', 'AreaGeometry.cs'), ('Spellcraft', '*.cs')):
            for path in sorted((root / sub).glob(pattern)):
                add(path.relative_to(root).as_posix(), path)
    for root in roots:
        for path in sorted(root.rglob('*.cs')):
            rel = path.relative_to(root).as_posix()
            if rel.startswith(('Original/', '_ProjectAssets/')) or rel.lower() in chosen:
                continue
            if first_line(path) == '// PURE308':
                add(rel, path)
    files = sorted(chosen.values(), key=lambda p: str(p).lower()) + sorted(cases.glob('*.cs'))
    return files


def unity_string(text):
    text = text.strip()
    if text.startswith('"'):
        return text.strip('"').encode('utf-8').decode('unicode_escape')
    return text


def find_books():
    """Every SpellBookSO asset of the project: [(relative path, guid, [(letter, {field: text})])]."""
    script = (SCRIPTS / 'Spellcraft/SpellBookSO.cs.meta').read_text(encoding='utf-8').split('guid: ')[1].split()[0]
    books = []
    for folder, _, names in os.walk(ASSETS):
        for name in names:
            if not name.endswith('.asset'):
                continue
            path = Path(folder) / name
            try:
                if path.stat().st_size > 2_000_000:
                    continue
                head = path.read_text(encoding='utf-8', errors='ignore')
            except OSError:
                continue
            if script not in head[:3000] or '  _entries:' not in head:
                continue
            entries = []
            for line in head.split('  _entries:', 1)[1].splitlines()[1:]:
                if line.startswith('  - Letter:'):
                    entries.append((unity_string(line.split(':', 1)[1]), {}))
                elif line.startswith('    ') and entries:
                    key, value = line.strip().split(':', 1)
                    entries[-1][1][key] = value.strip()
                elif line.strip():
                    break
            guid = Path(str(path) + '.meta').read_text(encoding='utf-8').split('guid: ')[1].split()[0]
            books.append((path.relative_to(ROOT).as_posix(), guid, entries))
    return sorted(books)


def write_books(path, books):
    lines = []
    for rel, guid, entries in books:
        lines.append('book\t%s\t%s' % (rel, '1' if guid == MAIN_BOOK_GUID else '0'))
        for letter, fields in entries:
            lines.append('\t'.join(['entry', letter] + [fields.get(k, '0') for k in ENTRY_FIELDS]))
    Path(path).write_bytes(('\n'.join(lines) + '\n').encode('utf-8'))


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser()
    ap.add_argument('--sheets'); ap.add_argument('--live', action='store_true'); ap.add_argument('--keep', action='store_true')
    ap.add_argument('--stage'); ap.add_argument('--cases'); ap.add_argument('--out')
    a = ap.parse_args()
    stage = Path(a.stage).resolve() if a.stage else STAGE
    cases = Path(a.cases).resolve() if a.cases else CASES
    out_dir = Path(a.out).resolve() if a.out else OUT
    staged_sheets = stage / '_ProjectAssets/Data/Spells'
    sheets_dir = Path(a.sheets) if a.sheets else (table308.LIVE_SHEETS if a.live else staged_sheets if any(staged_sheets.glob('Rules308_*.csv')) else table308.default_sheets())
    work = Path(tempfile.mkdtemp(prefix='pure308_'))
    files = sources(a.live, stage, cases)
    staged = sum(1 for f in files if stage in f.parents)
    print('sources: %d (%d from the stage, %d case files)' % (len(files), staged, len(list(cases.glob('*.cs')))))
    books = find_books()
    write_books(work / 'books.tsv', books)
    print('books: %d (%d entries)' % (len(books), sum(len(b[2]) for b in books)))
    if not any(b[1] == MAIN_BOOK_GUID for b in books):
        print('ERROR: the main-scene book copy (guid %s) was not found' % MAIN_BOOK_GUID); sys.exit(1)

    exe = work / 'Pure308.exe'
    rsp = ['-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-out:' + str(exe), '-langversion:9.0', '-deterministic', '-nowarn:0169;1701;1702;8632']
    rsp += ['-r:"%s"' % r for r in REFS] + ['"%s"' % f for f in files]
    (work / 'Pure308.rsp').write_text('\n'.join(rsp), encoding='utf-8')
    run = subprocess.run(['dotnet', str(CSC), '@' + str(work / 'Pure308.rsp')], capture_output=True, text=True, encoding='utf-8', errors='replace')
    errors = [l for l in run.stdout.splitlines() if ': error ' in l]
    print('compile: %s (%d errors)' % ('OK' if run.returncode == 0 else 'FAILED', len(errors)))
    for l in errors[:40]:
        print('  ' + l)
    if run.returncode != 0:
        sys.exit(1)
    (work / 'Pure308.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'rollForward': 'LatestMajor',
        'framework': {'name': 'Microsoft.NETCore.App', 'version': '6.0.0'}}}), encoding='utf-8')
    shutil.copyfile(REFS[1], work / REFS[1].name)
    out = work / 'out'
    run = subprocess.run(['dotnet', str(exe), str(ROOT), str(sheets_dir), str(work / 'books.tsv'), str(out)], capture_output=True, text=True,
                         encoding='utf-8', errors='replace')
    print(run.stdout.strip())
    if run.stderr.strip():
        print(run.stderr.strip()[:4000])
    report_path = out / 'pure308_report.json'
    if not report_path.exists():
        print('ERROR: the runner wrote no report'); sys.exit(1)
    report = json.loads(report_path.read_text(encoding='utf-8'))
    facts = report['facts']

    # the python mirror must build the same table text
    mirror = table308.load(sheets_dir)
    same_hash = mirror['tableHash'] == facts.get('tableHash') and mirror['sourceHash'] == facts.get('sourceHash')
    text_same = (out / 'pure308_table.txt').exists() and (out / 'pure308_table.txt').read_text(encoding='utf-8').rstrip('\n') == mirror['canonical']
    print('table hash: C# %s / python %s -> %s' % (facts.get('tableHash', '?')[:16], mirror['tableHash'][:16], 'same' if same_hash and text_same else 'DIFFERENT'))
    report['pythonMirror'] = {'tableHash': mirror['tableHash'], 'sourceHash': mirror['sourceHash'], 'same': bool(same_hash and text_same)}
    report['sheetsDir'] = sheets_dir.relative_to(ROOT).as_posix() if sheets_dir.is_relative_to(ROOT) else str(sheets_dir)
    report['sources'] = [f.relative_to(ROOT).as_posix() if f.is_relative_to(ROOT) else str(f) for f in files]
    report['books'] = [{'path': b[0], 'guid': b[1], 'entries': len(b[2]), 'main': b[1] == MAIN_BOOK_GUID} for b in books]
    by_clause = {}
    for item in report['items']:
        state = by_clause.setdefault(item['clause'], {'ok': 0, 'failed': 0})
        state['ok' if item['ok'] else 'failed'] += 1
    report['clauses'] = by_clause
    failed = report['failed'] + (0 if same_hash and text_same else 1)
    report['status'] = 'COMPLETE' if failed == 0 else 'FAILED'
    out_dir.mkdir(parents=True, exist_ok=True); (out_dir / 'Checks').mkdir(exist_ok=True)
    (out_dir / 'pure308_report.json').write_bytes((json.dumps(report, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    if (out / 'equivalence308.tsv').exists():
        shutil.copyfile(out / 'equivalence308.tsv', out_dir / 'Checks' / 'equivalence308.tsv')
    print('wrote', out_dir / 'pure308_report.json')
    print('checks %d, failed %d, clauses %d' % (report['checks'], failed, len(by_clause)))
    for key in ('equivalenceCases', 'equivalenceMismatches', 'equivalenceOldResolved', 'equivalenceNewResolved', 'statusAllOpen', 'statusAllShut'):
        print('  %s = %s' % (key, facts.get(key)))
    if not a.keep:
        shutil.rmtree(work, ignore_errors=True)
    else:
        print('kept', work)
    sys.exit(0 if failed == 0 else 1)


if __name__ == '__main__':
    main()
