"""#308 spell VFX overhaul (D308-52): edit-mode review stills of the five prototypes. System Python (PIL); the Unity editor must be
open on W_Demo_Main in Edit mode (the stills go through the editor command queue; nothing is saved into the scene).

  python Tools/Art/SpellVFX308/review308.py [ga,na,ma,sa,a] [v2|v1]

v2 (default, the bleeding look) writes stills_v2_<key>.png, overview_v2.png and compare_v1_v2.png (needs the v1 stills);
v1 (the first cutout look) writes stills_<key>.png and overview.png.

Per letter: Vfx308Build pose (launch / mid-flight 6 m / impact / +0.3 s, first-person eye; then a side view and a closer hit) +
Presentation297 shot. Output: Art/SpellVFX308/Review/stills_<key>.png and overview.png. These are POSES of the prefab stepped
the way Vfx120Effect steps it - not Play captures.
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
EYE, LOOK, ORG, TGT = '3344,100.2,2692', '3332,100.7,2692', '3343.3,99.95,2691.75', '3332,100.9,2692'
SIDE_EYE, SIDE_LOOK, CLOSE_EYE = '3338,101.0,2699', '3338,100.7,2692', '3337,101.0,2693.6'
NAMES = {'ga': 'ga (wood)', 'na': 'na (fire)', 'ma': 'ma (earth)', 'sa': 'sa (metal)', 'a': 'a (water)'}
BOOKS = {'ga': ['wood_splinter', 'ink_splat'], 'na': ['fire_tongue', 'fire_burst'], 'ma': ['earth_dust', 'ink_drops'], 'sa': ['metal_streak', 'ink_splat'], 'a': ['water_orb', 'water_ring']}
OUT = ROOT / 'Art/SpellVFX308/Review'
VER = sys.argv[2] if len(sys.argv) > 2 else 'v2'
TAG = '' if VER == 'v1' else '_v2'


def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT / 'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode:
        print('FAIL', arg, r.stdout[-400:], r.stderr[-400:])
    return r.stdout


def shot(name, eye, look, fov=60):
    call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{eye}:{look}:fov={fov}:w=1280:h=720:hideplayer')
    return Image.open(ROOT / 'Art/Presentation297/Stills' / f'{name}.png').convert('RGB')


def pose(key, when):
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', f'pose:{key}:{when}:{ORG}:{TGT}' + (':v1' if VER == 'v1' else ''))


def main():
    letters = (sys.argv[1] if len(sys.argv) > 1 else 'ga,na,ma,sa,a').split(',')
    OUT.mkdir(parents=True, exist_ok=True)
    W, H = 960, 540
    first = True
    for key in letters:
        tiles = []
        for when, label in (('launch', 'launch'), ('mid', 'mid-flight, 6 m out, from behind'), ('hit', 'impact'), ('hit+0.3', 'impact +0.3 s')):
            pose(key, when)
            if first:
                shot('v308_warm', EYE, LOOK); first = False      # the first still after a shader import can come out wrong
            tiles.append((shot(f'v308{TAG}_{key}_{when.replace("+", "p").replace(".", "")}', EYE, LOOK), label))
        pose(key, 'mid'); tiles.append((shot(f'v308{TAG}_{key}_side_mid', SIDE_EYE, SIDE_LOOK, 50), 'mid-flight from the side (not the player view)'))
        pose(key, 'hit+0.12'); tiles.append((shot(f'v308{TAG}_{key}_hit_close', CLOSE_EYE, TGT, 55), 'impact +0.12 s from 5.5 m'))
        card = Image.new('RGB', (W * 3, H * 2 + 48), (40, 38, 36)); d = ImageDraw.Draw(card)
        d.text((10, 16), f'#308 prototype {VER} {NAMES[key]} - EDIT-MODE POSE (not Play): 12 m shot, first-person eye, W_Demo_Main', fill=(230, 226, 214))
        for i, (im, label) in enumerate(tiles):
            x, y = (i % 3) * W, 48 + (i // 3) * H
            card.paste(im.resize((W, H), Image.LANCZOS), (x, y)); d.rectangle((x, y, x + 330, y + 22), fill=(40, 38, 36)); d.text((x + 8, y + 5), label, fill=(230, 226, 214))
        card.save(OUT / f'stills{TAG}_{key}.png')
    call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', 'pose:clear')
    # overview: one row per letter - launch, mid, impact, +0.3, closer hit, and its two flipbooks
    keys = [k for k in NAMES if (OUT / f'stills{TAG}_{k}.png').exists()]
    tw, th = 480, 270
    over = Image.new('RGB', (tw * 5 + th * 2, 40 + th * len(keys)), (40, 38, 36)); d = ImageDraw.Draw(over)
    d.text((10, 12), f'#308 spell VFX prototypes {VER} - launch / mid-flight from behind / impact / +0.3 s / closer hit / flipbooks (edit-mode poses, not Play)', fill=(230, 226, 214))
    for r, key in enumerate(keys):
        card = Image.open(OUT / f'stills{TAG}_{key}.png')
        for c, i in enumerate((0, 1, 2, 3, 5)):
            x, y = (i % 3) * W, 48 + (i // 3) * H
            over.paste(card.crop((x, y, x + W, y + H)).resize((tw, th), Image.LANCZOS), (c * tw, 40 + r * th))
        for c, book in enumerate(BOOKS[key]):
            f = OUT / f'flipbook{TAG}_{book}.png'
            if f.exists():
                b = Image.open(f); side = min(b.height - 40, (b.width - 24) // 2)
                over.paste(b.crop((0, 40, side, 40 + side)).resize((th, th), Image.LANCZOS), (tw * 5 + c * th, 40 + r * th))
        d.rectangle((0, 40 + r * th, 110, 40 + r * th + 20), fill=(40, 38, 36)); d.text((6, 44 + r * th), NAMES[key], fill=(230, 226, 214))
    over.save(OUT / f'overview{TAG}.png')
    # v1 beside v2, per letter: mid-flight from behind, impact, +0.3 s, closer hit
    if VER != 'v1' and all((OUT / f'stills_{k}.png').exists() for k in keys):
        cw, ch = 480, 270
        comp = Image.new('RGB', (cw * 8 + 16, 40 + ch * len(keys)), (40, 38, 36)); d = ImageDraw.Draw(comp)
        d.text((10, 12), 'left four: v1 (hard cutout ink)   |   right four: v2 (bleeding ink)   -   mid-flight / impact / +0.3 s / closer hit   (edit-mode poses, not Play)', fill=(230, 226, 214))
        for r, key in enumerate(keys):
            for v, name in enumerate((f'stills_{key}.png', f'stills_v2_{key}.png')):
                card = Image.open(OUT / name)
                for c, i in enumerate((1, 2, 3, 5)):
                    x, y = (i % 3) * W, 48 + (i // 3) * H
                    comp.paste(card.crop((x, y, x + W, y + H)).resize((cw, ch), Image.LANCZOS), (v * (cw * 4 + 16) + c * cw, 40 + r * ch))
            d.rectangle((0, 40 + r * ch, 110, 40 + r * ch + 20), fill=(40, 38, 36)); d.text((6, 44 + r * ch), NAMES[key], fill=(230, 226, 214))
        comp.save(OUT / 'compare_v1_v2.png')
    print('review', OUT)


if __name__ == '__main__':
    main()
