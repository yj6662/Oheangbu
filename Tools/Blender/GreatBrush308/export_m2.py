"""#308 GreatBrush motion2: 클립마다 애니메이션만 든 FBX(플레이어 뼈대, 메시 없음, 30 fps) + grip.json + 다시 들여와 검사.
    blender -b --factory-startup -P export_m2.py -- [이름 ...] [--noexport]
출력: motion2/export/A_Brush308_<이름>.fbx · motion2/grip.json · motion2/export-report.json.  .blend 는 저장하지 않는다.
내보내기 관례 = Tools/Blender/PlaytestPlayer/natural_locomotion_retarget.py · recovery_retarget.py 와 같다(뼈대만, add_leaf_bones=False, simplify 0, axis_forward='-Z', axis_up='Y').
테이크 이름이 파일 이름과 같도록 내보낼 때 장면 이름을 클립 이름으로 바꾼다.
검사: (1) Player_C02_ReRig.fbx 를 새로 들여와 뼈 이름 · 부모 · 쉬는 자세 · 뼈대 물체 변환을 견준다 (2) 내보낸 FBX 를 anim_offset=0 으로 다시 들여와 모든 프레임 · 모든 뼈의 월드 자리 · 회전을 .blend 와 견준다
      (3) FBX 파일 안의 Model · AnimStack 이름을 바이트로 읽는다."""
import bpy, json, math, os, re, sys, time
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2, clips_m2
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
NAMES = [a for a in ARGS if a in clips_m2.CLIPS] or list(C2.NAMES)
g = C.init(C.BLEND); arm = g['arm']; sc = bpy.context.scene; AW = g['AW']; sc.render.fps = 30; sc.render.fps_base = 1
EXP = C.OUT + '/export'; os.makedirs(EXP, exist_ok=True)
r5 = lambda v: [round(x, 5) for x in v]; r6m = lambda M: [[round(x, 6) for x in row] for row in M]
def unit(M): return Matrix.LocRotScale(M.translation, M.to_quaternion(), Vector((1, 1, 1)))
def xyzw(q): return [round(q.x, 6), round(q.y, 6), round(q.z, 6), round(q.w, 6)]
def fbx_names(path):
    b = open(path, 'rb').read(); return [m.decode() for m in re.findall(rb'([A-Za-z0-9_ :.|]{1,60})\x00\x01Model', b)], [m.decode() for m in re.findall(rb'([A-Za-z0-9_ :.|]{1,60})\x00\x01AnimStack', b)]
def skel(a): return {b.name: (b.parent.name if b.parent else None) for b in a.data.bones}
bones0 = skel(arm)

# ---- 0) 원본 플레이어 FBX 와 뼈대 견주기
before = set(bpy.data.objects); acts0 = set(bpy.data.actions); meshes0 = set(bpy.data.meshes)
bpy.ops.import_scene.fbx(filepath=C.PLAYER, automatic_bone_orientation=False)
new = [o for o in bpy.data.objects if o not in before]; pa = next(o for o in new if o.type == 'ARMATURE'); bonesP = skel(pa)
ref = dict(player_object=pa.name.split('.')[0], player_bone_count=len(bonesP), blend_bone_count=len(bones0), names_and_parents_equal=bonesP == bones0,
           object_matrix_err=round(max(abs(a - b) for ra, rb in zip(pa.matrix_world, AW) for a, b in zip(ra, rb)), 7), object_scale=r5(pa.scale), object_rot_euler_deg=[round(math.degrees(x), 3) for x in pa.rotation_euler],
           rest_err_mm=round(1000 * max(((pa.matrix_world @ b.head_local) - (AW @ arm.data.bones[b.name].head_local)).length for b in pa.data.bones if b.name in bones0), 4),
           rest_rot_err_deg=round(max(math.degrees((pa.matrix_world @ b.matrix_local).to_quaternion().rotation_difference((AW @ arm.data.bones[b.name].matrix_local).to_quaternion()).angle) for b in pa.data.bones if b.name in bones0), 4))
pm, _ = fbx_names(C.PLAYER); ref['player_fbx_models'] = len(pm)
for o in new: bpy.data.objects.remove(o, do_unlink=True)
for x in set(bpy.data.actions) - acts0: bpy.data.actions.remove(x)
print('M1 ref', json.dumps(ref))
report = dict(_reference=ref)

