# -*- coding: utf-8 -*-
"""hud308_theme_sheet.py - the HUD cluster BEFORE | AFTER the D308-15 theme (SPEC-HUD-LIQUID-308 x SPEC-UI-THEME-308).

Offline and deterministic (numpy + PIL). Every HUD pixel on this sheet comes from the numpy twin of UI/InkVessel308
(Tools/Art/hud308_twin.py) sampling the staged atlas: what is drawn here is what the stage ships, not a separate painting.
"before" = the same shader with both theme switches off (ThemeSpec308.Collar / Lids). It composites in sRGB (the #304 mockup
convention; the project is Linear) and it is not a game capture. Nothing under Oheangbu/Assets, the editor queue or Play is touched.

usage (repo root):
    python Tools/resource_guard.py --wait
    python Tools/Art/hud308_theme_sheet.py            the sheet + half-size jpg + json
    python Tools/Art/hud308_theme_sheet.py --check    render twice, compare the bytes
out:
    Art/UI308/HUD/hud308_theme_sheet.png   (2400 px wide, Korean captions)
    Art/UI308/HUD/hud308_theme_sheet_half.jpg
    Art/UI308/HUD/hud308_theme_sheet.json  (the picked scenes and the numbers printed on the sheet)
"""
import hashlib
import io
import json
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L        # noqa: E402
import hud308_assets as HA      # noqa: E402
import hud308_mock as HM        # noqa: E402  (sheet scaffolding, the real captures, the impact-frame look of the world)
import hud308_najeon as NJ      # noqa: E402
import hud308_twin as TW        # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Art', 'UI308', 'HUD')
CLEAN = os.path.join(ROOT, 'Art', 'UI304', 'clean')
CROP = HM.CROP                          # the standard cluster crop (design px): 284 x 262
CW, CH = CROP[2] - CROP[0], CROP[3] - CROP[1]
INK8, ASH8, PAPER8 = HM.INK8, HM.ASH8, HM.PAPER8
REP = {}
_CAP = {}
# This sheet is the D308-15 comparison (theme before | after). The key glyphs of D308-11b came later and have their own sheet
# (Tools/Art/hud308_11b_sheet.py): they are left out here so the two columns differ by the theme alone.
TW.SHOW_KEYS = False


def capture(name):
    if name not in _CAP:
        _CAP[name] = np.asarray(Image.open(os.path.join(CLEAN, name + '.png')).convert('RGB'), float) / 255.0
    return _CAP[name]


def pick_window(w, h, brightest):
    """The brightest / darkest w x h window of the two HUD-less captures (16 px grid), deterministic."""
    best = None
    for name in ('road', 'palace'):
        lum = L.lum(capture(name))
        ii = np.pad(lum.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
        ys = np.arange(0, lum.shape[0] - h + 1, 16)
        xs = np.arange(0, lum.shape[1] - w + 1, 16)
        m = (ii[ys[:, None] + h, xs[None, :] + w] - ii[ys[:, None], xs[None, :] + w] - ii[ys[:, None] + h, xs[None, :]] + ii[ys[:, None], xs[None, :]]) / (w * h)
        idx = np.unravel_index(m.argmax() if brightest else m.argmin(), m.shape)
        v = float(m[idx])
        if best is None or (v > best[0] if brightest else v < best[0]):
            best = (v, name, int(xs[idx[1]]), int(ys[idx[0]]))
    return best


def scaled(a, s):
    if abs(s - 1) < 1e-9:
        return a.copy()
    im = Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8))
    return np.asarray(im.resize((int(round(a.shape[1] * s)), int(round(a.shape[0] * s))), Image.LANCZOS), float) / 255.0


def draw(dst, region, s, themed, **kw):
    """The cluster as the stage draws it (the twin), onto dst = the picture of `region` (design px) at s screen px per px."""
    was = TW.theme(themed, themed)
    mips, TW.MIPS = TW.MIPS, True
    els = TW.cluster(scale=s, **kw)
    TW.paste(dst, [(rgba, x0 - int(round(region[0] * s)), y0 - int(round(region[1] * s)), aux) for rgba, x0, y0, aux in els])
    TW.MIPS = mips
    TW.theme(*was)
    return dst


