"""Review of the actual compact dark-foreground revision; no rendered mockups."""
from pathlib import Path
import json, html, re, collections
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Compact/InkLandscape/Darker'
def read(name): return json.loads((OUT/name).read_text(encoding='utf-8-sig'))
views=read('views.json')['views']
names={'inn':'금표 주막 앞','ground_detail':'발밑 흙결','mountain_path':'산길 조망','office':'시작 관청','DeepForest':'청림 심부','SouthGate':'황경 남문','Jeokro':'적로','Cheolong':'철옹','Hyeongang':'현강 · 수변','Hwanggyeong':'황경 분지','boundary_Hwanggyeong':'황경 경계','boundary_Cheongrim':'청림 경계','boundary_Jeokro':'적로 경계','boundary_Cheolong':'철옹 경계','boundary_Hyeongang':'현강 경계'}
for v in views:
    v['label']=names[v['id']]
    for prefix in ('before','final'):
        with Image.open(OUT/f'{prefix}_{v["id"]}.png') as im: assert im.size==(1920,1080)
checks=read('checks.json'); idem=read('idempotence_final.json'); colliders=read('active_colliders.json')
assert checks['status']==idem['status']=='PASS'
audits=[json.loads(p.read_text(encoding='utf-8-sig')) for p in sorted(OUT.glob('retention_cell_*.json'))]
assert all(a['status']=='PASS_RETENTION_IDENTITY' for a in audits)
counts=collections.defaultdict(lambda:[0,0])
for audit in audits:
    for row in audit['layers']:
        counts[row['layer']][0]+=row['before'];counts[row['layer']][1]+=row['after']
extra=read('supplement_retention.json')['rows']; static=read('static_retention.json')['rows']
assert all(r['paired'] and r['prototype']!='UNCLASSIFIED_PRESERVED' for r in extra)
species={'tree':'일반 나무','shrub':'관목','grass':'풀','low grass':'낮은 풀','ground cover':'지면 피복','rock':'바위'}
count_rows=''.join(f'<tr><td>{species[k]}</td><td>{v[0]:,}</td><td>{v[1]:,}</td><td>{v[1]/v[0]:.1%}</td></tr>' for k,v in counts.items())
count_md='\n'.join(f'|{species[k]}|{v[0]:,}|{v[1]:,}|{v[1]/v[0]:.1%}|' for k,v in counts.items())
a=read('performance_after.json'); b=read('performance_before.json')
assert a['start']==b['start'] and a['width']==b['width']==1920 and a['height']==b['height']==1080
assert a['status']==b['status']=='MEASURED_DIAGNOSTIC_CAMERA'
phase_names={'cold':'초기 생성','stationary':'정지','rotation':'회전','first_visit':'첫 접근','return_visit':'재방문','returned_stationary':'복귀 정지'}
perf_rows=[]; perf_md=[]
for old,new in zip(b['phases'],a['phases']):
    cells=[phase_names[new['phase']]]+[f'{old[k]:.2f} → {new[k]:.2f}' for k in ('cpuP50','cpuP95','cpuP99','gpuP50','gpuP95','gpuP99')]+[f'{old["maxDraws"]} → {new["maxDraws"]}']
    perf_rows.append('<tr>'+''.join('<td>'+x+'</td>' for x in cells)+'</tr>');perf_md.append('|'+ '|'.join(cells)+'|')
