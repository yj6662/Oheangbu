"""#308 enemy run (SPEC-ENEMY-RIG-VERIFY-308, section "run (D308-23)", TEST): run clips for agwi / changgui, built in Blender from
the repaired walk (same armature, same bones, same rest pose). Headless, CPU only, NO render (the sheets are drawn afterwards by
sheets_run308.py, the numpy rasteriser of Tools/Blender/EnemyRig308/sheets308.py).

  B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
  python Tools/resource_guard.py --wait
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRun308/enemyrun308.py -- build <id> [--no-export]

Reads (read only): the repaired walk FBX of Tools/Unity/Stage308_enemyrig (gated by sha256 in enemyrun308.json) and
Art/Characters308/DokkaebiVerify/fix/<id>_fix.json (walk numbers). Uses Tools/Blender/EnemyRig308/rig308.py for every measure, so
run and walk numbers share one definition. Writes: Art/Characters308/DokkaebiVerify/run/<id>_run.json, run/work/<id>_run.npz and
Tools/Unity/Stage308_enemyrun/Art/Characters/Folklore298/Animations/<id>/<id>-run308_run308.fbx (take Armature|run308).

Construction (parameters: enemyrun308.json, nothing numeric here but definitions):
  upper body  = the repaired walk at the same normalised phase (scaled about its mean), plus lean, bounce and head stabilising
  legs        = planned from scratch: the ball of the stance foot is fixed on the ground (moves back at exactly the clip speed in
                the in-place frame), heel-off pivots about the ball with the toes flat, the swing is a Hermite arc that continues
                position and velocity at toe-off and touch-down; two-bone IK with a knee guard and with the knee hinge axis taken
                from the walk; hips height = the highest that keeps the stance knee bent by the guard angle
  arms        = direction targets for upper arm and forearm, elbow hinge axis from the walk
  loop        = every curve is a function of the phase, so pose and velocity close; in place (hips height baked, no travel)
Blender axes: +Z up, the character faces -Y, its own LEFT is +X. Phase 0 = right foot mid-stance (as the repaired walk)."""
import bpy, sys, os, json, math, hashlib
import numpy as np
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__)); RIGDIR = os.path.join(HERE, '..', 'EnemyRig308'); sys.path.insert(0, RIGDIR); sys.path.insert(0, HERE)
import rig308 as R
import inside308 as INS

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace('\\', '/')
CFG = json.load(open(os.path.join(HERE, 'enemyrun308.json'), encoding='utf-8'))
ARGS = sys.argv[sys.argv.index('--') + 1:]
OUT = REPO + '/' + CFG['out']; WORK = OUT + '/work'
os.makedirs(WORK, exist_ok=True)
XAXIS = Vector((1, 0, 0)); UP = R.UP; FWD = R.FWD
TORSO = ['Hips', 'Spine02', 'Spine01', 'Spine', 'neck', 'Head', 'LeftShoulder', 'RightShoulder']


def sha(p): return hashlib.sha256(open(p, 'rb').read()).hexdigest()
def fold(deg): return min(deg, 360.0 - deg)
def wrap(x): return x - math.floor(x + .5)            # -> [-.5, .5)
def clamp01(x): return max(0.0, min(1.0, x))
def smooth(x): x = clamp01(x); return x * x * (3 - 2 * x)
def rotx(deg): return Quaternion(XAXIS, math.radians(deg))


def qpow(q, s):
    axis, angle = q.to_axis_angle()
    if angle > math.pi: angle -= 2 * math.pi
    return Quaternion(axis, angle * s)


def world_q(ob, pb): return (ob.matrix_world @ pb.matrix).to_3x3().normalized().to_quaternion()


def set_world_q(ob, name, q):
    pb = ob.pose.bones[name]; R.rot_about(ob, pb, R.joint(ob, name), q @ world_q(ob, pb).inverted())


def twist_align(ob, name, axis, local_axis, want, share=1.0):
    """Rotate the bone about `axis` (a world unit vector through its head) until its local_axis, seen across the axis, points along
    `want`: sets the roll of a limb bone without moving the limb."""
    pb = ob.pose.bones[name]; cur = world_q(ob, pb) @ local_axis
    a = cur - axis * cur.dot(axis); b = want - axis * want.dot(axis)
    if a.length < 1e-6 or b.length < 1e-6: return 0.0
    a.normalize(); b.normalize(); ang = math.atan2(axis.dot(a.cross(b)), a.dot(b)) * share
    R.rot_about(ob, pb, R.joint(ob, name), Quaternion(axis, ang)); return math.degrees(ang)