def nearest(a, k):
    return np.repeat(np.repeat(a, k, axis=0), k, axis=1)


def img(a):
    return HM.to_img(a)


# ---- the kit's first forms (what the kit review looked at), for the three-way panels only -----------------------------------
def first_form_atlas():
    """The staged atlas with the vessel cell's G channel replaced by the kit's FIRST collar (lacquer fills the lip and the neck)."""
    a = TW.ATLAS.copy()
    vx, vy = HA.VESSEL_AT
    cell = a[vy:vy + HA.CELL_H, vx:vx + HA.CELL_W]
    ys, xs = np.mgrid[0:HA.CELL_H, 0:HA.CELL_W]
    x = (xs + .5) / HA.PX - HA.MARGIN
    y = (ys + .5) / HA.PX - HA.MARGIN
    d = (cell[..., 0] - .5) * 2 * HA.SDF_RANGE
    lac, nac = L.collar_fields(d, x, y, HA.REF_W / 2.0, HA.SHAPE['lip_h'] * HA.REF_H, HA.SHAPE['neck_h'] * HA.REF_H,
                               HA.SHAPE['neck_w'] * HA.REF_W, 1.0 / HA.PX)
    cell[..., 1] = .5 * lac + .5 * nac
    return a


def kit_lid(kind, s):
    """The kit's first lid: lacquer disc 44 + paper hairline 1.4 px + the INK pictogram scaled by .74 as one pale shell face."""
    m = NJ.kit_roundel_mask(kind, s, 4)
    n = m.shape[0]
    ys, xs = np.mgrid[0:n, 0:n]
    X, Y = (xs + .5) / s, (ys + .5) / s
    r = np.hypot(X - 22.0, Y - 22.0)
    disc = np.clip((22.0 - r) * s + .5, 0, 1)
    inner = np.clip((22.0 - 1.4 - r) * s + .5, 0, 1)
    nc = L.NACRE
    hue = nc['H_centre'] + 62.0 * np.sin(2 * np.pi * (X * .6 + Y * .8) / 64.0 + .9)
    pearl = L.oklch(nc['L'] + nc['grain_dL'] * np.sin(2 * np.pi * (X * .8 - Y * .6) / nc['grain_period']), nc['C'] * L.NACRE_FACE_CHROMA, hue)
    rgb = np.ones((n, n, 3)) * L.T['Paper']
    rgb = rgb * (1 - inner[..., None]) + L.T['Lacquer'] * inner[..., None]
    ga = (m * inner)[..., None]
    rgb = rgb * (1 - ga) + pearl * ga
    return np.dstack([rgb, np.maximum(disc * .92, inner)])


def lid_tile(kind, code, form, s, bg, wet=1.0, fill=0.0):
    """One mark on a flat ground, 52 s px square. form: before | kit | after."""
    n = int(round(52 * s))
    dst = np.ones((n, n, 3)) * bg
    rect = (4.0, 4.0, 44.0, 44.0)
    if form == 'kit':
        TW.paste(dst, [(kit_lid(kind, s), int(round(4 * s)), int(round(4 * s)), None)])
        return dst
    was = TW.theme(form == 'after', form == 'after')
    mips, TW.MIPS = TW.MIPS, True
    el = TW.element(rect, code, TW.INK, [wet, 0, 0, 0], [0, 0, 0, 0], [code, 0, 0, fill], scale=s)
    TW.paste(dst, [el])
    TW.MIPS = mips
    TW.theme(*was)
    return dst


