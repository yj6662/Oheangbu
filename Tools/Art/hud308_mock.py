# -*- coding: utf-8 -*-
"""hud308_mock.py - concept MOCK SHEET for SPEC-HUD-LIQUID-308 (DECISIONS D308-11).

Offline and deterministic (numpy + PIL). This is a concept mock for the user, NOT the shader twin and NOT a game capture:
it composites in sRGB (the #304 mockup convention) and takes the Spec's TEST numbers as they are written.
It never touches Oheangbu/Assets, the editor queue or Play mode.

usage (repo root):
    python Tools/resource_guard.py --wait
    python Tools/Art/hud308_mock.py            # everything: single frames + the sheet + the report
    python Tools/Art/hud308_mock.py --probe    # quick look pieces only (Art/UI308/HUD/mock_frames/_probe)
out:
    Art/UI308/HUD/hud308_mock_sheet.png        (2400 px wide, Korean captions)
    Art/UI308/HUD/mock_frames/*.png            (single frames)
    Art/UI308/HUD/hud308_mock_report.json      (numbers measured on the mock)
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Art', 'UI308', 'HUD')
FRAMES = os.path.join(OUT, 'mock_frames')
REF_PNG = os.path.join(ROOT, 'Docs', 'Refs', 'HUD308', 'hud_orb_ref_20261004.png')
STILL_PNG = os.path.join(ROOT, 'Art', 'Playtest308', 'Vehicle', 'ride_seat.png')
FONT_DIR = 'C:/Windows/Fonts/'


def hexc(s):
    return np.array([int(s[i:i + 2], 16) for i in (1, 3, 5)]) / 255.0


INK = hexc('#141413')
PAPER = hexc('#E6E2D7')
CINNABAR = hexc('#B8392B')
SKY = hexc('#E4E0D6')      # DESIGN 2.3 worst case: the brightest sky
PINE = hexc('#2A2A26')     # DESIGN 2.3: the dark pine wood
SHEET_BG = hexc('#C9C5BA')
ASH = hexc('#5A574F')

# ---------------------------------------------------------------------------------------------------------------------
# Spec TEST numbers (SPEC-HUD-LIQUID-308, mockup px on 1920x1080, top-left origin). Copied, not re-decided.
# ---------------------------------------------------------------------------------------------------------------------
RECT = dict(hp=(64, 818, 114, 150), ink=(158, 880, 100, 132))
ARC = dict(cx=167, cy=925, r=132, deg=(64, 38, 12), size=44)     # dodge / jump / vehicle
SHAPE = dict(body_aspect=1.22, neck_h=.075, lip_h=.035, neck_w=.34, lip_w=.46, n=2.3, egg=.12)
GLASS = dict(outline=2.6, rim=2.2, rim_a=.72, wash=.14, hl_a=.55, wall=4.0, full_line=.07)
LIQ = dict(pool_px=5.0, pool_dark=.22, core_light=.06, men_px=1.1, men_a=.78, climb=2.0, alpha=.96)
PER = dict(
    hp=dict(hz=1.8, zeta=.28, tilt_g=.35, tilt_turn=.12, max_tilt=.22, wave_max=3.0, wave_tau=.70, wave_speed=7.0,
            drain_tau=.07, fill_tau=.22, wet_a=.34, wet_hold=.45, wet_dry=.35, kick_tilt=2.4, kick_wave=2.0),
    ink=dict(hz=1.1, zeta=.55, tilt_g=.25, tilt_turn=.08, max_tilt=.16, wave_max=1.8, wave_tau=.45, wave_speed=4.0,
             drain_tau=.11, fill_tau=.30, wet_a=.30, wet_hold=.6, wet_dry=3.4, kick_tilt=1.5, kick_wave=1.2),
)
LOW = dict(danger_hp=.35, hp_darken=.22, tide_a=.22, agit_px=.8, spell_cost=.15, dry_cover=.55)
MARK = dict(dry_a=.30, rim_a=.92, rim_px=2.2)
IMPACT = dict(gain=.85, shadow_px=9.0, shadow_a=.5, liquid_shift=.10, far_rim_ink=.6)
DRAIN_MIN_SPEED = .6
FRESH_LIGHTEN = .18
RECEIVED_S = .45
POUR_MIN_DELTA = .01

# ---------------------------------------------------------------------------------------------------------------------
# Mock-only look choices (NOT in the Spec; each one is listed in the report as a deviation). spec_only=True drops them.
# ---------------------------------------------------------------------------------------------------------------------
LOOK = dict(
    lens=True, lens_px=dict(hp=4.2, ink=3.6), lens_mix=dict(hp=.30, ink=.27), lens_back_a=.34,   # far half of the surface
    wall_a=.34,                 # paper value in the 1.4 px between the ink line and the liquid = glass thickness
    line_mod=.28,               # brush pressure: the ink line swells / thins round the body
    marble=.05,                 # faint thick / thin ink clouds inside the body
    depth=.12,                  # the body gets a little darker toward the bottom (thick ink settles)
    hl2_a=.26,                  # a second, short paper stroke low on the right
    wave_gain=dict(hp=9.0, ink=4.5),   # wave px gained per unit of tilt speed (the Spec leaves this gain open)
    impact_wash=.20,            # empty glass catches the light on the lit side (added paper wash)
)

PAD = 5        # px of margin round a vessel rect so the paper rim is not clipped in the mock
MPAD = 4       # px of margin round a 44 px mark rect: its paper rim (2.2 px + soft edge) spills past the rect, uncut
               # (review 2026-10-04: the rim was cut straight at the 44 px tile; the atlas cell has the same 4 px margin)
IMPACT_PT = (1010, 560)   # where the spell lands in this mock (mock px): up and to the right of the cluster


# ---------------------------------------------------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------------------------------------------------
def mix(a, b, t):
    t = np.asarray(t, float)
    if t.ndim == 2:
        t = t[..., None]
    return a * (1 - t) + b * t


class Layer:
    """Straight-alpha 'over' accumulator on the supersampled grid."""

    def __init__(self, shape):
        self.rgb = np.zeros(shape + (3,))
        self.a = np.zeros(shape)

    def put(self, col, a):
        a = np.clip(a, 0, 1)
        col = np.broadcast_to(col, self.rgb.shape)
        out = a + self.a * (1 - a)
        self.rgb = (col * a[..., None] + self.rgb * (self.a * (1 - a))[..., None]) / np.maximum(out, 1e-6)[..., None]
        self.a = out

    def down(self, ss):
        rgba = np.dstack([self.rgb * self.a[..., None], self.a])       # premultiplied for a correct box filter
        H, W = rgba.shape[:2]
        r = rgba.reshape(H // ss, ss, W // ss, ss, 4).mean(axis=(1, 3))
        r[..., :3] = r[..., :3] / np.maximum(r[..., 3:4], 1e-6)
        return r


def smin(a, b, k):
    h = np.clip(.5 + .5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)


def grid_noise(shape, cell_x, cell_y, seed):
    """Smooth deterministic noise in [0, 1]: a random grid stretched to the shape (anisotropic cells, in pixels)."""
    rng = np.random.default_rng(seed)
    H, W = shape
    gw, gh = max(2, int(W / max(1.0, cell_x)) + 2), max(2, int(H / max(1.0, cell_y)) + 2)
    g = (rng.random((gh, gw)) * 255).astype(np.uint8)
    im = Image.fromarray(g).resize((W, H), Image.BICUBIC)
    return np.asarray(im, float) / 255


def hash_bins(n, seed):
    return np.random.default_rng(seed).random(n)


# ---------------------------------------------------------------------------------------------------------------------
# vessel geometry: the Spec's "egg bottle" (super-ellipse body n = 2.3, 12 % narrower shoulders, short neck, flat lip)
# ---------------------------------------------------------------------------------------------------------------------
_GEO = {}


class Geo:
    pass


def vessel_geo(w, h, s, ss, neck=True, body_aspect=SHAPE['body_aspect']):
    key = (w, h, s, ss, neck, body_aspect)
    if key in _GEO:
        return _GEO[key]
    k = s * ss
    ow, oh = int(math.ceil((w + 2 * PAD) * s)), int(math.ceil((h + 2 * PAD) * s))
    W, H = ow * ss, oh * ss
    ys, xs = np.mgrid[0:H, 0:W]
    x = (xs + .5) / k - PAD
    y = (ys + .5) / k - PAD
    cx = w / 2
    lip_h = SHAPE['lip_h'] * h if neck else 0.0
    neck_h = SHAPE['neck_h'] * h if neck else 0.0
    top = (lip_h + neck_h - .02 * h) if neck else 1.5
    bottom = h - 1.5
    b = (bottom - top) / 2
    cy = top + b
    a = w / 2 - 2
    if body_aspect:
        a = min(a, b / body_aspect)
    dy = (y - cy) / b
    a_eff = a * (1 - SHAPE['egg'] * np.clip(-dy, 0, 1) ** 2)
    f = (np.abs(x - cx) / a_eff) ** SHAPE['n'] + np.abs(dy) ** SHAPE['n'] - 1
    gy, gx = np.gradient(f, 1.0 / k)
    d = f / np.maximum(np.hypot(gx, gy), 1e-4)
    if neck:
        def box(cx_, cy_, hw, hh):
            qx = np.abs(x - cx_) - hw
            qy = np.abs(y - cy_) - hh
            return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0)
        d_neck = box(cx, lip_h + neck_h / 2 + 2, SHAPE['neck_w'] * w / 2, neck_h / 2 + 4)
        d_lip = box(cx, lip_h / 2 + .75, SHAPE['lip_w'] * w / 2, lip_h / 2 + .25)
        d = smin(d, d_neck, 5.0)
        d = np.minimum(d, d_lip)
    g = Geo()
    g.w, g.h, g.s, g.ss, g.k = w, h, s, ss, k
    g.x, g.y, g.d = x, y, d
    g.cx, g.cy, g.a, g.b = cx, cy, a, b
    g.top, g.bottom = top, bottom
    g.full_y = top + GLASS['full_line'] * (bottom - top)      # 100 % reaches the shoulder, never the neck
    g.floor_y = bottom - GLASS['wall']
    g.lip_h, g.neck_h = lip_h, neck_h
    gy2, gx2 = np.gradient(d, 1.0 / k)
    nrm = np.maximum(np.hypot(gx2, gy2), 1e-4)
    g.nx, g.ny = gx2 / nrm, gy2 / nrm
    g.ang = np.arctan2(y - cy, x - cx)
    cav = (d + GLASS['wall']) < 0
    g.cav_half = cav.sum(axis=1) / k / 2.0                     # half width of the cavity per row (for the surface lens)
    g.row_y = y[:, 0]
    ins = d < 0
    cols = np.nonzero(ins.any(axis=0))[0]
    rows = np.nonzero(ins.any(axis=1))[0]
    g.sil_w = (cols[-1] - cols[0] + 1) / k
    g.sil_h = (rows[-1] - rows[0] + 1) / k
    g.sil_top = float(y[rows[0], 0])
    g.sil_bottom = float(y[rows[-1], 0])
    g.sil_left = float(x[0, cols[0]])
    g.sil_right = float(x[0, cols[-1]])
    body_rows = rows[y[rows, 0] > top + 6]
    g.body_w = float(ins[body_rows].sum(axis=1).max() / k)
    g.body_h = float(bottom - top)
    _GEO[key] = g
    return g


class VState:
    """What one vessel shows in one frame."""

    def __init__(self, level, **kw):
        self.level = level
        self.tilt = 0.0          # surface slope (height / width); + = lower on the right, piled up on the left
        self.wave = 0.0          # px
        self.phase = 0.0
        self.stain_top = None    # level where the wet film starts (the level before the loss)
        self.stain_a = 0.0
        self.stain_dry = 0.0     # 0..1, the film dries from the top down
        self.fresh_from = None   # level before the gain; the new share is paler for a moment
        self.fresh_a = 0.0
        self.thread = 0.0        # pouring thread 0..1
        self.low = 0.0           # 0..1 low HP state
        self.dry_ink = False     # ink below one spell: the pool breaks into a dry-brush smear
        self.tides = ()          # dried tide lines (levels)
        for k_, v in kw.items():
            if not hasattr(self, k_):
                raise KeyError(k_)
            setattr(self, k_, v)


def hp_state(level, **kw):
    """HP vessel at rest at this level, with the Spec's low state rules applied."""
    low = max(0.0, 1 - level / LOW['danger_hp']) if level < LOW['danger_hp'] else 0.0
    st = VState(level, low=low, **kw)
    if low > 0:
        st.tides = tuple(v for v in (.34, .27, .19) if v > level + .035)[:3]
        st.wave = max(st.wave, LOW['agit_px'])
    return st


