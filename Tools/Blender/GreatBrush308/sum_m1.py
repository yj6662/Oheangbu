"""#308 GreatBrush motion1: solve_m1 출력에서 요약 줄만 짧게.  … | python sum_m1.py"""
import sys, json
K = ('clip', 'seconds', 'planar_travel_m', 'feet', 'ankle_err_cm', 'min_body_z', 'max_rot_step_deg', 'src_hand_dist', 'hand_dist', 'l_on_share', 'l_off_frames', 'r_pull_cm', 'l_reach', 'l_elbow_deg', 'l_wrist_swing_deg', 'grip_err_cm_L', 'shaft_in_frames', 'shaft_in_max', 'head_in_frames', 'tip_speed_max', 'tip_speed_max_frame', 'tip_z', 'bend_deg_max')
for l in sys.stdin:
    if 'M1 summary' in l:
        d = json.loads(l.split('summary', 1)[1]); d['max_rot_step_deg'] = d['max_rot_step_deg'][:3]; print({k: d.get(k) for k in K})
    elif 'Error' in l or 'Traceback' in l or l.startswith('  File'): print(l.rstrip())
