"""#308 PeopleLite: 원본 몸을 그대로 본다(재질 묶음을 껐다 켜며). blender -b --factory-startup -P peek.py -- <fbx> <out.png> [숨길 재질 이름 조각,...]"""
import bpy, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pv
a = sys.argv[sys.argv.index('--') + 1:]; fbx, out = a[0], a[1]; hide = [h for h in (a[2].split(',') if len(a) > 2 else []) if h]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False, ignore_leaf_bones=False, automatic_bone_orientation=False)
Ps, Cs, Ns = [], [], []
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    me = o.data; M = np.array(o.matrix_world, dtype=np.float64)
    co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    A = pv.mesh_arrays(me, co); mats = []
    for s in o.material_slots:
        m = s.material; tex = None; alpha = False
        if m and m.node_tree:
            bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf and bsdf.inputs['Base Color'].links:
                n = bsdf.inputs['Base Color'].links[0].from_node
                if n.type == 'TEX_IMAGE' and n.image: tex = pv.image_array(n.image, 1024)
            alpha = bool(bsdf and bsdf.inputs['Alpha'].links)
        if m and any(h.lower() in m.name.lower() for h in hide): tex = np.zeros((1, 1, 4), dtype=np.float32); alpha = True
        if tex is not None and alpha: print('ALPHA', m.name, 'opaque share %.2f' % float((tex[..., 3] > .5).mean()))
        elif tex is not None: print('OPAQUE', m.name, 'alpha min %.2f share<.5 %.3f' % (float(tex[..., 3].min()), float((tex[..., 3] < .5).mean())))
        mats.append(dict(tex=tex, alpha=alpha))
    P, C, N = pv.sample(A, .0022, mats); Ps.append(P); Cs.append(C); Ns.append(N)
P = np.concatenate(Ps); C = np.concatenate(Cs); N = np.concatenate(Ns)
lo, hi = P.min(0), P.max(0)
row1 = [pv.render(P, C, N, v, (380, 640), lo, hi) for v in ('front', 'side', 'back', 'q34')]
hl = np.array([-.22, -.3, hi[2] - .42]); hh = np.array([.22, .3, hi[2] + .01])
sel = (P[:, 2] > hl[2])
row2 = [pv.render(P[sel], C[sel], N[sel], v, (380, 640), hl, hh) for v in ('front', 'side', 'q34', 'top')]
pv.save_png(out, pv.grid([row1, row2])); print('PEEK OK', out, 'height', hi[2], 'lo', lo)
