"""#308 GreatBrush: 실제 플레이어 몸(Player_C02_ReRig, 읽기만)에 큰 붓을 양손으로 쥐인 자세 스틸.
    blender -b --factory-startup -P pose.py -- <out_dir> <player_fbx> [자세 이름 ...] [--res 1280] [--blend] [--only-metrics]
방식: 붓의 자리 · 방향을 자세마다 정하고 → 소켓(GripSocket · GripSocketL)에 손 쥐기 틀을 놓고 → 두 뼈 IK(해석식)로 팔을 푼다.
손가락은 자루 굵기(3.4 cm)에 닿을 때까지 감는다(쉬는 자세에서 한 번 계산, 모든 자세가 같은 쥐기). 몸 뼈는 자세 모드 회전만 — 쉬는 자세는 건드리지 않는다.
좌표: 캐릭터는 -Y 를 본다. 오른쪽 = -X, 위 = +Z. 렌더는 Workbench(가벼움)."""
import bpy, importlib, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
ARGS = sys.argv[sys.argv.index('--') + 1:]
PDN = ARGS[ARGS.index('--poses') + 1] if '--poses' in ARGS else 'poses'
PD = importlib.import_module(PDN)
TAIL = getattr(PD, 'TAIL_LEN', .60); TIPL = getattr(PD, 'TIP_LEN', .95); HEADL = getattr(PD, 'HEAD_LEN', .38); HEADR = getattr(PD, 'HEAD_R', .048)
REF_PATH = os.path.join(HERE, PDN + '_refined.json'); REF = json.load(open(REF_PATH, encoding='utf-8')) if os.path.exists(REF_PATH) else {}
OUT, FBX = ARGS[0], ARGS[1]
RES = int(ARGS[ARGS.index('--res') + 1]) if '--res' in ARGS else 1280
NAMES = [a for a in ARGS[2:] if not a.startswith('--') and not a.isdigit() and a != PDN] or list(PD.POSES)
os.makedirs(os.path.join(OUT, 'stills'), exist_ok=True)

R_SHAFT = .017
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def rot(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis)
def euler(lean=0, side=0, yaw=0): return rot('Z', yaw) @ rot('X', lean) @ rot('Y', side)


def frame(d, b):
    d = d.normalized(); b = (b - d * b.dot(d)).normalized(); c = d.cross(b)
    return Matrix((d, b, c)).transposed()


def axis_rot(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis.normalized())


# ---------------------------------------------------------------- 장면
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'SM_GreatBrush308.blend'))
brush_rig = bpy.data.objects['GreatBrush308_Rig']; brush = bpy.data.objects['SM_GreatBrush308']
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=FBX, automatic_bone_orientation=False)
new = [o for o in bpy.data.objects if o not in before]
arm = next(o for o in new if o.type == 'ARMATURE'); body = next(o for o in new if o.type == 'MESH' and o.name.startswith('C02'))
for o in new:
    if o.type == 'EMPTY': o.hide_render = True; o.hide_viewport = True
AW = arm.matrix_world.copy(); S_ARM = AW.to_scale().x
BONES = {b.name: b for b in arm.data.bones}
ORDER = []
def walk(b):
    ORDER.append(b.name)
    for c in b.children: walk(c)
for b in arm.data.bones:
    if b.parent is None: walk(b)
REST_ROT = {n: b.matrix_local.to_3x3() for n, b in BONES.items()}
REST_HEAD = {n: AW @ b.head_local for n, b in BONES.items()}
REST_TAIL = {n: AW @ b.tail_local for n, b in BONES.items()}
PARENT = {n: (b.parent.name if b.parent else None) for n, b in BONES.items()}
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'


