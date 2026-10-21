"""#308 D308-126 rising ground VFX: one thin ink strand that rises off a domain's rim (user 2026-10-21 "장판 계열 vfx가 바닥에만
붙어있는 게 아니라, 위로 일렁거리는 효과, particle 등을 이용하는 게 좋을 거 같은데").

  python Tools/Art/SpellVFX308/strand308.py [preset ...]

Round 10 of the debate chose thin, uneven strands with gaps between them over a veil or a wall (Codex: "불꽃 모양을 없앤다 ...
가는 먹 섬유가 위로 풀리는 형태"). The existing wisp sheets are lying puffs (soot_wisp is stood up a quarter turn on the ember
patch) and read as a wide cloud, so this draws its own: a bundle of brush hairs that leaves the ground in one dense dry-brush
foot, sways more the higher it goes, and unravels at the top into two or three hairs that thin out - grown, held, unravelled,
gone in 16 cels (4 x 4, frame 1 top-left, the foot on the cel's bottom edge, so the particle pivot goes at the bottom).
Drawn from code, never from a concept picture (D308-28). Sheet channels as InkBleed308 reads them: R heat (0) - G wet heart
(the dense foot -> ink) - B rim (0) - A density. Outputs as bleed308.py / smoke308.py: Art/SpellVFX308/Flipbooks/v2/<preset>.png,
the game copy Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_<preset>.png, contact sheet
Art/SpellVFX308/Review/flipbook_v2_<preset>.png.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

ROOT = Path(__file__).resolve().parents[3]
BOOKS = ROOT / 'Art/SpellVFX308/Flipbooks/v2'
REVIEW = ROOT / 'Art/SpellVFX308/Review'
GAME = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures'
TILE, CELL, SS = 4, 256, 2                     # tiles, cel size, supersampling
# hairs = brush hairs in the bundle, width = foot width (fraction of the cel), sway = top sideways reach, waves = sway
# periods over the height, split = how far the top hairs part, dry = gap density along a hair (갈필), seed
LOOK = {
    'ink_strand': dict(hairs=9, width=.075, sway=.13, waves=1.3, split=.11, dry=.35, haze=1.6, seed=7),
}


def smooth(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def line_noise(rng, n, scale):
    v = ndi.gaussian_filter1d(rng.standard_normal(n).astype(np.float32), scale, mode='wrap')
    return v / (np.abs(v).max() + 1e-6)


def cel(lk, t, rng_seed):
    """one cel at life t (0..1): float A and G at CELL*SS"""
    n = CELL * SS
    a = np.zeros((n, n), np.float32); g = np.zeros((n, n), np.float32)
    rng = np.random.default_rng(rng_seed)                       # same hairs every cel - the strand is one thing that ages
    grow = smooth(t / .38)                                      # rises off the ground in the first ~6 cels
    top = .16 + .78 * grow                                      # reach (fraction of the cel height)
    fade = 1.0 - smooth((t - .62) / .38)                        # unravels and thins out over the last ~6 cels
    unravel = smooth((t - .45) / .55)
    phase = t * 2.4                                             # the wave travels up the strand
    steps = 220
    v = np.linspace(0, 1, steps, dtype=np.float32)              # along the strand, 0 = foot
    sway_noise = line_noise(rng, steps, 18)
    for k in range(lk['hairs']):
        off = (k / (lk['hairs'] - 1) - .5) * 2                  # -1..1 across the bundle
        hr = np.random.default_rng(rng_seed * 31 + k)
        reach = top * hr.uniform(.72, 1.0) * (1 if abs(off) < .5 else hr.uniform(.6, .95))
        h = v * reach
        rel = h / max(top, 1e-3)
        # spine: fixed foot, sway grows with height, a slow wave travelling up
        ab = h / .94                                            # sway by absolute height: a short strand stands straight
        sx = (lk['sway'] * ab ** 1.4 * np.sin(2 * np.pi * (lk['waves'] * ab - phase) + 1.1)
              + .03 * ab * sway_noise)
        # the bundle narrows going up, then the top hairs part (unravel) in their own directions
        part = lk['split'] * rel ** 2 * (.35 + unravel) * (off + .35 * hr.uniform(-1, 1))
        x = .5 + sx + off * lk['width'] * (1 - .7 * rel) * .5 + part
        y = 1 - h                                               # image y down, the foot on the bottom edge
        # density along the hair: dense foot, thin top, dry-brush gaps, gone from the top down as it ages
        gaps = smooth((line_noise(hr, steps, 3.5) + 1) / 2 / lk['dry'] * .9)
        gaps = .35 + .65 * np.where(rel < .18, 1.0, gaps)
        dens = (1 - rel) ** .9 * gaps * fade
        dens *= 1 - smooth((rel - (1.05 - .9 * unravel)) / .25) * unravel   # eaten from the top
        dens *= .55 + .45 * (1 - abs(off))                      # the middle hairs carry the ink
        rad = (2.4 - 1.5 * rel) * SS * (1.25 if abs(off) < .3 else 1.0)
        for i in range(steps):
            if dens[i] < .02: continue
            cx, cy = x[i] * n, y[i] * n
            r = max(rad[i] if np.ndim(rad) else rad, .7 * SS)
            x0, x1 = int(max(cx - 3 * r, 0)), int(min(cx + 3 * r + 1, n))
            y0, y1 = int(max(cy - 3 * r, 0)), int(min(cy + 3 * r + 1, n))
            if x0 >= x1 or y0 >= y1: continue
            yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
            d = np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (2 * r * r)) * dens[i]
            a[y0:y1, x0:x1] = np.maximum(a[y0:y1, x0:x1], d)
            g[y0:y1, x0:x1] = np.maximum(g[y0:y1, x0:x1], d * (1 - rel[i]) ** 2)
    # a faint wash of wet ink at the foot (the strand leaves the ground, not a point)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) / n
    foot = np.exp(-((xx - .5) / (lk['width'] * 1.3)) ** 2 - ((1 - yy) / .05) ** 2) * .5 * fade * (.4 + .6 * grow)
    a = np.maximum(a, foot); g = np.maximum(g, foot * .8)
    # soft paper bleed so the hairs are brush, not vector lines
    # and a thin vapour round the fibres, so it reads as ink rising, not a lock of hair
    a = np.clip(np.maximum(ndi.gaussian_filter(a, .8 * SS) * 1.15, ndi.gaussian_filter(a, 7 * SS) * lk['haze']), 0, 1)
    g = np.clip(ndi.gaussian_filter(g, .8 * SS), 0, 1)
    side =smooth(np.minimum(xx, 1 - xx) / .06) * smooth(yy / .04)  # never touch the cel's left / right / top border
    return a * side, g * side


def make(name):
    lk = LOOK[name]
    sheet = np.zeros((TILE * CELL, TILE * CELL, 4), np.float32)
    for i in range(TILE * TILE):
        t = i / (TILE * TILE - 1)
        a, g = cel(lk, t, lk['seed'])
        a = a.reshape(CELL, SS, CELL, SS).mean((1, 3)); g = g.reshape(CELL, SS, CELL, SS).mean((1, 3))
        r, c = divmod(i, TILE)
        sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, 1] = g
        sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, 3] = a
    img = Image.fromarray((np.clip(sheet, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
    BOOKS.mkdir(parents=True, exist_ok=True); REVIEW.mkdir(parents=True, exist_ok=True)
    img.save(BOOKS / f'{name}.png'); img.save(GAME / f'T_V308b_{name}.png')
    a = sheet[..., 3:4]; paper = np.array((.87, .84, .77), np.float32)
    pig, ink = np.array((.43, .17, .12), np.float32), np.array((.06, .05, .05), np.float32)
    col = pig + (ink - pig) * np.clip(sheet[..., 1:2] / np.maximum(a, 1e-3), 0, 1)
    card = Image.fromarray((np.clip(paper * (1 - a) + col * a, 0, 1) * 255).astype(np.uint8))
    ImageDraw.Draw(card).text((8, 8), f'{name}  16 cels  (foot at the bottom edge)', fill=(30, 30, 30))
    card.save(REVIEW / f'flipbook_v2_{name}.png')
    print('SHEET strand', name, TILE * CELL, 'alpha mean %.3f' % float(sheet[..., 3].mean()))


if __name__ == '__main__':
    for nm in [a for a in sys.argv[1:] if not a.startswith('--')] or list(LOOK):
        make(nm)
