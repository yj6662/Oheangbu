"""#308 enemy rig (SPEC-ENEMY-RIG-VERIFY-308, TEST): offline measure and walk-clip repair for dokkaebi / agwi / changgui.
Blender headless, CPU only, NO render (the sheets are drawn afterwards by sheets308.py, a numpy rasteriser).

  B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
  python Tools/resource_guard.py --wait
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRig308/enemyrig308.py -- inspect <id>
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRig308/enemyrig308.py -- measure <id>
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRig308/enemyrig308.py -- fix <id> [--no-mesh] [--abduction <deg>] [--lift-cm <cm>] [--no-plant]

Reads the source FBX under Assets (read only). Writes: Art/Characters308/DokkaebiVerify/fix/<id>_measure.json | <id>_fix.json,
fix/work/<id>_*.npz (posed vertices for the sheets) and, for `fix`, the repaired clip
Tools/Unity/Stage308_enemyrig/Art/Characters/Folklore298/Animations/<id>/<id>-walkfix308_walk_fix308.fbx (take Armature|walk_fix308).
The rig, the bone names, the rest pose and the in-place root convention are not changed: only key values of the walk action
(Hips location; upper arm / forearm / thigh / shin / foot rotation) are rewritten. Parameters: enemyrig308.json.
Feet: one hips lift, then only frames still under the ground are raised by two-bone IK (a foot above ground is never pushed
down). After a change here: sheets308.py, summary308.py, then Tools/Unity/Stage308_enemyrig/_Tools/make_data308.py (re-seal)."""
import bpy, sys, os, json, math, hashlib
import numpy as np
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import rig308 as R

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace('\\', '/')
CFG = json.load(open(os.path.join(HERE, 'enemyrig308.json'), encoding='utf-8'))
ARGS = sys.argv[sys.argv.index('--') + 1:]
OUT = REPO + '/' + CFG['out']; WORK = OUT + '/work'
os.makedirs(WORK, exist_ok=True)


def src(rel): return REPO + '/' + CFG['assets'] + '/' + rel
def sha(p): return hashlib.sha256(open(p, 'rb').read()).hexdigest()
def opt(name, default=None, cast=float):
    return cast(ARGS[ARGS.index(name) + 1]) if name in ARGS else default


def fold(deg): return min(deg, 360.0 - deg)   # q and -q are one rotation


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


def role_of(act, m):
    n = act.name.split('|')[-1]
    for cand in act.name.split('|'):
        if cand in ('Idle', 'Hit_Reaction', 'Hit_Reaction_1', 'Dead', m['attack']): n = cand
    return {'Idle': 'idle', m['attack']: 'attack', 'Hit_Reaction': 'hit', 'Hit_Reaction_1': 'stun', 'Dead': 'death'}.get(n, n)


def inspect(mid):
    m = CFG['monsters'][mid]
    for key in ('rig', 'walk', 'motion'):
        arm, mesh = R.load(src(m[key])); sc = bpy.context.scene
        print('INSPECT', mid, key, 'fps', sc.render.fps, 'arm scale', [round(x, 5) for x in arm.scale], 'rot', [round(math.degrees(x), 2) for x in arm.rotation_euler],
              'bones', len(arm.data.bones), 'verts', len(mesh.data.vertices) if mesh else None)
        for a in bpy.data.actions:
            print('INSPECT   action', repr(a.name), [round(x, 2) for x in a.frame_range], 'anomalies', R.clip_anomalies(arm, a))
        h = fbx_header(src(m[key])); print('INSPECT   header', h['global'], h['models'].get('Armature'), [s['name'] for s in h['stacks']])


def feet_only(frames):
    lo = [min(fr['Left']['foot_minz'], fr['Right']['foot_minz']) for fr in frames]
    return {'sole_min_cm': round(min(lo) * 100, 2), 'sole_min_first_frame_cm': round(lo[0] * 100, 2), 'sole_max_of_lowest_cm': round(max(lo) * 100, 2),
            'lowest_vertex_first_frame_cm': round(float(frames[0]['_V'][:, 2].min()) * 100, 2),
            'hips_travel_cm': [round(float(v) * 100, 1) for v in (np.array([fr['hips'] for fr in frames]).max(0) - np.array([fr['hips'] for fr in frames]).min(0))]}


