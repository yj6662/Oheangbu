"""#308 GreatBrush v4: 다시 들여온 FBX 에서 쉬는 자세 변형 오차 · 고리 사슬 하나가 움직이는 꼭짓점을 잰다(진단용)."""
import bpy, math, os, sys
from mathutils import Vector, Quaternion
OUT = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'SM_GreatBrush308.fbx'), colors_type='LINEAR')
O = bpy.data.objects; rig = next(o for o in O if o.type == 'ARMATURE'); mesh = next(o for o in O if o.type == 'MESH'); me = mesh.data
def ev():
    bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get(); e = mesh.evaluated_get(dg); m = e.to_mesh(); r = [v.co.copy() for v in m.vertices]; e.to_mesh_clear(); return r
rest = ev(); print('VERIFY rest_error', max((a - v.co).length for a, v in zip(rest, me.vertices)), 'keys', [(k.name, k.value) for k in me.shape_keys.key_blocks])
gn = [g.name for g in mesh.vertex_groups]
for chain in 'AD':
    for j in range(4):
        pb = rig.pose.bones['BristleRing_%s_%02d' % (chain, j + 1)]; pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = Quaternion((1, 0, 0), math.radians(10))
    p = ev(); mv = [(a - b).length for a, b in zip(p, rest)]
    users = sum(1 for v in me.vertices if any(gn[g.group].startswith('BristleRing_%s_' % chain) and g.weight > 1e-6 for g in v.groups))
    print('VERIFY ring', chain, 'moved', sum(1 for x in mv if x > 1e-4), 'users', users, 'max', max(mv))
    for j in range(4): rig.pose.bones['BristleRing_%s_%02d' % (chain, j + 1)].rotation_quaternion = Quaternion()
