# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 - the finalize pass (2026-10-04): the comparison image of what the user was shown and what the game
bundle now draws.

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_finalize.py         -> Art/UI308/Map/map308_finalize_compare.png, map308_finalize.json, bvalues308.json
    python Tools/Art/map308_finalize.py --bvalues   only bvalues308.json (no rendering)

Three columns, same place and scale:

    before finalize | after finalize | the direction sheet the user saw

for the unfolded map (partly explored: the sheet's panel (f) state), one partly explored minimap window (the boundary rim), one
fully explored minimap window, and the current-position mark and the hall sign at 100 % and 300 %.

Each column is drawn by ITS OWN code in its own process, composed the same way (the direction sheet's page: realm names,
place plates, marks): "before" = the tools and the bundle backed up in Art/UI308/Map/_before_finalize/ (the bundle after
the alignment pass: brush wedge, the #307 soft edge, the columned hall); "the sheet" = the tools backed up in
Art/UI308/Map/_before_align/ (the private drawing the user was shown: handled brush, crisp ink rim, two-storey hall);
"after" = today's tools and the stage bundle. Offline, numpy + PIL, nothing under Oheangbu/ is written.
"""
import json, shutil, subprocess, sys, tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / 'Tools/Art'
OUT = ROOT / 'Art/UI308/Map'
BEFORE = OUT / '_before_finalize'            # the bundle + tools as they were before this pass
SHEET = OUT / '_before_align'                # the tools that drew the sheet the user was shown
OLD_TOOLS = ROOT / 'Tools/_map308_before_tools'   # a temporary copy two levels under the root (the old library finds the project from its own place)
FONT = ROOT / 'Oheangbu/Assets/_Project/Art/UI/UI304/Fonts'
F_VIEW = (3000.0, 2180.0)                    # the sheet's panel (f): view centre, wake inn
F_WAKE = (3070.0, 2330.0)
F_HEADING = 120.0
WIN2 = dict(at=(2740.0, 2640.0), heading=30.0)   # ridge-and-forest window, all walked (the alignment pass's second window)
MARK_HEADING = 30.0
PAD = 12                                     # every minimap patch is shown with this margin round its 312 x 228 frame


def u8(a):
    return np.round(np.clip(np.asarray(a, float), 0, 1) * 255).astype(np.uint8)


def views(MK, kind):
    """the pictures of one column through a direction-sheet module MK (old or new). kind: 'bundle' = MK draws the notation
    from a bundle (before / after), 'sheet' = MK's private drawing (the sheet the user saw)."""
    world = MK.World.get('base'); out = {}
    parts = MK.walked_route(world); fog = MK.fog_of(world, parts); found = MK.found_on(world, parts)
    D, _ = MK.B.resample(parts[-2], 2.0); here = tuple(D[-1])
    v = MK.View(F_VIEW[0], F_VIEW[1], MK.MPP_FULL, 740, 760, 2)
    img = MK.render_new(v, world, fog)[0].out().copy()
    MK.map_overlays(img, v, world, found, F_WAKE, here, F_HEADING)
    out['full'] = img

    def patch(p):
        bg = np.ones(p.shape[:2] + (3,)) * 0.165
        MK.over(bg, p, 0, 0)
        P = MK.FRAME_PAD; H, W = 228 + 2 * PAD, 312 + 2 * PAD
        o = np.ones((H, W, 3)) * 0.165
        if P >= PAD: o[:] = bg[P - PAD:P - PAD + H, P - PAD:P - PAD + W]
        else: o[PAD - P:PAD - P + bg.shape[0], PAD - P:PAD - P + bg.shape[1]] = bg
        return o
    sc = MK.mini_scene(world)
    out['mini1'] = patch(MK.mini_new(world, *sc['here'], sc['heading'], sc['fog'], sc['found'])[0])
    out['mini2'] = patch(MK.mini_new(world, *WIN2['at'], WIN2['heading'], MK.Fog().all(), set())[0])

    def chip(src, size):
        a = np.ones((size + 8, size + 8, 3)) * MK.PAPER
        MK.over(a, src, 4, 4)
        return a
    for px in (28, 48):
        out[f'mark{px}'] = chip(MK.player_mark(px, MARK_HEADING), px)
        out[f'mark{px}_up'] = chip(MK.player_mark(px, 0.0), px)
    for px in (22, 30):
        out[f'hall{px}'] = chip(MK.icon('hall', px), px)
    return out, dict(here=[float(sc['here'][0]), float(sc['here'][1])], heading=sc['heading'])


def worker(which, tmp):
    """runs with an OLD tools folder first on sys.path."""
    sys.path.insert(0, str(TOOLS)); sys.path.insert(0, str(OLD_TOOLS))
    import map308_lib as ML, map308_mock as MK
    assert Path(MK.__file__).resolve().parent == OLD_TOOLS.resolve(), MK.__file__
    if which == 'before':
        ML.OUT = BEFORE / 'bundle'               # the old sheet code reads "the stage bundle": point it at the backed-up one
    pics, _ = views(MK, 'bundle' if which == 'before' else 'sheet')
    for k, v in pics.items(): Image.fromarray(u8(v)).save(Path(tmp) / f'{which}_{k}.png')


def font(name, px):
    return ImageFont.truetype(str(FONT / name), px)


def write_bvalues():
    """Art/UI308/Map/bvalues308.json: the contrast table of the stage README, from the notation values (computed, not measured on screen)."""
    leg = json.loads((OUT / 'map308_legibility.json').read_text(encoding='utf-8')); v = leg['values']; e = leg['walked_edge']
    note = json.loads((ROOT / 'Tools/Unity/Stage308_map/_ProjectAssets/Art/UI/UI308/Map/map308_notation.json').read_text(encoding='utf-8'))
    ridge = next(c for c in note['strokeClasses'] if c['class'] == 2)['ranks'][0]['inkAlpha']
    bv = {'walked paper / unwalked wash': v['walked_vs_unwalked']['ratio'], 'darkest slope wash / unwalked wash': v['darkest_wash_vs_unwalked']['ratio'],
          'great road ink / its paper band': v['road_daero_vs_paper']['ratio'], 'trail ink / its paper band': v['road_soro_vs_paper']['ratio'],
          'great road ink / darkest slope wash': v['road_daero_vs_darkest_wash']['ratio'],
          'ridge ink (rank 1, alpha %.2f) / darkest slope wash' % ridge: v['ridge_ink_vs_wash']['ratio'],
          'ink line over the unwalked wash / that wash': v['ink_line_vs_unwalked']['ratio'], 'cinnabar / paper': v['cinnabar_vs_paper']['ratio'],
          'walked-edge rim %s / unwalked wash' % e['rim_colour']: e['rim_vs_unwalked'], 'walked-edge rim %s / paper' % e['rim_colour']: e['rim_vs_paper'],
          'note': 'finalize pass 2026-10-04: sRGB-coded composites of the notation values (map308_legibility.json values, walked_edge); not measured on screen. '
                  'The walked edge is the crisp #308 edge (MapFog308_Edge); its rim = ink alpha %.2f over the unwalked wash, %g px inside the edge.' % (
                      note['values']['fogEdge']['inkAlpha'], note['values']['fogEdge']['px'])}
    (OUT / 'bvalues308.json').write_bytes((json.dumps(bv, ensure_ascii=False, indent=1) + chr(10)).encode('utf-8'))
    return bv


def main():
    if '--worker' in sys.argv:
        i = sys.argv.index('--worker'); worker(sys.argv[i + 1], sys.argv[i + 2]); return
    if '--bvalues' in sys.argv:                  # only the contrast table (no rendering)
        sys.stdout.reconfigure(encoding='utf-8'); print(json.dumps(write_bvalues(), ensure_ascii=False, indent=1)); return
    sys.stdout.reconfigure(encoding='utf-8')
    sys.path.insert(0, str(TOOLS))
    import map308_lib as M, map308_twin as TW, map308_mock as MK
    tmp = Path(tempfile.mkdtemp(prefix='map308_finalize_'))
    for which, src in (('before', BEFORE / 'tools'), ('sheet', SHEET / 'tools')):
        shutil.rmtree(OLD_TOOLS, ignore_errors=True); shutil.copytree(src, OLD_TOOLS)
        try:
            subprocess.run([sys.executable, str(Path(__file__).resolve()), '--worker', which, str(tmp)], check=True)
        finally:
            shutil.rmtree(OLD_TOOLS, ignore_errors=True)
    after, scene = views(MK, 'bundle')
    load = lambda n: np.asarray(Image.open(tmp / n).convert('RGB'), float) / 255.0
    keys = list(after)
    cols = [('고치기 전 (시안 맞춤 뒤 묶음: 붓 쐐기 · #307 부드러운 가장자리 · 기둥 전각)', {k: load(f'before_{k}.png') for k in keys}),
            ('고친 뒤 (게임이 읽는 묶음 — 스테이지 폴더)', after),
            ('사용자가 본 방향 시안 (맞추기 전의 따로 그린 그림, _before_align)', {k: load(f'sheet_{k}.png') for k in keys})]
    rows = [('full', 1, '펼친 지도 — 일부만 걸은 상태(시안 (바)의 자리: 중심 x %.0f · z %.0f, 2.66 m/px, 740×760 px), 100%%' % F_VIEW),
            ('mini1', 2, '미니맵 창 1 — 시안 (나)의 자리(x %.0f · z %.0f, 336×240 m), 일부만 걸은 상태: 경계 먹 테, 200%%' % tuple(scene['here'])),
            ('mini2', 2, '미니맵 창 2 — 산등성이와 숲(x %.0f · z %.0f), 전부 걸은 상태, 200%%' % WIN2['at'])]
    pad, capf, headf, smallf = 26, font('NotoSansKR-Bold.ttf', 24), font('NotoSerifKR-800.ttf', 34), font('NotoSansKR-Regular.ttf', 19)
    cw = 760
    W = pad + 3 * (cw + pad)
    hs = [max(c[1][key].shape[0] for c in cols) * z for key, z, _ in rows]
    mark_h = 8 + (48 + 8) * 3; hall_h = 8 + (30 + 8) * 3
    H = 150 + sum(h + 92 for h in hs) + 34 + mark_h + 10 + hall_h + 60 + 150
    sheet = Image.new('RGB', (W, H), (0x26, 0x26, 0x23)); d = ImageDraw.Draw(sheet)
    d.text((pad, 20), '오행부 #308 지도 — 마무리: 사용자가 본 시안과 게임 묶음의 남은 차이 넷을 시안 쪽으로 (같은 자리 · 같은 배율)', font=headf, fill=(0xE6, 0xE2, 0xD7))
    d.text((pad, 70), '세 열 모두 방향 시안의 조판(권역 이름 · 이름표 · 표식)으로 그렸다. 전부 오프라인 합성이다(편집기 · Play 미실행). 가운데 열 = 지금 스테이지 폴더의 묶음을 쌍둥이가 그린 것.',
           font=smallf, fill=(0xA7, 0xA3, 0x98))
    for i, (name, _) in enumerate(cols):
        d.text((pad + i * (cw + pad), 108), name, font=capf, fill=(0xE6, 0xE2, 0xD7) if i != 1 else (0xE0, 0x6A, 0x58))
    y = 150
    for (key, z, cap), h in zip(rows, hs):
        d.text((pad, y), cap, font=smallf, fill=(0xD6, 0xD1, 0xC4)); y += 34
        for i, (_, pics) in enumerate(cols):
            im = Image.fromarray(u8(pics[key]))
            if z != 1: im = im.resize((im.size[0] * z, im.size[1] * z), Image.NEAREST)
            sheet.paste(im, (pad + i * (cw + pad) + max(0, (cw - im.size[0]) // 2), y))
        y += h + 58
    d.text((pad, y), '현재 위치 표식(28 px 미니맵 · 48 px 펼친 지도, 방위 %.0f°와 0°)과 전각 기호(22 · 30 px): 100%% 와 300%%' % MARK_HEADING, font=smallf, fill=(0xD6, 0xD1, 0xC4)); y += 34
    for i, (_, pics) in enumerate(cols):
        for line, names, lh in ((0, ('mark28', 'mark28_up', 'mark48', 'mark48_up'), mark_h), (1, ('hall22', 'hall30'), hall_h)):
            x = pad + i * (cw + pad); y0 = y + (0 if line == 0 else mark_h + 10)
            for k in names:
                im = Image.fromarray(u8(pics[k]))
                sheet.paste(im, (x, y0 + (lh - im.size[1]) // 2)); x += im.size[0] + 6
                if k.endswith('_up'): x += 8; continue                    # the second heading: 100 % only
                big = im.resize((im.size[0] * 3, im.size[1] * 3), Image.NEAREST)
                sheet.paste(big, (x, y0 + (lh - big.size[1]) // 2)); x += big.size[0] + 14
    y += mark_h + 10 + hall_h + 26
    # ---- numbers (before / after)
    Bn = TW.Bundle(M.OUT)
    before_roads = OUT / 'map308_finalize_before_roads.json'       # the old bundle's footpaths, read the new way (every road window)
    if not before_roads.exists():
        before_roads.write_bytes((json.dumps(TW.road_centreline_all(TW.Bundle(BEFORE / 'bundle')), ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    leg = json.loads((OUT / 'map308_legibility.json').read_text(encoding='utf-8'))
    leg_b = json.loads((BEFORE / 'twin/map308_legibility.json').read_text(encoding='utf-8'))
    ic = leg['icons']; pm = ic['player_mark_at_28']; sh = ic['confusable_pairs_at_22px']['shift_1px_in_use']; pl = ic['confusable_pairs_at_22px']['plain']
    ra = leg['road_centre_line_all']; e = leg['walked_edge']
    res = dict(id='map308_finalize', spec='SPEC-MAP-OVERHAUL-308', note='before = Art/UI308/Map/_before_finalize (the bundle after the alignment pass), after = the stage bundle',
               position_mark=dict(before=dict(shape='brush wedge, one ink', ink_px_at_28=leg_b['icons'].get('player_ink_px_at_28')), after=pm, at_22=ic['player_mark_at_22']),
               walked_edge=dict(before='the #307 soft edge (MapFog308_Known) + a rim on its half-way line, alpha .45', after=e, notation=Bn.note['values']['fogEdge']),
               hall=dict(before='one wide roof on two (S) / four (L) columns and a base', after='two-storey roof, closed body with door slits, terrace', pairs=sh.get('hall')),
               icon_pairs=dict(plain=dict(before=leg_b['icons']['confusable_pairs_at_22px']['plain'], after=pl), shift_1px_in_use=sh,
                               blur_0_8px=dict(before=leg_b['icons']['confusable_pairs_at_22px']['blur_0_8px'], after=ic['confusable_pairs_at_22px']['blur_0_8px'])),
               footpath=dict(six_windows=dict(before=leg_b.get('road_centre_line'), after=leg.get('road_centre_line')), every_road_window_after=ra,
                             every_road_window_before=json.loads((OUT / 'map308_finalize_before_roads.json').read_text(encoding='utf-8')) if (OUT / 'map308_finalize_before_roads.json').exists() else None),
               frames=Bn.note['frames'], bundle_sha256={o['file']: o['sha256'] for o in Bn.note['outputs']},
               bundles={s: (json.loads((M.bundle_dir(s) / 'map308_notation.json').read_text(encoding='utf-8'))['outputs'] if (M.bundle_dir(s) / 'map308_notation.json').exists() else None) for s in sorted(M.STAGES)})
    (OUT / 'map308_finalize.json').write_bytes((json.dumps(res, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    write_bvalues()
    rb = res['footpath']['every_road_window_before'] or {}
    g = lambda dct, *ks: (dct or {}).get(ks[0], {}).get(ks[1], {}).get('share_ge_4_5') if len(ks) == 2 else None
    pc = lambda v: '—' if v is None else '%.1f%%' % (v * 100)
    lines = [
        '현재 위치: 붓 쐐기(먹 %s px, 한 색) → 자루 달린 붓(주사 촉 + 먹 가락지 · 자루 · 실테, 두 먹 층). 28 px 칸에서 그림 %d×%d px(판 포함 %d×%d), 길이 ÷ 폭 %.2f, 촉 끝 %s px, 둘째 먹 %d px.' % (
            '×'.join(str(v) for v in (leg_b['icons'].get('player_ink_px_at_28') or [])), pm['figure_px'][0], pm['figure_px'][1], pm['with_plate_px'][0], pm['with_plate_px'][1], pm['length_to_width'],
            '·'.join(str(v) for v in pm['tip_width_px_rows_1_to_3']), pm['second_ink_px']),
        '걸은 땅 경계: #307 부드러운 가장자리 + 중간선 먹 테 → 또렷한 경계(1 px 다듬음) + 그 안쪽 %.0f px 먹 테(%s, 씻김 대비 %.2f:1 · 종이 대비 %.2f:1). 경계는 걸은 칸 안쪽 %.1f–%.1f m(중앙값 %.1f m), 안 걸은 칸 위의 걸은 값 %d px / %s px.' % (
            Bn.note['values']['fogEdge']['px'], e['rim_colour'], e['rim_vs_unwalked'], e['rim_vs_paper'], e['edge_inset_m']['min'], e['edge_inset_m']['max'], e['edge_inset_m']['median'],
            e['walked_value_on_an_unwalked_cell_px'], format(e['px_checked'], ',')),
        '전각: 넓은 지붕 + 기둥 → 중층 지붕 + 문짝 낸 몸 + 월대. 22 px 먹 상관 — 장소 기호 210쌍 중 .70 초과 %d쌍(최대 %.3f %s–%s), 쓰는 기호 %d종을 ±1 px 밀어 %d쌍(최대 %.3f %s–%s), 전각의 가장 닮은 쌍 %s.' % (
            pl['above_0_70'], pl['max'], pl['max_pair'][0], pl['max_pair'][1], sh['glyphs'], sh['above_0_70'], sh['max'], sh['max_pair'][0], sh['max_pair'][1],
            ' · '.join('%s %.3f' % (b, r) for r, b in sh['hall'][:3])),
        '샛길 중심선 4.5:1 이상(길 창 전부, 전 → 뒤): 미니맵 1080p %s → %s · 1440p %s → %s(%d창), 펼친 지도 기본 배율 %s → %s(%d창). 큰길 · 길은 %s 이상.' % (
            pc(g(rb, 'minimap_1080p', 'soro')), pc(g(ra, 'minimap_1080p', 'soro')), pc(g(rb, 'minimap_1440p', 'soro')), pc(g(ra, 'minimap_1440p', 'soro')), ra['minimap_1080p']['windows'],
            pc(g(rb, 'full_map_Z2', 'soro')), pc(g(ra, 'full_map_Z2', 'soro')), ra['full_map_Z2']['windows'],
            pc(min(ra[t][c]['share_ge_4_5'] for t in ('minimap_1080p', 'minimap_1440p', 'full_map_Z2') for c in ('daero', 'gil')))),
        '틀: 테마 키트 칸을 이름으로 읽고 Unity처럼 아래 왼쪽부터 깐다 — 미니맵 312×228 = 48 + 12n, 칠반 836×910 = (80 + 9n) × (82 + 9m), 북쪽 조각 16×18(끝이 틀 위로 11 px).']
    for i, ln in enumerate(lines):
        d.text((pad, y + i * 30), ln, font=smallf, fill=(0xD6, 0xD1, 0xC4))
    sheet.save(OUT / 'map308_finalize_compare.png', optimize=True)
    print('wrote', OUT / 'map308_finalize_compare.png', sheet.size)
    for ln in lines: print(ln)
    shutil.rmtree(tmp, ignore_errors=True)


if __name__ == '__main__':
    main()
