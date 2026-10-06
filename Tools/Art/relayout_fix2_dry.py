#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D308-16 look fixes (Tools/Unity/Stage308_relayout_fix2) - the offline dry runs of the sub-stages, behind one door. Read only.

  python Tools/Art/relayout_fix2_dry.py list
  python Tools/Art/relayout_fix2_dry.py <substage> <that dry's own arguments>      e.g.  props dry --scene main   |   props mutations
  python Tools/Art/relayout_fix2_dry.py all                                         every registered sub-stage: its dry, then its mutations

A sub-stage registers by a row in DRIES: (name, script under Tools/Unity/Stage308_relayout_fix2/, dry arguments, mutation arguments).
Each script is run as its own process (its exit code: 0 green, 2 a FAIL / a mutation that did not turn red). Nothing is imported
here, so one sub-stage cannot break another. Add a row when a sub-stage gains a dry; do not put checks into this file.
"""
import subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / 'Tools' / 'Unity' / 'Stage308_relayout_fix2'
DRIES = [
    ('props', 'props/props_fix2_dry.py', ['dry'], ['mutations']),          # L2 stones, L3 wreck (off), L4 two fallen doors, L6 trees, L7 crystal (33 mutations)
    ('lantern', 'lantern/lantern308_dry.py', ['--mutations'], []),         # L5 the lantern paper (15 mutations inside the dry run)
    ('amneung', '../../Art/amneung308_look_dry.py', ['--with-base'], []),  # L1 the rock band look (its mutations: amneung/amneung308_look_mutations.py, about 15 min)
]


def call(script, args): return subprocess.call([sys.executable, str(STAGE / script)] + list(args), cwd=str(ROOT))


def main():
    a = sys.argv[1:]
    if not a or a[0] == 'list':
        for n, s, d, m in DRIES: print('%-10s %s  %s  (dry: %s | mutations: %s)' % (n, s, 'ok' if (STAGE / s).is_file() else 'MISSING', ' '.join(d), ' '.join(m) or '-'))
        return 0
    if a[0] == 'all':
        worst = 0
        for n, s, d, m in DRIES:
            for args in (d, m):
                if not args: continue
                rc = call(s, args); print('== %s %s -> exit %d' % (n, ' '.join(args), rc)); worst = max(worst, rc)
        return worst
    row = next((r for r in DRIES if r[0] == a[0]), None)
    if row is None: print('unknown sub-stage %s (list)' % a[0]); return 2
    return call(row[1], a[1:] or row[2])


if __name__ == '__main__':
    sys.exit(main())
