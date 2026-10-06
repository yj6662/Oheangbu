# -*- coding: utf-8 -*-
"""#308 장비·소지품 화면 x UI 테마 (SPEC-UI-EQUIPMENT-308 x SPEC-UI-THEME-308, DECISIONS D308-15): the themed concept sheet
of the surface's owner, and the numbers the two Specs ask for. Offline: Pillow + numpy, deterministic, no editor.

  python Tools/resource_guard.py --wait
  python Tools/Art/equip308_theme.py            -> Art/UI308/Equipment/equip308_theme_sheet.png (+ _half.jpg),
                                                   equip308_theme_<view>.png x 7, equip308_theme.json
  python Tools/Art/equip308_theme.py --check    renders twice and compares the bytes, re-measures; exit 1 when the sheet is
                                                   not deterministic, differs from the disk, or a measured floor fails

The screens come from Tools/Art/equip308_mock.py (render(view, theme=...)), which reads the kit by slot name through
Tools/Art/theme308_kit.py. Nothing here is a game capture: sRGB compositing (Unity blends in linear light).
Measured (json):
  focus    cinnabar ring against every ground pixel within 2 px of it, four visions (normal + Machado 2009 protan / deutan /
           tritan), for the ring over the bars, over a veil-coloured bed and over the 한지 bed (the pick)
  text     every text class against the worst pixel under its ink box, on the worst world (the brightest sky under the veil)
  nacre    shell area of each screen in px2 and % of 1920 x 1080 (limit .40 %)
  clear    distance from every themed rectangle to every text box (structure >= 8 px, ornament / inlay >= 16 px)
"""
import hashlib
import io
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import equip308_mock as EM      # noqa: E402
import theme308_lib as L        # noqa: E402  (colour maths only: contrast, colour-vision simulation)

OUT = EM.OUT
W, H = EM.W, EM.H
SKY = tuple(int(round(v * 255)) for v in L.SKY)
VISIONS = ('normal', 'protan', 'deutan', 'tritan')
WL = np.array([.2126, .7152, .0722])
PICK = {}                                                  # the default answers = EM.THEME
VIEWS = (('a_stone', 'stone', True), ('b_empty', 'empty', True), ('c_fragment', 'fragment', True), ('f_candidate', 'candidate', True),
         ('g_offshelter', 'stone_off', False), ('h_gear', 'gear', True), ('i_virtue', 'virtue', True))
NAMES = {EM.PAPER: '한지', EM.MIST: '안개', EM.INK: '먹', EM.OFF: '비활성', EM.CINL: '주사 밝음', EM.ASH: '재', EM.CIN: '주사'}
FLOOR = {'한지': 7.0, '먹': 7.0, '흑상감': 7.0, '안개': 4.5, '재': 4.5, '주사 밝음': 4.5}      # 비활성 = no floor (disabled + reason)


def lum(rgb01, vision='normal'):
    lin = L.srgb_to_lin(np.asarray(rgb01, float))
    if vision != 'normal':
        lin = np.clip(lin @ L.CVD[vision].T, 0, 1)
    return lin @ WL


def ratio(a, b):
    return (np.maximum(a, b) + .05) / (np.minimum(a, b) + .05)


def rgb01(im):
    return np.asarray(im.convert('RGB'), float) / 255.0


# ---------------------------------------------------------------------------------------------------------------- focus
def ring_alpha(rings):
    r = EM.Screen()
    r.im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    for q in rings:
        r.cell_focus(*q)
    return np.asarray(r.im)[..., 3] / 255.0


def near(mask, radius=2):
    out = np.zeros_like(mask)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            out |= np.roll(np.roll(mask, dy, 0), dx, 1)
    return out


def ring_contrast(ground, rings):
    """cinnabar against every ground pixel within 2 px of the ring's ink (alpha >= .5): min and 5th percentile per vision"""
    a = ring_alpha(rings)
    ink = a >= .5
    edge = near(ink) & (a < .05)
    g = ground[edge]
    cin = np.array(EM.CIN) / 255.0
    out = {}
    for v in VISIONS:
        c = ratio(lum(cin, v), lum(g, v))
        out[v] = (float(c.min()), float(np.percentile(c, 5)))
    return out


