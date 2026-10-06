# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 - alignment of the bundle with the direction sheet: the comparison image and the before / after numbers.

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_align.py            -> Art/UI308/Map/map308_align_compare.png, map308_align.json, bvalues308.json

For the unfolded map (the view of today's capture, all revealed) and two minimap windows (the direction sheet's panel (b)
scene, partly walked; a ridge-and-forest window, all walked) it lays four pictures side by side, same place and scale:

    today | bundle before | bundle after | direction sheet (as shown to the user)

"before" = the bundle, the twin and the direction sheet as they were backed up in Art/UI308/Map/_before_align/ (bundle/,
tools/): they are rendered by THEIR OWN code in a second process (--before-worker), so the old pictures are exactly the old
notation. "after" = the stage bundle through today's map308_twin. Offline, numpy + PIL, nothing under Oheangbu/ is written.
"""
import json, subprocess, sys, tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / 'Tools/Art'
OUT = ROOT / 'Art/UI308/Map'
BEFORE = OUT / '_before_align'
A_CENTRE, A_WAKE, HEADING = (2747.0, 2155.0), (3247.0, 1892.0), 22.0      # the view of today's capture (map_revealed.png)
MPP_FULL = 1970.0 / 740.0
CAP_FULL = ROOT / 'Art/UI304/after3/map_revealed.png'
WIN2 = dict(at=(2740.0, 2640.0), heading=30.0, ko='산등성이와 숲 (x 2740 · z 2640), 전부 걸은 상태')
FONT = ROOT / 'Oheangbu/Assets/_Project/Art/UI/UI304/Fonts'


def u8(a):
    return np.round(np.clip(np.asarray(a, float), 0, 1) * 255).astype(np.uint8)


def twin_views(TW, M, B, scene):
    """the bundle through a twin module (old or new): full view + the two minimap windows. -> dict name -> RGB float"""
    mp = M.parse_map(); out = {}
    walked_all = np.ones((M.FOG_H, M.FOG_W), bool)
    glyph = lambda m: dict(m, glyph=B.note['markerGlyphs'].get(m['id'], 'place'))
    # full map: the capture's view, all revealed; the capture shows the village and the wake inn
    V = TW.View(A_CENTRE[0], A_CENTRE[1], 740, 760, MPP_FULL, 1.0); L = {}
    img = TW.render(B, V, walked_all, layers=L)
    marks = [glyph(m) for m in mp['markers'] if m['id'] == 'village'] + [dict(id='wake', label='깨어날 곳', x=A_WAKE[0], z=A_WAKE[1], glyph='inn')]
    TW.draw_marks(B, img, V, marks, 30, player=dict(x=A_CENTRE[0], z=A_CENTRE[1], heading=HEADING), player_px=48, wake='wake', margin=10,
                  labels=dict(size=21, road_ink=L['road_ink']))
    out['full'] = img

    def mini(at, heading, walked, found):
        F = B.note['frames']['minimap']; ww, wh = F['window']; f = F['frameWidth']
        Vm = TW.View(at[0], at[1], ww, wh, 240.0 / wh, 1.0)
        im = TW.render(B, Vm, walked)
        TW.draw_marks(B, im, Vm, [glyph(m) for m in mp['markers'] if m['id'] in found], B.note['sizes']['minimap']['icon'],
                      player=dict(x=at[0], z=at[1], heading=heading), player_px=B.note['sizes']['minimap']['player'], wake=None,
                      margin=B.note['sizes']['minimap']['edgeMargin'])
        o = np.ones((wh + 2 * f, ww + 2 * f, 3)) * M.hexc('#110F0D'); o[f:f + wh, f:f + ww] = im
        K = TW.kit()
        if K.ok:
            TW.over_rgba(o, K.frame('Frame.Mini', F['outer'][0], F['outer'][1], 1.0), 0, 0)
            npc = K.simple('Piece.North', 1.0)
            TW.over_rgba(o, npc, int(round(o.shape[1] / 2 - npc.shape[1] / 2)), int(round(F['northCentreFromTop'] - npc.shape[0] / 2)))
        return o
    out['mini1'] = mini(scene['here'], scene['heading'], scene['walked'], scene['found'])
    out['mini2'] = mini(WIN2['at'], WIN2['heading'], walked_all, set())
    return out


def mock_scene(MK):
    """the direction sheet's own minimap scene (panel b): where the player stands, the walked cells, the found markers."""
    world = MK.World.get('base'); sc = MK.mini_scene(world)
    return world, sc, dict(here=sc['here'], heading=sc['heading'], walked=sc['fog'].g.copy(), found=set(sc['found']))


