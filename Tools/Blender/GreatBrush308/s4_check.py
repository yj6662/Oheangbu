"""#308 s4: 열 클립 수치 검사(읽기만). metrics/<이름>.json + metrics/brush_truth.json(+ 백업본) → metrics/s4_check.json + 짧은 표.   python s4_check.py"""
import json, math, os
M = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2'))
import sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clips_m2
T = json.load(open(M + '/metrics/brush_truth.json', encoding='utf-8'))['clips']; T0 = json.load(open(M + '/_backup_swing3/metrics/brush_truth.json', encoding='utf-8'))['clips']
NAMES = ['StanceIdle', 'ToStance', 'ToCarry', 'SwingSweep', 'SwingArc', 'SwingSlam', 'GuardIn', 'GuardHold', 'BuffDraw', 'CarryIdle']
def pt(t, clip, name, f): return t[clip]['points'][name]['blender'][f]
def dist(a, b): return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))
PTS = list(T['StanceIdle']['points'].keys()); BODY = [p for p in PTS if p not in ('TipSocket',)]
def seam(ca, fa, cb, fb, t2=None): return round(1000 * max(dist(pt(T, ca, p, fa), pt(t2 or T, cb, p, fb)) for p in BODY), 2)
out = {}
for n in NAMES:
    J = json.load(open('%s/metrics/%s.json' % (M, n), encoding='utf-8')); S = J['summary']; F = J['frames']; sp = clips_m2.CLIPS[n]; nf = len(F)
    on = [m for m in F if m['l_on'] >= 1]; tr = T[n]['points']
    # 오른손 쥠 점(자루 위) 미끄러짐: 오른손 관절 → GripSocket 거리의 변화(mm) / 왼손 관절 → GripSocketL 거리(쥔 동안)
    rg = [dist(tr['RightHand']['blender'][f], tr['GripSocket']['blender'][f]) for f in range(nf)]; lg = [dist(tr['LeftHand']['blender'][f], tr['GripSocketL']['blender'][f]) for f in range(nf)]
    lon = [lg[m['frame']] for m in on]
    r = dict(length_s=S['seconds'], frames=nf, loop=bool(sp.get('loop')), apex_s=sp.get('apex'), peak_tip_speed_mps=S['peak_tip_speed'], peak_tip_speed_s=S['peak_tip_speed_t'],
             tip_speed_at_apex_mps=round(F[int(round(sp['apex'] * 30))]['tip_speed'], 1) if sp.get('apex') is not None else None,
             tip_speed_2f_after_apex_mps=round(F[min(nf - 1, int(round(sp['apex'] * 30)) + 2)]['tip_speed'], 1) if sp.get('apex') is not None else None,
             tip_span_from_behind_m=dict(lateral=S['tip_span_xyz_m'][0], vertical=S['tip_span_xyz_m'][2], depth=S['tip_span_xyz_m'][1]), torso_yaw_vs_first_deg=S['torso_yaw_vs_first_deg'],
             right_grip_drift_mm_deg=S['grip_rel_drift'], right_wrist_to_gripsocket_mm_range=[round(min(rg) * 1000, 1), round(max(rg) * 1000, 1)],
             left_on_share=S['l_on_share'], left_hand_centre_to_socketL_err_cm_max_while_on=S['l_socket_err_cm_max'], left_wrist_to_socketL_mm_range_while_on=[round(min(lon) * 1000, 1), round(max(lon) * 1000, 1)] if lon else None,
             shaft_through_body_frames=S['shaft_in_frames'], shaft_through_body_max_verts=S['shaft_in_max'], bones=S['in_by_bone'], head_hit_frames=S['head_hit_frames'], bristle_in_body_frames=S['bristle_in_frames'],
             first_frame_vs_StanceIdle0_mm=seam(n, 0, 'StanceIdle', 0), last_frame_vs_StanceIdle0_mm=seam(n, nf - 1, 'StanceIdle', 0), loop_first_vs_last_mm=seam(n, 0, n, nf - 1) if sp.get('loop') else None, loop_err_deg=S['loop_err_deg'])
    out[n] = r
out['_seams'] = dict(GuardIn_last_vs_GuardHold0_mm=seam('GuardIn', len(T['GuardIn']['points']['Hips']['blender']) - 1, 'GuardHold', 0), ToCarry_last_vs_CarryIdle0_mm=seam('ToCarry', len(T['ToCarry']['points']['Hips']['blender']) - 1, 'CarryIdle', 0),
                     ToStance_first_vs_CarryIdle0_mm=seam('ToStance', 0, 'CarryIdle', 0), CarryIdle0_vs_backup_CarryIdle0_mm=seam('CarryIdle', 0, 'CarryIdle', 0, T0))
g = lambda c, p: pt(T, c, p, 0)
def along(c, a, b):   # a → b 를 자루 방향(GripSocket → BrushRootZ10cm)으로 잰 부호 있는 거리
    d = [(y - x) / .10 for x, y in zip(g(c, 'GripSocket'), g(c, 'BrushRootZ10cm'))]; return round(sum((q - p) * k for p, q, k in zip(a, b, d)), 4)
out['_hold'] = dict(gripsocket_to_gripsocketL_along_shaft_m=along('StanceIdle', g('StanceIdle', 'GripSocket'), g('StanceIdle', 'GripSocketL')), gripsocket_to_shaft_end_m=along('StanceIdle', g('StanceIdle', 'GripSocket'), g('StanceIdle', 'ShaftEndSocket')),
                    gripsocket_to_right_hold_point_m=-__import__('json').load(open(M + '/grip.json', encoding='utf-8'))['brush']['right_hand_hold_behind_gripsocket_m'],
                    stance0=dict(tip=g('StanceIdle', 'TipSocket'), right_wrist=g('StanceIdle', 'RightHand'), left_wrist=g('StanceIdle', 'LeftHand'), head=g('StanceIdle', 'Head'), hips=g('StanceIdle', 'Hips'), right_foot=g('StanceIdle', 'RightFoot'), left_foot=g('StanceIdle', 'LeftFoot')))
json.dump(out, open(M + '/metrics/s4_check.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for n in NAMES:
    r = out[n]; print('CK %-10s %.2fs apex %s | peak %.1f m/s @%.2f, at apex %s, +2f %s | span lat %.2f vert %.2f | yaw %s | Rdrift %s | Lerr %.2f cm on %.0f%% | in %d fr (max %d %s) head %d | seam0 %.1f end %.1f loop %s' % (
        n, r['length_s'], r['apex_s'], r['peak_tip_speed_mps'], r['peak_tip_speed_s'], r['tip_speed_at_apex_mps'], r['tip_speed_2f_after_apex_mps'], r['tip_span_from_behind_m']['lateral'], r['tip_span_from_behind_m']['vertical'], r['torso_yaw_vs_first_deg'],
        r['right_grip_drift_mm_deg'], r['left_hand_centre_to_socketL_err_cm_max_while_on'], 100 * r['left_on_share'], r['shaft_through_body_frames'], r['shaft_through_body_max_verts'], ','.join(r['bones']), r['head_hit_frames'], r['first_frame_vs_StanceIdle0_mm'], r['last_frame_vs_StanceIdle0_mm'], r['loop_first_vs_last_mm']))
print('CK seams', out['_seams']); print('CK hold', {k: v for k, v in out['_hold'].items() if k != 'stance0'}); print('CK stance0', out['_hold']['stance0'])
