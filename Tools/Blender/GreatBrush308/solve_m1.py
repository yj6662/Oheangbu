"""#308 GreatBrush motion1: 모캡 한 클립 → 플레이어 리그(리타깃) + 큰 붓(오른손 고정 쥐기) + 왼손 IK + 붓털 스프링 → 액션 3개를 .blend 에 굽고 수치를 적는다.
    blender -b --factory-startup -P solve_m1.py -- <clip> [--gap .42] [--gapmin .0] [--wristfix x,y,z | auto] [--blend 경로] [--tag 이름] [--nosave]
액션: M1_<clip>_Body(몸) · M1_<clip>_Brush(붓 리그 위치 · 회전 + 붓털 뼈) · M1_<clip>_Keys(BristleSplay).  수치: motion1/metrics/<clip>[_tag].json
리타깃: 뼈마다 월드 회전 변화량(쉬는 자세 대비)을 옮기고, 팔다리는 쉬는 자세 방향 차이를 보정한다. 엉덩이는 다리 길이 비로, 발목은 원본 발목 × 비율 자리에 두 뼈 IK."""
import bpy, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C
from common_m1 import X, Y, Z, frame, axis_rot, two_bone, seg_pt

ARGS = sys.argv[sys.argv.index('--') + 1:]
CLIP = ARGS[0]
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
GAP = float(opt('--gap', C.HAND_GAP)); GAPMIN = float(opt('--gapmin', 0)); TAG = opt('--tag', ''); BLEND = opt('--blend', C.BLEND); WFIX = opt('--wristfix', 'none'); SHIFT = float(opt('--gripshift', 0))      # SHIFT = 붓을 손 안에서 붓끝 쪽으로 민 거리(손이 꼬리에 가까워진다)
os.makedirs(C.OUT + '/metrics', exist_ok=True); os.makedirs(os.path.dirname(BLEND), exist_ok=True)
g = C.init(BLEND); arm = g['arm']; RH, RT, ORDER, PARENT = g['REST_HEAD'], g['REST_TAIL'], g['ORDER'], g['PARENT']
sc = bpy.context.scene

# ---------------------------------------------------------------- 원본 표본
before = set(bpy.data.objects); acts0 = set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath='%s/%s.fbx' % (C.MOCAP, CLIP), automatic_bone_orientation=False)
new = [o for o in bpy.data.objects if o not in before]; src = next(o for o in new if o.type == 'ARMATURE'); act = src.animation_data.action
SW = src.matrix_world.copy(); f0, f1 = (int(round(x)) for x in act.frame_range); NF = f1 - f0 + 1
by = {b.name.split(':')[-1]: b for b in src.pose.bones}
sr = {n: (SW @ b.bone.matrix_local) for n, b in by.items()}
SRH = {n: m.translation.copy() for n, m in sr.items()}; SRQ = {n: m.to_quaternion().to_matrix() for n, m in sr.items()}
S_pos = []; S_dq = []
for f in range(f0, f1 + 1):
    sc.frame_set(f); pos = {}; dq = {}
    for n, b in by.items():
        if 'Hand' in n and not n.endswith('Hand'): continue
        m = SW @ b.matrix; pos[n] = m.translation.copy(); dq[n] = m.to_quaternion().to_matrix() @ SRQ[n].inverted()
    S_pos.append(pos); S_dq.append(dq)
for o in new: bpy.data.objects.remove(o, do_unlink=True)
for a in set(bpy.data.actions) - acts0: bpy.data.actions.remove(a)
sc.frame_set(f0)

