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
if opt('--set'):       # swing3 시험용: --set "knots=[[0,1],[.4,20]];blend_in=.1" (clips_m2 값을 이번 실행에서만 덮어쓴다)
    SPEC = dict(SPEC); SPEC.update({kv.split('=', 1)[0].strip(): eval(kv.split('=', 1)[1]) for kv in opt('--set').split(';') if '=' in kv})
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


def wv(th, k=1, ph=0.0):
    """루프용 물결: th = 0 과 2π 에서 정확히 0(첫 프레임 자세를 건드리지 않는다). ph 로 몸과 붓의 때를 어긋나게 한다."""
    return math.sin(k * th - ph) + math.sin(ph)


def life(P, x, pr):
    """swing3: 대기 자세에 얹는 '살아 있음'(더하기만 한다, x = 0..1 루프 위치). 숨(가슴 · 어깨) · 무게 옮김(엉덩이 · 척추) · 머리 · 오른팔(붓 흔들림).
    다리는 부르는 쪽에서 legs() 로 다시 푼다. 게임에서는 윗몸 마스크로 재생되므로 움직임 대부분을 엉덩이 위에 둔다."""
    th = 2 * math.pi * x; kb = pr.get('breath_k', 1)
    b = wv(th, kb) + .22 * wv(th, 2 * kb, .6); bl = wv(th, kb, 1.0)                # 숨 · 늦게 따라오는 숨(팔)
    w = wv(th, 1, 2.2) / 1.6; w2 = wv(th, 2, .7) * .6 + wv(th, 1, 3.6) * .4        # 무게 옮김(한쪽으로 갔다 온다) · 느린 비틀림
    h = wv(th, 1, 4.1) * .7 + wv(th, 2, 1.3) * .3; h2 = wv(th, 2, 2.4) * .6 + wv(th, 3, .4) * .4
    a1 = wv(th, 3, 1.1) * .45 + wv(th, 1, 5.2) * .65; a2 = wv(th, 2, 2.6) * .6 + wv(th, 1, .5) * .5; a3 = wv(th, 2, 3.3) * .6 + wv(th, 3, 5.0) * .4
    P.hips_t = P.hips_t + Vector((pr.get('sway_cm', 0) * .01 * w, pr.get('sway_y_cm', 0) * .01 * w2, pr.get('bob_cm', 0) * .01 * b)); P.fk()
    def add(n, R): P.set_world(n, R @ P.Q[n])
    sr = pr.get('sway_roll', 0); cy = pr.get('chest_yaw', 0); bd = pr.get('breath_deg', 0); sd = pr.get('shoulder_deg', 0)
    add('Hips', C.rot('Y', sr * w))
    add('Spine02', C.rot('Y', -sr * .8 * w) @ C.rot('Z', cy * .4 * w2))
    add('Spine01', C.rot('X', -bd * b) @ C.rot('Y', -sr * .6 * w) @ C.rot('Z', cy * .6 * w2))
    add('Spine', C.rot('X', -bd * b) @ C.rot('Y', sr * .3 * w))
    add('RightShoulder', C.rot('Y', sd * b)); add('LeftShoulder', C.rot('Y', -sd * b))
    add('neck', C.rot('X', bd * 1.3 * b) @ C.rot('Z', pr.get('head_yaw', 0) * .4 * h - cy * .5 * w2))
    add('Head', C.rot('X', bd * .5 * b + pr.get('head_pitch', 0) * h2) @ C.rot('Z', pr.get('head_yaw', 0) * .6 * h) @ C.rot('Y', -sr * .3 * w))
    ad = pr.get('arm_deg')
    if ad:      # 붓이 몸과 따로 논다: 오른 위팔(어깨 축) + 손목. 가슴 숨의 일부를 늦게 되돌려 붓이 몸보다 늦게 따라오게 한다
        add('RightArm', C.rot('X', ad[0] * a1 + bd * 1.1 * bl) @ C.rot('Z', ad[1] * a2))
        add('RightHand', C.rot('Y', pr.get('hand_deg', 0) * a3) @ C.rot('X', pr.get('hand_deg', 0) * .6 * wv(th, 1, 2.0)))
    P.fk()


def pchip(xs, ys, m_end=None, m_start=None):
    """단조 3차 보간(Fritsch–Carlson). 시간표 마디 사이 속도 배율이 뚝뚝 끊기지 않게 한다."""
    n = len(xs); h = [xs[i + 1] - xs[i] for i in range(n - 1)]; dl = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]; m = [0.0] * n; m[0] = dl[0] if m_start is None else m_start; m[-1] = dl[-1] if m_end is None else m_end
    for i in range(1, n - 1):
        if dl[i - 1] * dl[i] > 0: w1 = 2 * h[i] + h[i - 1]; w2 = h[i] + 2 * h[i - 1]; m[i] = (w1 + w2) / (w1 / dl[i - 1] + w2 / dl[i])
    def f(x):
        for i in range(n - 1):
            if x <= xs[i + 1] or i == n - 2:
                t = (x - xs[i]) / h[i]; t2 = t * t; t3 = t2 * t
                return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i] + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1]
    return f


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
# ================================================================ 0) s4: 새 쥐기(오른손 = 앞손 · 조종, 왼손 = 꼬리 축) · 새 전투 태세 · 열쇠 자세 클립
SHIFT2 = C2.SHIFT2; LZ = C2.LZ; HG = -SHIFT2 - LZ         # HG = 왼손(꼬리 축) → 오른손 쥠 점(자루를 따라 붓끝 쪽, m)
bpy.data.objects['GripSocketL'].location = (0, 0, LZ)     # 붓 리그의 왼손 소켓(저장된다)
GRT = C.Grip('Right', dict(C.GRIP_CFG, **clips_m2.THUMB_UP))
src = Source('Great_Sword_Idle'); OLD = src.sample(1); src.close(); ref = Vector((.009, .037))     # 옛 전투 태세(모캡) — 어깨 걸치기 몸이 여기서 나온다
SC = dict(clips_m2.STANCE4)
if opt('--stance'): SC.update({kv.split('=', 1)[0].strip(): eval(kv.split('=', 1)[1]) for kv in opt('--stance').split(';') if '=' in kv})
L1R = (RH['RightForeArm'] - RH['RightArm']).length; L2R = (RH['RightHand'] - RH['RightForeArm']).length
HINT_R = Vector((-.5, .1, -.85)); HINT_L = Vector(clips_m2.HINT_L)        # 팔꿈치 방향(가슴 기준): 오른 = 아래 · 바깥, 왼 = 뒤 · 바깥 · 아래
def dirv(yaw, pitch): y_ = math.radians(yaw); p_ = math.radians(pitch); return Vector((-math.sin(y_) * math.cos(p_), -math.cos(y_) * math.cos(p_), math.sin(p_)))
def kcol(keys, t, col):
    if len(keys) == 1: return keys[0][col]
    return pchip([k[0] for k in keys], [k[col] for k in keys], 0.0, 0.0)(min(max(t, keys[0][0]), keys[-1][0]))
