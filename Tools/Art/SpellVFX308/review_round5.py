"""#308 spell VFX overhaul, 5th pass (texture and volume): the same poses from three angles and the first-person eye.
The Unity editor must be open on W_Demo_Main in Edit mode; nothing is saved into the scene. System Python (PIL).

  python Tools/Art/SpellVFX308/review_round5.py shoot <tag> [single,circle,cone,volley,path]     tag = r4 (before) | r5 (after)
  python Tools/Art/SpellVFX308/review_round5.py sheets

Each pose is shot from behind (0 deg), 45 deg and the side (90 deg) round its own centre, and from the caster's eye - a
flipbook that is one sheet of paper shows as a line from the side. `shoot` stores tiles in Review/round5_tiles/<tag>/;
`sheets` lays r4 beside r5: Review/round5_<group>.png. POSES, not Play.
"""
import math
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/SpellVFX308/Review'
ORG, TGT, EYE = '3343.3,99.95,2691.75', '3332,100.9,2692', '3344,100.9,2692.3'
# group -> [(label, pose key, when, aim, centre (x, y, z), camera distance)]
CASES = {
    'single': [(g + ' flight', k, 'mid', TGT, (3338.0, 100.7 + up, 2692.0), 5.0) for g, k, up in (('가', 'ga', 0), ('나', 'na', 0), ('마', 'ma', 1.9), ('사', 'sa', 0), ('아', 'a', .5))]
            + [(g + ' hit', k, 'hit+0.15', TGT, (3332.0, 100.9, 2692.0), 6.5) for g, k in (('가', 'ga'), ('나', 'na'), ('마', 'ma'), ('사', 'sa'), ('아', 'a'))],
    'circle': [('고 peak', '013_ACE0', 'hit', '3333.3,100,2692', (3333.3, 100.6, 2692.0), 9.0), ('곡 peak', '014_ACE1', 'hit', '3333.3,100,2692', (3333.3, 100.6, 2692.0), 9.0)],
    'cone': [('노 peak', '037_B178', 'hit', TGT, (3339.5, 100.8, 2692.0), 8.0), ('농 peak', '042_B18D', 'hit', TGT, (3339.5, 100.8, 2692.0), 8.0)],
    'volley': [('소 peak', '085_C18C', 'hit', TGT, (3337.0, 100.9, 2692.0), 8.0)],
    'path': [('모 peak', '061_BAA8', 'hit', TGT, (3336.1, 100.6, 2692.0), 10.0), ('오 peak', '109_C624', 'hit', TGT, (3334.9, 100.6, 2692.0), 10.0)],
}
VIEWS = ['behind 0', '45', 'side 90', 'first person']
try:
    FONT = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 14)
except OSError:
    FONT = ImageFont.load_default()


def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT / 'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode:
        print('FAIL', arg, r.stdout[-300:], r.stderr[-300:])


def shot(name, eye, look):
    call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{eye}:{look}:fov=60:w=1280:h=720:hideplayer')
    return Image.open(ROOT / 'Art/Presentation297/Stills' / f'{name}.png').convert('RGB')


def shoot(tag, groups):
    tiles = OUT / 'round5_tiles' / tag; tiles.mkdir(parents=True, exist_ok=True)
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear'); shot('v308_warm', EYE, TGT)
    for group in groups:
        for i, (label, key, when, aim, c, dist) in enumerate(CASES[group]):
            call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', f'pose:{key}:{when}:{ORG}:{aim}')
            look = f'{c[0]},{c[1]},{c[2]}'
            for v, az in enumerate((0, 45, 90)):
                eye = f'{c[0] + dist * math.cos(math.radians(az)):.2f},{c[1] + 1.2:.2f},{c[2] + dist * math.sin(math.radians(az)):.2f}'
                shot(f'v308_r5_{v}', eye, look).resize((640, 360), Image.LANCZOS).save(tiles / f'{group}_{i}_{v}.png')
            shot('v308_r5_3', EYE, look).resize((640, 360), Image.LANCZOS).save(tiles / f'{group}_{i}_3.png')
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear')


def sheets(pair=('r4', 'r5')):
    tw, th, bar = 320, 180, 22
    for group, cases in CASES.items():
        card = Image.new('RGB', (tw * 8 + 12, 40 + (th + bar) * len(cases)), (40, 38, 36)); d = ImageDraw.Draw(card)
        d.text((10, 10), f'#308 5th pass - {group}: left four = {pair[0]}, right four = {pair[1]}; behind / 45 deg / side / first person (EDIT-MODE POSES, not Play)', fill=(230, 226, 214), font=FONT)
        for r, (label, *_rest) in enumerate(cases):
            y = 40 + r * (th + bar)
            for t, tag in enumerate(pair):
                for v in range(4):
                    f = OUT / 'round5_tiles' / tag / f'{group}_{r}_{v}.png'; x = t * (tw * 4 + 12) + v * tw
                    if f.exists():
                        card.paste(Image.open(f).resize((tw, th), Image.LANCZOS), (x, y + bar))
                    d.text((x + 6, y + 3), f'{label}  {tag}  {VIEWS[v]}', fill=(230, 226, 214), font=FONT)
        card.save(OUT / (f'round5_{group}.png' if pair == ('r4', 'r5') else f'round_{pair[0]}_{pair[1]}_{group}.png'))
    print('round5 sheets', OUT)


if __name__ == '__main__':
    if len(sys.argv) > 2 and sys.argv[1] == 'shoot':
        shoot(sys.argv[2], (sys.argv[3] if len(sys.argv) > 3 else 'single,circle,cone,volley,path').split(','))
    else:
        sheets(tuple(sys.argv[2:4])) if len(sys.argv) > 3 else sheets()
