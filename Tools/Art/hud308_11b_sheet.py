# -*- coding: utf-8 -*-
"""hud308_11b_sheet.py - the HUD cluster after D308-11b (SPEC-HUD-LIQUID-308): key glyphs on the three marks, and the liquid
that moves only in answer to what the player really did.

Offline and deterministic (numpy + PIL). Every HUD pixel on this sheet comes from the numpy twin of UI/InkVessel308
(Tools/Art/hud308_twin.py) sampling the staged atlas; the key each mark shows is picked from the LIVE bindings
(Oheangbu/Assets/InputSystem_Actions.inputactions + the profile's VehicleKeyPath) by the twin's port of HudKeyGlyph308, and the
liquid poses of the strip are the states of the scripted timeline (AC-H16) at each action's peak. It composites in sRGB (the
#304 mockup convention; the project is Linear) and it is not a game capture. Nothing under Oheangbu/Assets, the editor queue
or Play is touched.

usage (repo root):
    python Tools/resource_guard.py --wait
    python Tools/Art/hud308_twin.py --report          (the sheet prints its numbers)
    python Tools/Art/hud308_11b_sheet.py              the sheet + half-size jpg + json
    python Tools/Art/hud308_11b_sheet.py --check      render twice, compare the bytes
out:
    Art/UI308/HUD/hud308_11b_sheet.png   (2400 px wide, Korean captions)
    Art/UI308/HUD/hud308_11b_sheet_half.jpg
    Art/UI308/HUD/hud308_11b_sheet.json  (the picked scenes and the numbers printed on the sheet)
"""
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L            # noqa: E402
import hud308_mock as HM            # noqa: E402  (sheet scaffolding)
import hud308_theme_sheet as TS     # noqa: E402  (the real captures, the brightest / darkest window, the twin's draw)
import hud308_twin as TW            # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Art', 'UI308', 'HUD')
CROP = HM.CROP
CW, CH = CROP[2] - CROP[0], CROP[3] - CROP[1]
INK8, ASH8, PAPER8 = HM.INK8, HM.ASH8, HM.PAPER8
REP = {}
REST = dict(hp=.62, ink=.44)                         # standing still: a flat line, exactly (no tilt, no ripple)
TW.SHOW_KEYS = True                                  # hud308_theme_sheet (imported for its helpers) switches the keys off for its own panels


def img(a):
    return HM.to_img(a)


def tile(region, s, bg, **kw):
    """The cluster on a flat ground or on a picture of `region` (design px), at s screen px per design px."""
    w, h = int(round((region[2] - region[0]) * s)), int(round((region[3] - region[1]) * s))
    dst = np.ones((h, w, 3)) * bg if np.ndim(bg) == 1 else TS.scaled(bg, s)
    mips, TW.MIPS = TW.MIPS, True
    els = TW.cluster(scale=s, **kw)
    TW.paste(dst, [(rgba, x0 - int(round(region[0] * s)), y0 - int(round(region[1] * s)), aux) for rgba, x0, y0, aux in els])
    TW.MIPS = mips
    return dst


def panel_band(sh):
    b = REP['numbers']['keys']['source']
    sh.head('(가) 실제 화면 아래쪽 띠 — 1080p 실제 크기',
            '배경 = HUD 없는 실제 캡처 Art/UI304/clean/road.png 의 y 760–1060. 플레이어가 가만히 서 있는 상태: 두 수면은 수평선이다(기울기 0 · 물결 0).\n'
            '표시마다 그 키의 건반 글자: 회피 = %s → ⇧, 점프 = %s → ␣, 자동차 = %s → G. 지금 바인딩에서 읽은 것이다(코드의 글자 상수가 아니다).'
            % (b['dodge'], b['jump'], b['vehicle']))
    region = (0, 760, 1920, 1060)
    dst = TS.capture('road')[region[1]:region[3], region[0]:region[2]].copy()
    mips, TW.MIPS = TW.MIPS, True
    els = TW.cluster(scale=1.0, **REST)
    TW.paste(dst, [(rgba, x0 - region[0], y0 - region[1], aux) for rgba, x0, y0, aux in els])
    TW.MIPS = mips
    sh.put(img(dst), sh.M + 200, sh.y)
    sh.cap(sh.M, sh.y + 8, 'D308-11b', 30, INK8, 'Black')
    sh.cap(sh.M, sh.y + 56, '건반 글자 붙임\n서 있으면 잔잔', 19, ASH8, 'Regular')
    sh.y += 300 + 14
    sh.rule()