# ---- 1) 내보내기
for n in NAMES:
    a = bpy.data.actions[C2.act(n)]; C.assign(arm, a); f0, f1 = (int(round(x)) for x in a.frame_range); sc.frame_start = f0; sc.frame_end = f1; sc.frame_set(f0)
    path = '%s/%s.fbx' % (EXP, C2.act(n)); report[n] = dict(file='export/' + os.path.basename(path), frames=[f0, f1], frame_count=f1 - f0 + 1, seconds=round((f1 - f0) / 30, 4))
    if '--noexport' not in ARGS:
        old = sc.name; sc.name = C2.act(n); bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y')
        sc.name = old
    report[n]['bytes'] = os.path.getsize(path); print('M1 export', n, report[n])

# ---- 2) 다시 들여와 검사(모든 프레임 · 모든 뼈)
for n in NAMES:
    path = '%s/%s.fbx' % (EXP, C2.act(n)); a = bpy.data.actions[C2.act(n)]; C.assign(arm, a); f0, f1 = report[n]['frames']
    before = set(bpy.data.objects); acts0 = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False, anim_offset=0.0)
    new = [o for o in bpy.data.objects if o not in before]; a2 = next(o for o in new if o.type == 'ARMATURE'); na = list(set(bpy.data.actions) - acts0)
    act = a2.animation_data.action if a2.animation_data else None; rng = [int(round(x)) for x in act.frame_range] if act else None
    models, stacks = fbx_names(path); perr = 0; rerr = 0; worst = None; apex = clips_m2.CLIPS[n].get('apex'); fa = int(round(apex * 30)) if apex is not None else None; aperr = None
    if act:
        for f in range(f0, f1 + 1):
            sc.frame_set(f); bpy.context.view_layer.update(); fe = 0
            for b in bones0:
                A = arm.matrix_world @ arm.pose.bones[b].matrix; B = a2.matrix_world @ a2.pose.bones[b].matrix; e = (A.translation - B.translation).length; fe = max(fe, e)
                if e > perr: perr = e; worst = (b, f)
                rerr = max(rerr, math.degrees(A.to_quaternion().rotation_difference(B.to_quaternion()).angle))
            if f == fa: aperr = fe
    report[n]['reimport'] = dict(objects=[(o.name, o.type) for o in new], bone_count=len(a2.data.bones), names_and_parents_equal=skel(a2) == bones0, fbx_model_nodes=len(models), fbx_models_are_armature_plus_bones=sorted(models) == sorted(['Armature'] + list(bones0)),
                                 take_names=stacks, actions=[x.name for x in na], range=rng, frame_count=(rng[1] - rng[0] + 1) if rng else None, frame_count_equal=bool(rng) and rng == [f0, f1],
                                 max_joint_pos_err_mm=round(perr * 1000, 4), max_joint_rot_err_deg=round(rerr, 4), worst=worst, apex_frame=fa, apex_joint_pos_err_mm=None if aperr is None else round(aperr * 1000, 4),
                                 arm_scale=r5(a2.scale), object_matrix_err=round(max(abs(x - y) for ra, rb in zip(a2.matrix_world, AW) for x, y in zip(ra, rb)), 7))
    print('M1 verify', n, json.dumps(report[n]['reimport'], ensure_ascii=False))
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    for x in na: bpy.data.actions.remove(x)

# ---- 3) grip.json
ALLC = list(C2.NAMES); S = {n: json.load(open('%s/metrics/%s.json' % (C.OUT, n), encoding='utf-8'))['summary'] for n in ALLC}
REL = {n: Matrix(S[n]['grip_rel_matrix']) for n in ALLC}; CARRY = [n for n in ALLC if 'special' in clips_m2.CLIPS[n]]; TWO = [n for n in ALLC if n not in CARRY]; R0 = REL['StanceIdle']
def spread(names): return dict(position_mm=round(max((REL[n].translation - R0.translation).length for n in names) * 1000, 4), rotation_deg=round(max(math.degrees(REL[n].to_quaternion().rotation_difference(R0.to_quaternion()).angle) for n in names), 4))
sp_two = spread(TWO); sp_carry = spread(CARRY); same = sp_carry['position_mm'] < .1 and sp_carry['rotation_deg'] < .05
rig = g['brush_rig']; RW = rig.matrix_world.copy(); SOCK = {k: (RW.inverted() @ bpy.data.objects[k].matrix_world) for k in ('GripSocket', 'GripSocketL', 'ShaftEndSocket') if k in bpy.data.objects}       # TipSocket 은 붓털 뼈의 자식이라 프레임마다 움직인다(쉬는 값: 축 방향 0.950 m · 축에서 0.015 m, v5/verify.log)
MX = Matrix.Diagonal((-1, 1, 1)); CB = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))       # Unity 가 FBX 를 들일 때 X 를 뒤집는다 / Blender 물체 축 (x,y,z) → Unity 모델 축 (-x, z, -y)
u3 = lambda v: [round(-v.x, 5), round(v.z, 5), round(-v.y, 5)]
def packs(R):
    """R = 손 뼈(Blender 뼈 축) 기준 붓 리그 뿌리(Blender 물체 축, 붓끝 +Z). → Blender 값 + Unity 식 값."""
    rot_u = MX @ R.to_3x3() @ CB.transposed(); pos_u = MX @ R.translation
    return dict(blender=dict(position_m=r5(R.translation), quaternion_xyzw=xyzw(R.to_quaternion()), matrix_rows=r6m(R)),
                unity_by_formula=dict(position_m=r5(pos_u), quaternion_xyzw=xyzw(rot_u.to_quaternion()), det=round(rot_u.determinant(), 6), tip_dir_in_hand=r5(rot_u @ Vector((0, 1, 0)))))