def neck_tile(kind, form, s, bg, level, pour=0.0):
    """The top of one vessel (neck, shoulders, the surface when full), cropped. form: before | kit | after."""
    rect = TW.LAYOUT[kind]
    code, tint = (1, TW.CINNABAR) if kind == 'hp' else (0, TW.INK)
    old = TW.use_atlas(first_form_atlas()) if form == 'kit' else None
    was = TW.theme(form != 'before', False)
    mips, TW.MIPS = TW.MIPS, True
    marks = [.25, -1.0, .35 if code else 0, 0] if pour > 0 else [0, 0, .35 if code else 0, 0]
    rgba, x0, y0, aux = TW.element(rect, code, tint, [level, 0, 0, 0], marks, [code, 0, 0, pour], scale=s)
    TW.MIPS = mips
    TW.theme(*was)
    if old is not None:
        TW.use_atlas(old)
    w, h = int(round((rect[2] + 8) * s)), int(round(58 * s))
    dst = np.ones((h, w, 3)) * bg
    TW.paste(dst, [(rgba, x0 - int(round((rect[0] - 4) * s)), y0 - int(round((rect[1] - 5) * s)), aux)])
    return dst


def detail_tile(kind, form):
    """The pictogram at 1080p real size (box sampled to 1 px per design px), shown 8x with hard pixels, on lacquer."""
    hi = NJ.kit_roundel_mask(kind, 8.0, 2) if form == 'kit' else NJ.render(kind, s=8.0, ss=2, with_ring=False)[0][..., 3]
    n = hi.shape[0] // 8
    one = hi[:n * 8, :n * 8].reshape(n, 8, n, 8).mean(axis=(1, 3))
    col = L.nacre_token()
    a = L.T['Lacquer'][None, None, :] * (1 - one[..., None]) + col[None, None, :] * one[..., None]
    return nearest(a, 8)


# ---- panels ---------------------------------------------------------------------------------------------------------------
STATE = dict(hp=.62, ink=.44, hp_tilt=.03, ink_tilt=-.018, hp_wave=.8, ink_wave=.45)


def panel_band(sh):
    sh.head('(가) 실제 화면 아래쪽 띠 — 1080p 실제 크기', '배경 = HUD 없는 실제 캡처 Art/UI304/clean/road.png 의 y 760–1060. 위 = 테마 전(D308-11 그대로), 아래 = 테마 후(D308-15).\n'
            '바뀐 곳은 둘뿐이다: 알병의 목(옻칠 목 고리 + 끊음질 한 줄)과 표시 셋(나전 뚜껑). 유리 몸통 · 액체 · 수면 · 배치는 화소까지 같다.')
    region = (0, 760, 1920, 1060)
    for themed in (False, True):
        dst = capture('road')[region[1]:region[3], region[0]:region[2]].copy()
        draw(dst, region, 1.0, themed, **STATE)
        sh.put(img(dst), sh.M + 200, sh.y)
        sh.cap(sh.M, sh.y + 8, '테마 전' if not themed else '테마 후', 30, INK8, 'Black')
        sh.cap(sh.M, sh.y + 56, '먹 기호 + 한지 테\n유리 목 = 먹 선' if not themed else '나전 뚜껑(옻칠 + 자개)\n옻칠 목 고리 + 자개 줄', 19, ASH8, 'Regular')
        sh.y += 300 + 14
    sh.rule()


