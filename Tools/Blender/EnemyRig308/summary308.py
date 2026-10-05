#!/usr/bin/env python3
"""#308 enemy rig: one Korean table of the offline numbers (reads fix/<id>_measure.json and fix/<id>_fix.json, writes
Art/Characters308/DokkaebiVerify/fix/ENEMYRIG_NUMBERS.md). Plain Python, no Blender.

  python Tools/Blender/EnemyRig308/summary308.py            write the table and print it
Every number is [O] offline (Blender headless on the source FBX). Rows marked (sim) simulate the game's arm relax on the source
bones: the Unity humanoid retarget is not reproduced offline."""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
FIX = ROOT / 'Art/Characters308/DokkaebiVerify/fix'
IDS = [('dokkaebi', '도깨비'), ('agwi', '아귀'), ('changgui', '창귀')]
S = ('Left', 'Right')


def load(mid):
    m = json.loads((FIX / ('%s_measure.json' % mid)).read_text(encoding='utf-8'))
    f = json.loads((FIX / ('%s_fix.json' % mid)).read_text(encoding='utf-8'))
    return m, f


def lr(d, key, fmt='%.1f'):
    return ' / '.join(fmt % d[s][key] if d[s].get(key) is not None else '-' for s in S)


def pen(p):
    return '손 %s cm · 소매 %s cm' % (' / '.join('%.1f' % p[s]['fore_hand_max_cm'] for s in S), ' / '.join('%.1f' % p[s]['upper_max_cm'] for s in S))


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    out = ['# 적 리그 검증 — 오프라인 수치 [O] (도깨비 · 아귀 · 창귀)', '',
           '전부 Blender 5.0.1 헤드리스로 원본 FBX를 직접 잰 값이다. `(모사)` = 게임의 팔 내림 식을 원본 본에 그대로 건 것(Unity Humanoid 변환은 재현하지 못함 → 게임 화면은 [I]).',
           '왼쪽 / 오른쪽 순서. 편집기 · Play 실측 0건.', '']
    data = {mid: load(mid) for mid, _ in IDS}
    # ---- walk: source
    out += ['## 1. 걷기 원본 (수리 전)', '', '| 항목 | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)]
    def row(label, fn): out.append('| %s | %s |' % (label, ' | '.join(fn(*data[mid]) for mid, _ in IDS)))
    row('키(레스트)', lambda m, f: '%.2f m' % m['rig']['height_m'])
    row('위팔 벌림 평균', lambda m, f: lr(f['before']['arms'], 'abduction_mean') + '°')
    row('위팔 벌림 최소 – 최대', lambda m, f: ' / '.join('%.0f–%.0f' % (f['before']['arms'][s]['abduction_min'], f['before']['arms'][s]['abduction_max']) for s in S) + '°')
    row('팔꿈치 굽힘 범위', lambda m, f: ' / '.join('%.0f–%.0f' % (f['before']['arms'][s]['elbow_min'], f['before']['arms'][s]['elbow_max']) for s in S) + '°')
    row('팔꿈치 범위 좌우 차', lambda m, f: '%.1f°' % f['before']['arms']['asymmetry']['elbow_range_L_minus_R'])
    row('손목 앞뒤 이동', lambda m, f: lr(f['before']['arms'], 'wrist_fore_aft_cm', '%.0f') + ' cm')
    row('팔–몸 관통(보정 없음)', lambda m, f: pen(f['before']['pen']))
    row('팔–몸 관통(지금 보정 .55 / .5, 모사)', lambda m, f: pen(f['before_with_legacy_relax']['pen']))
    row('디딘 발 속도 v_clip', lambda m, f: '%.3f m/s' % f['before']['gait']['stance_speed_m_s'])
    row('보폭(한 주기)', lambda m, f: '%.2f m' % f['before']['gait']['stride_m'])
    row('지금(WMPS 1.5) 미끄러짐', lambda m, f: '%.0f %% · 한 걸음 %.1f cm' % (f['before']['gait']['slide']['1.500']['ratio'] * 100, f['before']['gait']['slide']['1.500']['per_step_cm']))
    row('지금 분당 걸음(배우 속도별)', lambda m, f: ', '.join('%s m/s → %.0f' % (k, v) for k, v in f['before']['gait']['slide']['1.500']['cadence_steps_per_min'].items()))
    row('맞춘 뒤 분당 걸음', lambda m, f: ', '.join('%s m/s → %.0f' % (k, v) for k, v in list(f['before']['gait_if_matched'].values())[0]['cadence_steps_per_min'].items()))
    row('디딤 발바닥 높이(지면 0)', lambda m, f: ' / '.join('%.1f…%.1f' % (f['before']['gait'][s]['sole_min_cm_stance'], f['before']['gait'][s]['sole_max_cm_stance']) for s in S) + ' cm')
    row('디딤 비율', lambda m, f: lr(f['before']['gait'], 'stance_pct', '%.0f') + ' %')
    # ---- walk: repaired
    out += ['', '## 2. 걷기 수리본 `walk_fix308` (Blender 안 측정)', '', '| 항목 | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)]
    row('벌림 목표 평균(탐색 결과)', lambda m, f: ('%.0f°' % f['parameters']['abduction']['target_mean_deg']) if f['parameters']['abduction']['target_mean_deg'] is not None else '그대로(한계를 만족하는 값 없음)')
    row('위팔 벌림 평균', lambda m, f: lr(f['after']['arms'], 'abduction_mean') + '°')
    row('위팔 벌림 최소 – 최대', lambda m, f: ' / '.join('%.0f–%.0f' % (f['after']['arms'][s]['abduction_min'], f['after']['arms'][s]['abduction_max']) for s in S) + '°')
    row('팔꿈치 굽힘 범위', lambda m, f: ' / '.join('%.0f–%.0f' % (f['after']['arms'][s]['elbow_min'], f['after']['arms'][s]['elbow_max']) for s in S) + '°')
    row('팔꿈치 범위 좌우 차', lambda m, f: '%.1f°' % f['after']['arms']['asymmetry']['elbow_range_L_minus_R'])
    row('손목 앞뒤 이동(원본 대비)', lambda m, f: ' / '.join('%.0f cm (%.0f %%)' % (f['after']['arms'][s]['wrist_fore_aft_cm'], 100 * f['after']['arms'][s]['wrist_fore_aft_cm'] / f['before']['arms'][s]['wrist_fore_aft_cm']) for s in S))
    row('팔–몸 관통(보정 없음)', lambda m, f: pen(f['after']['pen']))
    row('손 · 아래팔 최소 간격', lambda m, f: ' / '.join(('%.1f' % f['after']['pen'][s]['fore_hand_clear_cm']) if f['after']['pen'][s]['fore_hand_clear_cm'] is not None else '> 25' for s in S) + ' cm')
    row('엉덩이 올림', lambda m, f: '%.2f cm' % f['parameters']['feet']['lift_cm'])
    row('디딤 발바닥 높이(평균)', lambda m, f: ' / '.join('%.1f…%.1f (%.1f)' % (f['after']['gait'][s]['sole_min_cm_stance'], f['after']['gait'][s]['sole_max_cm_stance'], f['after']['gait'][s]['sole_mean_cm_stance']) for s in S) + ' cm')
    row('발바닥 최저 – 최고(전 주기, 수리 전 → 뒤)', lambda m, f: ' / '.join('%.1f…%.1f → %.1f…%.1f' % (f['before']['sole_cm_whole_cycle'][s][0], f['before']['sole_cm_whole_cycle'][s][1], f['after']['sole_cm_whole_cycle'][s][0], f['after']['sole_cm_whole_cycle'][s][1]) for s in S) + ' cm')
    row('발 올림 보정(프레임 수 · 최대)', lambda m, f: ' / '.join('%d프레임 · %.1f cm' % (f['parameters']['feet']['per_foot'][s]['corrected_frames'], f['parameters']['feet']['per_foot'][s]['correction_max_cm']) for s in S))
    row('무릎 굽힘 최소 – 최대(수리 전 → 뒤)', lambda m, f: ' / '.join('%.0f–%.0f → %.0f–%.0f' % (f['before']['knee'][s]['min'], f['before']['knee'][s]['max'], f['after']['knee'][s]['min'], f['after']['knee'][s]['max']) for s in S) + '°')
    row('무릎 프레임 간 최대 변화(수리 전 → 뒤)', lambda m, f: ' / '.join('%.0f → %.0f' % (f['before']['knee'][s]['max_step_deg'], f['after']['knee'][s]['max_step_deg']) for s in S) + '°')
    row('무릎 2차 차분 최대(수리 전 → 뒤)', lambda m, f: ' / '.join('%.0f → %.0f' % (f['before']['knee'][s]['max_second_difference_deg'], f['after']['knee'][s]['max_second_difference_deg']) for s in S) + '°')
    row('디딘 발 속도 v_clip', lambda m, f: '%.3f m/s' % f['after']['gait']['stance_speed_m_s'])
    row('맞춘 뒤 분당 걸음', lambda m, f: ', '.join('%s m/s → %.0f' % (k, v) for k, v in list(f['after']['gait_if_matched'].values())[0]['cadence_steps_per_min'].items()))
    row('클립 안 앞뒤 미끄러짐(걸음당)', lambda m, f: lr(f['before']['gait'], 'slide_fore_aft_in_clip_cm') + ' → ' + lr(f['after']['gait'], 'slide_fore_aft_in_clip_cm') + ' cm')
    row('루프 이음(첫 = 끝)', lambda m, f: '%.3f°' % f['after']['loop_first_last_deg'])
    row('고치지 않은 본의 변화', lambda m, f: '%.4f°' % f['checks']['untouched_bones_max_rotation_change_deg'])
    row('왕복 검사(레스트 · 자세)', lambda m, f: ('pass' if f['roundtrip']['pass'] else 'FAIL') + ' — 레스트 %.4f° / %.5f cm, 자세 %.4f° / %.5f cm' % (f['roundtrip']['rest_max_rotation_deg'], f['roundtrip']['rest_max_position_cm'], f['roundtrip']['pose_max_rotation_deg'], f['roundtrip']['pose_max_position_cm']))
    row('파일', lambda m, f: '%d KB · sha %s' % (f['export']['bytes'] // 1024, f['export']['sha256'][:12]))
    # ---- idle / others
    out += ['', '## 3. 대기 · 다른 클립', '', '| 항목 | ' + ' | '.join(k for _, k in IDS) + ' |', '|---|' + '---|' * len(IDS)]
    row('대기 원본(스케일 뺌) 관통', lambda m, f: pen(m['clips']['idle']['pen']))
    row('대기 + 지금 보정 .55 / .5 (모사)', lambda m, f: pen(m['clips']['idle+relax_legacy']['pen']) + ' · 손 %s프레임' % ' / '.join('%d/%d' % (m['clips']['idle+relax_legacy']['pen'][s]['fore_hand_frames'], m['clips']['idle+relax_legacy']['pen'][s]['frames']) for s in S))
    row('대기 제안값(팔 / 팔꿈치)', lambda m, f: '%.2f / %.2f' % tuple(m['idle_sweep']['proposed']))
    row('대기 + 제안값 (모사)', lambda m, f: pen(m['clips']['idle+relax_proposed']['pen']))
    row('대기 테이크의 본 스케일', lambda m, f: ', '.join('%s %.3f' % kv for kv in m['clips']['idle']['scale_or_offset_bones'].items()) or '없음')
    for role, kr in (('attack', '공격'), ('hit', '피격'), ('stun', '스턴'), ('death', '사망')):
        row(kr + ' 관통', lambda m, f, role=role: pen(m['clips'][role]['pen']))
    row('사망 첫 프레임 회전(최대 본)', lambda m, f: '%.1f° (%s) — %s' % (m['clips']['death']['pop']['first_step_max_deg'], m['clips']['death']['pop']['first_step_bone'], '튐' if m['clips']['death']['pop']['pop'] else '한계 안'))
    row('사망: 25° 넘는 본 수', lambda m, f: '%d' % len(m['clips']['death']['pop']['bones_over_limit']))
    row('팔꿈치 과신전 프레임(전 클립)', lambda m, f: '%d' % sum(m['clips'][r]['arms'][s]['hyperextension_frames'] for r in ('walk', 'idle', 'attack', 'hit', 'stun', 'death') for s in S))
    row('팔꿈치 단면 최소(공격)', lambda m, f: lr(m['clips']['attack']['arms'], 'elbow_area_min_pct', '%.0f') + ' %')
    row('위팔 비틀림 범위(걷기)', lambda m, f: ' / '.join('%.0f…%.0f' % (m['clips']['walk']['arms'][s]['twist_upper_min'], m['clips']['walk']['arms'][s]['twist_upper_max']) for s in S) + '°')
    text = '\n'.join(out) + '\n'
    (FIX / 'ENEMYRIG_NUMBERS.md').write_text(text, encoding='utf-8')
    print(text)


if __name__ == '__main__':
    main()