class Pose:
    def __init__(self): self.L = {n: Matrix.Identity(3) for n in BONES}; self.hips_t = Vector((0, 0, 0)); self.Q = {}; self.H = {}

    def fk(self):
        for n in ORDER:
            p = PARENT[n]
            if p is None: self.Q[n] = self.L[n].copy(); self.H[n] = REST_HEAD[n] + self.hips_t
            else: self.Q[n] = self.Q[p] @ self.L[n]; self.H[n] = self.H[p] + self.Q[p] @ (REST_HEAD[n] - REST_HEAD[p])

    def set_world(self, n, q):
        """뼈 n 의 월드 회전(쉬는 자세 대비)을 q 로."""
        p = PARENT[n]; self.L[n] = (self.Q[p].inverted() @ q) if p else q.copy(); self.fk()

    def tail(self, n): return self.H[n] + self.Q[n] @ (REST_TAIL[n] - REST_HEAD[n])

    def apply(self):
        for n in ORDER:
            pb = arm.pose.bones[n]; r = REST_ROT[n]
            pb.rotation_quaternion = (r.inverted() @ self.L[n] @ r).to_quaternion()
            pb.location = (r.inverted() @ self.hips_t) / S_ARM if PARENT[n] is None else (0, 0, 0)
        bpy.context.view_layer.update()


def two_bone(A, W, l1, l2, hint):
    D = W - A; dist = D.length; reach = dist / (l1 + l2); dist = min(dist, (l1 + l2) * .999); dist = max(dist, abs(l1 - l2) * 1.01)
    Dn = D.normalized(); a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist); h = math.sqrt(max(l1 * l1 - a * a, 0))
    hp = hint - Dn * hint.dot(Dn); hp = hp.normalized() if hp.length > 1e-6 else Dn.orthogonal().normalized()
    E = A + Dn * a + hp * h; W2 = A + Dn * dist
    return E, W2, reach


# ---------------------------------------------------------------- 손 쥐기(쉬는 자세에서 한 번)
def seg_line_dist(p, q, c, a):
    best = 1e9
    for i in range(7):
        x = p.lerp(q, i / 6) - c; best = min(best, (x - a * x.dot(a)).length)
    return best