def ink_state(level, **kw):
    return VState(level, dry_ink=(0 < level < LOW['spell_cost']), **kw)


def render_vessel(kind, st, s=1.0, ss=None, impact=None, spec_only=False, neck=True, w=None, h=None,
                  body_aspect=SHAPE['body_aspect']):
    """One vessel -> straight-alpha RGBA float array, size ceil((w + 2 PAD) s) x ceil((h + 2 PAD) s).
    impact = ((lx, ly), gain): unit vector from the vessel centre to the impact point (screen px, y down)."""
    rw, rh = RECT[kind][2:]
    w = w or rw
    h = h or rh
    ss = ss or (4 if s <= 1.2 else 3)
    g = vessel_geo(w, h, s, ss, neck=neck, body_aspect=body_aspect)
    look = dict(LOOK)
    if spec_only:
        look.update(lens=False, wall_a=0.0, line_mod=0.0, marble=0.0, depth=0.0, hl2_a=0.0, impact_wash=0.0)
    d, x, y, k = g.d, g.x, g.y, g.k
    seed = 3 if kind == 'hp' else 5

    def cov(v):           # anti-aliased coverage of v > 0 (v in px)
        return np.clip(.5 + v * s, 0, 1)

    def band(v, lo, hi):
        return cov(v - lo) * cov(hi - v)

    wall = GLASS['wall']
    d_in = d + wall
    inside = cov(-d)
    cavity = cov(-d_in)
    L = Layer(d.shape)

    lit = dark = None
    along = None
    if impact is not None:
        (lx, ly), gain = impact
        ndl = g.nx * lx + g.ny * ly
        lit = np.clip(ndl, 0, 1) * gain
        dark = np.clip(-ndl, 0, 1) * gain
        along = ((x - g.cx) * lx + (y - g.cy) * ly) / max(g.a, g.b)

    # 1 paper rim outside the glass (the far side sinks toward ink on an impact frame)
    rim_col = PAPER
    if dark is not None:
        rim_col = mix(PAPER[None, None, :], INK[None, None, :], IMPACT['far_rim_ink'] * dark)
    L.put(rim_col, band(d, 0.0, GLASS['rim']) * GLASS['rim_a'])
    # 2 empty glass: faint paper wash
    wash = inside * GLASS['wash']
    if lit is not None and look['impact_wash'] > 0:
        wash = inside * (GLASS['wash'] + look['impact_wash'] * gain * np.clip(.5 + .5 * along, 0, 1))
    L.put(PAPER, wash)
    if look['wall_a'] > 0:
        L.put(PAPER, band(d, -wall - .2, -1.2) * look['wall_a'])

    liquid = CINNABAR if kind == 'hp' else INK
    col = liquid
    if kind == 'hp' and st.low > 0:
        col = mix(liquid, INK, LOW['hp_darken'] * st.low)       # low HP sinks toward dark red, never brighter
    span = g.floor_y - g.full_y
    base = g.floor_y - st.level * span
    wallness = np.clip(1 - (-d_in) / 6.0, 0, 1)
    surf = base + st.tilt * (x - g.cx) + st.wave * np.sin((x - g.cx) / (w * .9) * 2 * math.pi + st.phase)
    surf = surf - LIQ['climb'] * wallness ** 2                  # the meniscus climbs the glass
    ncol = d.shape[1]

    # 3 wet film left on the glass by the loss: bristle streaks, a soft uneven top, dries from the top down in fingers
    if st.stain_top is not None and st.stain_a > 0 and st.stain_top > st.level + 1e-4:
        top_y = g.floor_y - min(1.0, st.stain_top) * span
        tex = grid_noise(d.shape, 2.4 * k, 70 * k, seed + 21)
        edge = (grid_noise((1, ncol), 9 * k, 1, seed + 22)[0] * 2 - 1) * 1.6
        finger = grid_noise((1, ncol), 6.5 * k, 1, seed + 23)[0]
        p_col = np.clip(st.stain_dry * (1.30 - .6 * finger), 0, 1)
        top_eff = (top_y + edge + (base - top_y) * p_col)[None, :]
        in_band = cov(surf - y) * np.clip(.5 + (y - top_eff) * .30, 0, 1)
        fade = .50 + .50 * np.clip((y - top_eff) / np.maximum(1.0, base - top_eff), 0, 1)
        L.put(col, cavity * in_band * st.stain_a * (.50 + .62 * tex) * fade)
    # 4 dried tide lines (low HP): thin, stronger at the glass
    for lv in st.tides:
        ty = g.floor_y - lv * span - LIQ['climb'] * wallness ** 2
        near = np.clip(1 - (-d_in) / 16.0, 0, 1)
        L.put(col, cavity * np.clip(1 - np.abs(y - ty) / .9, 0, 1) * LOW['tide_a'] * (.35 + .65 * near) * 1.6)

    # 5 liquid body: darker pooling edge at the glass, slightly paler core
    liq = cavity * cov(y - surf)
    pool = np.clip(1 - (-d_in) / LIQ['pool_px'], 0, 1)
    body = col[None, None, :] * (1 - LIQ['pool_dark'] * pool)[..., None]
    body = mix(body, PAPER[None, None, :], LIQ['core_light'] * (1 - pool))
    if look['depth'] > 0:
        dep = np.clip((y - base) / max(6.0, g.floor_y - base), 0, 1)
        body = body * (1 - look['depth'] * dep)[..., None]
    if look['marble'] > 0:
        m = grid_noise(d.shape, 22 * k, 16 * k, seed + 11) * 2 - 1
        body = np.clip(body * (1 + look['marble'] * m * (.75 if kind == 'hp' else 1.8))[..., None], 0, 1)
    if st.fresh_from is not None and st.fresh_a > 0:
        old_y = g.floor_y - st.fresh_from * span
        body = mix(body, PAPER[None, None, :], FRESH_LIGHTEN * cov(old_y - y) * st.fresh_a)
    if lit is not None:
        body = mix(body, PAPER[None, None, :], np.clip(along, 0, 1) * IMPACT['liquid_shift'] * gain)
    if st.dry_ink:                                            # a dry-brush smear: horizontal bristles, paper shows through
        rowtex = grid_noise(d.shape, 34 * k, 1.25 * k, seed + 31)
        thr = np.quantile(rowtex, 1 - LOW['dry_cover'])
        ends = np.clip((g.cav_half[:, None] - np.abs(x - g.cx)) / 7.0, 0, 1) ** .6
        liq = liq * np.clip((rowtex - thr) / .07 + .5, 0, 1) * (.35 + .65 * ends)
    L.put(body, liq * LIQ['alpha'])

    # 5b (mock) the far half of the surface seen through the glass: a thin paler lens above the value line
    if look['lens'] and st.level > .012 and not st.dry_ink:
        row = int(np.clip(np.searchsorted(g.row_y, base), 0, len(g.row_y) - 1))
        half = max(4.0, g.cav_half[row])
        e = look['lens_px'][kind] * min(1.0, half / 30.0)
        lens_h = e * np.sqrt(np.clip(1 - ((x - g.cx) / half) ** 2, 0, 1))
        lens = cavity * cov(surf - y) * cov(y - (surf - lens_h))
        L.put(mix(col, PAPER, look['lens_mix'][kind]), lens * .92)
        L.put(PAPER, cavity * np.clip(1 - np.abs(y - (surf - lens_h)) / .8, 0, 1) * look['lens_back_a'] * (lens_h > .6))
    # 6 pouring thread from the neck to the surface
    if st.thread > 0:
        tx = g.cx + .3 * np.sin(y * .15 + 1.0)
        foot = 1.6 * np.clip(1 - (surf - y) / 7.0, 0, 1) ** 2
        th = cov(1.0 + foot - np.abs(x - tx)) * cov(surf - y) * cov(y - (g.lip_h + 1.0))
        L.put(mix(col, PAPER, .10), cavity * th * LIQ['alpha'] * st.thread)
    # 7 meniscus line (paper value)
    if st.level > .004:
        men = np.clip(1.0 - np.abs(y - surf) / LIQ['men_px'], 0, 1)
        if st.dry_ink:
            men = men * .5
        L.put(PAPER, cavity * men * LIQ['men_a'])
    # 8 static paper highlight stroke: a value, not a gradient
    win = np.clip(1 - np.abs(g.ang - math.radians(-142)) / math.radians(27), 0, 1)
    hw = 1.7 * win ** .6
    L.put(PAPER, band(d, -10.0 - hw, -10.0 + hw) * (win > 0) * GLASS['hl_a'])
    if look['hl2_a'] > 0:
        win2 = np.clip(1 - np.abs(g.ang - math.radians(38)) / math.radians(11), 0, 1)
        hw2 = 1.0 * win2 ** .6
        L.put(PAPER, band(d, -8.2 - hw2, -8.2 + hw2) * (win2 > 0) * look['hl2_a'])
    # 9 impact frame: ink shadow crescent on the far side, the glass line turns paper on the lit side
    lw = GLASS['outline']
    if look['line_mod'] > 0:
        lw = lw * (1 + look['line_mod'] * np.cos(g.ang - math.radians(118)) + .06 * np.sin(3 * g.ang + 1.3))
    line_col = np.broadcast_to(INK, L.rgb.shape)
    if lit is not None:
        L.put(INK, inside * np.clip(1 - (-d) / IMPACT['shadow_px'], 0, 1) * dark * IMPACT['shadow_a'])
        line_col = mix(INK[None, None, :], PAPER[None, None, :], lit)
    L.put(line_col, band(d, -lw, 0.0) * .96)
    return L.down(ss)


# ---------------------------------------------------------------------------------------------------------------------
# marks: ink pictogram + paper rim (the D13 marker grammar). Brush strokes with a wet head and a dry tail.
# ---------------------------------------------------------------------------------------------------------------------
def catmull(ctrl, widths, per=9):
    P = np.array(ctrl, float)
    Wd = np.array(widths, float)
    if len(P) == 2:
        ts = np.linspace(0, 1, per + 1)
        return P[0] + (P[1] - P[0]) * ts[:, None], (Wd[0] + (Wd[1] - Wd[0]) * ts) / 2
    Pp = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    pts, ws = [], []
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = Pp[i:i + 4]
        for t in np.linspace(0, 1, per, endpoint=False):
            pts.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
            ws.append(Wd[i] + (Wd[i + 1] - Wd[i]) * t)
    pts.append(P[-1])
    ws.append(Wd[-1])
    return np.array(pts), np.array(ws) / 2


class Tf:
    def __init__(self, scale=1.0, ox=0.0, oy=0.0):
        self.scale, self.ox, self.oy = scale, ox, oy

    def __call__(self, p):
        return (p[0] * self.scale + self.ox, p[1] * self.scale + self.oy)


