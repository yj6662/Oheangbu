#!/usr/bin/env python3
"""L8 study, the plan picture (PIL only). Called by delivery_stop308_plan.py; writes Art/Playtest308/Relayout/fix2/delivery_stop_plan.png (+ _half)."""
import math
import numpy as np


def draw(M, R, flat):
    from PIL import Image, ImageDraw, ImageFont
    J, F, X0, Z0, OUT, PAD_Y = M.J, M.F, M.X0, M.Z0, M.OUT, M.PAD_Y
    PLAT, EAVE, BODY, w, rect, h = M.PLAT, M.EAVE, M.BODY, M.w, M.rect, M.h
    INK = (34, 32, 28); PAPER = (244, 239, 228); PADC = (232, 214, 160); HOUSE = (140, 59, 46); NEW = (31, 95, 139); EARTH = (122, 90, 18); CUT = (170, 51, 51); GREY = (120, 120, 120)

    def font(sz, bold=False):
        try: return ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf' if bold else 'C:/Windows/Fonts/malgun.ttf', sz)
        except Exception: return ImageFont.load_default()
    W, Hh = 2400, 1930
    im = Image.new('RGB', (W, Hh), PAPER); dr = ImageDraw.Draw(im, 'RGBA')
    rin = J['routes']['in']['bends']; rout = J['routes']['out']['bends']; nodes = J['nodes']

    class Panel:
        def __init__(s, ox, oy, x0, x1, z0, z1, ppm, title):
            s.ox, s.oy, s.x0, s.x1, s.z0, s.z1, s.k = ox, oy, x0, x1, z0, z1, ppm; s.w = int((x1 - x0) * ppm); s.h = int((z1 - z0) * ppm)
            dr.text((ox, oy - 30), title, font=font(19, True), fill=INK)

        def p(s, q): return (s.ox + (q[0] - s.x0) * s.k, s.oy + (s.z1 - q[1]) * s.k)

        def contours(s, step, bold=5.0):
            xs = s.x0 + (np.arange(s.w) + 0.5) / s.k; zs = s.z1 - (np.arange(s.h) + 0.5) / s.k
            fx = xs - X0; fz = zs - Z0; ix = np.clip(fx.astype(int), 0, F.shape[1] - 2); iz = np.clip(fz.astype(int), 0, F.shape[0] - 2); u = (fx - ix)[None, :]; v = (fz - iz)[:, None]
            A = F[np.ix_(iz, ix)]; B_ = F[np.ix_(iz, ix + 1)]; C_ = F[np.ix_(iz + 1, ix)]; D_ = F[np.ix_(iz + 1, ix + 1)]
            G = (A + (B_ - A) * u) * (1 - v) + (C_ + (D_ - C_) * u) * v
            img = np.zeros((s.h, s.w, 3), dtype='float32') + np.array(PAPER, dtype='float32')
            gx = np.abs(np.gradient(G, axis=1)) * s.k; gz = np.abs(np.gradient(G, axis=0)) * s.k
            padm = (np.abs(G - PAD_Y) <= 0.10) & (np.hypot(gx, gz) <= math.tan(math.radians(2.0)))
            sl = np.degrees(np.arctan(np.hypot(gx, gz))); shade = np.clip((sl - 6) / 30.0, 0, 1)[..., None] * 26.0
            img = img - shade
            img[padm] = PADC

            def lines(st, dark, thick):
                L = np.floor(G / st); e = np.zeros(G.shape, bool)
                e[:, :-1] |= L[:, :-1] != L[:, 1:]; e[:-1, :] |= L[:-1, :] != L[1:, :]
                if thick: e[:, 1:] |= e[:, :-1].copy(); e[1:, :] |= e[:-1, :].copy()
                img[e] = img[e] * (1 - dark) + np.array(INK, dtype='float32') * dark
            lines(step, 0.33, False); lines(bold, 0.75, s.k >= 8)
            im.paste(Image.fromarray(np.clip(img, 0, 255).astype('uint8')), (s.ox, s.oy))
            dr.rectangle([s.ox, s.oy, s.ox + s.w, s.oy + s.h], outline=INK, width=2)

        def clabels(s, pts, sz=13):
            for q in pts: dr.text(s.p(q), '%.0f' % h(*q), font=font(sz), fill=(70, 66, 58), anchor='mm')

        def poly(s, pts, fill=None, outline=None, width=2):
            pp = [s.p(q) for q in pts]
            if fill: dr.polygon(pp, fill=fill)
            if outline: dr.line(pp + [pp[0]], fill=outline, width=width)

        def line(s, pts, fill, width=2, dash=None):
            pp = [s.p(q) for q in pts]
            if not dash: dr.line(pp, fill=fill, width=width); return
            for a, b in zip(pp[:-1], pp[1:]):
                L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / dash))
                for i in range(0, n, 2): dr.line([(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n), (a[0] + (b[0] - a[0]) * min(i + 1, n) / n, a[1] + (b[1] - a[1]) * min(i + 1, n) / n)], fill=fill, width=width)

        def dot(s, q, r=5, fill=INK): x, y = s.p(q); dr.ellipse([x - r, y - r, x + r, y + r], fill=fill)

        def circle(s, q, rad, outline, width=2, fill=None):
            x, y = s.p(q); r = rad * s.k
            # clipped to the panel: draw on a layer
            layer = Image.new('RGBA', (s.w, s.h), (0, 0, 0, 0)); ld = ImageDraw.Draw(layer)
            ld.ellipse([x - s.ox - r, y - s.oy - r, x - s.ox + r, y - s.oy + r], outline=outline + (255,), width=width, fill=fill)
            im.paste(layer, (s.ox, s.oy), layer)

        def text(s, q, t, sz=15, fill=INK, dx=6, dy=-8, bold=False):
            x, y = s.p(q); x += dx; y += dy; f = font(sz, bold)
            bb = dr.multiline_textbbox((x, y), t, font=f, spacing=2); dr.rectangle([bb[0] - 3, bb[1] - 2, bb[2] + 3, bb[3] + 2], fill=PAPER + (215,))
            dr.multiline_text((x, y), t, font=f, fill=fill, spacing=2)

        def arrow(s, a, b, fill, width=3):
            pa, pb = s.p(a), s.p(b); dr.line([pa, pb], fill=fill, width=width); ang = math.atan2(pb[1] - pa[1], pb[0] - pa[0])
            for d in (2.6, -2.6): dr.line([pb, (pb[0] + 14 * math.cos(ang + d), pb[1] + 14 * math.sin(ang + d))], fill=fill, width=width)

        def roads(s):
            for pl in (rin, rout):
                a = np.array(pl); L = []; Rr = []
                for i in range(len(a)):
                    if not (s.x0 - 12 <= a[i][0] <= s.x1 + 12 and s.z0 - 12 <= a[i][1] <= s.z1 + 12): continue
                    d = a[min(i + 1, len(a) - 1)] - a[max(i - 1, 0)]; d = d / (np.linalg.norm(d) + 1e-9); nrm = np.array([-d[1], d[0]])
                    L.append(tuple(a[i] + nrm * 4)); Rr.append(tuple(a[i] - nrm * 4))
                if len(L) >= 2: s.line(s.cl(L), (120, 100, 60), 2); s.line(s.cl(Rr), (120, 100, 60), 2)

        def cl(s, pts):
            return [(min(max(q[0], s.x0), s.x1), min(max(q[1], s.z0), s.z1)) for q in pts]

        def house(s, t=0.0, color=HOUSE, ghost=False):
            e = rect(EAVE, t); s.line(e + [e[0]], color, 1, dash=5)
            s.poly(rect(PLAT, t), fill=None if ghost else (207, 200, 184), outline=color, width=2 if ghost else 3)
            s.poly(rect(BODY, t), fill=None if ghost else color, outline=color, width=2)

        def nodes(s, names=True, sz=13):
            short = {'Parking': '정차', 'Interaction': '인도·서기', 'CompanionWait': '동행', 'CargoWait': '짐', 'Checkpoint': '쉼 발', 'InspectionDesk252': '검수대', 'capital_escort_rest': '벤치'}
            off = {'Parking': (8, -20), 'Interaction': (-118, -8), 'CompanionWait': (8, -6), 'CargoWait': (-70, -26), 'Checkpoint': (10, -6), 'InspectionDesk252': (-112, 4), 'capital_escort_rest': (8, 4)}
            for k, v in nodes.items():
                if k in ('Guesthouse252', 'CargoClerk252'): continue
                s.dot((v['x'], v['z']), 5 if names else 3)
                if names: s.text((v['x'], v['z']), '%s %.1f' % (short.get(k, k), v['y']), sz=sz, dx=off[k][0], dy=off[k][1])

    dr.text((24, 14), 'L8 화물 인도 정차(성저 객주) — 현황 실측과 선택지 A · B · C · D', font=font(30, True), fill=INK)
    dr.text((24, 56), '[O] height_p1b 4 m 격자(등고선은 그 보간) · 씬 YAML · 배치/콘텐츠 자산 · 메시 m_LocalAABB   [M] 편집기 검증(EXTFIX_VERIFY 7-다) 인용   노란 면 = 평지(106.65 ± 0.10, 경사 ≤ 2°) · 짙을수록 급경사 · 갈색 두 줄 = 길바닥(폭 8 m)', font=font(16), fill=(70, 66, 58))
    # ---------------- panel 1
    P1 = Panel(24, 130, 1735, 1955, 2235, 2465, 5, '① 주변 220 × 230 m — 등고선 1 m(굵은 선 5 m)'); P1.contours(1.0); P1.roads()
    P1.clabels([(1750, 2250), (1760, 2330), (1790, 2420), (1880, 2250), (1930, 2300), (1940, 2400), (1860, 2440), (1905, 2345)])
    for name, q in (('토 ② (1868, 2354)', (1868, 2354)), ('토 ③ (1906, 2390)', (1906, 2390))):
        P1.circle(q, 54, EARTH, 2); P1.circle(q, 16, EARTH, 1, fill=EARTH + (40,)); P1.dot(q, 6, EARTH)
    P1.text((1868, 2354), '토 ② (1868, 2354)', sz=14, fill=EARTH); P1.text((1906, 2390), '토 ③ (1906, 2390)\n안쪽 원 = 탐지 16 m\n바깥 원 = 54 m 규칙', sz=14, fill=EARTH)
    P1.house(); P1.nodes(names=False)
    P1.dot((1924.7, 2448.7), 7, (60, 60, 60)); P1.text((1924.7, 2448.7), '남문 앞 성황당 y 95.9', sz=14, dx=-176, dy=-10)
    P1.dot((1925.5, 2434.5), 5, GREY); P1.text((1925.5, 2434.5), '옹기장수 예정', sz=14, dx=10, dy=-8)
    for t in J.get('trunks', []):
        if t[6] in (0, 1): P1.dot((t[0], t[2]), 3, (59, 93, 42))
    P1.text((1866, 2262), '들어오는 가도 inspection_two__capital_delivery\n폭 8 m · 끝에서 40–110 m 구간 내리막 8–14°', sz=14, dx=0, dy=0); P1.arrow((1890, 2273), (1896, 2301), INK, 2)
    P1.text((1741, 2430), '나가는 가도 capital_delivery__south_gate\n폭 8 m · 남문 (2000, 2530)까지 277 m · 내리막 2–9°', sz=14, dx=0, dy=0); P1.arrow((1840, 2420), (1886, 2386), INK, 2)
    P1.text((1741, 2296), '객주 집 SM_Naeposa × 1.14\n기단 6.0 × 9.1 m · 몸채 3.1 × 4.6 m · 높이 7.6 m\n기단 윗면 103.16 · 밑 지형 %.2f – %.2f' % (R['today']['min'], R['today']['max']), sz=14, fill=HOUSE, dx=0, dy=0); P1.arrow((1792, 2292), (1805, 2305), HOUSE, 2)
    P1.text((1838, 2296), '평지 = 길 끝 다짐면 y 106.65\n약 %d m² · 전부 두 가도의 길바닥' % R['pad']['area_m2'], sz=14, fill=(90, 74, 26), dx=0, dy=0); P1.arrow((1848, 2300), (1832, 2320), (90, 74, 26), 2)
    P1.arrow((1930, 2448), (1950, 2462), INK, 3); P1.text((1741, 2462), '오른쪽 위 = 남문 · 도성 (방위 41°)   초록 점 = 나무 행   반경 200 m 안의 다른 씬 물체 없음 [O]', sz=14, dx=0, dy=0)
    for c in J['clusters']['16x16_le6']:
        b = c['best']
        if 1735 < b['x'] < 1955 and 2235 < b['z'] < 2465:
            P1.poly([(b['x'] - 8, b['z'] - 8), (b['x'] + 8, b['z'] - 8), (b['x'] + 8, b['z'] + 8), (b['x'] - 8, b['z'] + 8)], outline=NEW, width=3)
            P1.text((b['x'] - 8, b['z'] + 8), 'C2 평지 후보 y %.1f\n길 가장자리에서 %.0f m' % (b['y'], b['road_edge_min']), sz=14, fill=NEW, dx=-40, dy=-46)
    # ---------------- panel 2
    P2 = Panel(1160, 130, 1796, 1852, 2296, 2342, 22, '② 오늘 — 등고선 0.5 m(굵은 선 5 m), 노드 옆 숫자 = 발 높이 y'); P2.contours(0.5); P2.roads(); P2.house(); P2.nodes(sz=15)
    P2.clabels([(1800, 2300), (1802, 2336), (1812, 2302), (1830, 2303), (1846, 2304), (1848, 2338), (1834, 2322), (1826, 2338)], sz=15)
    a0 = w(PLAT[1], 0); a1 = w(R['pad']['edge_fwd_m'], 0)
    P2.line([a0, a1], CUT, 4); P2.text((1798, 2327), '집 앞 → 평지 %.1f m (붉은 선)\n비탈 14–18° · 땅 %.1f → 106.6' % (R['pad']['edge_fwd_m'] - PLAT[1], min(R['B']['front_edge_ground'])), sz=15, fill=CUT, dx=0, dy=0)
    P2.text((1798, 2300.5), '뒤 낮은 귀: 지형 %.2f, 기단 %.2f m 드러남 · 집 = 기단 윗면 103.16, 점선 = 처마 7.1 × 10.0 m' % (R['today']['min'], R['today']['base_max']), sz=15, fill=HOUSE, dx=0, dy=-8)
    P2.text((1826, 2317), '들어오는 가도 끝 (1821.2, 2320)', sz=14, dx=0, dy=0); P2.text((1829, 2331.5), '나가는 가도 시작 (1823.8, 2323)', sz=14, dx=0, dy=0)
    P2.text((1826, 2309), '남쪽 가장자리 밖 = 22° 비탈', sz=14, fill=GREY, dx=0, dy=0); P2.text((1798, 2340.5), '서 · 북서 = 20–29° 비탈', sz=14, fill=GREY, dx=0, dy=0)

    # ---------------- option panels
    def opt(i, title):
        P = Panel(24 + i * 594, 1350, 1798, 1836, 2298, 2334, 15, title); P.contours(0.5); P.roads(); return P
    PA = opt(0, 'A 제자리 마당 깎기 — 길보다 3.5 m 낮은 마당')
    yard = [w(PLAT[1], -7), w(R['pad']['edge_fwd_m'], -7), w(R['pad']['edge_fwd_m'], 7), w(PLAT[1], 7)]
    PA.poly(yard, fill=CUT + (70,), outline=CUT, width=3); PA.house(); PA.line([yard[1], yard[2]], CUT, 7)
    PA.text((1799, 2333.3), '마당 103.16 (%.1f × 14 m) · 깎기 %d m³\n길 끝 바로 밑에 석축 %.1f m(굵은 붉은 선)' % (R['pad']['edge_fwd_m'] - PLAT[1], R['A'][0]['cut_m3'], R['A'][0]['max_cut']), sz=14, fill=CUT, dx=0, dy=0)
    PA.text((1799, 2302.4), '수레 경사로 6° %.0f m · 8° %.0f m → 못 내려감\nTerrain_03_04 사본 = 보호 타일 새 예외 · 재굽기' % (R['A_ramp']['len_6'], R['A_ramp']['len_8']), sz=14, dx=0, dy=0)
    PB = opt(1, 'B 제자리에서 +%.2f m 올림' % R['B']['raise_m'])
    gap = [w(PLAT[1], -4.5), w(R['pad']['edge_fwd_m'], -4.5), w(R['pad']['edge_fwd_m'], 4.5), w(PLAT[1], 4.5)]
    PB.poly(gap, fill=CUT + (55,), outline=CUT, width=2); PB.house(color=NEW)
    PB.text((1799, 2333.3), '집 앞 %.1f m는 그대로 비탈(붉은 면):\n기단이 앞 땅보다 %.1f–%.1f m 높이 뜸' % (R['B']['gap_to_pad_m'], PAD_Y - max(R['B']['front_edge_ground']), PAD_Y - min(R['B']['front_edge_ground'])), sz=14, fill=CUT, dx=0, dy=0)
    PB.text((1799, 2302.4), '뒤 석축 %.1f m — 지금 치마 메시보다 %.1f m 더 필요\n(새 석축 메시) · 재굽기' % (R['B']['base_max'], R['B']['skirt_bottom'] - R['B']['min']), sz=14, fill=NEW, dx=0, dy=0)
    c1 = R['C1'][1]
    PC = opt(2, 'C1 평지 끝으로 %.0f m 당겨 +%.2f m 올림  ← 권장' % (c1['slide_m'], c1['raise_m']))
    PC.house(color=GREY, ghost=True); PC.house(t=c1['slide_m'], color=NEW); PC.arrow(w(0, 0), w(c1['slide_m'] - 3.6, 0), NEW, 3)
    PC.dot((1820, 2320), 5); PC.text((1820, 2320), '정차(그대로)', sz=14, dx=8, dy=-8)
    PC.text((1799, 2333.3), '기단 윗면 = 다짐면 106.65 → 수레 옆 하역 단\n정차 자리 ↔ 기단 앞 %.1f m · 사람 노드는 평지 끝 · 기단 앞턱' % c1['parking_to_platform_m'], sz=14, fill=NEW, dx=0, dy=0)
    PC.text((1799, 2302.4), '뒤 석축 %.1f m — 지금 치마 메시(깊이 2.77 m)로 덮임\n지형 수정 없음 · 집 · 노드 이동 → 재굽기' % c1['under']['base_max'], sz=14, dx=0, dy=0)
    PD = opt(3, 'D 지형 그대로 — 돌계단 %d단(%.0f°)' % (R['D']['stair']['risers'], R['D']['stair']['pitch_deg']))
    PD.house()
    for i in range(0, 12):
        f = PLAT[1] + (R['pad']['edge_fwd_m'] - PLAT[1]) * i / 11; PD.line([w(f, -4.5), w(f, 4.5)], (70, 70, 70), 2)
    PD.text((1799, 2333.3), '폭 9 m 계단 · 낙차 %.2f m · 수레는 위에 남음\n집은 여전히 길보다 3.5 m 아래' % R['D']['deck_at_pad']['above_platform'], sz=14, dx=0, dy=0)
    PD.text((1799, 2302.4), '106.65 높이 덱이면 지붕 꼭대기까지 %.1f m뿐(지붕 높이 다리)\n계단 충돌체 = 걷는 땅 위 → 재굽기' % (R['D']['roof_top'] - PAD_Y), sz=14, dx=0, dy=0)
    im.save(OUT / 'delivery_stop_plan.png'); im.resize((W // 2, Hh // 2), Image.LANCZOS).save(OUT / 'delivery_stop_plan_half.png')
    print('picture', OUT / 'delivery_stop_plan.png', im.size)
