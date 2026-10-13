"""#308 GreatBrush motion2: 게임용 클립 하나를 만든다(시간표 = clips_m2.py). v5 붓, 전투 태세에서 시작 · 끝, 정점 시각 = Spec, 제자리.
    blender -b --factory-startup -P solve_m2.py -- <이름> [--twist 도] [--nosave]
순서: 원본 표본(소수 프레임) → 리타깃(엉덩이 수평 이동 빼기 · 발목 IK) → 전투 태세와 앞뒤 섞기 → (내리찍기) 윗몸 요 → 오른손 당김 → 붓 = 오른손 고정 쥐기
      → 왼손 IK(닿을 때만) → 붓털 스프링 → 액션 A_Brush308_<이름>[__Brush · __Keys] + metrics/<이름>.json"""
import bpy, json, math, os, sys, time
import numpy as np
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2, clips_m2
from common_m1 import X, Y, Z, frame, axis_rot, two_bone, seg_pt
ARGS = sys.argv[sys.argv.index('--') + 1:]; NAME = ARGS[0]; SPEC = clips_m2.CLIPS[NAME]
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
GAP = C2.GAP; SHIFT = C2.SHIFT; FPS = 30
g = C.init(C.BLEND); arm = g['arm']; RH, RT, ORDER, PARENT = g['REST_HEAD'], g['REST_TAIL'], g['ORDER'], g['PARENT']; sc = bpy.context.scene
GR = C.Grip('Right'); GL = C.Grip('Left'); ARM_BEND = {'Right': Z, 'Left': -Z}
LEGS = ('LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase')
LIMB = ['LeftUpLeg', 'LeftLeg', 'RightUpLeg', 'RightLeg', 'LeftArm', 'LeftForeArm', 'RightArm', 'RightForeArm']
CHILD = {'LeftUpLeg': 'LeftLeg', 'LeftLeg': 'LeftFoot', 'RightUpLeg': 'RightLeg', 'RightLeg': 'RightFoot', 'LeftArm': 'LeftForeArm', 'LeftForeArm': 'LeftHand', 'RightArm': 'RightForeArm', 'RightForeArm': 'RightHand'}
tgt_ankle = {s: RH[s + 'Foot'].z for s in ('Left', 'Right')}
tgt_leg = (RH['LeftLeg'] - RH['LeftUpLeg']).length + (RH['LeftFoot'] - RH['LeftLeg']).length
tgt_h2u = RH['Hips'].z - (RH['LeftUpLeg'].z + RH['RightUpLeg'].z) / 2
tgt_arm = (RH['LeftForeArm'] - RH['LeftArm']).length + (RH['LeftHand'] - RH['LeftForeArm']).length


class Frame:
    """몸 한 프레임: 자세 + 발목 목표 · 발 회전 · 무릎 방향(섞을 때 다리를 다시 푼다)."""
    def __init__(self, P, ank, fq, knee): self.P, self.ank, self.fq, self.knee = P, ank, fq, knee


def legs(P, ank, fq, knee):
    for s in ('Left', 'Right'):
        up, lo, ft = s + 'UpLeg', s + 'Leg', s + 'Foot'; l1 = (RH[lo] - RH[up]).length; l2 = (RH[ft] - RH[lo]).length
        toe = ank[s] + fq[s] @ (RH[s + 'ToeBase'] - RH[ft]); dip = RH[s + 'ToeBase'].z - .004 - toe.z      # 섞인 발이 앞꿈치부터 땅에 묻히지 않게 발목을 그만큼 올린다
        if dip > 0: ank[s] = ank[s] + Vector((0, 0, dip))
        E, W2, reach = two_bone(P.H[up], ank[s], l1, l2, knee[s]); b = (E - P.H[up]).cross(W2 - E)
        if b.length < 1e-6: b = P.Q[up] @ X
        P.set_world(up, frame(E - P.H[up], b) @ frame(RH[lo] - RH[up], X).transposed()); P.set_world(lo, frame(W2 - E, b) @ frame(RH[ft] - RH[lo], X).transposed()); P.set_world(ft, fq[s])


