"""#308 spell VFX overhaul, 3rd pass: edit-mode review stills of the attack set (element x form). System Python (PIL).
The Unity editor must be open on W_Demo_Main in Edit mode; nothing is saved into the scene.

  python Tools/Art/SpellVFX308/review_attack308.py [ga,na,ma,sa,a]

Per letter two poses through Vfx308Build pose + Presentation297 shot:
  single letters - mid-flight from the first-person eye, and the hit +0.15 s (12 m shot)
  area letters   - just after the form starts, and at its peak, from behind and above the caster
Output: Art/SpellVFX308/Review/attack50_<element>.png (10 letters x 2 poses) and attack50_all.png (the 50 at their peak).
Giyeok letters (각 낙 삭 악) are not built (their casts do not read the visual set) and show as an empty tile.
POSES, not Play: area forms replay the plain AreaRemake branches with a plan made from the rule numbers.
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/SpellVFX308/Review'
EYE, LOOK, ORG, TGT = '3344,100.2,2692', '3332,100.7,2692', '3343.3,99.95,2691.75', '3332,100.9,2692'
AREA_EYE, AREA_LOOK = '3347.5,102.6,2692.6', '3336,100.0,2692'
AREA_AIM = '3334.3,100,2692'          # circle centre (9 m ahead of the caster) / the direction of the others
ELEMENTS = {'ga': '목 wood', 'na': '화 fire', 'ma': '토 earth', 'sa': '금 metal', 'a': '수 water'}
# letter, effect id, element, form  (the same table as Vfx308Build.Attack)
LETTERS = [
    ('가', 'ga', 'ga', 'Single'), ('각', '002_AC01', 'ga', 'Giyeok'), ('간', '003_AC04', 'ga', 'Single'), ('갓', '005_AC13', 'ga', 'Single'), ('강', '006_AC15', 'ga', 'Single'),
    ('고', '013_ACE0', 'ga', 'Circle'), ('곡', '014_ACE1', 'ga', 'Circle'), ('곤', '015_ACE4', 'ga', 'Circle'), ('곳', '017_ACF3', 'ga', 'Circle'), ('공', '018_ACF5', 'ga', 'Path'),
    ('나', 'na', 'na', 'Single'), ('낙', '026_B099', 'na', 'Giyeok'), ('난', '027_B09C', 'na', 'Single'), ('낫', '029_B0AB', 'na', 'Single'), ('낭', '030_B0AD', 'na', 'Single'),
    ('노', '037_B178', 'na', 'Cone'), ('녹', '038_B179', 'na', 'Cone'), ('논', '039_B17C', 'na', 'Cone'), ('놋', '041_B18B', 'na', 'Cone'), ('농', '042_B18D', 'na', 'Cone'),
    ('마', 'ma', 'ma', 'Single'), ('막', '050_B9C9', 'ma', 'Single'), ('만', '051_B9CC', 'ma', 'Single'), ('맛', '053_B9DB', 'ma', 'Single'), ('망', '054_B9DD', 'ma', 'Single'),
    ('모', '061_BAA8', 'ma', 'Path'), ('목', '062_BAA9', 'ma', 'Path'), ('몬', '063_BAAC', 'ma', 'Path'), ('못', '065_BABB', 'ma', 'Path'), ('몽', '066_BABD', 'ma', 'Path'),
    ('사', 'sa', 'sa', 'Single'), ('삭', '074_C0AD', 'sa', 'Giyeok'), ('산', '075_C0B0', 'sa', 'Single'), ('삿', '077_C0BF', 'sa', 'Single'), ('상', '078_C0C1', 'sa', 'Single'),
    ('소', '085_C18C', 'sa', 'Volley'), ('속', '086_C18D', 'sa', 'Volley'), ('손', '087_C190', 'sa', 'Volley'), ('솟', '089_C19F', 'sa', 'Volley'), ('송', '090_C1A1', 'sa', 'Volley'),
    ('아', 'a', 'a', 'Single'), ('악', '098_C545', 'a', 'Giyeok'), ('안', '099_C548', 'a', 'Single'), ('앗', '101_C557', 'a', 'Single'), ('앙', '102_C559', 'a', 'Single'),
    ('오', '109_C624', 'a', 'Path'), ('옥', '110_C625', 'a', 'Path'), ('온', '111_C628', 'a', 'Path'), ('옷', '113_C637', 'a', 'Path'), ('옹', '114_C639', 'a', 'Path'),
]
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
    want = (sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') else 'ga,na,ma,sa,a').split(',')
    OUT.mkdir(parents=True, exist_ok=True)
    tw, th, bar = 480, 270, 24
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear'); shot('v308_warm', EYE, LOOK)
    for elem in want:
        rows = [l for l in LETTERS if l[2] == elem]
        card = Image.new('RGB', (tw * 4, 40 + (th + bar) * 5), (40, 38, 36)); d = ImageDraw.Draw(card)
        d.text((10, 10), f'#308 attack set - {ELEMENTS[elem]} - element x form, two poses per letter (EDIT-MODE POSES, not Play)', fill=(230, 226, 214), font=FONT)
        for i, (glyph, key, _, form) in enumerate(rows):
            x, y = (i % 2) * tw * 2, 40 + (i // 2) * (th + bar)
            d.text((x + 8, y + 3), f'{glyph}   {form}' + ('   - not built: giyeok casts do not read the visual set' if form == 'Giyeok' else ''), fill=(230, 226, 214), font=FONT)
            if form == 'Giyeok':
                continue
            single = form == 'Single'
            if single and '--area' in sys.argv and all((OUT / 'attack50_tiles' / f'{key}_{c}.png').exists() for c in (0, 1)):      # range letters only: reuse the single tiles
                for c in (0, 1):
                    card.paste(Image.open(OUT / 'attack50_tiles' / f'{key}_{c}.png'), (x + c * tw, y + bar))
                continue
            for c, when in enumerate(('mid', 'hit+0.15') if single else ('launch', 'hit')):
                call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', f'pose:{key}:{when}:{ORG}:{TGT if single else AREA_AIM}')
                im = shot(f'v308_atk_{key}_{c}', EYE if single else AREA_EYE, LOOK if single else AREA_LOOK)
                im.resize((tw, th), Image.LANCZOS).save(OUT / 'attack50_tiles' / f'{key}_{c}.png') if (OUT / 'attack50_tiles').mkdir(exist_ok=True) is None else None
                card.paste(im.resize((tw, th), Image.LANCZOS), (x + c * tw, y + bar))
        card.save(OUT / f'attack50_{elem}.png')
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear')
    # all fifty at their peak
    w, h = 384, 216
    over = Image.new('RGB', (w * 10, 40 + (h + bar) * 5), (40, 38, 36)); d = ImageDraw.Draw(over)
    d.text((10, 10), '#308 attack set - 50 letters at the hit / peak, one row per element (EDIT-MODE POSES, not Play; empty = giyeok, not built)', fill=(230, 226, 214), font=FONT)
    for i, (glyph, key, elem, form) in enumerate(LETTERS):
        x, y = (i % 10) * w, 40 + (i // 10) * (h + bar)
        d.text((x + 6, y + 3), f'{glyph} {form}', fill=(230, 226, 214), font=FONT)
        f = OUT / 'attack50_tiles' / f'{key}_1.png'
        if form != 'Giyeok' and f.exists():
            over.paste(Image.open(f).resize((w, h), Image.LANCZOS), (x, y + bar))
    over.save(OUT / 'attack50_all.png')
    print('attack review', OUT)


if __name__ == '__main__':
    main()
