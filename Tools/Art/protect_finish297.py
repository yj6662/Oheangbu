"""#297 finish pass guard: back up the #296 candidate state it edits and keep what it must not edit unchanged.

record  — zip the #296 scene/Data/Materials (the parts #297 edits) and hash the #296 generated meshes/shaders/
          textures plus the #298 candidate folder (never edited by #297).
verify  — re-hash the guarded trees; also run the #296 protection check (originals, #295, canonical scene).
"""
from pathlib import Path
import hashlib, json, sys, subprocess, zipfile
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Baseline'
A296 = 'Oheangbu/Assets/_Project/Art/World/Architecture296'
BACKUP = [A296 + '/W_Demo_Compact_Architecture296.unity', A296 + '/W_Demo_Compact_Architecture296.unity.meta',
          A296 + '/Data', A296 + '/Materials', A296 + '/Navigation.asset']
GUARD = [A296 + '/Meshes', A296 + '/Shaders', A296 + '/Textures']


def sha(p):
    h = hashlib.sha256()
    with p.open('rb') as f:
        for block in iter(lambda: f.read(1 << 22), b''):
            h.update(block)
    return h.hexdigest()


def files(entries):
    for e in entries:
        p = ROOT / e
        if p.is_file():
            yield p
        elif p.is_dir():
            yield from (q for q in sorted(p.rglob('*')) if q.is_file())


def snapshot():
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in files(GUARD)}


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    base = OUT / 'guarded.json'
    if len(sys.argv) > 1 and sys.argv[1] == 'record':
        if base.exists():
            raise SystemExit('Existing #297 baseline retained')
        with zipfile.ZipFile(OUT / 'Candidate296.zip', 'w', zipfile.ZIP_DEFLATED, compresslevel=1) as z:
            backed = {}
            for p in files(BACKUP):
                rel = p.relative_to(ROOT).as_posix(); z.write(p, rel); backed[rel] = sha(p)
        (OUT / 'backup-manifest.json').write_text(json.dumps(backed, indent=1), encoding='utf8')
        data = snapshot(); base.write_text(json.dumps(data, indent=1), encoding='utf8')
        (OUT / 'git-status.txt').write_bytes(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT))
        print(json.dumps({'backedUp': len(backed), 'guarded': len(data), 'zip': str(OUT / 'Candidate296.zip')}))
    else:
        data = json.loads(base.read_text(encoding='utf8')); now = snapshot()
        changed = [p for p, h in data.items() if now.get(p) != h]
        added = [p for p in now if p not in data]
        r296 = subprocess.run([sys.executable, str(ROOT / 'Tools/Art/protect_architecture296.py'), 'verify'], cwd=ROOT,
                              capture_output=True, text=True)
        result = {'guarded': len(data), 'changed': changed, 'missing': [p for p in changed if p not in now], 'added': added,
                  'protect296': r296.returncode == 0, 'passed': not changed and not added and r296.returncode == 0}
        (OUT.parent / 'protection-check.json').write_text(json.dumps(result, indent=1), encoding='utf8')
        print(json.dumps(result, ensure_ascii=False)); sys.exit(0 if result['passed'] else 1)