# ---------------------------------------------------------------- 비례
src_floor = SRH['LeftToeBase'].z                                  # 원본 쉬는 자세의 바닥(발가락 뿌리) — 재생 중 발목 높이 = 쉬는 발목 - 이 값
src_ankle = {s: SRH[s + 'Foot'].z - src_floor for s in ('Left', 'Right')}
src_leg = (SRH['LeftLeg'] - SRH['LeftUpLeg']).length + (SRH['LeftFoot'] - SRH['LeftLeg']).length
tgt_leg = (RH['LeftLeg'] - RH['LeftUpLeg']).length + (RH['LeftFoot'] - RH['LeftLeg']).length
S = tgt_leg / src_leg
src_h2u = SRH['Hips'].z - SRH['LeftUpLeg'].z; tgt_h2u = RH['Hips'].z - (RH['LeftUpLeg'].z + RH['RightUpLeg'].z) / 2
tgt_ankle = {s: RH[s + 'Foot'].z for s in ('Left', 'Right')}
src_arm = (SRH['LeftForeArm'] - SRH['LeftArm']).length + (SRH['LeftHand'] - SRH['LeftForeArm']).length
tgt_arm = (RH['LeftForeArm'] - RH['LeftArm']).length + (RH['LeftHand'] - RH['LeftForeArm']).length
src_ground_z0 = min(min(p['LeftFoot'].z, p['RightFoot'].z) for p in S_pos)      # 재생 중 가장 낮은 발목(원본 월드)
print('M1 scale', json.dumps(dict(clip=CLIP, frames=NF, leg_ratio=round(S, 4), src_leg=round(src_leg, 3), tgt_leg=round(tgt_leg, 3), arm_ratio=round(tgt_arm / src_arm, 3), src_ankle_rest=round(src_ankle['Left'], 3),
                                   src_ankle_min_anim=round(src_ground_z0, 3), tgt_ankle=[round(tgt_ankle['Left'], 3), round(tgt_ankle['Right'], 3)])))
LIMB = ['LeftUpLeg', 'LeftLeg', 'RightUpLeg', 'RightLeg', 'LeftArm', 'LeftForeArm', 'RightArm', 'RightForeArm']
CHILD = {'LeftUpLeg': 'LeftLeg', 'LeftLeg': 'LeftFoot', 'RightUpLeg': 'RightLeg', 'RightLeg': 'RightFoot', 'LeftArm': 'LeftForeArm', 'LeftForeArm': 'LeftHand', 'RightArm': 'RightForeArm', 'RightForeArm': 'RightHand'}
CORR = {n: (RH[CHILD[n]] - RH[n]).normalized().rotation_difference((SRH[CHILD[n]] - SRH[n]).normalized()).to_matrix() for n in LIMB}
GR = C.Grip('Right'); GL = C.Grip('Left')
fixM = Matrix.Identity(3)
if WFIX not in ('none', 'auto'): fixM = GR.A.rotation_difference(Vector([float(x) for x in WFIX.split(',')]).normalized()).to_matrix()
ARM_BEND = {'Right': Z, 'Left': -Z}


def retarget(i):
    pos, dq = S_pos[i], S_dq[i]; P = C.Pose(); Q = {}
    hz = tgt_ankle['Left'] + (pos['Hips'].z - src_ankle['Left'] - src_h2u) * S + tgt_h2u
    P.hips_t = Vector((pos['Hips'].x * S, pos['Hips'].y * S, hz)) - RH['Hips']
    for n in ORDER:
        sn = C.ALIAS.get(n, n); p = PARENT[n]
        if sn in dq:
            q = dq[sn] @ CORR[n] if n in CORR else dq[sn].copy()
            P.L[n] = (Q[p].inverted() @ q) if p else q; Q[n] = q
        else: Q[n] = (Q[p] @ P.L[n]) if p else P.L[n].copy()
    P.fk(); m = {}
    # 다리: 발목을 원본 발목 × 비율 자리에. 무릎 방향은 리타깃된 FK 무릎에서.
    for s in ('Left', 'Right'):
        up, lo, ft = s + 'UpLeg', s + 'Leg', s + 'Foot'; sp = pos[ft]
        target = Vector((sp.x * S, sp.y * S, tgt_ankle[s] + (sp.z - src_ankle[s]) * S))
        l1 = (RH[lo] - RH[up]).length; l2 = (RH[ft] - RH[lo]).length; qf = P.Q[ft].copy()
        hint = P.H[lo] - P.H[up]
        E, W2, reach = two_bone(P.H[up], target, l1, l2, hint); b = (E - P.H[up]).cross(W2 - E)
        if b.length < 1e-6: b = P.Q[up] @ X
        P.set_world(up, frame(E - P.H[up], b) @ frame(RH[lo] - RH[up], X).transposed())
        P.set_world(lo, frame(W2 - E, b) @ frame(RH[ft] - RH[lo], X).transposed())
        P.set_world(ft, qf)
        m['leg_reach_' + s] = reach; m['ankle_err_' + s] = (P.H[ft] - target).length; m['ankle_z_' + s] = P.H[ft].z - tgt_ankle[s]
        m['src_ankle_z_' + s] = sp.z - src_ankle[s]
    return P, m


