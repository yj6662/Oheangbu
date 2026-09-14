# SPEC-SPIKE-WORLD-LOOKDEV — 수묵담채 월드 룩 실증 스파이크

> 2026-09-11 후속 규약: 이 문서의 단색 하늘·후처리 미추가 조건은 기존 C2 기준 씬의 계약이다. 사용자 결정 #204에 따라 분리 제작 씬은 `SPEC-EA-PROLOGUE`의 얇은 수묵 Skybox·고정 노출·약한 Bloom·SMAA를 사용한다. C2 기준과 공용 프로필을 소급 교체하지 않는다.

| 항목 | 값 |
|---|---|
| 상태 | **TEST** (2026-09-06 창설 — 예준 지시 "Stage 1 폐광 그레이박스 전에 수묵 담채화 풍의 세상이 실제 구현되는지 실증". 문답 12건 완료 → DECISIONS #150~#157) |
| 작성 | 2026-09-06 |
| 선행 | SPEC-SPIKE-INK-LOOKDEV **PASS**(§5.4 한지=검증 소품·§6 검증 문법·§10 예외 3 asmdef 승계) · SPEC-ART-INK-LOOK **PASS**(§4.1 팔레트·§4.5 Bloom·§7 Non-Goals 「월드 라이팅=레벨 몫」) · SPEC-SPELL-FX-ASSETS **PASS**(§4.2 무텍스처·무발광·§4.7 GUID 참조·§9 예외 2 육안) · SPEC-SPELL-FX-REWORK **TEST**(InkBody 조명 모델·검수 도구 선례) |
| 근거 | ART-CHARTER · ART-COLOR(LOCKED, hex TEST) · **ART-INK**(발광 상한·역번짐·광원 3등급 LOCKED) · ART-SKY(강토 Skybox LOCKED) · ART-SILHOUETTE · LDB-CHARTER 원칙 1·2 · **LDB-ATTRACTION**(인력 3층·확정 인력원·봉수 LOCKED) · LDB 7장·Combat 8장(시야 교란 기각) · NARR 서막(LOCKED) · Constitution 제2조 기둥 3·제3조 4·5항·제5조 슬롯 1+2 · PROD-CHARTER·PROD-AIASSET·PROD-PIPELINE · 원천 `참고_오행부_맵표현생성가이드.md`(가설) · 레거시 MandateOfInk 셰이더(예준 자작) · DECISIONS #75·#134·#136(교훈)·#142·#143·#147·**#150~#157** |

## 바이블 추적표 (절대 제약 → 본 Spec 반영 지점)

| 제약 | 출처 | 반영 |
|---|---|---|
| 채색=의미 — 환경은 채색 대상 아님 · 저채도 세계 | `Constitution:33` · `ArtAudio:14,20` | §5-1 환경 재질 출력 = 먹↔소지 lerp + `_TintRetain`(갈색 슬롯만) · §6 B2 |
| 기본 팔레트 한지 #F7F1E4 · 먹 #2A2622 · 갈색 #8A5A33 · 색 단일 출처 | `ArtAudio:19` · ART-INK-LOOK §4.1 | §5-8 `_Oh*` 전역 1경로 · `InkColor` 정식 필드 · §6 A2 |
| 빛은 전구가 아니라 먹이다 · 하등(환경) Emission 금지 · 지속 발광 = 인력 광원뿐 | `ArtAudio:29,31` | §5-1 Emission 프로퍼티 부재 · §5-2 지속 발광 1파일 · §6 A1 |
| 역번짐 — 프롤로그 광맥이 정본 사례 | `ArtAudio:30` | §5-2 「빛의 실체 + 이웃 램프의 물러남」 · §6 B7 |
| 오염은 빛나지 않는다 · 날것 마석 빛은 원석에만 탁하게 | `ArtAudio:32` | §5-2 원석 탁화 `GetRawVeinColor` · §6 B8 |
| 강토별 Skybox · 시간대 폐기 | `ArtAudio:37` · #75 | §7 Non-Goal(하늘 = 소지 단색, #154) |
| 앵커 실루엣은 배경과 한 번에 분리 | `ArtAudio:44` | §6 B6 |
| 아트는 인식을 방해하지 않는다 | `ArtAudio:15` | §5-3 주입점 450(작도 획 포스트 밖) · §6 A4 |
| 인력 3층 · 크리티컬 인력 화면 내 최강 · 광맥 = 오행색 · 봉수 = 오행색 불빛 | `LDB:96,97,100` | §5-2 광맥 오행 탁광·봉수 색 슬롯 · §5-3 마커 면제 · §6 B8·B10 |
| 시야 교란 기각(LEGACY) · 후반 황경 한정 | `LDB:115` · `Combat:87` | #151 4조건 · §6 B3·B10 |
| 프롤로그 = 청림 산간 폐광 · 어둠→아침 · 광맥이 길을 부른다 · 폐광 = 키트 원형 | `NARR:180` · `LDB:34,44,57` | §5-11 씬 구성 · `Prefabs/Kit/Mine_*` |
| HUD 화이트리스트 4종 | `Constitution:32` | §7 Non-Goal(HUD 무접촉) |
| 현행 아트 = 자리표시자 → 부분 해제 형식 | #134 · #142 · **#155** | §1 범위·불변조건·살아남는 자산 |
| 완주 전 룩 게이트 | #136 교훈 | §8 G2(P1 말 정지컷) |
| Validation 사전 선언·측정 후 이동 금지 · 보고 3단 | INK-LOOKDEV §6 · BRUSH-RENDERER §9 | §6 · §12 |
| 가이드 = 원천, 확정분만 승격 | `BIBLE_INDEX:75` · **#157** | §6 가설 항목(B2·B3·B4·B5) · §14 승격 후보 |

## 1. 목적

「먹 · 여백 · 거리 원근 · 먹선 · 역번짐」 5요소가 오행부 스택(URP 17.3 Forward+ · RenderGraph · SSAO Source=DepthNormals · 1인칭)에서 성립하고,
**예준이 승인한 Recraft 목표 컷과 같은 그림 언어로 읽히는가**를 갱 입구 안팎 한 장면으로 실증한다.
산출물(셰이더 계약 3종 · `WorldLookDriver` · `Prefabs/Kit/Mine_*` · `WorldLookAudit`)이 그대로 Stage 1(프롤로그 폐광 그레이박스)의 룩 툴킷이 된다.
#134 부분 해제 2호(#155): 범위 = 이 장면·컷 3장 / 불변조건 = 무텍스처·환경 Emission 0·색 단일 출처·인식 무접촉·HUD 4종 / 살아남는 자산 = 구조.

## 2. 소비하는 계약 (변경 금지)

- `ElementPaletteSO.PaperColor`(#F7F1E4)·`WoodColor`(#8A5A33)·`GetBaseColor(초성)` — 색 단일 출처. 밝히기의 산술 = `lerp(색, PaperColor, k)`(`ElementPaletteSO.cs:94-98`)
- `InkBody.shader` 3패스 SSAO 계약(`:389-442` DepthNormals) · 광원 「방향만」 원칙(`:297`) · Emission 프로퍼티 부재
- `VP_InkLook` Bloom threshold 1.0 · intensity 0.6 · scatter 0.7 (ART-INK-LOOK §4.5) — **#156 집행 후 전 씬의 기본 Volume**
- `PlayerRig.prefab` 1인칭 단일 카메라(FOV 60 · far 1000 · 후처리 on) · `DevSceneKit` 빌더 패턴 · `SpellRangeSceneBuilder.MovePlayer` 포즈 계보
- MCP 운용 수칙(`run-20260830.md` §MCP): `tests-run` 금지 · `script-execute` 불능 → `Scripts/Editor` 정적 string 메서드 + `reflection-method-call` · 정지→refresh→재생

## 3. Goals

1. 갱도 끝 어둠 → 광맥 역번짐 → 입구 역광 → 아침 산수(능선 3겹·소지 하늘)가 **한 장면**에서 성립
2. 5요소 각각에 **정량 계측 + 예준 육안** 판정(§6)
3. 걸어 다닐 때 깨지지 않음(먹선 지글거림·원경 팝) — 보행 프레임 시퀀스 계측
4. 맵 가이드 가설(§2·§3·§4·§5·§7·§22·§18)의 통과/기각 기록 → 승격 후보 확정(#157)
5. 크레딧: Meshy **0** · Recraft 6장 · 외부 소스 0

## 4. 불가침 (위반 = 버그)

- Drawing · Core · Combat · Presentation · Spellcraft **무변경** — 인식·판정 무접촉
- 환경 셰이더에 `_EmissionColor` 등 Emission 프로퍼티 **없음** · 지속 발광은 `InkLightSource.shader` 1파일
- 머티리얼·씬 에셋에 색 값 저장 **0** — 화면의 모든 색은 `_Oh*` 전역(팔레트) 경로
- `_OhWorldPost == 0`이면 포스트는 **분기로 원본 반환**(NaN 항등) — 드라이버 없는 씬 픽셀 불변
- `Assets/Spike/` 미사용 · asmdef `autoReferenced` 미접촉(Exit Blocking 부채 불증)
- 코드에 미감 상수 금지 — 수치는 재질·프리팹·SO
- 한글 `[Tooltip]` 금지(ShaderLab 파서 — 레거시 교훈)

## 5. 설계 (10층 · 커스텀 RenderFeature/RenderPass/VolumeComponent 코드 0줄)

1. **재질 `Shaders/InkWorld.shader`** — 환경 불투명 1종. LightMode `UniversalForwardOnly` / `ShadowCaster` / `DepthOnly` / `DepthNormals`(InkBody `:389-442` 복제). pragma `_MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _SHADOWS_SOFT _ADDITIONAL_LIGHTS _CLUSTER_LIGHT_LOOP _SCREEN_SPACE_OCCLUSION`(`_FORWARD_PLUS` 폐기 — `Core.hlsl:8-11`). 프래그먼트: `main = ndl×shadowAtt`(풀 램버트) → `add = Σ Luminance(light.color)×att×ndl×_AddLightGain`(클러스터 루프, `inputData.normalizedScreenSpaceUV/positionWS` 변수명) → `luma = saturate(main+add+_AmbientLevel)×AO` → `tone = smoothstep(_InkPoint, _PaperPoint, luma + (Fbm3(posWS×_CelNoiseScale)−0.5)×_CelNoiseStrength)` → `×(1−rim×_RimDark)×(1−saturate(−n.y)×_UndersideInk)` → `col = lerp(_OhInkColor, _OhPaperColor, tone); col = lerp(col, _BaseColor×luma, _TintRetain)`. 갈색은 `_UseWoodColor` 스위치로 `_OhWoodColor` 전역(MPB 금지). 프로퍼티 전부 무색 수치. 레거시 `S_ToonLitTemp:194-241`·`S_ObjectInkWash:165-172` 이식.
2. **광원 실체 `Shaders/InkLightSource.shader`** — Unlit. `col = lerp(_OhInkColor, src×_SourceIntensity, glow)` — `src` = `_UseVeinColor` 스위치(1 = `_OhVeinColor` 광맥 / 0 = `_OhBeaconColor` 봉수 — 둘 다 팔레트 `GetRawVeinColor(초성)` 전역, 초성은 드라이버 필드 = 의미만 저장). 띠 마스크(Fbm3 굽힘·끊김) 아래 `_Cutoff`는 **clip**(판 밖 벽이 그대로 보인다 — 판 전체를 먹으로 칠하면 역번짐 위에 검은 사각형이 생긴다), `_CoreStart` 아래는 먹 테두리(마스크 밖 = 먹). `_SourceIntensity` 1.0(LDR, 최대 채널 ≈0.41) · A/B 1.6 [TEST]. **DepthNormals 알파 −1 마커**(법선 텍스처 `R8G8B8A8_SNorm`, 클리어 a=1, 표준 a=0 → 3값). 프리팹 `VeinBand` = 판 + 백색 Point Light.
3. **포스트 `Shaders/InkWorldPost.shader`** + `PC_Renderer` **URP 내장 `FullScreenPassRendererFeature`** 서브에셋(인스펙터): injectionPoint **450 BeforeRenderingTransparents**(작도 획·InkSpray·InkDust 포스트 밖 · 깊이/법선/SSAO 유효) · `fetchColorBuffer 1` · `requirements Depth|Normal`. 순서: 깊이 소벨 `gradient/max(depth×0.08,0.02)` + **중심 깊이<far 가드** → 법선 소벨(`hasNormal=step(0.25,dot(n,n))` · `_NormalEdgeFadeDistance` · 떨림 해시 = 깊이 복원 월드 좌표 기본/화면 토글) → `lerp(color,_OhInkColor,edge)` → 원경 소지 씻김 `fade×strength×(1−keep×darkness)×(1−lightMarker)`(`_CameraNormalsTexture.a<−0.5` 직접 샘플) → `if (_OhWorldPost<0.5) return 원본` → `_DebugView` 0~4. 세피아·그레인·비네트·종이결 **없음**. 레거시 `S_InkPostTemp:72-88`·`S_InkWashV1:80-111` 이식.
4. **조명** — Directional 1개 Euler (18, 160, 0) [TEST](저각 아침 역광 · 문턱 햇빛 띠 ≈9m 계산치 · 우벽 z≈−8 햇빛 조각이 광맥과 경합 가능 — E 판정 전제). 백색·Intensity만. RPAsset 그림자 설정 무변경. Point Light 광맥 3 + 봉수 1.
5. **하늘** — 카메라 SolidColor = `_OhPaperColor`(#150·#154). 깊이 far·법선 0 → 자동 여백.
6. **안개 없음** — `fog 0`·Buto 미등록. 원근 = 포스트 단독(레거시: 재질 경사·고도 먹·안개 = 밴딩).
7. **지형 = 메시** — 갱도 큐브 **프리팹 키트** `Prefabs/Kit/Mine_{Straight6m,Timber,Rubble,Portal}` / 골짜기 절차 heightfield(**MeshCollider**) / 능선·앵커 폴리라인 압출 3겹(`castShadows Off`). Terrain·ProBuilder·Blender·Meshy 0.
8. **색 공급 `Scripts/App/WorldLookDriver.cs`**(`[ExecuteAlways]`) — `ElementPaletteSO` → `Shader.SetGlobalColor(_OhInkColor/_OhPaperColor/_OhWoodColor/_OhVeinColor/_OhBeaconColor)` + `SetGlobalFloat(_OhWorldPost,1)` + 카메라 배경; OnDisable → 0. **실측(2026-09-06)**: `SetGlobalColor`는 Properties 미선언 전역에 sRGB→리니어 변환을 **하지 않는다**(먹 0.165가 리니어로 들어가 화면 (113,108,103) 회색) → 드라이버가 `.linear`로 넘긴다. `Camera.backgroundColor`는 엔진이 변환한다(배경 = 247,241,228 정확). **전역 기본값 검정 → 드라이버 없는 씬에서 InkWorld는 검정**(#155 결합 명시).
9. **Volume** — #156 집행으로 `PC_RPAsset` 기본 = `VP_InkLook`(Bloom만). 씬 Global Volume도 `VP_InkLook`. Bloom "off" 계측 = `intensity.Override(0)`(active=false는 상위 레이어 폴백).
10. **계측 `Scripts/Editor/WorldLookAudit.cs`**(`public static string`, 1호출=1프레임 · 캡처 = 임시 카메라 + sRGB RT `Capture/CaptureCut` — 리그·플레이어 위치·플레이 여부와 무관한 고정 포즈, PNG = 화면 색공간) + `Scripts/App/Dev/LookdevWalker.cs`(UniTask, 웨이포인트 보행·자체 중력·프레임 캡처; Audit의 정적 래퍼 `Walk/Hold`).

**정본 충돌 해소** — 광원 hue 폐기(#153) · 먹 정식 필드 `InkColor`(#153) · Emission 부재 · 역번짐 = 빛의 실체 + 이웃 램프(#152) · 앰비언트 = `_AmbientLevel` 스칼라 · 하늘 = 소지(#150·#154).
**asmdef** — 팔레트 → BrushRender / Driver·Walker → App / Builder·Audit·DevSceneKit → EditorTools(+`Unity.RenderPipelines.Universal.Runtime`, §10 예외 ①) / Presentation·Core·Drawing·Combat 변경 0.

**5-11. 실증 씬 `Scenes/Dev/C1_WorldLookdev.unity`** (형상 = 전부 코드 조립 · 빌더 `WorldLookdevSceneBuilder` 배치표 단일 출처)

| 요소 | 위치·규모 | 재질 |
|---|---|---|
| 갱도 `Mine_Straight6m`×6 | z −36~0 · 3×3m · 벽/천장/바닥 분리 큐브 | `M_InkWorld_Tunnel`(`_AmbientLevel` 0.03) |
| 갱목 ×12 · 낙석 · 입구 프레임 | 3m 간격 ㄷ자 · 막장 · z 0 | `M_InkWorld_Wood`(WoodColor) · Tunnel |
| **광맥 `VeinBand`×3** | 우벽 x +1.5, z −30/−20/−11, 막장→입구 약→강 | `M_InkLightSource_Vein` + Point range 6/7/8 [TEST] |
| 골짜기 heightfield 128² | z 0~140 · x ±80 · 완만 내리막·길 홈 | `M_InkWorld_Rock`(0.35) |
| 능선 3겹 · 앵커 거목 · 봉수 | z 140~180 / 280~340 / 560~640 · 높이 40/70/110 · 앵커·봉수 z≈480 | `M_InkWorld_Ridge`(0.15) · `M_InkLightSource_Beacon` |
| 리그 · 귀환 포탈 | 스폰 (0, 지면+1.1, −34) · 포탈 z+20 우측 | `DevSceneKit.InstantiateRig` · `AddReturnPortal(Vector3)` |

거리 규약: 근경 0~25 / 중경 25~140 / 원경 140~640(far 1000). `_PaperFadeStart` **90m** · `_PaperFadeEnd` **420m** · strength 0.9 · keep 0.55 = **씬 최원경의 ≈15% / 65% 비율**(레거시 3회 재조정 교훈 — 비율로 기록, 다음 씬은 재조정 없이 산출). `_NormalEdgeFadeDistance` 140m.
컷: **1** 막장 (0,−30,yaw0,pitch0) / **2** 문턱 (0,−3,0,0) — **승인 컷 대비** / **3** 아침 산수 (6,+14,yaw−25,pitch−3). 동선: 막장 → 광맥 띠 → 문턱 → 골짜기 z+30(≈70m).

**5-12. Recraft 목표 컷 `Tools/Generate-LookdevRefs.ps1`** — recraftv3 · `RECRAFT_API_KEY`(.env, 출력 금지) · size **1820x1024**(16:9 — 렌더 컷 FOV 60 정합) · 텍스트만·참조 업로드 0 · 출력 `Docs/Refs/Lookdev/ref_cut{1,2,3}_{a,b}.png` → 예준 승인 **컷2 1장 필수** → `approved_cut2.png` → §6 B1 밴드 실측 고정. 프롬프트 원문은 스크립트가 단일 출처(§4.6 관행).

## 6. Validation (판정 기준 — 사전 선언, 측정 후 이동 금지 · 색공간 = PNG sRGB, 리니어 계산치 병기)

| # | 항목 | 기준 | 정량/육안 · 도구 |
|---|---|---|---|
| A1 | 발광 화이트리스트 | 지속 발광 셰이더 = `InkLightSource` 1종 · 환경 재질 Emission 프로퍼티 0 · `Hanji.shader` 월드 투입 0 | 정량 · `MaterialGate` |
| A2 | 색 단일 출처 | `_Oh*` 전역 = 팔레트값(`.linear` 비교 오차 0) · 씬 머티리얼 색 프로퍼티 0 · 카메라 배경 = PaperColor | 정량 · `MaterialGate`+`PaletteProbe` |
| A3 | DepthNormals·SSAO | `_DebugView 3` 환경 영역 0벡터 픽셀 0 · 갱목 밑동 반경 0.3m SSAO on/off 차분 존재(평균 AO<0.97) | 정량 · `LuminanceProbe` |
| A4 | 작도 무접촉 | Drawing/Core git 무변경 · 룩 씬 8획 작도 픽셀의 포스트 on/off 차분 0 | 정량 · `Capture` 쌍 |
| A5 | 타 씬 불변 | C1_TestHub 픽셀 차 0(피처 등록 전/후) — **P2 선행 게이트** | 정량 · `Capture`+diff |
| A6 | 경고 0 | 셰이더 컴파일 경고·RG assert·FSPRF 경고 0 | 정량 · console |
| A7 | RG·Forward+ 동작 | Frame Debugger에 FSPRF 1회(SSAO 뒤·투명 앞) · 검은 화면 없음 | 육안(도구) |
| B1 | 승인 컷 지표 밴드 | **승인 컷 = `Docs/Refs/Lookdev/approved_cut2.png`(ref_cut2_b, 2026-09-06 예준 승인) 실측(sRGB PNG)**: 소지 비율(luma≥0.85) **0.163** · 먹 비율(luma≤0.202) **0.423** · 저채도(S<0.15) **0.476** · luma Q25/50/75 **0.100/0.323/0.749** → 렌더 컷2 각 지표 **±0.15** 안 [TEST — 고정, 이동 금지] | 정량 · `Histogram` 동일 도구 |
| B2 | 가이드 §5 「먹 70~80%」 | 컷2·3 S<0.15 픽셀 비율 ≥ 0.70 [TEST] — B1과 모순 시 가설 기각 기록 | 정량 |
| B3 | 가이드 §7·§22 3단 원근 | 깊이 밴드 0~90 / 90~420 / 420~<far(**하늘 제외**) luma σ 단조 감소 · 최원경 밴드 vs 소지 ΔE(Lab) < 16 | 정량 · `LuminanceProbe` |
| B4 | 가이드 §3 선택 먹선 · 하늘 가드 | 밴드별 엣지 비율 단조 감소 · 배경(far) 픽셀 엣지 0 | 정량 · `_DebugView 1` |
| B5 | 가이드 §4 AO=먹 | A3 + 밑동·모서리 먹 고임 | 정량+육안 |
| B6 | 실루엣 분리 | 컷2 갱목 vs 바깥 luma 차 ≥0.5 · 컷3 최원경 능선 vs 하늘 ΔL ≥0.12 [TEST] | 정량 · 영역 마스크 |
| B7 | 역번짐 형상 | 컷1 광맥 on/off 차분 = 광맥에서 연결된 단일 영역 · 벽 월드 점 8개 투영 luma 단조 감소 | 정량 · `VeinDiff` |
| B8 | 광맥 색 판독(`LDB:97`) | 광맥 영역 HSV S ≥ **0.30**(절대) [TEST] · 최대 채널 ≤ 0.97(LDR) | 정량 · `Histogram` 마스크 |
| B9 | Bloom 기여 | Bloom on(0.6)/off(intensity 0) 차분: **하늘 제외** 환경 영역 평균 luma 상승 ≤0.01 · 최대 ≤0.03 [TEST] · 하늘·광맥 기록만 | 정량 · `BloomPair` |
| B10 | 인력 면제(#151-ii) | 원경 봉수(≈480m) 씻김 후 채도 보존 ≥80% | 정량 |
| C1~C4 | 보행 | 웨이포인트 8지점 시퀀스 · 엣지 마스크 프레임간 변화율 ≤ [TEST 선언 후] · 떨림 해시 월드/화면 A/B · SMAA A/B | 정량 · `LookdevWalker`+`EdgeFlicker` |
| D | 예산(사전 선언) | 씬 tris ≤150k(예상 ≈40k) · drawCalls ≤200 · 포스트 on/off ≤1.0ms@1080p 에디터(상대) · GPU ms 기록만(사양 미기록) · FX-ASSETS §8 부적용 | 정량 · `RenderStats`+profiler |
| E | 예준 육안 | 「먹으로 읽히는가 / 역번짐이 보이는가 / 광맥이 탁한 빛인가 / 안개가 아니라 여백인가 / 승인 컷과 같은 그림 언어인가」 | 육안 · 컷 3 쌍 + HTML 비교판 |

PASS = A~D 전수 + E + PR 머지. 보고 3단 IMPLEMENTED → VALIDATED → PASS.

## 7. Non-Goals

강토 Skybox·청림 대기 시드(#75·ART-SKY) · 안개(Buto) · 물 · Terrain 파이프라인(가이드 §8~12) · 식생(§16~17) · 하늘·구름·`InkSkyProfile`(§19~25) · 한지 **질감**의 월드 용처 · 실 에셋(KT·JejumokGwana) 룩 · 「담채 20~30%」의 실제 색(팔레트 확장 전제) · 빌드 절대 프레임 · 「색만 따라」 통계 · HUD · 게임 규칙·판정 일체 · Stage 1 레벨 자체.

## 8. 구현 계획 (승인 후)

| P | 내용 | 게이트 | 후퇴 |
|---|---|---|---|
| 0 | 문답 8 → #150~#157 ✔ · Spec ✔ · **#156 집행**(`PC_RPAsset` → `VP_InkLook`) · Recraft 6장 → 승인 → B1 고정 | G0 ✔ · G1 | 기각 항목 Non-Goals |
| 1 | 팔레트 필드 · Driver · `InkNoise3D.hlsl` · InkWorld · InkLightSource · 큐브 갱도 키트 + 광맥 3 — **✔ IMPLEMENTED 2026-09-06**(§12) | **G2 정지컷**(#136 교훈 — 포스트 없이 `WorldLookAudit.CaptureCut` 컷1·컷2 → §13 절차로 예준 판정 **대기**) | 클러스터 루프 실패 → 전역 벡터 폴백(예외 ③) · 「검정」 → `_AmbientLevel·_InkPoint` 1회 · 「먹 그림이지 담채가 아니다」 → 팔레트 환경 담채 열 결정 선행으로 **중단** |
| 2 | InkWorldPost + FSPRF(450) → **A5 먼저** → 골짜기·능선·앵커·봉수 · 빌더 완성 | A5·A6·A7 | NativeRenderPass 충돌 → `m_UseNativeRenderPass 0` · 마커 실패 → 채도 keep(예외 ④) |
| 3 | WorldLookAudit · LookdevWalker · DevSceneKit 확장 | — | — |
| 4 | 계측 A~D · 튜닝 1회 · 컷 3 쌍 · 보행 · AA A/B · HTML 비교판 · E · 3단 보고 · PR | G3·G4 | 「안개로 읽힘」 → 페이드 하향/포스트 층 기각 · 「하늘 미완성」 → 하늘 층 2차 · 지글 → 월드 해시·SMAA |

작업량 ≈ 4.5~5 작업일. 크레딧 Meshy 0 · Recraft 6장(단가 미실측·실측 후 기입).

## 9. 수치 [TEST] 요지

`_InkPoint` 0.04 · `_PaperPoint` 0.68 · `_TintRetain` 0.12(Wood)/0 · `_AmbientLevel` Tunnel 0.03 / Rock 0.35 / Ridge 0.15 · `_CelNoiseStrength` (레거시 승계) · `_RimWidth/_RimDark` (InkBody 승계) · `_UndersideInk` · `_SourceIntensity` 1.0(A/B 1.6) · `_rawTurbidity` 0.25 · 페이드 90/420/0.9/0.55 · 법선 엣지 감쇠 140m · 태양 (18,160,0) · Point range 6/7/8 · 알베도 1.0 그레이박스라 **램프 임계 재튜닝 1회는 확정 비용**.

## 10. Temporary Exceptions

1. `Oheangbu.EditorTools.asmdef` → `Unity.RenderPipelines.Universal.Runtime` 참조 추가 — Reason: FSPRF `SetActive`·Bloom 토글·SMAA 스위치 / Cleanup Gate: 스파이크 종료 시 회수 검토 / **Exit Blocking 아님**(Editor 전용 어셈블리 — 런타임 경계 무손상)
2. FSPRF를 `PC_Renderer`에 전역 등록 — Reason: 렌더러 복제 회피 / 전역 게이트 `_OhWorldPost 0` = 항등(A5) / fetchColorBuffer 블릿 비용 상시 — 실측 후 렌더러 복제 여부 결정 / Exit Blocking 아님
3. 클러스터 루프 손 HLSL 실패 시 전역 벡터 폴백(`_OhLightPos[4]`) — 채택 시 「빛의 실체 = Light」 문구 격하 / **Exit Blocking**
4. 광원 마커(법선 알파 −1) 실패 시 채도 keep 폴백 — #151-ii 증명 약화 / Exit Blocking 아님(기록)
5. `Docs/Refs/Lookdev/*.png`(Recraft 산출, 상업권) 커밋 — Reason: 승인 컷 = B1 밴드의 정본 / Cleanup Gate 없음
6. (승계) asmdef `autoReferenced` + `Assets/Spike/` — 본 스파이크 무접촉(Exit Blocking, INK-LOOKDEV §10-3 승계)

## 11. 검수 결정 (2026-09-05 · 2026-09-06 · 예준)

1. 실증 장면 = 갱 입구 안팎 한 장면 · 2. 가이드 = 가설, 스파이크 = 검증 · 3. Recraft 승인 컷 + 레거시 셰이더 참고 · 4. 걷는 씬 + 컷 3장
5~12. 문답 A~H = #150~#157. **G는 권장(씬 격리)이 아니라 전역 정정** — 술식 룩 재판정을 예준이 감수.

## 12. 구현 기록 (IMPLEMENTED — 착수 후 기입)

- **P0 (2026-09-06)**: 문답 8 → DECISIONS #150~#157 · #156 집행(`PC_RPAsset.m_VolumeProfile` SampleSceneProfile→`VP_InkLook`) · Recraft 6장(`Tools/Generate-LookdevRefs.ps1`, 1820×1024) → **컷2_b 승인** → B1 밴드 고정(소지 0.163 · 먹 0.423 · 저채도 0.476 · Q 0.10/0.32/0.75). Recraft가 「no people/no text」를 무시하는 경향 실측(6장 중 5장에 인물·2장에 문자) — B1은 히스토그램 지표라 영향 미미. 컷1은 구도 미달(외부 시점·청록 워시) — 재생성은 보류(B7은 렌더 자체 계측).
- **P1 (2026-09-06) IMPLEMENTED**: `Shaders/InkNoise3D.hlsl`(레거시 Fbm3 이식) · `Shaders/InkWorld.shader`(4패스 · Forward+ 클러스터 루프 손 HLSL **성공** — 예외 ③ 폴백 불사용 · HasErrors false) · `Shaders/InkLightSource.shader`(3패스 · clip 띠 · DepthNormals a=−1 · HasErrors false) · `ElementPaletteSO` `_ink`/`InkColor`/`_rawTurbidity`/`GetRawVeinColor` + 에셋 재직렬화 · `Scripts/App/WorldLookDriver.cs` · `Scripts/Editor/WorldLookdevSceneBuilder.cs`(§5-11 표 P1 범위 + 골짜기 평판 스탠드인) · 프리팹 `Prefabs/Kit/Mine_{Straight6m,Timber,Rubble,Portal}` · `Prefabs/Lookdev/VeinBand` · 재질 `M_InkWorld_{Tunnel,Rock,Wood}`·`M_InkLightSource_Vein`(무색 수치만) · 씬 `Scenes/Dev/C1_WorldLookdev.unity`(Build Settings 등록) · `Scripts/Editor/WorldLookAudit.cs` `Capture/CaptureCut`(P3 앞당김 — G2 캡처가 리그 위치에 흔들려서) · `Oheangbu.EditorTools.asmdef` +URP Runtime(§10-1 집행). 콘솔 컴파일 오류 0.
  **실측·정정 3건**: ① `Shader.SetGlobalColor` 미선언 전역 무변환(§5-8) — 첫 컷의 「먹이 회색」 원인, `.linear`로 정정 후 먹 = (42,38,34) = #2A2622 정확 ② 빌더에서 씬 생성·에셋 저장 전에 든 팔레트 참조가 「죽은 객체」로 fileID 0 저장(`DevSceneKit.EnsureParryConfig` 함정 재현) → 에셋 참조는 전부 NewScene·에셋 조작 뒤에 든다 ③ 스폰 캡슐이 낙석 콜라이더와 0.15m 겹침 → 낙석을 막장 쪽으로 0.35m.
  **G2 컷 실측(포스트 off, 1920×1080)**: 컷1 먹 비율 0.901 · 소지 0.002 · 저채도 0.027(먹은 갈색기라 S≥0.15 — B2 지표는 소지 영역이 있는 컷2·3용) · Q 0.15/0.15/0.16 / 컷2 소지 0.359 · 먹 0.443 · 저채도 0.486 · Q 0.15/0.29/0.95(B1 승인 컷 대비 소지 +0.196 — 골짜기가 평판 스탠드인이라 예상, P2 능선·씻김 후 재측) / 컷3 = 전부 소지(능선 없음, P2). 역번짐: 광맥 3 이웃 벽이 먹→소지로 물러남 관찰 · 광맥 실체 = 탁한 청록(`GetRawVeinColor('ㄱ')`) · 문턱 햇빛 띠(태양 18°) 바닥에 관찰. Bloom on/off(컷1 post vs raw) 차 = 배경 +4/255 — 계측 B9는 P4.
- **SPEC-WORLD-MAP 연동(2026-09-06, #158·#164)**: 본 Spec의 「far 1000 변경 금지」 계약은 **스파이크 씬(`C1_WorldLookdev`) 한정**으로 재범위 — 월드 씬은 `WorldScaleSO`가 far를 소유하고 `WorldScaleDriver`가 런타임에만 덮어쓴다(리그 프리팹 무변경). P2 `InkWorldPost`에 **`_OhScaleDriven` 전역 오버라이드 분기**(>0.5면 `_OhPaperFadeStart/End/Keep/NormalEdgeFade` 전역, 아니면 재질값 90/420)를 요청받음(SPEC-WORLD-MAP §4 층 1·§10-4) — 스파이크 씬은 Driver 없음 = 재질값 유지. P2 골짜기 heightfield는 월드맵 프롤로그 지리의 시드(yaw 0 고정·Sun 무변경)로 승격 예정.

## 13. 육안 검수 절차 [예준] (착수 후 기입 — 씬 경로 · 번호 단계 · 조정 가능한 재질/프리팹 안내)

**G2 (P1 말 정지컷 — 지금)**: 판정 대상 = `Screenshots/Lookdev/cut1_raw.png`(막장 z −30) · `cut2_raw.png`(문턱 z −3). 포스트·씻김·능선 없음 — 「먹·역번짐·탁광」만 본다.
1. 씬 `Assets/_Project/Scenes/Dev/C1_WorldLookdev.unity` 열고 플레이(스폰 = 막장, 전방 = 입구). 재조립 = 메뉴 `Oheangbu/Dev/월드 룩 씬 조립 (C1_WorldLookdev)`.
2. 판정 3문 — ① 갱도 벽이 **먹**으로 읽히는가(회색 콘크리트가 아니라) ② 광맥 이웃 벽이 **어둠이 물러나는 역번짐**으로 읽히는가(전구 후광이 아니라) ③ 광맥 실체가 **탁한 빛**인가(형광 아님) — 추가로 「먹 그림이지 담채가 아니다」면 §8 P1 후퇴(팔레트 환경 담채 열 선행) → 중단.
3. 조정 슬롯(값은 재질·프리팹이 든다, 코드 아님): 어둠의 깊이 `M_InkWorld_Tunnel._AmbientLevel`(0.03) · 먹↔소지 문턱 `_InkPoint/_PaperPoint`(0.04/0.68) · 역번짐 반경 = `VeinBand_n` Point Light range/intensity(6/7/8 · 0.6/0.8/1.0) · 역번짐 세기 `_AddLightGain`(1) · 광맥 탁도 `ElementPalette_Test._rawTurbidity`(0.25) · 광맥 밝기 `M_InkLightSource_Vein._SourceIntensity`(1.0). 바꾼 뒤 `WorldLookAudit.CaptureCut(1,false)`로 같은 포즈 재캡처.
4. 판정 결과는 §12에 기입 → 통과 시 P2(포스트·골짜기·능선) 착수.

**G3·G4 (P4)**: 착수 후 기입.

## 14. 승격 후보 (PASS 후 SPEC-ART-WORLD-LOOK로)

#157 목록 — §6 B2·B3·B4·B5 통과분 + #150·#151·#152·#153 규약. 보류 목록은 원천 잔류.
