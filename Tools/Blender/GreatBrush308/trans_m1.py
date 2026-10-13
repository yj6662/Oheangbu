"""#308 GreatBrush motion1: 손으로 만든 전환 둘(모캡 아님 — 열쇠 자세 둘 사이 보간).
    blender -b --factory-startup -P trans_m1.py -- [--carryG x,y,z] [--carryD x,y,z] [--to 0.75] [--back 0.35]
  Trans_ToCarry  = 전투 태세(Great_Sword_Idle 1프레임) → 왼손을 떼고 붓을 오른 어깨에 걸친다(기본 0.75 s)
  Trans_ToStance = 어깨 → 두 손 전투 태세(기본 0.35 s, 빠르게)
몸통 · 다리는 전투 태세 그대로(발 고정, 척추만 조금 편다). 오른팔은 붓을 따라가는 두 뼈 IK, 왼팔은 자루 → 늘어뜨림 보간. 붓털 2차 운동 없음.
액션 M1_Trans_*_{Body,Brush,Keys} + metrics/Trans_*.json (render_m1 · sheet_m1 이 그대로 읽는다)."""
import bpy, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C
from common_m1 import X, Y, Z, frame, axis_rot, two_bone, seg_pt
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
vec = lambda s: Vector([float(x) for x in s.split(',')])
CG = vec(opt('--carryG', '-.20,-.27,1.25')); CD = vec(opt('--carryD', '0,.75,.66')).normalized(); T_TO = float(opt('--to', .75)); T_BACK = float(opt('--back', .35))
GAP = .10; SHIFT = .30
g = C.init(); arm = g['arm']; RH, ORDER, PARENT = g['REST_HEAD'], g['ORDER'], g['PARENT']; sc = bpy.context.scene
assert C.assign_clip('Great_Sword_Idle'); sc.frame_set(1); bpy.context.view_layer.update()
P0 = C.Pose()
for n in ORDER:
    pb = arm.pose.bones[n]; r = g['REST_ROT'][n]; P0.L[n] = r @ pb.rotation_quaternion.to_matrix() @ r.inverted()
    if PARENT[n] is None: P0.hips_t = (r @ pb.location) * g['S_ARM']
P0.fk(); GR = C.Grip('Right'); GL = C.Grip('Left')
Qh0 = P0.Q['RightHand']; d0 = (Qh0 @ GR.A).normalized(); n0 = (Qh0 @ GR.N).normalized(); G0 = P0.H['RightHand'] + Qh0 @ (GR.C - RH['RightHand'])
ARM_BEND = {'Right': Z, 'Left': -Z}


def torso_caps(P):
    return [(P.H['Hips'] + Vector((0, 0, -.08)), P.H['Spine01'], .15), (P.H['Spine01'], P.H['neck'], .15), (P.H['Head'], P.H['Head'] + P.Q['Head'] @ Vector((0, -.03, .16)), .115)]


