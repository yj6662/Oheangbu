# -*- coding: utf-8 -*-
"""SPEC-HUD-LIQUID-308 atlas generator (deterministic; numpy + PIL only).

Writes the one texture UI/InkVessel308 samples, its import settings and the geometry constants the shader / profile use:
  Tools/Unity/Stage308_hud/_ProjectAssets/Art/UI/UI308/Textures/hud308_atlas.png   1024 x 512 RGBA, LINEAR data (sRGB off)
  .../Textures/import308.json     import settings the editor command HudLiquid308 applies
  .../Textures/hud308_atlas.json  cells, quad size, level lines, measurements (AC-H2), sha256 of the png

Atlas channels
  vessel cell (305 x 395 = the 114 x 150 design rect + 4 px margin on every side, 2.5 atlas px per design px)
    R  signed distance to the glass silhouette, design px: d = (R - .5) * 2 * 16   (< 0 inside)
    G  the theme's neck band (SPEC-UI-THEME-308, D308-15): 0 none, .5 lacquer, 1 nacre (the cut-shell line; its butt joints
       are lacquer). Shape = theme308_lib.collar_fields with COLLAR_BAND: the lip and the top third of the neck stay glass,
       the line runs along the band's lower edge. The shader draws it only while the theme's collar switch is on
    B  the static paper highlight strokes (a long one upper left, a short fainter one lower right; inside the glass)
    A  dry-brush / draining-film streaks, equalised line by line across the streaks (a threshold t leaves a coverage of 1 - t)
  mark cells (4 x 208 x 208 = the 44 design px mark + 4 px margin on every side, 4 atlas px per design px): dodge, jump,
  vehicle, vehicle out. The margin is the paper rim's room: the mark quad is drawn 4 px larger than its 44 px rect on every
  side (like the vessel quad), so the rim is never cut at the rect and no mip level pulls a neighbour cell in.
    R  glyph   G  paper rim (glyph grown by 2.2 px)   B  wetting order 0 -> 1 along the stroke   A  dry-brush bristles
  najeon lid cells (4 x 132 x 132 = the 44 design px lid at 3 atlas px per design px, 2 px gutter; Tools/Art/hud308_najeon.py):
    RGB  the baked shell colour of the lid's cut-shell rim line and of its pictogram pieces, as sRGB CODE values (this is a
         linear-data texture: the shader converts them itself), bled under zero alpha
    A    shell coverage
  The lid itself (lacquer disc) is drawn by the shader. Used only while the theme's button switch is on; the ink cells above
  stay in the atlas, so "no buttons" is one switch away.
  key cells (D308-11b; 6 x 8 = 48 x 28 x 28 = a 14 design px symbol box at 2 atlas px per design px, 1 px gutter on every side):
    A  coverage of ONE key symbol. 0-25 = A-Z, 26-35 = 0-9 (rendered from the project's own keycap face, the repo file
       Oheangbu/Assets/_Project/Art/UI/UI304/Fonts/NotoSansKR-Bold.ttf, cap height 10 px), 36.. = drawn symbols for the keys
       a letter cannot name without a word (Shift, Space, Ctrl, Alt, Tab, Enter, the three mouse buttons, an unnamed key).
       The keycap itself (lacquer square, paper hairline, lip) is drawn by the shader. One cell = one key: no word, no
       sentence can be put on the HUD through this block. Order = HudKeyGlyph308 (C#).
The glyphs ARE the pictograms of the concept sheet (Tools/Art/hud308_mock.py GLYPHS: a running body, a body leaping off a
ground stroke, the roofed cart, the cart under a rewinding hook): what the user is shown is what the atlas ships. They are
imported from that module, not copied, so the two cannot drift; redraw them there and run both generators again.
(Review 2026-10-04: the first atlas carried the Spec's abstract strokes, which did not read without text.)

usage (repo root):  python Tools/Art/hud308_assets.py [--out <dir>] [--check]
  --check  regenerate in memory and compare with the files on disk (exit 1 on any difference)
"""
import argparse, hashlib, io, json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # hud308_mock (the pictograms) lives next to this file
import theme308_lib as THEME          # noqa: E402  the kit: the collar's one definition (collar_fields, COLLAR_BAND)
import hud308_najeon as NAJEON        # noqa: E402  the lids' shell pieces

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DEFAULT_OUT = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_hud', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Textures')

