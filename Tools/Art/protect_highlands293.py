"""Record / verify the files the Highlands293 variant work must never modify.

python Tools/Art/protect_highlands293.py record   # once, before variant work
python Tools/Art/protect_highlands293.py verify   # after every build
"""
from pathlib import Path
import hashlib, json, sys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Highlands293/protected-baseline.json'
TREES = ['Oheangbu/Assets/_Project/Art/World/MountainTrail285',  # accepted 285/289 trail and shared shaders
         'Art/World/Compact/Rebuild/Mountain285']                 # its generator outputs (meshes, terrain, route)
FILES = ['Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity',
         'Oheangbu/Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset',
         'Oheangbu/ProjectSettings/EditorBuildSettings.asset']


def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def snapshot():
    paths = [p for tree in TREES for p in (ROOT / tree).rglob('*') if p.is_file()]
    paths += [ROOT / f for f in FILES if (ROOT / f).is_file()]
    return {str(p.relative_to(ROOT)).replace('\\', '/'): digest(p) for p in sorted(paths)}


if __name__ == '__main__':
    command = sys.argv[1] if len(sys.argv) > 1 else 'verify'
    if command == 'record':
        if OUT.exists():
            sys.exit('Baseline already recorded; delete it deliberately to re-record: ' + str(OUT))
        data = snapshot()
        OUT.write_text(json.dumps(data, indent=1), encoding='utf8')
        print('recorded', len(data), 'files')
    else:
        base = json.loads(OUT.read_text(encoding='utf8'))
        now = snapshot()
        changed = [p for p in base if now.get(p) != base[p]]
        added = [p for p in now if p not in base]
        report = dict(files=len(base), changed=changed, missing=[p for p in changed if p not in now], added=added)
        print(json.dumps(report, ensure_ascii=False, indent=1))
        sys.exit(1 if changed or added else 0)
