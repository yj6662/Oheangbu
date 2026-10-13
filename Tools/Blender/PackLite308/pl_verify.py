"""#308 PackLite: export check — re-import every LOD FBX and compare with the manifest entry; also read the RAW FBX geometry
to predict what Unity's importer will see (Unity mesh = (-x, y, z) * UnitScaleFactor/100 of the raw FBX vertices, root identity).
  blender -b --factory-startup -P Tools/Blender/PackLite308/pl_verify.py -- [Name ...]      (no names = all)
Writes Art/World/PackLite308/_cache/verify.json and prints one PLV line per file.
"""
import bpy, sys, json
from pathlib import Path
import numpy as np
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pl_load as L
OUT = HERE.parents[2] / 'Art/World/PackLite308'
from io_scene_fbx import parse_fbx


def raw_bounds(path):
    root, ver = parse_fbx.parse(str(path))
    mn, mx = np.full(3, np.inf), np.full(3, -np.inf); n = 0; rot = []
    for e in root.elems:
        if e.id != b'Objects': continue
        for g in e.elems:
            if g.id == b'Geometry':
                for c in g.elems:
                    if c.id == b'Vertices':
                        v = np.array(c.props[0], dtype=np.float64).reshape(-1, 3); mn = np.minimum(mn, v.min(0)); mx = np.maximum(mx, v.max(0)); n += len(v)
            if g.id == b'Model':
                for c in g.elems:
                    if c.id == b'Properties70':
                        for p in c.elems:
                            if p.props and p.props[0] in (b'Lcl Rotation', b'Lcl Scaling', b'Lcl Translation', b'PreRotation'): rot.append((p.props[0].decode(), [round(float(x), 4) for x in p.props[4:7]]))
    return mn, mx, n, rot


names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
res = {}; bad = 0
for jp in sorted(OUT.glob('*/*.json')):
    e = json.loads(jp.read_text(encoding='utf-8'))
    if 'lods' not in e or (names and e['name'] not in names): continue
    for lod in e['lods']:
        f = OUT / lod['fbx']
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(f))
        o = [x for x in bpy.data.objects if x.type == 'MESH'][0]
        co = np.array([o.matrix_world @ v.co for v in o.data.vertices])
        bmn, bmx = co.min(0), co.max(0)
        # Blender world (x,y,z) -> Unity (-x, z, -y)
        re_min = [-bmx[0], bmn[2], -bmx[1]]; re_max = [-bmn[0], bmx[2], -bmn[1]]
        us = L.fbx_unit_scale(str(f))
        rmn, rmx, n, rot = raw_bounds(f)
        un_min = [-rmx[0] * us, rmn[1] * us, rmn[2] * us]; un_max = [-rmn[0] * us, rmx[1] * us, rmx[2] * us]
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        want = e['bounds_unity'] if lod['lod'] == 0 else None
        d1 = max(abs(a - b) for a, b in zip(re_min + re_max, un_min + un_max))
        d0 = max(abs(a - b) for a, b in zip(un_min + un_max, want['min'] + want['max'])) if want else 0.0
        ok = d1 < 0.01 and d0 < 0.01 and tris == lod['tris'] and all(abs(x) < 1e-6 for k, v in rot if k in ('Lcl Rotation', 'Lcl Translation') for x in v)
        bad += not ok
        res[lod['fbx']] = {'ok': bool(ok), 'unity_min': [round(x, 3) for x in un_min], 'unity_max': [round(x, 3) for x in un_max], 'reimport_vs_raw_m': round(d1, 4), 'raw_vs_manifest_m': round(d0, 4),
                           'tris': tris, 'unit_scale': us, 'root': rot}
        print('PLV', 'OK ' if ok else 'BAD', lod['fbx'], 'tris', tris, 'unity', res[lod['fbx']]['unity_min'], res[lod['fbx']]['unity_max'], 'd', round(d1, 4), round(d0, 4), 'root', rot, flush=True)
(OUT / '_cache').mkdir(exist_ok=True)
(OUT / '_cache/verify.json').write_text(json.dumps(res, indent=1))
print('PLV total', len(res), 'bad', bad)
