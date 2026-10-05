# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 section 7 - the drawn parts of the bundle (helper of cartography308.py):

    pattern()        map308_pattern.png       256 x 256 RGBA, tileable: R forest dab clusters, G wave marks, B rock strokes, A wash grain
    stroke_atlas()   map308_stroke_atlas.png  1024 x 512 RGBA, 8 rows x 64 px: A ink, R paper underlay (road knockout)
    icons(cell)      map308_icons_L / _S.png  8 x 8 cells of 64 / 32 px: R ink, G paper plate, B second ink, A = max(R, G)

Everything is drawn from construction rules (no traced or downloaded picture). Deterministic: seeded generators only.
The rules are the direction sheet's (map308_mock.py, the look the user was shown); the sheet now reads these files.
The cartography is ink on paper (D308-14); the craft theme of D308-15 is not used here (frames: map308_frames.py).
"""
import math

import numpy as np
from PIL import Image, ImageDraw

import map308_lib as M

# ---------------------------------------------------------------------------------------------------------------------
# pattern (256 x 256, wrap). The four patterns are the direction sheet's (map308_mock.py: dab_cluster, the wave marks, the
# rock hatch, the paper grain), drawn once into a tile. The tile is made for the minimap scale: at Z1 (1.14 m / px) one
# texel is one screen px (terrain.patternPeriodM[1] = 292 m), so a tree dab is 2-4 px wide there; the other bands scale it.
# ---------------------------------------------------------------------------------------------------------------------
PATTERN = dict(size=256, forest_clusters=24, cluster_r=3.5, ripple_rows=24, ripple_waves=12, rock_pitch=9, grain_period=16)


def _hash01(*key):
    """deterministic 0..1 from integers (seeded generator: the same on every run)."""
    return float(np.random.default_rng([M.SEED, 7] + [int(k) for k in key]).random())


def pattern():
    n = PATTERN['size']; ss = 4
    rng = np.random.default_rng([M.SEED, 11])
    # R: tree dots (sumok-jeom). A cluster = two to four short dabs on a crown arc, each a little different (the sheet's
    # dab_cluster); the VALUE of a cluster is its density level: it appears where the forest density passes 1 - level.
    g = PATTERN['forest_clusters']; pitch = n / g; r = PATTERN['cluster_r']
    cl = []
    for gy in range(g):
        for gx in range(g):
            cl.append(((gx + (.5 if gy % 2 else 0) + .5 + (rng.random() - .5) * .7) * pitch, (gy + .5 + (rng.random() - .5) * .7) * pitch,
                       rng.random(), rng.random()))
    order = np.argsort([c[3] for c in cl], kind='stable')
    levels = np.empty(len(cl)); levels[order] = np.linspace(1.0 / len(cl), 1.0, len(cl))          # an even spread of levels
    im = Image.new('L', (n * ss, n * ss), 0); d = ImageDraw.Draw(im)
    for k in np.argsort(levels, kind='stable'):
        cx, cy, q, _ = cl[k]; lv = int(round(levels[k] * 255)); nd = 2 + int(q * 2.999)
        for i in range(nd):
            t_ = (i + .5) / nd - .5
            jx, jy, js = _hash01(k, i, 1), _hash01(k, i, 2), _hash01(k, i, 3)
            s_ = .24 + .16 * js; w_, h_ = r * s_ * 1.3, r * s_ * .78
            x = cx + (t_ * 1.15 + (jx - .5) * .25) * r; y = cy + (abs(t_) * .7 - .22 + (jy - .5) * .3) * r
            for ox in (-n, 0, n):
                for oy in (-n, 0, n):
                    d.ellipse([(x + ox - w_) * ss, (y + oy - h_) * ss, (x + ox + w_) * ss, (y + oy + h_) * ss], fill=lv)
    forest = M.down(np.asarray(im, float) / 255, ss)
    # G: wave marks (mulgyeol): rows of short sine strokes, every row shifted (the sheet's rule; the shader shows them only
    # well inside wide water)
    N = n * ss; yy, xx = np.mgrid[0:N, 0:N]; u = (xx + .5) / ss; v = (yy + .5) / ss
    rp = n / PATTERN['ripple_rows']; wl = n / PATTERN['ripple_waves']
    row = v / rp; ph = (u / wl + np.floor(row) * .375) % 1.0
    off = (row % 1.0 - .5) * rp + 1.6 * np.sin(ph * 2 * math.pi)
    ripple = M.down(np.clip(1.25 - np.abs(off) / .75, 0, 1) * ((ph > .12) & (ph < .62)), ss)
    # B: rock strokes (amjun): short slanted dry strokes on the steepest faces, thick at the head and lifted at the tail
    c = M.Canvas(n, n, ss, wrap=True)
    g = int(round(n / PATTERN['rock_pitch'])); pitch = n / g
    for gy in range(g):
        for gx in range(g):
            if rng.random() < .30: continue
            x = (gx + .5 + (rng.random() - .5) * .8) * pitch; y = (gy + .5 + (rng.random() - .5) * .8) * pitch
            a = math.radians(-50 + (rng.random() - .5) * 30); L = 5.0 + rng.random() * 5.0
            c.stroke([(x, y), (x + L * math.cos(a) * .5, y - L * math.sin(a) * .5), (x + L * math.cos(a), y - L * math.sin(a) * .92)],
                     2.2, w_fn=lambda t: 1.0 - .6 * t)
    rock = c.result()
    rock = rock * np.clip((M.tnoise(n, n, 6, 21, 2) - .22) * 6, 0, 1)                                # dry-brush breaks
    # A: paper grain of the unwalked wash: a fine cloud + short fibres, centred on 0.5 (the wash stays flat: values.washGrainAmp)
    cloud = M.tnoise(n, n, PATTERN['grain_period'], 31, 2) - .5
    c = M.Canvas(n, n, 2, wrap=True)
    for _ in range(260):
        x, y = rng.random() * n, rng.random() * n; a = rng.random() * math.pi; L = 5 + rng.random() * 9
        c.stroke([(x, y), (x + L * math.cos(a), y + L * math.sin(a))], 1.0, step=.5)
    fibre = c.result()
    grain = np.clip(.5 + cloud * .8 + (fibre - fibre.mean()) * .25, 0, 1)
    out = np.dstack([forest, ripple, rock, grain])
    return np.round(np.clip(out, 0, 1) * 255).astype(np.uint8)


# ---------------------------------------------------------------------------------------------------------------------
# stroke atlas (1024 x 512, 8 rows of 64 px; U = along the stroke, tileable; V: row top = the stroke's LEFT side)
# ---------------------------------------------------------------------------------------------------------------------
ROW_PX, ROW_MARGIN, INK_TOP, INK_BOTTOM = 64, 6, 12.0, 52.0      # ink span = 40 px centred on v = 32
ATLAS_ROWS = [  # name, axisV (the texel row that sits on the baked centre line), knockout (R channel), U mode
    dict(row=0, name='wet', ko='젖은 붓', axisV=32.0, knockout=True, uMode='repeat'),
    dict(row=1, name='medium', ko='중묵 붓', axisV=32.0, knockout=True, uMode='repeat'),
    dict(row=2, name='dry', ko='갈필(끊김)', axisV=32.0, knockout=True, uMode='repeat'),
    dict(row=3, name='teeth', ko='톱니 띠', axisV=48.0, knockout=False, uMode='repeat'),
    dict(row=4, name='dryline', ko='갈필 선', axisV=32.0, knockout=False, uMode='repeat'),
    dict(row=5, name='hachure', ko='빗금 단애선', axisV=46.0, knockout=False, uMode='repeat'),
    dict(row=6, name='battlement', ko='성가퀴 선', axisV=32.0, knockout=False, uMode='repeat'),
    dict(row=7, name='bridge', ko='다리', axisV=32.0, knockout=False, uMode='stretch'),
]
SAW = dict(teeth=24, ratio=.56)      # teeth per repeat (pitch = 1.07 x the class width), tooth share of the class width
DRY = dict(core_half=13.0, core_cover=.97, hair=.16, lift=.86, fly=.75, fly_at=8.4)   # dry row: half width of the unbroken core (texels of the 20), its cover,
                                                                 # depth of a hairline split in the core, edge loss in a lift, depth and place (texels off the centre line) of the flying white


def _n1(w, period, seed):
    return M.tnoise(1, w, period, seed, 2)[0]


def _aa(v, k):
    return np.clip(v * k + .5, 0, 1)


def _streak(h, w, seed, pu, pv, ss):
    """dry-brush streak field in 0..1 (long along U, fine across V), tileable in U."""
    gh = max(2, int(round(h / (pv * ss)))); gw = max(2, int(round(w / (pu * ss))))
    g = np.random.default_rng([M.SEED, seed]).random((gh, gw))
    y = np.arange(h) / h * gh; x = np.arange(w) / w * gw
    y0 = np.clip(np.floor(y).astype(int), 0, gh - 1); y1 = np.clip(y0 + 1, 0, gh - 1); fy = y - np.floor(y)
    x0 = np.floor(x).astype(int) % gw; x1 = (x0 + 1) % gw; fx = x - np.floor(x); fx = fx * fx * (3 - 2 * fx)
    return (g[np.ix_(y0, x0)] * (1 - fx)[None] + g[np.ix_(y0, x1)] * fx[None]) * (1 - fy)[:, None] + \
           (g[np.ix_(y1, x0)] * (1 - fx)[None] + g[np.ix_(y1, x1)] * fx[None]) * fy[:, None]


def stroke_atlas():
    W, ss = 1024, 2
    w, h = W * ss, ROW_PX * ss
    u = (np.arange(w) + .5) / ss; v = (np.arange(h) + .5) / ss
    U, V = np.meshgrid(u, v)
    ink = np.zeros((8, h, w)); ko = np.zeros((8, h, w))
    band_ko = _aa(np.minimum(V - ROW_MARGIN, ROW_PX - ROW_MARGIN - V), 1.0)          # paper underlay: the whole content band

    def band(top, bot, wob_t, wob_b, seed):
        et = top + wob_t * (_n1(w, 96, seed) - .5) * 2; eb = bot - wob_b * (_n1(w, 80, seed + 1) - .5) * 2
        return _aa(np.minimum(V - et[None], eb[None] - V), 1.0)

    # 0 wet brush: an even, dense stroke; the edge breathes a little
    ink[0] = band(INK_TOP - .8, INK_BOTTOM + .8, .8, .8, 100) * (.95 + .05 * M.tnoise(h, w, 48, 101, 2)); ko[0] = band_ko
    # 1 medium brush: more edge movement and a few dry streaks (bibaek)
    s = _streak(h, w, 111, 90, 2.4, ss)
    ink[1] = band(INK_TOP - 1.4, INK_BOTTOM + 1.4, 1.4, 1.4, 110) * (1 - .40 * _aa(s - .86, 14)); ko[1] = band_ko
    # 2 dry brush (the footpath). Dry-brush character without a pale centre line: the CORE of the stroke (the middle 65 % of
    # the ink span, cover .97, only shallow hairline splits) is never broken; the dry streaks (bibaek) run in the outer part, and at two places per
    # period the brush lifts - the edges fray away and the stroke narrows to its core, then picks up again. The stroke
    # never fades: what changes along it is its width and its edge, not the ink on its centre line.
    # History: the first row (three breaks, mean cover .63) gave 32 % of the trail centre-line samples >= 4.5:1; the second
    # (two pale places, cover min .55) 93-95 %: every failing sample sat in a pale place (map308_twin road_centre_line).
    s = _streak(h, w, 121, 70, 2.0, ss)
    env = np.ones(w)
    for c0, half in ((230.0, 26.0), (700.0, 34.0)):
        env = np.minimum(env, np.clip((np.abs(u - c0) - half * .35) / (half * .65), 0, 1))
    env = env * env * (3 - 2 * env)
    dist = np.abs(V - 32.0)                                                          # texels from the centre line
    core = _aa(DRY['core_half'] + .8 * (_n1(w, 64, 122)[None] - .5) - dist, .7)        # 1 inside the core
    lift = (1 - env)[None] * _aa(dist - (DRY['core_half'] + 1.5), .5)                  # the frayed edge of a lift
    thr = .66 - .20 * (1 - env)                                                      # more streaks towards a lift
    outer = (1 - .82 * _aa(s - thr[None], 12)) * (1 - DRY['lift'] * lift)
    hair = _aa(_streak(h, w, 123, 110, 1.1, ss) - .80, 16)                             # hairline splits inside the core: shallow
    # flying white: one long paper streak BESIDE the centre line (never on it), on alternating sides, away from the lifts.
    # A 2 px trail then thins to one side for a stretch; its other half and the centre line stay dark.
    gate = _aa(_n1(w, 150, 124) - .56, 9) * env; side = np.where(_n1(w, 300, 125) > .5, 1.0, -1.0)
    fly = gate[None] * _aa(1.5 - np.abs(V - 32.0 - (side * DRY['fly_at'])[None]), 1.1)
    inner = DRY['core_cover'] * (1 - DRY['hair'] * hair) * (1 - DRY['fly'] * fly)
    ink[2] = band(INK_TOP - 1.6, INK_BOTTOM + 1.6, 1.6, 1.6, 120) * (core * inner + (1 - core) * outer); ko[2] = band_ko
    # 3 saw band (sanjulgi) - the direction sheet's rule (saw_band): a thin spine on the axis and IRREGULAR teeth towards the
    # row top (= the stroke's left). Teeth differ in height (0.55 .. 1.45) and lean and their spacing is warped, so the band
    # never reads as a railway symbol. The class width (40 texels) = spine + a mean tooth; the tallest tooth ends at v 6.7.
    T = SAW; pitch = W / T['teeth']; spine = (1 - T['ratio']) / 2 * 40.0; tooth = T['ratio'] * 40.0; axis = ATLAS_ROWS[3]['axisV']
    Sw = u + pitch * .35 * (2 * _n1(w, int(round(pitch * 2.3 * ss)), 130) - 1)
    k = np.floor(Sw / pitch).astype(int) % T['teeth']; ph = Sw / pitch - np.floor(Sw / pitch)
    rng = np.random.default_rng([M.SEED, 131])
    amp = (.55 + .9 * rng.random(T['teeth']))[k]; skew = (.3 + .4 * rng.random(T['teeth']))[k]
    tri = np.where(ph < skew, ph / np.maximum(skew, 1e-3), (1 - ph) / np.maximum(1 - skew, 1e-3))
    top = axis - spine - tooth * tri * amp
    body = _aa(spine + .4 * (_n1(w, 96, 132)[None] - .5) - np.abs(V - axis), 1.0)
    teeth = _aa(V - top[None], 1.0) * (V <= axis)
    s = _streak(h, w, 133, 60, 2.2, ss)
    ink[3] = np.maximum(teeth, body) * (1 - .25 * _aa(s - .80, 10))
    # 4 dry line: a thin continuous dry-brush line (minor ridges, site outlines, distance ticks)
    s = _streak(h, w, 141, 80, 2.2, ss)
    ink[4] = band(INK_TOP - 1.2, INK_BOTTOM + 1.2, 1.2, 1.2, 140) * (1 - .70 * _aa(s - .74, 12))
    # 5 cliff hachure: the crest line at the bottom, short tapering hachures towards the row top (= the low side, baked per stroke)
    pitch = 25.6; kk = np.floor(u / pitch).astype(int) % 40; x = u - np.floor(u / pitch) * pitch
    rng = np.random.default_rng([M.SEED, 150])
    tip_v = (INK_TOP + rng.random(40) * 7.0)[kk]; lean = (4.0 + rng.random(40) * 4.0)[kk]; cx = (9.0 + rng.random(40) * 5.0)[kk]
    t = np.clip((41.0 - V) / np.maximum(41.0 - tip_v[None], 1e-3), 0, 1.2)             # 0 at the crest line, 1 at the tip
    centre = cx[None] + lean[None] * t; half = 3.6 * (1 - .72 * np.clip(t, 0, 1))
    hach = _aa(half - np.abs(x[None] - centre), 1.0) * _aa(V - tip_v[None], 1.0) * (V < 42)
    line = band(40.0, INK_BOTTOM, .6, 1.4, 151)
    s = _streak(h, w, 152, 60, 2.2, ss)
    ink[5] = np.maximum(hach, line) * (1 - .40 * _aa(s - .86, 14))
    # 6 battlement (seonggakwi): two lines with square merlons between them
    pitch = 48.0; x = u - np.floor(u / pitch) * pitch
    rail_t = band(INK_TOP, 21.5, .5, .5, 160); rail_b = band(42.5, INK_BOTTOM, .5, .8, 162)
    merlon = _aa(np.minimum(x - 12.0, 36.0 - x), 1.0)[None] * _aa(np.minimum(V - 20.0, 44.0 - V), 1.0)
    ink[6] = np.maximum(np.maximum(rail_t, rail_b), merlon) * (.96 + .04 * M.tnoise(h, w, 40, 163, 1))
    # 7 bridge (stretch: one period over the deck): two rails outside the road, a short cross bar at each end
    rail = np.maximum(_aa(np.minimum(V - INK_TOP, 18.5 - V), 1.0), _aa(np.minimum(V - 45.5, INK_BOTTOM - V), 1.0))
    bar = _aa(np.maximum(44.0 - u, u - (W - 44.0)), 1.0)[None] * _aa(np.minimum(V - 8.0, 56.0 - V), 1.0)
    ink[7] = np.maximum(rail, bar)
    out = np.zeros((8 * ROW_PX, W, 4))
    for r in range(8):
        a = M.down(ink[r], ss); k = M.down(ko[r], ss)
        a[:2] = 0; a[-2:] = 0; k[:2] = 0; k[-2:] = 0                              # 2 px of clear texels between rows (mip bleed)
        out[r * ROW_PX:(r + 1) * ROW_PX, :, 3] = a; out[r * ROW_PX:(r + 1) * ROW_PX, :, 0] = k
    return np.round(np.clip(out, 0, 1) * 255).astype(np.uint8)


# ---------------------------------------------------------------------------------------------------------------------
# icons (8 x 8 cells). Design box = 64 units, y down. The drawings are the direction sheet's (map308_mock.py panel e): solid
# brush silhouettes - a place's LOOK - with a rough brush edge and a paper plate. One set, two sheets: S (32 px cells, bold:
# no stroke under 3 px, so nothing is under 2 px at 22 px) and L (64 px cells, >= 5 px).
# ---------------------------------------------------------------------------------------------------------------------
ICON = dict(L=dict(cell=64, weight=6.3, min_w=6.2, rim=3.0, min_stroke=5.0), S=dict(cell=32, weight=7.2, min_w=6.7, rim=1.5, min_stroke=3.0))
FIT = 0.90          # the place glyphs are drawn on a 4..60 box and fitted to 6.8..57.2 so the paper plate never leaves the cell


class Pen:
    """vector helper on the 64-unit glyph grid (the sheet's G): polygons, rectangles, discs, round-capped lines. fill = False
    erases (a door, a cave mouth). Lines never go under the sheet's minimum width."""

    def __init__(self, big, fit=FIT):
        S = ICON['L' if big else 'S']
        self.big, self.fit, self.b, self.min_w, self.prims, self.hollow, self.hull = big, fit, S['weight'], S['min_w'], [], False, False
        self.second = []          # polygons of the SECOND ink (B channel: drawn in the ink token over the first ink)
        self.edge2 = 0.0          # the second ink also takes the outermost edge2 cell px of the whole figure (an ink outline)

    def ink2(self, pts):
        """a polygon of the second ink layer. It is part of the figure (R carries the whole silhouette) and of its plate."""
        self.pg(pts); self.second.append(self._t(pts))

    def _t(self, pts):
        return [(32 + (x - 32) * self.fit, 32 + (y - 32) * self.fit) for x, y in pts]

    def pg(self, pts, fill=True):
        self.prims.append(('f' if fill else 'e', self._t(pts)))

    def rc(self, x0, y0, x1, y1, fill=True):
        self.pg([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], fill)

    def ci(self, cx, cy, r, fill=True):
        self.prims.append(('d', self._t([(cx, cy)])[0], r * self.fit, fill))

    def ln(self, pts, w, fill=True, taper=False):
        self.prims.append(('s', self._t(pts), (max(w, self.min_w) if fill else w) * self.fit, taper, fill))

    def arc(self, cx, cy, rx, ry, a0, a1, w, n=24, taper=False):
        self.ln([(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)], w, taper=taper)


def _roof(p, cx, y, half, rise, curve=3.0):
    """tiled roof seen from the front: ridge on top, eaves swept up at both ends."""
    b = p.b
    p.pg([(cx - half, y), (cx - half + 3, y - curve), (cx - half * .45, y - rise * .7), (cx - half * .2, y - rise), (cx + half * .2, y - rise),
          (cx + half * .45, y - rise * .7), (cx + half - 3, y - curve), (cx + half, y), (cx + half - 2, y + b * .55), (cx - half + 2, y + b * .55)])


def _thatch(p, cx, y, half, rise):
    p.pg([(cx - half, y)] + [(cx + half * math.cos(math.radians(a)), y - rise * math.sin(math.radians(a)) ** .8) for a in range(180, -1, -15)] + [(cx + half, y)])


def _stake(p, x, top, bottom, w):
    p.pg([(x - w, bottom), (x - w, top + w * 1.6), (x, top), (x + w, top + w * 1.6), (x + w, bottom)])


BRUSH = dict(tip=3.9, tuft_w=17.2, belly=.76, neck=8.0, tuft_end=41.4, ferrule=(9.0, 47.6), handle=(5.4, 59.9), edge=dict(S=.95, L=1.7), rim=dict(S=.8, L=2.4))


def g_player(p):       # the current position: a BRUSH seen from above, its tip = the heading. Two inks: the cinnabar tuft (R, tinted
    # by the page) and, in the second layer (B, the ink token), the ferrule, a stub of the handle and a thin ink edge round the
    # tuft. Heading is carried three ways: the long pointed tip (hollow flanks), the round mass at the back of the tuft, and
    # the dark narrow handle behind it - so the mark still points at 22 px, at any angle, and without colour.
    p.fit = 1.0; K = BRUSH
    y0, y1, W, b_ = K['tip'], K['tuft_end'], K['tuft_w'], K['belly']
    right = []
    for i in range(41):
        t_ = i / 40
        if t_ <= b_: w = W * math.sin(math.pi / 2 * t_ / b_) ** 1.7                                   # hollow flank: a sharp tip
        else: w = K['neck'] + (W - K['neck']) * math.sqrt(max(0.0, 1 - ((t_ - b_) / (1 - b_)) ** 2))   # round shoulder
        right.append((32 + w, y0 + t_ * (y1 - y0)))
    p.pg(right + [(64 - x, y) for x, y in right[::-1]])
    fw, fy = K['ferrule']; hw, hy = K['handle']
    p.ink2([(32 - fw, y1 - .8), (32 + fw, y1 - .8), (32 + fw, fy), (32 - fw, fy)])                      # ferrule: a dark band across
    p.ink2([(32 - hw, fy - .5), (32 + hw, fy - .5), (32 + hw * .92, hy), (32 - hw * .92, hy)])           # handle stub
    p.edge2 = K['edge']['L' if p.big else 'S']


def g_inn(p):          # thatched roof (a full dark mound) on two posts + a hanging lantern
    b = p.b
    _thatch(p, 27, 31, 21, 20)
    p.ln([(13, 32), (13, 55)], b); p.ln([(41, 32), (41, 55)], b)
    if p.big: p.ln([(7, 56), (47, 56)], b * .9)
    p.ln([(54, 22), (54, 32)], b * .55)
    if p.big:
        p.rc(49, 32, 59, 47); p.rc(52, 36, 56, 43, False)
    else:
        p.ci(54, 40, 6.2)


def g_wake(p):         # the outer ink ring of the rest where you wake (drawn 1.5x behind the symbol); no paper plate inside
    p.fit = 1.0; p.hollow = True
    p.arc(32, 32, 21.5, 21.5, -105, 250, p.b * (1.0 if p.big else .9), 72, taper=True)


def g_village(p):      # three roofs: two behind, one in front
    b = p.b; p.hull = True
    for cx, y in ((17, 19), (47, 19), (32, 45)):
        p.pg([(cx - 14, y), (cx, y - 13), (cx + 14, y), (cx + 10, y + b * .8), (cx - 10, y + b * .8)])
        p.rc(cx - 8, y, cx + 8, y + 10)
        if p.big: p.rc(cx - 2.2, y + 4, cx + 2.2, y + 10.5, False)


def g_gaekju(p):       # the trading house: a small roof, and under it a carrying beam with a bale at each end (roof + load)
    b = p.b
    _roof(p, 32, 17, 15, 10)
    p.ln([(32, 19), (32, 57)], b)
    p.ln([(9, 28), (55, 28)], b * .9)
    for cx in (12.5, 51.5):
        p.ci(cx, 45, 9.6)
    if p.big: p.ln([(22, 57), (42, 57)], b * .8)


def g_relay(p):        # the post station: a small closed house and a tall hitching post with its bar
    b = p.b
    _roof(p, 20, 33, 16, 11)
    p.rc(9, 34, 31, 56)
    if p.big: p.rc(17, 43, 23, 57, False)
    p.ln([(48, 10), (48, 55)], b * 1.05)
    p.ln([(40.5, 19), (55.5, 19)], b)
    if p.big: p.ln([(41, 56), (55, 56)], b * .8)


def g_checkpoint(p):   # palisade gate: pointed stakes tied by a rail, a gate frame between them
    b = p.b
    for x in ((9, 19, 45, 55) if p.big else (9, 55)):
        _stake(p, x, 20, 57, b * .55)
    p.ln([(8.5, 34), (55.5, 34)], b * .8)
    p.ln([(22, 11), (42, 11)], b); p.ln([(24, 12), (24, 55)], b * .9); p.ln([(40, 12), (40, 55)], b * .9)


def g_gate(p):         # a wide gate tower roof (mullu) on a stone wall with its arch (hongye)
    p.rc(16, 31, 48, 57)
    p.pg([(26.5, 58), (26.5, 47.5), (29, 43.5), (32, 42.5), (35, 43.5), (37.5, 47.5), (37.5, 58)], False)
    _roof(p, 32, 26, 28, 14, curve=4.5)
    if p.big: p.rc(22, 32.5, 42, 36, False)


def g_mine(p):         # timber set of a drift mouth: cap beam and two raking legs, the dark mouth low inside
    b = p.b
    p.ln([(9.5, 15), (54.5, 15)], b * 1.2); p.ln([(14, 18), (9, 54)], b * 1.1); p.ln([(50, 18), (55, 54)], b * 1.1)
    p.pg([(23, 57), (26, 34), (38, 34), (41, 57)])


def g_cave(p):         # a leaning rock mound, its mouth low on one side
    p.pg([(4, 57), (6, 44), (12, 31), (22, 20), (35, 14), (47, 17), (56, 27), (60, 41), (60, 57)])
    p.pg([(33, 58), (33, 47), (36.5, 40), (43, 37), (49.5, 40), (53, 47), (53, 58)], False)


def g_temple(p):       # stone pagoda: three roofs on a narrow shaft, a finial
    b = p.b
    p.ln([(32, 8.5), (32, 12)], b * .8)
    for y, half in ((19, 9.5), (33, 13.5), (47, 17.5)):
        p.pg([(32 - half, y + .5), (32 - half + 2, y - 6), (32 + half - 2, y - 6), (32 + half, y + .5), (32 + half - 1, y + 3), (32 - half + 1, y + 3)])
        p.rc(32 - 4.4, y + 2, 32 + 4.4, y + 9)
    p.rc(19, 53.5, 45, 58)


def g_peak(p):         # three peaks
    p.pg([(3, 56), (16, 30), (23, 39), (33, 8), (44, 35), (50, 27), (61, 56)])
    if p.big: p.ln([(33, 22), (30, 34), (35, 43)], p.b * .5, False)


def g_bigtree(p):      # one old tree: a spreading crown leaning off its bent trunk
    for cx, cy, r in ((25, 19, 12.5), (41, 16, 10), (13, 27, 8.5), (50, 26, 8), (30, 28, 9.5)):
        p.ci(cx, cy, r)
    p.pg([(27, 33), (35, 33), (37.5, 45), (46, 57), (24, 57), (29.5, 46)])


def g_deepforest(p):   # two conifers
    b = p.b; p.hull = True
    for cx, top, s in ((20, 6, 1.0), (45, 17, .86)):
        for k in range(3):
            y = top + k * 11.5 * s
            p.pg([(cx, y), (cx + (9 + 3.5 * k) * s, y + 14.5 * s), (cx - (9 + 3.5 * k) * s, y + 14.5 * s)])
        p.rc(cx - b * .5, top + 35 * s, cx + b * .5, 58)


def g_worksite(p):     # felled logs: long logs lying across, stacked, each one shifted
    p.hull = True
    for y, x0, x1 in ((49.5, 11.5, 46), (33, 18, 52.5), (16.5, 11.5, 40)):
        p.ln([(x0, y), (x1, y)], 12.0)
        if p.big: p.ci(x1 if y != 33 else x0, y, 3.3, False)


def g_trace(p):        # three ink dots
    p.hull = True
    for cx, cy, r in ((32, 16, 8.6), (15, 45, 7.6), (49, 44, 9.2)):
        p.ci(cx, cy, r)


def g_camp(p):         # banner on a tall pole and a short palisade
    b = p.b
    p.ln([(15, 9), (15, 54.5)], b * .95)
    p.pg([(17, 7), (49, 14.5), (17, 27)])
    for x in (33, 44, 55):
        _stake(p, x, 37, 57, b * .52)
    p.ln([(30, 48), (56, 48)], b * .75)


def _hip_roof(p, cx, y, half, rise, top_half, lift, th):
    """a Korean roof from the front: a short ridge, hollow slopes, eaves swept up by `lift` at both ends, `th` thick."""
    L = [(cx - top_half, y - rise), (cx - top_half - (half - top_half) * .48, y - rise * .40), (cx - half + 4.5, y - 1.2), (cx - half, y - lift),
         (cx - half + .8, y + th * .6)]
    p.pg(L + [(cx - half + 5, y + th), (cx + half - 5, y + th)] + [(2 * cx - x, yy) for x, yy in L][::-1])


HALL = dict(upper=(17.0, 15.0, 12.0, 5.0), neck=8.0, lower=(36.0, 29.0, 13.0, 16.0), body=16.0, body_to=50.0, slit=4.4, terrace=25.0)


def g_hall(p):         # a Korean hall (jeongak), the direction sheet's two-storey roof: the ROOF is the sign - a small upper roof over
    # a wide lower one, eaves swept up - and under it a low closed body with door leaves on a stone terrace. No free-standing
    # columns under one flat roof (that read as a western temple front at 22 px).
    cx = 32; K = HALL
    y1, h1, r1, t1 = K['upper']; y2, h2, r2, t2 = K['lower']
    _hip_roof(p, cx, y1, h1, r1, t1, 3.2, 2.6)
    p.rc(cx - K['neck'], y1 + 2, cx + K['neck'], y2 - r2 + 1.5)                                        # the upper storey, mostly hidden
    _hip_roof(p, cx, y2, h2, r2, t2, 5.0, 3.0)
    by0, by1, bh = y2 + 2.5, K['body_to'], K['body']
    p.rc(cx - bh, by0, cx + bh, by1)
    w = 3.6 if p.big else K['slit']
    for x in ((-bh * .52, 0, bh * .52) if p.big else (-bh * .38, bh * .38)):                             # door leaves: paper slits in the body
        p.rc(cx + x - w / 2, by0 + 2.0, cx + x + w / 2, by1 + .5, False)
    tr = K['terrace']; p.pg([(cx - tr + 2, by1 + 1.5), (cx + tr - 2, by1 + 1.5), (cx + tr, 58), (cx - tr, 58)])   # stone terrace (woldae)


def g_waterside(p):    # a pavilion on its bank, open water beside and before it
    b = p.b
    p.pg([(22, 5), (39, 23), (34.5, 27), (9.5, 27), (5, 23)])
    p.ln([(13, 28), (13, 46)], b * .9); p.ln([(31, 28), (31, 46)], b * .9)
    p.rc(5, 46, 37, 52)
    p.ln([(41 + i * 3.6, 40 + 2.2 * math.sin(i * 1.26)) for i in range(5)], b * (1.0 if not p.big else .85))
    p.ln([(8.5 + i * 3.6, 54 + 2.2 * math.sin(i * 1.26)) for i in range(14)], b * (1.0 if not p.big else .85))


def g_place(p):        # a ring of dots: a place without a building
    p.hull = True
    for k in range(7):                                  # seven dots: each stays over 3 px at 22 px
        a = math.radians(360.0 / 7 * (k + .5))
        p.ci(32 + 20.0 * math.cos(a), 32 + 20.0 * math.sin(a), 7.1 if p.big else 6.2)


def g_coin(p):         # coin with a square hole
    b = p.b; p.fit = 1.0
    p.ci(32, 32, 25); p.ci(32, 32, 25 - b * .95, False)
    h = 5.2 + b * .9                                                 # half side of the square: its ring is .9 b thick round a hole
    p.rc(32 - h, 32 - h, 32 + h, 32 + h); p.rc(32 - 5.2 + (0 if p.big else 2.2), 32 - 5.2 + (0 if p.big else 2.2), 32 + 5.2 - (0 if p.big else 2.2), 32 + 5.2 - (0 if p.big else 2.2), False)


def g_pin(p):          # the player's own mark: two brush strokes
    b = p.b; p.hull = True
    p.ln([(11, 10), (30, 30), (53, 55)], b * 1.25, taper=True); p.ln([(53, 11), (33, 31), (10, 54)], b * 1.1, taper=True)


def g_objective(p):    # open brush ring (heard objective area), tinted cinnabar by the page
    p.fit = 1.0; p.hollow = True
    p.arc(32, 32, 21.0, 21.0, -62, 262, p.b * 1.1, 72, taper=True)


def g_shrine(p):       # reserved: the leaning spirit pole of a village shrine with its hanging ribbons, a stone at its foot
    b = p.b; p.hull = True
    p.ln([(50, 55), (15, 10)], b * .95)
    for x, y in ((22, 19), (31, 30.5), (40, 42)):
        p.ln([(x, y), (x - 1.5, y + 12)], b * .8, taper=True)
    p.ci(52, 50, 7.5)


def g_beacon(p):       # reserved: a beacon tower and its smoke drifting off (ink, not a light)
    b = p.b
    p.pg([(8, 57), (11, 36), (31, 36), (34, 57)])
    p.rc(7, 30, 35, 37)
    p.ln([(21, 27), (25, 20), (35, 17), (43, 11), (55, 8)], b * 1.25, taper=True)


def g_ferry(p):        # reserved: a ferry boat low on the water, its pole leaning
    b = p.b
    p.pg([(16, 38), (56, 38), (49, 49), (23, 49)])
    p.ln([(20, 9), (34, 38)], b)
    p.ln([(8.5 + i * 3.62, 56 + 1.6 * math.sin(i * 1.3)) for i in range(14)], b * 1.05)


GLYPHS = [  # cell number = index (row * 8 + column, from the PNG's top-left)
    ('player', '현재 위치', g_player), ('inn', '주막', g_inn), ('wake_ring', '깨어날 곳 고리', g_wake), ('village', '마을', g_village),
    ('gaekju', '객주', g_gaekju), ('relay', '역참', g_relay), ('checkpoint', '검문', g_checkpoint), ('gate', '성문', g_gate),
    ('mine', '갱 입구', g_mine), ('cave', '굴', g_cave), ('temple', '절', g_temple), ('peak', '봉우리', g_peak),
    ('bigtree', '큰 나무', g_bigtree), ('deepforest', '깊은 숲', g_deepforest), ('worksite', '일터', g_worksite), ('trace', '흔적', g_trace),
    ('camp', '군영', g_camp), ('hall', '전각', g_hall), ('waterside', '물가 누각', g_waterside), ('place', '그 밖의 장소', g_place),
    ('coin', '남긴 통보', g_coin), ('pin', '내 표식', g_pin), ('objective', '들은 목적 권역', g_objective), ('shrine', '성황당', g_shrine),
    ('beacon', '봉수', g_beacon), ('ferry', '나루', g_ferry),
]
RESERVED = ('shrine', 'beacon', 'ferry')
PLACE_GLYPHS = [g[0] for g in GLYPHS if g[0] not in ('player', 'wake_ring', 'coin', 'pin', 'objective')]   # the 21 signs of a PLACE
SECOND_INK = ('player',)             # the glyphs that use the second ink layer (B); every other cell has B = 0


def render_glyph(fn, size, ss=8):
    """-> (ink, plate, ink2) float [cell, cell]. ink = the whole figure (R); plate = the solid paper silhouette (ink grown by the
    rim, holes filled; G); ink2 = the second ink layer (B): the pen's ink2 polygons and, when the glyph asks for it, the
    outermost edge2 px of the figure. ink2 lies inside ink, so a reader that ignores B still draws the whole figure."""
    S = ICON[size]; cell = S['cell']; k = cell / 64.0
    pen = Pen(big=(size == 'L')); fn(pen)
    rim = S['rim'] if fn is not g_player else BRUSH['rim'][size]              # the brush fills its cell: a thinner plate rim
    ink = M.Canvas(cell, cell, ss); plate = M.Canvas(cell, cell, ss)
    for pr in pen.prims:
        if pr[0] == 's':
            pts = [(x * k, y * k) for x, y in pr[1]]; w = pr[2] * k
            wf = (lambda t: 1.0 + .30 * (1 - t) ** 2) if pr[3] else None
            ink.stroke(pts, w, wf, fill=255 if pr[4] else 0, step=.25)
            if pr[4]: plate.stroke(pts, w + 2 * rim, wf, step=.4)
        elif pr[0] == 'f':
            pts = [(x * k, y * k) for x, y in pr[1]]
            ink.poly(pts); plate.poly(pts); plate.stroke(pts + pts[:1], 2 * rim, step=.4)
        elif pr[0] == 'd':
            (x, y), r = pr[1], pr[2] * k
            ink.disc(x * k, y * k, r, fill=255 if pr[3] else 0)
            if pr[3]: plate.disc(x * k, y * k, r + rim)
        elif pr[0] == 'e':
            ink.poly([(x * k, y * k) for x, y in pr[1]], fill=0)
    if pen.hull:                                       # a loose glyph (dots, crossed strokes) stands on one paper piece: its convex hull
        cloud = []
        for pr in pen.prims:
            if pr[0] == 'f' or (pr[0] == 's' and pr[4]): cloud += [(x * k, y * k) for x, y in pr[1]]
            elif pr[0] == 'd' and pr[3]:
                (x, y), r = pr[1], pr[2] * k
                cloud += [(x * k + r * math.cos(a), y * k + r * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 12, endpoint=False)]
        hull = convex_hull(cloud)
        plate.poly(hull); plate.stroke(hull + hull[:1], 2 * rim, step=.4)
    # brush edge: the outline is softened and broken by a seeded noise (the sheet's rule); it never opens a stroke
    a = ink.array(); n = M.tnoise(a.shape[0], a.shape[1], 3 * ss, 400 + cell, 2)
    soft = M.box(a, ss // 2)
    a = (soft + (n - .5) * .26 > .5).astype(float)
    ink_out = M.down(a, ss)
    # second ink: its polygons, cut to the figure, and the figure's own outer edge
    ink2_out = np.zeros_like(ink_out)
    if pen.second or pen.edge2 > 0:
        c2 = M.Canvas(cell, cell, ss)
        for pts in pen.second: c2.poly([(x * k, y * k) for x, y in pts])
        b = c2.array() >= .5
        if pen.edge2 > 0:
            r = pen.edge2 * ss
            b = b | (M.near_dist(a < .5, r + 1) <= r)                       # within edge2 px of the outside
        ink2_out = M.down((b & (a >= .5)).astype(float), ss)
    # plate: fill the holes (everything the border cannot reach)
    if pen.hollow:
        solid = (plate.array() >= .5).astype(float)
    else:
        pim = plate.im.point(lambda v: 255 if v >= 128 else 0)
        ImageDraw.floodfill(pim, (0, 0), 128)
        solid = (np.asarray(pim) != 128).astype(float)
    plate_out = np.maximum(M.down(solid, ss), ink_out)
    return ink_out, plate_out, ink2_out


def convex_hull(pts):
    """Andrew's monotone chain; returns the hull in order (no repeated first point)."""
    P = sorted(set((round(x, 4), round(y, 4)) for x, y in pts))
    if len(P) < 3: return P
    cross = lambda o, a, b: (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for q in P:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0: lo.pop()
        lo.append(q)
    for q in reversed(P):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0: up.pop()
        up.append(q)
    return lo[:-1] + up[:-1]


def icons(size):
    S = ICON[size]; cell = S['cell']
    out = np.zeros((8 * cell, 8 * cell, 4)); rep = []
    for i, (gid, ko, fn) in enumerate(GLYPHS):
        ink, plate, ink2 = render_glyph(fn, size)
        edge = max(plate[0].max(), plate[-1].max(), plate[:, 0].max(), plate[:, -1].max())
        r, c = divmod(i, 8)
        sub = out[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
        sub[..., 0] = ink; sub[..., 1] = plate; sub[..., 2] = ink2; sub[..., 3] = np.maximum(ink, plate)
        ys, xs = np.nonzero(ink > .5)
        thin = thin_parts(ink > .5, 2)
        rep.append(dict(id=gid, cell=i, ink_bbox=[int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1],
                        ink_share=round(float((ink > .5).mean()), 4), ink2_share=round(float((ink2 > .5).mean()), 4), plate_touches_cell_edge=bool(edge > .02),
                        stroke_survival=round(open_survival(ink > .5, int(S['min_stroke'])), 4),
                        thin_part_px=thin[0], thin_px=thin[1]))
    return np.round(np.clip(out, 0, 1) * 255).astype(np.uint8), rep


def _open(mask, d):
    H, W = mask.shape
    er = np.ones((H - d + 1, W - d + 1), bool)
    for dy in range(d):
        for dx in range(d):
            er &= mask[dy:dy + H - d + 1, dx:dx + W - d + 1]
    op = np.zeros((H, W), bool)
    for dy in range(d):
        for dx in range(d):
            op[dy:dy + H - d + 1, dx:dx + W - d + 1] |= er
    return op


def open_survival(mask, d):
    """share of the ink that survives a morphological opening with a d x d square: 1.0 = no stroke is thinner than d px."""
    if not mask.any(): return 1.0
    return float((_open(mask, d) & mask).sum() / mask.sum())


def thin_parts(mask, d=2):
    """ink that a d x d opening removes = parts thinner than d px. -> (largest 8-connected removed piece in px, removed px).
    A pointed tip loses a pixel or two; a piece of 3 px or more is a body part (a line, a neck) under d px."""
    rem = mask & ~_open(mask, d)
    H, W = rem.shape; seen = np.zeros_like(rem); best = 0
    for i, j in zip(*np.nonzero(rem)):
        if seen[i, j]: continue
        st = [(i, j)]; seen[i, j] = True; n = 0
        while st:
            a, b = st.pop(); n += 1
            for da in (-1, 0, 1):
                for db in (-1, 0, 1):
                    y, x = a + da, b + db
                    if 0 <= y < H and 0 <= x < W and rem[y, x] and not seen[y, x]: seen[y, x] = True; st.append((y, x))
        best = max(best, n)
    return int(best), int(rem.sum())


def resize_channel(a, px):
    """one channel of a cell -> px x px (Lanczos), the way the offline tools scale an icon (channels one by one)."""
    return np.asarray(Image.fromarray(np.round(np.clip(a, 0, 1) * 255).astype(np.uint8), 'L').resize((px, px), Image.LANCZOS), float) / 255.0


def pair_correlation(cells, px=22, blur=0.0, shift=0):
    """Pearson correlation of the ink of every pair of glyphs at `px` (S cell scaled by Lanczos; blur = Gaussian sigma in px,
    a tolerance for a small shift: .8 px is the stricter reading). shift = n: the LARGEST correlation over every offset of one
    glyph by -n..n px in x and y (a marker lands on any pixel phase, so two signs must stay apart when one is a pixel off).
    cells = {id: ink [32, 32]}. -> sorted [(r, a, b)]."""
    from PIL import ImageFilter
    im = {}
    for k, v in cells.items():
        a = resize_channel(v, px)
        if blur > 0: a = np.asarray(Image.fromarray(np.round(a * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(blur)), float) / 255.0
        im[k] = a
    ids = list(cells); out = []
    offs = [(dy, dx) for dy in range(-shift, shift + 1) for dx in range(-shift, shift + 1)]
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            best = -1.0
            for dy, dx in offs:
                s = np.zeros_like(im[ids[j]])
                s[max(0, dy):px + min(0, dy), max(0, dx):px + min(0, dx)] = im[ids[j]][max(0, -dy):px + min(0, -dy), max(0, -dx):px + min(0, -dx)]
                best = max(best, float(np.corrcoef(im[ids[i]].ravel(), s.ravel())[0, 1]))
            out.append((best, ids[i], ids[j]))
    return sorted(out, reverse=True)
