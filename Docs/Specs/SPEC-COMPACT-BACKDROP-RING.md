# 축소 맵 원경 산 백드랍 링

상태: TEST. 사용자 승인: 2026-09-17. 결과는 `Art/World/WorldMacro/Compact/Backdrop/REPORT.md`에서 IMPLEMENTED / VALIDATED / PASS로 구분한다.

기존 지형 산의 **직선 능선·삼각 면**은 `Darker` → `Painterly` → `BroadBrush` → `InkPaintingStudy` 네 차례 재질 시안으로 가려지지 않았다. SPEC-COMPACT-INK-LANDSCAPE와 PROJECT_STATUS가 매 개정마다 이를 잔여 문제로 기록했다. 이 Spec은 같은 문제를 **재질이 아니라 기하**로 접근하되, 플레이 지형이 아닌 **그 바깥 원경층**에서만 시도한다.

---

## BACKDROP-SCOPE — 범위와 비목표 [LOCKED]

### 대상

`W_Demo_Compact` 씬 하나. 축소본 경계(X ±2000 · Z ±3000) 및 기존 배경 스커트(X ±3024 · Z ±4024) **바깥**에 콜라이더 없는 원산 기하를 세운다.

### 비목표 — 이번 작업이 변경하지 않는 것

- `WorldMacroTerrain` 높이장 · 저작된 능선/분지/하천 시트
- 도로 · 강 · NavMesh · 충돌 · 콘텐츠 ID · 저장 슬롯
- 23,879개 배치 트랜스폼
- `BackgroundContext_RenderOnly_NoGameplay_NoColliders` 스커트
- 하늘 · 노출 · 팔레트 · 지역색
- **기존 지형 재질의 대기 밴드** — 파생 재질 205개가 공유하므로 전역 룩 오염을 금한다

### 미지정 익명 원산 — 선점 금지 [LOCKED]

이 링은 **아직 지정되지 않은 방향의 익명 원산** 층이다.

