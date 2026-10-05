#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 4 (rev 2) - the pictures (PIL + numpy; no GPU, no editor). Called by relayout_fix4_dry.py --pictures.
  Art/Playtest308/Relayout/fix4/<A_yard|B_rope|C_potter>_plan.png        plan: ground, slope, vegetation rows, grass seeds, paths, rule circles, old / new
  Art/Playtest308/Relayout/fix4/<...>_views.png (+ _half.png)            software eye views from the path, before | after, with the pixel counts
  Art/Playtest308/Relayout/fix4/<...>_user_half.jpg                      the one picture per place for the user: before | after from the path + one honest sentence
The views carry no material, grass, light or post-process: a tree crown is a solid ellipsoid, a man is three boxes, an existing object
whose mesh is an FBX is its measured box. They show what is in the frame and how large; the look is judged on the editor stills.
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

import relayout_fix4_lib as F
import relayout_fix3_lib as L

INK = (34, 32, 28); PAPER = (244, 239, 228); OLD = (170, 60, 46); NEW = (31, 95, 139); ROAD = (150, 110, 40); RULE = (120, 60, 140); GREY = (90, 90, 90)


def font(sz, bold=False):
    try: return ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf' if bold else 'C:/Windows/Fonts/malgun.ttf', sz)
    except Exception: return ImageFont.load_default()