def grid_screen(theme, focus=None, kept=None, noring=False, world=None):
    EM.TH = dict(EM.THEME, **theme) if theme is not None else None
    EM.FLAT_WORLD = world
    try:
        s = EM.Screen()
        s.noring = noring
        EM.frame_page(s, True, False)
        EM.draw_grid(s, focus, kept)
    finally:
        EM.TH, EM.FLAT_WORLD = None, None
    return s


FOCUS_VARIANTS = (
    ('before', None, '테마 전 (#304): 찢긴 칩, 살대 없음. 테가 칩과 장막에 걸친다'),
    ('table', dict(grid='table', focus='none'), '키트 검토의 시안: 테가 살대 위를 그대로 지나간다'),
    ('sash_none', dict(grid='sash', focus='none'), '교창 격자, 받침 없음: 테가 살대와 울거미 위를 지나간다'),
    ('sash_veil', dict(grid='sash', focus='veil'), '교창 격자 + 장막색 받침: 살대는 가려지지만 테가 어둠 위에 선다'),
    ('sash_swell', dict(grid='sash', focus='swell'), '교창 격자 + 한지 받침(선택): 살대를 한지가 덮고 테가 그 위에 선다'),
)


def measure_focus():
    """min over the 16 칸 (each focused in turn) of the ring-to-ground contrast, on the worst world"""
    cells = [('gear', 0), ('gear', 5)] + [('gear', 1 + i) for i in range(4)] + [('stone', i) for i in range(5)] + [('virtue', i) for i in range(5)]
    res = {}
    for key, theme, _ in FOCUS_VARIANTS:
        worst = {v: [99.0, 99.0, None] for v in VISIONS}
        for c in cells:
            s = grid_screen(theme, focus=c, noring=True, world=SKY)
            m = ring_contrast(rgb01(s.im), s.rings)
            for v in VISIONS:
                if m[v][0] < worst[v][0]:
                    worst[v] = [m[v][0], m[v][1], '%s %d' % c]
        res[key] = {v: dict(min=round(worst[v][0], 2), p05=round(worst[v][1], 2), cell=worst[v][2]) for v in VISIONS}
    # the two other places a ring appears on the themed page: a 바꿔 낄 것 row and a 석경 chip
    EM.FLAT_WORLD = SKY
    try:
        for key, view in (('candidate_row', 'candidate'), ('fragment_chip', 'fragment')):
            s = EM.render(view, True, PICK, measure=True, noring=True)
            m = ring_contrast(rgb01(s.im), s.rings)
            res[key] = {v: dict(min=round(m[v][0], 2), p05=round(m[v][1], 2)) for v in VISIONS}
    finally:
        EM.FLAT_WORLD = None
    return res


def measure_weight():
    """how much mark each state puts on the screen (a 104 px 칸): the focus = 한지 bed + cinnabar ink, the kept = shell + moat"""
    b = EM.BED[104]
    ring = ring_alpha([(b['ring'][0] + 300, 300, 104 + b['ring'][1], 104)])
    fr = EM.kit().rect('frame_select', 114, 114)
    bed = (104 + 2 * b['x']) * (104 + 2 * b['y']) - (104 - 2 * b['inn']) ** 2
    return dict(bed_px2=int(bed), ring_px2=int(round(float(ring.sum()))), nacre_px2=int(round(float((fr[..., 3] * (fr[..., :3] @ WL > .45)).sum()))),
                moat_px2=int((104 + 2 * EM.MOAT) ** 2 - 104 ** 2))


def measure_kept():
    """the kept (nacre) frame: every shell pixel against the lacquer moat, and the token pair selected / focus"""
    fr = EM.kit().rect('frame_select', 114, 114)
    shell = (fr[..., 3] > .9) & (fr[..., :3] @ WL > .45)
    lac = np.array(EM.tcol('Lacquer')) / 255.0
    out = {'nacre_px2': round(float((fr[..., 3] * (fr[..., :3] @ WL > .45)).sum()), 1)}
    for v in VISIONS:
        c = ratio(lum(fr[shell][:, :3], v), lum(lac, v))
        out[v] = round(float(c.min()), 2)
    out['shell_vs_cinnabar'] = {v: round(float(ratio(lum(fr[shell][:, :3], v).mean(), lum(np.array(EM.CIN) / 255.0, v))), 2) for v in VISIONS}
    return out


