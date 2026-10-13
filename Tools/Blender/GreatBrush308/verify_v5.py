"""#308 GreatBrush v5: 내보낸 FBX 를 다시 들여와 계약을 검사한다.  blender -b --factory-startup -P verify_v5.py -- <v5 out_dir>   → <out>/verify.log"""
import bpy, json, math, os, sys
from mathutils import Vector, Quaternion
OUT = sys.argv[sys.argv.index('--') + 1]; fbx = os.path.join(OUT, 'SM_GreatBrush308.fbx')
REP = json.load(open(os.path.join(OUT, 'build-report.json'), encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fbx, colors_type='LINEAR')
bpy.context.view_layer.update()
O = bpy.data.objects; rig = next(o for o in O if o.type == 'ARMATURE'); mesh = next(o for o in O if o.type == 'MESH'); me = mesh.data
err = []
def need(c, m):
    if not c: err.append(m)
socks = {n: O.get(n) for n in ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket']}
for n, o in socks.items(): need(o is not None, 'missing ' + n)
g, gl, tip, end = (socks[n].matrix_world.translation.copy() for n in ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket'])
need(max(abs(s - 1) for s in rig.matrix_world.to_scale()) < 1e-4, 'rig scale %s' % (tuple(rig.matrix_world.to_scale()),))
bones = [b.name for b in rig.data.bones]
centre = ['Bristle_%02d' % (i + 1) for i in range(8)]; ringb = ['BristleRing_%s_%02d' % (c, j + 1) for c in 'ABCDEF' for j in range(4)]
for n in ['BrushHandle'] + centre + ringb:
    need(n in bones, 'missing bone ' + n)
    if n in bones: need(max(abs(s - 1) for s in (rig.matrix_world @ rig.pose.bones[n].matrix).to_scale()) < 1e-4, 'bone scale ' + n)
need(len(bones) == 33, 'bone count %d' % len(bones))
parents = {c: rig.data.bones['BristleRing_%s_01' % c].parent.name for c in 'ABCDEF' if 'BristleRing_%s_01' % c in bones}
need(all(p == 'Bristle_01' for p in parents.values()), 'ring parents %s' % parents)
# 고리 사슬 60도 간격
angs = [math.degrees(math.atan2(*(lambda h: (h.y, h.x))(rig.data.bones['BristleRing_%s_01' % c].head_local - rig.data.bones['Bristle_01'].head_local))) % 360 for c in 'ABCDEF']
# 리그 로컬에서 붓 축 둘레 각을 잰다(FBX 축 변환 뒤에도 같은 간격이어야 한다)
axis = (rig.data.bones['Bristle_08'].tail_local - rig.data.bones['Bristle_01'].head_local).normalized()
def ring_dir(c):
    v = rig.data.bones['BristleRing_%s_01' % c].head_local - rig.data.bones['Bristle_01'].head_local; return (v - axis * v.dot(axis)).normalized()
gaps = [math.degrees(ring_dir('ABCDEF'[i]).angle(ring_dir('ABCDEF'[(i + 1) % 6]))) for i in range(6)]
need(max(abs(x - 60) for x in gaps) < .5, 'ring spacing %s' % gaps)
keys = [k.name for k in me.shape_keys.key_blocks] if me.shape_keys else []
need('BristleSplay' in keys and 'BristlePress' in keys, 'shape keys %s' % keys)
moved = {k: sum(1 for a, b in zip(me.shape_keys.key_blocks[k].data, me.shape_keys.key_blocks[0].data) if (a.co - b.co).length > 1e-5) for k in keys[1:]}
need(all(v > 1000 for v in moved.values()), 'shape key moved verts %s' % moved)
L = (tip - end).length; need(abs(L - REP['shaft_to_tip_m']) < .01, 'length %.4f' % L)
need(abs((g - gl).length - REP['hand_gap_m']) < .002, 'hand gap %.4f' % (g - gl).length)
need(abs((tip - g).length - .95) < .002 and abs((g - end).length - .60) < .002, 'grip position')
dots = {n: (socks[n].matrix_world.to_3x3() @ Vector((0, 1, 0))).normalized().dot((tip - end).normalized()) for n in socks}
need(min(dots.values()) > .999, 'socket +Y %s' % dots)
me.calc_loop_triangles(); tris = len(me.loop_triangles); need(tris <= 18000, 'tris %d' % tris); need(tris == REP['triangles'], 'tris %d != build %d' % (tris, REP['triangles']))
sums = [sum(gr.weight for gr in v.groups) for v in me.vertices]; wsum = max(abs(x - 1) for x in sums); need(wsum < 1e-4, 'weight sum %.5f' % wsum)
infl = max(sum(1 for gr in v.groups if gr.weight > 1e-6) for v in me.vertices); need(infl <= 4, 'influences %d' % infl)
gnames = [vg.name for vg in mesh.vertex_groups]; ring_users = sum(1 for v in me.vertices if any(gnames[gr.group].startswith('BristleRing_') and gr.weight > 1e-6 for gr in v.groups))
need(ring_users > 1000, 'ring-weighted verts %d' % ring_users)
mats = [m.name for m in me.materials]; need(mats == REP['materials'], 'materials %s' % mats)
# 꼭짓점 색(선형): 붓털 면에서 R = 가닥 난수, G = 뿌리→끝
ca = me.color_attributes.get('Col') or (me.color_attributes[0] if len(me.color_attributes) else None); need(ca is not None, 'no colour attribute')
colinfo = {}
if ca is not None:
    bi = mats.index('M_GreatBrush_Bristle'); R = []; G = []; A = []; other = 0.0
    corner = ca.domain == 'CORNER'
    for p in me.polygons:
        for li in p.loop_indices:
            c = ca.data[li if corner else me.loops[li].vertex_index].color
            if p.material_index == bi: R.append(c[0]); G.append(c[1]); A.append(c[3])
            else: other = max(other, c[0], c[1], c[2], c[3])
    colinfo = {'name': ca.name, 'domain': ca.domain, 'data_type': ca.data_type, 'g_mean': sum(G) / len(G), 'g_max': max(G), 'g_min': min(G), 'r_min': min(R), 'r_max': max(R),
               'r_distinct': len(set(round(x, 3) for x in R)), 'non_bristle_max': other, 'build_g_mean': REP['vertex_color']['bristle_g_mean'],
               'a_mean': sum(A) / len(A), 'a_min': min(A), 'a_max': max(A), 'build_a_mean': REP['vertex_color']['bristle_a_mean']}
    need(abs(colinfo['a_mean'] - REP['vertex_color']['bristle_a_mean']) < .01 and colinfo['a_min'] < .01 and colinfo['a_max'] > .99, 'wetness alpha %s' % colinfo)

    need(abs(colinfo['g_mean'] - REP['vertex_color']['bristle_g_mean']) < .01, 'G mean %.4f vs build %.4f (gamma?)' % (colinfo['g_mean'], REP['vertex_color']['bristle_g_mean']))
    need(colinfo['g_max'] > .99 and colinfo['g_min'] < .01, 'G range'); need(colinfo['r_distinct'] >= REP['strands'] * .9, 'R distinct %d' % colinfo['r_distinct']); need(other < .01, 'non-bristle colour %.3f' % other)
# 휨 시험: 가운데 사슬을 45도까지 휘면 TipSocket 이 따라가고, 고리 사슬만 돌려도 가닥이 움직인다
def bend(names, ax, deg):
    for n in names:
        pb = rig.pose.bones[n]; pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ rig.matrix_world.to_3x3().inverted() @ ax, math.radians(deg))
def evaluated():
    bpy.context.view_layer.update(); e = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()); m = e.to_mesh(); r = [v.co.copy() for v in m.vertices]; e.to_mesh_clear(); return r
key_values = {k.name: k.value for k in me.shape_keys.key_blocks[1:]}; need(all(abs(v) < 1e-6 for v in key_values.values()), 'shape key default values %s' % key_values)
rest = evaluated(); rest_err = max((a - v.co).length for a, v in zip(rest, me.vertices)); need(rest_err < 1e-5, 'rest pose deforms the mesh %.5f' % rest_err)
bend(centre, Vector((1, 0, 0)), 45 / 8); bpy.context.view_layer.update()
tip2 = O['TipSocket'].matrix_world.translation.copy(); need((tip2 - tip).length > .05, 'tip does not follow bend')
bend(centre, Vector((1, 0, 0)), 0); bend(['BristleRing_A_%02d' % (j + 1) for j in range(4)], Vector((1, 0, 0)), 10); bpy.context.view_layer.update()
em = evaluated(); ring_move = max((a - b).length for a, b in zip(em, rest)); ring_moved = sum(1 for a, b in zip(em, rest) if (a - b).length > 1e-4)
need(ring_moved < len(rest) * .4, 'ring chain A moves too many verts %d' % ring_moved)
need(ring_move > .005, 'ring chain A does not move strands (%.4f)' % ring_move)
ax_ = (g - end).normalized(); tip_off = ((tip - end) - ax_ * (tip - end).dot(ax_)).length
need(abs(tip_off - REP['tip_socket_offset_from_axis_m']) < 5e-4, 'tip offset %.4f' % tip_off)
r = {'fbx': fbx, 'tip_socket_offset_from_axis_m': tip_off, 'triangles': tris, 'vertices_after_import': len(me.vertices), 'length_m': L, 'hand_gap_m': (g - gl).length, 'grip_to_tip_m': (tip - g).length, 'grip_to_end_m': (g - end).length,
     'bone_count': len(bones), 'bones': bones, 'ring_parents': parents, 'ring_spacing_deg': gaps, 'shape_keys': keys, 'shape_key_moved_verts': moved, 'shape_key_default_values': key_values, 'rest_pose_error_m': rest_err,
     'socket_y_dot_tip': dots, 'rig_scale': tuple(rig.matrix_world.to_scale()), 'weight_sum_error': wsum, 'max_influences': infl, 'ring_weighted_verts': ring_users,
     'tip_shift_at_45deg_m': (tip2 - tip).length, 'ring_A_10deg_max_vertex_move_m': ring_move, 'ring_A_moved_verts': ring_moved, 'vertex_color': colinfo, 'materials': mats, 'errors': err}
line = 'VERIFY ' + json.dumps(r) + '\n' + ('VERIFY DONE' if not err else 'VERIFY FAILED') + '\n'
open(os.path.join(OUT, 'verify.log'), 'w', encoding='utf-8').write(line); print(line)
