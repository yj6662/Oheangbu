#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 3 - the pictures (PIL + numpy software views of the REAL meshes; no GPU, no editor). Called by relayout_fix3_dry.py --pictures.
  Art/Playtest308/Relayout/fix3/delivery_c1_plan.png    plan: old / new house, roads, lanes, nodes, clear widths, the swept car
  Art/Playtest308/Relayout/fix3/delivery_c1_views.png   before | after from the in-road (40 m, 15 m), Parking, the south-gate road, and down the south-gate road
  Art/Playtest308/Relayout/fix3/wreck_l3_views.png      the fallen wreck before | after from the road (15 m, 6 m)
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

import relayout_fix3_lib as L
import relayout_fix3_wreck as WK

INK = (34, 32, 28); PAPER = (244, 239, 228); PADC = (230, 214, 168); OLD = (150, 60, 46); NEW = (31, 95, 139); GREY = (120, 120, 120); LANE = (196, 150, 70); CAR = (70, 90, 60)


def font(sz, bold=False):
    try: return ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf' if bold else 'C:/Windows/Fonts/malgun.ttf', sz)
    except Exception: return ImageFont.load_default()


def band(pl, half):
    """polygon of a polyline widened by half on both sides."""
    left = []; right = []
    for i, p in enumerate(pl):
        a = pl[max(0, i - 1)]; b = pl[min(len(pl) - 1, i + 1)]; dx, dz = b[0] - a[0], b[1] - a[1]; ln = math.hypot(dx, dz) or 1.0; nx, nz = -dz / ln, dx / ln
        left.append((p[0] + nx * half, p[1] + nz * half)); right.append((p[0] - nx * half, p[1] - nz * half))
    return left + right[::-1]