# ---- design numbers (SPEC-HUD-LIQUID-308 §1, TEST) ----
REF_W, REF_H = 114, 150            # HP vessel rect, design px (the ink vessel is the same shape at 100 x 132)
MARGIN = 4                         # the quad is this much larger than the rect on every side (paper rim room)
PX = 2.5                           # atlas px per design px in the vessel cell
QUAD_W, QUAD_H = REF_W + 2 * MARGIN, REF_H + 2 * MARGIN
CELL_W, CELL_H = int(QUAD_W * PX), int(QUAD_H * PX)   # 305 x 395
SDF_RANGE = 16.0                   # design px mapped to R 0..1
SS = 3                             # supersampling of the distance field
ATLAS_W, ATLAS_H = 1024, 512
VESSEL_AT = (16, 16)
MARK_REF = 44                      # the mark's rect, design px (LayoutSpec308.MarkSize)
MARK_MARGIN = 4                    # the mark quad is this much larger than the rect on every side (paper rim room)
MARK_PX = 4                        # atlas px per design px in a mark cell
MARK = (MARK_REF + 2 * MARK_MARGIN) * MARK_PX      # 208
MARK_SS = 4
MARKS = (('dodge', (344, 16)), ('jump', (560, 16)), ('vehicle', (776, 16)), ('vehicle_out', (344, 240)))
SHAPE = dict(body_aspect=1.22, neck_h=.075, lip_h=.035, neck_w=.34, lip_w=.46, n=2.3, egg=.12, wall=4.0, full_line=.07)
MARK_RIM_PX = 2.2
SEED = 308
# najeon lids (theme): 44 design px at 3 atlas px per design px, 2 px gutter on every side, in the free block right of vehicle_out
LID = int(NAJEON.BUTTON['cell_px'] * NAJEON.ATLAS_PX)      # 132
LID_GUTTER = 2
LIDS = (('dodge_n', 'dodge', (562, 242)), ('jump_n', 'jump', (698, 242)), ('vehicle_n', 'vehicle', (562, 378)),
        ('vehicle_out_n', 'vehicle_out', (698, 378)))
COLLAR = THEME.COLLAR_BAND
# key glyphs (D308-11b): one symbol per cell, in the free block right of the lids. Order = HudKeyGlyph308 (C#)
KEY_BOX = 14                       # design px one cell covers (the symbol box inside the 18 px keycap)
KEY_PX = 2                         # atlas px per design px
KEY_CELL = KEY_BOX * KEY_PX        # 28
KEY_GUTTER = 1
KEY_PITCH = KEY_CELL + 2 * KEY_GUTTER
KEY_COLS, KEY_ROWS = 6, 8
KEY_AT = (838, 244)                # top-left of the block, gutters included (838 .. 1018 x 244 .. 484)
KEY_SS = 8
KEY_CAP = 10.0                     # cap height of the letters, design px
KEY_MAX_W = 12.0                   # widest ink of a symbol, design px (W and M are squeezed to it)
KEY_SYMBOLS = ('Shift', 'Space', 'Ctrl', 'Alt', 'Tab', 'Enter', 'MouseLeft', 'MouseRight', 'MouseMiddle', 'Generic')
KEY_NAMES = tuple('ABCDEFGHIJKLMNOPQRSTUVWXYZ') + tuple('0123456789') + KEY_SYMBOLS + ('', '')
KEY_FONT = os.path.join(ROOT, 'Oheangbu', 'Assets', '_Project', 'Art', 'UI', 'UI304', 'Fonts', 'NotoSansKR-Bold.ttf')   # read only
assert len(KEY_NAMES) == KEY_COLS * KEY_ROWS


def smin(a, b, k):
    h = np.clip(.5 + .5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)