class Glyph:
    """44-unit canvas. m = wet ink coverage, tex = bristle value per pixel (for the dry state)."""

    U = 44.0

    def __init__(self, s, ss=4):
        self.s, self.ss = s, ss
        n = int(round(self.U * s)) * ss
        self.n = n
        self.k = n / self.U
        ys, xs = np.mgrid[0:n, 0:n]
        self.x = (xs + .5) / self.k
        self.y = (ys + .5) / self.k
        self.m = np.zeros((n, n))
        self.tex = np.full((n, n), .5)
        self.rough = (grid_noise((n, n), 2.4 * self.k, 2.4 * self.k, 77) - .5) * .5      # hand-drawn edge, units

    def _cov(self, sd):
        return np.clip(.5 - (sd + self.rough) * self.k / self.ss, 0, 1)

    def _add(self, cov, tex):
        take = cov > self.m
        self.tex = np.where(take, tex, self.tex)
        self.m = np.maximum(self.m, cov)

    def stroke(self, ctrl, widths, dry_from=None, seed=0, bins=5, tf=None):
        if tf is not None:
            ctrl = [tf(p) for p in ctrl]
            widths = [w * tf.scale for w in widths]
        pts, rad = catmull(ctrl, widths)
        n = len(pts)
        best = np.full(self.m.shape, 1e9)
        bu = np.zeros(self.m.shape)
        bv = np.zeros(self.m.shape)
        for i in range(n - 1):
            p0, p1 = pts[i], pts[i + 1]
            e = p1 - p0
            el = max(1e-6, float(e @ e))
            px, py = self.x - p0[0], self.y - p0[1]
            t = np.clip((px * e[0] + py * e[1]) / el, 0, 1)
            qx, qy = px - t * e[0], py - t * e[1]
            r = rad[i] + (rad[i + 1] - rad[i]) * t
            sd = np.hypot(qx, qy) - r
            take = sd < best
            best = np.where(take, sd, best)
            bu = np.where(take, (i + t) / (n - 1), bu)
            bv = np.where(take, (px * (-e[1]) + py * e[0]) / math.sqrt(el) / np.maximum(r, .3), bv)
        cov = self._cov(best)
        h = hash_bins(64, 100 + seed)
        tex = h[np.clip(((bv + 1) * .5 * bins).astype(int), 0, bins - 1)]
        if dry_from is not None:        # flying white toward the tail: bristles drop out one by one
            u = np.clip((bu - dry_from) / max(1e-6, 1 - dry_from), 0, 1)
            cov = cov * (tex > u * .95).astype(float) * (1 - .25 * u)
        self._add(cov, tex)

    def disc(self, cx, cy, r, tf=None):
        if tf is not None:
            cx, cy = tf((cx, cy))
            r *= tf.scale
        self._add(self._cov(np.hypot(self.x - cx, self.y - cy) - r), np.full(self.m.shape, .2))

    def poly(self, pts, tf=None, seed=0, cut=False):
        if tf is not None:
            pts = [tf(p) for p in pts]
        im = Image.new('L', (self.n, self.n), 0)
        ImageDraw.Draw(im).polygon([(p[0] * self.k, p[1] * self.k) for p in pts], fill=255)
        cov = np.asarray(im.filter(ImageFilter.GaussianBlur(self.ss * .35)), float) / 255
        if cut:
            self.m = self.m * (1 - cov)
            return
        h = hash_bins(64, 300 + seed)
        self._add(cov, h[np.clip((self.y / 1.6).astype(int), 0, 63)])

    def cut_disc(self, cx, cy, r, tf=None):
        if tf is not None:
            cx, cy = tf((cx, cy))
            r *= tf.scale
        cov = np.clip(.5 - (np.hypot(self.x - cx, self.y - cy) - r) * self.k / self.ss, 0, 1)
        self.m = self.m * (1 - cov)

    def ring(self, cx, cy, r, width, tf=None, seed=0):
        pts = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in np.linspace(0, 360, 31)]
        self.stroke(pts, [width] * len(pts), tf=tf, seed=seed)


def glyph_dodge(g):
    """Side-step: a low leaning body; the trailing leg and sleeve break into dry-brush streaks (speed)."""
    g.disc(31.6, 11.6, 4.5)
    g.stroke([(28.6, 16.2), (23.6, 21.6), (19.2, 26.4)], [6.8, 6.8, 5.8], seed=1)                     # torso
    g.stroke([(20.0, 26.4), (27.4, 28.4), (29.0, 37.2)], [5.4, 4.6, 2.8], seed=2)                     # front leg, knee forward
    g.stroke([(18.6, 26.6), (12.0, 31.0), (3.6, 33.6)], [5.4, 4.2, 2.6], dry_from=.42, seed=3)        # trailing leg
    g.stroke([(27.6, 17.8), (33.6, 22.6), (39.6, 20.4)], [3.8, 3.4, 2.0], seed=4)                     # leading arm
    g.stroke([(26.0, 17.4), (18.0, 15.6), (6.4, 16.6)], [4.6, 4.4, 3.0], dry_from=.30, seed=5)        # sleeve streaming behind
    g.stroke([(3.0, 23.4), (12.6, 23.0)], [1.2, 2.2], seed=6)                                         # speed streak
    return (g.x - 2.0) / 40.0          # wetting order: tail -> head (the way the stroke is drawn)


def glyph_jump(g):
    """Jump: an upright body off the ground, arms thrown up, dry streaks under the feet, a ground stroke below."""
    g.stroke([(5.6, 40.8), (21.0, 40.0), (39.0, 40.6)], [2.4, 3.6, 2.2], dry_from=.62, seed=1)        # ground
    g.disc(22.4, 6.2, 4.3)
    g.stroke([(22.2, 10.4), (22.0, 20.0)], [6.6, 5.8], seed=2)                                        # torso
    g.stroke([(20.6, 12.4), (14.6, 10.6), (10.6, 3.6)], [3.8, 3.4, 2.0], seed=3)                      # arms up
    g.stroke([(23.8, 12.4), (30.0, 10.6), (34.2, 3.8)], [3.8, 3.4, 2.0], seed=4)
    g.stroke([(20.6, 19.4), (17.6, 24.6), (14.4, 30.0)], [5.2, 4.2, 2.6], seed=5)                     # legs flung apart
    g.stroke([(23.4, 19.4), (26.6, 24.6), (29.8, 30.0)], [5.2, 4.2, 2.6], seed=6)
    g.stroke([(14.4, 32.4), (13.6, 37.4)], [2.4, 1.2], dry_from=.25, seed=7)                          # lift streaks
    g.stroke([(22.0, 30.4), (22.0, 37.8)], [1.8, 1.0], dry_from=.35, seed=8)
    g.stroke([(29.8, 32.4), (30.6, 37.4)], [2.4, 1.2], dry_from=.25, seed=9)
    return (42.0 - g.y) / 38.0         # wetting order: ground -> head


def _cart(g, tf=None, detail=True):
    """The palanquin cart, side view: upturned roof, box with two paper windows, one big spoked wheel, a shaft."""
    tf = tf or Tf()
    g.poly([(11.4, 13.2), (14.8, 6.8), (29.2, 6.8), (32.6, 13.2)], tf=tf, seed=1)                      # roof ridge
    g.stroke([(3.4, 10.8), (11.4, 13.6), (22.0, 14.4), (32.6, 13.6), (40.6, 10.8)], [1.6, 3.4, 3.8, 3.4, 1.6], tf=tf, seed=2)   # eaves, tips up
    g.poly([(9.8, 15.4), (34.2, 15.4), (34.2, 27.4), (9.8, 27.4)], tf=tf, seed=3)                      # box
    if detail:
        g.poly([(12.8, 17.8), (20.4, 17.8), (20.4, 22.6), (12.8, 22.6)], tf=tf, cut=True)              # windows
        g.poly([(23.6, 17.8), (31.2, 17.8), (31.2, 22.6), (23.6, 22.6)], tf=tf, cut=True)
    g.stroke([(34.0, 25.4), (42.4, 28.8)], [2.8, 1.4], tf=tf, seed=4)                                  # shaft
    wx, wy, wr = 22.0, 33.4, 7.4
    g.cut_disc(wx, wy, wr + 3.1, tf=tf)                                                               # air round the wheel
    g.ring(wx, wy, wr, 2.8 if detail else 3.4, tf=tf, seed=5)
    if detail:
        for a in (0, 60, 120):
            ca, sa = math.cos(math.radians(a + 18)), math.sin(math.radians(a + 18))
            g.stroke([(wx - ca * wr, wy - sa * wr), (wx + ca * wr, wy + sa * wr)], [1.15, 1.15], tf=tf, seed=6 + a)
    g.disc(wx, wy, 2.2 if detail else 2.8, tf=tf)


def glyph_vehicle(g):
    _cart(g)
    return (g.x - 2.0) / 40.0


def glyph_vehicle_out(g):
    """The cart is out and near: G takes it back. The same cart, smaller, under a rewinding hook (the recall stroke)."""
    _cart(g, Tf(.76, 0.4, 9.8), detail=False)
    cx, cy, r = 24.6, 21.2, 17.0
    angs = np.linspace(-130, 28, 15)                    # head first: wet head up on the left, dry tail low on the right
    pts = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in angs]
    g.stroke(pts, list(np.linspace(4.2, 1.8, 15)), dry_from=.60, seed=9)
    a = math.radians(angs[0])
    px, py = pts[0]
    vx, vy = math.sin(a), -math.cos(a)                  # travel direction at the head (toward decreasing angle)
    nx, ny = -vy, vx
    g.poly([(px + vx * 6.4, py + vy * 6.4), (px + nx * 5.2 - vx * 1.0, py + ny * 5.2 - vy * 1.0),
            (px - nx * 5.2 - vx * 1.0, py - ny * 5.2 - vy * 1.0)], seed=4)
    return (g.x - 2.0) / 40.0


GLYPHS = dict(dodge=glyph_dodge, jump=glyph_jump, vehicle=glyph_vehicle, vehicle_out=glyph_vehicle_out)
_GCACHE = {}


def dilate(im, r):
    """Octagonal dilation of an L image by about r px (alternating 3x3 square and cross steps)."""
    for i in range(int(round(r))):
        if i % 2 == 0:
            im = im.filter(ImageFilter.MaxFilter(3))
        else:
            a = np.asarray(im)
            b = a.copy()
            b[1:] = np.maximum(b[1:], a[:-1])
            b[:-1] = np.maximum(b[:-1], a[1:])
            b[:, 1:] = np.maximum(b[:, 1:], a[:, :-1])
            b[:, :-1] = np.maximum(b[:, :-1], a[:, 1:])
            im = Image.fromarray(b)
    return im


def glyph_fields(kind, s, ss):
    """Fields of one pictogram on the 44-unit canvas grown by MPAD on every side (supersampled).
    Returns (glyph, coverage, bristle value, wetting order, rim, wide rim, rim normal x, rim normal y)."""
    key = (kind, s, ss)
    if key not in _GCACHE:
        g = Glyph(s, ss)
        order = np.clip(GLYPHS[kind](g), 0, 1)
        pad = int(round(MPAD * s)) * ss
        m = np.pad(g.m, pad)
        tex = np.pad(g.tex, pad, constant_values=.5)
        order = np.pad(order, pad, mode='edge')
        hard = Image.fromarray((np.clip(m * 1.6, 0, 1) * 255).astype(np.uint8))
        rim_px = MARK['rim_px'] * g.k
        rim = np.asarray(dilate(hard, rim_px).filter(ImageFilter.GaussianBlur(g.k * .35)), float) / 255
        rim_wide = np.asarray(dilate(hard, rim_px * 1.6).filter(ImageFilter.GaussianBlur(g.k * .35)), float) / 255
        blur = np.asarray(hard.filter(ImageFilter.GaussianBlur(g.k * 2.2)), float) / 255
        gy, gx = np.gradient(blur)
        nrm = np.maximum(np.hypot(gx, gy), 1e-5)
        _GCACHE[key] = (g, m, tex, order, rim, rim_wide, -gx / nrm, -gy / nrm)
    return _GCACHE[key]


def render_mark(kind, state='wet', fill=1.0, s=1.0, ss=4, impact=None):
    """state: wet | dry | rewet (fill 0..1 along the stroke order). Returns straight-alpha RGBA, a (44 + 2 MPAD) s px
    square: the 44 px mark rect sits MPAD s px inside it."""
    g, m, tex, order, rim, rim_wide, nx, ny = glyph_fields(kind, s, ss)
    dry_a = MARK['dry_a'] * (.45 + 1.1 * tex)            # dry = bristle streaks round alpha .30
    if state == 'wet':
        wet = np.ones_like(m)
    elif state == 'dry':
        wet = np.zeros_like(m)
    else:
        edge = fill * 1.08 - .04 + (tex - .5) * .10          # a slightly ragged wet front
        wet = np.clip((edge - order) / .05, 0, 1)
    ink_a = m * (wet + (1 - wet) * dry_a)
    L = Layer(m.shape)
    rim_col = PAPER
    if impact is not None:
        (lx, ly), gain = impact
        ndl = nx * lx + ny * ly
        dark = np.clip(-ndl, 0, 1) * gain
        lit = np.clip(ndl, 0, 1) * gain
        rim_col = mix(PAPER[None, None, :], INK[None, None, :], IMPACT['far_rim_ink'] * dark)
        rim = np.maximum(rim, rim_wide * np.clip((lit - .25) / .2, 0, 1))      # the lit side of the rim looks thicker
    L.put(rim_col, rim * MARK['rim_a'])
    L.put(INK, ink_a)
    return L.down(g.ss)