def model_rest(R):
    hb = arm.data.bones['RightHand']; MH = unit(AW @ hb.matrix_local); MB = MH @ R; j = lambda nm: AW @ arm.data.bones[nm].head_local
    H = j('RightHand'); e1 = (j('RightHandMiddle1') - H).normalized(); v = j('RightHandIndex1') - j('RightHandPinky1'); e2 = (v - e1 * v.dot(e1)).normalized(); w = j('RightHandThumb1') - H; e3 = (w - e1 * w.dot(e1) - e2 * w.dot(e2)).normalized()
    co = lambda p: [round(p.dot(e), 5) for e in (e1, e2, e3)]; tip = MB.to_3x3() @ Vector((0, 0, 1)); by = MB.to_3x3() @ Vector((0, 1, 0)); bx = MB.to_3x3() @ Vector((1, 0, 0))
    return dict(blender_model_space=dict(right_hand_joint=r5(H), brush_root_pos=r5(MB.translation), brush_tip_dir=r5(tip), brush_blenderY_dir=r5(by), brush_blenderX_dir=r5(bx)),
                unity_model_space=dict(right_hand_joint=u3(H), brush_root_pos=u3(MB.translation), brush_tip_dir=u3(tip), brush_blenderY_dir=u3(by), brush_blenderX_dir=u3(bx)),
                joint_basis=dict(e1='normalize(RightHandMiddle1 - RightHand)', e2='normalize(part of (RightHandIndex1 - RightHandPinky1) perpendicular to e1)', e3='normalize(part of (RightHandThumb1 - RightHand) perpendicular to e1 and e2)',
                                 e3_dot_e1xe2_blender=round(e3.dot(e1.cross(e2)), 4), brush_root_offset_from_hand_joint=co(MB.translation - H), brush_tip_dir=co(tip), brush_blenderY_dir=co(by), brush_blenderX_dir=co(bx)))
def keys01(curve):
    b = [1 if w >= .5 else 0 for w in curve]; out = [[0.0, b[0]]]
    for i in range(1, len(b)):
        if b[i] != b[i - 1]: out.append([round(i / 30, 4), b[i]])
    return out