def draw_plan(D, wd, S, path):
    d = D['delivery']; H = d['house']; rules = d['rules']; W = wd.W; st = wd.stops['main']
    rin = W['routes'][H['road']['routes'][0]]; rout = W['routes'][H['road']['routes'][1]]
    X0, X1, Z0, Z1, K = 1798.0, 1848.0, 2294.0, 2340.0, 40
    Wd, Hd = int((X1 - X0) * K), int((Z1 - Z0) * K); TOP = 120; im = Image.new('RGB', (Wd, Hd + TOP + 250), PAPER); dr = ImageDraw.Draw(im, 'RGBA')
    def P(q): return ((q[0] - X0) * K, TOP + (Z1 - q[1]) * K)
    xs = X0 + (np.arange(Wd) + .5) / K; zs = Z1 - (np.arange(Hd) + .5) / K; GX, GZ = np.meshgrid(xs, zs); G = L.hv(GX, GZ)
    gx = np.abs(np.gradient(G, axis=1)) * K; gz = np.abs(np.gradient(G, axis=0)) * K; sl = np.degrees(np.arctan(np.hypot(gx, gz)))
    img = np.zeros((Hd, Wd, 3), 'float32') + np.array(PAPER, 'float32') - np.clip((sl - 4) / 30.0, 0, 1)[..., None] * 34.0
    img[(np.abs(G - S['pad']) <= 0.10) & (sl <= 2.0)] = PADC
    for stp, dark in ((0.5, 26), (2.0, 70)):
        Lv = np.floor(G / stp); e = np.zeros(G.shape, bool); e[:, 1:] |= Lv[:, 1:] != Lv[:, :-1]; e[1:, :] |= Lv[1:, :] != Lv[:-1, :]; img[e] -= dark
    im.paste(Image.fromarray(np.clip(img, 0, 255).astype('uint8')), (0, TOP))
    for pl, nm in ((rin, '들어오는 가도 (폭 8 m)'), (rout, '남문으로 나가는 가도 (폭 8 m)')):
        dr.polygon([P(q) for q in band(pl, 4.0)], outline=(150, 110, 40, 255)); dr.polygon([P(q) for q in band(pl, rules['lane_half_m'])], fill=(196, 150, 70, 60))
        dr.line([P(q) for q in pl], fill=(120, 80, 20, 255), width=2)
    dr.text(P((1834.0, 2321.6)), '들어오는 가도 — 폭 8 m, 가운데 띠 = 자동차 차로(± 1.3 m)', font=font(17), fill=(110, 70, 10))
    dr.text(P((1833.5, 2334.2)), '남문 가도 →', font=font(17), fill=(110, 70, 10))
    for q in S['swept']: dr.polygon([P(c) for c in q], outline=(70, 90, 60, 70))
    dr.polygon([P(c) for c in S['carp']], fill=(70, 90, 60, 150), outline=CAR + (255,)); dr.text(P((S['park'][0] - 3.4, S['park'][1] + 3.1)), '정차 자리 (1820, 2320) — 그대로', font=font(16, True), fill=CAR)
    c = S['turn']['centre']; r = S['turn']['outer'] * K; pc = P(c); dr.ellipse([pc[0] - r, pc[1] - r, pc[0] + r, pc[1] + r], outline=(70, 90, 60, 130))
    hb = st['nodes'][H['node']]; old = (hb['pos'][0], 0, hb['pos'][2], hb['yaw'])
    dr.polygon([P(q) for q in L.rect_poly(old, H['base_rect'])], outline=OLD + (255,), fill=OLD + (40,)); dr.polygon([P(q) for q in L.rect_poly(old, H['body_rect'])], fill=OLD + (120,))
    dr.text(P((1800.0, 2304.6)), '지금: 22° 비탈, 길보다 3.49 m 아래', font=font(17, True), fill=OLD)
    for vn, vv in H['variants'].items():
        if vn == H['variant']: continue
        vp = (vv['xz'][0], 0, vv['xz'][1], vv['yaw']); pts = [P(q) for q in L.rect_poly(vp, H['base_rect'])]; dr.line(pts + [pts[0]], fill=(110, 110, 110, 255), width=2)
        dr.text(P((1805.2, 2322.3)), '선택지 그림의 C1 자리(회색 테):\n기단 밑 0.23 m 뜸 · 길 끝을 0.45 m 침범 → 쓰지 않음', font=font(15), fill=(90, 90, 90))
    pose = S['pose']; dr.polygon([P(q) for q in S['base']], outline=NEW + (255,), fill=NEW + (50,)); dr.polygon([P(q) for q in S['plinth']], outline=NEW + (200,))
    pts = [P(q) for q in S['roof']]; dr.line(pts + [pts[0]], fill=NEW + (150,), width=1)
    dr.polygon([P(q) for q in L.rect_poly(pose, H['body_rect'])], fill=NEW + (190,))
    bx = H['body_rect']; dr.polygon([P(q) for q in L.rect_poly(pose, (bx[0], bx[1], 0.5, bx[3]))], fill=(140, 190, 220, 230), outline=NEW + (255,))
    a = L.to_world(pose, bx[0] + 0.5, 0.5); b = L.to_world(pose, bx[1] - 0.5, 0.5); dr.line([P(a), P(b)], fill=(200, 40, 40, 255), width=5)
    dr.text(P((pose[0] - 9.8, pose[2] - 4.4)), '새 자리: 길 끝 남쪽 가장자리, 기단 윗면 = 다짐면 %.2f\n하늘색 = 트인 마루칸, 붉은 선 = 문(서쪽을 봄)' % S['pad'], font=font(16, True), fill=NEW)
    at = S['at']; lab = {'Interaction': '서기 · 인도 지점', 'InspectionDesk252': '검수대', 'CargoWait': '짐', 'CompanionWait': '동행', 'Checkpoint': '쉼 발', 'capital_escort_rest': '벤치'}
    for nm, p in at.items():
        if nm == 'CargoClerk252': continue
        o = st['nodes'][nm]['pos']; po = P((o[0], o[2])); pn = P(p)
        dr.ellipse([po[0] - 5, po[1] - 5, po[0] + 5, po[1] + 5], outline=OLD + (255,)); dr.line([po, pn], fill=(90, 90, 90, 110), width=1)
        dr.ellipse([pn[0] - 7, pn[1] - 7, pn[0] + 7, pn[1] + 7], fill=INK + (255,)); dr.text((pn[0] + 9, pn[1] + (8 if nm in ('CargoWait', 'capital_escort_rest') else -24)), lab[nm], font=font(15, True), fill=INK)
    # dimensions
    def dim(a, b, text, col=(170, 40, 40)):
        dr.line([P(a), P(b)], fill=col + (255,), width=2); m = P(((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)); dr.text((m[0] + 6, m[1] - 10), text, font=font(15, True), fill=col)
    fz = max(q[1] for q in S['base']); dim((1830.5, fz), (1830.5, 2320.0), '기단 ↔ 길 중심 %.2f m (길바닥 밖 %.2f m)' % (2320.0 - fz, 2320.0 - fz - 4.0))
    dim((1838.0, 2316.0), (1838.0, 2324.0), '길바닥 폭 8.0 m 그대로')
    cb = min(((L.seg_d(c_, a_, b_), c_) for c_ in S['carp'] for a_, b_ in L.poly_edges(S['base'])), key=lambda q: q[0])
    dim(cb[1], (cb[1][0], fz), '차 ↔ 기단 %.2f m' % L.poly_poly_d(S['carp'], S['base']), (40, 110, 60))
    dr.text((20, 14), '화물 인도 정차(성저 객주) — D308-22 답 1 "C1 집을 평지 끝으로 옮김"의 실측 배치', font=font(30, True), fill=INK)
    dr.text((20, 62), '[O] height_p1b 4 m 격자 · 씬 YAML · 집 실제 메시 15개 + 돌 치마 메시.  노란 면 = 다짐면(%.2f ± 0.10, 경사 ≤ 2°, %.0f m²).  등고선 0.5 m(굵은 선 2 m).  붉은 테 = 지금 집, 파란 = 새 집, 빈 원 = 지금 노드, 검은 점 = 새 노드, 초록 = 차와 차가 쓸고 가는 자리(우회전 바깥 반지름 %.2f m)' % (S['pad'], S['flat_area'], S['turn']['outer']), font=font(15), fill=INK)
    y = TOP + Hd + 14
    txt = ['· 집: (%.2f, %.3f, %.2f) yaw %.0f — 평면 %.1f m 이동, %.2f m 올림. 기단 밑(돌 치마 포함)이 사방에서 땅속(가장 덜 묻힌 곳 %.2f m). 기단 앞턱 %.2f – %.2f m.' % (pose[0], pose[1], pose[2], pose[3], math.hypot(pose[0] - hb['pos'][0], pose[2] - hb['pos'][2]), pose[1] - hb['pos'][1], -max(p[6] for p in S['prof']), 0.10, 0.18),
           '· 길: 기단은 들어오는 가도 길바닥 밖 0.33 m, 처마는 0.03 m 밖(처마 밑 3.59 m). 남문 가도와는 3.3 m. 다짐면 칸을 하나도 덮지 않는다 → 두 길 폭 8 m · 회차 여유 그대로.',
           '· 사람 노드 6개: 전부 다짐면 위(경사 0 – 3°), 차로 중심에서 2.4 m 이상, 정차한 차에서 1.1 m 이상. 서기 · 검수대는 마루칸 앞, 벤치는 처마선 밑 창 아래.',
           '· 문은 서쪽 박공(트인 마루칸 안)에 있다. 마당(북쪽)에서는 창 하나와 마루칸 난간이 있는 긴 면이 보인다. 문을 마당으로 돌리면(yaw 0) 10 m 축이 비탈로 내려가 치마 없는 면이 뜬다 → 새 치마 메시 없이는 불가.']
    for i, t in enumerate(txt): dr.text((20, y + i * 30), t, font=font(17), fill=INK)
    im.save(path); return path


def scene_layers(D, wd, S, after, with_car=True):
    d = D['delivery']; H = d['house']; rules = d['rules']; st = wd.stops['main']
    parts, skirt, key = wd.house(d['offline']['house_scale'], d['offline']['house_child_offset'])
    G = L.ground_tris(1770, 1900, 2270, 2380, 2.0); cen = G.mean(axis=1); gy = L.hv(cen[:, 0], cen[:, 2])
    xs, zs, flat = S['flat']; ix = np.clip(((cen[:, 0] - xs[0]) / 0.25).round().astype(int), 0, len(xs) - 1); iz = np.clip(((cen[:, 2] - zs[0]) / 0.25).round().astype(int), 0, len(zs) - 1)
    inwin = (cen[:, 0] >= xs[0]) & (cen[:, 0] <= xs[-1]) & (cen[:, 2] >= zs[0]) & (cen[:, 2] <= zs[-1]); isflat = inwin & flat[iz, ix]
    rin = wd.W['routes'][H['road']['routes'][0]]; rout = wd.W['routes'][H['road']['routes'][1]]
    onroad = np.array([min(min(L.seg_d((c[0], c[2]), a, b) for a, b in zip(pl, pl[1:])) for pl in (rin, rout)) <= 4.0 for c in cen])
    layers = [(G[~isflat & ~onroad], (150, 156, 128), True), (G[isflat | onroad], (206, 188, 144), True)]
    hb = st['nodes'][H['node']]; pose = S['pose'] if after else (hb['pos'][0], hb['pos'][1], hb['pos'][2], hb['yaw'])
    for T, pid in L.house_tris(pose, parts, skirt): layers.append((T, L.PART_COLOURS[pid], True))
    def feet(nm):
        if after: p = S['at'][nm]; return p, L.h(*p)
        o = st['nodes'][nm]['pos']; return (o[0], o[2]), L.h(o[0], o[2])
    p, g = feet('CargoClerk252'); layers.append((L.box_tris(p, (0.5, 1.7, 0.4), 0, g), (60, 70, 110), True))
    p, g = feet('InspectionDesk252'); layers.append((L.box_tris(p, (0.72, 0.77, 0.75), 270 if after else 311.2, g), (120, 84, 50), True))
    p, g = feet('capital_escort_rest'); layers.append((L.box_tris(p, (0.5, 0.46, 1.3), 90 if after else 0, g), (120, 84, 50), True))
    if with_car: layers.append((L.box_tris(S['park'], (1.8, 2.6, 3.0), S['park_yaw'], L.h(*S['park']) + 0.5), (70, 90, 60), True))
    return layers


def draw_views(D, wd, S, path):
    d = D['delivery']; H = d['house']; pic = d['offline']['pictures']; eh = pic['eye_height_m']; W_, H_ = 800, 450
    rows = []
    hc = (S['pose'][0], S['pose'][1] + 2.2, S['pose'][2])
    for e in pic['eyes']:
        if e.get('route'):
            pl = wd.W['routes'][e['route']]; ln = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pl, pl[1:]))
            s = ln - e['from_end_m'] if 'from_end_m' in e else e['from_start_m']; acc = 0.0; xz = pl[-1]
            for a, b in zip(pl, pl[1:]):
                sg = math.hypot(b[0] - a[0], b[1] - a[1])
                if acc + sg >= s: t = (s - acc) / sg; xz = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t); break
                acc += sg
        else: xz = tuple(e['eye'])
        eye = (xz[0], L.h(*xz) + eh, xz[1])
        if e.get('target_route'):
            pl = wd.W['routes'][e['target_route']]; acc = 0.0; t_xz = pl[-1]
            for a, b in zip(pl, pl[1:]):
                sg = math.hypot(b[0] - a[0], b[1] - a[1])
                if acc + sg >= e['target_from_start_m']: t = (e['target_from_start_m'] - acc) / sg; t_xz = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t); break
                acc += sg
            tgt = (t_xz[0], L.h(*t_xz) + 1.0, t_xz[1])
        else: tgt = hc
        rows.append((e['id'], eye, tgt, e['fov']))
    im = Image.new('RGB', (W_ * 2 + 30, 70 + (H_ + 34) * len(rows)), PAPER); dr = ImageDraw.Draw(im)
    dr.text((10, 8), '인도 정차 — 지금 | 옮긴 뒤 (소프트웨어 뷰: 실제 집 메시 · 돌 치마 · height_p1b 지형, 눈높이 1.6 m; 재질 · 풀 · 나무 · 조명 없음)', font=font(20, True), fill=INK)
    title = {'in_road_40m': '들어오는 가도, 길 끝 40 m 앞', 'in_road_15m': '들어오는 가도, 길 끝 15 m 앞', 'parking': '정차 자리에서 집 쪽', 'south_gate_road_back': '남문 가도 24 m 지점에서 뒤돌아봄', 'in_road_15m_down_south_gate_road': '길 끝 15 m 앞에서 남문 가도 쪽을 봄(집이 시야를 막는가)'}
    stats = {}
    for i, (rid, eye, tgt, fov) in enumerate(rows):
        for j, after in enumerate((False, True)):
            layers = scene_layers(D, wd, S, after, with_car=(rid != 'parking'))
            img, zb = L.render(layers, eye, tgt, fov, W_, H_)
            # share of the frame the house takes (pixels whose colour came from a house layer cannot be told apart here: re-render the house alone)
            himg, hz = L.render(layers[2:18], eye, tgt, fov, W_, H_); vis = (hz < np.inf) & (hz <= zb + 1e-6); stats[(rid, after)] = float(vis.mean())
            im.paste(Image.fromarray(img), (10 + j * (W_ + 10), 60 + i * (H_ + 34) + 24))
            dr.text((10 + j * (W_ + 10), 60 + i * (H_ + 34)), '%s — %s · 눈 (%.0f, %.1f, %.0f) · 집이 화면의 %.1f %%' % (title.get(rid, rid), '옮긴 뒤' if after else '지금', eye[0], eye[1], eye[2], 100 * stats[(rid, after)]), font=font(15, True), fill=NEW if after else OLD)
    im.save(path); return path, stats


