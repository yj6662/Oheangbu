"""#308 enemy rig verification (SPEC-ENEMY-RIG-VERIFY-308): shared Blender-side measures for the Meshy 24-bone humanoids
(dokkaebi / agwi / changgui). Headless, CPU only, no render. Same definitions as Art/Characters308/DokkaebiVerify/work/analyze.py
(that file is the dokkaebi-only first pass; this module is the same measures, parametrised and trimmed).
Blender axes: +Z up, character faces -Y, character LEFT = +X. Lengths in metres unless a key says cm."""
import bpy, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

UP = Vector((0, 0, 1)); FWD = Vector((0, -1, 0))
ARM = ['Shoulder', 'Arm', 'ForeArm', 'Hand']
LEG = ['UpLeg', 'Leg', 'Foot', 'ToeBase']
SIDES = ('Left', 'Right')


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False, use_anim=True, ignore_leaf_bones=False)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next((o for o in bpy.data.objects if o.type == 'MESH'), None)
    return arm, mesh


def set_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        if act is not None and len(act.slots): ad.action_slot = act.slots[0]
    except Exception as ex:
        print('slot', ex)


def fcurves_of(act):
    """Blender 5 keeps F-curves in the slot's channelbag; older builds expose Action.fcurves."""
    try:
        return act.layers[0].strips[0].channelbag(act.slots[0]).fcurves
    except Exception:
        return act.fcurves


def verts_world(mesh):
    dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float64); me.vertices.foreach_get('co', co); ev.to_mesh_clear()
    co = co.reshape(-1, 3); M = np.array(mesh.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def norm(v): return v / (np.linalg.norm(v) + 1e-12)
def ang(a, b): return math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(norm(a), norm(b)))))))


def hull_area(p2):
    pts = sorted(set((round(float(x), 6), round(float(y), 6)) for x, y in p2))
    if len(pts) < 3: return 0.0
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo = []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    up = []
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    h = lo[:-1] + up[:-1]
    return abs(sum(h[i][0] * h[(i + 1) % len(h)][1] - h[(i + 1) % len(h)][0] * h[i][1] for i in range(len(h)))) / 2


def plane_basis(n):
    n = n / np.linalg.norm(n); a = np.array([1., 0, 0]) if abs(n[0]) < .9 else np.array([0., 1, 0])
    u = np.cross(n, a); u /= np.linalg.norm(u); v = np.cross(n, u)
    return u, v


def rot_about(ob, pb, pivot, q):
    """Rotate a pose bone by the world-space quaternion q about a world pivot (its own head keeps the location channel)."""
    MW = ob.matrix_world
    pb.matrix = MW.inverted() @ (Matrix.Translation(pivot) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pivot) @ (MW @ pb.matrix))
    bpy.context.view_layer.update()


def joint(ob, name): return (ob.matrix_world @ ob.pose.bones[name].matrix).translation.copy()


def relax(ob, arm_amt, elbow_amt, hang_down=.95, hang_forward=.12):
    """Blender copy of EnemyRigMotion298.Relax: upper arm toward the hang direction, forearm toward the upper-arm line.
    A simulation of the game formula on the source bones - the Unity humanoid retarget itself is not reproduced here."""
    if arm_amt <= 0 and elbow_amt <= 0: return
    hang = (-UP * hang_down + FWD * hang_forward).normalized()
    for side in SIDES:
        up, lo, ha = (ob.pose.bones[side + n] for n in ('Arm', 'ForeArm', 'Hand'))
        U = joint(ob, side + 'Arm'); L = joint(ob, side + 'ForeArm')
        d = (L - U).normalized(); rot_about(ob, up, U, d.rotation_difference(d.slerp(hang, arm_amt)))
        U = joint(ob, side + 'Arm'); L = joint(ob, side + 'ForeArm'); H = joint(ob, side + 'Hand')
        along = (L - U).normalized(); fore = (H - L).normalized(); rot_about(ob, lo, L, fore.rotation_difference(fore.slerp(along, elbow_amt)))


def strip_scale(arm):
    """Humanoid clips carry no bone scale and no non-root translation: drop them (the Idle takes carry a Hips scale)."""
    for pb in arm.pose.bones:
        pb.scale = (1, 1, 1)
        if pb.name != 'Hips': pb.location = (0, 0, 0)
    bpy.context.view_layer.update()


