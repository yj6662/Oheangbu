"""#308 - footprints of the dungeons and the realm outbuildings, for Dungeon308 veg:clear (plants inside them are taken out of the sheets).
  python Tools/Art/footprints308.py   # -> Tools/Unity/Stage308_dungeon/footprints308.json
"""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B

SHEETS = ['Assets/_Project/Art/World/Architecture296/Data/757de110844e3b4479c04f9c49d8a6b6_DryLandscape.asset',
          'Assets/_Project/Art/World/Architecture296/Data/9be35ffe935cc2a48a2c9e57deed2af6_fb0262ee6f18ce44db0b6468d94e5e6f_Dressing.asset'] + \
         ['Assets/_Project/Art/World/Flora308/Flora308_%s.asset' % r for r in ('Cheongrim', 'Hwanggyeong', 'Cheolong', 'Jeokro', 'Hyeongang')]


def main():
    feet = []; D = B.RB / 'Dungeon308'
    for i in json.loads((D / 'index.json').read_text(encoding='utf-8'))['dungeons']:
        L = json.loads((D / i / 'layout.json').read_text(encoding='utf-8')); s = L['site']
        if L.get('underground'):      # only the open stair pit touches the surface: the lattice cells of the hole, one cell of margin
            for i_, j_ in L['holes']: feet.append(dict(x=j_ * 4.0 + 2.0, z=i_ * 4.0 + 2.0, yaw=0.0, w=10.0, d=10.0))
        else: feet.append(dict(x=s['x'], z=s['z'], yaw=s['yaw'], w=L['cols'] * L['cell'][0] + 5.0, d=2 * L['cell'][1] + 9.0))      # + the entrance steps
    for h in ([] if '--pits' in sys.argv else json.loads((B.RB / 'Hamlet308/layout.json').read_text(encoding='utf-8'))['houses']):      # --pits: only the stair pits (the houses were cleared in pass 1)
        feet.append(dict(x=h['x'], z=h['z'], yaw=h['yaw'], w=h['w'] + 3.0, d=h['d'] + 4.0))
    out = B.ROOT / 'Tools/Unity/Stage308_dungeon/footprints308.json'; out.write_text(json.dumps(dict(sheets=SHEETS, feet=feet), ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    print('footprints308:', len(feet), '->', out)


if __name__ == '__main__':
    main()