def vessel_implicit(x, y):
    """The Spec silhouette as an implicit field on design px (rect top-left origin, y down). < 0 inside (not a true distance)."""
    p, w, h = SHAPE, float(REF_W), float(REF_H)
    cx = w / 2
    lip_h, neck_h = p['lip_h'] * h, p['neck_h'] * h
    top = lip_h + neck_h - .02 * h
    bottom = h - 1.5
    b = (bottom - top) / 2
    cy = top + b
    a = min(w / 2 - 2, b / p['body_aspect'])
    dy = (y - cy) / b
    a_eff = a * (1 - p['egg'] * np.clip(-dy, 0, 1) ** 2)
    f = (np.abs(x - cx) / a_eff) ** p['n'] + np.abs(dy) ** p['n'] - 1
    eps = .05
    fx = ((np.abs(x + eps - cx) / a_eff) ** p['n'] - (np.abs(x - eps - cx) / a_eff) ** p['n']) / (2 * eps)
    dy2 = (y + eps - cy) / b
    dy1 = (y - eps - cy) / b
    f2 = (np.abs(x - cx) / (a * (1 - p['egg'] * np.clip(-dy2, 0, 1) ** 2))) ** p['n'] + np.abs(dy2) ** p['n']
    f1 = (np.abs(x - cx) / (a * (1 - p['egg'] * np.clip(-dy1, 0, 1) ** 2))) ** p['n'] + np.abs(dy1) ** p['n']
    fy = (f2 - f1) / (2 * eps)
    d = f / np.maximum(np.hypot(fx, fy), 1e-4)

    def box(cx_, cy_, hw, hh):
        qx = np.abs(x - cx_) - hw
        qy = np.abs(y - cy_) - hh
        return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0)
    d = smin(d, box(cx, lip_h + neck_h / 2 + 2, p['neck_w'] * w / 2, neck_h / 2 + 4), 5.0)
    d = np.minimum(d, box(cx, lip_h / 2 + .75, p['lip_w'] * w / 2, lip_h / 2 + .25))
    geo = dict(cx=cx, cy=cy, a=a, b=b, top=top, bottom=bottom,
               full_y=top + p['full_line'] * (bottom - top), floor_y=bottom - p['wall'])
    return d, geo


def edt(mask, radius):
    """Exact Euclidean distance (px) from every True pixel to the nearest False pixel, capped at `radius`."""
    h, w = mask.shape
    big = 1e6
    idx = np.arange(h, dtype=np.float64)[:, None] * np.ones((1, w))
    above = np.maximum.accumulate(np.where(~mask, idx, -big), axis=0)
    below = np.minimum.accumulate(np.where(~mask, idx, big)[::-1], axis=0)[::-1]
    g = np.minimum(np.minimum(idx - above, below - idx), radius + 1.0)
    g2 = g * g
    best = np.full((h, w), (radius + 1.0) ** 2)
    r = int(math.ceil(radius))
    pad = np.full((h, w + 2 * r), (radius + 1.0) ** 2)
    pad[:, r:r + w] = g2
    for dx in range(-r, r + 1):
        np.minimum(best, pad[:, r + dx:r + dx + w] + dx * dx, out=best)
    return np.minimum(np.sqrt(best), radius)


def signed_distance(mask, radius):
    """Signed distance in px of the grid (negative inside), from a binary mask."""
    d_in = edt(mask, radius)
    d_out = edt(~mask, radius)
    return np.where(mask, -(d_in - .5), d_out - .5)


def down(a, k):
    h, w = a.shape[0] // k, a.shape[1] // k
    return a[:h * k, :w * k].reshape(h, k, w, k).mean(axis=(1, 3))


def streaks(shape, rng, cells_along, cells_across, vertical):
    """Anisotropic brush streak noise in 0..1, histogram-equalised to a uniform distribution."""
    h, w = shape
    acc = np.zeros(shape)
    amp, total = 1.0, 0.0
    for octave in range(3):
        ca, cc = cells_along * (2 ** octave), cells_across * (2 ** octave)
        grid = (ca + 1, cc + 1) if vertical else (cc + 1, ca + 1)   # few cells along the streak, many across it
        low = Image.fromarray((rng.random(grid) * 255).astype(np.uint8), 'L')
        acc += amp * np.asarray(low.resize((w, h), Image.BICUBIC), float) / 255
        total += amp
        amp *= .5
    acc /= total
    # equalise ACROSS the streaks, line by line: any band cut along the streaks then keeps exactly 1 - t of its pixels above
    # a threshold t (the low-ink body is a thin strip at the vessel bottom, a global equalisation is not uniform there)
    axis = 1 if vertical else 0
    order = np.argsort(acc, axis=axis, kind='stable')
    rank = np.argsort(order, axis=axis, kind='stable')
    return (rank + .5) / acc.shape[axis]


