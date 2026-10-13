"""#308 carry2: 어깨 걸치기 세 클립을 .blend 에서 수치로 잰다(읽기만, 저장 안 함).
    blender -b --factory-startup -P carry2_check.py -- [--out 파일.json]
좌표: 캐릭터는 -Y 를 본다. 앞(forward) = -y, 오른쪽(right) = -x, 위 = +z. 자루 = 꼬리 끝 → 붓머리 목(반지름 0.017 m), 붓머리 = 목 → 붓끝.
재는 값: 붓끝(TipSocket) - 머리 뼈(앞 · 오른쪽 · 위), 자루 축 ↔ 머리 · 목 뼈 선분 거리, 자루 축 ↔ 머리 · 목 메시 꼭짓점 거리, 자루 겉 ↔ 어깨 메시 틈, 오른손 쥐기 오차(StanceIdle 0 프레임 대비),
        전환 클립의 첫 · 끝 자세가 StanceIdle 0 / CarryIdle 0 과 같은가(모든 뼈 국소 회전 · 붓 월드 자리)."""
import bpy, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix
from mathutils.geometry import intersect_line_line
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
g = C.init(opt('--blend', C.BLEND)); arm = g['arm']; AW = g['AW']; rig = g['brush_rig']; sc = bpy.context.scene; dom = g['dom']
HEAD = np.array([d in ('Head', 'head_end', 'headfront') for d in dom]); NECK = np.array([d == 'neck' for d in dom])
SHO = np.array([d in ('RightShoulder', 'RightArm', 'Spine', 'Spine01', 'neck') for d in dom]); BODY = ~np.array([('Hand' in d or d == 'RightForeArm') for d in dom])
r1 = lambda x: round(float(x), 1); r3 = lambda v: [round(float(x), 3) for x in v]


def seg_seg(p1, q1, p2, q2):
    """두 선분 사이 가장 가까운 거리."""
    d1 = q1 - p1; d2 = q2 - p2; r = p1 - p2; a = d1.dot(d1); e = d2.dot(d2); f = d2.dot(r); c = d1.dot(r); b = d1.dot(d2); den = a * e - b * b
    s = max(0, min(1, (b * f - c * e) / den)) if den > 1e-12 else 0
    t = (b * s + f) / e
    if t < 0: t = 0; s = max(0, min(1, -c / a))
    elif t > 1: t = 1; s = max(0, min(1, (b - c) / a))
    return ((p1 + d1 * s) - (p2 + d2 * t)).length


def unit(M): return Matrix.LocRotScale(M.translation, M.to_quaternion(), Vector((1, 1, 1)))
def hand_rel(): return unit(AW @ arm.pose.bones['RightHand'].matrix).inverted() @ rig.matrix_world
def locals_(): return {pb.name: pb.matrix_basis.to_quaternion().copy() for pb in arm.pose.bones}, arm.pose.bones['Hips'].matrix_basis.translation.copy(), rig.matrix_world.copy()
def pose_diff(a, b):
    worst = max(((n, math.degrees(a[0][n].rotation_difference(b[0][n]).angle)) for n in a[0]), key=lambda kv: min(kv[1], 360 - kv[1])); w = min(worst[1], 360 - worst[1])
    return dict(max_bone_rot_deg=round(w, 4), worst_bone=worst[0], hips_pos_mm=round((a[1] - b[1]).length * AW.to_scale().x * 1000, 4), brush_pos_mm=round((a[2].translation - b[2].translation).length * 1000, 4),
                brush_rot_deg=round(math.degrees(a[2].to_quaternion().rotation_difference(b[2].to_quaternion()).angle), 4))


def at(clip, f):
    assert C.assign_clip(clip), clip; sc.frame_set(f); bpy.context.view_layer.update()


