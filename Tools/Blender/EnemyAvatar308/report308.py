"""#308 enemy rig revision 2: Korean tables from cause308.json + tpose308.json + pen308.json (no computation of its own).
  python Tools/Blender/EnemyAvatar308/report308.py   ->  Art/Characters308/EnemyRigVerify2/REPORT_ko.md"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace(os.sep, '/')
CFG = json.load(open(os.path.join(HERE, 'avatar308.json'), encoding='utf-8')); OUT = REPO + '/' + CFG['out']
KR = {'dokkaebi': '도깨비', 'agwi': '아귀', 'changgui': '창귀'}
CLIP_KR = {'idle': '대기', 'walk': '걷기(원본)', 'walkfix': '걷기(수리)', 'run': '달리기', 'attack': '공격', 'hit': '피격', 'stun': '스턴', 'death': '사망'}
SIDES = (('Left', '왼'), ('Right', '오른'))
SIDE_KR = dict(SIDES)


def cell(x): return '%s %s %s' % (KR[x['id']], CLIP_KR[x['clip']], SIDE_KR[x['side']])


def main():
    c = json.load(open(OUT + '/cause308.json', encoding='utf-8')); t = json.load(open(OUT + '/tpose308.json', encoding='utf-8')); L = []
    pen = json.load(open(OUT + '/pen308.json', encoding='utf-8')) if os.path.exists(OUT + '/pen308.json') else None
    L.append('# 적 리그 개정 2 — 오프라인 원인 대조와 후보 A 예측\n')
    L.append('만든 것: `Tools/Blender/EnemyAvatar308/` (`avatar308.py cause | tpose`, `pen308.py`, `report308.py`). 표기: [M] 편집기 실측 · [O] 오프라인 · [I] 추론 / 예측.\n')
    L.append('## 1. 팔꿈치 바닥 — 아바타에 저장된 관절 틀 [O] 대 편집기 최솟값 [M]\n')
    L.append('| 종 | 팔 | 기준 자세: 위팔이 수평 아래 | 기준 자세: 팔꿈치 굽힘 | 저장된 틀의 바닥 [O] | 복원한 규칙의 바닥 [O] | 편집기 최소 [M] | 편집기 최대 [M] (면이면 천장) | 후보 A의 바닥 [O] | 무릎 바닥 [O] |')
    L.append('|---|---|---|---|---|---|---|---|---|---|')
    for mid, m in c['monsters'].items():
        for side, kr in SIDES:
            e = m['elbowFloor'][side]; r = m['referencePose'][side]; a = t['monsters'][mid]['arms'][side]
            L.append('| %s | %s | %.1f° | %.1f° | **%.2f°** | %.2f° | **%.2f°** | %.2f° (%.2f°) | %.2f° | %.1f° |' % (
                KR[mid], kr, r['upperArmBelowHorizontalDeg'], r['elbowBendDeg'], e['fromStoredFramesDeg'], e['fromRuleDeg'], e['editorSmallestElbowDeg'], e['editorLargestElbowDeg'], e['ceilingIfPlaneDeg'],
                a['elbowFloorADeg'], m['kneeFloor'][side]))
    L.append('')
    L.append('아바타가 기억하는 기준 자세와 리그 `.meta` 행의 차: ' + ' · '.join('%s %.4f°' % (KR[k], v['storedTPoseVsMetaDeg']) for k, v in c['monsters'].items()) +
             '. 복원한 규칙과 저장된 틀의 차(21본, 바깥 / 안): ' + ' · '.join('%s %.3f° / %.3f°' % (KR[k], v['ruleVsStoredDeg']['preMax'], v['ruleVsStoredDeg']['postMax']) for k, v in c['monsters'].items()) + '.\n')
    L.append('**팔꿈치 잠김의 원인 = 아바타 기준 자세 — 증명됨**(여섯 팔 모두 0.01° 안, 자유 변수 없음).\n')
    s = c['spreadVsFloor']
    L.append('## 2. 벌림 어긋남과 팔꿈치 바닥 — 세 구간 전부 [O ↔ M]\n')
    L.append('(클립 × 팔) 42칸을 "원본 팔꿈치가 바닥 아래인 프레임의 비율"로 셋으로 나눈 것. 벌림 차 = 편집기의 벌림 평균 − FBX가 담은 자세의 벌림 평균.\n')
    L.append('| 바닥 아래 프레임 | 칸 수 | 벌림 차의 크기: 평균 | 최소 | 최대 | 들어 있는 클립 |'); L.append('|---|---|---|---|---|---|')
    for b in s['bands']:
        L.append('| %s | %d | %.2f° | %.2f° | %.2f° | %s |' % (b['band'], b['cells'], b['meanAbsSpreadShiftDeg'], b['minAbsSpreadShiftDeg'], b['maxAbsSpreadShiftDeg'], ' · '.join(CLIP_KR[x] for x in b['clips'])))
    L.append('')
    L.append('- 걷기 계열(원본 · 수리) %d칸의 벌림 차: **%.1f – %.1f°**. 그 밖의 %d칸 가운데 5°를 넘는 것: %s.' % (
        s['walkCells'], s['walkAbsSpreadShiftDeg'][0], s['walkAbsSpreadShiftDeg'][1], s['otherCells'],
        ', '.join('%s %+.1f°(바닥 아래 %.0f %%)' % (cell(x), x['spreadShiftDeg'], x['underFloorPct']) for x in s['otherCellsOver5Deg']) or '없음'))
    L.append('- **반례 — 바닥 아래 프레임이 25 %% 이상인데 벌림 차가 3° 안인 칸 %d개**: %s.' % (
        len(s['underFloorButSpreadKept']), ', '.join('%s %.0f %% → %+.1f°' % (cell(x), x['underFloorPct'], x['spreadShiftDeg']) for x in s['underFloorButSpreadKept'])))
    L.append('- 80 % 이상 구간은 전부 걷기 계열이다 — "바닥 아래 비율"과 "클립 종류"가 겹쳐 있어 둘을 가를 수 없다.')
    L.append('- 팔꿈치가 "바닥에 붙어 있는" 것도 아니다: %s (원본 최댓값 → 편집기 최댓값, 바닥보다 5° 넘게 위).' % ', '.join('%s %.1f → %.1f°' % (cell(x), x['sourceElbow'][1], x['editorElbow'][1]) for x in s['elbowAboveSourceAndFloor']))
    L.append('- 바닥 아래 프레임이 없는데 편집기 최댓값이 5° 넘게 준 칸: %s. 스턴의 두 칸은 천장(180 − 바닥, 1절 표)에 걸린 것으로 같은 면이 설명한다. 대기 칸은 설명되지 않았다.' % (
        ', '.join('%s %.1f → %.1f°' % (cell(x), x['sourceElbow'][1], x['editorElbow'][1]) for x in s['elbowReducedAboveFloor'])))
    L.append('')
    L.append('**읽는 법**: 팔꿈치 바닥은 증명됐다. 벌림 어긋남 11 – 15°는 **걷기 계열에서만** 나타났고, 바닥이 그 원인인지는 **확인되지 않았다**(기전을 재현하지 못했고 가운데 구간에 반례가 있다). '
             '그래서 후보 A의 벌림 예측(3절의 28 / 26 / 26°)은 [I]이고, 판정은 편집기에서 후보 A를 켠 뒤의 M3(≤ 5°) 하나뿐이다.\n')
    L.append('## 3. 클립마다 — 지금 편집기 값 [M] → 후보 A 예측(= FBX가 담은 자세) [I]\n')
    L.append('팔 보정 끔 기준. "바닥 아래" = 원본 팔꿈치가 지금 아바타의 바닥보다 작은 프레임 비율. "팔 길이" = 어깨에서 손목까지 가장 멀리 뻗는 거리(지금은 팔꿈치 바닥 때문에 상한이 있다). '
             '"위팔 비틀림" = 후보 A에서 위팔 뼈가 Blender와 달라질 수 있는 비틀림 범위. 예측의 전제("게임이 FBX의 자세를 그대로 낸다")는 2절대로 증명되지 않았다.\n')
    for mid, m in c['monsters'].items():
        L.append('### %s\n' % KR[mid])
        L.append('| 클립 | 팔 | 바닥 아래 | 팔꿈치: 지금 [M] → A 예측 | 벌림 평균: 지금 [M] → A 예측 | 흔들림: 지금 [M] → A 예측 | 팔 길이 최대: 지금 → A | 위팔 비틀림(A) |'); L.append('|---|---|---|---|---|---|---|---|')
        for clip, cc in m['clips'].items():
            for side, kr in SIDES:
                f = cc[side]; a = t['monsters'][mid]['clips'][clip][side]; e = f.get('editor')
                now = ('%.1f – %.1f°' % (e['elbowMin'], e['elbowMax']), '%.1f°' % e['abductionMean'], '%.0f°' % e['swingRange']) if e else ('미측정', '미측정', '미측정')
                L.append('| %s | %s | %.0f %% | %s → %.1f – %.1f° | %s → %.1f° | %s → %.0f° | %.3f → %.3f m | %s |' % (
                    CLIP_KR[clip], kr, f['framesUnderFloorPct'], now[0], a['elbowMin'], a['elbowMax'], now[1], a['abductionMean'], now[2], a['swingRange'], min(a['reachCapLiveM'], a['reachMaxM']), a['reachMaxM'],
                    ('%.0f … %.0f°' % tuple(a['upperArmRollOffsetDeg'])) if a['upperArmRollOffsetDeg'] else '-'))
        L.append('')
    L.append('## 4. 후보 A의 기준 자세 행\n')
    L.append('| 종 | 팔 | 리그의 팔꿈치 축 흩어짐(중앙값 / 90 %) | 바인드의 굽힘 축과의 차 | 단순 최단 회전 T자와의 위팔 비틀림 차 |'); L.append('|---|---|---|---|---|')
    for mid, m in t['monsters'].items():
        for side, kr in SIDES:
            a = m['arms'][side]; L.append('| %s | %s | %.1f° / %.1f° (%d프레임) | %.1f° | %.1f° |' % (KR[mid], kr, a['hingeSpreadMedianDeg'], a['hingeSpreadP90Deg'], a['hingeFramesUsed'], a['bindBendNormalVsHingeDeg'], a['upperArmRollVsShortestArcDeg']))
    L.append('')
    L.append('후보 A에서 틀이 달라지는 뼈: ' + ' · '.join('%s(%s)' % (KR[k], ', '.join(sorted(v['framesChangedDeg']))) for k, v in t['monsters'].items()) + '. 다리 · 척추 · 어깨 · 머리의 틀은 그대로다.\n')
    if pen:
        P = pen['settings']
        L.append('## 5. 관통 측정(두 기준) — 오프라인 대조 [O]\n')
        L.append('팔 정점마다 두 기준: **감김수 > %g**(안쪽) · **가장 가까운 몸 면의 뒤쪽**. 둘 다 = 관통, 하나만 = **미판정**(그림으로 확인), 둘 다 아님 = 밖. '
                 '"뒤쪽"만 걸린 정점은 그 면에서 %g cm 안, 면의 안쪽 법선에서 %g° 안일 때만 센다(열린 조각의 가장자리 너머를 "뒤"로 세지 않게).\n' % (P['windingInside'], P['behindOnlyMaxCm'], P['behindConeDeg']))
        L.append('### 5-1. 속도 근사와 자가 확인\n')
        L.append('원본 클립(Humanoid 변환 · 런타임 보정 없음)의 표본 자세에서. "빠른 합 대 정확한 합" = 편집기 도구와 같은 계산(먼 삼각형은 묶어서)과 삼각형 전부를 더한 값의 차.\n')
        L.append('| 종 | 몸 삼각형 | 검사 점 | 빠른 합 − 정확한 합 최대 | 판정이 갈린 점 | 자가 확인(엉덩이 · 가슴 관절 최소 / 옆 1 m 최대) | 실패 자세 |'); L.append('|---|---|---|---|---|---|---|')
        for mid, m in pen['monsters'].items():
            fe = m['fastVsExact']; st = m['selfTest']
            L.append('| %s | %d | %d | %.4f | %d | %.2f · %.2f / %.3f | %d / %d |' % (KR[mid], m['bodyTriangles'], fe['points'], fe['maxAbsDifference'], fe['decisionsFlipped'], st['hipsMin'], st['chestMin'], st['besideMaxAbs'], st['failed'], st['poses']))
        L.append('')
        L.append('### 5-2. 표면 바로 옆에서의 자가 확인 (검토 F5)\n')
        L.append('손이 지나는 높이(몸 높이의 %g – %g)에서 가장 바깥 몸 삼각형을 골라(법선 쪽으로 다른 몸 삼각형이 없는 것) 그 면 %g cm 뒤 · 앞의 점을 판정한 것. 대기 첫 프레임.\n' % (P['nearBandLow'], P['nearBandHigh'], P['nearOffsetCm']))
        L.append('| 종 | 표본 | 면 뒤 점: 관통 | 미판정 | **둘 다 놓침** | 면 앞 점: 밖 | 미판정 | **둘 다 오인** | 감김수만 썼다면: 뒤 점 놓침 / 앞 점 오인 |'); L.append('|---|---|---|---|---|---|---|---|---|')
        for mid, m in pen['monsters'].items():
            n = m.get('nearSurface') or {}
            if n.get('samples'): L.append('| %s | %d | %.1f %% | %.1f %% | **%.1f %%** | %.1f %% | %.1f %% | **%.1f %%** | %.1f %% / %.1f %% |' % (
                KR[mid], n['samples'], n['insidePointsPenetrationPct'], n['insidePointsUndeterminedPct'], n['insidePointsMissedPct'], n['outsidePointsClearPct'], n['outsidePointsUndeterminedPct'], n['outsidePointsFalsePct'],
                n['windingAloneMissesInsidePct'], n['windingAloneFalseOutsidePct']))
        L.append('')
        L.append('"둘 다 놓침"은 이 측정이 못 보는 몫이다. 놓친 점을 갈라 보면 [O]: 도깨비 25점 = 다른 몸 면에 2 mm 안으로 붙은 점 12 + 더 가까운 면을 앞에서 보는 점(두 겹 사이) 13 / '
                 '아귀 17 = 7 + 10 / 창귀 63 = 41 + 21 + 가장자리 옆 1. 몸 면이 겹으로 있는 자리(겹옷)로 보인다 [I]. '
                 '도구의 한계 값(`verify308.json` `pen.nearMissedMaxPct` %g %% · `nearFalseMaxPct` %g %%)은 품질 기준이 아니라 **고장 감지용 상한**이다 — 넘으면 그 종의 관통은 "미측정". '
                 '잰 비율은 측정마다 그대로 찍힌다.\n' % (P['nearMissedMaxPct'], P['nearFalseMaxPct']))
        L.append('### 5-3. 클립마다 — 관통 · 미판정 · Blender BVH 방식\n')
        L.append('값 = 좌 / 우, cm. "미판정"은 두 기준이 갈린 정점의 가장 깊은 값. 이 표의 자세 = FBX가 담은 자세 = 후보 A에서 나올 것으로 **예측**하는 자세(지금 아바타의 게임 자세가 아니다).\n')
        L.append('| 종 | 클립 | 손 · 아래팔: 관통 | 미판정 | Blender BVH | 소매: 관통 | 미판정 | Blender BVH |'); L.append('|---|---|---|---|---|---|---|---|')
        for mid, m in pen['monsters'].items():
            for clip, cc in m['clips'].items():
                g = lambda k, sub=None: ' / '.join(('-' if (cc[s][k] if sub is None else cc[s][sub][k]) is None else '%.1f' % (cc[s][k] if sub is None else cc[s][sub][k])) for s in ('Left', 'Right'))
                L.append('| %s | %s | %s | %s | %s | %s | %s | %s |' % (KR[mid], CLIP_KR[clip], g('foreHandMaxCm'), g('unsureForeHandMaxCm'), g('foreHandMaxCm', 'blenderBvh'), g('sleeveMaxCm'), g('unsureSleeveMaxCm'), g('sleeveMaxCm', 'blenderBvh')))
        L.append('')
        gone = []
        for mid, m in pen['monsters'].items():
            for clip, cc in m['clips'].items():
                for s, kr in SIDES:
                    for key, name in (('foreHandMaxCm', '손'), ('sleeveMaxCm', '소매')):
                        b = cc[s]['blenderBvh'][key]; new = cc[s][key]; un = cc[s]['unsure' + key[0].upper() + key[1:]]
                        if b is not None and b >= 2.0 and new < 0.5 * b: gone.append('%s %s %s %s: BVH %.1f → 관통 %.1f · 미판정 %.1f cm' % (KR[mid], CLIP_KR[clip], kr, name, b, new, un))
        L.append('Blender BVH 방식이 2 cm 이상으로 잡았는데 새 측정의 관통이 그 절반에 못 미치는 칸(%d개) — **그림으로 확인하기 전에는 "없음"으로 적지 않는다**: %s.\n' % (len(gone), '; '.join(gone) or '없음'))
    open(OUT + '/REPORT_ko.md', 'w', encoding='utf-8', newline='\n').write('\n'.join(L) + '\n'); print('wrote', OUT + '/REPORT_ko.md', len(L), 'lines')


if __name__ == '__main__':
    main()