# ---------------------------------------------------------------------------------------------------------------- text
def colour_name(c):
    if c in NAMES:
        return NAMES[c]
    return '흑상감' if c == EM.tcol('InlayDark') else '#%02X%02X%02X' % c


def measure_text(theme):
    """every text class against the worst pixel under its ink box, over the seven screens, on the worst world.
    Named roles = text; unnamed sizes = picture glyphs (the 한자 inside a stamp / seal, the rubbing) -> listed, no floor."""
    classes, pattern = {}, 1.0
    EM.FLAT_WORLD = SKY
    try:
        for name, view, shop in VIEWS:
            s = EM.render(view, shop, theme, measure=True)
            g = rgb01(EM.render(view, shop, theme, measure=True, notext=True).im)
            for t in s.texts:
                x, y, w, h = t['ink']
                box = g[max(0, y):y + h, max(0, x):x + w]
                if box.size == 0:
                    continue
                col = np.array(t['color']) / 255.0
                under = box.reshape(-1, 3)
                if t['op'] < 1.0:
                    col = col * t['op'] + under * (1 - t['op'])
                lg = lum(under)
                c = ratio(lum(col), lg)
                k = int(np.argmin(c))
                cn = colour_name(t['color'])
                named = t['role'] in EM.ROLE and not t['rub']
                key = '%s | %s' % (t['role'], cn)
                spread = float(ratio(lg.max(), lg.min()))
                row = classes.setdefault(key, dict(role=t['role'], colour=cn, kind='text' if named else 'picture glyph', n=0, worst=99.0))
                row['n'] += 1
                if float(c[k]) < row['worst']:
                    row.update(worst=round(float(c[k]), 2), text=t['text'], screen=name, ground='#%02X%02X%02X' % tuple(int(round(v * 255)) for v in under[k]),
                               ground_spread=round(spread, 3))
                if named:
                    pattern = max(pattern, spread)
    finally:
        EM.FLAT_WORLD = None
    for row in classes.values():
        fl = FLOOR.get(row['colour']) if row['kind'] == 'text' else None
        row['floor'] = fl
        row['ok'] = True if fl is None else row['worst'] >= fl
    return dict(classes=sorted(classes.values(), key=lambda r: (r['kind'] != 'text', r['worst'])), max_ground_spread_under_text=round(pattern, 3))


# ---------------------------------------------------------------------------------------------------------------- distances / nacre
NEED = dict(structure=8.0, ornament=16.0, inlay=16.0, bed=2.0)      # px from a text box; board / plaque = surfaces, state / symbol / rail = listed


def gap(a, b):
    dx = max(b[0] - (a[0] + a[2]), a[0] - (b[0] + b[2]), 0.0)
    dy = max(b[1] - (a[1] + a[3]), a[1] - (b[1] + b[3]), 0.0)
    return float(np.hypot(dx, dy))


def measure_clear(theme):
    out, area = {}, {}
    for name, view, shop in VIEWS:
        s = EM.render(view, shop, theme, measure=True)
        area[name] = round(s.nacre, 1)
        for cls, x, y, w, h in s.marks:
            for t in s.texts:
                if t['role'] not in EM.ROLE or t['rub']:
                    continue                                        # picture glyphs belong to their plaque / pane
                if cls in ('plaque', 'board') or (cls == 'inlay' and t['color'] == EM.tcol('InlayDark')):
                    continue
                d = gap((x, y, w, h), t['line'])
                row = out.setdefault(cls, dict(min=1e9))
                if d < row['min']:
                    row.update(min=round(d, 1), text=t['text'], screen=name, rect=[round(float(v), 1) for v in (x, y, w, h)])
    for cls, row in out.items():
        row['need'] = NEED.get(cls)
        row['ok'] = True if row['need'] is None else row['min'] >= row['need']
    return out, area


# ---------------------------------------------------------------------------------------------------------------- sheet
BG, TXT, SUB, RED = (0x1B, 0x1B, 0x1A), EM.PAPER, EM.MIST, EM.CINL
M, G = 70, 70


