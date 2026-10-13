"""#308 PackLite probe: size / material / connected-component statistics of pack prefabs (no output files).
  blender -b --factory-startup -P Tools/Blender/PackLite308/pl_probe.py -- <prefab> [<prefab> ...]
"""
import bpy, bmesh, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import pl_load as L
import numpy as np

for src in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    L._fbx_cache.clear()
    t = time.time(); st = {}
    o = L.build(src, 'probe', st)
    me = o.data
    n = len(me.vertices); co = np.empty(n * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    mn, mx = co.min(0), co.max(0)
    print('\n== %s  parts=%d fbx=%d tris=%d  load %.1fs' % (Path(src).stem, st['parts'], st['fbx'], st['source_tris'], time.time() - t))
    print('   blender bounds x[%.2f %.2f] y[%.2f %.2f] z[%.2f %.2f]  -> unity size X %.2f  Y %.2f  Z %.2f' % (mn[0], mx[0], mn[1], mx[1], mn[2], mx[2], mx[0] - mn[0], mx[2] - mn[2], mx[1] - mn[1]))
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    mi = np.empty(nt, dtype=np.int32); me.loop_triangles.foreach_get('material_index', mi)
    ar = np.empty(nt); me.loop_triangles.foreach_get('area', ar)
    nz = np.empty(nt * 3); me.loop_triangles.foreach_get('normal', nz); nz = nz.reshape(-1, 3)
    tv = np.empty(nt * 3, dtype=np.int32); me.loop_triangles.foreach_get('vertices', tv); tv = tv.reshape(-1, 3)
    zc = co[tv][:, :, 2].mean(1)
    for i, m in enumerate(me.materials):
        k = mi == i
        if not k.any(): continue
        info = eval(m.get('pl_info', '{}'))
        alb = Path(info.get('albedo') or '').name
        print('   mat %-28s tris %7d  area %8.1f  z %.1f..%.1f  up %.2f  alb %s clip=%s' % (m.name, k.sum(), ar[k].sum(), zc[k].min(), zc[k].max(), (nz[k][:, 2] * ar[k]).sum() / max(ar[k].sum(), 1e-9), alb, info.get('alphaclip')))
    # connected components (welded)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    seen = set(); sizes = []
    for v in bm.verts:
        if v.index in seen: continue
        stack = [v]; seen.add(v.index); c = 0
        while stack:
            a = stack.pop(); c += 1
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen: seen.add(b.index); stack.append(b)
        sizes.append(c)
    sizes = np.array(sizes)
    print('   welded verts %d  components %d  (>=1000 v: %d, 100-999: %d, <100: %d holding %d verts)' % (len(bm.verts), len(sizes), (sizes >= 1000).sum(), ((sizes >= 100) & (sizes < 1000)).sum(), (sizes < 100).sum(), sizes[sizes < 100].sum()))
    bm.free()
