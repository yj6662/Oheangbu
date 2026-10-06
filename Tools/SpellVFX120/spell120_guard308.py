"""SPEC-SPELL-120-308: the importer never writes a protected asset (2026-10-04; second pass the same day).

Protected (project rule): asset folders and scene roots Watershed295*, Reworld292*, MountainTrail285*, the scene
W_Demo_Compact.unity, 03_Content.asset - and, second pass, what the protected scene W_Demo_Compact.unity reads: the original
spell book Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset and the summon profiles under
Assets/_Project/Art/Demo/Summons/. The one book left as an import target is the Architecture296 copy (the book W_Demo_Main
reads). The editor importer (Editor/WorldMacro/SpellTable308.cs) asks SpellImportGuard308
(Editor/WorldMacro/SpellImportGuard308.cs, plain string logic) which books and summon profiles it may write. This tool fails
if a protected path can become a write target:

  run     compiles SpellImportGuard308.cs alone (Unity's Roslyn, netstandard only) and runs it under dotnet on the real
          candidates of the project (the four spell books of the copy chain, every SummonCombatProfile asset) and on
          synthetic paths; its verdicts must equal an independent python judgement written from the project rule, and the
          named cases (MUST_REFUSE / MUST_WRITE) must come out as listed
  mirror  the C# lists and spell120_table308.PROTECTED_TREES / PROTECTED_FILES / PROTECTED_PATHS are the same lists
  static  in the importer source every asset write (SetDirty / SaveAssetIfDirty) follows a SpellImportGuard308.Demand
          of the same method, Targets() is the guard's Writable list, SaveAssets is not called, and the import still
          refuses when the book W_Demo_Main reads is not a target (the original book no longer has to be one)
  disk    no protected book carries an imported table; no file the importer may write is referenced by a protected file
          (guid search in W_Demo_Compact.unity / 03_Content.asset); with --before <SHA1_BEFORE.txt> the protected books and
          profiles still have the sha1 of the backup

Read-only against Oheangbu/Assets.
usage: python Tools/SpellVFX120/spell120_guard308.py [--live] [--stage <dir>] [--before <file>] [--out <dir>]
  --live   judge the copies under Assets/_Project/Scripts instead of the stage
  --stage  another stage folder (its Editor/WorldMacro/SpellImportGuard308.cs and SpellTable308.cs are judged)
  --out    another report folder (default Art/SpellVFX120/Spell120_308)
"""
import argparse, hashlib, json, os, posixpath, re, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import spell120_table308 as table308

ROOT = table308.repo_root(__file__)
ASSETS = ROOT / 'Oheangbu/Assets/_Project'
SCRIPTS = ASSETS / 'Scripts'
STAGE = ROOT / 'Tools/Unity/Stage308_spell'
OUT = ROOT / 'Art/SpellVFX120/Spell120_308'
UNITY = Path('C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data')
CSC = UNITY / 'DotNetSdkRoslyn/csc.dll'
REF = UNITY / 'NetStandard/ref/2.1.0/netstandard.dll'
ORIGINAL_BOOK = 'Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset'
COPY_NAME = '607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset'
MAIN_BOOK_GUID = 'cf106cd4906cd294fb5743ccd65a2862'   # the Architecture296 copy W_Demo_Main reads
SUMMONS_FOLDER = 'Assets/_Project/Art/Demo/Summons/'
# The project rule, written here a second time on purpose (not imported from the mirror): the independent judgement.
RULE_TREES = ('Watershed295', 'Reworld292', 'MountainTrail285')
RULE_FILES = ('W_Demo_Compact.unity', '03_Content.asset')
RULE_PATHS = ('Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset', 'Assets/_Project/Art/Demo/Summons/')
RULE_LABEL = 'SKIPPED (protected)'

