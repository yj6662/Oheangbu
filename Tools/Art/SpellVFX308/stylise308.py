"""#308 spell VFX overhaul (D308-52): frames -> wet ink -> packed flipbook sheet. System Python (numpy, scipy, PIL).

  python Tools/Art/SpellVFX308/stylise308.py [preset ...]        (no preset = all folders under Art/SpellVFX308/Forms)

This is the stylising stage that After Effects was meant to do (Roughen Edges / Simple Choker / Posterize Time); AE could not be
driven headless on this PC on 2026-10-10, so the same steps are done here:
  1. wet fusion   - blur + threshold joins drops that nearly touch into necks (a 2D metaball)
  2. brushed edge - the outline is pushed in and out by a small noise, a different one each cel (line boil at 12 Hz)
  3. erosion field- the distance to the outline, noised: the game shader raises a threshold through it, so a stroke that fades
                    THINS and breaks into beads instead of going transparent like smoke
  4. halo         - a soft bleed round the wet core (drawn faintly, only while the stroke is young)
Sheet channels: R heat (fire) - G erosion field - B halo - A coverage. 4 x 4 cells, frame 1 top-left, row-major.
Outputs: Art/SpellVFX308/Flipbooks/<preset>.png, the game copy Oheangbu/Assets/_Project/Art/SpellVFX308/Textures/T_V308_<preset>.png,
and a contact sheet Art/SpellVFX308/Review/flipbook_<preset>.png (ink on paper, for eyes only).
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

ROOT = Path(__file__).resolve().parents[3]
FORMS = ROOT / 'Art/SpellVFX308/Forms'
BOOKS = ROOT / 'Art/SpellVFX308/Flipbooks'
REVIEW = ROOT / 'Art/SpellVFX308/Review'
GAME = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Textures'
TILE = 4
# per preset: cell size in the game sheet, edge roughness (cell px), erosion reach (fraction of the cell), review colours (pigment, ink)
LOOK = {
    'ink_splat': dict(cell=256, rough=1.3, reach=.055, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'ink_drops': dict(cell=128, rough=.8, reach=.09, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'wood_splinter': dict(cell=256, rough=.5, reach=.03, pig=(.20, .36, .22), ink=(.04, .09, .06)),
    'fire_tongue': dict(cell=256, rough=1.6, reach=.07, pig=(.45, .13, .07), ink=(.07, .04, .03), heat=True),
    'fire_burst': dict(cell=256, rough=1.6, reach=.06, pig=(.45, .13, .07), ink=(.07, .04, .03), heat=True),
    'earth_dust': dict(cell=256, rough=1.8, reach=.07, pig=(.47, .38, .26), ink=(.14, .10, .06)),
    'metal_streak': dict(cell=256, rough=.25, reach=.012, pig=(.55, .58, .62), ink=(.05, .05, .06)),
    'water_ring': dict(cell=256, rough=.9, reach=.04, pig=(.14, .25, .38), ink=(.03, .05, .09)),
    'water_orb': dict(cell=256, rough=1.2, reach=.12, pig=(.14, .25, .38), ink=(.03, .05, .09)),
}
PAPER = np.array((.87, .84, .77), np.float32)
HEAT = np.array((.93, .46, .13), np.float32)


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def noise(rng, n, scale):
    """smooth noise in 0..1, feature size `scale` px"""
    v = ndi.gaussian_filter(rng.standard_normal((n, n)).astype(np.float32), scale, mode='wrap')
    return np.clip(.5 + v / (v.std() * 4 + 1e-6), 0, 1)


def stylise(name):
    look = LOOK[name]; cell = look['cell']
    files = sorted((FORMS / name).glob('f*.png'))[:TILE * TILE]
    rng = np.random.default_rng(sum(map(ord, name)))
    sheet = np.zeros((TILE * cell, TILE * cell, 4), np.float32)
    grain = None
    for i, file in enumerate(files):
        src = np.asarray(Image.open(file).convert('RGBA'), np.float32) / 255
        n = src.shape[0]; ss = n / cell
        cov, heat = src[..., 3], src[..., 0]
        # 1 wet fusion
        fused = np.maximum(cov, smooth(ndi.gaussian_filter(cov, ss * 1.5), .36, .5))
        # 2 brushed edge: warp by a low noise, then re-threshold with a finer one
        if look['rough'] > 0:
            amp = look['rough'] * ss
            dx, dy = (noise(rng, n, ss * 5) - .5) * 2 * amp, (noise(rng, n, ss * 5) - .5) * 2 * amp
            yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
            fused = ndi.map_coordinates(fused, [yy + dy, xx + dx], order=1, mode='constant')
            heat = ndi.map_coordinates(heat, [yy + dy, xx + dx], order=1, mode='constant')
            soft = ndi.gaussian_filter(fused, ss * .6)
            fused = smooth(soft + (noise(rng, n, ss * 1.6) - .5) * .22 * min(1.0, look['rough']), .40, .60)
        solid = fused > .5
        # 3 erosion field
        if grain is None:
            grain = .62 + .38 * noise(np.random.default_rng(7), n, ss * 3.5)      # one grain for the sheet: holes stay put while it melts
        dist = ndi.distance_transform_edt(solid).astype(np.float32)
        field = np.clip(dist / (look['reach'] * n), 0, 1) ** .6 * grain
        field = np.maximum(field, .05) * fused
        # 4 halo
        halo = ndi.gaussian_filter(fused, ss * 4.5) * .9
        heat = ndi.gaussian_filter(heat * solid, ss * 1.2) if look.get('heat') else np.zeros_like(fused)
        out = np.stack([heat, field, halo, fused], -1)
        out = np.asarray(Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((cell, cell), Image.BOX), np.float32) / 255
        # nothing may touch the cell border (the neighbour cell would bleed in under bilinear / mips)
        edge = np.minimum(np.arange(cell), np.arange(cell)[::-1]).astype(np.float32)
        ramp = np.clip((edge - 1) / 3, 0, 1); out *= (ramp[:, None] * ramp[None, :])[..., None]
        r, c = i // TILE, i % TILE
        sheet[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = out
    img = Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA')
    BOOKS.mkdir(parents=True, exist_ok=True); GAME.mkdir(parents=True, exist_ok=True); REVIEW.mkdir(parents=True, exist_ok=True)
    img.save(BOOKS / f'{name}.png'); img.save(GAME / f'T_V308_{name}.png')
    # ---- contact sheet: what the game shader makes of it, on paper. Top block = fresh, bottom block = the same cels half eroded.
    pig, ink = np.array(look['pig'], np.float32), np.array(look['ink'], np.float32)

    def shade(erode):
        thr = -0.01 + 1.03 * erode
        a = sheet[..., 3] * smooth(sheet[..., 1], thr - .08, thr)
        a = np.maximum(a, sheet[..., 2] * .22 * (1 - erode) ** 2)
        col = pig[None, None] + (ink - pig)[None, None] * smooth(sheet[..., 1], .18, .5)[..., None]
        if look.get('heat'):
            col = col + (HEAT - col) * np.clip(sheet[..., 0] * 1.4, 0, 1)[..., None]
        return PAPER[None, None] * (1 - a[..., None]) + col * a[..., None]
    k = 1024 // (TILE * cell) if TILE * cell < 1024 else 1
    blocks = [Image.fromarray((np.clip(shade(e), 0, 1) * 255).astype(np.uint8)).resize((TILE * cell * k, TILE * cell * k), Image.NEAREST if k > 1 else Image.BOX) for e in (0.0, .5)]
    w = blocks[0].width
    card = Image.new('RGB', (w * 2 + 24, w + 40), (40, 38, 36))
    card.paste(blocks[0], (0, 40)); card.paste(blocks[1], (w + 24, 40))
    d = ImageDraw.Draw(card)
    d.text((8, 12), f'{name}  -  16 cels at 12 Hz, left: fresh, right: half eroded (the fade thins the stroke)   sheet {TILE * cell} px', fill=(230, 226, 214))
    card.save(REVIEW / f'flipbook_{name}.png')
    print('SHEET', name, TILE * cell, 'coverage mean %.3f' % float(sheet[..., 3].mean()))


if __name__ == '__main__':
    names = sys.argv[1:] or sorted(p.name for p in FORMS.iterdir() if p.is_dir() and p.name in LOOK)
    for nm in names:
        stylise(nm)
