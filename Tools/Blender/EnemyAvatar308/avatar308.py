"""#308 enemy rig revision 2 (SPEC-ENEMY-RIG-VERIFY-308 '개정 2'): cause check and candidate A (T-pose reference), offline.
Pure numpy; reads the Blender dump (dump308.py), the rig .meta files, copies of Unity's import artifacts and the editor's measure JSON.
Writes only under Art/Characters308/EnemyRigVerify2 and Tools/Unity/Stage308_enemyrig2 (never under Assets).

  python Tools/Blender/EnemyAvatar308/avatar308.py cause     -> EnemyRigVerify2/cause308.json   (frames Unity stored, floors, the rule check)
  python Tools/Blender/EnemyAvatar308/avatar308.py tpose     -> EnemyRigVerify2/tpose308.json + stage Data/avatarA308.json + stage _Expected/*.meta
  python Tools/Blender/EnemyAvatar308/report308.py           -> EnemyRigVerify2/REPORT_ko.md (Korean tables from the two JSON files)
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import humanoid308 as H, avatarblob308 as AB

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace(os.sep, '/')
CFG = json.load(open(os.path.join(HERE, 'avatar308.json'), encoding='utf-8'))
OUT = REPO + '/' + CFG['out']; STAGE = REPO + '/' + CFG['stage']
SIDES = ('Left', 'Right')
CLIPS = ['idle', 'walk', 'walkfix', 'run', 'attack', 'hit', 'stun', 'death']
r2 = lambda v: round(float(v), 2)


def blob_axes(mid, dump):
    blob = AB.read_axes(REPO + '/' + CFG['work'] + '/artifacts/' + mid + '_rig.artifact'); seq = H.human_order(dump); ax = {}
    for n, (pa, a) in enumerate(blob['nodes'][1:], 1): ax[seq[n - 1]] = blob['axes'][a]
    return blob, ax, seq


def source_measures(dump, av, clip):
    """Arm measures of the clip as the FBX holds it (joint positions by forward kinematics on the avatar skeleton)."""
    rows = []
    for L, hp in H.clip_locals(dump, av, clip):
        G, P = av.fk(L, hp); m = H.arm_measures(P, av); m['P'] = P; m['L'] = L; rows.append(m)
    return rows


def fold(rows, side):
    f = lambda k: np.array([r[side][k] for r in rows])
    return {'elbowMin': r2(f('elbow').min()), 'elbowMax': r2(f('elbow').max()), 'abductionMean': r2(f('abduction').mean()), 'abductionMin': r2(f('abduction').min()),
            'abductionMax': r2(f('abduction').max()), 'swingRange': r2(np.ptp(f('swing')))}


# ------------------------------------------------------------------------------------------------------------- cause
def cause():
    out = {'schema': '308.enemyrig2.cause.1', 'status': '[O] offline: Unity import artifacts (read-only copies) + rig .meta + Blender dump; editor numbers are quoted from measure_<id>.json [M]',
           'monsters': {}}
    for mid in CFG['monsters']:
        dump, av, rows, order, text = H.load_monster(REPO, CFG, mid); blob, ax, seq = blob_axes(mid, dump)
        meas = json.load(open(REPO + '/' + CFG['unityMeasure'] + '/measure_%s.json' % mid, encoding='utf-8'))['sets']
        m = {'referencePose': {}, 'storedTPoseVsMetaDeg': 0.0, 'ruleVsStoredDeg': {}, 'elbowFloor': {}, 'kneeFloor': {}, 'clips': {}}
        # 1) the T-pose Unity kept in the avatar = the .meta rows?
        worst = 0.0
        for n, i in enumerate(seq, 1):
            if dump.bones[i] == 'Hips': continue
            worst = max(worst, H.rot_angle(H.q2m(*blob['pose'][n]['q']).T @ av.L[i]))
        m['storedTPoseVsMetaDeg'] = round(worst, 4)
        # 2) the rule against the stored frames
        fr = H.unity_frames(av); dpre = 0.0; dpost = 0.0
        for human, bone in H.HUMAN.items():
            if human == 'Hips': continue
            a = ax[dump.bones.index(bone)]
            dpre = max(dpre, H.rot_angle(H.q2m(*a['pre']).T @ fr[human]['pre'])); dpost = max(dpost, H.rot_angle(H.q2m(*a['post']).T @ fr[human]['post']))
        m['ruleVsStoredDeg'] = {'preMax': round(dpre, 3), 'postMax': round(dpost, 3)}
        # 3) reference pose numbers and floors (from the STORED frames)
        for s, side in enumerate(SIDES):
            iS = av.idx(side + 'Shoulder'); iU = av.idx(side + 'UpperArm'); iE = av.idx(side + 'LowerArm'); iH = av.idx(side + 'Hand')
            ua = H.unit(av.P[iE] - av.P[iU]); fa = H.unit(av.P[iH] - av.P[iE])
            m['referencePose'][side] = {'upperArmBelowHorizontalDeg': r2(math.degrees(math.asin(-ua[1]))), 'forearmBelowHorizontalDeg': r2(math.degrees(math.asin(-fa[1]))),
                                        'elbowBendDeg': r2(H.angle(ua, fa)), 'upperArmDir': [round(float(x), 3) for x in ua], 'forearmDir': [round(float(x), 3) for x in fa]}
            pre = H.q2m(*ax[iE]['pre']); a = H.unit(av.lp[iE]); stored = math.degrees(math.asin(abs(float(a @ pre[:, 2]))))
            floors = [meas[k]['elbowMinDeg'][s] for k in meas if k.endswith('/off')]; ceil = [meas[k]['elbowMaxDeg'][s] for k in meas if k.endswith('/off')]
            m['elbowFloor'][side] = {'fromStoredFramesDeg': round(stored, 2), 'fromRuleDeg': round(H.mid_floor(av, fr, side + 'UpperArm', side + 'LowerArm'), 2),
                                     'editorSmallestElbowDeg': round(min(floors), 2), 'editorLargestElbowDeg': round(max(ceil), 2), 'ceilingIfPlaneDeg': round(180 - stored, 2)}
            iK = av.idx(side + 'LowerLeg'); preK = H.q2m(*ax[iK]['pre']); t = H.unit(av.lp[iK])
            m['kneeFloor'][side] = round(math.degrees(math.asin(abs(float(t @ preK[:, 2])))), 2)
        # 4) per clip: source (FBX) against the editor (relax off)
        for clip in CLIPS:
            if clip not in dump.clips: continue
            src = source_measures(dump, av, clip); key = ('walkfix' if clip == 'walkfix' else clip) + '/off'; c = {}
            for s, side in enumerate(SIDES):
                f = fold(src, side); floor = m['elbowFloor'][side]['fromStoredFramesDeg']
                el = np.array([r[side]['elbow'] for r in src]); f['framesUnderFloorPct'] = round(float((el < floor).mean() * 100), 1)
                if key in meas:
                    e = meas[key]; f['editor'] = {'elbowMin': e['elbowMinDeg'][s], 'elbowMax': e['elbowMaxDeg'][s], 'abductionMean': e['abductionMeanDeg'][s], 'swingRange': e['swingRangeDeg'][s]}
                    f['abductionMeanEditorMinusSource'] = r2(e['abductionMeanDeg'][s] - f['abductionMean'])
                    f['elbowClampModelMinusEditor'] = [r2(min(max(f['elbowMin'], floor), 180 - floor) - e['elbowMinDeg'][s]), r2(min(max(f['elbowMax'], floor), 180 - floor) - e['elbowMaxDeg'][s])]
                c[side] = f
            m['clips'][clip] = c
        out['monsters'][mid] = m
    # Spread shift (editor - source, mean over the clip) against the share of frames whose source elbow is under the floor. ALL THREE
    # bands are kept (review F1: the first version quoted only the two outer ones and read "the spread shift is the consequence of the
    # elbow lock" out of them). The middle band holds the counter-examples, and every cell of the top band is a walk clip.
    cells = [{'id': mid, 'clip': clip, 'side': s, 'underFloorPct': c[s]['framesUnderFloorPct'], 'spreadShiftDeg': c[s]['abductionMeanEditorMinusSource'],
              'swingShiftDeg': r2(c[s]['editor']['swingRange'] - c[s]['swingRange']), 'sourceElbow': [c[s]['elbowMin'], c[s]['elbowMax']], 'editorElbow': [c[s]['editor']['elbowMin'], c[s]['editor']['elbowMax']],
              'floorDeg': m['elbowFloor'][s]['fromStoredFramesDeg']}
             for mid, m in out['monsters'].items() for clip, c in m['clips'].items() for s in SIDES if 'editor' in c[s]]
    cells.sort(key=lambda x: (x['underFloorPct'], x['id'], x['clip'], x['side'])); bands = []
    for name, pick in (('<= 5 %', lambda p: p <= 5), ('5 - 80 %', lambda p: 5 < p < 80), ('>= 80 %', lambda p: p >= 80)):
        sel = [x for x in cells if pick(x['underFloorPct'])]; a = [abs(x['spreadShiftDeg']) for x in sel]
        bands.append({'band': name, 'cells': len(sel), 'meanAbsSpreadShiftDeg': r2(np.mean(a)) if a else None, 'minAbsSpreadShiftDeg': r2(min(a)) if a else None, 'maxAbsSpreadShiftDeg': r2(max(a)) if a else None,
                      'clips': sorted({x['clip'] for x in sel})})
    walk = lambda x: x['clip'] in ('walk', 'walkfix')
    out['spreadVsFloor'] = {
        'bands': bands, 'cells': cells,
        # many frames under the floor, yet the spread barely moves (and none of them is a walk)
        'underFloorButSpreadKept': [x for x in cells if x['underFloorPct'] >= 25 and abs(x['spreadShiftDeg']) <= 3],
        # the editor's largest elbow is well above BOTH the source's largest and the floor: the elbow is not simply held at the floor
        'elbowAboveSourceAndFloor': [x for x in cells if x['editorElbow'][1] - max(x['sourceElbow'][1], x['floorDeg']) > 5],
        # no frame under the floor, yet the editor's largest elbow is clearly smaller than the source's
        'elbowReducedAboveFloor': [x for x in cells if x['underFloorPct'] <= 5 and x['sourceElbow'][1] - x['editorElbow'][1] > 5],
        'walkCells': len([x for x in cells if walk(x)]), 'walkAbsSpreadShiftDeg': [r2(min(abs(x['spreadShiftDeg']) for x in cells if walk(x))), r2(max(abs(x['spreadShiftDeg']) for x in cells if walk(x)))],
        'otherCells': len([x for x in cells if not walk(x)]), 'otherCellsOver5Deg': [x for x in cells if not walk(x) and abs(x['spreadShiftDeg']) > 5],
        'reading': 'The elbow floor is proven. The spread shift of 11 - 15 deg shows up in the walk family only; whether the floor causes it is NOT established (mechanism not reproduced, counter-examples in the middle band).'}
    os.makedirs(OUT, exist_ok=True)
    json.dump(out, open(OUT + '/cause308.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    for mid, m in out['monsters'].items():
        print(mid, 'stored T-pose vs .meta %.4f deg | rule vs stored frames pre %.3f post %.3f deg' % (m['storedTPoseVsMetaDeg'], m['ruleVsStoredDeg']['preMax'], m['ruleVsStoredDeg']['postMax']))
        for side in SIDES:
            e = m['elbowFloor'][side]; print('   %-5s elbow floor: stored frames %.2f | rule %.2f | editor smallest %.2f (largest %.2f, plane ceiling %.2f) | knee floor %.2f | ref: upper arm %.1f below horizontal, bend %.1f' % (
                side, e['fromStoredFramesDeg'], e['fromRuleDeg'], e['editorSmallestElbowDeg'], e['editorLargestElbowDeg'], e['ceilingIfPlaneDeg'], m['kneeFloor'][side],
                m['referencePose'][side]['upperArmBelowHorizontalDeg'], m['referencePose'][side]['elbowBendDeg']))
        for clip, c in m['clips'].items():
            print('   %-8s' % clip, ' | '.join('%s under floor %5.1f %% spread shift %s elbow clamp-model err %s' % (s[0], c[s]['framesUnderFloorPct'], c[s].get('abductionMeanEditorMinusSource'), c[s].get('elbowClampModelMinusEditor')) for s in SIDES))
    sv = out['spreadVsFloor']
    for b in sv['bands']: print('spread vs floor | band %-8s cells %2d | abs spread shift mean %s min %s max %s | clips %s' % (b['band'], b['cells'], b['meanAbsSpreadShiftDeg'], b['minAbsSpreadShiftDeg'], b['maxAbsSpreadShiftDeg'], b['clips']))
    print('   under the floor >= 25 %% of the frames but spread kept within 3 deg: %d cells %s' % (len(sv['underFloorButSpreadKept']), [(x['id'], x['clip'], x['side'], x['underFloorPct'], x['spreadShiftDeg']) for x in sv['underFloorButSpreadKept']]))
    print('   editor elbow max above source max and floor by > 5 deg: %s' % [(x['id'], x['clip'], x['side'], x['sourceElbow'][1], x['editorElbow'][1]) for x in sv['elbowAboveSourceAndFloor']])
    print('   no frame under the floor, editor elbow max > 5 deg below the source: %s' % [(x['id'], x['clip'], x['side'], x['sourceElbow'][1], x['editorElbow'][1]) for x in sv['elbowReducedAboveFloor']])
    print('   walk cells %d: abs spread shift %s | other cells %d, of them over 5 deg: %s' % (sv['walkCells'], sv['walkAbsSpreadShiftDeg'], sv['otherCells'], [(x['id'], x['clip'], x['side'], x['underFloorPct'], x['spreadShiftDeg']) for x in sv['otherCellsOver5Deg']]))


# ------------------------------------------------------------------------------------------------------------- candidate A
def f32(v):
    v = float(np.float32(v)); return 0.0 if abs(v) < 1e-7 else float(np.format_float_positional(np.float32(v), unique=True, trim='0'))


def fmt_q(q): return '{x: %s, y: %s, z: %s, w: %s}' % tuple(np.format_float_positional(np.float32(f32(c)), unique=True, trim='-') for c in q)


def tpose():
    out = {'schema': '308.enemyrig2.tpose.1', 'status': '[O] offline construction + [I] prediction: under a T-pose reference the elbow ANGLE is representable (floor 0), and the clip numbers below are what the FBX holds. That the game then shows them is NOT established offline (the step in which Unity re-poses a limb is not reproduced; the hinge direction is not free - Unity turns the upper arm about its own axis to meet it). The editor M3 under candidate A is the only verdict.',
           'monsters': {}}
    data = {'schema': '308.enemyrig2.avatarA.1', 'note': 'SPEC-ENEMY-RIG-VERIFY-308 개정 2 candidate A (TEST): avatar reference pose rows of the SAME skeleton. Only the rotation of four rows changes; no bone, no position, no weight.',
            'monsters': []}
    os.makedirs(STAGE + '/Data', exist_ok=True); os.makedirs(STAGE + '/_Expected', exist_ok=True)
    for mid in CFG['monsters']:
        dump, av, rows, order, text = H.load_monster(REPO, CFG, mid); m = {'arms': {}, 'clips': {}}; newL = [x.copy() for x in av.L]; drow = []
        src_all = {c: source_measures(dump, av, c) for c in CLIPS if c in dump.clips}
        for s, side in enumerate(SIDES):
            iS = av.idx(side + 'Shoulder'); iU = av.idx(side + 'UpperArm'); iE = av.idx(side + 'LowerArm'); iH = av.idx(side + 'Hand')
            y = H.unit(av.lp[iE])                                         # upper-arm direction in its own bone frame
            # the rig's own elbow hinge: normal of the bend plane in the upper-arm bone frame, over every clip frame with a clear bend
            ns = []
            for c, rws in src_all.items():
                for r in rws:
                    d = H.unit(r['L'][iE] @ av.lp[iH]); e = H.angle(y, d)
                    if 20 <= e <= 160: ns.append(H.unit(np.cross(y, d)))
            ns = np.array(ns); n = H.unit(ns.mean(0)); n = H.unit(n - float(n @ y) * y)
            dev = np.degrees(np.arccos(np.clip(ns @ n, -1, 1))); nb = H.unit(np.cross(y, H.unit(av.L[iE] @ av.lp[iH])))
            out_dir = np.array([-1.0, 0, 0]) if s == 0 else np.array([1.0, 0, 0]); up = np.array([0, 1.0, 0]) if s == 0 else np.array([0, -1.0, 0])
            src_frame = np.stack([y, n, np.cross(y, n)], 1); dst_frame = np.stack([out_dir, up, np.cross(out_dir, up)], 1)
            GU = dst_frame @ src_frame.T                                   # upper arm global: bone direction -> the T line, the rig's hinge -> vertical
            newL[iU] = av.G[iS].T @ GU
            newL[iE] = H.arc(H.unit(av.L[iE] @ av.lp[iH]), H.unit(av.lp[iH])) @ av.L[iE]
            # a straight elbow means: the forearm bone direction (its child offset, seen from the upper arm) lies on the upper-arm line
            fore_dir_in_upper = H.unit(newL[iE] @ av.lp[iH])
            if H.angle(fore_dir_in_upper, y) > 1e-3: newL[iE] = H.arc(fore_dir_in_upper, y) @ newL[iE]
            enforce = H.arc(H.unit(av.P[iE] - av.P[iU]), out_dir) @ av.G[iU]   # what a plain shortest-arc 'enforce T-pose' would give
            roll_vs_enforce = H.rot_angle(enforce.T @ GU)
            m['arms'][side] = {'hingeNormalInUpperArmFrame': [round(float(x), 4) for x in n], 'hingeSpreadMedianDeg': r2(np.median(dev)), 'hingeSpreadP90Deg': r2(np.percentile(dev, 90)),
                               'hingeFramesUsed': int(len(ns)), 'bindBendNormalVsHingeDeg': r2(H.angle(nb, n)), 'upperArmRollVsShortestArcDeg': r2(roll_vs_enforce)}
            for i in (iU, iE):
                q = H.m2q(newL[i]); drow.append({'name': dump.bones[i], 'rotation': [f32(c) for c in q], 'liveRotation': [f32(c) for c in rows[dump.bones[i]]['q']]})
        # the new reference pose and what the rule makes of it
        new_rows = {k: dict(v) for k, v in rows.items()}
        for d in drow: new_rows[d['name']] = dict(new_rows[d['name']], q=np.array(d['rotation']))
        avA = H.Avatar(new_rows, dump.bones, dump.parents); frA = H.unity_frames(avA); fr = H.unity_frames(av)
        for side in SIDES:
            iU = avA.idx(side + 'UpperArm'); iE = avA.idx(side + 'LowerArm'); iH = avA.idx(side + 'Hand')
            ua = H.unit(avA.P[iE] - avA.P[iU]); fa = H.unit(avA.P[iH] - avA.P[iE])
            m['arms'][side].update({'referenceUpperArmDir': [round(float(x), 4) for x in ua], 'referenceForearmDir': [round(float(x), 4) for x in fa],
                                    'elbowFloorLiveDeg': round(H.mid_floor(av, fr, side + 'UpperArm', side + 'LowerArm'), 2), 'elbowFloorADeg': round(H.mid_floor(avA, frA, side + 'UpperArm', side + 'LowerArm'), 2),
                                    'kneeFloorDeg_unchanged': round(H.mid_floor(avA, frA, side + 'UpperLeg', side + 'LowerLeg'), 2)})
        # frames of every bone that is not an arm bone must not move
        moved = {h: round(max(H.rot_angle(fr[h]['pre'].T @ frA[h]['pre']), H.rot_angle(fr[h]['post'].T @ frA[h]['post'])), 4) for h in fr}
        m['framesChangedDeg'] = {h: v for h, v in moved.items() if v > 1e-3}
        # predictions under A: the clip as the FBX holds it (joint positions), per clip; reach of the wrist from the shoulder
        scale = CFG['monsters'][mid]['targetHeight'] / float(dump.z['V0'][:, 2].max() - dump.z['V0'][:, 2].min())
        for clip, rws in src_all.items():
            c = {}
            for s, side in enumerate(SIDES):
                f = fold(rws, side); iU = av.idx(side + 'UpperArm'); iE = av.idx(side + 'LowerArm'); iH = av.idx(side + 'Hand'); y = H.unit(av.lp[iE]); n = np.array(m['arms'][side]['hingeNormalInUpperArmFrame'])
                reach = np.array([np.linalg.norm(r[side]['wrist'] - r[side]['shoulder_pos']) for r in rws]) * scale
                fwd = np.array([r[side]['wrist'][2] - r['P'][av.idx('Hips')][2] for r in rws]) * scale
                roll = []
                for r in rws:
                    d = H.unit(r['L'][iE] @ av.lp[iH]); e = H.angle(y, d)
                    if 20 <= e <= 160: roll.append(math.degrees(math.atan2(float(np.cross(n, H.unit(np.cross(y, d))) @ y), float(n @ H.unit(np.cross(y, d))))))
                l1 = float(np.linalg.norm(av.lp[iE])) * scale; l2 = float(np.linalg.norm(av.lp[iH])) * scale; fl = math.radians(m['arms'][side]['elbowFloorLiveDeg'])
                f.update({'reachCapLiveM': round(math.sqrt(l1 * l1 + l2 * l2 + 2 * l1 * l2 * math.cos(fl)), 3), 'reachStraightM': round(l1 + l2, 3), 'reachMaxM': round(float(reach.max()), 3), 'wristForwardOfHipsMaxM': round(float(fwd.max()), 3), 'wristForwardAtFrame': int(fwd.argmax()),
                          'upperArmRollOffsetDeg': [r2(min(roll)), r2(max(roll))] if roll else None})
                c[side] = f
            m['clips'][clip] = c
        out['monsters'][mid] = m
        rig_rel = CFG['monsters'][mid]['rig']; dependents = [CFG['monsters'][mid][k] for k in ('walk', 'motion', 'fixed') if CFG['monsters'][mid].get(k)]
        data['monsters'].append({'id': mid, 'rig': rig_rel.replace('Oheangbu/', '', 1), 'dependents': [d.replace('Oheangbu/', '', 1) for d in dependents],
                                 'rows': [{'name': d['name'], 'x': d['rotation'][0], 'y': d['rotation'][1], 'z': d['rotation'][2], 'w': d['rotation'][3],
                                           'liveX': d['liveRotation'][0], 'liveY': d['liveRotation'][1], 'liveZ': d['liveRotation'][2], 'liveW': d['liveRotation'][3]} for d in drow],
                                 'elbowFloorLiveDeg': [m['arms'][s]['elbowFloorLiveDeg'] for s in SIDES], 'elbowFloorADeg': [m['arms'][s]['elbowFloorADeg'] for s in SIDES]})
        # expected .meta of the rig under A (text substitution of the four rotation lines; reference only - the editor command writes the rows through the importer)
        exp = text
        for d in drow:
            a = exp.index('    - name: %s\n' % d['name'], exp.index('    skeleton:\n')); b = exp.index('      rotation: ', a); e = exp.index('\n', b)
            exp = exp[:b] + '      rotation: ' + fmt_q(d['rotation']) + exp[e:]
        open(STAGE + '/_Expected/' + os.path.basename(rig_rel) + '.meta', 'w', encoding='utf-8', newline='\n').write(exp)
    json.dump(out, open(OUT + '/tpose308.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    json.dump(data, open(STAGE + '/Data/avatarA308.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    for mid, m in out['monsters'].items():
        print(mid, 'frames changed (deg):', m['framesChangedDeg'])
        for side in SIDES:
            a = m['arms'][side]; print('   %-5s floor live %.2f -> A %.2f | hinge spread median %.1f p90 %.1f (%d frames) | bind bend normal vs hinge %.1f | roll vs shortest-arc T %.1f | ref dirs %s %s' % (
                side, a['elbowFloorLiveDeg'], a['elbowFloorADeg'], a['hingeSpreadMedianDeg'], a['hingeSpreadP90Deg'], a['hingeFramesUsed'], a['bindBendNormalVsHingeDeg'], a['upperArmRollVsShortestArcDeg'],
                a['referenceUpperArmDir'], a['referenceForearmDir']))
        for clip, c in m['clips'].items():
            print('   %-8s' % clip, ' | '.join('%s elbow %5.1f..%5.1f spread %5.1f swing %5.1f reach %.3f fwd %.3f roll %s' % (s[0], c[s]['elbowMin'], c[s]['elbowMax'], c[s]['abductionMean'], c[s]['swingRange'], c[s]['reachMaxM'], c[s]['wristForwardOfHipsMaxM'], c[s]['upperArmRollOffsetDeg']) for s in SIDES))


if __name__ == '__main__':
    {'cause': cause, 'tpose': tpose}[sys.argv[1]]()