def torso_caps(P):
    return [(P.H['Hips'] + Vector((0, 0, -.08)), P.H['Spine01'], .15), (P.H['Spine01'], P.H['neck'], .15), (P.H['Head'], P.H['Head'] + P.Q['Head'] @ Vector((0, -.03, .16)), .115)]


def left_attempt(P, sock, d, n, caps, hint):
    s = 'Left'; up, lo, hd = 'LeftArm', 'LeftForeArm', 'LeftHand'; gr = GL; A = P.H[up]
    l1 = (RH[lo] - RH[up]).length; l2 = (RH[hd] - RH[lo]).length; u_rest = RH[lo] - RH[up]; f_rest = RH[hd] - RH[lo]; fa = f_rest.normalized(); tw_c = math.radians(-80)
    qh = gr.hand_q(d, n); W = sock - qh @ (gr.C - RH[hd])
    E, W2, reach = two_bone(A, W, l1, l2, hint); b = (E - A).cross(W2 - E)
    if b.length < 1e-7: b = ARM_BEND[s]
    qu = frame(E - A, b) @ frame(u_rest, ARM_BEND[s]).transposed(); qf = frame(W2 - E, b) @ frame(f_rest, ARM_BEND[s]).transposed()
    q = (qf.inverted() @ qh).to_quaternion(); proj = fa.dot(Vector((q.x, q.y, q.z))); tw = 2 * math.atan2(proj, q.w)
    tw = (tw - tw_c + math.pi) % (2 * math.pi) - math.pi + tw_c
    sw = (q @ Quaternion(fa, tw).inverted()); swing = 2 * math.acos(max(-1, min(1, abs(sw.w))))
    elbow = math.degrees((A - E).angle(W2 - E)); dtw = abs(tw - tw_c)
    cost = swing ** 2 + .04 * dtw ** 2 + 6 * max(0, dtw - math.radians(75)) ** 2 + (max(0, reach - .95) * 40) ** 2 + (max(0, 60 - elbow) / 15) ** 2 + (max(0, elbow - 160) / 25) ** 2
    for a_, b_, r_ in caps:
        cost += (max(0, r_ + .045 - seg_pt(W2, a_, b_)) * 25) ** 2 + (max(0, r_ + .02 - seg_pt(E, a_, b_)) * 25) ** 2
    return cost, dict(qh=qh, qu=qu, qf=qf, tw=tw, swing=swing, reach=reach, elbow=elbow, fa=fa, W=W, W2=W2, E=E, n=n)


# ---------------------------------------------------------------- 1) 몸 + 붓 자리
POSES = []; MET = []; BR = []
vloc = []; PULLMAX = float(opt('--pullmax', .16)); l_arm = tgt_arm; raw = []
for i in range(NF):
    P, m = retarget(i)
    if WFIX != 'none' and WFIX != 'auto': P.set_world('RightHand', P.Q['RightHand'] @ fixM)
    POSES.append(P); MET.append(m)
    # 팔이 원본보다 12 % 짧다: 왼손이 자루에 닿지 않으면 오른손(붓)을 왼 어깨 쪽으로 당긴다(최대 PULLMAX, 그보다 멀면 왼손을 놓는다)
    Qh = P.Q['RightHand']; d = (Qh @ GR.A).normalized(); Gp = P.H['RightHand'] + Qh @ (GR.C - RH['RightHand']); v = P.H['LeftArm'] - (Gp - d * GAP)
    over = v.length - .03 - .93 * l_arm
    raw.append(v.normalized() * over if 0 < over <= PULLMAX else Vector())