def panel_scene(sh, key, title):
    v, name, x, y = TS.pick_window(CW, CH, key == 'bright')
    REP.setdefault('scenes', {})[key] = dict(capture='Art/UI304/clean/%s.png' % name, x=x, y=y, w=CW, h=CH, mean_luminance=round(v, 4))
    leg = REP['numbers']['keys']['legibility_1080p']['lacquer']['brightest_sky_E4E0D6' if key == 'bright' else 'darkest_pine_2A2A26']
    sh.head(title, '배경 = 두 캡처(road · palace)에서 평균 휘도가 가장 %s %d×%d 창: %s.png (%d, %d), 평균 휘도 %.3f. 체력 62 %% · 먹 44 %%, 서 있음.\n'
            '왼쪽부터: 1×(1080p의 실제 화소) · 2×(4K 또는 UI 크기 2배) · 2× 건반 글자 없음(D308-11b 전) · 건반 셋만 6×(쓸 수 있음 | 지금 못 씀).'
            % ('높은' if key == 'bright' else '낮은', CW, CH, name, x, y, v))
    bg = TS.capture(name)[y:y + CH, x:x + CW]
    x0 = sh.M
    y0 = sh.y + 34
    for s, keys, lab in ((1.0, None, '1×  건반 글자'), (2.0, None, '2×  건반 글자'), (2.0, False, '2×  건반 글자 없음')):
        dst = tile(CROP, s, bg, keys=keys, **REST)
        sh.put(img(dst), x0, y0)
        sh.cap(x0, y0 - 30, lab, 22, INK8, 'Bold')
        x0 += dst.shape[1] + 16
    # the three keycaps alone, 6x, on the mean colour of this window: usable | cannot be used now
    flat = bg.reshape(-1, 3).mean(axis=0)
    cells = TW.live_key_cells()
    yy = y0
    for wet, lab in ((1.0, '쓸 수 있음'), (0.0, '지금 못 씀 (α .42)')):
        xx = x0 + 6
        for i in range(3):
            n = int(round(24 * 6))
            dst = np.ones((n, n, 3)) * flat
            mips, TW.MIPS = TW.MIPS, True
            TW.paste(dst, [TW.key_element((3.0, 3.0, 18.0, 18.0), cells[i], wet, scale=6.0, origin=(0.0, 0.0))])
            TW.MIPS = mips
            sh.put(img(dst), xx, yy)
            xx += n + 8
        sh.cap(xx + 6, yy + 50, lab, 21, INK8, 'Bold')
        yy += 24 * 6 + 10
    sh.cap(x0 + 6, yy + 4, '이 재질의 대비 [M, 1080p]: 기호 / 건반 %.2f:1 · 마른 기호 %.2f:1 · 건반 꼴 / 땅 %.2f:1 (면 %.2f · 실테 %.2f)'
           % (leg['symbol_vs_keycap'], leg['dry_symbol_vs_keycap'], leg['keycap_shape_vs_ground'], leg['keycap_face_vs_ground'], leg['keycap_hairline_vs_ground']),
           19, INK8, 'Regular')
    sh.y = y0 + CH * 2 + 18
    sh.rule()


def panel_states(sh):
    k = REP['numbers']['keys']
    sh.head('(라) 표시 셋의 세 상태 — 건반 글자와 함께',
            '줄마다 한 표시. 왼쪽부터: 쓸 수 있음(자개가 박혀 있다) · 돌아오는 중 55 %(다시 박히는 중 — 건반은 아직 마른 채) · 지금 못 씀(빠진 자리, 건반 기호 α .42).\n'
            '각 상태 = 4× 밝은 하늘 #E4E0D6 · 4× 검은 솔숲 #2A2A26 · 1× 둘(1080p 실제 화소). 건반은 뚜껑의 왼쪽 아래 테를 물고, 기호 조각은 가리지 않는다. 표시가 숨으면 건반도 없다.')
    names = (('회피', 2, 0), ('점프', 3, 1), ('자동차 (부를 수 있음)', 4, 2), ('자동차 (나와 있음 = 회수)', 5, 2))
    cells = TW.live_key_cells()
    for label, code, slot in names:
        x0 = sh.M + 290
        sh.cap(sh.M, sh.y + 96, label, 24, INK8, 'Bold')
        for wet, fill in ((1.0, 0.0), (0.0, .55), (0.0, 0.0)):
            for bg in (L.SKY, L.PINE):
                t = mark_tile(code, cells[slot], wet, fill, 4.0, bg)
                sh.put(img(t), x0, sh.y)
                x0 += t.shape[1] + 6
            for j, bg in enumerate((L.SKY, L.PINE)):
                t = mark_tile(code, cells[slot], wet, fill, 1.0, bg)
                sh.put(img(t), x0, sh.y + j * (t.shape[0] + 6))
            x0 += 64 + 30
        sh.y += 62 * 4 + 12
    pl = k['place']['keys']
    sh.cap(sh.M, sh.y + 4, '자리 [M]: 건반 18×18 px, 뚜껑 중심에서 (−15, +26) px(온 화소에 맞춘다). 용기와 %.1f px 이상 · 다른 뚜껑과 %.1f px 이상 떨어지고 제 뚜껑의 테를 %.1f–%.1f px 문다. 군집 범위 그대로.'
           % (min(v['clear_of_vessels_px'] for v in pl.values()), min(v['clear_of_other_lids_px'] for v in pl.values()),
              min(v['bites_own_lid_px'] for v in pl.values()), max(v['bites_own_lid_px'] for v in pl.values())), 20, INK8, 'Regular')
    sh.y += 40
    sh.rule()


