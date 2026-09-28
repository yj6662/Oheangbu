"""Build the compact ink-landscape review from real Unity captures and receipts."""
from pathlib import Path
import json, html, re, hashlib
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Compact/InkLandscape'
def read(name): return json.loads((OUT/name).read_text(encoding='utf-8-sig'))
views=read('views.json')['views']
names={'inn':'금표 주막 앞','ground_detail':'발밑 지면','mountain_path':'산길 조망','office':'시작 관청','DeepForest':'청림 심부','SouthGate':'황경 남문','Jeokro':'적로','Cheolong':'철옹','Hyeongang':'현강','Hwanggyeong':'황경 분지','boundary_Hwanggyeong':'황경 경계','boundary_Cheongrim':'청림 경계','boundary_Jeokro':'적로 경계','boundary_Cheolong':'철옹 경계','boundary_Hyeongang':'현강 경계'}
for v in views:
 v['label']=names[v['id']]
 for prefix in ('before_loaded','after'):
  with Image.open(OUT/f'{prefix}_{v["id"]}.png') as im: assert im.size==(1920,1080)
checks=read('checks.json');application=read('application.json');source=read('source_preservation.json');idem=read('idempotence.json')
assert not any(c.startswith('FAIL') for c in checks['checks'])
a=read('performance_final_after.json');b=read('performance_final_before.json')
assert a['status']==b['status']=='MEASURED_DIAGNOSTIC_CAMERA'
assert a['start']==b['start'] and (a['width'],a['height'])==(1920,1080)
phases={'cold':'초기 생성','stationary':'정지','rotation':'연속 회전','first_visit':'첫 접근','return_visit':'재방문','returned_stationary':'복귀 정지'}
rows=[];mdrows=[]
for old,new in zip(b['phases'],a['phases']):
 label=phases[new['phase']]
 vals=[f'{old[k]:.2f} → {new[k]:.2f}' for k in ('cpuP50','cpuP95','gpuP50','gpuP95')]
 rows.append('<tr><td>'+label+'</td>'+''.join('<td>'+v+'</td>' for v in vals)+'</tr>')
 mdrows.append('|'+label+'|'+'|'.join(vals)+'|')
