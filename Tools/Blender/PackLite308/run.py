"""#308 PackLite batch driver: ONE Blender process per item, resource guard before each, log to Art/World/PackLite308/_cache/run.log.

  python Tools/Blender/PackLite308/run.py [--only A,B] [--force] [--class palace]     # lite copies (packlite308.json)
  python Tools/Blender/PackLite308/run.py --manifest                                    # only rebuild manifest.json + manifest.md
Never start two of these at once (one Blender at a time). CPU only; no editor involved.
"""
import json, subprocess, sys, time
from pathlib import Path
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / 'Art/World/PackLite308'
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe'


def manifest():
    items = []
    for jp in sorted(OUT.glob('*/*.json')):
        if jp.parent.name.startswith('_'): continue
        try: e = json.loads(jp.read_text(encoding='utf-8'))
        except Exception: continue
        if 'lods' in e: items.append(e)
    ver = {}
    vp = OUT / '_cache/verify.json'
    if vp.exists(): ver = json.loads(vp.read_text())
    for e in items:
        for l in e['lods']: l['export_check'] = ver.get(l['fbx'], {}).get('ok')
    tot_src = sum(e.get('source_tris', 0) for e in items); tot0 = sum(e['lods'][0]['tris'] for e in items)
    doc = {'generated': time.strftime('%Y-%m-%d %H:%M'), 'conventions': {
        'space': 'Unity: metres, Y up, root transform identity (FBX exported with axis_forward=-Z, axis_up=Y, FBX_SCALE_ALL, bake_space_transform)',
        'pivot': 'bbox centre in XZ; Y=0 = ground the source was authored on (below_ground_m = how deep the platform skirt goes below it)',
        'front': 'kept as the source prefab; generated modules face Unity -Z (door side)', 'lod': 'LOD0 textured, LOD1 = 35 % with <= 6 materials, LOD2 = prism-and-roof proxy with one palette material'},
        'totals': {'models': len(items), 'source_tris': tot_src, 'lod0_tris': tot0}, 'items': items}
    (OUT / 'manifest.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    rows = ['# PackLite308 manifest (%s)' % doc['generated'], '', '모델 %d · 원본 삼각형 %s → LOD0 %s' % (len(items), format(tot_src, ','), format(tot0, ',')), '',
            '| 분류 | 이름 | 크기 X×Y×Z (m) | 원본 tri | LOD0 | LOD1 | LOD2 | 실루엣 IoU LOD0 (앞/옆/위) | LOD1 최소 | 재질 0/1/2 | 지붕 구멍 | 원본 |', '|---|---|---|---:|---:|---:|---:|---|---:|---|---:|---|']
    for e in sorted(items, key=lambda e: (e['class'], e['name'])):
        l0, l1, l2 = e['lods']
        s = e['bounds_unity']['size']
        rows.append('| %s | %s | %.1f × %.1f × %.1f | %s | %s | %s | %s | %.3f / %.3f / %.3f | %.3f | %d / %d / %d | %.3f | %s |' % (
            e['class'], e['name'], s[0], s[1], s[2], format(e.get('source_tris', 0), ','), format(l0['tris'], ','), format(l1['tris'], ','), format(l2['tris'], ','),
            l0['iou'].get('fromNegZ', 0), l0['iou'].get('fromPosX', 0), l0['iou'].get('top', 0), min(l1['iou'].get(k, 0) for k in ('fromNegZ', 'fromPosX', 'top')),
            l0['materials'], l1['materials'], l2['materials'], l0['iou'].get('roof_hole_frac', 0), e.get('reference', '') or e.get('source', '')))
    (OUT / 'manifest.md').write_text('\n'.join(rows) + '\n', encoding='utf-8')
    print('manifest:', len(items), 'models', tot_src, '->', tot0)


def main():
    argv = sys.argv[1:]
    if '--manifest' in argv: return manifest()
    script = 'packlite308.py'; cfgname = 'packlite308.json'
    if '--gen' in argv: script = 'pl_modules.py'
    cfg = json.loads((HERE / cfgname).read_text(encoding='utf-8'))
    only = set(argv[argv.index('--only') + 1].split(',')) if '--only' in argv else None
    cls = argv[argv.index('--class') + 1] if '--class' in argv else None
    logp = OUT / '_cache/run.log'; logp.parent.mkdir(parents=True, exist_ok=True)
    names = [it['name'] for it in cfg['items'] if (not only or it['name'] in only) and (not cls or it['class'] == cls)]
    if '--gen' in argv: names = ['__gen__']
    for n in names:
        if (OUT / '_cache/STOP').exists(): print('STOP file present'); break
        subprocess.run([sys.executable, str(REPO / 'Tools/resource_guard.py'), '--wait'], capture_output=True)
        for _ in range(360):   # never two Blender processes at once (another driver, or someone's own Blender)
            tl = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq blender.exe', '/NH'], capture_output=True, text=True).stdout
            if 'blender.exe' not in tl.lower(): break
            time.sleep(10)
        t = time.time()
        cmd = [BLENDER, '-b', '--factory-startup', '-P', str(HERE / script), '--'] + ([] if n == '__gen__' else ['--only', n]) + (['--force'] if '--force' in argv else [])
        p = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='ignore')
        lines = [l for l in (p.stdout or '').splitlines() if l.startswith('PL ') or 'Traceback' in l or l.startswith('  File') or 'Error' in l and 'could not find' not in l]
        with open(logp, 'a', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n== %s exit %d %.0fs\n' % (n, p.returncode, time.time() - t))
        print('\n'.join(l for l in lines if 'DONE' in l or 'FAILED' in l or 'skip' in l or 'Traceback' in l or 'GEN' in l), flush=True)
    manifest()


if __name__ == '__main__':
    main()