def draw_wreck(D, wd, WR, path):
    w0, w1, root, road = WR['before'], WR['after'], WR['root'], WR['road']
    G = L.ground_tris(root[0] - 30, root[0] + 30, root[2] - 30, root[2] + 30, 1.0); cen = G.mean(axis=1)
    onroad = np.array([min(L.seg_d((c[0], c[2]), a, b) for a, b in zip(road, road[1:])) <= 4.0 for c in cen])
    col = {'Cabin': (150, 60, 50), 'Roof': (70, 72, 78), 'Wheel': (110, 84, 56), 'DetachedWheel': (110, 84, 56), 'SM_M_WoodenBox': (140, 110, 70)}
    def layers(w):
        out = [(G[~onroad], (150, 156, 128), True), (G[onroad], (206, 188, 144), True)]
        for k in w.order:
            m = w.mesh_of(k)
            if m is None: continue
            Mx = w.world(k); Wv = m[0] @ Mx[:3, :3].T + Mx[:3, 3]; out.append((Wv[m[1]], col.get(w.nodes[k]['name'], (96, 80, 60)), True))
        return out
    near = min(((L.seg_d((root[0], root[2]), a, b), a, b) for a, b in zip(road, road[1:])), key=lambda q: q[0]); a, b = near[1], near[2]
    t = max(0.0, min(1.0, ((root[0] - a[0]) * (b[0] - a[0]) + (root[2] - a[1]) * (b[1] - a[1])) / ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2))); foot = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
    along = ((b[0] - a[0]) / math.hypot(b[0] - a[0], b[1] - a[1]), (b[1] - a[1]) / math.hypot(b[0] - a[0], b[1] - a[1])); off = math.sqrt(max(0.0, 15.0 ** 2 - near[0] ** 2))
    e15 = (foot[0] - along[0] * off, foot[1] - along[1] * off); u = ((foot[0] - root[0]) / near[0], (foot[1] - root[2]) / near[0]); e6 = (root[0] + u[0] * 6.0, root[2] + u[1] * 6.0)
    W_, H_ = 800, 450; im = Image.new('RGB', (W_ * 2 + 30, 70 + (H_ + 34) * 2), PAPER); dr = ImageDraw.Draw(im)
    dr.text((10, 8), '가마 잔해(L3) — 지금 | 수리 뒤 (소프트웨어 뷰: 실제 가마 FBX 메시, 눈높이 1.6 m; 재질 · 풀 없음)', font=font(20, True), fill=INK)
    for i, (nm, e, fov) in enumerate((('길 위 15 m (가도 중심선)', e15, 50), ('길 가장자리 6 m', e6, 60))):
        eye = (e[0], L.h(*e) + 1.6, e[1]); tgt = (root[0], L.h(root[0], root[2]) + 0.9, root[2] + 0.8)
        for j, w in enumerate((w0, w1)):
            img, _ = L.render(layers(w), eye, tgt, fov, W_, H_)
            im.paste(Image.fromarray(img), (10 + j * (W_ + 10), 60 + i * (H_ + 34) + 24))
            dr.text((10 + j * (W_ + 10), 60 + i * (H_ + 34)), '%s — %s · 눈 (%.1f, %.1f, %.1f)' % (nm, '수리 뒤(fix2 자세 + 지붕 · 바퀴 떼어 놓음)' if j else '지금', *eye), font=font(15, True), fill=NEW if j else OLD)
    im.save(path); return path


def draw_all(D, wd, S, WR):
    L.OUT.mkdir(parents=True, exist_ok=True); out = []
    out.append(draw_plan(D, wd, S, L.OUT / 'delivery_c1_plan.png'))
    p, stats = draw_views(D, wd, S, L.OUT / 'delivery_c1_views.png'); out.append(p)
    (L.OUT / 'delivery_c1_views.txt').write_text('\n'.join('%s %s house share of the frame %.2f %%' % (k[0], 'after' if k[1] else 'before', 100 * v) for k, v in stats.items()), encoding='utf-8')
    out.append(draw_wreck(D, wd, WR, L.OUT / 'wreck_l3_views.png'))
    return out
