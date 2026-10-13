"""#308 HeroHanok: small tiling textures made here (no pack source): roof tile, dancheong beam band, lattice door leaf.
  python Tools/Blender/HeroHanok308/textures.py        (system python, numpy only)
Writes Art/World/PackLite308/hero/Hero_{Tile,Dancheong,Lattice}_BC.png (+ _N.png).  No emission, plain albedo + tangent normal.
"""
import sys
from pathlib import Path
import numpy as np
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / 'PackLite308'))
import pl_view as V
OUT = HERE.parents[2] / 'Art/World/PackLite308/hero'


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-gx, -gy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def save(name, img):   # img[0] = top row; V.save_png writes row 0 at the top; UV v=0 is the bottom row -> flip
    V.save_png(str(OUT / name), img[::-1])


def tile():
    S = 256; rng = np.random.default_rng(3)
    v, u = np.mgrid[0:S, 0:S] / S
    pu = (u * 4) % 1.0; pv = (v * 4) % 1.0           # 4 pitches x 4 courses; u=0 valley centre, u=.5 crest centre
    x = np.abs(pu - 0.5)
    cyl = np.sqrt(np.clip(1 - (x / 0.2) ** 2, 0, 1))  # 수키와
    val = 0.25 * np.clip((x - 0.2) / 0.3, 0, 1) ** 2 * -1 + 0.0   # 암키와 dips toward the valley centre
    h = np.where(x < 0.2, 0.35 + 0.65 * cyl, 0.35 + val) + 0.10 * (1 - pv)
    lip = (pv > 0.93).astype(np.float32)
    base = np.array([0.205, 0.215, 0.235])
    a = base[None, None, :] * (0.72 + 0.42 * h[:, :, None]) * (1 - 0.35 * lip[:, :, None])
    a = a * (1 + rng.normal(0, 0.05, (S, S, 1))) + rng.normal(0, 0.008, (S, S, 3))
    save('Hero_Tile_BC.png', np.clip(a, 0, 1)); save('Hero_Tile_N.png', normal_from_height(h, 10.0))


def dancheong():
    W, H = 256, 64; rng = np.random.default_rng(5)
    img = np.empty((H, W, 3)); img[:] = (0.34, 0.50, 0.41)             # 뇌록
    red, white, blue, yel, blk, grn2 = (0.62, 0.19, 0.13), (0.90, 0.88, 0.80), (0.15, 0.31, 0.58), (0.84, 0.60, 0.20), (0.08, 0.08, 0.09), (0.20, 0.42, 0.30)
    seq = [(red, 9), (white, 3), (blue, 8), (white, 3), (yel, 6), (blk, 2), (grn2, 7), (white, 2), (red, 6), (blk, 2)]
    x = 0
    for c, w in seq:
        img[:, x:x + w] = c; img[:, W - x - w:W - x] = c; x += w
    # 머리초 petals: scalloped edge into the green field
    yy = np.arange(H)
    for k in range(3):
        cy = H * (k + 0.5) / 3
        for xi in range(x, x + 14):
            r = 14 - (xi - x)
            m = np.abs(yy - cy) < r * 0.7
            col = red if (xi - x) < 5 else (yel if (xi - x) < 8 else white if (xi - x) < 10 else blue)
            img[m, xi] = col; img[m, W - 1 - xi] = col
    img[:4] = blk; img[4:7] = white; img[-4:] = blk; img[-7:-4] = white
    img = img * (1 + rng.normal(0, 0.04, (H, W, 1)))
    save('Hero_Dancheong_BC.png', np.clip(img, 0, 1))


def lattice():
    S = 256; rng = np.random.default_rng(9)
    img = np.empty((S, S, 3)); h = np.zeros((S, S))
    paper, bar, frame = (0.80, 0.72, 0.56), (0.42, 0.12, 0.09), (0.50, 0.14, 0.10)
    img[:] = paper
    yy, xx = np.mgrid[0:S, 0:S]                     # row 0 = top of the leaf
    d1 = (xx + yy) % 20; d2 = (xx - yy) % 20         # 빗살
    m = (d1 < 4) | (d2 < 4) | (xx % 40 < 3)
    img[m] = bar; h[m] = 0.6
    top = 16; bot = int(S * 0.76)
    panel = yy >= bot                                 # 궁판
    img[panel] = (0.46, 0.13, 0.10); h[panel] = 0.5
    inner = panel & (yy > bot + 12) & (yy < S - 18) & (xx > 26) & (xx < S - 26)
    img[inner] = (0.40, 0.11, 0.09); h[inner] = 0.3
    fr = (xx < 14) | (xx >= S - 14) | (yy < top) | (yy >= S - 12) | ((yy >= bot - 6) & (yy < bot + 6))
    img[fr] = frame; h[fr] = 1.0
    img = img * (1 + rng.normal(0, 0.035, (S, S, 1)))
    save('Hero_Lattice_BC.png', np.clip(img, 0, 1)); save('Hero_Lattice_N.png', normal_from_height(h, 2.5))


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    tile(); dancheong(); lattice(); print('textures ->', OUT)