class Rig:
    def __init__(self, arm, mesh):
        self.arm, self.mesh = arm, mesh
        me = mesh.data; self.N = len(me.vertices)
        self.groups = [g.name for g in mesh.vertex_groups]; self.gi = {n: i for i, n in enumerate(self.groups)}
        W = np.zeros((self.N, len(self.groups)))
        for v in me.vertices:
            for g in v.groups: W[v.index, g.group] = g.weight
        self.W = W; self.dom = W.argmax(1)
        me.calc_loop_triangles()
        self.tris = np.array([t.vertices[:] for t in me.loop_triangles], dtype=np.int64)
        keep = arm.animation_data.action if arm.animation_data else None
        set_action(arm, None)
        for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()
        self.V0 = verts_world(mesh); MW = arm.matrix_world
        self.rest = {b.name: {'head': np.array(MW @ b.head_local), 'tail': np.array(MW @ b.tail_local), 'R': np.array((MW @ b.matrix_local).to_3x3().normalized())} for b in arm.data.bones}
        self.height = float(self.V0[:, 2].max() - self.V0[:, 2].min()); self.rest_min_z = float(self.V0[:, 2].min())
        set_action(arm, keep)
        self._sets()

    def w(self, *names): return sum(self.W[:, self.gi[n]] for n in names if n in self.gi)

    def joints_now(self):
        MW = self.arm.matrix_world; out = {}
        for pb in self.arm.pose.bones:
            m = MW @ pb.matrix; out[pb.name] = (np.array(m.translation), np.array(m.to_3x3().normalized()))
        return out

    def _sets(self):
        S = {}
        armw_all = self.w(*[s + p for s in SIDES for p in ARM[1:]])
        self.body_tris = self.tris[(armw_all[self.tris] < .05).all(1)]
        self.rest_ring = {}; self.restA = {}
        for side in SIDES:
            u0 = self.rest[side + 'Arm']['head']; e0 = self.rest[side + 'ForeArm']['head']; h0 = self.rest[side + 'Hand']['head']
            ua = norm(e0 - u0); fa = norm(h0 - e0)
            chain = self.w(side + 'Arm', side + 'ForeArm', side + 'Hand'); chain4 = chain + self.w(side + 'Shoulder')
            d = {}
            def ring(center, n, mask, half=.025, rad=.30):
                rel = self.V0 - center; s = rel @ n; perp = np.linalg.norm(rel - np.outer(s, n), axis=1)
                return np.where(mask & (np.abs(s) < half) & (perp < rad))[0]
            d['elbow'] = ring(e0, norm(ua + fa), chain > .6)
            d['shoulder'] = ring(u0 + ua * .06, ua, chain4 + self.w('Spine', 'Spine01') > .6)
            s_up = (self.V0 - u0) @ ua
            fore_hand = self.w(side + 'ForeArm', side + 'Hand') >= .9
            upper = (self.dom == self.gi[side + 'Arm']) & (chain >= .9) & (s_up > .5 * np.linalg.norm(e0 - u0))
            d['test'] = np.where(fore_hand | upper)[0]
            d['hand'] = np.where(self.w(side + 'Hand') >= .5)[0]
            d['armtris'] = np.where((chain4[self.tris] > .5).any(1))[0]
            d['foot'] = np.where(self.w(side + 'Foot', side + 'ToeBase') >= .5)[0]
            S[side] = d
            for name, c, n in (('shoulder', u0 + ua * .06, ua), ('elbow', e0, norm(ua + fa))):
                idx = d[name]; rel = self.V0[idx] - c; u, v = plane_basis(n)
                self.rest_ring[side + name] = hull_area(np.stack([rel @ u, rel @ v], 1)) if len(idx) >= 4 else 0
            self.restA[side] = norm(self.rest[side + 'Arm']['R'].T @ np.cross(ua, fa))
        self.S = S
        tri0 = self.V0[self.tris]; self.area0 = np.linalg.norm(np.cross(tri0[:, 1] - tri0[:, 0], tri0[:, 2] - tri0[:, 0]), axis=1) / 2

    def part_labels(self):
        """0 body, 1 left arm, 2 right arm, 3 legs/feet - for the software sheet tint only."""
        lab = np.zeros(self.N, dtype=np.uint8)
        legs = self.w(*[s + p for s in SIDES for p in LEG]) >= .5
        lab[legs] = 3
        for i, side in enumerate(SIDES): lab[self.w(side + 'Arm', side + 'ForeArm', side + 'Hand') >= .5] = 1 + i
        return lab

    def frame(self, penetration=True):
        """Measures of the pose that is on the bones right now."""
        arm = self.arm; V = verts_world(self.mesh); J = self.joints_now(); fr = {}
        q = {pb.name: pb.matrix_basis.to_quaternion() for pb in arm.pose.bones}
        tv = V[self.tris]; area = np.linalg.norm(np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0]), axis=1) / 2
        ratio = area / np.maximum(self.area0, 1e-12); valid = self.area0 > 2e-5
        for side in SIDES:
            S = self.S[side]; d = {}
            U, Ra = J[side + 'Arm']; E, Rf = J[side + 'ForeArm']; H, Rh = J[side + 'Hand']
            ua = norm(E - U); fa = norm(H - E); sx = 1 if side == 'Left' else -1
            for name, c, n in (('shoulder', U + ua * .06, ua), ('elbow', E, norm(ua + fa))):
                if self.rest_ring[side + name] <= 0: continue
                rel = V[S[name]] - c; u, v = plane_basis(n)
                d['area_' + name] = (hull_area(np.stack([rel @ u, rel @ v], 1)) / self.rest_ring[side + name] - 1) * 100
            for b in ('Arm', 'ForeArm'):
                qq = q[side + b]; tw = math.degrees(2 * math.atan2(qq.y, qq.w)); d['twist_' + b] = (tw + 180) % 360 - 180
            cr = np.cross(ua, fa); n0w = Ra @ self.restA[side]
            d['elbow_flex'] = math.degrees(math.atan2(float(cr @ n0w), float(ua @ fa))); d['elbow_angle'] = ang(ua, fa)
            d['abduction'] = math.degrees(math.atan2(ua[0] * sx, -ua[2])); d['swing'] = math.degrees(math.atan2(-ua[1], -ua[2]))
            d['wrist'] = H.round(4).tolist()
            at = S['armtris']; rr = ratio[at]; vv = valid[at]
            d['tri_stretch'] = int(((rr > 2.0) & vv).sum()); d['tri_collapse'] = int(((rr < .4) & vv).sum())
            idx = S['foot']; z = V[idx, 2]; low = idx[np.argsort(z)[:15]]
            d['foot_minz'] = float(z.min()); d['low_idx'] = low.tolist(); d['ankle'] = J[side + 'Foot'][0].round(4).tolist()
            d['knee_bend'] = ang(J[side + 'Leg'][0] - J[side + 'UpLeg'][0], J[side + 'Foot'][0] - J[side + 'Leg'][0])
            fr[side] = d
        if penetration:
            bvh = BVHTree.FromPolygons([Vector(p) for p in V], [tuple(int(i) for i in t) for t in self.body_tris], all_triangles=True)
            for side in SIDES:
                ins = []; mind = 9.0; mind_fore = 9.0
                for i in self.S[side]['test']:
                    p = Vector(V[i]); loc, nrm, ti, dist = bvh.find_nearest(p, .25)
                    if loc is None: continue
                    if (p - loc).dot(nrm) < 0 and dist > .002:
                        odd = 0
                        for dvec in (Vector((0, 0, 1)), Vector((.5, .7, -.5)).normalized(), Vector((-.6, -.3, .74)).normalized()):
                            o = p.copy(); n = 0
                            for _ in range(40):
                                hit = bvh.ray_cast(o, dvec)
                                if hit[0] is None: break
                                n += 1; o = hit[0] + dvec * 1e-4
                            odd += n % 2
                        if odd >= 2: ins.append((int(i), dist, int(ti)))
                    else:
                        mind = min(mind, dist)
                        if self.dom[i] != self.gi[side + 'Arm']: mind_fore = min(mind_fore, dist)
                fr[side]['pen_count'] = len(ins); fr[side]['pen_max_cm'] = max([x[1] for x in ins], default=0) * 100
                # sleeve cloth (upper-arm vertices) against the torso reads differently from a hand inside a thigh: keep them apart
                upper_g = self.gi[side + 'Arm']
                fr[side]['pen_upper_cm'] = max([x[1] for x in ins if self.dom[x[0]] == upper_g], default=0) * 100
                fr[side]['pen_fore_cm'] = max([x[1] for x in ins if self.dom[x[0]] != upper_g], default=0) * 100
                fr[side]['min_clear_cm'] = mind * 100 if mind < 9 else None
                fr[side]['clear_fore_cm'] = mind_fore * 100 if mind_fore < 9 else None   # None = farther than 25 cm
                wh = {}
                for i, dist, ti in ins:
                    k = self.groups[self.dom[self.body_tris[ti][0]]] + '<-' + self.groups[self.dom[i]]; wh[k] = max(wh.get(k, 0), round(dist * 100, 2))
                fr[side]['pen_where'] = wh
        fr['hips'] = J['Hips'][0].round(5).tolist(); fr['head'] = J['Head'][0].round(5).tolist(); fr['chest'] = J['Spine'][0].round(5).tolist()
        fr['_V'] = V; fr['_q'] = q
        return fr