def build_vessel(rng):
    gh, gw = CELL_H * SS, CELL_W * SS
    ys, xs = np.mgrid[0:gh, 0:gw]
    x = (xs + .5) / (PX * SS) - MARGIN        # design px, rect top-left origin
    y = (ys + .5) / (PX * SS) - MARGIN
    f, geo = vessel_implicit(x, y)
    mask = f < 0
    sd = signed_distance(mask, SDF_RANGE * PX * SS) / (PX * SS)      # design px
    sd_cell = down(sd, SS)
    r = np.clip(.5 + sd_cell / (2 * SDF_RANGE), 0, 1)
    xc, yc = down(x, SS), down(y, SS)
    ang = np.degrees(np.arctan2(yc - geo['cy'], xc - geo['cx']))
    aa = 1.0 / PX
    band = np.clip((sd_cell + 11.5) / aa + .5, 0, 1) * np.clip((-8.5 - sd_cell) / aa + .5, 0, 1)
    t = np.clip(1 - np.abs(ang + 142.0) / 26.0, 0, 1)
    hl = band * t ** .5 * (.55 + .45 * np.clip(1 - np.abs(ang + 150.0) / 30.0, 0, 1))    # brush taper: fuller at the start
    # the concept sheet's second, short paper stroke low on the right (alpha .26 against the first one's .55)
    t2 = np.clip(1 - np.abs(ang - 38.0) / 11.0, 0, 1)
    hw2 = 1.0 * t2 ** .6
    hl2 = np.clip((sd_cell + 8.2 + hw2) / aa + .5, 0, 1) * np.clip((-8.2 + hw2 - sd_cell) / aa + .5, 0, 1) * (t2 > 0) * (.26 / .55)
    hl = np.maximum(hl, hl2)
    streak = streaks((CELL_H, CELL_W), rng, 5, 46, vertical=True)
    # theme: the lacquer neck band and its cut-shell line (the kit's one definition of the shape)
    lip_h, neck_h, neck_w = SHAPE['lip_h'] * REF_H, SHAPE['neck_h'] * REF_H, SHAPE['neck_w'] * REF_W
    lac, nac = THEME.collar_fields(sd, x, y, geo['cx'], lip_h, neck_h, neck_w, 1.0 / (PX * SS), spec=COLLAR)
    collar = down(.5 * lac + .5 * nac, SS)
    hw = neck_w / 2.0 - COLLAR['inset']
    n_piece = max(1, int(round(2 * hw / COLLAR['piece'])))
    low = lip_h + neck_h + COLLAR['bottom']
    per = float((PX * SS) ** 2)
    geo['collar'] = dict(
        form='band: lip and the top third of the neck stay glass, the shell line runs along the lower edge',
        top_px=round(lip_h + neck_h * COLLAR['top'], 3), bottom_px=round(low, 3),
        line_px=[round(low - COLLAR['line_up'] - COLLAR['line_w'] / 2, 3), round(low - COLLAR['line_up'] + COLLAR['line_w'] / 2, 3)],
        line_half_px=round(hw, 3), pieces=n_piece, piece_px=round(2 * hw / n_piece, 4), joint_px=THEME.NACRE['joint_px'],
        lacquer_px2=round(float(lac.sum()) / per, 1), nacre_px2=round(float(nac.sum()) / per, 1),
        clear_of_full_line_px=round(geo['full_y'] - low, 2),
        thread_shows_above_px=round(lip_h + neck_h * COLLAR['top'] - 1.0, 2),      # the pour thread starts 1 px under the top
        ink_vessel_scale=round(100.0 / REF_W, 4))
    cell = np.dstack([r, collar, np.clip(hl, 0, 1), streak])
    # ---- measurements (AC-H2) ----
    ins = mask
    cols = np.nonzero(ins.any(axis=0))[0]
    rows = np.nonzero(ins.any(axis=1))[0]
    sw = (cols[-1] - cols[0] + 1) / (PX * SS)
    sh = (rows[-1] - rows[0] + 1) / (PX * SS)
    body_rows = rows[(rows / (PX * SS) - MARGIN) >= geo['top']]
    body_h = (body_rows[-1] - body_rows[0] + 1) / (PX * SS)
    # the 8-bit field decoded like the shader does, against the analytic mask: largest disagreement in design px
    dec = (np.round(r * 255) / 255 - .5) * 2 * SDF_RANGE
    wrong = (dec < 0) != (down(mask.astype(float), SS) > .5)
    worst = float(np.abs(sd_cell[wrong]).max()) if wrong.any() else 0.0
    measure = dict(silhouette_w=round(float(sw), 2), silhouette_h=round(float(sh), 2), h_over_w=round(float(sh / sw), 3),
                   body_h_over_w=round(float(body_h / sw), 3), silhouette_px=int(round(float(mask.sum()) / (PX * SS) ** 2)),
                   fill_range_px=round(geo['floor_y'] - geo['full_y'], 2), atlas_vs_formula_max_px=round(worst, 3))
    return cell, geo, measure


