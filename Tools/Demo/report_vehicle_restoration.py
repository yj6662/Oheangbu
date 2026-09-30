"""Preserve the bounded repair evidence and produce a local comparison page."""
from pathlib import Path
import json
import shutil
from collections import Counter

root = Path(__file__).resolve().parents[2]
out = root / 'Art/World/WorldMacro/Compact/VehicleRestoration'
acts = root / 'Art/Demo/ActsTerrain'
for name in ('before', 'after'):
    shutil.copy2(acts / f'Captures/vehicle_{name}.png', out / f'{name}.png')
    shutil.copy2(acts / f'capture_vehicle_{name}.json', out / f'capture_{name}.json')
shutil.copy2(acts / 'DemoEscortRuntimeChecks.json', out / 'runtime.json')
audit = json.loads((out / 'audit.json').read_text(encoding='utf-8-sig'))
runtime = json.loads((out / 'runtime.json').read_text(encoding='utf-8-sig'))
assert audit['status'] == 'MATCHES_SOURCE_PREFAB' and not audit['differences'] and not audit['failures']
assert runtime['status'] == 'PASS_BOUNDED_API_INTEGRATION' and runtime['cleaned']
counts = Counter(c['category'] for c in runtime['checks'])
report = f'''# 축소본 마석 자동차 배치 복원 — 2026-09-16

## 실제 원인과 수정

소환은 기존 차량을 활성화·이동하는 경로다. 올바른 MagicStoneCar_TEST 프리팹이 연결돼 있었으나, 전체 지도 축소 과정에서 차량의 하위 오브젝트까지 각각 월드 좌표를 압축했다. 앞뒤 바퀴가 겹치고 엔진이 차체 안으로 들어가며 등롱과 탑승 위치가 어긋났다.

- 제작 원형과 대조해 40개 하위 위치를 복원했다. 메시·재질·차량 크기·주행 프로필을 교체하지 않았다.
- 원본 데모 씬에서 승객·화물 소켓 2개의 로컬 위치를 읽어 추가 복원했다.
- 축소 코드는 차량 루트 위치만 변환하고 하위 부품·소켓의 상대 위치를 유지하도록 수정했다.
- 기존 축소 씬은 W_Demo_Compact_before_vehicle.unity에 백업했다. 새 Meshy 생성·유료 재생성·빌드는 하지 않았다.

## 검사

| 항목 | 결과 | 근거와 범위 |
|---|---|---|
| 제작 원형 배치 | 통과 | 하위 Transform {audit['matchedTransforms']}개 대조, 복원 후 위치 차이 0; 별도 승객·화물 소켓 2개 원본 일치 |
| 메시·재질 | 통과 | 메시 {audit['meshChecks']}개, 렌더러 재질 목록 {audit['materialChecks']}개 원형 일치 |
| 차량 월드 위치 | 통과 | 복원 전후 루트 위치 동일 |
| 컴파일 | 통과 | Unity 재컴파일 오류 0 |
| Play 연결 | 통과 | 실제 API 탑승 {runtime['boards']}회·하차 {runtime['exits']}회, 자동 회수 {runtime['recallReceipts']}회, 동행 짧은 이동 {runtime['shortWalkMetres']:.2f}m |
| 검사 격리 | 통과 | UUID 시험 저장, 런타임 전용 캠페인 fixture; 프로필·저장 접미사 복원 후 Edit mode 복귀 |
| 외관 비교 | 자료 확보 | 같은 카메라 1920×1080 Edit-scene 렌더. 실제 소환 순간의 Game View 캡처로 간주하지 않음 |
| 직접 키 입력·좌석 시야·전체 주행 | 미검증 | 이번 검사는 제한된 Play API 통합 검사 |
| 컨셉아트 완전 일치·최종 외형 | 사용자 검토 | 기존 제작 모델의 정상 배치로 복원한 결과이며 새 모델 제작이 아님 |

전체 검사 기록의 범주별 수: {dict(counts)}. 격리·fixture·파일 보존 검사까지 포함된 수이며 모두를 실제 플레이 행동 수로 세지 않는다.

## 근거 파일

- before.json: 최초 40개 위치 차이. rootAfter는 수정 전 기록의 기본값이므로 이동 증거가 아님.
- repair.json: 후속 승객·화물 소켓 2개 복원 기록.
- audit.json: 최종 차이 0, 루트 전후 동일.
- runtime.json: 마지막 Play 검사 전체 기록과 미검증 항목.
- before.png / after.png: 동일 카메라 외관 비교. 추가 소켓 복원은 내부 접점만 변경하므로 외관은 동일.

액트 진행 및 도로·내비게이션의 기존 미완료 항목은 별도 ActsTerrain 보고서대로 유지된다.
'''
(out / 'REPORT.md').write_text(report, encoding='utf-8')
html = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>마석 자동차 배치 복원</title><style>body{margin:0;background:#191d1c;color:#eee8d9;font:17px/1.7 system-ui,sans-serif}main{max-width:1440px;margin:auto;padding:38px 24px}h1{font-size:30px}p{max-width:960px;color:#c5c8bf}a{color:#bddacb}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}img{width:100%;display:block}figure{margin:0;background:#252c29}figcaption{padding:14px}aside{padding:20px;border-left:3px solid #99b7a4;background:#252c29;margin:24px 0}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style><main><h1>마석 자동차 — 축소 중 변형된 부품 배치 복원</h1><p>새 자동차 프리팹은 연결돼 있었습니다. 지도 축소가 차량 내부 좌표에도 적용돼 바퀴·엔진·등롱이 압축된 문제였습니다. 하위 위치 40개와 승객·화물 소켓 2개를 원형으로 복원했습니다.</p><div class="grid"><figure><a href="before.png"><img src="before.png" alt="복원 전 차량"></a><figcaption>수정 전 · 앞뒤 바퀴 겹침, 차체 안으로 들어간 엔진, 어긋난 등롱</figcaption></figure><figure><a href="after.png"><img src="after.png" alt="복원 후 차량"></a><figcaption>수정 후 · 제작 원형의 부품 간격과 앞 구동부 배치 복원</figcaption></figure></div><aside>같은 카메라의 1920×1080 Edit-scene 비교입니다. Play API 탑승·하차 각 2회와 자동 회수는 통과했습니다. 직접 입력·좌석 시야·전체 주행은 미검증입니다.</aside><p>축소 처리는 앞으로 차량 루트만 옮깁니다. 원본 제작 모델·주행 프로필을 보존했으며 새 빌드는 만들지 않았습니다. 컨셉아트에 맞춘 추가 모델 수정과 최종 외형 승인은 별도입니다.</p><p><a href="REPORT.md">검사 보고서</a> · <a href="audit.json">원형 대조 결과</a> · <a href="runtime.json">Play 검사 기록</a> · <a href="../../MagicStoneCar/REPORT.md">기존 자동차 제작 기록</a></p></main></html>'''
(out / 'REVIEW.html').write_text(html, encoding='utf-8')
status = root / 'Docs/PROJECT_STATUS.md'
line = '**2026-09-16 · 축소본 마석 자동차 복원:** 맵 축소가 차량 하위 위치까지 변환한 원인을 확인했다. 제작 프리팹 기준 40개 위치와 원본 데모의 승객·화물 소켓 2개를 복원하고, 향후 축소는 차량 루트만 이동하도록 수정했다. 하위88개·메시56개·재질목록58개 대조 후 차이0, Play API 탑승/하차 각2회·자동 회수 통과. 직접 입력·좌석 시야·전체 주행은 미검증. 새 모델 생성·빌드 없음. [전후 비교](../Art/World/WorldMacro/Compact/VehicleRestoration/REVIEW.html) · [보고서](../Art/World/WorldMacro/Compact/VehicleRestoration/REPORT.md).\n\n'
text = status.read_text(encoding='utf-8-sig')
if not text.startswith(line):
    status.write_text(line + text, encoding='utf-8')
print(json.dumps({'review':str(out/'REVIEW.html'),'audit':audit['status'],'runtime':runtime['status'],'categories':dict(counts)},ensure_ascii=False))
