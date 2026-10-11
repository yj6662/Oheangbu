"""#308 spell VFX overhaul, 4th pass: edit-mode review stills of the four range forms (circle, cone, volley, path).
The Unity editor must be open on W_Demo_Main in Edit mode; nothing is saved into the scene. System Python (PIL).

  python Tools/Art/SpellVFX308/review_range308.py [circle,cone,volley,path]

Per case one row: the 3rd-pass picture (Review/attack50_tiles_r3) | start | peak | going | first-person eye | from farther back.
For the circle the two eye views put the ring 12 m and 15 m away; the others start at the caster, so the far view is the camera
pulled 3 m behind (as the game does after a cast). Output: Art/SpellVFX308/Review/range_<form>.png.
POSES, not Play: Vfx308Build replays the plain AreaRemake branches with a plan made from the rule numbers.
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/SpellVFX308/Review'
ORG = '3343.3,99.95,2691.75'
# form -> (aim point, side camera eye / look, [(label, eye, look) x 2], cases [(letter, effect id)])
FORMS = {
    'circle': dict(aim='3333.3,100,2692', side=('3338.5,103.4,2699', '3333.3,100.4,2692'),
                   eyes=[('first person, ring 12 m away', '3345.3,100.9,2692', '3333.3,100.6,2692'), ('first person, ring 15 m away', '3348.3,100.9,2692', '3333.3,100.6,2692')],
                   cases=[('고', '013_ACE0'), ('곡', '014_ACE1'), ('곤', '015_ACE4')]),
    'cone': dict(aim='3332,100.9,2692', side=('3341.5,103.6,2699', '3339,100.6,2692'),
                 eyes=[('first person', '3344,100.9,2692.3', '3336,100.5,2692'), ('3 m behind the caster', '3347,101.5,2692.7', '3336,100.5,2692')],
                 cases=[('노', '037_B178'), ('논', '039_B17C'), ('농', '042_B18D')]),
    'volley': dict(aim='3332,100.9,2692', side=('3340,103.2,2699.5', '3336,100.8,2692'),
                   eyes=[('first person', '3344,100.9,2692.3', '3334,100.7,2692'), ('3 m behind the caster', '3347,101.5,2692.7', '3334,100.7,2692')],
                   cases=[('소', '085_C18C'), ('손', '087_C190'), ('솟', '089_C19F')]),
    'path': dict(aim='3332,100.9,2692', side=('3340,104.8,2702', '3337,100,2692'),
                 eyes=[('first person', '3344,100.9,2692.3', '3334,100.3,2692'), ('3 m behind the caster', '3347,101.6,2692.7', '3334,100.3,2692')],
                 cases=[('모', '061_BAA8'), ('오', '109_C624'), ('공', '018_ACF5'), ('몽', '066_BABD')]),
}
try:
    FONT = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
except OSError:
    FONT = ImageFont.load_default()


def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT / 'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode:
        print('FAIL', arg, r.stdout[-300:], r.stderr[-300:])


def shot(name, eye, look):
    call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{eye}:{look}:fov=60:w=1280:h=720:hideplayer')
    return Image.open(ROOT / 'Art/Presentation297/Stills' / f'{name}.png').convert('RGB')


def main():
    want = (sys.argv[1] if len(sys.argv) > 1 else 'circle,cone,volley,path').split(',')
    tw, th, bar = 480, 270, 24
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear'); shot('v308_warm', '3344,100.9,2692.3', '3336,100.5,2692')
    for form in want:
        F = FORMS[form]
        cols = ['3rd pass (before)', 'start', 'peak', 'going'] + [e[0] + ' (peak)' for e in F['eyes']]
        card = Image.new('RGB', (tw * len(cols), 40 + (th + bar) * len(F['cases'])), (40, 38, 36)); d = ImageDraw.Draw(card)
        d.text((10, 10), f'#308 range form: {form} - 4th pass beside the 3rd (EDIT-MODE POSES, not Play)', fill=(230, 226, 214), font=FONT)
        for r, (glyph, key) in enumerate(F['cases']):
            y = 40 + r * (th + bar)
            tiles = []
            old = OUT / 'attack50_tiles_r3' / f'{key}_1.png'
            tiles.append(Image.open(old) if old.exists() else Image.new('RGB', (tw, th), (60, 58, 56)))
            for c, when in enumerate(('launch', 'hit', 'fade')):
                call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', f'pose:{key}:{when}:{ORG}:{F["aim"]}')
                tiles.append(shot(f'v308_rng_{key}_{c}', *F['side']))
                if when == 'hit':
                    eyes = [shot(f'v308_rng_{key}_eye{k}', e[1], e[2]) for k, e in enumerate(F['eyes'])]
            tiles += eyes
            for c, im in enumerate(tiles):
                card.paste(im.resize((tw, th), Image.LANCZOS), (c * tw, y + bar))
                d.text((c * tw + 8, y + 3), f'{glyph}  {cols[c]}', fill=(230, 226, 214), font=FONT)
        card.save(OUT / f'range_{form}.png')
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear')
    print('range review', OUT)


if __name__ == '__main__':
    main()