def build_mark(kind, rng):
    """One 208 x 208 mark cell from the concept sheet's pictogram (hud308_mock.GLYPHS) with its paper rim grown on a padded
    canvas (the sheet's own 44 px tile cuts the rim at its border; the atlas must not).
    rng is not used (the pictograms carry their own seeded bristles); the argument keeps the call order of build()."""
    import hud308_mock as mock                      # same folder; importing it draws nothing and writes nothing
    from PIL import ImageFilter
    assert abs(mock.MARK['rim_px'] - MARK_RIM_PX) < 1e-9, 'the sheet and the atlas must grow the paper rim by the same px'
    g = mock.Glyph(float(MARK_PX), MARK_SS)         # 44 units x 4 atlas px x 4 supersamples
    assert g.n == MARK_REF * MARK_PX * MARK_SS, g.n
    order_ss = np.clip(mock.GLYPHS[kind](g), 0, 1)
    pad = MARK_MARGIN * MARK_PX * MARK_SS
    core_ss = np.pad(np.clip(g.m, 0, 1), pad)
    tex_ss = np.pad(g.tex, pad, constant_values=.5)
    order_ss = np.pad(order_ss, pad, mode='edge')
    hard = Image.fromarray((np.clip(core_ss * 1.6, 0, 1) * 255).astype(np.uint8))
    rim_ss = np.asarray(mock.dilate(hard, MARK_RIM_PX * g.k).filter(ImageFilter.GaussianBlur(g.k * .35)), float) / 255
    rim_ss = np.maximum(rim_ss, core_ss)            # the rim always covers the glyph
    core, rim, order, bristle = down(core_ss, MARK_SS), down(rim_ss, MARK_SS), down(order_ss, MARK_SS), down(tex_ss, MARK_SS)
    # nothing may touch the cell border: mip levels and bilinear taps must not pull a neighbour in
    edge = max(float(rim[:2].max()), float(rim[-2:].max()), float(rim[:, :2].max()), float(rim[:, -2:].max()))
    assert edge < .02, ('mark rim reaches the cell border', kind, edge)
    per = float(MARK_PX * MARK_PX)
    return np.dstack([core, rim, order, bristle]), dict(glyph_px=int(round(float(core.sum()) / per)),
                                                         rim_px=int(round(float(rim.sum()) / per)),
                                                         bristle_mean=round(float(bristle[core > .5].mean()), 3))


def build_lid(kind):
    """One najeon lid cell with its gutter: (LID + 2 gutter)^2 x 4, rgb = shell colour (sRGB code values), a = coverage."""
    rgba, info = NAJEON.cell(kind)
    assert rgba.shape[0] == LID, rgba.shape
    edge = max(float(rgba[:2, :, 3].max()), float(rgba[-2:, :, 3].max()), float(rgba[:, :2, 3].max()), float(rgba[:, -2:, 3].max()))
    assert edge < .02, ('lid shell reaches the cell border', kind, edge)
    return np.pad(rgba, ((LID_GUTTER, LID_GUTTER), (LID_GUTTER, LID_GUTTER), (0, 0)), mode='edge'), info


_KEYFONT = {}


def _key_font():
    """The keycap face at the size whose capital H is KEY_CAP design px high on the supersampled grid."""
    if 'f' not in _KEYFONT:
        k = KEY_PX * KEY_SS
        trial = ImageFont.truetype(KEY_FONT, 400)
        box = trial.getbbox('H')
        size = int(round(400 * KEY_CAP * k / float(box[3] - box[1])))
        f = ImageFont.truetype(KEY_FONT, size)
        hb = f.getbbox('H')
        _KEYFONT['f'] = f
        _KEYFONT['cap_top'] = hb[1]
        _KEYFONT['cap_h'] = hb[3] - hb[1]
        with open(KEY_FONT, 'rb') as fh:
            _KEYFONT['sha'] = hashlib.sha256(fh.read()).hexdigest()
    return _KEYFONT['f']


def _key_letter(ch):
    """One capital letter / digit: centred on its ink, standing on the common baseline, squeezed to KEY_MAX_W if wider."""
    k = KEY_PX * KEY_SS
    n = KEY_BOX * k
    f = _key_font()
    box = f.getbbox(ch)
    w, h = box[2] - box[0], box[3] - box[1]
    tile = Image.new('L', (w + 8, int(_KEYFONT['cap_h'] * 1.6) + 8), 0)
    ImageDraw.Draw(tile).text((4 - box[0], 4 - _KEYFONT['cap_top']), ch, fill=255, font=f)      # the cap line sits at y = 4
    max_w = int(round(KEY_MAX_W * k))
    if w > max_w:
        tile = tile.resize((int(round(tile.width * max_w / float(w))), tile.height), Image.LANCZOS)
        w = max_w
    # a tail under the baseline (Q) may hang at most 1 design px below it: such a glyph is pressed down to fit, from its cap line
    rows = np.nonzero(np.asarray(tile).max(axis=1) > 5)[0]
    allowed = 4 + _KEYFONT['cap_h'] + k
    if rows[-1] + 1 > allowed:
        tile = tile.resize((tile.width, int(round(tile.height * (allowed - 4) / float(rows[-1] + 1 - 4)))), Image.LANCZOS)
    ink = np.asarray(tile, float) / 255
    cols = np.nonzero(ink.max(axis=0) > .02)[0]
    cell = np.zeros((n, n))
    x0 = int(round((n - (cols[-1] - cols[0] + 1)) / 2.0)) - cols[0]
    y0 = int(round((n - _KEYFONT['cap_h']) / 2.0)) - 4                    # the capital height is centred in the box
    ys, xs = np.nonzero(ink > 0)
    yy, xx = ys + y0, xs + x0
    ok = (yy >= 0) & (yy < n) & (xx >= 0) & (xx < n)
    cell[yy[ok], xx[ok]] = ink[ys[ok], xs[ok]]
    return cell


