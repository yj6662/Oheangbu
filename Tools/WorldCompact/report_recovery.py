"""Assemble the compact recovery evidence; does not modify Unity or user saves."""
import csv
import html
import json
import math
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Compact/Recovery'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

road = read(OUT.parent / 'road_physics_report.json')
assemblies = read(OUT / 'assemblies.json')
surfaces = read(OUT / 'surface_build.json')['rows']
nav = read(ROOT / 'Art/Demo/ActsTerrain/terrain_navigation_audit.json')
escort = read(ROOT / 'Art/Demo/ActsTerrain/DemoEscortRuntimeChecks.json')
for src, dest in [(OUT.parent/'road_physics_report.json', 'road_physics.json'),
                  (ROOT/'Art/Demo/ActsTerrain/terrain_navigation_audit.json', 'navigation.json'),
                  (ROOT/'Art/Demo/ActsTerrain/navigation_link_repairs.json', 'navigation_repair.json'),
                  (ROOT/'Art/Demo/ActsTerrain/DemoEscortRuntimeChecks.json', 'escort_regression.json')]:
    shutil.copy2(src, OUT / dest)

drive = [json.loads(line) for line in (OUT/'_drive_fe05b7f6195847b6882005f12a1435e1.jsonl').read_text().splitlines()]
occupied = [x for x in drive if x['occupied']]
def distance(a, b):
    return math.sqrt(sum((a[k]-b[k])**2 for k in ('x', 'y', 'z')))
drive_summary = {
    'status': 'USER_CONFIRMED_NATIVE_MOVEMENT',
    'scope': 'User confirmed actual movement; observer logged native Play state. Not proof of all driving cases.',
    'occupiedSamples': len(occupied),
    'poweredSamples': sum(x['torque'] > 0 and x['throttle'] > 0 for x in occupied),
    'maximumSpeedMps': max(x['speed'] for x in occupied),
    'maximumTorqueNm': max(x['torque'] for x in occupied),
    'firstPosition': occupied[0]['position'], 'lastPosition': occupied[-1]['position'],
    'displacementM': distance(occupied[0]['position'], occupied[-1]['position']),
    'unverified': ['reverse/brake/steer individually', 'V and menu/focus round trip', 'new game and restored-save driving',
                   'full escort journey', 'all road traction', 'moving performance']}
(OUT/'drive_summary.json').write_text(json.dumps(drive_summary, ensure_ascii=False, indent=2), encoding='utf-8')

with (OUT/'buildings.csv').open('w', encoding='utf-8-sig', newline='') as f:
    w=csv.writer(f);w.writerow(['assembly','status','source_width_m','source_height_m','source_depth_m',
        'current_width_m','current_height_m','current_depth_m','max_relative_position_error_m','rotation_error_deg','scale_error','notes'])
    for a in assemblies['assemblies']:
        w.writerow([a['path'],a['status'],*[a['sourceSize'][k] for k in ('x','y','z')],
                    *[a['currentSize'][k] for k in ('x','y','z')],a['maxPositionError'],a['maxRotationError'],a['maxScaleError'],' | '.join(a['findings'])])
with (OUT/'roads.csv').open('w', encoding='utf-8-sig', newline='') as f:
    fields=['id','status','carriage','width','length','expectedSamples','failedSamples','issueCount',
            'missingCenters','missingEdges','heightMismatches','longitudinalExceedances','crossSlopeExceedances',
            'maximumLongitudinalDegrees','maximumCrossSlopeDegrees']
    w=csv.DictWriter(f, fields, extrasaction='ignore');w.writeheader();w.writerows(road['routes'])