commits=[float(re.search(r'Commit: ([\d.]+)',p.read_text()).group(1)) for p in OUT.glob('*_timing.txt')]
peak=max(commits+[a['commit'],b['commit']]); memory=max(p['maxMemoryMiB'] for p in a['phases'])
cpu_min=min(p['cpuP50'] for p in a['phases']); cpu_max=max(p['cpuP50'] for p in a['phases']);gpu_min=min(p['gpuP50'] for p in a['phases']);gpu_max=max(p['gpuP50'] for p in a['phases'])
static_n=sum(x['after'] for x in static); old_extra=sum(x['before'] for x in extra); new_extra=sum(x['after'] for x in extra)
receipt={'status':'IMPLEMENTED_TEST_VISUAL_REVIEW_PENDING','views':len(views),'resolution':[1920,1080],'cellsSampled':len(audits),'population':dict(counts),'supplementaryBefore':old_extra,'supplementaryAfter':new_extra,'staticBefore':len(static),'staticAfter':static_n,'commitPeak':peak,'cpuMedianRange':[cpu_min,cpu_max],'gpuMedianRange':[gpu_min,gpu_max],'cpu120fpsTargetMet':False,'video':False,'build':False}
(OUT/'summary.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
report=f'''# 짙은 근경과 성긴 식생 — 축소본 TEST

2026-09-16 · `W_Demo_Compact` 적용 완료. 최종 외형 승인 대기.

## 적용 결과

- 지면 먹–한지 혼합 `.14~.30`, 흙길 `.17~.29`, 산 `.06~.18`, 산 경사 전환12~37°. 가까운 산 기본색을 처음부터 짙게 두고, 추가 어두운 결만30~350m에서 줄인다. 150~900m/900~2200m 공통 대기는 지형·수목·카드에 한 번 적용한다.
- 지면 기본 채도와 추가 지역색 기여도 각각.2 유지. 직전판 하늘·전역 노출은 그대로다. 주막 마당과 관청 지면 포함205개 파생 재질을 새 전용 폴더에 작성했다.
- 풀/낮은 풀/피복40%, 관목50%, 일반 나무65%, 바위100% 잔존을 기존 열린 지면 마스크 뒤에 적용한다. 심부/수변도 동일하게 감축한다. 위치·회전·크기·ID·LOD 거리·바람·생성 예산은 보존한다.
- 정적 자동 배치 나무149그루 중{static_n}그루 유지. 같은 root 아래 모든 LOD를 함께 전환한다. KCISA 신목 및 보존 지정 FixedPlacements는 유지한다. 일반 정적 나무149그루에는 원래 Collider가 없었다.
- 추가 군락643패킷은 {old_extra:,}→{new_extra:,}개. 근경·원경 파트 모두 같은 저장 인덱스로 감축했다. 기존4패킷의 오래된 Count를 실제 배열 길이로 정정했다(원본 자료 보존).

## 실제 수량 · 동일성 검사

15개 검토 위치에 대응하는 중복 제외13개256m 셀 표본이다. 전 세계 개체 수 전수조사는 아니다. 고정 해시 선택이므로 소규모 집단은 목표 비율과 통계적으로 다를 수 있다.

|종류|직전판|이번판|실제 잔존율|
|---|---:|---:|---:|
{count_md}

- 각 셀의 원본 후보와 유지 후보 ID/행렬 비교,16개64m 분할의 재생성 일치, 나무 근경/원경 ID 일치 통과. 변경·새 ID·중복·분할 누락0.
- 활성 충돌체 {colliders['active']}개/2셀은 현재 표시 후보와 유지 후보에 모두 대응, 위치 불일치0. 전 구역의 실제 보행 검증은 아니다.
- Transform23,879개의 TRS·메시·충돌 정의 보존. 원본 재질/프로필/식생/지리의 파일 해시 보존. 기존 여백 마스크/고도/서식지 유지.
- 고정 산 표면/법선/명암121조합 × 거리0~2600m, 총314,721개 명도 표본에서 감소0. 실제 전환부의 mip·SSAO·그림자 cascade 표본 변화까지 모든 화면 픽셀의 단조성을 보증하는 검사는 아니다.
- Unity 셰이더 오류0. 기존 `SurfaceVertex` 잠재적 미초기화 경고1개는 남아 있다.
- Unity 임포트 정리 후 재적용208개 에셋 바이트 변경0. 씬은 실시간 제출 계측값5개 외 동일. 첫 반복의 원시 비교에는 재질3개 임포트 정리와 진단값 차이가 있었으며 `idempotence.json`에 남겼다. 최종 동일성 결과는 `idempotence_final.json`이다.

## 실제 Play 성능

Unity {a['unityVersion']} / {a['gpuDevice']} / {a['cpuDevice'].strip()}. 원래 Game View2560×1440, 비교 렌더 타깃1920×1080, 품질0, VSync0. 두 판 모두 동일 주막 카메라에서49초(정지·회전·224m 진단 카메라 접근/복귀,14m/s), 사전 생성 없음. 플레이어 자동 보행이나 영상은 수행하지 않았다.

|구간|CPU 중앙값 ms|CPU p95|CPU p99|GPU 중앙값 ms|GPU p95|GPU p99|최대 주 식생 제출 패킷|
|---|---:|---:|---:|---:|---:|---:|---:|
{chr(10).join(perf_md)}

이번판 CPU 중앙값{cpu_min:.2f}~{cpu_max:.2f}ms, GPU{gpu_min:.2f}~{gpu_max:.2f}ms. **GPU 부담은 감소했으나 CPU는 이전 측정보다 높고8.33ms 목표 미달이다. 120fps 달성으로 보고하지 않는다.** 생성 완료 시점과 상주 패킷 수가 달라 일부 제출량은 증가했다. 두 실행의 차이를 오직 셰이더나 감축의 인과 효과로 단정하지 않는다. 최대 측정 메모리{memory:.0f}MiB. 세부 생성/제출/충돌 시간·GC·메모리는 두 performance JSON의 구간별 값에 남겼다.

## 시각·잔여 사항

-15개 동일 구도 전후1080p 이미지 제공. 바닥이 회갈색으로 낮아지고 가까운 산이 더 짙으며, 먼 능선은 종이색 대기로 이어진다. 유지 식생 사이 여백이 넓어졌다.
- 산의 뾰족한 윤곽·삼각 면·도로 가장자리의 기존 형상은 바꾸지 않았다. 이를 재질 개선 완료로 해결된 것처럼 보고하지 않는다.
- 사용자의 최종 농도/밀도 판단, 직접 입력 경계 통과/보행, 모든 시설의 통행 검사는 미검증. 이번 작업은 지형·건물·퀘스트·보상·저장을 변경하지 않았다.
- 캡처 순차 수행, 계측·캡처 기록상 최대 시스템 커밋{peak:.1%}.85% 중단 규칙 적용. 영상·새 빌드 없음.

## 파일

- 전용 프로필/시트/재질: `Assets/_Project/Art/World/WorldCompact/InkLandscape/Darker/`
- 현재 씬: `Assets/_Project/Scenes/World/W_Demo_Compact.unity`
- 직전 디스크/열린 씬: `before_disk.unity`, `before_open.unity`. 추가 군락의 Unity 참조 원본은 `Darker/BaselineSupplement.asset`.
- 적용/검사 명령: `chapter3:compact:recovery:ink-dark:apply`, `verify`, `retention:<셀번호>`. 반복 적용은 보존한 직전판에서 다시 파생한다.
- 이미지 `before_*.png`, 최종 `final_*.png`. 중간 `after_*.png`도 보존했다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
options=''.join(f'<option value="{v["id"]}">{v["label"]}</option>' for v in views)
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>짙은 근경 · 성긴 식생</title>
<style>*{box-sizing:border-box}body{margin:0;background:#181b19;color:#e5e0d3;font:16px/1.65 system-ui,"Malgun Gothic",sans-serif}main{max-width:1560px;margin:auto;padding:32px}h1{font-size:34px;margin:0}p{max-width:1040px}a{color:#c7d7c4}nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:22px 0}button,select{font:inherit;background:#303a32;color:#fff;border:1px solid #657666;border-radius:5px;padding:8px 16px;cursor:pointer}button:hover{background:#455343}.tag{color:#d5c094;font-size:13px;letter-spacing:.06em}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}img{width:100%;display:block;aspect-ratio:16/9;background:#111;object-fit:contain}figcaption{padding:8px 0;color:#b9b9aa}.single{display:none}.note{background:#252c26;border-left:3px solid #a9ac8a;padding:12px 18px}.facts{display:flex;gap:24px;flex-wrap:wrap;margin:22px 0}.facts div{background:#252c26;padding:12px 20px}.facts strong{display:block;font-size:22px}details{margin:18px 0;padding:18px;background:#222822}summary{font-size:19px;cursor:pointer}.scroll{overflow:auto}table{border-collapse:collapse;width:100%;margin:16px 0;font-size:14px}th,td{padding:9px 12px;border-bottom:1px solid #424b40;text-align:left;white-space:nowrap}th{color:#c3d0b9}code{color:#d3c6a9}footer{margin-top:32px;color:#aeb2a7}@media(max-width:800px){main{padding:16px}.pair{grid-template-columns:1fr}h1{font-size:27px}}</style>
<main><div class="tag">W_DEMO_COMPACT · 2026.09.16 · TEST</div><h1>어두운 발밑, 짙은 앞산, 옅은 뒷산</h1>
<p>지면을 회갈색으로 낮추고 가까운 산부터 먹 농도를 높였습니다. 심부와 수변까지 식생을 줄이되, 남은 개체의 위치와 지역별 수종은 유지했습니다.</p>
<nav><label for="view">비교 구도</label><select id="view">OPTIONS</select><button id="mode">크게 비교</button><button id="swap" hidden>적용 전 보기</button><a href="REPORT.md">검사 보고서</a><a href="../REVIEW.html">이전 판</a></nav>
<div class="pair" id="pair"><figure><img id="before" alt="직전 수정판"><figcaption>적용 전 · 직전 수묵담채 수정판</figcaption></figure><figure><img id="after" alt="짙은 근경과 식생 감축 적용"><figcaption>적용 후 · 새 전용 TEST 프로필</figcaption></figure></div>
<div class="single" id="single"><img id="large" alt="선택 비교 화면"><figcaption id="label">적용 후</figcaption></div><p id="position"></p>
<div class="facts"><div><strong>.14~.30</strong>지면 먹–한지 범위</div><div><strong>.06~.18</strong>산 먹–한지 범위</div><div><strong>12~37°</strong>산자락 농담 전환</div><div><strong>풀40 · 관목50 · 나무65%</strong>직전판 대비 목표 잔존율</div></div>
<p class="note">GPU 비용은 줄었지만 CPU 8.33ms 목표는 아직 미달입니다. 산의 뾰족한 윤곽·삼각 면은 이번에 바꾸지 않았습니다. 농도와 밀도의 최종 판단은 이 화면을 기준으로 검토해 주세요.</p>
<details open><summary>실제 잔존 수량과 보존</summary><p>대표13개 셀의 실제 생성 결과입니다. 작은 집단은 고정 해시 선택으로 목표 비율과 차이가 있습니다. 주 식생 외 추가 군락 EXTRA_COUNT개, 정적 나무 STATIC_COUNT그루를 남겼습니다.</p><div class="scroll"><table><tr><th>종류</th><th>이전</th><th>이후</th><th>잔존율</th></tr>COUNT_ROWS</table></div><p>643개 추가 군락은 근경·원경에 같은 인덱스를 적용했습니다. 기존4군락의 오래된 개체 수 기록도 실제 배열과 일치시켰습니다. 신목·보존 수동 배치·바위·건물·지형·통행 데이터는 유지합니다.</p></details>
<details><summary>1080p Play 측정 — 이전 → 이후</summary><p>동일 주막 카메라의49초 진단: 정지·회전·224m 접근/복귀. 사전 생성 없음. 실제 플레이어 완주가 아닙니다.</p><div class="scroll"><table><tr><th>구간</th><th>CPU 중앙값</th><th>CPU p95</th><th>CPU p99</th><th>GPU 중앙값</th><th>GPU p95</th><th>GPU p99</th><th>최대 주 식생 제출 패킷</th></tr>PERF_ROWS</table></div><p>MEMORY_TEXT</p></details>
<details><summary>검증과 남은 항목</summary><ul><li>원본Transform23,879개의 위치·회전·크기·메시·충돌 정의 보존.</li><li>13셀 ID/행렬 부분집합·반복/분할 생성·나무 근경/원경 일치. 활성 충돌체5개 일치.</li><li>산 거리 명도314,721표본 감소0. 실제 화면의 그림자·mip 전환은 별도 시각 판단 대상.</li><li>Unity 셰이더 오류0, 기존 SurfaceVertex 경고1개. 재적용208에셋 변경0, 식생의 추가 감소 없음.</li><li>미검증: 직접 입력 경계 이동/보행, 전체 구역 충돌체 전수조사, 최종 미술 승인.</li></ul></details>
<footer>실제 Unity 정지 이미지15구도 · 1920×1080 · 캡처 순차 · 커밋85% 중단 · 영상/새 빌드 없음<br><a href="checks.json">구조·셰이더 검사</a> · <a href="summary.json">수량 요약</a> · <a href="performance_before.json">이전 계측</a> · <a href="performance_after.json">이후 계측</a> · <a href="idempotence_final.json">재적용 검사</a></footer></main>
<script>const views=VIEWS;let single=false,showBefore=false;const $=id=>document.getElementById(id);function render(){const v=views.find(v=>v.id===$('view').value);$('before').src='before_'+v.id+'.png';$('after').src='final_'+v.id+'.png';$('large').src=(showBefore?'before_':'final_')+v.id+'.png';$('label').textContent=showBefore?'적용 전':'적용 후';$('swap').textContent=showBefore?'적용 후 보기':'적용 전 보기';$('position').textContent=v.label+' · 카메라 ('+[v.eye.x,v.eye.y,v.eye.z].map(x=>x.toFixed(1)).join(', ')+') · FOV60°';location.replace('#'+v.id)}$('view').onchange=render;$('mode').onclick=()=>{single=!single;$('pair').style.display=single?'none':'grid';$('single').style.display=single?'block':'none';$('swap').hidden=!single;$('mode').textContent=single?'나란히 비교':'크게 비교';render()};$('swap').onclick=()=>{showBefore=!showBefore;render()};const start=decodeURIComponent(location.hash.slice(1));if(views.some(v=>v.id===start))$('view').value=start;render();</script></html>'''
page=page.replace('OPTIONS',options).replace('COUNT_ROWS',count_rows).replace('PERF_ROWS',''.join(perf_rows)).replace('EXTRA_COUNT',f'{old_extra:,}→{new_extra:,}').replace('STATIC_COUNT',f'149→{static_n}').replace('MEMORY_TEXT',f'이번 CPU 중앙값{cpu_min:.2f}~{cpu_max:.2f}ms / GPU{gpu_min:.2f}~{gpu_max:.2f}ms. 최대 메모리{memory:.0f}MiB, 계측·캡처 커밋 최대{peak:.1%}. CPU 목표 미달.').replace('VIEWS',json.dumps(views,ensure_ascii=False))
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps(receipt,ensure_ascii=False,indent=2))
