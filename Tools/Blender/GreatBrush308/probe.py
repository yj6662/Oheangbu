"""#308 GreatBrush: 플레이어 FBX 뼈 · 크기 탐침(읽기만)."""
import bpy, sys
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=sys.argv[sys.argv.index('--') + 1], automatic_bone_orientation=False)
for o in bpy.context.scene.objects:
    print('PROBE obj', o.type, o.name, 'parent', o.parent.name if o.parent else None, 'scale', tuple(round(x, 4) for x in o.scale), 'rot', tuple(round(x, 3) for x in o.rotation_euler), 'loc', tuple(round(x, 3) for x in o.location))
    if o.type == 'MESH':
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        print('PROBE   bounds', [round(min(p[i] for p in pts), 3) for i in range(3)], [round(max(p[i] for p in pts), 3) for i in range(3)], 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons), 'mats', [m.name for m in o.data.materials], 'sk', bool(o.data.shape_keys))
    if o.type == 'ARMATURE':
        for b in o.data.bones:
            h = o.matrix_world @ b.head_local; t = o.matrix_world @ b.tail_local
            print('PROBE   bone', b.name, '<-', b.parent.name if b.parent else None, 'head', tuple(round(x, 3) for x in h), 'tail', tuple(round(x, 3) for x in t), 'roll_x', tuple(round(x, 2) for x in (o.matrix_world.to_3x3() @ b.matrix_local.to_3x3().col[0])))
print('PROBE actions', [a.name for a in bpy.data.actions])
print('PROBE DONE')