class Grip:
    """한 손의 쥐기: 쉬는 자세 월드 축에서 자루 축(A) · 손바닥 법선(N) · 자루 중심점(C) + 손가락 국소 회전."""

    def __init__(self, side, cfg, slant=0):
        s = side; self.side = s; self.slant = slant; hh = REST_HEAD[s + 'Hand']
        mcp = [REST_HEAD[s + 'Hand' + f + '1'] for f in ('Index', 'Middle', 'Ring', 'Pinky')]
        F = (REST_HEAD[s + 'HandMiddle1'] - hh).normalized(); A0 = (mcp[0] - mcp[3]).normalized()
        N = F.cross(A0).normalized(); N = -N if N.z > 0 else N                      # 손바닥은 T자세에서 아래를 본다
        A = (A0 - N * A0.dot(N)).normalized()
        sg = 1 if N.cross(A).dot(F) > 0 else -1
        A = (axis_rot(N, slant * sg) @ A).normalized()                             # 자루가 손바닥을 비스듬히 가로지르는 각(엄지 쪽 끝이 손가락 쪽으로 기운다 — 악수 쥐기)
        Fp = N.cross(A); Fp = -Fp if Fp.dot(F) < 0 else Fp
        mid = (mcp[0] + mcp[1] + mcp[2] + mcp[3]) / 4
        self.C = mid + N * (R_SHAFT + cfg['palm']) - Fp * cfg['back']; self.A, self.N, self.F = A, N, Fp
        self.L = {}; self.report = {}
        for f, rf in (('Index', cfg['rf']), ('Middle', cfg['rf']), ('Ring', cfg['rf'] * .95), ('Pinky', cfg['rf'] * .85)):
            names = [s + 'Hand' + f + str(i) for i in (1, 2, 3)]
            pr = [REST_HEAD[n] for n in names] + [REST_TAIL[names[2]]]
            k = (pr[1] - pr[0]).normalized().cross(N).normalized(); ang = [0, 0, 0]; caps = cfg['caps']; thr = R_SHAFT + rf

            def chain(ang):
                Q = Matrix.Identity(3); p = [pr[0]]
                for j in range(3): Q = Q @ axis_rot(k, ang[j]); p.append(p[-1] + Q @ (pr[j + 1] - pr[j]))
                return p
            for j in range(3):
                while ang[j] < caps[j]:
                    p = chain(ang)
                    if seg_line_dist(p[j].lerp(p[j + 1], .35), p[j + 1], self.C, A) <= thr: break
                    ang[j] += 1
            ang = [a + cfg.get('squeeze', 0) for a in ang]
            for j in range(3): self.L[names[j]] = axis_rot(k, ang[j])
            p = chain(ang); self.report[f] = {'deg': ang, 'tip_to_axis_cm': round(seg_line_dist(p[3], p[3], self.C, A) * 100, 2)}
        # 엄지: 손가락 반대쪽으로 자루 아래를 돌아 검지 · 중지 위에 얹힌다
        names = [s + 'HandThumb' + str(i) for i in (1, 2, 3)]; pr = [REST_HEAD[n] for n in names] + [REST_TAIL[names[2]]]
        t0 = (pr[1] - pr[0]).normalized(); kt = N.cross(Fp).normalized()
        best = None
        t1 = (N * math.cos(math.radians(cfg['thumb_psi'])) + Fp * math.sin(math.radians(cfg['thumb_psi'])) + A * cfg['thumb_along']).normalized()
        Q1 = t0.rotation_difference(t1).to_matrix()
        Q1 = axis_rot(t1, cfg.get('thumb_roll', 0)) @ Q1
        ang = [0, 0]; thr = R_SHAFT + cfg['rt']

        def tchain(ang):
            Q = Q1.copy(); p = [pr[0], pr[0] + Q @ (pr[1] - pr[0])]
            for j in range(2): Q = Q @ (Q1.inverted() @ axis_rot(kt, ang[j]) @ Q1); p.append(p[-1] + Q @ (pr[j + 2] - pr[j + 1]))
            return p
        for j in range(2):
            while ang[j] < cfg['thumb_caps'][j]:
                p = tchain(ang)
                if seg_line_dist(p[j + 1].lerp(p[j + 2], .35), p[j + 2], self.C, A) <= thr: break
                ang[j] += 1
        self.L[names[0]] = Q1
        for j in range(2): self.L[names[j + 1]] = Q1.inverted() @ axis_rot(kt, ang[j]) @ Q1
        p = tchain(ang); self.report['Thumb'] = {'deg': ang, 'seg1_to_axis_cm': round(seg_line_dist(p[0], p[1], self.C, A) * 100, 2), 'tip_to_axis_cm': round(seg_line_dist(p[3], p[3], self.C, A) * 100, 2)}

    def hand_q(self, d, n):
        """자루 방향 d(엄지 쪽이 붓끝), 손바닥이 향하는 방향 n(자루 축 쪽) → 손 뼈 월드 회전."""
        return frame(d, n) @ frame(self.A, self.N).transposed()


GRIPS = {(s, sl): Grip(s, PD.GRIP, sl) for s in ('Right', 'Left') for sl in PD.SLANTS}
print('POSE grip', json.dumps({'%s%d' % k: g.report for k, g in GRIPS.items()}))

# 손 · 손가락 꼭짓점(몸 관통 검사에서 뺀다)과 뼈별 꼭짓점
gi = {g.index: g.name for g in body.vertex_groups}
dom = []
for v in body.data.vertices:
    best = max(v.groups, key=lambda g: g.weight, default=None); dom.append(gi[best.group] if best else '')
dom = np.array(dom); is_hand = np.array(['Hand' in d for d in dom]); is_arm = np.array([('Arm' in d or 'Hand' in d) for d in dom])


def body_points():
    dg = bpy.context.evaluated_depsgraph_get(); ev = body.evaluated_get(dg); me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); ev.to_mesh_clear()
    M = np.array(body.matrix_world, dtype=np.float32)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def dist_to_segment(P, a, b):
    a = np.array(a, dtype=np.float32); b = np.array(b, dtype=np.float32); ab = b - a
    t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1); return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t


# ---------------------------------------------------------------- 자세 풀이
ARM_BEND = {'Right': Z, 'Left': -Z}