def ref_arrays(frames):
    return {'frames': [fr['f'] - 1 for fr in frames],   # Unity frame index = Blender frame - 1 (importer offset)
            **{side: {'abduction': [round(fr[side]['abduction'], 2) for fr in frames], 'swing': [round(fr[side]['swing'], 2) for fr in frames],
                      'elbow': [round(fr[side]['elbow_angle'], 2) for fr in frames]} for side in R.SIDES}}


def measure(mid):
    m = CFG['monsters'][mid]; g = CFG['game']; lim = CFG['limits']; lr = g['legacy_relax']; hang = g['hang']
    out = {'schema': '308.enemyrig.measure.1', 'id': mid, 'status': 'offline Blender measure of the source FBX [O]; the relax rows are a simulation of the game formula [O sim / I game]', 'clips': {}, 'ref': {}}
    arm, mesh = R.load(src(m['walk'])); rig = R.Rig(arm, mesh); act = bpy.data.actions[0]; fps = bpy.context.scene.render.fps
    out['rig'] = {'height_m': round(rig.height, 4), 'rest_min_z_cm': round(rig.rest_min_z * 100, 2), 'verts': rig.N, 'tris': int(len(rig.tris)), 'fps': fps,
                  'armature_scale': [round(x, 6) for x in arm.scale], 'bones': len(arm.data.bones),
                  'test_verts': {s: int(len(rig.S[s]['test'])) for s in R.SIDES}, 'foot_verts': {s: int(len(rig.S[s]['foot'])) for s in R.SIDES}}
    print('MEASURE', mid, 'walk', act.name, act.frame_range[:], 'fps', fps, flush=True)
    raw = R.sample_clip(rig, act)
    out['clips']['walk'] = {'action': act.name, 'frames': len(raw), 'arms': R.arm_summary(raw[:-1]), 'pen': R.pen_summary(raw[:-1]),
                            'gait': R.gait_summary(raw, fps, [g['wmps_now']], speeds=m['speeds'])}
    out['ref']['walk'] = ref_arrays(raw)
    rel = R.sample_clip(rig, act, relax_amt=lr, hang=hang)
    out['clips']['walk+relax_legacy'] = {'relax': lr, 'arms': R.arm_summary(rel[:-1]), 'pen': R.pen_summary(rel[:-1])}
    v = out['clips']['walk']['gait']['stance_speed_m_s']
    out['clips']['walk']['gait_if_matched'] = R.gait_summary(raw, fps, [round(v, 3)], speeds=m['speeds'])['slide']
    # motion takes
    arm, mesh = R.load(src(m['motion'])); rig = R.Rig(arm, mesh)
    acts = {role_of(a, m): a for a in bpy.data.actions}
    print('MEASURE', mid, 'motion roles', {k: a.name for k, a in acts.items()}, flush=True)
    idle_dump = {}
    for role in ('idle', 'attack', 'hit', 'stun', 'death'):
        a = acts.get(role)
        if a is None: out['clips'][role] = {'missing': True}; continue
        bad = R.clip_anomalies(arm, a); human = bool(bad)
        fr = R.sample_clip(rig, a, human=human)
        e = {'action': a.name, 'frames': len(fr), 'scale_or_offset_bones': bad, 'humanoid_strip': human, 'arms': R.arm_summary(fr), 'pen': R.pen_summary(fr), 'feet': feet_only(fr),
             'pop': R.pop_summary(fr, lim['pop_deg'])}
        out['clips'][role] = e
        print('MEASURE', mid, role, 'pen', e['pen']['Left']['max_cm'], e['pen']['Right']['max_cm'], 'pop', e['pop']['first_step_max_deg'], flush=True)
        if role != 'idle': continue
        sf = CFG['sheet_frames']['idle']; f0 = int(round(a.frame_range[0]))
        idle_dump['raw'] = np.array([fr[f - f0]['_V'] for f in sf], dtype=np.float32)
        leg = R.sample_clip(rig, a, human=human, relax_amt=lr, hang=hang)
        out['clips']['idle+relax_legacy'] = {'relax': lr, 'arms': R.arm_summary(leg), 'pen': R.pen_summary(leg)}
        idle_dump['legacy'] = np.array([leg[f - f0]['_V'] for f in sf], dtype=np.float32)
        sw = CFG['idle_sweep']; frames = list(range(f0, int(round(a.frame_range[1])) + 1, sw['every'])); rows = []
        raw_upper = max(e['pen'][s]['upper_max_cm'] for s in R.SIDES)
        for am in sw['arm']:
            for el in sw['elbow']:
                if am == 0 and el > 0: continue
                s = R.sample_clip(rig, a, frames=frames, human=human, relax_amt=(am, el), hang=hang); p = R.pen_summary(s); ar = R.arm_summary(s)
                rows.append({'arm': am, 'elbow': el, 'fore_hand_max_cm': max(p[x]['fore_hand_max_cm'] for x in R.SIDES), 'upper_max_cm': max(p[x]['upper_max_cm'] for x in R.SIDES),
                             'frames_fore_hand_inside': max(p[x]['fore_hand_frames'] for x in R.SIDES), 'frames': len(s),
                             'abduction_mean': [ar['Left']['abduction_mean'], ar['Right']['abduction_mean']], 'elbow_mean': [round((ar[x]['elbow_min'] + ar[x]['elbow_max']) / 2, 1) for x in R.SIDES],
                             'min_clear_cm': [p[x]['min_clear_cm'] for x in R.SIDES],
                             'fore_hand_clear_cm': min([p[x]['fore_hand_clear_cm'] for x in R.SIDES if p[x]['fore_hand_clear_cm'] is not None], default=None)})
                print('MEASURE', mid, 'idle sweep', rows[-1], flush=True)
        ok = [r for r in rows if r['fore_hand_max_cm'] <= lim['pen_fore_hand_cm'] and r['upper_max_cm'] <= max(lim['pen_upper_cm'], raw_upper + .5)
              and (r['fore_hand_clear_cm'] is None or r['fore_hand_clear_cm'] >= lim['idle_hand_clear_cm'])]
        best = max(ok, key=lambda r: (r['arm'], r['elbow'])) if ok else {'arm': 0.0, 'elbow': 0.0}
        out['idle_sweep'] = {'rows': rows, 'rule': 'largest arm (then elbow) relax whose hand/forearm penetration is <= %.1f cm, hand/forearm clearance >= %.1f cm and sleeve penetration <= max(%.1f, raw %.2f + 0.5) cm on every sampled frame' % (lim['pen_fore_hand_cm'], lim['idle_hand_clear_cm'], lim['pen_upper_cm'], raw_upper),
                             'proposed': [best['arm'], best['elbow']], 'sampled_every': sw['every']}
        pr = R.sample_clip(rig, a, human=human, relax_amt=(best['arm'], best['elbow']), hang=hang)
        out['clips']['idle+relax_proposed'] = {'relax': [best['arm'], best['elbow']], 'arms': R.arm_summary(pr), 'pen': R.pen_summary(pr)}
        idle_dump['proposed'] = np.array([pr[f - f0]['_V'] for f in sf], dtype=np.float32)
        np.savez_compressed(WORK + '/%s_idle.npz' % mid, tris=rig.tris.astype(np.int32), labels=rig.part_labels(), frames=np.array(sf), **idle_dump)
    json.dump(out, open(OUT + '/%s_measure.json' % mid, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print('MEASURE DONE', mid)


# ---------------------------------------------------------------------------------------------------- walk repair
def leg_raise(ob, side, dz, min_bend_deg=6.0):
    """Two-bone IK: lift the ankle by dz (world Z) with the hip fixed, the knee kept on its side, the foot's world rotation kept.
    A push down never straightens the knee past min_bend_deg (or past the source bend when that is straighter): near full
    extension a centimetre of reach costs many degrees of knee, which reads as a pop. The foot then stays a little high."""
    up, lo, ft = (ob.pose.bones[side + n] for n in ('UpLeg', 'Leg', 'Foot'))
    P = R.joint(ob, side + 'UpLeg'); K = R.joint(ob, side + 'Leg'); A = R.joint(ob, side + 'Foot')
    Fw = (ob.matrix_world @ ft.matrix).to_3x3().normalized().to_quaternion()
    l1 = (K - P).length; l2 = (A - K).length
    bend = min(math.degrees((K - P).angle(A - K)), min_bend_deg)
    reach = math.sqrt(l1 * l1 + l2 * l2 + 2 * l1 * l2 * math.cos(math.radians(bend)))
    A2 = A + Vector((0, 0, dz)); d = (A2 - P).length; d = max(abs(l1 - l2) + 1e-4, min(d, reach)); e = (A2 - P).normalized()
    kp = K - P; pole = kp - e * kp.dot(e)
    if pole.length < 1e-6: pole = R.FWD - e * R.FWD.dot(e)
    pole.normalize()
    ca = max(-1.0, min(1.0, (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d))); sa = math.sqrt(max(0.0, 1 - ca * ca))
    K2 = P + (e * ca + pole * sa) * l1
    R.rot_about(ob, up, P, (K - P).rotation_difference(K2 - P))
    K = R.joint(ob, side + 'Leg'); A = R.joint(ob, side + 'Foot')
    R.rot_about(ob, lo, K, (A - K).rotation_difference(P + e * d - K))
    A = R.joint(ob, side + 'Foot'); cur = (ob.matrix_world @ ft.matrix).to_3x3().normalized().to_quaternion()
    R.rot_about(ob, ft, A, Fw @ cur.inverted())


def fix_pose(rig, f, P):
    """Apply the repair to the pose that frame_set(f) just evaluated. Order: hips lift, upper arms, elbows, stance feet."""
    ob = rig.arm
    if abs(P['lift']) > 1e-7:
        hp = ob.pose.bones['Hips']; M = ob.matrix_world @ hp.matrix; M.translation.z += P['lift']
        hp.matrix = ob.matrix_world.inverted() @ M; bpy.context.view_layer.update()
    for side in R.SIDES:
        sx = 1 if side == 'Left' else -1
        if P['abd_target'] is not None:
            U = R.joint(ob, side + 'Arm'); E = R.joint(ob, side + 'ForeArm'); ua = (E - U).normalized()
            abd = math.degrees(math.atan2(ua.x * sx, -ua.z)); new = P['abd_target'] + P['abd_scale'] * (abd - P['abd_mean'][side])
            # frontal-plane adduction about the body's fore-aft axis: the fore-aft swing angle is left as authored
            R.rot_about(ob, ob.pose.bones[side + 'Arm'], U, Quaternion((0, 1, 0), math.radians(-sx * (new - abd))))
        s0, s1, d0, d1 = P['elbow'][side]
        if abs(s1 - s0) > 1e-6 and (abs(s0 - d0) > 1e-6 or abs(s1 - d1) > 1e-6):
            U = R.joint(ob, side + 'Arm'); E = R.joint(ob, side + 'ForeArm'); H = R.joint(ob, side + 'Hand')
            ua = (E - U).normalized(); fa = (H - E).normalized(); flex = math.degrees(ua.angle(fa)); n = ua.cross(fa)
            new = d0 + (flex - s0) * (d1 - d0) / (s1 - s0)
            if n.length > 1e-5: R.rot_about(ob, ob.pose.bones[side + 'ForeArm'], E, Quaternion(n.normalized(), math.radians(new - flex)))
        dz = P['plant'][side].get(f, 0.0)
        if abs(dz) > 1e-5: leg_raise(ob, side, dz, P['knee_min_bend'])


def evaluate(rig, act, P, frames, penetration=True):
    R.set_action(rig.arm, act); sc = bpy.context.scene; out = []
    for f in frames:
        sc.frame_set(f); fix_pose(rig, f, P); fr = rig.frame(penetration); fr['f'] = f; out.append(fr)
    return out


def plant_table(raw, fps, lift, cap):
    """Ground floor, per foot and per frame: after the hips lift, a frame whose lowest sole vertex is still under z = 0 gets the
    upward correction that puts it on the ground (the knee bends a little more). A foot above the ground is never pushed down:
    near full leg extension a push down costs many knee degrees per centimetre and locks the knee (tried, rejected - see the
    README). The correction is continuous by construction (zero wherever the source is above ground), so no blending is needed."""
    F = raw[:-1]; n = len(F); table = {}; info = {}; st = R.stance_masks(raw, fps)
    for side in R.SIDES:
        iv = st[side]['mask']; mask = iv | np.roll(iv, 1)   # frames touching a stance interval (for the report only)
        mz = np.array([fr[side]['foot_minz'] for fr in F]); dz = np.clip(-(mz + lift), 0.0, cap)
        table[side] = {F[i]['f']: float(dz[i]) for i in range(n) if dz[i] > 1e-6}
        info[side] = {'stance_frames_touched': [F[i]['f'] for i in np.where(mask)[0]], 'corrected_frames': int((dz > 1e-6).sum()),
                      'correction_max_cm': round(float(dz.max()) * 100, 2),
                      'sole_source_cm_by_frame': {str(F[i]['f']): round(float(mz[i]) * 100, 2) for i in range(n)},
                      'correction_cm_by_frame': {str(F[i]['f']): round(float(dz[i]) * 100, 2) for i in range(n) if dz[i] > 1e-6}}
    return table, info


def fix(mid):
    m = CFG['monsters'][mid]; g = CFG['game']; lim = CFG['limits']; fx = CFG['fix']; lr = g['legacy_relax']; hang = g['hang']
    out = {'schema': '308.enemyrig.fix.1', 'id': mid, 'status': 'IMPLEMENTED (offline): Blender numbers [O]; nothing imported into Unity', 'source': m['walk'], 'source_sha256': sha(src(m['walk']))}
    arm, mesh = R.load(src(m['walk'])); rig = R.Rig(arm, mesh); act = bpy.data.actions[0]; sc = bpy.context.scene; fps = sc.render.fps
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1])); cyc = list(range(f0, f1))
    rest0 = {b.name: np.array(b.matrix_local) for b in arm.data.bones}; arm_scale0 = [round(x, 6) for x in arm.scale]
    src_name = act.name
    raw = R.sample_clip(rig, act); loop0 = max(fold(math.degrees(raw[0]['_q'][n].rotation_difference(raw[-1]['_q'][n]).angle)) for n in raw[0]['_q'])
    before = {'arms': R.arm_summary(raw[:-1]), 'pen': R.pen_summary(raw[:-1]), 'gait': R.gait_summary(raw, fps, [g['wmps_now']], speeds=m['speeds']), 'loop_first_last_deg': round(loop0, 4)}
    rel = R.sample_clip(rig, act, relax_amt=lr, hang=hang)
    before_legacy = {'relax': lr, 'arms': R.arm_summary(rel[:-1]), 'pen': R.pen_summary(rel[:-1])}
    sf = CFG['sheet_frames']['walk']
    dump = {'before': np.array([raw[f - f0]['_V'] for f in sf], dtype=np.float32), 'legacy': np.array([rel[f - f0]['_V'] for f in sf], dtype=np.float32)}
    # ---- parameters from the source
    A = before['arms']; calm = 'Left' if A['Left']['elbow_max'] <= A['Right']['elbow_max'] else 'Right'
    P = {'abd_mean': {s: A[s]['abduction_mean'] for s in R.SIDES}, 'abd_scale': fx['abduction']['variation_scale'], 'abd_target': None,
         'elbow': {s: (A[s]['elbow_min'], A[s]['elbow_max'], A[calm]['elbow_min'], A[calm]['elbow_max']) for s in R.SIDES}, 'lift': 0.0, 'plant': {s: {} for s in R.SIDES},
         'knee_min_bend': fx['feet']['knee_min_bend_deg']}
    F = raw[:-1]
    st0 = R.stance_masks(raw, fps)
    stance_mean = np.mean([np.mean([F[i][s]['foot_minz'] for i in np.where(st0[s]['mask'])[0]]) for s in R.SIDES if st0[s]['mask'].any()])
    lift = opt('--lift-cm'); P['lift'] = (lift / 100.0) if lift is not None else float(-stance_mean) + fx['feet']['lift_bias_cm'] / 100.0
    feet_info = {'lift_cm': round(P['lift'] * 100, 2), 'lift_rule': fx['feet']['lift'] if lift is None else 'command line', 'ground_z': 0.0, 'plant': False, 'knee_min_bend_deg': P['knee_min_bend']}
    if fx['feet']['plant'] and '--no-plant' not in ARGS:
        P['plant'], info = plant_table(raw, fps, P['lift'], fx['feet']['max_plant_cm'] / 100.0); feet_info.update({'plant': True, 'lift_bias_cm': fx['feet']['lift_bias_cm'], 'per_foot': info})
    # ---- abduction: the smallest mean spread that keeps the hands out of the body and the sleeves within the limit
    ab = fx['abduction']; forced = opt('--abduction'); tried = []
    cands = [forced] if forced is not None else list(np.arange(ab['target_start_deg'], ab['target_max_deg'] + 1e-6, ab['target_step_deg']))
    chosen = None
    for t in cands:
        t = float(t)
        if t >= min(P['abd_mean'].values()): break   # never open the arms wider than the source mean
        P['abd_target'] = t; frs = evaluate(rig, act, P, cyc); p = R.pen_summary(frs)
        clear = [p[s]['fore_hand_clear_cm'] for s in R.SIDES if p[s]['fore_hand_clear_cm'] is not None]
        row = {'target_deg': t, 'fore_hand_max_cm': max(p[s]['fore_hand_max_cm'] for s in R.SIDES), 'upper_max_cm': max(p[s]['upper_max_cm'] for s in R.SIDES),
               'fore_hand_clear_cm': min(clear) if clear else None}
        tried.append(row); print('FIX', mid, 'abduction try', row, flush=True)
        if forced is not None or (row['fore_hand_max_cm'] <= lim['pen_fore_hand_cm'] and row['upper_max_cm'] <= lim['pen_upper_cm']
                                  and (row['fore_hand_clear_cm'] is None or row['fore_hand_clear_cm'] >= lim['walk_hand_clear_cm'])): chosen = t; break
    P['abd_target'] = chosen
    out['parameters'] = {'abduction': {'source_mean_deg': P['abd_mean'], 'target_mean_deg': chosen, 'variation_scale': P['abd_scale'], 'search': tried,
                                       'note': None if chosen is not None else 'no target below the source mean met the penetration limits: upper arms left as authored'},
                         'elbow': {'calmer_side': calm, 'remap_from_to_deg': {s: [round(x, 2) for x in P['elbow'][s]] for s in R.SIDES}}, 'feet': feet_info}
    # ---- final poses -> key values
    bones_rot = [s + n for s in R.SIDES for n in ('Arm', 'ForeArm', 'UpLeg', 'Leg', 'Foot')]
    R.set_action(arm, act); vals = {}; mats = {}
    for f in cyc:
        sc.frame_set(f); fix_pose(rig, f, P)
        vals[f] = {'Hips.loc': tuple(arm.pose.bones['Hips'].location), **{b: tuple(arm.pose.bones[b].rotation_quaternion) for b in bones_rot}}
    vals[f1] = dict(vals[f0])
    fcs = R.fcurves_of(act); written = 0
    def write(path, count, key):
        nonlocal written
        prev = None; seq = {}
        for f in range(f0, f1 + 1):
            v = np.array(vals[f][key], dtype=np.float64)
            if count == 4 and prev is not None and float(v @ prev) < 0: v = -v
            seq[f] = v; prev = v
        for i in range(count):
            fc = fcs.find(path, index=i)
            if fc is None: raise RuntimeError('no F-curve ' + path + '[%d]' % i)
            have = {int(round(k.co.x)) for k in fc.keyframe_points}
            for f in range(f0, f1 + 1):
                if f not in have: fc.keyframe_points.insert(f, float(seq[f][i]))
            for k in fc.keyframe_points:
                f = int(round(k.co.x))
                if f in seq: k.co.y = float(seq[f][i]); k.interpolation = 'LINEAR'; written += 1
            fc.update()
    write('pose.bones["Hips"].location', 3, 'Hips.loc')
    for b in bones_rot: write('pose.bones["%s"].rotation_quaternion' % b, 4, b)
    act.name = fx['take']
    # ---- after
    aft = R.sample_clip(rig, act); loop1 = max(fold(math.degrees(aft[0]['_q'][n].rotation_difference(aft[-1]['_q'][n]).angle)) for n in aft[0]['_q'])
    hips_loop = float(np.linalg.norm(np.array(aft[0]['hips']) - np.array(aft[-1]['hips'])) * 100)
    def sole_cycle(frames): return {s: [round(min(fr[s]['foot_minz'] for fr in frames[:-1]) * 100, 2), round(max(fr[s]['foot_minz'] for fr in frames[:-1]) * 100, 2)] for s in R.SIDES}
    before['sole_cm_whole_cycle'] = sole_cycle(raw); before['knee'] = R.knee_summary(raw)
    after = {'arms': R.arm_summary(aft[:-1]), 'pen': R.pen_summary(aft[:-1]), 'gait': R.gait_summary(aft, fps, [g['wmps_now']], speeds=m['speeds']), 'sole_cm_whole_cycle': sole_cycle(aft),
             'loop_first_last_deg': round(loop1, 4), 'hips_first_last_cm': round(hips_loop, 4)}
    after['knee'] = R.knee_summary(aft)
    v = after['gait']['stance_speed_m_s']; after['gait_if_matched'] = R.gait_summary(aft, fps, [round(v, 3)], speeds=m['speeds'])['slide']
    vb = before['gait']['stance_speed_m_s']; before['gait_if_matched'] = R.gait_summary(raw, fps, [round(vb, 3)], speeds=m['speeds'])['slide']
    # unchanged bones: every other bone's key values must still be the source's
    untouched = max((fold(math.degrees(raw[i]['_q'][n].rotation_difference(aft[i]['_q'][n]).angle)) for i in range(len(raw)) for n in raw[0]['_q'] if n not in bones_rot), default=0.0)
    def steps(frames):
        jump = {}
        for i in range(len(frames) - 1):
            for n in frames[0]['_q']:
                an = fold(math.degrees(frames[i]['_q'][n].rotation_difference(frames[i + 1]['_q'][n]).angle))
                if an > jump.get(n, (0, 0))[0]: jump[n] = (round(an, 2), frames[i + 1]['f'])
        return jump
    jump = steps(aft); jump0 = steps(raw)
    out.update({'before': before, 'before_with_legacy_relax': before_legacy, 'after': after, 'ref_after': ref_arrays(aft), 'ref_before': ref_arrays(raw),
                'checks': {'keys_written': written, 'untouched_bones_max_rotation_change_deg': round(untouched, 5), 'max_step_deg_after': dict(sorted(jump.items(), key=lambda kv: -kv[1][0])[:6]), 'max_step_deg_before_same_bones': {n: jump0[n] for n in dict(sorted(jump.items(), key=lambda kv: -kv[1][0])[:6])},
                           'max_step_increase_deg': round(max(jump[n][0] - jump0[n][0] for n in jump), 2),
                           'fps': fps, 'frames': [f0, f1], 'source_action': src_name}})
    dump['after'] = np.array([aft[f - f0]['_V'] for f in sf], dtype=np.float32)
    np.savez_compressed(WORK + '/%s_walk.npz' % mid, tris=rig.tris.astype(np.int32), labels=rig.part_labels(), frames=np.array(sf), **dump)
    # ---- export: same structure as the source file (armature + skinned mesh, cm, Y up, Armature -90 X), one take, frames 0..31
    R.set_action(arm, act); session = {}
    for f in range(f0, f1 + 1):
        sc.frame_set(f); session[f] = {pb.name: np.array(pb.matrix) for pb in arm.pose.bones}
    for fc in fcs:
        for k in fc.keyframe_points: k.co.x -= 1; k.handle_left.x -= 1; k.handle_right.x -= 1
        fc.update()
    for a in list(bpy.data.actions):
        if a != act: bpy.data.actions.remove(a)
    sc.frame_start = f0 - 1; sc.frame_end = f1 - 1; sc.frame_set(f0 - 1)
    with_mesh = '--no-mesh' not in ARGS
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
    if with_mesh: mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    target = REPO + '/' + CFG['stage'] + '/' + m['fixed']; os.makedirs(os.path.dirname(target), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, object_types={'ARMATURE', 'MESH'} if with_mesh else {'ARMATURE'}, use_mesh_modifiers=False,
                             add_leaf_bones=False, use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X',
                             path_mode='STRIP', embed_textures=False, mesh_smooth_type='OFF')
    out['export'] = {'file': CFG['stage'] + '/' + m['fixed'], 'sha256': sha(target), 'bytes': os.path.getsize(target), 'with_mesh': with_mesh,
                     'header': fbx_header(target), 'source_header': fbx_header(src(m['walk']))}
    # ---- round trip: read the exported file back and compare rest pose and every frame with the session
    arm2, mesh2 = R.load(target); act2 = bpy.data.actions[0]; sc = bpy.context.scene
    rest_rot = 0.0; rest_pos = 0.0
    for b in arm2.data.bones:
        A0 = Matrix(rest0[b.name]); B0 = b.matrix_local
        rest_rot = max(rest_rot, fold(math.degrees(A0.to_3x3().normalized().to_quaternion().rotation_difference(B0.to_3x3().normalized().to_quaternion()).angle)))
        rest_pos = max(rest_pos, (A0.translation - B0.translation).length)
    R.set_action(arm2, act2); g0, g1 = int(round(act2.frame_range[0])), int(round(act2.frame_range[1])); rt_rot = 0.0; rt_pos = 0.0
    for f in range(g0, g1 + 1):
        sc.frame_set(f)
        if f not in session: continue
        for pb in arm2.pose.bones:
            A0 = Matrix(session[f][pb.name]); B0 = pb.matrix
            rt_rot = max(rt_rot, fold(math.degrees(A0.to_3x3().normalized().to_quaternion().rotation_difference(B0.to_3x3().normalized().to_quaternion()).angle)))
            rt_pos = max(rt_pos, (A0.translation - B0.translation).length)
    out['roundtrip'] = {'action': act2.name, 'frames': [g0, g1], 'fps': sc.render.fps, 'armature_scale': [round(x, 6) for x in arm2.scale], 'source_armature_scale': arm_scale0,
                        'bones': len(arm2.data.bones), 'bone_names_equal': sorted(b.name for b in arm2.data.bones) == sorted(rest0.keys()),
                        'rest_max_rotation_deg': round(rest_rot, 5), 'rest_max_position_cm': round(rest_pos, 5),
                        'pose_max_rotation_deg': round(rt_rot, 5), 'pose_max_position_cm': round(rt_pos, 5),
                        'pass': bool(rest_rot < .05 and rest_pos < .01 and rt_rot < .1 and rt_pos < .02 and [g0, g1] == [f0, f1] and sc.render.fps == fps)}
    json.dump(out, open(OUT + '/%s_fix.json' % mid, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print('FIX DONE', mid, 'roundtrip', out['roundtrip'])


cmd, mid = ARGS[0], ARGS[1]
if mid not in CFG['monsters']: raise SystemExit('unknown id ' + mid)
{'inspect': inspect, 'measure': measure, 'fix': fix}[cmd](mid)