class Sheet:
    def __init__(self, width):
        self.w, self.items, self.y = width, [], M

    def title(self, tag, text, lines=()):
        self.items.append(('title', self.y, tag, text, lines))
        self.y += 62 + 36 * len(lines) + 14

    def row(self, pics, gapx=G):
        """[(image, caption or None)] left to right; returns the x of each"""
        x, top, hmax = M, self.y, 0
        xs = []
        for im, cap in pics:
            self.items.append(('pic', top + (34 if cap else 0), x, im))
            if cap:
                self.items.append(('cap', top, x, cap))
            xs.append(x)
            x += im.width + gapx
            hmax = max(hmax, im.height + (34 if cap else 0))
        self.y = top + hmax + 44
        return xs

    def table(self, x, y, rows, widths, size=24, bold_first=True):
        self.items.append(('table', y, x, rows, widths, size, bold_first))
        return y + len(rows) * (size + 14)

    def build(self):
        im = Image.new('RGB', (self.w, self.y + M), BG)
        d = ImageDraw.Draw(im)
        for it in self.items:
            if it[0] == 'title':
                _, y, tag, text, lines = it
                d.text((M, y), tag, font=EM.font('S9', 44), fill=RED)
                d.text((M + (90 if tag else 0), y + 4), text, font=EM.font('S8', 38), fill=TXT)
                for k, ln in enumerate(lines):
                    d.text((M + (90 if tag else 0), y + 60 + k * 36), ln, font=EM.font('N4', 25), fill=SUB)
            elif it[0] == 'pic':
                _, y, x, pic = it
                im.paste(pic, (x, y))
                d.rectangle([x - 1, y - 1, x + pic.width, y + pic.height], outline=(0x3A, 0x39, 0x36))
            elif it[0] == 'cap':
                _, y, x, cap = it
                d.text((x, y), cap, font=EM.font('N7', 22), fill=TXT)
            else:
                _, y, x, rows, widths, size, bold_first = it
                for r, row in enumerate(rows):
                    cx = x
                    for c, cell in enumerate(row):
                        txt, col = (cell if isinstance(cell, tuple) else (cell, TXT if r == 0 or (c == 0 and bold_first) else SUB))
                        d.text((cx, y + r * (size + 14)), str(txt), font=EM.font('N7' if r == 0 else 'N4', size), fill=col)
                        cx += widths[c]
                    if r == 0:
                        d.line([x, y + size + 9, x + sum(widths), y + size + 9], fill=(0x4A, 0x48, 0x44))
        return im


def up(im, f=2):
    return im.resize((im.width * f, im.height * f), Image.NEAREST)


def fmt(v, floor=None, p05=None):
    s = '%.2f' % v
    if p05 is not None and p05 - v > .05:      # an uneven ground: the worst pixel and the 5th percentile
        s += ' (%.2f)' % p05
    return (s, RED) if floor is not None and v < floor else (s, TXT)


