"""#308 enemy run (SPEC-ENEMY-RIG-VERIFY-308 "달리기 (D308-23)"): read-only look at the repaired walk FBX of one monster.
Headless, CPU, no render. Prints the skeleton (rest pose in world metres) and the walk's joint tracks.
  blender -b --factory-startup -t 4 --python Tools/Blender/EnemyRun308/inspect_run308.py -- <id>"""
import bpy, sys, os, json, math
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, '..', 'EnemyRig308'))
import rig308 as R

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace('\\', '/')
mid = sys.argv[sys.argv.index('--') + 1]
path = REPO + '/Tools/Unity/Stage308_enemyrig/Art/Characters/Folklore298/Animations/%s/%s-walkfix308_walk_fix308.fbx' % (mid, mid)
arm, mesh = R.load(path); rig = R.Rig(arm, mesh); act = bpy.data.actions[0]; sc = bpy.context.scene
print('INS', mid, 'arm scale', tuple(round(x, 4) for x in arm.scale), 'rot', tuple(round(math.degrees(x), 2) for x in arm.rotation_euler), 'fps', sc.render.fps, 'action', act.name, tuple(act.frame_range))
print('INS height', round(rig.height, 4), 'rest min z', round(rig.rest_min_z, 4))
for b in arm.data.bones:
    r = rig.rest[b.name]; Rm = r['R']
    print('INS bone %-14s parent %-12s head %s tail %s  X %s Y %s Z %s' % (b.name, b.parent.name if b.parent else '-', np.round(r['head'], 3).tolist(), np.round(r['tail'], 3).tolist(),
          np.round(Rm[:, 0], 2).tolist(), np.round(Rm[:, 1], 2).tolist(), np.round(Rm[:, 2], 2).tolist()))
for s in R.SIDES:
    l1 = np.linalg.norm(rig.rest[s + 'Leg']['head'] - rig.rest[s + 'UpLeg']['head']); l2 = np.linalg.norm(rig.rest[s + 'Foot']['head'] - rig.rest[s + 'Leg']['head'])
    a1 = np.linalg.norm(rig.rest[s + 'ForeArm']['head'] - rig.rest[s + 'Arm']['head']); a2 = np.linalg.norm(rig.rest[s + 'Hand']['head'] - rig.rest[s + 'ForeArm']['head'])
    print('INS', s, 'thigh %.3f shin %.3f ankle rest z %.3f toe rest %s | upper arm %.3f forearm %.3f' % (l1, l2, rig.rest[s + 'Foot']['head'][2], np.round(rig.rest[s + 'ToeBase']['head'], 3).tolist(), a1, a2))
    idx = rig.S[s]['foot']; V = rig.V0[idx]
    print('INS', s, 'foot verts rest: y %.3f..%.3f z %.3f..%.3f x %.3f..%.3f' % (V[:, 1].min(), V[:, 1].max(), V[:, 2].min(), V[:, 2].max(), V[:, 0].min(), V[:, 0].max()))
fr = R.sample_clip(rig, act, penetration=False)
fcs = R.fcurves_of(act)
paths = sorted({fc.data_path for fc in fcs})
print('INS fcurves', len(list(fcs)), 'paths', len(paths)); print('INS paths sample', paths[:6], '...')
print('INS keyed non-rotation', [p for p in paths if 'rotation_quaternion' not in p])
for f in fr:
    J = None
    print('INS f%02d hips %s head %s chest %s | L ankle %s sole %.3f knee %.0f | R ankle %s sole %.3f knee %.0f | swingL %.0f swingR %.0f elbL %.0f elbR %.0f' % (
        f['f'], np.round(f['hips'], 3).tolist(), np.round(f['head'], 3).tolist(), np.round(f['chest'], 3).tolist(),
        np.round(f['Left']['ankle'], 3).tolist(), f['Left']['foot_minz'], f['Left']['knee_bend'], np.round(f['Right']['ankle'], 3).tolist(), f['Right']['foot_minz'], f['Right']['knee_bend'],
        f['Left']['swing'], f['Right']['swing'], f['Left']['elbow_angle'], f['Right']['elbow_angle']))