rows='\n'.join(f"| {r['id']} | {r['failedSamples']}/{r['expectedSamples']} | {r['missingCenters']} / {r['missingEdges']} | {r['longitudinalExceedances']} / {r['crossSlopeExceedances']} |" for r in road['routes'])
surface_rows='\n'.join(f"| {r['path'].split('/')[-1]} | {r['oldInvertedTriangles']} → {r['newInvertedTriangles']} | {r['unsupportedM2']:.3f} |" for r in surfaces)
report=f'''# 축소 맵 복구 — 적용 결과와 남은 결함

전체 복구는 미완료다. 차량 실제 움직임은 사용자 확인을 받았고, 관청 앞 검은 그림자·도로 표시 메시·내비게이션 단절 1곳을 수정했다. 도로 물리 표본 {road['totalFailedSamples']}개 실패는 남아 있다.

## 보존과 적용

- 현재 4×6km 지리와 콘텐츠·보상·저장을 유지했다. 디스크 원본 `before_disk.unity`와 미저장 열림 상태 `before_open_scene.unity`를 각각 보존했다.
- 수정 씬: `Assets/_Project/Scenes/World/W_Demo_Compact.unity`. 도로·마당은 `Assets/_Project/Art/World/WorldCompact/Recovery`의 파생 메시를 참조한다.
- 기준 원본 씬 SHA256: `{assemblies['sourceAfter']}`. 비교 전후 동일했다. 원본 메시·차량 모델·물리 프로필을 교체하지 않았다.
- 새 빌드·영상·자동 전체 완주는 만들지 않았다. 캡처는 1920×1080 한 장씩 수행했다. 마지막 도로 검사 시스템 커밋 {road['commitRatio']*100:.1f}%.

## 차량 — 사용자 확인 / 제한된 API 회귀 통과

호송 표현 컴포넌트가 일반 탑승도 호송으로 처리하면서 운전자 소유권을 끄던 경로를 수정했다. 호송 출발·진행 중에만 탑승을 제어하며, 기존 메뉴·사망 차단은 유지한다. 요청 가속·토크·접지·속도·운전자·입력 차단 상태를 읽는 진단 정보를 추가했다. 소환 검사에는 앞 엔진을 포함한 추가 차체 BoxCollider의 초기 겹침을 반영했다.

사용자가 실제 움직임을 확인했다. 관찰 로그에는 탑승 {len(occupied)}표본, 양수 구동 {drive_summary['poweredSamples']}표본, 최고속도 {drive_summary['maximumSpeedMps']:.3f}m/s, 최고 토크 {drive_summary['maximumTorqueNm']:.0f}Nm, 처음–마지막 탑승 위치 변위 {drive_summary['displacementM']:.2f}m가 기록됐다. 모든 조작을 시험자가 직접 수행했다는 뜻은 아니다.

호송 Play API 회귀: `{escort['status']}`. 검사 {len(escort['checks'])}개, 보호 파일 {len(escort['protectedFiles'])}개, 탑승 {escort['boards']}회·하차 {escort['exits']}회·회수 {escort['recallReceipts']}회. 짧은 NPC 이동 {escort['shortWalkMetres']:.2f}m. UUID 시험 저장·임시 프로필만 사용하고 종료 시 복원했다. 전체 검문·객주 인도 주행, 역주행·제동·V전환·포커스 왕복·실제 앱 재실행은 미검증이다.

## 검은 면 — 원인 특정과 비교 이미지 확인

관청 앞 카메라 위치 (882,137,240), 주시점 (830,133,176), FOV 60°에서 비교했다. 도로 표시·길 색상·SSAO·지형 그림자를 각각 끈 경우에도 큰 검은 삼각형이 남았고, 주광 그림자를 끄면 사라졌다. 각 진단 후 설정은 복원했다.

주광 방향 교차 검사에서 관청 마당 `Playtest_Village_Office/Courtyard`가 지면에서 1.137328m 떨어진 그림자 차폐면으로 확인됐다. 같은 외곽 안에서 실제 지형 삼각면에 맞춘 마당으로 교체한 뒤 교차 거리는 0.004757m로 줄었다. 지면과 중복되는 마당 표시 메시만 그림자 생성을 끄고, 실제 지형·건물·나무와 전역 조명·노출·색보정은 유지했다. `baseline.png`와 `after.png`에서 큰 검은 쐐기 면이 없어졌다. 화면 전체의 미술 품질 승인이나 다른 모든 위치의 그림자 검증을 의미하지 않는다.

## 도로 표시 — 뒤집힘 수정 / 물리 통행은 별도

도로 8개는 현재 중심선·폭을 따라 다시 만들고 실제 지지 삼각형으로 잘랐다. 급커브와 교차 영역의 겹침을 제거했다. 이전 전체 뒤집힘 349개(축소 후 새로 생긴 177개 포함)가 0개로 줄었다. 파이썬 출력→Unity 변환에서 수치적으로 붕괴하는 극소 면은 면적을 기록하고 제거했다. 길 표시에는 충돌체를 추가하지 않았다.

지지면이나 계획 높이가 맞지 않는 구간을 공중에 이어 붙이지 않았다. 아래 누락 면적은 남은 표시/지지 불일치이며, 경사 검사 597개와 다른 지표다.

| 표시 메시 | 뒤집힘 전 → 후 | 미충족 표시 면적 m² |
|---|---:|---:|
{surface_rows}

## 건축물 — 크기 보존 / 차이 4개 별도 분류

프리팹 내부·비활성·비렌더 Transform을 포함해 원본과 공통인 {assemblies['scaleChecks']:,}개에서 localScale·localRotation 변화가 없었다. 조립체 {len(assemblies['assemblies'])}개 중 240개는 상대 변환·메시·콜라이더 비교를 통과했다. 지리 메시를 제외한 실제 메시 bounds와 원본/현재 치수는 `buildings.csv`에 기록했다. 도로·마당을 포함한 전체 묶음 크기와 건물 본체 크기를 혼동하지 않는다.

- 이전 주막 매싱 3개 파츠: 이미 기록된 피벗 보정 파생본. 피벗은 0.137568m 다르지만 메시 크기와 조립체 기준 중심 오차는 0. 기존 `inn_pivot_repair.json`과 일치한다.
- C2 주막의 `Forecourt_Maru_FromExistingAsset`: 기존 제작 코드에서 꺼 둔 빈 조직 루트가 다르다. 현재 비활성으로 유지되며 주막 실제 메시 치수는 유지된다.
- 호송 `checkpoint_2/Parking`, `cargo_delivery/Parking`: 원본 대비 상대 XZ 약 1.021m / 0.948m 차이가 남는다. 건물 크기 변형은 아니지만 주차 소켓 위치 검증은 미완료이므로 정상으로 분류하지 않았다. 임의로 원복해 지지면 검사를 우회하지 않았다.

모든 출입구·계단·좌석의 실제 사람/차량 통과, 소환수·통보 드롭·국·수변·식생 접지·지도 표식의 전체 회귀는 미검증이다.

## 내비게이션과 도로 물리

`GroundSeam_27`의 연결 끝점을 같은 경계의 실제 지지·캡슐 여유가 있는 지점으로 옮겼다. 지형 전체를 깎거나 검사 기준을 완화하지 않았다. 수정 뒤 저장된 내비게이션 검사 {len(nav['checks'])}개 결과 `{nav['status']}`. 실제 AI 전체 이동을 대신하는 검사가 아니다.

37개 경로 {road['totalSamples']:,}표본을 재검사했다. 실패 {road['totalFailedSamples']}표본 / {road['totalIssues']}문제. 검사 시 물리 지형은 변경되지 않았고, 기준은 차량 14°·보행 30° 및 기존 통행 폭이다. 실패가 남은 경로는 정상으로 보고하지 않는다.

| 경로 | 실패 / 전체 | 중심 / 가장자리 지지 누락 | 종단 / 횡단 초과 |
|---|---:|---:|---:|
{rows}

## 다음 복구 대상

1. 폐광–주막의 급커브 실제 종단 경사와 MainPath 전폭 지지 누락.
2. 나무다리 앞 겹치는 두 보행로의 공통 높이, 객주 접점의 실제 바닥 연결.
3. 상경 가도·나머지 지역의 보호 구역 접점과 도로 전폭 지지. 수정 구간만 부착물·내비게이션을 갱신하고 같은 검사로 재집계.
4. 두 주차 소켓과 저장 복귀·하차·국·호송 대기점·지도 좌표의 실제 지형 연결.
5. 남은 도로 복구 후 실제 입력 조합과 이동 성능 확인. 이번 자료는 전체 통행 완료 또는 최종 빌드 승인 자료가 아니다.
'''
(OUT/'REPORT.md').write_text(report, encoding='utf-8')