PULL = [sum(raw[max(0, i - 2):i + 3], Vector()) / len(raw[max(0, i - 2):i + 3]) for i in range(NF)]
for i in range(NF):
    P = POSES[i]; m = MET[i]; Qh = P.Q['RightHand'].copy(); dl = PULL[i]; m['r_pull_cm'] = dl.length * 100
    if dl.length > 1e-4:
        A = P.H['RightArm']; E0 = P.H['RightForeArm']; W0 = P.H['RightHand']; l1 = (E0 - A).length; l2 = (W0 - E0).length
        E, W2, reach = two_bone(A, W0 + dl, l1, l2, E0 - A); qu0 = P.Q['RightArm'].copy(); qf0 = P.Q['RightForeArm'].copy()
        P.set_world('RightArm', (E0 - A).rotation_difference(E - A).to_matrix() @ qu0)
        P.set_world('RightForeArm', (W0 - E0).rotation_difference(W2 - E).to_matrix() @ qf0); P.set_world('RightHand', Qh)
    d = (Qh @ GR.A).normalized(); n = (Qh @ GR.N).normalized(); Gp = P.H['RightHand'] + Qh @ (GR.C - RH['RightHand'])
    pos = S_pos[i]; sv = pos['RightHand'] - pos['LeftHand']; m['src_hand_dist'] = sv.length
    m['axis_vs_src_deg'] = math.degrees(d.angle(sv)) if sv.length > 1e-6 else 0
    vloc.append(S_dq[i]['RightHand'].inverted() @ sv.normalized())
    for n_, l in GR.L.items(): P.L[n_] = l
    m['l_fk_elbow'] = P.H['LeftForeArm'] - P.H['LeftArm']
    P.fk(); BR.append(dict(G=Gp + d * SHIFT, Gh=Gp, d=d, n=n))
mv = sum(vloc, Vector()) / NF; spread = max(math.degrees(v.angle(mv)) for v in vloc)
print('M1 srcaxis', json.dumps(dict(mean_local=[round(x, 3) for x in mv.normalized()], mean_len=round(mv.length, 3), max_dev_deg=round(spread, 1), gripA=[round(x, 3) for x in GR.A], A_vs_mean_deg=round(math.degrees(GR.A.angle(mv)), 1),
                                     axis_vs_src_deg_mean=round(sum(m['axis_vs_src_deg'] for m in MET) / NF, 1), src_hand_dist=[round(min(m['src_hand_dist'] for m in MET), 3), round(max(m['src_hand_dist'] for m in MET), 3)])))

# ---------------------------------------------------------------- 2) 왼손: 손 간격(미끄러짐 허용 시) + 손바닥 방향 경로(비터비)
gaps = []
for i in range(NF):
    P = POSES[i]; b = BR[i]; gp = GAP
    if GAPMIN > 0:
        while gp > GAPMIN and ((b['Gh'] - b['d'] * gp) - P.H['LeftArm']).length - .03 > .93 * l_arm: gp -= .01     # 손목은 소켓보다 3 cm 쯤 어깨 쪽
    gaps.append(gp)
if GAPMIN > 0:      # 급히 바뀌지 않게: 둘레 최소 → 평균
    gm = [min(gaps[max(0, i - 3):i + 4]) for i in range(NF)]; gaps = [sum(gm[max(0, i - 3):i + 4]) / len(gm[max(0, i - 3):i + 4]) for i in range(NF)]
