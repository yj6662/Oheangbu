"""#308 GreatBrush motion2: solve_m2 출력에서 요약만 짧게."""
import sys, json
DROP = ('l_on_curve', 'grip_rel_matrix', 'knots', 'use', 'name', 'src', 'carry')
for l in sys.stdin:
    if 'M1 summary' in l:
        d = json.loads(l.split('summary', 1)[1]); print(d['name'], {k: v for k, v in d.items() if k not in DROP})
    elif 'Error' in l or 'Traceback' in l or l.startswith('  File') or 'M1 twist' in l: print(l.rstrip())
