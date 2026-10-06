# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - kit part 2: the atlas (frames, plates, shell pieces, inlay masks) and the kit manifest.
Deterministic (numpy + PIL encode only, seeded, no clock). No picture file is ever read here (AC-T11).

Writes into the STAGE only (never into Oheangbu/Assets):
  Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme/
    theme308_atlas.png     512 x 512 RGBA (sRGB, straight alpha), every cell authored at 2 texels per design px
    theme308_atlas.json    cell rects (top-left and Unity bottom-left origin), borders, use (Simple / Sliced / Tiled), tints,
                           import settings, per-cell sha256
    theme308_manifest.json every kit file: name, pixel size, channels, bytes, sha256 (tokens + tiles + atlas)

Two kinds of cell (Spec 5.1):
  baked  two materials in one shape (lacquer + nacre, porcelain + inlay): fixed colours, Graphic.color = white
  mask   one material: white RGB + alpha coverage, tinted by a token through Graphic.color
Every stretched line is built so that Image.Type.Tiled never stretches a shell piece: butt joints sit exactly on the slice
lines, and the middle section of a frame is one whole period. A period holds several pieces of ONE length (three or four),
each with its own hue: a side is then not one piece repeated, and a rect cut at any multiple of the piece length still ends
on a joint (Tiled clips the last period, it never squeezes it).

usage (repo root):  python Tools/Art/theme308_assets.py            write
                    python Tools/Art/theme308_assets.py --check    build twice in memory and compare with the files (exit 1)