- `LDB:99` **[LOCKED]** 의 원경층 지배 랜드마크(황경=지붕의 바다+**사산**, 철옹=**산등성이를 타는 성곽**, 현강=잠긴 궁궐 지붕+원경 사찰 탑, 전역 앵커=황경 방향 하늘의 대기 차)를 **구현하지 않으며 대표하지도 않는다.**
- `LDB:124` **[LOCKED]** 의 **산군=인왕산**을 구현하지 않는다. 반입할 FBX는 원래 인왕제색도 참조로 생성된 모델이므로, 링 안의 특정 봉우리를 **사산·인왕산·철옹 성곽 산등성이로 명명하거나 그렇게 취급하지 않는다.**
- 후속 작업이 이 링을 **뜯지 않고** 지정 랜드마크를 덮어쓸 수 있어야 한다.
- 균일 링은 `LDB:99`의 방향성을 담지 않는다. 이는 결함이 아니라 **범위 밖**이다 — 형상·재질 검증을 먼저 한다는 사용자 판단(#218)에 따른다.

---

## BACKDROP-SOURCE — 모델 출처 [TEST]

레거시 `MandateOfInk`에서 생성된 Meshy 텍스트→3D 산 모델 2종을 **복사해 사용**한다. 신규 Meshy 생성 없음(크레딧 소모 0).

- `SM_Mountain_Peak.fbx` — 둥근 돔형 단봉
- `SM_Mountain_Ridge.fbx` — 완만하게 겹치는 저능선

반입 위치: `Art/World/WorldCompact/Backdrop/Models/`.

- **base_color PNG 2종은 반입하지 않는다** — 본 Spec의 재질은 Meshy 알베도를 쓰지 않는다. 미참조 15.6MB를 남기지 않는다.
- **`.meta`를 복사하지 않는다** — GUID는 원 프로젝트 자산 DB 소유. Unity가 새로 발급하고 임포터 설정은 `ModelImporter` API로 지정한다(`.meta` 텍스트 편집 금지).

임포트 계약: `useFileUnits`/`globalScale` 1 유지(베이크된 임포트 스케일은 bounds 정규화가 상쇄할 대상), `bakeAxisConversion` 0(원본과 일치 — 축 매핑 전제), `isReadable` 0(`Renderer.bounds`는 직렬화된 AABB에서 나오므로 readable 불필요), `addColliders` 0, `importCameras`/`importLights` 0(발광 상한), `materialImportMode` None, LOD·2차 UV·압축 0.

`importNormals`는 `probe`에서 판단한다 — 재질이 **기하 법선의 경사**로 바위/여백을 가르므로 법선 품질이 곧 재질 품질이다.

---

## BACKDROP-GEOMETRY — 배치 [TEST]

동심 타원 2겹, 총 36개. 결정론적 삼각함수 변이(Perlin 아님)로 반복을 깬다. 모든 수치는 `CompactBackdropRingProfile` SO가 소유한다 — **코드 상수 금지**(CLAUDE.md 데이터 주도).

```
angle   = AngleOffset + i * (360 / Count)
wobble  = 1 + Wobble.x * sin(i * Wobble.y + WobblePhase)
pos     = RingCentre + (sin(rad)*RadiusX*wobble, 0, cos(rad)*RadiusZ*wobble); pos.y = BaseY
usePeak = (i % PeakSelect.x) == PeakSelect.y
yaw     = angle + 180        // 링 중심을 향한다
rotation = Euler(-90, yaw, 0)
target   = BaseSize * (widthVar, heightVar, depthVar)
```

현재값 [TEST · 2026-09-17 실측 반영]: Inner 16개 @ 3400/4500(폭 1600m), Outer 20개 @ 5200/6400(폭 2400m). `BaseY=-120`(밑동 기준), `PeakSelect=(5,1)` → Peak 7 / Ridge 29. 실측 결과 중심 거리 3,350~6,819m, 봉우리 89~1,513m.

**종횡비 보존 [LOCKED — 2026-09-17 추가]** — 크기는 **폭(`BaseWidth`)만 지정**하고 높이·깊이는 **모델 자신의 실측 비율에서 도출**한다. 세 축을 독립 지정하면 Ridge 모델(원본 종횡비 0.154의 낮고 넓은 저능선)이 최대 11.6배까지 뾰족해져 바늘 첨탑이 되며, 이는 프로젝트가 반복 기록한 "날카로운 산 윤곽" 증상을 백드랍에서 재생산한다. `HeightEmphasis`는 그 위에 얹는 보정이며 1.3을 넘기지 않는다.

**bounds 정규화 [LOCKED]** — Meshy FBX 루트는 베이크된 임포트 스케일과 축 보정 회전을 지닌다. `localScale`을 맹목적으로 쓰지 않는다. 인스턴스화 → `localScale=1` → **yaw를 제거한 상태**에서 렌더러 월드 bounds 실측 → 목표 대비 비율로 스케일. yaw가 걸린 AABB는 *회전된 형상*의 경계라 폭·깊이를 독립 제어할 수 없다(Peak 모델에서 축당 ~99m 오차로 실측됨). 축 매핑은 `(tx/sx, tz/sz, ty/sy)` — probe로 확정(legacy 오차 0.0008m vs direct 6,314m).

**밑동 기준 [LOCKED]** — `BaseY`는 모델 중심이 아니라 **밑동**에 적용한다. 중심 기준으로 앉히면 밑동이 허공에 뜨고 봉우리는 지형 능선에 묻힌다.

**경사 분포 검증** — 비균일 스케일은 법선을 왜곡한다. 재질이 `acos(n.y)` 경사로 바위/여백을 가르므로 `probe`가 경사 히스토그램을 지형 산과 대조하고, 지형의 여백/바위 **면적 비율**을 백드랍 자체 분위수에 매핑해 `_CIMountainSlope`를 권고한다(단순 각도 배율은 90°를 넘겨 성립하지 않는다).

**간섭 금지 [LOCKED — 2026-09-17 정교화]** — 실패 조건은 **인스턴스 중심이 스커트 footprint 안에 있고 동시에 봉우리가 스커트 상단+여유에 못 미치는 경우**이며, 이때 빌드를 **중단(throw)**한다. 가장자리 겹침은 **허용이자 의도**다 — 그 겹침이 모델의 잘린 밑동을 가리는 수단이다. 스커트 footprint와 상단 높이는 빌드 시 **실측**한다(가정 금지, 2026-09-17 실측 4218×6442m · 상단 518.8m).

---

## BACKDROP-MATERIAL — 지형과 동일 셰이더 [TEST]

**기존 산과 같은 `Oheangbu/Study/InkPaintingTerrain`을 쓴다.** 수묵담채 룩과 드러난 바위·흙·수목 질감이 지형과 자동으로 일치한다.

### 경계 밖에서도 유효한 근거

초기 검토의 "경계 밖에서 먹 경로 전체가 죽는다"는 판단은 **오판이었다.** 산 먹 가중치는 `InkPaintingStudy.hlsl:47-49`에서

```hlsl
float mass = max(CIMountainWeight(n,soil), geography.r*(1-saturate(soil)));
```

**`max`**다. 경계 밖에서 `geography.r`은 0이라 둘째 항만 사라진다. 첫째 항 `CIMountainWeight`(`CompactInkLandscape.hlsl:161-166`)는 `degrees(acos(saturate(n.y)))`의 **순수 법선 경사 함수**로 월드 좌표에 의존하지 않는다. 실제 먹을 칠하는 `IPMountain(p,n,derivatives)`도 월드 위치·법선·미분만 받는다.

경계 밖에서 잃는 것은 (a) 저작된 지리의 산 가중치 **보정**(경사만으로도 산은 산으로 분류되므로 손실 경미), (b) `_PaintedFarPath` 먼 흙길 분기(원산에 도로가 없으므로 무관) 둘뿐이다.

### 바이블 정합

- `ArtAudio:29` **[LOCKED]** "빛은 전구가 아니라 먹이다 — 강조는 **먹 농도·번짐·획 굵기**로 말한다." 단일 톤 대기 혼합으로 거리를 말하는 방식은 이 어휘를 쓰지 않는다. `InkPaintingTerrain`은 먹 농도·비백·여백으로 말하므로 조항의 어휘 그 자체다.
- `ArtAudio:32` **[LOCKED]** "빛=안전·인력 / 어둠·번짐=위험의 읽기를 배신하지 않는다." 근경과 원경이 다른 색 문법을 쓰면 이 읽기가 원경에서 어긋난다.
- `LDB:101` "높이는 인간 밖의 것만 가진다 — **산**·오염이 키운 것·마석 노두·신수·옛 시대." 원산 링은 이 명제를 지평선에서 반복하는 장치다. 지형 산과 같은 재질이어야 **같은 세계의 산**으로 읽힌다.

### 전용 사본 설정

`Art/World/WorldCompact/Backdrop/M_BackdropMountain.mat` — 현재 산 재질을 **복제**해 시작하고 아래만 변경한다. 두 모델 공용 1개(SRP 배치), `enableInstancing` 사용.

| 프로퍼티 | 값 | 이유 |
|---|---|---|
| `_PaintedGeoEnabled` | 0 | 경계 밖에서 어차피 0 반환. 명시적으로 꺼서 텍스처 페치 절약 |
| `_PaintedFormEnabled`/`_PaintedFormAuthored` | 0 | form 경로는 `geography.r` 게이트라 밖에서 무효 |
| `_PaintedRoadBank`/`_GroundPath`/`_PaintedFarPath` | 0 | 원산에 도로 없음 |
| `_CIEnabled`/`_CIDarkNear` | 1 유지 | 경사 기반 산 먹 경로의 스위치 |
| `_PaintedInkEnabled` | 1 유지 | `IPMountain` 붓결 |
| `_CIFarAirRange` | (1500,3400) → **확장** | ↓ |
| `_CIAirStrengths` | 재튜닝 | ↓ |

**대기 밴드 포화 — 유일한 실질 과제.** `FarAirRange`가 3400m에서 포화하므로 4400m부터 시작하는 링은 내/외측이 같은 톤이 되어 겹산의 깊이가 죽는다. 백드랍 **전용 사본에서만** `_CIFarAirRange`를 넓힌다(시작값 약 3000~9000). **기존 지형 재질의 밴드는 건드리지 않는다.** 구체 수치는 렌더 이미지로만 정해지며 2~4회 반복 조정을 예상한다 — 산술로 확정하지 않는다.

### 나무

실제 수목 메시를 심지 않는다. 4400m에서 개별 나무는 픽셀 이하이며, 드로우콜·정렬 비용과 이동 중 떨림 검증만 늘어난다. 산 재질의 경사별 먹 농담·비백 결이 수목의 인상을 담당한다 — 수묵화의 원산 문법과 일치한다.

### 발광 상한

`InkPaintingTerrain`에 발광 항이 없음을 단언한다(`_EmissionColor` 부재). `ArtAudio:31` 광원 3등급의 어느 등급에도 해당하지 않는 환경 기하이므로 Emission 금지.

---

## BACKDROP-PRESERVATION — 보존 계약 [LOCKED]

- **콜라이더 0 · LODGroup 0.** 발견 시 방어적으로 파괴하고 개수를 리시트에 기록한다.
- 렌더러: `ShadowCastingMode.Off`, `receiveShadows=false`, `LightProbeUsage.Off`, `ReflectionProbeUsage.Off`.
- 백드랍 레이어는 `NavMeshSurface` include 마스크에서 **제외**한다 — 마스크를 읽어 단언하고 가정하지 않는다.
- **멱등 재생성:** 명명된 백드랍 루트 하나만이 유일한 삭제 대상이다. 루트 **바깥** 트랜스폼 수를 파괴 전/재생성 후에 세어 동일성을 단언한다.
- 계층: `WorldMacro_AuthoredGeography` 아래 `BackdropRing_RenderOnly_NoGameplay_NoColliders`, 자식 `Backdrop_{Ring}_{ii}_{Peak|Ridge}`.
- 씬 변경은 전부 Unity 경유(`script-execute` + `SaveScene`). 190만 줄 `.unity`를 텍스트 편집하지 않는다.
- 빌더 진입점은 `CompactInkLandscapeAuthoring.Guard()`를 차용한다 — Play 중 거부, 대상 씬 확인, 커밋률 85% 중단.

---

## BACKDROP-VERIFY — 검증 [LOCKED]

### 실측해 단언한다

| # | 항목 |
|---|---|
| V1 | 인스턴스 36개, Peak/Ridge 분할 |
| V2 | 렌더러 수·삼각형 총계 |
| V3 | 콜라이더 0 |
| V4 | LODGroup 0 |
| V5 | 전 렌더러 그림자/프로브 설정 |
| V6 | 스커트 간섭 없음(방향별 AABB 교차) |
| V7 | 중심 최소/최대 거리 — far clip 22000 여유 |
| V8 | 백드랍 루트 밖 트랜스폼 수 전/후 동일 |
| V9 | 시트 에셋 SHA-256 불변 |
| V10 | 셰이더 컴파일 — `isSupported && !ShaderHasError` |
| V11 | 단일 공유 재질, `HasPropertyBlock` 없음 |
| V12 | **기존 지형 재질의 대기 밴드 불변** |
| V13 | 달성 크기 vs 목표, 경사 분포 vs 지형 산 |
| V14 | 백드랍 레이어가 NavMesh 마스크에서 제외 |
| V15 | 발광 프로퍼티 없음 |

추가 측정: 고정 시점 1080p 전후 스크린샷(증거이며 PASS 아님), Play GPU 중앙값 델타(SPEC-COMPACT-INK-LANDSCAPE의 **0.5ms** 기준 준용), `NavMesh.CalculateTriangulation()` 정점 수 전/후, `Structure()` 다이제스트 전/후 diff.

### 2026-09-17 실측 결과

V1~V6·V8·V10~V12·V15 통과: 인스턴스 36(Peak 7/Ridge 29)·렌더러 36·삼각형 447,766·**콜라이더 0**·**LODGroup 0**·**백드랍 밖 트랜스폼 23,879→23,879**·스커트 침범 0·셰이더 오류 0·**기존 지형 재질 30개 대기 밴드 불변**·발광 프로퍼티 없음. 목표 대비 최대 크기 오차 **0.00024m**. 상세·미검증 목록은 `Art/World/WorldMacro/Compact/Backdrop/REPORT.md`.

### 측정 없이 주장하지 않는다

- 링이 **보기 좋은지**, 백드랍의 바위·흙·수목 질감이 지형 산과 실제로 **이어져 보이는지**. 미술 판단이며 스크린샷+사람 검토만이 답한다. 셰이더 컴파일 PASS는 룩 PASS가 아니다.
- 확장한 `_CIFarAirRange` 값의 적정성 — 포화를 푸는 **방향**은 코드로 확인했으나 구체 수치는 렌더 이미지로만 정해진다.
- 산 밑단 비가시성 — 카메라 높이·피치·FOV·시선별 지형 실루엣에 의존.
- z-fighting 없음 — V6가 **실제로 실행되어 통과했을 때만** 말할 수 있다. 반지름만 보고 주장하지 않는다.
- **이동 중 떨림** — 정지 스크린샷이 대신하지 못한다. 이전 시안마다 반복 기록된 미검증 항목이다.
- 빌드 프레임레이트 — 에디터 Play 계측은 참고치다.
- 23,879 트랜스폼의 **바이트 동일성** — V8은 개수를 증명한다. 값은 `Structure()` diff가 최강 주장이다.

---

## 잔여 · 후속

- `LDB:99` 원경층 방향성(사산·철옹 성곽 산등성이·현강 사찰 탑) 반영 — **미결**
- `LDB:124` 산군=인왕산 실제 구현 — **미결**, 이 링이 선점하지 않는다
- 미술 승인 — **TBD**