def fbx_header(path):
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(path); out = {'version': version, 'models': {}, 'stacks': []}
    def props(node):
        d = {}
        for p70 in node.elems:
            if p70.id == b'Properties70':
                for p in p70.elems: d[p.props[0].decode('utf-8', 'replace')] = list(p.props[4:])
        return d
    for e in root.elems:
        if e.id == b'GlobalSettings':
            g = props(e); out['global'] = {k: g.get(k) for k in ('UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign', 'UnitScaleFactor')}
        if e.id == b'Objects':
            for o in e.elems:
                name = o.props[1].split(b'\x00')[0].decode('utf-8', 'replace') if len(o.props) > 1 and isinstance(o.props[1], bytes) else ''
                if o.id == b'Model' and name in ('Armature', 'char1'):
                    p = props(o); out['models'][name] = {k: p.get(k) for k in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling')}
                if o.id == b'Model': out['model_count'] = out.get('model_count', 0) + 1
                if o.id == b'AnimationStack':
                    p = props(o); out['stacks'].append({'name': name, 'LocalStart': p.get('LocalStart'), 'LocalStop': p.get('LocalStop')})
    return out


class Builder:
    def __init__(self, mid):
        self.mid = mid; self.m = m = CFG['monsters'][mid]; self.fps = CFG['fps']
        src = REPO + '/' + CFG['walk_stage'] + '/' + m['walk']
        if sha(src) != m['walk_sha256']: raise SystemExit('refused: %s is not the gated repaired walk (sha %s, config %s): re-base enemyrun308.json' % (src, sha(src)[:12], m['walk_sha256'][:12]))
        self.src = src
        self.arm, self.mesh = R.load(src); self.rig = R.Rig(self.arm, self.mesh); self.act = bpy.data.actions[0]; self.sc = bpy.context.scene
        if self.sc.render.fps != self.fps: raise SystemExit('refused: source fps %s != config %s' % (self.sc.render.fps, self.fps))
        self.f0, self.f1 = int(round(self.act.frame_range[0])), int(round(self.act.frame_range[1])); self.wn = self.f1 - self.f0
        self.N = m['cycle_frames']; self.T = self.N / self.fps; self.v = m['clip_speed']; self.ns = m['stance_frames']; self.d = self.ns / self.N
        # touch-down and toe-off fall on whole frames: an odd stance length puts mid-stance half a frame after phase 0
        off = (self.ns % 2) * .5 / self.N; self.mid_phase = {'Right': off, 'Left': .5 + off}
        self.rest0 = {b.name: np.array(b.matrix_local) for b in self.arm.data.bones}; self.arm_scale0 = [round(x, 6) for x in self.arm.scale]
        self.bones = [pb.name for pb in self.arm.pose.bones]; self.extra_abd = {s: 0.0 for s in R.SIDES}
        self.inside = INS.ArmInside(self.rig, CFG['recheck'])        # the stricter arm-in-body test (winding number + elbow ring)
        self.reference()

    # ------------------------------------------------------------------ the walk as reference
    def base(self, phi):
        wf = self.f0 + (phi % 1.0) * self.wn; fl = math.floor(wf)
        self.sc.frame_set(int(fl), subframe=float(wf - fl)); bpy.context.view_layer.update()

    def reference(self):
        ob = self.arm; m = self.m; R.set_action(ob, self.act)
        quats = {b: [] for b in TORSO}; hips = []; headq = []
        hinge = {s + n: [] for s in R.SIDES for n in ('UpLeg', 'Leg', 'Arm')}
        for f in range(self.f0, self.f1):
            self.sc.frame_set(f); bpy.context.view_layer.update()
            for b in TORSO: quats[b].append(np.array(ob.pose.bones[b].rotation_quaternion))
            hips.append(np.array(R.joint(ob, 'Hips'))); headq.append(np.array(world_q(ob, ob.pose.bones['Head'])))
            for s in R.SIDES:
                P = R.joint(ob, s + 'UpLeg'); K = R.joint(ob, s + 'Leg'); A = R.joint(ob, s + 'Foot')
                if math.degrees((K - P).angle(A - K)) > 30:
                    n = (K - P).cross(A - K).normalized()
                    hinge[s + 'UpLeg'].append(np.array(world_q(ob, ob.pose.bones[s + 'UpLeg']).inverted() @ n)); hinge[s + 'Leg'].append(np.array(world_q(ob, ob.pose.bones[s + 'Leg']).inverted() @ n))
                U = R.joint(ob, s + 'Arm'); E = R.joint(ob, s + 'ForeArm'); H = R.joint(ob, s + 'Hand')
                if math.degrees((E - U).angle(H - E)) > 20:
                    hinge[s + 'Arm'].append(np.array(world_q(ob, ob.pose.bones[s + 'Arm']).inverted() @ (E - U).cross(H - E).normalized()))
        def mean_q(arr):
            a = np.array(arr); a[(a @ a[0]) < 0] *= -1; q = a.mean(0); return Quaternion(q / np.linalg.norm(q))
        self.qmean = {b: mean_q(quats[b]) for b in TORSO}; self.head_mean_w = mean_q(headq)
        self.hips_mean = np.array(hips).mean(0); self.hips_rest = self.rig.rest['Hips']['head']
        self.hinge = {}; self.hinge_info = {}
        for k, arr in hinge.items():
            a = np.array(arr); mean = a.mean(0); mean /= np.linalg.norm(mean)
            self.hinge[k] = Vector(mean); self.hinge_info[k] = {'samples': len(arr), 'local_axis': [round(float(x), 3) for x in mean],
                                                             'max_deviation_deg': round(float(np.degrees(np.arccos(np.clip(a @ mean, -1, 1))).max()), 1)}
        self.foot = {}
        for s in R.SIDES:
            self.sc.frame_set(m['walk_ref_frame'][s]); bpy.context.view_layer.update()
            ft = ob.pose.bones[s + 'Foot']; toe = ob.pose.bones[s + 'ToeBase']
            a = R.joint(ob, s + 'Foot'); b = R.joint(ob, s + 'ToeBase'); fwd = Vector((b.x - a.x, b.y - a.y, 0)).normalized()
            # the walk's mid-stance foot can stand a few degrees toes-down: level it about the ball until heel and toe sit as in the rest pose
            idx = self.rig.S[s]['foot']; y0 = self.rig.V0[idx, 1]; heel = y0 > np.percentile(y0, 80); toes = y0 < np.percentile(y0, 20)
            rest_gap = float(self.rig.V0[idx][heel, 2].min() - self.rig.V0[idx][toes, 2].min()); level = 0.0
            def gap():
                W = R.verts_world(self.mesh)[idx]; return float(W[heel, 2].min() - W[toes, 2].min())
            for _ in range(12):
                g = gap() - rest_gap
                if abs(g) < 2e-4: break
                step = -math.degrees(math.atan2(g, float(np.ptp(y0)) * .8)); level += step
                R.rot_about(ob, ft, R.joint(ob, s + 'ToeBase'), rotx(step)); set_world_q(ob, s + 'ToeBase', rotx(-step) @ world_q(ob, toe))     # the toes stay as they stood
            V = R.verts_world(self.mesh)[idx]; drop = float(V[:, 2].min())
            a = R.joint(ob, s + 'Foot') - Vector((0, 0, drop)); b = R.joint(ob, s + 'ToeBase') - Vector((0, 0, drop)); V = V - np.array([0, 0, drop])
            self.foot[s] = {'qflat': world_q(ob, ft), 'qtoe': world_q(ob, toe), 'ankle': a, 'ball': b, 'pole': fwd, 'toe_basis': toe.rotation_quaternion.copy(), 'ref_level_deg': level, 'ref_rest_heel_minus_toe_cm': rest_gap * 100,
                            'ref_sole_min_cm': float(V[:, 2].min() * 100), 'ref_heel_cm': float(V[heel, 2].min() * 100), 'ref_toe_cm': float(V[toes, 2].min() * 100)}
        self.leg = {s: ((R.joint(ob, s + 'Leg') - R.joint(ob, s + 'UpLeg')).length, (R.joint(ob, s + 'Foot') - R.joint(ob, s + 'Leg')).length) for s in R.SIDES}

    # ------------------------------------------------------------------ plan
    def local_phase(self, side, phi):
        """0 = touch-down, d = toe-off, 1 = next touch-down."""
        psi = (phi - self.mid_phase[side] + self.d / 2) % 1.0
        return 0.0 if psi > 1 - 1e-7 else psi

    def stance_ankle(self, side, psi):
        """Ankle, heel pitch and ball while the foot is on the ground: the ball is the fixed point, the heel lifts about it."""
        F = self.foot[side]; fe = self.m['feet']; d = self.d
        ball = Vector((self.x0 + (F['ball'].x - self.x0) * fe['track_scale'], self.y_td[side] + self.v * self.T * psi, F['ball'].z))
        h = fe['heel_off_start'] * d
        pitch = fe['heel_off_pitch'] * (clamp01((psi - h) / max(1e-9, d - h)) ** fe['heel_off_power']) if psi > h else 0.0
        return ball + rotx(pitch) @ (F['ankle'] - F['ball']), pitch, ball

    def foot_plan(self, side, phi):
        d = self.d; psi = self.local_phase(side, phi); fe = self.m['feet']
        if psi <= d + 1e-9:
            ankle, pitch, ball = self.stance_ankle(side, min(psi, d)); return {'stance': True, 'psi': psi, 'ankle': ankle, 'pitch': pitch}
        u = (psi - d) / (1 - d); eps = 1e-4
        a0, _, _ = self.stance_ankle(side, d); a0b, _, _ = self.stance_ankle(side, d - eps); a1, _, _ = self.stance_ankle(side, 0.0)
        # end velocities per unit of swing: leaving as the stance ends, arriving at ground speed and coming DOWN onto the ground
        m0 = (a0 - a0b) / eps * (1 - d); m1 = Vector((0, self.v * self.T, -fe.get('touch_down_drop_speed', 0.0) * self.T)) * (1 - d)
        h00 = 2 * u ** 3 - 3 * u ** 2 + 1; h10 = u ** 3 - 2 * u ** 2 + u; h01 = -2 * u ** 3 + 3 * u ** 2; h11 = u ** 3 - u ** 2
        ankle = a0 * h00 + m0 * h10 + a1 * h01 + m1 * h11     # in place: the foot comes back to the same touch-down spot, already moving back at ground speed
        # two bumps on top of the arc: the heel recovery behind the body (early) and the knee drive in front of it (late)
        ankle.z += fe['swing_lift'] * math.sin(math.pi * u ** fe['swing_lift_skew']) ** 2
        ankle.z += fe['swing_drive'] * math.sin(math.pi * clamp01((u - fe['swing_drive_from']) / (1 - fe['swing_drive_from']))) ** 2
        return {'stance': False, 'psi': psi, 'u': u, 'ankle': ankle}

    def hips_rise(self, phi):
        """Hips height over the base level. hips.shape 'ballistic' (a gait with a flight phase): one step = half a cycle = stance, then
        flight. The flight is a free fall under hips.gravity; the stance dip is the half sine that leaves and meets the flight with
        its vertical speed, so the curve has no kink and the bounce follows from cadence and stance share (no bounce number).
        Otherwise the plain cosine of hips.bounce."""
        h = self.m['hips']; d = self.d
        if h.get('shape') == 'ballistic' and d < .5 - 1e-9:
            chi = (phi - self.mid_phase['Right'] + d / 2) % .5; g = h['gravity']; tf = (.5 - d) * self.T; ts = d * self.T
            if chi > .5 - 1e-9: chi = 0.0
            if chi <= d: return -(g * tf / 2) * ts / math.pi * math.sin(math.pi * chi / d)
            tau = (chi - d) * self.T; return g / 2 * tau * (tf - tau)
        return -h['bounce'] * math.cos(4 * math.pi * (phi - h['bounce_lag']))

    def hips_target(self, phi, z_base):
        h = self.m['hips']
        return Vector((self.x0 - h['sway'] * math.cos(2 * math.pi * phi), self.hips_mean[1] + h['fore_aft'] * math.sin(4 * math.pi * phi), z_base + self.hips_rise(phi)))

    # ------------------------------------------------------------------ pose
    def pose_torso(self, phi, z_base):
        ob = self.arm; m = self.m; self.base(phi)
        s = m['torso_motion_scale']
        if abs(s - 1) > 1e-6:
            for b in TORSO:
                pb = ob.pose.bones[b]; q = pb.rotation_quaternion.copy(); qm = self.qmean[b]
                if q.dot(qm) < 0: q.negate()
                pb.rotation_quaternion = qm @ qpow(qm.inverted() @ q, s)
            bpy.context.view_layer.update()
        hp = ob.pose.bones['Hips']; M = ob.matrix_world @ hp.matrix; M.translation = self.hips_target(phi, z_base)
        hp.matrix = ob.matrix_world.inverted() @ M; bpy.context.view_layer.update()
        R.rot_about(ob, hp, R.joint(ob, 'Hips'), rotx(m['lean']['pelvis']))
        for b in ('Spine02', 'Spine01', 'Spine'): R.rot_about(ob, ob.pose.bones[b], R.joint(ob, b), rotx(m['lean']['spine'] / 3.0))
        # one shoulder carried lower than the other (data, per side): the collar bone turns about the forward axis
        for side, deg in m.get('shoulder_drop', {}).items():
            if deg: R.rot_about(ob, ob.pose.bones[side + 'Shoulder'], R.joint(ob, side + 'Shoulder'), Quaternion(Vector((0, 1, 0)), math.radians(deg if side == 'Left' else -deg)))
        hd = m['head']; want = rotx(-hd['pitch']) @ self.head_mean_w
        k1 = hd['stabilise'] * hd['neck_share']
        delta = want @ world_q(ob, ob.pose.bones['Head']).inverted(); R.rot_about(ob, ob.pose.bones['neck'], R.joint(ob, 'neck'), qpow(delta, k1))
        delta = want @ world_q(ob, ob.pose.bones['Head']).inverted(); R.rot_about(ob, ob.pose.bones['Head'], R.joint(ob, 'Head'), qpow(delta, (hd['stabilise'] - k1) / max(1e-6, 1 - k1)))

    def pose_arms(self, phi):
        ob = self.arm; a = self.m['arms']; d = self.d
        def direction(sw, abd, sx):
            sw = math.radians(sw); abd = math.radians(abd)
            return Vector((sx * math.sin(abd), -math.sin(sw) * math.cos(abd), -math.cos(sw) * math.cos(abd)))
        for side in R.SIDES:
            sx = 1 if side == 'Left' else -1
            p = {**a, **a.get('side', {}).get(side, {})}                                  # arms.side.<Side> overrides the shared pairs (asymmetry is data)
            # the arm leads with the opposite leg: forward-most when the other foot touches down
            peak = self.mid_phase['Right' if side == 'Left' else 'Left'] - d / 2 + p['phase_lead']
            c = math.cos(2 * math.pi * (phi - peak)); cf = math.cos(2 * math.pi * (phi - peak - p['fore_lag']))
            ex = self.extra_abd[side]; gain = p.get('clear_search', {}).get('upper_gain', 0.0)      # opened by the clearance search (per side), see build()
            ut = direction(p['upper_swing'][0] + p['upper_swing'][1] * c, p['upper_abd'][0] + ex * gain + p['upper_abd'][1] * c, sx)
            ftd = direction(p['fore_swing'][0] + p['fore_swing'][1] * cf, p['fore_abd'][0] + ex + p['fore_abd'][1] * cf, sx)
            U = R.joint(ob, side + 'Arm'); E = R.joint(ob, side + 'ForeArm')
            R.rot_about(ob, ob.pose.bones[side + 'Arm'], U, (E - U).rotation_difference(ut))
            n = ut.cross(ftd)
            # the upper arm takes only a share of the roll that would put the elbow hinge on the arm plane: the whole of it wrings the
            # auto-rigged shoulder (cross-section collapse); the forearm's own aim takes up the rest
            if n.length > 1e-4: twist_align(ob, side + 'Arm', ut, self.hinge[side + 'Arm'], n.normalized(), a.get('twist_share', 1.0))
            E = R.joint(ob, side + 'ForeArm'); H = R.joint(ob, side + 'Hand')
            R.rot_about(ob, ob.pose.bones[side + 'ForeArm'], E, (H - E).rotation_difference(ftd))

    def leg_ik(self, side, target, min_bend):
        """Two-bone IK to an ankle target with a knee guard (never straighter than min_bend) and the knee hinge on the leg plane."""
        ob = self.arm; up = ob.pose.bones[side + 'UpLeg']; lo = ob.pose.bones[side + 'Leg']
        P = R.joint(ob, side + 'UpLeg'); K = R.joint(ob, side + 'Leg'); A = R.joint(ob, side + 'Foot'); l1 = (K - P).length; l2 = (A - K).length
        reach = math.sqrt(l1 * l1 + l2 * l2 + 2 * l1 * l2 * math.cos(math.radians(min_bend)))
        v = target - P; dist = v.length; deficit = max(0.0, dist - reach); dc = max(abs(l1 - l2) + 1e-4, min(dist, reach)); e = v.normalized()
        pole = self.foot[side]['pole'] - e * self.foot[side]['pole'].dot(e); pole.normalize()
        ca = max(-1.0, min(1.0, (l1 * l1 + dc * dc - l2 * l2) / (2 * l1 * dc))); sa = math.sqrt(max(0.0, 1 - ca * ca))
        K2 = P + (e * ca + pole * sa) * l1; A2 = P + e * dc
        e1 = (K2 - P).normalized(); e2 = (A2 - K2).normalized(); n = e1.cross(e2).normalized()
        R.rot_about(ob, up, P, (K - P).rotation_difference(K2 - P)); twist_align(ob, side + 'UpLeg', e1, self.hinge[side + 'UpLeg'], n)
        K = R.joint(ob, side + 'Leg'); A = R.joint(ob, side + 'Foot')
        R.rot_about(ob, lo, K, (A - K).rotation_difference(A2 - K)); twist_align(ob, side + 'Leg', e2, self.hinge[side + 'Leg'], n)
        return deficit

    def sole(self, side):
        V = R.verts_world(self.mesh)[self.rig.S[side]['foot']]; return float(V[:, 2].min())

    def pose_leg(self, side, phi, ends=None):
        """ends = the foot / toe basis quaternions of this side's toe-off and touch-down frames (needed for a swing frame)."""
        ob = self.arm; F = self.foot[side]; fe = self.m['feet']; kn = self.m['knee']; plan = self.foot_plan(side, phi)
        ft = ob.pose.bones[side + 'Foot']; toe = ob.pose.bones[side + 'ToeBase']; target = plan['ankle'].copy(); info = {'stance': plan['stance'], 'deficit_cm': 0.0, 'raised_cm': 0.0}
        for it in range(4):
            deficit = self.leg_ik(side, target, kn['stance_min_bend'] if plan['stance'] else kn['min_bend'])
            if plan['stance']:
                set_world_q(ob, side + 'Foot', rotx(plan['pitch']) @ F['qflat']); set_world_q(ob, side + 'ToeBase', F['qtoe'])
            else:
                u = plan['u']; e = smooth(u) ** fe['swing_foot_ease']
                q0, q1 = ends['foot_to'].copy(), ends['foot_td'].copy()
                if q0.dot(q1) < 0: q1.negate()
                ft.rotation_quaternion = q0.slerp(q1, e)
                if fe['swing_toe_up']:      # toes lifted through the middle of the swing so that they clear the ground
                    bpy.context.view_layer.update(); R.rot_about(ob, ft, R.joint(ob, side + 'Foot'), rotx(-fe['swing_toe_up'] * math.sin(math.pi * u) ** 2))
                t0, t1 = ends['toe_to'].copy(), F['toe_basis'].copy()
                if t0.dot(t1) < 0: t1.negate()
                t2 = ends['toe_td'].copy()
                if t1.dot(t2) < 0: t2.negate()
                r = fe['toe_release']
                toe.rotation_quaternion = t0.slerp(t1, smooth(u / r)) if u < 1 - r else t1.slerp(t2, smooth((u - (1 - r)) / r))
                bpy.context.view_layer.update()
            z = self.sole(side); info['deficit_cm'] = round(deficit * 100, 3)
            # stance: the sole sits on the ground (both ways); swing: only ever raised
            if plan['stance'] and abs(z) > 2e-4 and deficit < 1e-6: target.z -= z; info['raised_cm'] = round(info['raised_cm'] - z * 100, 3)
            elif (not plan['stance']) and z < -1e-4: target.z -= z; info['raised_cm'] = round(info['raised_cm'] - z * 100, 3)
            else: break
        info['sole_cm'] = round(self.sole(side) * 100, 3); return info

    def solve_height(self):
        """Highest hips base that keeps every stance frame's knee bent by at least knee.stance_min_bend (minus knee.extra_drop)."""
        ob = self.arm; kn = self.m['knee']; z_ref = float(self.hips_mean[2]); best = 1e9; rows = []
        hipy = []
        for k in range(self.N):
            self.pose_torso(k / self.N, z_ref); hipy.append([R.joint(ob, s + 'UpLeg').y for s in R.SIDES])
        hipy = np.array(hipy); self.hip_y_mean = {s: float(hipy[:, i].mean()) for i, s in enumerate(R.SIDES)}
        fe = self.m['feet']; ab = {s: (self.foot[s]['ankle'] - self.foot[s]['ball']) for s in R.SIDES}
        # stance centre of the BALL: so far behind the hip joint (mean over the cycle) as the data says
        self.y_td = {s: self.hip_y_mean[s] + fe['stance_centre_behind_hip'] - self.v * self.T * self.d / 2 for s in R.SIDES}
        for k in range(self.N):
            phi = k / self.N; self.pose_torso(phi, z_ref)
            for s in R.SIDES:
                plan = self.foot_plan(s, phi)
                if not plan['stance']: continue
                P = R.joint(ob, s + 'UpLeg'); A = plan['ankle']; l1, l2 = self.leg[s]
                reach = math.sqrt(l1 * l1 + l2 * l2 + 2 * l1 * l2 * math.cos(math.radians(kn['stance_min_bend'])))
                hz = math.hypot(P.x - A.x, P.y - A.y)
                if hz >= reach: raise SystemExit('refused: %s stance frame %d of the %s foot is %.1f cm beyond reach horizontally - shorten the stance or move its centre' % (self.mid, k, s, (hz - reach) * 100))
                z_max = A.z + math.sqrt(reach * reach - hz * hz) - (P.z - z_ref)
                rows.append({'frame': k, 'side': s, 'psi': round(plan['psi'], 4), 'horizontal_cm': round(hz * 100, 1), 'hips_base_max_cm': round(z_max * 100, 2)})
                best = min(best, z_max)
        self.z_base = best - kn['extra_drop']
        return {'walk_hips_mean_cm': round(z_ref * 100, 2), 'hips_base_cm': round(self.z_base * 100, 2), 'limiting': min(rows, key=lambda r: r['hips_base_max_cm']), 'rows': rows,
                'hip_y_mean_cm': {s: round(v * 100, 2) for s, v in self.hip_y_mean.items()}, 'ball_touch_down_y_cm': {s: round(v * 100, 2) for s, v in self.y_td.items()}}

    def pose_frame(self, k, ends):
        phi = k / self.N; self.pose_torso(phi, self.z_base); self.pose_arms(phi)
        return {s: self.pose_leg(s, phi, ends.get(s)) for s in R.SIDES}

    def capture(self):
        ob = self.arm
        return {'Hips.loc': tuple(ob.pose.bones['Hips'].location), **{b: tuple(ob.pose.bones[b].rotation_quaternion) for b in self.bones}}

    def build_poses(self):
        ob = self.arm; N = self.N
        self.x0 = float(self.hips_rest[0]); height = self.solve_height()
        # stance-end frames first: their foot / toe rotations are the ends of the swing interpolation
        ends = {}
        for s in R.SIDES:
            k_td = int(round((self.mid_phase[s] - self.d / 2) * N)) % N; k_to = int(round((self.mid_phase[s] + self.d / 2) * N)) % N
            e = {}
            for key, k in (('td', k_td), ('to', k_to)):
                self.pose_torso(k / N, self.z_base); self.pose_leg(s, k / N)
                e['foot_' + key] = ob.pose.bones[s + 'Foot'].rotation_quaternion.copy(); e['toe_' + key] = ob.pose.bones[s + 'ToeBase'].rotation_quaternion.copy()
            e['k_td'] = k_td; e['k_to'] = k_to; ends[s] = e
        vals = {}; infos = {}; posed = []; self.posed_verts = []
        for k in range(N):
            infos[k] = self.pose_frame(k, ends); vals[k] = self.capture()
            fr = self.rig.frame(True); fr['f'] = k; self.posed_verts.append(fr.pop('_V')); posed.append(fr)
        vals[N] = dict(vals[0])
        return vals, infos, height, {s: {'touch_down_frame': ends[s]['k_td'], 'toe_off_frame': ends[s]['k_to']} for s in R.SIDES}, R.pen_summary(posed)


def write_keys(b, vals):
    """Keys of the run on frames f0 .. f0+N of the walk's action (all bone rotations + Hips location), everything later removed."""
    act = b.act; fcs = R.fcurves_of(act); f0 = b.f0; N = b.N; written = 0
    def write(path, count, key):
        nonlocal written
        prev = None; seq = {}
        for k in range(N + 1):
            v = np.array(vals[k][key], dtype=np.float64)
            if count == 4 and prev is not None and float(v @ prev) < 0: v = -v
            seq[f0 + k] = v; prev = v
        for i in range(count):
            fc = fcs.find(path, index=i)
            if fc is None: raise RuntimeError('no F-curve ' + path + '[%d]' % i)
            have = {int(round(kp.co.x)) for kp in fc.keyframe_points}
            for f in seq:
                if f not in have: fc.keyframe_points.insert(f, float(seq[f][i]))
            for kp in fc.keyframe_points:
                f = int(round(kp.co.x))
                if f in seq: kp.co.y = float(seq[f][i]); kp.interpolation = 'LINEAR'; written += 1
            fc.update()
    write('pose.bones["Hips"].location', 3, 'Hips.loc')
    for name in b.bones: write('pose.bones["%s"].rotation_quaternion' % name, 4, name)
    removed = 0
    for fc in fcs:
        for kp in reversed(list(fc.keyframe_points)):
            if kp.co.x > f0 + N + 1e-6: fc.keyframe_points.remove(kp); removed += 1
        fc.update()
    act.name = CFG['take']
    return written, removed


def sample(b, act, frames=None):
    """rig308.sample_clip plus what a run needs on top: torso lean, head attitude, ball of the foot, contact by height."""
    ob = b.arm; rig = b.rig; R.set_action(ob, act); sc = bpy.context.scene; out = []
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    for f in (frames if frames is not None else range(f0, f1 + 1)):
        fl = math.floor(f); sc.frame_set(int(fl), subframe=float(f - fl)); bpy.context.view_layer.update()
        fr = rig.frame(True); fr['f'] = f
        hips = R.joint(ob, 'Hips'); chest = R.joint(ob, 'Spine'); look = world_q(ob, ob.pose.bones['headfront']) @ Vector((0, 1, 0))
        fr['lean_deg'] = math.degrees(math.atan2(-(chest.y - hips.y), chest.z - hips.z)); fr['head_pitch_deg'] = math.degrees(math.asin(max(-1, min(1, look.z))))
        fr['head_z'] = R.joint(ob, 'Head').z
        for s in R.SIDES:
            fr[s]['ball'] = [round(x, 5) for x in R.joint(ob, s + 'ToeBase')]
            th = R.joint(ob, s + 'Leg') - R.joint(ob, s + 'UpLeg'); fr[s]['thigh_flex_deg'] = math.degrees(math.atan2(-th.y, -th.z))      # thigh forward of straight down, side view
            fr[s]['shoulder_z'] = R.joint(ob, s + 'Arm').z; fr[s]['hand_z'] = R.joint(ob, s + 'Hand').z; fr[s]['hand_x'] = R.joint(ob, s + 'Hand').x
            V = fr['_V'][rig.S[s]['foot']]
            fr[s]['heel_z'] = float(V[rig.heel_mask[s], 2].min()); fr[s]['toe_z'] = float(V[rig.toe_mask[s], 2].min())
        out.append(fr)
    return out


def run_summary(b, fr):
    """Numbers of the built run: rig308's gait / arm / penetration / knee summaries (same definitions as the walk tables) and the
    run-only ones (contact by height, flight, lean, bounce, head, loop seam)."""
    N = b.N; fps = b.fps; dt = 1.0 / fps; lim = CFG['limits']; F = fr[:-1]; eps = lim['contact_eps_cm'] / 100.0
    out = {'arms': R.arm_summary(F), 'pen': R.pen_summary(F), 'gait': R.gait_summary(fr, fps, [b.v], speeds=b.m['speeds']), 'knee': R.knee_summary(fr)}
    con = {s: np.array([f[s]['foot_minz'] <= eps for f in F]) for s in R.SIDES}
    ball_v = {}
    for s in R.SIDES:
        by = np.array([f[s]['ball'][1] for f in fr]); v = (by[1:] - by[:-1]) / dt; m = con[s] & np.roll(con[s], -1)     # intervals with the foot down at both ends
        ball_v[s] = {'contact_frames': int(con[s].sum()), 'contact_list': [int(i) for i in np.where(con[s])[0]], 'contact_pct': round(float(con[s].mean() * 100), 1),
                     'ball_speed_mean': round(float(v[m].mean()), 4) if m.any() else None, 'ball_speed_min': round(float(v[m].min()), 4) if m.any() else None, 'ball_speed_max': round(float(v[m].max()), 4) if m.any() else None,
                     'ball_slide_cm_per_step': round(float(np.abs(v[m] - b.v).sum() * dt * 100), 3) if m.any() else None,
                     'sole_min_cm_cycle': round(min(f[s]['foot_minz'] for f in F) * 100, 3), 'sole_max_cm_cycle': round(max(f[s]['foot_minz'] for f in F) * 100, 2),
                     'sole_mean_cm_contact': round(float(np.mean([F[i][s]['foot_minz'] for i in np.where(con[s])[0]])) * 100, 3) if con[s].any() else None,
                     'heel_max_cm_contact': round(max(F[i][s]['heel_z'] for i in np.where(con[s])[0]) * 100, 2) if con[s].any() else None,
                     'toe_max_cm_contact': round(max(F[i][s]['toe_z'] for i in np.where(con[s])[0]) * 100, 2) if con[s].any() else None,
                     'ankle_lift_cm': round((max(f[s]['ankle'][2] for f in F) - min(f[s]['ankle'][2] for f in F)) * 100, 1),
                     'knee_min_contact_deg': round(min(F[i][s]['knee_bend'] for i in np.where(con[s])[0]), 1) if con[s].any() else None,
                     'knee_max_deg': round(max(f[s]['knee_bend'] for f in F), 1), 'knee_min_deg': round(min(f[s]['knee_bend'] for f in F), 1)}
    st = R.stance_masks(fr, fps); gated = []
    for s in R.SIDES:
        m = con[s] & np.roll(con[s], -1); vy = st[s]['vel'][:, 1]
        ball_v[s]['sole_speed_contact_mean'] = round(float(vy[m].mean()), 4) if m.any() else None; ball_v[s]['sole_speed_contact_min'] = round(float(vy[m].min()), 4) if m.any() else None
        ball_v[s]['sole_speed_contact_max'] = round(float(vy[m].max()), 4) if m.any() else None; ball_v[s]['plateau_intervals'] = int(st[s]['mask'].sum()); ball_v[s]['contact_intervals'] = int(m.sum())
        if m.any(): gated.append(vy[m])
    out['stance_speed_contact_m_s'] = round(float(np.concatenate(gated).mean()), 4) if gated else None
    flight = ~con['Left'] & ~con['Right']; both = con['Left'] & con['Right']
    hz = np.array([f['hips'][2] for f in F]); head = np.array([f['head_z'] for f in F]); lean = np.array([f['lean_deg'] for f in F]); hp = np.array([f['head_pitch_deg'] for f in F])
    hx = np.array([f['hips'][0] for f in F]); hy = np.array([f['hips'][1] for f in F])
    acc = (np.roll(hz, -1) - 2 * hz + np.roll(hz, 1)) / (dt * dt)                                  # vertical acceleration of the hips, cyclic second difference
    for s in R.SIDES: ball_v[s]['thigh_flex_max_deg'] = round(max(f[s]['thigh_flex_deg'] for f in F), 1)
    gap = np.array([math.dist(f['Left']['wrist'], f['Right']['wrist']) for f in F]) if 'wrist' in F[0]['Left'] else None
    out['shape'] = {'hips_accel_flight_mean_m_s2': round(float(acc[flight].mean()), 2) if flight.any() else None, 'hips_accel_stance_max_m_s2': round(float(acc.max()), 2),
                    'hips_lowest_frame': int(np.argmin(hz)), 'hips_highest_frame': int(np.argmax(hz)),
                    'shoulder_height_left_minus_right_cm': round(float(np.mean([f['Left']['shoulder_z'] - f['Right']['shoulder_z'] for f in F]) * 100), 2),
                    'hand_height_left_minus_right_cm': [round(float(x * 100), 1) for x in (min(f['Left']['hand_z'] - f['Right']['hand_z'] for f in F), max(f['Left']['hand_z'] - f['Right']['hand_z'] for f in F))],
                    'hand_to_hand_cm': [round(float(gap.min() * 100), 1), round(float(gap.max() * 100), 1)] if gap is not None else None}
    out['run'] = {'cycle_frames': N, 'seconds': round(N * dt, 4), 'clip_speed_design': b.v, 'stride_m_design': round(b.v * N * dt, 4), 'steps_per_minute_at_clip_rate': round(120.0 / (N * dt), 1),
                  'stance_share_design': round(b.d, 4), 'flight_share_design': round(1 - 2 * b.d, 4), 'flight_frames_by_height': int(flight.sum()), 'flight_list': [int(i) for i in np.where(flight)[0]],
                  'double_support_frames_by_height': int(both.sum()), 'contact_eps_cm': lim['contact_eps_cm'], 'feet': ball_v,
                  'hips_height_cm': [round(float(hz.min() * 100), 2), round(float(hz.max() * 100), 2)], 'bounce_cm': round(float((hz.max() - hz.min()) * 100), 2),
                  'hips_sway_cm': round(float((hx.max() - hx.min()) * 100), 2), 'hips_fore_aft_cm': round(float((hy.max() - hy.min()) * 100), 2),
                  'head_height_range_cm': round(float((head.max() - head.min()) * 100), 2), 'lean_deg': [round(float(lean.min()), 1), round(float(lean.mean()), 1), round(float(lean.max()), 1)],
                  'head_pitch_deg': [round(float(hp.min()), 1), round(float(hp.mean()), 1), round(float(hp.max()), 1)],
                  'cadence_by_actor_speed': {('%.2f' % s): {'playback_rate': round(s / b.v, 3), 'steps_per_minute': round(120.0 * (s / b.v) / (N * dt), 1)} for s in b.m['speeds']}}
    names = list(fr[0]['_q'].keys())
    def step(i, j, n): return fold(math.degrees(fr[i]['_q'][n].rotation_difference(fr[j]['_q'][n]).angle))
    close = max(step(0, N, n) for n in names)
    steps = np.array([[step(i, i + 1, n) for i in range(N)] for n in names])                       # bones x intervals (the last interval ends on the copy of frame 0)
    d2 = np.abs(np.roll(steps, -1, axis=1) - steps)                                                # change of step size from one interval to the next, cyclic
    seam = d2[:, -1]; inner = d2[:, :-1]; bone = int(np.argmax(seam))
    out['loop'] = {'first_last_pose_deg': round(float(close), 5), 'hips_first_last_cm': round(float(np.linalg.norm(np.array(fr[0]['hips']) - np.array(fr[N]['hips'])) * 100), 5),
                   'seam_step_change_max_deg': round(float(seam.max()), 2), 'seam_bone': names[bone], 'inner_step_change_max_deg': round(float(inner.max()), 2),
                   'inner_step_change_of_seam_bone_max_deg': round(float(inner[bone].max()), 2), 'max_step_deg': round(float(steps.max()), 2), 'max_step_bone': names[int(np.argmax(steps.max(1)))],
                   'note': 'seam = the interval that closes the loop; a seam value no larger than the inner ones means the velocity is continuous across the loop'}
    return out


def build(mid):
    b = Builder(mid); m = b.m; rig = b.rig; ob = b.arm; sc = b.sc; lim = CFG['limits']
    # heel / toe thirds of the foot vertices (for the "is the stance foot flat" numbers), from the rest pose
    rig.heel_mask = {}; rig.toe_mask = {}
    for s in R.SIDES:
        y = rig.V0[rig.S[s]['foot'], 1]; rig.heel_mask[s] = y > np.percentile(y, 80); rig.toe_mask[s] = y < np.percentile(y, 20)
    fix = json.load(open(REPO + '/' + CFG['walk_numbers'] + '/%s_fix.json' % mid, encoding='utf-8'))
    v_walk = fix['after']['gait']['stance_speed_m_s']
    out = {'schema': '308.enemyrun.build.1', 'id': mid, 'status': 'IMPLEMENTED (offline): Blender numbers [O]; nothing imported into Unity', 'source': CFG['walk_stage'] + '/' + m['walk'], 'source_sha256': m['walk_sha256'],
           'config_sha256': sha(os.path.join(HERE, 'enemyrun308.json')), 'rig308_sha256': sha(os.path.join(RIGDIR, 'rig308.py')), 'parameters': m,
           'reference': {'hinge_axes_from_walk': b.hinge_info, 'walk_stance_speed_m_s': v_walk, 'leg_lengths_cm': {s: [round(x * 100, 2) for x in b.leg[s]] for s in R.SIDES},
                         'flat_foot_reference': {s: {k: round(v, 3) for k, v in b.foot[s].items() if k.startswith('ref_')} for s in R.SIDES}}}
    print('RUN', mid, 'hinges', b.hinge_info, flush=True)
    # ---- the walk as it would have to play at each actor speed (sheet rows + numbers), sampled before its keys are overwritten
    walk_src_name = b.act.name; tiles = CFG['sheet']['tiles']; every = CFG['sheet']['tile_every_frames']
    wfr = sample(b, b.act); wsum = run_summary_walk(b, wfr, v_walk)
    rate = b.v / v_walk; walk_all = []
    for i in range(max(b.N, tiles * every) + 1):                 # the walk at the rate it needs for the clip speed, one pose per run frame time
        wf = b.f0 + ((i * rate) % b.wn); fl = math.floor(wf); sc.frame_set(int(fl), subframe=float(wf - fl)); bpy.context.view_layer.update(); walk_all.append(R.verts_world(b.mesh).astype(np.float32))
    walk_tiles = [walk_all[i * every] for i in range(tiles)]
    out['walk_at_run_speed'] = wsum
    # ---- poses
    # arms: the smallest extra spread (forearm, and a share of it on the upper arm) that keeps hand and forearm out of the body with
    # the clearance the data asks for - the rule of the walk repair's abduction search, here upward from the designed pose
    cs = m['arms'].get('clear_search'); tried = []
    while True:
        vals, infos, height, contacts, pen = b.build_poses()
        # each side on its own (an arm that is clear keeps its designed pose), and by both tests: rig308's (weights >= 0.9, clearance
        # >= clear_cm) and the stricter one of inside308.py (no vertex inside by winding number, the elbow ring >= ring_clear_cm off the
        # body). The stricter one is slow, so it runs only on a pose that already passes rig308's on both sides.
        easy = {s: pen[s]['fore_hand_max_cm'] <= lim['pen_fore_hand_cm'] and pen[s]['upper_max_cm'] <= lim['pen_upper_cm'] and (pen[s]['fore_hand_clear_cm'] is None or cs is None or pen[s]['fore_hand_clear_cm'] >= cs['clear_cm']) for s in R.SIDES}
        strict = INS.fold([b.inside.measure(V) for V in b.posed_verts]) if all(easy.values()) or cs is None else None
        good = {s: easy[s] and strict is not None and strict[s]['inside_vertices_max'] == 0 and (strict[s]['ring_clear_min_cm'] is None or cs is None or strict[s]['ring_clear_min_cm'] >= cs['ring_clear_cm']) for s in R.SIDES}
        row = {'extra_abd_deg': dict(b.extra_abd), 'fore_hand_max_cm': {s: pen[s]['fore_hand_max_cm'] for s in R.SIDES}, 'upper_max_cm': {s: pen[s]['upper_max_cm'] for s in R.SIDES},
               'fore_hand_clear_cm': {s: pen[s]['fore_hand_clear_cm'] for s in R.SIDES}, 'where': {s: pen[s]['where'] for s in R.SIDES}, 'strict': strict if strict is not None else 'not run (rig308 test not passed yet)'}
        tried.append(row); print('RUN', mid, 'arm clearance try', row, flush=True)
        ok = all(good.values()); fail = [s for s in R.SIDES if not (easy[s] if strict is None else good[s])]
        todo = [s for s in fail if cs is not None and b.extra_abd[s] + cs['step_deg'] <= cs['max_extra_deg'] + 1e-9]
        if cs is None or ok or not todo: break
        for s in todo: b.extra_abd[s] += cs['step_deg']
    out['arm_clearance_search'] = {'rule': cs, 'tried': tried, 'chosen_extra_abd_deg': dict(b.extra_abd), 'met': bool(ok), 'met_by_side': {s: bool(v) for s, v in good.items()}}
    out['height_solve'] = {k: v for k, v in height.items() if k != 'rows'}; out['height_solve']['stance_rows'] = height['rows']; out['contacts_design'] = contacts
    out['frames'] = {str(k): infos[k] for k in infos}
    worst = max(max(infos[k][s]['deficit_cm'] for s in R.SIDES) for k in infos)
    print('RUN', mid, 'hips base %.2f cm (walk mean %.2f), limiting %s, worst reach deficit %.2f cm' % (height['hips_base_cm'], height['walk_hips_mean_cm'], height['limiting'], worst), flush=True)
    written, removed = write_keys(b, vals)
    # ---- measure
    fr = sample(b, b.act); summ = run_summary(b, fr); out.update(summ)
    st_rows = [b.inside.measure(f['_V'], True) for f in fr[:-1]]
    sc.frame_set(b.f0); bpy.context.view_layer.update()                # the control points of the self-test belong to the first frame's pose
    st_test, st_ok = b.inside.self_test(fr[0]['_V'], {n: np.array(R.joint(ob, n)) for n in ('Spine02', 'Spine01', 'Spine', 'Hips')})
    out['inside_strict'] = {'rule': 'winding number > %s of a forearm / hand vertex (weight >= %s) against everything but that arm; ring = arm vertices outside the rig308 clearance set' % (CFG['recheck']['winding_inside'], CFG['recheck']['arm_weight_min']),
                            'self_test_winding': st_test, 'self_test_ok': st_ok, **INS.fold(st_rows)}
    print('RUN', mid, 'strict', out['inside_strict'], flush=True)
    out['checks'] = {'keys_written': written, 'keys_removed_after_cycle': removed, 'fps': b.fps, 'frames': [b.f0, b.f0 + b.N], 'source_action': walk_src_name,
                                                                                    'worst_reach_deficit_cm': worst}
    g = summ['gait']; rn = summ['run']; v_meas = summ['stance_speed_contact_m_s']      # sole speed while the sole is down (a run has a flight phase: the walk's speed-plateau rule alone takes in the last airborne interval)
    out['against_limits'] = {
        'stance_speed_m_s': v_meas, 'stance_speed_plateau_rule_m_s': g['stance_speed_m_s'], 'slide_ratio_at_clip_speed': round(abs(1 - v_meas / b.v), 4),
        'slide_per_step_cm': max(rn['feet'][s]['ball_slide_cm_per_step'] or 0 for s in R.SIDES), 'sole_min_cm': min(rn['feet'][s]['sole_min_cm_cycle'] for s in R.SIDES),
        'stance_sole_mean_cm': max(rn['feet'][s]['sole_mean_cm_contact'] or 0 for s in R.SIDES), 'pen_fore_hand_cm': max(summ['pen'][s]['fore_hand_max_cm'] for s in R.SIDES),
        'pen_upper_cm': max(summ['pen'][s]['upper_max_cm'] for s in R.SIDES), 'knee_min_contact_deg': min(rn['feet'][s]['knee_min_contact_deg'] or 0 for s in R.SIDES), 'loop_deg': summ['loop']['first_last_pose_deg']}
    a = out['against_limits']
    a['pass'] = {'slide': a['slide_ratio_at_clip_speed'] <= lim['slide_ratio'] and a['slide_per_step_cm'] <= lim['slide_per_step_cm'], 'sole': a['sole_min_cm'] >= lim['sole_min_cm'] and a['stance_sole_mean_cm'] <= lim['stance_sole_mean_max_cm'],
                 'penetration': a['pen_fore_hand_cm'] <= lim['pen_fore_hand_cm'] and a['pen_upper_cm'] <= lim['pen_upper_cm'],
                 'inside_strict': bool(out['inside_strict']['self_test_ok'] and all(out['inside_strict'][s]['inside_vertices_max'] == 0 for s in R.SIDES)), 'knee_guard': a['knee_min_contact_deg'] >= m['knee']['stance_min_bend'] - .5, 'loop': a['loop_deg'] <= lim['loop_deg']}
    print('RUN', mid, 'limits', a, flush=True)
    print('RUN', mid, 'run', {k: v for k, v in rn.items() if k != 'feet'}, flush=True)
    for s in R.SIDES: print('RUN', mid, s, rn['feet'][s], flush=True)
    print('RUN', mid, 'shape', summ['shape'], flush=True)
    print('RUN', mid, 'knee', {s: (summ['knee'][s]['min'], summ['knee'][s]['max'], summ['knee'][s]['max_step_deg'], summ['knee'][s]['max_second_difference_deg']) for s in R.SIDES}, 'loop', summ['loop'], flush=True)
    print('RUN', mid, 'arms', {s: {k: summ['arms'][s][k] for k in ('abduction_mean', 'swing_min', 'swing_max', 'elbow_min', 'elbow_max', 'wrist_fore_aft_cm', 'twist_upper_min', 'twist_upper_max', 'shoulder_area_min_pct', 'elbow_area_min_pct', 'tri_stretch_max', 'tri_collapse_max', 'hyperextension_frames')} for s in R.SIDES}, flush=True)
    print('RUN', mid, 'pen', {s: {k: summ['pen'][s][k] for k in ('fore_hand_max_cm', 'upper_max_cm', 'fore_hand_clear_cm', 'where')} for s in R.SIDES}, flush=True)
    out['ref_run'] = {'frames': [f['f'] - 1 for f in fr], **{s: {'abduction': [round(f[s]['abduction'], 2) for f in fr], 'swing': [round(f[s]['swing'], 2) for f in fr], 'elbow': [round(f[s]['elbow_angle'], 2) for f in fr],
                                                              'knee': [round(f[s]['knee_bend'], 2) for f in fr], 'sole_cm': [round(f[s]['foot_minz'] * 100, 2) for f in fr]} for s in R.SIDES},
                      'hips_cm': [[round(x * 100, 2) for x in f['hips']] for f in fr]}
    run_tiles = [fr[(i * every) % b.N]['_V'].astype(np.float32) for i in range(tiles)]
    np.savez_compressed(WORK + '/%s_run.npz' % mid, tris=rig.tris.astype(np.int32), labels=rig.part_labels(), run=np.array(run_tiles), walk=np.array(walk_tiles), all_run=np.array([f['_V'] for f in fr[:-1]], dtype=np.float32),
                        all_walk=np.array(walk_all, dtype=np.float32),
                        run_frames=np.array([(i * every) % b.N for i in range(tiles)]), walk_frames=np.array([((i * every * rate) % b.wn) for i in range(tiles)]), tile_seconds=np.array([i * every / b.fps for i in range(tiles)]),
                        clip_speed=b.v, walk_rate=rate, height=rig.height)
    if '--no-export' in ARGS:
        json.dump(out, open(OUT + '/%s_run.json' % mid, 'w', encoding='utf-8'), indent=1, ensure_ascii=False); print('RUN DONE (no export)', mid); return
    # ---- export: the structure of the repaired walk file (armature + skinned mesh, cm, Y up, Armature -90 X), one take, frames 0..N
    act = b.act; fcs = R.fcurves_of(act); R.set_action(ob, act); session = {}
    for f in range(b.f0, b.f0 + b.N + 1):
        sc.frame_set(f); bpy.context.view_layer.update(); session[f] = {pb.name: np.array(pb.matrix) for pb in ob.pose.bones}
    for fc in fcs:
        for kp in fc.keyframe_points: kp.co.x -= 1; kp.handle_left.x -= 1; kp.handle_right.x -= 1
        fc.update()
    for a2 in list(bpy.data.actions):
        if a2 != act: bpy.data.actions.remove(a2)
    sc.frame_start = b.f0 - 1; sc.frame_end = b.f0 + b.N - 1; sc.frame_set(b.f0 - 1)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); b.mesh.select_set(True); bpy.context.view_layer.objects.active = ob
    target = REPO + '/' + CFG['stage'] + '/' + m['run']; os.makedirs(os.path.dirname(target), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=False,
                             add_leaf_bones=False, use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X',
                             path_mode='STRIP', embed_textures=False, mesh_smooth_type='OFF')
    out['export'] = {'file': CFG['stage'] + '/' + m['run'], 'sha256': sha(target), 'bytes': os.path.getsize(target), 'with_mesh': True, 'header': fbx_header(target), 'walk_header': fbx_header(b.src)}
    # ---- round trip: read the exported file back; rest pose, bone names, frame count and every frame's pose (the Hips track = the root curve)
    arm2, mesh2 = R.load(target); act2 = bpy.data.actions[0]; sc = bpy.context.scene
    rest_rot = 0.0; rest_pos = 0.0
    for bone in arm2.data.bones:
        A0 = Matrix(b.rest0[bone.name]); B0 = bone.matrix_local
        rest_rot = max(rest_rot, fold(math.degrees(A0.to_3x3().normalized().to_quaternion().rotation_difference(B0.to_3x3().normalized().to_quaternion()).angle)))
        rest_pos = max(rest_pos, (A0.translation - B0.translation).length)
    R.set_action(arm2, act2); g0, g1 = int(round(act2.frame_range[0])), int(round(act2.frame_range[1])); rt_rot = 0.0; rt_pos = 0.0; root_pos = 0.0; root = []
    for f in range(g0, g1 + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        if f not in session: continue
        for pb in arm2.pose.bones:
            A0 = Matrix(session[f][pb.name]); B0 = pb.matrix
            rt_rot = max(rt_rot, fold(math.degrees(A0.to_3x3().normalized().to_quaternion().rotation_difference(B0.to_3x3().normalized().to_quaternion()).angle)))
            rt_pos = max(rt_pos, (A0.translation - B0.translation).length)
            if pb.name == 'Hips': root_pos = max(root_pos, (A0.translation - B0.translation).length); root.append(R.joint(arm2, 'Hips'))
    drift = (root[-1] - root[0]).length * 100 if root else None
    hdr = out['export']['header']; whdr = out['export']['walk_header']
    out['roundtrip'] = {'action': act2.name, 'frames': [g0, g1], 'fps': sc.render.fps, 'armature_scale': [round(x, 6) for x in arm2.scale], 'source_armature_scale': b.arm_scale0,
                        'bones': len(arm2.data.bones), 'bone_names_equal': sorted(bn.name for bn in arm2.data.bones) == sorted(b.rest0.keys()),
                        'rest_max_rotation_deg': round(rest_rot, 5), 'rest_max_position_cm': round(rest_pos, 5), 'pose_max_rotation_deg': round(rt_rot, 5), 'pose_max_position_cm': round(rt_pos, 5),
                        'root_curve_max_position_cm': round(root_pos, 5), 'root_first_last_cm': round(drift, 5) if drift is not None else None,
                        'header_equal_to_walk': hdr.get('global') == whdr.get('global') and hdr.get('model_count') == whdr.get('model_count') and hdr.get('version') == whdr.get('version') and close_model(hdr['models'].get('Armature'), whdr['models'].get('Armature')),
                        'take': [s['name'] for s in hdr['stacks']]}
    rt = out['roundtrip']
    rt['pass'] = bool(rest_rot < .05 and rest_pos < .01 and rt_rot < .1 and rt_pos < .02 and [g0, g1] == [b.f0, b.f0 + b.N] and sc.render.fps == b.fps and rt['bone_names_equal'] and rt['header_equal_to_walk']
                      and rt['take'] == ['Armature|' + CFG['take']] and drift is not None and drift < .01)
    json.dump(out, open(OUT + '/%s_run.json' % mid, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print('RUN DONE', mid, 'roundtrip', out['roundtrip'], 'file', out['export']['bytes'], out['export']['sha256'][:12])


def close_model(a, b, tol=1e-4):
    """Armature node of two FBX headers: same translation / rotation / scaling within a float round trip."""
    if a is None or b is None: return False
    return all(x is not None and y is not None and len(x) == len(y) and all(abs(p - q) <= tol for p, q in zip(x, y)) for x, y in ((a.get(k), b.get(k)) for k in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling')))


def run_summary_walk(b, wfr, v_walk):
    """The repaired walk seen the way the run is: what it does when it has to carry each actor speed (playback rate, cadence), and
    its lean / bounce / knee numbers for the comparison table."""
    fps = b.fps; n = len(wfr) - 1; T = n / fps; F = wfr[:-1]
    hz = np.array([f['hips'][2] for f in F]); lean = np.array([f['lean_deg'] for f in F]); hp = np.array([f['head_pitch_deg'] for f in F]); head = np.array([f['head_z'] for f in F])
    arms = R.arm_summary(F); knee = R.knee_summary(wfr); eps = CFG['limits']['contact_eps_cm'] / 100.0
    con = {s: np.array([f[s]['foot_minz'] <= eps for f in F]) for s in R.SIDES}
    return {'stance_speed_m_s': v_walk, 'cycle_frames': n, 'stride_m': round(v_walk * T, 4),
            'by_actor_speed': {('%.2f' % s): {'playback_rate': round(s / v_walk, 3), 'steps_per_minute': round(120.0 * (s / v_walk) / T, 1)} for s in b.m['speeds']},
            'bounce_cm': round(float((hz.max() - hz.min()) * 100), 2), 'lean_deg': [round(float(lean.min()), 1), round(float(lean.mean()), 1), round(float(lean.max()), 1)],
            'head_pitch_deg': [round(float(hp.min()), 1), round(float(hp.mean()), 1), round(float(hp.max()), 1)], 'head_height_range_cm': round(float((head.max() - head.min()) * 100), 2),
            'flight_frames_by_height': int((~con['Left'] & ~con['Right']).sum()), 'contact_pct': {s: round(float(con[s].mean() * 100), 1) for s in R.SIDES},
            'ankle_lift_cm': {s: round((max(f[s]['ankle'][2] for f in F) - min(f[s]['ankle'][2] for f in F)) * 100, 1) for s in R.SIDES},
            'knee': {s: [knee[s]['min'], knee[s]['max']] for s in R.SIDES},
            'arms': {s: {k: arms[s][k] for k in ('abduction_mean', 'swing_min', 'swing_max', 'elbow_min', 'elbow_max', 'wrist_fore_aft_cm')} for s in R.SIDES}}


cmd, mid = ARGS[0], ARGS[1]
if mid not in CFG['monsters']: raise SystemExit('unknown id ' + mid)
{'build': build}[cmd](mid)
