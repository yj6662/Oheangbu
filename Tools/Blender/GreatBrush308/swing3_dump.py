"""#308 swing3: metrics/<이름>.json 의 프레임 값을 짧게.  python swing3_dump.py <이름> [step]"""
import json, os, sys
M = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2'))
d = json.load(open('%s/metrics/%s.json' % (M, sys.argv[1]), encoding='utf-8')); st = int(sys.argv[2]) if len(sys.argv) > 2 else 1
for m in d['frames'][::st]:
    gh = [m['G'][k] - m['d'][k] * .30 for k in range(3)]
    print('f%3d t%.2f src%6.1f tip %5.2f %5.2f %5.2f spd %5.1f d %5.2f %5.2f %5.2f yS %5.1f yH %5.1f hips %5.2f %5.2f %4.2f Gh %5.2f %5.2f %5.2f head %5.2f %5.2f %4.2f lr %.2f le %3.0f re %3.0f lon %.2f pull %4.1f in %d/%d %s' % (m['frame'], m['t'], m['srcf'], *m['tip'], m['tip_speed'], *m['d'], m.get('yaw_spine', 0), m['yaw_hips'], *m['hips'], *gh, *m['head'], m['l_reach'], m['l_elbow_deg'], m['r_elbow_deg'], m['l_on'], m['r_pull_cm'], m['shaft_in'], m['head_in'], m['in_by_bone'] or ''))