def n_pref(d, v): v = Vector(v); v = v - d * v.dot(d); return v.normalized() if v.length > .05 else (Z - d * Z.dot(d)).normalized()
def body_pose(b, ft=None):
    """b = (골반 요, 가슴 요, 숙임, 옆 기울임, dx, dy, dz, 머리 요, 머리 숙임) — 전투 태세(STANCE4) 대비 차이. 발은 태세 자리에 박혀 있다.
    ft(D308-87, 온몸 동작) = (왼 dx, dy, dz, 발끝 숙임, 오른 dx, dy, dz, 발끝 숙임) — 발목을 태세 자리에서 옮긴 양(m) · 발끝을 아래로 숙인 각(도, + = 뒤꿈치 듦). 없으면 0."""
    hy = SC['hy'] + b[0]; cy = SC['cy'] + b[1]; lean = SC['lean'] + b[2]; side = b[3]; hd = SC['hd'] + b[7]; hp = SC['hp'] + b[8]
    P = C.Pose(); P.hips_t = Vector(SC['hips']) + Vector(b[4:7]); P.fk()
    R = lambda yw, ln, sd=0: C.rot('Z', -yw) @ C.rot('X', ln) @ C.rot('Y', sd)
    P.set_world('Hips', R(hy, lean * .25, side * .3)); P.set_world('Spine02', R(hy + (cy - hy) * .35, lean * .5, side * .6)); P.set_world('Spine01', R(hy + (cy - hy) * .7, lean * .8, side * .9)); P.set_world('Spine', R(cy, lean, side))
    P.set_world('neck', R((cy + hd) / 2, lean * .4 + hp * .5, side * .4)); P.set_world('Head', R(hd, hp))
    ank = {}; fq = {}; knee = {}
    for s, k in (('Left', 'lf'), ('Right', 'rf')):
        o = ft[(0 if s == 'Left' else 4):][:4] if ft else (0, 0, 0, 0)
        ank[s] = Vector((SC[k][0] + o[0], SC[k][1] + o[1], tgt_ankle[s] + o[2])); fq[s] = C.rot('Z', -SC[k][2]) @ C.rot('X', o[3]); knee[s] = C.rot('Z', -SC[k][2]) @ Vector((0, -1, .3))
    legs(P, ank, fq, knee); F = Frame(P, ank, fq, knee); F.src_hand = 0; F.srcf = 0; F.hint = P.Q['Spine'] @ HINT_R; return F
def tail_pen4(P, G, d, n, hint):
    """오른손 뒤쪽 자루가 제 오른팔에 닿는 벌점."""
    qh = GR.hand_q(d, n); W = G - qh @ (GR.C - RH['RightHand']); A = P.H['RightArm']; E, W2, reach = two_bone(A, W, L1R, L2R, hint); pen = 0
    for sd in (-.10, -.20, -.30):
        p_ = G + d * sd; pen += (max(0, .10 - seg_pt(p_, A, E)) * 60) ** 2 + (max(0, .05 - seg_pt(p_, E, E.lerp(W2, .7))) * 80) ** 2
    return pen
def ring_fix(e1, d, nref, K_):
    """고리 기준 e1 을 조금 돌려 nref 가 정확히 격자 위에 오게 한다(클립끼리 이음매가 격자 반 칸만큼 어긋나지 않게)."""
    e2 = d.cross(e1); a0 = math.atan2(nref.dot(e2), nref.dot(e1)); st = 2 * math.pi / K_; k = int(round(a0 / st)); dl = a0 - k * st
    e1n = (e1 * math.cos(dl) + e2 * math.sin(dl)).normalized(); return e1n, d.cross(e1n), k % K_
def ref_n(P, s, sock, d, hint, pref, soft, extra=None):
    """한 프레임 기준 풀이: 손바닥 방향 n 을 72방향에서 고른다(비용 + 약한 선호). 클립 처음 · 끝 자세는 이것으로 정의된다."""
    e1 = n_pref(d, pref); e2 = d.cross(e1); best = None
    for k in range(72):
        a = 2 * math.pi * k / 72; nk = e1 * math.cos(a) + e2 * math.sin(a); c = attempt(P, s, sock, d, nk, torso_caps(P), hint)[0] + (nk.angle(e1) / math.radians(soft)) ** 2 + (extra(nk) if extra else 0)
        if best is None or c < best[0]: best = (c, nk)
    return best[1]
