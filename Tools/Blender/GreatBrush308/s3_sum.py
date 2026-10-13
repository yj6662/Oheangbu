import sys, re
t = sys.stdin.read()
for k in ('shaft_in_frames', 'l_on_share', 'torso_yaw_vs_first_deg', 'ankle_move_cm_max', 'tip_span_xyz_m', 'tip_speed_curve', 'aim_r_reach_max', 'aim_r_reach_over_frames', 'twist_deg'):
    m = re.search(r"'%s': (\[[^\]]*\]|[^,}]*)" % k, t); print(k, m.group(1) if m else None)
if 'Error' in t or 'Trace' in t: print(t[-1500:])
