"""#308 GreatBrush v6 = v5 + 꼬리 장식(고리 · 끈 · 술)이 매달려 흔들리게 하는 뼈 셋 (사용자 2026-10-14 "중력에 영향받도록 해").
    blender -b --factory-startup -P tail_v6.py
v5 의 .blend 를 읽기만 하고 v6/ 에 새로 쓴다. 메시 · 붓털 뼈 · 소켓 · 키는 그대로, 꼬리 끝 아래 꼭짓점의 가중치만 바뀐다.
  Tail_01 고리(자루 마개에 걸린 자리가 축) → Tail_02 끈 → Tail_03 매듭 + 술,  TailEndSocket = 술 끝(Tail_03 의 자식).
Unity: GreatBrushTail308 이 이 사슬을 진자로 푼다(뼈 축 관례는 가정하지 않는다 — 자식 관절 위치로 방향을 잰다)."""
import bpy, os, json, shutil, math
from mathutils import Vector, Matrix
ROOT = 'C:/Users/yj666/Oheangbu/Art/Characters308/GreatBrush308'; SRC = ROOT + '/v5'; OUT = ROOT + '/v6'; os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=SRC + '/SM_GreatBrush308.blend')
rig = bpy.data.objects['GreatBrush308_Rig']; ob = bpy.data.objects['SM_GreatBrush308']; me = ob.data
z0 = rig.data.bones['BrushHandle'].head_local.z                     # 꼬리 끝(자루 마개 바닥)
# 꼬리 끝에서 잰 높이(build_v5.py 의 "꼬리 고리 + 술"): 고리 -.057..-.011, 끈 -.054..-.114, 매듭 -.114..-.138, 술 -.138..-.269
J = [-.012, -.054, -.114, -.269]
bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT'); prev = rig.data.edit_bones['BrushHandle']
for i in range(3):
    b = rig.data.edit_bones.new('Tail_%02d' % (i + 1)); b.head = (0, 0, z0 + J[i]); b.tail = (0, 0, z0 + J[i + 1]); b.roll = 0; b.parent = prev; b.use_connect = i > 0; prev = b
bpy.ops.object.mode_set(mode='OBJECT')
G = {n: ob.vertex_groups.new(name=n) for n in ('Tail_01', 'Tail_02', 'Tail_03')}; handle = ob.vertex_groups['BrushHandle']
mat = {}
for p in me.polygons:
    for v in p.vertices: mat[v] = p.material_index
count = {n: 0 for n in G}
for v in me.vertices:
    zr = v.co.z - z0
    if zr > -.0005: continue                                        # 자루 · 마개
    if mat.get(v.index) == 1 and zr > -.060: n = 'Tail_01'          # 놋쇠 고리
    elif zr > -.084: n = 'Tail_02'                                  # 끈 위쪽 고리(아래쪽 고리는 매듭과 같이 움직여 끈이 두 관절을 잇는다)
    else: n = 'Tail_03'
    for g in ob.vertex_groups: g.remove([v.index])
    G[n].add([v.index], 1.0, 'REPLACE'); count[n] += 1
print('TAIL weights', count, 'tail end z', round(z0, 4))
e = bpy.data.objects.new('TailEndSocket', None); e.empty_display_type = 'ARROWS'; e.empty_display_size = .04
bpy.context.scene.collection.objects.link(e); e.parent = rig; e.parent_type = 'BONE'; e.parent_bone = 'Tail_03'; bpy.context.view_layer.update()
e.matrix_world = Matrix.Translation(Vector((0, 0, z0 + J[3]))) @ Matrix.Rotation(math.radians(90), 4, 'X')
bpy.context.view_layer.update()
# 확인: 사슬을 40 도 꺾었을 때 꼬리 꼭짓점이 따라오는가
dg = bpy.context.evaluated_depsgraph_get(); rest = [v.co.copy() for v in ob.evaluated_get(dg).data.vertices]
rig.pose.bones['Tail_01'].rotation_mode = 'XYZ'; rig.pose.bones['Tail_01'].rotation_euler = (math.radians(40), 0, 0); bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get(); moved = [(a - b.co).length for a, b in zip(rest, ob.evaluated_get(dg).data.vertices)]
print('TAIL bend test: vertices moved', sum(1 for d in moved if d > 1e-5), 'of', len(moved), 'max', round(max(moved), 4), 'm · tail vertices', sum(count.values()))
rig.pose.bones['Tail_01'].rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
for k in me.shape_keys.key_blocks: k.value = 0.0
bpy.ops.wm.save_as_mainfile(filepath=OUT + '/SM_GreatBrush308.blend')
for o in bpy.context.scene.objects: o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=OUT + '/SM_GreatBrush308.fbx', use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
                         bake_anim=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False, path_mode='COPY', embed_textures=True,
                         colors_type='LINEAR')
shutil.copyfile(SRC + '/T_GreatBrush308_Bristle.png', OUT + '/T_GreatBrush308_Bristle.png')
rep = json.load(open(SRC + '/build-report.json', encoding='utf-8')); rep['version'] = 'v6'; rep['bones'] = [b.name for b in rig.data.bones]; rep['sockets'] = rep['sockets'] + ['TailEndSocket']
rep['tail_chain'] = dict(bones=['Tail_01', 'Tail_02', 'Tail_03'], joints_below_tail_end_m=J, vertices=count, note='ring / cord / knot + tassel; TailEndSocket = tassel end')
json.dump(rep, open(OUT + '/build-report.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print('TAIL exported', os.path.getsize(OUT + '/SM_GreatBrush308.fbx'), 'bones', len(rig.data.bones), 'objects', [o.name for o in bpy.context.scene.objects])
