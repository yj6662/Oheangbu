# -*- coding: utf-8 -*-
"""SPEC-HUD-LIQUID-308 x SPEC-UI-THEME-308 (D308-15): the HUD's najeon pieces. Deterministic (numpy + PIL, seeded), no image
file is read, nothing is traced: every pictogram is a list of cut shell pieces built from coordinates.

What lives here
  BUTTON              geometry of the three action marks as najeon lids (design px of the 44 px mark rect)
  pieces(kind)        the pictogram of a mark as cut shell pieces (julreumjil) + the cut-shell line near the rim (kkeuneumjil)
  render(kind, ...)   those pieces -> straight-alpha RGBA: rgb = the kit's nacre colour model (theme308_lib.nacre_rgb, one hue
                      family per piece from a seeded draw, wide faces at the kit's face chroma), a = shell coverage.
                      Where two pieces meet, the later one cuts a hairline of lacquer out of the earlier one (the butt joint)
  cell(kind)          the atlas cell hud308_assets.py bakes (44 design px at 3 atlas px per design px, colour bled under alpha)
  kit_roundel(kind)   the kit review's first form (theme308_sheet.roundel: the INK pictogram scaled by .74, one shell face),
                      kept only as the "before" of the detail measurement
  detail(kind)        what survives at 1080p real size: shell components and enclosed lacquer counters at 1 px per design px
                      against the 8x drawing (kit review weakness 4)

Why the pictograms are redrawn instead of scaled: a shell piece cannot be cut thinner than about 1.5 px at 1080p, so the
lid's pictogram is built from pieces that are at least that wide with gaps at least that wide (a cross-spoked wheel instead of
three hairline spokes, window slots instead of cut-outs), and every piece gets its own hue family: a lid of fitted shell, not
a pale icon on a black button.

usage (repo root):  python Tools/Art/hud308_najeon.py            numbers (areas, detail at 1080p)
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import theme308_lib as L   # noqa: E402  (the kit's library: colour model, strips, tokens)

SEED = 308
U = 44.0                       # the mark rect, design px (LayoutSpec308.MarkSize); pieces are written in this space, y down
C = U / 2.0
# TEST numbers (design px). The lid = the mark rect's inscribed circle; the shell line sits 2 px inside its edge.
BUTTON = dict(
    radius=22.0,               # lacquer lid (drawn by the shader, analytic edge)
    ring_r=19.25,              # centre radius of the cut-shell line
    ring_w=1.5,                # its width (kit: NacreLineThin)
    ring_piece=10.1,           # target length of one cut piece (12 pieces round the lid)
    ring_inner=18.5,           # everything further out than this is the rim line: it never dims with the state
    glyph_r=17.0,              # the pictogram stays inside this radius (1.5 px of lacquer to the line)
    dry_alpha=0.30,            # a mark that cannot be used now: the pictogram's shell has fallen out, its seat shows (kit)
    cell_px=44.0,              # the baked cell covers the central 44 px of the 52 px mark quad
)
ATLAS_PX = 3                   # atlas px per design px in a najeon cell
SS = 4
KINDS = ('dodge', 'jump', 'vehicle', 'vehicle_out')
RING_START = dict(dodge=-71.0, jump=-83.0, vehicle=-64.0, vehicle_out=-77.0)     # joints differ from lid to lid
FACE = L.NACRE_FACE_CHROMA     # wide faces keep 60 % of the line chroma (kit rule)
MIN_PIECE = 1.5                # px: narrowest shell feature and narrowest lacquer gap that is meant to be read


# ---- piece builders (44-space, y down) ----------------------------------------------------------------------------------
def circle(cx, cy, r, n=28):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def capsule(p0, p1, w0, w1, n=7):
    """A tapered limb from p0 (width w0) to p1 (width w1) with round ends."""
    ax, ay = p0
    bx, by = p1
    a = math.atan2(by - ay, bx - ax)
    pts = []
    for i in range(n + 1):                               # cap round p1
        t = a - math.pi / 2 + math.pi * i / n
        pts.append((bx + w1 / 2 * math.cos(t), by + w1 / 2 * math.sin(t)))
    for i in range(n + 1):                               # cap round p0
        t = a + math.pi / 2 + math.pi * i / n
        pts.append((ax + w0 / 2 * math.cos(t), ay + w0 / 2 * math.sin(t)))
    return pts


def P(poly, axis=0.0, **kw):
    return dict(poly=poly, axis=axis, **kw)


def limb(p0, p1, w0, w1, **kw):
    return P(capsule(p0, p1, w0, w1), math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0])), **kw)


def box(x0, y0, x1, y1, **kw):
    return P([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], 0.0, **kw)


def tf(pcs, s, ox, oy):
    return [dict(p, poly=[(x * s + ox, y * s + oy) for x, y in p['poly']]) for p in pcs]


def ring(kind):
    b = BUTTON
    a0 = RING_START[kind]
    return [dict(p, line=True) for p in L.arc_strips(C, C, b['ring_r'], a0, a0 + 360.0, b['ring_w'], piece=b['ring_piece'])]


def cart(detail=True):
    """The palanquin cart, side view, as shell: upturned roof (two pieces), box with two window slots, one wheel, a shaft."""
    roof_l = [(7.0, 14.2), (9.8, 13.1), (12.6, 10.6), (15.0, 9.2), (22.0, 9.2), (22.0, 14.6), (13.0, 14.6), (8.0, 15.9)]
    roof_r = [(44.0 - x, y) for x, y in roof_l][::-1]
    pcs = [P(roof_l, 8.0), P(roof_r, -8.0)]
    wx, wy = 22.0, 30.6
    if detail:
        pcs += [box(11.0, 16.4, 17.9, 26.2), box(17.9, 16.4, 26.1, 26.2), box(26.1, 16.4, 33.0, 26.2)]   # three panels
        pcs += [box(12.6, 18.4, 16.2, 21.0, cut=True), box(27.8, 18.4, 31.4, 21.0, cut=True)]              # window slots (lacquer)
        pcs.append(limb((33.2, 24.4), (37.4, 26.6), 2.3, 1.7))                                             # shaft
        pcs.append(P(circle(wx, wy, 7.2), cut=True, clear=1.5))                                            # air round the wheel
        pcs += L.arc_strips(wx, wy, 6.0, 15.0, 375.0, 2.4, piece=6.3)
        pcs += [box(wx - 4.9, wy - 0.8, wx + 4.9, wy + 0.8), box(wx - 0.8, wy - 4.9, wx + 0.8, wy + 4.9)]  # cross spokes
        pcs.append(P(circle(wx, wy, 2.0, 20)))                                                             # hub
    else:                                                # the small cart under the recall hook: no slots, no spokes, no hub
        pcs += [box(11.0, 16.4, 22.0, 26.2), box(22.0, 16.4, 33.0, 26.2)]
        pcs.append(P(circle(wx, wy, 9.2), cut=True))
        pcs += L.arc_strips(wx, wy, 5.6, 15.0, 375.0, 3.0, piece=9.0)                                      # a plain rim: one open counter
    return pcs


def pieces(kind):
    """Shell pieces of one lid, in drawing order (a later piece cuts its joint out of the earlier ones)."""
    if kind == 'vehicle':
        g = cart(True)
    elif kind == 'vehicle_out':
        # the cart is out and near: the same cart, smaller, under the rewinding hook (the recall stroke, D308-8c)
        g = tf(cart(False), .60, 8.2, 11.6)
        r, a0, a1 = 13.9, -118.0, 38.0
        g += L.arc_strips(C, C, r, a0, a1, 2.2, piece=7.6)
        h = math.radians(a0)
        hx, hy = C + r * math.cos(h), C + r * math.sin(h)
        vx, vy = math.sin(h), -math.cos(h)               # travel direction at the head: towards decreasing angle (rewinding)
        nx, ny = math.cos(h), math.sin(h)
        g.append(P([(hx + vx * 5.4, hy + vy * 5.4), (hx + nx * 3.0 - vx * .6, hy + ny * 3.0 - vy * .6),
                    (hx - nx * 3.0 - vx * .6, hy - ny * 3.0 - vy * .6)], math.degrees(h) - 90))
    elif kind == 'dodge':
        # a low leaning body running right; the sleeve and the trailing leg stream behind, one speed streak
        g = [
            limb((25.2, 18.4), (18.8, 17.0), 3.6, 3.2), limb((18.8, 17.0), (10.2, 17.7), 3.0, 1.8),       # sleeve behind
            limb((19.4, 25.8), (14.2, 29.2), 4.0, 3.2), limb((14.2, 29.2), (9.0, 30.8), 3.0, 2.0),        # trailing leg
            limb((20.4, 25.6), (26.2, 27.2), 4.0, 3.4), limb((26.2, 27.2), (27.6, 33.8), 3.2, 2.2),       # front leg
            limb((26.4, 18.7), (31.2, 22.4), 3.0, 2.6), limb((31.2, 22.4), (35.4, 20.8), 2.4, 1.8),       # leading arm
            limb((27.2, 17.4), (19.8, 25.6), 5.4, 4.8),                                                    # torso
            P(circle(29.7, 13.7, 3.3, 24)),                                                                # head
            limb((7.4, 23.6), (14.2, 23.4), 1.6, 1.6),                                                     # speed streak
        ]
    elif kind == 'jump':
        # an upright body off the ground, arms thrown up, three lift streaks, a ground line of cut strips
        g = L.strips([(13.4, 35.2), (30.6, 35.2)], 2.0, piece=6.4)
        g += [
            limb((15.7, 30.2), (15.3, 31.8), 1.6, 1.6), limb((22.0, 29.6), (22.0, 31.8), 1.6, 1.6),
            limb((28.3, 30.2), (28.7, 31.8), 1.6, 1.6),                                                    # lift streaks
            limb((20.9, 20.2), (18.5, 24.3), 4.0, 3.4), limb((18.5, 24.3), (16.2, 28.2), 3.2, 2.2),       # legs flung apart
            limb((23.1, 20.2), (25.5, 24.3), 4.0, 3.4), limb((25.5, 24.3), (27.8, 28.2), 3.2, 2.2),
            limb((20.6, 14.6), (16.4, 13.2), 3.0, 2.6), limb((16.4, 13.2), (13.4, 8.2), 2.6, 1.8),        # arms up
            limb((23.4, 14.6), (27.6, 13.2), 3.0, 2.6), limb((27.6, 13.2), (30.6, 8.2), 2.6, 1.8),
            limb((22.0, 13.4), (22.0, 20.4), 5.2, 4.6),                                                    # torso
            P(circle(22.0, 9.3, 3.2, 24)),                                                                 # head
        ]
    else:
        raise KeyError(kind)
    return ring(kind) + g


# ---- rendering ----------------------------------------------------------------------------------------------------------
def _mask(poly, n, k):
    im = Image.new('L', (n, n), 0)
    ImageDraw.Draw(im).polygon([(x * k, y * k) for x, y in poly], fill=255)
    return np.asarray(im) > 127


def _grow(m, r):
    """Binary dilation by about r samples (square structuring element, built from 3 x 3 steps)."""
    r = int(round(r))
    if r <= 0:
        return m
    im = Image.fromarray((m * 255).astype(np.uint8))
    for _ in range(r):
        im = im.filter(ImageFilter.MaxFilter(3))
    return np.asarray(im) > 127


def _chroma(rgb, k):
    lab = L.srgb_to_oklab(rgb)
    return L.oklab_to_srgb(lab[..., 0], lab[..., 1] * k, lab[..., 2] * k)


_KEY = dict(dodge=21, jump=22, vehicle=23, vehicle_out=24)


def render(kind, s=float(ATLAS_PX), ss=SS, with_ring=True):
    """One lid's shell -> (rgba, info). rgba: straight-alpha float (44 s) x (44 s); info: areas in design px^2, piece count."""
    n = int(round(U * s)) * ss
    k = s * ss
    pcs = [p for p in pieces(kind) if with_ring or not p.get('line')]
    masks = [_mask(p['poly'], n, k) for p in pcs]
    joint = L.NACRE['joint_px'] * k
    for j, p in enumerate(pcs):                               # a later piece cuts its seat out of every earlier piece
        if p.get('cut'):                                      # a lacquer hole (window slot, the air round the wheel)
            reach = _grow(masks[j], p['clear'] * k) if p.get('clear') else masks[j]
        else:                                                 # a shell piece: a hairline butt joint round it
            reach = _grow(masks[j], joint)
        for i in range(j):
            if not pcs[i].get('cut') and (masks[i] & reach).any():
                masks[i] &= ~reach
    rng_f = np.random.default_rng([SEED, _KEY[kind]])
    fam = rng_f.choice(3, size=len(pcs), p=L.NACRE_FAMILY_SHARE)
    rgb = np.zeros((n, n, 3))
    acc = np.zeros((n, n))
    ys, xs = np.mgrid[0:n, 0:n]
    X, Y = (xs + .5) / k, (ys + .5) / k
    area_line = area_face = 0.0
    count = 0
    for i, p in enumerate(pcs):
        if p.get('cut'):
            continue
        m = masks[i]
        if not m.any():
            continue
        count += 1
        rng = np.random.default_rng([SEED + _KEY[kind], i])
        ph, gph, dl = rng.random() * 2 * np.pi, rng.random() * 2 * np.pi, (rng.random() - .5) * 2 * L.NACRE['piece_dL']
        ax = math.radians(p.get('axis', 0.0))
        cx_, cy_ = np.mean([q[0] for q in p['poly']]), np.mean([q[1] for q in p['poly']])
        t = (X - cx_) * math.cos(ax) + (Y - cy_) * math.sin(ax)
        across = -(X - cx_) * math.sin(ax) + (Y - cy_) * math.cos(ax)
        # the family is ALWAYS the seeded draw (kit rule nacre.family_share 42 / 43 / 15 %: pink is the rare flash). arc_strips
        # hands out a cycling 'fam' (green, blue, pink, ...); taken as it came, the 12 rim pieces read as a candy stripe with
        # 34 % pink - the look the kit review removed from the kit's own frames (apply review 2026-10-04)
        col = L.nacre_rgb(t[m], across[m], int(fam[i]), ph, gph, dl)
        if not p.get('line'):
            col = _chroma(col, FACE)
        rgb[m] = col
        acc[m] = 1.0
        a = float(m.sum()) / (k * k)
        if p.get('line'):
            area_line += a
        else:
            area_face += a
    o = n // ss
    pre = np.dstack([rgb * acc[..., None], acc]).reshape(o, ss, o, ss, 4).mean(axis=(1, 3))
    pre[..., :3] = pre[..., :3] / np.maximum(pre[..., 3:4], 1e-6)
    return pre, dict(pieces=count, nacre_px2=round(area_line + area_face, 1), line_px2=round(area_line, 1),
                     face_px2=round(area_face, 1))