def solve(name):
    pd = PD.POSES[name]; P = Pose(); P.hips_t = Vector(pd.get('hips_t', (0, 0, 0)))
    for n, e in pd.get('L', {}).items(): P.L[n] = euler(*e)
    P.fk()
    if 'head' in pd:
        qh = euler(*pd['head']); qs = P.Q['Spine']
        P.set_world('neck', qs.to_quaternion().slerp(qh.to_quaternion(), .5).to_matrix()); P.set_world('Head', qh)
    m = {'pose': name}
    # 다리: 발을 땅에 두고 엉덩이 높이에 맞춰 무릎을 굽힌다
    for s in ('Left', 'Right'):
        f = pd.get('feet', {}).get(s, {}); up, lo, ft = s + 'UpLeg', s + 'Leg', s + 'Foot'
        target = REST_HEAD[ft] + Vector(f.get('t', (0, 0, 0))); qf = euler(f.get('pitch', 0), 0, f.get('yaw', 0))
        hint = Vector(f.get('knee', (0, -1, 0))); hint = rot('Z', f.get('yaw', 0)) @ hint
        l1 = (REST_HEAD[lo] - REST_HEAD[up]).length; l2 = (REST_HEAD[ft] - REST_HEAD[lo]).length
        E, W2, reach = two_bone(P.H[up], target, l1, l2, hint)
        b = (E - P.H[up]).cross(W2 - E)
        if b.length < 1e-6: b = (rot('Z', f.get('yaw', 0)) @ X)
        P.set_world(up, frame(E - P.H[up], b) @ frame(REST_HEAD[lo] - REST_HEAD[up], X).transposed())
        P.set_world(lo, frame(W2 - E, b) @ frame(REST_HEAD[ft] - REST_HEAD[lo], X).transposed())
        P.set_world(ft, qf); m['leg_' + s] = {'reach': round(reach, 3), 'knee_deg': round(math.degrees((P.H[up] - E).angle(W2 - E)), 1)}
    # 붓: 자리 · 방향(--refine 이면 처음 값 둘레에서 팔 · 손목이 가장 편한 곳을 찾는다 — 찾은 값은 출력해서 poses.py 에 적어 둔다)
    G = Vector(pd['brush']['G']); d = Vector(pd['brush']['d']).normalized(); gap = PD.HAND_GAP
    if name in REF and '--refine' not in ARGS: G = Vector(REF[name]['G']); d = Vector(REF[name]['d']).normalized()
    free = {s for s in ('Right', 'Left') if pd.get('hands', {}).get(s, {}).get('free')}
    caps = [(P.H['Hips'] + Vector((0, 0, -.08)), P.H['Spine01'], .15), (P.H['Spine01'], P.H['neck'], .15), (P.H['Head'], P.H['Head'] + P.Q['Head'] @ Vector((0, -.03, .16)), .115)]
    for s in ('Left', 'Right'): caps += [(P.H[s + 'UpLeg'], P.H[s + 'Leg'], .09), (P.H[s + 'Leg'], P.H[s + 'Foot'], .07)]

    def seg_pt(p, a, b):
        ab = b - a; t = max(0, min(1, (p - a).dot(ab) / max(ab.dot(ab), 1e-9))); return (p - (a + ab * t)).length

    def arm_solve(s, sock, d, hc):
        up, lo, hd = s + 'Arm', s + 'ForeArm', s + 'Hand'
        e1 = (Z - d * Z.dot(d)); e1 = e1.normalized() if e1.length > .05 else (Y - d * Y.dot(d)).normalized(); e2 = d.cross(e1)
        dflt = (-.55, .45, -.7) if s == 'Right' else (.55, .45, -.7)
        hint = Vector(hc.get('hint', dflt)); A = P.H[up]
        l1 = (REST_HEAD[lo] - REST_HEAD[up]).length; l2 = (REST_HEAD[hd] - REST_HEAD[lo]).length
        u_rest = REST_HEAD[lo] - REST_HEAD[up]; f_rest = (REST_HEAD[hd] - REST_HEAD[lo]); fa = f_rest.normalized()
        tw_c = math.radians(80 if s == 'Right' else -80)       # 아래팔 비틀림의 편한 가운데(T자세 손바닥 아래 = 0, 엄지 위 = ±90)

        def attempt(phi, sl):
            g = GRIPS[(s, sl)]; n = e1 * math.cos(math.radians(phi)) + e2 * math.sin(math.radians(phi))
            qh = g.hand_q(d, n); W = sock - qh @ (g.C - REST_HEAD[hd])
            E, W2, reach = two_bone(A, W, l1, l2, hint); b = (E - A).cross(W2 - E)
            if b.length < 1e-7: b = ARM_BEND[s]
            qu = frame(E - A, b) @ frame(u_rest, ARM_BEND[s]).transposed(); qf = frame(W2 - E, b) @ frame(f_rest, ARM_BEND[s]).transposed()
            q = (qf.inverted() @ qh).to_quaternion(); proj = fa.dot(Vector((q.x, q.y, q.z))); tw = 2 * math.atan2(proj, q.w)
            tw = (tw - tw_c + math.pi) % (2 * math.pi) - math.pi + tw_c
            sw = (q @ Quaternion(fa, tw).inverted()); swing = 2 * math.acos(max(-1, min(1, abs(sw.w))))
            hdir = qf.inverted() @ qh @ fa; flex = math.degrees(math.asin(max(-1, min(1, hdir.dot(g.N))))); dev = math.degrees(math.asin(max(-1, min(1, hdir.dot(g.A)))))
            elbow = math.degrees((A - E).angle(W2 - E)); dtw = abs(tw - tw_c)
            cost = swing ** 2 + (sl / 90) ** 2 + .04 * dtw ** 2 + 6 * max(0, dtw - math.radians(75)) ** 2 + (max(0, reach - .95) * 40) ** 2 + (max(0, 60 - elbow) / 15) ** 2 + (max(0, elbow - 160) / 25) ** 2
            for a_, b_, r_ in caps[:3]:
                cost += (max(0, r_ + .045 - seg_pt(W2, a_, b_)) * 25) ** 2 + (max(0, r_ + .02 - seg_pt(E, a_, b_)) * 25) ** 2
            return cost, dict(phi=phi, qh=qh, W=W, W2=W2, E=E, qu=qu, qf=qf, tw=tw, swing=swing, reach=reach, elbow=elbow, flex=flex, dev=dev, n=n, fa=fa, cost=cost, slant=sl)
        sls = hc.get('slants', PD.SLANTS)
        cands = [attempt(hc['phi'], sl) for sl in sls] if 'phi' in hc else [attempt(p, sl) for sl in sls for p in range(0, 360, hc.get('step', 3))]
        return min(cands, key=lambda c: c[0])

    def total(G, d, step=3):
        c = 0; out = {}
        for s, sock in (('Right', G), ('Left', G - d * gap)):
            if s in free: continue
            hc = dict(pd.get('hands', {}).get(s, {})); hc.setdefault('step', step)
            cs, r = arm_solve(s, sock, d, hc); c += cs; out[s] = r
        tail = G - d * TAIL; tip = G + d * TIPL
        for s, sock in (('Right', G), ('Left', G - d * gap)):      # 자루가 제 팔을 지나가지 않게(쥔 손 둘레는 뺀다)
            if s in free: continue
            r = out[s]; A = P.H[s + 'Arm']
            for i in range(25):
                p = tail.lerp(tip, i / 24)
                if (p - sock).length < .13: continue
                c += (max(0, .075 - seg_pt(p, A, r['E'])) * 40) ** 2 + (max(0, .06 - seg_pt(p, r['E'], r['W2'])) * 40) ** 2
        for i in range(25):                                        # 자루 · 붓털이 몸통 · 머리 · 다리를 지나가지 않게
            p = tail.lerp(tip, i / 24); rr = .02 if i / 24 < 1 - HEADL / (TAIL + TIPL) else HEADR + .005
            for a_, b_, r_ in caps: c += (max(0, r_ + rr + .015 - seg_pt(p, a_, b_)) * 40) ** 2
        return c, out

    if 'refine' in pd and '--refine' in ARGS:
        import random
        rnd = random.Random(308); G0, d0 = G.copy(), d.copy(); best, _ = total(G, d, 9); lock = pd['refine']
        for it in range(lock.get('n', 420)):
            k = 1 - it / lock.get('n', 420) * .7
            G2 = G + Vector([rnd.gauss(0, lock.get('g', .05)) * k for _ in range(3)]); d2 = (d + Vector([rnd.gauss(0, lock.get('d', .10)) * k for _ in range(3)])).normalized()
            c2, _ = total(G2, d2, 9); c2 += ((G2 - G0).length * lock.get('wg', 3.5)) ** 2 + (d2.angle(d0) * lock.get('wd', 2.5)) ** 2
            if 'tip' in lock: c2 += (((G2 + d2 * TIPL) - Vector(lock['tip'])).length * 12) ** 2
            if c2 < best: best, G, d = c2, G2, d2
        REF[name] = {'G': [round(x, 4) for x in G], 'd': [round(x, 4) for x in d]}
        print('POSE refined', name, 'G=(%.3f, %.3f, %.3f), d=(%.3f, %.3f, %.3f)' % (*G, *d), 'cost %.3f' % best)
    socks = {'Right': G, 'Left': G - d * gap}
    ctot, sol = total(G, d); m['cost'] = round(ctot, 3)
    for s in free:      # 쥐지 않는 손: 자세 자료의 팔 회전 그대로, 손가락은 힘을 뺀다
        N = GRIPS[(s, 0)].N
        for f in ('Index', 'Middle', 'Ring', 'Pinky'):
            for j, a in zip((1, 2, 3), (22, 28, 16)):
                bn = s + 'Hand' + f + str(j); P.L[bn] = axis_rot((REST_TAIL[bn] - REST_HEAD[bn]).normalized().cross(N), a)
        P.fk()
    for s in ('Right', 'Left'):
        if s in free: continue
        r = sol[s]; g = GRIPS[(s, r['slant'])]; up, lo, hd = s + 'Arm', s + 'ForeArm', s + 'Hand'; fa = r['fa']
        P.set_world(up, r['qu']); P.set_world(lo, r['qf'] @ axis_rot(fa, math.degrees(r['tw']) * PD.TWIST_SHARE)); P.set_world(hd, r['qh'])
        for n, l in g.L.items(): P.L[n] = l
        P.fk()
        m['arm_' + s] = {'phi': r['phi'], 'slant': r['slant'], 'cost': round(r['cost'], 2), 'reach': round(r['reach'], 3), 'elbow_deg': round(r['elbow'], 1), 'wrist_swing_deg': round(math.degrees(r['swing']), 1),
                         'wrist_flex_deg': round(r['flex'], 1), 'wrist_dev_deg': round(r['dev'], 1), 'forearm_twist_deg': round(math.degrees(r['tw']), 1),
                         'palm_faces': [round(x, 2) for x in r['n']], 'elbow_pos': [round(x, 2) for x in r['E']]}
    P.apply()
    # 붓 놓기 + 붓털
    brush_rig.matrix_world = Matrix.Translation(G) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    br = pd.get('bristle', {}); keys = brush.data.shape_keys.key_blocks
    keys['BristlePress'].value = br.get('press', 0); keys['BristleSplay'].value = br.get('splay', 0)
    bpy.context.view_layer.update()
    deg, ax = br.get('bend', (0, (1, 0, 0))); ax = Vector(ax).normalized(); rw = brush_rig.matrix_world.to_3x3()
    for i in range(8):
        pb = brush_rig.pose.bones['Bristle_%02d' % (i + 1)]; pb.rotation_mode = 'QUATERNION'
        w = ((i + 1) / 8) ** 1.5 - (i / 8) ** 1.5
        pb.rotation_quaternion = Quaternion((rw @ pb.bone.matrix_local.to_3x3()).inverted() @ ax, math.radians(deg * w))
    bpy.context.view_layer.update()
    # ---- 검사(전부 평가된 메시 · 뼈에서 잰다)
    for s, sock in (('Right', 'GripSocket'), ('Left', 'GripSocketL')):
        if s in free: continue
        g = GRIPS[(s, m['arm_' + s]['slant'])]; hb = arm.pose.bones[s + 'Hand']; M = AW @ hb.matrix
        c_world = M @ (AW @ hb.bone.matrix_local).inverted() @ g.C          # 쥔 손의 자루 중심점이 실제로 간 곳
        m['arm_' + s]['grip_to_socket_cm'] = round((c_world - bpy.data.objects[sock].matrix_world.translation).length * 100, 2)
    Pw = body_points(); tail = bpy.data.objects['ShaftEndSocket'].matrix_world.translation; neck = G + d * (TIPL - HEADL)
    tipw = bpy.data.objects['TipSocket'].matrix_world.translation
    ds, _ = dist_to_segment(Pw, tail, neck); pen = (ds < R_SHAFT + .002) & ~is_hand
    dh, th = dist_to_segment(Pw, neck, G + d * TIPL); penh = (dh < HEADR * (1 - th * .8)) & ~is_hand
    bones, cnt = np.unique(dom[pen | penh], return_counts=True)
    m['shaft_inside_body_verts'] = int(pen.sum()); m['head_inside_body_verts'] = int(penh.sum()); m['inside_by_bone'] = dict(zip(bones.tolist(), cnt.tolist()))
    nb = ~is_arm; m['shaft_clearance_torso_cm'] = round(float((ds[nb].min() - R_SHAFT) * 100), 1); m['bristle_clearance_torso_cm'] = round(float((dh[nb].min() - HEADR) * 100), 1)
    for s in ('Right', 'Left'):
        if s in free: continue
        sel = np.array([d_.startswith(s + 'Hand') for d_ in dom]); x = ds[sel] - R_SHAFT
        m['arm_' + s]['hand_skin_min_gap_mm'] = round(float(x.min() * 1000), 1); m['arm_' + s]['hand_verts_inside_shaft_3mm'] = int((x < -.003).sum())
        m['arm_' + s]['hand_verts_within_4mm'] = int((np.abs(x) < .004).sum())
        for f in ('Index', 'Middle', 'Pinky', 'Thumb'):
            pb = arm.pose.bones[s + 'Hand' + f + '3']; tp = AW @ pb.tail; a = np.array(tp, dtype=np.float32)[None]
            m['arm_' + s][f + '_tip_to_axis_cm'] = round(float(dist_to_segment(a, tail, neck)[0][0]) * 100, 2)
    m['tip'] = [round(x, 3) for x in tipw]; m['tail'] = [round(x, 3) for x in tail]
    m['feet_z'] = {s: round((AW @ arm.pose.bones[s + 'Foot'].head).z, 3) for s in ('Left', 'Right')}
    m['min_body_z'] = round(float(Pw[:, 2].min()), 3)
    return m, P, G, d


