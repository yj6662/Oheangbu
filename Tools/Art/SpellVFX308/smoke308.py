"""#308 spell VFX, ink smoke (user 2026-10-15 "판을 만들어서 퍼뜨리는 것보다 연기같은 VFX로 현실적인 느낌을 더 살릴 수는 없을까?").

  python Tools/Art/SpellVFX308/smoke308.py [preset ...]

One billow from birth to gone in 16 cels (4 x 4, frame 1 top-left, interpolated in game): a lumpy compact puff that swells,
is curled apart by a divergence-free flow and thins out. Unlike bleed308.py (ink dropped in water, a flat wash with a pooled
rim) the volume is lit: the light comes from above, the optical depth toward it is baked in G, so the shader sinks the
shadowed underside to ink and leaves the lit top in the vertex colour - a rolling mass instead of a painted card. No rim.
Sheet channels as InkBleed308 reads them: R heat (root of a fire smoke, first cels only) - G shadow (-> ink) - B rim (0) -
A density. Outputs the same places as bleed308.py: Art/SpellVFX308/Flipbooks/v2/<preset>.png, the game copy
Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_<preset>.png, contact sheet Art/SpellVFX308/Review/flipbook_v2_<preset>.png.
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
TILE, N, CELL, SUB = 4, 384, 256, 6
# puffs = lit lumps in the billow, r0 / r1 = mass radius at birth / reach of the drift (fraction of the cell), rise = buoyancy,
# curl = noise warp (wisps grow with age), heat = fire at the root (first cels), shade = occlusion from above, seed
LOOK = {
    'ink_smoke': dict(puffs=15, r0=.2, r1=.34, rise=.5, curl=1.0, heat=0.0, shade=1.0, seed=11),
    'fire_smoke': dict(puffs=15, r0=.2, r1=.34, rise=.6, curl=1.0, heat=1.0, shade=1.0, seed=23),
}
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)


def noise(rng, scale, octaves=3):
    f = np.zeros((N, N), np.float32); amp = 1.0
    for o in range(octaves):
        f += ndi.gaussian_filter(rng.standard_normal((N, N)).astype(np.float32), scale / (2 ** o), mode='wrap') * amp
        amp *= .5
    return f / (np.abs(f).max() + 1e-6)


def curl(psi):
    gy, gx = np.gradient(psi)
    return gy, -gx                                                    # divergence-free: v = (d psi/dy, -d psi/dx)


def advect(f, vx, vy):
    return ndi.map_coordinates(f, [YY - vy, XX - vx], order=1, mode='constant')


def make(name):
    # a cluster of lit spheres (each lump of a billow is round and lit on top) that drift apart, swell, are warped by a
    # growing noise and eaten from the edge - cauliflower at birth, torn wisps at the end
    lk = LOOK[name]; rng = np.random.default_rng(lk['seed'])
    c = N * .5
    K = lk['puffs']
    ang = rng.uniform(0, 2 * np.pi, K); dist = np.sqrt(rng.uniform(0, 1, K)) * lk['r0'] * N * .75
    px, py = c + np.cos(ang) * dist, c + .1 * N + np.sin(ang) * dist * .6
    rad = rng.uniform(.38, .7, K) * lk['r0'] * N
    vx = np.cos(ang) * rng.uniform(.4, 1.0, K); vy = np.sin(ang) * rng.uniform(.2, .6, K) - rng.uniform(.6, 1.2, K) * lk['rise']
    spread = (lk['r1'] - lk['r0']) * N
    w0, w1 = noise(rng, 18, 3), noise(rng, 18, 3)
    eat = noise(rng, 9, 3)
    zoff = rng.uniform(-.3, .3, K) * lk['r0'] * N
    light = np.array([-.35, -.75, .55], np.float32); light /= np.linalg.norm(light)
    cels = []
    for k in range(TILE * TILE):
        t = k / (TILE * TILE - 1)
        ease = 1 - (1 - t) ** 2.2                                          # fast at birth, settling
        warp = (2 + 26 * t) * lk['curl']
        qx, qy = XX + w0 * warp, YY + w1 * warp
        dens = np.zeros((N, N), np.float32); zs, lams = [], []
        for n in range(K):
            r = rad[n] * (1 + 1.3 * ease)
            cx, cy = px[n] + vx[n] * spread * ease, py[n] + vy[n] * spread * ease
            dx, dy = (qx - cx) / r, (qy - cy) / r
            h = np.sqrt(np.clip(1 - dx * dx - dy * dy, 0, 1))
            lam = np.clip(dx * light[0] + dy * light[1] + h * light[2], 0, 1)
            zs.append(np.where(h > 0, h * r + zoff[n], -1e4)); lams.append(lam)   # the front-most lump is the one seen (and lit)
            dens += h
        zs = np.stack(zs); top = zs.max(0)
        wt = np.exp(np.clip((zs - top) / (.06 * lk['r0'] * N), -30, 0))           # soft front: neighbouring lumps blend at the crease
        lit = (wt * np.stack(lams)).sum(0) / (wt.sum(0) + 1e-6)
        # thinning and eating: the body loses density with age, the edge is eaten by noise first
        body = dens * (1.1 - .85 * t ** 1.1)
        body = body * np.clip((body * 1.3 + eat * .5 - t * 1.0) * 2.2, 0, 1)
        alpha = (1.0 - np.exp(-body * 2.0)) * (1 - .45 * t * t)
        # occlusion: lumps below others are darker (density accumulated from the top), plus each lump's own light
        od = np.cumsum(ndi.gaussian_filter(body, 4), axis=0) / N * lk['shade'] * 3
        shadow = np.clip(.75 * (1 - lit) ** 1.3 + .3 * (1 - np.exp(-od)), 0, 1) * np.clip(alpha * 1.3, 0, 1)
        heat = np.clip((dens - .9) * 1.2, 0, 1) * (1 - lit * .5) * (.75 + .25 * eat) * lk['heat'] * np.clip(1 - t * 2.6, 0, 1)
        cel = np.stack([heat, shadow, np.zeros_like(alpha), alpha], -1)
        cel = np.asarray(Image.fromarray((np.clip(cel, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((CELL, CELL), Image.BOX), np.float32) / 255
        edge = np.minimum(np.arange(CELL), np.arange(CELL)[::-1]).astype(np.float32)
        ramp = np.clip((edge - 1) / 14, 0, 1); ramp = ramp * ramp * (3 - 2 * ramp)
        cel *= (ramp[:, None] * ramp[None, :])[..., None]
        cels.append(cel)
    sheet = np.zeros((TILE * CELL, TILE * CELL, 4), np.float32)
    for i, cel in enumerate(cels):
        r, cc = i // TILE, i % TILE
        sheet[r * CELL:(r + 1) * CELL, cc * CELL:(cc + 1) * CELL] = cel
    img = Image.fromarray((sheet * 255 + .5).astype(np.uint8), 'RGBA')
    for d in (BOOKS, GAME, REVIEW):
        d.mkdir(parents=True, exist_ok=True)
    img.save(BOOKS / f'{name}.png'); img.save(GAME / f'T_V308b_{name}.png')
    # contact sheet as the shader draws it: lit tone (pale wash) sinking to ink in the shadow, heat at the root, on paper
    paper, lit, ink, hot = (np.array(x, np.float32) for x in ((.87, .84, .77), (.42, .40, .38), (.06, .055, .05), (.66, .24, .05)))
    a = sheet[..., 3:4]
    col = lit + (ink - lit) * np.clip((sheet[..., 1:2] - .15) / .6, 0, 1)
    col = col + (hot - col) * np.clip((sheet[..., 0:1] - .3) / .45, 0, 1)
    shade = paper * (1 - a) + col * a
    card = Image.fromarray((np.clip(shade, 0, 1) * 255).astype(np.uint8))
    ImageDraw.Draw(card).text((8, 8), f'{name}  16 cels  (lit from above, shadow -> ink)', fill=(30, 30, 30))
    card.save(REVIEW / f'flipbook_v2_{name}.png')
    print('SHEET smoke', name, TILE * CELL, 'alpha mean %.3f' % float(sheet[..., 3].mean()))




def wisp(name='soot_wisp', src='fire_lick', tile=4):
    """the 노 soot (pq_smoke 2026-10-15): fire_lick's cels with the cut root removed - each cel's ink meets its left border in a
    straight block (the lick's root), which showed in game as the straight edge of a card. Alpha fades in over the left 60 %
    (from 10 %) and toward the top / bottom borders, the heat is kept only as a faint tint inside the wisp."""
    img = np.asarray(Image.open(GAME / f'T_V308b_{src}.png').convert('RGBA'), np.float32) / 255
    cell = img.shape[0] // tile
    u = (np.arange(cell, dtype=np.float32) + .5) / cell
    left = np.clip((u - .1) / .5, 0, 1); left = left * left * (3 - 2 * left)
    side = np.clip(np.minimum(u, 1 - u) / .18, 0, 1); side = side * side * (3 - 2 * side)
    mask = side[:, None] * left[None, :]
    out = img.copy()
    for r in range(tile):
        for c in range(tile):
            cl = out[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
            cl[..., 3] *= mask; cl[..., 2] *= mask; cl[..., 1] *= mask; cl[..., 0] *= mask * .6
    Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(GAME / f'T_V308b_{name}.png')
    Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(BOOKS / f'{name}.png')
    a = out[..., 3:4]; paper = np.array((.87, .84, .77), np.float32); col = np.array((.35, .22, .18), np.float32)
    col = col + (np.array((.06, .05, .05), np.float32) - col) * np.clip((out[..., 1:2] - .2) / .5, 0, 1)
    Image.fromarray((np.clip(paper * (1 - a) + col * a, 0, 1) * 255).astype(np.uint8)).save(REVIEW / f'flipbook_v2_{name}.png')
    print('SHEET wisp', name, 'from', src, img.shape[0])


if __name__ == '__main__':
    for nm in [a for a in sys.argv[1:] if not a.startswith('--')] or list(LOOK) + ['soot_wisp']:
        wisp(nm) if nm == 'soot_wisp' else make(nm)
