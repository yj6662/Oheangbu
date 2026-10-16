#!/usr/bin/env python3
"""#308 노 fire, 19th pass (Spec 11.34; user 2026-10-16 "자국도 불 따라 늦게").

  python Tools/Art/SpellVFX308/stain_wipe19.py [--flip] [--preview DIR]

The 18th-pass ground stain (T_V308b_fire_stain_vfx.png, fire17_fluid.py stain) appears whole at once while the fire ring lights
near -> sides -> far. This repacks it as a 4 x 4 flipbook (cel 512^2, sheet 2048^2) in which the stain is revealed from the caster
side to the far side; the last cel is the whole stain, so the end look is unchanged. StainVfx plays it with a texIndex-over-life
curve (0 -> 15 over the first ~.36 s, then held at exactly 15).

  reveal coordinate w (0 = near, 1 = far), per pixel:
    lin = straight near -> far distance over [-RE, RE]
    ang = angle from the near direction / pi        (follows the ring: near arc -> sides -> far arc)
    w   = lerp(lin, ang, smoothstep(.15, .45, r / R)) + low-frequency noise, clipped to [0, 1]
  cel i < 15 keeps the pixels with w < s_i (soft edge EDGE), s_i = (i + 1) / 16 * (1 + EDGE); cel 15 = the whole stain.

Near = +Z of the stain sheet (bottom image rows, Unity UV v = 0 = the quad's -AxisY = toward the caster); --flip puts near at the top
rows instead. Unverified until the first Play (Spec 11.34). Alpha and colour are downsampled separately (alpha-weighted colour, the
source is already colour-bled) and the outer EDGE_PX pixels of each cel fade out so mip levels do not bleed between cels."""
import argparse
from pathlib import Path
import numpy as np
from scipy.ndimage import gaussian_filter
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
GAME = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures'
SRC = GAME / 'T_V308b_fire_stain_vfx.png'
DST = GAME / 'T_V308b_fire_stainwipe_vfx.png'

R = 5.4          # the stain sheet covers [-R, R] m (fire17_fluid.py STAIN.R)
RE = 4.0         # linear term range: almost all stain alpha is inside r < 4 m
CEL = 512
GRID = 4
EDGE = .12       # reveal edge softness in w
NOISE = .10      # reveal edge raggedness in w
EDGE_PX = 6      # cel border fade (mip bleeding between cels)
SEED = 19


def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def down(img):
    """1024 straight-alpha RGBA float -> CEL, alpha-weighted colour (falls back to the plain average where alpha is ~0)."""
    a = img[..., 3]
    def box(x):
        return np.asarray(Image.fromarray(x.astype(np.float32), 'F').resize((CEL, CEL), Image.BOX))
    ad = box(a)
    out = np.zeros((CEL, CEL, 4), np.float32)
    for c in range(3):
        pm = box(img[..., c] * a)
        plain = box(img[..., c])
        out[..., c] = np.where(ad > 1e-4, pm / np.maximum(ad, 1e-4), plain)
    out[..., 3] = ad
    return np.clip(out, 0, 1)


def reveal_coord(flip):
    yy, xx = np.mgrid[0:CEL, 0:CEL].astype(np.float64)
    X = (xx / (CEL - 1) * 2 - 1) * R
    Z = (yy / (CEL - 1) * 2 - 1) * R
    u = -Z if flip else Z                       # + toward the caster
    r = np.hypot(X, Z)
    lin = np.clip((RE - u) / (2 * RE), 0, 1)
    ang = np.abs(np.arctan2(X, u)) / np.pi
    k = ss(.15, .45, r / R)
    n = gaussian_filter(np.random.default_rng(SEED).random((CEL, CEL)), CEL / 24)
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    return np.clip(lin * (1 - k) + ang * k + NOISE * (n - .5), 0, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--flip', action='store_true', help='near = top image rows instead of bottom')
    ap.add_argument('--preview', help='also write a small labelled preview of the 16 cels to DIR')
    a = ap.parse_args()

    src = np.asarray(Image.open(SRC).convert('RGBA')).astype(np.float32) / 255
    if src.shape[:2] != (1024, 1024):
        raise SystemExit('unexpected stain size %s' % (src.shape,))
    full = down(src)
    i = np.arange(CEL)
    e = np.minimum(i, CEL - 1 - i)
    border = ss(0, EDGE_PX, np.minimum(e[:, None], e[None, :]).astype(np.float64))
    full[..., 3] *= border
    w = reveal_coord(a.flip)

    sheet = np.zeros((GRID * CEL, GRID * CEL, 4), np.float32)
    for c in range(GRID * GRID):
        cel = full.copy()
        if c < GRID * GRID - 1:
            s = (c + 1) / (GRID * GRID) * (1 + EDGE)
            cel[..., 3] *= ss(s, s - EDGE, w)
        r0, c0 = divmod(c, GRID)                 # row-major from the top-left, same order as fire17_fluid.py's band sheet
        sheet[r0 * CEL:(r0 + 1) * CEL, c0 * CEL:(c0 + 1) * CEL] = cel
        print('  cel %2d alpha mean %.4f' % (c, cel[..., 3].mean()))
    img = (np.clip(sheet, 0, 1) * 255 + .5).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(DST)
    print('SHEET fire stainwipe', img.shape[1], 'x', img.shape[0], 'near =', 'top' if a.flip else 'bottom',
          'source alpha mean %.3f' % src[..., 3].mean(), DST.name)

    if a.preview:
        Path(a.preview).mkdir(parents=True, exist_ok=True)
        rgb = img[..., :3].astype(np.float32) / 255
        al = img[..., 3:].astype(np.float32) / 255
        ground = np.array([.78, .74, .66], np.float32)
        comp = (rgb * al + ground * (1 - al)) * 255
        p = Path(a.preview) / 'stainwipe_preview.jpg'
        Image.fromarray(comp.astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(p, quality=88)
        print('PREVIEW', p)


if __name__ == '__main__':
    main()