# ---------------------------------------------------------------- 렌더
def setup_render():
    s = bpy.context.scene
    engines = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    s.render.engine = 'BLENDER_WORKBENCH'
    s.render.resolution_x = RES; s.render.resolution_y = RES * 9 // 16; s.render.resolution_percentage = 100; s.render.image_settings.file_format = 'PNG'
    s.render.film_transparent = False
    if s.world is None: s.world = bpy.data.worlds.new('Grey')
    try: s.world.use_nodes = False
    except Exception: pass
    s.world.color = (.30, .30, .30)
    sh = s.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = .35; sh.show_cavity = True; sh.cavity_type = 'BOTH'
    sh.show_object_outline = False; sh.show_specular_highlight = True; sh.curvature_ridge_factor = .6; sh.curvature_valley_factor = .8
    s.display.light_direction = (-.45, .35, .82); s.display.render_aa = '8'; s.view_settings.view_transform = 'Standard'
    fm = bpy.data.materials.new('Floor'); fm.diffuse_color = (.62, .62, .62, 1)
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); fl = bpy.context.active_object; fl.name = 'Floor'; fl.data.materials.append(fm)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); s.collection.objects.link(cam); s.camera = cam; cam.data.sensor_fit = 'VERTICAL'; cam.data.clip_start = .05
    print('POSE engines', engines, 'images', [(i.name, i.size[:], i.packed_file is not None, i.filepath) for i in bpy.data.images])
    return cam, fl