Rc = REL['CarryIdle']
grip = dict(
    note='붓은 매 프레임 "오른손 뼈 × 고정 쥐기 변환"으로 놓는다. left_hand_on 이 1 인 구간에서만 왼손을 GripSocketL 에 두 뼈 IK 로 붙인다. Unity 값(unity_by_formula · unity_model_space)은 축 변환식으로 계산만 했다 — 편집기에서 확인하지 않았다.',
    brush=dict(model='v5 SM_GreatBrush308', root='리그 뿌리 = GripSocket 자리(아래 sockets_in_brush_root 로 확인). Blender 물체 축: 붓끝 = +Z. Unity 모델 뿌리 축(식): 붓끝 = +Y.',
               sockets_in_brush_root_blender={k: dict(position_m=r5(m.translation), quaternion_xyzw=xyzw(m.to_quaternion())) for k, m in SOCK.items()},
               tip_socket_rest='TipSocket 은 붓털 뼈 자식(움직임). 쉬는 자리 = 뿌리에서 붓끝 쪽 0.950 m, 축에서 0.015 m 벗어남(v5/verify.log)', units='m. 손 뼈의 lossyScale 이 1 이 아니면 자리 값을 그만큼 나눠 쓴다(Blender 뼈대 물체 배율 0.01 은 뺀 값).', right_hand_hold_behind_gripsocket_m=C2.SHIFT, gripsocketL_behind_gripsocket_m=round(-SOCK['GripSocketL'].translation.z, 5) if 'GripSocketL' in SOCK else None, left_hand_behind_right_hand_along_shaft_m=C2.GAP,
               warning='오른손은 GripSocket 이 아니라 그보다 꼬리 쪽 0.30 m 를 쥔다. 그래서 GripSocketL(GripSocket 뒤 0.42 m)은 그대로 쓰지만 두 손 사이는 자루를 따라 0.12 m 다(Spec §4 의 "두 손 사이 0.42 m"가 아니다).'),
    assumptions=['손 뼈 로컬 축(blender) = Blender FBX 들여오기(automatic_bone_orientation=False)가 만든 RightHand 뼈 축(뼈 방향 = +Y). Player_C02_ReRig.fbx 가 Blender 5.0 에서 primary Y / secondary X 로 나온 파일이라 FBX 노드 축과 같다고 가정했다.',
                 'unity_by_formula: Unity 는 FBX(오른손 좌표)를 들일 때 X 를 뒤집는다고 가정 → 손 뼈 로컬에서 자리 (x,y,z)→(-x,y,z). 붓 쪽은 Blender 물체 축 (x,y,z) → Unity 모델 뿌리 축 (-x,z,-y)(axis_forward=-Z, axis_up=Y 로 내보낸 FBX 의 뿌리 GameObject 기준, bakeAxisConversion 꺼짐). 회전 = diag(-1,1,1) · R · C^T.',
                 '뼈 축 가정이 싫으면 rest_pose_model_space(쉬는 자세 모델 공간: handBindWorld^-1 × 붓 자세) 또는 joint_basis(관절 자리 차이만으로 만든 기저 — 손 뼈와 함께 도는 값이라 어느 자세에서나 쓴다. 외적을 쓰지 않아 좌우 뒤집힘과 무관)를 쓴다.',
                 'unity_model_space = (-x, z, -y). 쉬는 자세 = Player_C02_ReRig.fbx 의 바인드 자세.'],
    right_hand_to_brush_root=dict(explain='손 뼈 로컬 기준 붓 뿌리(= GripSocket)의 자리 · 회전. 두 손 클립 공통(StanceIdle 첫 프레임 값).', **packs(R0), rest_pose_model_space=model_rest(R0), spread_over_two_hand_clips=sp_two),
    carry_grip=dict(same_as_two_hand=same, spread_vs_two_hand=sp_carry, clips=CARRY, **({} if same else dict(right_hand_to_brush_root=dict(**packs(Rc), rest_pose_model_space=model_rest(Rc))))),
    fps=30,
    clips={n: dict(file='export/' + C2.act(n) + '.fbx', length_s=S[n]['seconds'], frames=S[n]['frames'], fps=30, loop=bool(clips_m2.CLIPS[n].get('loop')), apex_s=clips_m2.CLIPS[n].get('apex'), spec_apex_s=clips_m2.CLIPS[n].get('spec'),
                   measured=dict(peak_tip_speed_s=S[n]['peak_tip_speed_t'], peak_tip_speed_mps=S[n]['peak_tip_speed'], tip_lowest_s=S[n]['tip_z_min_t'], tip_lowest_z_m=S[n]['tip_z_min']),
                   speed_factors_baked=S[n].get('speed_factors'), source=S[n].get('src'), left_hand_on=keys01(S[n]['l_on_curve']), left_hand_weight_per_frame=S[n]['l_on_curve'],
                   gripsocketL_offset_m=round(-SOCK['GripSocketL'].translation.z, 5), left_hand_to_socketL_err_cm_max_while_on=S[n]['l_socket_err_cm_max'], wrist_to_wrist_m=S[n]['hand_dist'], grip_drift_in_clip=dict(position_mm=S[n]['grip_rel_drift'][0], rotation_deg=S[n]['grip_rel_drift'][1]))
           for n in ALLC})
json.dump(grip, open(C.OUT + '/grip.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(report, open(C.OUT + '/export-report.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
bad = [n for n in NAMES if not (report[n]['reimport']['names_and_parents_equal'] and report[n]['reimport']['frame_count_equal'] and report[n]['reimport']['max_joint_pos_err_mm'] < 1)]
open(C.OUT + '/PROGRESS.md', 'a', encoding='utf-8').write('- %s export_m2: FBX %d개(export/) + grip.json + export-report.json. 다시 들여오기 검사 실패 %s\n' % (time.strftime('%H:%M'), len(NAMES), bad or '없음'))
print('M1 grip', json.dumps(dict(two=sp_two, carry=sp_carry, same=same, R0=packs(R0), sockets=grip['brush']['sockets_in_brush_root_blender']), ensure_ascii=False))
print('M1 DONE bad', bad)