MAIN_CS = r'''
using System; using System.Collections.Generic; using System.IO;
using Oheangbu.EditorTools.WorldMacro;
static class GuardMain
{
    static int Main(string[] args)
    {
        var paths = File.ReadAllLines(args[0]);
        var skipped = new List<string>();
        var open = SpellImportGuard308.Writable(paths, skipped);
        var o = new List<string>();
        o.Add("TREES " + string.Join("|", SpellImportGuard308.ProtectedTrees));
        o.Add("FILES " + string.Join("|", SpellImportGuard308.ProtectedFiles));
        o.Add("PATHS " + string.Join("|", SpellImportGuard308.ProtectedPaths));
        o.Add("LABEL " + SpellImportGuard308.SkipLabel);
        foreach (string p in paths) o.Add((SpellImportGuard308.Protected(p) ? "P " : "W ") + p);
        foreach (string p in open) o.Add("OPEN " + p);
        foreach (string p in skipped) o.Add("SKIP " + p);
        foreach (string p in paths)
        {
            bool threw = false;
            try { SpellImportGuard308.Demand(p); } catch (InvalidOperationException) { threw = true; }
            o.Add((threw ? "DEMAND-THROWS " : "DEMAND-PASSES ") + p);
        }
        o.Add("EMPTY " + (SpellImportGuard308.Protected("") && SpellImportGuard308.Protected(null) ? "protected" : "open"));
        File.WriteAllLines(args[1], o);
        return 0;
    }
}
'''


def rule_protected(path):
    """The project rule, independently: a folder or file name that starts with a protected tree name, a protected file name,
    or a protected path (the one file, or anything under the folder), letter case ignored, wherever the path is rooted."""
    parts = [s for s in re.split(r'[\\/]', path)]
    if any(s.lower().startswith(t.lower()) for s in parts for t in RULE_TREES) or parts[-1].lower() in [f.lower() for f in RULE_FILES]:
        return True
    clean = posixpath.normpath('/' + path.replace('\\', '/')).lower() + '/'
    for entry in RULE_PATHS:
        anchored = '/' + entry.lower()
        if (anchored in clean) if entry.endswith('/') else clean.endswith(anchored + '/'):
            return True
    return False


def find_candidates():
    """(books of the copy chain, summon profiles, the protected files that exist) as Unity asset paths (Assets/...)."""
    profile_script = (SCRIPTS / 'App/Demo/SummonCombatProfile.cs.meta').read_text(encoding='utf-8').split('guid: ')[1].split()[0]
    books, profiles, holders = [], [], []
    for folder, _, names in os.walk(ASSETS):
        for name in names:
            path = Path(folder) / name
            unity = 'Assets/_Project/' + path.relative_to(ASSETS).as_posix()
            if name.lower() in [f.lower() for f in RULE_FILES]:
                holders.append(unity)
            if not name.endswith('.asset'):
                continue
            if unity == ORIGINAL_BOOK or name == COPY_NAME or name.endswith('_' + COPY_NAME):
                books.append(unity); continue
            try:
                if path.stat().st_size > 200_000:
                    continue
                head = path.read_text(encoding='utf-8', errors='ignore')[:3000]
            except OSError:
                continue
            if 'guid: ' + profile_script in head:
                profiles.append(unity)
    return sorted(books), sorted(profiles), sorted(holders)


def guid_of(unity_path):
    meta = ROOT / 'Oheangbu' / (unity_path + '.meta')
    m = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8'), re.M) if meta.exists() else None
    return m.group(1) if m else ''