K = 72; costs = np.zeros((NF, K)); E1 = []; minreach = []
e1 = None
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']
    if e1 is None:
        e1 = Z - d * Z.dot(d); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized()
    else: e1 = (e1 - d * e1.dot(d)).normalized()                       # 평행 이동(붓이 세로를 지날 때 뒤집히지 않게)
    e2 = d.cross(e1); E1.append((e1.copy(), e2.copy()))
    caps = torso_caps(P); hint = MET[i]['l_fk_elbow'].normalized() + (P.Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35; sock = b['Gh'] - d * gaps[i]
    mr = 9
    for k in range(K):
        a = 2 * math.pi * k / K; costs[i, k], r_ = left_attempt(P, sock, d, e1 * math.cos(a) + e2 * math.sin(a), caps, hint)
        mr = min(mr, r_['reach'])
        if i == 0 and '--debug' in ARGS and k % 6 == 0: print('M1 dbg', k * 5, round(costs[i, k], 1), 'reach %.2f elbow %.0f swing %.0f tw %.0f' % (r_['reach'], r_['elbow'], math.degrees(r_['swing']), math.degrees(r_['tw'])), 'sock', [round(x, 2) for x in sock], 'W', [round(x, 2) for x in r_['W']], 'shoulder', [round(x, 2) for x in P.H['LeftArm']], 'RH', [round(x, 2) for x in P.H['RightHand']])
    minreach.append(mr)
# 왼손이 닿지 않는 구간(한 손 무기 원본 · 팔을 뻗은 구간)은 왼손을 놓는다: 닿음 여부 → 둘레 침식 → 평균 = 쥐기 가중치(0 = 원본 왼팔 그대로)
feas = [1.0 if r <= .985 else 0.0 for r in minreach]
er = [min(feas[max(0, i - 2):i + 3]) for i in range(NF)]; LW = [sum(er[max(0, i - 3):i + 4]) / len(er[max(0, i - 3):i + 4]) for i in range(NF)]
LW = [x * x * (3 - 2 * x) for x in LW]
for i in range(NF):
    if er[i] == 0: costs[i, :] = 0
dk = np.abs(np.arange(K)[:, None] - np.arange(K)[None, :]); dk = np.minimum(dk, K - dk) * (360 / K)
TR = (dk / 12.0) ** 2                                                   # 프레임 사이 12도 = 비용 1
acc = costs[0].copy(); back = np.zeros((NF, K), dtype=int)
for i in range(1, NF):
    tot = acc[:, None] + TR; back[i] = tot.argmin(axis=0); acc = tot.min(axis=0) + costs[i]
path = [int(acc.argmin())]
for i in range(NF - 1, 0, -1): path.append(int(back[i][path[-1]]))
path.reverse()
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']; e1, e2 = E1[i]; a = 2 * math.pi * path[i] / K
    caps = torso_caps(P); hint = MET[i].pop('l_fk_elbow').normalized() + (P.Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35; sock = b['Gh'] - d * gaps[i]
    cst, r = left_attempt(P, sock, d, e1 * math.cos(a) + e2 * math.sin(a), caps, hint)
    Lfk = {n_: P.L[n_].copy() for n_ in ('LeftArm', 'LeftForeArm', 'LeftHand')}; w = LW[i]
    P.set_world('LeftArm', r['qu']); P.set_world('LeftForeArm', r['qf'] @ axis_rot(r['fa'], math.degrees(r['tw']) * C.TWIST_SHARE)); P.set_world('LeftHand', r['qh'])
    if w < 1:
        for n_ in Lfk: P.L[n_] = Lfk[n_].to_quaternion().slerp(P.L[n_].to_quaternion(), w).to_matrix()
    for n_, l in GL.L.items(): P.L[n_] = Quaternion().slerp(l.to_quaternion(), .35 + .65 * w).to_matrix()
    P.fk(); m = MET[i]
    m.update(l_on=w, gap=gaps[i], l_cost=cst, l_reach=r['reach'], l_elbow_deg=r['elbow'], l_wrist_swing_deg=math.degrees(r['swing']), l_twist_deg=math.degrees(r['tw']), l_phi=path[i] * 360 / K,
             hand_dist=(P.H['LeftHand'] - P.H['RightHand']).length, r_elbow_deg=math.degrees((P.H['RightArm'] - P.H['RightForeArm']).angle(P.H['RightHand'] - P.H['RightForeArm'])))

# ---------------------------------------------------------------- 3) 붓털 스프링(붓 로컬, 붓끝 속도가 끈다)
DT = 1 / 30; tips = [b['G'] + b['d'] * C.TIPL for b in BR]
vel = [(tips[min(i + 1, NF - 1)] - tips[max(i - 1, 0)]) / (DT * (min(i + 1, NF - 1) - max(i - 1, 0)) or 1) for i in range(NF)] if NF > 1 else [Vector()]
RM = [Matrix((b['d'].cross(b['n'] - b['d'] * b['n'].dot(b['d'])).normalized() * -1, (b['n'] - b['d'] * b['n'].dot(b['d'])).normalized(), b['d'])).transposed() for b in BR]   # 열 = 로컬 X · Y(손바닥 법선) · Z(붓끝)
for i in range(NF):
    if RM[i].determinant() < 0: RM[i] = Matrix((-RM[i].col[0], RM[i].col[1], RM[i].col[2])).transposed()
vl = [(RM[i].transposed() @ vel[i]) for i in range(NF)]
CH = [dict(name='C', w=30, z=.35, kv=.11, mx=math.radians(45))] + [dict(name=r, w=w, z=z, kv=.11 * k, mx=math.radians(55)) for r, w, z, k in
      zip('ABCDEF', (24, 34, 28, 40, 22, 36), (.30, .40, .28, .45, .30, .38), (1.25, .85, 1.10, .70, 1.30, .90))]
state = {c['name']: [Vector((0, 0)), Vector((0, 0))] for c in CH}; BEND = []; SPLAY = []; sp = 0; SUB = 4
for i in range(NF):
    for k in range(SUB):
        t = (k + 1) / SUB; v = vl[max(i - 1, 0)].lerp(vl[i], t); vp = Vector((v.x, v.y)); sp_ = vp.length
        for c in CH:
            tgt = -vp.normalized() * c['mx'] * math.tanh(c['kv'] * sp_ / c['mx']) if sp_ > 1e-6 else Vector((0, 0))
            bnd, bv = state[c['name']]; h = DT / SUB
            bv = bv + (c['w'] ** 2 * (tgt - bnd) - 2 * c['z'] * c['w'] * bv) * h; bnd = bnd + bv * h
            if bnd.length > c['mx'] * 1.25: bnd = bnd.normalized() * c['mx'] * 1.25
            state[c['name']] = [bnd, bv]
    BEND.append({n: s[0].copy() for n, s in state.items()})
    sp += (min(.4, .035 * vel[i].length) - sp) * .5; SPLAY.append(sp)

# ---------------------------------------------------------------- 4) 굽기 + 재기
brush_rig = g['brush_rig']; brush = g['brush']; keys = brush.data.shape_keys
for idb, suf in ((arm, 'Body'), (brush_rig, 'Brush'), (keys, 'Keys')):
    nm = 'M1_%s_%s' % (CLIP, suf); old = bpy.data.actions.get(nm)
    if old: bpy.data.actions.remove(old)
    a = bpy.data.actions.new(nm); a.use_fake_user = True
    if idb.animation_data is None: idb.animation_data_create()
    idb.animation_data.action = a
is_hand = g['is_hand']; dom = g['dom']; prevq = None; prevb = None; prevL = None; flips = {}
for i in range(NF):
    f = f0 + i; P = POSES[i]; b = BR[i]; m = MET[i]
    prevq = P.apply(prevq)
    q = RM[i].to_quaternion()
    if prevb is not None and q.dot(prevb) < 0: q.negate()
    prevb = q; brush_rig.location = b['G']; brush_rig.rotation_quaternion = q
    bd = BEND[i]
    def bq(pb, vec, w):
        ang = vec.length * w
        if ang < 1e-7: return Quaternion()
        ax = Vector((-vec.y, vec.x, 0)).normalized(); return Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ ax, ang)
    for j in range(8):
        pb = brush_rig.pose.bones['Bristle_%02d' % (j + 1)]; pb.rotation_quaternion = bq(pb, bd['C'], ((j + 1) / 8) ** 1.5 - (j / 8) ** 1.5)
    for r in 'ABCDEF':
        for j in range(4):
            pb = brush_rig.pose.bones['BristleRing_%s_%02d' % (r, j + 1)]; pb.rotation_quaternion = bq(pb, bd[r], (.1, .25, .35, .30)[j])
    keys.key_blocks['BristleSplay'].value = SPLAY[i]; keys.key_blocks['BristlePress'].value = 0
    bpy.context.view_layer.update()
    for n in ORDER:
        pb = arm.pose.bones[n]; pb.keyframe_insert('rotation_quaternion', frame=f)
        if PARENT[n] is None: pb.keyframe_insert('location', frame=f)
    brush_rig.keyframe_insert('location', frame=f); brush_rig.keyframe_insert('rotation_quaternion', frame=f)
    for pb in brush_rig.pose.bones:
        if pb.name.startswith('Bristle'): pb.keyframe_insert('rotation_quaternion', frame=f)
    keys.key_blocks['BristleSplay'].keyframe_insert('value', frame=f)
    # ---- 재기(평가된 메시 · 뼈)
    AW = g['AW']
    for s, sock, gr in (('Right', 'GripSocket', GR), ('Left', 'GripSocketL', GL)):
        hb = arm.pose.bones[s + 'Hand']; M = AW @ hb.matrix; c_world = M @ (AW @ hb.bone.matrix_local).inverted() @ gr.C
        sw_ = bpy.data.objects[sock].matrix_world.translation
        sw_ = b['Gh'] - b['d'] * (m['gap'] if s == 'Left' else 0)
        if s == 'Left' and m['l_on'] < 1: m['grip_err_cm_Left'] = 0.0; continue
        m['grip_err_cm_' + s] = (c_world - sw_).length * 100
    Pw = C.body_points(); tail = b['G'] - b['d'] * C.TAIL; neck = b['G'] + b['d'] * (C.TIPL - C.HEADL); tip = b['G'] + b['d'] * C.TIPL
    ds, _ = C.dist_to_segment(Pw, tail, neck); pen = (ds < C.R_SHAFT - .004) & ~is_hand
    dh, th = C.dist_to_segment(Pw, neck, tip); penh = (dh < C.HEADR * (1 - th * .8) - .004) & ~is_hand
    bn, cnt = np.unique(dom[pen | penh], return_counts=True)
    m['shaft_in'] = int(pen.sum()); m['head_in'] = int(penh.sum()); m['in_by_bone'] = dict(zip(bn.tolist(), cnt.tolist())); m['min_body_z'] = float(Pw[:, 2].min())
    tipw = bpy.data.objects['TipSocket'].matrix_world.translation
    m['tip'] = list(tipw); m['tip_rigid'] = list(tip); m['tip_speed'] = vel[i].length; m['frame'] = f; m['t'] = i / 30; m['hips'] = list(P.H['Hips']); m['d'] = list(b['d']); m['G'] = list(b['G'])
    m['bend_deg'] = math.degrees(bd['C'].length); m['splay'] = SPLAY[i]; m['head'] = list(P.H['Head'])
    if prevL is not None:
        for n in ORDER:
            if 'Hand' in n and not n.endswith('Hand'): continue
            a = math.degrees((prevL[n].inverted() @ P.L[n]).to_quaternion().angle)
            a = min(a, 360 - a)
            if a > flips.get(n, (0, 0))[0]: flips[n] = (a, f)
    prevL = {n: P.L[n].copy() for n in ORDER}
top = sorted(flips.items(), key=lambda kv: -kv[1][0])[:6]


def rng(k): v = [m[k] for m in MET]; return [round(min(v), 3), round(max(v), 3)]


# 발이 붙어 있는 구간(원본 발목이 바닥에서 2 cm 안)에서 리타깃된 발목의 높이 · 수평 미끄러짐
slide = {}
for s in ('Left', 'Right'):
    zz = []; sl = 0
    for i in range(NF):
        if MET[i]['src_ankle_z_' + s] < .02:
            zz.append(MET[i]['ankle_z_' + s])
            if i and MET[i - 1]['src_ankle_z_' + s] < .02:
                dt_ = (POSES[i].H[s + 'Foot'] - POSES[i - 1].H[s + 'Foot']); ds_ = (S_pos[i][s + 'Foot'] - S_pos[i - 1][s + 'Foot']) * S
                sl = max(sl, abs(Vector((dt_.x, dt_.y)).length - Vector((ds_.x, ds_.y)).length))
    slide[s] = dict(planted_frames=len(zz), ankle_z_cm=[round(min(zz) * 100, 2), round(max(zz) * 100, 2)] if zz else None, extra_slide_cm_per_frame=round(sl * 100, 3))
summary = dict(clip=CLIP, tag=TAG, frames=[f0, f1], seconds=round((NF - 1) / 30, 3), gap=GAP, gapmin=GAPMIN, gripshift=SHIFT, wristfix=WFIX, leg_ratio=round(S, 4),
               ankle_err_cm=round(max(max(m['ankle_err_Left'], m['ankle_err_Right']) for m in MET) * 100, 2), leg_reach=[rng('leg_reach_Left'), rng('leg_reach_Right')], feet=slide,
               hips_z=rng('hips') if False else [round(min(m['hips'][2] for m in MET), 3), round(max(m['hips'][2] for m in MET), 3)],
               planar_travel_m=[round(MET[-1]['hips'][0] - MET[0]['hips'][0], 3), round(MET[-1]['hips'][1] - MET[0]['hips'][1], 3)],
               min_body_z=round(min(m['min_body_z'] for m in MET), 3), max_rot_step_deg=[(n, round(a, 1), f) for n, (a, f) in top],
               l_on_share=round(sum(1 for m in MET if m['l_on'] >= 1) / NF, 3), l_off_frames=[m['frame'] for m in MET if m['l_on'] < 1][:1] + [m['frame'] for m in MET if m['l_on'] < 1][-1:], r_pull_cm=rng('r_pull_cm'), src_hand_dist=rng('src_hand_dist'), src_hand_dist_scaled=[round(x * tgt_arm / src_arm, 3) for x in rng('src_hand_dist')], hand_dist=rng('hand_dist'), gap_range=rng('gap'),
               l_reach=rng('l_reach'), l_elbow_deg=rng('l_elbow_deg'), l_wrist_swing_deg=rng('l_wrist_swing_deg'), l_twist_deg=rng('l_twist_deg'), l_cost=rng('l_cost'),
               grip_err_cm_L=rng('grip_err_cm_Left'), grip_err_cm_R=rng('grip_err_cm_Right'), r_elbow_deg=rng('r_elbow_deg'), axis_vs_src_deg=rng('axis_vs_src_deg'),
               shaft_in_frames=sum(1 for m in MET if m['shaft_in'] > 0), head_in_frames=sum(1 for m in MET if m['head_in'] > 0), shaft_in_max=max(m['shaft_in'] for m in MET), head_in_max=max(m['head_in'] for m in MET),
               tip_speed_max=round(max(m['tip_speed'] for m in MET), 2), tip_speed_max_frame=f0 + max(range(NF), key=lambda i: MET[i]['tip_speed']), tip_z=[round(min(m['tip'][2] for m in MET), 3), round(max(m['tip'][2] for m in MET), 3)],
               bend_deg_max=round(max(m['bend_deg'] for m in MET), 1))
print('M1 summary', json.dumps(summary))
json.dump(dict(summary=summary, frames=MET), open('%s/metrics/%s%s.json' % (C.OUT, CLIP, ('_' + TAG) if TAG else ''), 'w', encoding='utf-8'), indent=0, default=lambda o: float(o))
if '--nosave' not in ARGS:
    sc.frame_start = f0; sc.frame_end = f1
    try: bpy.ops.file.pack_all()
    except Exception as e: print('M1 pack', e)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print('M1 saved', BLEND, [a.name for a in bpy.data.actions])
print('M1 DONE', CLIP)
