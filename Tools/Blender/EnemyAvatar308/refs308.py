"""#308 enemy rig revision 2: who references the three rig avatars? Read-only scan of Assets (YAML text files and .meta files).
  python Tools/Blender/EnemyAvatar308/refs308.py  ->  Art/Characters308/EnemyRigVerify2/avatar_refs.json"""
import os, sys, json, time

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
ASSETS = os.path.join(REPO, 'Oheangbu', 'Assets')
ROOT = os.path.join(ASSETS, '_Project', 'Art', 'Characters', 'Folklore298')


def rel(p): return os.path.relpath(p, REPO).replace(os.sep, '/')


guids = {}
for mid in ('dokkaebi', 'agwi', 'changgui'):
    for line in open(os.path.join(ROOT, 'Models', mid, mid + '-rig_result_rigged_character_fbx_url.fbx.meta'), encoding='utf-8'):
        if line.startswith('guid:'): guids[mid] = line.split()[1].encode(); break
EXT = ('.prefab', '.unity', '.asset', '.controller', '.overrideController', '.playable', '.mask', '.anim', '.meta', '.mat')
out = {m: [] for m in guids}; n = 0; skipped = []; t0 = time.time()
for base, dirs, files in os.walk(ASSETS):
    for f in files:
        if not f.endswith(EXT): continue
        p = os.path.join(base, f)
        try:
            size = os.path.getsize(p)
            if f.endswith('.asset') and size > 64 * 1024 * 1024: skipped.append(rel(p)); continue
            with open(p, 'rb') as fh:
                head = fh.read(64)
                if f.endswith('.asset') and not head.startswith(b'%YAML'): continue      # binary asset: holds no text guid
                data = head + fh.read()
        except OSError: continue
        n += 1
        for mid, g in guids.items():
            c = data.count(g)
            if c: out[mid].append({'file': rel(p), 'count': c})
res = {'guids': {m: g.decode() for m, g in guids.items()}, 'files_scanned': n, 'seconds': round(time.time() - t0, 1), 'skipped_large_assets': skipped, 'refs': out}
os.makedirs(os.path.join(REPO, 'Art', 'Characters308', 'EnemyRigVerify2'), exist_ok=True)
json.dump(res, open(os.path.join(REPO, 'Art', 'Characters308', 'EnemyRigVerify2', 'avatar_refs.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print('scanned', n, 'files in', res['seconds'], 's; skipped', len(skipped))
for m in out:
    print(m, len(out[m]), 'files')
    for r in out[m]: print('   ', r['count'], r['file'])
