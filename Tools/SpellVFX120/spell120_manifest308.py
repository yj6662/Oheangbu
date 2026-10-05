"""SPEC-SPELL-120-308: the deployment manifest of the stage Tools/Unity/Stage308_spell, and the copy that follows it.

The stage mirrors Oheangbu/Assets/_Project/Scripts (its .cs files) and Oheangbu/Assets/_Project (its _ProjectAssets folder).
A partial copy does not compile, so the copy is one command and runs in a fixed order:
  new      .cs files that do not exist in the project yet
  replace  .cs files that replace a live file (their pre-deploy text is Original/<path>.orig)
  shared   the replaced files the spell deploy layer (SPEC-SPELL-DEPLOY-308) edits too; copied last
  sheet    the rule sheets, to Data/Spells (they reach the game only through spell308-import)

usage: python Tools/SpellVFX120/spell120_manifest308.py                      write DEPLOY_MANIFEST308.tsv into the stage
       ... --check                                                          the manifest equals the stage, every live file a
                                                                            replace/shared row overwrites still equals its .orig
       ... --backup <dir> --target <_Project dir>                           copy the live files that will be replaced into <dir>
       ... --deploy --target <_Project dir>                                 copy everything (refuses when --check fails)
       ... --revert <backup dir> --target <_Project dir>                    put the backup back and delete what the deploy added
--target is always spelled out (the real one is Oheangbu/Assets/_Project); nothing here has a default that writes into Assets.
Run spell308-revert in the editor BEFORE --revert when a table was imported (the importer lives in the code this removes).
"""
import argparse, hashlib, shutil, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / 'Tools/Unity/Stage308_spell'
LIVE = ROOT / 'Oheangbu/Assets/_Project'
MANIFEST = 'DEPLOY_MANIFEST308.tsv'
SHARED = {'App/CombatLifetimeScope.cs', 'App/BrushStrokeFeedAdapter.cs', 'App/CombatLoopWiring.cs'}
ORDER = {'new': 0, 'replace': 1, 'shared': 2, 'sheet': 3}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def scan(stage):
    """[(group, target path under _Project, source path under the stage, sha)] in copy order."""
    rows = []
    for p in stage.rglob('*.cs'):
        rel = p.relative_to(stage).as_posix()
        if rel.startswith('Original/') or rel.startswith('_ProjectAssets/'):
            continue
        group = 'shared' if rel in SHARED else 'replace' if (stage / 'Original' / (rel + '.orig')).exists() else 'new'
        rows.append((group, 'Scripts/' + rel, rel, sha(p)))
    for p in (stage / '_ProjectAssets/Data/Spells').glob('*.csv'):
        rows.append(('sheet', 'Data/Spells/' + p.name, '_ProjectAssets/Data/Spells/' + p.name, sha(p)))
    rows.sort(key=lambda r: (ORDER[r[0]], r[1]))
    return rows


def read_manifest(stage):
    path = stage / MANIFEST
    if not path.exists():
        return None
    rows = []
    for line in path.read_text(encoding='utf-8').splitlines():
        if line and not line.startswith('#'):
            rows.append(tuple(line.split('\t')))
    return rows


def write_manifest(stage, rows):
    counts = {g: sum(1 for r in rows if r[0] == g) for g in ORDER}
    head = ['# SPEC-SPELL-120-308 deployment manifest (spell120_manifest308.py). group, target under Oheangbu/Assets/_Project, source under the stage, sha256[:16]',
            '# copy order: new %(new)d -> replace %(replace)d -> shared %(shared)d (also edited by SPEC-SPELL-DEPLOY-308) -> sheet %(sheet)d' % counts]
    (stage / MANIFEST).write_bytes(('\n'.join(head + ['\t'.join(r) for r in rows]) + '\n').encode('utf-8'))
    return counts


