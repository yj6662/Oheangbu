#!/usr/bin/env python3
"""#308 enemy run: one Korean table of the offline numbers (reads run/<id>_run.json and run/<id>_recheck.json, writes
Art/Characters308/DokkaebiVerify/run/RUN_NUMBERS.md). Plain Python, no Blender.

  python Tools/Blender/EnemyRun308/summary_run308.py            write the table and print it
Every number is [O] offline (Blender headless on the built clip, measured with Tools/Blender/EnemyRig308/rig308.py - the definitions
of the walk tables). Nothing here was measured in the Unity editor or in Play."""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / 'Art/Characters308/DokkaebiVerify/run'
IDS = [('agwi', '아귀'), ('changgui', '창귀')]
S = ('Left', 'Right')


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    D = {mid: json.loads((RUN / ('%s_run.json' % mid)).read_text(encoding='utf-8')) for mid, _ in IDS}
    K = {mid: json.loads((RUN / ('%s_recheck.json' % mid)).read_text(encoding='utf-8')) for mid, _ in IDS}      # the exported file measured again
    for mid, _ in IDS:
        if K[mid]['sha256'] != D[mid]['export']['sha256']: raise SystemExit('refused: %s_recheck.json is not about the exported file of %s_run.json (run recheck_run308.py)' % (mid, mid))
    out = ['# 아귀 · 창귀 달리기 클립 — 오프라인 수치 [O]', '',
           '전부 Blender 5.0.1 헤드리스에서 **지은 클립을 직접 잰 값**이다(측정 정의는 걷기 표와 같은 `rig308.py`). Unity Humanoid 변환 · 게임 조명 · Play는 들어 있지 않다. 편집기 · Play 실측 0건.',
           '왼쪽 / 오른쪽 순서. 원자료: `run/<id>_run.json`(짓는 자리에서 잰 값) · `run/<id>_recheck.json`(내보낸 FBX만 다시 읽어 잰 값). 그림: `run/<id>_run_front|side|back.jpg` · 사용자용 `run/<id>_run_user.jpg`.',
           '다시 내기: `bash Tools/Blender/EnemyRun308/run_build.sh` → `recheck_run308.py -- <id>`(종마다) → `python Tools/Blender/EnemyRun308/summary_run308.py`.', '']
    def table(title, rows):
        out.extend(['## ' + title, '', '| 항목 | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)])
        for label, fn in rows: out.append('| %s | %s |' % (label, ' | '.join(fn(D[mid]) for mid, _ in IDS)))
        out.append('')
    def lr(d, key, fmt='%.1f'): return ' / '.join(fmt % d[s][key] if d[s].get(key) is not None else '-' for s in S)
    def speeds(j, which):
        if which == 'walk': return ' · '.join('%s m/s: %.2f배속 = %.0f걸음' % (k.rstrip('0').rstrip('.'), v['playback_rate'], v['steps_per_minute']) for k, v in j['walk_at_run_speed']['by_actor_speed'].items())
        return ' · '.join('%s m/s: %.2f배속 = %.0f걸음' % (k.rstrip('0').rstrip('.'), v['playback_rate'], v['steps_per_minute']) for k, v in j['run']['cadence_by_actor_speed'].items())
    table('1. 걸음새 — 걷기 수리본을 그 속도로 틀 때와 달리기', [
        ('걷기 수리본: 디딘 발 속도 · 주기 · 보폭', lambda j: '%.3f m/s · %d프레임 · %.2f m' % (j['walk_at_run_speed']['stance_speed_m_s'], j['walk_at_run_speed']['cycle_frames'], j['walk_at_run_speed']['stride_m'])),
        ('걷기로 배우 속도를 내면(분당 걸음)', lambda j: speeds(j, 'walk')),
        ('**달리기**: 주기 · 보폭(설계 속도 × 주기)', lambda j: '%d프레임 = %.3f s · %.2f m' % (j['run']['cycle_frames'], j['run']['seconds'], j['run']['stride_m_design'])),
        ('달리기로 배우 속도를 내면(분당 걸음)', lambda j: speeds(j, 'run')),
        ('디딘 발 속도: 발 앞꿈치(고정점) · 발바닥 접지 구간 · 걷기와 같은 속도 고원 규칙', lambda j: '%.3f · %.3f · %.3f m/s' % (j['run']['feet']['Left']['ball_speed_mean'], j['against_limits']['stance_speed_m_s'], j['against_limits']['stance_speed_plateau_rule_m_s'])),
        ('설계 속도 %.1f m/s에서의 미끄러짐(접지 구간 기준) · 앞꿈치 한 걸음 미끄러짐' % D['agwi']['run']['clip_speed_design'], lambda j: '%.1f %% · %.3f cm' % (j['against_limits']['slide_ratio_at_clip_speed'] * 100, j['against_limits']['slide_per_step_cm'])),
        ('디딤 비율(설계) · 접지 프레임(발바닥 ≤ %.1f cm)' % D['agwi']['run']['contact_eps_cm'], lambda j: '%.0f %% · %s / %d' % (j['run']['stance_share_design'] * 100, lr(j['run']['feet'], 'contact_frames', '%d'), j['run']['cycle_frames'])),
        ('체공: 설계 비율 · 두 발이 다 뜬 프레임 · 두 발이 다 닿은 프레임', lambda j: '%.0f %% · %d / %d · %d' % (j['run']['flight_share_design'] * 100, j['run']['flight_frames_by_height'], j['run']['cycle_frames'], j['run']['double_support_frames_by_height'])),
        ('걷기의 체공 프레임(같은 규칙)', lambda j: '%d / %d' % (j['walk_at_run_speed']['flight_frames_by_height'], j['walk_at_run_speed']['cycle_frames'])),
    ])
    table('2. 몸 — 숙임 · 출렁임 · 머리', [
        ('앞으로 숙임(엉덩이 → 윗가슴 선, 최소 · 평균 · 최대): 걷기 → 달리기', lambda j: '%.0f · %.0f · %.0f° → %.0f · %.0f · %.0f°' % (*j['walk_at_run_speed']['lean_deg'], *j['run']['lean_deg'])),
        ('설계 숙임(골반 + 허리 회전의 합)', lambda j: '%.0f + %.0f°' % (j['parameters']['lean']['pelvis'], j['parameters']['lean']['spine'])),
        ('엉덩이 높이 범위 · 상하 출렁임: 걷기 → 달리기', lambda j: '%.1f – %.1f cm · %.1f → %.1f cm' % (*j['run']['hips_height_cm'], j['walk_at_run_speed']['bounce_cm'], j['run']['bounce_cm'])),
        ('엉덩이 상하 곡선 · 체공 중 수직 가속(자유낙하 −9.81) · 가장 낮은 / 높은 프레임', lambda j: '%s · %s m/s² · %d / %d' % ('디딤 반사인 + 체공 자유낙하(출렁임은 박자에서 나온다)' if j['parameters']['hips'].get('shape') == 'ballistic' else '코사인(자료 값)',
            '%.2f' % j['shape']['hips_accel_flight_mean_m_s2'] if j['shape']['hips_accel_flight_mean_m_s2'] is not None else '-', j['shape']['hips_lowest_frame'], j['shape']['hips_highest_frame'])),
        ('엉덩이 기준 높이(무릎 보호로 찾은 값) · 걷기 평균', lambda j: '%.1f cm · %.1f cm' % (j['height_solve']['hips_base_cm'], j['height_solve']['walk_hips_mean_cm'])),
        ('머리 높이 흔들림: 걷기 → 달리기', lambda j: '%.1f → %.1f cm' % (j['walk_at_run_speed']['head_height_range_cm'], j['run']['head_height_range_cm'])),
        ('머리 끄덕임(얼굴 방향의 상하 각 범위): 걷기 → 달리기', lambda j: '%.1f° → %.1f°(평균 %.1f°)' % (j['walk_at_run_speed']['head_pitch_deg'][2] - j['walk_at_run_speed']['head_pitch_deg'][0], j['run']['head_pitch_deg'][2] - j['run']['head_pitch_deg'][0], j['run']['head_pitch_deg'][1])),
        ('좌우 흔들림(엉덩이)', lambda j: '%.1f cm' % j['run']['hips_sway_cm']),
    ])
    table('3. 다리 · 발', [
        ('발바닥 전 주기 최저(지면 0)', lambda j: lr(j['run']['feet'], 'sole_min_cm_cycle', '%.2f') + ' cm'),
        ('디딤 발바닥 평균', lambda j: lr(j['run']['feet'], 'sole_mean_cm_contact', '%.2f') + ' cm'),
        ('내딛는 발: 발바닥 최고 · 발목 올림 폭(걷기 → 달리기)', lambda j: '%s cm · %.0f → %.0f cm' % (lr(j['run']['feet'], 'sole_max_cm_cycle'), max(j['walk_at_run_speed']['ankle_lift_cm'].values()), max(j['run']['feet'][s]['ankle_lift_cm'] for s in S))),
        ('디딤 끝 뒤꿈치 들림(최대) · 디딤 중 발가락 최고', lambda j: '%s cm · %s cm' % (lr(j['run']['feet'], 'heel_max_cm_contact'), lr(j['run']['feet'], 'toe_max_cm_contact', '%.2f'))),
        ('무릎 굽힘: 디딤 최소 · 전체 최소 · 최대', lambda j: '%s° · %s° · %s°' % (lr(j['run']['feet'], 'knee_min_contact_deg'), lr(j['run']['feet'], 'knee_min_deg'), lr(j['run']['feet'], 'knee_max_deg'))),
        ('무릎 보호값(디딤 / 내딛기) · 엉덩이 추가 내림', lambda j: '%.0f° / %.0f° · %.1f cm' % (j['parameters']['knee']['stance_min_bend'], j['parameters']['knee']['min_bend'], j['parameters']['knee']['extra_drop'] * 100)),
        ('무릎 한 프레임 최대 변화 · 2차 차분 최대(걷기 수리본: 아귀 21 / 17 · 13 / 14, 창귀 19 / 18 · 16 / 25)', lambda j: '%s° · %s°' % (lr(j['knee'], 'max_step_deg'), lr(j['knee'], 'max_second_difference_deg'))),
        ('허벅지 앞으로 든 각 최대(옆에서, 곧게 선 다리 0°)', lambda j: lr(j['run']['feet'], 'thigh_flex_max_deg') + '°'),
        ('걷기의 무릎 범위(비교)', lambda j: ' / '.join('%.0f–%.0f°' % tuple(j['walk_at_run_speed']['knee'][s]) for s in S)),
        ('발 닿는 프레임 → 떼는 프레임', lambda j: ' / '.join('%d → %d' % (j['contacts_design'][s]['touch_down_frame'], j['contacts_design'][s]['toe_off_frame']) for s in S)),
        ('디딤 도달 부족(무릎 보호에 걸려 발이 목표에 못 간 양, 전 프레임 최대)', lambda j: '%.2f cm' % j['checks']['worst_reach_deficit_cm']),
        ('걷기 한가운데 디딤의 발을 평평하게 고친 각(기준 자세)', lambda j: ' / '.join('%.1f°' % j['reference']['flat_foot_reference'][s]['ref_level_deg'] for s in S)),
    ])
    table('4. 팔', [
        ('위팔 앞뒤 각(+ = 앞) 범위: 걷기(왼) → 달리기(왼 / 오른)', lambda j: '%.0f…%.0f° → %s' % (j['walk_at_run_speed']['arms']['Left']['swing_min'], j['walk_at_run_speed']['arms']['Left']['swing_max'], ' / '.join('%.0f…%.0f°' % (j['arms'][s]['swing_min'], j['arms'][s]['swing_max']) for s in S))),
        ('팔꿈치 굽힘 범위: 걷기(왼) → 달리기(왼 / 오른)', lambda j: '%.0f–%.0f° → %s' % (j['walk_at_run_speed']['arms']['Left']['elbow_min'], j['walk_at_run_speed']['arms']['Left']['elbow_max'], ' / '.join('%.0f–%.0f°' % (j['arms'][s]['elbow_min'], j['arms'][s]['elbow_max']) for s in S))),
        ('손목 앞뒤 이동 폭: 걷기 → 달리기', lambda j: '%.0f → %s cm' % (j['walk_at_run_speed']['arms']['Left']['wrist_fore_aft_cm'], lr(j['arms'], 'wrist_fore_aft_cm', '%.0f'))),
        ('벌림: 평균(걷기 수리본 26°) · 최소 – 최대', lambda j: '%s° · %s' % (lr(j['arms'], 'abduction_mean'), ' / '.join('%.0f–%.0f°' % (j['arms'][s]['abduction_min'], j['arms'][s]['abduction_max']) for s in S))),
        ('어깨 높이 왼 − 오른(평균) · 손과 손 사이 거리', lambda j: '%.1f cm · %.0f – %.0f cm' % (j['shape']['shoulder_height_left_minus_right_cm'], *j['shape']['hand_to_hand_cm'])),
        ('손 · 아래팔 관통 / 소매 관통', lambda j: '%s cm / %s cm' % (lr(j['pen'], 'fore_hand_max_cm'), lr(j['pen'], 'upper_max_cm'))),
        ('손 · 아래팔과 몸 사이 최소 간격(가중치 0.9 이상 정점 — `rig308.py`의 검사 집합)', lambda j: lr(j['pen'], 'fore_hand_clear_cm', '%.2f') + ' cm'),
        ('엄격한 검사(감김수): 몸 안에 든 손 · 아래팔 정점 최대 수 · 감김수 최대(0.5 넘으면 안쪽)', lambda j: '%s개 · %s' % (lr(j['inside_strict'], 'inside_vertices_max', '%d'), lr(j['inside_strict'], 'winding_max', '%.2f'))),
        ('팔꿈치 고리(가중치 0.5 – 0.9, 위 집합이 빼는 정점)와 몸 사이 최소 간격', lambda j: lr(j['inside_strict'], 'ring_clear_min_cm', '%.2f') + ' cm'),
        ('엄격한 검사의 자가 확인(척추 관절 3곳 = 안쪽, 1 m 옆 = 바깥)', lambda j: '통과' if j['inside_strict']['self_test_ok'] else '**실패 — 위 두 줄은 무효**'),
        ('간격 탐색이 더 벌린 각(팔마다 따로, 아래팔 / 위팔은 그 %.0f %%)' % (D['agwi']['parameters']['arms']['clear_search']['upper_gain'] * 100), lambda j: '+%s°(%d번 시도, 조건 %s)' % (lr(j['arm_clearance_search'], 'chosen_extra_abd_deg', '%.0f') if False else ' / '.join('%.0f' % j['arm_clearance_search']['chosen_extra_abd_deg'][s] for s in S), len(j['arm_clearance_search']['tried']), '충족' if j['arm_clearance_search']['met'] else '미충족')),
        ('어깨 단면(바인드 대비 변화, 한계 −45 %)', lambda j: lr(j['arms'], 'shoulder_area_min_pct', '%.0f') + ' %'),
        ('팔꿈치 단면 · 팔꿈치 과신전 프레임', lambda j: '%s %% · %s' % (lr(j['arms'], 'elbow_area_min_pct', '%.0f'), lr(j['arms'], 'hyperextension_frames', '%d'))),
        ('위팔 비틀림 범위', lambda j: ' / '.join('%.0f…%.0f°' % (j['arms'][s]['twist_upper_min'], j['arms'][s]['twist_upper_max']) for s in S)),
    ])
    table('5. 루프 · 파일 · 왕복 검사', [
        ('첫 자세 = 마지막 자세(최대 본 회전 차) · 엉덩이 위치 차', lambda j: '%.4f° · %.4f cm' % (j['loop']['first_last_pose_deg'], j['loop']['hips_first_last_cm'])),
        ('루프를 닫는 걸음의 변화 / 안쪽 걸음의 최대 변화(작으면 속도가 이어진다)', lambda j: '%.1f° / %.1f°(%s)' % (j['loop']['seam_step_change_max_deg'], j['loop']['inner_step_change_max_deg'], j['loop']['seam_bone'])),
        ('한 프레임 최대 본 회전', lambda j: '%.1f°(%s)' % (j['loop']['max_step_deg'], j['loop']['max_step_bone'])),
        ('프레임 · fps · 테이크', lambda j: '0 – %d · %d · `%s`' % (j['run']['cycle_frames'], j['checks']['fps'], j['roundtrip']['take'][0])),
        ('왕복 검사: 레스트 · 자세 · 엉덩이 곡선 · 제자리', lambda j: '%.4f° / %.5f cm · %.4f° / %.5f cm · %.5f cm · %.4f cm → **%s**' % (j['roundtrip']['rest_max_rotation_deg'], j['roundtrip']['rest_max_position_cm'], j['roundtrip']['pose_max_rotation_deg'], j['roundtrip']['pose_max_position_cm'], j['roundtrip']['root_curve_max_position_cm'], j['roundtrip']['root_first_last_cm'], 'pass' if j['roundtrip']['pass'] else 'FAIL')),
        ('본 수 · 본 이름 · FBX 머리말(단위 · 축 · Armature 노드)', lambda j: '%d · %s · %s' % (j['roundtrip']['bones'], '같음' if j['roundtrip']['bone_names_equal'] else '다름', '걷기 수리본과 같음' if j['roundtrip']['header_equal_to_walk'] else '다름')),
        ('파일', lambda j: '%d KB · sha256 `%s…`' % (round(j['export']['bytes'] / 1024), j['export']['sha256'][:12])),
        ('바탕(걷기 수리본) sha256', lambda j: '`%s…`' % j['source_sha256'][:12]),
    ])
    out += ['## 5-2. 내보낸 FBX만 다시 읽어 잰 값 (`recheck_run308.py` — 짓는 자리의 자료를 쓰지 않는다)', '', '| 항목 | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)]
    def krow(label, fn): out.append('| %s | %s |' % (label, ' | '.join(fn(K[mid]) for mid, _ in IDS)))
    krow('파일 sha256 · 프레임 · 테이크', lambda k: '`%s…` · %d – %d · `%s`' % (k['sha256'][:12], k['frames'][0], k['frames'][1], k['take']))
    krow('뼈대: 본 수 · 이름 / 부모 / 순서 · 레스트 회전 차 · 레스트 위치 차 · 메시 레스트 차 · 가중치 차', lambda k: '%d · %s · %.1e · %.1e m · %.1e m · %.1e' % (k['structure']['bone_count'], '같음' if k['structure']['names_parents_order_equal'] else '**다름**', k['structure']['rest_rotation_max_abs_diff'], k['structure']['rest_translation_max_diff_m'], k['structure']['mesh_rest_max_diff_m'], k['structure']['weights_max_diff']))
    krow('엉덩이 말고 본 이동 최대 · 뼈 길이 변화 최대', lambda k: '%.1e m · %.1e m' % (k['channels']['non_hips_bone_translation_max_m'], k['channels']['bone_length_change_max_m']))
    krow('주기 · 보폭 · 분당 걸음', lambda k: '%.4f s · %.3f m · %.1f' % (k['gait']['seconds'], k['gait']['stride_m_at_clip_speed'], k['gait']['steps_per_minute']))
    krow('접지 프레임(왼 / 오른) · 두 발 다 뜬 프레임 · 두 발 다 닿은 프레임', lambda k: '%d / %d · %s · %d' % (len(k['gait']['feet']['Left']['contact_frames']), len(k['gait']['feet']['Right']['contact_frames']), k['gait']['flight_frames'], k['gait']['double_support_frames']))
    krow('접지 중 앞꿈치 속도(최소 · 평균 · 최대) · 옆 방향 최대', lambda k: ' / '.join('%.3f · %.3f · %.3f m/s' % tuple(k['gait']['feet'][s]['ball_speed_contact_m_s']) for s in S) + ' · ' + ' / '.join('%.3f' % k['gait']['feet'][s]['ball_lateral_speed_abs_max_m_s'] for s in S))
    krow('발바닥 최저 · 최고', lambda k: '%s cm · %s cm' % (lr(k['gait']['feet'], 'sole_min_cm', '%.2f'), lr(k['gait']['feet'], 'sole_max_cm')))
    krow('무릎: 접지 최소 · 최대 · 2차 차분 최대 · 허벅지 든 각 최대', lambda k: '%s° · %s° · %s° · %s°' % (lr(k['gait']['feet'], 'knee_min_contact_deg'), lr(k['gait']['feet'], 'knee_max_deg'), lr(k['gait']['feet'], 'knee_max_second_difference_deg'), lr(k['gait']['feet'], 'thigh_flex_max_deg')))
    krow('엉덩이: 출렁임 · 체공 중 수직 가속 · 순이동 · 앞뒤 평균', lambda k: '%.2f cm · %s m/s² · %.5f cm · %.2f cm' % (k['gait']['hips_bounce_cm'], k['gait']['hips_accel_flight_mean_m_s2'], k['gait']['hips_net_travel_cm'], k['gait']['hips_fore_aft_mean_cm']))
    krow('루프: 첫 = 마지막 회전 · 관절 · 메시 · 닫는 걸음 / 안쪽 최대', lambda k: '%.5f° · %.5f cm · %.5f cm · %.1f° / %.1f°' % (k['loop']['first_last_rotation_max_deg'], k['loop']['first_last_joint_max_cm'], k['loop']['first_last_mesh_max_cm'], k['loop']['seam_step_change_max_deg'], k['loop']['inner_step_change_max_deg']))
    krow('감김수 검사: 안쪽 정점 최대 · 감김수 최대 · 자가 확인', lambda k: '%s개 · %s · %s' % (lr(k['inside'], 'inside_vertices_max', '%d'), lr(k['inside'], 'winding_max', '%.2f'), '통과' if k['inside']['self_test_ok'] else '**실패**'))
    krow('팔꿈치 고리 간격 최소 · `rig308` 검사 집합(소매 포함) 간격 최소', lambda k: '%s cm · %s cm' % (lr(k['inside'], 'ring_clear_min_cm', '%.2f'), lr(k['inside'], 'test_set_clear_min_cm', '%.2f')))
    krow('판정(뼈대 · 루프 · 제자리 · 발바닥 · 미끄러짐 · 무릎 보호 · 안쪽 없음 · 엉덩이만 이동)', lambda k: '전부 통과' if all(k['pass'].values()) else '**밖: ' + ', '.join(n for n, v in k['pass'].items() if not v) + '**')
    out.append('')
    lim = json.loads((Path(__file__).resolve().parent / 'enemyrun308.json').read_text(encoding='utf-8'))['limits']
    out += ['## 6. 한계 대비 (오프라인)', '', '| 한계(TEST) | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)]
    def ok(j, k): return '충족' if j['against_limits']['pass'][k] else '**밖**'
    out.append('| 미끄러짐 ≤ %.0f %% · ≤ %.0f cm / 걸음 | %s |' % (lim['slide_ratio'] * 100, lim['slide_per_step_cm'], ' | '.join(ok(D[m], 'slide') for m, _ in IDS)))
    out.append('| 발바닥 최저 ≥ %.1f cm · 디딤 평균 ≤ %.1f cm | %s |' % (lim['sole_min_cm'], lim['stance_sole_mean_max_cm'], ' | '.join(ok(D[m], 'sole') for m, _ in IDS)))
    out.append('| 손 · 아래팔 관통 %.0f cm · 소매 ≤ %.1f cm | %s |' % (lim['pen_fore_hand_cm'], lim['pen_upper_cm'], ' | '.join(ok(D[m], 'penetration') for m, _ in IDS)))
    out.append('| 엄격한 검사(감김수): 몸 안에 든 손 · 아래팔 정점 0 | %s |' % ' | '.join(ok(D[m], 'inside_strict') for m, _ in IDS))
    out.append('| 내보낸 FBX 재측정(5-2) | %s |' % ' | '.join('전부 통과' if all(K[m]['pass'].values()) else '**밖**' for m, _ in IDS))
    out.append('| 디딤 무릎 굽힘 ≥ 보호값 | %s |' % ' | '.join(ok(D[m], 'knee_guard') for m, _ in IDS))
    out.append('| 루프 이음 ≤ %.2f° | %s |' % (lim['loop_deg'], ' | '.join(ok(D[m], 'loop') for m, _ in IDS)))
    out.append('| 어깨 단면 ≥ 바인드의 55 %%(= −45 %% 이상) | %s |' % ' | '.join('충족' if min(D[m]['arms'][s]['shoulder_area_min_pct'] for s in S) >= -45 else '**밖**' for m, _ in IDS))
    out.append('| 왕복 검사 | %s |' % ' | '.join('pass' if D[m]['roundtrip']['pass'] else '**FAIL**' for m, _ in IDS))
    a, c = D['agwi'], D['changgui']
    out += ['', '## 7. 두 달리기가 서로 다른가 (AC-R2)', '', '| 항목 | 아귀 | 창귀 |', '|---|---|---|',
            '| 걸음 빠르기 @%.1f m/s | 분당 %.0f | 분당 %.0f |' % (a['run']['clip_speed_design'], a['run']['steps_per_minute_at_clip_rate'], c['run']['steps_per_minute_at_clip_rate']),
            '| 보폭 | %.2f m | %.2f m |' % (a['run']['stride_m_design'], c['run']['stride_m_design']),
            '| 두 발이 다 뜬 프레임 | %d / %d | %d / %d |' % (a['run']['flight_frames_by_height'], a['run']['cycle_frames'], c['run']['flight_frames_by_height'], c['run']['cycle_frames']),
            '| 상하 출렁임 | %.1f cm | %.1f cm |' % (a['run']['bounce_cm'], c['run']['bounce_cm']),
            '| 숙임(평균, 엉덩이 → 윗가슴) · 설계 회전 합 | %.0f° · %.0f° | %.0f° · %.0f° |' % (a['run']['lean_deg'][1], a['parameters']['lean']['pelvis'] + a['parameters']['lean']['spine'], c['run']['lean_deg'][1], c['parameters']['lean']['pelvis'] + c['parameters']['lean']['spine']),
            '| 위팔 앞뒤 각(왼 / 오른) | %s | %s |' % tuple(' / '.join('%.0f…%.0f°' % (j['arms'][s]['swing_min'], j['arms'][s]['swing_max']) for s in S) for j in (a, c)),
            '| 팔꿈치(왼 / 오른) | %s | %s |' % tuple(' / '.join('%.0f–%.0f°' % (j['arms'][s]['elbow_min'], j['arms'][s]['elbow_max']) for s in S) for j in (a, c)),
            '| 손목 앞뒤 이동(왼 / 오른) | %s cm | %s cm |' % tuple(lr(j['arms'], 'wrist_fore_aft_cm', '%.0f') for j in (a, c)),
            '| 내딛는 발바닥 최고 | %.0f cm | %.0f cm |' % (max(a['run']['feet'][s]['sole_max_cm_cycle'] for s in S), max(c['run']['feet'][s]['sole_max_cm_cycle'] for s in S)),
            '| 디딤 한가운데 무릎(왼, 최대) | %.0f° | %.0f° |' % (max(a['ref_run']['Left']['knee'][i] for i in a['run']['feet']['Left']['contact_list']), max(c['ref_run']['Left']['knee'][i] for i in c['run']['feet']['Left']['contact_list'])),
            '| 머리 평균 상하 각 | %.1f° | %.1f° |' % (a['run']['head_pitch_deg'][1], c['run']['head_pitch_deg'][1]), '']
    text = '\n'.join(out) + '\n'
    (RUN / 'RUN_NUMBERS.md').write_text(text, encoding='utf-8', newline='\n'); print(text)


if __name__ == '__main__':
    main()