# ---------------------------------------------------------------------------------------------------------------------
# cluster composition
# ---------------------------------------------------------------------------------------------------------------------
def paste(dst, rgba, x, y):
    """sRGB-space 'over' of a straight-alpha float RGBA onto dst (H, W, 3) at integer x, y; clips at the borders."""
    x, y = int(round(x)), int(round(y))
    h, w = rgba.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    src = rgba[y0 - y:y1 - y, x0 - x:x1 - x]
    a = src[..., 3:4]
    dst[y0:y1, x0:x1] = dst[y0:y1, x0:x1] * (1 - a) + src[..., :3] * a


def mark_centres():
    return [(ARC['cx'] + ARC['r'] * math.cos(math.radians(deg)), ARC['cy'] - ARC['r'] * math.sin(math.radians(deg)))
            for deg in ARC['deg']]


def impact_dir(cx, cy, pt):
    dx, dy = pt[0] - cx, pt[1] - cy
    n = math.hypot(dx, dy) or 1.0
    return (dx / n, dy / n)


DEFAULT_MARKS = (('dodge', 'wet', 1.0), ('jump', 'wet', 1.0), ('vehicle', 'wet', 1.0))
CROP = (48, 768, 332, 1030)          # the standard cluster crop (mock px), 284 x 262


def draw_cluster(dst, region, s, hp, ink, marks=DEFAULT_MARKS, impact_pt=None, gain=IMPACT['gain'], spec_only=False):
    """dst: float RGB sized to region * s. hp / ink: VState. marks: 3 x (glyph, state, fill) or None (not drawn)."""
    x0, y0 = region[0], region[1]
    for kind, st in (('hp', hp), ('ink', ink)):          # ink is drawn second = in front
        rx, ry, rw, rh = RECT[kind]
        imp = None
        if impact_pt is not None:
            imp = (impact_dir(rx + rw / 2, ry + rh / 2, impact_pt), gain)
        v = render_vessel(kind, st, s=s, impact=imp, spec_only=spec_only)
        paste(dst, v, (rx - PAD - x0) * s, (ry - PAD - y0) * s)
    for (mx, my), mk in zip(mark_centres(), marks):
        if mk is None:
            continue
        kind, state, fill = mk
        imp = None
        if impact_pt is not None:
            imp = (impact_dir(mx, my, impact_pt), gain)
        r = render_mark(kind, state, fill, s=s, impact=imp)
        half = ARC['size'] / 2 + MPAD
        paste(dst, r, (mx - half - x0) * s, (my - half - y0) * s)


def flat(region, s, col):
    h = int(round((region[3] - region[1]) * s))
    w = int(round((region[2] - region[0]) * s))
    return np.ones((h, w, 3)) * col[None, None, :]


# ---------------------------------------------------------------------------------------------------------------------
# the real still (a #308 playtest capture) with the old #304 meters cloned out, and the impact-frame look of the world
# ---------------------------------------------------------------------------------------------------------------------
_STILL = {}
ROAD_PNG = os.path.join(ROOT, 'Art', 'UI304', 'clean', 'road.png')


def still_full(name='road'):
    """Float RGB of a real capture.
    road = the HUD-less clean capture (1920x1080): nothing has to be painted out and the player is on foot.
    seat = a #308 playtest capture (2560x1440); its old #304 meters are replaced by the grass band right above them."""
    key = ('full', name)
    if key not in _STILL:
        if name == 'road':
            a = np.asarray(Image.open(ROAD_PNG).convert('RGB'), float) / 255
        else:
            a = np.asarray(Image.open(STILL_PNG).convert('RGB'), float) / 255
            f = a.shape[1] / 1920.0
            x0, y0, x1, y1 = [int(round(v * f)) for v in (30, 874, 650, 1028)]
            dy = int(round(144 * f))
            src = a[y0 - dy:y1 - dy, x0:x1].copy()
            pad = int(14 * f)
            im = Image.new('L', (x1 - x0, y1 - y0), 0)
            ImageDraw.Draw(im).rectangle([pad, pad, im.width - pad, im.height - pad], fill=255)
            m = np.asarray(im.filter(ImageFilter.GaussianBlur(pad * .45)), float) / 255
            a[y0:y1, x0:x1] = a[y0:y1, x0:x1] * (1 - m[..., None]) + src * m[..., None]
        _STILL[key] = a
    return _STILL[key]


def world_variant(mode, impact_pt, name='road'):
    """mode: normal | f1 (two values, inverted, paper spreading from the impact point) | f2 (stroke silhouette).
    An imitation of the WORDS of SPEC-SPELL-DEPLOY-308 section 8, for context only; it is not that Spec's art."""
    key = (mode, impact_pt, name)
    if key in _STILL:
        return _STILL[key]
    a = still_full(name)
    if mode == 'normal':
        _STILL[key] = a
        return a
    f = a.shape[1] / 1920.0
    lum = a @ np.array([.2126, .7152, .0722])
    lum = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2 * f)), float) / 255
    two = np.clip((lum - .40) / .05 + .5, 0, 1)             # 1 = paper side
    H, W = lum.shape
    ys, xs = np.mgrid[0:H, 0:W]
    px, py = impact_pt[0] * f, impact_pt[1] * f
    r = np.hypot(xs - px, ys - py) / H
    if mode == 'f1':
        val = .88 - two * (.88 - .07)                       # paper -> ink .07, ink lines -> paper .88
        bloom = np.clip(1 - r / .36, 0, 1) ** 1.6
        val = val * (1 - bloom) + .88 * bloom
        ang = np.arctan2(ys - py, xs - px)
        for a0 in (-2.62, -1.93, -.71, .22, .97, 2.31, 2.86):   # needle lines to the screen edge
            dist = np.abs(np.sin(ang - a0)) * r * H
            val = np.maximum(val, .88 * np.clip(1.6 * f - dist, 0, 1) * (np.cos(ang - a0) > 0))
    else:
        val = .07 + two * (.88 - .07)                       # background pressed to one paper value, dark things stay ink
    t = np.clip((val - .07) / (.88 - .07), 0, 1)
    out = INK[None, None, :] + (PAPER - INK)[None, None, :] * t[..., None]
    _STILL[key] = out
    return out


def still_region(region, s, mode='normal', impact_pt=IMPACT_PT, name='road'):
    a = world_variant(mode, impact_pt, name)
    f = a.shape[1] / 1920.0
    box = [int(round(v * f)) for v in region]
    im = Image.fromarray((np.clip(a[box[1]:box[3], box[0]:box[2]], 0, 1) * 255 + .5).astype(np.uint8))
    w = int(round((region[2] - region[0]) * s))
    h = int(round((region[3] - region[1]) * s))
    return np.asarray(im.resize((w, h), Image.LANCZOS), float) / 255


# ---------------------------------------------------------------------------------------------------------------------
# liquid motion (the Spec's damped spring, fixed step 1/240 s) and level / film timing
# ---------------------------------------------------------------------------------------------------------------------
def spring_run(kind, yaw_fn, t_end, kick=0.0, kick_wave=0.0, dt=1 / 240.0):
    p = PER[kind]
    w0 = 2 * math.pi * p['hz']
    th, v, A, ph = 0.0, kick, kick_wave, 0.0
    out = []
    for i in range(int(round(t_end / dt)) + 1):
        t = i * dt
        target = max(-p['max_tilt'], min(p['max_tilt'], p['tilt_turn'] * yaw_fn(t) / 180.0))
        acc = -w0 * w0 * (th - target) - 2 * p['zeta'] * w0 * v
        v += acc * dt
        th += v * dt
        A += (-A / p['wave_tau'] + LOOK['wave_gain'][kind] * abs(v)) * dt
        A = min(A, p['wave_max'])
        ph += p['wave_speed'] * dt
        out.append((t, th, A, ph))
    return out


def sample(run, t):
    return run[min(len(run) - 1, max(0, int(round(t * 240))))]


def drain_level(kind, start, target, t):
    """Visible level t seconds after the value dropped: exponential chase with a floor speed."""
    p = PER[kind]
    lv, dt = start, 1 / 240.0
    for _ in range(int(round(max(0.0, t) / dt))):
        lv = max(target, lv - max((lv - target) / p['drain_tau'], DRAIN_MIN_SPEED) * dt)
    return lv


def fill_level(kind, start, target, t):
    return target - (target - start) * math.exp(-max(0.0, t) / PER[kind]['fill_tau'])


def film(kind, t):
    """(alpha, dry 0..1) of the wet film t seconds after the loss."""
    p = PER[kind]
    if t <= p['wet_hold']:
        return p['wet_a'], 0.0
    u = min(1.0, (t - p['wet_hold']) / p['wet_dry'])
    return p['wet_a'] * (1 - .3 * u), u


# ---------------------------------------------------------------------------------------------------------------------
# sheet building
# ---------------------------------------------------------------------------------------------------------------------
def font(size, weight='Bold', serif=False):
    try:
        if serif:
            f = ImageFont.truetype(FONT_DIR + 'NotoSerifKR-VF.ttf', size)
            f.set_variation_by_name(weight)
            return f
        return ImageFont.truetype(FONT_DIR + ('NotoSansKR-Bold.ttf' if weight in ('Bold', 'Black') else 'NotoSansKR-Regular.ttf'), size)
    except Exception:
        return ImageFont.truetype(FONT_DIR + 'malgunbd.ttf', size)


def to_img(arr):
    return Image.fromarray((np.clip(arr, 0, 1) * 255 + .5).astype(np.uint8))


def save_frame(arr_or_img, name):
    im = arr_or_img if isinstance(arr_or_img, Image.Image) else to_img(arr_or_img)
    im.save(os.path.join(FRAMES, name))
    return im


def c8(c):
    return tuple(int(round(v * 255)) for v in c)


INK8, ASH8, PAPER8, BG8, CIN8 = c8(INK), c8(ASH), c8(PAPER), c8(SHEET_BG), c8(CINNABAR)
EDGE8 = (120, 117, 109)


class Sheet:
    W = 2400
    M = 40

    def __init__(self):
        self.im = Image.new('RGB', (self.W, 9000), BG8)
        self.dr = ImageDraw.Draw(self.im)
        self.y = 0

    def title(self, text, subs):
        h = 96 + 34 * len(subs)
        self.dr.rectangle([0, 0, self.W, h], fill=INK8)
        self.dr.text((self.M, 18), text, fill=PAPER8, font=font(46, 'Black', serif=True))
        for i, sline in enumerate(subs):
            self.dr.text((self.M, 86 + 34 * i), sline, fill=(167, 163, 152), font=font(23, 'Regular'))
        self.y = h + 30

    def head(self, text, note=None):
        self.dr.ellipse([self.M, self.y + 16, self.M + 16, self.y + 32], fill=CIN8)
        self.dr.text((self.M + 28, self.y - 2), text, fill=INK8, font=font(36, 'Black', serif=True))
        self.y += 58
        if note:
            for line in note.split('\n'):
                self.dr.text((self.M + 28, self.y), line, fill=ASH8, font=font(22, 'Regular'))
                self.y += 32
        self.y += 12

    def put(self, img, x, y=None, border=True):
        y = self.y if y is None else y
        self.im.paste(img, (int(x), int(y)))
        if border:
            self.dr.rectangle([int(x) - 1, int(y) - 1, int(x) + img.width, int(y) + img.height], outline=EDGE8, width=1)

    def cap(self, x, y, text, size=21, fill=INK8, weight='Bold', anchor='la'):
        f = font(size, weight)
        yy = y
        for line in text.split('\n'):
            self.dr.text((x, yy), line, fill=fill, font=f, anchor=anchor)
            yy += int(size * 1.45)
        return yy

    def rule(self):
        self.y += 22
        self.dr.line([self.M, self.y, self.W - self.M, self.y], fill=(150, 146, 137), width=2)
        self.y += 26

    def finish(self, path):
        self.im.crop((0, 0, self.W, self.y + 24)).save(path)