def solve_right_seq(out, a0=True, a1=True):
    """프레임 목록(F.aim = (오른손 쥠 점, 자루 방향))의 오른팔: 손바닥 방향 · 팔꿈치 돌림을 프레임 사이 DP 로. 처음 · 끝은 한 프레임 기준 풀이에 묶는다."""
    N_ = len(out); K_ = 72; cs = np.zeros((N_, K_)); E_ = []; e1_ = None; refs = {}; nR = SC['nR']; soft = math.radians(SC['nR_soft'])
    for i_, on_ in ((0, a0), (N_ - 1, a1)):
        if on_: F = out[i_]; refs[i_] = ref_n(F.P, 'Right', F.aim[0], F.aim[1], F.hint, nR, SC['nR_soft'], lambda nk, F=F: tail_pen4(F.P, F.aim[0], F.aim[1], nk, F.hint))
    for i, F in enumerate(out):
        G_, d = F.aim; base = n_pref(d, nR); e1_ = base if e1_ is None else (e1_ - d * e1_.dot(d)).normalized(); kr = None
        if i in refs: e1_, e2_, kr = ring_fix(e1_, d, refs[i], K_)
        else: e2_ = d.cross(e1_)
        E_.append((e1_.copy(), e2_.copy())); caps = torso_caps(F.P)
        for k in range(K_):
            a_ = 2 * math.pi * k / K_; nk = e1_ * math.cos(a_) + e2_ * math.sin(a_); cs[i, k] = attempt(F.P, 'Right', G_, d, nk, caps, F.hint)[0] + (nk.angle(base) / soft) ** 2
        if kr is not None: cs[i, :] += 1e5; cs[i, kr] -= 1e5
    dk_ = np.abs(np.arange(K_)[:, None] - np.arange(K_)[None, :]); dk_ = np.minimum(dk_, K_ - dk_) * (360 / K_); TR_ = (dk_ / 20.0) ** 2
    acc_ = cs[0].copy(); back_ = np.zeros((N_, K_), dtype=int)
    for i in range(1, N_): tot_ = acc_[:, None] + TR_; back_[i] = tot_.argmin(axis=0); acc_ = tot_.min(axis=0) + cs[i]
    path_ = [int(acc_.argmin())]
    for i in range(N_ - 1, 0, -1): path_.append(int(back_[i][path_[-1]]))
    path_.reverse(); PH = list(range(-150, 181, 30)); S_ = len(PH); c2 = np.zeros((N_, S_)); HN = []
    for i, F in enumerate(out):
        G_, d = F.aim; e1_, e2_ = E_[i]; a_ = 2 * math.pi * path_[i] / K_; nk = e1_ * math.cos(a_) + e2_ * math.sin(a_); ax = (G_ - F.P.H['RightArm']).normalized(); h0 = (F.hint - ax * F.hint.dot(ax)).normalized(); caps = torso_caps(F.P); row = []
        for j, ph in enumerate(PH):
            h_ = axis_rot(ax, ph) @ h0; c2[i, j] = attempt(F.P, 'Right', G_, d, nk, caps, h_)[0] + tail_pen4(F.P, G_, d, nk, h_) + (ph / 70.0) ** 2 + (1e5 if i in refs and ph != 0 else 0); row.append(h_)
        HN.append((nk, row))
    dp_ = np.abs(np.arange(S_)[:, None] - np.arange(S_)[None, :]); dp_ = np.minimum(dp_, S_ - dp_) * 30; T2 = (dp_ / 35.0) ** 2; acc_ = c2[0].copy(); bk = np.zeros((N_, S_), dtype=int)
    for i in range(1, N_): tot_ = acc_[:, None] + T2; bk[i] = tot_.argmin(axis=0); acc_ = tot_.min(axis=0) + c2[i]
    p2 = [int(acc_.argmin())]
    for i in range(N_ - 1, 0, -1): p2.append(int(bk[i][p2[-1]]))
    p2.reverse(); reach_ = []; wr_ = []
    for i, F in enumerate(out):
        G_, d = F.aim; c_, r_ = attempt(F.P, 'Right', G_, d, HN[i][0], torso_caps(F.P), HN[i][1][p2[i]]); set_arm(F.P, 'Right', r_); F.P.fk(); reach_.append(r_['reach']); wr_.append(math.degrees(r_['swing']))
    return dict(r_reach=[round(x, 2) for x in reach_], r_wrist=[round(x) for x in wr_], r_swivel=[PH[j] for j in p2])
def stance_brush(): return (0, 0) + tuple(SC['p']) + (SC['yaw'], SC['pitch'])
def brush_at(k):
    """brush 열쇠 값(틀 요, px, py, pz, yaw, pitch) → (왼손 축 점, 자루 방향)"""
    fy, px, py, pz, yw, pt = k; return C.rot('Z', -fy) @ Vector((px, py, pz)), dirv(fy + yw, pt)
def make_stance():
    F = body_pose((0,) * 9); p, d = brush_at(stance_brush()[1:]); F.aim = (p + d * HG, d); solve_right_seq([F]); hl = F.P.Q['Spine'] @ HINT_L
    nl = ref_n(F.P, 'Left', p, d, hl, SC['nL'], SC['nL_soft']); c, r = attempt(F.P, 'Left', p, d, nl, torso_caps(F.P), hl); set_arm(F.P, 'Left', r); F.P.fk(); F.t = 0; return F
STANCE = make_stance()
def build_keyed():
    N_ = int(round(SPEC['seconds'] * FPS)) + 1; out = []; LIFE = SPEC.get('life'); BK = SPEC['body']; RK = [(k[0],) + stance_brush()[1:] if k[1] == 'S' else k for k in SPEC['brush']]
    for i in range(N_):
        t = i / FPS; FK = SPEC.get('feet'); F = body_pose([kcol(BK, t, c) for c in range(1, 10)], [kcol(FK, t, c) for c in range(1, 9)] if FK else None); kb = [kcol(RK, t, c) for c in range(1, 7)]
        for n_b in ('LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand'): F.P.L[n_b] = STANCE.P.L[n_b].copy()
        F.P.fk(); dp_ = Vector((0, 0, 0))
        if LIFE:
            x = i / (N_ - 1); th = 2 * math.pi * x; base = F.P.H['Spine'].copy(); life(F.P, x, LIFE); legs(F.P, F.ank, F.fq, F.knee); dp_ = (F.P.H['Spine'] - base) * LIFE.get('brush_follow', .7)
            kb[4] += LIFE.get('tip_yaw', 0) * (wv(th, 1, 1.3) * .6 + wv(th, 2, 3.9) * .4); kb[5] += LIFE.get('tip_pitch', 0) * (wv(th, 2, .4) * .5 + wv(th, 1, 2.7) * .6)
            F.hint = F.P.Q['Spine'] @ HINT_R
        p, d = brush_at(kb); F.aim = (p + dp_ + d * HG, d); F.t = t; out.append(F)
    info['right_arm'] = solve_right_seq(out); return out
FR = []; LW_FORCE = None; info = dict(name=NAME, use=SPEC['use']); KEYED = SPEC.get('kind') == 'keyed'; SH = None; FW = None
if KEYED:
    FR = build_keyed(); info.update(src='손으로 만듦(열쇠 자세 · 새 쥐기)', speed_factors=[], root='none', stance={k_: (list(v_) if isinstance(v_, tuple) else v_) for k_, v_ in SC.items()})
