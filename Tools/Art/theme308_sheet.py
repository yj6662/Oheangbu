# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - the SAMPLE SHEET: the kit as shipped, applied as quick composites to the three surfaces.

  python Tools/resource_guard.py --wait
  python Tools/Art/theme308_sheet.py            -> Art/UI308/Theme/theme308_sheet.png (+ _half.jpg) and theme308_sheet.json
  python Tools/Art/theme308_sheet.py --check    render twice in memory and compare the bytes (no files written)

This script is a CONSUMER of the kit: through Tools/Art/theme308_kit.py it reads the staged atlas, its json and the lattice
tiles from Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme/ and lays them out the way Unity's Image does
(Simple / Sliced / Tiled with borders, tiles laid from the bottom-left, 2 texels per design px). Nothing here is a game capture: it
composites in sRGB code values (Unity is linear), the HUD vessels and pictograms come from Tools/Art/hud308_mock.py, the
equipment page from Tools/Art/equip308_mock.py (both imported, their own layouts untouched), and the two procedural HUD
parts (collar, roundel) are numpy twins of what UI/InkVessel308 will draw. Writes only under Art/UI308/Theme/.
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
import theme308_lib as L        # noqa: E402
from theme308_kit import Kit    # noqa: E402  (the staged kit, laid out the way Unity's Image lays a sprite out)
import theme308_spec as TS      # noqa: E402  (fonts, paste helpers, the collar / roundel twins)
import hud308_mock as HM        # noqa: E402
import equip308_mock as EM      # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Art', 'UI308', 'Theme')
CAP_MAP = os.path.join(ROOT, 'Art', 'UI304', 'after3', 'map_revealed.png')
T = L.T
NACRE = L.nacre_token()
SW, PADX = 2400, 48
BG = L.hexc('#CFCBC0')
TXT = L.hexc('#141413')
SUB = L.hexc('#4A4740')
WARN = L.hexc('#9A2E22')
VEIL90 = L.over(T['Veil'], L.SKY, .90)
TODAY_PAPER = np.array([213, 209, 198]) / 255.0     # today's minimap values, measured on the captures (map Spec L1)
TODAY_WASH = np.array([162, 158, 146]) / 255.0
TODAY_LINE = np.array([91, 90, 84]) / 255.0
c8 = TS.c8
REP = {}


