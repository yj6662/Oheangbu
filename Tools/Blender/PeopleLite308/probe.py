"""#308 PeopleLite: 팩 FBX 구조 조사 (headless). blender -b --factory-startup -P probe.py -- <fbx> <out.json>"""
import bpy, sys, json
from mathutils import Vector
fbx, out = sys.argv[sys.argv.index('--') + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False, ignore_leaf_bones=False, automatic_bone_orientation=False)
R = dict(objects=[], meshes=[], arm=None, images=[])
for o in bpy.data.objects:
    R['objects'].append(dict(name=o.name, type=o.type, parent=o.parent.name if o.parent else None, loc=list(o.location), rot=list(o.rotation_euler), scale=list(o.scale)))
    if o.type == 'ARMATURE':
        R['arm'] = dict(name=o.name, bones=[dict(n=b.name, p=b.parent.name if b.parent else None, head=[round(v, 4) for v in (o.matrix_world @ b.head_local)], deform=b.use_deform) for b in o.data.bones])
    if o.type == 'MESH':
        me = o.data; me.calc_loop_triangles()
        ws = [o.matrix_world @ v.co for v in me.vertices]
        mn = [min(w[i] for w in ws) for i in range(3)]; mx = [max(w[i] for w in ws) for i in range(3)]
        mats = []
        tri_by = {}
        for t in me.loop_triangles: tri_by[t.material_index] = tri_by.get(t.material_index, 0) + 1
        for i, s in enumerate(o.material_slots):
            m = s.material; tex = []
            if m and m.node_tree:
                for n in m.node_tree.nodes:
                    if n.type == 'TEX_IMAGE' and n.image: tex.append(n.image.filepath)
            mats.append(dict(i=i, name=m.name if m else None, tris=tri_by.get(i, 0), tex=tex))
        used = {}
        for v in me.vertices:
            for g in v.groups:
                if g.weight > 0: used[g.group] = used.get(g.group, 0) + 1
        R['meshes'].append(dict(name=o.name, verts=len(me.vertices), tris=len(me.loop_triangles), min=mn, max=mx, uv=[u.name for u in me.uv_layers], mats=mats,
                                groups=len(o.vertex_groups), used_groups={o.vertex_groups[k].name: n for k, n in used.items()}, shapekeys=len(me.shape_keys.key_blocks) if me.shape_keys else 0))
R['images'] = [[i.name, i.filepath, list(i.size), bool(i.packed_file)] for i in bpy.data.images]
json.dump(R, open(out, 'w', encoding='utf-8'), indent=1)
print('PROBE OK', len(R['meshes']), 'meshes')
