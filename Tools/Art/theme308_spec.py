# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - Spec support: the measured numbers and the design sheet. NOT a kit and NOT a game capture.

  python Tools/Art/theme308_spec.py            writes Art/UI308/Theme/spec_theme_v0.png (+ _view.jpg) and spec_theme_v0.json
  python Tools/Art/theme308_spec.py --check    renders twice in memory and compares the bytes (determinism), no files written
  python Tools/Art/theme308_spec.py --numbers  only the json (no sheet)

Everything is composited in sRGB code values (as DESIGN 2.3 and the #308 mocks do); Unity is linear, so the alpha of thin
anti-aliased edges will differ slightly in game (see the Spec, Temporary Exceptions).
The HUD vessels and pictograms come from Tools/Art/hud308_mock.py (imported, not copied). If that module cannot be imported
the HUD panels fall back to the hardware alone and the json says so.
"""
import hashlib
import io
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L   # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Art', 'UI308', 'Theme')
FONTS = os.path.join(ROOT, 'Oheangbu', 'Assets', '_Project', 'Art', 'UI', 'UI304', 'Fonts')
T = L.T
NACRE = L.nacre_token()

try:
    import hud308_mock as HM
    HUD_OK = True
except Exception as e:      # the HUD mock belongs to another work stream and may be mid-edit
    HM, HUD_OK, HUD_ERR = None, False, repr(e)

_fonts = {}


def font(px, face='sans'):
    name = {'sans': 'NotoSansKR-Regular.ttf', 'bold': 'NotoSansKR-Bold.ttf', 'serif': 'NotoSerifKR-800.ttf',
            'serif6': 'NotoSerifKR-600.ttf', 'serif9': 'NotoSerifKR-900.ttf'}[face]
    key = (name, px)
    if key not in _fonts:
        _fonts[key] = ImageFont.truetype(os.path.join(FONTS, name), px)
    return _fonts[key]


def c8(c):
    return tuple(int(round(float(v) * 255)) for v in np.clip(np.asarray(c)[:3], 0, 1))


def flat(w, h, col):
    return np.ones((h, w, 3)) * np.asarray(col, float)[None, None, :]


def paste(dst, rgba, x, y, alpha=1.0):
    """sRGB 'over' of straight-alpha RGBA (or RGB + mask) onto dst (H, W, 3); clips."""
    x, y = int(round(x)), int(round(y))
    h, w = rgba.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    src = rgba[y0 - y:y1 - y, x0 - x:x1 - x]
    a = src[..., 3:4] * alpha
    dst[y0:y1, x0:x1] = dst[y0:y1, x0:x1] * (1 - a) + src[..., :3] * a


def tint(mask, col, alpha=1.0):
    """coverage mask -> straight-alpha RGBA of one colour"""
    return np.dstack([np.ones(mask.shape + (3,)) * np.asarray(col, float)[None, None, :], np.clip(mask * alpha, 0, 1)])


def rect(dst, x, y, w, h, col, a=1.0):
    x, y, w, h = int(round(x)), int(round(y)), int(round(w)), int(round(h))
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 > x0 and y1 > y0:
        dst[y0:y1, x0:x1] = dst[y0:y1, x0:x1] * (1 - a) + np.asarray(col, float) * a


def tile_fill(tile, w, h):
    th, tw = tile.shape
    return np.tile(tile, (h // th + 2, w // tw + 2))[:h, :w]


def resize_mask(m, w, h):
    return np.asarray(Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS), float) / 255


def grey_at_ratio(ground, toward, ratio):
    """the mix of `ground` toward `toward` whose contrast against `ground` is `ratio` (bisection)"""
    lo, hi = 0.0, 1.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if L.contrast(L.over(toward, ground, mid), ground) < ratio:
            lo = mid
        else:
            hi = mid
    return L.over(toward, ground, (lo + hi) / 2), (lo + hi) / 2


# ---------------------------------------------------------------------------------------------------------------------
# components (design px at 1080p, rendered at scale s)
# ---------------------------------------------------------------------------------------------------------------------
COLLAR = dict(line_y=.62, line_w=2.0, piece=6.5, inset=2.5)       # nacre line inside the lacquer collar (share of neck h)
ROUNDEL = dict(d=44.0, rim=1.4, rim_a=.92, glyph=.74, dry_a=.30)  # lacquer button under a HUD pictogram
MINIFRAME = dict(win=(294.0, 210.0), frame=9.0, line_w=1.5, line_in=1.0, corner=14.0, north=(7.0, 12.0), pad=4.0)
BOARD = dict(margin=18.0, line_in=3.5, line_w=1.5, step=4.0, band=(7.0, 17.0), band_u=10.0 / 3.0, band_bar=1.0)
SLOT = dict(pitch=124.0, chip=104.0, bar_v=4.0, bar_h=3.0, clear=8.0)
PLAQUE = dict(inset=7.0, w1=2.0, gap=2.5, w2=1.0, foot=3.0)
KEYCAP = dict(h=40.0, rim=2.0, lip=5.0)
SELECT = dict(w=2.0, piece=13.0, out=3.0)


def collar(kind, s, ss=4):
    """Lacquer collar (neck + lip of the egg bottle) with one cut-shell line. RGBA sized like HM.render_vessel."""
    rw, rh = HM.RECT[kind][2:]
    g = HM.vessel_geo(rw, rh, s, ss)
    yb = g.lip_h + g.neck_h - .5
    inside = np.clip(.5 - g.d * g.k / ss, 0, 1) * np.clip((yb - g.y) * g.k / ss + .5, 0, 1)
    H, W = inside.shape
    lac = np.dstack([np.ones((H, W, 3)) * T['Lacquer'], inside])
    out = lac.reshape(H // ss, ss, W // ss, ss, 4)
    pre = np.concatenate([out[..., :3] * out[..., 3:4], out[..., 3:4]], axis=-1).mean(axis=(1, 3))
    pre[..., :3] /= np.maximum(pre[..., 3:4], 1e-6)
    ly = g.lip_h + g.neck_h * COLLAR['line_y']
    hw = HM.SHAPE['neck_w'] * rw / 2 - COLLAR['inset']
    pcs = L.strips([(g.cx - hw + HM.PAD, ly + HM.PAD), (g.cx + hw + HM.PAD, ly + HM.PAD)], COLLAR['line_w'],
                   piece=COLLAR['piece'])
    na = L.render_pieces(rw + 2 * HM.PAD, rh + 2 * HM.PAD, pcs, s=s, ss=ss, seed=L.SEED + (1 if kind == 'hp' else 2))
    a = na[..., 3:4]
    pre[..., :3] = pre[..., :3] * (1 - a) + na[..., :3] * a
    pre[..., 3] = np.maximum(pre[..., 3], na[..., 3])
    return pre, float(na[..., 3].sum() / (s * s)), float(inside.mean() * (W / ss) * (H / ss) / (s * s))


def roundel(kind, state='wet', fill=1.0, s=1.0, ss=4):
    """Lacquer button + paper hairline rim + the pictogram as cut shell. state: wet | dry | rewet."""
    n = int(round(ROUNDEL['d'] * s)) * ss
    k = s * ss
    ys, xs = np.mgrid[0:n, 0:n]
    r = np.hypot((xs + .5) / k - ROUNDEL['d'] / 2, (ys + .5) / k - ROUNDEL['d'] / 2)
    disc = np.clip((ROUNDEL['d'] / 2 - r) * k / ss + .5, 0, 1)
    inner = np.clip((ROUNDEL['d'] / 2 - ROUNDEL['rim'] - r) * k / ss + .5, 0, 1)
    if HUD_OK:
        g = HM.Glyph(s, ss)                                    # the primitives, not glyph_fields (that API is in flux)
        order = np.clip(HM.GLYPHS[kind](g), 0, 1)
        gn = int(round(n * ROUNDEL['glyph']))
        o = (n - gn) // 2
        m = np.zeros((n, n))
        od = np.zeros((n, n))
        m[o:o + gn, o:o + gn] = resize_mask(np.clip(g.m, 0, 1), gn, gn)
        od[o:o + gn, o:o + gn] = resize_mask(np.clip(order, 0, 1), gn, gn)
    else:
        m = np.clip((7 - np.abs(r - 9)) * k / ss, 0, 1) * 0
        od = np.zeros((n, n))
    wet = np.ones_like(m) if state == 'wet' else np.zeros_like(m) if state == 'dry' else np.clip((fill - od) / .05, 0, 1)
    ga = m * (wet + (1 - wet) * ROUNDEL['dry_a']) * inner
    col = L.nacre_rgb((xs + .5) / k, (ys + .5) / k, 1, .6, 1.1)
    rgb = np.ones((n, n, 3)) * T['Paper']                      # the hairline rim (paper, as the #304 marks carry)
    a = disc * ROUNDEL['rim_a']
    a2 = inner
    rgb = rgb * (1 - a2[..., None]) + T['Lacquer'] * a2[..., None]
    a = np.maximum(a, a2)
    rgb = rgb * (1 - ga[..., None]) + col * ga[..., None]
    pre = np.dstack([rgb * a[..., None], a]).reshape(n // ss, ss, n // ss, ss, 4).mean(axis=(1, 3))
    pre[..., :3] /= np.maximum(pre[..., 3:4], 1e-6)
    out_share = float((m * (1 - inner)).sum() / max(1e-6, m.sum()))
    return pre, float((m * inner).sum() / (k * k)), out_share


def minimap_frame(s=1.0, ss=4):
    """SPEC-MAP-OVERHAUL-308 section 5 with the kit's values: a rectangular lacquer door frame (9 px) round the 294 x 210
    map window, one cut-shell line just outside the window, a stepped corner piece on every corner and the north piece
    on the top side. The map inside is a stand-in. Returns (RGBA, nacre area px^2, lacquer area px^2)."""
    M = MINIFRAME
    ww, wh = M['win']
    f, pad = M['frame'], M['pad']
    W, H = ww + 2 * f + 2 * pad, wh + 2 * f + 2 * pad
    ow, oh = int(round(W * s)), int(round(H * s))
    k = s * ss
    ys, xs = np.mgrid[0:oh * ss, 0:ow * ss]
    X, Y = (xs + .5) / k - pad, (ys + .5) / k - pad                  # origin = the frame's outer corner
    aa = lambda v: np.clip(v * k / ss + .5, 0, 1)
    box = lambda x0, y0, x1, y1: aa(np.minimum(np.minimum(X - x0, x1 - X), np.minimum(Y - y0, y1 - Y)))
    outer = box(0, 0, ww + 2 * f, wh + 2 * f)
    win = box(f, f, f + ww, f + wh)
    rgb = np.zeros(X.shape + (3,))
    al = np.zeros(X.shape)

    def put(col, a):
        nonlocal rgb, al
        a = np.clip(a, 0, 1)
        o = a + al * (1 - a)
        rgb = (np.asarray(col, float) * a[..., None] + rgb * (al * (1 - a))[..., None]) / np.maximum(o, 1e-6)[..., None]
        al = o
    put(T['Sheet'], win * .94)                                      # walked ground: paper (stand-in)
    blob = aa(74 - np.hypot((X - f - ww * .46) * .62, Y - f - wh * .52))
    put(np.asarray(L.hexc('#8F8B81')), win * (1 - blob))            # unwalked ground (the map Spec's wash value)
    road = aa(1.3 - np.abs(Y - f - wh * .52 + 16 * np.sin((X - 40) / 38.0))) * win * blob
    put(T['Ink'], road * .9)
    put(T['Lacquer'], outer * (1 - win))
    li = M['line_in']
    pcs = L.strips([(f - li, f - li), (f + ww + li, f - li), (f + ww + li, f + wh + li), (f - li, f + wh + li)],
                   M['line_w'], piece=12, closed=True)
    Wf, Hf = ww + 2 * f, wh + 2 * f
    _, _, cp = L.najeon_corner(M['corner'])
    for fx_, fy_ in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        for p in cp:
            pcs.append(dict(p, poly=[((x if fx_ > 0 else Wf - x), (y if fy_ > 0 else Hf - y)) for x, y in p['poly']]))
    nw, nh, npc = L.najeon_north(*M['north'])
    for p in npc:
        pcs.append(dict(p, poly=[(x + Wf / 2 - nw / 2, y + f / 2 - nh / 2) for x, y in p['poly']]))
    na = L.render_pieces(W, H, [dict(p, poly=[(x + pad, y + pad) for x, y in p['poly']]) for p in pcs], s=s, ss=ss,
                         seed=L.SEED + 7)
    pre = np.dstack([rgb * al[..., None], al]).reshape(oh, ss, ow, ss, 4).mean(axis=(1, 3))
    pre[..., :3] /= np.maximum(pre[..., 3:4], 1e-6)
    a = na[..., 3:4]
    pre[..., :3] = pre[..., :3] * (1 - a) + na[..., :3] * a
    pre[..., 3] = np.maximum(pre[..., 3], na[..., 3])
    # the player arrow: cinnabar head with a paper rim (DESIGN 5.13), unchanged by the theme
    cx, cy = (pad + f + ww * .46) * s * 4, (pad + f + wh * .52) * s * 4
    tri = lambda rr: [(cx, cy - 11 * rr * s * 4), (cx + 8 * rr * s * 4, cy + 9 * rr * s * 4), (cx, cy + 4.5 * rr * s * 4),
                      (cx - 8 * rr * s * 4, cy + 9 * rr * s * 4)]
    for rr, col in ((1.28, T['Paper']), (1.0, T['Cinnabar'])):
        im = Image.new('L', (ow * 4, oh * 4), 0)
        ImageDraw.Draw(im).polygon(tri(rr), fill=255)
        m = np.asarray(im.resize((ow, oh), Image.LANCZOS), float) / 255
        pre[..., :3] = pre[..., :3] * (1 - m[..., None]) + col * m[..., None]
    lac = (ww + 2 * f) * (wh + 2 * f) - ww * wh
    return pre, float(na[..., 3].sum() / (s * s)), float(lac)


def map_board_corner(w, h, s=1.0, ss=4):
    """Top-left corner of the full-map lacquer board (chilban): a cut-shell line that turns in by one aja step at the
    corner, a wanja band between the line and the paper, the paper sheet on top. RGB (opaque)."""
    B = BOARD
    ow, oh = int(round(w * s)), int(round(h * s))
    rgb = flat(ow, oh, T['Lacquer'])
    b0, b1 = B['band']
    tile = L.lattice('wanja', scale=s, u=B['band_u'], bar=B['band_bar'], edge=True)
    full = tile_fill(tile, ow, oh)
    ys, xs = np.mgrid[0:oh, 0:ow]
    X, Y = (xs + .5) / s, (ys + .5) / s
    d = np.minimum(X, Y)
    band = ((d >= b0) & (d < b1)).astype(float) * full
    rgb = rgb * (1 - band[..., None]) + T['Wood'] * band[..., None]
    path = L.stepped_corner_path(B['line_in'], B['step'], max(w, h) + 30)
    na = L.render_pieces(w, h, L.strips(path, B['line_w'], piece=12), s=s, ss=ss, seed=L.SEED + 9)[:oh, :ow]
    rgb = rgb * (1 - na[..., 3:4]) + na[..., :3] * na[..., 3:4]
    m = int(round(B['margin'] * s))
    rgb[m:, m:] = T['Sheet']
    return rgb


def select_frame(w, h, s=1.0, ss=4):
    """Kept selection: four cut-shell strips round a cell (the focus stays the cinnabar brush frame of #304)."""
    o = SELECT['out']
    W, H = w + 2 * o + 4, h + 2 * o + 4
    p = [(2, 2), (W - 2, 2), (W - 2, H - 2), (2, H - 2)]
    pcs = L.strips(p, SELECT['w'], piece=SELECT['piece'], closed=True)
    return L.render_pieces(W, H, pcs, s=s, ss=ss, seed=L.SEED + 11)


def plaque(w, h, s=1.0, ss=4, single=False, iron=False):
    """Porcelain plaque: flat cool white, a foot line and an inlaid border. Returns RGB (opaque) + its inlay coverage."""
    ow, oh = int(round(w * s)), int(round(h * s))
    rgb = flat(ow, oh, T['Porcelain'])
    rect(rgb, 0, oh - PLAQUE['foot'] * s, ow, PLAQUE['foot'] * s, T['PorcelainShade'])
    m = L.inlay_frame(w, h, s=s, ss=ss, inset=PLAQUE['inset'], w1=PLAQUE['w1'] if not single else 1.5,
                      gap=PLAQUE['gap'], w2=0.0 if single else PLAQUE['w2'])[:oh, :ow]
    col = T['InlayIron'] if iron else T['InlayDark']
    rgb = rgb * (1 - m[..., None]) + col * m[..., None]
    return np.dstack([rgb, np.ones((oh, ow))]), float(m.sum() / (s * s))


def keycap(w, s=1.0):
    h = KEYCAP['h']
    ow, oh = int(round(w * s)), int(round(h * s))
    rgb = flat(ow, oh, T['InlayDark'])
    r = KEYCAP['rim'] * s
    rect(rgb, r, r, ow - 2 * r, oh - 2 * r, T['PorcelainShade'])
    rect(rgb, r, r, ow - 2 * r, oh - 2 * r - KEYCAP['lip'] * s, T['Porcelain'])
    return np.dstack([rgb, np.ones((oh, ow))])


# ---------------------------------------------------------------------------------------------------------------------
# numbers
# ---------------------------------------------------------------------------------------------------------------------
def numbers():
    rep = dict(spec='SPEC-UI-THEME-308', decision='D308-15', note='sRGB compositing; TEST values')
    rep['tokens304'] = dict(L.TOK304)
    rep['tokens308'] = dict(L.TOK308, Nacre=L.to_hex(NACRE))
    rep['nacre_model'] = dict(L.NACRE)
    hs = np.linspace(L.NACRE['H_centre'] - L.NACRE['H_span'] / 2, L.NACRE['H_centre'] + L.NACRE['H_span'] / 2, 191)
    dl = L.NACRE['grain_dL'] + L.NACRE['piece_dL']
    ext = np.array([L.oklch(np.float64(L.NACRE['L'] + d), L.NACRE['C'], h) for h in hs for d in (-dl, 0.0, dl)])
    base = np.array([L.oklch(np.float64(L.NACRE['L']), L.NACRE['C'], h) for h in hs])
    lumv = L.lum(ext)
    rep['nacre'] = dict(
        anchors={str(int(h)): L.to_hex(L.oklch(np.float64(L.NACRE['L']), L.NACRE['C'], h)) for h in (150, 170, 205, 245, 285, 320, 340)},
        max_channel=int(round(ext.max() * 255)), min_channel=int(round(ext.min() * 255)),
        lum_min=round(float(lumv.min()), 4), lum_max=round(float(lumv.max()), 4),
        value_ratio_inside_nacre=round(float((lumv.max() + .05) / (lumv.min() + .05)), 3),
        value_ratio_hue_only=round(float((L.lum(base).max() + .05) / (L.lum(base).min() + .05)), 3),
        max_saturation_hsv=round(float(((ext.max(axis=1) - ext.min(axis=1)) / ext.max(axis=1)).max()), 3))
    # the shader form: rgb = N0 + A cos(h) + B sin(h) fitted on the arc (sRGB code values)
    Hr = np.radians(hs)
    G = np.stack([np.ones_like(Hr), np.cos(Hr), np.sin(Hr)], axis=1)
    coef, *_ = np.linalg.lstsq(G, base, rcond=None)
    err = np.abs(G @ coef - base).max() * 255
    rep['nacre_shader_fit'] = dict(N0=[round(float(v), 4) for v in coef[0]], A=[round(float(v), 4) for v in coef[1]],
                                   B=[round(float(v), 4) for v in coef[2]], max_error_8bit=round(float(err), 2))
    veil90, veil85 = L.over(T['Veil'], L.SKY, .90), L.over(T['Veil'], L.SKY, .85)
    c = lambda a, b: round(L.contrast(a, b), 2)
    pairs = [   # (name, text, ground, required)
        ('한지 / 장막 α.90', T['Paper'], veil90, 7.0), ('안개 / 장막 α.90', T['Mist'], veil90, 4.5),
        ('한지 / 장막 α.85', T['Paper'], veil85, 7.0), ('안개 / 장막 α.85', T['Mist'], veil85, 4.5),
        ('먹 / 생지', T['Ink'], T['Sheet'], 7.0), ('재 / 생지', T['Ash'], T['Sheet'], 4.5),
        ('먹 / 칩', T['Ink'], T['Chip'], 7.0), ('재 / 칩', T['Ash'], T['Chip'], 4.5),
        ('먹 / 백자', T['Ink'], T['Porcelain'], 7.0), ('재 / 백자', T['Ash'], T['Porcelain'], 4.5),
        ('흑상감 / 백자', T['InlayDark'], T['Porcelain'], 7.0),
    ]
    rows = []
    for name, tx, gr, req in pairs:
        c0 = L.contrast(tx, gr)
        rows.append(dict(pair=name, contrast=round(c0, 2), required=req, max_pattern_ratio=round(c0 / req, 3)))
    rep['text_pairs'] = rows
    cap = min(r['max_pattern_ratio'] for r in rows if r['pair'] != '재 / 칩')
    rep['pattern_cap'] = dict(binding=min((r for r in rows if r['pair'] != '재 / 칩'), key=lambda r: r['max_pattern_ratio'])['pair'],
                              raw=cap, cap_test=1.15,
                              note='재 / 칩 (4.73) leaves 1.05 only: no pattern under Ash text on a chip')
    after = []
    for name, tx, gr, req in pairs:
        toward = T['Paper'] if L.lum(gr) < .2 else T['Ink']
        pat, mixv = grey_at_ratio(gr, toward, 1.15)
        after.append(dict(pair=name, text_vs_pattern=round(L.contrast(tx, pat), 2), pattern=L.to_hex(pat), mix=round(mixv, 3),
                          ok=bool(L.contrast(tx, pat) >= req)))
    rep['text_on_pattern_at_cap'] = after
    rep['materials'] = {
        '자개 / 옻칠': c(NACRE, T['Lacquer']), '자개 / 밝은 하늘': c(NACRE, L.SKY), '자개 / 솔숲': c(NACRE, L.PINE),
        '옻칠 / 밝은 하늘': c(T['Lacquer'], L.SKY), '옻칠 / 솔숲': c(T['Lacquer'], L.PINE),
        '한지 테 α.92 / 솔숲': c(L.over(T['Paper'], L.PINE, .92), L.PINE),
        '자개 마름(α.30) / 옻칠': c(L.over(NACRE, T['Lacquer'], .30), T['Lacquer']),
        '살대 / 장막(불투명)': c(T['Wood'], T['Veil']), '살대 / 장막 α.90(하늘 위)': c(T['Wood'], veil90),
        '살대 / 칩': c(T['Wood'], T['Chip']), '살대 / 생지': c(T['Wood'], T['Sheet']),
        '백자 / 밝은 하늘': c(T['Porcelain'], L.SKY), '백자 / 솔숲': c(T['Porcelain'], L.PINE),
        '백자 / 장막 α.90': c(T['Porcelain'], veil90), '백자 그늘 / 백자': c(T['PorcelainShade'], T['Porcelain']),
        '백자 / 한지': c(T['Porcelain'], T['Paper']),
        '흑상감 / 백자': c(T['InlayDark'], T['Porcelain']), '철사 상감 / 백자': c(T['InlayIron'], T['Porcelain']),
        '주사 / 백자': c(T['Cinnabar'], T['Porcelain']), '철사 상감 / 주사': c(T['InlayIron'], T['Cinnabar']),
        '자개 / 장막 α.90': c(NACRE, veil90), '주사 / 장막 α.90': c(T['Cinnabar'], veil90),
        '자개 / 주사': c(NACRE, T['Cinnabar']),
    }
    cvd = {}
    for kind in ('protan', 'deutan', 'tritan'):
        f = lambda col: L.cvd(col, kind)
        arc = np.array([f(x) for x in base])
        cvd[kind] = {
            '자개(선택) / 주사(초점)': c(f(NACRE), f(T['Cinnabar'])),
            '자개 / 옻칠': c(f(NACRE), f(T['Lacquer'])),
            '자개 마름 / 자개 젖음': c(f(L.over(NACRE, T['Lacquer'], .30)), f(NACRE)),
            '흑상감 / 백자': c(f(T['InlayDark']), f(T['Porcelain'])),
            '철사 상감 / 주사': c(f(T['InlayIron']), f(T['Cinnabar'])),
            '자개 색띠 명도비': round(float((L.lum(arc).max() + .05) / (L.lum(arc).min() + .05)), 3),
        }
    rep['cvd'] = cvd
    lat = {}
    for name in L.LATTICE_ORDER:
        f = L.LATTICE[name]
        t = L.lattice_tile(name)
        q = np.round(t * 255).astype(np.uint8)
        lat[name] = dict(ko=f['ko'], unit_px=f['u'], bar_px=f['bar'], bar_ratio=round(f['bar'] / f['u'], 4),
                         period_design_px=[round(f['tile'][0] * f['u'], 2), round(f['tile'][1] * f['u'], 2)],
                         tile_px=[int(t.shape[1]), int(t.shape[0])], open_ratio=round(L.open_ratio(t), 3),
                         seam_error=round(L.seam_error(t), 5), bytes_r8=int(t.size),
                         sha256=hashlib.sha256(q.tobytes()).hexdigest()[:16])
    rep['lattice'] = lat
    rep['lattice_bytes_r8_with_mips'] = int(sum(v['bytes_r8'] for v in lat.values()) * 4 / 3)
    # ornament area at 1080p (design px^2 of nacre / inlay), measured on the renders
    area = {}
    if HUD_OK:
        hp_c, hp_n, hp_l = collar('hp', 1.0)
        ik_c, ik_n, ik_l = collar('ink', 1.0)
        area['hud_collar_nacre'] = round(hp_n + ik_n, 1)
        area['hud_collar_lacquer'] = round(hp_l + ik_l, 1)
        gl = [roundel(kk, 'wet', 1, 1.0) for kk in ('dodge', 'jump', 'vehicle', 'vehicle_out')]
        area['hud_roundel_glyph_nacre'] = [round(g[1], 1) for g in gl]
        area['hud_roundel_glyph_outside_disc_share'] = [round(g[2], 4) for g in gl]
        area['hud_roundel_lacquer_each'] = round(math.pi * (ROUNDEL['d'] / 2) ** 2, 1)
    _, mn, ml = minimap_frame(1.0)
    area['minimap_frame_nacre'] = round(mn, 1)
    area['minimap_frame_lacquer'] = round(ml, 1)
    sel = select_frame(SLOT['chip'], SLOT['chip'], 1.0)
    area['select_frame_nacre'] = round(float(sel[..., 3].sum()), 1)
    w_, h_, p_ = L.najeon_chrys(22)
    area['chrys_r22_nacre'] = round(float(L.render_pieces(w_, h_, p_)[..., 3].sum()), 1)
    w_, h_, p_ = L.najeon_plum(6)
    area['plum_r6_nacre'] = round(float(L.render_pieces(w_, h_, p_)[..., 3].sum()), 1)
    w_, h_, p_ = L.najeon_petal(7, 10)
    area['petal_pip_nacre'] = round(float(L.render_pieces(w_, h_, p_)[..., 3].sum()), 1)
    rail = L.strips([(0, 2), (1792, 2)], 2.0, piece=12)
    area['rail_line_nacre'] = round(sum(abs((p['poly'][1][0] - p['poly'][0][0])) * 2.0 for p in rail), 1)
    _, pl = plaque(200, 200, 1.0)
    area['plaque200_inlay'] = round(pl, 1)
    screen = 1920 * 1080.0
    hud_n = area.get('hud_collar_nacre', 0) + sum(area.get('hud_roundel_glyph_nacre', [0, 0, 0])[:3]) + area['minimap_frame_nacre']
    hud_l = area.get('hud_collar_lacquer', 0) + 3 * area.get('hud_roundel_lacquer_each', 0) + area['minimap_frame_lacquer']
    menu_n = area['rail_line_nacre'] + area['select_frame_nacre'] + 15 * area['petal_pip_nacre'] + area['chrys_r22_nacre']
    rep['ornament_area'] = dict(area, hud_nacre_total=round(hud_n, 1), hud_nacre_screen_pct=round(hud_n / screen * 100, 3),
                                hud_lacquer_total=round(hud_l, 1), hud_lacquer_screen_pct=round(hud_l / screen * 100, 3),
                                menu_nacre_total=round(menu_n, 1), menu_nacre_screen_pct=round(menu_n / screen * 100, 3))
    cells = dict(frame_mini=(128, 128), frame_board=(192, 192), line_najeon=(256, 8), line_najeon_thin=(256, 8),
                 corner_fret=(32, 32), piece_north=(32, 32), plate_lacquer=(64, 64), plate_round=(96, 96),
                 plaque_porcelain=(96, 96), plaque_porcelain_single=(96, 96), keycap_porcelain=(64, 64),
                 frame_select=(64, 64), pip_petal=(24, 28), motif_chrys=(96, 96), motif_plum=(48, 48),
                 border_fret=(200, 28), border_vine=(176, 56), motif_gwigap=(96, 64), sanggam_lotus=(256, 34),
                 sanggam_stamps=(224, 22), sanggam_vine=(216, 42), white=(8, 8))
    used = sum((w + 4) * (h + 4) for w, h in cells.values())      # 2 px padding on every side
    rep['memory'] = dict(atlas_cells_px=cells, atlas_cells_area_padded=used,
                         atlas_target_px=[512, 512], atlas_fill=round(used / (512 * 512.0), 3),
                         atlas_rgba32=512 * 512 * 4, atlas_rgba32_mips=int(512 * 512 * 4 * 4 / 3),
                         atlas_bc7_mips=int(512 * 512 * 4 / 3), atlas_cap_px=[1024, 512],
                         atlas_cap_rgba32_mips=int(1024 * 512 * 4 * 4 / 3),
                         lattice_r8_mips=rep['lattice_bytes_r8_with_mips'])
    rep['hud_mock_imported'] = HUD_OK
    return rep


# ---------------------------------------------------------------------------------------------------------------------
# sheet
# ---------------------------------------------------------------------------------------------------------------------
SW, PADX = 2400, 48
BG = L.hexc('#CFCBC0')
TXT = L.hexc('#141413')
SUB = L.hexc('#4A4740')


class Sheet:
    def __init__(self):
        self.parts = []          # (y, array)
        self.labels = []         # (x, y, text, size, face, colour)
        self.y = 0

    def band(self, h, col=BG):
        a = flat(SW, int(h), col)
        y0 = self.y
        self.parts.append((y0, a))
        self.y += int(h)
        return a, y0

    def text(self, x, y, s, size=20, face='sans', col=TXT):
        self.labels.append((x, y, s, size, face, col))

    def head(self, title, sub=None):
        a, y0 = self.band(86 if sub else 60)
        self.text(PADX, y0 + 14, title, 30, 'serif')
        if sub:
            self.text(PADX, y0 + 54, sub, 18, 'sans', SUB)

    def build(self):
        img = np.concatenate([a for _, a in self.parts], axis=0)
        im = Image.fromarray((np.clip(img, 0, 1) * 255 + .5).astype(np.uint8))
        d = ImageDraw.Draw(im)
        for x, y, s, size, face, col in self.labels:
            d.text((x, y), s, font=font(size, face), fill=c8(col))
        return im


def panel_materials(sh, rep):
    sh.head('가. 재질 — 옻칠 · 자개 · 백자 · 상감 · 살대, 그리고 그대로 남는 #304 토큰',
            '모든 값은 LDR sRGB. 발광·HDR·광택 그라데이션 없음. 자개의 빛깔은 OKLCH 명도 %.3f · 채도 %.3f에서 색상만 %d°–%d° 호 위를 오간다 (최대 채널 %d, 한지 230 이하).'
            % (L.NACRE['L'], L.NACRE['C'], L.NACRE['H_centre'] - L.NACRE['H_span'] / 2, L.NACRE['H_centre'] + L.NACRE['H_span'] / 2,
               rep['nacre']['max_channel']))
    a, y0 = sh.band(300)
    sw = [('옻칠 Lacquer', T['Lacquer']), ('자개 Nacre', NACRE), ('백자 Porcelain', T['Porcelain']),
          ('백자 그늘', T['PorcelainShade']), ('흑상감 InlayDark', T['InlayDark']), ('철사 상감 InlayIron', T['InlayIron']),
          ('살대 Wood', T['Wood'])]
    x = PADX
    for name, col in sw:
        rect(a, x, 10, 150, 96, col)
        rect(a, x, 10, 150, 1, TXT, .25)
        sh.text(x, y0 + 112, name, 18, 'bold')
        sh.text(x, y0 + 136, L.to_hex(col), 18, 'sans', SUB)
        x += 170
    x += 30
    sh.text(x, y0 + 112, '#304 (그대로)', 18, 'bold')
    for i, k in enumerate(('Veil', 'Ink', 'Paper', 'Sheet', 'Chip', 'Mist', 'Ash', 'Cinnabar')):
        rect(a, x + i * 104, 10, 96, 96, T[k])
        sh.text(x + i * 104, y0 + 136, L.to_hex(T[k]), 15, 'sans', SUB)
    # nacre arc strip, families, grain close-up
    hs = np.linspace(L.NACRE['H_centre'] - L.NACRE['H_span'] / 2, L.NACRE['H_centre'] + L.NACRE['H_span'] / 2, 700)
    strip = np.array([L.oklch(np.float64(L.NACRE['L']), L.NACRE['C'], h) for h in hs])
    a[176:206, PADX:PADX + 700] = strip[None, :, :]
    sh.text(PADX, y0 + 212, '자개 색상 호 (녹 150° → 청 245° → 분홍 340°). 노랑·주황 쪽은 쓰지 않는다.', 16, 'sans', SUB)
    # one strip per family at 6x and a "flat" comparison
    x = PADX + 760
    rect(a, x - 12, 164, 780, 124, T['Lacquer'])
    for i in range(3):
        pcs = L.strips([(4, 6 + i * 9), (120, 6 + i * 9)], 5.0, piece=22, fam0=i)
        na = L.render_pieces(126, 34, [dict(p, fam=i) for p in pcs], s=6, ss=2, seed=L.SEED + 20 + i)
        paste(a, na[int(i * 54):int(i * 54) + 44], x, 172 + i * 38)
    sh.text(x + 790, y0 + 172, '조각 확대 ×6 (녹 · 청 · 분홍 계열)', 16, 'sans', SUB)
    sh.text(x + 790, y0 + 196, '결: 조각 축을 가로질러 %.1f px 주기, 명도 ±%.3f' % (L.NACRE['grain_period'], L.NACRE['grain_dL']), 16, 'sans', SUB)
    sh.text(x + 790, y0 + 220, '빛깔: 조각 축을 따라 %.0f px 주기, 색상 ±%.0f°' % (L.NACRE['hue_period'], L.NACRE['piece_span'] / 2), 16, 'sans', SUB)
    sh.text(x + 790, y0 + 244, '이음: 조각 사이 옻칠 %.1f px (끊음질)' % L.NACRE['joint_px'], 16, 'sans', SUB)


def panel_lattice(sh, rep):
    sh.head('나. 문살 8종 — 모듈 격자 위의 구성 규칙으로 직접 생성 (레퍼런스 그림은 무늬 계열만 참고, 추적·복제 없음)',
            '윗줄: 살대(Wood) + 창호지(Chip), 타일 텍셀 1:1 (디자인 px ×2). 아랫줄: 같은 타일을 장막 위에 (살대 / 장막 = %.2f:1). 모든 타일 이음매 오차 0.'
            % rep['materials']['살대 / 장막(불투명)'])
    a, y0 = sh.band(470)
    x = PADX
    for name in L.LATTICE_ORDER:
        f = L.LATTICE[name]
        t = L.lattice_tile(name)
        m = tile_fill(t, 256, 256)
        p = flat(256, 256, T['Chip'])
        p = p * (1 - m[..., None]) + T['Wood'] * m[..., None]
        a[10:266, x:x + 256] = p
        m2 = tile_fill(t, 256, 72)
        q = flat(256, 72, T['Veil'])
        q = q * (1 - m2[..., None]) + T['Wood'] * m2[..., None]
        a[276:348, x:x + 256] = q
        r = rep['lattice'][name]
        sh.text(x, y0 + 356, '%s %s' % (f['ko'], f['hanja']), 20, 'bold')
        sh.text(x, y0 + 384, '모듈 %g px · 살 %g px (%.3f)' % (f['u'], f['bar'], f['bar'] / f['u']), 15, 'sans', SUB)
        sh.text(x, y0 + 406, '주기 %g×%g px · 타일 %d×%d' % (r['period_design_px'][0], r['period_design_px'][1], r['tile_px'][0], r['tile_px'][1]), 15, 'sans', SUB)
        sh.text(x, y0 + 428, '열림 %.0f %%' % (r['open_ratio'] * 100), 15, 'sans', SUB)
        x += 290


def panel_najeon(sh, rep):
    sh.head('다. 나전 무늬 — 자개 조각(끊음질 = 곧은 띠, 줄음질 = 오려 낸 꼴)으로 지은 가는 테·모서리 무늬',
            '위: 확대 ×3. 아래: 1080p 실제 크기. 바탕 옻칠 %s. 조각마다 색상 위상·결 위상·명도 치우침이 씨앗 308에서 정해진다.' % L.TOK308['Lacquer'])
    a, y0 = sh.band(380)
    rect(a, PADX - 12, 6, SW - 2 * PADX + 24, 364, T['Lacquer'])
    items = [('뇌문 테 (끊음질)', L.najeon_fret(7)), ('당초 테 (줄음질)', L.najeon_vine(3)), ('국화', L.najeon_chrys(22)),
             ('매화', L.najeon_plum(10)), ('꽃잎(단계)', L.najeon_petal(7, 10)), ('구름', L.najeon_cloud(1.0)),
             ('귀갑', L.najeon_gwigap(4, 2))]
    x = PADX + 10
    for i, (name, (w, h, pcs)) in enumerate(items):
        big = L.render_pieces(w, h, pcs, s=3, ss=3, seed=L.SEED + 30 + i)
        paste(a, big, x, 28)
        one = L.render_pieces(w, h, pcs, s=1, ss=4, seed=L.SEED + 30 + i)
        paste(a, one, x, 262)
        sh.text(x, y0 + 318, name, 18, 'bold', T['Paper'])
        sh.text(x, y0 + 342, '%d×%d px · 조각 %d' % (round(w), round(h), len(pcs)), 15, 'sans', T['Mist'])
        x += max(int(w * 3) + 44, 190)


def panel_inlay(sh, rep):
    sh.head('라. 상감 무늬 — 백자 바탕에 파서 메운 선 (흑상감 %s %.2f:1 · 철사 %s %.2f:1)' % (
        L.TOK308['InlayDark'], rep['materials']['흑상감 / 백자'], L.TOK308['InlayIron'], rep['materials']['철사 상감 / 백자']),
        '선은 붓 가장자리가 아니라 칼로 판 가장자리다(번짐·갈필 없음). 철사 상감은 재질색이지 신호색이 아니다 — 초점·경고·상태에 쓰지 않는다.')
    a, y0 = sh.band(330)
    rect(a, PADX - 12, 6, SW - 2 * PADX + 24, 314, T['Porcelain'])
    rect(a, PADX - 12, 6 + 314 - 4, SW - 2 * PADX + 24, 4, T['PorcelainShade'])
    x = PADX + 10
    pl, _ = plaque(150, 96, s=2)
    paste(a, pl, x, 26)
    sh.text(x, y0 + 232, '이중선 테 (판·그림 틀)', 18, 'bold')
    sh.text(x, y0 + 256, '바깥 2 · 틈 2.5 · 안 1 px, 가장자리에서 7 px', 15, 'sans', SUB)
    x += 340
    for name, m, note in (('연판 띠', L.inlay_lotus(7, s=2), '꽃잎 16×13 px · 선 1.3'),
                          ('인화 국화 줄', L.inlay_stamps(8, s=2), '간격 14 px · 점 9개'),
                          ('당초 선', L.inlay_vine(3, s=2), '주기 36 px · 선 1.3')):
        paste(a, tint(m, T['InlayDark']), x, 40)
        paste(a, tint(m, T['InlayIron']), x, 120)
        m1 = resize_mask(m, m.shape[1] // 2, m.shape[0] // 2)
        paste(a, tint(m1, T['InlayDark']), x, 190)
        sh.text(x, y0 + 232, name, 18, 'bold')
        sh.text(x, y0 + 256, note, 15, 'sans', SUB)
        x += m.shape[1] + 60
    kc = keycap(40, 2)
    paste(a, kc, x, 40)
    kc2 = keycap(78, 2)
    paste(a, kc2, x + 100, 40)
    sh.text(x + 28, y0 + 54, 'Q', 36, 'bold', T['InlayDark'])
    sh.text(x + 124, y0 + 54, 'Enter', 36, 'bold', T['InlayDark'])
    sh.text(x, y0 + 232, '건반 = 백자 단추', 18, 'bold')
    sh.text(x, y0 + 256, '면 백자 · 테 흑상감 2 · 턱 5 px', 15, 'sans', SUB)


def hud_cluster(bg_kind, s):
    """The #308 cluster with the theme hardware, on a flat sky / pine ground or on the road still."""
    region = HM.CROP
    if bg_kind == 'road':
        dst = HM.still_region(region, s)
    else:
        dst = HM.flat(region, s, L.SKY if bg_kind == 'sky' else L.PINE)
    dst = np.array(dst, float)
    x0, y0 = region[0], region[1]
    for kind, st in (('hp', HM.HP_N), ('ink', HM.INK_N)):
        rx, ry, rw, rh = HM.RECT[kind]
        v = HM.render_vessel(kind, st, s=s)
        paste(dst, v, (rx - HM.PAD - x0) * s, (ry - HM.PAD - y0) * s)
        cl, _, _ = collar(kind, s)
        paste(dst, cl, (rx - HM.PAD - x0) * s, (ry - HM.PAD - y0) * s)
    marks = (('dodge', 'wet', 1.0), ('jump', 'dry', 0.0), ('vehicle', 'rewet', .55))
    for (mx, my), (kind, state, fill) in zip(HM.mark_centres(), marks):
        r_, _, _ = roundel(kind, state, fill, s=s)
        paste(dst, r_, (mx - ROUNDEL['d'] / 2 - x0) * s, (my - ROUNDEL['d'] / 2 - y0) * s)
    return dst


def panel_hud(sh, rep):
    sh.head('마. HUD 위의 테마 — 유리 몸통은 그대로, 목테만 옻칠 + 자개 한 줄 · 표시 셋은 옻칠 단추 + 자개 기호 · 미니맵은 옻칠 원테',
            '×2 확대. 왼쪽부터 밝은 하늘 %s · 검은 솔숲 %s · 실제 길 장면. 옻칠 / 하늘 %.1f:1, 자개 / 옻칠 %.1f:1, 한지 실테 / 솔숲 %.1f:1. 군집의 표시: 회피 = 젖음, 점프 = 마름(α.30), 자동차 = 되젖음 55 %%.'
            % (L.to_hex(L.SKY), L.to_hex(L.PINE), rep['materials']['옻칠 / 밝은 하늘'], rep['materials']['자개 / 옻칠'],
               rep['materials']['한지 테 α.92 / 솔숲']))
    a, y0 = sh.band(560)
    x = PADX
    if HUD_OK:
        for kind in ('sky', 'pine', 'road'):
            c_ = hud_cluster(kind, 2.0)
            a[10:10 + c_.shape[0], x:x + c_.shape[1]] = c_
            x += c_.shape[1] + 24
        # the three states of one roundel on both grounds, x3
        for row, bgc in enumerate((L.SKY, L.PINE)):
            rect(a, x, 10 + row * 180, 3 * 150 + 20, 164, bgc)
            for col, (state, fill) in enumerate((('wet', 1.0), ('rewet', .5), ('dry', 0.0))):
                r_, _, _ = roundel('dodge', state, fill, s=3.0, ss=3)
                paste(a, r_, x + 18 + col * 150, 26 + row * 180)
        sh.text(x, y0 + 376, '표시 한 개의 세 상태 ×3', 18, 'bold')
        sh.text(x, y0 + 402, '젖음 = 자개가 박혀 있다', 16, 'sans', SUB)
        sh.text(x, y0 + 424, '되젖음 = 획 순서대로 다시 박힌다', 16, 'sans', SUB)
        sh.text(x, y0 + 446, '마름 = 자개 α.30 (%.2f:1, 빠진 자리)' % rep['materials']['자개 마름(α.30) / 옻칠'], 16, 'sans', SUB)
        sh.text(x, y0 + 468, '빈 자리(잠김) = 단추째 그리지 않는다', 16, 'sans', SUB)
    else:
        sh.text(x, y0 + 20, 'hud308_mock import failed: ' + HUD_ERR[:120], 16, 'sans', T['Cinnabar'])
    sh.text(PADX, y0 + 536, '군집 (왼쪽 아래). 유리·액체·수면은 SPEC-HUD-LIQUID-308 그대로이고 목테(목 + 입술)만 옻칠이 된다. 마개와 받침은 두지 않는다(붓는 실 · 안전 영역 4 px).', 16, 'sans', SUB)
    a, y0 = sh.band(560)
    x = PADX
    mr, _, _ = minimap_frame(2.0)
    for kind in ('sky', 'pine', 'road'):
        if kind == 'road' and HUD_OK:
            st = np.array(HM.still_region((1574, 772, 1906, 1028), 2.0), float)
            a[10:10 + st.shape[0], x:x + st.shape[1]] = st
        else:
            rect(a, x, 10, 664, 512, L.SKY if kind == 'sky' else L.PINE)
        paste(a, mr, x + 332 - mr.shape[1] / 2, 266 - mr.shape[0] / 2)
        x += 688
    mr1, _, _ = minimap_frame(1.0)
    for row, bgc in enumerate((L.SKY, L.PINE)):
        rect(a, x, 10 + row * 262, 248, 252, bgc)
        paste(a, mr1[:, :240], x + 8, 136 + row * 262 - mr1.shape[0] / 2)
    sh.text(PADX, y0 + 530, '미니맵 틀 (×2, 맨 오른쪽 = 1080p 실제 크기의 왼쪽 부분). 모양·자리·크기는 SPEC-MAP-OVERHAUL-308 §5 소유: 직사각 옻칠 문틀 9 px, 창 바로 밖 끊음질 선 1.5 px, 네 귀 아자 꺾임 14 px, 윗변의 북쪽 자개 조각 12 px. 안쪽 지도는 자리 표시다.',
            16, 'sans', SUB)


def panel_menu(sh, rep):
    sh.head('바. 메뉴 위의 테마 — 칸 격자 = 살대 + 창호지 · 선택 유지 = 자개 테 · 그림 틀·풀이 판·건반 = 백자 상감 · 레일 = 끊음질 한 줄',
            '×1 (1080p 실제 크기). 초점(주사 붓 테 + 방점)은 #304 그대로다. 왼쪽 칸부터: 기본 · 초점 · 선택 유지(자개 테) · 빈 칸(창호지 α.16) · 잠긴 칸(종이 없는 빗살).')
    a, y0 = sh.band(470)
    veil = L.over(T['Veil'], L.hexc('#6B675E'), .93)
    rect(a, PADX - 12, 6, SW - 2 * PADX + 24, 454, veil)
    # rail with a cut-shell line
    rail = L.strips([(2, 3), (1500, 3)], 2.0, piece=12)
    paste(a, L.render_pieces(1504, 7, rail, s=1, ss=4, seed=L.SEED + 40), PADX + 10, 64)
    xs_ = PADX + 60
    for name, sel in (('일시정지', 0), ('소지품', 1), ('술식', 0), ('차패', 0), ('지도', 0), ('설정', 0)):
        if sel:
            rect(a, xs_ - 16, 50, 150, 14, T['Paper'], .95)
            sh.text(xs_, y0 + 6, name, 34, 'serif', T['Paper'])
            xs_ += 170
        else:
            sh.text(xs_, y0 + 20, name, 24, 'serif6', T['Mist'])
            xs_ += 24 * len(name) + 44
    kq = keycap(34, 1)
    paste(a, kq[:34], PADX + 12, 22)
    sh.text(PADX + 22, y0 + 26, 'Q', 16, 'bold', T['InlayDark'])
    # slot row
    gx, gy = PADX + 40, 120
    p, ch = SLOT['pitch'], SLOT['chip']
    n = 5
    for i in range(n + 1):
        rect(a, gx + i * p - (p - ch) / 2 - SLOT['bar_v'] / 2, gy - 12, SLOT['bar_v'], ch + 12 + 42, T['Wood'])
    rect(a, gx - (p - ch) / 2 - 2, gy - 12, n * p + 4, SLOT['bar_h'], T['Wood'])
    rect(a, gx - (p - ch) / 2 - 2, gy + ch + 39, n * p + 4, SLOT['bar_h'], T['Wood'])
    hanja = '木火土金水'
    names = ('목', '화', '토', '금', '수')
    for i in range(n):
        cx = gx + i * p
        if i == 3:
            rect(a, cx, gy, ch, ch, T['Chip'], .16)
        elif i == 4:
            m = tile_fill(resize_mask(L.lattice_tile('bit'), 16, 16), int(ch), int(ch))
            paste(a, tint(m, T['Wood']), cx, gy)
        else:
            rect(a, cx, gy, ch, ch, T['Chip'])
            sh.text(cx + 24, y0 + gy + 14, hanja[i], 56, 'serif9', T['Ink'])
        sh.text(cx + (26 if i == 1 else 0), y0 + gy + ch + 8, names[i], 20, 'sans', T['Mist'] if i < 4 else T['Off'])
        if i == 1:      # focus: four cinnabar strokes crossing the corners by 12 px + the dab
            for (rx, ry, rw_, rh_) in ((cx - 12, gy - 3, ch + 24, 4), (cx - 12, gy + ch - 1, ch + 24, 4),
                                       (cx - 3, gy - 12, 4, ch + 24), (cx + ch - 1, gy - 12, 4, ch + 24)):
                rect(a, rx, ry, rw_, rh_, T['Cinnabar'])
            im = Image.new('L', (34 * 4, 26 * 4), 0)
            ImageDraw.Draw(im).ellipse([8, 20, 128, 84], fill=255)
            dab = np.asarray(im.rotate(14, resample=Image.BICUBIC).resize((34, 26), Image.LANCZOS), float) / 255
            paste(a, tint(dab, T['Cinnabar']), cx - 10, gy + ch + 8)
        if i == 2:
            sf = select_frame(ch, ch, 1.0)
            paste(a, sf, cx - SELECT['out'] - 2, gy - SELECT['out'] - 2)
    # tier pips under the first cell: 2 of 3 reached
    w_, h_, p_ = L.najeon_petal(7, 10)
    for j in range(3):
        pip = L.render_pieces(w_, h_, p_, s=1, ss=4, seed=L.SEED + 50 + j, alpha=1.0 if j < 2 else 0.0)
        paste(a, pip, gx + 34 + j * 14, gy + ch + 12)
        if j >= 2:
            pm = (pip[..., 3] * 0)
            rect(a, gx + 34 + j * 14 + 3, gy + ch + 16, 4, 8, T['Mist'], .35)
    # porcelain plaque (detail picture frame), tooltip plaque, key legend
    px_ = gx + n * p + 70
    pl, _ = plaque(200, 200, 1)
    paste(a, pl, px_, 110)
    sh.text(px_ + 52, y0 + 150, '木', 96, 'serif9', T['InlayDark'])
    sh.text(px_, y0 + 322, '그림 틀 200² (백자 판 + 이중선)', 16, 'sans', T['Mist'])
    tx = px_ + 250
    tp, _ = plaque(330, 96, 1, single=True)
    paste(a, tp, tx, 110)
    sh.text(tx + 22, y0 + 126, '모든 속성 위력 +6%', 24, 'serif', T['Ink'])
    sh.text(tx + 22, y0 + 162, '기본 +4% · 강화 +2%', 20, 'sans', T['Ash'])
    sh.text(tx, y0 + 216, '풀이 판 (백자 판 + 한 줄선). 먹 / 백자 %.1f:1 · 재 / 백자 %.2f:1'
            % (L.contrast(T['Ink'], T['Porcelain']), L.contrast(T['Ash'], T['Porcelain'])), 16, 'sans', T['Mist'])
    kx = tx
    for lab, wv, verb in (('Enter', 70, '바꿔 끼기'), ('X', 34, '빼기')):
        kc = keycap(wv, 1)
        paste(a, kc[:34], kx, 262)
        sh.text(kx + 9, y0 + 266, lab, 16, 'bold', T['InlayDark'])
        sh.text(kx + wv + 10, y0 + 264, verb, 22, 'serif6', T['Paper'])
        kx += wv + 10 + 22 * len(verb) + 34
    # full-map frame corner: door frame + lattice band + inner frame round the paper map, a chrysanthemum stud
    fx, fy = tx + 440, 100
    FW, FH = 520, 300
    bc = map_board_corner(FW, FH, 1.0)
    fx, fy = int(fx), int(fy)
    a[fy:fy + FH, fx:fx + FW] = bc[:FH, :FW]
    sh.text(fx + 60, y0 + fy + 44, '강토 지도 · 청림', 24, 'serif', T['Ink'])
    sh.text(fx + 60, y0 + fy + 84, '칠반: 종이 밖 18 px 띠', 16, 'sans', T['Ash'])
    sh.text(fx + 60, y0 + fy + 108, '끊음질 선 1.5 px (귀에서 아자 꺾임) + 완자살 띠 10 px', 16, 'sans', T['Ash'])
    sh.text(fx, y0 + fy + FH + 10, '펼친 지도 판의 왼쪽 위 귀 (SPEC-MAP-OVERHAUL-308 §5의 칠반에 키트 값을 넣은 것)', 16, 'sans', T['Mist'])


def panel_legibility(sh, rep):
    cap = rep['pattern_cap']
    sh.head('사. 글 밑 무늬 상한 — %.2f:1 (묶는 조건: %s, %.3f까지 가능)' % (cap['cap_test'], cap['binding'], cap['raw']),
            '왼쪽 = 상한 1.15:1의 무늬 위 글, 오른쪽 = 1.50:1 (금지 예). 20 px 보조 글자(안개·재) 기준. 기본값은 "글 밑은 민 바탕"이고, 상한은 넘지 못할 천장이다.')
    a, y0 = sh.band(250)
    veil90 = L.over(T['Veil'], L.SKY, .90)
    samples = [('장막 α.90 + 완자살', veil90, T['Paper'], T['Mist'], T['Paper'], 'wanja', 8),
               ('생지 + 정자살', T['Sheet'], T['Ink'], T['Ash'], T['Ink'], 'jeongja', 16)]
    x = PADX
    for name, ground, tcol, tsub, toward, fam, u in samples:
        for ratio in (1.15, 1.50):
            pat, _ = grey_at_ratio(ground, toward, ratio)
            m = tile_fill(L.lattice_tile(fam, scale=1), 540, 150)
            p = flat(540, 150, ground)
            p = p * (1 - m[..., None]) + pat * m[..., None]
            a[10:160, x:x + 540] = p
            sh.text(x + 20, y0 + 26, '먹 회복 초당 5', 28, 'serif6', tcol)
            sh.text(x + 20, y0 + 72, '착용 중인 닳은 붓과 비교 · 조선통보 1,240', 20, 'sans', tsub)
            sh.text(x + 20, y0 + 108, '쉼터 곁에서만 열린다', 20, 'sans', tsub)
            sh.text(x, y0 + 168, '%s · 무늬 %.2f:1 · 보조 글자 %.2f:1 · 주 글자 %.2f:1' % (
                name, ratio, L.contrast(tsub, pat), L.contrast(tcol, pat)), 16, 'bold' if ratio > 1.2 else 'sans',
                T['Cinnabar'] if L.contrast(tsub, pat) < 4.5 else SUB)
            x += 564
    sh.text(PADX, y0 + 204, '글 상자와의 거리: 조용한 무늬(≤ 1.15:1)는 글 밑 허용 · 구조선(살대·백자 그늘, ≤ 2.2:1)은 8 px 이상 · 장식(자개·상감·종이 위 살대, > 2.2:1)은 16 px 이상 띄운다.',
            17, 'sans', TXT)


def build_sheet(rep):
    sh = Sheet()
    a, y0 = sh.band(120)
    sh.text(PADX, y0 + 22, '오행부 #308 UI 테마 — 나전칠기 · 백자 상감 · 자개 공예 · 창호 (Spec 보조 도해 v0)', 40, 'serif9')
    sh.text(PADX, y0 + 78, 'SPEC-UI-THEME-308 · DECISIONS D308-15 · 2026-10-04 · TEST · numpy + PIL 절차 생성(씨앗 308, 결정적) · sRGB 합성 · 게임 캡처가 아니며 시안(목업)도 아니다',
            18, 'sans', SUB)
    panel_materials(sh, rep)
    panel_lattice(sh, rep)
    panel_najeon(sh, rep)
    panel_inlay(sh, rep)
    panel_hud(sh, rep)
    panel_menu(sh, rep)
    panel_legibility(sh, rep)
    sh.band(30)
    return sh.build()


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, 'PNG', optimize=False)
    return b.getvalue()


def dump(obj):
    return json.dumps(obj, ensure_ascii=False, indent=1, sort_keys=False)


def main():
    rep = numbers()
    if '--numbers' in sys.argv:
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, 'spec_theme_v0.json'), 'w', encoding='utf-8', newline='\n') as f:
            f.write(dump(rep) + '\n')
        print(dump(rep))
        return 0
    b1 = png_bytes(build_sheet(rep))
    if '--check' in sys.argv:
        rep2 = numbers()
        b2 = png_bytes(build_sheet(rep2))
        same = (b1 == b2) and (dump(rep) == dump(rep2))
        print('determinism:', 'same bytes' if same else 'DIFFERENT', hashlib.sha256(b1).hexdigest()[:16])
        return 0 if same else 1
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'spec_theme_v0.png'), 'wb') as f:
        f.write(b1)
    im = Image.open(io.BytesIO(b1)).convert('RGB')
    im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(os.path.join(OUT, 'spec_theme_v0_view.jpg'), quality=90)
    rep['sheet'] = dict(file='Art/UI308/Theme/spec_theme_v0.png', size=[im.width, im.height], sha256=hashlib.sha256(b1).hexdigest())
    with open(os.path.join(OUT, 'spec_theme_v0.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(dump(rep) + '\n')
    print('wrote', os.path.join(OUT, 'spec_theme_v0.png'), im.size, rep['sheet']['sha256'][:16])
    return 0


if __name__ == '__main__':
    sys.exit(main())