def build(numbers):
    shot = lambda view, shop=True, theme=PICK: EM.render(view, shop, theme)
    before_a, after_a = EM.render('stone'), shot('stone')
    before_f, after_f = EM.render('candidate'), shot('candidate')
    after_b, after_c, after_i = shot('empty'), shot('fragment'), shot('virtue')
    phase2_h = shot('gear', theme=dict(phase2=True))
    sh = Sheet(M * 2 + W * 2 + G)
    sh.title('', '오행부 #308  장비·소지품 화면 × UI 테마 (나전칠기 · 백자 상감 · 자개 · 창호)', (
        'SPEC-UI-EQUIPMENT-308 × SPEC-UI-THEME-308 (TEST) · D308-12 · D308-15 · 2026-10-04 · 모든 화면 1920×1080, sRGB 합성 시안(게임 캡처 아님). 수치와 문구는 테마 전 시안과 같다.',
        '테마 질문 여섯의 기본값으로 그렸다: ① 옻칠 우세(먹장막을 옻칠로 읽는다) ③ 칸 격자 = 정자살 ⑤ 자개 빛 흐름 끔(구운 정지 색) ⑥ 철사 상감 안 씀. 레일 선 · 백자 건반은 2단계(맨 아래 줄).'))
    sh.title('(1)', '전체 화면: 테마 전(왼쪽) → 테마(오른쪽). 오행 마석 칸 초점, 쉼터 곁', (
        '바뀐 것: 칸 격자 = 줄마다 한 짝의 교창(울거미 6 px · 살 4 px, 창호지가 살에 끼워져 있다) · 열 가름 = 살대 3 px · 그림 틀 = 백자 판 + 이중 상감선 + 굽의 연판 띠 · 강화 단계 = 자개 꽃잎',
        '초점 = 살대를 덮는 한지 받침 위의 주사 붓 테 + 방점(붓의 일은 그대로). 꼴 무늬는 화면에 둘뿐이다: 첫 줄 창의 옻칠 궁판에 자개 국화 하나(글 없는 자리), 백자 판 굽의 연판 띠 한 줄.'))
    sh.row([(before_a, None), (after_a, None)])
    sh.title('(2)', '테마: 후보 줄 초점(왼쪽) · 빈 칸 초점(오른쪽)', (
        '왼쪽: 초점이 바꿔 낄 것으로 가면 붓 칸에는 자개 끊음질 테(Frame.Select)가 옻칠 띠 안에 남는다. 초점 줄의 칩도 같은 한지 받침 위에 선다.',
        '오른쪽: 빈 칸 = 옅은 창호지(α .16) + 실루엣. 초점 받침은 테 밑의 띠라서 빈 칸의 어두운 속이 그대로 보인다.'))
    sh.row([(after_f, None), (after_b, None)])
    sh.title('(3)', '테마: 석경 탭(왼쪽) · 오덕 칸 초점(오른쪽)', (
        '왼쪽: 석경 칩도 같은 교창(다섯 칸씩). 탁본 그림 틀은 #304 그대로다(밝은 글자가 어두운 바탕에 선다 — 백자 판을 쓰지 않는다).',
        '오른쪽: 못 새긴 오덕 = 종이 없는 빗살(Lattice.Bit) + 비활성색 관변 테. 새긴 오덕의 관변 테와 한자는 백자 판에서 흑상감색.'))
    sh.row([(after_c, None), (after_i, None)])

    sh.title('(4)', '1080p 화소 그대로(×1): 칸 격자 · 그림 틀 · 상태 열 — 왼쪽이 테마 전, 오른쪽이 테마')
    g, pq, st = (60, 222, 740, 942), (1190, 218, 1418, 446), (1456, 140, 1876, 880)
    plq = Image.new('RGB', (228, 228 * 2 + 62), BG)
    plq.paste(before_a.crop(pq), (0, 0))
    plq.paste(after_a.crop(pq), (0, 228 + 62))
    ImageDraw.Draw(plq).text((0, 228 + 26), '↓ 테마', font=EM.font('N7', 22), fill=TXT)
    sh.row([(before_a.crop(g), '칸 격자 · 테마 전'), (after_a.crop(g), '칸 격자 · 테마'), (plq, '그림 틀'),
            (before_a.crop(st), '상태 열 · 테마 전'), (after_a.crop(st), '상태 열 · 테마 (살대 · 꽃잎)')], gapx=40)

    sh.title('(5)', '칸의 두 상태(200 %): 초점 · 선택 유지 — 왼쪽이 테마 전, 오른쪽이 테마', (
        '초점(위): 테마 전에는 테의 바깥 절반이 장막 위에 선다. 테마에서는 한지 받침(칸 밖 14 × 12 px, 안 8 px)이 살대를 덮고 테 전체가 그 위에 선다.',
        '선택 유지(아래): 안개색 붓 테 → 자개 끊음질 테 2 px(칸 밖 3 px) + 옻칠 띠 8 px. 값(주사 대 자개)과 꼴(굵은 붓 테 + 방점 대 가는 끊음질) 둘 다로 갈린다.'))
    fo, ke, ca = (84, 596, 344, 776), (84, 290, 344, 450), (1090, 478, 1430, 678)
    sh.row([(up(before_a.crop(fo)), '초점 · 테마 전'), (up(after_a.crop(fo)), '초점 · 테마'),
            (up(before_f.crop((84, 298, 344, 458))), '선택 유지 · 테마 전'), (up(after_f.crop(ke)), '선택 유지 · 테마')], gapx=40)
    sh.row([(up(before_f.crop(ca)), '후보 줄 초점 · 테마 전'), (up(after_f.crop(ca)), '후보 줄 초점 · 테마')], gapx=40)

    # ---- weakness 6: the three grids side by side
    sh.title('(6)', '칸 격자의 세 안(×1) — 표가 아니라 창으로 읽혀야 한다 (키트 검토의 약점 6)', (
        'A 키트 검토의 시안: 살대가 20 px 틈의 가운데에 서고 찢긴 칩이 8 px 떠 있다. 줄마다 칸 수가 달라 바깥 틀이 계단이다 → 표.',
        'B 문짝 한 짝: 민 바깥 틀 5 × 4, 종이가 살에 끼워지고 이름은 띠 안에. 칸이 없는 자리는 옻칠 판 — 둘째 줄 끝의 한 칸짜리 판이 "잠긴 칸"처럼 읽힌다.',
        'C 교창 네 짝(선택): 줄(= 묶음)마다 민 틀 한 짝, 종이가 살에 끼워지고 이름은 문지방 아래 장막 위에. 칸 없는 살을 그리지 않는다. 첫 줄만 두 칸 너비의 옻칠 궁판(자개 국화)으로 둘째 줄과 너비를 맞춘다.'))
    alts = [(grid_screen(dict(grid=k, focus='swell' if k != 'table' else 'none'), focus=('stone', 0)).im.convert('RGB').crop(g), cap)
            for k, cap in (('table', 'A 표 격자 (키트 검토의 시안)'), ('leaf', 'B 문짝 한 짝'), ('sash', 'C 교창 네 짝 — 선택'))]
    sh.row(alts, gapx=40)

    # ---- weakness 1: focus variants + the table
    sh.title('(7)', '초점이 화면에서 가장 센 표여야 한다 (약점 1): 살대 위의 주사 붓 테 — 네 가지 색각', (
        '재는 법: 테의 먹(알파 ≥ .5)에서 2 px 안에 있는 모든 바탕 화소와 주사(#B8392B)의 명도비. 16칸을 차례로 초점에 두고 가장 낮은 값(괄호 = 5 % 분위). 세계는 가장 밝은 하늘(#E4E0D6) 위의 장막.',
        '색각 모의 = Machado 2009, 세기 1.0(모델이지 사람 시험이 아니다). 바닥 3:1. 테마 전과 A안의 1.00은 찢긴 칩 가장자리 · 칸 이름 글자가 테에 닿는 화소다.'))
    crops = []
    for key, theme, cap in FOCUS_VARIANTS[1:]:
        s = grid_screen(theme, focus=('stone', 0))
        crops.append((up(s.im.convert('RGB').crop(fo)), cap.split(':')[0]))
    xs = sh.row(crops, gapx=40)
    rows = [['', '정상', '적색맹', '녹색맹', '청색맹', '']]
    for key, theme, cap in FOCUS_VARIANTS:
        m = numbers['focus'][key]
        rows.append([cap] + [fmt(m[v]['min'], 3.0, m[v]['p05']) for v in VISIONS] + [('선택' if key == 'sash_swell' else '', RED)])
    for key, cap in (('candidate_row', '테마: 바꿔 낄 것 줄의 초점(칩 56, 한지 받침)'), ('fragment_chip', '테마: 석경 칩의 초점(한지 받침)')):
        m = numbers['focus'][key]
        rows.append([cap] + [fmt(m[v]['min'], 3.0) for v in VISIONS] + [''])
    k = numbers['kept']
    rows.append(['선택 유지: 자개 테 / 옻칠 띠 (참고)'] + [fmt(k[v]) for v in VISIONS] + [''])
    rows.append(['선택(자개) / 초점(주사) 값 차 (바닥 2.5)'] + [fmt(k['shell_vs_cinnabar'][v], 2.5) for v in VISIONS] + [''])
    w = numbers['weight']
    rows.append(['표의 무게: 초점 = 한지 받침 %d px² + 주사 테 %d px² · 선택 유지 = 자개 %d px² + 옻칠 띠 %d px²' % (w['bed_px2'], w['ring_px2'], w['nacre_px2'], w['moat_px2']), '', '', '', '', ''])
    sh.y = sh.table(M, sh.y, rows, [1180, 190, 190, 190, 190, 120]) + 50

    # ---- phase 2
    sh.title('(8)', '2단계(모든 메뉴 페이지에 같이): 레일 = 끊음질 선, 채운 건반 = 백자 단추 — 만들어 두었고 꺼져 있다', (
        '위 = 1단계(지금의 기본: 레일과 건반은 #304 그대로), 아래 = 2단계를 켠 같은 화면(낀 장비 칸 초점, 범례 셋). 한 화면만 다른 건반을 쓰지 않으려고 기본은 끔이다.',))
    r1, r2 = (56, 44, 1864, 132), (56, 960, 760, 1024)
    p1 = shot('gear')
    strip = Image.new('RGB', (1808, 88 * 2 + 12), BG)
    strip.paste(p1.crop(r1), (0, 0))
    strip.paste(phase2_h.crop(r1), (0, 100))
    leg = Image.new('RGB', (704, 64 * 2 + 12), BG)
    leg.paste(p1.crop(r2), (0, 0))
    leg.paste(phase2_h.crop(r2), (0, 76))
    sh.row([(strip, None), (leg, None)], gapx=40)

    # ---- tables
    sh.title('(9)', '잰 값: 글 대비 · 자개 넓이 · 글 상자와의 거리', (
        '글 대비 = 글자색과 그 잉크 상자 밑 가장 불리한 화소의 명도비(일곱 화면, 가장 밝은 하늘 위의 장막). 바닥: 주 글자 7:1 · 보조 글자 4.5:1. 비활성 글은 바닥이 없다(사유 글이 붙는다).',))
    y0 = sh.y
    rows = [['글의 층 (역할 | 색)', '가장 낮은 대비', '바닥', '그 글 · 화면 · 밑바탕']]
    for r in numbers['text']['classes']:
        if r['kind'] != 'text':
            continue
        rows.append(['%s | %s' % (r['role'], r['colour']), fmt(r['worst'], r['floor']), '%.1f' % r['floor'] if r['floor'] else '—',
                     '"%s" · %s · %s' % (r['text'][:16], r['screen'], r['ground'])])
    yb = sh.table(M, y0, rows, [330, 210, 110, 900], size=22)
    rows = [['자개 넓이 (화면)', 'px²', '화면의 %']]
    for name, a in sorted(numbers['nacre']['screens'].items()):
        rows.append([name, '%.0f' % a, '%.3f' % (a / (W * H) * 100)])
    rows.append(['2단계(레일 선)를 켠 h_gear', '%.0f' % numbers['nacre']['phase2_h_gear'], '%.3f' % (numbers['nacre']['phase2_h_gear'] / (W * H) * 100)])
    rows.append([('한도 (메뉴 한 화면)', TXT), '%.0f' % (W * H * .004), '.400'])
    y1 = sh.table(M + 1640, y0, rows, [470, 140, 160], size=22)
    rows = [['테마 요소와 글 상자의 거리', '가장 가까운 거리', '요구', '어디']]
    label = dict(structure='구조(살대 · 울거미 · 빗살)', ornament='장식(자개 국화)', inlay='상감선(연판 띠 · 굽 선)', bed='초점 한지 받침',
                 state='선택 유지 자개 테(상태 표)', symbol='자개 꽃잎(글줄의 기호)', rail='레일 끊음질 선(2단계)')
    for cls in ('structure', 'ornament', 'inlay', 'bed', 'state', 'symbol', 'rail'):
        r = numbers['clear'].get(cls)
        if not r:
            continue
        rows.append([label[cls], fmt(r['min'], r['need']), ('≥ %d px' % r['need']) if r['need'] else '—', '"%s" · %s' % (r['text'][:14], r['screen'])])
    y2 = sh.table(M + 2500, y0, rows, [470, 230, 130, 520], size=22)
    sh.y = max(yb, y1, y2) + 30
    return sh.build()


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, 'PNG')
    return b.getvalue()


