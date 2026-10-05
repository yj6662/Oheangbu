# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (DECISIONS D308-15) - shared library: material tokens, colour maths and the construction rules of the
traditional patterns. Deterministic (numpy + PIL only, no clock, no unseeded RNG, no file reads).

Nothing here is traced or downloaded: every pattern is built from its construction rule on a module grid.
  lattice(name)          eight window-lattice (munsal) families as tileable coverage masks (bars = 1, openings = 0)
  nacre_rgb / pieces     the mother-of-pearl colour model (LDR, low chroma, OKLCH) and a shell-piece renderer
  najeon_*               najeon motifs made of cut shell pieces (fret, vine, chrysanthemum, plum, petal, cloud, hexagons)
  inlay_*                sanggam (inlay) line motifs for porcelain surfaces, as coverage masks
The colour model is the numpy twin of the optional shader UI/Nacre308 (Spec section 5.4).
"""
import math

import numpy as np
from PIL import Image, ImageDraw

SEED = 308


def hexc(s):
    s = s.lstrip('#')
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], float) / 255.0


def to_hex(c):
    v = np.clip(np.round(np.asarray(c, float) * 255), 0, 255).astype(int)
    return '#%02X%02X%02X' % (v[0], v[1], v[2])


# ---- tokens ---------------------------------------------------------------------------------------------------------
# #304 tokens (UiStyle304SO, unchanged; the theme never copies them into a new asset, it reads them)
TOK304 = dict(Ink='#141413', Veil='#0F0F0E', Ash='#5A574F', Mist='#A7A398', Paper='#E6E2D7', Sheet='#DFDBD0',
              Chip='#D6D1C4', Cinnabar='#B8392B', CinnabarLift='#D65A43', Off='#6E6B64', KeyLip='#B8B3A7')
# #308 theme tokens (TEST). Lacquer / porcelain / inlay / wood are plain sRGB values; nacre is defined in OKLCH below.
TOK308 = dict(
    Lacquer='#110F0D',         # heukchil: black lacquer ground (HUD hardware, rims, roundels)
    Porcelain='#DDE0DB',       # baekja: cool white ground (plaques, key caps); below Paper in every channel
    PorcelainShade='#BFC5C0',  # foot / lip line of a porcelain piece (a value step, not a gradient)
    InlayDark='#2A2623',       # heuk-sanggam: dark clay fill
    InlayIron='#7A3B2B',       # iron-red clay fill (material colour, never a signal colour)
    Wood='#4B4037',            # lattice bar (sal) and door frame (ulgeomi) on lacquer / veil / paper
)
SKY = hexc('#E4E0D6')          # DESIGN 2.3 worst case: the brightest sky
PINE = hexc('#2A2A26')         # DESIGN 2.3: the dark pine wood
T = {k: hexc(v) for k, v in {**TOK304, **TOK308}.items()}

# nacre (jagae): OKLCH, low chroma, hue kept on an arc (green - blue - pink); values are LDR by construction
NACRE = dict(
    L=0.788,                   # base lightness (OKLab L); with grain + piece offsets every channel stays <= 217
    C=0.034,                   # chroma of the iridescence (cap; the "second accent" guard)
    C_base=0.012, H_base=105.0,   # the token Nacre itself (a piece seen flat on)
    H_centre=245.0, H_span=190.0,  # the allowed hue arc: 150 deg (green) .. 340 deg (pink) through blue
    families=(170.0, 245.0, 320.0),   # hue centre of a piece: green / blue / pink family
    piece_span=56.0,           # hue swing inside one piece (deg, peak to peak)
    hue_period=30.0,           # px (design) for one swing along the piece axis
    grain_dL=0.012,            # +- lightness of the striations
    grain_period=2.6,          # px (design) across the piece axis
    piece_dL=0.012,            # +- lightness offset per piece
    joint_px=0.5,              # lacquer joint between cut pieces (kkeuneumjil): a hairline (1 texel at 2x). The first
                               # value, 0.9 px, read as a dashed line at 1080p (review 2026-10-04)
)

# kit rules added with the atlas (kept out of NACRE so that the Spec diagram's json does not change)
NACRE_FAMILY_SHARE = (0.42, 0.43, 0.15)   # share of green / blue / pink pieces in a seeded draw: pink is the rare flash
NACRE_FACE_CHROMA = 0.6                   # wide shell faces (petals, rosettes, HUD pictograms) keep 60 % of the line chroma


# ---- colour maths ---------------------------------------------------------------------------------------------------
def srgb_to_lin(c):
    c = np.asarray(c, float)
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(np.asarray(c, float), 0, 1)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def lum(c):
    l = srgb_to_lin(c)
    return .2126 * l[..., 0] + .7152 * l[..., 1] + .0722 * l[..., 2]


def contrast(a, b):
    la, lb = float(lum(np.asarray(a))), float(lum(np.asarray(b)))
    return (max(la, lb) + .05) / (min(la, lb) + .05)


def over(fg, bg, a):
    """sRGB-coded 'over' (what the #304 / #308 mocks and DESIGN 2.3 measure)."""
    return np.asarray(fg, float) * a + np.asarray(bg, float) * (1 - a)


def oklab_to_srgb(L, a, b):
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    return lin_to_srgb(np.stack([r, g, bb], axis=-1))


def srgb_to_oklab(c):
    """sRGB 0..1 (..., 3) -> OKLab (..., 3); the inverse of oklab_to_srgb."""
    l = srgb_to_lin(c)
    lm = 0.4122214708 * l[..., 0] + 0.5363325363 * l[..., 1] + 0.0514459929 * l[..., 2]
    mm = 0.2119034982 * l[..., 0] + 0.6806995451 * l[..., 1] + 0.1073969566 * l[..., 2]
    sm = 0.0883024619 * l[..., 0] + 0.2817188376 * l[..., 1] + 0.6299787005 * l[..., 2]
    l_, m_, s_ = np.cbrt(lm), np.cbrt(mm), np.cbrt(sm)
    return np.stack([0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
                     1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
                     0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_], axis=-1)


def oklch(L, C, h_deg):
    h = np.radians(h_deg)
    return oklab_to_srgb(L, C * np.cos(h), C * np.sin(h))


def nacre_token():
    return oklch(np.float64(NACRE['L']), NACRE['C_base'], NACRE['H_base'])


def nacre_rgb(t, across, family=1, phase=0.0, grain_phase=0.0, dL=0.0, shimmer=0.0):
    """The nacre colour of one cut piece. t / across = design px along / across the piece axis (arrays).
    shimmer = hue offset in degrees (0 = static; the optional UI/Nacre308 input). Returns sRGB 0..1."""
    n = NACRE
    hue = n['families'][family % 3] + shimmer + n['piece_span'] / 2 * np.sin(2 * np.pi * t / n['hue_period'] + phase)
    lo, hi = n['H_centre'] - n['H_span'] / 2, n['H_centre'] + n['H_span'] / 2
    hue = np.clip(hue, lo, hi)                                   # never leaves the arc, with or without shimmer
    L = n['L'] + dL + n['grain_dL'] * np.sin(2 * np.pi * across / n['grain_period'] + grain_phase)
    return oklch(L, n['C'], hue)


# colour-vision-deficiency simulation (Machado, Oliveira, Fernandes 2009, severity 1.0; applied on linear RGB)
CVD = dict(
    protan=np.array([[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]]),
    deutan=np.array([[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]]),
    tritan=np.array([[1.255528, -0.076749, -0.178779], [-0.078411, 0.930809, 0.147602], [0.004733, 0.691367, 0.303900]]),
)


def cvd(c, kind):
    return lin_to_srgb(np.clip(CVD[kind] @ srgb_to_lin(np.asarray(c, float)), 0, 1))


# ---- lattice (munsal) families ---------------------------------------------------------------------------------------
# Every family is a list of bar centre lines on a module grid (unit u) inside one period. u and bar are DESIGN px at
# 1080p; tiles are authored at 2x (TILE_SCALE) so every bar edge lands on a texel edge.
TILE_SCALE = 2


def _gwigap_segs():
    a, h = 1.0, 28.0 / 16.0            # side a = 1 u; the period height sqrt(3) a = 27.71 px is rounded to 28 px (1.0 % tall)
    return [(.5 * a, 0, 1.5 * a, 0), (1.5 * a, 0, 2 * a, h / 2), (2 * a, h / 2, 1.5 * a, h),
            (.5 * a, h, 0, h / 2), (0, h / 2, .5 * a, 0), (2 * a, h / 2, 3 * a, h / 2)]


def _sutdae_segs():
    s = []
    for cx, cy, vert in ((0, 0, True), (1, 1, True), (1, 0, False), (0, 1, False)):
        for i in (1, 3, 5):
            o = i / 6.0
            if vert:                    # three rods standing in the cell, reaching the neighbour's first rod
                s.append((cx + o, cy - 1 / 6.0, cx + o, cy + 1 + 1 / 6.0))
            else:
                s.append((cx - 1 / 6.0, cy + o, cx + 1 + 1 / 6.0, cy + o))
    return s


LATTICE = dict(
    jeongja=dict(ko='정자살', hanja='井字', u=16, bar=2.0, tile=(1, 1),
                 segs=[(0, 0, 1, 0), (0, 0, 0, 1)]),
    # tti: three bands (3 / 5 / 3 horizontal bars one module apart) with a 7 u run of bare vertical bars between them:
    # the long runs are what makes it a ttisal and not a grid with rows left out (the first tile had 3 u runs)
    tti=dict(ko='띠살', hanja='細箭', u=8, bar=1.5, tile=(1, 24),
             segs=[(0, 0, 0, 24)] + [(0, y, 1, y) for y in (1, 2, 3, 10, 11, 12, 13, 14, 21, 22, 23)]),
    # bar rule: modules of 16 px and more = 2.0 px (whole pixels at 1080p), 8 px modules and the two slanted families = 1.5 px
    yongja=dict(ko='용자살', hanja='用字', u=16, bar=2.0, tile=(2, 4),
                segs=[(0, 0, 0, 4), (0, 0, 2, 0)]),
    aja=dict(ko='아자살', hanja='亞字', u=8, bar=1.5, tile=(4, 4),
             segs=[(0, 0, 4, 0), (0, 0, 0, 4), (0, 1, 1, 1), (3, 1, 4, 1), (0, 3, 1, 3), (3, 3, 4, 3),
                   (1, 0, 1, 1), (1, 3, 1, 4), (3, 0, 3, 1), (3, 3, 3, 4)]),
    wanja=dict(ko='완자살', hanja='卍字', u=8, bar=1.5, tile=(3, 3),
               segs=[(0, 0, 3, 0), (0, 0, 0, 3), (0, 1, 2, 1), (2, 0, 2, 2), (1, 2, 3, 2), (1, 1, 1, 3)]),
    bit=dict(ko='빗살', hanja='斜', u=16, bar=1.5, tile=(1, 1),
             segs=[(0, 0, 1, 1), (0, 1, 1, 0)]),
    sutdae=dict(ko='숫대살', hanja='算木', u=24, bar=2.0, tile=(2, 2), segs=_sutdae_segs()),
    gwigap=dict(ko='귀갑살', hanja='龜甲', u=16, bar=1.5, tile=(3, 28.0 / 16.0), segs=_gwigap_segs()),
)
LATTICE_ORDER = ('tti', 'yongja', 'aja', 'wanja', 'jeongja', 'bit', 'sutdae', 'gwigap')
LATTICE_EDGE = ('jeongja', 'tti', 'yongja', 'aja', 'wanja', 'sutdae')   # axis-aligned: bars at the tile's left / bottom


def lattice(name, scale=TILE_SCALE, ss=4, edge=False, u=None, bar=None):
    """One period of a lattice family -> float coverage (H, W) in 0..1, tileable in both directions.
    scale = texels per design px. edge=True shifts the bars so the bar that sits on the period boundary lies fully
    at the left and at the bottom of the tile (a Tiled Image of n periods + one bar then ends on whole bars)."""
    f = LATTICE[name]
    u = float(u or f['u'])
    b = float(bar or f['bar'])
    Tw, Th = f['tile'][0] * u * scale, f['tile'][1] * u * scale
    Tw, Th = int(round(Tw)), int(round(Th))
    k = u * scale
    hb = b * scale / 2.0
    W, H = Tw * ss, Th * ss
    ys, xs = np.mgrid[0:H, 0:W]
    x = (xs + .5) / ss
    y = (ys + .5) / ss
    sx, sy = (hb, -hb) if edge else (0.0, 0.0)
    m = np.zeros((H, W), bool)
    for (x0, y0, x1, y1) in f['segs']:
        for ox in (-Tw, 0, Tw):
            for oy in (-Th, 0, Th):
                ax, ay, bx, by = x0 * k + ox + sx, y0 * k + oy + sy, x1 * k + ox + sx, y1 * k + oy + sy
                if abs(ay - by) < 1e-9:
                    m |= (np.abs(y - ay) <= hb) & (x >= min(ax, bx) - hb) & (x <= max(ax, bx) + hb)
                elif abs(ax - bx) < 1e-9:
                    m |= (np.abs(x - ax) <= hb) & (y >= min(ay, by) - hb) & (y <= max(ay, by) + hb)
                else:
                    dx, dy = bx - ax, by - ay
                    L2 = dx * dx + dy * dy
                    tt = np.clip(((x - ax) * dx + (y - ay) * dy) / L2, 0, 1)
                    m |= np.hypot(x - (ax + tt * dx), y - (ay + tt * dy)) <= hb
    return m.reshape(Th, ss, Tw, ss).mean(axis=(1, 3))


def lattice_tile(name, scale=TILE_SCALE):
    """The shipped tile of a family (edge-anchored bars for the axis-aligned families: every bar edge on a texel edge)."""
    return lattice(name, scale=scale, edge=name in LATTICE_EDGE)


def seam_error(tile):
    """Tileability: 0 when the tile's first and last rows / columns continue each other (max gradient jump across the wrap
    is no larger than inside the tile)."""
    t = np.asarray(tile, float)
    inner = max(np.abs(np.diff(t, axis=0)).max(), np.abs(np.diff(t, axis=1)).max())
    wrap = max(np.abs(t[0] - t[-1]).max(), np.abs(t[:, 0] - t[:, -1]).max())
    return float(max(0.0, wrap - inner))


def open_ratio(tile):
    """Share of the period that is opening (paper), 0..1."""
    return float(1.0 - np.asarray(tile, float).mean())


# ---- shell pieces (najeon) --------------------------------------------------------------------------------------------
def _circle(cx, cy, r, n=28):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def _quad(ax, ay, bx, by, w):
    dx, dy = bx - ax, by - ay
    L = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / L * w / 2, dx / L * w / 2
    return [(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)]


def strips(poly, width, piece=11.0, gap=None, fam0=0, closed=False):
    """kkeuneumjil: a polyline laid with straight cut strips (butt joints, 'gap' px of lacquer between pieces)."""
    gap = NACRE['joint_px'] if gap is None else gap
    out = []
    pts = list(poly) + ([poly[0]] if closed else [])
    k = fam0
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        L = math.hypot(bx - ax, by - ay)
        if L < 1e-6:
            continue
        n = max(1, int(round(L / piece)))
        ux, uy = (bx - ax) / L, (by - ay) / L
        for i in range(n):
            s0 = i * L / n + (gap / 2 if i > 0 else -width / 2)
            s1 = (i + 1) * L / n - (gap / 2 if i < n - 1 else -width / 2)
            out.append(dict(poly=_quad(ax + ux * s0, ay + uy * s0, ax + ux * s1, ay + uy * s1, width),
                            axis=math.degrees(math.atan2(uy, ux)), fam=k % 3))
            k += 1
    return out


def arc_strips(cx, cy, r, a0, a1, width, piece=11.0, gap=None, fam0=0):
    """Curved cut strips along a circular arc (degrees, clockwise on screen)."""
    gap = NACRE['joint_px'] if gap is None else gap
    L = abs(math.radians(a1 - a0)) * r
    n = max(1, int(round(L / piece)))
    out = []
    for i in range(n):
        g = math.degrees(gap / 2 / r)
        b0 = a0 + (a1 - a0) * i / n + (g if a1 > a0 else -g)
        b1 = a0 + (a1 - a0) * (i + 1) / n - (g if a1 > a0 else -g)
        outer, inner = [], []
        for j in range(7):
            a = math.radians(b0 + (b1 - b0) * j / 6)
            outer.append((cx + (r + width / 2) * math.cos(a), cy + (r + width / 2) * math.sin(a)))
            inner.append((cx + (r - width / 2) * math.cos(a), cy + (r - width / 2) * math.sin(a)))
        mid = (b0 + b1) / 2 + 90
        out.append(dict(poly=outer + inner[::-1], axis=mid, fam=(fam0 + i) % 3))
    return out


def _petal(cx, cy, ang_deg, r1, r2, hw, n=10, sharp=0.75):
    """A petal / leaf piece from radius r1 to r2 along ang, half width hw at the belly."""
    a = math.radians(ang_deg)
    ux, uy = math.cos(a), math.sin(a)
    left, right = [], []
    for i in range(n + 1):
        u = i / n
        w = hw * math.sin(math.pi * u) ** sharp
        rr = r1 + (r2 - r1) * u
        left.append((cx + ux * rr - uy * w, cy + uy * rr + ux * w))
        right.append((cx + ux * rr + uy * w, cy + uy * rr - ux * w))
    return left + right[::-1]


def najeon_fret(n, u=4.0, width=1.5):
    """noemun fret border: a base line with one hooked key per period (5 u), height 3 u. Returns (w, h, pieces)."""
    P, pcs = 5 * u, []
    pcs += strips([(0, 3 * u), (n * P, 3 * u)], width, piece=12)
    for i in range(n):
        x0 = i * P
        key = [(x0 + u, 3 * u), (x0 + u, 0), (x0 + 4 * u, 0), (x0 + 4 * u, 2 * u), (x0 + 2.5 * u, 2 * u),
               (x0 + 2.5 * u, u)]
        pcs += strips(key, width, piece=14, fam0=i)
    return n * P, 3 * u + width, [dict(p, poly=[(x, y + width / 2) for x, y in p['poly']]) for p in pcs]


def najeon_vine(n, P=44.0, A=6.0, width=1.6):
    """dangcho scrolling vine: a sine stem of cut strips, one leaf at every crest and trough, a bud on every crossing."""
    h = 2 * A + 16
    cy = h / 2
    stem = [(x, cy + A * math.sin(2 * math.pi * x / P)) for x in np.arange(0, n * P + .01, P / 16)]
    pcs = strips(stem, width, piece=7.5)
    for i in range(2 * n):
        x = (i + .5) * P / 2
        up = (i % 2 == 0)
        y = cy + (A if up else -A)
        pcs.append(dict(poly=_petal(x, y, 62 if up else -62, 1.2, 9.5, 2.6), axis=62 if up else -62, fam=i % 3))
        pcs.append(dict(poly=_petal(x, y, 128 if up else -128, 1.2, 6.5, 2.0), axis=128 if up else -128,
                        fam=(i + 1) % 3))
    for i in range(n * 2 + 1):
        x = i * P / 2
        if 2 < x < n * P - 2:
            pcs.append(dict(poly=_circle(x, cy + (5.2 if i % 2 else -5.2), 1.7, 14), axis=0, fam=(i + 2) % 3))
    return n * P, h, pcs


def najeon_chrys(r=22.0, n=14, rings=1):
    """gukhwa rosette: n petal pieces round a centre disc (corner / stud motif). Canvas 2r+2."""
    c = r + 1
    pcs = [dict(poly=_circle(c, c, r * .2, 20), axis=0, fam=1)]
    hw = math.pi * (r * .62) / n * .86
    for i in range(n):
        a = 360.0 * i / n - 90
        pcs.append(dict(poly=_petal(c, c, a, r * .27, r, hw, sharp=.62), axis=a, fam=i % 3))
    if rings > 1:
        for i in range(n):
            a = 360.0 * (i + .5) / n - 90
            pcs.append(dict(poly=_petal(c, c, a, r * .27, r * .6, hw * .55, sharp=.62), axis=a, fam=(i + 1) % 3))
    return 2 * c, 2 * c, pcs


def najeon_plum(r=10.0):
    """maehwa: five round petals and a centre (tier pip / small accent). Canvas 2r+2."""
    c = r + 1
    pcs = []
    for i in range(5):
        a = math.radians(72 * i - 90)
        pcs.append(dict(poly=_circle(c + .53 * r * math.cos(a), c + .53 * r * math.sin(a), .43 * r, 22),
                        axis=72 * i - 90, fam=i % 3))
    pcs.append(dict(poly=_circle(c, c, .17 * r, 14), axis=0, fam=1))
    return 2 * c, 2 * c, pcs


def najeon_petal(w=10.0, h=14.0):
    """One plum petal (tier pip): a teardrop pointing up."""
    return w + 2, h + 2, [dict(poly=_petal(w / 2 + 1, h + 1, -90, 0, h, w / 2, sharp=.9), axis=-90, fam=1)]


def najeon_cloud(s=1.0):
    """gureum (yeongji cloud head): three lobes, a curled waist and a tapering tail. Canvas 60 x 34 at s = 1."""
    P = lambda x, y: (x * s, y * s)
    pcs = [
        dict(poly=_circle(*P(21, 12), 9.5 * s), axis=12, fam=1),
        dict(poly=_circle(*P(9.5, 18.5), 7.2 * s), axis=12, fam=0),
        dict(poly=_circle(*P(33, 17), 7.6 * s), axis=12, fam=2),
        dict(poly=_circle(*P(21.5, 22.5), 5.0 * s), axis=12, fam=1),
    ]
    tail = [(36, 25.5, 16, 8.5, 3.6), (44, 28.2, 6, 8.0, 2.8), (51, 28.8, -6, 7.0, 2.0)]
    for i, (x, y, a, ln, hw) in enumerate(tail):
        pcs.append(dict(poly=_petal(x * s - ln * s / 2 * math.cos(math.radians(a)),
                                    y * s - ln * s / 2 * math.sin(math.radians(a)), a, 0, ln * s, hw * s, sharp=.8),
                        axis=a, fam=i % 3))
    return 60 * s, 34 * s, pcs


def najeon_gwigap(cols=3, rows=2, a=7.0, width=1.3):
    """gwigap (turtle shell) hexagon net of cut strips. Returns (w, h, pieces)."""
    hh = math.sqrt(3) * a
    pcs, seen = [], set()
    for cx in range(cols):
        for cy in range(rows):
            ox = cx * 1.5 * a + a + 1
            oy = cy * hh + (hh / 2 if cx % 2 else 0) + hh / 2 + 1
            v = [(ox + a * math.cos(math.radians(60 * i)), oy + a * math.sin(math.radians(60 * i))) for i in range(6)]
            for i in range(6):
                p, q = v[i], v[(i + 1) % 6]
                key = tuple(sorted(((round(p[0], 1), round(p[1], 1)), (round(q[0], 1), round(q[1], 1)))))
                if key in seen:
                    continue
                seen.add(key)
                pcs += strips([p, q], width, piece=99, fam0=len(seen))
    w = cols * 1.5 * a + .5 * a + 2
    h = rows * hh + hh / 2 + 2
    return w, h, pcs


def najeon_corner(size=14.0, step=5.0, inset=3.5, width=1.5):
    """Corner.Fret: the aja (stepped) turn of a cut-shell line at a frame corner. Box size x size, corner at (0, 0)."""
    m = inset + step * .7
    pts = [(size, inset), (m, inset), (m, m), (inset, m), (inset, size)]
    return size, size, strips(pts, width, piece=9)


def najeon_north(w=7.0, h=12.0):
    """Piece.North: one pointed shell piece (a plum petal standing on the frame's top side)."""
    return w + 2, h + 2, [dict(poly=_petal(w / 2 + 1, h + 1, -90, 0, h, w / 2, sharp=.8), axis=-90, fam=2)]


def stepped_corner_path(inset, step, far):
    """A frame line that turns in by one aja step at the top-left corner: from (inset, far) up, round the corner, to
    (far, inset). Used by frame lines that carry their own corners."""
    i, s = inset, step
    return [(i, far), (i, i + 2 * s), (i + s, i + 2 * s), (i + s, i + s), (i + 2 * s, i + s), (i + 2 * s, i), (far, i)]


def render_pieces(w, h, pieces, s=1.0, ss=4, seed=SEED, shimmer=0.0, alpha=1.0):
    """Shell pieces -> straight-alpha RGBA float array (ceil(h s), ceil(w s), 4). Every piece gets its own hue phase,
    grain phase and lightness offset from a seeded generator, so the same call always returns the same bytes."""
    ow, oh = int(math.ceil(w * s)), int(math.ceil(h * s))
    W, H = ow * ss, oh * ss
    k = s * ss
    rgb = np.zeros((H, W, 3))
    acc = np.zeros((H, W))
    for i, p in enumerate(pieces):
        xs_ = [q[0] * k for q in p['poly']]
        ys_ = [q[1] * k for q in p['poly']]
        x0, x1 = max(0, int(min(xs_)) - 1), min(W, int(max(xs_)) + 2)
        y0, y1 = max(0, int(min(ys_)) - 1), min(H, int(max(ys_)) + 2)
        if x1 <= x0 or y1 <= y0:
            continue
        im = Image.new('L', (x1 - x0, y1 - y0), 0)
        ImageDraw.Draw(im).polygon([(a - x0, b - y0) for a, b in zip(xs_, ys_)], fill=255)
        m = np.asarray(im) > 127
        rng = np.random.default_rng([seed, p.get('rid', i)])     # 'rid' = a stable piece identity (kit cells); default = index
        ph, gph, dl = rng.random() * 2 * np.pi, rng.random() * 2 * np.pi, (rng.random() - .5) * 2 * NACRE['piece_dL']
        yy, xx = np.mgrid[y0:y1, x0:x1]
        X, Y = (xx + .5) / k, (yy + .5) / k
        ax = math.radians(p.get('axis', 0.0))
        cx_, cy_ = np.mean([q[0] for q in p['poly']]), np.mean([q[1] for q in p['poly']])
        t = (X - cx_) * math.cos(ax) + (Y - cy_) * math.sin(ax)
        across = -(X - cx_) * math.sin(ax) + (Y - cy_) * math.cos(ax)
        col = nacre_rgb(t, across, p.get('fam', 1), ph, gph, dl, shimmer)
        sub = rgb[y0:y1, x0:x1]
        sub[m] = col[m]
        acc[y0:y1, x0:x1][m] = 1.0
    pre = np.dstack([rgb * acc[..., None], acc]).reshape(oh, ss, ow, ss, 4).mean(axis=(1, 3))
    pre[..., :3] = pre[..., :3] / np.maximum(pre[..., 3:4], 1e-6)
    pre[..., 3] *= alpha
    return pre


# ---- HUD vessel collar (the lacquer neck ring of the egg bottle; Spec section 3 / 5.2) ----------------------------------
# Not a kit texture: SPEC-HUD-LIQUID-308 bakes it into the reserved G channel of its vessel cell (0 none, .5 lacquer, 1 nacre)
# and UI/InkVessel308 colours it. This is the one definition of its shape; the HUD generator and the theme sheet both call it.
COLLAR = dict(
    bottom=-0.5,     # px: the collar ends this far past the neck's lower end (lip_h + neck_h), still above the full line
    line_y=0.62,     # the cut-shell line sits at this share of the neck height below the lip
    line_w=2.0,      # px
    piece=6.5,       # px: target length of one cut piece
    inset=2.5,       # px: the line stops this far inside the neck's half width
)


# The HUD's adopted form (SPEC-HUD-LIQUID-308 section 1, kit review weaknesses 2 and 3): a lacquer BAND round the neck.
# The lip and the top third of the neck stay glass (an open mouth, not a stopper; the pouring thread shows above the band),
# and the cut-shell line runs along the band's lower edge, so a pearl line always lies between the lacquer and the ink below.
COLLAR_BAND = dict(COLLAR,
    top=1.0 / 3.0,   # the lacquer starts this share of the neck height below the lip (absent = it fills the lip too)
    line_up=2.0,     # px from the collar's lower end up to the line's centre (absent = line_y is used): 1 px of lacquer below
)


def collar_fields(d, x, y, cx, lip_h, neck_h, neck_w, px, spec=None):
    """Coverage fields of the collar on any sample grid of a vessel.
    d = signed distance to the glass silhouette (design px, < 0 inside); x, y = design px in the vessel rect (y down);
    cx = the vessel's centre x; lip_h / neck_h / neck_w = the bottle's lip height, neck height and neck width (design px);
    px = design px per sample (the anti-aliasing width). spec = the collar form (None = COLLAR, the first form; the HUD
    passes COLLAR_BAND). Returns (lacquer, nacre), both 0..1; nacre lies inside lacquer and its butt joints are lacquer.
    G channel = 0.5 * lacquer + 0.5 * nacre."""
    c = COLLAR if spec is None else spec
    d, x, y = np.asarray(d, float), np.asarray(x, float), np.asarray(y, float)
    lac = np.clip(.5 - d / px, 0, 1) * np.clip((lip_h + neck_h + c['bottom'] - y) / px + .5, 0, 1)
    if 'top' in c:                                               # the lip (and the neck above the band) is left as glass
        lac = lac * np.clip((y - (lip_h + neck_h * c['top'])) / px + .5, 0, 1)
    ly = lip_h + neck_h * c['line_y']
    if 'line_up' in c:
        ly = lip_h + neck_h + c['bottom'] - c['line_up']
    hw = neck_w / 2.0 - c['inset']
    n = max(1, int(round(2 * hw / c['piece'])))
    t = (x - (cx - hw)) / (2 * hw) * n                          # piece coordinate along the line
    inside = np.clip((hw - np.abs(x - cx)) / px + .5, 0, 1) * np.clip((c['line_w'] / 2 - np.abs(y - ly)) / px + .5, 0, 1)
    to_joint = np.abs(t - np.round(t)) * (2 * hw / n)            # px to the nearest joint centre
    interior = (np.round(t) > 0) & (np.round(t) < n)             # the two ends of the line are not joints
    joint = np.where(interior, np.clip((NACRE['joint_px'] / 2 - to_joint) / px + .5, 0, 1), 0.0)
    return lac, inside * (1 - joint) * lac


# ---- inlay (sanggam) line motifs: coverage masks, tinted InlayDark / InlayIron by the caller ---------------------------
def _mask(w, h, s, ss, draw_fn):
    ow, oh = int(math.ceil(w * s)), int(math.ceil(h * s))
    im = Image.new('L', (ow * ss, oh * ss), 0)
    draw_fn(ImageDraw.Draw(im), s * ss)
    return np.asarray(im, float).reshape(oh, ss, ow, ss).mean(axis=(1, 3)) / 255.0


def inlay_frame(w, h, s=1.0, ss=4, inset=7.0, w1=2.0, gap=2.5, w2=1.0):
    """Double line border (thick outside, thin inside): the plaque / picture frame of the porcelain surfaces."""
    def fn(d, k):
        for off, lw in ((inset, w1), (inset + w1 + gap, w2)):
            a, b = off * k, (off + lw) * k
            W, H = w * k, h * k
            d.rectangle([a, a, W - a - 1, H - a - 1], fill=255)
            d.rectangle([b, b, W - b - 1, H - b - 1], fill=0)
    return _mask(w, h, s, ss, fn)


def inlay_lotus(n, pw=16.0, ph=13.0, s=1.0, ss=4, lw=1.3):
    """yeonpan (lotus petal) band: a row of pointed arches with a centre vein, standing on a base line."""
    def fn(d, k):
        d.rectangle([0, (ph + 1.5) * k, n * pw * k, (ph + 1.5 + lw) * k], fill=255)
        for i in range(n):
            x0 = i * pw
            for side in (-1, 1):
                pts = []
                for j in range(13):
                    u = j / 12.0
                    x = x0 + pw / 2 + side * (pw / 2 - .8) * math.cos(u * math.pi / 2) ** .8
                    y = 1 + (ph) * (1 - math.sin(u * math.pi / 2) ** 1.25)
                    pts.append((x * k, y * k))
                d.line(pts, fill=255, width=max(1, int(round(lw * k))), joint='curve')
            d.line([((x0 + pw / 2) * k, (ph * .42) * k), ((x0 + pw / 2) * k, (ph + 1) * k)], fill=255,
                   width=max(1, int(round(lw * .8 * k))))
    return _mask(n * pw, ph + 4, s, ss, fn)


def inlay_stamps(n, pitch=14.0, r=3.1, s=1.0, ss=4):
    """inhwa (stamped chrysanthemum) row: a centre dot and eight petal dots per stamp."""
    def fn(d, k):
        for i in range(n):
            cx, cy = (i + .5) * pitch, r + 2.2
            d.ellipse([(cx - .95) * k, (cy - .95) * k, (cx + .95) * k, (cy + .95) * k], fill=255)
            for j in range(8):
                a = math.radians(45 * j)
                px, py = cx + r * math.cos(a), cy + r * math.sin(a)
                d.ellipse([(px - 1.05) * k, (py - 1.05) * k, (px + 1.05) * k, (py + 1.05) * k], fill=255)
    return _mask(n * pitch, 2 * r + 4.4, s, ss, fn)


def inlay_vine(n, P=36.0, A=4.5, s=1.0, ss=4, lw=1.3):
    """dangcho line: a sine stem with one short leaf stroke at every crest and trough."""
    h = 2 * A + 12

    def fn(d, k):
        cy = h / 2
        pts = [(x * k, (cy + A * math.sin(2 * math.pi * x / P)) * k) for x in np.arange(0, n * P + .01, 1.0)]
        d.line(pts, fill=255, width=max(1, int(round(lw * k))), joint='curve')
        for i in range(2 * n):
            x = (i + .5) * P / 2
            up = (i % 2 == 0)
            y = cy + (A if up else -A)
            a = math.radians(58 if up else -58)
            leaf = [((x + t * 7.5 * math.cos(a) - math.sin(a) * 1.6 * math.sin(math.pi * t)) * k,
                     (y + t * 7.5 * math.sin(a) + math.cos(a) * 1.6 * math.sin(math.pi * t)) * k)
                    for t in np.linspace(0, 1, 9)]
            d.line(leaf, fill=255, width=max(1, int(round(lw * k))), joint='curve')
    return _mask(n * P, h, s, ss, fn)