def sheet_views(MK, world, sc):
    """the direction sheet's pictures of the same three views (its own code: before = private drawing, after = the bundle)."""
    out = {}
    v = MK.View(A_CENTRE[0], A_CENTRE[1], MK.MPP_FULL, 740, 760, 2)
    img = MK.render_new(v, world)[0].out().copy()
    MK.map_overlays(img, v, world, {'village'}, A_WAKE, A_CENTRE, HEADING)
    out['full'] = img

    def patch(p):
        bg = np.ones(p.shape[:2] + (3,)) * 0.066
        MK.over(bg, p, 0, 0)
        P = MK.FRAME_PAD
        return bg[P:bg.shape[0] - P, P:bg.shape[1] - P]
    out['mini1'] = patch(MK.mini_new(world, *sc['here'], sc['heading'], sc['fog'], sc['found'])[0])
    out['mini2'] = patch(MK.mini_new(world, *WIN2['at'], WIN2['heading'], MK.Fog().all(), set())[0])
    return out


OLD_TOOLS = ROOT / 'Tools/_map308_before_tools'      # a temporary copy of _before_align/tools two levels under the root (the old
                                                     # library finds the project from its own place), removed when the run ends


def before_worker(tmp):
    """runs with the OLD tools first on sys.path: the old twin, the old sheet, the old bundle."""
    sys.path.insert(0, str(TOOLS)); sys.path.insert(0, str(OLD_TOOLS))
    import map308_lib as M, map308_twin as TW, map308_mock as MK
    assert Path(TW.__file__).resolve().parent == OLD_TOOLS.resolve() and Path(MK.__file__).resolve().parent == OLD_TOOLS.resolve(), TW.__file__
    world, sc, scene = mock_scene(MK)
    B = TW.Bundle(BEFORE / 'bundle')
    for k, v in twin_views(TW, M, B, scene).items(): Image.fromarray(u8(v)).save(Path(tmp) / f'bundle_{k}.png')
    for k, v in sheet_views(MK, world, sc).items(): Image.fromarray(u8(v)).save(Path(tmp) / f'sheet_{k}.png')
    np.save(Path(tmp) / 'walked.npy', scene['walked'])
    (Path(tmp) / 'scene.json').write_text(json.dumps(dict(here=list(scene['here']), heading=scene['heading'], found=sorted(scene['found']))), encoding='utf-8')


def font(name, px):
    return ImageFont.truetype(str(FONT / name), px)