def measure():
    clear, area = measure_clear(PICK)
    p2 = EM.render('gear', True, dict(phase2=True), measure=True)
    cp2, _ = measure_clear(dict(phase2=True))
    if 'rail' in cp2:
        clear['rail'] = cp2['rail']
    limit = EM._kit['limits']['NacreMaxScreenPctMenu']
    worst = max(max(area.values()), p2.nacre)
    return dict(
        spec='SPEC-UI-EQUIPMENT-308 x SPEC-UI-THEME-308', decision='D308-15', status='TEST', generator='Tools/Art/equip308_theme.py',
        method='sRGB compositing of the concept mock (not a capture); world = brightest sky #E4E0D6 under the veil; Machado 2009 severity 1.0',
        theme=dict(EM.THEME), geometry=dict(sash=EM.TG, bed={str(k): v for k, v in EM.BED.items()}, moat=EM.MOAT),
        kit_atlas_sha256=EM.kit().meta['sha256'],
        focus=measure_focus(), kept=measure_kept(), weight=measure_weight(), text=measure_text(PICK), clear=clear,
        text_before_theme=dict(max_ground_spread_under_text=measure_text(None)['max_ground_spread_under_text']),
        nacre=dict(screens=area, phase2_h_gear=round(p2.nacre, 1), limit_pct=limit, worst_pct=round(worst / (W * H) * 100, 3),
                   figurative_motifs_per_screen=1),
    )