"""
import hashlib
import io
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L      # noqa: E402
import theme308_tiles as TL   # noqa: E402  (part 1: tokens + lattice tiles; rebuilt in memory for the manifest)

OUT = TL.OUT
S = L.TILE_SCALE              # texels per design px (2)
SS = 4                        # supersampling of the shell pieces and the inlay masks
GUT = 6                       # texels between two cells and to the atlas edge
EXT = 3                       # texels of every cell's edge copied into its gutter (wrap along a tiled axis, clamp otherwise)
ATLAS_W, ATLAS_H = 512, 512
T = L.T
G = L.NACRE['joint_px']
FAM_P = L.NACRE_FAMILY_SHARE  # green / blue / pink pieces (seeded draw, never a fixed green-blue-pink cycle; pink is the
                              # rare flash - an even three-way split read as candy stripes at 300 %)
FACE_CHROMA = L.NACRE_FACE_CHROMA   # wide shell faces (petals, rosettes) keep 60 % of the line chroma: pearl, not pastel

# geometry of the frames (design px at 1080p; TEST - the consuming Specs own the rect sizes, the kit owns these)
MINI = dict(slice=24.0, period=36.0, piece=12.0, frame=9.0, line=(7.0, 8.5), ear=(3.75, 13.75))
# band0 is a whole px: the 1 px bars of the band then cover whole pixels at 1080p (mip 1); 6.5 smeared every bar over two
BOARD = dict(slice=(40.0, 42.0, 40.0, 40.0), period=27.0, piece=9.0, line_c=3.75, line_w=1.5, step=4.0,
             band0=6.0, band_h=10.0, band_bottom_extra=2.0, u=3.0, bar=1.0, notch=9.0)
SELECT = dict(slice=12.0, period=30.0, piece=10.0, line_w=2.0)
LINE = dict(period=128.0, h=4.0, w=2.0, w_thin=1.5, y_thin=(1.0, 2.5),
            cuts=(11.0, 9.5, 12.5, 9.0, 12.5, 9.5, 11.5, 9.5, 12.5, 9.5, 11.5, 9.5))
PLATE = dict(size=32.0, slice=8.0, rim=1.5, rim_a=0.92)
ROUND = dict(size=48.0, d=44.0, rim=1.4, rim_a=0.92)
# gap 3.0 (not 2.5): the 1 px inner line then lies on a whole pixel at 1080p instead of two half-covered ones
PLAQUE = dict(size=48.0, slice=16.0, inset=7.0, w1=2.0, gap=3.0, w2=1.0, w_single=1.5, foot=3.0)
KEYCAP = dict(size=32.0, slice=12.0, rim=2.0, lip=5.0)
NORTH_SEAT = 2.0               # px of lacquer round the north petal
NORTH_CELL = (16.0, 18.0)      # px: 2 px of air above the seat's tip (a 16 px cell cut the tip flat)


# ---- drawing on a cell (straight-alpha float RGBA at S texels per design px) -------------------------------------------
def blank(w, h):
    return np.zeros((int(round(h * S)), int(round(w * S)), 4))


def put(img, col, cov):
    a = np.clip(cov, 0, 1)
    b = img[..., 3]
    o = a + b * (1 - a)
    img[..., :3] = (np.asarray(col, float)[None, None, :] * a[..., None] + img[..., :3] * (b * (1 - a))[..., None]) / np.maximum(o, 1e-6)[..., None]
    img[..., 3] = o


def over(img, src):
    a = src[..., 3]
    b = img[..., 3]
    o = a + b * (1 - a)
    img[..., :3] = (src[..., :3] * a[..., None] + img[..., :3] * (b * (1 - a))[..., None]) / np.maximum(o, 1e-6)[..., None]
    img[..., 3] = o


def box(img, x0, y0, x1, y1, col, a=1.0):
    """Axis-aligned rect in design px with exact box coverage (crisp when the edges sit on the half-px grid)."""
    H, W = img.shape[:2]
    xs, ys = np.arange(W), np.arange(H)
    cx = np.clip(np.minimum(x1 * S, xs + 1) - np.maximum(x0 * S, xs), 0, 1)
    cy = np.clip(np.minimum(y1 * S, ys + 1) - np.maximum(y0 * S, ys), 0, 1)
    put(img, col, cy[:, None] * cx[None, :] * a)


def ring(img, x0, y0, x1, y1, t, col, a=1.0):
    """A rectangular line of thickness t whose outer edge is the given rect."""
    box(img, x0, y0, x1, y0 + t, col, a)
    box(img, x0, y1 - t, x1, y1, col, a)
    box(img, x0, y0 + t, x0 + t, y1 - t, col, a)
    box(img, x1 - t, y0 + t, x1, y1 - t, col, a)


# ---- cut-shell pieces ---------------------------------------------------------------------------------------------------
def run(ax, ay, bx, by, width, joints=(), ends=(False, False)):
    """kkeuneumjil: a straight run from A to B, butt joints centred at the given distances from A. ends = (at A, at B):
    True leaves half a joint at that end (the run continues in the next tile), False ends flat on the point."""
    Ln = math.hypot(bx - ax, by - ay)
    ux, uy = (bx - ax) / Ln, (by - ay) / Ln
    cuts = [0.0] + sorted(joints) + [Ln]
    out = []
    for i in range(len(cuts) - 1):
        s0 = cuts[i] + (G / 2 if (i > 0 or ends[0]) else 0.0)
        s1 = cuts[i + 1] - (G / 2 if (i < len(cuts) - 2 or ends[1]) else 0.0)
        out.append(dict(poly=L._quad(ax + ux * s0, ay + uy * s0, ax + ux * s1, ay + uy * s1, width),
                        axis=math.degrees(math.atan2(uy, ux))))
    return out


def dress(pieces, key):
    """Give every piece a stable identity and a hue family from a seeded draw (the same key -> the same pieces)."""
    rng = np.random.default_rng([L.SEED, key])
    fam = rng.choice(3, size=len(pieces), p=FAM_P)
    return [dict(p, rid=i, fam=int(fam[i])) for i, p in enumerate(pieces)]


def shells(img, pieces, key, redress=True, chroma=1.0):
    w, h = img.shape[1] / float(S), img.shape[0] / float(S)
    pcs = dress(pieces, key) if redress else pieces
    na = L.render_pieces(w, h, pcs, s=S, ss=SS, seed=L.SEED + key)
    if chroma != 1.0:                                  # scale the OKLab chroma: lightness and hue stay where they are
        lab = L.srgb_to_oklab(na[..., :3])
        na[..., :3] = L.oklab_to_srgb(lab[..., 0], lab[..., 1] * chroma, lab[..., 2] * chroma)
    over(img, na[:img.shape[0], :img.shape[1]])
    return float(na[..., 3].sum() / (S * S))          # nacre area in design px^2


def mirror(pts, W, H, fx, fy):
    return [((W - x) if fx else x, (H - y) if fy else y) for x, y in pts]


# ---- cells --------------------------------------------------------------------------------------------------------------
def cell_frame_mini():
    """Frame.Mini: lacquer door frame 9 px, one cut-shell line just outside the window, a square ear on every corner."""
    c, m, f = MINI['slice'], MINI['period'], MINI['frame']
    W = 2 * c + m
    img = blank(W, W)
    ring(img, 0, 0, W, W, f, T['Lacquer'])
    lc = (MINI['line'][0] + MINI['line'][1]) / 2
    lw = MINI['line'][1] - MINI['line'][0]
    e, n = MINI['ear']
    pcs = []
    j = [c - n + i * MINI['piece'] for i in range(int(round(m / MINI['piece'])) + 1)]
    pcs += run(n, lc, W - n, lc, lw, j) + run(n, W - lc, W - n, W - lc, lw, j)
    pcs += run(lc, n, lc, W - n, lw, j) + run(W - lc, n, W - lc, W - n, lw, j)
    ear = [(lc, n), (e, n), (e, e), (n, e), (n, lc)]
    for fx in (0, 1):
        for fy in (0, 1):
            pcs += L.strips(mirror(ear, W, W, fx, fy), lw, piece=20)
    area = shells(img, pcs, 1)
    return dict(name='frame_mini', kit='Frame.Mini', img=img, border=(c, c, c, c), use='Tiled', fill_center=False,
                mode='baked', tile='9', period_px=m, piece_px=MINI['piece'], nacre_px2=area,
                note='rect = 48 + 12 n on both axes (the 312 x 228 minimap frame: n = 22 and 15); window = rect inset 9; '
                     'the middle period is three 12 px pieces')


def cell_frame_board():
    """Frame.MapBoard: the lacquer board under the full map: a cut-shell line with a two-step aja corner and a wanja band."""
    B = BOARD
    sl, sb, sr, st = B['slice']
    p = B['period']
    W, H = sl + p + sr, st + p + sb
    img = blank(W, H)
    box(img, 0, 0, W, H, T['Lacquer'])
    # the wanja band: one global lattice, bars on every boundary of the band, four strips that meet at the inner corners
    h, w = img.shape[:2]
    tile = L.lattice('wanja', scale=S, u=B['u'], bar=B['bar'], edge=True)
    th, tw = tile.shape
    ys, xs = np.mgrid[0:h, 0:w]
    b0 = B['band0']
    ox, oy = int(round(b0 * S)), int(round((b0 + B['bar']) * S)) - th
    lat = tile[(ys - oy) % th, (xs - ox) % tw] > .5
    X, Y = (xs + .5) / S, (ys + .5) / S
    bb = b0 + B['band_bottom_extra']
    A = (X >= b0) & (X < W - b0) & (Y >= b0) & (Y < H - bb)
    Bm = (X >= b0 + B['band_h']) & (X < W - b0 - B['band_h']) & (Y >= b0 + B['band_h']) & (Y < H - bb - B['band_h'])
    nt = b0 + B['notch']
    notch = ((X < nt) | (X >= W - nt)) & ((Y < nt) | (Y >= H - bb - B['notch']))
    reg = A & ~Bm & ~notch
    er = reg.copy()
    k = int(round(B['bar'] * S))
    for d in range(1, k + 1):
        for dy, dx in ((d, 0), (-d, 0), (0, d), (0, -d), (d, d), (d, -d), (-d, d), (-d, -d)):
            er &= np.roll(np.roll(reg, dy, axis=0), dx, axis=1)
    band = reg & (lat | ~er)
    put(img, T['Wood'], band.astype(float))
    # the cut-shell line
    c, lw, s = B['line_c'], B['line_w'], B['step']
    a = c + 2 * s
    q = B['piece']
    mid = [i * q for i in range(int(round(p / q)) + 1)]
    jx = [v - a for v in [sl - 2 * q, sl - q] + [sl + v for v in mid] + [W - sr + q, W - sr + 2 * q]]
    jy = [v - a for v in [st - 2 * q, st - q] + [st + v for v in mid] + [H - sb + q, H - sb + 2 * q + (sb - st)]]
    pcs = run(a, c, W - a, c, lw, jx) + run(a, H - c, W - a, H - c, lw, jx)
    pcs += run(c, a, c, H - a, lw, jy) + run(W - c, a, W - c, H - a, lw, jy)
    stair = [(c, a), (c + s, a), (c + s, c + s), (a, c + s), (a, c)]
    for fx in (0, 1):
        for fy in (0, 1):
            pcs += L.strips(mirror(stair, W, H, fx, fy), lw, piece=20)
    area = shells(img, pcs, 2)
    return dict(name='frame_board', kit='Frame.MapBoard', img=img, border=(sl, sb, sr, st), use='Tiled', fill_center=False,
                mode='baked', tile='9', period_px=p, piece_px=B['piece'], nacre_px2=area,
                band_contrast=round(L.contrast(T['Wood'], T['Lacquer']), 2),
                note='rect = 80 + 9 n wide, 82 + 9 m high (the 836 x 892 board: n = 84, m = 90); body = White x Lacquer under it; '
                     'the middle period is three 9 px pieces over three periods of the band')


def _line(width, y0, key, name, kit):
    P, h = LINE['period'], LINE['h']
    img = blank(P, h)
    cuts = np.cumsum(LINE['cuts'])[:-1]
    assert abs(sum(LINE['cuts']) - P) < 1e-9
    area = shells(img, run(0, y0 + width / 2, P, y0 + width / 2, width, list(cuts), ends=(True, True)), key)
    return dict(name=name, kit=kit, img=img, border=(0, 0, 0, 0), use='Tiled', mode='baked', tile='x', period_px=P,
                nacre_px2=area, line_px=[y0, y0 + width],
                note='length = 128 n keeps whole pieces (the 1792 px rail: n = 14); any other length ends on a cut piece')


def cell_line():
    return _line(LINE['w'], (LINE['h'] - LINE['w']) / 2, 3, 'line_najeon', 'Inlay.Line')


def cell_line_thin():
    return _line(LINE['w_thin'], LINE['y_thin'][0], 4, 'line_najeon_thin', 'Inlay.LineThin')


def cell_corner_fret():
    """Corner.Fret: the two-step aja turn alone, for a frame laid from Inlay.LineThin strips. Line centre 1.75 px in."""
    n, c, s, lw = 16.0, 1.75, BOARD['step'], BOARD['line_w']
    img = blank(n, n)
    a = c + 2 * s
    pcs = L.strips([(c, a), (c + s, a), (c + s, c + s), (a, c + s), (a, c)], lw, piece=20)
    pcs += run(c, a, c, n, lw) + run(a, c, n, c, lw)
    area = shells(img, pcs, 5)
    return dict(name='corner_fret', kit='Corner.Fret', img=img, border=(0, 0, 0, 0), use='Simple', mode='baked',
                nacre_px2=area, note='top-left corner; flip for the other three; the line leaves the cell 1.0 - 2.5 px from its edge')


def _centred(img, w, h, pcs):
    ox, oy = (img.shape[1] / S - w) / 2.0, (img.shape[0] / S - h) / 2.0
    return [dict(p, poly=[(x + ox, y + oy) for x, y in p['poly']]) for p in pcs]


def cell_piece_north():
    """Piece.North: a pointed shell piece on its own lacquer seat (the petal is taller than the 9 px frame, and nacre never
    sits on the bare world: the seat is the petal grown by 2 px)."""
    img = blank(*NORTH_CELL)
    w, h, pcs = L.najeon_north(7.0, 12.0)
    ox, oy = (NORTH_CELL[0] - w) / 2.0, NORTH_CELL[1] - h - 1.0       # centred across, the seat's foot on the cell's lower edge
    pcs = [dict(p, poly=[(x + ox, y + oy) for x, y in p['poly']]) for p in pcs]
    k = S * SS
    hh, ww = img.shape[0] * SS, img.shape[1] * SS
    im = Image.new('L', (ww, hh), 0)
    d = ImageDraw.Draw(im)
    pts = [(x * k, y * k) for x, y in pcs[0]['poly']]
    d.polygon(pts, fill=255)
    d.line(pts + [pts[0]], fill=255, width=int(round(2 * NORTH_SEAT * k)), joint='curve')
    seat = (np.asarray(im, float) / 255.0).reshape(img.shape[0], SS, img.shape[1], SS).mean(axis=(1, 3))
    put(img, T['Lacquer'], seat)
    area = shells(img, pcs, 6, redress=False, chroma=FACE_CHROMA)
    return dict(name='piece_north', kit='Piece.North', img=img, border=(0, 0, 0, 0), use='Simple', mode='baked', nacre_px2=area,
                note='petal 7 x 12 px pointing up on a lacquer seat (petal + 2 px), cell 16 x 18 px; cell bottom = 7 px inside the '
                     'frame outer edge, so the seat ends on the frame line and the tip stands 9 px above the frame')


def cell_pip_petal():
    img = blank(12, 14)
    w, h, pcs = L.najeon_petal(7.0, 10.0)
    area = shells(img, _centred(img, w, h, [dict(p, fam=1) for p in pcs]), 7, redress=False, chroma=FACE_CHROMA)
    return dict(name='pip_petal', kit='Pip.Petal', img=img, border=(0, 0, 0, 0), use='Simple', mode='baked', nacre_px2=area,
                note='a reached tier; a tier not reached yet stays the #304 Mist a.35 dab')


def cell_plate_lacquer():
    n, r = PLATE['size'], PLATE['rim']
    img = blank(n, n)
    box(img, 0, 0, n, n, T['Lacquer'])
    rim = blank(n, n)
    ring(rim, 0, 0, n, n, r, T['Paper'], PLATE['rim_a'])
    over(img, rim)
    img[..., 3] = np.maximum(img[..., 3], 0.0)
    b = PLATE['slice']
    return dict(name='plate_lacquer', kit='Plate.Icon', img=img, border=(b, b, b, b), use='Sliced', fill_center=True, mode='baked',
                note='small lacquer plate with a paper hairline (1.5 px, a .92 over lacquer); under a map legend icon')


def cell_plate_round():
    n, d, r = ROUND['size'], ROUND['d'], ROUND['rim']
    img = blank(n, n)
    h, w = img.shape[:2]
    ss = SS
    ys, xs = np.mgrid[0:h * ss, 0:w * ss]
    rr = np.hypot((xs + .5) / (S * ss) - n / 2, (ys + .5) / (S * ss) - n / 2)
    disc = (rr <= d / 2).astype(float)
    inner = (rr <= d / 2 - r).astype(float)
    dn = lambda m: m.reshape(h, ss, w, ss).mean(axis=(1, 3))
    put(img, T['Paper'], dn(disc) * ROUND['rim_a'])
    put(img, T['Lacquer'], dn(inner))
    return dict(name='plate_round', kit='Plate.Round', img=img, border=(0, 0, 0, 0), use='Simple', mode='baked',
                note='lacquer button 44 px + paper hairline 1.4 px, for menus (the HUD draws its own in UI/InkVessel308)')


def _plaque(single):
    n, P = PLAQUE['size'], PLAQUE
    img = blank(n, n)
    box(img, 0, 0, n, n, T['Porcelain'])
    box(img, 0, n - P['foot'], n, n, T['PorcelainShade'])
    i = P['inset']
    if single:
        ring(img, i, i, n - i, n - i, P['w_single'], T['InlayDark'])
    else:
        ring(img, i, i, n - i, n - i, P['w1'], T['InlayDark'])
        j = i + P['w1'] + P['gap']
        ring(img, j, j, n - j, n - j, P['w2'], T['InlayDark'])
    b = P['slice']
    return img, (b, b, b, b)


def cell_plaque():
    img, b = _plaque(False)
    return dict(name='plaque_porcelain', kit='Plaque.Porcelain', img=img, border=b, use='Sliced', fill_center=True, mode='baked',
                note='porcelain plaque + inlaid double line (2 / 3 / 1 px, 7 px in) + foot 3 px; the detail picture frame')


def cell_plaque_single():
    img, b = _plaque(True)
    return dict(name='plaque_porcelain_single', kit='Plaque.PorcelainSingle', img=img, border=b, use='Sliced', fill_center=True,
                mode='baked', note='porcelain plaque + one inlaid line 1.5 px + foot 3 px; text panels keep the pattern-free inside')


def cell_keycap():
    n, K = KEYCAP['size'], KEYCAP
    img = blank(n, n)
    box(img, 0, 0, n, n, T['InlayDark'])
    r = K['rim']
    box(img, r, r, n - r, n - r, T['PorcelainShade'])
    box(img, r, r, n - r, n - r - K['lip'], T['Porcelain'])
    b = K['slice']
    return dict(name='keycap_porcelain', kit='Key.Porcelain', img=img, border=(b, b, b, b), use='Sliced', fill_center=True,
                mode='baked', note='filled key cap: porcelain face, dark inlay rim 2 px, shade lip 5 px (same slices as #304 keycap)')


def cell_frame_select():
    c, m, lw = SELECT['slice'], SELECT['period'], SELECT['line_w']
    W = 2 * c + m
    img = blank(W, W)
    j = [c + i * SELECT['piece'] for i in range(int(round(m / SELECT['piece'])) + 1)]
    pcs = run(0, lw / 2, W, lw / 2, lw, j) + run(0, W - lw / 2, W, W - lw / 2, lw, j)
    jv = [v - lw - G for v in j]
    pcs += run(lw / 2, lw + G, lw / 2, W - lw - G, lw, jv) + run(W - lw / 2, lw + G, W - lw / 2, W - lw - G, lw, jv)
    area = shells(img, pcs, 8)
    return dict(name='frame_select', kit='Frame.Select', img=img, border=(c, c, c, c), use='Tiled', fill_center=False, mode='baked',
                tile='9', period_px=m, piece_px=SELECT['piece'], nacre_px2=area,
                note='rect = 24 + 10 n (a 104 px cell + 5 px on every side = 114: n = 9); kept selection, never the focus; '
                     'the middle period is three 10 px pieces')


def cell_white():
    img = blank(4, 4)
    box(img, 0, 0, 4, 4, (1.0, 1.0, 1.0))
    return dict(name='white', kit='White', img=img, border=(0, 0, 0, 0), use='Simple', mode='mask',
                note='lattice bars, column rules, board body: one rect each, tinted by a token, in the atlas batch')


def _motif(name, kit, cw, ch, builder, key, tile=None, period=None, note='', chroma=1.0):
    img = blank(cw, ch)
    w, h, pcs = builder
    if tile is None:
        pcs = _centred(img, w, h, pcs)
    area = shells(img, pcs, key, redress=False, chroma=chroma)
    return dict(name=name, kit=kit, img=img, border=(0, 0, 0, 0), use='Tiled' if tile else 'Simple', mode='baked', tile=tile,
                period_px=period, nacre_px2=area, reserve=True, note=note)


def _mask(name, kit, m, period, note):
    h, w = m.shape
    img = np.dstack([np.ones((h, w, 3)), np.clip(m, 0, 1)])
    return dict(name=name, kit=kit, img=img, border=(0, 0, 0, 0), use='Tiled', mode='mask', tile='x', period_px=period,
                reserve=True, tint='InlayDark', note=note)


def build_cells():
    cells = [cell_frame_mini(), cell_frame_board(), cell_line(), cell_line_thin(), cell_corner_fret(), cell_piece_north(),
             cell_plate_lacquer(), cell_plate_round(), cell_plaque(), cell_plaque_single(), cell_keycap(), cell_frame_select(),
             cell_pip_petal(), cell_white()]
    cells += [
        _motif('motif_chrys', 'Motif.Chrys', 48, 48, L.najeon_chrys(22.0), 11, chroma=FACE_CHROMA,
               note='reserve: one figurative motif per screen, never on the HUD'),
        _motif('motif_plum', 'Motif.Plum', 24, 24, L.najeon_plum(10.0), 12, chroma=FACE_CHROMA, note='reserve'),
        _motif('border_fret', 'Border.Fret', 100, 14, L.najeon_fret(5), 13, tile='x', period=20.0, note='reserve: thin border, period 20 px'),
        _motif('border_vine', 'Border.Vine', 88, 28, L.najeon_vine(2), 14, tile='x', period=44.0, note='reserve: wide border, period 44 px'),
        _motif('motif_gwigap', 'Motif.Gwigap', 48, 32, L.najeon_gwigap(4, 2), 15, note='reserve'),
        _mask('sanggam_lotus', 'Sanggam.Lotus', L.inlay_lotus(8, s=S, ss=SS), 16.0, 'reserve: lotus petal band on porcelain, period 16 px'),
        _mask('sanggam_stamps', 'Sanggam.Stamps', L.inlay_stamps(8, s=S, ss=SS), 14.0, 'reserve: stamped chrysanthemum row, period 14 px'),
        _mask('sanggam_vine', 'Sanggam.Vine', L.inlay_vine(3, s=S, ss=SS), 36.0, 'reserve: vine line, period 36 px'),
    ]
    return cells


# ---- atlas --------------------------------------------------------------------------------------------------------------
def pack(cells):
    """Skyline bottom-left packing, deterministic (tallest first)."""
    order = sorted(cells, key=lambda c: (-c['img'].shape[0], -c['img'].shape[1], c['name']))
    sky = [[GUT, GUT, ATLAS_W - 2 * GUT]]          # x, y, w
    for c in order:
        h, w = c['img'].shape[:2]
        need = w + GUT
        best = None
        for i, (sx, sy, sw) in enumerate(sky):
            if sx + w > ATLAS_W - GUT:
                continue
            y, wl, j = sy, need, i
            while wl > 0 and j < len(sky):
                y = max(y, sky[j][1])
                wl -= sky[j][2]
                j += 1
            if wl > 0 and sx + w > ATLAS_W - GUT:
                continue
            if y + h > ATLAS_H - GUT:
                continue
            if best is None or (y, sx) < (best[0], best[1]):
                best = (y, sx)
        if best is None:
            raise RuntimeError('atlas overflow at ' + c['name'])
        y, x = best
        c['rect'] = (x, y, w, h)
        x1, top = x + need, y + h + GUT
        new = []
        for sx, sy, sw in sky:
            if sx + sw <= x or sx >= x1:
                new.append([sx, sy, sw])
                continue
            if sx < x:
                new.append([sx, sy, x - sx])
            if sx + sw > x1:
                new.append([x1, sy, sx + sw - x1])
        new.append([x, top, min(need, ATLAS_W - GUT - x)])
        new.sort()
        sky = [s for s in new if s[2] > 0]
    return cells


def compose(cells):
    at = np.zeros((ATLAS_H, ATLAS_W, 4))
    for c in cells:
        x, y, w, h = c['rect']
        img = c['img']
        at[y:y + h, x:x + w] = img
        tile = c.get('tile')
        for e in range(1, EXT + 1):                                  # gutter: wrap along a tiled axis, clamp otherwise
            at[y:y + h, x - e] = img[:, (w - e) if tile == 'x' else 0]
            at[y:y + h, x + w - 1 + e] = img[:, (e - 1) if tile == 'x' else w - 1]
        for e in range(1, EXT + 1):
            at[y - e, x - EXT:x + w + EXT] = at[y, x - EXT:x + w + EXT]
            at[y + h - 1 + e, x - EXT:x + w + EXT] = at[y + h - 1, x - EXT:x + w + EXT]
    rgb, a = at[..., :3].copy(), at[..., 3]
    known = a > 0
    for _ in range(8):                                               # colour bleed under transparent texels (no dark fringe)
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0:
                    continue
                k = np.zeros_like(known)
                v = np.zeros_like(rgb)
                ys0, ys1 = max(0, dy), ATLAS_H + min(0, dy)
                xs0, xs1 = max(0, dx), ATLAS_W + min(0, dx)
                k[ys0:ys1, xs0:xs1] = known[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
                v[ys0:ys1, xs0:xs1] = rgb[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
                acc += v * k[..., None]
                cnt += k
        new = ~known & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        known |= new
    rgb[~known] = 0.0
    out = np.dstack([rgb, a])
    return np.round(np.clip(out, 0, 1) * 255).astype(np.uint8)


def png_bytes(arr, mode):
    b = io.BytesIO()
    Image.fromarray(arr, mode).save(b, 'PNG', optimize=False)
    return b.getvalue()


def dump(o):
    return (json.dumps(o, ensure_ascii=False, indent=1) + '\n').encode('utf-8')


def build():
    cells = pack(build_cells())
    at = compose(cells)
    png = png_bytes(at, 'RGBA')
    used = sum((c['rect'][2] + GUT) * (c['rect'][3] + GUT) for c in cells)
    meta = dict(
        spec='SPEC-UI-THEME-308', decision='D308-15', status='TEST', generator='Tools/Art/theme308_assets.py',
        file='theme308_atlas.png', size=[ATLAS_W, ATLAS_H], texels_per_design_px=S, gutter=GUT, extrude=EXT,
        fill=round(used / float(ATLAS_W * ATLAS_H), 3), sha256=hashlib.sha256(png).hexdigest(),
        bytes_rgba32=ATLAS_W * ATLAS_H * 4, bytes_rgba32_mips=int(ATLAS_W * ATLAS_H * 4 * 4 / 3),
        import_settings=dict(kind='Sprite', spriteMode='Multiple', meshType='FullRect', pixelsPerUnit=100 * S, sRGB=True,
                             alphaSource='FromInput', alphaIsTransparency=True, mips=True, wrap='Clamp', filter='Trilinear',
                             format='RGBA32', compression='None', npotScale='None', readable=False, maxSize=512),
        hud_collar=dict(L.COLLAR, where='hud308_atlas.png vessel cell, G channel: 0 none, 0.5 lacquer, 1 nacre',
                        function='theme308_lib.collar_fields'),
        cells=[],
    )
    for c in sorted(cells, key=lambda c: c['name']):
        x, y, w, h = c['rect']
        l, b, r, t = c['border']
        e = dict(name=c['name'], kit=c['kit'], rect=[x, y, w, h], rect_unity=[x, ATLAS_H - y - h, w, h],
                 design_px=[w / float(S), h / float(S)], border_px=[l, b, r, t],
                 border_texels=[int(round(v * S)) for v in (l, b, r, t)], use=c['use'], mode=c['mode'],
                 colour='white (baked colours)' if c['mode'] == 'baked' else 'token ' + c.get('tint', '(caller)'),
                 sha256=hashlib.sha256(at[y:y + h, x:x + w].tobytes()).hexdigest())
        for k in ('fill_center', 'tile', 'period_px', 'piece_px', 'line_px', 'band_contrast', 'reserve'):
            if c.get(k) is not None:
                e[k] = c[k]
        if 'nacre_px2' in c:
            e['nacre_px2'] = round(c['nacre_px2'], 1)
        e['note'] = c['note']
        meta['cells'].append(e)
    tiles, rep = TL.build()
    man = dict(spec='SPEC-UI-THEME-308', generator='Tools/Art/theme308_tiles.py + Tools/Art/theme308_assets.py', files=[])
    for k in sorted(tiles):
        v = tiles[k]
        e = dict(name=k, bytes=len(v), sha256=hashlib.sha256(v).hexdigest())
        if k.endswith('.png'):
            r = rep[k.split('lat308_')[1][:-4]]
            e.update(size=r['size'], channels='LA (white + alpha; imported as Alpha8)', kind='lattice tile, Repeat')
        else:
            e.update(kind='data')
        man['files'].append(e)
    mj = dump(meta)
    man['files'].append(dict(name='theme308_atlas.png', bytes=len(png), sha256=hashlib.sha256(png).hexdigest(),
                             size=[ATLAS_W, ATLAS_H], channels='RGBA (sRGB, straight alpha)', kind='atlas, %d cells' % len(cells)))
    man['files'].append(dict(name='theme308_atlas.json', bytes=len(mj), sha256=hashlib.sha256(mj).hexdigest(), kind='data'))
    man['files'].sort(key=lambda e: e['name'])
    files = {'theme308_atlas.png': png, 'theme308_atlas.json': mj, 'theme308_manifest.json': dump(man)}
    return files, meta, tiles


def main():
    files, meta, tiles = build()
    if '--check' in sys.argv:
        files2, _, tiles2 = build()
        bad = [k for k in files if files[k] != files2[k]] + [k for k in tiles if tiles[k] != tiles2[k]]
        for k, v in list(files.items()) + list(tiles.items()):
            p = os.path.join(OUT, k.replace('/', os.sep))
            if not os.path.exists(p) or open(p, 'rb').read() != v:
                bad.append(k)
        n = len(files) + len(tiles)
        print('check:', 'OK (%d files, two builds and the disk agree) atlas %s' % (n, meta['sha256'][:16]) if not bad
              else 'DIFFERENT ' + ', '.join(sorted(set(bad))))
        return 1 if bad else 0
    os.makedirs(OUT, exist_ok=True)
    for k, v in files.items():
        with open(os.path.join(OUT, k), 'wb') as f:
            f.write(v)
    print('atlas %dx%d fill %.3f cells %d sha %s' % (ATLAS_W, ATLAS_H, meta['fill'], len(meta['cells']), meta['sha256'][:16]))
    for c in meta['cells']:
        print('  %-24s %-22s %3dx%-3d at %3d,%-3d %-6s %s' % (c['name'], c['kit'], c['rect'][2], c['rect'][3], c['rect'][0], c['rect'][1],
                                                           c['use'], c['sha256'][:10]))
    return 0


if __name__ == '__main__':
    sys.exit(main())
