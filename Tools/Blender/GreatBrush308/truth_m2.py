"""#308 GreatBrush motion2: Unity 쥐기 보정용 "참값" — 열 클립 모든 프레임의 붓 소켓 · 관절 월드 자리(m).  읽기만 한다(.blend 저장 안 함).
    blender -b --factory-startup -P truth_m2.py
출력: motion2/metrics/brush_truth.json"""
import bpy, json, os, sys
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
g = C.init(C.BLEND); arm = g['arm']; AW = g['AW']; rig = g['brush_rig']; sc = bpy.context.scene
JOINTS = ['RightHand', 'LeftHand', 'RightForeArm', 'RightArm', 'RightShoulder', 'Head', 'Hips', 'Spine', 'LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase']
SOCKS = [k for k in ('GripSocket', 'TipSocket', 'GripSocketL', 'ShaftEndSocket') if k in bpy.data.objects]
r5 = lambda v: [round(float(x), 5) for x in v]
def uni(v): return [round(float(-v[0]), 5), round(float(v[2]), 5), round(float(-v[1]), 5)]
# 고리 A 쪽 축: 붓 리그 로컬에서 BristleRing_A_01 뼈 머리가 축(+Z)에서 벗어난 방향을 재서 확인한다
RA = rig.pose.bones['BristleRing_A_01'].bone.head_local.copy(); RAside = Vector((RA.x, RA.y, 0))
AXIS = Vector((1, 0, 0)); axis_note = 'Blender 붓 리그(GreatBrush308_Rig) 로컬 +X  (= Unity 모델 축 식 (x,y,z)->(-x,z,-y) 로 Unity 로컬 -X)'
ring = dict(BristleRing_A_01_head_in_rig_local_blender=r5(RA), radial_dir_in_rig_local_blender=r5(RAside.normalized()) if RAside.length > 1e-6 else None, dot_with_local_plusX=round(RAside.normalized().dot(AXIS), 4) if RAside.length > 1e-6 else None)
print('TR ring', json.dumps(ring))
out = dict(convention=dict(
    blender='캐릭터는 원점에 서서 -Y 를 본다. 오른쪽 = -X, 위 = +Z. 단위 m, 월드 자리.',
    unity='unity = [-x_b, z_b, -y_b] → 오른쪽 +X, 위 +Y, 앞 +Z. 캐릭터 뿌리가 원점 · 회전 없음일 때의 캐릭터 공간(= 월드).',
    points='관절 = 뼈 머리(head) 자리. Spine = 맨 위 척추 뼈(mixamo Spine2). 붓 소켓 = 같은 이름 빈 물체의 월드 자리. TipSocket 은 붓털 뼈 자식이라 붓털 휨에 따라 축에서 벗어난다.',
    RingASide10cm='GripSocket + 0.10 m × (붓 리그 로컬 +X 의 월드 방향). 쓴 축: ' + axis_note, ring_a_check=ring,
    BrushRootZ10cm='GripSocket + 0.10 m × (붓 리그 로컬 +Z = 붓끝 방향) — 참고용',
    source='GreatBrush308_motion2.blend 의 구운 액션 A_Brush308_<이름>[__Brush · __Keys], 프레임 0 부터, 30 fps. 시트 스틸과 같은 장면.'), fps=30, clips={})
# 쉬는 자세
rest = {n: AW @ arm.data.bones[n].head_local for n in JOINTS}
out['rest'] = {n: dict(blender=r5(p), unity=uni(p)) for n, p in rest.items()}
U = {n: uni(p) for n, p in rest.items()}
chk = dict(RightHand_unity_x_positive=U['RightHand'][0] > 0, LeftHand_unity_x_negative=U['LeftHand'][0] < 0, Head_unity_y_positive=U['Head'][1] > 0, toes_unity_z_ahead_of_ankles=U['LeftToeBase'][2] > U['LeftFoot'][2] and U['RightToeBase'][2] > U['RightFoot'][2])
out['convention']['rest_pose_check'] = chk; print('TR rest check', json.dumps(chk)); assert all(chk.values())
for clip in C2.NAMES:
    assert C.assign_clip(clip), clip; a = bpy.data.actions[C2.act(clip)]; f0, f1 = (int(round(x)) for x in a.frame_range)
    names = SOCKS + ['RingASide10cm', 'BrushRootZ10cm'] + JOINTS; pts = {n: dict(blender=[], unity=[]) for n in names}
    for f in range(f0, f1 + 1):
        sc.frame_set(f); bpy.context.view_layer.update(); cur = {}
        for k in SOCKS: cur[k] = bpy.data.objects[k].matrix_world.translation.copy()
        R = rig.matrix_world.to_3x3(); G = cur['GripSocket']
        cur['RingASide10cm'] = G + (R @ AXIS).normalized() * .10; cur['BrushRootZ10cm'] = G + (R @ Vector((0, 0, 1))).normalized() * .10
        for n in JOINTS: cur[n] = AW @ arm.pose.bones[n].head
        for n in names: pts[n]['blender'].append(r5(cur[n])); pts[n]['unity'].append(uni(cur[n]))
    out['clips'][clip] = dict(frames=f1 - f0 + 1, first_frame=f0, points=pts)
    print('TR clip', clip, f1 - f0 + 1, json.dumps({n: pts[n]['unity'][0] for n in ('GripSocket', 'TipSocket', 'GripSocketL', 'RightHand', 'LeftHand', 'Head')}))
    print('TR root', clip, 'rig-GripSocket mm', round((rig.matrix_world.translation - bpy.data.objects['GripSocket'].matrix_world.translation).length * 1000, 3))
p = C.OUT + '/metrics/brush_truth.json'; json.dump(out, open(p, 'w', encoding='utf-8'), ensure_ascii=False); print('TR DONE', p, os.path.getsize(p))
