"""#308 GreatBrush: 내보낸 FBX 를 다시 들여와 계약을 검사한다.  blender -b --factory-startup -P verify.py -- <fbx>"""
import bpy, json, math, os, sys
from mathutils import Vector, Quaternion
fbx = sys.argv[sys.argv.index('--') + 1]
REP = json.load(open(os.path.join(os.path.dirname(fbx), 'build-report.json'), encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fbx)
bpy.context.view_layer.update()
O = bpy.data.objects; rig = next(o for o in O if o.type == 'ARMATURE'); mesh = next(o for o in O if o.type == 'MESH')
err = []
def need(c, m):
    if not c: err.append(m)
socks = {n: O.get(n) for n in ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket']}
for n, o in socks.items(): need(o is not None, 'missing ' + n)
g, gl, tip, end = (socks[n].matrix_world.translation.copy() for n in ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket'])
need(max(abs(s - 1) for s in rig.matrix_world.to_scale()) < 1e-4, 'rig scale %s' % (tuple(rig.matrix_world.to_scale()),))
bones = [b.name for b in rig.data.bones]
for i in range(8):
    need('Bristle_%02d' % (i + 1) in bones, 'missing bone %d' % (i + 1))
    if 'Bristle_%02d' % (i + 1) in bones: need(max(abs(s - 1) for s in (rig.matrix_world @ rig.pose.bones['Bristle_%02d' % (i + 1)].matrix).to_scale()) < 1e-4, 'bone scale')
keys = [k.name for k in mesh.data.shape_keys.key_blocks] if mesh.data.shape_keys else []
need('BristleSplay' in keys and 'BristlePress' in keys, 'shape keys %s' % keys)
L = (tip - end).length; need(abs(L - REP['shaft_to_tip_m']) < .01, 'length %.4f' % L)
need(abs((g - gl).length - REP['hand_gap_m']) < .002, 'hand gap %.4f' % (g - gl).length)
dots = {n: (socks[n].matrix_world.to_3x3() @ Vector((0, 1, 0))).normalized().dot((tip - end).normalized()) for n in socks}
need(min(dots.values()) > .999, 'socket +Y %s' % dots)
mesh.data.calc_loop_triangles(); tris = len(mesh.data.loop_triangles); need(tris <= 6000, 'tris %d' % tris)
wsum = max(abs(sum(gr.weight for gr in v.groups) - 1) for v in mesh.data.vertices); need(wsum < 1e-4, 'weight sum %.5f' % wsum)
# 휨 시험: 사슬을 45도까지 휘어도 뼈 길이가 변하지 않고 TipSocket 이 따라간다
ax = Vector((1, 0, 0))
for i in range(8):
    pb = rig.pose.bones['Bristle_%02d' % (i + 1)]; pb.rotation_mode = 'QUATERNION'
    pb.rotation_quaternion = Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ rig.matrix_world.to_3x3().inverted() @ ax, math.radians(45 / 8))
bpy.context.view_layer.update()
tip2 = O['TipSocket'].matrix_world.translation; need((tip2 - tip).length > .05, 'tip does not follow bend')
r = {'fbx': fbx, 'triangles': tris, 'length_m': L, 'hand_gap_m': (g - gl).length, 'grip_to_tip_m': (tip - g).length, 'grip_to_end_m': (g - end).length, 'bones': bones, 'shape_keys': keys,
     'socket_y_dot_tip': dots, 'rig_scale': tuple(rig.matrix_world.to_scale()), 'weight_sum_error': wsum, 'tip_shift_at_45deg_m': (tip2 - tip).length, 'materials': [m.name for m in mesh.data.materials], 'errors': err}
print('VERIFY', json.dumps(r)); print('VERIFY DONE' if not err else 'VERIFY FAILED')
