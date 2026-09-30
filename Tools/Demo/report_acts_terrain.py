"""Publish local evidence without running Unity, changing saves or building a player."""
from pathlib import Path
import collections
import hashlib
import html
import json
import re

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Demo/ActsTerrain'
def read(name):
    return json.loads((OUT / name).read_text(encoding='utf-8-sig'))
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def count_actual(name):
    return sum(c.get('category') == 'ACTUAL' and c.get('status') == 'PASS' for c in read(name)['checks'])

baseline = read('Baseline/manifest.json')
protected = [x for x in baseline if x['path'].endswith('W_Demo_Campaign.unity') or '/Art/Demo/Foundation/' in x['path']]
preservation = [{'path': x['path'], 'before': x['sha256'], 'after': sha(ROOT / x['path']),
                 'status': 'PASS' if sha(ROOT / x['path']) == x['sha256'] else 'FAIL'} for x in protected]
(OUT / 'preservation.json').write_text(json.dumps(preservation, ensure_ascii=False, indent=2), encoding='utf-8')
profile = (ROOT / 'Oheangbu/Assets/_Project/Art/World/WorldCompact/ActsTerrain/Campaign_TEST.asset').read_text(encoding='utf-8-sig')
flags = re.findall(r'^    Implemented: (\d)', profile, re.M)
road = read('road_physics_after.json')
before = read('road_physics_before.json')
nav = read('terrain_navigation_audit.json')
checks = read('technical_checks.json')
runtime = read('runtime_checks.json')
shortcut = read('shortcut_physics.json')
routes = {r['id']: r for r in road['routes']}
main = routes['Content.MainPath']
items = [
    ('통과', '컴파일·진행·경제·v6→v7 이행', f"컴파일 오류 0. Edit 모드 검사 {len(checks['checks'])}개. 840통보 필수 흐름, 국 선택/늦은 획득, 호송340, 간이 정비 조건, 실패한 저장 제안의 미확정/재시도 검사."),
    ('통과', '여울·익수·환경 사망 Play API', f"{len(runtime['checks'])}개. 실제 수면 질의, 감속, 발 잠김, 仁 우회, 마른 지면 통보 드롭·재사망, 실제 차량 탑승 후 익수·하차 복구·회수. 시험 위치와 시간은 진단 도구가 설정."),
    ('통과', '호송의 짧은 Play API 연결', f"실제 경로 {count_actual('DemoEscortRuntimeChecks.json')}개. 왕소 이동/운반/탑승2회/하차2회/자동 회수/보행 위치 저장. 사전 캠페인 단계는 메모리 시험 fixture; 검문·인도 전체 여정의 증거가 아님."),
    ('통과', '남문 Play API 연결', f"실제 경로 {count_actual('DemoSouthGateRuntimeChecks.json')}개. 장수 피해·처치·보상·문 열림·종료 UI. 인도 선행 상태는 시험 fixture. 발 접지 외형은 미검증."),
    ('통과', '국 하강 발판의 국부 물리', f"{len(shortcut['checks'])}개 지지/캡슐 표본. 기존 1.8m 발판을 재사용하고 1.54m 통행 구간을 검사. 겹쳤던 추가 경사면은 비활성화, 원형 보존. 전면2.2m 단차·국 사용 증거 유지."),
    ('통과', '지도 경로 갱신', '축소본의 야외 MainPath 벡터만 실제 수정 경로에 맞췄다. 국 지름길은 상단 발견 저장 뒤에만 표시. 기존 종이/지리/실내/발견 상태 유지.'),
    ('실패', '전체 도로 통행 기준', f"37개 경로 {road['totalSamples']:,}표본 중 {road['totalFailedSamples']}실패 / {road['totalIssues']}문제. 이전 {before['totalFailedSamples']}/{before['totalIssues']}. 전폭 지지·경사 결함이 남아 전체 통행 통과로 처리하지 않음."),
    ('실패', '내비게이션 이음 한 곳', f"{len(nav['checks'])}통과, {len(nav['failures'])}실패. GroundSeam_27은 실제 지면/장애물 조건을 충족하는 연결을 확보하지 못함. 실패 위치를 삭제하거나 검사 폭을 줄여 통과 처리하지 않음."),
    ('미검증', '완주·가독성·난도·성능', '선택 탐험 없이 남문 완주, 실제 입력 운전, 전체 호송 중 환경 사망/저장 실패 복구, 30초 보스 재도전, 여울 시각 구분, 이동 중 프레임타임은 미검증. 영상·자동 완주·새 빌드 없음.'),
]
route_ids = ['Trail_Inn_Logging','Trail_Logging_Deep','Trail_Deep_Dragon','Road_Inn_Post','Road_Post_Merchant','Road_Merchant_Pass','Road_Pass_SouthPost','Road_SouthPost_Gate','Content.MainPath']
route_rows = '\n'.join(f"| {k} | {routes[k]['failedSamples']} | {routes[k]['missingCenters']} | {routes[k]['missingEdges']} |" for k in route_ids)
report = f'''# 액트 진행·실제 지형 통행 — 구현 중간 보고

2026-09-16. **계획 전체 완료가 아니다.** 수정 후보는 `Assets/_Project/Scenes/World/W_Demo_Compact.unity`와 `WorldCompact/ActsTerrain` 전용 TEST 데이터다. 필수 연결과 지형 결함이 남아 기존 활성 {sum(x == '1' for x in flags)}/{len(flags)}단계를 유지했다. 액트3의 나머지6단계는 준비 데이터와 시험 연결만 있으며 일반 진행에는 아직 열지 않았다. 원본 데모와 기존 빌드는 보존했다.

## 적용한 진행 구조

| 액트 | 필수 흐름 | 단계 보상 |
|---|---|---|
| 1 | 관청 의뢰 → 폐광 조사 → 아전 보고 → 금표 주막 휴식 | 보고100 |
| 2 | 정담·왕소 → 벌목장 → 금극목 학습 → 청룡 | 80+40+160, ㄱ·국·仁 |
| 3 | 호송 출발 → 검문2곳 → 인도 → 남문 | 60+60+220+120; 일반 진행 활성 보류 |

필수 합계840, 호송340. 선택 보상 폐광160·벌목장160·국80. 국 재탐험은 필수 진행 계산에서 분리했고 실제 국 사용 증거는 유지한다. 벌목장 해결 증거는 적의 휴식 재생성과 분리했다. 현재 목적 권역은 전체 지도에만 표시하고, 주요 대화에 이유·장소·행동 안내를 연결했다. 상시 목표 문구를 제거했다.

액트4 적로(ㄴ·눈·禮), 5 철옹(ㅅ·숫·義), 6 현강(ㅇ·웅·智), 7 황경귀환(ㅁ·뭄·信), 8 잔월회·사찰, 9 현신·결말은 예약 데이터만 기록했다. 신규 활성 콘텐츠가 아니다.

## 적용한 지형 규칙

실제 수면6개의65,554삼각형으로 수면 범위·높이를 질의한다. 여울0.55m, 감속최대35%, 익수대기0.65초, 낙사6m는 별도 TEST 프로필이다. 물 위에 있다는 이유만으로 사망시키지 않고 발의 잠김 깊이를 사용한다. 최근 접지 높이와 안전한 마른 위치를 분리했으며, 임시 국 발판/수중 바닥은 영구 복귀 지점으로 거부한다. 환경 사망은仁/회피를 우회한다. 탑승 중 사망에서는 소유권 해제·차량 회수와 기존 호송 복구 경로를 사용한다.

깊은 물 내비게이션 제외 볼륨7,088개와20개 파생 NavMesh를 제작했다. 수면 보행 콜라이더는 추가하지 않았다. 기존 깊은 하상 표본은 확인했지만 모든 여울·교량의 가독성,64개 하상 미검출 표본의 현장 분류와 전 지역 지상 이동은 완료하지 않았다.

## 검사 결과

| 구분 | 항목 | 근거와 범위 |
|---|---|---|
''' + '\n'.join(f'| {a} | {b} | {c} |' for a,b,c in items) + f'''

## 실제 수정과 남은 통행 문제

폐광 출구의 오래된 MainPath가 산 안쪽으로 돌아가던 부분을 실제 지면·캡슐 여유가 있는 외곽 경로로 수정했다. 전 구간 중심 지지 누락은15→{main['missingCenters']}으로 감소했다. 다만 기존4.1m 전폭 기준 가장자리 누락은{main['missingEdges']}개로, 좁은1.54m 보행 통로 검사와 전폭 통과를 구분한다. 전체 MainPath {main['failedSamples']}표본은 아직 실패다.

Terrain_050의 도로 가장자리 보간2곳을 국부 세분화했고 Road_Post_Merchant 실패가7→5로 감소했다. 첫 검문 쉼터 복귀점을 실제 마른 지면으로 보정했다. 왕소의 적재·출발 대기점을 건물 기단 바깥으로 옮기고 접근 경유점을 추가했다. 잘못 걸을 수 있던 마루를 NavMesh에서 제외한 뒤 보행 저장까지 통과했다. 기존 이음7개를 실제 지면에 맞췄지만 GroundSeam_27은 여전히 실패다.

| 경로 | 실패 표본 | 중심 지지 누락 | 가장자리 누락 |
|---|---:|---:|---:|
{route_rows}

다음 구현 순서: 남은 상경 가도의 횡경사·접점 수리 → GroundSeam_27 물리 연결 → 검문/인도 실제 연결과 호송 환경 사망·쓰기 실패 재시도 → 여울/교량/점프·긴 내리막 현장 검사 → 액트3 활성 → 최종 통합 검증 후 빌드1회. 실패를 의도된 장애물로 취급하거나 이미 통과한 것으로 보고하지 않는다.

## 이미지와 보존

`Captures/cave.png`, `guk.png`는 1920×1080 편집 씬 지형 진단이다. 실제 입력 Play 화면이나 최종 아트 승인이 아니다. `relay.png`, `seam.png`는 나뭇잎에 가려 시각 검증 자료로 불충분하며 별도 실패 자료로만 남긴다. `guk_before_reuse.png`는 폐기한 중복 경사면 상태다.

원본 씬·Foundation 에셋 해시 검사 {len(preservation)}개 중 {sum(x['status']=='PASS' for x in preservation)}개 일치. 실제 검사마다 사용자 저장과 원본 파일 보존 여부를 별도로 검증했다. v6 완료 ID·지갑·강화·국·仁·화물을 보존하고 변경 보상의 소급 차액은 지급하지 않는다.

검사 원장은 같은 폴더의 `technical_checks.json`, `runtime_checks.json`, `DemoEscortRuntimeChecks.json`, `DemoSouthGateRuntimeChecks.json`, `shortcut_physics.json`, `road_physics_after.json`, `terrain_navigation_audit.json`, `preservation.json`에 있다. 실패한 이전 시도와 기준 자료는 별도로 남겼다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
rows=''.join(f'<tr><td class="{a}">{a}</td><td>{html.escape(b)}</td><td>{html.escape(c)}</td></tr>' for a,b,c in items)
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>액트·지형 구현 검토</title><style>
body{margin:auto;max-width:1180px;padding:40px 24px;background:#ebe6da;color:#262b27;font:16px/1.7 system-ui,sans-serif}h1{font-size:36px}h2{margin-top:40px}a{color:#365f55}table{border-collapse:collapse;width:100%;background:#f6f3eb}td,th{padding:14px;border-bottom:1px solid #cdc7b9;text-align:left;vertical-align:top}td:first-child{white-space:nowrap}.통과{color:#287052}.실패{color:#a3332f}.미검증{color:#8a682a}.notice{border-left:5px solid #a3332f;padding:16px;background:#f6f3eb}.flow{display:flex;gap:12px;flex-wrap:wrap}.flow div{flex:1;min-width:220px;padding:20px;background:#f6f3eb}.hold{border:1px dashed #9c6c42}img{width:100%;height:auto}figure{margin:24px 0}small,figcaption{color:#62675f}summary{cursor:pointer;font-weight:600}
</style><h1>액트별 성장과 실제 지형 통행</h1><p class="notice"><b>구현 중 · 빌드 후보 미완료</b><br>진행·보상·환경 사망의 첫 연결을 적용했습니다. 상경길 통행 결함이 남아 액트3 일반 진행은 아직 활성화하지 않았습니다.</p>
<div class="flow"><div><b>1 · 폐광의 흔적</b><p>관청 → 조사 → 보고 → 주막</p>보고100통보 · 첫 정비</div><div><b>2 · 물든 숲</b><p>벌목장 → 금극목 → 청룡</p>80+40+160 · 국 재탐험은 선택</div><div class="hold"><b>3 · 봉인된 화물</b><p>검문 → 인도 → 남문</p>60+60+220+120 · 활성 보류</div></div><p>액트4~9는 연결과 핵심 보상만 예약했습니다. 원본 데모·기존 빌드 보존. 영상과 자동 완주는 제작하지 않았습니다.</p>
<h2>검사 결과</h2><table><thead><tr><th>상태</th><th>항목</th><th>실행 범위</th></tr></thead><tbody>'''+rows+'''</tbody></table>
<h2>실제 씬 진단</h2><p>1920×1080 정지 렌더입니다. 플레이 완주나 최종 아트 승인 자료가 아닙니다.</p><figure><img src="Captures/cave.png"><figcaption>폐광 외곽 — 오래된 중심 경로를 실제 바닥을 따라 수정. 길 전체 전폭 검사는 아직 실패가 남았습니다.</figcaption></figure><figure><img src="Captures/guk.png"><figcaption>국 재탐험 — 기존 상단·하강 발판 재사용, 중복 경사면 제거. 통행 캡슐141표본 통과, 수동 이동·외형 미검증.</figcaption></figure><details><summary>폐기 시안과 시야 가림 자료</summary><img src="Captures/guk_before_reuse.png"><p>기존 발판과 겹친 추가 경사면은 비활성화했습니다.</p><img src="Captures/relay.png"><p>나뭇잎에 가려져 왕소·차량의 시각 검증으로 사용하지 않았습니다.</p></details>
<h2>남은 작업</h2><p>상경 가도·내비 이음 수리, 검문과 인도 및 호송 사망 복구 연결 검사, 여울 가독성과 실제 입력 이동, 최종 통합 검증이 남았습니다. 이 검사 전에는 빌드를 만들지 않습니다.</p><p><a href="REPORT.md">전체 보고서</a> · <a href="road_physics_after.json">도로 표본 원장</a> · <a href="terrain_navigation_audit.json">내비게이션 원장</a> · <a href="preservation.json">원본 보존 확인</a></p></html>'''
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps({'report':str(OUT/'REPORT.md'),'preserved':len(preservation),'preservationFailures':[x['path'] for x in preservation if x['status']!='PASS'],'activeStages':sum(x=='1' for x in flags),'totalStages':len(flags)},ensure_ascii=False))
