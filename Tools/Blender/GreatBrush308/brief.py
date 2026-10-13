"""#308 GreatBrush: pose.py 출력에서 자세별 핵심 수치만 한 줄씩(조정용)."""
import json, sys
for l in sys.stdin:
    if l.startswith('POSE metrics'):
        m = json.JSONDecoder().raw_decode(l[13:])[0]; o = [m['pose'], 'cost', m.get('cost')]
        for s in ('Right', 'Left'):
            if 'arm_' + s not in m: continue
            a = m['arm_' + s]
            o.append('\n   %s[slant %d phi %d reach %.2f elbow %d swing %d flex %d dev %d twist %d sock %.1fcm in3mm %d gap %.0fmm]' % (
                s[0], a['slant'], a['phi'], a['reach'], a['elbow_deg'], a['wrist_swing_deg'], a['wrist_flex_deg'], a['wrist_dev_deg'], a['forearm_twist_deg'],
                a['grip_to_socket_cm'], a['hand_verts_inside_shaft_3mm'], a['hand_skin_min_gap_mm']))
        o.append('\n   inside shaft %d head %d %s clr torso %.1f/%.1f cm tip %s tail %s knees %d/%d minz %.2f' % (
            m['shaft_inside_body_verts'], m['head_inside_body_verts'], m['inside_by_bone'], m['shaft_clearance_torso_cm'], m['bristle_clearance_torso_cm'],
            m['tip'], m['tail'], m['leg_Left']['knee_deg'], m['leg_Right']['knee_deg'], m['min_body_z']))
        print(' '.join(str(x) for x in o))
    elif 'grip' not in l and 'engines' not in l: print(l.rstrip()[:300])