at('StanceIdle', 0); REL0 = hand_rel(); ST0 = locals_()
out = {}
for clip in ('CarryIdle', 'ToCarry', 'ToStance'):
    a = bpy.data.actions[C2.act(clip)]; f0, f1 = (int(round(x)) for x in a.frame_range); rows = []
    for f in range(f0, f1 + 1):
        at(clip, f); RW = rig.matrix_world; G = RW.translation.copy(); d = (RW.to_3x3() @ Vector((0, 0, 1))).normalized()
        tail = G - d * C.TAIL; nk = G + d * (C.TIPL - C.HEADL); tipax = G + d * C.TIPL; tip = bpy.data.objects['TipSocket'].matrix_world.translation.copy()
        hb = arm.pose.bones['Head']; nb = arm.pose.bones['neck']; Hh = AW @ hb.head; Ht = AW @ hb.tail; Nh = AW @ nb.head; Nt = AW @ nb.tail; sh = AW @ arm.pose.bones['RightArm'].head
        P = C.body_points(); ds, ts = C.dist_to_segment(P, tail, nk); dh, th = C.dist_to_segment(P, nk, tipax)
        rel = hand_rel(); hand = AW @ arm.pose.bones['RightHand'].head
        i_sho = int(np.argmin(np.where(SHO, ds, 9)))
        rows.append(dict(frame=f, tip_minus_head=dict(forward=round(-(tip.y - Hh.y), 3), right=round(-(tip.x - Hh.x), 3), up=round(tip.z - Hh.z, 3)), tip=r3(tip), head_bone=r3(Hh), right_shoulder_joint=r3(sh), tip_above_shoulder_joint_m=round(tip.z - sh.z, 3),
                         pitch_deg=r1(math.degrees(math.asin(d.z))), yaw_out_deg=r1(math.degrees(math.atan2(-d.x, d.y))), hand=r3(hand), hand_forward_of_shoulder_m=round(-(hand.y - sh.y), 3),
                         axis_to_head_bone_cm=r1(seg_seg(tail, tipax, Hh, Ht) * 100), axis_to_neck_bone_cm=r1(seg_seg(tail, tipax, Nh, Nt) * 100),
                         shaft_surface_to_head_mesh_cm=r1((ds[HEAD].min() - C.R_SHAFT) * 100), shaft_surface_to_neck_mesh_cm=r1((ds[NECK].min() - C.R_SHAFT) * 100), bristle_axis_to_head_mesh_cm=r1(dh[HEAD].min() * 100),
                         shaft_surface_to_shoulder_mesh_cm=r1((ds[SHO].min() - C.R_SHAFT) * 100), shoulder_contact_point=r3(P[i_sho]), shoulder_contact_along_shaft_from_hand_m=round(float(ts[i_sho]) * (nk - tail).length - (C.TAIL - C2.SHIFT), 3),
                         shaft_surface_to_body_mesh_cm=r1((ds[BODY].min() - C.R_SHAFT) * 100), bristle_axis_to_body_mesh_cm=r1(dh[BODY].min() * 100),
                         grip_pos_err_mm=round((rel.translation - REL0.translation).length * 1000, 4), grip_rot_err_deg=round(math.degrees(rel.to_quaternion().rotation_difference(REL0.to_quaternion()).angle), 4),
                         r_elbow_deg=r1(math.degrees((AW @ arm.pose.bones['RightArm'].head - AW @ arm.pose.bones['RightForeArm'].head).angle(AW @ arm.pose.bones['RightHand'].head - AW @ arm.pose.bones['RightForeArm'].head)))))
    def mn(k): i = min(range(len(rows)), key=lambda j: rows[j][k]); return [rows[i][k], rows[i]['frame']]
    def mx(k): i = max(range(len(rows)), key=lambda j: rows[j][k]); return [rows[i][k], rows[i]['frame']]
    S = dict(frames=len(rows), min_axis_to_head_bone_cm=mn('axis_to_head_bone_cm'), min_axis_to_neck_bone_cm=mn('axis_to_neck_bone_cm'), min_shaft_surface_to_head_mesh_cm=mn('shaft_surface_to_head_mesh_cm'),
             min_shaft_surface_to_neck_mesh_cm=mn('shaft_surface_to_neck_mesh_cm'), min_bristle_axis_to_head_mesh_cm=mn('bristle_axis_to_head_mesh_cm'), min_shaft_surface_to_body_mesh_cm=mn('shaft_surface_to_body_mesh_cm'),
             min_bristle_axis_to_body_mesh_cm=mn('bristle_axis_to_body_mesh_cm'), shoulder_gap_cm=[mn('shaft_surface_to_shoulder_mesh_cm')[0], mx('shaft_surface_to_shoulder_mesh_cm')[0]],
             max_grip_pos_err_mm=mx('grip_pos_err_mm')[0], max_grip_rot_err_deg=mx('grip_rot_err_deg')[0], r_elbow_deg=[mn('r_elbow_deg')[0], mx('r_elbow_deg')[0]], pitch_deg=[mn('pitch_deg')[0], mx('pitch_deg')[0]])
    out[clip] = dict(summary=S, first=rows[0], last=rows[-1], frames=rows)
    print('C2 sum', clip, json.dumps(S)); print('C2 first', clip, json.dumps(rows[0])); print('C2 last', clip, json.dumps({k: rows[-1][k] for k in ('tip_minus_head', 'pitch_deg', 'hand')}))
    if clip != 'CarryIdle': print('C2 frames', clip, [(r['frame'], r['pitch_deg'], r['axis_to_head_bone_cm'], r['shaft_surface_to_head_mesh_cm'], r['bristle_axis_to_head_mesh_cm'], r['shaft_surface_to_body_mesh_cm'], r['r_elbow_deg']) for r in rows])
# 이음매
at('CarryIdle', 0); CA0 = locals_(); at('CarryIdle', int(bpy.data.actions[C2.act('CarryIdle')].frame_range[1])); CA1 = locals_()
at('ToCarry', 0); TC0 = locals_(); at('ToCarry', int(bpy.data.actions[C2.act('ToCarry')].frame_range[1])); TC1 = locals_()
at('ToStance', 0); TS0 = locals_(); at('ToStance', int(bpy.data.actions[C2.act('ToStance')].frame_range[1])); TS1 = locals_()
out['seams'] = dict(ToCarry_first_vs_StanceIdle0=pose_diff(TC0, ST0), ToCarry_last_vs_CarryIdle0=pose_diff(TC1, CA0), ToStance_first_vs_CarryIdle0=pose_diff(TS0, CA0), ToStance_last_vs_StanceIdle0=pose_diff(TS1, ST0), CarryIdle_last_vs_first=pose_diff(CA1, CA0))
for k, v in out['seams'].items(): print('C2 seam', k, json.dumps(v))
if opt('--out'): json.dump(out, open(opt('--out'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('C2 DONE')