def panel_scene(sh, key, title):
    v, name, x, y = pick_window(CW, CH, key == 'bright')
    REP.setdefault('scenes', {})[key] = dict(capture='Art/UI304/clean/%s.png' % name, x=x, y=y, w=CW, h=CH, mean_luminance=round(v, 4))
    sh.head(title, '배경 = 두 캡처(road · palace)에서 평균 휘도가 가장 %s %d×%d 창: %s.png (%d, %d), 평균 휘도 %.3f. 체력 62 %% · 먹 44 %% · 표시 셋 젖음.\n'
            '왼쪽 둘 = 1×(1080p의 실제 화소), 오른쪽 둘 = 2×(같은 것을 화소 2배로 다시 그린 것: 4K 또는 UI 크기 2배). 각 쌍의 왼쪽이 테마 전, 오른쪽이 테마 후.'
            % ('높은' if key == 'bright' else '낮은', CW, CH, name, x, y, v))
    bg = capture(name)[y:y + CH, x:x + CW]
    x0 = sh.M
    y0 = sh.y + 34
    for s in (1.0, 2.0):
        for themed in (False, True):
            dst = draw(scaled(bg, s), CROP, s, themed, **STATE)
            sh.put(img(dst), x0, y0)
            sh.cap(x0, y0 - 30, '%d×  %s' % (s, '테마 전' if not themed else '테마 후'), 22, INK8, 'Bold')
            x0 += dst.shape[1] + 16
        x0 += 14
    # numbers on this very ground
    bl = float(L.lum(bg).mean())
    lac, nac, paper = float(L.lum(L.T['Lacquer'])), float(L.lum(L.nacre_token())), float(L.lum(L.T['Paper']))
    c = lambda a, b: (max(a, b) + .05) / (min(a, b) + .05)
    rows = ['이 배경(평균 휘도 %.3f) 위에서' % bl,
            '  옻칠 / 배경  %.2f:1' % c(lac, bl),
            '  자개 / 배경  %.2f:1  (자개는 늘 옻칠 위)' % c(nac, bl),
            '  자개 / 옻칠  %.2f:1' % c(nac, lac),
            '  한지 테 / 배경  %.2f:1' % c(paper, bl),
            '', '밝은 장면: 옻칠이 꼴을 준다.' if key == 'bright' else '어두운 장면: 옻칠은 묻히고',
            '뚜껑은 검은 원판 + 진주빛 조각.' if key == 'bright' else '자개 테와 조각, 한지 테가 남는다.']
    REP['scenes'][key].update(lacquer_vs_ground=round(c(lac, bl), 2), nacre_vs_ground=round(c(nac, bl), 2), paper_rim_vs_ground=round(c(paper, bl), 2))
    sh.cap(x0 + 8, y0 + 6, '\n'.join(rows), 21, INK8, 'Regular')
    sh.y = y0 + CH * 2 + 18
    sh.rule()