# ---------------------------------------------------------------------------------------------------------------------
# panels
# ---------------------------------------------------------------------------------------------------------------------
HP_N = hp_state(.62, tilt=.03, wave=.8, phase=.7)
INK_N = ink_state(.44, tilt=-.018, wave=.45, phase=2.1)
FULL = (0, 0, 1920, 1080)


def cluster_on(bg_kind, s, region=CROP, hp=HP_N, ink=INK_N, marks=DEFAULT_MARKS, impact_pt=None, mode='normal',
               spec_only=False, bg_col=None):
    """bg_kind: paper | dark | flat (bg_col) | still (the HUD-less road capture) | grass (the #308 capture, meters cloned out)."""
    if bg_kind == 'paper':
        dst = flat(region, s, SKY)
    elif bg_kind == 'dark':
        dst = flat(region, s, PINE)
    elif bg_kind == 'flat':
        dst = flat(region, s, bg_col)
    else:
        dst = still_region(region, s, mode=mode, name='road' if bg_kind == 'still' else 'seat')
    draw_cluster(dst, region, s, hp, ink, marks, impact_pt=impact_pt, spec_only=spec_only)
    return dst


def on_bg(rgba, bgc):
    c = np.ones((rgba.shape[0], rgba.shape[1], 3)) * bgc
    paste(c, rgba, 0, 0)
    return c


def panel_a(sh, rep):
    sh.head('가. 군집 — 100 % 와 200 %',
            '왼쪽 아래 그대로. 체력(주묵)이 왼쪽 위에서 크게, 먹이 오른쪽 아래에서 맞닿는다. 표시 셋은 오른쪽 위 호에 회피 · 점프 · 자동차 순서.\n'
            '200 % = 두 배 해상도로 다시 그린 것(4K 화면에 가깝다). 100 % = 1920×1080에서의 실제 픽셀 크기.')
    a_still2 = save_frame(cluster_on('still', 2), 'a_cluster_still_200.png')
    a_paper2 = save_frame(cluster_on('paper', 2), 'a_cluster_paper_200.png')
    a_still1 = save_frame(cluster_on('still', 1), 'a_cluster_still_100.png')
    a_paper1 = save_frame(cluster_on('paper', 1), 'a_cluster_paper_100.png')
    full = still_region(FULL, 1)
    draw_cluster(full, FULL, 1, HP_N, INK_N, DEFAULT_MARKS)
    full_im = save_frame(full, 'a_fullframe_1920.png')
    y0 = sh.y
    x = sh.M
    sh.put(a_still2, x)
    sh.cap(x, y0 + a_still2.height + 8, '실제 화면 위 · 200 %\n(HUD 없는 플레이 캡처 Art/UI304/clean/road.png의\n왼쪽 아래 모서리)', 20)
    x += a_still2.width + 14
    sh.put(a_paper2, x)
    sh.cap(x, y0 + a_paper2.height + 8, '한지색 바탕 · 200 %\n(#E4E0D6 = 가장 밝은 하늘. 한지 테는 바탕에 묻히고\n먹 선만 남는다)', 20)
    x += a_paper2.width + 14
    sh.put(a_still1, x)
    sh.put(a_paper1, x, y0 + 262)
    sh.cap(x, y0 + 524 + 8, '100 % (실제 크기)\n위: 실제 화면 · 아래: 한지색', 20)
    x += a_still1.width + 14
    fw = sh.W - sh.M - x
    fh = int(round(fw * 1080 / 1920))
    sh.put(full_im.resize((fw, fh), Image.LANCZOS), x)
    sx = fw / 1920.0
    sh.dr.rectangle([x + 44 * sx, y0 + 766 * sx, x + 336 * sx, y0 + 1030 * sx], outline=CIN8, width=2)
    yy = sh.cap(x, y0 + fh + 10, '화면 전체(1920×1080을 줄인 것). 주사 네모 = 군집.', 20)
    g = vessel_geo(114, 150, 1, 4)
    gi = vessel_geo(100, 132, 1, 4)
    yy = sh.cap(x, yy + 4,
                '· 군집 범위 (64, 784) – (318, 1012) px, 화면의 1.4 %%\n'
                '· 체력 용기 %d×%d px · 먹 용기 %d×%d px (실루엣)\n'
                '· 표시 44 px 셋. 글자 · 건반 없음\n'
                '· 기울임 없음(수면이 수평이어야 액체로 읽힌다)' % (round(g.sil_w), round(g.sil_h), round(gi.sil_w), round(gi.sil_h)),
                20, fill=ASH8, weight='Regular')
    rep['silhouette'] = dict(hp=dict(w=round(g.sil_w, 1), h=round(g.sil_h, 1), h_over_w=round(g.sil_h / g.sil_w, 3),
                                     body_h_over_w=round(g.body_h / g.body_w, 3)),
                             ink=dict(w=round(gi.sil_w, 1), h=round(gi.sil_h, 1), h_over_w=round(gi.sil_h / gi.sil_w, 3)))
    sh.y = max(yy, y0 + 524 + 8 + 3 * 29) + 4
    sh.rule()