def bleed(rgba, rounds=6):
    """Spread the shell colour under zero alpha (filtering and mips then never mix black into an edge)."""
    rgb, a = rgba[..., :3].copy(), rgba[..., 3]
    known = a > 1e-3
    for _ in range(rounds):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            sh_k = np.roll(np.roll(known, dy, 0), dx, 1)
            sh_c = np.roll(np.roll(rgb, dy, 0), dx, 1)
            acc += sh_c * sh_k[..., None]
            cnt += sh_k
        new = (~known) & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        known = known | new
    rgb[~known] = L.nacre_token()
    return np.dstack([rgb, a])


def cell(kind):
    """The atlas cell: (132, 132, 4) float, rgb = shell colour as sRGB code values, a = coverage; plus its numbers."""
    rgba, info = render(kind)
    r = np.hypot(*(np.mgrid[0:rgba.shape[0], 0:rgba.shape[1]] + .5 - rgba.shape[0] / 2.0)) / ATLAS_PX
    vis = rgba[..., 3] > .5
    info['max_radius_px'] = round(float(r[rgba[..., 3] > .02].max()), 2)
    info['glyph_max_radius_px'] = round(float(r[(rgba[..., 3] > .02) & (r < BUTTON['ring_inner'] - .8)].max()), 2)
    info['max_channel_255'] = int(round(float(rgba[..., :3][vis].max()) * 255))
    lab = L.srgb_to_oklab(rgba[..., :3][vis])
    hue = np.degrees(np.arctan2(lab[:, 2], lab[:, 1])) % 360
    info['hue_deg'] = [round(float(hue.min()), 1), round(float(hue.max()), 1)]
    info['oklab_chroma_max'] = round(float(np.hypot(lab[:, 1], lab[:, 2]).max()), 4)
    return bleed(rgba), info


