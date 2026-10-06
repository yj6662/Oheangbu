"""#308 enemy run (SPEC-ENEMY-RIG-VERIFY-308, run section): measure the EXPORTED run FBX again, from the file alone - nothing of the
build session is used. Headless, CPU only, NO render.

  B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
  python Tools/resource_guard.py --wait
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRun308/recheck_run308.py -- <id>

Reads the staged run FBX and the repaired walk FBX (both read only). Writes Art/Characters308/DokkaebiVerify/run/<id>_recheck.json.
What it measures on top of the build's own numbers:
  structure   bone names / parents / order and rest matrices against the walk FBX, skin weights, mesh rest shape
  gait        contact by sole height, flight, ball-of-foot speed while the sole is down, sole height, knee, hips travel, loop
  inside      generalised winding number of every arm vertex (weight >= arm_weight_min) against the body surface - a test that does
              not depend on the nearest face normal, with a self-test (points inside the torso must read inside, a far point outside)
  elbow ring  smallest distance to the body of the arm vertices rig308 leaves out of its clearance set (weights below its 0.9)
Vertex sets and the limits come from rig308.py / enemyrun308.json; the thresholds of this file are in enemyrun308.json 'recheck'."""
import bpy, sys, os, json, math, hashlib
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, '..', 'EnemyRig308')); sys.path.insert(0, HERE)
import rig308 as R
import inside308 as INS

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace('\\', '/')
CFG = json.load(open(os.path.join(HERE, 'enemyrun308.json'), encoding='utf-8'))
mid = sys.argv[sys.argv.index('--') + 1]; M = CFG['monsters'][mid]; RC = CFG['recheck']; LIM = CFG['limits']


def sha(p): return hashlib.sha256(open(p, 'rb').read()).hexdigest()


def structure(arm, mesh):
    rig = R.Rig(arm, mesh)
    return {'bones': [(b.name, b.parent.name if b.parent else None) for b in arm.data.bones], 'rest': {b.name: np.array(b.matrix_local) for b in arm.data.bones},
            'V0': rig.V0.copy(), 'W': rig.W.copy(), 'groups': list(rig.groups), 'tris': rig.tris.copy(), 'arm_scale': [round(x, 6) for x in arm.scale]}, rig


walk_path = REPO + '/' + CFG['walk_stage'] + '/' + M['walk']; run_path = REPO + '/' + CFG['stage'] + '/' + M['run']
arm, mesh = R.load(walk_path); sw, _ = structure(arm, mesh)
arm, mesh = R.load(run_path); sr, rig = structure(arm, mesh); act = bpy.data.actions[0]; sc = bpy.context.scene; fps = sc.render.fps
f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1])); N = f1 - f0; dt = 1.0 / fps
out = {'schema': '308.enemyrun.recheck.1', 'id': mid, 'file': CFG['stage'] + '/' + M['run'], 'sha256': sha(run_path), 'walk_sha256': sha(walk_path), 'take': act.name, 'fps': fps, 'frames': [f0, f1], 'cycle_frames': N}
out['structure'] = {'bone_count': len(sr['bones']), 'names_parents_order_equal': sr['bones'] == sw['bones'], 'armature_scale': sr['arm_scale'], 'walk_armature_scale': sw['arm_scale'],
                    'rest_rotation_max_abs_diff': float(max(np.abs(sr['rest'][n][:3, :3] - sw['rest'][n][:3, :3]).max() for n, _ in sr['bones'])),
                    'rest_translation_max_diff_m': float(max(np.abs(sr['rest'][n][:3, 3] - sw['rest'][n][:3, 3]).max() for n, _ in sr['bones']) * arm.scale[0]), 'mesh_rest_max_diff_m': float(np.abs(sr['V0'] - sw['V0']).max()),
                    'weights_max_diff': float(np.abs(sr['W'] - sw['W']).max()), 'groups_equal': sr['groups'] == sw['groups'], 'triangles_equal': bool((sr['tris'] == sw['tris']).all())}

R.set_action(arm, act); ob = arm; frames = []
inside = INS.ArmInside(rig, RC)
heel = {}; toe = {}
for s in R.SIDES:
    y = rig.V0[rig.S[s]['foot'], 1]; heel[s] = y > np.percentile(y, 80); toe[s] = y < np.percentile(y, 20)
