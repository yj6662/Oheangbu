"""#308 s4: solve_m2 출력에서 판정에 쓰는 수치만 짧게 뽑는다(표준 입력)."""
import json, sys
for ln in sys.stdin:
    ln = ln.rstrip()
    if ln.startswith('M1 summary'):
        S = json.loads(ln[11:]); ra = S.get('right_arm', {}); c = S.get('l_on_curve', [])
        def mm(v): return [min(v), max(v)] if v else None
        print('S4', S['name'], 'frames', S['frames'], 'sec', S['seconds'], '| peak tip', S['peak_tip_speed'], 'm/s @', S['peak_tip_speed_t'], '| tip span xyz', S.get('tip_span_xyz_m'), '| torso yaw vs f0', S.get('torso_yaw_vs_first_deg'))
        print('S4  grip drift mm/deg', S['grip_rel_drift'], '| L on', S['l_on_share'], 'off frames', [i for i, w in enumerate(c) if w < 1][:12], '| L sock err cm', S['l_socket_err_cm_max'], '| L reach', S['l_reach'], 'elbow', S['l_elbow_deg'], 'wrist', S['l_wrist_swing_deg_max'], 'Lswivel', sorted(set(S.get('l_swivel', []))))
        print('S4  R reach', mm(ra.get('r_reach')), 'wrist', mm(ra.get('r_wrist')), 'swivel', sorted(set(ra.get('r_swivel', []))), '| shaft_in frames', S['shaft_in_frames'], 'max', S['shaft_in_max'], 'bristle_in', S['bristle_in_frames'], 'head_hit', S['head_hit_frames'], S['in_by_bone'])
        print('S4  start/end vs stance deg', S['start_vs_stance_deg'], S['end_vs_stance_deg'], '| loop', S['loop_err_deg'], '| max rot step', S['max_rot_step_deg'][:3], '| ankle ik err cm', S['ankle_ik_err_cm'], 'min z', S['min_body_z'])
        if ra.get('r_reach') and max(ra['r_reach']) > .97: print('S4  R reach curve', ra['r_reach'])
        if S.get('l_minreach') and max(S['l_minreach']) > .985: print('S4  L minreach', S['l_minreach'])
    elif 'Error' in ln or 'Traceback' in ln or ln.startswith('  File') or ln.startswith('M1 DONE'): print(ln[:300])
    if ln.startswith('M1 DONE'):
        import os
        nm = ln.split()[-1]; M = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2', 'metrics', nm + '.json'), encoding='utf-8'))['frames']
        bad = [(m['frame'], m['shaft_in'] + m['head_in'], ''.join(k[:1] + k[-3:] + str(v) + ' ' for k, v in m['in_by_bone'].items())) for m in M if m['shaft_in'] + m['head_in'] > 0]
        if bad: print('S4  in:', ' | '.join('%d:%s' % (f, b.strip()) for f, c, b in bad)[:700])
        print('S4  tip xz by frame(4):', ' '.join('%d(%.2f,%.2f)' % (m['frame'], m['tip'][0], m['tip'][2]) for m in M[::4])[:500])
