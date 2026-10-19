"""#308 spell VFX overhaul, bleeding ink (D308-53 (3), approved 2026-10-10 "지금 느낌 좋네"): forms -> ink dropped in water.

  python Tools/Art/SpellVFX308/bleed308.py [--sim] [--pack] [preset ...]
      (reads Art/SpellVFX308/Forms2, made by inkforms308.py -- all --out Forms2 --drops 0.5 --thick 1.6)

Three stages:
  --sim   every cel the wet form is poured into a small 2D fluid: the ink is carried by a curling, divergence-free flow (that
          pulls the thread-like strands out), spreads a little, and thins. Writes Art/SpellVFX308/AE/in/<preset>/d_####.png
          (R heat, G wet heart, B wash density).
  (AE)    Tools/AfterEffects/SpellVFX308/bleed308.jsx (AfterFX.exe -r): turbulent displace (finer wandering strands), paper /
          brush fibre multiplied into the wash, and the pooled rim (the wash minus its own blur = the dark edge where ink
          gathers). Writes Art/SpellVFX308/AE/out/<preset>/wash_####.tif and rim_####.tif.
  --pack  packs the sheet from the AE output when it is there (else from the sim alone, with a numpy rim - same channels).
With no stage flag both sim and pack run (AE output, if present and as new as the sim, is used).
Sheet channels: R heat (fire) - G wet heart - B pooled rim - A ink density (soft). 4 x 4 cells, frame 1 top-left; made to be
interpolated between frames. Outputs: Art/SpellVFX308/Flipbooks/v2/<preset>.png, the game copy
Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_<preset>.png, contact sheet Art/SpellVFX308/Review/flipbook_v2_<preset>.png.
How much each element bleeds is its character: metal hardly at all and it is gone at once, water and earth the most, fire is
carried upward, earth lies low.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

ROOT = Path(__file__).resolve().parents[3]
FORMS = ROOT / 'Art/SpellVFX308/Forms2'
BOOKS = ROOT / 'Art/SpellVFX308/Flipbooks/v2'
REVIEW = ROOT / 'Art/SpellVFX308/Review'
GAME = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures'
AE_IN, AE_OUT = ROOT / 'Art/SpellVFX308/AE/in', ROOT / 'Art/SpellVFX308/AE/out'
TILE, N, SUB, AE_N = 4, 384, 5, 512          # sheet tiles, working resolution, flow steps per cel, AE frame size
# flow: curl strength (px / step at N), strand scale (px), bias = steady drift (x, y; y down), spread = diffusion sigma per step,
# keep = how much wash survives a cel, pour = how much of the form enters the wash, heart = how solid the wet heart stays,
# out = outward push at the start (bursts), loop = periodic preset, still = 16 separate stills, dense = wash opacity curve (<1 = denser)
LOOK = {
    'ink_splat': dict(cell=256, curl=2.6, scale=16, bias=(0, .35), spread=.55, keep=.90, pour=.9, heart=.95, out=1.4, dense=.9, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'ink_drops': dict(cell=128, curl=2.2, scale=14, bias=(0, .5), spread=.6, keep=.9, pour=.95, heart=.9, still=True, dense=.9, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'wood_splinter': dict(cell=256, curl=1.3, scale=12, bias=(0, .15), spread=.4, keep=.88, pour=.95, heart=1.0, out=.6, dense=.75, pig=(.13, .24, .16), ink=(.04, .09, .06)),
    'fire_tongue': dict(cell=256, curl=2.4, scale=14, bias=(0, -1.9), spread=.55, keep=.80, pour=.8, heart=.75, loop=True, heat=True, dense=1.0, pig=(.40, .12, .07), ink=(.07, .04, .03)),
    'fire_burst': dict(cell=256, curl=2.6, scale=14, bias=(0, -1.6), spread=.5, keep=.86, pour=.85, heart=.85, out=.4, heat=True, dense=.9, pig=(.40, .12, .07), ink=(.07, .04, .03)),
    'earth_dust': dict(cell=256, curl=1.8, scale=20, bias=(0, .05), spread=.8, keep=.92, pour=.8, heart=.7, out=.5, dense=.9, pig=(.47, .38, .26), ink=(.14, .10, .06)),
    'metal_streak': dict(cell=256, curl=.5, scale=10, bias=(0, 0), spread=.3, keep=.55, pour=.6, heart=1.0, out=.2, dense=.8, pig=(.42, .45, .48), ink=(.05, .05, .06)),
    'water_ring': dict(cell=256, curl=2.0, scale=18, bias=(0, .55), spread=.65, keep=.90, pour=.85, heart=.9, out=.5, dense=.9, pig=(.14, .25, .38), ink=(.03, .05, .09)),
    'water_orb': dict(cell=256, curl=2.2, scale=18, bias=(0, .8), spread=.6, keep=.82, pour=.7, heart=.9, loop=True, dense=.9, pig=(.14, .25, .38), ink=(.03, .05, .09)),
    # 4th pass, the range forms
    'wood_stroke': dict(cell=256, curl=1.3, scale=13, bias=(0, -.1), spread=.45, keep=.9, pour=.95, heart=1.0, dense=.75, pig=(.13, .24, .16), ink=(.04, .09, .06)),
    'fire_lick': dict(cell=256, curl=2.2, scale=14, bias=(1.3, -.7), spread=.5, keep=.8, pour=.85, heart=.7, loop=True, heat=True, dense=.9, pig=(.40, .12, .07), ink=(.07, .04, .03)),
    'speed_line': dict(cell=256, curl=.7, scale=10, bias=(-.9, 0), spread=.35, keep=.72, pour=.8, heart=1.0, still=True, dense=.8, pig=(.42, .45, .48), ink=(.05, .05, .06)),
    'earth_heave': dict(cell=256, curl=1.8, scale=18, bias=(0, .25), spread=.7, keep=.92, pour=.85, heart=.8, out=.2, dense=.85, pig=(.47, .38, .26), ink=(.14, .10, .06)),
    'ink_pool': dict(cell=256, curl=1.3, scale=18, bias=(0, 0), spread=.6, keep=.92, pour=.9, heart=.22, out=.9, dense=.95, pig=(.13, .2, .15), ink=(.03, .05, .04)),      # 5th pass: the ground of a circle
    'water_crest': dict(cell=256, curl=1.7, scale=16, bias=(0, .4), spread=.55, keep=.9, pour=.9, heart=.95, dense=.85, pig=(.14, .25, .38), ink=(.03, .05, .09)),
    # 6th pass (D308-83): the ink before the spell. hair = bristle lines along +X cut into the wash (a thrown stroke has a direction)
    'ink_throw': dict(cell=256, curl=.9, scale=12, bias=(.8, .3), spread=.4, keep=.86, pour=.95, heart=1.0, dense=.8, hair=.55, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'ink_wisp': dict(cell=256, curl=2.0, scale=14, bias=(-.8, -.5), spread=.5, keep=.72, pour=.6, heart=.4, loop=True, dense=.9, hair=.2, pig=(.16, .16, .17), ink=(.03, .03, .04)),
    'ink_gather': dict(cell=256, curl=1.5, scale=14, bias=(0, 0), spread=.45, keep=.82, pour=.9, heart=.8, out=-.5, dense=.85, pig=(.16, .16, .17), ink=(.03, .03, .04)),
}
PAPER = np.array((.87, .84, .77), np.float32)
HEAT = np.array((.93, .46, .13), np.float32)
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)


def psi(rng, scale):
    """stream function with three octaves; its curl is a divergence-free flow"""
    f = np.zeros((N, N), np.float32)
    for o, w in ((1.0, 1.0), (.5, .55), (.25, .3)):
        v = ndi.gaussian_filter(rng.standard_normal((N, N)).astype(np.float32), scale * o, mode='wrap')
        f += w * v / (v.std() + 1e-6) * scale * o
    return f


def flow(stream):
    return np.gradient(stream, axis=0), -np.gradient(stream, axis=1)         # (vx, vy)


def advect(field, vx, vy):
    return ndi.map_coordinates(field, [YY - vy, XX - vx], order=1, mode='constant')


def load(file):
    src = np.asarray(Image.open(file).convert('RGBA').resize((N, N), Image.BOX), np.float32) / 255
    cov = ndi.gaussian_filter(src[..., 3], 1.0)
    return cov, src[..., 0] * src[..., 3]


def sim(name):
    look = LOOK[name]
    files = sorted((FORMS / name).glob('f*.png'))[:TILE * TILE]
    rng = np.random.default_rng(sum(map(ord, name)) + 53)
    s0, s1 = psi(rng, look['scale']), psi(rng, look['scale'])
    cy, cx = N * .5, N * .5
    rad = np.sqrt((XX - cx) ** 2 + (YY - cy) ** 2) + 1e-3
    out_x, out_y = (XX - cx) / rad, (YY - cy) / rad
    wash = np.zeros((N, N), np.float32); warm = np.zeros((N, N), np.float32)
    order = list(range(len(files)))
    passes = order + order if look.get('loop') else order          # a loop is run twice; the second time round is kept
    out = AE_IN / name; out.mkdir(parents=True, exist_ok=True)
    kept = 0
    for step, i in enumerate(passes):
        cov, heat = load(files[i])
        if look.get('still'):
            wash[:] = 0; warm[:] = 0
        t = step / max(1, len(files) - 1)
        ph = 0.0 if look.get('loop') or look.get('still') else t * 1.2
        vx, vy = flow(np.cos(ph) * s0 + np.sin(ph) * s1)
        push = look.get('out', 0.0) * max(0.0, 1.0 - t * 2.2)
        vx = vx * look['curl'] + look['bias'][0] + out_x * push
        vy = vy * look['curl'] + look['bias'][1] + out_y * push
        for _ in range(SUB * (3 if look.get('still') else 1)):
            wash = np.maximum(wash, cov * look['pour'])                 # the form keeps pouring while it is there
            warm = np.maximum(warm, heat)
            wash = ndi.gaussian_filter(advect(wash, vx, vy), look['spread']); warm = ndi.gaussian_filter(advect(warm, vx, vy), look['spread'])
            wash *= look['keep'] ** (1.0 / SUB); warm *= .72 ** (1.0 / SUB)
        if step < len(passes) - len(files):
            continue
        heart = ndi.gaussian_filter(cov, 1.6) * look['heart']
        density = np.clip(np.maximum(wash, heart * .98), 0, 1)
        hot = np.clip(np.maximum(warm * .8, heat), 0, 1) if look.get('heat') else np.zeros_like(density)
        cel = (np.stack([hot, heart, density], -1) * 255 + .5).astype(np.uint8)
        Image.fromarray(cel, 'RGB').resize((AE_N, AE_N), Image.BICUBIC).save(out / f'd_{kept:04d}.png'); kept += 1
    print('SIM', name, kept, 'cels ->', out)


def pack(name):
    look = LOOK[name]; cell = look['cell']
    src, ae = AE_IN / name, AE_OUT / name
    plain = sorted(src.glob('d_*.png'))
    washed, rims = sorted(ae.glob('wash_*.tif')), sorted(ae.glob('rim_*.tif'))
    use_ae = len(washed) >= len(plain) > 0 and len(rims) >= len(plain) and washed[0].stat().st_mtime >= plain[0].stat().st_mtime
    frames = []
    for i in range(len(plain)):
        if use_ae:
            a = np.asarray(Image.open(washed[i]).convert('RGB'), np.float32) / 255
            rim = np.asarray(Image.open(rims[i]).convert('RGB'), np.float32)[..., 2] / 255
        else:
            a = np.asarray(Image.open(plain[i]).convert('RGB'), np.float32) / 255
            rim = np.clip(a[..., 2] - ndi.gaussian_filter(a[..., 2], 9), 0, 1)
        hot, heart, density = a[..., 0], a[..., 1], a[..., 2]
        if look.get('hair'):
            # bristle lines: noise that changes fast across the stroke and slowly along it, the same for every cel of a sheet
            hr = np.random.default_rng(sum(map(ord, name)) + 97).standard_normal(density.shape).astype(np.float32)
            hr = ndi.gaussian_filter(hr, (1.3, density.shape[1] / 9.0), mode='nearest'); hr = hr / (np.abs(hr).max() + 1e-6)
            comb = np.clip(1.0 - look['hair'] * np.clip(hr * 2.2 + .35, 0, 1), 0, 1)
            density = density * comb; heart = heart * np.clip(comb * 1.15, 0, 1)
        # paler toward the edge: a soft curve on the wash, the heart stays full; the rim is where the ink pooled
        alpha = np.clip(np.maximum(density ** look['dense'] * .95, heart), 0, 1)
        rim = np.clip(rim * 3.2, 0, 1) * (1 - np.clip(heart * 1.2, 0, 1))
        cel = np.stack([hot, heart, rim, alpha], -1)
        cel = np.asarray(Image.fromarray((np.clip(cel, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((cell, cell), Image.BOX), np.float32) / 255
        edge = np.minimum(np.arange(cell), np.arange(cell)[::-1]).astype(np.float32)
        ramp = np.clip((edge - 1) / 10, 0, 1); ramp = ramp * ramp * (3 - 2 * ramp)
        cel *= (ramp[:, None] * ramp[None, :])[..., None]              # the wash must die out before the cell border
        frames.append(cel)
    sheet = np.zeros((TILE * cell, TILE * cell, 4), np.float32)
    for i, cel in enumerate(frames[:TILE * TILE]):
        r, c = i // TILE, i % TILE
        sheet[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = cel
    img = Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA')
    for d in (BOOKS, GAME, REVIEW):
        d.mkdir(parents=True, exist_ok=True)
    img.save(BOOKS / f'{name}.png'); img.save(GAME / f'T_V308b_{name}.png')
    # contact sheet as the game shader draws it: pigment wash, ink heart, pooled rim, heat; right block = half faded
    pig, ink = np.array(look['pig'], np.float32), np.array(look['ink'], np.float32)

    def shade(fade):
        a = np.clip(sheet[..., 3] + sheet[..., 2] * .5, 0, 1) * fade
        k = np.clip(np.clip((sheet[..., 1] - .2) / .5, 0, 1) + sheet[..., 2] * .8, 0, 1)[..., None]
        col = pig[None, None] + (ink - pig)[None, None] * k
        if look.get('heat'):
            col = col + (HEAT - col) * np.clip((sheet[..., 0] - .3) / .45, 0, 1)[..., None]
        return PAPER[None, None] * (1 - a[..., None]) + col * a[..., None]
    k = max(1, 1024 // (TILE * cell))
    blocks = [Image.fromarray((np.clip(shade(f), 0, 1) * 255).astype(np.uint8)).resize((TILE * cell * k, TILE * cell * k), Image.BILINEAR) for f in (1.0, .45)]
    w = blocks[0].width
    card = Image.new('RGB', (w * 2 + 24, w + 40), (40, 38, 36))
    card.paste(blocks[0], (0, 40)); card.paste(blocks[1], (w + 24, 40))
    ImageDraw.Draw(card).text((8, 12), f'v2 {name}  ({"After Effects pass" if use_ae else "numpy only"})  -  16 cels (interpolated in game), left: whole, right: fading   sheet {TILE * cell} px', fill=(230, 226, 214))
    card.save(REVIEW / f'flipbook_v2_{name}.png')
    print('SHEET v2', name, TILE * cell, 'AE' if use_ae else 'numpy', 'alpha mean %.3f' % float(sheet[..., 3].mean()))


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    do_sim = '--sim' in sys.argv or '--pack' not in sys.argv
    do_pack = '--pack' in sys.argv or '--sim' not in sys.argv
    for nm in (args or [n for n in LOOK if (FORMS / n).is_dir()]):
        if do_sim:
            sim(nm)
        if do_pack:
            pack(nm)
