"""#308 swing3: 휘두르기 셋 · 대기 둘을 .blend 에서 수치로 잰다(읽기만, 저장 안 함).
    blender -b --factory-startup -P swing3_check.py -- [--out 파일.json] [--blend 경로]
좌표: 캐릭터는 -Y 를 본다. 오른쪽 = -x, 위 = +z. 뒤 카메라에서 가로 = x, 세로 = z.
휘두르기: 정점 시각 · 붓끝 최고 속도와 때 · 붓끝 길의 가로/세로 폭 · 윗몸 요(맨 위 척추, 첫 프레임 대비) · 발 이동 · 왼손-GripSocketL 오차 · 이음매(첫/끝 프레임 vs StanceIdle 0, 모든 뼈 + 붓).
대기: 루프 닫힘(끝 vs 첫) · 가슴/엉덩이/머리/붓끝/손 움직임 폭.  + 어깨 걸치기 전환 이음매(carry2_check 와 같은 식)."""
import bpy, json, math, os, sys
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2, clips_m2
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
g = C.init(opt('--blend', C.BLEND)); arm = g['arm']; AW = g['AW']; rig = g['brush_rig']; sc = bpy.context.scene; GL = C.Grip('Left')
REST = {b.name: (AW @ b.matrix_local).to_quaternion() for b in arm.data.bones}
r1 = lambda x: round(float(x), 1); r3 = lambda v: [round(float(x), 3) for x in v]


def unit(M): return Matrix.LocRotScale(M.translation, M.to_quaternion(), Vector((1, 1, 1)))
def locals_(): return {pb.name: pb.matrix_basis.to_quaternion().copy() for pb in arm.pose.bones}, arm.pose.bones['Hips'].matrix_basis.translation.copy(), rig.matrix_world.copy()
def pose_diff(a, b):
    worst = max(((n, math.degrees(a[0][n].rotation_difference(b[0][n]).angle)) for n in a[0]), key=lambda kv: min(kv[1], 360 - kv[1])); w = min(worst[1], 360 - worst[1])
    return dict(max_bone_rot_deg=round(w, 4), worst_bone=worst[0], hips_pos_mm=round((a[1] - b[1]).length * AW.to_scale().x * 1000, 4), brush_pos_mm=round((a[2].translation - b[2].translation).length * 1000, 4),
                brush_rot_deg=round(math.degrees(a[2].to_quaternion().rotation_difference(b[2].to_quaternion()).angle), 4))
def at(clip, f): assert C.assign_clip(clip), clip; sc.frame_set(f); bpy.context.view_layer.update()
def rng(clip): a = bpy.data.actions[C2.act(clip)]; return tuple(int(round(x)) for x in a.frame_range)
def J(n): return AW @ arm.pose.bones[n].head
def yaw(n):
    q = (AW @ arm.pose.bones[n].matrix).to_quaternion() @ REST[n].inverted(); f = q @ Vector((0, -1, 0)); return math.degrees(math.atan2(-f.x, -f.y))
def span(vs): return [round(max(v[k] for v in vs) - min(v[k] for v in vs), 4) for k in range(3)]
def path_len(vs): return round(sum((b - a).length for a, b in zip(vs, vs[1:])), 3)