def _key_grid():
    k = KEY_PX * KEY_SS
    n = KEY_BOX * k
    ys, xs = np.mgrid[0:n, 0:n]
    return (xs + .5) / k, (ys + .5) / k, k        # design px of the 14 px box, y down


def _stroke(pts, width, x, y):
    """Coverage of a polyline of the given width (design px), butt-rounded: distance to the nearest segment <= width / 2."""
    d = np.full(x.shape, 1e9)
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        vx, vy = bx - ax, by - ay
        t = np.clip(((x - ax) * vx + (y - ay) * vy) / max(vx * vx + vy * vy, 1e-9), 0, 1)
        d = np.minimum(d, np.hypot(x - ax - t * vx, y - ay - t * vy))
    return (d <= width / 2.0).astype(float)


def _poly(pts, k, n):
    im = Image.new('L', (n, n), 0)
    ImageDraw.Draw(im).polygon([(px * k, py * k) for px, py in pts], fill=255)
    return np.asarray(im, float) / 255 > .5


def _outline(mask, width_px, k):
    """The band of `width_px` (design px) just inside a filled shape."""
    return (mask & (edt(mask, width_px * k + 2) <= width_px * k)).astype(float)


def _key_symbol(name):
    """The drawn key symbols, design px of the 14 px box (y down). Strokes about as heavy as the Bold letters' stems."""
    x, y, k = _key_grid()
    n = x.shape[0]
    W = 1.7
    if name == 'Shift':            # the block arrow, hollow
        m = _poly([(7, 1.9), (12.1, 7.5), (9.2, 7.5), (9.2, 12.1), (4.8, 12.1), (4.8, 7.5), (1.9, 7.5)], k, n)
        return _outline(m, 1.55, k)
    if name == 'Space':            # the open box under a space
        return _stroke([(2.3, 6.9), (2.3, 10.7), (11.7, 10.7), (11.7, 6.9)], W, x, y)
    if name == 'Ctrl':             # the caret
        return _stroke([(2.9, 9.2), (7, 4.6), (11.1, 9.2)], W, x, y)
    if name == 'Alt':              # the option switch
        return np.maximum(_stroke([(1.9, 4.4), (5, 4.4), (9, 10.4), (12.1, 10.4)], 1.6, x, y), _stroke([(8.6, 4.4), (12.1, 4.4)], 1.6, x, y))
    if name == 'Tab':              # arrow to a bar
        a = _stroke([(1.9, 7), (9.6, 7)], 1.6, x, y)
        a = np.maximum(a, _stroke([(6.7, 4.1), (9.6, 7), (6.7, 9.9)], 1.6, x, y))
        return np.maximum(a, _stroke([(12, 3.4), (12, 10.6)], 1.6, x, y))
    if name == 'Enter':            # the return arrow
        a = _stroke([(11.4, 2.9), (11.4, 8.6), (2.6, 8.6)], 1.6, x, y)
        return np.maximum(a, _stroke([(5.5, 5.7), (2.6, 8.6), (5.5, 11.5)], 1.6, x, y))
    if name.startswith('Mouse'):   # a mouse seen from above; the pressed button is filled
        cx, hw, top, bot, r = 7.0, 3.6, 1.8, 12.2, 3.6
        qx = np.abs(x - cx) - (hw - r)
        qy = np.abs(y - (top + bot) / 2) - ((bot - top) / 2 - r)
        d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r      # rounded box, < 0 inside
        body = d < 0
        a = ((d < 0) & (d > -1.3)).astype(float)
        a = np.maximum(a, (body & (np.abs(y - 6.6) <= .6)).astype(float))
        if name == 'MouseMiddle':
            a = np.maximum(a, ((np.abs(x - cx) <= 1.0) & (y >= 3.3) & (y <= 6.0)).astype(float))
        else:
            a = np.maximum(a, (body & (np.abs(x - cx) <= .55) & (y <= 6.6)).astype(float))
            side = (x < cx) if name == 'MouseLeft' else (x > cx)
            a = np.maximum(a, (body & side & (y <= 6.6)).astype(float))
        return a
    if name == 'Generic':          # a key that has no symbol of its own: a blank keycap
        qx = np.abs(x - 7.0) - 3.9
        qy = np.abs(y - 7.2) - 3.3
        d = np.maximum(qx, qy)
        return ((d < 0) & (d > -1.4)).astype(float)
    return np.zeros(x.shape)