loc = {pb.name: [] for pb in ob.pose.bones}
for f in range(f0, f1 + 1):
    sc.frame_set(f); bpy.context.view_layer.update(); V = R.verts_world(mesh); J = rig.joints_now(); fr = {'V': V, 'hips': J['Hips'][0].copy(), 'q': {pb.name: pb.matrix_basis.to_quaternion() for pb in ob.pose.bones}, 'J': {n: J[n][0].copy() for n in J}}
    for pb in ob.pose.bones: loc[pb.name].append(np.array(pb.location))
    for s in R.SIDES:
        idx = rig.S[s]['foot']; fr[s] = {'sole': float(V[idx, 2].min()), 'ball': J[s + 'ToeBase'][0].copy(), 'knee': R.ang(J[s + 'Leg'][0] - J[s + 'UpLeg'][0], J[s + 'Foot'][0] - J[s + 'Leg'][0]),
                                         'thigh': math.degrees(math.atan2(-(J[s + 'Leg'][0] - J[s + 'UpLeg'][0])[1], -(J[s + 'Leg'][0] - J[s + 'UpLeg'][0])[2]))}
    frames.append(fr)
F = frames[:-1]; eps = LIM['contact_eps_cm'] / 100.0
con = {s: np.array([f[s]['sole'] <= eps for f in F]) for s in R.SIDES}; feet = {}
for s in R.SIDES:
    by = np.array([f[s]['ball'][1] for f in frames]); bx = np.array([f[s]['ball'][0] for f in frames]); v = (by[1:] - by[:-1]) / dt; vx = (bx[1:] - bx[:-1]) / dt; m = con[s] & np.roll(con[s], -1)
    kn = np.array([f[s]['knee'] for f in F]); d2 = np.roll(kn, -1) - 2 * kn + np.roll(kn, 1)
    feet[s] = {'contact_frames': [int(i) for i in np.where(con[s])[0]], 'ball_speed_contact_m_s': [round(float(v[m].min()), 4), round(float(v[m].mean()), 4), round(float(v[m].max()), 4)] if m.any() else None,
               'ball_lateral_speed_abs_max_m_s': round(float(np.abs(vx[m]).max()), 4) if m.any() else None, 'ball_slide_cm_per_step': round(float(np.abs(v[m] - M['clip_speed']).sum() * dt * 100), 3) if m.any() else None,
               'sole_min_cm': round(min(f[s]['sole'] for f in F) * 100, 3), 'sole_max_cm': round(max(f[s]['sole'] for f in F) * 100, 2),
               'knee_min_contact_deg': round(float(kn[con[s]].min()), 1) if con[s].any() else None, 'knee_min_deg': round(float(kn.min()), 1), 'knee_max_deg': round(float(kn.max()), 1),
               'knee_max_step_deg': round(float(np.abs(np.roll(kn, -1) - kn).max()), 1), 'knee_max_second_difference_deg': round(float(np.abs(d2).max()), 1), 'thigh_flex_max_deg': round(max(f[s]['thigh'] for f in F), 1)}
flight = ~con['Left'] & ~con['Right']; both = con['Left'] & con['Right']; hz = np.array([f['hips'][2] for f in F]); acc = (np.roll(hz, -1) - 2 * hz + np.roll(hz, 1)) / (dt * dt)
hips = np.array([f['hips'] for f in frames])
out['gait'] = {'seconds': round(N * dt, 4), 'stride_m_at_clip_speed': round(M['clip_speed'] * N * dt, 4), 'steps_per_minute': round(120.0 / (N * dt), 1), 'feet': feet,
               'flight_frames': [int(i) for i in np.where(flight)[0]], 'double_support_frames': int(both.sum()), 'hips_bounce_cm': round(float((hz.max() - hz.min()) * 100), 2),
               'hips_lowest_frame': int(np.argmin(hz)), 'hips_highest_frame': int(np.argmax(hz)), 'hips_accel_flight_mean_m_s2': round(float(acc[flight].mean()), 2) if flight.any() else None,
               'hips_net_travel_cm': round(float(np.linalg.norm(hips[-1] - hips[0]) * 100), 5), 'hips_fore_aft_range_cm': round(float((hips[:, 1].max() - hips[:, 1].min()) * 100), 3),
               'hips_fore_aft_mean_cm': round(float(hips[:-1, 1].mean() * 100), 2)}