def main():
    if '--before-worker' in sys.argv:
        before_worker(sys.argv[sys.argv.index('--before-worker') + 1]); return
    sys.stdout.reconfigure(encoding='utf-8')
    sys.path.insert(0, str(TOOLS))
    import map308_lib as M, map308_twin as TW, map308_mock as MK
    import shutil
    tmp = Path(tempfile.mkdtemp(prefix='map308_align_'))
    shutil.rmtree(OLD_TOOLS, ignore_errors=True); shutil.copytree(BEFORE / 'tools', OLD_TOOLS)
    try:
        subprocess.run([sys.executable, str(Path(__file__).resolve()), '--before-worker', str(tmp)], check=True)
    finally:
        shutil.rmtree(OLD_TOOLS, ignore_errors=True)
    load = lambda n: np.asarray(Image.open(tmp / n).convert('RGB'), float) / 255.0
    sj = json.loads((tmp / 'scene.json').read_text(encoding='utf-8'))
    scene = dict(here=tuple(sj['here']), heading=sj['heading'], walked=np.load(tmp / 'walked.npy'), found=set(sj['found']))
    Bn = TW.Bundle(M.OUT); Bo = TW.Bundle(BEFORE / 'bundle')
    after = twin_views(TW, M, Bn, scene)
    world = MK.World.get('base')
    fog = MK.Fog(); fog.g = scene['walked'].copy()
    today = dict(full=np.asarray(Image.open(CAP_FULL).convert('RGB'), float)[170:170 + 760, 590:590 + 740] / 255.0)

    def today_mini(at, heading, fg, found):
        o, _ = MK.mini_today(world, at[0], at[1], heading, fg, found)
        bg = np.ones(o.shape[:2] + (3,)) * 0.30
        MK.over(bg, o, 0, 0)
        return bg
    today['mini1'] = today_mini(scene['here'], scene['heading'], fog, scene['found'])
    today['mini2'] = today_mini(WIN2['at'], WIN2['heading'], MK.Fog().all(), set())
    cols = [('지금 (실제 캡처 / 지금 그림의 쌍둥이)', today), ('묶음 — 맞추기 전 (게임이 읽던 것)', {k: load(f'bundle_{k}.png') for k in ('full', 'mini1', 'mini2')}),
            ('묶음 — 맞춘 뒤 (게임이 읽는 것)', after), ('방향 시안 (사용자가 본 그림, 맞추기 전의 따로 그린 것)', {k: load(f'sheet_{k}.png') for k in ('full', 'mini1', 'mini2')})]
    rows = [('full', 1, f'펼친 지도 — 지금 캡처의 보기(중심 x {A_CENTRE[0]:.0f} · z {A_CENTRE[1]:.0f}, 2.66 m/px, 740×760 px, 전부 드러낸 상태), 100%'),
            ('mini1', 2, f"미니맵 창 1 — 시안 (나)의 자리(x {scene['here'][0]:.0f} · z {scene['here'][1]:.0f}, 336×240 m, 1.14 m/px), 일부만 걸은 상태, 200%"),
            ('mini2', 2, f"미니맵 창 2 — {WIN2['ko']}, 200%")]
    pad, capf, headf, smallf = 26, font('NotoSansKR-Bold.ttf', 24), font('NotoSerifKR-800.ttf', 34), font('NotoSansKR-Regular.ttf', 19)
    cw = 760
    W = pad + 4 * (cw + pad)
    hs = []
    for key, z, _ in rows:
        hs.append(max(c[1][key].shape[0] for c in cols) * z)
    H = 150 + sum(h + 92 for h in hs) + 130
    sheet = Image.new('RGB', (W, H), (0x26, 0x26, 0x23)); d = ImageDraw.Draw(sheet)
    d.text((pad, 20), '오행부 #308 지도 — 묶음을 방향 시안에 맞춘 결과 (같은 자리 · 같은 배율)', font=headf, fill=(0xE6, 0xE2, 0xD7))
    d.text((pad, 70), '왼쪽부터: 지금 → 맞추기 전 묶음(쌍둥이 합성) → 맞춘 뒤 묶음(쌍둥이 합성, 게임 셰이더의 안개 가장자리 · 밉 고정 포함) → 사용자가 본 방향 시안. '
                      '전부 오프라인 합성이다(왼쪽 위 "지금"만 실제 캡처). 맞춘 뒤부터 방향 시안도 묶음을 읽어 그린다.', font=smallf, fill=(0xA7, 0xA3, 0x98))
    for i, (name, _) in enumerate(cols):
        d.text((pad + i * (cw + pad), 108), name, font=capf, fill=(0xE6, 0xE2, 0xD7) if i != 2 else (0xE0, 0x6A, 0x58))
    y = 150
    for (key, z, cap), h in zip(rows, hs):
        d.text((pad, y), cap, font=smallf, fill=(0xD6, 0xD1, 0xC4)); y += 34
        for i, (_, pics) in enumerate(cols):
            im = Image.fromarray(u8(pics[key]))
            if z != 1: im = im.resize((im.size[0] * z, im.size[1] * z), Image.NEAREST)
            sheet.paste(im, (pad + i * (cw + pad) + max(0, (cw - im.size[0]) // 2), y))
        y += h + 58
    # ---- numbers (before / after)
    rw_b, rw_a = TW.ridge_windows(Bo), TW.ridge_windows(Bn)
    ic_b, ic_a = TW.icon_numbers(Bo), TW.icon_numbers(Bn)
    cb, ca = Bo.note['counts'], Bn.note['counts']
    tex_mb = lambda: round((1000 * 1500 * 1 * 4 / 3 + 256 * 256 * 4 * 4 / 3 + 1024 * 512 * 4 * 4 / 3 + 512 * 512 * 4 + 256 * 256 * 4) / 2 ** 20, 2)
    strip = json.loads((OUT / 'stripcheck308.json').read_text(encoding='utf-8')) if (OUT / 'stripcheck308.json').exists() else {}
    leg = json.loads((OUT / 'map308_legibility.json').read_text(encoding='utf-8'))
    leg_b = json.loads((BEFORE / 'twin/map308_legibility.json').read_text(encoding='utf-8'))
    res = dict(id='map308_align', spec='SPEC-MAP-OVERHAUL-308', note='before = Art/UI308/Map/_before_align (bundle of 05:45), after = the stage bundle',
               ridge_windows=dict(before=rw_b, after=rw_a, review_quote='541 windows: 44.7 % rank 1-2, 78.2 % any rank, 64.7 km (the review counted windows another way; '
                                  'before / after here are one method: a window every 60 m from the start of each baked road stroke)',
                                  direction_sheet_km=77.7),
               icons=dict(before=ic_b, after=ic_a),
               budgets=dict(before=dict(strokes=cb['strokes'], points=cb['points'], bytes=cb['bytes'], maxVertices9Bins_bake=cb['maxVertices9Bins']),
                            after=dict(strokes=ca['strokes'], points=ca['points'], bytes=ca['bytes'], maxVertices9Bins_bake=ca['maxVertices9Bins']),
                            caps=dict(points=24000, bytes=600000, vertices9Bins=6000, textureMB=8.0),
                            texture_mb_computed=tex_mb(), texture_rule='terrain BC7 + mips, pattern / stroke atlas RGBA32 + mips, icons RGBA32 no mips (sizes unchanged)',
                            stripcheck=[c['label'] for c in strip.get('checks', []) if 'largest 3 x 3' in c.get('label', '')]),
               road_centre_line=dict(before=leg_b.get('road_centre_line'), after=leg.get('road_centre_line')),
               bundle_sha256={o['file']: o['sha256'] for o in Bn.note['outputs']})
    (OUT / 'map308_align.json').write_bytes((json.dumps(res, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    v = leg['values']
    bv = {'walked paper / unwalked wash': v['walked_vs_unwalked']['ratio'], 'darkest slope wash / unwalked wash': v['darkest_wash_vs_unwalked']['ratio'],
          'great road ink / its paper band': v['road_daero_vs_paper']['ratio'], 'trail ink / its paper band': v['road_soro_vs_paper']['ratio'],
          'great road ink / darkest slope wash': v['road_daero_vs_darkest_wash']['ratio'],
          'ridge ink (rank 1, alpha %.2f) / darkest slope wash' % Bn.classes[2]['ranks'][0]['inkAlpha']: v['ridge_ink_vs_wash']['ratio'],
          'ink line over the unwalked wash / that wash': v['ink_line_vs_unwalked']['ratio'], 'cinnabar / paper': v['cinnabar_vs_paper']['ratio'],
          'note': 'alignment pass 2026-10-04: sRGB-coded composites of the notation values (map308_legibility.json values); not measured on screen. '
                  'Ridge inks are now .88 / .78 / .56 by rank (the direction sheet), the walked edge is the soft #307 edge with the rim on its half-way line.'}
    (OUT / 'bvalues308.json').write_bytes((json.dumps(bv, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    f = lambda r: '%.1f%%' % (r * 100)
    pb, pa = ic_b['confusable_pairs_at_22px'], ic_a['confusable_pairs_at_22px']
    lines = [
        '능선(길 위 미니맵 창 %d곳, 60 m마다): 1 · 2등급 40 m 이상 %s → %s, 등급 무관 %s → %s. 능선 %.1f → %.1f km(시안 77.7 km). 비탈 담묵이 넓은 창(%d → %d곳) 가운데 능선 없는 창 %d → %d곳.' % (
            rw_a['windows'], f(rw_b['share_rank_1_2']), f(rw_a['share_rank_1_2']), f(rw_b['share_any_rank']), f(rw_a['share_any_rank']), rw_b['ridge_km'], rw_a['ridge_km'],
            rw_b['windows_with_slope_wash'], rw_a['windows_with_slope_wash'], rw_b['of_those_without_a_ridge'], rw_a['of_those_without_a_ridge']),
        '아이콘(장소 기호 21종 · 210쌍, 22 px 먹 상관): .70 넘는 쌍 %d → %d(최대 %.2f → %.2f); 0.8 px 흐려서 %d → %d(최대 %.2f → %.2f). 2 px보다 가는 몸통: S 판 %d → %d종, 22 px %d → %d종.' % (
            pb['plain']['above_0_70'], pa['plain']['above_0_70'], pb['plain']['max'], pa['plain']['max'], pb['blur_0_8px']['above_0_70'], pa['blur_0_8px']['above_0_70'],
            pb['blur_0_8px']['max'], pa['blur_0_8px']['max'], len(ic_b['thin_parts']['glyphs_with_a_thin_part_at_S_32px']), len(ic_a['thin_parts']['glyphs_with_a_thin_part_at_S_32px']),
            len(ic_b['thin_parts']['glyphs_with_a_thin_part_at_22px']), len(ic_a['thin_parts']['glyphs_with_a_thin_part_at_22px'])),
        '현재 위치: 먹 %d×%d px(28 px 칸), 뒤쪽 파임 %d → %d px, 반 바퀴 돌린 자기 상관 %.2f → %.2f.  띠: 획 %d → %d · 점 %d → %d(상한 24,000) · %d → %d B · 3×3칸 정점(굽는 쪽 셈) %d → %d(상한 6,000) · 텍스처 %.2f MB(계산, 상한 8).' % (
            ic_a['player_ink_px_at_28'][0], ic_a['player_ink_px_at_28'][1], ic_b['player_notch_px_at_28'], ic_a['player_notch_px_at_28'],
            ic_b['player_half_turn_self_correlation_at_28'], ic_a['player_half_turn_self_correlation_at_28'],
            cb['strokes'], ca['strokes'], cb['points'], ca['points'], cb['bytes'], ca['bytes'], cb['maxVertices9Bins'], ca['maxVertices9Bins'], tex_mb())]
    for i, ln in enumerate(lines):
        d.text((pad, y + i * 32), ln, font=smallf, fill=(0xD6, 0xD1, 0xC4))
    sheet.save(OUT / 'map308_align_compare.png', optimize=True)
    print('wrote', OUT / 'map308_align_compare.png', sheet.size)
    for ln in lines: print(ln)


if __name__ == '__main__':
    main()