def build_keys():
    """The key block: (rows x pitch, cols x pitch) coverage and per-symbol measurements (design px)."""
    block = np.zeros((KEY_ROWS * KEY_PITCH, KEY_COLS * KEY_PITCH))
    info = {}
    for i, name in enumerate(KEY_NAMES):
        if not name:
            continue
        hi = _key_letter(name) if len(name) == 1 else _key_symbol(name)
        cell = down(hi, KEY_SS)
        edge = max(float(cell[:2].max()), float(cell[-2:].max()), float(cell[:, :2].max()), float(cell[:, -2:].max()))
        assert edge < .02, ('key symbol reaches the cell border', name, edge)
        r, c = divmod(i, KEY_COLS)
        y0, x0 = r * KEY_PITCH + KEY_GUTTER, c * KEY_PITCH + KEY_GUTTER
        block[y0:y0 + KEY_CELL, x0:x0 + KEY_CELL] = cell
        rows = np.nonzero(cell.max(axis=1) > .1)[0]
        cols = np.nonzero(cell.max(axis=0) > .1)[0]
        info[name] = dict(cell=i, ink_w_px=round((cols[-1] - cols[0] + 1) / float(KEY_PX), 2), ink_h_px=round((rows[-1] - rows[0] + 1) / float(KEY_PX), 2),
                          ink_px2=round(float(cell.sum()) / (KEY_PX * KEY_PX), 1))
    return block, info


def uv_rect(x, y, w, h):
    """Pixel rect (top-left origin) -> Unity uv rect (x, y, w, h), v up."""
    return [round(x / ATLAS_W, 6), round(1 - (y + h) / ATLAS_H, 6), round(w / ATLAS_W, 6), round(h / ATLAS_H, 6)]