def verdict(n):
    bad = []
    for v in VISIONS:
        for key in ('sash_swell', 'candidate_row', 'fragment_chip'):
            if n['focus'][key][v]['min'] < 3.0:
                bad.append('focus %s %s %.2f < 3' % (key, v, n['focus'][key][v]['min']))
    bad += ['text %s | %s %.2f < %.1f' % (r['role'], r['colour'], r['worst'], r['floor']) for r in n['text']['classes'] if not r['ok']]
    bad += ['clear %s %.1f < %.0f' % (c, r['min'], r['need']) for c, r in n['clear'].items() if not r['ok']]
    if n['nacre']['worst_pct'] > n['nacre']['limit_pct']:
        bad.append('nacre %.3f %% > %.2f %%' % (n['nacre']['worst_pct'], n['nacre']['limit_pct']))
    return bad


def main():
    check = '--check' in sys.argv[1:]
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    numbers = measure()
    bad = verdict(numbers)
    numbers['floors_failed'] = bad
    sheet = build(numbers)
    data = png_bytes(sheet)
    sha = hashlib.sha256(data).hexdigest()
    path = os.path.join(OUT, 'equip308_theme_sheet.png')
    if check:
        again = hashlib.sha256(png_bytes(build(measure() | {'floors_failed': bad}))).hexdigest()
        disk = hashlib.sha256(open(path, 'rb').read()).hexdigest() if os.path.exists(path) else None
        print('determinism:', 'same bytes' if again == sha else 'DIFFERENT', sha[:16])
        print('disk:', 'same' if disk == sha else 'DIFFERENT (run without --check)')
        print('floors:', 'all met' if not bad else ' | '.join(bad))
        sys.exit(0 if again == sha and disk == sha and not bad else 1)
    os.makedirs(OUT, exist_ok=True)
    with open(path, 'wb') as fh:
        fh.write(data)
    sheet.resize((sheet.width // 2, sheet.height // 2), Image.LANCZOS).save(os.path.join(OUT, 'equip308_theme_sheet_half.jpg'), quality=90)
    for name, view, shop in VIEWS:
        EM.render(view, shop, PICK).save(os.path.join(OUT, 'equip308_theme_%s.png' % name))
    numbers['sheet'] = dict(file='equip308_theme_sheet.png', size=list(sheet.size), sha256=sha)
    with open(os.path.join(OUT, 'equip308_theme.json'), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(numbers, fh, ensure_ascii=False, indent=1)
    print('sheet', sheet.size, sha[:16], '->', path)
    f = numbers['focus']
    for key in ('before', 'table', 'sash_none', 'sash_veil', 'sash_swell', 'candidate_row', 'fragment_chip'):
        print('focus %-14s' % key, ' '.join('%s %.2f' % (v, f[key][v]['min']) for v in VISIONS))
    print('kept nacre / lacquer', ' '.join('%s %.2f' % (v, numbers['kept'][v]) for v in VISIONS))
    for r in numbers['text']['classes']:
        print('text %-9s %-22s %6.2f floor %s  "%s" %s on %s' % (r['kind'][:7], r['role'] + ' | ' + r['colour'], r['worst'], r['floor'], r['text'][:14], r['screen'], r['ground']))
    print('ground spread under text (max):', numbers['text']['max_ground_spread_under_text'])
    for c, r in numbers['clear'].items():
        print('clear %-10s %6.1f px (need %s) "%s" %s' % (c, r['min'], r['need'], r['text'][:14], r['screen']))
    print('nacre', numbers['nacre'])
    print('floors:', 'all met' if not bad else ' | '.join(bad))


if __name__ == '__main__':
    main()