def panel_b(sh, rep):
    sh.head('나. 수위 — 체력 100 · 60 · 25 · 8 %  /  먹 100 · 50 · 15 · 8 · 0 %',
            '값 = 가운데 열의 수면 높이. 100 %에서도 목은 빈다. 체력 35 % 아래: 검붉게 가라앉고, 마른 물때 테가 남고, 수면이 잔잔히 떤다.\n'
            '먹이 술식 한 번 값(15 %)보다 적으면 고인 먹이 갈필 자국으로 갈라진다(다음 술식을 낼 수 없다). 깜빡임 · 맥동 · 발광은 어디에도 없다.')
    cells = [('hp', hp_state(1.0), '체력 100 %', '어깨선까지'), ('hp', hp_state(.60), '체력 60 %', ''),
             ('hp', hp_state(.25, phase=1.1), '체력 25 %', '낮음: 검붉음 + 물때 테'), ('hp', hp_state(.08, phase=2.0), '체력 8 %', '낮음'),
             ('ink', ink_state(1.0), '먹 100 %', ''), ('ink', ink_state(.50), '먹 50 %', ''),
             ('ink', ink_state(.15), '먹 15 %', '딱 한 번 값'), ('ink', ink_state(.08), '먹 8 %', '갈필: 한 번 값 미만'),
             ('ink', ink_state(0.0), '먹 0 %', '빈 병')]
    gap = 22
    centres = []
    widths = [int(math.ceil((RECT[k][2] + 2 * PAD) * 2)) for k, _, _, _ in cells]
    total = sum(widths) + gap * (len(cells) - 1) + 40
    for r, (bgc, name, lab) in enumerate(((SKY, 'paper', '한지색 바탕(#E4E0D6)'), (PINE, 'dark', '어두운 바탕(#2A2A26 솔숲)'))):
        sh.cap(sh.M, sh.y, lab, 20, fill=ASH8)
        sh.y += 32
        row = np.ones((336, sh.W - 2 * sh.M, 3)) * bgc
        x = (row.shape[1] - total) // 2
        for i, (kind, st, _, _) in enumerate(cells):
            v = render_vessel(kind, st, s=2)
            if i == 4:
                x += 40
            paste(row, v, x, 336 - v.shape[0] - 6)
            save_frame(on_bg(v, bgc), 'b_%s_%03d_%s.png' % (kind, int(round(st.level * 100)), name))
            if r == 0:
                centres.append(x + sh.M + v.shape[1] // 2)
            x += v.shape[1] + gap
        sh.put(to_img(row), sh.M, sh.y)
        sh.y += 336 + 8
    for (kind, st, lab, sub), cx in zip(cells, centres):
        sh.cap(cx, sh.y, lab, 22, anchor='ma')
        if sub:
            sh.cap(cx, sh.y + 30, sub, 19, fill=ASH8, weight='Regular', anchor='ma')
    sh.y += 64
    sh.rule()


def panel_c(sh, rep):
    sh.head('다. 움직임 — 출렁임 · 먹이 빠짐 · 붓기 · 피격 (정지 화면)',
            '입력이 있을 때만 움직이고 3초 안에 멎는다. 값(수위)은 가운데 열에서 그대로 읽힌다. 아래 숫자는 Spec의 감쇠 스프링과 수위 식을 그대로 적분한 값이다.')
    # --- 1 slosh after a quick view turn
    yaw = lambda t: 420.0 if t < .22 else 0.0
    runs = dict(hp=spring_run('hp', yaw, 3.0), ink=spring_run('ink', yaw, 3.0))
    rep['slosh'] = []
    rep['slosh_peak_tilt'] = dict(hp=round(max(abs(r[1]) for r in runs['hp']), 3), ink=round(max(abs(r[1]) for r in runs['ink']), 3),
                                  hp_target_cap=PER['hp']['max_tilt'], ink_target_cap=PER['ink']['max_tilt'])
    region = (60, 814, 262, 1016)
    s = 1.9
    x = sh.M
    y0 = sh.y
    for i, t in enumerate((.10, .22, .36, .52, .80, 1.40)):
        _, th_h, a_h, p_h = sample(runs['hp'], t)
        _, th_i, a_i, p_i = sample(runs['ink'], t)
        dst = flat(region, s, SKY)
        draw_cluster(dst, region, s, hp_state(.62, tilt=th_h, wave=a_h, phase=p_h + .7),
                     ink_state(.44, tilt=th_i, wave=a_i, phase=p_i + 2.1), marks=(None, None, None))
        im = save_frame(dst, 'c_slosh_%d_t%03d.png' % (i + 1, int(round(t * 100))))
        sh.put(im, x)
        tag = '(회전 중)' if t < .22 else ('(회전 끝)' if t == .22 else '')
        sh.cap(x + 4, y0 + im.height + 6, 't = %.2f s %s' % (t, tag), 21)
        sh.cap(x + 4, y0 + im.height + 36, '기울기 체력 %+.3f · 먹 %+.3f' % (th_h, th_i), 19, fill=ASH8, weight='Regular')
        rep['slosh'].append(dict(t=t, hp_tilt=round(th_h, 4), ink_tilt=round(th_i, 4), hp_wave_px=round(a_h, 2), ink_wave_px=round(a_i, 2)))
        x += im.width + 3
    sh.y = y0 + 384 + 70
    sh.cap(sh.M, sh.y, '① 빠른 시점 회전(초당 420°로 0.22초) 뒤. 체력은 가볍게 여러 번 넘실거리고(1.8 Hz · ζ .28), 먹은 느리고 끈적하게 한 번에 돌아온다(1.1 Hz · ζ .55).', 21)
    sh.y += 46

    # --- 2 ink spent (drain + wet film) and 3 ink gained (pouring thread)
    y0 = sh.y
    x = sh.M
    rep['drain'] = []
    for i, t in enumerate((-1, .04, .12, .40, 2.2, 4.2)):
        if t < 0:
            st = ink_state(.62)
            lab, sub = '쓰기 전', '수위 62 %'
        else:
            lv = drain_level('ink', .62, .47, t)
            fa, fd = film('ink', t)
            gone = t >= PER['ink']['wet_hold'] + PER['ink']['wet_dry']
            st = ink_state(lv, stain_top=.62, stain_a=0.0 if gone else fa, stain_dry=fd, wave=1.2 * math.exp(-t / .45), phase=4.0 * t)
            lab = 't = %.2f s' % t
            sub = '수위 %.0f %% · %s' % (lv * 100, '젖은 자국' if fd == 0 else ('다 마름' if gone else '마름 %.0f %%' % (fd * 100)))
            rep['drain'].append(dict(t=t, level=round(lv, 3), film_alpha=0.0 if gone else round(fa, 3), film_dry=round(fd, 2)))
        im = save_frame(on_bg(render_vessel('ink', st, s=2), SKY), 'c_ink_spent_%d.png' % (i + 1))
        sh.put(im, x)
        sh.cap(x + 4, y0 + im.height + 6, lab, 20)
        sh.cap(x + 4, y0 + im.height + 34, sub, 18, fill=ASH8, weight='Regular')
        x += im.width + 4
    x_fill = x + 36
    x = x_fill
    rep['fill'] = []
    t_stop = PER['ink']['fill_tau'] * math.log(.30 / POUR_MIN_DELTA)
    for i, t in enumerate((.04, .25, .60, 1.40)):
        lv = fill_level('ink', .32, .62, t)
        pouring = (.62 - lv) > POUR_MIN_DELTA
        fresh = 1.0 if pouring else max(0.0, 1 - (t - t_stop) / RECEIVED_S)
        st = ink_state(lv, fresh_from=.32, fresh_a=fresh, thread=1.0 if pouring else 0.0, wave=1.2 if pouring else .3, phase=4.0 * t + 1.0)
        im = save_frame(on_bg(render_vessel('ink', st, s=2), SKY), 'c_ink_gain_%d.png' % (i + 1))
        sh.put(im, x)
        sh.cap(x + 4, y0 + im.height + 6, 't = %.2f s' % t, 20)
        sh.cap(x + 4, y0 + im.height + 34, '수위 %.0f %% · %s' % (lv * 100, '붓는 실' if pouring else '섞임'), 18, fill=ASH8, weight='Regular')
        rep['fill'].append(dict(t=t, level=round(lv, 3), thread=bool(pouring)))
        x += im.width + 4
    rep['fill_thread_stops_at_s'] = round(t_stop, 2)
    sh.y = y0 + 284 + 66
    sh.cap(sh.M, sh.y, '② 먹을 쓴 순간(62 → 47 %, 술식 한 번). 수면은 0.2초쯤에 내려가고, 잃은 몫은\n유리에 젖은 자국으로 남았다가 4초에 걸쳐 위에서부터 마른다(잔먹 3–5초와 같은 수명).', 21)
    sh.cap(x_fill, sh.y, '③ 먹 덩어리를 뽑은 순간(32 → 62 %). 목에서 붓는 실이\n내려오고, 새로 든 몫은 잠깐 옅다(담묵).', 21)
    sh.y += 78

    # --- 4 HP hit and 5 annotated close-up
    y0 = sh.y
    x = sh.M
    rep['hit'] = []
    run = spring_run('hp', lambda tt: 0.0, 1.0, kick=1.4, kick_wave=2.0)
    for i, t in enumerate((-1, .04, .20, .45, .65, .85)):
        if t < 0:
            st = hp_state(.80)
            lab, sub = '맞기 전', '수위 80 %'
        else:
            lv = drain_level('hp', .80, .50, t)
            fa, fd = film('hp', t)
            gone = t >= PER['hp']['wet_hold'] + PER['hp']['wet_dry']
            _, th, aw, ph = sample(run, t)
            st = hp_state(lv, stain_top=.80, stain_a=0.0 if gone else fa, stain_dry=fd, tilt=th, wave=aw, phase=ph)
            lab = 't = %.2f s' % t
            sub = '수위 %.0f %% · %s' % (lv * 100, '젖은 자국' if fd == 0 else ('다 마름' if gone else '마름 %.0f %%' % (fd * 100)))
            rep['hit'].append(dict(t=t, level=round(lv, 3), film_alpha=0.0 if gone else round(fa, 3), film_dry=round(fd, 2)))
        im = save_frame(on_bg(render_vessel('hp', st, s=2), SKY), 'c_hp_hit_%d.png' % (i + 1))
        sh.put(im, x)
        sh.cap(x + 4, y0 + im.height + 6, lab, 20)
        sh.cap(x + 4, y0 + im.height + 34, sub, 18, fill=ASH8, weight='Regular')
        x += im.width + 4
    zx = x + 30
    st = ink_state(.50, tilt=.045, wave=1.0, phase=.9, stain_top=.66, stain_a=.30, stain_dry=.25)
    v = render_vessel('ink', st, s=5, ss=2)
    crop = to_img(on_bg(v, hexc('#8E8B80'))).crop((0, 205, v.shape[1], 205 + 320))
    save_frame(crop, 'c_closeup_500.png')
    sh.put(crop, zx)
    notes = [((97, 62), (150, 10), '빛 획: 한지색 값, 고정'), ((300, 134), (182, 54), '젖은 자국: 붓털 결, 위에서부터 마른다'),
             ((236, 171), (8, 212), '수면 뒤쪽 반(옅은 렌즈) — 시안 추가'), ((452, 190), (196, 250), '수면선(한지색) + 벽을 타는 메니스커스'),
             ((62, 262), (150, 288), '고임 테: 벽 쪽이 짙다')]
    f19 = font(19, 'Bold')
    for (px, py), (tx, ty), text in notes:
        tw = sh.dr.textlength(text, font=f19)
        ex = tx + tw + 6 if px > tx + tw / 2 else tx - 4          # the leader lands on the nearer end of its label
        sh.dr.line([zx + px, y0 + py, zx + ex, y0 + ty + 13], fill=PAPER8, width=2)
        sh.dr.ellipse([zx + px - 4, y0 + py - 4, zx + px + 4, y0 + py + 4], fill=PAPER8, outline=INK8)
        sh.dr.rectangle([zx + tx - 4, y0 + ty - 1, zx + tx + tw + 6, y0 + ty + 27], fill=INK8)
        sh.dr.text((zx + tx + 1, y0 + ty - 1), text, fill=PAPER8, font=f19)
    sh.cap(zx, y0 + 320 + 8, '⑤ 500 % 확대(먹 50 %, 회색 바탕):\n무엇이 액체로 읽히게 하는가', 21)
    sh.y = y0 + 320 + 66
    sh.cap(sh.M, sh.y, '④ 피격(체력 80 → 50 %). 잃은 몫이 주사 자국으로 450 ms 머문 뒤 350 ms에 마른다. 충격으로 수면이 한 번 넘실거린다.', 21)
    sh.y += 40
    sh.rule()


def mark_tile(kind, state, fill, bgc, s, size=None):
    n = int(round(44 * s))
    mp = int(round(MPAD * s))
    size = size or n + 2 * mp
    c = np.ones((size, size, 3)) * bgc
    if kind is not None:
        paste(c, render_mark(kind, state, fill, s=s), (size - n) // 2 - mp, (size - n) // 2 - mp)
    return c


def dashed_box(im, t_, ox, col):
    d_ = ImageDraw.Draw(im)
    for q in range(8, t_ - 8, 12):
        d_.line([ox + q, 8, ox + q + 6, 8], fill=col, width=1)
        d_.line([ox + q, t_ - 9, ox + q + 6, t_ - 9], fill=col, width=1)
        d_.line([ox + 8, q, ox + 8, q + 6], fill=col, width=1)
        d_.line([ox + t_ - 9, q, ox + t_ - 9, q + 6], fill=col, width=1)


def panel_d(sh, rep):
    sh.head('라. 표시 셋 — 모든 상태 (회피 · 점프 · 자동차)',
            '젖음(농묵) = 지금 쓸 수 있다  ·  마름(갈필, 옅음) = 지금 못 쓴다  ·  되젖음 = 돌아오는 중(그은 방향으로 다시 젖는다)  ·  잠긴 기능은 그리지 않는다(자리는 비워 둔다).\n'
            '각 칸: 왼쪽 300 % 한지색 / 오른쪽 300 % 어두운 바탕 / 아래 100 % 실제 크기. 글자와 건반은 붙이지 않았다(기본값). 깜빡임 · 맥동 없음, 주사 없음.')
    rows = [
        ('회피', '옆으로 비켜 달리는 몸 +\n뒤로 갈라지는 갈필', [
            ('dodge', 'wet', 1.0, '준비', '젖음'),
            ('dodge', 'rewet', .25, '식는 중 0.15 s', '냉각 .6초의 25 %'),
            ('dodge', 'rewet', .75, '식는 중 0.45 s', '75 %'),
            ('dodge', 'dry', 1.0, '못 씀', '작도 · 공중 · 앉음 · 탑승'),
            (None, '', 0, '숨김', '회피가 없는 장면')]),
        ('점프', '땅 획 위로 뜬 몸 +\n발밑 갈필', [
            ('jump', 'wet', 1.0, '준비', '젖음'),
            ('jump', 'dry', 1.0, '공중', '뛰는 프레임에 마름'),
            ('jump', 'rewet', .5, '착지 뒤 60 ms', '120 ms에 다 젖음'),
            ('jump', 'dry', 1.0, '못 씀', '웅크림 · 작도 · 탑승'),
            (None, '', 0, '숨김', '이동 프로필 없는 장면')]),
        ('자동차', '지붕 · 몸 · 바퀴.\n회수 = 되감는 갈고리', [
            (None, '', 0, '숨김', '첫 의뢰 전'),
            ('vehicle', 'wet', 1.0, '부를 수 있음', 'G = 소환'),
            ('vehicle_out', 'wet', 1.0, '나와 있고 가까움', 'G = 회수(20 m 안)'),
            ('vehicle', 'dry', 1.0, '받지 않음', '보스 필드: G 무동작'),
            ('vehicle', 'rewet', .4, '바쁨', '호출 획 진행 40 %'),
            ('vehicle_out', 'dry', 1.0, '탑승 중', '나와 있음 기호, 마름')]),
    ]
    big = 3
    tile = (44 + 2 * MPAD) * big
    cw = tile * 2 + 2
    lab_w = 300
    gap = 20
    for name, desc, cells in rows:
        y0 = sh.y
        sh.dr.text((sh.M, y0 + 20), name, fill=INK8, font=font(40, 'Black', serif=True))
        sh.cap(sh.M, y0 + 80, desc, 19, fill=ASH8, weight='Regular')
        x = sh.M + lab_w
        for kind, state, fill, lab, sub in cells:
            both = to_img(np.hstack([mark_tile(kind, state, fill, SKY, big), np.ones((tile, 2, 3)) * SHEET_BG,
                                     mark_tile(kind, state, fill, PINE, big)]))
            sm = to_img(np.hstack([mark_tile(kind, state, fill, SKY, 1, size=60), np.ones((60, 2, 3)) * SHEET_BG,
                                   mark_tile(kind, state, fill, PINE, 1, size=60)]))
            if kind is None:
                for im_, t_ in ((both, tile), (sm, 60)):
                    dashed_box(im_, t_, 0, ASH8)
                    dashed_box(im_, t_, t_ + 2, (120, 118, 110))
            else:
                save_frame(both, 'd_%s_%s_%03d.png' % (kind, state, int(round(fill * 100))))
            sh.put(both, x, y0)
            sh.put(sm, x + (cw - sm.width) // 2, y0 + tile + 6)
            sh.cap(x + cw // 2, y0 + tile + 72, lab, 21, anchor='ma')
            sh.cap(x + cw // 2, y0 + tile + 102, sub, 18, fill=ASH8, weight='Regular', anchor='ma')
            x += cw + gap
        sh.y = y0 + tile + 142
    y0 = sh.y
    sh.cap(sh.M, y0, '군집 안에서 · 150 % (풀밭 바탕: #308 플레이 캡처 ride_seat.png의 왼쪽 아래, 옛 계기는 위쪽 풀밭을 복제해 지웠다)', 21)
    y0 += 38
    ctx = [((('dodge', 'rewet', .4), ('jump', 'dry', 1.0), ('vehicle_out', 'wet', 1.0)), '회피 직후 공중: 회피 되젖는 중 · 점프 마름 · 자동차 회수 가능'),
           ((('dodge', 'wet', 1.0), ('jump', 'wet', 1.0), ('vehicle', 'dry', 1.0)), '보스 필드 안: 자동차 마름 = G를 눌러도 아무 일 없음을 미리 알린다'),
           ((('dodge', 'wet', 1.0), ('jump', 'wet', 1.0), None), '첫 의뢰 전: 자동차 자리는 비어 있다'),
           ((('dodge', 'dry', 1.0), ('jump', 'dry', 1.0), ('vehicle_out', 'dry', 1.0)), '탑승 중: 셋 다 마름')]
    x = sh.M
    for i, (marks, text) in enumerate(ctx):
        im = save_frame(cluster_on('grass', 1.5, marks=marks), 'd_context_%d.png' % (i + 1))
        sh.put(im, x, y0)
        words = text.split(': ')
        sh.cap(x, y0 + im.height + 6, words[0], 20)
        sh.cap(x, y0 + im.height + 34, words[1], 18, fill=ASH8, weight='Regular')
        x += im.width + 18
    sh.cap(x + 6, y0 + 4,
           '읽는 법\n'
           '· 기호가 진하면 그 키는 지금 듣는다.\n'
           '· 옅고 갈라졌으면 지금은 듣지 않는다.\n'
           '· 반쯤 진하면 곧 돌아온다(회피 .6초).\n'
           '· 지붕 달린 수레 = G가 부른다.\n'
           '· 되감는 갈고리 = G가 거둔다.\n'
           '· 자리가 비었으면 아직 없는 기능.', 20, fill=ASH8, weight='Regular')
    sh.y = y0 + 393 + 70
    sh.rule()


def panel_e(sh, rep):
    sh.head('마. 임팩트 프레임 반응 (D308-10b) — 평소 장면 옆에',
            '술식이 맞는 점은 화면 가운데 쪽(군집에서 보면 오른쪽 위). 그쪽 유리 선과 테는 한지색으로, 반대쪽은 먹 그림자로 눌린다. 수위와 색은 그대로라 체력 · 먹이 계속 읽힌다.\n'
            '빛은 한지색 값(230)까지만 간다. 발광 · HDR · 블룸 없음. 1–3장(약 42 ms씩)만 유지되고 초당 3회를 넘지 않는다. 세계 쪽 장면은 SPEC-SPELL-DEPLOY-308 §8의 글을 흉내 낸 배경이다(그쪽 시안이 아니다).')

    def states(t):
        if t is None:
            return HP_N, INK_N
        out = []
        for kind, lv, ph0 in (('hp', .62, .7), ('ink', .44, 2.1)):
            run = spring_run(kind, lambda tt: 0.0, .3, kick=PER[kind]['kick_tilt'] * IMPACT['gain'], kick_wave=PER[kind]['kick_wave'])
            _, th, aw, ph = sample(run, t)
            out.append((hp_state if kind == 'hp' else ink_state)(lv, tilt=th, wave=aw, phase=ph + ph0))
        return out

    cells = [('still', 'normal', None, None, '평소 장면'),
             ('still', 'f1', IMPACT_PT, .010, '임팩트 1장(반전): 밝은 곳은 먹, 어두운 곳은 한지색'),
             ('still', 'f2', IMPACT_PT, .052, '임팩트 2장(획 실루엣): 밝은 곳은 한지 한 값'),
             ('paper', 'normal', IMPACT_PT, .052, '한지색 바탕에서 HUD만(세계 효과 없이)')]
    x = sh.M
    y0 = sh.y
    mx = []
    for i, (bgk, mode, pt, t, lab) in enumerate(cells):
        hp, ink = states(t)
        arr = cluster_on(bgk, 2, hp=hp, ink=ink, impact_pt=pt, mode=mode)
        if pt is not None:
            only = flat(CROP, 2, np.zeros(3))
            draw_cluster(only, CROP, 2, hp, ink, DEFAULT_MARKS, impact_pt=pt)
            mx.append(int(round(float(only.max()) * 255)))
        im = save_frame(arr, 'e_impact_%d_%s_%s.png' % (i + 1, bgk, mode))
        sh.put(im, x)
        sh.cap(x, y0 + im.height + 8, lab, 20)
        x += im.width + 16
    rep['impact_max_channel_on_black'] = mx
    sh.y = y0 + 524 + 46
    y0 = sh.y
    full = still_region(FULL, 1, mode='f1')
    hp, ink = states(.010)
    draw_cluster(full, FULL, 1, hp, ink, DEFAULT_MARKS, impact_pt=IMPACT_PT)
    fim = save_frame(full, 'e_fullframe_impact1.png').resize((800, 450), Image.LANCZOS)
    sh.put(fim, sh.M, y0)
    k = 800 / 1920.0
    sh.dr.ellipse([sh.M + IMPACT_PT[0] * k - 10, y0 + IMPACT_PT[1] * k - 10, sh.M + IMPACT_PT[0] * k + 10, y0 + IMPACT_PT[1] * k + 10], outline=CIN8, width=3)
    sh.dr.rectangle([sh.M + 44 * k, y0 + 766 * k, sh.M + 336 * k, y0 + 1030 * k], outline=CIN8, width=2)
    sh.cap(sh.M + 824, y0 + 2,
           '임팩트 1장의 화면 전체(줄인 것). 주사 동그라미 = 술식이 맞은 점, 주사 네모 = 군집.\n'
           '\n'
           '· 방향: 요소마다 자기 중심에서 맞은 점을 향한 쪽이 "빛 쪽"이다(용기 둘과 표시 셋이 조금씩 다른 각도).\n'
           '· 유리 선: 빛 쪽은 한지색, 반대쪽은 먹 그대로. 바깥 한지 테: 반대쪽이 먹 쪽으로 60 %% 어두워진다.\n'
           '· 안쪽 그림자: 반대쪽 벽 9 px 안에 먹 초승달(α .5). 액체: 빛 쪽으로 한지를 최대 10 %% 섞는다.\n'
           '· 액체가 차인다: 맞은 점의 반대쪽으로 한 번 쏠렸다가 1.5초 안에 멎는다(수위는 그대로).\n'
           '· 군집 픽셀의 최대 채널(검은 바탕에서 잰 값): %s — 한지색 230을 넘지 않는다.\n'
           '· 봐 달라는 곳: 1장에서 땅이 먹색이 되면 먹 용기의 먹과 바탕이 같은 값이 된다. 수면선 · 빈 유리의 한지 담채 ·\n'
           '   빛 쪽 한지 선으로 먹 수위가 읽히는가(위 둘째 칸). 모자라면 1장 동안 먹·한지 값을 맞바꾸는\n'
           '   SPEC-SPELL-DEPLOY-308 §9의 방식을 용기에도 쓴다(두 Spec의 계약을 맞출 때 정한다).' % ' · '.join(str(v) for v in mx),
           20, fill=ASH8, weight='Regular')
    sh.y = y0 + 450 + 10
    sh.rule()


def panel_f(sh, rep):
    ref = Image.open(REF_PNG).convert('RGB')
    orb_w, orb_h = 67.0, 65.0                    # the Spec's measurement of the red orb
    orb_cy = 51.5
    g = vessel_geo(114, 150, 1, 4)
    sh.head('바. 레퍼런스와 같은 높이로 견주기 — "살짝 길고 좁게"',
            '레퍼런스의 붉은 구슬과 체력 용기의 위 · 아래 끝을 같은 높이에 맞췄다(주사 점선). 구슬은 높이 ÷ 폭 = %.2f, 알병은 %.3f(몸통만 %.2f)이다.\n'
            '같은 높이에서 폭이 약 %d %% 좁다. 배치는 레퍼런스와 같은 대각선이다: 큰 것이 왼쪽 위, 작은 것이 오른쪽 아래에서 맞닿고, 작은 표시가 오른쪽 위로 돈다.'
            % (orb_h / orb_w, g.sil_h / g.sil_w, g.body_h / g.body_w, round((1 - (g.sil_w / g.sil_h) / (orb_w / orb_h)) * 100)))
    s = 2.0
    k = g.sil_h * s / orb_h                      # reference scale so that the orb is as tall as the vessel
    rw, rh = int(round(ref.width * k)), int(round(ref.height * k))
    y0 = sh.y
    sh.put(ref.resize((rw, rh), Image.BICUBIC), sh.M)
    top_px = (orb_cy - orb_h / 2) * k
    bot_px = (orb_cy + orb_h / 2) * k
    ry0 = RECT['hp'][1] + g.sil_top - top_px / s
    region = (44, ry0, 44 + 290, ry0 + rh / s)
    ours_im = save_frame(cluster_on('flat', s, region=region, bg_col=hexc('#201F1C')), 'f_ours_same_height.png')
    x2 = sh.M + rw + 16
    sh.put(ours_im, x2)

    def overlay(wd, ht, vw, neck, sc, title, body_aspect=None):
        """Vessel outline at scale sc with the reference orb (dashed) at the same height."""
        gg = vessel_geo(vw, 150, sc, 3, neck=neck, body_aspect=body_aspect)
        v = render_vessel('hp', VState(0.0), s=sc, ss=3, spec_only=True, w=vw, h=150, neck=neck, body_aspect=body_aspect)
        im = Image.new('RGB', (wd, ht), c8(SKY))
        vx = (wd - v.shape[1]) // 2
        vy = (ht - v.shape[0]) // 2 + 6
        im.paste(to_img(on_bg(v, SKY)), (vx, vy))
        d_ = ImageDraw.Draw(im)
        ccx = vx + (gg.cx + PAD) * sc
        t_ = vy + (gg.sil_top + PAD) * sc
        b_ = vy + (gg.sil_bottom + PAD) * sc
        hh = (b_ - t_) / 2
        hw_ = hh * orb_w / orb_h
        for a in range(0, 360, 6):
            a0, a1 = math.radians(a), math.radians(a + 3.4)
            d_.line([ccx + hw_ * math.cos(a0), t_ + hh + hh * math.sin(a0), ccx + hw_ * math.cos(a1), t_ + hh + hh * math.sin(a1)],
                    fill=CIN8, width=max(2, int(round(sc * 1.4))))
        d_.text((10, 6), title, fill=INK8, font=font(20, 'Bold'))
        return im, gg, vy + (gg.sil_top + PAD) * sc

    ow = 480
    big, gb, _ = overlay(ow, rh, 114, True, s, '겹쳐 보기 · 이 시안(B 알병)', body_aspect=SHAPE['body_aspect'])
    # shift so the vessel sits on the same guide lines as the other two pictures
    vd = ImageDraw.Draw(big)
    vd.text((10, rh - 92), '주사 점선 = 레퍼런스 구슬 (같은 높이)', fill=CIN8, font=font(19, 'Bold'))
    vd.text((10, rh - 62), '먹 선 = 체력 알병', fill=INK8, font=font(19, 'Bold'))
    vd.text((10, rh - 32), '폭: 구슬 %.0f px → 알병 %.0f px' % (orb_w * k, g.sil_w * s), fill=ASH8, font=font(19, 'Bold'))
    save_frame(big, 'f_outline_overlay.png')
    x3 = x2 + ours_im.width + 16
    sh.put(big, x3)
    for yy in (top_px, bot_px):                  # same-height guides across the reference and the mock
        for xx in range(sh.M, x2 + ours_im.width, 18):
            sh.dr.line([xx, y0 + yy, xx + 9, y0 + yy], fill=CIN8, width=2)
    # the Spec's four silhouette options, each against the orb at the same height
    x4 = x3 + ow + 16
    mw = (sh.W - sh.M - x4 - 8) // 2
    mh = (rh - 8) // 2
    rep['silhouette_options'] = {}
    for i, (name, vw, neck) in enumerate((('A 둥근 병', 128, True), ('B 알병 ← 이 시안', 114, True), ('C 긴 병', 104, True), ('D 달걀(목 없음)', 116, False))):
        gg = vessel_geo(vw, 150, 1.2, 3, neck=neck, body_aspect=None)
        ratio = gg.sil_h / gg.sil_w
        im, _, _ = overlay(mw, mh, vw, neck, 1.2, '%s  %.2f' % (name, ratio))
        save_frame(im, 'f_option_%s.png' % 'ABCD'[i])
        sh.put(im, x4 + (i % 2) * (mw + 8), y0 + (i // 2) * (mh + 8))
        rep['silhouette_options']['ABCD'[i]] = dict(h_over_w=round(ratio, 3),
                                                    narrower_than_orb=round(1 - (gg.sil_w / gg.sil_h) / (orb_w / orb_h), 3))
    sh.cap(sh.M, y0 + rh + 8, '레퍼런스(%.1f배로 키운 것)' % k, 21)
    sh.cap(x2, y0 + rh + 8, '시안 · 200 % · 어두운 바탕', 21)
    sh.cap(x3, y0 + rh + 8, '실루엣 겹쳐 보기 · 200 %', 21)
    sh.cap(x4, y0 + rh + 8, 'Spec의 모양 선택지 넷(숫자 = 높이 ÷ 폭).\n크기도 자료 한 칸(1.0배 = 이 시안, 1.25배 = 크게).', 19, fill=ASH8, weight='Regular')
    rep['reference'] = dict(orb_h_over_w=round(orb_h / orb_w, 3), vessel_h_over_w=round(g.sil_h / g.sil_w, 3),
                            body_h_over_w=round(g.body_h / g.body_w, 3),
                            narrower_at_same_height=round(1 - (g.sil_w / g.sil_h) / (orb_w / orb_h), 3), ref_scale=round(k, 3))
    sh.y = y0 + rh + 70
    sh.rule()


def footer(sh, rep):
    sh.head('시안이 Spec(SPEC-HUD-LIQUID-308)과 다른 곳',
            '아래 그림: 짝마다 왼쪽 = Spec 값 그대로(렌즈 · 유리 두께 · 굵기 변화 · 농담 없음), 오른쪽 = 이 시안의 손질(아래 1–4번). 200 %.')
    hp_s = hp_state(.62, tilt=.05, wave=1.2, phase=.7, stain_top=.80, stain_a=.34)
    ink_s = ink_state(.44, tilt=-.03, wave=.6, phase=2.1, stain_top=.59, stain_a=.30)
    x = sh.M
    y0 = sh.y
    for bgc, bname in ((SKY, 'paper'), (PINE, 'dark')):
        for kind, st in (('hp', hp_s), ('ink', ink_s)):
            for spec_only in (True, False):
                im = save_frame(on_bg(render_vessel(kind, st, s=2, spec_only=spec_only), bgc),
                                'g_compare_%s_%s_%s.png' % (kind, 'spec' if spec_only else 'mock', bname))
                sh.put(im, x, y0 + (320 - im.height))
                sh.cap(x + im.width // 2, y0 + 326, 'Spec 그대로' if spec_only else '시안', 19,
                       fill=ASH8 if spec_only else INK8, anchor='ma')
                x += im.width + (6 if spec_only else 26)
    sh.y = y0 + 320 + 46
    lines = [
        '1. 수면 "렌즈": 수면선 위에 옅은 액체색의 얇은 타원(체력 4.2 px · 먹 3.6 px)을 더했다. 민 막대가 아니라 병 속 액체로 읽히게 하려는 것이다. 값은 그대로 아래쪽 선(수면선)에서 읽는다.',
        '2. 유리 두께: 먹 선과 액체 사이 1.4 px에 한지색 α .34를 넣었다(Spec은 담채 .14뿐). 어두운 바탕에서 먹 용기의 먹과 먹 선이 붙어 한 덩어리가 되는 것을 막는다.',
        '3. 먹 선의 굵기: 2.6 px 고정이 아니라 붓 압처럼 ±28 % 변한다(왼쪽 아래가 굵다). 빛 획도 하나 더(오른쪽 아래, α .26). 벡터 그림 같던 인상을 줄이려는 것이다.',
        '4. 액체 몸: 바닥 쪽이 12 % 짙고(가라앉은 먹), 옅은 농담 얼룩(±5 %)이 있다. Spec의 고임 테만으로는 평평해 보였다. 광택 그라데이션이 아니라 농담이다.',
        '5. 먹 모자람: 세로 줄 대신 가로 갈필 자국(바닥에 남은 붓 자국)으로 그렸다. 세로 줄은 바코드처럼 보였다. 덮임 55 %는 그대로다.',
        '6. 젖은 자국: 고른 세로 줄 대신 붓털 결 + 고르지 않은 윗선 + 갈라지며 마르는 가장자리로 그렸다. 알파 · 머묾 · 마름 시간은 Spec 그대로다.',
        '7. 표시 기호: Spec의 추상 획(점 + 꼬리, 고리 + 지붕) 대신 그림 기호로 다시 그렸다 — 달리는 몸, 뛰어오른 몸, 지붕 달린 수레. 글자 없이 읽혀야 해서다. 문법(먹 기호 + 한지 테, 젖음 · 마름 · 되젖음)은 그대로다.',
        '8. 자동차 "나와 있음" 기호: 열린 바퀴 대신 작은 수레 + 되감는 갈고리 화살. 44 px에서 "거둔다"가 더 잘 읽혔다.',
        '9. 임팩트 반응: 빛 쪽 빈 유리에 한지 담채 +.20을 더했다(1장의 검은 세계에서 병이 사라지지 않게). 값 맞바꿈은 넣지 않았다 — SPEC-SPELL-DEPLOY-308 §9와 맞춰야 할 곳이다.',
        '10. 배경 그림: 지시된 Art/Playtest308/Vehicle/call_stroke.png는 없다. 같은 묶음의 캡처는 옛 계기가 찍혀 있고 탑승 · 회수 도중이라, 가 · 마 패널은 HUD 없는 캡처 road.png를 썼다. 풀밭(라)만 ride_seat.png다.',
        '11. Spec에 숫자가 없어 시안에서 정한 값: 물결 들뜸(기울기 속도 1당 체력 9 px · 먹 4.5 px), 피격 때의 기울기 충격(1.4/s). 용기 사각형 둘레에 5 px 여백을 줘 한지 테가 잘리지 않게 했다.',
    ]
    for ln in lines:
        sh.cap(sh.M + 28, sh.y, ln, 20, fill=INK8, weight='Regular')
        sh.y += 33
    sh.y += 14
    sh.head('검토 뒤(2026-10-04) 스테이지에 옮긴 것 · 아직 시안에만 있는 것')
    lines = [
        '· 스테이지 셰이더 · 아틀라스에 옮겼다: 1(수면 렌즈) · 2(유리 두께) · 3(굵기 변화, 둘째 빛 획) · 4의 바닥 농담 · 7 · 8(그림 기호 — 아틀라스가 이 시트의 기호를 그대로 쓴다).',
        '· 아직 시안에만 있다: 4의 농담 얼룩 · 5(가로 갈필 — 스테이지는 세로 결) · 6(붓털 결의 고르지 않은 윗선 — 스테이지는 고른 결) · 9(빛 쪽 한지 담채). 스테이지의 실제 그림은 twin_states_v1.png다.',
        '· 표시의 한지 테는 44 px 사각형 밖 4 px까지 그려진다(잘리지 않게). 군집 사각형 범위는 그대로다.',
    ]
    for ln in lines:
        sh.cap(sh.M + 28, sh.y, ln, 20, fill=INK8, weight='Regular')
        sh.y += 33
    sh.y += 14
    sh.head('시안을 그리며 드러난 것')
    pk = rep.get('slosh_peak_tilt', {})
    lines = [
        '· 출렁임이 목표 상한을 넘는다: 체력 기울기가 %.3f까지 간다(목표 상한 %.2f). 스프링의 넘침이다. 스테이지는 기울기 자체를 목표 상한의 1.5배(체력 .33 · 먹 .24)에서 자른다(자료 한 칸).' % (pk.get('hp', 0), pk.get('hp_target_cap', 0)),
        '· 마른 표시는 어두운 바탕에서 한지 테(α .92) 때문에 여전히 밝은 덩어리다. "못 씀"이 "준비"보다 덜 눈에 띄게 하려면 마름일 때 테도 옅게 하는 선택지가 있다.',
        '· 100 % 크기(44 px)에서 자동차 회수 기호는 빽빽하다. 최종 기호는 사람 손(또는 Recraft)으로 다시 그리는 편이 낫다. 이 시안의 기호는 절차 생성 초안이다.',
        '· 먹 0 %와 먹 8 %(갈필)의 차이가 100 % 크기에서 작다. 먹 모자람은 표시보다 "술식이 안 나간다"는 사실 쪽이 먼저 느껴질 수 있다.',
    ]
    for ln in lines:
        sh.cap(sh.M + 28, sh.y, ln, 20, fill=INK8, weight='Regular')
        sh.y += 33
    sh.y += 10
    sh.cap(sh.M + 28, sh.y, '이 시트는 오프라인 그림이다(numpy + PIL, sRGB 합성). Unity 캡처가 아니며, 색 · 선 굵기 · 알파는 선형 색 공간에서 다시 맞춰야 한다. 상태: 시안(TEST) — IMPLEMENTED 아님.',
           20, fill=CIN8)
    sh.y += 36


def build():
    os.makedirs(FRAMES, exist_ok=True)
    rep = dict(source='Tools/Art/hud308_mock.py',
               stills=['Art/UI304/clean/road.png (HUD-less capture; panels A and E)',
                       'Art/Playtest308/Vehicle/ride_seat.png (old meters cloned out; panel D context)'],
               note='offline mock, sRGB compositing; not a game capture and not the shader twin')
    sh = Sheet()
    sh.title('오행부 HUD 군집 시안 v1 — 체력 · 먹 알병 + 회피 · 점프 · 자동차',
             ['SPEC-HUD-LIQUID-308 · DECISIONS D308-11 · 2026-10-04 · 오프라인 렌더(게임 캡처가 아니다) · 상태: 시안(TEST)',
              '요청: "체력 및 먹 잔량 HUD는 레퍼런스보다 살짝 길고 좁은 형태로. 실제 먹물이 들어있는 거 같은 액체효과. 임팩트 프레임, 잔먹 잔류까지 적용."'])
    panel_a(sh, rep)
    panel_b(sh, rep)
    panel_c(sh, rep)
    panel_d(sh, rep)
    panel_e(sh, rep)
    panel_f(sh, rep)
    footer(sh, rep)
    sh.finish(os.path.join(OUT, 'hud308_mock_sheet.png'))
    rep['sheet'] = dict(path='Art/UI308/HUD/hud308_mock_sheet.png', size=[sh.W, sh.y + 24])
    with open(os.path.join(OUT, 'hud308_mock_report.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(rep, f, ensure_ascii=False, indent=1)
    print(json.dumps(rep, ensure_ascii=False, indent=1))


def probe():
    scratch = os.path.join(FRAMES, '_probe')
    os.makedirs(scratch, exist_ok=True)
    rows = []
    for bgc in (SKY, PINE, hexc('#8A877C')):
        cells = []
        for kind in ('dodge', 'jump', 'vehicle', 'vehicle_out'):
            for state, fill in (('wet', 1), ('rewet', .5), ('dry', 1)):
                c = np.ones((240, 330, 3)) * bgc
                paste(c, render_mark(kind, state, fill, s=5), 6, 10)
                paste(c, render_mark(kind, state, fill, s=2), 232, 10)
                paste(c, render_mark(kind, state, fill, s=1), 232, 110)
                cells.append(c)
        rows.append(np.hstack(cells))
    to_img(np.vstack(rows)).save(os.path.join(scratch, 'marks.png'))
    rows = []
    for bgc in (SKY, PINE):
        cells = []
        for spec_only in (True, False):
            for kind, st in (('hp', hp_state(.62, tilt=.05, wave=1.2, phase=.7, stain_top=.8, stain_a=.34)),
                             ('ink', ink_state(.44, tilt=-.03, wave=.6, phase=2.1, stain_top=.62, stain_a=.30, stain_dry=.4)),
                             ('ink', ink_state(.08)), ('hp', hp_state(.18))):
                c = np.ones((500, 390, 3)) * bgc
                paste(c, render_vessel(kind, st, s=3, spec_only=spec_only), 6, 6)
                cells.append(c)
        rows.append(np.hstack(cells))
    to_img(np.vstack(rows)).save(os.path.join(scratch, 'vessels.png'))
    for bgk in ('paper', 'still', 'grass'):
        to_img(cluster_on(bgk, 2)).save(os.path.join(scratch, 'cluster_%s_200.png' % bgk))
        to_img(cluster_on(bgk, 1)).save(os.path.join(scratch, 'cluster_%s_100.png' % bgk))
    print('probe written to', scratch)


if __name__ == '__main__':
    if '--probe' in sys.argv:
        probe()
    else:
        build()