def check(stage, live, quiet=False):
    problems = []
    rows = scan(stage)
    manifest = read_manifest(stage)
    if manifest is None:
        problems.append('no ' + MANIFEST + ' in the stage (run this tool without arguments)')
    elif manifest != rows:
        was, now = {r[1]: r for r in manifest}, {r[1]: r for r in rows}
        for key in sorted(set(was) | set(now)):
            if was.get(key) != now.get(key):
                problems.append('manifest is stale: ' + key)
    for group, target, source, _ in rows:
        if group in ('replace', 'shared'):
            orig = stage / 'Original' / (source + '.orig')
            there = live / target
            if not there.exists():
                problems.append('live file missing: ' + target)
            elif there.read_bytes() != orig.read_bytes():
                problems.append('LIVE CHANGED since the stage copied it: ' + target + (' (shared with the deploy layer: re-apply Original/SHARED_HUNKS308.diff on the new live file)' if group == 'shared' else ''))
        elif group == 'new' and (live / target).exists() and (live / target).read_bytes() != (stage / source).read_bytes():
            problems.append('a file the stage calls new already exists with other content: ' + target)
    if not quiet:
        counts = {g: sum(1 for r in rows if r[0] == g) for g in ORDER}
        print('stage %s: new %d, replace %d, shared %d, sheet %d' % (stage.relative_to(ROOT).as_posix() if stage.is_relative_to(ROOT) else stage,
                                                                    counts['new'], counts['replace'], counts['shared'], counts['sheet']))
        for line in problems:
            print('PROBLEM', line)
        print('CHECK ' + ('ok' if not problems else 'FAILED (%d)' % len(problems)))
    return rows, problems


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true'); ap.add_argument('--backup'); ap.add_argument('--deploy', action='store_true')
    ap.add_argument('--revert'); ap.add_argument('--target'); ap.add_argument('--stage'); ap.add_argument('--live')
    a = ap.parse_args()
    stage = Path(a.stage).resolve() if a.stage else STAGE
    live = Path(a.live).resolve() if a.live else LIVE          # what --check compares the .orig files with
    if not (a.check or a.backup or a.deploy or a.revert):
        counts = write_manifest(stage, scan(stage))
        print('wrote %s: new %d, replace %d, shared %d, sheet %d' % ((stage / MANIFEST).as_posix(), counts['new'], counts['replace'], counts['shared'], counts['sheet']))
        return 0
    if a.check and not (a.backup or a.deploy or a.revert):
        return 1 if check(stage, live)[1] else 0
    if not a.target:
        print('refused: --backup, --deploy and --revert need --target <the _Project folder> spelled out'); return 2
    target = Path(a.target).resolve()
    if a.backup:
        rows, problems = check(stage, target if not a.live else live)
        backup = Path(a.backup).resolve(); n = 0
        for group, rel, source, _ in rows:
            if group in ('replace', 'shared') and (target / rel).exists():
                (backup / rel).parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(target / rel, backup / rel); n += 1
        (backup / 'ADDED_BY_DEPLOY.txt').write_bytes(('\n'.join(r[1] for r in rows if r[0] in ('new', 'sheet')) + '\n').encode('utf-8'))
        print('backup: %d live files copied to %s; ADDED_BY_DEPLOY.txt lists the %d files the deploy adds' % (n, backup.as_posix(), sum(1 for r in rows if r[0] in ('new', 'sheet'))))
        return 0
    if a.deploy:
        rows, problems = check(stage, target)
        if problems:
            print('refused: nothing was copied'); return 1
        n = 0
        for group, rel, source, digest in rows:
            if group == 'shared' and (target / rel).read_bytes() != (stage / 'Original' / (source + '.orig')).read_bytes():
                print('STOPPED before %s: the live file changed during the copy. %d files are already copied: finish by hand or --revert.' % (rel, n)); return 1
            (target / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(stage / source, target / rel); n += 1
        bad = [rel for group, rel, source, digest in rows if sha(target / rel) != digest]
        print('deploy: %d files copied to %s%s' % (n, target.as_posix(), '' if not bad else '; MISMATCH after copy: ' + ' '.join(bad)))
        return 1 if bad else 0
    if a.revert:
        backup = Path(a.revert).resolve()
        listed = backup / 'ADDED_BY_DEPLOY.txt'
        if not listed.exists():
            print('refused: %s has no ADDED_BY_DEPLOY.txt (not a backup of this tool)' % backup.as_posix()); return 2
        restored = removed = 0
        for path in backup.rglob('*.cs'):
            rel = path.relative_to(backup).as_posix()
            shutil.copyfile(path, target / rel); restored += 1
        for rel in listed.read_text(encoding='utf-8').splitlines():
            for victim in (target / rel, target / (rel + '.meta')):
                if rel and victim.exists():
                    victim.unlink(); removed += 1
        sheets = target / 'Data/Spells'
        if sheets.exists() and not any(sheets.iterdir()):
            sheets.rmdir()
            meta = target / 'Data/Spells.meta'
            if meta.exists():
                meta.unlink()
        print('revert: %d live files restored, %d added files (and their .meta) removed' % (restored, removed))
        return 0
    return 0


if __name__ == '__main__':
    sys.exit(main())