elif 'special' not in SPEC:
    kn = SPEC['knots']; T_end = kn[-1][0] + SPEC['blend_out']; NF = int(round(T_end * FPS)) + 1; src = Source(SPEC['src'])
    TAILV = SPEC.get('tail_speed', .5) if SPEC['blend_out'] else 0      # 마지막 마디 뒤(전투 태세로 섞이는 동안) 원본이 흐르는 속도 배율
    _pc = pchip([k[0] for k in kn], [k[1] for k in kn], FPS * TAILV) if SPEC.get('smooth') else None
    def srcf(t):
        if t >= kn[-1][0]: return kn[-1][1] + (t - kn[-1][0]) * FPS * TAILV
        if _pc: return _pc(t)
        for (t0, a), (t1, b) in zip(kn, kn[1:]):
            if t <= t1: return a + (b - a) * (t - t0) / max(t1 - t0, 1e-9)
    def offset(f):
        r = SPEC['root']
        if r == 'none': return Vector((0, 0))
        if r == 'lowpass': return src.lowpass(f) - ref
        return Vector(r) * ((f - src.f0) / max(1, src.f1 - src.f0))
    TWS = SPEC.get('twist_src')                                  # swing3: 윗몸 요 창을 원본 프레임으로 준다(시간표를 바꿔도 같은 동작 구간에 걸린다)
    def env(t, f=0): return ease((f - TWS[0]) / (TWS[1] - TWS[0])) if TWS else ease((t - .12) / .30)                     # 윗몸 요가 들어오는 창(내리찍기)
    PLANT = SPEC.get('plant'); YLIM = SPEC.get('yaw_limit'); H0 = STANCE.P.hips_t
    def spine_yaw(P): v = P.Q['Spine'] @ Vector((0, -1, 0)); return math.degrees(math.atan2(-v.x, -v.y))
    Y0 = spine_yaw(STANCE.P)
    def planted(F):
        """swing3: 발을 전투 태세 자리에 박는다. 엉덩이 수평 이동은 원본의 PLANT['xy'] 배만 남기고 PLANT['max'] m 로 자른다(높이는 원본 그대로)."""
        dv = Vector((F.P.hips_t.x - H0.x, F.P.hips_t.y - H0.y)) * PLANT.get('xy', .5); mx = PLANT.get('max', .10)
        if dv.length > mx: dv = dv * (mx / dv.length)
        dz = (F.P.hips_t.z - H0.z) * PLANT.get('z', 1.0); F.P.hips_t = Vector((H0.x + dv.x, H0.y + dv.y, H0.z + dz)); F.P.fk()
        F.ank = {s: v.copy() for s, v in STANCE.ank.items()}; F.fq = {s: v.copy() for s, v in STANCE.fq.items()}; F.knee = {s: v.copy() for s, v in STANCE.knee.items()}
    AIM = SPEC.get('aim')      # swing3: 붓 길을 목표(오른손 쥠 점 · 자루 방향)로 다룬다 → build_aim
    def kf(key, t, dflt=0.0, col=1):
        ks = AIM.get(key) if AIM else None
        if not ks: return dflt
        if len(ks) == 1: return ks[0][col]
        return pchip([k[0] for k in ks], [k[col] for k in ks])(min(max(t, ks[0][0]), ks[-1][0]))
    def ylim(dy):
        k = .6 * YLIM; a = abs(dy)
        return dy if a <= k else math.copysign(k + (YLIM - k) * math.tanh((a - k) / (YLIM - k)), dy)
    def vslerp(a, b, w): return (Quaternion().slerp(a.rotation_difference(b), w) @ a).normalized()
    def prep(i, twist):
        """원본 한 프레임 → 발 박기 · 윗몸 요 제한(캐릭터 정면 기준 절대값, 부드럽게)까지."""
        t = i / FPS; f = srcf(t); F = src.sample(f, offset(f), twist * env(t, f)); F.t = t; F.srcf = f
        if PLANT is not None: planted(F)
        if AIM: F.h0 = hand_state(F.P); F.neck0 = F.P.H['neck'].copy()
        if YLIM is not None:
            dy = (spine_yaw(F.P) + 180) % 360 - 180; lim = ylim(dy)
            if abs(dy - lim) > .05: yaw(F, -(dy - lim))
        if AIM and kf('twist', t): yaw(F, kf('twist', t))
        if AIM and kf('upright', t) > 1e-4:       # 윗몸 숙임 · 기울임을 전투 태세 쪽으로 덜어 낸다(요를 누르면 어깨가 손 높이까지 내려와 꼬리 자루가 팔에 걸린다)
            for n_b in ('Spine02', 'Spine01', 'Spine', 'RightShoulder', 'LeftShoulder'): F.P.L[n_b] = slerp(F.P.L[n_b], STANCE.P.L[n_b], kf('upright', t))
            F.P.fk()
        if PLANT is not None or YLIM is not None or AIM: legs(F.P, F.ank, F.fq, F.knee)
        return F
    def wts(t): return (ease(t / SPEC['blend_in']) if SPEC['blend_in'] > 0 else 1), (ease((t - kn[-1][0]) / SPEC['blend_out']) if SPEC['blend_out'] > 0 else 0)
    def build(twist):
        out = []
        for i in range(NF):
            F0 = prep(i, twist); w_in, w_out = wts(F0.t); F = blend(STANCE, F0, w_in); F = blend(F, STANCE, w_out)
            if SPEC.get('life'): life(F.P, i / (NF - 1), SPEC['life']); legs(F.P, F.ank, F.fq, F.knee)
            F.t = F0.t; F.srcf = F0.srcf; out.append(F)
        return out
    def body_caps(P):
        return torso_caps(P) + [(P.H[s_ + 'UpLeg'], P.H[s_ + 'Leg'], .085) for s_ in ('Left', 'Right')] + [(P.H[s_ + 'Leg'], P.H[s_ + 'Foot'], .06) for s_ in ('Left', 'Right')]
    def closest(p, a_, b_): ab = b_ - a_; t_ = max(0, min(1, (p - a_).dot(ab) / max(ab.dot(ab), 1e-9))); return a_ + ab * t_
    def clear(P, G, d, margin=.02):
        """자루(꼬리 끝 ~ 붓머리 목)가 몸통 · 머리 · 다리 캡슐에 들어가면 목표를 통째로 밀어낸다(자루 방향은 그대로)."""
        caps = body_caps(P)
        for it in range(10):
            worst = None
            for sd in (-.30, -.22, -.14, .2, .4, .6, .8, 1.0, 1.2):
                p_ = G + d * sd
                for a_, b_, r_ in caps:
                    c_ = closest(p_, a_, b_); need = r_ + margin - (p_ - c_).length
                    if need > 0 and (worst is None or need > worst[0]):
                        h_ = Vector((p_.x - c_.x, p_.y - c_.y, 0)); h_ = (h_.normalized() if h_.length > 1e-4 else Vector((0, 0, 0))) + Vector((0, -.6, .15)); worst = (need, h_.normalized())      # 수평으로, 앞쪽으로 치우쳐 민다
            if worst is None: break
            G = G + worst[1] * worst[0]
        return G
    def arm_pts(P, G, d, n, hint):
        qh = GR.hand_q(d, n); W = G - qh @ (GR.C - RH['RightHand']); A = P.H['RightArm']; E, W2, reach = two_bone(A, W, (RH['RightForeArm'] - RH['RightArm']).length, (RH['RightHand'] - RH['RightForeArm']).length, hint); return A, E, W2
    def tail_pen(P, G, d, n, hint):
        """꼬리 쪽 자루(오른손 뒤 0.08 ~ 0.30 m)가 자기 오른팔에 닿는 벌점."""
        A, E, W2 = arm_pts(P, G, d, n, hint); pen = 0
        for sd in (-.10, -.16, -.22, -.30):
            p_ = G + d * sd; pen += (max(0, .17 - seg_pt(p_, A, E)) * 60) ** 2 + (max(0, .10 - seg_pt(p_, E, E.lerp(W2, .8))) * 80) ** 2
        return pen
    def build_aim(twist):
        """swing3: 돌지 않는 휘두르기. 몸(척추 · 다리 · 머리)은 원본을 전투 태세와 섞고, 붓은 '오른손 쥠 점 + 자루 방향' 목표로 다룬다:
        목표 = 원본(요 제한 전)의 손 · 붓 방향을 목뼈 기준으로 옮긴 것 → 전투 태세 목표와 같은 가중치로 섞음(자리는 선형, 방향은 구면) → aim 열쇠(out · side · fwd · lift · yaw · pitch · hand · dir)로 다듬음.
        오른팔은 그 목표로 IK(손바닥 방향은 프레임 사이 DP), 왼손은 뒤 단계의 기존 IK. 첫 · 끝 프레임은 전투 태세 팔 그대로."""
        d_s, n_s, G_s = hand_state(STANCE.P); out = []
        for i in range(NF):
            F0 = prep(i, twist); t = F0.t; w_in, w_out = wts(t); w = w_in * (1 - w_out); d0, n0, G0 = F0.h0
            R = C.rot('Z', -kf('yaw', t)); d = (R @ d0).normalized(); s_ = d.cross(Z); pt = math.radians(kf('pitch', t))
            if s_.length > .05 and abs(pt) > 1e-5: s_.normalize(); d = (d * math.cos(pt) + s_.cross(d) * math.sin(pt)).normalized()
            rel = R @ (G0 - F0.neck0); rel = Vector((rel.x * (1 + kf('out', t)), rel.y * (1 + kf('out', t)), rel.z)); Graw = F0.P.H['neck'] + rel + Vector((kf('side', t), -kf('fwd', t), kf('lift', t)))
            F = blend(STANCE, F0, w_in); F = blend(F, STANCE, w_out); F.t = t; F.srcf = F0.srcf
            G_ = G_s.lerp(Graw, w); d = vslerp(d_s, d, w); n_ = vslerp(n_s, (R @ n0).normalized(), w)
            if AIM.get('hand'): G_ = G_.lerp(Vector((kf('hand', t, col=1), kf('hand', t, col=2), kf('hand', t, col=3))), max(0.0, min(1.0, kf('hand', t, col=4))))     # 오른손 쥠 점을 절대 자리로 끈다(t, x, y, z, 가중치)
            if AIM.get('dir'): d = vslerp(d, Vector((kf('dir', t, col=1), kf('dir', t, col=2), kf('dir', t, col=3))).normalized(), max(0.0, min(1.0, kf('dir', t, col=4))))  # 자루 방향을 절대 방향으로 끈다
            if w > 0 and not AIM.get('noclear'): G_ = G_.lerp(clear(F.P, G_, d), min(1.0, 3 * w))
            F.aim = (G_, d, n_); out.append(F)
        K_ = 72; cs = np.zeros((NF, K_)); E_ = []; e1_ = None
        for i, F in enumerate(out):
            G_, d, n_ = F.aim
            if e1_ is None: e1_ = (n_s - d * n_s.dot(d)).normalized()
            else: e1_ = (e1_ - d * e1_.dot(d)).normalized()
            e2_ = d.cross(e1_); E_.append((e1_.copy(), e2_.copy())); caps = torso_caps(F.P); F.hint = (STANCE.P.H['RightForeArm'] - STANCE.P.H['RightArm']).normalized() + Vector((-.25, .25, -.35))
            for k in range(K_):
                a_ = 2 * math.pi * k / K_; nk = e1_ * math.cos(a_) + e2_ * math.sin(a_); cs[i, k] = attempt(F.P, 'Right', G_, d, nk, caps, F.hint)[0]
                if i in (0, NF - 1): cs[i, k] += 60 * nk.angle((n_s - d * n_s.dot(d)).normalized()) ** 2       # 처음 · 끝은 전투 태세의 손바닥 방향
        dk_ = np.abs(np.arange(K_)[:, None] - np.arange(K_)[None, :]); dk_ = np.minimum(dk_, K_ - dk_) * (360 / K_); TR_ = (dk_ / 20.0) ** 2
        acc_ = cs[0].copy(); back_ = np.zeros((NF, K_), dtype=int)
        for i in range(1, NF): tot_ = acc_[:, None] + TR_; back_[i] = tot_.argmin(axis=0); acc_ = tot_.min(axis=0) + cs[i]
        path_ = [int(acc_.argmin())]
        for i in range(NF - 1, 0, -1): path_.append(int(back_[i][path_[-1]]))
        path_.reverse(); reach_ = []; ARMB = ('RightShoulder', 'RightArm', 'RightForeArm', 'RightHand')
        PH = list(range(-150, 181, 30)); S_ = len(PH); c2 = np.zeros((NF, S_)); HN = []
        for i, F in enumerate(out):      # 팔꿈치 돌림(어깨→손목 축 둘레)도 프레임 사이 DP 로 고른다: 꼬리 자루가 제 팔에 닿지 않게
            G_, d, n_ = F.aim; e1_, e2_ = E_[i]; a_ = 2 * math.pi * path_[i] / K_; nk = e1_ * math.cos(a_) + e2_ * math.sin(a_); ax = (G_ - F.P.H['RightArm']).normalized(); h0 = (F.hint - ax * F.hint.dot(ax)).normalized(); caps = torso_caps(F.P); row = []
            for j, ph in enumerate(PH):
                h_ = axis_rot(ax, ph) @ h0; c2[i, j] = attempt(F.P, 'Right', G_, d, nk, caps, h_)[0] + tail_pen(F.P, G_, d, nk, h_) + (ph / 70.0) ** 2 + (1e4 if i in (0, NF - 1) and ph != 0 else 0); row.append(h_)
            HN.append((nk, row))
        dp_ = np.abs(np.arange(S_)[:, None] - np.arange(S_)[None, :]); dp_ = np.minimum(dp_, S_ - dp_) * 30; T2 = (dp_ / 35.0) ** 2; acc_ = c2[0].copy(); bk = np.zeros((NF, S_), dtype=int)
        for i in range(1, NF): tot_ = acc_[:, None] + T2; bk[i] = tot_.argmin(axis=0); acc_ = tot_.min(axis=0) + c2[i]
        p2 = [int(acc_.argmin())]
        for i in range(NF - 1, 0, -1): p2.append(int(bk[i][p2[-1]]))
        p2.reverse(); info['aim_swivel_deg'] = [PH[j] for j in p2]
        for i, F in enumerate(out):
            G_, d, n_ = F.aim; c_, r_ = attempt(F.P, 'Right', G_, d, HN[i][0], torso_caps(F.P), HN[i][1][p2[i]]); set_arm(F.P, 'Right', r_); reach_.append(r_['reach'])
            ws = {0: 0.0, 1: .35, 2: .75}.get(min(i, NF - 1 - i))
            if ws is not None:
                for n_b in ARMB: F.P.L[n_b] = slerp(STANCE.P.L[n_b], F.P.L[n_b], ws)
            F.P.fk()
        info['aim_r_reach_max'] = round(max(reach_), 3); info['aim_r_reach_over_frames'] = sum(1 for x in reach_ if x > .999)
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
    info['twist_deg'] = tw; FR = build_aim(tw) if AIM else build(tw)
    speeds = [round((b - a) / ((t1 - t0) * FPS), 2) for (t0, a), (t1, b) in zip(kn, kn[1:]) if t1 > t0]
    info.update(src=SPEC['src'], knots=kn, speed_factors=speeds, src_window=[kn[0][1], kn[-1][1]], blend_in=SPEC['blend_in'], blend_out=SPEC['blend_out'], root=SPEC['root'], src_scale=round(src.S, 4), arm_ratio=round(tgt_arm / src.arm, 3))
    src.close()
