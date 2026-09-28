"""Package actual Unity evidence; never turn an art reference into a measured result."""
from pathlib import Path
import json, hashlib, html, re
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Mountain285'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/MountainTrail285'
scene=ASSET/'W_Cheongrim_GraniteTrail.unity'
checks=(OUT/'unity-checks.txt').read_text(encoding='utf-8')
walk=(OUT/'walk-check.txt').read_text(encoding='utf-8')
timing=(OUT/'frame-times.txt').read_text(encoding='utf-8')
sources=json.loads((OUT/'sources.json').read_text(encoding='utf-8'))
baseline=json.loads((OUT/'preserved.json').read_text(encoding='utf-8'))
changed=[p for p,h in baseline.items() if not (ROOT/p).exists() or hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
source_errors=[a['path'] for a in sources if not (ROOT/a['path']).exists() or hashlib.sha256((ROOT/a['path']).read_bytes()).hexdigest()!=a['sha256']]
assert not source_errors,source_errors
preserve={'checked_utc':datetime.now(timezone.utc).isoformat(),'checked':len(baseline),'changed':changed}
(OUT/'preservation-check.json').write_text(json.dumps(preserve,indent=2),encoding='utf-8')
passed=sum(s.startswith('PASS ') for s in checks.splitlines())
failed=sum(s.startswith('FAIL ') for s in checks.splitlines())
limitations='큰 암릉의 외곽 실루엣과 상부 마감, 소나무 수관 밀도, 먼 산의 지형별 식생은 추가 보완 대상이다. 이번 수정도 목표 일러스트 수준의 미술 완료로 판정하지 않는다.'
dressing=json.loads((OUT/'dressing288-counts.json').read_text(encoding='utf-8'))
facts=f'''# 암릉 산길 대표 구간 285

## 이번 다듬기 — 급경사 사면·무료 목재 잔도289

계단 아래 암벽이 바깥으로 퍼지는 폭을 줄여288보다 가파르게 세웠다. 길의 중심선·폭·높이와 상단 턱은 유지하며, 사면은 길 아래에서 계속 보이도록 남겼다. 바뀐 실제 충돌면의5지점 경사는 아래 Unity 검사 원문에 각도로 기록한다.

Poly Haven / Rico Cilliers의 **Modular Wooden Pier**를 CC0로 취득했다. 부두 완성형을 그대로 배치하지 않고, 원본의 낡은 판재10종과 통나무3종을 추출해 약5m 잔도에 판재{dressing['planks']}개와 지지/난간 부재로 재조립했다. 판재의 닳은 끝선과 굴곡, 원래 UV를 유지한다. 2K diffuse/normal/ARM 총6장과 원본 blend1파일을 해시 검증했고 sources.json에 기록했다. 발판·난간·가새·장선·가로 받침에 실제 가져온 부재를 사용한다. 기존 잔도의 불규칙한 폭·방향·높이는 이어가며, 발판은 단순 BoxCollider, 난간은 CapsuleCollider를 사용한다. 보강 목재는 시각 표현만 담당한다. 원본 UV를 쓰는 전용 Timber289 셰이더를 추가해 나뭇결이 부재를 따라가도록 했다.

수정 전288 씬/에셋/제작기와6시점 화면은 BeforePier289에 보존했다. 검사는 이번 재조립 이후 새로 실행했다. 축소맵 이식 준비 문서 `Docs/Plans/PLAN-MOUNTAIN-COMPACT-INTEGRATION-289.md`를 작성했으며, 실제 후보/정본의 지형이나 저장 슬롯은 아직 변경하지 않았다. 다음은 최신 후보에서 위치·연결 높이·보행/차량 경로를 확인한 후 부품 이식, 지도/발견/체크포인트/NavMesh 갱신, 게임 플레이어와 시스템 회귀 순서다.

## 이전 다듬기 — 식생·잔도·하부 사면·원경288

식생은 같은 간격의 줄 배치를 없애고 바위가 드러나는 빈 구간과 군락을 나눴다. 풀·두 형태의 고사리·낮은 지피류 {dressing['habitats']}개체를 음지와 계곡 턱에 나눠 배치했다. 하부 암반에는 느릅나무 어린 개체 {dressing['elms']}그루를 추가했다. 기존 소나무와 함께 사용하며 새로운 식물 모델을 생성한 것은 아니다. 군락은 고정 seed288로 재현하고 기존 메시/카드 LOD를 사용한다. 2단계 식생의 마지막 LOD가 화면 비율4.5%에서 조기 소멸하던 설정을0.2%로 낮춰 근중경 군락이 남도록 수정했다. 작은 식생에는 Collider를 추가하지 않았다.

288의 잔도는23개의 서로 다른 폭·두께·끝선·작은 비틀림을 가진 판재로 바꿨다. 기둥 간격·높이·기울기와 난간의 처짐도 달리했다. 실제 목재 표면에는 기존 CC0 풍화 목재를 재사용한다. 판재별 단순 충돌을 유지하며 눈에 보이는 닳음과 작은 틈이 통행을 끊지 않는지는 이번 Play 왕복 시험으로 확인한다.

계단 아래 받침 절벽은 가장자리에서 바깥쪽으로 퍼지는 경사 암반으로 바꿨다. 절벽의 높이에 따라 외측으로 넓어지며 약한 절리·파단면을 유지한다. 길과 상단 턱은 보존했다. 먼 산은 기존 둥근 능선 대신 비대칭의 가파른 사면, 좁은 능선, 크기가 다른 봉우리와 안부, 계곡으로 이어지는 넓은 하부를 가진 Terrain 높이 데이터로 교체했다. 높이맵 지형이므로 돌출·오버행은 표현하지 않는다.

수정 전287 에셋·씬·제작기와 동일 시점 화면은 BeforeDressing288에 보존했다. 기존 고정4시점에 계단 아래 사면과 원경 전망2시점을 추가했다. 288 당시 검사 결과는 BeforePier289에 보존했다. 아래 검사·Play·프레임 수치는 최신289 결과다.

## 이전 다듬기 — 자연 디딤길287

균일하게 반복되던16cm 계단을 폐기했다. 두 오르막을45개의 암반 디딤면으로 구성하고 단차 약5.5–20.5cm, 전후 깊이 약0.25–1.23m, 비스듬하고 굴곡진 앞선과 경사진 디딤면을 조합했다. 각 모서리의 위치는 고정 seed287의 배치 원장 natural-step-layout.json에서 재현된다. 표면과 보행 높이 프로필도 함께 갱신했다.

오르막 기준 왼쪽(계곡 쪽)에 높이·너비가 달라지는 낮은 암반 턱2개 메시를 이어 붙이고, 묻힌 바위35개와 풀·고사리133군락을 배치했다. 식생은 실제 턱 메시로 접지하며 별도 Collider를 추가하지 않는다. 턱 두 메시와 큰 바위의 단순 BoxCollider만 충돌을 담당한다. 바위는 동일 스캔 메시/재질을 공유하며 식생은 기존3단계LOD를 재사용한다.

수정 전286 씬/메시/제작 소스와 같은 시점 화면은 BeforeNatural287에 복구본으로 보존했다. 이번 검증은 수정 후 다시 실행한 기록이다. 수동 플레이와 사용자 미술 승인은 별도로 남는다.

## 누적 조형 변경 — 286

기존109.9m 대표 구간에서 산체·표면·계단·접합부를 함께 수정했다. 길을 위로 늘린 절벽 대신 독립된 상부 암릉과 끝이 끊기는 절리를 적용했다. 당시 보행 메시의 단차가 절벽 조형에 영향을 주지 않도록 분리했다. 절벽과 받침에 단차가 전파되어 생기던 띠 모양 굴곡을 제거했다. 메시에는7.3m/2.8m/1.05m 규모의 평평한 파단면을 섞었고, 규칙적인 세로 주름을 줄였다.

계단은 약2.5cm 모서리 깎기, 비대칭 가장자리, 얕게 닳은 중앙부를 갖는다. 안쪽을45cm 연장해 암반 속으로 물렸다. 계단 전용 재질은 절벽과 같은 스캔 원본을 쓰며, 그늘에서도 단차가 읽히도록 간접광과 표면 명도를 별도로 조정했다.

멀어질수록 스캔 대비와 미세 법선을 줄이고 월드 좌표의 약한 풍화 명암으로 넘어간다. Terrain 흙에도 거리별 디테일 감소를 적용했다. 지형 간접광에는 상한을 두었다. 기존 전용 Volume에서 ACES, 노출+0.35, 대비−7, 채도−20을 적용하고 햇빛 방향·그림자 강도와110–650m 거리 안개를 조정했다. 하늘의 낮은 고도는 같은 안개색으로 이어진다. 새 렌더러 기능을 공용 Renderer에 추가하지 않았다.

수정 전 씬/재질/메시/제작 소스는 BeforeRefinement286/recovery.zip, 이전 동일 시점 화면은 같은 폴더의 view-*.png에 보존했다. 현재 단계에서 경로 길이를 확장하지 않았다.

## 구현

별도 씬: `{scene.relative_to(ROOT).as_posix()}`. 길이109.916m, 높이차21m, 일반 폭1.8m 이상, 전망부 최대 약3.7m. 불규칙한 자연석 디딤면 두 구간과 약5m 목재 잔도를 연결했다. Terrain1025 높이 데이터, Blender에서 제작한 연속 절벽/하부 받침, 독립 통행 메시, 기존 Meshy 소나무와 하층 식생을 조합했다. 절벽은 불균등한 두 절리 방향으로 큰 면을 나누고, 표면의 규칙적인 사인파 요철은 제거했다.

암석·흙·풍화 목재·바위 원본·구름 HDRI 등 Poly Haven CC0 원본 {len(sources)}파일의 해시를 확인했다. 실제 적용 목록과 원본 주소는 sources.json 및 Unity 폴더의 SOURCE_LICENSES.md에 기록했다. 바위 FBX는 가까운 잔해용으로 축소·LOD 처리했으며 산 전체를 이 바위로 채우지 않았다. 원본 바위의 고유 UV 대신 지역 암석 재질로 통일했다. 암석 높이 PNG는 원본 보관용이며 메시 변위 입력으로 쓰지 않았다.

표면은 월드 좌표 삼축 투영의 albedo/normal/ARM이며 근접에서는 작은 디테일을, 멀어지면 미세 무늬·법선을 줄이고 약한 풍화 명암을 남긴다. Terrain과 절벽은 표면 함수를 공유하지만 조명 계산은 다르다. 절벽은 환경 반사 없이 주광·그림자·범위를 제한한 간접광으로 명암을 계산하며, Terrain은 URP PBR 조명을 유지한다. 암석의 과도한 밝기를 줄이기 위해 HDR 재질 색을 낮추고 절벽의 ShadowCaster도 같은 재질 버퍼를 사용하는 전용 패스로 교체했다. Terrain의 Forward/DepthNormals를 수정했으며 기본 BaseMap/AddPass/Meta는 URP 기본 구현이다. 이 실험은 2레이어, 실시간 조명, basemap거리2200m로 제한한다. 구름 하늘은 정지 HDRI, 계곡 안개는 기존 이동 셰이더를 적용한 깊이 완화 면이다. 볼륨 유체 안개는 구현하지 않았다.

큰 절벽은 3단계 시각 LOD와 별도 저해상도 고정 충돌을 쓴다. 길과 계단은 정밀 MeshCollider, 잔도는 판재/난간 Collider다. 작은 잔해는 5개 군집으로 합치고 충돌·그림자를 생략했다. 소나무 원본은 보존하고 이 씬에서만 솔잎 폭과 LOD 거리를 조정했다. 풀·고사리의 마지막 LOD 소멸 거리도288에서 별도 조정했다.

## 이번 변경에서 실행한 검사

Unity 정적/물리 샘플 검사: {passed} PASS / {failed} FAIL. 씬 재개방 후 persistent NavMesh도 확인했다.

```
{checks.strip()}
```

Play 자동 입력:

```
{walk.strip()}
```

직접 키보드로 탐색한 수동 플레이와는 구분한다. 중앙에서 좌우0.38m 떨어진 경로를 각각 오르고 내려오는 CharacterController 충돌 시험이며 모든 자유 이동 방향·낙하·복구 상황을 증명하지 않는다.

## 실측

```
{timing.strip()}
```

FrameTimingManager 값이며 같은 프레임마다 수집했다. Editor와 작은 별도 장면의 결과다. 전체 축소맵, 독립 실행 빌드, 다른 하드웨어의120fps 달성 증거로 승계하지 않는다. 이동 중 미세한 식생 반짝임·안개 경계의 주관적 판정은 수동 검토가 남아 있다.

## 보존과 정합성

기존 정본/후보/이전 산길/렌더링 설정/저장 등 보존 대상 {len(baseline)}파일 중 변경 {len(changed)}개. 상세 preservation-check.json. 원본 복구 묶음은 Recovery.zip. 신규 씬은 빌드 목록에 추가하지 않았고 Campaign·장비·저장 슬롯을 연결하지 않았다. 공용 Editor WorldMacro asmdef에 Unity.Mathematics 참조만 추가했다.

compact_rebuild_consistency.py의 읽기 전용 정적 점검을 이번에도 새로 실행했다: 9 PASS / 0 FAIL, 기존 열린 항목6. refinement-consistency.json/txt에 기록했다. 이 검사는 Campaign/배치 위상을 다루며 산의 미술·충돌 통과 증거로 사용하지 않는다. 이번 지형·접합·NavMesh·셰이더·보행은 위의 별도 새 검사로 확인한다.

## 사용자 미술 판정과 다음 순서

미술 승인 대기. {limitations}

1. 대표109.9m의 큰 절벽 면, 길의 높이감, 근접 표면을 검토한다.
2. 새 급경사 사면과 가져온 목재 잔도를 검토한다.
3. 축소맵의 최신 후보에서 이식 위치/높이와 본선·차량 연결을 조사한다. 구체 순서와 완료 조건은 PLAN-MOUNTAIN-COMPACT-INTEGRATION-289.md를 따른다. 과거의415m 실험 확장을 자동 선행 조건으로 삼지 않으며, 정본 승격과 EA 완료는 별도다.

## 실행과 재조립

Unity 메뉴 `Oheangbu > 별도 맵 > 암릉 산길 285 열기 (이 씬에서 Play)`를 선택한 뒤 Play. W/A/S/D 이동, 마우스 시점, Shift 빠르게, Tab 비행 전환, 비행 중 Q/E 높이, R 시작점 복귀, Esc 마우스 해제.

제작 입력은 Tools/Blender/build_mountain285.py의 경로·폭·절리 설정이다. 이 파일이 route.json과 메시/높이 데이터를 출력하고 Unity 제작기가 MountainTrailProfile/Spline/지형/충돌/NavMesh를 함께 갱신한다. Unity Spline의 수동 편집을 Blender 입력으로 역변환하는 기능은 없다. 같은 원본 입력으로 build해도 기존285 에셋을 갱신하며 오브젝트가 누적되지 않는다. 기존 씬이 수정 상태이면 재조립을 거부한다.

`prepare_mountain285.py` → Blender5.0의 `build_mountain285.py` → `shader_mountain285.py` → Unity AssetDatabase.Refresh → `CompactRebuildAuthoring.Mountain285("build")` 순서다. 검사/캡처/실행은 각각 check / capture / clay / raw / walk-play 명령으로 분리했다. 검토 시점은 제작기 Capture285의 고정4시점과288에서 추가한 하부 사면·원경2시점이다. 289 목재의 재추출은 보존된 Sources/modular_wooden_pier 원본에 Tools/Blender/inspect_pier289.py → build_pier289.py를 실행한 뒤 같은 Unity build를 사용한다. Timber289.shader는 원본 UV용 독립 셰이더 에셋이다.
'''
(OUT/'REPORT.md').write_text(facts,encoding='utf-8')

esc=html.escape
figures=''.join(f'<figure><a href="view-{i}.png"><img src="view-{i}.png" alt="실제 Unity {label}" loading="lazy"></a><figcaption>{label} · 1920×1080 실제 Unity 캡처</figcaption></figure>' for i,label in enumerate(['돌계단의 오르막','절벽과 길의 연결','내려오는 방향','잔도와 바위 접합','계단 아래 경사 암벽','가파른 원경 산세']))
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>암릉 산길 285 · 실제 구현 검토</title>
<style>*{{box-sizing:border-box}}body{{margin:0;background:#e9e7df;color:#292d2a;font:16px/1.75 "Malgun Gothic",sans-serif}}main{{max-width:1450px;margin:auto;padding:50px 28px}}h1{{font-size:34px;letter-spacing:-1px;margin:0 0 12px}}h2{{font-size:22px;margin-top:42px}}p{{max-width:1050px}}a{{color:#465f57;text-underline-offset:4px}}.meta,figcaption{{color:#60655e;font-size:14px}}figure{{margin:32px 0 46px}}img{{width:100%;display:block;height:auto}}figcaption{{margin-top:9px}}details{{border-top:1px solid #b8bcb2;padding:20px 0}}summary{{cursor:pointer}}pre{{white-space:pre-wrap;word-break:break-word;background:#dedfd6;padding:22px;font:14px/1.7 Consolas,monospace}}.links{{display:flex;gap:25px;flex-wrap:wrap}}code{{overflow-wrap:anywhere}}</style>
<main><div class="meta">별도 보행 실험 씬 · 구현과 미술 판정을 분리</div><h1>가파른 하부 암벽 · CC0 목재 잔도</h1>
<p>Terrain 산체 위에 절벽, 돌계단과 짧은 잔도를 연결한 109.9m 대표 구간입니다. 높이차는21m입니다. 계단 아래 암벽을 이전보다 가파르게 세웠습니다. 잔도는 Poly Haven의 낡은 목재 부재를 가져와 원래 UV와 표면을 유지하며 새로 조립했습니다. 불규칙한 판재와 난간, 하부 지지 구조를 함께 적용했습니다. 아래는 실제 Unity 화면이며, 아래 펼침 영역에서 수정 전과 비교할 수 있습니다.</p>
<p><strong>미술 승인은 대기 중입니다.</strong> {limitations}</p>
<p class="links"><a href="REPORT.md">구현·검증 보고서</a><a href="{scene.as_uri()}">Unity 씬 파일</a><a href="../Hybrid284/REVIEW.html">이전284 비교본</a><a href="sources.json">에셋 출처·해시</a></p>
{figures}
<details><summary>수정 전 동일 시점과 비교</summary><p>아래는 이번 급경사·잔도 수정 전288 화면입니다. 위의 최종 화면과 카메라 위치·방향·화각이 같습니다.</p>{''.join(f'<figure><img src="BeforePier289/view-{i}.png" loading="lazy" alt="수정 전 동일 시점 {i+1}"><figcaption>수정 전 · 시점 {i+1}</figcaption></figure>' for i in range(6))}</details>
<details><summary>포스트 처리 전 화면</summary><p>아래는 Volume 후처리를 끈 현재 씬입니다. 셰이더와 거리 안개, 구름은 유지됩니다.</p>{''.join(f'<figure><img src="raw-{i}.png" loading="lazy" alt="포스트 전 시점 {i+1}"></figure>' for i in range(6))}</details>
<h2>축소맵 이식 준비</h2><p>실제 축소맵 지형과 저장 슬롯은 보존했습니다. <a href="file:///C:/Users/yj666/Oheangbu/Docs/Plans/PLAN-MOUNTAIN-COMPACT-INTEGRATION-289.md">이식 순서와 통과 기준</a>에 위치 선정, 지형 접합, 지도·발견·체크포인트·NavMesh 연결 및 게임 플레이어 회귀 검사를 정리했습니다.</p><h2>직접 확인</h2><p>Unity에서 <strong>Oheangbu → 별도 맵 → 암릉 산길285 열기 (이 씬에서 Play)</strong>를 선택하고 Play하세요. 기존 로비 대신 이 보행 전용 씬을 실행합니다.</p><p>W/A/S/D 이동 · 마우스 시점 · Shift 빠르게 · Tab 비행 · Q/E 비행 높이 · R 시작점 · Esc 마우스 해제</p>
<h2>검증 기록</h2><p>새 씬 검사 {passed} PASS / {failed} FAIL. 실제 Play의 자동 왕복 보행 통과 여부는 아래 원문으로 확인할 수 있습니다. 수동 탐색과 사용자 미술 판단은 아직 완료하지 않았습니다.</p>
<pre>{esc(walk)}\n{esc(timing)}</pre><p>보존 대상{len(baseline)}파일 중 변경{len(changed)}개. <a href="preservation-check.json">전체 보존 검사</a></p>
<details><summary>Unity 검사 원문</summary><pre>{esc(checks)}</pre></details>
<details><summary>메시 조형만 확인 — 무채색·무안개</summary><p>절벽·길·목재의 재질을 임시 회색으로 바꿨습니다. Terrain과 식생 재질은 유지한 조형 점검 이미지입니다.</p>{''.join(f'<figure><img src="clay-{i}.png" loading="lazy" alt="조형 점검 {i+1}"></figure>' for i in range(6))}</details>
<details><summary>제작 목표 일러스트 — 실제 Unity 화면 아님</summary><p>구도와 조형 방향을 위한 생성 예상도입니다. 아래 이미지의 완성도를 실제 구현 결과로 취급하지 않습니다.</p><figure><img src="concept-walk.png" loading="lazy" alt="생성 목표 일러스트"></figure><figure><img src="concept-overview.png" loading="lazy" alt="생성 원경 목표 일러스트"></figure></details>
<h2>사용한 무료 에셋</h2><p>Powered by Poly Haven. <a href="https://polyhaven.com/license">CC0 라이선스</a> 원본{len(sources)}파일을 내려받고 해시를 확인했습니다. 기존 소나무 에셋은 별도 복제 재질과 LOD로 재사용했습니다.</p><p class="links">{''.join(f'<a href="https://polyhaven.com/a/{slug}">{esc(slug.replace("_"," "))}</a>' for slug in sorted({a['asset'] for a in sources}))}</p>
</main></html>'''
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')

note=f'''<!-- mountain-trail-285:start -->
**절벽 산길 #285 / 급경사·CC0 잔도289 — 구현 검토본 (2026-09-26):** 별도 `W_Cheongrim_GraniteTrail` 씬에109.9m/상승21m, Terrain + Blender 연속 절벽 + 돌계단2구간 +5m 잔도 + 소나무/잔해를 구현했다. Poly Haven CC0 원본{len(sources)}파일을 확보했다. 이번289에서 하부 사면을 가파르게 조정하고 Modular Wooden Pier의 판재10종·통나무3종을 원본UV/2K표면으로 잔도에 적용했다. 수정 전288을 BeforePier289에 보존했다. [축소맵 이식 준비](C:/Users/yj666/Oheangbu/Docs/Plans/PLAN-MOUNTAIN-COMPACT-INTEGRATION-289.md)를 작성했다. 실제 축소맵/저장 변경은 아직 수행하지 않았다. 이번288에서 식생 군락/빈 암반, 풀·고사리2형태·지피류와 하부 느릅나무를 적용했다. 잔도 판재의 폭·두께·끝선과 난간을 불규칙하게 만들고, 계단 아래 경사진 암반 및 가파른 원경 Terrain을 구현했다. 수정 전287은 BeforeDressing288에 보존했다. 균일한16cm 단차를 폐기하고45개의 가변 단차·깊이·방향을 가진 암반 디딤면으로 교체했다. 왼쪽 계곡 가장자리에 낮은 암반 턱과 묻힌 바위·풀·고사리를 추가했다. 보행 프로필과 메시를 함께 갱신했다. 절벽에서 계단 단차 전파를 제거하고, 원경의 스캔·법선 대비를 줄였다. 전용 Volume의 명암·노출·채도와 거리 안개를 다듬었다. 식생은 원본별2–3단계LOD와 솔잎 가시성 보완, 작은 잔해는5군집/무충돌이다.

289 재조립 이후 새 Unity{passed}검사 PASS/{failed}FAIL, 경로 중앙/양옆1779접지와 안쪽 접합199지점, NavMesh 연결, 좌우0.38m 편향 경로의 실제 Play 자동 CharacterController 왕복을 확인했다. CPU/GPU 수치는 [실측 원문](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/Mountain285/frame-times.txt)에 기록했다. 작은 Editor1080p 대표 구간 결과이며 전체 게임120fps 승인으로 승계하지 않는다. 이전 대상{len(baseline)}파일 중 변경{len(changed)}개. 정본/후보/저장/빌드 연결은 보존한다.

**사용자 미술 승인 대기:** 상부 암릉 외곽 마감, 수관 밀도/LOD와 먼 산 식생은 보완 대상으로 남긴다. 수정 전286 복구본과 동일 카메라 비교를 BeforeNatural287에 보존했다. 다음은 이식 준비 문서에 따라 최신 축소맵 후보의 위치/연결/데이터를 확인한다. 수동 자유 탐색과 전체 게임 완료는 미검증이다. [실제 화면](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/Mountain285/REVIEW.html) · [보고서](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/Mountain285/REPORT.md) · [사양](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-MOUNTAIN-TRAIL-285.md).
<!-- mountain-trail-285:end -->
'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/DECISIONS.md']:
    p=ROOT/rel;s=p.read_text(encoding='utf-8-sig');s=re.sub(r'<!-- mountain-trail-285:start -->.*?<!-- mountain-trail-285:end -->\n?',lambda _:note,s,flags=re.S);p.write_text(s,encoding='utf-8')
spec=ROOT/'Docs/Specs/SPEC-MOUNTAIN-TRAIL-285.md'
spec.write_text(facts,encoding='utf-8')
print(json.dumps({'checks_pass':passed,'checks_fail':failed,'preserved':len(baseline),'changed':changed,'source_files':len(sources),'review':str(OUT/'REVIEW.html')},ensure_ascii=False))