class Source:
    def __init__(self, clip):
        before = set(bpy.data.objects); acts0 = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath='%s/%s.fbx' % (C.MOCAP, clip), automatic_bone_orientation=False)
        self.new = [o for o in bpy.data.objects if o not in before]; self.src = next(o for o in self.new if o.type == 'ARMATURE'); self.acts = set(bpy.data.actions) - acts0
        self.SW = self.src.matrix_world.copy(); self.by = {b.name.split(':')[-1]: b for b in self.src.pose.bones if not ('Hand' in b.name and not b.name.endswith('Hand'))}
        sr = {n: (self.SW @ b.bone.matrix_local) for n, b in self.by.items()}
        self.RH = {n: m.translation.copy() for n, m in sr.items()}; self.RQ = {n: m.to_quaternion().to_matrix() for n, m in sr.items()}
        self.f0, self.f1 = (int(round(x)) for x in self.src.animation_data.action.frame_range)
        floor = self.RH['LeftToeBase'].z; self.ankle = {s: self.RH[s + 'Foot'].z - floor for s in ('Left', 'Right')}
        self.S = tgt_leg / ((self.RH['LeftLeg'] - self.RH['LeftUpLeg']).length + (self.RH['LeftFoot'] - self.RH['LeftLeg']).length)
        self.h2u = self.RH['Hips'].z - self.RH['LeftUpLeg'].z
        self.arm = (self.RH['LeftForeArm'] - self.RH['LeftArm']).length + (self.RH['LeftHand'] - self.RH['LeftForeArm']).length
        self.CORR = {n: (RH[CHILD[n]] - RH[n]).normalized().rotation_difference((self.RH[CHILD[n]] - self.RH[n]).normalized()).to_matrix() for n in LIMB}
        self.hips = {}
        for f in range(self.f0, self.f1 + 1): sc.frame_set(f); h = self.SW @ self.by['Hips'].head; self.hips[f] = Vector((h.x, h.y))

    def lowpass(self, f, r=10):
        fs = [min(max(k, self.f0), self.f1) for k in range(int(math.floor(f)) - r, int(math.floor(f)) + r + 1)]; a = sum((self.hips[k] for k in fs), Vector((0, 0))) / len(fs)
        fs2 = [min(k + 1, self.f1) for k in fs]; b = sum((self.hips[k] for k in fs2), Vector((0, 0))) / len(fs2); return a.lerp(b, f - math.floor(f))

    def sample(self, f, off=Vector((0, 0)), twist=0.0):
        """원본 프레임 f(소수) → Frame. off = 뺄 수평 이동(원본 m), twist = 윗몸에 더할 요(도, +면 오른쪽)."""
        f = min(max(f, self.f0), self.f1); sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f)); pos = {}; dq = {}
        for n, b in self.by.items():
            m = self.SW @ b.matrix; pos[n] = m.translation.copy(); dq[n] = m.to_quaternion().to_matrix() @ self.RQ[n].inverted()
        S = self.S; P = C.Pose(); Q = {}
        hz = tgt_ankle['Left'] + (pos['Hips'].z - self.ankle['Left'] - self.h2u) * S + tgt_h2u
        P.hips_t = Vector(((pos['Hips'].x - off.x) * S, (pos['Hips'].y - off.y) * S, hz)) - RH['Hips']
        for n in ORDER:
            sn = C.ALIAS.get(n, n); p = PARENT[n]
            if sn in dq:
                q = dq[sn] @ self.CORR[n] if n in self.CORR else dq[sn].copy(); P.L[n] = (Q[p].inverted() @ q) if p else q; Q[n] = q
            else: Q[n] = (Q[p] @ P.L[n]) if p else P.L[n].copy()
        P.fk(); ank = {}; fq = {}; knee = {}
        for s in ('Left', 'Right'):
            sp = pos[s + 'Foot']; ank[s] = Vector(((sp.x - off.x) * S, (sp.y - off.y) * S, tgt_ankle[s] + (sp.z - self.ankle[s]) * S)); fq[s] = P.Q[s + 'Foot'].copy(); knee[s] = P.H[s + 'Leg'] - P.H[s + 'UpLeg']
        F = Frame(P, ank, fq, knee)
        if abs(twist) > 1e-3: yaw(F, twist)
        legs(P, ank, fq, knee); F.src_hand = (pos['RightHand'] - pos['LeftHand']).length; return F

    def close(self):
        for o in self.new: bpy.data.objects.remove(o, do_unlink=True)
        for a in self.acts: bpy.data.actions.remove(a)


def yaw(F, deg):
    """윗몸을 세로축으로 돌린다(+ = 캐릭터 오른쪽). 엉덩이 20 % → 척추 셋에 나눠 100 %."""
    P = F.P; old = {n: P.Q[n].copy() for n in ('Hips', 'Spine02', 'Spine01', 'Spine')}
    for n, k in (('Hips', .2), ('Spine02', .47), ('Spine01', .73), ('Spine', 1.0)): P.set_world(n, C.rot('Z', -deg * k) @ old[n])


def slerp(a, b, w): return a.to_quaternion().slerp(b.to_quaternion(), w).to_matrix()


def blend(Fa, Fb, w):
    """Fa(1-w) + Fb(w). 뼈 국소 회전 slerp, 엉덩이 · 발목 목표는 선형, 다리는 다시 푼다(발이 땅에서 뜨거나 묻히지 않게)."""
    if w <= 0 or w >= 1:
        Fs = Fa if w <= 0 else Fb; P = C.Pose(); P.hips_t = Fs.P.hips_t.copy(); P.L = {n: m.copy() for n, m in Fs.P.L.items()}; P.fk()
        F = Frame(P, dict(Fs.ank), dict(Fs.fq), dict(Fs.knee)); F.src_hand = getattr(Fs, 'src_hand', 0); return F
    P = C.Pose(); P.hips_t = Fa.P.hips_t.lerp(Fb.P.hips_t, w)
    for n in ORDER: P.L[n] = slerp(Fa.P.L[n], Fb.P.L[n], w)
    P.fk(); ank = {s: Fa.ank[s].lerp(Fb.ank[s], w) for s in Fa.ank}; fq = {s: slerp(Fa.fq[s], Fb.fq[s], w) for s in Fa.fq}; knee = {s: Fa.knee[s].lerp(Fb.knee[s], w) for s in Fa.knee}
    legs(P, ank, fq, knee); F = Frame(P, ank, fq, knee); F.src_hand = getattr(Fb, 'src_hand', 0); return F