names = [pb.name for pb in ob.pose.bones]
def qang(a, b): d = math.degrees(a.rotation_difference(b).angle); return min(d, 360 - d)
steps = np.array([[qang(frames[i]['q'][n], frames[i + 1]['q'][n]) for i in range(N)] for n in names]); d2 = np.abs(np.roll(steps, -1, axis=1) - steps)
out['loop'] = {'first_last_rotation_max_deg': round(max(qang(frames[0]['q'][n], frames[N]['q'][n]) for n in names), 5),
               'first_last_joint_max_cm': round(float(max(np.linalg.norm(frames[0]['J'][n] - frames[N]['J'][n]) for n in names) * 100), 5),
               'first_last_mesh_max_cm': round(float(np.abs(frames[0]['V'] - frames[N]['V']).max() * 100), 5), 'seam_step_change_max_deg': round(float(d2[:, -1].max()), 2), 'inner_step_change_max_deg': round(float(d2[:, :-1].max()), 2)}
out['channels'] = {'non_hips_bone_translation_max_m': float(max(np.abs(np.array(loc[n])).max() for n in names if n != 'Hips') * arm.scale[0]),
                   'bone_length_change_max_m': float(max(abs(np.linalg.norm(f['J'][c] - f['J'][p]) - np.linalg.norm(frames[0]['J'][c] - frames[0]['J'][p])) for f in frames for c, p in sr['bones'] if p))}

# ---- inside test by winding number + the elbow ring clearance (inside308.py: the same test the builder's clearance search uses)
selftest, self_ok = inside.self_test(F[0]['V'], F[0]['J']); pen = INS.fold([inside.measure(f['V'], True) for f in F])
out['inside'] = {'rule': 'generalised winding number > %s of a forearm / hand vertex (weight >= %s) against the surface of everything but that arm' % (RC['winding_inside'], RC['arm_weight_min']),
                 'self_test_winding': selftest, 'self_test_ok': self_ok, 'vertices_tested': {s: int(len(inside.fore[s])) for s in R.SIDES}, 'ring_vertices': {s: int(len(inside.ring[s])) for s in R.SIDES},
                 'note': 'ring = arm vertices (weight >= %s, forearm / hand share > %s) that rig308 leaves out of its clearance set (it takes weights >= 0.9); test set = rig308 clearance set, sleeve included' % (RC['arm_weight_min'], RC['ring_fore_weight_min']),
                 **{s: pen[s] for s in R.SIDES}}
lim_ok = {'structure': out['structure']['names_parents_order_equal'] and out['structure']['rest_rotation_max_abs_diff'] < RC['rest_tolerance'] and out['structure']['rest_translation_max_diff_m'] < RC['rest_tolerance'] and out['structure']['mesh_rest_max_diff_m'] < RC['rest_tolerance'] and out['structure']['weights_max_diff'] < RC['rest_tolerance'] and out['structure']['triangles_equal'],
          'loop': out['loop']['first_last_rotation_max_deg'] <= LIM['loop_deg'] and out['loop']['first_last_joint_max_cm'] < RC['loop_joint_cm'], 'in_place': out['gait']['hips_net_travel_cm'] < RC['loop_joint_cm'],
          'sole': min(feet[s]['sole_min_cm'] for s in R.SIDES) >= LIM['sole_min_cm'], 'slide': all(feet[s]['ball_slide_cm_per_step'] is not None and feet[s]['ball_slide_cm_per_step'] <= LIM['slide_per_step_cm'] for s in R.SIDES),
          'knee_guard': all(feet[s]['knee_min_contact_deg'] is not None and feet[s]['knee_min_contact_deg'] >= M['knee']['stance_min_bend'] - .5 for s in R.SIDES),
          'inside_none': out['inside']['self_test_ok'] and all(pen[s]['inside_vertices_max'] == 0 for s in R.SIDES), 'only_hips_translates': out['channels']['non_hips_bone_translation_max_m'] < RC['rest_tolerance']}
out['pass'] = lim_ok
json.dump(out, open(REPO + '/' + CFG['out'] + '/%s_recheck.json' % mid, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print('RECHECK', mid, 'pass', lim_ok, flush=True)
print('RECHECK', mid, 'gait', {k: v for k, v in out['gait'].items() if k != 'feet'}, flush=True)
for s in R.SIDES: print('RECHECK', mid, s, feet[s], pen[s], flush=True)
print('RECHECK', mid, 'structure', out['structure'], 'loop', out['loop'], 'channels', out['channels'], 'inside self-test', out['inside']['self_test_winding'], flush=True)