def build():
    rng = np.random.default_rng(SEED)
    atlas = np.zeros((ATLAS_H, ATLAS_W, 4))
    atlas[..., 0][:, :336] = 1.0          # vessel region: far outside the glass; mark region: no glyph
    atlas[..., 2] = .5
    atlas[..., 3] = .5
    cell, geo, measure = build_vessel(rng)
    vx, vy = VESSEL_AT
    atlas[vy:vy + CELL_H, vx:vx + CELL_W] = cell
    cells = {'vessel': uv_rect(vx, vy, CELL_W, CELL_H)}
    marks = {}
    for kind, (mx, my) in MARKS:
        m, info = build_mark(kind, rng)
        atlas[my:my + MARK, mx:mx + MARK] = m
        cells[kind] = uv_rect(mx, my, MARK, MARK)
        marks[kind] = info
    lids = {}
    for name, kind, (lx, ly) in LIDS:
        block, info = build_lid(kind)
        g = LID_GUTTER
        atlas[ly - g:ly + LID + g, lx - g:lx + LID + g] = block
        cells[name] = uv_rect(lx, ly, LID, LID)
        lids[kind] = info
    # key glyphs (D308-11b): alpha = coverage, the block's background is alpha 0
    key_block, key_info = build_keys()
    kx, ky = KEY_AT
    atlas[ky:ky + key_block.shape[0], kx:kx + key_block.shape[1], 3] = key_block
    cell0 = uv_rect(kx + KEY_GUTTER, ky + KEY_GUTTER, KEY_CELL, KEY_CELL)
    keys = dict(decision='D308-11b', box_px=KEY_BOX, atlas_px_per_design_px=KEY_PX, cell_px=KEY_CELL, pitch_px=KEY_PITCH, columns=KEY_COLS, rows=KEY_ROWS,
                origin_px=[kx + KEY_GUTTER, ky + KEY_GUTTER], cap_px=KEY_CAP, max_ink_w_px=KEY_MAX_W,
                grid=[cell0[0], cell0[1], round(KEY_PITCH / float(ATLAS_W), 6), round(KEY_PITCH / float(ATLAS_H), 6)], cell=[cell0[2], cell0[3]],
                names=list(KEY_NAMES), symbols=key_info,
                font=dict(file='Oheangbu/Assets/_Project/Art/UI/UI304/Fonts/NotoSansKR-Bold.ttf', sha256=_KEYFONT['sha'],
                          note='the #304 keycap face (UiType304.Keycap18 / KeycapSm16); read only, letters and digits only'))
    b = NAJEON.BUTTON
    k_ink = 100.0 / REF_W
    theme = dict(
        spec='SPEC-UI-THEME-308', decision='D308-15', kit='Tools/Art/theme308_lib.py (collar_fields, COLLAR_BAND, nacre model)',
        collar=geo['collar'],
        button=dict(radius_px=b['radius'], quad_px=MARK_REF + 2 * MARK_MARGIN, cell_px=b['cell_px'], ring_inner_px=b['ring_inner'],
                    ring_r_px=b['ring_r'], ring_w_px=b['ring_w'], glyph_r_px=b['glyph_r'], dry_alpha=b['dry_alpha'],
                    atlas_px_per_design_px=NAJEON.ATLAS_PX),
        lids=lids,
        # nacre the HUD cluster shows at 1080p: both collar lines + the three lids that can be up at once (the larger vehicle lid)
        nacre_px2_cluster=round(geo['collar']['nacre_px2'] * (1 + k_ink * k_ink) + lids['dodge']['nacre_px2'] + lids['jump']['nacre_px2']
                                + max(lids['vehicle']['nacre_px2'], lids['vehicle_out']['nacre_px2']), 1),
        lacquer_px2_cluster=round(geo['collar']['lacquer_px2'] * (1 + k_ink * k_ink) + 3 * math.pi * b['radius'] ** 2, 1))
    img = Image.fromarray((np.clip(atlas, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
    buf = io.BytesIO()
    img.save(buf, format='PNG', optimize=False, compress_level=6)
    png = buf.getvalue()
    quad_y = lambda y_down: (QUAD_H - (MARGIN + y_down)) / QUAD_H        # rect y (down) -> quad uv y (up)
    info = dict(
        spec='SPEC-HUD-LIQUID-308', generator='Tools/Art/hud308_assets.py', seed=SEED,
        atlas=dict(file='hud308_atlas.png', size=[ATLAS_W, ATLAS_H], sha256=hashlib.sha256(png).hexdigest()),
        ref_rect=[REF_W, REF_H], margin_px=MARGIN, quad_ref=[QUAD_W, QUAD_H], sdf_range_px=SDF_RANGE,
        level=dict(floor_uv=round(quad_y(geo['floor_y']), 5), full_uv=round(quad_y(geo['full_y']), 5),
                   neck_top_uv=round(quad_y(1.0), 5), wavelength_px=round(.9 * REF_W, 2)),
        body=dict(cx=geo['cx'], cy=geo['cy'], a=geo['a'], b=round(geo['b'], 3)),
        mark=dict(size_px=MARK_REF, margin_px=MARK_MARGIN, atlas_px_per_design_px=MARK_PX),
        cells=cells, measure=measure, marks=marks, shape=SHAPE, theme=theme, keys=keys)
    imports = dict(textures=[dict(file='hud308_atlas.png', kind='Default', sRGB=False, mips=True, wrap='Clamp',
                                  filter='Trilinear', compression='None', maxSize=1024, alphaIsTransparency=False,
                                  npotScale='None', readable=False)])
    return png, info, imports


def dump(obj):
    return (json.dumps(obj, ensure_ascii=False, indent=1) + '\n').encode('utf-8')


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=DEFAULT_OUT)
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    png, info, imports = build()
    files = {'hud308_atlas.png': png, 'hud308_atlas.json': dump(info), 'import308.json': dump(imports)}
    if a.check:
        bad = []
        for name, data in files.items():
            path = os.path.join(a.out, name)
            if not os.path.exists(path) or open(path, 'rb').read() != data:
                bad.append(name)
        print('CHECK ' + ('same' if not bad else 'DIFFERENT: ' + ', '.join(bad)))
        sys.exit(1 if bad else 0)
    os.makedirs(a.out, exist_ok=True)
    for name, data in files.items():
        with open(os.path.join(a.out, name), 'wb') as f:
            f.write(data)
    print(json.dumps(dict(out=a.out, sha256=info['atlas']['sha256'], level=info['level'], measure=info['measure'],
                          marks=info['marks'], cells=info['cells'], theme=info['theme'],
                          keys={k: v for k, v in info['keys'].items() if k != 'symbols'}), ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