def panel_impact(sh):
    pt = HM.IMPACT_PT
    region = (48, 500, 1068, 1030)          # the cluster and the impact point in one picture
    s = 1.0
    sh.head('(라) 임팩트 프레임 반응 (D308-10b) — 테마 후, 1080p 실제 크기',
            '빛의 방향은 셰이더가 화소마다 라이브 전개 층의 점(_OhImpactHudPoint308, ImpactHud308.hlsl)에서 구한다 — UI/InkMeter · UI/InkReveal과 같은 점 · 같은 틀. 주사 고리 = 임팩트 점(도해 표시).\n'
            '왼쪽 = 평소, 가운데 = 1장(반전), 오른쪽 = 2장(획 실루엣). 빛 쪽: 유리 선과 뚜껑 가장자리가 한지값, 반대쪽: 한지 테 · 자개가 먹 쪽으로 눌리고 먹 그림자. 자개는 값을 올리지 않는다(최대 채널 %s).'
            % REP['numbers']['ldr']['cluster_max_channel_255'])
    x0 = sh.M
    w = int((sh.W - 2 * sh.M - 2 * 16) / 3)
    crop_w = w
    reg = (region[0], region[1], region[0] + crop_w, region[3])
    for mode, label in (('normal', '평소 (임팩트 없음)'), ('f1', '1장 · 반전 + HUD 반응'), ('f2', '2장 · 획 실루엣 + HUD 반응')):
        world = HM.world_variant(mode, pt, 'road')
        dst = world[reg[1]:reg[3], reg[0]:reg[2]].copy()
        kw = dict(STATE)
        if mode != 'normal':
            kw.update(impact=(pt, 1.0), hp_tilt=-.2, ink_tilt=-.12, hp_wave=2.0, ink_wave=1.2)
        draw(dst, reg, s, True, **kw)
        im = img(dst)
        if mode != 'normal' and reg[0] <= pt[0] < reg[2]:
            from PIL import ImageDraw
            d = ImageDraw.Draw(im)
            cx, cy = pt[0] - reg[0], pt[1] - reg[1]
            d.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], outline=HM.CIN8, width=3)
        sh.put(im, x0, sh.y + 34)
        sh.cap(x0, sh.y + 4, label, 22, INK8, 'Bold')
        x0 += w + 16
    sh.y += 34 + (region[3] - region[1]) + 16
    # the same three frames at 2x, cluster only, before | after
    sh.cap(sh.M, sh.y, '같은 세 장, 군집만 2× — 윗줄 테마 전 · 아랫줄 테마 후 (임팩트 점은 오른쪽 위 %d, %d)' % pt, 22, INK8, 'Bold')
    sh.y += 36
    for themed in (False, True):
        x0 = sh.M
        for mode in ('normal', 'f1', 'f2'):
            world = HM.world_variant(mode, pt, 'road')
            bg = world[CROP[1]:CROP[3], CROP[0]:CROP[2]]
            kw = dict(STATE)
            if mode != 'normal':
                kw.update(impact=(pt, 1.0), hp_tilt=-.2, ink_tilt=-.12, hp_wave=2.0, ink_wave=1.2)
            dst = draw(scaled(bg, 2.0), CROP, 2.0, themed, **kw)
            sh.put(img(dst), x0, sh.y)
            x0 += dst.shape[1] + 16
        # one impact, three sides: the light follows the point for every element alike (after only: the right column)
        for k, (p2, lab) in enumerate((((-300, 300), '점 = 왼쪽 위'), ((190, 1500), '점 = 아래'))):
            bg = HM.world_variant('f2', pt, 'road')[CROP[1]:CROP[3], CROP[0]:CROP[2]]
            kw = dict(STATE)
            kw.update(impact=(p2, 1.0))
            dst = draw(scaled(bg, 1.0), CROP, 1.0, themed, **kw)
            sh.put(img(dst), x0, sh.y + k * CH)
            sh.dr.rectangle([x0, sh.y + k * CH, x0 + 118, sh.y + k * CH + 26], fill=PAPER8)
            sh.cap(x0 + 6, sh.y + k * CH + 2, lab, 17, INK8, 'Bold')
        sh.cap(x0 + CW + 12, sh.y + 6, '테마 전' if not themed else '테마 후', 28, INK8, 'Black')
        sh.cap(x0 + CW + 12, sh.y + 50, '오른쪽 두 칸(1×) =\n같은 2장에서 임팩트 점만\n옮긴 것: 다섯 요소가\n모두 같은 쪽에서 빛을\n받는다.', 18, ASH8, 'Regular')
        sh.y += CH * 2 + 14
    sh.rule()