else:
    _cg, _cd = clips_m2.CARRY_G, clips_m2.CARRY_D
    if opt('--carry'):      # 시험용: --carry sx,sy,sz,pitch,yaw,L
        _v = [float(x) for x in opt('--carry').split(',')]; _cg, _cd = clips_m2.carry_gd(_v[:3], _v[3], _v[4], _v[5])
    BUMP = Vector([float(x) for x in opt('--bump').split(',')]) if opt('--bump') else Vector(clips_m2.CARRY_BUMP)
    CG = Vector(_cg); CD = Vector(_cd).normalized(); P0 = OLD.P; d0, n0, G0 = hand_state(STANCE.P)
    def body(s, breath=0.0):
        P = C.Pose(); P.hips_t = P0.hips_t + Vector((0, 0, .015 * s))
        for n in ORDER:
            k = 1 - .4 * s if n in ('Spine02', 'Spine01', 'Spine', 'neck', 'Head') else 1
            P.L[n] = Quaternion().slerp(P0.L[n].to_quaternion(), k).to_matrix() if k < 1 else P0.L[n].copy()
            if n in ('Spine01', 'Spine') and breath: P.L[n] = C.rot('X', -breath) @ P.L[n]
        P.fk(); legs(P, {s_: v.copy() for s_, v in OLD.ank.items()}, OLD.fq, OLD.knee); return P
    def hang(P):
        Lh = {'LeftArm': C.rot('Y', 74), 'LeftForeArm': C.rot('Z', -16), 'LeftHand': Matrix.Identity(3)}
        P.L['LeftArm'] = P.Q[PARENT['LeftArm']].inverted() @ (P.Q['Spine'] @ Lh['LeftArm']); P.L['LeftForeArm'] = Lh['LeftForeArm']; P.L['LeftHand'] = Lh['LeftHand']; P.fk()
    P1 = body(1); hint1 = Vector((-.5, .2, -.85)); c1, r1 = best_n(P1, 'Right', CG, CD, torso_caps(P1), hint1); n1 = r1['n']; hint0 = STANCE.P.H['RightForeArm'] - STANCE.P.H['RightArm']
    info['carry'] = dict(G=list(CG), d=list(CD), r_reach=round(r1['reach'], 3), r_elbow=round(r1['elbow'], 1), r_wrist_swing=round(math.degrees(r1['swing']), 1))
    def pose_at(s, breath=0.0):
        Fc = Frame(body(1.0, breath), {s_: v.copy() for s_, v in OLD.ank.items()}, OLD.fq, OLD.knee); Fb = blend(STANCE, Fc, ease(s)); P = Fb.P; u = ease((s - .12) / .88)
        d = Quaternion().slerp(d0.rotation_difference(CD), u) @ d0; d.normalize()
        G = G0.lerp(CG, u) + BUMP * math.sin(math.pi * u) + Vector((0, 0, .004 * breath))
        nn = Quaternion().slerp(n0.rotation_difference(n1), u) @ n0; nn = (nn - d * nn.dot(d)).normalized()
        if u > 0:           # u = 0 이면 오른팔은 모캡 그대로(전투 태세와 똑같이 시작 · 끝나게)
            c, r = attempt(P, 'Right', G, d, nn, torso_caps(P), hint0.normalized().lerp(hint1.normalized(), u)); set_arm(P, 'Right', r)
            if u < .25:
                for n_ in ('RightArm', 'RightForeArm', 'RightHand'): P.L[n_] = slerp(STANCE.P.L[n_], P.L[n_], u / .25)
        P.fk(); F = Frame(P, Fb.ank, Fb.fq, Fb.knee); F.src_hand = 0; F.u = u; return F
    CL = float(opt('--carry').split(',')[5]) if opt('--carry') else clips_m2.CARRY_L; S0 = CG + CD * CL; SIDE = Z.cross(CD).normalized(); LIFE = SPEC.get('life')
    def carry_alive(x):
        """swing3: 어깨 걸치기 대기 한 프레임. 몸 = body(1) + life(), 자루는 어깨에 얹힌 점(S0, 어깨 관절을 따라 움직인다)을 받침으로 조금씩 끄덕 · 흔들린다. x = 0 이면 pose_at(1, 0) 과 같다."""
        P = body(1.0); life(P, x, LIFE); legs(P, {s: v.copy() for s, v in OLD.ank.items()}, OLD.fq, OLD.knee); th = 2 * math.pi * x
        S = S0 + (P.H['RightArm'] - P1.H['RightArm']) * LIFE.get('shaft_follow', .9)
        d = (axis_rot(Z, LIFE.get('shaft_yaw', 0) * (wv(th, 2, .8) * .5 + wv(th, 1, 3.0) * .5)) @ axis_rot(SIDE, LIFE.get('shaft_pitch', 0) * (wv(th, 1, 1.9) * .6 + wv(th, 3, 4.4) * .3)) @ CD).normalized()
        G = S - d * CL; nn = (n1 - d * n1.dot(d)).normalized()
        c, r = attempt(P, 'Right', G, d, nn, torso_caps(P), hint1); set_arm(P, 'Right', r); P.fk(); F = Frame(P, OLD.ank, OLD.fq, OLD.knee); F.src_hand = 0; F.u = 1.0; return F
    NF = int(round(SPEC['seconds'] * FPS)) + 1; LW_FORCE = []; SH = []; FW = []
    for i in range(NF):
        x = i / (NF - 1)
        if SPEC['special'] == 'carry_idle': s = 1.0; br = .6 * math.sin(2 * math.pi * x); lw = 0.0
        elif SPEC['special'] == 'to_carry': s = x; br = 0; lw = 1 - ease(s / .30)
        else: s = 1 - ease(x) ** .8; br = 0; lw = 1 - ease(s / .30)
        F = carry_alive(x) if (SPEC['special'] == 'carry_idle' and LIFE) else pose_at(s, br); F.t = i / FPS; F.srcf = 0; F.hang = lw < 1; FR.append(F); LW_FORCE.append(lw); SH.append(SHIFT2 + (SHIFT - SHIFT2) * F.u); FW.append(1 - F.u)
    info.update(src='손으로 만듦(전투 태세 ↔ 어깨 걸치기 열쇠 자세 보간)', speed_factors=[], root='none')