def mark_tile(code, cell, wet, fill, s, bg):
    """One mark with its key glyph on a flat ground: the 44 px lid in a 62 px square (room for the keycap at its lower left)."""
    n = int(round(62 * s))
    dst = np.ones((n, n, 3)) * bg
    mark = (14.0, 4.0, 44.0, 44.0)
    cx, cy = mark[0] + 22 + TW.KEY_OFFSET[0], mark[1] + 22 + TW.KEY_OFFSET[1]
    key = (float(round(cx - 9)), float(round(cy - 9)), 18.0, 18.0)
    mips, TW.MIPS = TW.MIPS, True
    els = [TW.element(mark, code, TW.INK, [wet, 0, 0, 0], [0, 0, 0, 0], [code, 0, 0, 0 if wet >= 1 else fill], scale=s, origin=(0.0, 0.0)),
           TW.key_element(key, cell, wet, scale=s, origin=(0.0, 0.0))]
    TW.paste(dst, els)
    TW.MIPS = mips
    return dst


def panel_material(sh):
    k = REP['numbers']['keys']['legibility_1080p']
    sh.head('(마) 건반의 재질 — 옻칠 건반(채택) | 백자 건반(Key.Porcelain, 대안)',
            '같은 꼴 · 같은 자리에서 재질만 바꿔 그렸다. 줄 = 가장 밝은 장면 / 가장 어두운 장면(위 (나) · (다)의 창). 각 재질 = 2× 군집 + 건반 셋 6×.\n'
            '백자: 밝은 장면에서 면이 땅에 묻히고(%.2f:1) 꼴을 1 px 테가 진다. 어두운 장면에서는 뚜껑보다 %.2f배 밝은 덩어리가 된다(옻칠 건반 %.2f배). 그래서 옻칠을 골랐다.'
            % (k['porcelain']['brightest_sky_E4E0D6']['keycap_face_vs_ground'], k['porcelain']['keycap_vs_lid_mean_luminance_dark_scene'],
               k['lacquer']['keycap_vs_lid_mean_luminance_dark_scene']))
    cells = TW.live_key_cells()
    for key in ('bright', 'dark'):
        sc = REP['scenes'][key]
        name = os.path.splitext(os.path.basename(sc['capture']))[0]
        bg = TS.capture(name)[sc['y']:sc['y'] + CH, sc['x']:sc['x'] + CW]
        flat = bg.reshape(-1, 3).mean(axis=0)
        x0 = sh.M
        for style, lab in (('lacquer', '옻칠 건반 (채택)'), ('porcelain', '백자 건반 (대안)')):
            TW.key_style(style)
            dst = tile(CROP, 2.0, bg, **REST)
            sh.put(img(dst), x0, sh.y + 30)
            sh.cap(x0, sh.y, '%s — %s' % (lab, '가장 밝은 장면' if key == 'bright' else '가장 어두운 장면'), 21, INK8, 'Bold')
            xx = x0 + dst.shape[1] + 12
            yy = sh.y + 30
            for wet in (1.0, 0.0):
                x1 = xx
                for i in range(3):
                    n = 24 * 6
                    t = np.ones((n, n, 3)) * flat
                    mips, TW.MIPS = TW.MIPS, True
                    TW.paste(t, [TW.key_element((3.0, 3.0, 18.0, 18.0), cells[i], wet, scale=6.0, origin=(0.0, 0.0))])
                    TW.MIPS = mips
                    sh.put(img(t), x1, yy)
                    x1 += n + 6
                yy += n + 8
            row = k[style]['brightest_sky_E4E0D6' if key == 'bright' else 'darkest_pine_2A2A26']
            sh.cap(xx, yy + 4, '기호 / 건반 %.2f:1\n마른 기호 %.2f:1\n면 / 땅 %.2f:1 · 테 / 땅 %.2f:1' % (
                row['symbol_vs_keycap'], row['dry_symbol_vs_keycap'], row['keycap_face_vs_ground'], row['keycap_hairline_vs_ground']), 20, INK8, 'Regular')
            x0 = xx + 3 * (24 * 6 + 6) + 40
        TW.key_style('lacquer')
        sh.y += 30 + CH * 2 + 16
    sh.rule()