def panel_collar(sh):
    n = REP['numbers']
    th, sep = n['pour_thread'], n['collar_vs_ink_full_vessel_1080p']['dark_2A2A26']
    sh.head('(마) 목테 — 약점 2(마개로 읽힌다) · 약점 3(먹 용기의 목테와 먹이 한 덩어리)',
            '세 꼴을 같은 셰이더로 그렸다: 테마 전(유리 목) | 키트 첫 꼴(옻칠이 입술과 목을 다 채움 = 마개) | 이번 꼴(목 고리: 입술과 목 위 1/3은 유리, 자개 줄이 고리의 아랫단).\n'
            '윗줄 = 먹 용기 가득(100 %), 검은 솔숲 #2A2A26 · 아랫줄 = 붓는 중(실), 밝은 하늘 #E4E0D6. 각 칸 = 4×, 그 오른쪽 작은 칸 = 1×(1080p 실제 화소).')
    forms = (('before', '테마 전'), ('kit', '키트 첫 꼴(마개형)'), ('after', '이번: 목 고리'))
    for row, (bg, level, pour, kind, lab) in enumerate(((L.PINE, 1.0, 0.0, 'ink', '먹 용기 100 %'), (L.SKY, .36, 1.0, 'hp', '체력 용기 · 붓는 중'))):
        x0 = sh.M
        for form, name in forms:
            big = neck_tile(kind, form, 4.0, bg, level, pour)
            one = neck_tile(kind, form, 1.0, bg, level, pour)
            sh.put(img(big), x0, sh.y + 30)
            sh.put(img(one), x0 + big.shape[1] + 10, sh.y + 30)
            sh.cap(x0, sh.y, '%s — %s' % (name, lab), 21, INK8, 'Bold')
            x0 += big.shape[1] + one.shape[1] + 10 + 26
        hh = big.shape[0]
        if row == 0:
            sh.cap(x0, sh.y + 30, '약점 3 [M, 1080p · 검은 솔숲]\n옻칠 / 먹  %.2f:1 (한 값)\n자개 줄 / 옻칠  %.2f:1\n자개 줄 / 먹  %.2f:1\n→ 고리와 먹 사이에 늘\n   진주빛 선이 선다.' % (
                sep['lacquer_vs_ink'], sep['shell_line_vs_lacquer'], sep['shell_line_vs_ink']), 20, INK8, 'Regular')
        else:
            sh.cap(x0, sh.y + 30, '약점 2 [M]\n실이 고리 위에서 보이는 길이\n  체력 %.1f px · 먹 %.1f px\n고리 뒤로 가리는 길이\n  체력 %.1f px · 먹 %.1f px\n키트 첫 꼴은 %.1f px · %.1f px를\n가렸다(입술부터 목 끝까지).' % (
                th['hp']['shows_above_band_px'], th['ink']['shows_above_band_px'], th['hp']['hidden_behind_band_px'], th['ink']['hidden_behind_band_px'],
                th['first_form_hidden_px']['hp'], th['first_form_hidden_px']['ink']), 20, INK8, 'Regular')
        sh.y += 30 + hh + 16
    lr = n['level_reading']
    sh.cap(sh.M, sh.y, '값 읽기는 그대로다 [M]: 목 고리를 켠 그림과 끈 그림은 고리 아래에서 화소 차 %s, 액체 · 수면선 덮임 차 %s. 고리 아래 끝은 가득 선보다 체력 %.2f px · 먹 %.2f px 위. '
           'AC-H3.1 최악 %.2f px(테마 전과 같은 값, 표의 일곱 행 모두 동일).' % (lr['rgba_max_diff_below_the_band'], lr['body_and_surface_coverage_max_diff'],
                                                                   lr['band_clear_of_full_line_px']['hp'], lr['band_clear_of_full_line_px']['ink'], lr['H3_1_worst_px']), 20, INK8, 'Regular')
    sh.y += 40
    sh.rule()