def sample_clip(rig, act, frames=None, human=False, relax_amt=None, penetration=True, hang=(.95, .12)):
    """Evaluate act on rig.arm at the given frames (default: every frame of its range)."""
    set_action(rig.arm, act); sc = bpy.context.scene
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    out = []
    for f in (frames if frames is not None else range(f0, f1 + 1)):
        sc.frame_set(f)
        if human: strip_scale(rig.arm)
        if relax_amt is not None: relax(rig.arm, relax_amt[0], relax_amt[1], hang[0], hang[1])
        bpy.context.view_layer.update()
        fr = rig.frame(penetration); fr['f'] = f; out.append(fr)
    return out


def clip_anomalies(arm, act):
    """Bones whose take carries a scale or a non-root translation (dropped by a humanoid clip)."""
    set_action(arm, act); sc = bpy.context.scene; bad = {}
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    for f in (f0, (f0 + f1) // 2, f1):
        sc.frame_set(f)
        for pb in arm.pose.bones:
            ds = max(abs(c - 1) for c in pb.scale)
            if ds > 1e-3: bad[pb.name] = round(max(bad.get(pb.name, 0), max(pb.scale)), 4)
    return bad


def pen_summary(frames):
    out = {}
    for side in SIDES:
        pm = [fr[side].get('pen_max_cm', 0) for fr in frames]; k = int(np.argmax(pm))
        cl = [fr[side]['min_clear_cm'] for fr in frames if fr[side].get('min_clear_cm') is not None]
        cf = [fr[side]['clear_fore_cm'] for fr in frames if fr[side].get('clear_fore_cm') is not None]
        out[side] = {'frames_inside': int(sum(1 for p in pm if p > 0)), 'frames': len(frames), 'max_cm': round(float(max(pm)), 2), 'at_f': frames[k]['f'],
                     'upper_max_cm': round(float(max(fr[side].get('pen_upper_cm', 0) for fr in frames)), 2),
                     'fore_hand_max_cm': round(float(max(fr[side].get('pen_fore_cm', 0) for fr in frames)), 2),
                     'fore_hand_frames': int(sum(1 for fr in frames if fr[side].get('pen_fore_cm', 0) > 0)),
                     'where': frames[k][side].get('pen_where', {}), 'min_clear_cm': round(float(min(cl)), 2) if cl else None,
                     'fore_hand_clear_cm': round(float(min(cf)), 2) if cf else None}
    return out


def arm_summary(frames):
    out = {}
    for side in SIDES:
        ab = np.array([fr[side]['abduction'] for fr in frames]); sw = np.array([fr[side]['swing'] for fr in frames])
        ef = np.array([fr[side]['elbow_flex'] for fr in frames]); ea = np.array([fr[side]['elbow_angle'] for fr in frames])
        wr = np.array([fr[side]['wrist'] for fr in frames]); ch = np.array([fr['chest'] for fr in frames])
        out[side] = {'abduction_mean': round(float(ab.mean()), 2), 'abduction_min': round(float(ab.min()), 2), 'abduction_max': round(float(ab.max()), 2),
                     'swing_range': round(float(sw.max() - sw.min()), 2), 'swing_min': round(float(sw.min()), 2), 'swing_max': round(float(sw.max()), 2),
                     'elbow_min': round(float(ea.min()), 2), 'elbow_max': round(float(ea.max()), 2), 'elbow_range': round(float(ea.max() - ea.min()), 2),
                     'elbow_signed_min': round(float(ef.min()), 2), 'hyperextension_frames': int((ef < 0).sum()),
                     'twist_upper_min': round(min(fr[side]['twist_Arm'] for fr in frames), 1), 'twist_upper_max': round(max(fr[side]['twist_Arm'] for fr in frames), 1),
                     'twist_fore_min': round(min(fr[side]['twist_ForeArm'] for fr in frames), 1), 'twist_fore_max': round(max(fr[side]['twist_ForeArm'] for fr in frames), 1),
                     'wrist_fore_aft_cm': round(float(((wr[:, 1] - ch[:, 1]).max() - (wr[:, 1] - ch[:, 1]).min()) * 100), 1),
                     'shoulder_area_min_pct': round(min(fr[side].get('area_shoulder', 0) for fr in frames), 1), 'elbow_area_min_pct': round(min(fr[side].get('area_elbow', 0) for fr in frames), 1),
                     'tri_stretch_max': max(fr[side]['tri_stretch'] for fr in frames), 'tri_collapse_max': max(fr[side]['tri_collapse'] for fr in frames)}
    L, R = out['Left'], out['Right']
    out['asymmetry'] = {'abduction_mean_L_minus_R': round(L['abduction_mean'] - R['abduction_mean'], 2), 'elbow_range_L_minus_R': round(L['elbow_range'] - R['elbow_range'], 2),
                        'elbow_max_L_minus_R': round(L['elbow_max'] - R['elbow_max'], 2), 'swing_range_L_over_R': round(L['swing_range'] / max(1e-6, R['swing_range']), 3),
                        'wrist_fore_aft_L_over_R': round(L['wrist_fore_aft_cm'] / max(1e-6, R['wrist_fore_aft_cm']), 3)}
    return out


def stance_masks(frames, fps):
    """Per foot, per interval k -> k+1 of a walk cycle (the last frame equals the first): backward speed of the 15 lowest sole
    vertices and the stance mask. These clips shuffle - the sole stays within a few cm of the ground in swing too - so stance is
    read from the speed plateau (within 25 % of the median backward speed), the rule of work/walk_sum2.py, not from height."""
    n = len(frames) - 1; dt = 1.0 / fps; out = {}
    for side in SIDES:
        vel = np.array([(frames[k + 1]['_V'][frames[k][side]['low_idx']] - frames[k]['_V'][frames[k][side]['low_idx']]).mean(0) / dt for k in range(n)])
        vy = vel[:, 1]; top = np.percentile(vy, 90)
        plateau = float(np.median(vy[vy > .5 * top])) if top > 0 else float('nan')
        hit = np.abs(vy - plateau) < .25 * abs(plateau) if plateau == plateau else np.zeros(n, dtype=bool)
        # one stance per cycle: the longest cyclic run of plateau intervals. A stray interval elsewhere (the lifted foot being
        # drawn back at the same speed) is swing, and planting it would push a raised foot into the ground.
        mask = np.zeros(n, dtype=bool); best = (0, 0)
        if hit.all(): mask[:] = True
        elif hit.any():
            for start in range(n):
                if hit[start] and not hit[start - 1]:
                    length = 0
                    while hit[(start + length) % n]: length += 1
                    if length > best[0]: best = (length, start)
            for k in range(best[0]): mask[(best[1] + k) % n] = True
        out[side] = {'vel': vel, 'mask': mask, 'plateau': plateau, 'stray_intervals': int(hit.sum() - mask.sum())}
    return out


def gait_summary(frames, fps, wmps_list=(1.5,), speeds=()):
    """Walk cycle numbers. Stance speed = mean backward speed of the planted sole = the speed the actor must travel at for zero
    slide when the clip plays at rate 1 (what EnemyRigMotion298.WalkMetresPerSecond has to hold)."""
    n = len(frames) - 1; dt = 1.0 / fps; T = n * dt; F = frames[:-1]; st = stance_masks(frames, fps)
    res = {'cycle_frames': n, 'seconds': round(T, 4), 'stance_rule': 'speed plateau +-25 %, longest cyclic run'}
    allv = []; stance_n = []
    for side in SIDES:
        con = st[side]['mask']; vy = st[side]['vel'][:, 1]; vx = st[side]['vel'][:, 0]; vz = st[side]['vel'][:, 2]
        mz = np.array([fr[side]['foot_minz'] for fr in F]); az = np.array([fr[side]['ankle'][2] for fr in F])
        if not con.any(): res[side] = {'stance_frames': 0}; stance_n.append(0); continue
        v = vy[con]
        res[side] = {'stance_frames': int(con.sum()), 'stance_list': [F[i]['f'] for i in np.where(con)[0]], 'stance_pct': round(float(con.mean() * 100), 1), 'stray_plateau_intervals': st[side]['stray_intervals'],
                     'stance_speed_mean': round(float(v.mean()), 4), 'stance_speed_min': round(float(v.min()), 3), 'stance_speed_max': round(float(v.max()), 3),
                     'slide_fore_aft_in_clip_cm': round(float(np.abs(v - v.mean()).sum() * dt * 100), 2), 'slide_lateral_in_clip_cm': round(float(np.abs(vx[con]).sum() * dt * 100), 2),
                     'slide_vertical_in_clip_cm': round(float(np.abs(vz[con]).sum() * dt * 100), 2),
                     'sole_min_cm_stance': round(float(mz[con].min() * 100), 2), 'sole_max_cm_stance': round(float(mz[con].max() * 100), 2),
                     'sole_mean_cm_stance': round(float(mz[con].mean() * 100), 2),
                     'sole_max_cm_swing': round(float(mz[~con].max() * 100), 2) if (~con).any() else None, 'ankle_lift_cm': round(float((az.max() - az.min()) * 100), 1)}
        allv.append(v); stance_n.append(int(con.sum()))
    allv = np.concatenate(allv) if allv else np.array([]); v = float(allv.mean()) if len(allv) else float('nan')
    L, Rr = st['Left']['mask'], st['Right']['mask']
    res['stance_speed_m_s'] = round(v, 4); res['stride_m'] = round(v * T, 4)
    res['double_support_frames'] = int((L & Rr).sum()); res['flight_frames'] = int((~L & ~Rr).sum())
    res['steps_per_minute_at_clip_rate'] = round(120.0 / T, 1)
    res['slide'] = {}
    for wm in wmps_list:
        stt = sum(stance_n) / 2.0 * dt
        res['slide']['%.3f' % wm] = {'ratio': round(abs(1 - v / wm), 4), 'per_step_cm': round(abs(wm - v) * stt * 100, 2), 'per_cycle_cm': round(abs(v - wm) * T * 100, 2),
                                     'playback_rate': {('%.2f' % s): round(s / wm, 3) for s in speeds},
                                     'cadence_steps_per_min': {('%.2f' % s): round(120.0 * (s / wm) / T, 1) for s in speeds}}
    if res['Left'].get('stance_frames') and res['Right'].get('stance_frames'):
        res['asymmetry'] = {'stance_pct_L_minus_R': round(res['Left']['stance_pct'] - res['Right']['stance_pct'], 1),
                            'stance_speed_L_minus_R': round(res['Left']['stance_speed_mean'] - res['Right']['stance_speed_mean'], 3),
                            'sole_min_cm_L_minus_R': round(res['Left']['sole_min_cm_stance'] - res['Right']['sole_min_cm_stance'], 2)}
    return res


def knee_summary(frames):
    """Knee bend per frame of a walk cycle (0 = straight) and its roughness: the largest frame-to-frame change and the largest
    second difference. A repair that plants the feet must not make these much larger than the source's."""
    out = {}
    for side in SIDES:
        k = np.array([fr[side]['knee_bend'] for fr in frames[:-1]]); d1 = np.roll(k, -1) - k; d2 = np.roll(k, -1) - 2 * k + np.roll(k, 1)
        out[side] = {'bend_deg': [round(float(x), 1) for x in k], 'min': round(float(k.min()), 1), 'max': round(float(k.max()), 1),
                     'max_step_deg': round(float(np.abs(d1).max()), 1), 'max_second_difference_deg': round(float(np.abs(d2).max()), 1)}
    return out


def pop_summary(frames, limit=25.0):
    """First-frame pop: per-bone rotation between the first two frames against the median step of the next ten."""
    names = list(frames[0]['_q'].keys())
    def step(a, b, name):
        an = math.degrees(frames[a]['_q'][name].rotation_difference(frames[b]['_q'][name]).angle); return min(an, 360 - an)
    first = {n: step(0, 1, n) for n in names}
    later = {n: float(np.median([step(i, i + 1, n) for i in range(1, min(11, len(frames) - 1))])) for n in names}
    over = {n: round(v, 1) for n, v in first.items() if v > limit}
    worst = max(first, key=first.get)
    return {'first_step_max_deg': round(first[worst], 1), 'first_step_bone': worst, 'later_median_of_that_bone_deg': round(later[worst], 2),
            'bones_over_limit': over, 'limit_deg': limit, 'pop': len(over) > 0}


def strip_private(frames):
    for fr in frames:
        fr.pop('_V', None); fr.pop('_q', None)
        for side in SIDES: fr[side].pop('low_idx', None)
    return frames