at('StanceIdle', 0); ST0 = locals_()
out = {}; SW = [n for n in ('SwingSweep', 'SwingArc', 'SwingSlam') if bpy.data.actions.get(C2.act(n))]
for clip in SW:
    f0, f1 = rng(clip); T = []; Y = []; YH = []; feet = {k: [] for k in ('LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase')}; lerr = []; D = []; hips = []
    for f in range(f0, f1 + 1):
        at(clip, f); T.append(bpy.data.objects['TipSocket'].matrix_world.translation.copy()); Y.append(yaw('Spine')); YH.append(yaw('Hips')); hips.append(J('Hips'))
        for k in feet: feet[k].append(J(k))
        lb = arm.pose.bones['LeftHand']; ML = AW @ lb.matrix; cL = ML @ (AW @ lb.bone.matrix_local).inverted() @ GL.C; lerr.append((cL - bpy.data.objects['GripSocketL'].matrix_world.translation).length * 100)
        D.append((rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized())
    def unwrap(a):
        o = [a[0]]
        for v in a[1:]:
            while v - o[-1] > 180: v -= 360
            while v - o[-1] < -180: v += 360
            o.append(v)
        return o
    Y = unwrap(Y); YH = unwrap(YH); n = len(T); spd = [(T[min(i + 1, n - 1)] - T[max(i - 1, 0)]).length * 30 / max(1, min(i + 1, n - 1) - max(i - 1, 0)) for i in range(n)]
    ip = max(range(n), key=lambda i: spd[i]); apex = clips_m2.CLIPS[clip].get('apex'); ia = apex * 30; i0 = int(math.floor(ia)); i1 = min(i0 + 1, n - 1); w = ia - i0
    at(clip, f0); A0 = locals_(); at(clip, f1); A1 = locals_()
    # 붓 방향이 쓸고 간 각(이웃 프레임 사이 각의 합) — 감기(첫 프레임 → 가장 느린 되돌이 점)와 긋기를 나눠 적는다
    ang = [math.degrees(D[i].angle(D[i + 1])) for i in range(n - 1)]; iw = min(range(1, max(2, int(ia))), key=lambda i: spd[i]) if ia > 2 else 0
    ihold = [i for i in range(n) if ia <= i <= ia + 4]
    S = dict(frames=n, seconds=round((n - 1) / 30, 4), apex_s=apex, tip_speed_at_apex=r1(spd[i0] * (1 - w) + spd[i1] * w), peak_tip_speed=r1(spd[ip]), peak_tip_speed_s=round(ip / 30, 3),
             tip_speed_after_apex_frames={('%.3f' % (i / 30)): r1(spd[i]) for i in ihold}, tip_span_lateral_x_m=span(T)[0], tip_span_vertical_z_m=span(T)[2], tip_span_depth_y_m=span(T)[1], tip_path_m=path_len(T),
             tip_x_range=[round(min(t.x for t in T), 3), round(max(t.x for t in T), 3)], tip_z_range=[round(min(t.z for t in T), 3), round(max(t.z for t in T), 3)],
             windup_slowest_s=round(iw / 30, 3), brush_travel_deg_windup=r1(sum(ang[:iw])), brush_travel_deg_stroke_to_apex=r1(sum(ang[iw:i1])), brush_travel_deg_stroke_to_hold=r1(sum(ang[iw:min(n - 1, i1 + 3)])),
             torso_yaw_deg_vs_first=[r1(min(y - Y[0] for y in Y)), r1(max(y - Y[0] for y in Y))], hips_yaw_deg_vs_first=[r1(min(y - YH[0] for y in YH)), r1(max(y - YH[0] for y in YH))],
             feet_move_max_cm={k: r1(max((p - v[0]).length for p in v) * 100) for k, v in feet.items()}, hips_move_cm=[r1(x * 100) for x in span(hips)],
             left_hand_to_socketL_cm_max=round(max(lerr), 2), left_hand_off_frames=sum(1 for e in lerr if e > .5),
             seam_first_vs_StanceIdle0=pose_diff(A0, ST0), seam_last_vs_StanceIdle0=pose_diff(A1, ST0))
    out[clip] = dict(summary=S, tip=[r3(t) for t in T], tip_speed=[r1(s) for s in spd], torso_yaw=[r1(y - Y[0]) for y in Y], left_err_cm=[round(e, 2) for e in lerr])
    print('S3 sum', clip, json.dumps(S)); print('S3 tip', clip, [(i, round(t.x, 2), round(t.y, 2), round(t.z, 2), r1(spd[i]), r1(Y[i] - Y[0])) for i, t in enumerate(T)])
for clip in ('StanceIdle', 'CarryIdle'):
    f0, f1 = rng(clip); P = {k: [] for k in ('Spine', 'Hips', 'Head', 'RightHand', 'LeftHand', 'RightShoulder', 'LeftShoulder', 'RightArm', 'LeftFoot', 'RightFoot')}; T = []; HY = []; D = []
    for f in range(f0, f1 + 1):
        at(clip, f); T.append(bpy.data.objects['TipSocket'].matrix_world.translation.copy()); HY.append(yaw('Head')); D.append((rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized())
        for k in P: P[k].append(J(k))
    at(clip, f0); A0 = locals_(); at(clip, f1); A1 = locals_(); n = len(T); spd = [(T[min(i + 1, n - 1)] - T[max(i - 1, 0)]).length * 30 / max(1, min(i + 1, n - 1) - max(i - 1, 0)) for i in range(n)]
    # 붓이 몸과 따로 노는 정도: 붓끝 자리에서 가슴(Spine) 자리를 뺀 것의 폭
    rel = [t - s for t, s in zip(T, P['Spine'])]
    S = dict(frames=n, seconds=round((n - 1) / 30, 4), loop_last_vs_first=pose_diff(A1, A0), first_vs_StanceIdle0=pose_diff(A0, ST0) if clip == 'StanceIdle' else None,
             amp_cm={k: [r1(x * 100) for x in span(v)] for k, v in P.items()}, tip_amp_cm=[r1(x * 100) for x in span(T)], tip_minus_chest_amp_cm=[r1(x * 100) for x in span(rel)], tip_path_cm=r1(path_len(T) * 100), tip_speed_max=round(max(spd), 3),
             head_yaw_range_deg=r1(max(HY) - min(HY)), brush_dir_range_deg=r1(max(D[0].angle(d) for d in D) * 180 / math.pi), chest_minus_hips_amp_cm=[r1(x * 100) for x in span([a - b for a, b in zip(P['Spine'], P['Hips'])])])
    out[clip] = dict(summary=S, tip=[r3(t) for t in T]); print('S3 sum', clip, json.dumps(S))
E = {}
for clip in ('CarryIdle', 'ToCarry', 'ToStance'):
    f0, f1 = rng(clip); at(clip, f0); a = locals_(); at(clip, f1); E[clip] = (a, locals_())
out['seams'] = dict(ToCarry_first_vs_StanceIdle0=pose_diff(E['ToCarry'][0], ST0), ToCarry_last_vs_CarryIdle0=pose_diff(E['ToCarry'][1], E['CarryIdle'][0]), ToStance_first_vs_CarryIdle0=pose_diff(E['ToStance'][0], E['CarryIdle'][0]),
                    ToStance_last_vs_StanceIdle0=pose_diff(E['ToStance'][1], ST0), CarryIdle_last_vs_first=pose_diff(E['CarryIdle'][1], E['CarryIdle'][0]))
for other in ('GuardIn', 'BuffDraw'):
    f0, f1 = rng(other); at(other, f0); out['seams'][other + '_first_vs_StanceIdle0'] = pose_diff(locals_(), ST0)
for k, v in out['seams'].items(): print('S3 seam', k, json.dumps(v))
if opt('--out'): json.dump(out, open(opt('--out'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('S3 DONE')