def panel_lids(sh):
    n = REP['numbers']
    sh.head('(바) 표시 셋 — 약점 4("검은 단추 + 옅은 하늘빛 아이콘", 자동차 기호의 세부)',
            '줄마다 한 기호. 왼쪽부터: 테마 전(먹 기호 + 한지 테) | 키트 첫 꼴(옻칠 원판 + 한지 실테 + 먹 기호를 .74배로 줄인 자개 한 조각) | 이번(나전 뚜껑: 끊음질 테 12조각 + 자개 조각으로 다시 그린 기호).\n'
            '각 꼴 = 4× 밝은 하늘 · 4× 검은 솔숲 · 1× 둘. 이번 꼴은 조각마다 색 계열(녹 · 청 · 드물게 분홍)이 다르고 이음은 옻칠 머리카락 선이다. 한지 실테는 없다(자개 테가 그 일을 한다).')
    codes = dict(dodge=2, jump=3, vehicle=4, vehicle_out=5)
    names = dict(dodge='회피', jump='점프', vehicle='자동차(부를 수 있음)', vehicle_out='자동차(나와 있음 = 회수)')
    top = sh.y
    for kind in ('dodge', 'jump', 'vehicle', 'vehicle_out'):
        x0 = sh.M + 250
        sh.cap(sh.M, sh.y + 80, names[kind], 24, INK8, 'Bold')
        for form in ('before', 'kit', 'after'):
            for bg in (L.SKY, L.PINE):
                t = lid_tile(kind, codes[kind], form, 4.0, bg)
                sh.put(img(t), x0, sh.y)
                x0 += t.shape[1] + 6
            for k, bg in enumerate((L.SKY, L.PINE)):
                t = lid_tile(kind, codes[kind], form, 1.0, bg)
                sh.put(img(t), x0, sh.y + k * (t.shape[0] + 6))
            x0 += 52 + 34
        sh.y += 208 + 12
    # states
    sh.cap(sh.M, sh.y + 8, '이번 꼴의 세 상태 4× (자동차): 박혀 있다 = 쓸 수 있음 · 다시 박히는 중 55 %% · 빠진 자리 = 지금 못 씀(자개 α .30 = %.2f:1, 테는 그대로) · 숨김 = 그리지 않음'
           % n['lids']['vehicle']['dry_seat_vs_lacquer'], 21, INK8, 'Bold')
    sh.y += 44
    x0 = sh.M
    for bg in (L.SKY, L.PINE):
        for wet, fill in ((1.0, 0.0), (0.0, .55), (0.0, 0.0)):
            t = lid_tile('vehicle', 4, 'after', 4.0, bg, wet, fill)
            sh.put(img(t), x0, sh.y)
            x0 += t.shape[1] + 6
        x0 += 20
    # detail at 1080p: the vehicle pictogram box-sampled to the real pixel grid, shown 8x with hard pixels (right of the table)
    d = n.get('vehicle_detail_1080p')
    yy = top
    for form, lab in (('kit', '키트 첫 꼴 · 자동차 기호의 1080p 화소(8배)'), ('najeon', '이번 꼴 · 자동차 기호의 1080p 화소(8배)')):
        t = detail_tile('vehicle', form)
        sh.put(img(t), 1960, yy)
        key = 'kit_first_form_ink_pictogram_at_074' if form == 'kit' else 'najeon_lid'
        txt = lab + ('\n옻칠 칸(창 · 바퀴살 사이) %d개 중 %d개가 남는다' % (d[key]['counters'], d[key]['counters_1080p']) if d else '')
        yy = sh.cap(1960, yy + t.shape[0] + 8, txt, 19, INK8, 'Bold') + 16
    sh.cap(1960, yy, '기준: 화소가 50 % 이상 자개이면 자개.\n16가지 화소 위상 중 가장 나쁜 것.', 18, ASH8, 'Regular')
    sh.y += 208 + 20
    sh.rule()


def panel_numbers(sh):
    n = REP['numbers']
    a, ldr, imp = n['area'], n['ldr'], n['impact_one_side']
    lids = n['lids']
    lines = [
        '발광 0 · HDR 0 · 시간 항 0: 군집 최대 채널 %s / 255 (한지 230), 뚜껑 자개 %s, 목 줄 자개 식 %s. 자개 빛 흐름 끔(구운 정지 색 + 정지 식).' % (
            ldr['cluster_max_channel_255'], ldr['lid_shell_max_channel_255'], ldr['collar_formula_max_channel_255']),
        '자개 넓이: 군집 %.0f px² = 화면의 %.3f %% (미니맵 틀 1,558 px² 더하면 %.3f %%, 상한 .20 %%). 옻칠 %.0f px². HUD의 꼴 무늬 0. HUD 항목 수는 그대로(화이트리스트).' % (
            a['nacre_px2_cluster'], a['nacre_pct_cluster'], a['nacre_pct_with_minimap_frame'], a['lacquer_px2_cluster']),
        '뚜껑: 자개 / 옻칠 %.2f–%.2f:1, 자개 테 / 검은 솔숲 %.2f–%.2f:1, 옻칠 / 밝은 하늘 %.2f:1, 빠진 자리 %.2f:1.' % (
            min(v['shell_vs_lacquer'] for v in lids.values()), max(v['shell_vs_lacquer'] for v in lids.values()),
            min(v['rim_line_vs_pine'] for v in lids.values()), max(v['rim_line_vs_pine'] for v in lids.values()),
            lids['vehicle']['lacquer_vs_sky'], lids['vehicle']['dry_seat_vs_lacquer']),
        '임팩트: 요소마다 빛 방향과 임팩트 점 방향의 최대 차 %.3f°, 행이 위에서 아래로 가는 타깃과 그 반대 타깃의 차 %s px. 이 스테이지에서 임팩트 전역을 쓰는 코드 0.' % (
            imp['direction_error_deg_max'], imp['rows_up_vs_rows_down_max_diff_px']),
        '되돌리기: HudLiquid308 자료의 Theme.Collar / Theme.Lids 두 칸(재질 값은 hud308-setup이 다시 넣는다). 둘 다 끄면 이 시트의 "테마 전"과 같다.',
        '이 시트는 셰이더의 numpy 쌍둥이를 sRGB로 합성한 것이다(게임 캡처가 아니다). 셰이더는 한 번도 컴파일되지 않았다. 아틀라스 sha256 %s…' % TW.AJ['atlas']['sha256'][:16],
    ]
    sh.head('(사) 잰 값 [M] — Art/UI308/HUD/twin_report.json 의 theme 묶음')
    for line in lines:
        sh.cap(sh.M + 28, sh.y, line, 21, INK8, 'Regular')
        sh.y += 34