def table(headers, values):
    return '<table><thead><tr>'+''.join('<th>'+html.escape(str(v))+'</th>' for v in headers)+'</tr></thead><tbody>'+''.join('<tr>'+''.join('<td>'+html.escape(str(v))+'</td>' for v in row)+'</tr>' for row in values)+'</tbody></table>'

page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>축소 맵 복구 검토</title><style>
body{margin:0;background:#151b1b;color:#edeade;font:16px/1.7 system-ui,sans-serif}main{max-width:1320px;margin:auto;padding:40px 28px}h1{font-size:32px}h2{margin-top:46px}a{color:#abd7ce}.note{background:#332d22;border-left:4px solid #ba9862;padding:18px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}img{width:100%;height:auto;background:#111}figure{margin:0}figcaption{padding:8px 0;color:#c2c6bd}table{width:100%;border-collapse:collapse;font-size:14px}th,td{text-align:left;padding:9px;border-bottom:1px solid #3e4845}th{color:#b3d6cc}details{margin:20px 0}small{color:#bec4bd}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style><main>
<p>오행부 · 축소 맵 · 복구 기록</p><h1>차량 주행과 관청 앞 검은 면</h1>
<p class="note">차량 실제 움직임은 사용자 확인. 관청 앞 그림자와 도로 표시 뒤집힘, 내비게이션 단절 1곳을 수정했습니다.<br>전체 복구는 미완료입니다. 도로 물리 실패 597개와 주차 소켓 2곳 등은 남은 검사로 구분합니다.</p>
<p><a href="REPORT.md">전체 기술 보고서</a> · <a href="roads.csv">37개 경로 결과 CSV</a> · <a href="buildings.csv">244개 조립체 비교 CSV</a></p>
<h2>같은 카메라의 수정 전후</h2><div class="grid"><figure><a href="baseline.png"><img src="baseline.png" alt="수정 전 관청 앞 검은 그림자"></a><figcaption>수정 전 · 지면 위에 떠 있는 마당 메시의 그림자</figcaption></figure><figure><a href="after.png"><img src="after.png" alt="지면에 맞춘 마당과 도로 수정 후"></a><figcaption>수정 후 · 실제 지면에 맞춘 마당, 지형 그림자는 유지</figcaption></figure></div>
<p>1920×1080, 카메라 (882,137,240) → (830,133,176), FOV 60°. 진단은 한 요소씩 분리하고 원래 설정으로 복원했습니다.</p>
<details><summary>그림자 발생원 분리 이미지</summary><div class="grid">'''
for img,label in [('roads','도로 표시만 끔 — 검은 면 남음'),('path','지형 길 색상만 끔 — 검은 면 남음'),('ao','SSAO만 끔 — 검은 면 남음'),('terrainShadows','지형 그림자만 끔 — 검은 면 남음'),('shadows','주광 그림자만 끔 — 검은 면 사라짐')]:
    page+=f'<figure><a href="{img}.png"><img loading="lazy" src="{img}.png" alt="{label}"></a><figcaption>{label}</figcaption></figure>'
page+='</div></details><h2>차량 검사</h2><p>일반 탑승에서 호송 로직이 운전자 소유권을 해제하던 문제를 수정했습니다. 앞 엔진 충돌체도 소환 빈자리 검사에 포함했습니다.</p>'
page+=table(['항목','결과'],[['실제 움직임','사용자 확인'],['관찰 로그 최대 속도',f"{drive_summary['maximumSpeedMps']:.3f}m/s"],['탑승 위치 변위',f"{drive_summary['displacementM']:.2f}m"],['왕소·화물 탑승/하차/회수','제한된 Play API 회귀 통과'],['전체 도로 주행·후진/제동/V·메뉴/포커스 조합','미검증']])
page+='<p><a href="drive_summary.json">주행 기록</a> · <a href="escort_regression.json">호송 회귀 원자료</a></p><h2>도로 표시와 건물</h2><p>도로 8개와 관청 마당을 실제 지면 삼각면에 맞췄습니다. 공통 Transform 7,172개의 크기·회전은 동일합니다. 조립체 240개는 비교 통과, 4개는 피벗·비활성 루트·주차 소켓 차이로 보고서에 따로 기록했습니다.</p>'
page+=table(['표시 메시','뒤집힘 전 → 후','미충족 표시 면적 m²'],[[r['path'].split('/')[-1],f"{r['oldInvertedTriangles']} → {r['newInvertedTriangles']}",f"{r['unsupportedM2']:.3f}"] for r in surfaces])
page+='<h2>남은 실제 통행 결함</h2><p>18,174개 표본 중 597개 실패, 총 642개 문제. 표시 메시 수정과 물리 통행은 별개입니다. 저장 내비게이션 66개 검사는 통과했으나, 아래 경사·지지면을 정상 통행으로 판정하지 않았습니다.</p>'
page+=table(['경로','실패/전체','최대 종단°','최대 횡단°'],[[r['id'],f"{r['failedSamples']}/{r['expectedSamples']}",f"{r['maximumLongitudinalDegrees']:.2f}",f"{r['maximumCrossSlopeDegrees']:.2f}"] for r in road['routes']])
page+='<p><a href="road_physics.json">도로 물리 원자료</a> · <a href="navigation.json">내비게이션 검사</a> · <a href="assemblies.json">건축물 원자료</a></p><p><small>원본·열린 씬 백업과 사용자 저장을 보존했습니다. 새 빌드·영상·자동 전체 완주는 만들지 않았습니다. 메뉴·차량 전체 조합, 저장 재실행·모든 소켓·이동 성능은 미검증입니다.</small></p></main></html>'
(OUT/'REVIEW.html').write_text(page, encoding='utf-8')
print(json.dumps({'report':str(OUT/'REPORT.md'),'review':str(OUT/'REVIEW.html'),'remainingRoadSamples':road['totalFailedSamples'], 'drive':drive_summary},ensure_ascii=False))