peak_memory=max(x['maxMemoryMiB'] for x in a['phases']);before_mem=max(x['maxMemoryMiB'] for x in b['phases'])
commit=max(float(re.search(r'Commit: ([0-9.]+)',p.read_text()).group(1)) for p in OUT.glob('*_timing.txt'))
opts=''.join(f'<option value="{v["id"]}">{html.escape(v["label"])}</option>' for v in views)
shader_checks=''.join('<li>'+html.escape(c)+'</li>' for c in checks['checks'] if not c.startswith('UNVERIFIED'))
report=f'''# 축소 맵 수묵담채 복원 — 구현·검토 보고서

2026-09-16 · W_Demo_Compact 전용 TEST 적용. 최종 미술 판단은 사용자 검토 대기.

## 결과

- 지형/도로/배경과 주막 마당 {application['groundRenderers']}개 Renderer, 식생 199종 재질 조합을 전용 파생 설정에 연결했다. 총 파생 재질 205개.
- 흙·풀·암석 공급자 텍스처를 보존했다. 가까이는 약한 입자결과 노멀을 남기고, 30~150m에 걸쳐 먹–한지 명암 및 넓은 안료 농담으로 정리한다.
- 가파른 근경 암벽은 평지보다 짙게 분리한다. 150~350m에서 암석 대비를 줄이고 350~900m에서 큰 능선 위주로 바꾼다. 900~2200m 먼 대기는 종이색으로 수렴한다.
- 지형과 모든 수목 LOD·추가 EarlyRegionFoliage 카드가 같은 CIAtmosphere 함수를 사용한다. 기존 거리 워시는 opt-in 경로에서 교체하여 중첩하지 않는다.
- 기본 자연 지면 채도 .2. 지면의 지역 tint 강도 .12/.06/.38 → .024/.012/.076. 식생은 기존 지역 tint 인자를 분리하고 그 색도 기여를 .2로 줄인다. 원본 텍스처/소재 색 전체를 .2로 곱하지 않는다. 하늘의 지역색도 기준 하늘 대비 .2로 낮추고 기존 이동·보간을 유지한다.
- 370개 셀 × 4096개 가중치. 완전 개방 지점 풀25%·관목35%, 수목/암석100%. 주 군락 노이즈와 연결한 부드러운 마스크; 수변 보호 표본35,227개, 심부/거목 StoryCluster 보호. 낮은 풀·먼 피복도 같은 수용 확률을 사용한다.
- 기존 주막 마당의 별도 어두운 CaveFloor 재질은 사본으로 전환해 밝은 주변 지면과 연결했다. KCISA 자체 지면 UV/텍스처와 마당의 월드 투영 방식을 구분하며, 건물 메시/크기/계단은 건드리지 않았다.

## 설정과 출처

- `Assets/_Project/Art/World/WorldCompact/InkLandscape/Profile.asset`: CompactInkLandscapeProfile.
- 같은 폴더 `Dressing.asset`, `Sky.asset`, `RegionalSky.asset`, `Materials/`: 축소 맵 파생 설정. 공급자/공유 원본 보존.
- 레거시 `MandateOfInk/Assets/_Project/Art/Shaders/S_ToonLitTemp.shader`의 먹–한지 명암 원리를 참고했다.
- 현 프로젝트 `CodexInkLandscape.shader`와 이전 C2 Ground_With_Path의 저주파 얼룩/농담을 재해석했다. 장식 곡선 무늬는 복제하지 않았다.
- 현재 NaturalSurface의 흙·풀·암석 텍스처 및 경로 마스크를 재사용했다. 전역 노출·후처리·조명은 변경하지 않았다.
- 상세 수치는 `Docs/Specs/SPEC-COMPACT-INK-LANDSCAPE.md` 및 Profile.asset에 기록했다.

## 보존·정적 검증

- 원본 비교 대상 Transform23,879개: 로컬 위치·회전·크기, 메시 참조, 콜라이더 직렬화 동일.
- 원본 에셋 {source['count']}개 SHA256: 변경 {len(source['changed'])}개. 별도 추가 식생/마당 원본 재질도 동일.
- 추가 식생의 근경/원경 메시·배치 행렬·범위 동일. 수동 확정 배치 및 기본 시드/LOD 거리 동일.
- 파생 에셋 {idem['assets']}개에 재적용: 파일 차이 {len(idem['changedAfterSecondApply'])}개. 값의 반복 감소 없음.
- 수정 셰이더4개 오류0. 기존 CompactNaturalVegetation의 SurfaceVertex 잠재 초기화 경고1개는 남아 있다.
- 재적용 후 일부 재질에서 새 프로퍼티가 직렬화되지 않는 문제를 잡았다. 임시 완성 Material을 작성한 뒤 기존 GUID 에셋에 직렬화 복사하여 재임포트/재적용 검사를 통과했다.

## 실제 Editor Play 성능

{a['unityVersion']} / {a['gpuDevice']} / {a['cpuDevice']}.
1080p 진단 카메라, 품질{a['quality']}, VSync{a['vSync']}. 원본과 수정판은 동일 위치 (907,135.2,208), 같은 방향에서 각각49초: 초기·정지·회전·224m 접근·복귀. 식생을 미리 생성하지 않았다. 플레이어를 자동 보행시키거나 영상을 만들지 않았다.

|상태|CPU 중앙값 ms 원본→수정|CPU p95|GPU 중앙값|GPU p95|
|---|---|---|---|---|
'''+ '\n'.join(mdrows)+f'''

Unity 추적 메모리 최대 {before_mem:.0f}→{peak_memory:.0f}MiB. 캡처 당시 시스템 커밋 최대 {commit*100:.2f}%, 85% 중단 규칙 준수. GPU/CPU 8.33ms 목표는 위 숫자로 판정하며, CPU가 초과하므로 120fps 달성으로 보지 않는다. Editor·다른 프로세스 비용이 포함된 단일 비교로, 정확한 인게임 개선율이나 빌드 FPS로 일반화하지 않는다.

`performance_final_before.json`, `performance_final_after.json`은 최종 고정 구도 비교다. 앞선 performance_before/after 파일은 시작 위치가 달라 비교 결론에서 제외했다. 스크린샷 *_timing.txt의 6회 동기 RenderRequest 비용은 프레임타임이 아니다. 화면별 *_submissions.json은 주 식생 Renderer 제출 비용만 포함한다.

## 화면과 남은 확인

15개 동일 구도의 before_loaded/after 1080p 쌍. 주막·산길·관청·황경·5강토 경계·지면 근접·보호 심부 숲을 포함한다. 최초 before_*.png는 스트리밍 준비가 덜 된 보관본으로 검토 페이지에서 제외했다. 바람 위상은 촬영 시각에 따라 다르므로 픽셀 단위 형상 일치는 비교하지 않는다.

가까운 흙결·밝은 여백·주막 마당 연결·먼 나무와 능선의 공동 소실을 정지 이미지에서 확인했다. 현재 지형의 바늘 봉우리/급한 단면, 수목 카드의 디더 패턴과 지면 삼각형 명암 일부는 남아 있다. 재질로 메시 형상 결함을 해결했다고 판정하지 않는다.

**미검증:** 실제 사용자 입력으로 접근/회전/경계 통과 시 전환의 가독성과 튐, 모든 구간의 통행 회귀, 최종 예술적 승인. 짧은 진단 카메라 이동의 계측은 실제 보행 완주를 대신하지 않는다. 새 빌드·영상 없음.

## 재현

Editor Edit 상태의 W_Demo_Compact에서 기존 큐로 `chapter3:compact:recovery:ink:verify`, `...:capture:after:inn`. `apply`는 원본 기준 절대 적용이다. `baseline.json`과 `supplement_sources.json`은 원본 연결을 담으며 덮어쓰지 않는다. `before_disk.unity`와 `before_open.unity`로 시작 당시 디스크/열린 씬을 따로 보존했다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>수묵담채 룩 복원 · 축소 맵</title>
<style>
:root{--paper:#e9e5db;--ink:#302e28;--muted:#706d63;--line:#bbb7ab}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.8 system-ui,'Malgun Gothic',sans-serif}main{max-width:1600px;margin:auto;padding:48px 40px}h1{font-size:clamp(30px,4vw,50px);font-weight:500;letter-spacing:-2px;margin:8px 0}h2{font-size:24px;font-weight:500;margin:42px 0 15px}p{max-width:1000px}.eyebrow{letter-spacing:2px;font-size:11px;color:var(--muted)}header{border-bottom:1px solid var(--line);padding-bottom:22px}.facts{display:flex;gap:34px;flex-wrap:wrap;margin:24px 0;font-size:13px}.facts b{font-size:23px;font-weight:500;display:block}a{color:#6d473a}select,button{font:inherit;border:1px solid var(--line);padding:8px 13px;background:#f5f1e7;color:var(--ink);cursor:pointer}.controls{display:flex;flex-wrap:wrap;align-items:center;gap:12px;margin:24px 0 14px}.meta{color:var(--muted);font-size:12px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}img{display:block;width:100%;aspect-ratio:16/9;object-fit:contain;background:#cbc9c1}figcaption{font-size:12px;margin:7px 0}.single{position:relative}.single .label{position:absolute;left:15px;top:15px;background:#ede8dbdd;padding:4px 12px;font-size:12px}.notice{padding:15px 20px;background:#dfdace;border-left:3px solid #8a695b;margin:28px 0}.cards{display:grid;grid-template-columns:repeat(3,1fr);gap:25px}.cards article{border-top:1px solid var(--line);padding-top:15px}.cards b{font-size:18px;font-weight:500}.cards p{font-size:13px}table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:12px;border-bottom:1px solid var(--line);text-align:left}th{font-weight:500}.scroll{overflow:auto}details{border:1px solid var(--line);padding:15px 20px;margin:24px 0}summary{cursor:pointer}li{margin:7px 0}footer{border-top:1px solid var(--line);margin:32px 0;padding-top:20px;display:flex;flex-wrap:wrap;gap:25px;font-size:13px}.hidden{display:none}@media(max-width:800px){main{padding:24px 15px}.pair,.cards{grid-template-columns:1fr}.facts{gap:18px}}
</style><main><header><div class="eyebrow">OHEANGBU / COMPACT WORLD / 2026.09.16</div><h1>가까이는 재료, 멀리는 수묵담채</h1><p>레거시의 먹빛 겹산과 종이색 대기, 이전 C2의 밝은 지면을 현재 축소 맵에 결합했습니다. 지형·건물·통행은 유지하고 표면과 식생 여백을 조정한 TEST 시안입니다.</p></header>
<div class="facts"><div><b>20%</b>추가 지역색 기여</div><div><b>25% / 35%</b>열린 곳 풀 / 관목 밀도</div><div><b>15 구도</b>동일 위치 1080p 비교</div><div><b>0 변경</b>지형·건물 Transform / 충돌</div></div>
<div class="controls"><label for="view">구도</label><select id="view">OPTIONS</select><button id="mode">한 장씩 크게 보기</button><button id="swap" class="hidden">적용 전 보기</button><a id="full" target="_blank">수정판 원본 이미지</a></div><p class="meta" id="meta"></p>
<div class="pair" id="pair"><figure><img id="before" alt="적용 전 Unity 화면"><figcaption>적용 전 · 스트리밍 준비 완료</figcaption></figure><figure><img id="after" alt="수묵담채 적용 후 Unity 화면"><figcaption>수정판 · 동일 카메라</figcaption></figure></div><div class="single hidden" id="single"><img id="large" alt="선택한 비교 화면"><span class="label" id="label">수정판</span></div>
<p class="meta">실제 Unity 렌더 캡처입니다. 자동 보행·합성 이미지·새 빌드가 아닙니다. 바람 위상은 촬영 시각에 따라 달라집니다.</p>
<h2>표현의 변화</h2><div class="cards"><article><b>밝은 지면과 흙길</b><p>30m 안에서는 실제 표면결과 약한 노멀을 남깁니다. 30~150m에서 미세 무늬를 줄이고 회백색의 넓은 농담으로 이어집니다. 주막의 별도 마당도 함께 연결했습니다.</p></article><article><b>능선과 같은 공기 속의 나무</b><p>암벽150~350m, 산350~900m, 먼 대기900~2200m. 주 식생의 모든 LOD와 추가 수목 카드에도 같은 거리 함수를 적용했습니다.</p></article><article><b>군락 사이 여백</b><p>수변·심부 숲·거목 군락과 수동 배치를 보호합니다. 열린 구역의 풀과 관목만 줄이며 낮은 풀·먼 피복에도 같은 마스크를 씁니다.</p></article></div>
<div class="notice">남은 형상 문제: 바늘처럼 솟은 봉우리, 급한 지형 단면, 일부 카드 디더 패턴은 그대로입니다. 이번 변경은 재질과 배치 밀도 범위이며, 산의 실루엣을 다시 만들지는 않았습니다.</div>
<h2>실제 Play 루프 계측</h2><p>고정 주막 구도에서 각각49초, 식생 사전 생성 없이 정지·회전·224m 카메라 접근·복귀를 비교했습니다. 1920×1080 진단 카메라 계측이며 플레이어 보행 검증과 구분합니다.</p><div class="scroll"><table><thead><tr><th>상태</th><th>CPU 중앙값</th><th>CPU p95</th><th>GPU 중앙값</th><th>GPU p95</th></tr></thead><tbody>ROWS</tbody></table></div><p class="meta">단위 ms, 원본 → 수정판. MEMORY</p><p><b>120fps 목표 미달.</b> CPU가8.33ms를 초과합니다. Editor 단일 비교 결과이며 빌드 FPS나 전체 구간 성능을 보장하지 않습니다.</p>
<details><summary>구조·소스·셰이더 검사 결과</summary><ul>CHECKS</ul><p>원본 에셋199개 SHA 변경0. 파생 에셋 반복 적용 차이0. 성능 원자료는 아래 링크에서 확인할 수 있습니다.</p></details>
<p>정지 화면에서 흙결·여백·능선 농도·산과 나무의 대기 연결을 확인했습니다. 실제 조작으로 경계를 지날 때의 전환 가독성과 최종 수묵담채 인상은 검토가 필요합니다.</p>
<footer><a href="REPORT.md">기술 보고서</a><a href="checks.json">구조 검사</a><a href="source_preservation.json">원본 보존</a><a href="idempotence.json">반복 적용</a><a href="performance_final_before.json">성능 원본</a><a href="performance_final_after.json">성능 수정판</a><a href="views.json">촬영 좌표</a></footer></main>
<script>const views=VIEWS;let single=false,showBefore=false;const $=id=>document.getElementById(id);function update(){const v=views.find(v=>v.id===$('view').value);const pre='before_loaded_'+v.id+'.png',post='after_'+v.id+'.png';$('before').src=pre;$('after').src=post;$('large').src=showBefore?pre:post;$('label').textContent=showBefore?'적용 전':'수정판';$('full').href=post;$('swap').textContent=showBefore?'수정판 보기':'적용 전 보기';$('meta').textContent=v.label+' · 위치 '+Object.values(v.eye).map(n=>n.toFixed(1)).join(', ')+' · FOV60° / 1920×1080';location.hash=v.id;}$('view').onchange=update;$('mode').onclick=()=>{single=!single;$('pair').classList.toggle('hidden',single);$('single').classList.toggle('hidden',!single);$('swap').classList.toggle('hidden',!single);$('mode').textContent=single?'나란히 비교':'한 장씩 크게 보기';update()};$('swap').onclick=()=>{showBefore=!showBefore;update()};const start=location.hash.slice(1);if(views.some(v=>v.id===start))$('view').value=start;update();</script></html>'''
page=page.replace('OPTIONS',opts).replace('ROWS',''.join(rows)).replace('CHECKS',shader_checks).replace('MEMORY',f'Unity 추적 메모리 최대 {before_mem:.0f} → {peak_memory:.0f}MiB. 캡처 시스템 커밋 최대 {commit*100:.2f}%.').replace('VIEWS',json.dumps(views,ensure_ascii=False))
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print('review:',OUT/'REVIEW.html')