# ---- the kit's first form (before), for the detail measurement only -------------------------------------------------------
def kit_roundel_mask(kind, s, ss=SS):
    """Shell coverage of the first form: the ink pictogram (hud308_mock.GLYPHS) scaled by .74 inside the lid, one face."""
    import hud308_mock as HM
    g = HM.Glyph(s, ss)
    HM.GLYPHS[kind](g)
    n = g.n
    gn = int(round(n * .74))
    o = (n - gn) // 2
    m = np.zeros((n, n))
    src = Image.fromarray((np.clip(g.m, 0, 1) * 255).astype(np.uint8))
    m[o:o + gn, o:o + gn] = np.asarray(src.resize((gn, gn), Image.LANCZOS), float) / 255
    return m.reshape(n // ss, ss, n // ss, ss).mean(axis=(1, 3))


# ---- what survives at 1080p -------------------------------------------------------------------------------------------------
def _label(b, eight):
    """Connected components of a small boolean array -> (labels, count)."""
    h, w = b.shape
    lab = np.zeros((h, w), int)
    nb = ((0, 1), (0, -1), (1, 0), (-1, 0)) + (((1, 1), (1, -1), (-1, 1), (-1, -1)) if eight else ())
    c = 0
    for y in range(h):
        for x in range(w):
            if b[y, x] and not lab[y, x]:
                c += 1
                stack = [(y, x)]
                lab[y, x] = c
                while stack:
                    cy, cx = stack.pop()
                    for dy, dx in nb:
                        yy, xx = cy + dy, cx + dx
                        if 0 <= yy < h and 0 <= xx < w and b[yy, xx] and not lab[yy, xx]:
                            lab[yy, xx] = c
                            stack.append((yy, xx))
    return lab, c


def _shrink(m, r):
    return ~_grow(~m, r)


_HI = {}


def _counters(b, min_samples):
    """Enclosed False areas of a boolean picture (4-connected, not touching the border) -> label array, list of labels."""
    lab, c = _label(~b, False)
    edge = set(lab[0]) | set(lab[-1]) | set(lab[:, 0]) | set(lab[:, -1])
    return lab, [i for i in range(1, c + 1) if i not in edge and (lab == i).sum() >= min_samples]


def detail(kind, form='najeon'):
    """Kit review weakness 4: does the pictogram keep its detail at 1080p real size?
    The designed COUNTERS (lacquer areas enclosed by shell: window slots, the spaces between the spokes) are found on the 8x
    drawing with its hairline joints closed (a joint is a seam, not a gap). That drawing is then box-sampled onto the screen
    grid at 1 px per design px in 16 sub-pixel phases:
      counters_1080p   enclosed lacquer areas that are still there when a pixel counts as shell at >= 50 % coverage
                       (the worst phase): a spoke thinner than a pixel drops out and the two spaces beside it merge
      modulation_min   (shell - hole) / (shell + hole) at the darkest pixel of each designed counter against the shell
                       within 1.5 px of it (the worst phase, the weakest counter)
    The rim line is left out."""
    key = (kind, form)
    if key not in _HI:
        _HI[key] = kit_roundel_mask(kind, 8.0, 2) if form == 'kit' else render(kind, s=8.0, ss=2, with_ring=False)[0][..., 3]
    n = _HI[key].shape[0] // 8
    closed = _shrink(_grow(_HI[key] > .5, 3), 3)                 # joints of .5 px (4 samples) are closed, 1.5 px gaps stay
    lab, holes = _counters(closed, 64)                           # >= 1 px^2

    def box(a, dy, dx):
        a = np.roll(a.astype(float), (dy, dx), axis=(0, 1))
        return a[:n * 8, :n * 8].reshape(n, 8, n, 8).mean(axis=(1, 3))
    seen = len(holes)
    mods = [1.0] * len(holes)
    rings = [_grow(lab == i, 12) & closed for i in holes]         # the shell within 1.5 px of each counter
    for dy in (0, 2, 4, 6):
        for dx in (0, 2, 4, 6):
            cov = box(closed, dy, dx)
            seen = min(seen, len(_counters(cov >= .5, 1)[1]))
            for j, i in enumerate(holes):
                hf, rf = box(lab == i, dy, dx), box(rings[j], dy, dx)
                c_hole = float(cov[np.unravel_index(hf.argmax(), hf.shape)])
                sel = rf > .5
                c_shell = float(cov[sel].mean()) if sel.any() else float(cov[rf > 0].max())
                mods[j] = min(mods[j], (c_shell - c_hole) / max(c_shell + c_hole, 1e-6))
    return dict(counters=len(holes), counters_1080p=seen, kept_share=round(seen / len(holes), 2) if holes else 1.0,
                modulation_min=round(min(mods), 3) if mods else None)


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    out = {}
    for kind in KINDS:
        _, info = cell(kind)
        info['detail_1080p'] = detail(kind)
        info['detail_1080p_kit_first_form'] = detail(kind, 'kit')
        out[kind] = info
    print(json.dumps(dict(button=BUTTON, cells=out), ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