def panel_liquid(sh):
    t = REP['numbers']['timeline']
    names = ('가만히 서 있음 (처음 3초)', '걷기 · 멈춤 (2.2 m/s)', '달리기 · 멈춤 (5.5 m/s)', '옆 회피 (12 m/s)', '점프 · 착지', '급한 시점 전환 (480°/s)',
             '피격 (체력 80 → 62)', '먹을 씀 (60 → 45)', '먹을 채움 (45 → 75)', '임팩트 프레임 (그로기 상대)', '가만히 서 있음 (끝 5초)')
    sh.head('(바) 액체 — 서 있을 때와 행동 뒤 (타임라인의 실제 상태)',
            '아래 칸은 그린 그림이 아니라 AC-H16 타임라인을 돌린 모델의 상태다: 행동마다 수면이 가장 크게 움직인 프레임. 2×, 용기만. 칸 아래 = 그 프레임의 기울기 · 물결 · 벽에서의 진폭, 그리고 행동이 끝난 뒤 수평선으로 돌아오기까지.\n'
            '가만히 서 있으면 어느 수위에서든(저체력 포함) 수평선이다. 응답은 세기에 비례한다: 걷기 < 달리기 < 회피. 값(수위)은 출렁임에 움직이지 않는다 — 가운데 열이 축이다.')
    # the frame of each action's peak (HP for movement and the hit, ink for the ink events), from the twin's own run
    full = TW.tl_run()
    T = full['t']
    picks = []
    for k, seg in enumerate(t['segments']):
        start = TW.TL_START[k]; end = TW.TL_START[k + 1] if k + 1 < len(TW.TL_START) else TW.TL_END
        idx = [i for i in range(len(T)) if start - 1e-6 <= T[i] < end]
        series = full['ink_amp'] if seg in ('ink spent', 'ink refill') else full['hp_amp']
        f = idx[len(idx) // 2] if k in (0, len(t['segments']) - 1) else max(idx, key=lambda i: series[i])
        picks.append(f)
    run = TW.tl_run(capture=set(picks))
    region = (56, 806, 268, 1020)
    s = 1.75
    per_row = 6
    x0 = sh.M
    tiles = []
    for k, f in enumerate(picks):
        hp, ink = run['states'][f]
        dst = tile(region, s, L.PINE if k % 2 else L.SKY, marks=((1, 0, None), (1, 0, None), (1, 0, None)), **TW.pose(hp, ink))
        tiles.append((k, f, dst, hp, ink))
    REP['strip'] = []
    for n, (k, f, dst, hp, ink) in enumerate(tiles):
        if n and n % per_row == 0:
            sh.y += dst.shape[0] + 118
            x0 = sh.M
        sh.put(img(dst), x0, sh.y + 34)
        seg = t['segments'][k]
        sh.cap(x0, sh.y, names[k], 21, INK8, 'Bold')
        amp_hp, amp_ink = full['hp_amp'][f], full['ink_amp'][f]
        if k in (0, len(picks) - 1):
            txt = '기울기 0 · 물결 0 px\n벽에서 %.1f px — 수평선' % max(amp_hp, amp_ink)
        elif seg in ('ink spent', 'ink refill'):
            txt = '먹 기울기 %+.3f · 물결 %.1f px\n벽에서 체력 %.1f · 먹 %.1f px\n멎음 +%.2f s' % (ink['Tilt'], ink['WavePx'], amp_hp, amp_ink, t['settle_s'][k])
        else:
            txt = '체력 기울기 %+.3f · 물결 %.1f px\n벽에서 체력 %.1f · 먹 %.1f px\n멎음 +%.2f s' % (hp['Tilt'], hp['WavePx'], amp_hp, amp_ink, t['settle_s'][k])
        sh.cap(x0, sh.y + 34 + dst.shape[0] + 6, txt, 18, INK8 if k not in (0, len(picks) - 1) else ASH8, 'Regular')
        REP['strip'].append(dict(action=seg, t=round(T[f], 3), hp_tilt=round(hp['Tilt'], 4), hp_wave_px=round(hp['WavePx'], 3), ink_tilt=round(ink['Tilt'], 4),
                                 ink_wave_px=round(ink['WavePx'], 3), hp_px_at_glass=round(amp_hp, 2), ink_px_at_glass=round(amp_ink, 2)))
        x0 += dst.shape[1] + 12
    # the 12th cell: low HP standing still (the tremble is gone)
    dst = tile(region, s, L.PINE, marks=((1, 0, None), (1, 0, None), (1, 0, None)), hp=.18, ink=.08)
    sh.put(img(dst), x0, sh.y + 34)
    sh.cap(x0, sh.y, '저체력 18 % · 먹 8 %, 서 있음', 21, INK8, 'Bold')
    sh.cap(x0, sh.y + 34 + dst.shape[0] + 6, '떨림 없음(D308-11b): 검붉은 값 ·\n물때 테 · 갈필 결로 말한다\n기울기 0 · 물결 0 px', 18, ASH8, 'Regular')
    sh.y += 34 + dst.shape[0] + 100
    sh.rule()


def panel_numbers(sh):
    n = REP['numbers']
    t, k, a = n['timeline'], n['keys'], n['actions']
    lg = k['legibility_1080p']['lacquer']
    lines = [
        '건반 글자의 출처: 회피 %s · 점프 %s (InputAction의 바인딩 effectivePath) · 자동차 %s (자료 — 소환 장치가 gKey를 직접 본다: %s). 재바인딩 코드 %d건 · Gameplay 맵의 패드 바인딩 %d개.' % (
            k['source']['dodge'], k['source']['jump'], k['source']['vehicle'], '일치' if k['source']['vehicle_path_matches_summon'] else '불일치',
            k['source']['rebinding_calls_in_project'], len(k['source']['gamepad_bindings_in_gameplay'])),
        '판독 [M, 1080p]: 대문자 높이 %d–%d px · 획의 가장 짙은 화소 덮임 %.2f · 세 기호가 나눠 쓰는 화소 %.0f %% 이하. 기호 / 건반 %.2f:1 · 마른 기호 %.2f:1.' % (
            k['symbols_1080p']['letter_cap_rows_px'][0], k['symbols_1080p']['letter_cap_rows_px'][1], k['symbols_1080p']['peak_coverage_min'],
            k['symbols_1080p']['most_alike_pair_shared_pixels'] * 100, lg['brightest_sky_E4E0D6']['symbol_vs_keycap'], lg['brightest_sky_E4E0D6']['dry_symbol_vs_keycap']),
        '화이트리스트: Graphic %d개 그대로 · 글자 객체 %d개 · 건반 칸 %d개(한 칸 = 기호 하나, 낱말 없음). 임팩트 프레임이 건반에 주는 화소 변화 %g. 건반 넓이 %.0f px² = 화면의 %.3f %%.' % (
            k['whitelist']['graphics_built'], k['whitelist']['text_objects_built'], k['whitelist']['cells_in_block'], k['states']['key_pixels_changed_by_an_impact_frame'],
            k['place']['key_px2_total'], k['place']['key_area_share_of_screen'] * 100),
        '액체: 서 있는 %d프레임의 진폭 최대 %g px · 10초 무행동(수위 넷, 저체력 포함)의 기울기 + 물결 + 위상 %g. 행동 뒤 멎음 최장 %.2f s. 수위 차 %g · 값 차 %g(행동을 뺀 실행과).' % (
            t['rest_frames'], t['rest_max_px'], a['idle_10s_at_four_levels']['tilt_plus_ripple_plus_phase'], max(t['settle_s']), t['level_drift_max'], t['value_drift_max']),
        '세기(체력 액체, 벽에서 px): 걷기 %.1f < 달리기 %.1f < 회피 %.1f · 점프 · 착지 %.1f · 급회전 %.1f · 피격 %.1f · 임팩트 %.1f / 먹: 씀 %.1f · 채움 %.1f.  "움직임 줄이기": 전부 0.' % (
            t['hp_peak_px'][1], t['hp_peak_px'][2], t['hp_peak_px'][3], t['hp_peak_px'][4], t['hp_peak_px'][5], t['hp_peak_px'][6], t['hp_peak_px'][9],
            t['ink_peak_px'][7], t['ink_peak_px'][8]),
        '없앤 것: 저체력 잔떨림 · 시점 카메라 입력 · 기울기 목표값 따라가기 · 세로 가속의 연속 항. 소스 검색: LowAgitation %d건 · Step의 움직임 인자 없음(%s) · 셰이더 시간 항 %d건.' % (
            a['sources']['low_agitation_mentions'], '예' if a['sources']['step_takes_no_motion_input'] else '아니오', a['sources']['shader_time_terms']),
        '이 시트는 셰이더의 numpy 쌍둥이를 sRGB로 합성한 것이다(게임 캡처가 아니다). 셰이더는 한 번도 컴파일되지 않았다. 타임라인 그림: Art/UI308/HUD/hud308_slosh_timeline.png. 아틀라스 sha256 %s…' % TW.AJ['atlas']['sha256'][:16],
    ]
    sh.head('(사) 잰 값 [M] — Art/UI308/HUD/twin_report.json 의 keys · actions · timeline 묶음')
    for line in lines:
        sh.cap(sh.M + 28, sh.y, line, 20, INK8, 'Regular')
        sh.y += 33


def build():
    path = os.path.join(OUT, 'twin_report.json')
    with open(path, encoding='utf-8') as f:
        rep = json.load(f)
    if 'keys' not in rep or rep.get('atlas_sha256') != TW.AJ['atlas']['sha256']:
        raise SystemExit('run "python Tools/Art/hud308_twin.py --report" first: the sheet prints its numbers')
    REP.clear()
    REP['numbers'] = dict(keys=rep['keys'], actions=rep['actions'], timeline={k: v for k, v in rep['timeline'].items() if k != 'trace'})
    sh = HM.Sheet()
    sh.title('오행부 #308 HUD — D308-11b: 표시에 건반 글자 · 액체는 플레이어의 행동에만 흔들린다',
             ['SPEC-HUD-LIQUID-308 §2.3 · §3.5 · 스테이지 Tools/Unity/Stage308_hud · IMPLEMENTED(오프라인) — VALIDATED 아님: Assets · 편집기 큐 · Play 0',
              '모든 HUD 화소 = UI/InkVessel308의 numpy 쌍둥이가 스테이지 아틀라스를 읽어 그린 것. 건반 글자 = 지금 바인딩에서 고른 아틀라스 칸, 액체의 자세 = 타임라인(AC-H16)을 돌린 모델의 상태.'])
    panel_band(sh)
    panel_scene(sh, 'bright', '(나) 가장 밝은 장면 — 1× · 2×')
    panel_scene(sh, 'dark', '(다) 가장 어두운 장면 — 1× · 2×')
    panel_states(sh)
    panel_material(sh)
    panel_liquid(sh)
    panel_numbers(sh)
    return sh.im.crop((0, 0, sh.W, sh.y + 24))


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    im = build()
    data = TS.png_bytes(im)
    if '--check' in sys.argv:
        again = TS.png_bytes(build())
        print('determinism: ' + ('same bytes' if again == data else 'DIFFERENT') + ' ' + hashlib.sha256(data).hexdigest()[:16])
        sys.exit(0 if again == data else 1)
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'hud308_11b_sheet.png'), 'wb') as f:
        f.write(data)
    im.resize((im.width // 2, im.height // 2), Image.LANCZOS).convert('RGB').save(os.path.join(OUT, 'hud308_11b_sheet_half.jpg'), quality=90)
    out = dict(spec='SPEC-HUD-LIQUID-308 (D308-11b)', generator='Tools/Art/hud308_11b_sheet.py', size=list(im.size),
               sha256=hashlib.sha256(data).hexdigest(), atlas_sha256=TW.AJ['atlas']['sha256'],
               note='numpy twin of UI/InkVessel308 on real HUD-less captures, sRGB composite; key glyph cells from the live bindings; '
                    'liquid poses = states of the AC-H16 timeline at each action\'s peak',
               scenes=REP.get('scenes'), strip=REP.get('strip'), numbers=REP['numbers'])
    with open(os.path.join(OUT, 'hud308_11b_sheet.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(json.dumps(dict(sheet=os.path.join(OUT, 'hud308_11b_sheet.png'), size=out['size'], sha256=out['sha256'][:16], scenes=out['scenes']),
                     ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