def ease(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


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


def best_n(P, s, sock, d, caps, hint, step=3):
    e1 = Z - d * Z.dot(d); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized(); e2 = d.cross(e1); best = None
    for k in range(0, 360, step):
        a = math.radians(k); c, r = attempt(P, s, sock, d, e1 * math.cos(a) + e2 * math.sin(a), caps, hint)
        if best is None or c < best[0]: best = (c, r)
    return best


def hand_state(P):
    Qh = P.Q['RightHand']; return (Qh @ GR.A).normalized(), (Qh @ GR.N).normalized(), P.H['RightHand'] + Qh @ (GR.C - RH['RightHand'])


# ================================================================ 1) 몸 프레임 목록
src = Source('Great_Sword_Idle'); STANCE = src.sample(1); src.close(); ref = Vector((.009, .037))     # 전투 태세의 원본 엉덩이 XY
FR = []; LW_FORCE = None; info = dict(name=NAME, use=SPEC['use'])
if 'special' not in SPEC:
    kn = SPEC['knots']; T_end = kn[-1][0] + SPEC['blend_out']; NF = int(round(T_end * FPS)) + 1; src = Source(SPEC['src'])
    def srcf(t):
        if t >= kn[-1][0]: return kn[-1][1] + (t - kn[-1][0]) * FPS * (.5 if SPEC['blend_out'] else 0)
        for (t0, a), (t1, b) in zip(kn, kn[1:]):
            if t <= t1: return a + (b - a) * (t - t0) / max(t1 - t0, 1e-9)
    def offset(f):
        r = SPEC['root']
        if r == 'none': return Vector((0, 0))
        if r == 'lowpass': return src.lowpass(f) - ref
        return Vector(r) * ((f - src.f0) / max(1, src.f1 - src.f0))
    def env(t): return ease((t - .12) / .30)                     # 윗몸 요가 들어오는 창(내리찍기)
    def build(twist):
        out = []
        for i in range(NF):
            t = i / FPS; f = srcf(t); F = src.sample(f, offset(f), twist * env(t))
            w_in = ease(t / SPEC['blend_in']) if SPEC['blend_in'] > 0 else 1; w_out = ease((t - kn[-1][0]) / SPEC['blend_out']) if SPEC['blend_out'] > 0 else 0
            F = blend(STANCE, F, w_in); F = blend(F, STANCE, w_out); F.t = t; F.srcf = f; out.append(F)
        return out
    tw = 0.0
    if SPEC.get('twist') == 'auto' and not opt('--twist'):
        # 뒤 어깨 카메라에서 붓머리가 몸 오른쪽 밖으로 나오는 가장 작은 각: 정점 프레임에서 붓머리 가운데를 몸 깊이로 투영한 가로 자리 ≤ -0.42 m
        ia = int(round(SPEC['apex'] * FPS))
        for cand in range(0, 95, 5):
            F = src.sample(srcf(ia / FPS), offset(srcf(ia / FPS)), cand); d, n, Gh = hand_state(F.P); hc = Gh + d * (SHIFT + C.TIPL - C.HEADL / 2); hp = F.P.H['Hips']
            cam_y = hp.y + 2.8; px = (hc.x - hp.x) * (cam_y - hp.y) / (cam_y - hc.y)
            print('M1 twist', cand, 'head', [round(x, 2) for x in hc], 'proj_x', round(px, 2))
            tw = cand
            if px <= -.42: break
    elif opt('--twist'): tw = float(opt('--twist'))
    elif isinstance(SPEC.get('twist'), (int, float)): tw = float(SPEC['twist'])
    info['twist_deg'] = tw; FR = build(tw)
    speeds = [round((b - a) / ((t1 - t0) * FPS), 2) for (t0, a), (t1, b) in zip(kn, kn[1:]) if t1 > t0]
    info.update(src=SPEC['src'], knots=kn, speed_factors=speeds, src_window=[kn[0][1], kn[-1][1]], blend_in=SPEC['blend_in'], blend_out=SPEC['blend_out'], root=SPEC['root'], src_scale=round(src.S, 4), arm_ratio=round(tgt_arm / src.arm, 3))
    src.close()
else:
    _cg, _cd = clips_m2.CARRY_G, clips_m2.CARRY_D
    if opt('--carry'):      # 시험용: --carry sx,sy,sz,pitch,yaw,L
        _v = [float(x) for x in opt('--carry').split(',')]; _cg, _cd = clips_m2.carry_gd(_v[:3], _v[3], _v[4], _v[5])
    BUMP = Vector([float(x) for x in opt('--bump').split(',')]) if opt('--bump') else Vector(clips_m2.CARRY_BUMP)
    CG = Vector(_cg); CD = Vector(_cd).normalized(); P0 = STANCE.P; d0, n0, G0 = hand_state(P0)
    def body(s, breath=0.0):
        P = C.Pose(); P.hips_t = P0.hips_t + Vector((0, 0, .015 * s))
        for n in ORDER:
            k = 1 - .4 * s if n in ('Spine02', 'Spine01', 'Spine', 'neck', 'Head') else 1
            P.L[n] = Quaternion().slerp(P0.L[n].to_quaternion(), k).to_matrix() if k < 1 else P0.L[n].copy()
            if n in ('Spine01', 'Spine') and breath: P.L[n] = C.rot('X', -breath) @ P.L[n]
        P.fk(); legs(P, STANCE.ank, STANCE.fq, STANCE.knee); return P
    def hang(P):
        Lh = {'LeftArm': C.rot('Y', 74), 'LeftForeArm': C.rot('Z', -16), 'LeftHand': Matrix.Identity(3)}
        P.L['LeftArm'] = P.Q[PARENT['LeftArm']].inverted() @ (P.Q['Spine'] @ Lh['LeftArm']); P.L['LeftForeArm'] = Lh['LeftForeArm']; P.L['LeftHand'] = Lh['LeftHand']; P.fk()
    P1 = body(1); hint1 = Vector((-.5, .2, -.85)); c1, r1 = best_n(P1, 'Right', CG, CD, torso_caps(P1), hint1); n1 = r1['n']; hint0 = P0.H['RightForeArm'] - P0.H['RightArm']
    info['carry'] = dict(G=list(CG), d=list(CD), r_reach=round(r1['reach'], 3), r_elbow=round(r1['elbow'], 1), r_wrist_swing=round(math.degrees(r1['swing']), 1))
    def pose_at(s, breath=0.0):
        P = body(ease(s), breath); u = ease((s - .12) / .88)
        d = Quaternion().slerp(d0.rotation_difference(CD), u) @ d0; d.normalize()
        G = G0.lerp(CG, u) + BUMP * math.sin(math.pi * u) + Vector((0, 0, .004 * breath))
        nn = Quaternion().slerp(n0.rotation_difference(n1), u) @ n0; nn = (nn - d * nn.dot(d)).normalized()
        if u > 0:           # u = 0 이면 오른팔은 모캡 그대로(전투 태세와 똑같이 시작 · 끝나게)
            c, r = attempt(P, 'Right', G, d, nn, torso_caps(P), hint0.normalized().lerp(hint1.normalized(), u)); set_arm(P, 'Right', r)
            if u < .25:
                for n_ in ('RightArm', 'RightForeArm', 'RightHand'): P.L[n_] = slerp(P0.L[n_], P.L[n_], u / .25)
        P.fk(); F = Frame(P, STANCE.ank, STANCE.fq, STANCE.knee); F.src_hand = 0; return F
    NF = int(round(SPEC['seconds'] * FPS)) + 1; LW_FORCE = []
    for i in range(NF):
        x = i / (NF - 1)
        if SPEC['special'] == 'carry_idle': s = 1.0; br = .6 * math.sin(2 * math.pi * x); lw = 0.0
        elif SPEC['special'] == 'to_carry': s = x; br = 0; lw = 1 - ease(s / .30)
        else: s = 1 - ease(x) ** .8; br = 0; lw = 1 - ease(s / .30)
        F = pose_at(s, br); F.t = i / FPS; F.srcf = 0; F.hang = lw < 1; FR.append(F); LW_FORCE.append(lw)
    info.update(src='손으로 만듦(전투 태세 ↔ 어깨 걸치기 열쇠 자세 보간)', speed_factors=[], root='none')
NF = len(FR); POSES = [F.P for F in FR]; MET = [dict(frame=i, t=FR[i].t, srcf=FR[i].srcf, src_hand_dist=FR[i].src_hand) for i in range(NF)]
print('M1 built', NAME, NF, 'frames', round((NF - 1) / FPS, 3), 's', json.dumps({k: v for k, v in info.items() if k != 'knots'}, ensure_ascii=False, default=float))

# ================================================================ 2) 오른손 당김 + 붓
PULLMAX = .16; raw = []
for i in range(NF):
    P = POSES[i]; d, n, Gp = hand_state(P); v = P.H['LeftArm'] - (Gp - d * GAP); over = v.length - .03 - .93 * tgt_arm
    raw.append(v.normalized() * over * (1 if LW_FORCE is None else LW_FORCE[i]) if 0 < over <= PULLMAX else Vector())      # 손으로 만든 전환은 왼손을 쥔 만큼만 당긴다(시작 · 끝이 전투 태세와 같게)
PULL = [sum(raw[max(0, i - 2):i + 3], Vector()) / len(raw[max(0, i - 2):i + 3]) for i in range(NF)]
if SPEC.get('loop'): PULL = [sum(PULL, Vector()) / NF] * NF if max(p.length for p in PULL) > 0 else PULL       # 루프는 처음 · 끝이 같아야 한다
BR = []
for i in range(NF):
    P = POSES[i]; m = MET[i]; Qh = P.Q['RightHand'].copy(); dl = PULL[i]; m['r_pull_cm'] = dl.length * 100
    if dl.length > 1e-4:
        A = P.H['RightArm']; E0 = P.H['RightForeArm']; W0 = P.H['RightHand']; l1 = (E0 - A).length; l2 = (W0 - E0).length
        E, W2, reach = two_bone(A, W0 + dl, l1, l2, E0 - A); qu0 = P.Q['RightArm'].copy(); qf0 = P.Q['RightForeArm'].copy()
        P.set_world('RightArm', (E0 - A).rotation_difference(E - A).to_matrix() @ qu0); P.set_world('RightForeArm', (W0 - E0).rotation_difference(W2 - E).to_matrix() @ qf0); P.set_world('RightHand', Qh)
    m['l_fk_elbow'] = P.H['LeftForeArm'] - P.H['LeftArm']
    if LW_FORCE is not None and LW_FORCE[i] < 1: Lkeep = {n_: P.L[n_].copy() for n_ in ('LeftArm', 'LeftForeArm', 'LeftHand')}
    for n_, l in GR.L.items(): P.L[n_] = l
    P.fk(); d, n, Gp = hand_state(P); BR.append(dict(G=Gp + d * SHIFT, Gh=Gp, d=d, n=n))

# ================================================================ 3) 왼손
K = 72; costs = np.zeros((NF, K)); E1 = []; minreach = []; e1 = None
def lhint(i): return MET[i]['l_fk_elbow'].normalized() + (POSES[i].Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35
if LW_FORCE is not None:        # 손으로 만든 클립: 왼팔 늘어뜨린 자세를 먼저 적어 둔다(가중치 0 쪽)
    for i in range(NF):
        P = POSES[i]; Lh = {'LeftArm': C.rot('Y', 74), 'LeftForeArm': C.rot('Z', -16), 'LeftHand': Matrix.Identity(3)}
        P.L['LeftArm'] = P.Q[PARENT['LeftArm']].inverted() @ (P.Q['Spine'] @ Lh['LeftArm']); P.L['LeftForeArm'] = Lh['LeftForeArm']; P.L['LeftHand'] = Lh['LeftHand']; P.fk()
        MET[i]['l_fk_elbow'] = STANCE.P.H['LeftForeArm'] - STANCE.P.H['LeftArm']
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']
    if e1 is None:
        e1 = Z - d * Z.dot(d); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized()
    else: e1 = (e1 - d * e1.dot(d)).normalized()
    e2 = d.cross(e1); E1.append((e1.copy(), e2.copy())); caps = torso_caps(P); hint = lhint(i); sock = b['Gh'] - d * GAP; mr = 9
    for k in range(K):
        a = 2 * math.pi * k / K; costs[i, k], r_ = attempt(P, 'Left', sock, d, e1 * math.cos(a) + e2 * math.sin(a), caps, hint); mr = min(mr, r_['reach'])
    minreach.append(mr)
feas = [1.0 if r <= .985 else 0.0 for r in minreach]
er = [min(feas[max(0, i - 2):i + 3]) for i in range(NF)]; LW = [sum(er[max(0, i - 3):i + 4]) / len(er[max(0, i - 3):i + 4]) for i in range(NF)]; LW = [x * x * (3 - 2 * x) for x in LW]
if LW_FORCE is not None: LW = [min(a, b_) if f < 1 else a for a, b_, f in zip(LW_FORCE, LW, feas)]
else:
    i = 0
    while i < NF:           # 1 에 닿지 못하는 짧은 쥠은 버린다(잡다 마는 손)
        if LW[i] > 0:
            j = i
            while j < NF and LW[j] > 0: j += 1
            if max(LW[i:j]) < 1: LW[i:j] = [0.0] * (j - i)
            i = j
        else: i += 1
    if not SPEC.get('loop'):   # 전투 태세(왼손 쥠)에서 시작 · 끝나는 클립: 첫 · 끝 프레임은 쥔 채로, 2프레임에 걸쳐 놓고 · 잡는다
        if SPEC.get('blend_in', 0) > 0 or feas[0]: LW[0] = 1.0; LW[1] = max(LW[1], .5)
        if SPEC.get('blend_out', 0) > 0: LW[-1] = 1.0; LW[-2] = max(LW[-2], .6); LW[-3] = max(LW[-3], .2)
er = [1 if w > 0 else 0 for w in LW]
for i in range(NF):
    if er[i] == 0: costs[i, :] = 0
dk = np.abs(np.arange(K)[:, None] - np.arange(K)[None, :]); dk = np.minimum(dk, K - dk) * (360 / K); TR = (dk / 30.0) ** 2       # 속도 배율을 구운 클립은 프레임 사이 변화가 크다(12 → 30도)
acc = costs[0].copy(); back = np.zeros((NF, K), dtype=int)
for i in range(1, NF): tot = acc[:, None] + TR; back[i] = tot.argmin(axis=0); acc = tot.min(axis=0) + costs[i]
path = [int(acc.argmin())]
for i in range(NF - 1, 0, -1): path.append(int(back[i][path[-1]]))
path.reverse()
if SPEC.get('loop'): path = [max(set(path), key=path.count)] * NF
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']; e1, e2 = E1[i]; a = 2 * math.pi * path[i] / K; m = MET[i]; w = LW[i]
    cst, r = attempt(P, 'Left', b['Gh'] - d * GAP, d, e1 * math.cos(a) + e2 * math.sin(a), torso_caps(P), lhint(i)); m.pop('l_fk_elbow')
    Lfk = {n_: P.L[n_].copy() for n_ in ('LeftArm', 'LeftForeArm', 'LeftHand')}; set_arm(P, 'Left', r)
    if w < 1:
        for n_ in Lfk: P.L[n_] = slerp(Lfk[n_], P.L[n_], w)
    for n_, l in GL.L.items(): P.L[n_] = Quaternion().slerp(l.to_quaternion(), .35 + .65 * w).to_matrix()
    P.fk()
    m.update(l_on=w, l_cost=cst if w >= 1 else 0, l_reach=r['reach'], l_elbow_deg=r['elbow'], l_wrist_swing_deg=math.degrees(r['swing']), l_twist_deg=math.degrees(r['tw']),
             hand_dist=(P.H['LeftHand'] - P.H['RightHand']).length, r_elbow_deg=math.degrees((P.H['RightArm'] - P.H['RightForeArm']).angle(P.H['RightHand'] - P.H['RightForeArm'])))

# ================================================================ 4) 붓털 스프링
DT = 1 / FPS; tips = [b['G'] + b['d'] * C.TIPL for b in BR]
vel = [(tips[min(i + 1, NF - 1)] - tips[max(i - 1, 0)]) / (DT * max(1, min(i + 1, NF - 1) - max(i - 1, 0))) for i in range(NF)]
RM = []
for b in BR:
    dn = (b['n'] - b['d'] * b['n'].dot(b['d'])).normalized(); RM.append(Matrix((dn.cross(b['d']), dn, b['d'])).transposed())
vl = [(RM[i].transposed() @ vel[i]) for i in range(NF)]
CH = [dict(name='C', w=30, z=.35, kv=.11, mx=math.radians(45))] + [dict(name=r, w=w, z=z, kv=.11 * k, mx=math.radians(55)) for r, w, z, k in
      zip('ABCDEF', (24, 34, 28, 40, 22, 36), (.30, .40, .28, .45, .30, .38), (1.25, .85, 1.10, .70, 1.30, .90))]
state = {c['name']: [Vector((0, 0)), Vector((0, 0))] for c in CH}; BEND = []; SPLAY = []; sp = 0; SUB = 4
for rep in range(2 if SPEC.get('loop') else 1):       # 루프는 한 바퀴 돌려 정상 상태에서 다시 굽는다
    BEND = []; SPLAY = []
    for i in range(NF):
        for k in range(SUB):
            v = vl[max(i - 1, 0)].lerp(vl[i], (k + 1) / SUB); vp = Vector((v.x, v.y)); s_ = vp.length
            for c in CH:
                tgt = -vp.normalized() * c['mx'] * math.tanh(c['kv'] * s_ / c['mx']) if s_ > 1e-6 else Vector((0, 0)); bnd, bv = state[c['name']]; h = DT / SUB
                bv = bv + (c['w'] ** 2 * (tgt - bnd) - 2 * c['z'] * c['w'] * bv) * h; bnd = bnd + bv * h
                if bnd.length > c['mx'] * 1.25: bnd = bnd.normalized() * c['mx'] * 1.25
                state[c['name']] = [bnd, bv]
        BEND.append({n: s[0].copy() for n, s in state.items()}); sp += (min(.4, .035 * vel[i].length) - sp) * .5; SPLAY.append(sp)

# ================================================================ 5) 굽기 + 재기
brush_rig = g['brush_rig']; brush = g['brush']; keys = brush.data.shape_keys
for idb, suf in ((arm, ''), (brush_rig, '__Brush'), (keys, '__Keys')):
    nm = C2.act(NAME, suf); old = bpy.data.actions.get(nm)
    if old: bpy.data.actions.remove(old)
    a = bpy.data.actions.new(nm); a.use_fake_user = True
    if idb.animation_data is None: idb.animation_data_create()
    idb.animation_data.action = a
is_hand = g['is_hand']; dom = g['dom']; prevq = None; prevb = None; prevL = None; flips = {}; AW = g['AW']; REL = []
def bq(pb, vec, w):
    ang = vec.length * w
    if ang < 1e-7: return Quaternion()
    return Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ Vector((-vec.y, vec.x, 0)).normalized(), ang)
for i in range(NF):
    f = i; P = POSES[i]; b = BR[i]; m = MET[i]; prevq = P.apply(prevq); q = RM[i].to_quaternion()
    if prevb is not None and q.dot(prevb) < 0: q.negate()
    prevb = q; brush_rig.location = b['G']; brush_rig.rotation_quaternion = q; bd = BEND[i]
    for j in range(8): pb = brush_rig.pose.bones['Bristle_%02d' % (j + 1)]; pb.rotation_quaternion = bq(pb, bd['C'], ((j + 1) / 8) ** 1.5 - (j / 8) ** 1.5)
    for r in 'ABCDEF':
        for j in range(4): pb = brush_rig.pose.bones['BristleRing_%s_%02d' % (r, j + 1)]; pb.rotation_quaternion = bq(pb, bd[r], (.1, .25, .35, .30)[j])
    keys.key_blocks['BristleSplay'].value = SPLAY[i]; keys.key_blocks['BristlePress'].value = 0; bpy.context.view_layer.update()
    for n in ORDER:
        pb = arm.pose.bones[n]; pb.keyframe_insert('rotation_quaternion', frame=f)
        if PARENT[n] is None: pb.keyframe_insert('location', frame=f)
    brush_rig.keyframe_insert('location', frame=f); brush_rig.keyframe_insert('rotation_quaternion', frame=f)
    for pb in brush_rig.pose.bones:
        if pb.name.startswith('Bristle'): pb.keyframe_insert('rotation_quaternion', frame=f)
    keys.key_blocks['BristleSplay'].keyframe_insert('value', frame=f)
    # 재기(평가된 장면에서)
    hb = arm.pose.bones['RightHand']; MH = AW @ hb.matrix; MHn = Matrix.LocRotScale(MH.translation, MH.to_quaternion(), Vector((1, 1, 1))); REL.append(MHn.inverted() @ brush_rig.matrix_world)
    sockL = bpy.data.objects['GripSocketL'].matrix_world.translation; lb = arm.pose.bones['LeftHand']; ML = AW @ lb.matrix; cL = ML @ (AW @ lb.bone.matrix_local).inverted() @ GL.C
    m['l_socket_err_cm'] = (cL - sockL).length * 100 if m['l_on'] >= 1 else 0.0; m['l_hand_to_socketL_cm'] = (cL - sockL).length * 100
    for s in ('Left', 'Right'):
        az = (AW @ arm.pose.bones[s + 'Foot'].head).z - tgt_ankle[s]; m['ankle_z_' + s] = az; m['ankle_err_' + s] = ((AW @ arm.pose.bones[s + 'Foot'].head) - FR[i].ank[s]).length
    Pw = C.body_points(); tail = b['G'] - b['d'] * C.TAIL; neck = b['G'] + b['d'] * (C.TIPL - C.HEADL); tip = b['G'] + b['d'] * C.TIPL
    ds, _ = C.dist_to_segment(Pw, tail, neck); pen = (ds < C.R_SHAFT - .004) & ~is_hand; dh, th = C.dist_to_segment(Pw, neck, tip); penh = (dh < C.HEADR * (1 - th * .8) - .004) & ~is_hand
    bn, cnt = np.unique(dom[pen | penh], return_counts=True); hd = np.array(['ead' in x or x == 'neck' for x in dom])
    m.update(shaft_in=int(pen.sum()), head_in=int(penh.sum()), in_head_bone=int((pen & hd).sum() + (penh & hd).sum()), in_by_bone=dict(zip(bn.tolist(), cnt.tolist())), min_body_z=float(Pw[:, 2].min()),
             tip=list(bpy.data.objects['TipSocket'].matrix_world.translation), tip_speed=vel[i].length, hips=list(P.H['Hips']), head=list(P.H['Head']), d=list(b['d']), G=list(b['G']), bend_deg=math.degrees(bd['C'].length), splay=SPLAY[i],
             yaw_hips=math.degrees(math.atan2(-(P.Q['Hips'] @ Vector((0, -1, 0))).x, -(P.Q['Hips'] @ Vector((0, -1, 0))).y)))
    if prevL is not None:
        for n in ORDER:
            if 'Hand' in n and not n.endswith('Hand'): continue
            a = math.degrees((prevL[n].inverted() @ P.L[n]).to_quaternion().angle); a = min(a, 360 - a)
            if a > flips.get(n, (0, 0))[0]: flips[n] = (a, f)
    prevL = {n: P.L[n].copy() for n in ORDER}
relerr = max((R_.translation - REL[0].translation).length for R_ in REL), max(math.degrees(R_.to_quaternion().rotation_difference(REL[0].to_quaternion()).angle) for R_ in REL)
def rng(k, n=3): v = [m[k] for m in MET]; return [round(min(v), n), round(max(v), n)]
on = [m for m in MET if m['l_on'] >= 1]; iap = max(range(NF), key=lambda i: MET[i]['tip_speed']); izm = min(range(NF), key=lambda i: MET[i]['tip'][2])
# 미끄러짐: 발목이 바닥 높이(2 cm 안)에 있는 이웃 프레임 사이의 수평 이동
slide = {}
for s in ('Left', 'Right'):
    tot = 0; mx = 0
    for i in range(1, NF):
        if MET[i]['ankle_z_' + s] < .02 and MET[i - 1]['ankle_z_' + s] < .02: dxy = FR[i].ank[s] - FR[i - 1].ank[s]; dd = Vector((dxy.x, dxy.y)).length; tot += dd; mx = max(mx, dd)
    slide[s] = dict(total_cm=round(tot * 100, 1), max_cm_per_frame=round(mx * 100, 1))
loop_err = None
if SPEC.get('loop'):
    loop_err = round(max(math.degrees((POSES[0].L[n].inverted() @ POSES[-1].L[n]).to_quaternion().angle) for n in ORDER), 2)
st_err = [round(max(min(a_, 360 - a_) for a_ in (math.degrees((STANCE.P.L[n].inverted() @ POSES[k].L[n]).to_quaternion().angle) for n in ORDER if not ('Hand' in n or 'Arm' in n or 'Shoulder' in n))), 1) for k in (0, -1)]
summary = dict(info, frames=NF, seconds=round((NF - 1) / FPS, 3), peak_tip_speed=round(MET[iap]['tip_speed'], 1), peak_tip_speed_t=round(iap / FPS, 3), tip_z_min=round(MET[izm]['tip'][2], 3), tip_z_min_t=round(izm / FPS, 3),
               start_vs_stance_deg=st_err[0], end_vs_stance_deg=st_err[1], hips_xy_start_end=[[round(x, 3) for x in MET[0]['hips'][:2]], [round(x, 3) for x in MET[-1]['hips'][:2]]], hips_xy_range=[rng('hips')] if False else [[round(min(m['hips'][k] for m in MET), 3), round(max(m['hips'][k] for m in MET), 3)] for k in (0, 1)],
               yaw_start_end=[round(MET[0]['yaw_hips']), round(MET[-1]['yaw_hips'])], ankle_z_cm=[round(min(min(m['ankle_z_Left'], m['ankle_z_Right']) for m in MET) * 100, 2), round(max(max(m['ankle_z_Left'], m['ankle_z_Right']) for m in MET) * 100, 1)],
               ankle_ik_err_cm=round(max(max(m['ankle_err_Left'], m['ankle_err_Right']) for m in MET) * 100, 2), min_body_z=round(min(m['min_body_z'] for m in MET), 3), foot_slide=slide, max_rot_step_deg=[(n, round(a, 1), f) for n, (a, f) in sorted(flips.items(), key=lambda kv: -kv[1][0])[:4]],
               src_hand_dist=rng('src_hand_dist'), hand_dist=rng('hand_dist'), l_on_share=round(len(on) / NF, 3), l_on_curve=[round(m['l_on'], 2) for m in MET], l_socket_err_cm_max=round(max([m['l_socket_err_cm'] for m in on] or [0]), 2),
               r_pull_cm=rng('r_pull_cm', 1), l_reach=[round(min([m['l_reach'] for m in on] or [0]), 3), round(max([m['l_reach'] for m in on] or [0]), 3)], l_elbow_deg=[round(min([m['l_elbow_deg'] for m in on] or [0])), round(max([m['l_elbow_deg'] for m in on] or [0]))],
               l_wrist_swing_deg_max=round(max([m['l_wrist_swing_deg'] for m in on] or [0])), shaft_in_frames=sum(1 for m in MET if m['shaft_in'] > 0), shaft_in_max=max(m['shaft_in'] for m in MET), bristle_in_frames=sum(1 for m in MET if m['head_in'] > 0),
               head_hit_frames=sum(1 for m in MET if m['in_head_bone'] > 0), in_by_bone=sorted({k for m in MET for k in m['in_by_bone']}), bend_deg_max=round(max(m['bend_deg'] for m in MET), 1), loop_err_deg=loop_err,
               grip_rel_drift=[round(relerr[0] * 1000, 3), round(relerr[1], 3)], grip_rel_matrix=[[round(x, 6) for x in row] for row in REL[0]])
print('M1 summary', json.dumps(summary, ensure_ascii=False, default=float))
json.dump(dict(summary=summary, frames=MET), open('%s/metrics/%s.json' % (C.OUT, NAME), 'w', encoding='utf-8'), ensure_ascii=False, default=float)
if '--nosave' not in ARGS:
    sc.frame_start = 0; sc.frame_end = NF - 1
    try: bpy.ops.file.pack_all()
    except Exception as e: print('M1 pack', e)
    bpy.ops.wm.save_as_mainfile(filepath=C.BLEND)
    open(C.OUT + '/PROGRESS.md', 'a', encoding='utf-8').write('- %s solve %s: %d 프레임(%.2f s), 왼손 쥠 %.0f %%, 자루 관통 %d 프레임 → .blend 저장\n' % (time.strftime('%H:%M'), NAME, NF, (NF - 1) / FPS, 100 * len(on) / NF, summary['shaft_in_frames']))
print('M1 DONE', NAME)
