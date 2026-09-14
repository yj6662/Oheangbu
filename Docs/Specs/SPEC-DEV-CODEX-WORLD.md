# SPEC-DEV-CODEX-WORLD — 정본 수묵 월드 씬

| 항목 | 값 |
|---|---|
| State | TEST |
Created: 2026-09-07
Updated: 2026-09-07 — 정본 원경의 사실적 산세·공통 원색·거리 대기 적용
Owner: Codex / 예준 육안 판정

## 1. 요청과 범위

최초 요청: 기존 월드 룩 판정 전에 새로운 씬에 Codex 버전의 새로운 월드를 구현하여 Claude Code 결과와 비교한다. 이 비교 구현과 최초 기술 검증은 §6에 보존한다.

현재 요청(2026-09-07): 예준이 **C2_CodexWorld를 이후 월드 제작의 정본으로 선택**했다(DECISIONS #170). 앞으로 월드 룩·배치 개선은 이 씬을 기준으로 진행한다. 기존 Claude 씬은 과거 비교·기술 검증 자료로 보존한다. 채택과 함께 요청한 원경 농도·바위/산 면 경계 보완은 §8에서 검증한다. 기존 WORLD-LOOKDEV/WORLD-MAP의 전체 PASS나 S2 완성을 소급 선언하지 않으며, 과거 C1 판정 대기를 현재 C2 개선의 금지 조건으로 삼지 않는다.

참조: CLAUDE.md → BIBLE_INDEX → ART-INK / ART-COLOR / ART-SILHOUETTE / LDB-ATTRACTION / PROD-PIPELINE. 기존 SPEC-SPIKE-WORLD-LOOKDEV §5·§13 및 SPEC-WORLD-MAP A13의 팔레트·발광 제한을 따른다.

## 2. 경험과 공간

어두운 폐광에서 나와 굽은 산길을 걷는다. 불규칙한 갱구가 밝은 풍경을 두르고, 비대칭 소나무와 침식된 화강암 사이로 작은 주막과 겹산이 보인다. 실제 충돌 지형과 기존 PlayerRig를 사용한다. 지형은 절차 메시, 건축과 소나무는 생성한 메시 및 조립 부재로 구성한다. 제작 수치는 별도 CodexWorldSettingsSO 및 저장된 씬/재질에 보존한다.

정본 씬: Assets/_Project/Scenes/Dev/C2_CodexWorld.unity
전용 자산: Assets/_Project/Art/CodexWorld
전용 Editor 도구: Assets/_Project/Scripts/Editor/CodexWorld

## 3. 시각 제약

- 환경 색은 기존 ElementPalette_Test의 한지·먹·목재에서만 유도한다. 환경 지속 Emission은 0이다.
- 광맥과 주막 등불만 인력 광원이다. 등불은 WORLD-MAP A13이 의도한 목재↔한지 0.30 파생색을 전용 셰이더에서 계산한다(공용 GetLanternColor 메서드는 아직 미구현).
- 이 절차 생성 월드는 텍스처/PBR/안개 볼륨/Bloom/비네트/세피아/종이결 후처리를 추가하지 않는다. 수령 자산의 원본 셰이더 복구·텍스처 보존은 SPEC-ASSET-INTAKE의 별도 단계이며 이 규칙을 근거로 원본 정보를 제거하지 않는다.
- 가까운 갱구와 소나무는 짙게, 산은 원거리 순서대로 소지에 물러나게 한다. 주광 응답을 제한하여 바위 한 면이 하얗게 날아가지 않게 한다.
- 기존 전투·작도·인식·리그 프리팹·렌더러·공용 팔레트·기존 월드 씬/셰이더는 변경하지 않는다.

## 4. Temporary Exceptions

CodexInkLandscape는 거리별 소지 물러남을 재질에서 계산한다. C2가 기준 씬으로 채택돼도 이 기술은 TEST이며 프로젝트 전체의 포스트 단일 소유 방침을 자동 교체하지 않는다. 원본 InkWorld 및 렌더러 Feature는 수정하지 않는다. 신규 셰이더의 물러남과 별도 안개/거리 포스트를 중복 적용하지 않는다. C2의 지형 개선은 승인 범위이며 Terrain 제작 구조의 전면 전환이나 S2 완성 선언에 해당하지 않는다.

씬 인스턴스의 CameraRigController에만 별도 카메라 설정 복사본을 연결하여 ShoulderStart=false로 시작한다. 기존 리그 프리팹과 전투 설정 정본은 유지하며 V 토글도 유지한다. 공용 VP_InkLook의 Bloom은 비교 씬 전용 VolumeProfile에서 0으로 override한다. R 재시작이 가능하도록 빌드 목록 맨 뒤에 새 씬만 추가한다.

## 5. Validation

1. Unity C# 컴파일, 새 셰이더 지원/오류/4 패스 확인.
2. 저장된 새 씬의 MainCamera·AudioListener·CombatLoopWiring 각 1개, missing script/material 검사.
3. 경로 충돌 높이 샘플 및 Play 진입/종료 시 예외 확인. 전투 기능을 재검증했다고 주장하지 않는다.
4. 갱도·입구·야외·주막 고정 카메라 캡처를 Screenshots/CodexWorld에 저장하고 직접 검토한다. 대표 비교는 눈높이 1.8m/FOV60으로 기록한다.
5. 메시 삼각형·렌더러 총량을 측정한다. 에디터 수치와 실제 빌드 성능을 구분한다.
6. 기존 작업 보존 여부를 해시와 Git diff로 확인한다. 변경된 새 자산 목록과 잔여 한계를 기록한다.
7. 기술 검증과 예준의 미술 판정을 구분한다. 원본은 P1 기술검수 씬이므로 범위 차이만으로 모델 우열을 판정하지 않는다.

## 6. 최초 비교 구현·검증 기록

- 2026-09-07: Spec 선행 작성. 구현 시작. 사용 도구=Unity AI Game Developer MCP, 절차 메시/C# 및 프로젝트 소유 셰이더. 유료 생성 자산 사용 없음.
- 구현: 폐광 34m, 높이 변화가 있는 160×220m 골짜기와 S자 산길, 충돌 암반, 비틀린 소나무와 실제 솔잎 메시, 드문 마른 풀, 맞배지붕·기와골·서까래·창살·열린 문·앞마루·등불을 갖춘 주막, 겹산 4계층+중앙 능선. 시드는 90726.
- 최종 Unity 6000.3.9f1에서 컴파일 완료. 환경 셰이더 지원=true/HasErrors=false/4패스, 등불 셰이더 지원=true/HasErrors=false/3패스.
- 씬 무결성 PASS: 오브젝트452, 렌더러399, 충돌체183, MainCamera/AudioListener/CombatLoopWiring 각1. missing script/material/shader/mesh 0.
- 전체 인스턴스 메시 합계 507,480 tris / 고유 메시44. 컬링 전 에디터 기하량이며 빌드 프레임시간을 측정한 값이 아니다.
- 경로 충돌 높이 111점 PASS(0~220m/2m 간격): 누락0, 최대 오차0.0721m<허용0.15m.
- Play 물리 이동 PASS: 실제 리그와 같은 CharacterController(height2/radius0.5/slope45/step0.3/skin0.08), z0→218→29→주막 접근→앞마루의 421경유점/1,923 Move. 측면 막힘0, 최대 경유점 오차0.0589m, 바닥422표본 누락0. 실제 플레이어 위치 변경0, 임시 검사 객체 제거 확인.
- 실제 Game View에서 1인칭 시작과 플레이어 접지 확인. 최종 재검증(2026-09-07 12:55:03 KST 이후) Error0/Exception0. 도중 검사 보조 코드의 HideAndDontSave 충돌체가 Physics.IgnoreCollision 오류를 내는 결함은 일반 Play 임시 객체로 수정 후 재검증했다. 기존 콘솔 기록은 보존했다.
- 육안 반복 수정: 매끈한 길 경계를 깨뜨림, 바위형 수관을 솔잎 메시로 교체, 갱도 바닥 틈·지붕 뒷면·주막 지면 침투 교정. Mesh CopySerialized 후 CPU/GPU 형상 불일치는 native vertex/index 버퍼 명시 갱신으로 교정했다.
- 고정5컷=Oheangbu/Screenshots/CodexWorld/cut1_mine.png, cut2_threshold.png, cut3_valley.png, cut4_inn.png, cut5_upper_path.png. 모든 컷 1920×1080/FOV60/지면+눈높이1.8m, 카메라 post=true/Bloom0/AA없음. 풍경용 캡처에서는 리그의 임시 몸체 렌더러를 잠깐 숨겼다가 원복한다. 실제 Game View도 별도로 확인했다.
- 비교 원본=같은 위치(0,1.8,-3)/회전0/FOV60의 C1_WorldLookdev를 재캡처한 claude_threshold.png. 원본은 post=false이며 기존 P1 범위를 담는다. 원래 Screenshots/Lookdev/cut2_raw.png도 원본 씬에서 재생성했다.
- 보존 확인: 원본 C1_WorldLookdev·W_Cheongrim_FarSet 씬, InkWorld·InkLightSource, PlayerRig.prefab, ElementPalette_Test.asset의 SHA256 6종이 작업 전후 일치. 기존 staging과 수정 내용 유지. 기존 파일 변경은 상태판의 새 Spec 항목과 Build Settings 끝의 새 씬 등록이다.
- 외부 유료 생성/API 크레딧 사용0. 자산은 절차 생성 원본 및 재생성 도구와 함께 저장했다.

## 7. 검토와 이어서 작업하기

- Unity에서 C2_CodexWorld를 열어 Play. 시작은 입구/1인칭. WASD 이동, 마우스 시점, V 시점 전환, R 재시작은 기존 리그 입력을 사용한다.
- 조립 메뉴: Oheangbu > Dev > Codex World > Build canonical world (regenerate scene). 현재 씬이 dirty이면 먼저 저장해야 한다. 재조립은 C2 씬 배치를 재생성하므로 수동 배치 수정은 별도 씬으로 보존한다.
- 검수 메뉴: 같은 경로의 Capture five views / Validate scene. ProbeTraversal은 Play에서 reflection 호출하는 물리 검사 도구다.
- 배치 기본값은 CodexWorld_Settings.asset, 재질 튜닝은 Art/CodexWorld/Materials. 일반 재조립은 재질 수정값을 유지하며 ApplyFirstReview는 명시적 초기 룩 재적용 명령이다.
- 현재 원경 개선은 `Refresh realistic mountains (preserve placement)`로 산 9개 메시·공유 재질을 갱신한다. `Apply shared mountain surface and atmosphere`는 현재 SO의 원색·대기 값을 공유 재질에 적용한다. 두 명령은 저장된 C2에서 실행하고 배치를 보존한다. `Refresh smooth geometry (preserve placement)`는 산 외의 기존 소유 메시까지 갱신하는 더 넓은 명령이다. `ApplyFirstReview`는 이전 비교용 룩을 되돌리는 명시적 명령이므로 현재 정본 농도를 적용하는 용도로 사용하지 않는다.
- 기준 씬 선택은 완료됐다(DECISIONS #170). 남은 미술 판정은 §8 개선 결과다. 정본 채택은 전투 콘텐츠·퀘스트·완성된 S2·경계 낙사 처리·실제 빌드 최적화의 완료를 뜻하지 않는다.

## 8. 정본 채택 후 원경·암반 개선

### 8.1 요구와 참고

2026-09-07 예준 요청: “원경 산이 내가 제공한 해당 스크린샷만큼의 농도”를 갖고, 바위·산의 로우 폴리 메시 경계가 뚜렷하게 드러나는 현상을 해결한다. 참조 원본은 `C:/Users/yj666/Desktop/오행부 프로젝트/스크린샷2.png`(레거시 프로젝트)다. 넓은 산 덩어리의 짙은 먹 농도와 서로 구분되는 겹별 명도를 참고하며 레거시의 빌딩 배치·텍스처·안개 설정을 함께 이식하는 요구로 확대하지 않는다.

- 원경을 과도하게 소지에 씻지 않고 짙은 산 덩어리로 읽히게 한다. 가장 먼 겹까지 모두 같은 먹으로 뭉개지지 않도록 겹별 명도 차이를 남긴다.
- 바위와 산의 의도하지 않은 삼각면·링·접합 경계를 줄인다. 실제 암반의 큰 꺾임과 실루엣은 보존한다.
- 기하·공유 법선·셰이더 응답을 먼저 조사하고 필요한 분할/법선 보정과 농도 튜닝을 적용한다. 폴리 수 증가나 화면 블러 자체를 완료 기준으로 삼지 않는다. 후처리는 원인을 가리기 위해 작도·전투 가독성을 낮추지 않는다.
- 개선 수치는 기존 SO/재질/생성 도구에서 재현 가능하게 저장한다. 이후 수령 모델로 대체할 후보는 SPEC-ASSET-INTAKE의 분류·적합성 근거를 사용한다.

### 8.2 검증

1. 기존 고정 화각에서 변경 전후 캡처를 비교한다. 원경 농도·겹 분리, 근경 바위 표면, 산 윤곽을 각각 확인한다.
2. 메시 정점/삼각형·공유 법선과 재질 변경값을 기록한다. 이전 §6 기하량을 현재값으로 재사용하지 않는다.
3. Unity 컴파일·셰이더 오류·씬 참조 무결성을 재검사한다. 충돌 기하가 바뀌면 영향 범위의 충돌·보행 검증을 갱신한다.
4. 개선 구현과 기술 검증, 예준의 최종 육안 판정을 분리해서 기록한다. 전체 미술 PASS는 아직 선언하지 않는다.

### 8.3 실행 기록

- 2026-09-07: 정본 채택 후 개선을 실제 C2 씬에 적용하고 저장했다. 바위의 공유 법선과 연속 기하, 산의 촘촘한 곡면 구성으로 삼각면 경계를 줄였다. 해상도와 산4계층의 농도/물러남은 `CodexWorldSettingsSO`에 저장했다. 큰 암반 형태를 남기며 조명 반응·노이즈·림을 제한했다. 화면 전체를 흐리는 블러로 처리하지 않았다.
- `RefreshCanonicalGeometry`: C2 소유 메시22개 갱신(이 갱신 집합의 고유 정점47,595 / 고유 삼각형95,038), 현재 씬 Collider91개 재계산. 배치·GUID와 기존 C1 씬을 보존했다. 갱신 집합 수치와 씬 전체 인스턴스 수치는 아래처럼 구분한다.
- `ApplyCanonicalLook`: 산 재질4개·바위 재질2개와 설정 저장 완료. 고정5컷 전부 재캡처·확인하여 원경 농도가 짙어지고 바위 면 경계가 크게 줄어든 것을 관찰했다. 전경 블러/AA 없이 메시·공유 법선과 재질 반응을 고친 결과다. 예준의 최종 미술 판정이나 모든 경계가 제거됐다는 선언은 아니다.

| 검증 | 실제 결과 |
|---|---|
| 씬 저장 | 성공 |
| 오브젝트 / Renderer / Collider | 452 / 399 / 183 |
| Missing script·참조 | 0 |
| 씬 전체 고유 메시 | 44 |
| 씬 전체 인스턴스 삼각형 | 999,444(이전507,480) — 컬링 전 에디터 기하량 |
| 경로 높이 | 111점, 누락0 / 허용오차 초과0 / 최대오차0.0721m |
| Play 물리 이동 | 421/421경유점, CharacterController.Move 1,923회, 측면 막힘0 |
| 이동 오차 | 최대 측방0.0176m / 최대 경유점0.0589m |
| 이동 중 접지 | 422표본, 바닥 누락0 |
| 실제 플레이어 변위 | 0 — 임시 검사 객체 사용 |
| 최종 에디터 상태 | C2 씬 열림, Play 종료, isCompiling=false |
| 최종 콘솔 | 최근5분 Error0 / Exception0 |

실제 FPS·빌드 프로파일은 측정하지 않았다. 메시 증가 비용은 후속 빌드/LOD 검토에 포함한다. 원장 감사의 수령 모델을 C2에 전부 배치하거나 물리 검증한 결과가 아니다. 새 고정 캡처는 [입구](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut2_threshold.png)·[야외](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut3_valley.png)·[상부 산길](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut5_upper_path.png)에서 확인한다.

## 9. 사실적인 원경 산·공통 원색·거리 대기 [TEST]

### 9.1 구현 계약 (DECISIONS #172)

- 2026-09-07 사용자 요청을 받아 기존 C2 산의 형상과 확산 음영을 사실적으로 개선한다. 수묵 색 기조는 유지하며 다른 환경·전투·보행 지형은 보존한다.
- 시각 검토 원장의 Landscape는 건축 기반 지형판, Yongmeori는 해안 층리 절벽이다. 이를 전체 산으로 확대 복제하지 않고 기존 C2 소유 Ridge 생성 원본을 개선한다. 이번 작업은 새 외부 모델 수급이 아니며 Meshy/Blender로 생성·가공했다고 기록하지 않는다.
- 주능선과 지능선, 비대칭 사면, 다중 규모 침식 형태를 갖는 연속 높이장으로 산 메시를 갱신한다. 공유 법선과 충분한 깊이 분할로 메시 경계를 줄인다. 기존 9개 메시 GUID·배치와 산 외의 자산을 보존한다.
- 산 9개는 하나의 공유 재질을 사용한다. 원색은 같은 팔레트 파생값이며 겹별 ToneFloor/Ceiling, InkDensity, WashStrength는 더 이상 사용하지 않는다. 표면 명암은 실제 법선·주광·동일한 표면 규칙으로만 달라진다.
- 대기는 픽셀별 `d = distance(cameraWS, positionWS)`, `T = exp(-density * max(0, d - start))`, `C = surface * T + atmosphere * (1-T)`로 연속 계산한다. 시작 거리·밀도·대기색의 팔레트 혼합비와 원색/음영 파라미터는 SO에 저장한다. 카메라를 움직이면 같은 산의 색이 바뀌며 재질 에셋은 바뀌지 않는다.
- 산 전용 셰이더가 대기를 한 번 소유한다. 공용 Landscape 셰이더、RenderSettings fog、전역 포스트를 변경하거나 대기를 중첩하지 않는다. §3의 원거리 물러남과 §4의 대기 예외는 산에 한해 이 방식으로 구현한다.
- 수정 전 기준: `Art/CodexWorldBackups/BeforeRealisticMountains/`, `Screenshots/CodexWorld/BeforeRealisticMountains/`.

### 9.2 검증 계획

컴파일/셰이더 오류, 9개 산 공유 재질, 겹별 재질/PropertyBlock 부재, 유한한 메시·법선·삼각형 수, 기존 씬 무결성과 111점 경로 감사, 고정 5컷을 확인한다. 같은 표면을 3개 거리에서 렌더하고 대기 밀도 0의 대조군과 비교하여 조명/원색과 거리 변화가 분리되는지 검증한다. 미술 판정 및 빌드 성능은 실제 검증 범위와 구분한다.

### 9.3 적용·검증 결과 (2026-09-07)

- 기존 9개 Ridge 메시를 256×128 연속 높이장으로 갱신했다. 비대칭 주능선·분기형 지능선은 도메인 변형/다중 규모 능선 노이즈로 구성한다. 실제 지형 스캔이나 수리 침식 시뮬레이션은 아니다. 양 끝의 급한 절벽을 수정하고 공유 경사 법선으로 삼각망 음영을 줄였다.
- 공통 재질=`Art/CodexWorld/Materials/Mountain_Shared.mat`, 셰이더=`Oheangbu/CodexMountain`(Forward/DepthOnly/DepthNormals 3패스). 기본색은 공통 `_RockTone`으로 팔레트에서 유도한다. 동일한 확산광·풍화 표면 함수·공간 곡률 음영을 모든 산에 적용한다. 기존 Ridge_0..3 재질과 SO 겹별 프로필은 비교용 LEGACY 자료로 남고 정본 산에서 참조하지 않는다.
- SO 값: MountainRockTone=.16, Ambient=.30, Diffuse=.95, SurfaceVariation=.18, AtmosphereStart=65m, AtmosphereDensity=.00085/m, AtmosphereTone=.68. 이 값들은 TEST 조절값이다. Inspector 변경 후 위 Apply 메뉴로 저장된 공통 재질에 반영한다.
- `CodexMountainAudit.Validate()` PASS: 산9·공유재질1·PropertyBlock0·산 Collider0·그림자 투사0·셰이더 지원/오류검사 정상·유한 정점/단위 법선 이상0. 산 합계594,432tris. 씬 총량1,521,462tris(이전999,444 대비+522,018), 고유 메시44. LOD/실제 빌드 프레임시간 검증은 아직 하지 않았다.
- `CodexMountainAudit.ProbeDistance()` GPU 대조 검사 PASS: 같은 월드 위치의 표면을 고정 직교 배율로 두고 카메라만200/500/900m 이동. 대기 OFF RGB 차이0; 대기 ON과 지수 감쇠 수식의 최대 선형 RGB 오차2.9802322e-8. 측정 R값 OFF=.034107745(3거리 동일), ON=.09984364/.22152738/.34227642. 원본 재질은 변경하지 않고 임시 검사 재질·카메라·메시를 제거한다. 원본 결과=`Screenshots/CodexWorld/mountain-distance-probe.json`.
- `CodexWorldAudit.Validate()` PASS: 452objects/399renderers/183colliders, 카메라·Listener·CombatLoopWiring 각1, missing0. 보행 경로111점 누락/허용초과0·최대오차.0721m. 보행·전투 코드와 충돌 메시 변경0이며 이번 작업의 Play 이동 재검증으로 이전 기록을 재표기하지 않는다.
- 고정5컷(1920×1080/FOV60/눈높이1.8m/post=true/AA없음)을 다시 렌더해 확인했다. 기존 수묵 색조에서 산세와 확산 음영을 사실적으로 바꾼 구현이며 사진 기반 PBR 산으로 판정하지 않는다. 사용자 미술 판정은 대기한다. 최근15분 Unity Error0/Exception0.
- 보존: C1/InkWorld/PlayerRig SHA256이 이전 기록과 일치. 수정 전 C2와 비교해 Transform 변경0, Renderer 변경9. Unity 저장 시 기본 UniversalAdditionalLightData 4개가 기존 광원에 직렬화됐으며 광원 위치·색·강도 변경0. 씬·설정 저장 완료.
- 외부 생성 API 사용0. 기하/재질/거리 검사 코드는 프로젝트에 남아 재현 가능하다.

## 10. 정본 바위 전면 교체 [TEST]

### 10.1 요청·제작 계약

2026-09-07 요청: 맵에 깔린 바위를 전부 없애고 아트 디렉션에 맞게 다시 생성한다. 외부 도구 사용 허용. ART-COLOR/ART-INK/ART-SILHOUETTE 및 PROD-AIASSET(#171)의 수령 자산 우선 원칙을 따른다.

- C2에서 기존 `Granite_*` 메시를 쓰는 모든 바위 인스턴스를 제거한다. 대상에는 거대 버팀 암반·가는 절벽 조각·산포석·갱구 바위·낙석·주막 접근석을 포함한다. 원경 Ridge와 보행 지형·건축·소나무·광맥은 보존한다. 백업은 `Art/CodexWorldBackups/BeforeRockReplacement`.
- 시각 검토 원장의 SeyeonjeongPavilion 암석을 실제 기하 원본으로 재사용한다. Blender MCP에서 별도 작업 씬에 임포트하여 형상 보존·면 경계 보정·단위/피벗 정리·LOD 및 충돌용 메시를 만들고 별도 FBX로 출력한다. 원본 FBX/재질은 수정하지 않는다. 부족 모델이 확인되지 않아 Meshy 호출은 필수가 아니다.
- 바위는 둥근 계란·가는 기둥 대신 넓은 접지부, 절리면과 풍화된 모서리, 크기 차이가 있는 암반 군집으로 읽히게 한다. 수묵 팔레트·무발광·부드러운 명암을 사용하고 과장된 세로 줄무늬를 제거한다.
- 새 배치는 산기슭의 큰 암반 → 중간 파편 → 드문 작은 돌 순서로 구성한다. 길·주막 접근·갱구 시야를 열고 작은 돌은 반복적으로 균등 산포하지 않는다. 모든 생성 수치와 원본 출처를 기록한다.
- 검증: 기존 Granite 참조0, 새 모델/재질/LOD/충돌 참조 정상, 고정5컷 직접 확인, 경로111점 및 Play421경유점 이동 검사, Unity 오류 확인. 미술 판정과 빌드 성능 검증은 별도다.

### 10.2 적용·검증 결과 (2026-09-07)

- 기존 C2 `Granite_*` 메시 인스턴스 **254개 제거**, 최종 새 바위 **202개** 배치. 기존 바위 참조0. 낮은 암반·절리 블록·파편 군집·갱구·낙석·주막 접근석을 모두 새 모델로 교체했다. 이후 재배치 실행의 removed=0은 최초 제거 후 이미 새 모델만 남았다는 뜻이다.
- 실제 제작 도구는 **Blender MCP**. 원본은 시각 검토된 SeyeonjeongPavilion `SM_Rock_A/C/D/F/I/K/L/N.fbx` 8종이다. 중복 정점 정리 → 표면 세분화/부드러운 법선 → 근경 최대12k 삼각형 감축 → LOD1/2 → 충돌 프록시 → 단위·바닥 피벗 정규화 → FBX 출력 → Unity 변환 메시/재질/LODGroup 배치. Meshy나 새 AI 생성 모델은 사용하지 않았고 크레딧 사용0이다.
- 모델 원본/텍스처는 수정하지 않았다. 파생 산출물=`Assets/_Project/Art/CodexWorld/RockKit/`, 출처·정규화·메시 수치=`RockKit.provenance.json`, Blender 작업본=`Art/Blender/C2_RockKit.blend`, 재현 스크립트=`Tools/Blender/build_c2_rock_kit.py`. 파생 모델은 원본 에셋의 라이선스를 따른다.
- Unity `CodexRockKit.ReplaceAll()`은 원본8종의 메시·축/스케일을 먼저 확인하고 새 배치를 구성한 다음 기존 인스턴스를 제거한다. 재배치 메뉴=`Oheangbu > Dev > Codex World > Replace all rocks with refined kit`. 일반 BuildScene도 UseDerivedRockKit=true일 때 이 단계를 거쳐 이전 바위 룩으로 돌아가지 않는다.
- 새 수묵 재질 `Rock_Refined` / `Rock_Refined_Mine`: 강한 세로 붓줄/획0, 작은 안료 변화·부드러운 음영·팔레트 기반 먹 농도. 바위만 재질을 바꾸며 기존 골짜기 바닥/갱도 벽 재질과 원경 거리 대기는 유지한다. SO의 RockSurfaceTone=.35/Ambient=.28/Diffuse=.72, LOD 전환 화면비=.18/.06/.003. 설치 크기는 균일 스케일로 원본 비율을 보존한다.
- `CodexRockKit.Validate()` PASS: 새 바위202, 종류8, 각LOD3, 기존 Granite0, 오류0. 충돌110개×160tris=17,600tris. 모든 인스턴스를 LOD0으로 가정한 바위 합계2,265,996tris이며 실제 화면은 LOD·컬링으로 선택한다.
- 일반 씬 감사 PASS: Objects1007/Renderers751/Colliders203, 카메라·MainCamera·AudioListener·CombatLoopWiring 각1, 누락 script/재질/셰이더/메시0. 감사의4,124,452tris는 비활성 LOD까지 모두 합친 에디터 저장량이며 동시 렌더량이나 이전 단일LOD와 동일 조건의 성능 수치가 아니다. 실제 빌드 FPS 미측정.
- 보행 지형111점 검사 PASS: 누락0, 허용초과0, 최대오차.0721m. Play CharacterController 이동 PASS: 421/421경유점, Move1923, 측면막힘0, 접지422점누락0, 최대측방편차.0176m/경유점오차.0589m, 실제 플레이어 변위0, 검사 객체 제거. 기존 전투 입력 검증으로 확대하지 않는다.
- 고정5컷 재촬영·직접 확인: `Screenshots/CodexWorld/cut1..5` 파일. 기존 바위254개가 사라지고 새 군집 배치·갱구 프레임·주막 접근석을 확인했다. 최신10분 Unity Error0/Exception0. 미술 최종 판정은 사용자에게 남아 있다.

## 11. 원경 산 LOD [TEST]

### 11.1 구현 계약

2026-09-07 사용자 요청: 원경 산 LOD 작업 진행. ART-SILHOUETTE/ART-INK 및 PROD-PIPELINE에 따라 기존 산세·공통 재질·거리 대기를 유지하면서 화면 크기에 따른 기하량을 줄인다.

- 기존9개 원경의 LOD0 메시·GUID·배치를 보존하고 하위 LOD 메시와 LODGroup을 추가한다. 기존 높이장 정점/법선/곡률색을 표본 추출하여 조명이 겹마다 다시 계산되지 않게 한다. 각 열의 최고점은 저해상도에서도 포함해 주능선 손실을 줄인다.
- 전환 기준은 SO에서 조절하는 투영 높이 비율이며 LODGroup의 기준 크기는 산의 높이다. 넓은 산의 폭 때문에 가장 높은 정밀도로 고정되는 것을 피한다. 최저 LOD는 카메라 far clip 이전에 통째로 사라지지 않는다.
- URP의 LOD 디더 크로스페이드를 Forward/Depth/DepthNormals 모든 패스에 동일 적용한다. 별도 머티리얼 색·거리 안개·런타임 지형 변형은 추가하지 않는다.
- Build/Refresh/Apply와 감사 경로가 LOD 추가 후에도 작동하도록 연결한다. 바위 LOD·보행 지형·전투 코드는 변경하지 않는다.
- 검증:9개 그룹/각3단계·공유재질·삼각형 감소·유한 법선·씬 무결성, 고정컷과 강제LOD/자동LOD 비교, 전환 구간 디더 검사, 기존 거리 대기 GPU 검사. 실제 빌드 프레임시간과 메시 개수 감소는 구분한다.
- 수정 전 백업=`Art/CodexWorldBackups/BeforeMountainLOD`.

### 11.2 적용·검증 결과 (2026-09-07)

- C2 원경9개에 각3단계 LOD를 설치했다. 기존 LOD0의 메시·GUID·Transform·공통 재질은 보존하고, `Art/CodexWorld/MountainLOD`에 하위 메시18개를 생성했다. 원본 높이장 표본 간격은 LOD1=(1,4), LOD2=(2,8)이며 선택한 열의 최고점을 추가 보존한다. 각 단계의 산9개 삼각형 합계는 **594,432 → 155,462 → 41,132**(LOD0 대비 약74%/93% 감소). 실제 동시 렌더량·프레임 개선율과는 구분한다.
- `CodexWorld_Settings`의 전환 기준=.65/.30, 기준 크기=산 높이, 마지막 단계 기준=0/마지막 fade 폭=0. PC의 기존 lodBias=2를 반영하면 유효 화면 높이 약32.5%/15%에서 전환한다. 초기 .40/.17은 골짜기에서9개 모두 LOD0을 유지했으므로 조정했다. 컷3 위치에서 최종 선택 추정은 LOD0 1개/LOD1 8개이며, 이는 전환 중 중복 렌더·프러스텀 컬링을 제외한 거리/높이 계산이다. 전역 품질 설정은 변경하지 않았다.
- 기본 `MountainLodAnimateCrossFading=true`: Unity 시간 기반 CrossFade로 전환 후 디더가 정지 화면에 계속 남지 않게 한다. 폭=.15는 시간 기반 모드를 끈 경우의 화면 크기 전환 구간이다. Forward/DepthOnly/DepthNormals에 동일한 URP 크로스페이드 함수를 적용했다. 마지막 LOD는 LOD 컬링으로 사라지지 않지만 카메라 far clip은 계속 적용된다.
- `CodexMountainLOD.Install/Validate()` PASS: 그룹9/각3LOD/오류0/공유 재질/추가 Collider0. 메뉴=`Oheangbu > Dev > Codex World > Rebuild mountain LODs`. BuildScene/ApplyRealisticMountains/RefreshCanonicalGeometry에서 하위 메시도 재생성한다. Refresh는 저장한 C2를 연 상태에서 실행하도록 제한하여 하위 LOD만 오래된 상태로 남는 것을 막는다.
- `CodexMountainLODAudit.Probe()` **54조건 PASS**: 9개 산 각각 3단계·아주 먼 거리·2개 전환 중간점 GPU 렌더. 960×540/FOV60/post off/lodBias1, 강제 단계와 자동 선택의 전체 화면 RGB 오차0. 일반 크기/전환 실루엣 IoU 최저 .988932. 높이1.5%(약8px)에서는 IoU 최저 .979592이나 양방향 실루엣 차이1px 이내로 확인했다. 전체 화면 RGB 평균오차 최댓값 .005310. 전환 마스크 검사는 결정적인 표본을 위해 일시적으로 화면 크기 모드를 사용하며 시간 애니메이션 지속시간 측정은 아니다. 모든 임시 설정·레이어·강제 LOD는 복원한다. 최초 비동기 셰이더 컴파일 중 빈 프레임이 검출되어 감사 실행 중에만 동기 컴파일을 사용한다. 결과=`Screenshots/CodexWorld/MountainLOD/gpu-validation.json`.
- 거리 대기 GPU 재검사 PASS: 200/500/900m, 대기 OFF 색 차이0, ON 수식 최대오차2.98e-8. LOD별 색 보정이나 별도 옅은 산 재질은 추가하지 않았다.
- 씬 무결성 PASS: 1025objects/769renderers/203colliders, script/재질/셰이더/mesh 누락0. 경로111점 누락0/최대오차.0721m. 전체4,321,046tris는 비활성 LOD까지 합한 저장 기하량이다. 빌드 FPS는 미측정.
- 고정5컷은 `CaptureSettledViews()`로 전환을 마친 자동 LOD 상태를 촬영한다(촬영 동안만 fadeMode=None, 이후 복원). C1/InkWorld/PlayerRig SHA256은 기존 기록과 일치한다. 미술 최종 판정은 사용자에게 남아 있다.

## 12. 길과 지형 표면 통합 [TEST]

### 12.1 구현 계약

2026-09-07 사용자 승인: 지면 위 별도 길 메시를 제거하고 길을 지형 재질에 통합한다. ART-INK/ART-COLOR/ART-SILHOUETTE 및 PROD-PIPELINE을 따른다.

- 기존 Winding_Path는 계산 지형보다 .075m 높고, 지형과 다른 삼각분할·별도 그림자를 사용했다. 지형 전용 재질의 월드 좌표 마스크로 길을 혼합하여 공극·겹침 자체를 제거한다.
- 기존 경로/폭은 유지하고 가장자리는 흙과 부드럽고 불규칙하게 섞는다. 동일한 지형 위치·법선·광원·그림자·깊이를 사용하며 새 발광·색 팔레트를 도입하지 않는다.
- SO가 마스크 해상도·경계 혼합 폭·가장자리 불규칙도·길 명암 수치를 소유한다. 기존 공용 재질을 수정하지 않고 지형 전용 파생 재질을 만든다. 다른 모델의 셰이더 동작은 기본값에서 동일하다.
- 실제 지형/Collider 메시·Transform·산 LOD·바위 배치·게임플레이는 보존한다. BuildScene에도 통합 경로를 연결하여 리본이 재등장하지 않게 한다.
- 검증: 별도 길 Renderer0, 마스크/지형 재질 참조 정상, 전후 지형 기하·Collider 동일, 경로111점·셰이더 오류·고정컷 확인. 백업=Art/CodexWorldBackups/BeforeGroundPath.

### 12.2 적용·검증 결과 (2026-09-07)

- 별도 Winding_Path 1개/4,404tris를 제거하고 Valley에 전용 Ground_With_Path 재질을 적용했다. 길은 동일 지형 표면의 안료 혼합이며 별도 위치·깊이·그림자·Collider가 없다. BuildScene의 Trail 생성 경로와 .075m 오프셋 생성 코드도 제거했다. 구 Trail/Path 자산 파일은 이전 기록용으로 보존하되 현재 씬 참조는 없다.
- GroundPathMask.asset은 1024×2048 선형 R8, mipmap/trilinear/anisotropy4이다. 기존 PathX/폭 변화를 따라 베이크하고 경계 혼합 폭 .8m·불규칙도 .22m를 적용했다. 중간 혼합 값의 텍셀19,988개. 지형 전용 재질만 _GroundPath=1이며 기존 Granite 등 재질은 기본값0으로 종전 셰이딩을 사용한다.
- CodexGroundPath.Apply/Validate PASS: ribbons0, groundCollider가 같은 Ground mesh 참조, 마스크 중심/바깥219쌍 오류0, 지형45,056tris. 재적용 메뉴=Oheangbu > Dev > Codex World > Blend path into ground. 경로 위치·폭·마스크 해상도·혼합 폭·안료 톤은 CodexWorld_Settings에서 조절한 뒤 재적용한다.
- 수정 전후 Ground.asset SHA256 동일(8624e1e9cca5e63a6489f081ea12eafff5cf0f1d0b580f4f265882e691bd535f). 씬 직렬화 비교: 기존 길의 GameObject/Transform/MeshRenderer/MeshFilter만 제거, 새 오브젝트0. 남은 변경은 부모 자식 목록과 지형 Renderer의 재질 참조뿐이다. C1/InkWorld/PlayerRig 해시도 이전 기록과 일치한다.
- 씬 무결성·경로111점 PASS: 1024objects/768renderers/203colliders, 누락 script/material/shader/mesh0, 경로 누락0·최대오차.0721m. 전체 저장 기하4,316,642tris는 모든LOD 합계이며 실제 프레임 성능 수치가 아니다. 물리 기하 변경이 없어 이번에는 기존 Play 이동 검사를 반복하지 않았다.
- 고정5컷 재촬영. 컷2/3/4/5에서 길과 지형의 틈·리본 자체의 가장자리 그림자가 사라지고 경계가 주변 흙과 섞이는 것을 확인했다. 산색·LOD·바위·건축 배치는 유지한다. 시각 최종 판정은 사용자에게 남아 있다.

## 13. 초가 주막 교체·등불 강화 [TEST]

### 13.1 구현 계약

2026-09-07 사용자 요청: 기존 주막 삭제 후 적당한 초가집 형태 에셋으로 대체, 등불 라이팅 강화, 에셋 색채 억제. ART-COLOR/ART-INK/ART-SILHOUETTE 및 PROD-AIASSET(#171)을 따른다.

- 수령 원장에 완성 초가집이 없어, 프로젝트 밖 제공 패키지 Desktop/오행부 프로젝트/Assets/House_1.unitypackage와 House_2.unitypackage를 검사한다. 실제 모델·텍스처·크기·축·지붕·출입 방향을 확인하여 적합한 것을 선택한다. 패키지의 불필요한 씬 설정은 정본에 가져오지 않는다.
- 선택 에셋의 기하와 텍스처/노멀 정보를 보존한 파생 재질로 채도를 낮춘다. 기존 주막은 정본에서 제거하고 지형 접지·출입 방향·길 접근·Collider를 확인한다. 원본 패키지·수령 재질은 별도 보존한다.
- 등불은 주막 진입부에 두고 실제 주변 조명과 발광 면을 함께 확인한다. 안식의 난색은 등불에 집중하며 전역 노출/Bloom으로 밝히지 않는다. 건물 자체 Emission은0이다. 배치·톤·등불 수치는 SO에 둔다.
- 검증: 기존 주막0·새 모델/재질 정상·지형/길/산 보존·경로/충돌·고정컷 및 근접컷·Unity 오류, 패키지 출처·선택 이유·기하 수치를 기록한다. 백업=Art/CodexWorldBackups/BeforeThatchedInn.

### 13.2 적용 결과 (2026-09-07)

- 제공받은 House_1/House_2 패키지의 아트 파일71개를 원래 GUID로 수령했다. 패키지 씬2개는 제외, 기존/상호 GUID 충돌0. 두 모델의 4방향 렌더와 모든 하위 MeshRenderer·재질을 확인했다. House_1=ㄱ자 초가/열린 마루/294renderers/79,126tris, House_2=일자 연속 방/338renderers/86,400tris. 작은 발자국과 열린 마루가 주막 진입 장면에 맞는 House_1을 선택했다. 원본 URP Lit 재질은 정상이며 복구가 필요한 셰이더는 없었다. 수령/시각 분류 원장에2항목을 추가해 reviewed 원장1,593개가 되었다. 기존9팩 전수 감사 수치는 역사적 기준으로 보존한다.
- C2의 JoseonInn을 제거하고 Thatched_Inn으로 교체했다. 원본 UV·노멀·텍스처와79,126tris를 보존하여 재질별11개 MeshRenderer로 통합했다. 원본 FBX·재질은 수정하지 않았다. 정본 재생성 코드도 초가 적용을 호출한다. 재적용 메뉴=Oheangbu > Dev > Codex World > Replace inn with thatched asset. 파생 프리팹=Art/CodexWorld/ThatchedInn/ThatchedInn.prefab.
- 파생 DesaturatedAssetLit는 설치된 URP17 Lit의 Forward/GBuffer/Depth/Shadow 동작을 기반으로 한다. 선형 RGB 명도 혼합으로 원색 성분을6% 유지하고 albedo 값=.85, smoothness=.12, metallic0, 건물 Emission0. 노멀·기와가 아닌 볏짚 결·회벽·목재의 세부 명암을 유지한다. 검은 전역 ambient에서 재질이 모두 검게 뭉개지는 문제는 이 재질의 bakedGI에 팔레트 소지색 기반 확산광 .32를 보충하여 해결했다. 이는 발광이 아니며 전역 노출/Bloom/환경광을 변경하지 않는다. 복사한 URP 셰이더의 Unity 라이선스를 UnityShaderLicense.txt로 동봉했다.
- 주막 등불2개는 열린 마루에 배치했다. 각각 Point Light intensity6/range7m/soft shadows, 등불 면 intensity1.2. 실제 광원 색은 기존 팔레트 Wood/Paper에서 파생한다. 등불 종이 면의 그림자 투사를 꺼서 자신의 Point Light를 가리지 않게 했다. 나무 프레임과 건축물은 그림자를 유지한다.
- 배치=(−15, Height−.12,38), yaw192°. 원본 .8 균일 배율에서는 마루 위 처마 보가 2m 플레이어보다 낮아, 발자국 배율XZ=.8을 유지하고 Y=1.2로 늘렸다. 지형 기하는 수정하지 않는다. 마루 진입에 세계 높이 약.2m 간격의 낮은 돌계단5개를 추가했다. 플레이어의 높이·반경·stepOffset·충돌 규칙은 보존한다. 원본 메시 전체에 무차별 볼록 충돌을 씌우지 않고 지붕 재질3배치를 제외한8개 정적 MeshCollider와 계단 BoxCollider를 사용한다.
- 출처/검수=Docs/Assets/HouseAssetIntake.json, HousePackageManifest.json, Screenshots/CodexWorld/HouseCandidates. Meshy/Blender 생성·외부 크레딧 사용은 없었다. 파생 건물에는 아직 별도 LOD를 추가하지 않았으며 원경 산/바위의 기존 LOD는 유지한다.

### 13.3 검증 결과 (2026-09-07)

- CodexThatchedInn.Validate PASS: 구 주막0, 새32renderers(모델11+등불16+계단5), 채도 억제 재질11종, 잘못된 셰이더/재질0, 총79,378tris, Collider13, Point Light2. 최종 Bounds 크기 약12.00×6.12×11.72m. 이는 단일 주막의 저장 기하이며 실제 프레임 비용을 뜻하지 않는다.
- 최종 고정5컷 촬영 후 등불을 실제로 ON/OFF하여 비교했다. 발광 면은 양쪽에서 그대로 둔 채 Point Light만 바꿨다. 컷4의 변화 픽셀74,996개, 전체 평균 RGB 차이 .002618/채널 합 최대차494. 벽·목재·마루의 실제 난색 조명 기여를 확인했다. 결과=Screenshots/CodexWorld/InnLighting/{on,off}.png 및 validation.txt. 캡처 중에만 셰이더를 동기 컴파일하고 산 CrossFade를 정지시킨 뒤 원래 설정으로 복원한다.
- Play 모드에서 실제 Player의 CharacterController 설정(키2m/반경.5m/step.3m/slope45/skin.08)을 복사한 임시 프로브로 본선 z0→218→29→주막 계단→마루를 물리 이동했다. 424/424경유점 PASS, 실제 Player 위치 변경0, 임시 프로브 제거. 경로 중2회의 측면 접촉은 있었으나 통행을 막지 않았다. 플레이어 입력·전투·성능 시험은 아니다. 결과=Screenshots/CodexWorld/traversal-validation.txt.
- 첫 접근은 ㄱ자 건물의 막힌 측면을 향했으며, 열린 마루 경로로 바꾼 뒤에도 .8 균일 배율의 낮은 처마 보가 통행을 막았다. 실제 건물 높이와 계단을 수정하여 해결했다. 검사를 통과하려고 플레이어 크기나 Collider를 끄지 않았다. 마지막 마감에서 계단 윗면 높이는 유지하면서 아랫면을 지면 아래까지 연장하고 기단의 저채도 돌 재질로 교체했다.
- 저장된 C2 무결성 PASS: 1015objects/761renderers/188colliders, 누락 script/material/shader/mesh0. 지형 경로111점 누락0·최대오차.0721m. 전체 저장 기하4,376,864tris는 모든 LOD를 합친 값이다. 마감 검사에서 Unity Error/Exception0. C1/InkWorld/PlayerRig 및 Ground.asset SHA256은 수정 전 기록과 모두 일치한다. 최종 계단 마감 후 Play 이동 재검사도 동일한424/424 PASS. 사용자 미술 최종 판정은 별도다.

### 13.4 기단과 계단 질감 일치 [TEST] (2026-09-07)

사용자가 원본 h1_house_ground의 돌계단과 새 Inn_Entry_Step의 늘어진 무늬를 비교하며 기단 가장자리 질감 사용 또는 Meshy 생성을 요청했다. 기존 아틀라스 전체를 Cube UV에 적용한 부분을 수정한다. 원본의 돌 표면 UV·노멀 정보를 먼저 조사하여 재사용하고, 같은 기단 재질과 마모된 돌 형태를 맞춘다. 원본 텍스처/모델과 검증된 계단 윗면 높이·통행 Collider는 보존한다. 씬·프리팹·재생성 코드를 함께 갱신하고 근접 렌더에서 확인한다. 백업=Art/CodexWorldBackups/BeforeInnStepUV.


- 적용: Inn_Entry_Step 5개의 재질을 h1_house_ground_Muted로 통일했다. 원본 아틀라스 전체를 Cube에 늘리던 UV를 폐기하고, 원본 계단 두 단의 실제 UV(기단 통합 메시의 삼각형645/683~698 주변)를 조사하여 윗면/앞면 사각 패치를 따로 사용한다. 단마다 두 패치와 좌우 반전을 조합한다. 수직 면은 돌 앞면 영역 안에서 약.2m 단위로 반복하며 목재·마루 영역으로 넘어가지 않는다. 색·노멀 맵·저채도 셰이더는 기단과 동일한 재질이다.
- 모서리에는 local .025m 라운드를 주고 분석적 법선과 재계산한 tangent를 적용했다. 기존5개 BoxCollider·Transform·높이·발자국을 그대로 유지하며, 표면 둥글림은 박스 안쪽 수 cm 범위다. 새 렌더 메시5개 총960tris(종전 Cube60tris), 주막 전체80,278tris. 수치/UV패치는 CodexWorld_Settings가 소유하고, CodexInnSteps.Configure를 주막 재생성에도 연결했다. 원본 에셋 및 텍스처는 수정하지 않았고 Meshy 생성/크레딧 사용0이다.
- 씬과 재사용 프리팹을 모두 저장했다. 전후 직렬화 비교에서 Transform/Collider 변경0, 계단5개의 MeshFilter/MeshRenderer 참조 갱신을 확인했다. 추가 변경은 Unity URP가 두 등불에 자동 부착한 기본 UniversalAdditionalLightData와 해당 컴포넌트 목록이며 Light 강도·범위·색은 동일하다. 이전424경유점 통행 검사는 물리 형상이 같으므로 반복하지 않았다.
- 최종 검증 PASS: 1015objects/761renderers/188colliders, 누락 script/material/shader/mesh0, 지형111점 누락0·최대오차.0721m. 전체 저장 기하4,377,764tris는 모든LOD 포함이며 빌드 성능 측정이 아니다. Unity Error/Exception0. C1/InkWorld/PlayerRig/Ground 해시는 이전 기록과 일치. 고정5컷과 원본 계단이 함께 보이는 근접컷(Screenshots/CodexWorld/InnSteps/close.png)으로 목재 줄무늬 제거·기단 돌 질감 일치·반복 완화를 확인했다. 등불 실제 ON/OFF 비교도 PASS(변화74,699px). 미술 최종 판정은 별도다.