# The named cases of the second pass (2026-10-04): what must be refused and what must stay writable, spelled out.
MUST_REFUSE = [
    'Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset',
    'ASSETS/_PROJECT/DATA/WORLD/SPELLBOOK_WORLDMACROSUMMON_TEST.ASSET',
    'Assets\\_Project\\Data\\World\\SpellBook_WorldMacroSummon_TEST.asset',
    'Oheangbu/Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset',
    'Assets/_Project/Art/Demo/Summons/x.asset',
    'Assets/_Project/Art/Demo/Summons/WoodDeer/Combat_Gom.asset',
    'Assets/_Project/Art/Demo/Summons/WoodDeer/Sub/Deep/x.asset',
    'assets/_project/art/demo/summons/firehaetae/combat_nom.asset',
    'Assets\\_Project\\Art\\Demo\\Summons\\FireHaetae\\Combat_Nom.asset',
    'Assets/_Project/Art/Demo/Other/../Summons/x.asset',
    'Assets/_Project//Art/./Demo/Summons/x.asset',
    'C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/Demo/Summons/WoodDeer/Combat_Gom.asset',
]
MUST_WRITE = [
    'Assets/_Project/Art/World/Architecture296/Data/x.asset',
    'Assets/_Project/Art/World/Architecture296/Data/dde80d292e16ddb48a9102e5bcae089d_17a09b6fa2e231f41a18e15962f1a2c4_' + COPY_NAME,
    'Assets/_Project/Data/Demo/Summons/x.asset',
    'Assets/_Project/Art/World/Architecture296/Summons/x.asset',
    'Assets/_Project/Art/Demo/SummonsOld/x.asset',
    'Assets/_Project/Art/Demo/Summons.asset',
    'Assets/_Project/Art/Demo/x.asset',
    'Assets/_Project/Data/World/x.asset',
    'Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST_Copy.asset',
    'Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset.meta',
    'Assets/_Project/Art/World/MountainIntegration290/Data/Animation273/WoodDeer.asset',
    'Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/Animation273/WoodDeer.asset',
]
SYNTHETIC = [
    'Assets/_Project/Art/World/Reworld292/Data/x.asset', 'Assets/_Project/Art/World/Reworld292_Backup/x.asset',
    'Assets\\_Project\\Art\\World\\Watershed295\\Data\\x.asset', 'Assets/_Project/Art/World/watershed295/x.asset',
    'Assets/_Project/Art/World/MountainTrail285/Data/x.asset', 'Assets/_Project/Art/World/MountainTrail285b/x.asset',
    'Assets/_Project/Scenes/World/W_Demo_Compact.unity', 'Assets/_Project/Data/WorldCompact/03_Content.asset',
    'Assets/_Project/Art/World/Any/Reworld292_SpellBook.asset', 'Assets/_Project/Scenes/World/Watershed295_Root.unity',
    'Assets/_Project/Art/World/MyReworld292/x.asset', 'Assets/_Project/Scenes/World/W_Demo_Main.unity',
] + MUST_REFUSE + MUST_WRITE


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('--live', action='store_true'); ap.add_argument('--stage'); ap.add_argument('--before')
    ap.add_argument('--out')
    a = ap.parse_args()
    stage = Path(a.stage).resolve() if a.stage else STAGE
    root = SCRIPTS if a.live else stage
    guard = root / 'Editor/WorldMacro/SpellImportGuard308.cs'
    importer = root / 'Editor/WorldMacro/SpellTable308.cs'
    items = []

    def check(group, ok, text, detail=''):
        items.append({'group': group, 'ok': bool(ok), 'text': text, 'detail': detail})
        if not ok:
            print('FAIL [%s] %s%s' % (group, text, (' :: ' + detail) if detail else ''))
        return ok

    if not guard.exists() or not importer.exists():
        check('run', False, 'the guard and the importer exist', str(guard)); return finish(items, a)
    books, profiles, holders = find_candidates()
    paths = books + profiles + [p for p in SYNTHETIC if p not in books and p not in profiles]

    # ---- run: the real C# on the real candidates ----
    work = Path(tempfile.mkdtemp(prefix='guard308_'))
    try:
        (work / 'Main.cs').write_text(MAIN_CS, encoding='utf-8')
        (work / 'paths.txt').write_text('\n'.join(paths) + '\n', encoding='utf-8')
        exe = work / 'Guard308.exe'
        cmd = ['dotnet', str(CSC), '-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-out:' + str(exe), '-langversion:9.0', '-r:' + str(REF), str(guard), str(work / 'Main.cs')]
        run = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
        if not check('run', run.returncode == 0, 'SpellImportGuard308.cs compiles alone (no Unity type) and has the three lists', run.stdout[-600:]):
            return finish(items, a)
        (work / 'Guard308.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'rollForward': 'LatestMajor',
            'framework': {'name': 'Microsoft.NETCore.App', 'version': '6.0.0'}}}), encoding='utf-8')
        run = subprocess.run(['dotnet', str(exe), str(work / 'paths.txt'), str(work / 'out.txt')], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if not check('run', run.returncode == 0 and (work / 'out.txt').exists(), 'the guard runs', (run.stdout + run.stderr)[-600:]):
            return finish(items, a)
        lines = (work / 'out.txt').read_text(encoding='utf-8-sig').splitlines()
    finally:
        shutil.rmtree(work, ignore_errors=True)
    verdict = {l[2:]: l[0] == 'P' for l in lines if l[:2] in ('P ', 'W ')}
    opened = [l[5:] for l in lines if l.startswith('OPEN ')]
    skipped = [l[5:] for l in lines if l.startswith('SKIP ')]
    throws = {l.split(' ', 1)[1] for l in lines if l.startswith('DEMAND-THROWS ')}
    trees = next(l[6:] for l in lines if l.startswith('TREES ')).split('|')
    files = next(l[6:] for l in lines if l.startswith('FILES ')).split('|')
    listed = next(l[6:] for l in lines if l.startswith('PATHS ')).split('|')
    label = next(l[6:] for l in lines if l.startswith('LABEL '))

    wrong = [p for p in paths if verdict.get(p) != rule_protected(p)]
    check('run', not wrong, 'the C# guard judges every candidate as the project rule does (%d paths: %d books, %d profiles, %d synthetic)' % (len(paths), len(books), len(profiles), len(paths) - len(books) - len(profiles)), ' | '.join(wrong[:6]))
    named = ['refused expected: ' + p for p in MUST_REFUSE if verdict.get(p) is not True] + ['writable expected: ' + p for p in MUST_WRITE if verdict.get(p) is not False]
    check('run', not named, 'the named cases: the original book and every path under Art/Demo/Summons are refused (%d), another Summons folder, a neighbour '
          'file and the Architecture296 copy stay writable (%d)' % (len(MUST_REFUSE), len(MUST_WRITE)), ' | '.join(named[:6]))
    leaked = [p for p in opened if rule_protected(p)]
    check('run', not leaked, 'no protected path is in the list the importer may write (Writable)', ' | '.join(leaked[:6]))
    check('run', sorted(opened + skipped) == sorted(paths) and all(rule_protected(p) for p in skipped), 'every candidate is either writable or skipped, and only protected ones are skipped')
    check('run', throws == {p for p in paths if rule_protected(p)}, 'Demand throws for exactly the protected paths (the statement in front of every write)')
    check('run', 'EMPTY protected' in lines, 'a target without a path is never written')
    book_open = [p for p in books if p in opened]; book_skip = [p for p in books if p in skipped]
    prof_open = [p for p in profiles if p in opened]; prof_skip = [p for p in profiles if p in skipped]
    main_book = [p for p in books if guid_of(p) == MAIN_BOOK_GUID]
    check('run', len(books) == 4 and len(main_book) == 1 and book_open == main_book and ORIGINAL_BOOK in book_skip,
          'the chain has four books; the one import target is the copy W_Demo_Main reads; the original book is skipped', 'targets ' + str(book_open))
    demo = [p for p in profiles if p.startswith(SUMMONS_FOLDER)]
    check('run', demo and all(p in prof_skip for p in demo) and prof_open and not any(p.startswith(SUMMONS_FOLDER) for p in prof_open),
          'every summon profile under Art/Demo/Summons is skipped (%d) and the profiles of the other unprotected folders stay targets (%d)' % (len(demo), len(prof_open)))

    # ---- mirror ----
    check('mirror', tuple(trees) == tuple(table308.PROTECTED_TREES) == RULE_TREES and tuple(files) == tuple(table308.PROTECTED_FILES) == RULE_FILES and
          tuple(listed) == tuple(table308.PROTECTED_PATHS) == RULE_PATHS and label == table308.SKIP_LABEL == RULE_LABEL,
          'the C# lists, the python mirror (spell120_table308) and the project rule name the same trees, files and paths, and the same skip label', '%s %s %s %s' % (trees, files, listed, label))
    check('mirror', all(table308.protected_path(p) == rule_protected(p) for p in paths), 'the python mirror judges every candidate as the project rule does',
          ' | '.join(p for p in paths if table308.protected_path(p) != rule_protected(p))[:400])

    # ---- static: the importer source ----
    code = importer.read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    body = '\n'.join(l for l in code.split('\n') if not l.strip().startswith('//'))
    check('static', 'internal static List<string> Targets() => SpellImportGuard308.Writable(Chain(), null);' in body, 'Targets() is the guard\'s Writable list of the chain')
    check('static', 'AssetDatabase.SaveAssets(' not in body and 'AssetDatabase.CreateAsset(' not in body and 'AssetDatabase.MoveAsset(' not in body and 'AssetDatabase.DeleteAsset(' not in body,
          'the importer never calls SaveAssets / CreateAsset / MoveAsset / DeleteAsset')
    rows = body.split('\n'); unguarded = []; writes = 0
    for i, line in enumerate(rows):
        if 'EditorUtility.SetDirty(' in line or 'AssetDatabase.SaveAssetIfDirty(' in line:
            writes += 1
            # walk back to the start of the method: a Demand must stand between the method head and this write
            j, seen = i, 'SpellImportGuard308.Demand(' in line and line.index('SpellImportGuard308.Demand(') < max(line.find('EditorUtility.SetDirty('), line.find('AssetDatabase.SaveAssetIfDirty('))
            while j > 0 and not seen:
                j -= 1
                if 'SpellImportGuard308.Demand(' in rows[j]:
                    seen = True
                if re.match(r'^        (static|internal static|public static|private static) ', rows[j]):
                    break
            if not seen:
                unguarded.append('line %d: %s' % (i + 1, line.strip()[:80]))
    check('static', writes >= 6 and not unguarded, 'every asset write of the importer (SetDirty / SaveAssetIfDirty, %d sites) follows a SpellImportGuard308.Demand in its method' % writes, ' | '.join(unguarded[:6]))
    check('static', body.count('SpellImportGuard308.SkipLabel') >= 5 and '(protected tree)' not in body and body.count('skipped (protected)') >= 3,
          'import, revert, verify, summon-power and report name what they skip with the guard\'s label ("%s"), and the closing lines say "skipped (protected)"' % RULE_LABEL)
    check('static', 'targets[0] != OriginalBook' not in body and 'W_Demo_Main references none of the' in body and 'is the book W_Demo_Main reads' in body and
          'if (targets.Count == 0)' in body and 'if (carries) bad++;' in body,
          'the import no longer needs the original book as a target, still refuses when no target is the book W_Demo_Main reads (import and verify), '
          'refuses an import without any target, and verify fails on a skipped book that carries a table')
    other = []
    for p in sorted((root / 'Editor/WorldMacro').glob('Spell*308*.cs')):
        if p.name in ('SpellTable308.cs', 'SpellImportGuard308.cs'):
            continue
        text = '\n'.join(l for l in p.read_text(encoding='utf-8-sig').split('\n') if not l.strip().startswith('//'))
        if re.search(r'AssetDatabase\.(SaveAssets|SaveAssetIfDirty|CreateAsset|ImportAsset|MoveAsset|DeleteAsset)\(|EditorUtility\.SetDirty\(|EditorSceneManager\.(SaveScene|SaveOpenScenes|MarkSceneDirty)\(', text):
            other.append(p.name)
    check('static', not other, 'no other #308 editor file of the stage writes an asset or saves a scene', ' '.join(other))

    # ---- disk ----
    carries = []
    for p in book_skip:
        m = re.search(r'^  _tableHash: (\S+)', (ROOT / 'Oheangbu' / p).read_text(encoding='utf-8'), re.M)
        if m:
            carries.append(p)
    check('disk', not carries, 'no protected book carries an imported table (the original book included)', ' | '.join(carries))
    # what the protected files read (a guid search: direct references only): nothing the importer may write
    guids = {p: guid_of(p) for p in books + profiles}
    referenced = {}
    for holder in holders:
        data = (ROOT / 'Oheangbu' / holder).read_bytes()
        referenced[holder] = [p for p in books + profiles if guids[p] and guids[p].encode('ascii') in data]
    read_and_open = sorted({p for found in referenced.values() for p in found if p in opened})
    read_total = sorted({p for found in referenced.values() for p in found})
    check('disk', any(h.lower().endswith('/w_demo_compact.unity') for h in holders) and all(guids.values()) and not read_and_open,
          'no book or profile the importer may write is referenced by a protected file (%s reference %d of the %d candidates, every one skipped)' % (
              ' + '.join(posixpath.basename(h) for h in holders), len(read_total), len(books + profiles)), ' | '.join(read_and_open[:6]))
    if a.before:
        before = {}
        for line in Path(a.before).read_text(encoding='utf-8-sig').splitlines():
            m = re.match(r'^([0-9a-f]{40})\s+\*?(.+)$', line.strip())
            if m:
                before[m.group(2).replace('\\', '/')] = m.group(1)
        changed = []; compared = 0
        for p in book_skip + prof_skip:
            key = next((k for k in before if k.endswith(p)), None)
            if key is None:
                continue
            compared += 1
            if hashlib.sha1((ROOT / 'Oheangbu' / p).read_bytes()).hexdigest() != before[key]:
                changed.append(p)
        check('disk', compared == len(book_skip + prof_skip) and not changed, 'the protected books and profiles have the sha1 of the backup (%d compared)' % compared, ' | '.join(changed[:6]))

    print('books: %d targets, %d skipped (protected)' % (len(book_open), len(book_skip)))
    for p in book_open: print('  TARGET  ' + p)
    for p in book_skip: print('  SKIPPED ' + p)
    print('summon profiles: %d targets, %d skipped (protected)' % (len(prof_open), len(prof_skip)))
    by = {}
    for p in profiles:
        key = ('SKIPPED ' if p in prof_skip else 'TARGET  ') + '/'.join(p.split('/')[:-1])
        by[key] = by.get(key, 0) + 1
    for key in sorted(by): print('  %s  x%d' % (key, by[key]))
    for holder in holders: print('referenced by %s: %d of the candidates (%d of them import targets)' % (holder, len(referenced[holder]), sum(1 for p in referenced[holder] if p in opened)))
    return finish(items, a, {'books': {'targets': book_open, 'skipped': book_skip}, 'profiles': {'targets': prof_open, 'skipped': prof_skip},
                             'referencedByProtectedFiles': referenced})


def finish(items, a, facts=None):
    failed = sum(1 for i in items if not i['ok'])
    payload = {'status': 'COMPLETE' if failed == 0 else 'FAILED', 'mode': 'live' if a.live else 'stage', 'checks': len(items), 'failed': failed, 'facts': facts or {}, 'items': items}
    out = Path(a.out).resolve() if a.out else OUT
    out.mkdir(parents=True, exist_ok=True)
    (out / 'guard308_report.json').write_bytes((json.dumps(payload, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    print('guard308 (%s): %d checks, %d failed' % (payload['mode'], len(items), failed))
    sys.exit(0 if failed == 0 else 1)


if __name__ == '__main__':
    main()