def to_pil(rgba):
    return Image.fromarray((np.clip(rgba, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')


def rgb8(c):
    return tuple(int(round(float(v) * 255)) for v in np.asarray(c)[:3])


# ---------------------------------------------------------------------------------------------------------------------
# sheet plumbing
# ---------------------------------------------------------------------------------------------------------------------
class Sheet:
    def __init__(self):
        self.parts, self.labels, self.y = [], [], 0

    def band(self, h, col=BG):
        a = TS.flat(SW, int(h), col)
        y0 = self.y
        self.parts.append(a)
        self.y += int(h)
        return a, y0

    def text(self, x, y, s, size=18, face='sans', col=SUB):
        self.labels.append((x, y, s, size, face, col))

    def head(self, tag, title, subs=()):
        a, y0 = self.band(64 + 26 * len(subs) + 6)
        TS.rect(a, PADX, 20, 14, 30, T['Cinnabar'])
        self.text(PADX + 26, y0 + 12, tag + '  ' + title, 32, 'serif', TXT)
        for i, s in enumerate(subs):
            self.text(PADX + 26, y0 + 60 + 26 * i, s, 18, 'sans', SUB)

    def build(self):
        img = np.concatenate(self.parts, axis=0)
        im = Image.fromarray((np.clip(img, 0, 1) * 255 + .5).astype(np.uint8))
        d = ImageDraw.Draw(im)
        for x, y, s, size, face, col in self.labels:
            d.text((x, y), s, font=TS.font(size, face), fill=c8(col))
        return im


def put(a, img, x, y):
    """opaque RGB or straight-alpha RGBA onto the band"""
    if img.shape[2] == 3:
        img = np.dstack([img, np.ones(img.shape[:2])])
    TS.paste(a, img, x, y)


def frame1(a, x, y, w, h, col=TXT, al=.35):
    for r in ((x - 1, y - 1, w + 2, 1), (x - 1, y + h, w + 2, 1), (x - 1, y, 1, h), (x + w, y, 1, h)):
        TS.rect(a, r[0], r[1], r[2], r[3], col, al)


# ---------------------------------------------------------------------------------------------------------------------
# (a) lattice
# ---------------------------------------------------------------------------------------------------------------------
LAT_USE = dict(
    tti='긴 옆 띠 (예비) · 위 = 가운데 띠 창', yongja='큰 면의 열 나눔 = 살대 사각형', aja='귀 꺾임의 원형 (틀의 귀)',
    wanja='지도 판의 띠 (u 3 px로 구움)', jeongja='칸 격자 그 자체 = 살대 사각형', bit='잠긴 칸 (종이 없는 살)',
    sutdae='수치 구역 머리 띠 (예비)', gwigap='예비 (완자살의 대안)')


LAT_WINDOW = dict(tti=56.0)      # design px above the tile's bottom where the 300 % window starts: run - middle band - run


def leaf(kit, fam, w, h, s, paper, frame=4.0, lift=0.0):
    """one door leaf: a wood frame (ulgeomi) round a lattice field, on paper or bare (the ground shows through).
    lift = design px of the period skipped at the bottom (a Tiled Image starts at the tile's bottom)."""
    W, H = int(round(w * s)), int(round(h * s))
    out = np.zeros((H, W, 4))
    f = int(round(frame * s))
    if paper is not None:
        out[..., :3], out[..., 3] = paper, 1.0
    m = kit.lattice(fam, w - 2 * frame, h - 2 * frame + lift, s)
    if lift:
        m = m[:m.shape[0] - int(round(lift * s))]
    mh, mw = min(m.shape[0], H - 2 * f), min(m.shape[1], W - 2 * f)
    lay = np.zeros((H, W))
    lay[f:f + mh, f:f + mw] = m[:mh, :mw]
    lay[:f], lay[H - f:], lay[:, :f], lay[:, W - f:] = 1, 1, 1, 1
    a0 = out[..., 3]
    o = lay + a0 * (1 - lay)
    out[..., :3] = (T['Wood'][None, None, :] * lay[..., None] + out[..., :3] * (a0 * (1 - lay))[..., None]) / np.maximum(o, 1e-6)[..., None]
    out[..., 3] = o
    return out


def panel_lattice(sh, kit):
    cw, cv = L.contrast(T['Wood'], T['Chip']), L.contrast(T['Wood'], T['Veil'])
    REP['lattice'] = dict(wood_on_paper=round(cw, 2), wood_on_veil=round(cv, 2))
    sh.head('(가)', '창호 — 문살 8종', (
        '키트 타일(Lattice/lat308_*.png, 디자인 px의 2배 저작)을 Tiled Image처럼 깐 것. 윗줄 = 300 % (창호지 위), 아랫줄 = 1080p 실제 크기: 왼쪽은 창호지가 있는 칸, 오른쪽은 종이 없는 살(장막 위).',
        '살대 / 창호지 %.2f:1 (장식 등급 — 글에서 16 px), 살대 / 장막 %.2f:1 (구조 등급 — 글에서 8 px). 문짝 한 짝처럼 울거미(문틀) 4 px 안에 살을 넣어야 표 계산 격자가 아니라 창으로 읽힌다.' % (cw, cv)))
    a, y0 = sh.band(560)
    x = PADX
    for fam in L.LATTICE_ORDER:
        f = L.LATTICE[fam]
        big = leaf(kit, fam, 88, 88, 3.0, T['Chip'], lift=LAT_WINDOW.get(fam, 0.0))
        put(a, big, x, 8)
        r1 = leaf(kit, fam, 104, 104, 1.0, T['Chip'], 3.0)
        put(a, r1, x, 290)
        TS.rect(a, x + 120, 290, 104, 104, T['Veil'])
        r2 = leaf(kit, fam, 104, 104, 1.0, None, 3.0)
        put(a, r2, x + 120, 290)
        sh.text(x, y0 + 404, '%s %s' % (f['ko'], f['hanja']), 22, 'bold', TXT)
        sh.text(x, y0 + 434, '모듈 %g · 살 %g px' % (f['u'], f['bar']), 16)
        t = kit.tiles[fam]
        sh.text(x, y0 + 456, '타일 %d×%d · 열림 %.0f %%' % (t.shape[1], t.shape[0], (1 - t.mean()) * 100), 16)
        sh.text(x, y0 + 478, LAT_USE[fam], 16, 'sans', TXT)
        x += 288


# ---------------------------------------------------------------------------------------------------------------------
# stand-in minimap content, today's minimap and the framed one
# ---------------------------------------------------------------------------------------------------------------------
def mini_map(w, h, s):
    """The same stand-in content for both versions (the map itself belongs to SPEC-MAP-OVERHAUL-308)."""
    ss = 3
    W, H = int(round(w * s)), int(round(h * s))
    k = s * ss
    im = Image.new('RGB', (W * ss, H * ss), rgb8(TODAY_WASH))
    d = ImageDraw.Draw(im)
    cx, cy = w * .48 * k, h * .50 * k
    for ox, oy, r in ((0, 0, 58), (34, -18, 40), (-38, 10, 44), (10, 34, 42), (46, 22, 36), (-20, -34, 38), (-52, -22, 26)):
        d.ellipse([cx + (ox - r) * k, cy + (oy - r) * k, cx + (ox + r) * k, cy + (oy + r) * k], fill=rgb8(TODAY_PAPER))
    for pts in (((-150, -6), (-60, -2), (0, 0)), ((0, 0), (44, -40), (92, -84)), ((0, 0), (56, 12), (130, 46)), ((0, 0), (-50, 46), (-96, 92))):
        d.line([(cx + px * k, cy + py * k) for px, py in pts], fill=rgb8(TODAY_LINE), width=max(1, int(round(2.2 * k))), joint='curve')
    for rr, col in ((1.3, T['Paper']), (1.0, T['Cinnabar'])):
        d.polygon([(cx, cy - 11 * rr * k), (cx + 8 * rr * k, cy + 9 * rr * k), (cx, cy + 4.5 * rr * k), (cx - 8 * rr * k, cy + 9 * rr * k)], fill=rgb8(col))
    return np.asarray(im.resize((W, H), Image.LANCZOS), float) / 255.0


def mini_today(s):
    """today: a 300 x 214 translucent paper with a soft edge and an ink north tick, no frame. Returns (RGBA, cx, cy)."""
    w, h, pad = 300.0, 214.0, 12.0
    W, H = int(round((w + 2 * pad) * s)), int(round((h + 2 * pad) * s))
    out = np.zeros((H, W, 4))
    m = mini_map(w, h, s)
    x0, y0 = int(round(pad * s)), int(round(pad * s))
    out[y0:y0 + m.shape[0], x0:x0 + m.shape[1], :3] = m
    ys, xs = np.mgrid[0:H, 0:W]
    X, Y = (xs + .5) / s - pad, (ys + .5) / s - pad
    edge = np.minimum(np.minimum(X, w - X), np.minimum(Y, h - Y))
    al = np.clip(edge / 4.0, 0, 1) * .90
    lum = L.lum(out[..., :3])
    out[..., 3] = al * np.where(lum > L.lum(TODAY_WASH) + .05, 1.0, .88)
    tick = np.dstack([np.ones((H, W, 3)) * T['Paper'], ((np.abs(X - w / 2) <= 2.2) & (Y >= -10.5) & (Y <= -.5)).astype(float) * .9])
    TS.paste(out[..., :3], tick, 0, 0)
    out[..., 3] = np.maximum(out[..., 3], tick[..., 3])
    tk = ((np.abs(X - w / 2) <= 1.0) & (Y >= -9.5) & (Y <= -1.5)).astype(float)
    out[..., :3] = out[..., :3] * (1 - tk[..., None]) + T['Ink'] * tk[..., None]
    return out, (pad + w / 2) * s, (pad + h / 2) * s


def mini_themed(kit, s):
    """SPEC-MAP-OVERHAUL-308 section 5 with the kit: window 294 x 210, Frame.Mini 312 x 228, Piece.North on the top side."""
    ww, wh, f, up = 294.0, 210.0, 9.0, 12.0
    W, H = int(round((ww + 2 * f) * s)), int(round((wh + 2 * f + up) * s))
    out = np.zeros((H, W, 4))
    m = mini_map(ww, wh, s)
    x0, y0 = int(round(f * s)), int(round((f + up) * s))
    out[y0:y0 + m.shape[0], x0:x0 + m.shape[1], :3] = m
    out[y0:y0 + m.shape[0], x0:x0 + m.shape[1], 3] = 1.0
    fr = kit.rect('frame_mini', ww + 2 * f, wh + 2 * f, s)
    lay = np.zeros_like(out)
    oy = int(round(up * s))
    lay[oy:oy + fr.shape[0], :fr.shape[1]] = fr[:H - oy, :W]
    north = kit.simple('piece_north', s)
    ncw, nch = kit.cells['piece_north']['design_px']                         # cell bottom = 7 px inside the frame's outer edge
    nx, ny = int(round((ww / 2 + f - ncw / 2) * s)), int(round((up + 7 - nch) * s))
    for src, px, py in ((lay, 0, 0), (north, nx, ny)):
        hh, wd = src.shape[:2]
        dst = out[py:py + hh, px:px + wd]
        sa, da = src[..., 3], dst[..., 3]
        o = sa + da * (1 - sa)
        dst[..., :3] = (src[..., :3] * sa[..., None] + dst[..., :3] * (da * (1 - sa))[..., None]) / np.maximum(o, 1e-6)[..., None]
        dst[..., 3] = o
    return out, (f + ww / 2) * s, (up + f + wh / 2) * s


# ---------------------------------------------------------------------------------------------------------------------
# HUD
# ---------------------------------------------------------------------------------------------------------------------
MARKS = (('dodge', 'wet', 1.0), ('jump', 'wet', 1.0), ('vehicle', 'wet', 1.0))
FACE_CHROMA = L.NACRE_FACE_CHROMA      # the same share the kit gives its wide shell faces
COLLAR_FAM = (1, 0, 1, 1, 0, 2)   # hue family of the collar's cut pieces in order: blue and green, pink is the rare flash


def pearl(X, Y):
    """A wide shell face (a pictogram): the nacre colour at reduced chroma, the hue drifting slowly across the face."""
    n = L.NACRE
    hue = n['H_centre'] + 62.0 * np.sin(2 * np.pi * (X * .6 + Y * .8) / 64.0 + .9)
    lt = n['L'] + n['grain_dL'] * np.sin(2 * np.pi * (X * .8 - Y * .6) / n['grain_period'])
    return L.oklch(lt, n['C'] * FACE_CHROMA, hue)


def collar(kind, s, ss=4):
    """numpy twin of the collar UI/InkVessel308 will draw from the G channel: theme308_lib.collar_fields gives the lacquer
    and shell coverage, every cut piece takes one hue family. RGBA sized like HM.render_vessel."""
    rw, rh = HM.RECT[kind][2:]
    g = HM.vessel_geo(rw, rh, s, ss)
    nw = HM.SHAPE['neck_w'] * rw
    lac, nac = L.collar_fields(g.d, g.x, g.y, g.cx, g.lip_h, g.neck_h, nw, ss / g.k)
    hw = nw / 2.0 - L.COLLAR['inset']
    n = max(1, int(round(2 * hw / L.COLLAR['piece'])))
    idx = np.clip(np.floor((g.x - (g.cx - hw)) / (2 * hw) * n), 0, n - 1).astype(int)
    fam = np.array(COLLAR_FAM)[(idx + (0 if kind == 'hp' else 2)) % len(COLLAR_FAM)]
    col = np.zeros(g.x.shape + (3,))
    for f in range(3):
        col[fam == f] = L.nacre_rgb(g.x, g.y, f, 1.3 * f, .7)[fam == f]
    share = nac / np.maximum(lac, 1e-6)
    rgb = T['Lacquer'][None, None, :] * (1 - share[..., None]) + col * share[..., None]
    H, W = lac.shape
    pre = np.dstack([rgb * lac[..., None], lac]).reshape(H // ss, ss, W // ss, ss, 4).mean(axis=(1, 3))
    pre[..., :3] /= np.maximum(pre[..., 3:4], 1e-6)
    return pre


def roundel(kind, state='wet', fill=1.0, s=1.0, ss=4):
    """numpy twin of the HUD button UI/InkVessel308 will draw: lacquer disc 44 px, paper hairline, the pictogram as shell.
    Returns (RGBA, nacre area px^2)."""
    R = TS.ROUNDEL
    n = int(round(R['d'] * s)) * ss
    k = s * ss
    ys, xs = np.mgrid[0:n, 0:n]
    X, Y = (xs + .5) / k, (ys + .5) / k
    r = np.hypot(X - R['d'] / 2, Y - R['d'] / 2)
    disc = np.clip((R['d'] / 2 - r) * k / ss + .5, 0, 1)
    inner = np.clip((R['d'] / 2 - R['rim'] - r) * k / ss + .5, 0, 1)
    g = HM.Glyph(s, ss)
    order = np.clip(HM.GLYPHS[kind](g), 0, 1)
    gn = int(round(n * R['glyph']))
    o = (n - gn) // 2
    m, od = np.zeros((n, n)), np.zeros((n, n))
    m[o:o + gn, o:o + gn] = TS.resize_mask(np.clip(g.m, 0, 1), gn, gn)
    od[o:o + gn, o:o + gn] = TS.resize_mask(order, gn, gn)
    wet = np.ones_like(m) if state == 'wet' else np.zeros_like(m) if state == 'dry' else np.clip((fill - od) / .05, 0, 1)
    ga = m * (wet + (1 - wet) * R['dry_a']) * inner
    rgb = np.ones((n, n, 3)) * T['Paper']
    rgb = rgb * (1 - inner[..., None]) + T['Lacquer'] * inner[..., None]
    al = np.maximum(disc * R['rim_a'], inner)
    rgb = rgb * (1 - ga[..., None]) + pearl(X, Y) * ga[..., None]
    pre = np.dstack([rgb * al[..., None], al]).reshape(n // ss, ss, n // ss, ss, 4).mean(axis=(1, 3))
    pre[..., :3] /= np.maximum(pre[..., 3:4], 1e-6)
    return pre, float((m * inner).sum() / (k * k))


def hud_draw(dst, region, s, themed, hp=None, ink=None, marks=MARKS):
    hp = hp or HM.HP_N
    ink = ink or HM.INK_N
    if not themed:
        HM.draw_cluster(dst, region, s, hp, ink, marks)
        return
    x0, y0 = region[0], region[1]
    for kind, st in (('hp', hp), ('ink', ink)):
        rx, ry, rw, rh = HM.RECT[kind]
        TS.paste(dst, HM.render_vessel(kind, st, s=s), (rx - HM.PAD - x0) * s, (ry - HM.PAD - y0) * s)
        TS.paste(dst, collar(kind, s), (rx - HM.PAD - x0) * s, (ry - HM.PAD - y0) * s)
    d = TS.ROUNDEL['d']
    for (mx, my), (kind, state, fill) in zip(HM.mark_centres(), marks):
        TS.paste(dst, roundel(kind, state, fill, s=s)[0], (mx - d / 2 - x0) * s, (my - d / 2 - y0) * s)


_still = {}


def still(name):
    if name not in _still:
        _still[name] = HM.still_full('road') if name == 'road' else np.asarray(Image.open(EM.WORLD).convert('RGB'), float) / 255.0
    return _still[name]


def pick_window(w, h, brightest):
    """the brightest / darkest w x h window of the two HUD-less captures (16 px grid), deterministic"""
    best = None
    for name in ('road', 'palace'):
        lum = L.lum(still(name))
        ii = np.pad(lum.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
        ys = np.arange(0, lum.shape[0] - h + 1, 16)
        xs = np.arange(0, lum.shape[1] - w + 1, 16)
        m = (ii[ys[:, None] + h, xs[None, :] + w] - ii[ys[:, None], xs[None, :] + w] - ii[ys[:, None] + h, xs[None, :]] + ii[ys[:, None], xs[None, :]]) / (w * h)
        idx = np.unravel_index(m.argmax() if brightest else m.argmin(), m.shape)
        v = float(m[idx])
        if best is None or (v > best[0] if brightest else v < best[0]):
            best = (v, name, int(xs[idx[1]]), int(ys[idx[0]]))
    return best


def crop(name, x, y, w, h, s):
    a = still(name)[y:y + h, x:x + w]
    im = Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8))
    return np.asarray(im.resize((int(round(w * s)), int(round(h * s))), Image.LANCZOS), float) / 255.0


def panel_hud(sh, kit):
    sh.head('(라-1)', 'HUD 군집 + 미니맵 틀 — 실제 화면 아래쪽 띠 (1080p 실제 크기)', (
        '배경 = HUD 없는 실제 캡처(Art/UI304/clean/road.png)의 y 740–1060. 윗줄 = 오늘의 시안(SPEC-HUD-LIQUID-308 시안 + 틀 없는 미니맵), 아랫줄 = 테마를 입힌 것.',
        '유리 몸통 · 액체 · 수면은 그대로다. 바뀐 것: 목(목 + 입술)이 옻칠 목테 + 자개 한 줄, 표시 셋이 옻칠 단추 + 자개 기호, 미니맵이 옻칠 문틀(Frame.Mini) + 끊음질 선 + 북쪽 자개 조각(Piece.North).'))
    region = (0, 740, 1920, 1060)
    a, y0 = sh.band(2 * 320 + 60)
    for row, themed in enumerate((False, True)):
        dst = np.array(HM.still_region(region, 1.0, name='road'), float)
        hud_draw(dst, region, 1.0, themed)
        mm, cx, cy = mini_themed(kit, 1.0) if themed else mini_today(1.0)
        TS.paste(dst, mm, 1740 - cx, 900 - 740 - cy)
        yy = 8 + row * 340
        a[yy:yy + 320, PADX:PADX + 1920] = dst
        frame1(a, PADX, yy, 1920, 320)
        sh.text(PADX + 1940, y0 + yy + 4, '오늘의 시안' if not themed else '테마 적용', 24, 'bold', TXT)
        for i, s in enumerate(('먹 기호 + 한지 테(판 없음)', '유리 목 = 먹 선', '미니맵 = 틀 없는 한지 300×214', '북쪽 = 먹 눈금') if not themed else
                              ('옻칠 단추 44 + 자개 기호', '목테 = 옻칠 + 끊음질 한 줄', '미니맵 = 문틀 9 px, 창 294×210', '북쪽 = 자개 꽃잎(옻칠 받침)')):
            sh.text(PADX + 1940, y0 + yy + 44 + 26 * i, s, 17)

    wins = {}
    for key, brightest in (('bright', True), ('dark', False)):
        v, name, x, y = pick_window(352, 268, brightest)
        wins[key] = dict(capture=name, x=x, y=y, mean_luminance=round(v, 4))
    REP['hud_windows'] = wins
    for key, title in (('bright', '밝은 장면'), ('dark', '어두운 장면')):
        w = wins[key]
        sh.head('(라-2)' if key == 'bright' else '(라-3)', 'HUD 군집과 미니맵 틀 — %s (×2)' % title, (
            '배경 = 두 캡처에서 가장 %s 352×268 창 (%s.png %d,%d · 평균 휘도 %.3f). 왼쪽부터: 오늘의 군집 · 테마 군집 · 오늘의 미니맵 · 테마 미니맵.' % (
                '밝은' if key == 'bright' else '어두운', w['capture'], w['x'], w['y'], w['mean_luminance']),))
        a, y0 = sh.band(560)
        x = PADX
        cw, ch = HM.CROP[2] - HM.CROP[0], HM.CROP[3] - HM.CROP[1]
        for themed in (False, True):
            dst = crop(w['capture'], w['x'] + (352 - cw) // 2, w['y'] + (268 - ch) // 2, cw, ch, 2.0)
            hud_draw(dst, HM.CROP, 2.0, themed)
            a[8:8 + dst.shape[0], x:x + dst.shape[1]] = dst
            frame1(a, x, 8, dst.shape[1], dst.shape[0])
            x += dst.shape[1] + 8
        x += 8
        for themed in (False, True):
            dst = crop(w['capture'], w['x'] + 16, w['y'] + 4, 320, 262, 1.75)
            mm, cx, cy = mini_themed(kit, 1.75) if themed else mini_today(1.75)
            TS.paste(dst, mm, dst.shape[1] / 2 - cx, dst.shape[0] / 2 - cy + 6)
            a[8:8 + dst.shape[0], x:x + dst.shape[1]] = dst
            frame1(a, x, 8, dst.shape[1], dst.shape[0])
            x += dst.shape[1] + 8
        sh.text(PADX, y0 + 8 + ch * 2 + 8, '군집 ×2 (체력 62 % · 먹 44 %, 표시 셋 = 젖음). 미니맵 ×1.75 (안쪽 지도는 자리 표시 — 지도 내용은 SPEC-MAP-OVERHAUL-308 소유).', 17)

    sh.head('(라-4)', '목테가 값을 가리는가 — 가득(100 %)과 바닥(8 %) ×1.5, 표시의 세 상태', (
        '목테는 목 + 입술만 덮는다(사각형 높이의 위 11 %). 가득 선은 어깨에 있어 목테 아래 끝보다 체력 6.95 px · 먹 6.16 px 아래다 [M, Spec §3]. 받침 · 마개는 없다.',))
    a, y0 = sh.band(440)
    x = PADX
    full = (HM.hp_state(1.0), HM.ink_state(1.0))
    low = (HM.hp_state(.08), HM.ink_state(.08))
    for (hp, ink), cap in ((full, '가득 100 %'), (low, '바닥 8 %')):
        for bgc in (L.SKY, L.PINE):
            dst = TS.flat(int(cw * 1.5), int(ch * 1.5), bgc)
            hud_draw(dst, HM.CROP, 1.5, True, hp, ink)
            a[8:8 + dst.shape[0], x:x + dst.shape[1]] = dst
            frame1(a, x, 8, dst.shape[1], dst.shape[0])
            sh.text(x + 8, y0 + 14, cap, 18, 'bold', TXT if bgc is L.SKY else T['Paper'])
            x += dst.shape[1] + 8
    area = []
    for row, bgc in enumerate((L.SKY, L.PINE)):
        TS.rect(a, x, 8 + row * 172, 3 * 150 + 20, 164, bgc)
        for col, (state, fill) in enumerate((('wet', 1.0), ('rewet', .55), ('dry', 0.0))):
            r_, ar = roundel('vehicle', state, fill, s=3.0, ss=3)
            TS.paste(a, r_, x + 19 + col * 150, 24 + row * 172)
            area.append(ar)
    sh.text(x, y0 + 356, '표시 하나의 세 상태 ×3: 젖음 · 되젖음 55 %% · 마름(자개 α.30 = %.2f:1)' % L.contrast(L.over(NACRE, T['Lacquer'], .30), T['Lacquer']), 16)
    sh.text(x, y0 + 378, '기호는 넓은 조각이라 채도를 선의 60 %로 낮춘다(진주빛).', 16)
    REP['hud_roundel'] = dict(glyph_scale=TS.ROUNDEL['glyph'], face_chroma=FACE_CHROMA, vehicle_glyph_nacre_px2=round(area[0], 1))
    sh.text(PADX, y0 + 8 + int(ch * 1.5) + 8, '바탕: 가장 밝은 하늘 %s / 검은 솔숲 %s (DESIGN §2.3). 옻칠 / 하늘 %.2f:1, 자개 / 옻칠 %.2f:1, 한지 실테 α.92 / 솔숲 %.2f:1.' % (
        L.to_hex(L.SKY), L.to_hex(L.PINE), L.contrast(T['Lacquer'], L.SKY), L.contrast(NACRE, T['Lacquer']),
        L.contrast(L.over(T['Paper'], L.PINE, .92), L.PINE)), 17)


# ---------------------------------------------------------------------------------------------------------------------
# equipment page: the #308 mock re-composed with the kit (the mock's own functions, patched for one call)
# ---------------------------------------------------------------------------------------------------------------------
WOOD8, LAC8, INLAY8, PORC8 = rgb8(T['Wood']), rgb8(T['Lacquer']), rgb8(T['InlayDark']), rgb8(T['Porcelain'])
ROWS = (2, 4, 5, 5)
BAR_V, BAR_H, CLEAR, FRAME_W = 4, 3, 8, 6


def equip_themed(kit, view, phase2=True):
    S0 = EM.Screen
    orig = dict(frame_page=EM.frame_page, draw_grid=EM.draw_grid, picture=EM.picture, keycap=S0.keycap, steps=S0.steps)

    def blit(s, rgba, x, y):
        s.blit(to_pil(rgba), x, y)

    def frame_page(s, at_shop, fragment_tab):
        s.blit(Image.open(EM.WORLD).convert('RGBA').resize((EM.W, EM.H), Image.LANCZOS), 0, 0)
        veil = Image.open(os.path.join(EM.AS, 'veil_wash.png')).convert('L').crop((40, 0, 40 + EM.W, EM.H))
        s.mask(veil, EM.VEIL, 0, 0)
        s.stroke('stroke_sweep', EM.INK, -380, 640, 1700, 620, .3, rot=-8)
        if phase2:
            blit(s, kit.strip('line_najeon', 1792), 64, 112)                  # the rail: one cut-shell line (128 x 14)
        else:
            s.stroke('stroke_line', EM.PAPER, 64, 106, 1792, 16, .45)
        x = 64
        x += s.keycap(x, 60, 'Q') + 44
        for t in EM.TABS + (['정비'] if at_shop else []):
            slot = EM.tw(t, 'title34')
            if t == '소지품':
                s.stroke('stroke_swell', EM.PAPER, x - 26, 86, slot + 56, 36, .95)
                s.text_bl(x, 90, t, 'title34', EM.PAPER)
            else:
                s.text_bl(x, 90, t, 'label24', EM.MIST)
            x += slot + 44
        x += s.keycap(x, 60, 'E') + 40
        x += s.keycap(x, 60, 'Esc') + 10
        s.text_bl(x, 86, '닫기', 'meta20', EM.MIST)
        held = len(EM.known_letters())
        x = 64
        for name, n, sel in (('장비', len(EM.SAVE['owned']), not fragment_tab), ('석경', held, fragment_tab)):
            role = 'title32' if sel else 'label24'
            lw = EM.tw(name, role)
            if sel:
                s.stroke('stroke_swell', EM.PAPER, x - 20, 192, max(150, lw + 6 + EM.tw(str(n), 'meta20') + 44), 30, .9)
            s.text_bl(x, 189, name, role, EM.PAPER if sel else EM.MIST)
            nw = s.text_bl(x + lw + 6, 189, str(n), 'meta20', EM.MIST)
            x += lw + 6 + nw + 44
        bl = EM.baseline(160, 'fig36')
        vw = s.text_bl(EM.RR, bl, format(EM.SAVE['currency'], ','), 'fig36', EM.PAPER, anchor='r', tab=True)
        mw = s.text_bl(EM.RR - vw - 14, bl, '조선통보', 'meta20', EM.MIST, anchor='r')
        s.sprite(EM.amask('coin'), EM.MIST, EM.RR - vw - 14 - mw - 10 - 26, bl - 21, 26, 26)
        s.rect(739, 232, 3, 708, WOOD8)                                        # column rules: lattice bars (yongja)
        s.rect(1439, 232, 3, 708, WOOD8)

    def bars(s):
        """jeongja lattice: the bars ARE the grid. A thicker door frame (ulgeomi) round the leaf, thin bars inside,
        every row closed only as far as it has cells."""
        GX, GY, PX, PY, CS = EM.GX, EM.GY, EM.PX, EM.PY, EM.CS
        half = (PX - CS) // 2                                                  # 10 px: the middle of the 20 px gap
        xl = GX - half - BAR_V // 2
        right = lambda n: GX + PX * (n - 1) + CS + half - BAR_V // 2           # left edge of the bar that closes n cells
        ys = [GY + PY * r - half - BAR_H for r in range(len(ROWS))] + [GY + PY * (len(ROWS) - 1) + CS + 39]
        for r, n in enumerate(ROWS):
            for i in range(1, n):
                s.rect(GX + PX * i - half - BAR_V // 2, ys[r], BAR_V, ys[r + 1] - ys[r], WOOD8)
        for k, y in enumerate(ys):
            n = max(ROWS[k - 1] if k > 0 else 0, ROWS[k] if k < len(ROWS) else 0)
            outer = k == 0 or k == len(ROWS)
            s.rect(xl, y, right(n) + BAR_V - xl, FRAME_W if outer else BAR_H, WOOD8)
        s.rect(xl - (FRAME_W - BAR_V), ys[0], FRAME_W, ys[-1] + FRAME_W - ys[0], WOOD8)          # the hinge side of the frame
        for r, n in enumerate(ROWS):                                                             # the stepped closing side
            y1 = ys[r + 1] + (FRAME_W if r == len(ROWS) - 1 else BAR_H)
            s.rect(right(n), ys[r], FRAME_W, y1 - ys[r], WOOD8)

    def draw_grid(s, focus, kept=None):
        bars(s)
        for kind, i, x, y in EM.slot_cells():
            foc = focus == (kind, i)
            label_col = EM.PAPER if foc else EM.MIST
            if kind == 'gear':
                worn = EM.SAVE['equipped'][i]
                if worn:
                    s.chip(x, y)
                    s.gear(i, x + 8, y + 8, 88)
                else:
                    s.chip(x, y, op=.16)
                    s.gear(i, x + 12, y + 12, 80, ghost=True)
                lv = EM.SAVE['owned'][worn] if worn else 0
                s.text(x, y + 110, EM.SLOT_NAMES[i] + (' +%d' % lv if lv else ''), 'meta20', label_col)
            elif kind == 'stone':
                s.chip(x, y)
                s.stamp(i, x + 20, y + 20, 64, EM.INK, 28)
                lw = s.text(x, y + 110, EM.ENAME[i], 'meta20', label_col)
                s.steps(x + lw + 10, y + 117, EM.SAVE['levels'][i], 3, 18, 14, 19)
            else:
                have = EM.VIRTUES[i] in EM.SAVE['virtues']
                if have:
                    s.chip(x, y)
                    s.seal(x + 8, y + 8, 88, EM.INK)
                    s.text(x + EM.CS / 2.0, y + 26, EM.VIRTUES[i], ('S9', 46, 1.1, 0), EM.INK, anchor='c')
                else:                                                          # locked: lattice without paper (bit)
                    m = kit.lattice('bit', 88, 88, 1.0)
                    s.mask(Image.fromarray((m * 255 + .5).astype(np.uint8), 'L'), WOOD8, x + 8, y + 8)
                    s.seal(x + 8, y + 8, 88, EM.OFF)
                s.text(x, y + 110, EM.VREAD[i], 'meta20', (EM.PAPER if foc else EM.MIST) if have else EM.OFF)
            if foc:
                s.cell_focus(x, y, EM.CS, EM.CS)
                s.dab(x - 30, y + 113, 24, 18)
            elif kept == (kind, i):
                blit(s, kit.rect('frame_select', EM.CS + 10, EM.CS + 10), x - 5, y - 5)

    def picture(s, kind, i=0, letter=None, empty=False):
        x, y, n = 1204, 232, 200
        if empty or kind == 'glyph':
            return orig['picture'](s, kind, i, letter, empty)
        blit(s, kit.rect('plaque_porcelain', n, n), x, y)                      # porcelain plaque + inlaid double line
        if kind == 'gear':
            s.gear(i, x + 20, y + 18, 160)
        elif kind == 'stone':
            s.stamp(i, x + 36, y + 34, 128, INLAY8, 58)
        elif kind == 'virtue':
            s.seal(x + 30, y + 28, 140, INLAY8)
            s.text(x + n / 2.0, y + 48, EM.VIRTUES[i], ('S9', 92, 1.1, 0), INLAY8, anchor='c')

    def keycap(self, x, y, label, hollow=False):
        if hollow or not phase2:
            return orig['keycap'](self, x, y, label, hollow)
        w = max(34, int(round(EM.tw(label, 'key16'))) + 20)
        blit(self, kit.rect('keycap_porcelain', w, 34), x, y)
        self.text(x + w / 2.0, y + 6, label, 'key16', INLAY8, anchor='c')
        return w

    def steps(self, x, y, level, maximum, w=28, h=21, pitch=36, on=EM.PAPER, off=EM.PENDING):
        k = min(2.0, h / 10.0 * .92)                                          # the 7 x 10 petal stands as tall as the dab it replaces
        pet = kit.simple('pip_petal', k)
        ghost = kit.simple('pip_petal', k, tint=T['Mist'])                    # a tier not reached: the same petal, Mist a.35
        ghost[..., 3] *= .35
        for j in range(maximum):
            blit(self, pet if j < level else ghost, x + j * pitch + (w - pet.shape[1]) / 2.0, y + (h - pet.shape[0]) / 2.0)

    EM.frame_page, EM.draw_grid, EM.picture, S0.keycap, S0.steps = frame_page, draw_grid, picture, keycap, steps
    try:
        im = EM.render(view, True)
    finally:
        EM.frame_page, EM.draw_grid, EM.picture, S0.keycap, S0.steps = (orig[k] for k in ('frame_page', 'draw_grid', 'picture', 'keycap', 'steps'))
    return im


def arr(im):
    return np.asarray(im.convert('RGB'), float) / 255.0


def scaled(im, f):
    return arr(im.resize((int(round(im.width * f)), int(round(im.height * f))), Image.LANCZOS))


def panel_equip(sh, kit):
    today = EM.render('stone', True)
    themed = equip_themed(kit, 'stone')
    today_c = EM.render('candidate', True)
    themed_c = equip_themed(kit, 'candidate')
    sh.head('(라-5)', '장비 · 소지품 화면 — 오늘의 시안(왼쪽)과 테마 적용(오른쪽)', (
        '오늘 = Art/UI308/Equipment/equip308_a_stone.png 그대로(같은 함수로 다시 그림). 테마 = 같은 배치 · 같은 글 · 같은 수치에 키트만 갈아 끼운 것. 먹장막은 그대로이고 옻칠로 읽는다.',
        '바뀐 것: 칸 격자 = 살대(정자살, 울거미 6 px · 살 4 / 3 px) + 창호지 칩 · 열 가름 = 살대 3 px · 그림 틀 = 백자 판 + 이중 상감선 · 강화 단계 = 자개 꽃잎 · 잠긴 오덕 칸 = 종이 없는 빗살 · (2단계) 레일 = 끊음질 선, 채운 건반 = 백자 단추. 초점(주사 붓 테 + 방점)은 #304 그대로.'))
    f = 1133 / 1920.0
    a, y0 = sh.band(660)
    for i, im in enumerate((today, themed)):
        p = scaled(im, f)
        x = PADX + i * (1133 + 38)
        a[8:8 + p.shape[0], x:x + p.shape[1]] = p
        frame1(a, x, 8, p.shape[1], p.shape[0])
    sh.head('', '같은 화면의 1080p 화소 (×1): 레일 · 칸 격자 · 그림 틀 · 선택 유지', (
        '칸 격자: 살대는 칩에서 8 px, 이름 글 상자에서 8 px 이상. 줄마다 칸 수만큼만 닫는다(2 · 4 · 5 · 5). 오른쪽 아래 = 후보 줄에 초점이 갔을 때 붓 칸에 남는 선택 유지 표: 오늘은 안개색 붓 테, 테마는 자개 끊음질 테(Frame.Select).',))
    a, y0 = sh.band(196)
    rl = (56, 44, 1320, 132)
    for i, im in enumerate((today, themed)):
        q = arr(im.crop(rl))
        a[8 + i * 96:8 + i * 96 + q.shape[0], PADX:PADX + q.shape[1]] = q
        frame1(a, PADX, 8 + i * 96, q.shape[1], q.shape[0])
        sh.text(PADX + q.shape[1] + 16, y0 + 8 + i * 96 + 8, ('오늘: 레일 = 붓 선(한지 α.45), 채운 건반 = 한지 면 + 먹 테', '테마(2단계): 레일 = 끊음질 선 2 px, 채운 건반 = 백자 단추')[i], 17, 'bold', TXT)
        sh.text(PADX + q.shape[1] + 16, y0 + 8 + i * 96 + 34, ('', '선택 탭의 한지 부풂과 탭 글자는 그대로다(글 밑은 건드리지 않는다).')[i], 16)
    a, y0 = sh.band(700)
    g = (60, 292, 740, 962)
    d = (760, 222, 1424, 470)
    k = (70, 296, 350, 470)
    for i, im in enumerate((today, themed)):
        p = arr(im.crop(g))
        x = PADX + i * (680 + 16)
        a[8:8 + p.shape[0], x:x + p.shape[1]] = p
        frame1(a, x, 8, p.shape[1], p.shape[0])
        q = arr(im.crop(d))
        xx, yy = PADX + 2 * 696 + 8, 8 + i * 256
        a[yy:yy + q.shape[0], xx:xx + q.shape[1]] = q
        frame1(a, xx, yy, q.shape[1], q.shape[0])
    for i, im in enumerate((today_c, themed_c)):
        q = arr(im.crop(k))
        xx, yy = PADX + 2 * 696 + 8 + i * 296, 8 + 2 * 256
        a[yy:yy + q.shape[0], xx:xx + q.shape[1]] = q
        frame1(a, xx, yy, q.shape[1], q.shape[0])
    return themed


# ---------------------------------------------------------------------------------------------------------------------
# full map: today's capture with the lacquer board slipped under the paper (quick composite)
# ---------------------------------------------------------------------------------------------------------------------
BOARD = (542, 126, 1378, 1036)      # the map Spec says 1018; see the caption (the control row would sit on the band)


def map_themed(kit):
    cap = arr(Image.open(CAP_MAP))
    out = cap.copy()
    x0, y0, x1, y1 = BOARD
    w, h = x1 - x0, y1 - y0
    body = np.dstack([np.ones((h, w, 3)) * T['Lacquer'], np.ones((h, w))])
    TS.paste(out, body, x0, y0)
    TS.paste(out, kit.rect('frame_board', w, h), x0, y0)
    lum = L.lum(cap[y0:y1, x0:x1])
    key = np.clip((lum - .10) / .12, 0, 1)                                    # paper, ink on paper, key caps and their words
    ys, xs = np.mgrid[0:h, 0:w]
    inner = ((xs >= 34) & (xs < w - 34) & (ys >= 34) & (ys < 824)).astype(float)   # the sheet itself, away from its torn edge
    ring = ((xs < 20) | (xs >= w - 20) | (ys < 20) | (ys >= h - 22)).astype(float)   # the kit's band: never keyed over
    m = np.maximum(inner, key) * (1 - ring)
    out[y0:y1, x0:x1] = out[y0:y1, x0:x1] * (1 - m[..., None]) + cap[y0:y1, x0:x1] * m[..., None]
    return Image.fromarray((np.clip(cap, 0, 1) * 255 + .5).astype(np.uint8)), Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8))


def panel_map(sh, kit):
    today, themed = map_themed(kit)
    sh.head('(라-6)', '펼친 지도 — 종이를 받친 칠반 (빠른 합성)', (
        '오늘 = 실제 캡처 Art/UI304/after3/map_revealed.png. 테마 = 그 캡처의 종이 · 조작 줄 밑에 흑칠 판과 Frame.MapBoard(끊음질 선 + 두 단 아자 꺾임 + 완자살 띠)를 끼운 것.',
        '지도 면(종이 · 먹)은 건드리지 않는다. 범례 기호 밑 판(Plate.Icon)과 목록 가름선(Inlay.LineThin)은 지도 Spec의 시안이 입힌다. 오른쪽 = 왼쪽 위 귀의 1080p 화소 ×2.',
        '주의: 지도 Spec의 판은 836×892(아래 끝 y 1018)인데 조작 줄(y 979–1013)이 그 아랫변 18 px 띠와 겹친다. 여기서는 판을 836×910(아래 끝 y 1036, Tiled 주기와도 맞음)으로 그렸다 — 지도 Spec이 정할 일.'))
    f = 940 / 1920.0
    a, y0 = sh.band(550)
    REP['map_board'] = dict(rect=list(BOARD), size=[BOARD[2] - BOARD[0], BOARD[3] - BOARD[1]], spec_size=[836, 892])
    for i, im in enumerate((today, themed)):
        p = scaled(im, f)
        x = PADX + i * (940 + 12)
        a[8:8 + p.shape[0], x:x + p.shape[1]] = p
        frame1(a, x, 8, p.shape[1], p.shape[0])
    cr = (532, 116, 732, 246)
    for i, im in enumerate((today, themed)):
        q = arr(im.crop(cr).resize((400, 260), Image.NEAREST))
        xx, yy = PADX + 2 * 952 + 4, 8 + i * 268
        a[yy:yy + q.shape[0], xx:xx + min(q.shape[1], SW - PADX - xx)] = q[:, :SW - PADX - xx]


# ---------------------------------------------------------------------------------------------------------------------
# (b) najeon on lacquer, (c) porcelain with inlay
# ---------------------------------------------------------------------------------------------------------------------
def on(col, rgba):
    out = TS.flat(rgba.shape[1], rgba.shape[0], col)
    TS.paste(out, rgba, 0, 0)
    return out


def panel_najeon(sh, kit):
    cn = L.contrast(NACRE, T['Lacquer'])
    nmax = 0
    for name in ('frame_select', 'line_najeon', 'line_najeon_thin', 'corner_fret', 'pip_petal', 'motif_chrys'):
        t = kit.tex(name)
        nmax = max(nmax, int(round(t[..., :3][t[..., 3] > .5].max() * 255)))
    REP['nacre_max_channel'] = nmax
    sh.head('(나)', '나전칠기 — 옻칠 위의 자개: 틀 · 선 · 조각', (
        '아틀라스(theme308_atlas.png)의 칸을 Unity처럼 깐 것(Tiled = 가운데 구간이 한 주기 = 같은 길이 조각 셋, 이음이 슬라이스 선 위에 있어 조각이 늘어나지 않는다). 윗줄 = 300 %, 아랫줄 = 1080p 실제 크기.',
        '옻칠 %s 은 민 값(광택 · 그라데이션 없음). 자개는 LDR 색: 조각마다 녹 · 청 · 분홍 계열을 씨앗으로 뽑고 조각 안에서 색상만 ±28° 흔들린다. 자개 / 옻칠 %.2f:1 · 최대 채널 %d.' % (
            L.TOK308['Lacquer'], cn, nmax)))
    a, y0 = sh.band(700)
    TS.rect(a, PADX - 12, 0, SW - 2 * PADX + 24, 700, T['Lacquer'])
    lab = lambda x, y, s, sub=None: (sh.text(x, y0 + y, s, 17, 'bold', T['Paper']), sub and sh.text(x, y0 + y + 22, sub, 15, 'sans', T['Mist']))
    # 300 %
    x = PADX + 8
    mm, _, _ = mini_themed(kit, 3.0)
    put(a, mm[:300, :390], x, 16)
    lab(x, 326, 'Frame.Mini · 미니맵 문틀', '문틀 9 px · 선 1.5 px · 귀 14 px · Piece.North')
    x += 420
    bd = kit.rect('frame_board', 200, 160, 3.0)
    paper = np.dstack([np.ones((bd.shape[0], bd.shape[1], 3)) * T['Sheet'], np.zeros(bd.shape[:2])])
    paper[54:, 54:, 3] = 1.0
    bd2 = on(T['Lacquer'], bd)
    TS.paste(bd2, paper, 0, 0)
    put(a, bd2[:300, :390], x, 16)
    lab(x, 326, 'Frame.MapBoard · 칠반의 귀', '선 1.5 px + 두 단 꺾임 · 완자살 띠 10 px (살대색)')
    x += 420
    put(a, kit.rect('frame_select', 114, 114, 3.0)[:300, :300], x, 16)
    ch = Image.open(os.path.join(EM.AS, 'tile_chip.png')).convert('RGBA').resize((312, 312), Image.LANCZOS)
    put(a, (np.asarray(ch, float) / 255.0)[:285, :285], x + 15, 31)
    put(a, kit.rect('frame_select', 114, 114, 3.0)[:300, :300], x, 16)
    lab(x, 326, 'Frame.Select · 선택 유지', '끊음질 네 띠 2 px, 칸 밖 3 px')
    x += 330
    put(a, kit.strip('line_najeon', 128, 3.0), x, 30)
    put(a, kit.strip('line_najeon_thin', 128, 3.0), x, 70)
    lab(x, 96, 'Inlay.Line 2 px / LineThin 1.5 px', '조각 9 – 12.5 px · 이음 %.1f px · 주기 128 px' % L.NACRE['joint_px'])
    xx = x
    for name, cap in (('corner_fret', 'Corner.Fret'), ('piece_north', 'Piece.North'), ('pip_petal', 'Pip.Petal')):
        put(a, kit.simple(name, 3.0), xx, 160)
        sh.text(xx, y0 + 216, cap, 15, 'sans', T['Mist'])
        xx += 110
    put(a, kit.rect('plate_lacquer', 40, 40, 3.0), x, 250)
    sh.text(x, y0 + 376, 'Plate.Icon', 15, 'sans', T['Mist'])
    put(a, kit.simple('plate_round', 3.0), x + 150, 244)
    sh.text(x + 150, y0 + 392, 'Plate.Round 44', 15, 'sans', T['Mist'])
    x += 420
    for i, (name, cap) in enumerate((('motif_chrys', '국화'), ('motif_plum', '매화'), ('motif_gwigap', '귀갑'))):
        put(a, kit.simple(name, 2.0), x + (0, 110, 170)[i], 16 + (0, 20, 14)[i])
    put(a, kit.strip('border_fret', 200, 2.0), x, 130)
    put(a, kit.strip('border_vine', 176, 2.0), x, 176)
    lab(x, 250, '예비 무늬 ×2 (소비처 없음)', '국화 · 매화 · 귀갑 · 뇌문 테 · 당초 테')
    # real size
    x = PADX + 8
    y = 452
    mm, _, _ = mini_themed(kit, 1.0)
    put(a, mm, x, y - 30)
    x += 350
    put(a, bd_real(kit), x, y - 22)
    x += 300
    cell = on(T['Lacquer'], kit.rect('frame_select', 114, 114, 1.0))
    ch1 = np.asarray(Image.open(os.path.join(EM.AS, 'tile_chip.png')).convert('RGBA').resize((104, 104), Image.LANCZOS), float) / 255.0
    TS.paste(cell, ch1, 5, 5)
    put(a, cell, x, y)
    x += 150
    put(a, kit.strip('line_najeon', 384, 1.0), x, y)
    put(a, kit.strip('line_najeon_thin', 384, 1.0), x, y + 16)
    xx = x
    for name in ('corner_fret', 'piece_north', 'pip_petal'):
        put(a, kit.simple(name, 1.0), xx, y + 40)
        xx += 40
    put(a, kit.rect('plate_lacquer', 40, 40, 1.0), xx + 10, y + 36)
    put(a, kit.simple('plate_round', 1.0), xx + 70, y + 32)
    put(a, kit.strip('border_fret', 300, 1.0), x, y + 96)
    put(a, kit.strip('border_vine', 264, 1.0), x, y + 120)
    x += 420
    for i, name in enumerate(('motif_chrys', 'motif_plum', 'motif_gwigap')):
        put(a, kit.simple(name, 1.0), x + (0, 60, 96)[i], y + (0, 12, 8)[i])
    sh.text(PADX + 8, y0 + 392, '아래 = 1080p 실제 크기 (디자인 px 1 = 화소 1, 아틀라스의 밉 1단계)', 17, 'bold', T['Paper'])


def bd_real(kit):
    bd = on(T['Lacquer'], kit.rect('frame_board', 290, 200, 1.0))
    bd[18:, 18:] = T['Sheet']
    return bd[:180, :270]


def panel_porcelain(sh, kit):
    ci, ca, cd, cr = (L.contrast(T['Ink'], T['Porcelain']), L.contrast(T['Ash'], T['Porcelain']),
                      L.contrast(T['InlayDark'], T['Porcelain']), L.contrast(T['InlayIron'], T['Porcelain']))
    REP['porcelain'] = dict(ink=round(ci, 2), ash=round(ca, 2), inlay_dark=round(cd, 2), inlay_iron=round(cr, 2),
                            shade=round(L.contrast(T['PorcelainShade'], T['Porcelain']), 2),
                            vs_paper=round(L.contrast(T['Porcelain'], T['Paper']), 3))
    sh.head('(다)', '백자 상감 — 밝은 판과 파서 메운 선', (
        '백자 %s 는 찬 흰빛의 민 값(유약 반사 없음), 굽 선은 값 한 단(백자 그늘 %s). 상감선은 칼로 판 가장자리(번짐 · 갈필 없음). 먹 / 백자 %.2f:1 · 재 / 백자 %.2f:1 · 흑상감 / 백자 %.2f:1.' % (
            L.TOK308['Porcelain'], L.TOK308['PorcelainShade'], ci, ca, cd),
        '판 안쪽(글이 놓이는 자리)은 무늬 0. 철사 상감 %s (%.2f:1)은 재질색이지 신호색이 아니다 — 세 표면에서는 쓰지 않는다(질문 6). 띠 무늬 셋은 예비(소비처 없음).' % (L.TOK308['InlayIron'], cr)))
    a, y0 = sh.band(470)
    veil = L.over(T['Veil'], L.hexc('#6B675E'), .93)
    TS.rect(a, PADX - 12, 0, SW - 2 * PADX + 24, 470, veil)
    x = PADX + 12
    s = EM.Screen(240, 240, bg=rgb8(veil))
    s.blit(to_pil(kit.rect('plaque_porcelain', 200, 200)), 20, 20)
    s.stamp(0, 56, 54, 128, INLAY8, 58)
    put(a, arr(s.im), x, 10)
    sh.text(x + 20, y0 + 240, 'Plaque.Porcelain 200² (×1)', 17, 'bold', T['Paper'])
    sh.text(x + 20, y0 + 262, '이중선 2 / 3 / 1 px · 굽 3 px', 15, 'sans', T['Mist'])
    x += 260
    put(a, kit.rect('plaque_porcelain', 200, 200, 3.0)[:270, :270], x, 30)
    sh.text(x, y0 + 310, '왼쪽 위 귀 ×3', 15, 'sans', T['Mist'])
    x += 300
    put(a, kit.rect('plaque_porcelain_single', 380, 104, 1.0), x, 30)
    sh.text(x + 22, y0 + 46, '모든 속성 위력 +6%', 26, 'serif', T['Ink'])
    sh.text(x + 22, y0 + 86, '기본 +4% · 강화 +2% · 조선통보 1,240', 20, 'sans', T['Ash'])
    sh.text(x, y0 + 144, 'Plaque.PorcelainSingle (풀이 판, ×1)', 17, 'bold', T['Paper'])
    sh.text(x, y0 + 166, '한 줄선 1.5 px · 글 밑 무늬 0', 15, 'sans', T['Mist'])
    kx = x
    for lab_, w in (('Q', 34), ('Enter', 66), ('Esc', 48)):
        put(a, kit.rect('keycap_porcelain', w, 34, 1.0), kx, 220)
        sh.labels.append((kx + w / 2 - TS.font(16, 'bold').getlength(lab_) / 2, y0 + 225, lab_, 16, 'bold', T['InlayDark']))
        kx += w + 14
    put(a, kit.rect('keycap_porcelain', 66, 34, 3.0), kx + 20, 210)
    sh.labels.append((kx + 20 + 99 - TS.font(48, 'bold').getlength('Enter') / 2, y0 + 225, 'Enter', 48, 'bold', T['InlayDark']))
    sh.text(x, y0 + 326, 'Key.Porcelain (채운 건반, ×1 과 ×3)', 17, 'bold', T['Paper'])
    sh.text(x, y0 + 348, '면 백자 · 테 흑상감 2 px · 턱 백자 그늘 5 px', 15, 'sans', T['Mist'])
    sh.text(x, y0 + 368, '2단계 (전 메뉴 공통으로 같이 바꾼다)', 15, 'sans', T['Mist'])
    x += 560
    W = SW - PADX - x
    TS.rect(a, x, 20, W, 420, T['Porcelain'])
    TS.rect(a, x, 20 + 417, W, 3, T['PorcelainShade'])
    yy = 40
    for name, cap in (('sanggam_lotus', '연판 띠'), ('sanggam_stamps', '인화 국화 줄'), ('sanggam_vine', '당초 선')):
        put(a, kit.strip(name, kit.cells[name]['design_px'][0] * 2, 2.0, tint=T['InlayDark']), x + 24, yy)
        put(a, kit.strip(name, kit.cells[name]['design_px'][0] * 3, 1.0, tint=T['InlayDark']), x + 24, yy + 64)
        put(a, kit.strip(name, kit.cells[name]['design_px'][0] * 2, 1.0, tint=T['InlayIron']), x + 24 + 420, yy + 64)
        sh.text(x + 24, y0 + yy + 92, cap + ' — 위 ×2 흑상감, 아래 ×1 흑상감 / 철사(재질색)', 15, 'sans', T['Ash'])
        yy += 130


def panel_kit(sh, kit):
    m = kit.meta
    sh.head('(키트)', '아틀라스 한 장 + 문살 타일 8장 — 스테이지에 있는 그대로', (
        'theme308_atlas.png %d×%d RGBA (sRGB, 무압축, 밉맵, Clamp) · 칸 %d개 · 채움 %.0f %% · 디자인 px의 2배 저작 · 칸 사이 6 텍셀(가장자리 3 텍셀 복제, 투명 텍셀 밑에 색 번짐). 왼쪽 = 화소 1:1(체크 = 투명).'
        % (m['size'][0], m['size'][1], len(m['cells']), m['fill'] * 100),
        '구운 색 = 두 재질이 한 꼴에 있는 칸(Graphic.color 흰색). 흰 마스크 = 한 재질(색은 토큰). Tiled 칸은 가운데 구간이 한 주기(조각 셋)라 조각이 늘어나지 않는다. 예비 = 소비처 없음.'))
    a, y0 = sh.band(540)
    H, W = kit.at.shape[:2]
    ys, xs = np.mgrid[0:H, 0:W]
    chk = np.where(((xs // 8 + ys // 8) % 2)[..., None] == 0, L.hexc('#8E8A80'), L.hexc('#7C786F'))
    al = kit.at[..., 3:4]
    a[8:8 + H, PADX:PADX + W] = chk * (1 - al) + kit.at[..., :3] * al
    frame1(a, PADX, 8, W, H)
    cols = (0, 250, 470, 580, 770, 900)
    x0 = PADX + W + 40
    for cx, t in zip(cols, ('칸', '키트 이름', '텍셀', '쓰는 법', '슬라이스 px', '꼴 · 쓰는 곳')):
        sh.text(x0 + cx, y0 + 8, t, 17, 'bold', TXT)
    use = dict(frame_mini='미니맵 문틀 (312×228 = 48 + 12n)', frame_board='펼친 지도 칠반 (80 + 9n × 82 + 9m)', line_najeon='끊음질 선 2 px: 레일',
               line_najeon_thin='끊음질 선 1.5 px: 지도 가름', corner_fret='두 단 아자 꺾임 (낱개 귀)', piece_north='북쪽 꽃잎 + 옻칠 받침',
               plate_lacquer='범례 기호 밑 칠 판', plate_round='옻칠 단추 44 (메뉴)', plaque_porcelain='백자 판 + 이중선: 그림 틀',
               plaque_porcelain_single='백자 판 + 한 줄선: 풀이 판', keycap_porcelain='백자 건반 (2단계)', frame_select='선택 유지 테 (114 = 24 + 10n)',
               pip_petal='강화 단계 꽃잎', white='살대 · 판 사각형', motif_chrys='국화', motif_plum='매화', border_fret='뇌문 테', border_vine='당초 테',
               motif_gwigap='귀갑', sanggam_lotus='연판 띠', sanggam_stamps='인화 국화 줄', sanggam_vine='당초 선')
    order = [c for c in m['cells'] if not c.get('reserve')] + [c for c in m['cells'] if c.get('reserve')]
    for i, c in enumerate(order):
        y = y0 + 36 + i * 22
        col = SUB if c.get('reserve') else TXT
        b = c['border_px']
        row = (c['name'], c['kit'], '%d×%d' % (c['rect'][2], c['rect'][3]), c['use'] + (' · 구운 색' if c['mode'] == 'baked' else ' · 마스크'),
               '—' if not any(b) else ('%g' % b[0] if len(set(b)) == 1 else '%g / 아래 %g' % (b[0], b[1])), ('예비: ' if c.get('reserve') else '') + use[c['name']])
        for cx, t in zip(cols, row):
            sh.text(x0 + cx, y, t, 16, 'sans', col)
    REP['kit'] = dict(atlas=m['size'], cells=len(m['cells']), fill=m['fill'], reserve=sum(1 for c in m['cells'] if c.get('reserve')))


def panel_notes(sh):
    sh.head('', 'Spec과 다르게 만든 것 · 알려진 흠', ())
    left = (
        '다르게 만든 것 (이유)',
        '· Frame.Mini 128² → 168²: 가운데 구간 36 px = 12 px 조각 셋. 미니맵 틀의 두 변(264 · 180 px)은 12의 배수라 조각 경계에서 끝난다.',
        '· Frame.MapBoard 192² → 214×218, 아래 슬라이스 42: 주기 27 px = 9 px 조각 셋(완자 9 × 3). 띠의 모듈 3 px · 살 1 px(온 화소).',
        '· Frame.Select 64² → 108²: 주기 30 px = 10 px 조각 셋. 114 px 테의 변(90 px)은 9조각이다.',
        '· Piece.North에 옻칠 받침을 구웠다: 꽃잎 12 px가 문틀 9 px보다 커서 자개가 맨 세계 위에 놓였다(§4.3 위반).',
        '· 넓은 자개 면(꽃잎 · 국화 · HUD 기호)은 채도를 60 %로: 선의 채도 그대로면 하늘색 아이콘으로 읽혔다.',
        '· 조각 계열을 씨앗 추첨(녹 42 · 청 43 · 분홍 15 %)으로: 녹-청-분홍 순환은 300 %에서 사탕 줄무늬로 읽혔다.',
        '· 미니맵 틀의 귀 = 바깥으로 접은 네모 귀(창 때문에 안으로 못 접는다). 칠반의 귀 = 안으로 두 단.',
        '· 만들지 않은 것: 셰이더(1단계 0) · SDF · 둥근 미니맵 틀(지도 Spec 질문 4) · 바탕 텍스처(민 값 = White × 토큰) · 받침 · C#.',
    )
    right = (
        '알려진 흠',
        '· sRGB 합성이다. Unity는 선형이라 가는 선 가장자리의 알파가 조금 다르게 보인다(캡처로 맞출 일).',
        '· HUD 기호가 단추 안에서 .74배가 된다: 1080p에서 자동차 기호의 바퀴살 · 지붕 세부가 뭉개진다.',
        '· 지도 Spec의 칠반(아래 끝 y 1018)은 조작 줄(y 979–1013)과 아랫변 띠가 겹친다. 시안은 910 높이로 그렸다.',
        '· 칸 격자의 살대(장막 위 1.91:1)는 어두운 화면에서 흐리다. 초점 붓 테와 방점은 살대 위를 지나간다.',
        '· 예비 무늬 8칸은 소비처가 없고, 뇌문 · 당초 테의 가로 이음매는 재지 않았다.',
        '· 장비 시안은 2단계(레일 선 · 백자 건반)까지 입혀 그렸다. 1단계만 배포하면 그 둘은 #304 그대로다.',
        '· 거터 3 텍셀: 밉 2단계(540p 이하)에서는 이웃 칸이 번질 수 있다. 미니맵 안쪽 지도는 자리 표시다.',
        '· 자개 선은 300 %에서 파스텔 띠로 보인다(1080p에서는 진주빛 한 줄). 빛 흐름은 없다(정지 색).',
        '· 검토(2026-10-04)에서 고친 것: 이음 .9 → .5 px(1080p에서 점선으로 읽혔다) · 틀의 한 주기에 조각 셋(한 변이 한 조각의 되풀이였다)',
        '  · 띠살의 띠 사이 3u → 7u · 용자살 살 3 → 2 px · 칠반 띠와 백자 안쪽 선을 온 화소에(반 화소 번짐) · 북쪽 조각 칸 16×18.',
    )
    a, y0 = sh.band(30 + 26 * max(len(left), len(right)))
    for i, t in enumerate(left):
        sh.text(PADX + 26, y0 + 4 + 26 * i, t, 17, 'bold' if i == 0 else 'sans', TXT if i == 0 else SUB)
    for i, t in enumerate(right):
        sh.text(PADX + 1190, y0 + 4 + 26 * i, t, 17, 'bold' if i == 0 else 'sans', TXT if i == 0 else SUB)


# ---------------------------------------------------------------------------------------------------------------------
# (e) legibility
# ---------------------------------------------------------------------------------------------------------------------
LINES = (('먹 회복 초당 5', 28, 'serif6', 'main'), ('착용 중인 닳은 붓과 견줌 · 쉼터 곁', 20, 'sans', 'sub'),
         ('87 / 119   +32%   1,240', 24, 'bold', 'main'))
TW, TH = 440, 140


def under_text(bg, lines, cols):
    """contrast of every text line against the worst pixel of the ground under its ink box (before the text is drawn)"""
    d = ImageDraw.Draw(Image.new('L', (TW, TH)))
    res, boxes = {}, []
    y = 14
    for text, size, face, role in lines:
        bb = d.textbbox((18, y), text, font=TS.font(size, face))
        reg = bg[max(0, bb[1]):bb[3], max(0, bb[0]):min(TW, bb[2])]
        lt, lb = float(L.lum(cols[role])), L.lum(reg)
        c = (np.maximum(lt, lb) + .05) / (np.minimum(lt, lb) + .05)
        key = {'main': 'main', 'sub': 'sub'}[role] + ('_num' if text[0].isdigit() else '')
        res[key] = round(float(c.min()), 2)
        boxes.append(reg)
        y += {28: 44, 20: 36, 24: 0}[size]
    lum = np.concatenate([L.lum(b).ravel() for b in boxes])
    res['pattern'] = round(float((lum.max() + .05) / (lum.min() + .05)), 3)
    return res


def sample(kind, ground, toward, fam=None, ratio=None, kit=None):
    bg = TS.flat(TW, TH, ground)
    if kind == 'cap':                    # a lattice in the grey that sits exactly `ratio` from the ground
        pat, _ = TS.grey_at_ratio(ground, toward, ratio)
        m = kit.lattice(fam, TW, TH, 1.0)
        bg = bg * (1 - m[..., None]) + pat * m[..., None]
    elif kind == 'wood':
        m = kit.lattice(fam, TW, TH, 1.0)
        bg = bg * (1 - m[..., None]) + T['Wood'] * m[..., None]
    elif kind == 'nacre':
        for yy in range(10, TH, 14):
            TS.paste(bg, kit.strip('line_najeon_thin', TW, 1.0), 0, yy)
    elif kind == 'inlay':
        for yy in range(6, TH, 24):
            TS.paste(bg, kit.strip(fam, TW, 1.0, tint=T['InlayDark']), 0, yy)
    return bg


def panel_legibility(sh, kit):
    sh.head('(마)', '글 가독성 — 바탕과 무늬 위의 글 · 수치, 잰 대비', (
        '숫자는 글자 색과 그 글 상자 밑 바탕 화소 가운데 가장 불리한 화소의 대비(WCAG 휘도비, sRGB 합성). 바닥: 주 글자 · 수치 7:1, 보조 글자(20 px) 4.5:1. "무늬" = 글 상자 밑 바탕의 가장 밝은 화소와 가장 어두운 화소의 비.',
        '첫 줄 = 기본 설계(글 밑은 민 바탕). 둘째 줄 = 상한 1.15:1의 무늬를 깐 경우(넘지 못할 천장). 셋째 줄 = 금지 예: 키트의 실제 색(살대 · 자개 · 상감)을 글 밑에 깔면 이렇게 된다 — 그래서 구조선은 글에서 8 px, 장식은 16 px 띄운다.'))
    dark = dict(main=T['Paper'], sub=T['Mist'])
    light = dict(main=T['Ink'], sub=T['Ash'])
    rows = [
        [('장막 α.90 (하늘 위)', 'plain', VEIL90, dark, {}), ('옻칠', 'plain', T['Lacquer'], dark, {}), ('생지', 'plain', T['Sheet'], light, {}),
         ('창호지(칩)', 'plain', T['Chip'], light, {}), ('백자', 'plain', T['Porcelain'], light, {})],
        [('장막 + 완자살 1.15', 'cap', VEIL90, dark, dict(fam='wanja', ratio=1.15)), ('장막 + 빗살 1.15', 'cap', VEIL90, dark, dict(fam='bit', ratio=1.15)),
         ('옻칠 + 귀갑살 1.15', 'cap', T['Lacquer'], dark, dict(fam='gwigap', ratio=1.15)), ('생지 + 정자살 1.15', 'cap', T['Sheet'], light, dict(fam='jeongja', ratio=1.15)),
         ('백자 + 아자살 1.15', 'cap', T['Porcelain'], light, dict(fam='aja', ratio=1.15))],
        [('금지: 장막 + 살대 빗살', 'wood', VEIL90, dark, dict(fam='bit')), ('금지: 옻칠 + 자개 선', 'nacre', T['Lacquer'], dark, {}),
         ('금지: 생지 + 살대 정자살', 'wood', T['Sheet'], light, dict(fam='jeongja')), ('금지: 창호지 + 살대 아자살', 'wood', T['Chip'], light, dict(fam='aja')),
         ('금지: 백자 + 상감 띠', 'inlay', T['Porcelain'], light, dict(fam='sanggam_stamps'))],
    ]
    out = []
    for r, row in enumerate(rows):
        a, y0 = sh.band(TH + 62)
        for i, (name, kind, ground, cols, kw) in enumerate(row):
            bg = sample(kind, ground, cols['main'], kit=kit, **kw)
            m = under_text(bg, LINES, cols)
            x = PADX + i * (TW + 26)
            a[4:4 + TH, x:x + TW] = bg
            frame1(a, x, 4, TW, TH)
            y = 14
            for text, size, face, role in LINES:
                sh.labels.append((x + 18, y0 + 4 + y, text, size, face, cols[role]))
                y += {28: 44, 20: 36, 24: 0}[size]
            ok = m['main'] >= 7 and m['main_num'] >= 7 and m['sub'] >= 4.5
            sh.text(x, y0 + TH + 8, name, 17, 'bold', TXT if ok else WARN)
            sh.text(x, y0 + TH + 30, '무늬 %.2f:1 · 주 %.2f · 수치 %.2f · 보조 %.2f%s' % (
                m['pattern'], m['main'], m['main_num'], m['sub'], '' if ok else '  ← 바닥 미달'), 16, 'sans', SUB if ok else WARN)
            out.append(dict(name=name, row=('plain', 'cap_1.15', 'forbidden')[r], ok=bool(ok), **m))
    REP['legibility'] = out


# ---------------------------------------------------------------------------------------------------------------------
def build():
    REP.clear()
    kit = Kit()
    sh = Sheet()
    a, y0 = sh.band(128)
    sh.text(PADX, y0 + 22, '오행부 #308 UI 테마 키트 — 나전칠기 · 백자 상감 · 자개 공예 · 창호', 44, 'serif9', TXT)
    sh.text(PADX, y0 + 84, 'SPEC-UI-THEME-308 · DECISIONS D308-15 · 2026-10-04 · TEST · 키트 = Tools/Unity/Stage308_theme (아틀라스 %s… · 문살 타일 8) · sRGB 합성 시안 — 게임 캡처가 아니다 · 발광 · HDR · 블룸 0' % kit.meta['sha256'][:12], 18)
    panel_lattice(sh, kit)
    panel_najeon(sh, kit)
    panel_porcelain(sh, kit)
    panel_kit(sh, kit)
    panel_hud(sh, kit)
    panel_equip(sh, kit)
    panel_map(sh, kit)
    panel_legibility(sh, kit)
    panel_notes(sh)
    sh.band(30)
    REP['kit_atlas_sha256'] = kit.meta['sha256']
    return sh.build()


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, 'PNG', optimize=False)
    return b.getvalue()


def main():
    im = build()
    b1 = png_bytes(im)
    rep = json.dumps(REP, ensure_ascii=False, indent=1)
    if '--check' in sys.argv:
        b2 = png_bytes(build())
        same = b1 == b2 and rep == json.dumps(REP, ensure_ascii=False, indent=1)
        print('determinism:', 'same bytes' if same else 'DIFFERENT', hashlib.sha256(b1).hexdigest()[:16])
        return 0 if same else 1
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'theme308_sheet.png'), 'wb') as f:
        f.write(b1)
    im.convert('RGB').resize((im.width // 2, im.height // 2), Image.LANCZOS).save(os.path.join(OUT, 'theme308_sheet_half.jpg'), quality=90)
    REP['sheet'] = dict(file='Art/UI308/Theme/theme308_sheet.png', size=[im.width, im.height], sha256=hashlib.sha256(b1).hexdigest())
    with open(os.path.join(OUT, 'theme308_sheet.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(REP, ensure_ascii=False, indent=1) + '\n')
    print('wrote', os.path.join(OUT, 'theme308_sheet.png'), im.size, REP['sheet']['sha256'][:16])
    return 0


if __name__ == '__main__':
    sys.exit(main())