def attempt(P, s, sock, d, n, caps, hint):
    gr = GR if s == 'Right' else GL; up, lo, hd = s + 'Arm', s + 'ForeArm', s + 'Hand'; A = P.H[up]
    l1 = (RH[lo] - RH[up]).length; l2 = (RH[hd] - RH[lo]).length; u_rest = RH[lo] - RH[up]; f_rest = RH[hd] - RH[lo]; fa = f_rest.normalized(); tw_c = math.radians(80 if s == 'Right' else -80)
    qh = gr.hand_q(d, n); W = sock - qh @ (gr.C - RH[hd])
    E, W2, reach = two_bone(A, W, l1, l2, hint); b = (E - A).cross(W2 - E)
    if b.length < 1e-7: b = ARM_BEND[s]
    qu = frame(E - A, b) @ frame(u_rest, ARM_BEND[s]).transposed(); qf = frame(W2 - E, b) @ frame(f_rest, ARM_BEND[s]).transposed()
    q = (qf.inverted() @ qh).to_quaternion(); proj = fa.dot(Vector((q.x, q.y, q.z))); tw = 2 * math.atan2(proj, q.w)
    tw = (tw - tw_c + math.pi) % (2 * math.pi) - math.pi + tw_c
    sw = (q @ Quaternion(fa, tw).inverted()); swing = 2 * math.acos(max(-1, min(1, abs(sw.w)))); elbow = math.degrees((A - E).angle(W2 - E)); dtw = abs(tw - tw_c)
    cost = swing ** 2 + .04 * dtw ** 2 + 6 * max(0, dtw - math.radians(75)) ** 2 + (max(0, reach - .95) * 40) ** 2 + (max(0, 60 - elbow) / 15) ** 2 + (max(0, elbow - 160) / 25) ** 2
    for a_, b_, r_ in caps: cost += (max(0, r_ + .045 - seg_pt(W2, a_, b_)) * 25) ** 2 + (max(0, r_ + .02 - seg_pt(E, a_, b_)) * 25) ** 2
    return cost, dict(qh=qh, qu=qu, qf=qf, tw=tw, swing=swing, reach=reach, elbow=elbow, fa=fa, n=n)


def set_arm(P, s, r):
    P.set_world(s + 'Arm', r['qu']); P.set_world(s + 'ForeArm', r['qf'] @ axis_rot(r['fa'], math.degrees(r['tw']) * C.TWIST_SHARE)); P.set_world(s + 'Hand', r['qh'])


def best_n(P, s, sock, d, caps, hint, near=None, step=5):
    e1 = Z - d * Z.dot(d); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized(); e2 = d.cross(e1); best = None
    for k in range(0, 360, step):
        a = math.radians(k); n = e1 * math.cos(a) + e2 * math.sin(a); c, r = attempt(P, s, sock, d, n, caps, hint)
        if near is not None: c += (math.degrees(n.angle(near)) / 25) ** 2
        if best is None or c < best[0]: best = (c, r)
    return best


def body(s):
    """s = 0 전투 태세 … 1 어깨 걸치기. 몸통 · 다리(발 고정)."""
    P = C.Pose(); P.hips_t = P0.hips_t + Vector((0, 0, .015 * s))
    for n in ORDER:
        k = 1 - .4 * s if n in ('Spine02', 'Spine01', 'Spine', 'neck', 'Head') else 1
        P.L[n] = Quaternion().slerp(P0.L[n].to_quaternion(), k).to_matrix() if k < 1 else P0.L[n].copy()
    P.fk()
    for sd in ('Left', 'Right'):
        up, lo, ft = sd + 'UpLeg', sd + 'Leg', sd + 'Foot'; target = P0.H[ft]; qf = P0.Q[ft].copy()
        l1 = (RH[lo] - RH[up]).length; l2 = (RH[ft] - RH[lo]).length
        E, W2, reach = two_bone(P.H[up], target, l1, l2, P0.H[lo] - P0.H[up]); b = (E - P.H[up]).cross(W2 - E)
        P.set_world(up, frame(E - P.H[up], b) @ frame(RH[lo] - RH[up], X).transposed()); P.set_world(lo, frame(W2 - E, b) @ frame(RH[ft] - RH[lo], X).transposed()); P.set_world(ft, qf)
    return P


def hang(P, w):
    """왼팔을 늘어뜨린다(w = 1 완전히)."""
    Lh = {'LeftArm': C.rot('Y', 74), 'LeftForeArm': C.rot('Z', -16), 'LeftHand': Matrix.Identity(3)}
    for n in ('LeftArm', 'LeftForeArm', 'LeftHand'):
        tgt = (P.Q[PARENT[n]].inverted() @ (P.Q['Spine'] @ Lh[n])) if n == 'LeftArm' else Lh[n]
        P.L[n] = P.L[n].to_quaternion().slerp(tgt.to_quaternion(), w).to_matrix(); P.fk()


