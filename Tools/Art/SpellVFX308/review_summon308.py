"""#308 6th pass (D308-83b): the ink body of a summon, in edit-mode poses. W_Demo_Main must be open.

  python Tools/Art/SpellVFX308/review_summon308.py [deer]

Row 1: the summon clock - rising out of the ink (0.5 s), complete with the core lifted for its two cels (1.15 s),
       standing (2.4 s, core as plain pigment), scattering (4.2 s).
Row 2: standing at 2.4 s from the side, three-quarter front, behind, and with the core lifted (the attack moment).
Output: Art/SpellVFX308/Review/summon_<key>_r1.png
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
BUILD = 'Oheangbu.EditorTools.SpellVFX120.Vfx308Build'
FEET, FWD = '3340,100,2692', '1,0,0'
SIDE = ('3340.3,99.7,2697.2', '3340.3,99.6,2692')
FRONT = ('3344.6,99.9,2695.4', '3340.2,99.6,2692')
BACK = ('3335.6,100.0,2695.0', '3340.2,99.6,2692')


def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT / 'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.stdout


def shot(name, view):
    call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{view[0]}:{view[1]}:fov=40:w=1280:h=720:hideplayer')
    return Image.open(ROOT / 'Art/Presentation297/Stills' / f'{name}.png').convert('RGB').crop((240, 60, 1040, 660)).resize((480, 360))


def main():
    key = sys.argv[1] if len(sys.argv) > 1 else 'deer'
    plan = [[('0.5', '', SIDE), ('1.15', '', SIDE), ('2.4', '', SIDE), ('4.2', '', SIDE)],
            [('2.4', '', FRONT), ('2.4', '', BACK), ('2.4', ':glow', SIDE), ('2.4', ':glow', FRONT)]]
    rows = []
    for y, line in enumerate(plan):
        row = []
        for x, (age, glow, view) in enumerate(line):
            out = call(BUILD, f'summon:pose:{key}:{age}{glow}:{FEET}:{FWD}')
            if 'summon pose' not in out:
                print('FAIL', age, glow, out[-300:])
            row.append(shot(f'summon_{key}_{y}{x}', view))
        rows.append(row)
    call(BUILD, 'summon:pose:clear')
    sheet = Image.new('RGB', (480 * 4, 360 * len(rows)))
    for y, row in enumerate(rows):
        for x, im in enumerate(row):
            sheet.paste(im, (x * 480, y * 360))
    out = ROOT / f'Art/SpellVFX308/Review/summon_{key}_r2.png'
    sheet.save(out)
    print(out)


if __name__ == '__main__':
    main()