NF = len(FR); SH = SH or [SHIFT2] * NF; FW = FW or [1.0] * NF; POSES = [F.P for F in FR]; MET = [dict(frame=i, t=FR[i].t, srcf=FR[i].srcf, src_hand_dist=FR[i].src_hand) for i in range(NF)]
print('M1 built', NAME, NF, 'frames', round((NF - 1) / FPS, 3), 's', json.dumps({k: v for k, v in info.items() if k != 'knots'}, ensure_ascii=False, default=float))

# ================================================================ 2) 오른손 당김 + 붓
PULLMAX = .16; raw = []
for i in range(NF):
    P = POSES[i]; d, n, Gp = hand_state(P); v = P.H['LeftArm'] - (Gp + d * (SH[i] + LZ)); over = (v.length - .03 - .93 * tgt_arm) if not KEYED else 0
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
    for n_, l in GR.L.items(): P.L[n_] = slerp(l, GRT.L[n_], FW[i]) if 0 < FW[i] < 1 else (GRT.L[n_] if FW[i] >= 1 else l)
    P.fk(); d, n, Gp = hand_state(P); BR.append(dict(G=Gp + d * SH[i], Gh=Gp, d=d, n=n))

# ================================================================ 3) 왼손
K = 72; costs = np.zeros((NF, K)); E1 = []; minreach = []; e1 = None
LHF = {}
def lhint0(i): return (POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * clips_m2.LHINT_FWD
def lhint(i): return (LHF[i] if i in LHF else lhint0(i)) if (KEYED or i in LENDS) else MET[i]['l_fk_elbow'].normalized() + (POSES[i].Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35
if LW_FORCE is not None:        # 손으로 만든 클립: 왼팔 늘어뜨린 자세를 먼저 적어 둔다(가중치 0 쪽)
    for i in range(NF):
        P = POSES[i]; Lh = {'LeftArm': C.rot('Y', 74), 'LeftForeArm': C.rot('Z', -16), 'LeftHand': Matrix.Identity(3)}
        if SPEC.get('special') == 'carry_idle' and SPEC.get('life'):     # swing3: 늘어뜨린 왼팔도 조금 흔들린다(첫 · 끝 프레임은 0)
            th_ = 2 * math.pi * i / (NF - 1); hg = SPEC['life'].get('hang_deg', 0)
            Lh['LeftArm'] = C.rot('X', hg * (wv(th_, 1, 2.9) * .6 + wv(th_, 2, 1.0) * .3)) @ C.rot('Y', 74 + hg * .5 * wv(th_, 2, 4.0)); Lh['LeftForeArm'] = C.rot('Z', -16 - hg * .8 * wv(th_, 1, 3.6)); Lh['LeftHand'] = C.rot('Z', -hg * .6 * wv(th_, 2, 5.2))
        P.L['LeftArm'] = P.Q[PARENT['LeftArm']].inverted() @ (P.Q['Spine'] @ Lh['LeftArm']); P.L['LeftForeArm'] = Lh['LeftForeArm']; P.L['LeftHand'] = Lh['LeftHand']; P.fk()
        MET[i]['l_fk_elbow'] = STANCE.P.H['LeftForeArm'] - STANCE.P.H['LeftArm']
NL = SC['nL']; NLS = math.radians(SC['nL_soft']); LREF = {}
LENDS = [k_ for k_, on_ in ((0, KEYED or SPEC.get('special') == 'to_carry'), (NF - 1, KEYED or SPEC.get('special') == 'to_stance')) if on_]
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']
    if e1 is None:
        e1 = Z - d * Z.dot(d); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized()
    else: e1 = (e1 - d * e1.dot(d)).normalized()
    caps = torso_caps(P); hint = lhint(i); sock = b['G'] + d * LZ; mr = 9; kr = None; basel = n_pref(d, NL)
    if i in LENDS: e1, e2, kr = ring_fix(e1, d, ref_n(P, 'Left', sock, d, hint, NL, SC['nL_soft']), K); LREF[i] = kr
    else: e2 = d.cross(e1)
    E1.append((e1.copy(), e2.copy()))
    for k in range(K):
        a = 2 * math.pi * k / K; nk_ = e1 * math.cos(a) + e2 * math.sin(a); costs[i, k], r_ = attempt(P, 'Left', sock, d, nk_, caps, hint); mr = min(mr, r_['reach']); costs[i, k] += (nk_.angle(basel) / NLS) ** 2
    if kr is not None: costs[i, :] += 1e5; costs[i, kr] -= 1e5
    minreach.append(mr)
feas = [1.0 if r <= .985 else 0.0 for r in minreach]
info['l_minreach'] = [round(r, 2) for r in minreach]
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
if SPEC.get('loop') and not KEYED: path = [max(set(path), key=path.count)] * NF
if KEYED:       # s4: 왼 팔꿈치 방향(자루 쪽 치우침 f)도 프레임 사이 DP — 꼬리 쪽 자루가 왼 아래팔을 지나가지 않게, 손목이 꺾이지 않게
    FS = [-60, -30, 0, 30, 60]; HR = []; c3 = np.zeros((NF, len(FS))); L1L = (RH['LeftForeArm'] - RH['LeftArm']).length; L2L = (RH['LeftHand'] - RH['LeftForeArm']).length
    for i in range(NF):
        P = POSES[i]; b = BR[i]; d = b['d']; e1, e2 = E1[i]; a = 2 * math.pi * path[i] / K; nk_ = e1 * math.cos(a) + e2 * math.sin(a); sock = b['G'] + d * LZ; qh = GL.hand_q(d, nk_); W_ = sock - qh @ (GL.C - RH['LeftHand'])
        ax_ = (W_ - P.H['LeftArm']).normalized(); h0_ = lhint0(i); h0_ = (h0_ - ax_ * h0_.dot(ax_)).normalized(); HR.append([axis_rot(ax_, f_) @ h0_ for f_ in FS])
        for j, f_ in enumerate(FS):
            h_ = HR[i][j]; E_, W2_, rc_ = two_bone(P.H['LeftArm'], W_, L1L, L2L, h_); pen = 0
            for sd in (.07, .12, .17): pen += (max(0, .065 - seg_pt(sock - d * sd, E_, E_.lerp(W2_, .8))) * 90) ** 2 + (max(0, .09 - seg_pt(sock - d * sd, P.H['LeftArm'], E_)) * 60) ** 2
            c3[i, j] = attempt(P, 'Left', sock, d, nk_, torso_caps(P), h_)[0] + pen + (f_ / 80.0) ** 2 + (1e5 if i in LENDS and f_ != 0 else 0)
    T3 = ((np.array(FS)[:, None] - np.array(FS)[None, :]) / 35.0) ** 2; acc = c3[0].copy(); bk3 = np.zeros((NF, len(FS)), dtype=int)
    for i in range(1, NF): tot = acc[:, None] + T3; bk3[i] = tot.argmin(axis=0); acc = tot.min(axis=0) + c3[i]
    p3 = [int(acc.argmin())]
    for i in range(NF - 1, 0, -1): p3.append(int(bk3[i][p3[-1]]))
    p3.reverse(); LHF = {i: HR[i][p3[i]] for i in range(NF)}; info['l_swivel'] = [FS[j] for j in p3]
for i in range(NF):
    P = POSES[i]; b = BR[i]; d = b['d']; e1, e2 = E1[i]; a = 2 * math.pi * path[i] / K; m = MET[i]; w = LW[i]
    cst, r = attempt(P, 'Left', b['G'] + d * LZ, d, e1 * math.cos(a) + e2 * math.sin(a), torso_caps(P), lhint(i)); m.pop('l_fk_elbow')
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
             yaw_hips=math.degrees(math.atan2(-(P.Q['Hips'] @ Vector((0, -1, 0))).x, -(P.Q['Hips'] @ Vector((0, -1, 0))).y)),
             yaw_spine=math.degrees(math.atan2(-(P.Q['Spine'] @ Vector((0, -1, 0))).x, -(P.Q['Spine'] @ Vector((0, -1, 0))).y)), ankle_move_cm=max((FR[i].ank[s] - FR[0].ank[s]).length for s in ('Left', 'Right')) * 100)
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
_ys = [(m['yaw_spine'] - MET[0]['yaw_spine'] + 180) % 360 - 180 for m in MET]
info.update(torso_yaw_vs_first_deg=[round(min(_ys), 1), round(max(_ys), 1)], ankle_move_cm_max=round(max(m['ankle_move_cm'] for m in MET), 2),
            tip_span_xyz_m=[round(max(m['tip'][k] for m in MET) - min(m['tip'][k] for m in MET), 3) for k in range(3)], tip_speed_curve=[round(m['tip_speed'], 1) for m in MET])
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