def build():
    path = os.path.join(OUT, 'twin_report.json')
    with open(path, encoding='utf-8') as f:
        rep = json.load(f)
    if 'theme' not in rep or rep.get('atlas_sha256') != TW.AJ['atlas']['sha256']:
        raise SystemExit('run "python Tools/Art/hud308_twin.py --report" first: the sheet prints its numbers')
    REP.clear()
    REP['numbers'] = rep['theme']
    sh = HM.Sheet()
    sh.title('오행부 #308 HUD — 테마 전 | 테마 후  (D308-15: 나전칠기 · 자개 — 옻칠 목 고리 + 나전 뚜껑)',
             ['SPEC-HUD-LIQUID-308 × SPEC-UI-THEME-308 · 스테이지 Tools/Unity/Stage308_hud · IMPLEMENTED(오프라인) — VALIDATED 아님: Assets · 편집기 큐 · Play 0',
              '모든 HUD 화소 = UI/InkVessel308의 numpy 쌍둥이가 스테이지 아틀라스를 읽어 그린 것. "테마 전" = 같은 셰이더에서 테마 두 칸을 끈 것. 질문의 기본 답: 1 옻칠 우세 · 2 옻칠 단추 · 5 빛 흐름 끔 · 6 철사 상감 안 씀.'])
    panel_band(sh)
    panel_scene(sh, 'bright', '(나) 가장 밝은 장면 — 테마 전 | 테마 후, 1× · 2×')
    panel_scene(sh, 'dark', '(다) 가장 어두운 장면 — 테마 전 | 테마 후, 1× · 2×')
    panel_impact(sh)
    panel_collar(sh)
    panel_lids(sh)
    panel_numbers(sh)
    return sh.im.crop((0, 0, sh.W, sh.y + 24))


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, format='PNG', optimize=False, compress_level=6)
    return b.getvalue()


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    im = build()
    data = png_bytes(im)
    if '--check' in sys.argv:
        again = png_bytes(build())
        print('determinism: ' + ('same bytes' if again == data else 'DIFFERENT') + ' ' + hashlib.sha256(data).hexdigest()[:16])
        sys.exit(0 if again == data else 1)
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'hud308_theme_sheet.png'), 'wb') as f:
        f.write(data)
    im.resize((im.width // 2, im.height // 2), Image.LANCZOS).convert('RGB').save(os.path.join(OUT, 'hud308_theme_sheet_half.jpg'), quality=90)
    out = dict(spec='SPEC-HUD-LIQUID-308 x SPEC-UI-THEME-308', generator='Tools/Art/hud308_theme_sheet.py', size=list(im.size),
               sha256=hashlib.sha256(data).hexdigest(), atlas_sha256=TW.AJ['atlas']['sha256'],
               note='numpy twin of UI/InkVessel308 on real HUD-less captures, sRGB composite; before = theme switches off',
               scenes=REP.get('scenes'), numbers=REP['numbers'])
    with open(os.path.join(OUT, 'hud308_theme_sheet.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(json.dumps(dict(sheet=os.path.join(OUT, 'hud308_theme_sheet.png'), size=out['size'], sha256=out['sha256'][:16], scenes=out['scenes']),
                     ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