# ---- 어깨 걸치기 열쇠 자세: 오른손 손바닥 방향을 찾고, 자루가 몸에 묻히지 않을 때까지 올린다
P1 = body(1); caps = torso_caps(P1); hint1 = Vector((-.5, .2, -.85))
c1, r1 = best_n(P1, 'Right', CG, CD, caps, hint1, step=3); n1 = r1['n']
print('M1 carry', json.dumps(dict(G=[round(x, 3) for x in CG], d=[round(x, 3) for x in CD], cost=round(c1, 2), reach=round(r1['reach'], 3), elbow=round(r1['elbow'], 1), wrist_swing=round(math.degrees(r1['swing']), 1), twist=round(math.degrees(r1['tw']), 1))))
hint0 = P0.H['RightForeArm'] - P0.H['RightArm']


def ease(x): x = max(0, min(1, x)); return x * x * (3 - 2 * x)


def pose_at(s):
    """s = 0 … 1 (태세 → 걸치기) 의 온몸 + 붓."""
    P = body(ease(s)); u = ease((s - .12) / .88)                       # 붓은 왼손이 풀리기 시작한 뒤에 움직인다
    d = Quaternion().slerp(d0.rotation_difference(CD), u) @ d0; d.normalize()
    G = G0.lerp(CG, u) + Vector((0, -.05, .10)) * math.sin(math.pi * u)  # 살짝 앞 · 위로 돌아 올라간다
    nn = Quaternion().slerp(n0.rotation_difference(n1), u) @ n0; nn = (nn - d * nn.dot(d)).normalized()
    caps = torso_caps(P); hint = hint0.normalized().lerp(hint1.normalized(), u)
    c, r = attempt(P, 'Right', G, d, nn, caps, hint); set_arm(P, 'Right', r)
    for n_, l in GR.L.items(): P.L[n_] = l
    P.fk(); Gh = P.H['RightHand'] + P.Q['RightHand'] @ (GR.C - RH['RightHand']); dd = (P.Q['RightHand'] @ GR.A).normalized()
    won = 1 - ease(s / .30)                                             # 왼손: 0 – 30 % 구간에서 자루를 놓는다
    cl, rl = best_n(P, 'Left', Gh - dd * GAP, dd, caps, (P0.H['LeftForeArm'] - P0.H['LeftArm']).normalized() + (P.Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35, near=(P0.Q['LeftHand'] @ GL.N))
    set_arm(P, 'Left', rl); hang(P, 1 - won)
    for n_, l in GL.L.items(): P.L[n_] = Quaternion().slerp(l.to_quaternion(), .35 + .65 * won).to_matrix()
    P.fk()
    return P, dict(G=Gh + dd * SHIFT, Gh=Gh, d=dd, n=(P.Q['RightHand'] @ GR.N).normalized()), dict(r_cost=c, r_reach=r['reach'], r_elbow=r['elbow'], r_wrist_swing=math.degrees(r['swing']), l_on=won, grip_err_R=(Gh - G).length * 100)


def bake(name, svals):
    brush_rig = g['brush_rig']; brush = g['brush']; keys = brush.data.shape_keys
    for idb, suf in ((arm, 'Body'), (brush_rig, 'Brush'), (keys, 'Keys')):
        nm = 'M1_%s_%s' % (name, suf); old = bpy.data.actions.get(nm)
        if old: bpy.data.actions.remove(old)
        a = bpy.data.actions.new(nm); a.use_fake_user = True
        if idb.animation_data is None: idb.animation_data_create()
        idb.animation_data.action = a
    MET = []; prevq = None; prevb = None
    for i, s in enumerate(svals):
        f = i + 1; P, b, m = pose_at(s); prevq = P.apply(prevq)
        dn = (b['n'] - b['d'] * b['n'].dot(b['d'])).normalized(); R = Matrix((dn.cross(b['d']), dn, b['d'])).transposed(); q = R.to_quaternion()
        if prevb is not None and q.dot(prevb) < 0: q.negate()
        prevb = q; brush_rig.location = b['G']; brush_rig.rotation_quaternion = q
        for pb in brush_rig.pose.bones:
            if pb.name.startswith('Bristle'): pb.rotation_quaternion = Quaternion(); pb.keyframe_insert('rotation_quaternion', frame=f)
        keys.key_blocks['BristleSplay'].value = 0; keys.key_blocks['BristlePress'].value = 0; bpy.context.view_layer.update()
        for n in ORDER:
            pb = arm.pose.bones[n]; pb.keyframe_insert('rotation_quaternion', frame=f)
            if PARENT[n] is None: pb.keyframe_insert('location', frame=f)
        brush_rig.keyframe_insert('location', frame=f); brush_rig.keyframe_insert('rotation_quaternion', frame=f); keys.key_blocks['BristleSplay'].keyframe_insert('value', frame=f)
        Pw = C.body_points(); tail = b['G'] - b['d'] * C.TAIL; neck = b['G'] + b['d'] * (C.TIPL - C.HEADL); tip = b['G'] + b['d'] * C.TIPL
        ds, _ = C.dist_to_segment(Pw, tail, neck); pen = (ds < C.R_SHAFT - .004) & ~g['is_hand']; dh, th = C.dist_to_segment(Pw, neck, tip); penh = (dh < C.HEADR * (1 - th * .8) - .004) & ~g['is_hand']
        bn, cnt = np.unique(g['dom'][pen | penh], return_counts=True)
        m.update(frame=f, t=i / 30, s=s, G=list(b['G']), d=list(b['d']), hips=list(P.H['Hips']), head=list(P.H['Head']), tip=list(tip), shaft_in=int(pen.sum()), head_in=int(penh.sum()), in_by_bone=dict(zip(bn.tolist(), cnt.tolist())),
                 shaft_clear_cm=float((ds[~g['is_arm']].min() - C.R_SHAFT) * 100), tip_speed=0.0)
        MET.append(m)
    for i in range(len(MET)):
        a, b_ = MET[max(0, i - 1)], MET[min(len(MET) - 1, i + 1)]; MET[i]['tip_speed'] = (Vector(b_['tip']) - Vector(a['tip'])).length * 30 / max(1, b_['frame'] - a['frame'])
    rng = lambda k: [round(min(m[k] for m in MET), 2), round(max(m[k] for m in MET), 2)]
    summ = dict(clip=name, frames=[1, len(MET)], seconds=round((len(MET) - 1) / 30, 3), r_reach=rng('r_reach'), r_elbow=rng('r_elbow'), r_wrist_swing=rng('r_wrist_swing'), grip_err_R=rng('grip_err_R'), shaft_in_frames=sum(1 for m in MET if m['shaft_in']),
                shaft_in_max=max(m['shaft_in'] for m in MET), head_in_frames=sum(1 for m in MET if m['head_in']), in_by_bone_last=MET[-1]['in_by_bone'] if name.endswith('Carry') else MET[0]['in_by_bone'], shaft_clear_cm_end=round(MET[-1]['shaft_clear_cm'], 1), tip_speed_max=round(max(m['tip_speed'] for m in MET), 1))
    print('M1 summary', json.dumps(summ)); json.dump(dict(summary=summ, frames=MET), open('%s/metrics/%s.json' % (C.OUT, name), 'w', encoding='utf-8'), default=float)


n_to = round(T_TO * 30); n_back = round(T_BACK * 30)
bake('Trans_ToCarry', [i / n_to for i in range(n_to + 1)])
bake('Trans_ToStance', [1 - ease(i / n_back) ** .8 for i in range(n_back + 1)])     # 되돌아올 때는 앞쪽이 빠르다
if '--nosave' not in ARGS: bpy.ops.wm.save_as_mainfile(filepath=C.BLEND); print('M1 saved')
print('M1 DONE')
