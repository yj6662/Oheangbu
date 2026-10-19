"""#308 6th pass (D308-83): the ink before the spell, in edit-mode poses. W_Demo_Main must be open.

  python Tools/Art/SpellVFX308/review_deploy6.py

Row 1-2: the sweep and the arc (thrown ink at the apex; the tip path is a stand-in arc, not the clip).
Row 3-4: 가 and 아 leaving - the gathering ink under the body (launch, +0.1 s, +0.2 s, mid flight).
Output: Art/SpellVFX308/Review/deploy6_r1.png
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
FEET, EYE, LOOK = '3340,100,2690', '3343.2,100.3,2693.6', '3340,99.6,2690.8'
ORG, TGT = '3340,99.8,2691.3', '3340,100.2,2703'
BOLT_EYE, BOLT_LOOK = '3343.6,100.4,2692.2', '3340,99.9,2692.4'


def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT / 'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.stdout


def shot(name, eye, look):
    call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{eye}:{look}:fov=50:w=1280:h=720:hideplayer')
    return Image.open(ROOT / 'Art/Presentation297/Stills' / f'{name}.png').convert('RGB').crop((240, 60, 1040, 660)).resize((480, 360))


def main():
    build = 'Oheangbu.EditorTools.SpellVFX120.Vfx308Build'
    rows = []
    for state, times in (('sweep', (.3, .42, .52, .7)), ('arc', (.5, .68, .8, 1.0))):
        row = []
        for t in times:
            out = call(build, f'swingink:pose:{state}:{t}:{FEET}:0,0,1:{EYE}')
            if 'swing pose' not in out:
                print('FAIL', state, t, out[-300:])
            row.append(shot(f'deploy6_{state}_{int(t * 100):03d}', EYE, LOOK))
        rows.append(row)
    call(build, 'swingink:pose:clear')
    for key in ('ga', 'a'):
        row = []
        for when in ('0.05', '0.13', '0.22', 'mid'):
            call(build, f'pose:{key}:{when}:{ORG}:{TGT}')
            row.append(shot(f'deploy6_{key}_{when.replace(".", "")}', BOLT_EYE, BOLT_LOOK))
        rows.append(row)
    call(build, 'pose:clear')
    sheet = Image.new('RGB', (480 * 4, 360 * len(rows)))
    for y, row in enumerate(rows):
        for x, im in enumerate(row):
            sheet.paste(im, (x * 480, y * 360))
    out = ROOT / 'Art/SpellVFX308/Review/deploy6_r1.png'
    sheet.save(out)
    print(out)


if __name__ == '__main__':
    main()