class Plan:
    def __init__(self, win, K, title, lines):
        self.win = win; self.K = K; X0, X1, Z0, Z1 = win; self.Wd, self.Hd = int((X1 - X0) * K), int((Z1 - Z0) * K); self.TOP = 46 + 24 * len(lines)
        self.im = Image.new('RGB', (self.Wd, self.Hd + self.TOP), PAPER); self.dr = ImageDraw.Draw(self.im, 'RGBA')
        xs = X0 + (np.arange(self.Wd) + .5) / K; zs = Z1 - (np.arange(self.Hd) + .5) / K; GX, GZ = np.meshgrid(xs, zs); G = F.hv(GX, GZ)
        gx = np.abs(np.gradient(G, axis=1)) * K; gz = np.abs(np.gradient(G, axis=0)) * K; sl = np.degrees(np.arctan(np.hypot(gx, gz)))
        img = np.zeros((self.Hd, self.Wd, 3), 'float32') + np.array(PAPER, 'float32') - np.clip((sl - 6) / 30.0, 0, 1)[..., None] * 60.0
        for stp, dark in ((0.5, 14), (2.0, 44)):
            Lv = np.floor(G / stp); e = np.zeros(G.shape, bool); e[:, 1:] |= Lv[:, 1:] != Lv[:, :-1]; e[1:, :] |= Lv[1:, :] != Lv[:-1, :]; img[e] -= dark
        self.im.paste(Image.fromarray(np.clip(img, 0, 255).astype('uint8')), (0, self.TOP))
        self.dr.text((14, 8), title, font=font(24, True), fill=INK)
        for i, t in enumerate(lines): self.dr.text((14, 42 + 24 * i), t, font=font(16), fill=INK)

    def P(self, q): return ((q[0] - self.win[0]) * self.K, self.TOP + (self.win[3] - q[1]) * self.K)

    def circle(self, c, r, outline, fill=None, width=2):
        p = self.P(c); k = r * self.K; self.dr.ellipse([p[0] - k, p[1] - k, p[0] + k, p[1] + k], outline=outline, fill=fill, width=width)

    def veg(self):
        for s_ in F.seeds(self.win): self.dr.point(self.P((s_[0], s_[2])), fill=(70, 130, 70, 255))
        for r in F.veg_rows(self.win):
            c = (r['pos'][0], r['pos'][2])
            if r['tree']: self.circle(c, r['R'], (30, 80, 40, 150), (60, 110, 70, 46), 1); self.circle(c, max(r['trunk'], .12), (50, 40, 30, 255), (50, 40, 30, 255), 1)
            else: self.circle(c, r['R'], (110, 110, 110, 200), (150, 150, 150, 90), 1)

    def route(self, pl, half, label=None):
        vis = [q for q in pl if self.win[0] - 20 <= q[0] <= self.win[1] + 20 and self.win[2] - 20 <= q[1] <= self.win[3] + 20]
        if len(vis) < 2: return
        left = []; right = []
        for i, p in enumerate(vis):
            a = vis[max(0, i - 1)]; b = vis[min(len(vis) - 1, i + 1)]; dx, dz = b[0] - a[0], b[1] - a[1]; ln = math.hypot(dx, dz) or 1.0; nx, nz = -dz / ln, dx / ln
            left.append((p[0] + nx * half, p[1] + nz * half)); right.append((p[0] - nx * half, p[1] - nz * half))
        self.dr.polygon([self.P(q) for q in left + right[::-1]], fill=(196, 150, 70, 44)); self.dr.line([self.P(q) for q in vis], fill=ROAD + (255,), width=2)

    def poly(self, pts, outline, fill=None, width=2):
        pp = [self.P(q) for q in pts]
        if fill: self.dr.polygon(pp, fill=fill)
        self.dr.line(pp + [pp[0]], fill=outline, width=width)

    def mark(self, q, text, col, r=5, dy=-9, dx=8, bold=False):
        p = self.P(q); self.dr.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=col + (255,)); self.dr.text((p[0] + dx, p[1] + dy), text, font=font(15, bold), fill=col)

    def grid(self, step=10):
        X0, X1, Z0, Z1 = self.win
        for x in range(int(X0 // step * step), int(X1) + 1, step):
            p = self.P((x, Z0)); self.dr.line([(p[0], self.TOP), (p[0], self.TOP + self.Hd)], fill=(0, 0, 0, 22)); self.dr.text((p[0] + 2, self.TOP + self.Hd - 16), str(x), font=font(11), fill=(60, 60, 60))
        for z in range(int(Z0 // step * step), int(Z1) + 1, step):
            p = self.P((X0, z)); self.dr.line([(0, p[1]), (self.Wd, p[1])], fill=(0, 0, 0, 22)); self.dr.text((2, p[1] + 1), str(z), font=font(11), fill=(60, 60, 60))

    def eye(self, e, look, label):
        p = self.P((e[0], e[2])); q = self.P((look[0], look[2])); self.dr.line([p, q], fill=(0, 0, 0, 60), width=1); self.dr.polygon([(p[0], p[1] - 7), (p[0] - 6, p[1] + 5), (p[0] + 6, p[1] + 5)], fill=(0, 0, 0, 220)); self.dr.text((p[0] + 8, p[1] - 4), label, font=font(13, True), fill=INK)


def shot(layers, eye, look, size=(800, 450)):
    img, _, _ = F.render(layers, eye, look, 60, 960, 540); return Image.fromarray(img).resize(size, Image.LANCZOS)


def views_sheet(path, title, cells):
    """cells = [(caption, [(label, layers), ...], eye, look, lines)]: one row per eye, one column per state."""
    cw, ch = 800, 450; cols = max(len(c[1]) for c in cells); top = 50; rowh = ch + 96; im = Image.new('RGB', (cols * (cw + 12) + 12, top + rowh * len(cells) + 8), PAPER); dr = ImageDraw.Draw(im)
    dr.text((14, 10), title, font=font(24, True), fill=INK)
    for i, (cap, states, eye, look, lines) in enumerate(cells):
        y0 = top + i * rowh
        for j, (label, layers) in enumerate(states):
            x0 = 12 + j * (cw + 12)
            im.paste(shot(layers, eye, look, (cw, ch)), (x0, y0)); dr.rectangle([x0, y0, x0 + cw, y0 + ch], outline=(90, 90, 90))
            dr.rectangle([x0, y0, x0 + 150, y0 + 26], fill=(255, 255, 255)); dr.text((x0 + 6, y0 + 3), label, font=font(16, True), fill=OLD if j == 0 else NEW)
        dr.text((14, y0 + ch + 4), cap, font=font(17, True), fill=INK)
        for k, t in enumerate(lines): dr.text((14, y0 + ch + 28 + 20 * k), t, font=font(14), fill=INK)
    im.save(path); im.resize((im.size[0] // 2, im.size[1] // 2), Image.LANCZOS).save(str(path).replace('.png', '_half.png')); print('wrote', path, im.size)


def wrap(dr, text, fnt, width):
    """break a Korean sentence at spaces so that no line is wider than `width` pixels."""
    out = []; cur = ''
    for word in text.split(' '):
        t = (cur + ' ' + word).strip()
        if dr.textlength(t, font=fnt) <= width or not cur: cur = t
        else: out.append(cur); cur = word
    if cur: out.append(cur)
    return out


def user_picture(path, title, rows, sentence):
    """the one picture per place for the user. rows = [(caption, before layers, after layers, eye, look)]; before | after from the path;
    one honest sentence under them. Written at half size (two 480 x 270 cells per row)."""
    cw, ch = 480, 270; pad = 10; top = 44; W = 2 * cw + 3 * pad; tmp = Image.new('RGB', (10, 10)); td = ImageDraw.Draw(tmp); f_s = font(15); lines = wrap(td, sentence, f_s, W - 2 * pad)
    H = top + len(rows) * (ch + 30) + 12 + 22 * len(lines) + 30
    im = Image.new('RGB', (W, H), PAPER); dr = ImageDraw.Draw(im); dr.text((pad, 8), title, font=font(20, True), fill=INK)
    for i, (cap, before, after, eye, look) in enumerate(rows):
        y0 = top + i * (ch + 30)
        for j, (label, layers) in enumerate((('지금', before), ('바꾼 뒤', after))):
            x0 = pad + j * (cw + pad); im.paste(shot(layers, eye, look, (cw, ch)), (x0, y0)); dr.rectangle([x0, y0, x0 + cw, y0 + ch], outline=(90, 90, 90))
            dr.rectangle([x0, y0, x0 + 74, y0 + 22], fill=(255, 255, 255)); dr.text((x0 + 5, y0 + 2), label, font=font(14, True), fill=OLD if j == 0 else NEW)
        dr.text((pad, y0 + ch + 4), cap, font=font(14, True), fill=INK)
    y = top + len(rows) * (ch + 30) + 6
    for t in lines: dr.text((pad, y), t, font=f_s, fill=INK); y += 22
    dr.text((pad, y + 4), '소프트웨어 뷰 [O]: 재질 · 풀 · 조명 · 먹 룩 없음, 수관은 속이 찬 타원체, 사람은 상자 셋. 실제 모습은 편집기 스틸이 판정한다.', font=font(12), fill=(90, 90, 90))
    im.save(path, quality=88); print('wrote', path, im.size)


def draw_all(P, T, out):
    import relayout_fix4_dry as D
    W = out['W']; geo = out['geo']; V = out['views']; F.OUT.mkdir(parents=True, exist_ok=True); rules = P['rules']
    # ------------------------------------------------------------------ A
    A = next(p for p in P['places'] if p['id'] == 'A_yard'); lf = out['lf']; enc = W.enc[A['lesson']]; st = out.get('stone')
    pl = Plan((3310, 3368, 2636, 2716), 14, '적재장 (금 교습 자리) — 평면', [
        '갈색 선 = 길 중심선(띠 = 폭), 초록 원 = 수관 · 가운데 점 = 줄기, 초록 점 = 풀 씨앗(서쪽 절반은 0 = 맨땅)',
        '보라 원 = 교습 감지 %.0f m(충돌체는 전부 그 밖). 진한 파랑 = 충돌체(굽기 필요), 파란 테 = 충돌체 없음' % enc['Detection'],
        '회색 = 지금 있는 것(경계 상자): 높은 석대(국 재방문) · 그 서쪽 승강 자리 · 선별대 · 궤 · 각목'])
    pl.veg()
    for rid in ('geumpyo_inn__logging', 'logging__high_cache', 'high_cache__geumpyo_inn', 'logging_deep251', 'root_approach263'):
        if rid in W.routes: pl.route(W.routes[rid], W.width[rid] / 2)
    pl.circle(lf, enc['Detection'], RULE + (255,), None, 2); pl.circle(lf, float(W.points[A['anchor_point']]['Radius']), (180, 30, 30, 255), (180, 30, 30, 40), 2)
    for k, poly, piv in out['existing']:
        if poly: pl.poly(poly, GREY + (255,), (150, 150, 150, 120), 1)
        else: pl.dr.point(pl.P(piv), fill=GREY + (255,))
    if st: pl.dr.text(pl.P((3326.4, 2672.4)), '높은 석대 %.1f m' % st['height'], font=font(14, True), fill=GREY)
    pl.mark((3361.1, 2690.8), '선별대 · 궤 · 각목', GREY); pl.mark((3318.0, 2688.0), '벌목장 조사 지점', (120, 30, 120))
    for r in A['rows']:
        fp, cols, top = geo[r['id']]
        if r.get('solid'):
            pl.poly(fp, NEW + (160,), None, 1)
            for c in cols: pl.poly(c, NEW + (255,), NEW + (150,), 2)
        else: pl.poly(fp, NEW + (255,), None, 2)
        pl.dr.text(pl.P((r['x'] + .3, r['z'] + .9)), r['id'].replace('A_', ''), font=font(13, True), fill=NEW)
    pl.mark(lf, '교습 나무 (웃자란 나무)', (180, 30, 30), bold=True)
    for d in P['read']['distances_m'] + ['L']: pl.eye(V[('A', d)]['eye'], V[('A', d)]['look'], ('눈 %d m' % d) if d != 'L' else '교습 나무 15 m 앞')
    pl.grid(); pl.im.save(F.OUT / 'A_yard_plan.png'); print('wrote', F.OUT / 'A_yard_plan.png', pl.im.size)
    cells = []
    for d in P['read']['distances_m'] + ['L']:
        v = V[('A', d)]; s_ = v['stacks']; lb, la = v['lesson_before'], v['lesson_after']
        cells.append((('길 위, 적재장 %d m 앞 (눈 높이 1.6 m)' % d) if d != 'L' else '길 위, 교습 나무 15 m 앞 (더미 사이에 서서 본다) — 교습 나무는 높이 %.1f m의 어린 나무' % out['lesson_height'], [('지금', v['before']), ('적재장으로', v['after'])], v['eye'], v['look'],
                      ['통나무 더미 + 삼발이: 화면의 %.2f %% · 가려지지 않은 몫 %.0f %% (지금 0). 회색 상자 = 지금도 있는 높은 석대' % (100 * s_[3], 100 * s_[2]), '교습 나무가 보이는 픽셀: %d → %d' % (lb[0], la[0]) if lb[0] else '이 눈은 더미를 본다(교습 나무는 화면 밖)']))
    views_sheet(F.OUT / 'A_yard_views.png', '적재장 — 길에서 본 모습 (소프트웨어 뷰: 재질 · 풀 · 조명 없음, 수관은 속이 찬 타원체)', cells)
    v15, v40 = V[('A', 15)], V[('A', 40)]
    user_picture(F.OUT / 'A_yard_user_half.jpg', '적재장 (금 교습 자리) — 길에서 본 모습', [
        ('15 m 앞', v15['before'], v15['after'], v15['eye'], v15['look']), ('40 m 앞 (비탈 위)', v40['before'], v40['after'], v40['eye'], v40['look'])],
        '15 m 앞에서는 석대 곁에 통나무 더미 넷과 통나무를 달아 올린 삼발이가 한 무리로 서지만(화면의 %.1f %%), 40 m 앞에서는 화면의 %.2f %%뿐이라 "빈터에 뭔가 쌓여 있다" 정도이고, 교습 나무 자체는 높이 %.1f m의 어린 나무 그대로라 과녁으로는 여전히 작습니다.' % (100 * v15['stacks'][3], 100 * v40['stacks'][3], out['lesson_height']))
    # ------------------------------------------------------------------ B
    atB, oldB = out['atB'], out['oldB']; specB = next(p for p in T['points'] if p['id'] == 'sinmok_rope_keeper308'); personB = next(n for n in T['people'] if n['id'] == 'sinmok_rope_keeper308')
    boss = F.P3(W.enc['sinmok263']['Feet'])[::2]; rest = F.P3(W.points['sinmok_rest263']['Position'])[::2]; feet = W.feet['sinmok_rest263'][::2]; orr = out.get('old_rope_over_ground')
    pl = Plan((3646, 3692, 3226, 3256), 24, '금줄 꼬는 노인 — 평면', [
        '보라 = 싸움을 지키는 두 규칙: 신목 43 m 원 · 쉼터 12 m 원(둘 다 그대로). 파랑 = 옮긴 자리(지금 자리는 화면 밖 남동쪽 28 m, 풀밭 선반)',
        '옮긴 자리: 접근로 옆 %.2f m, 신목 %.1f m, 쉼터 발 %.1f m, 격자 경사 %.1f° (8° 규칙을 이 사람에게만 16°로: 길가에 8° 이하 땅이 없다)' % (W.route_d(atB, ['sinmok_approach263'])[0], D.d2(atB, boss), D.d2(atB, feet), F.tri_slope(*atB)),
        '새 금줄 = 장대 둘(흰 줄), 걸이마다 타래. 눕힌 멍석 · 짚단(파란 네모), 짚가리(파란 원). 묵은 금줄은 자료로는 땅 위 %s m에 떠 있다(미실측)' % ('%.1f – %.1f' % orr if orr else '?')])
    pl.veg(); pl.route(W.routes['sinmok_approach263'], W.width['sinmok_approach263'] / 2)
    pl.circle(boss, specB['clear']['boss_min_m'], RULE + (255,), None, 2); pl.circle(rest, specB['clear']['rests_min_m'], RULE + (255,), None, 2); pl.circle(feet, specB['clear']['rests_min_m'], RULE + (120,), None, 1)
    pl.mark(rest, '쉼터(돌무더기) — 자료의 높이가 땅 위 5.5 m', (160, 110, 0))
    pl.dr.line([pl.P((3685.0, 3240.0)), pl.P((3689.4, 3240.0))], fill=(90, 60, 30, 255), width=4); pl.dr.text(pl.P((3682.0, 3242.6)), '묵은 금줄', font=font(15, True), fill=(90, 60, 30))
    pl.circle(atB, float(specB['radius']), NEW + (255,), NEW + (30,), 2); pl.mark(atB, '노인', NEW, bold=True, dy=-22, dx=6)
    for r in next(p for p in P['places'] if p['id'] == 'B_rope')['rows']:
        m = D.mesh_of(P, r['mesh']); span = (m['hi'][2] - m['lo'][2]) / 2 - 0.1
        ends = [D.to_world((r['x'], r['z']), r['yaw'], 0, s_ * span) for s_ in (-1, 1)]; pl.dr.line([pl.P(q) for q in ends], fill=(255, 255, 255, 255), width=6); pl.dr.line([pl.P(q) for q in ends], fill=NEW + (255,), width=2)
        for q in ends: pl.circle(q, .12, NEW + (255,), NEW + (255,), 1)
    for pr in personB['props']:
        q, wyaw = D.prop_pose(personB, atB, pr)
        if pr.get('hang_m') is not None: pl.circle(q, .22, (150, 110, 40, 255), (190, 160, 90, 255), 2)
        elif pr.get('tilt'): pl.poly(D.prop_box(pr, q, wyaw), NEW + (255,), (190, 170, 110, 170), 2)
        else: pl.poly(D.prop_ring(pr, q, wyaw, 16), NEW + (255,), (190, 170, 110, 200), 2)
        lab = {'StrawHeap': ('짚가리', -2.6, -.9), 'WorkMat': ('멍석', -2.2, 1.5), 'RopeCoil0': ('타래(걸이)', .35, .5), 'RopeCoil1': ('타래(걸이)', .35, .2), 'Sheaf0': ('짚단', -.9, 2.1), 'Sheaf1': ('짚단', -2.6, .4)}.get(pr['name'], (pr['name'], .3, .2))
        t = (q[0] + lab[1], q[1] + lab[2]); pl.dr.line([pl.P(q), pl.P((t[0] + .5, t[1] - .25))], fill=(0, 0, 0, 110), width=1); pl.dr.text(pl.P(t), lab[0], font=font(13, True), fill=INK)
    for d in (45, 15, 6):
        e = V[('B', d)]['eye']
        if pl.win[0] <= e[0] <= pl.win[1]: pl.eye(e, V[('B', d)]['look'], '눈 %d m' % d)
    pl.grid(5); pl.im.save(F.OUT / 'B_rope_plan.png'); print('wrote', F.OUT / 'B_rope_plan.png', pl.im.size)
    cells = []
    for d in (45, 15, 6):
        v = V[('B', d)]; ma, mb, pp, prp = v['man_after'], v['man_before'], v['paper'], v['props']
        cells.append(('접근로 위, 노인 %d m 앞' % d, [('지금 (같은 눈)', v['before']), ('옮긴 뒤', v['after'])], v['eye'], v['look'],
                      ['노인: 지금 %d px → 옮긴 뒤 %d px (가려지지 않은 몫 %.0f %%) · 짚가리 · 멍석 · 짚단 · 타래 %d px' % (mb[0], ma[0], 100 * ma[2], prp[0]), '흰 종이 술 %d px. 묵은 금줄은 자료가 놓은 자리(땅 위 4 m 넘게)에 그렸다 — 읽힌다는 근거가 아니다' % pp[0]]))
    views_sheet(F.OUT / 'B_rope_views.png', '금줄 꼬는 노인 — 접근로에서 본 모습 (소프트웨어 뷰)', cells)
    v15, v45 = V[('B', 15)], V[('B', 45)]
    user_picture(F.OUT / 'B_rope_user_half.jpg', '금줄 꼬는 노인 — 접근로에서 본 모습', [
        ('15 m 앞', v15['before'], v15['after'], v15['eye'], v15['look']), ('45 m 앞', v45['before'], v45['after'], v45['eye'], v45['look'])],
        '지금은 같은 눈(45 m)에서 사람이 %d px라 안 보이는데, 옮긴 뒤에는 길가에 사람(%d px)과 짚가리, 그 뒤에 흰 종이 술 달린 새 금줄이 서지만, 45 m 앞에서는 여전히 작은 꼴들이고, 서 있는 땅이 12° 비탈이라 멍석과 짚단을 기울여 눕혔으며, 묵은 금줄은 자료로는 땅 위 4 m 넘게 떠 있어 "함께 보인다"고 말할 수 없습니다.' % (v45['man_before'][0], v45['man_after'][0]))
    # ------------------------------------------------------------------ C
    atC, oldC = out['atC'], out['oldC']; pc = P['person']['south_gate_potter308']; restp = W.points[pc['rest']]; rq = F.P3(restp['Position'])[::2]; feetC = W.feet[pc['rest']][::2]; specC = next(p for p in T['points'] if p['id'] == 'south_gate_potter308'); personC = next(n for n in T['people'] if n['id'] == 'south_gate_potter308')
    pl = Plan((1904, 1944, 2424, 2462), 24, '옹기장수 — 평면', [
        '갈색 띠 = 가도 8 m · 진한 띠 = 차로. 주황 원 = 쉬기 3.0 m, 파랑 원 = 말 걸기 2.5 m — 안 겹친다(%.2f m > 5.5)' % D.d2(atC, rq),
        '옮긴 자리(길이 꺾이는 바깥쪽 어깨): 돌무더기 %.2f m(지금 %.2f), 리스폰 발 %.2f m, 길 중심 %.2f m, 경사 %.1f°' % (D.d2(atC, rq), D.d2(oldC, rq), D.d2(atC, feetC), W.route_d(atC, [pc['route']])[0], F.tri_slope(*atC)),
        '갈색 원 = 옹기(지게 위 둘 · 땅 둘, 전부 갈색 질그릇). 빨간 테 = 1차안의 자리(동쪽 어깨 7.62 m)'])
    pl.veg(); pl.route(W.routes[pc['route']], pc['carriageway_half_m']); pl.route(W.routes[pc['route']], pc['car_lane_half_m'])
    for k in F.scene().subtree_keys(pc['altar_key']):
        b = F.scene_box(k)
        if b is not None and b[1] != 'BoxCollider': pl.poly(F.box_footprint(b[0]), GREY + (255,), (150, 150, 150, 120), 1)
    pl.circle(rq, float(restp['Radius']), (200, 120, 0, 255), (200, 120, 0, 30), 2); pl.mark(rq, '성황당 돌무더기', (160, 100, 0), bold=True); pl.mark(feetC, '리스폰 발', (160, 100, 0), r=3)
    pl.mark(oldC, '지금', OLD, bold=True); pl.circle(oldC, float(specC['radius']), OLD + (160,), None, 1); pl.circle((1926.0, 2443.0), .5, OLD + (150,), None, 1); pl.dr.text(pl.P((1926.7, 2443.4)), '1차안', font=font(12), fill=OLD)
    pl.circle(atC, float(specC['radius']), NEW + (255,), NEW + (36,), 2); pl.mark(atC, '옮긴 자리', NEW, bold=True, dx=-78)
    yawC = D.body_yaw(personC, atC); f1 = D.to_world(atC, yawC, 0, 1.2); pl.dr.line([pl.P(atC), pl.P(f1)], fill=NEW + (255,), width=3)
    for pr in personC['props']:
        q, wyaw = D.prop_pose(personC, atC, pr); rad = D.prefab_size(pr['prefab'])['size'][0] * pr['scale'] / 2; pl.circle(q, rad, NEW + (255,), (150, 116, 80, 255) if not pr.get('on') else (190, 150, 110, 255), 1)
    for d in P['read']['distances_m']: pl.eye(V[('C', d)]['eye'], V[('C', d)]['look'], '눈 %d m' % d)
    pl.grid(5); pl.im.save(F.OUT / 'C_potter_plan.png'); print('wrote', F.OUT / 'C_potter_plan.png', pl.im.size)
    cells = []
    for d in P['read']['distances_m']:
        v = V[('C', d)]; g = v['group']
        cells.append(('가도 위, 돌무더기 %d m 앞' % d, [('지금', v['before']), ('옮긴 뒤', v['after'])], v['eye'], v['look'],
                      ['사람과 돌무더기가 벌어진 각: 지금 %.1f° → %.1f° · 눈에서 사람 %.1f m, 돌무더기 %.1f m' % (v['span0'], v['span'], v['depth'][0], v['depth'][1]), '사람 + 짐 + 돌무더기가 가려지지 않은 몫 %.0f %% (화면의 %.2f %%)' % (100 * g[2], 100 * g[3])]))
    vr = V[('C', 'rest')]
    cells.append(('성황당에서 쉬고 일어난 자리(리스폰 발)에서 그를 봄', [('지금', vr['before']), ('옮긴 뒤', vr['after'])], vr['eye'], vr['look'], ['옮긴 뒤: 사람 %d px (가려지지 않은 몫 %.0f %%)' % (vr['man'][0], 100 * vr['man'][2])]))
    views_sheet(F.OUT / 'C_potter_views.png', '옹기장수 — 가도에서 본 모습 (소프트웨어 뷰)', cells)
    v15, v40 = V[('C', 15)], V[('C', 40)]
    user_picture(F.OUT / 'C_potter_user_half.jpg', '옹기장수 — 가도에서 본 모습', [
        ('15 m 앞', v15['before'], v15['after'], v15['eye'], v15['look']), ('40 m 앞', v40['before'], v40['after'], v40['eye'], v40['look'])],
        '돌무더기에서 %.1f m 떨어져 따로 서 있던 사람을 길이 꺾이는 자리의 돌무더기 옆 %.1f m(같은 깊이)로 옮기고 옹기를 갈색 질그릇 넷(지게 위 둘 · 땅 둘)으로 바꿨지만, 돌무더기 자체가 작아 40 m 앞에서는 둘 다 작은 점이고, 쉬기와 말 걸기 원이 안 겹치려면 5.5 m보다 더 붙일 수는 없습니다.' % (D.d2(oldC, rq), D.d2(atC, rq)))