def look(cam, pos, target, fov, roll_up=Z):
    cam.location = pos; cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.angle_y = math.radians(fov)


def shot(path):
    bpy.context.scene.render.filepath = path; bpy.ops.render.render(write_still=True)


cam, floor = setup_render()
metrics = []
for name in NAMES:
    m, P, G, d = solve(name); pd = PD.POSES[name]; metrics.append(m)
    print('POSE metrics', json.dumps(m))
    if '--only-metrics' in ARGS: continue
    pts = [P.H['Head'] + Vector((0, 0, .22)), REST_HEAD['LeftFoot'] * 1, REST_HEAD['RightFoot'] * 1, Vector((0, 0, 0)), Vector(m['tip']), Vector(m['tail']),
           P.H['LeftHand'], P.H['RightHand'], P.H['LeftForeArm'], P.H['RightForeArm']]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)]); c = (lo + hi) / 2
    c.z = max(c.z, 0.0); rad = (max((p - c).length for p in pts) + .10) * PD.FIT
    cdir = Vector(pd.get('cam_dir', (-.62, -.74, .25))).normalized(); fov = 38
    look(cam, c + cdir * rad / math.sin(math.radians(fov / 2) * .98), c, fov); shot(os.path.join(OUT, 'stills', name + '_q34.png'))
    # 게임 어깨 카메라: 머리 높이 축에서 뒤 2.8 m · 위 0.55 m, FOV 60. 발이 화면에 들어오도록 축을 아래로 PITCH 도 숙였다(게임에서는 마우스 피치).
    piv = Vector((0, 0, PD.SHOULDER_PIVOT_Z)); q = rot('X', PD.SHOULDER_PITCH)
    look(cam, piv + q @ Vector((0, 2.8, .55)), piv + q @ Vector((0, 2.8, .55)) + q @ Vector((0, -1, 0)), 60); shot(os.path.join(OUT, 'stills', name + '_shoulder.png'))
    if pd.get('hands_closeup'):
        mid = G - d * PD.HAND_GAP / 2; hd = Vector(pd['hands_closeup']).normalized()
        look(cam, mid + hd * .95, mid, 36); shot(os.path.join(OUT, 'stills', name + '_hands.png'))
    if '--hands' in ARGS:   # 쥐기 검사용: 손등 · 손가락 쪽 · 손바닥 쪽 · 자루 축 방향
        for s, sock in (('Right', G), ('Left', G - d * PD.HAND_GAP)):
            if 'arm_' + s not in m: continue
            n = Vector(m['arm_' + s]['palm_faces']); f = arm.pose.bones[s + 'HandMiddle1'].head - arm.pose.bones[s + 'Hand'].head; f = (AW.to_3x3() @ f).normalized()
            for tag, hv in (('back', -n), ('fingers', f + n * .3), ('palm', n + f * .2), ('axis', d + f * .25), ('axisb', -d + f * .25)):
                look(cam, sock + hv.normalized() * .34, sock, 36); shot(os.path.join(OUT, 'stills', 'chk_' + name + '_' + s + '_' + tag + '.png'))
    if '--blend' in ARGS: bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'pose_' + name + '.blend'), copy=True)
if '--refine' in ARGS: json.dump(REF, open(REF_PATH, 'w', encoding='utf-8'), indent=1)
json.dump(metrics, open(os.path.join(OUT, 'pose-metrics' + ('' if len(NAMES) == len(PD.POSES) else '-partial') + '.json'), 'w', encoding='utf-8'), indent=1)
print('POSE DONE')
