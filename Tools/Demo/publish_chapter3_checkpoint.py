"""Copy the current verified checkpoint into the existing PR worktree; no git mutation or build."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
target = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint').resolve()
branch = subprocess.check_output(['git', '-C', str(target), 'branch', '--show-current'], text=True).strip()
if branch != 'codex/demo-foundation':
    raise RuntimeError('Unexpected destination branch: ' + branch)
report = json.loads((root / 'Art/Demo/Chapter3/runtime_chapter3_guk_v6_pass.json').read_text(encoding='utf-8-sig'))
assert report['status'] == 'PASS_API_INTEGRATION' and report['restored'] and not report['holdingPlay']
paths = [
    'Art/Demo/Chapter3', 'Art/Demo/Chapter4',
    'Docs/PROJECT_STATUS.md', 'Docs/Specs/SPEC-DEMO-CHEONGRYONG.md',
    'Tools/Demo/publish_chapter3_checkpoint.py',
    'Oheangbu/Assets/_Project/Art/Demo/Chapter3',
    'Oheangbu/Assets/_Project/Art/Demo/Chapter3.meta',
    'Oheangbu/Assets/_Project/Art/Demo/Foundation',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity.meta',
    'Art/Demo/Foundation/chapter3-guk-upper-01_1920x1080.png',
]
files = []
for relative in paths:
    source = root / relative
    if not source.exists():
        raise FileNotFoundError(source)
    files.extend(p for p in source.rglob('*') if p.is_file()) if source.is_dir() else files.append(source)
files.extend(p for p in (root / 'Oheangbu/Assets/_Project/Scripts').rglob('*')
             if p.is_file() and p.suffix in ('.cs', '.meta', '.asmdef'))
manifest = []
for source in sorted(set(files)):
    if source.suffix in ('.tmp', '.log'):
        continue
    relative = source.relative_to(root)
    destination = target / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    expected = digest(source)
    if digest(destination) != expected:
        raise RuntimeError('Copy mismatch: ' + str(relative))
    manifest.append({'path': relative.as_posix(), 'sha256': expected, 'bytes': source.stat().st_size})
result = {'status': 'COPIED_HASH_VERIFIED', 'target': str(target), 'branch': branch,
          'scope': 'Working files only. No staging, commit, push, build or completion assertion.', 'files': manifest}
for base in (root, target):
    (base / 'Art/Demo/Chapter3/checkpoint_manifest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'status': result['status'], 'files': len(manifest), 'bytes': sum(p['bytes'] for p in manifest)}))
