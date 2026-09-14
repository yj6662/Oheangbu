# 외부 플레이테스트 — 산길·폐광·청림·황경 외형 제작 및 전달 계획

Lifecycle: ACTIVE · State: TEST · 작성 2026-09-13 · 목표 전달일 2026-09-15(화, KST)

**2026-09-14 사용자 후속 결정 — 빌드 시점:** 기능·미술 작업마다 중간 실행물이나 ZIP을 다시 만들지 않는다. 작업 중에는 Editor 컴파일·필요한 기능 검사·정지 이미지로 검토하고, 합의한 작업 범위를 마친 뒤 최종 플레이테스트 빌드를 한 번 구성한다. 기존 후보와 검증 기록은 보존한다. 최종 실행물의 실행·저장·성능 검증은 그 빌드에서 수행하며, 수정이 필요하면 원인과 재빌드 필요성을 먼저 명시한다.

이 문서는 사용자가 요청한 **Ultra 계획 → 낮은 단계부터 High까지 작업별 실행 → 미술을 갖춘 뒤 테스트 빌드**의 실행 계약이다. 계획 자체는 제작 완료 기록이나 Bible 상태 전이가 아니다. 이번 조사에서는 Editor 실행·씬 변경·이미지 생성·바이너리 빌드를 하지 않았다. 실제 적용 결과는 별도 실행 보고서에 남긴다.

## 1. 이번 전달물의 모습

플레이어가 폐광 내부에서 시작해 산길로 나오고, 금표 주막과 청림의 숲을 지나 상경 가도에서 도성 스카이라인을 발견하고, 황경 성저·남문·주요 도시 외부 공간까지 직접 걸어볼 수 있는 **미술을 갖춘 연속 공간**을 만든다. 먼저 산길·폐광, 다음 청림, 다음 황경 접근부와 외관을 작업한다. 플레이어 모델과 다섯 소환수 외형의 실제 플레이 연결은 외부 전달 전 필수다. 미술이 빠진 상태의 빌드를 먼저 전달하는 순서는 쓰지 않는다.

황경은 성저의 생활권, 남문, 접근 가능한 대표 거리와 궁궐 외곽 마당, 이들을 묶는 성벽·기와지붕 스카이라인으로 범위를 고정한다. 도시 전역의 집 내부·상점·퀘스트·NPC 일정·궁성 피날레·신규 보스 AI까지 이번 날짜에 완성하는 계획으로 확대하지 않는다. 보행 가능한 미술 검토 구간과 본편 진행 완료를 각각 기록한다.

문서 라우팅은 `BIBLE_INDEX → LDB-REALMS / LDB-ATTRACTION / LDB-HWANGGYEONG / LDB-EA, NARR-STORYLINE / NARR-FINALE, ART-COLOR / ART-INK, PROD-AIASSET → SPEC-WORLD-MACRO / SPEC-WORLD-MACRO-CONTENT / SPEC-WORLD-MACRO-PLAYTEST → 실제 코드·자산`을 따른다. 사용자 최신 요청이 이전 첫 구간 계획의 “플레이어 캡슐 유지”보다 우선한다. 게임의 작도·경제·진행 규칙은 바꾸지 않는다.

## 2. 출발점과 완료를 혼동하면 안 되는 부분

| 항목 | 확인한 현재 상태 | 이번 작업의 기준 |
|---|---|---|
| 활성 제작 씬 | `Assets/_Project/Scenes/World/W_WorldMacro_Playtest.unity` | 현재 씬에 단계별 국소 적용. 최초 Build나 전체 BuildAll 재실행 금지 |
| 첫 실제 플레이 | 폐광 조사, 실제 적 3개, 선택 소지품, 벌목꾼·약초꾼, 금표 주막 휴식·저장 | 위치·진행 ID·보상·일회 상태 보존. 외형 수정 뒤 해당 검사 재실행 |
| 콘텐츠 위치 | 콘텐츠 시트 총 83개. 첫 구간 외는 대체로 위치/검토 표시 | 83개가 전투·퀘스트 83개라는 식으로 표현하지 않음 |
| 폐광·주막 | 최신 `Playtest/AssetReuse`에서 보유 모델 부재 사용, 실제 표면과 구조용 보조 면 공존 | 이 결과에서 이어 작업. 이전 상자형 주막/반원 갱도로 되돌리지 않음 |
| 기존 검증 | AssetReuse 보고서에 통로 표본 16,426개 실패 0, 저장 서비스 15개, 원본 해시 3,985개 유지 | 해당 시점·범위 기록. 변경 후 검사 및 사용자 직접 조작을 대신하지 않음 |
| 지형·하늘·수면 | V4의 짙은 먹 지면과 저채도 지역색, RegionalInkSky, 보류 중 수면 | 현재 파일·설정 보존. 전역 노출·블룸, 수계·산계·지형·경로 재생성 안 함 |
| 도시 | 기존 Palace, 남문 등 대체형, 도성 예약 연결 | 주요 외부 공간과 실루엣을 보유 건축 부재로 구체화 |
| 플레이어 | C02 A2 몸/B2 동작/C3 눈꺼풀의 독립 실험본 존재 | 플레이테스트 전용 파생 시각 자식으로 연결하고 실제 게임 거리에서 검수 |
| 소환수 | 사슴·해태·석장승·호랑이·거북의 정적 모델과 등장/소멸 표시 경로 존재 | 실작도 경로·수명·지면·전투 표시 연결을 확인. 정적 모델을 리깅/보행 완료로 표현하지 않음 |
| 120fps | 목표 8.33ms. 기존 측정은 구버전·제한된 카메라의 Editor 결과 | 최종 자산/카메라/플레이 조건에서 CPU·GPU·중앙/P95·메모리 실측 |

현재 실제 시작 발 위치는 `Playtest.asset`의 `(3406.6194, 195.77368, 1129.2522)`, yaw 210이다. 지도 `Mine` 예약 위치와 다르다. 첫 구간은 `Playtest.asset.MainPath`, `BranchPath`, 실제 Cave Entry 및 상호작용 위치를 사용한다. `WorldMacroSeed`의 예약 폐광 좌표로 캐릭터나 미술을 다시 옮기지 않는다.

## 3. 제작할 공간과 기존 경로 연결

| 구간 | 읽어야 하는 실제 자료 | 목표 외형·동선 | 완료 경계 |
|---|---|---|---|
| A. 폐광 내부·산길·금표 주막 | Playtest MainPath/BranchPath, `WorldMacro_Landmarks_Authored/Cave`, `Playtest_OwnedAssets`, 조사/조우/주막 5개 표적 | 동굴의 비대칭 암반, 입구 빛과 바깥 산, 암면→산길 접속, 길 옆 암석/하층 식생, 소지품 지선, 주막의 초가·마루·온기 | 기존 실제 플레이 전 구간. 조사/교전 시 시야와 출입구·충돌 폭 유지 |
| B. 청림 외림·벌목 흔적·심부 방향 | `Trail_Inn_Logging`, `Trail_Logging_Deep`; `geumpyo_stone`, `logging_dungeon`, `metal_lesson`, `root_dungeon`, `tree_warning` 등 | 밝은 외림→수관 아래 어둑한 숲, 벌목 공터·목재 군집·버려진 생활 흔적, 심부의 과성장과 산세 | 주요 탐험 동선과 눈에 들어오는 외형. 창고/뿌리굴의 전투·방 전체는 기존 구현 여부 별도 |
| C. 역참·객주·상경 가도 | `Road_Inn_Post` → `Road_Post_Merchant` → `Road_Merchant_Pass` → `Road_Pass_SouthPost` → `Road_SouthPost_Gate` | 숲 가장자리 생활 흔적, 역참·객주 앞마당, 산길 고개, 점점 커지는 지붕/성벽 원경, 성저 진입 | 실제 지리 연결과 시선 확보. 화물 계약/호송/검문/보스 기능 추가로 간주하지 않음 |
| D. 황경 성저·남문·도시 대표 외부 | `SouthPost`, `SouthGate`, `Road_Gate_CapitalReservation`, `Hwanggyeong`, 실제 `Palace` | 성저 집 지붕과 생활권, 실제 성문·성벽 부재, 남문 광장, 대표 거리, 궁궐 방향의 마당·지붕 실루엣 | 명시된 외부 보행 구역만. 궁성 내부·피날레·도시 전역 상세화는 별도 |

조사 시 지리 시트의 주요 표식은 Inn `(1982,133.164,700)`, EastPass `(1052,329.912,192)`, SouthPost `(196,249.847,-1312)`, SouthGate `(-270,99.664,-1260)`, Hwanggyeong `(-300,72.839,-630)`이다. 실제 Palace는 `(-300,76.868,-788.138)`, yaw 180, 마당 76×88m다. 이 수치는 재배치 목표가 아니라 실제 씬 조사 시 대조할 식별 정보다. 주막 체크포인트와 콘텐츠 위치는 거점 중심과 구분한다.

`Return_Cheongrim_Inn → Return_Inn_EastFoothillPass → Return_EastFoothillPass_Capital`은 산기슭 귀환의 물리 연결이다. 상경 가도·성저·남문을 읽히게 하는 주 검토 경로를 이 지름길로 대체하지 않는다. 통행 가능 여부·보스 승리·국 획득을 경로 존재로 자동 충족시키지 않는다.

LDB-EA의 본편 흐름은 청룡전·국 재탐험 뒤 상경·성저 인도·남문전이며, 문이 열리는 순간 EA가 끝난다. 현재 이 단계들은 대부분 예약이다. 도시 내부 외부공간을 이번 미술 검토에서 직접 걸을 수 있도록 만드는 것은 **테스트 구역 공개**이며 본편 진행 해금 구현이 아니다. 후반 황경의 참상·몰락한 황룡·피날레 시나리오를 현재 평시 접근 외형에 임의로 섞지 않는다.

## 4. 보유 자산을 사용하는 구체적 원장

후보 검색 정본은 `Docs/Assets/ModelCatalog.reviewed.json`이다. `reviewStatus`, `notes`, 실제 썸네일을 함께 읽는다. `VISUALLY_REVIEWED`는 형상 분류 이력이며 최종 게임 미술 승인이 아니다. 아래 공급자 경로는 모두 Unity 프로젝트 상대 경로다.

| 역할 | 확인한 가족·파일 | 적용 방식과 주의 |
|---|---|---|
| 폐광 암면·바닥 | `BillemotdonggulLavaTubePack`, 기존 `Landmarks/Models/LM_Wall02A_LOD1.fbx`, `LM_Floor02B_LOD2.fbx`; Wall05B/Ceiling05A 파생본 | AssetReuse의 원래 UV·가벼운 암면을 재사용. 반복 간격·크기·회전·입구 실루엣을 실제 표면에서 정리. 바닥 충돌은 보존하고 보이는 판 가장자리·틈을 수정 |
| 초가 주막·성저 생활 건축 | `Assets/House_2/house2 .fbx`의 실제 초가지붕·회벽/목재 패널·기둥, Seyeonjeong 마루 | `.fbx` 이름의 공백까지 정확히 사용. 현재 부재 선택 방식을 재사용하며 지붕만 얹은 상자를 최종 건물로 만들지 않음 |
| 가구·생활/광산 소품 | `KTinteractiveProp`, `KoreanTraditionalFestival`의 소반·수납장·등기구·짚주머니·바구니·곡물자루; `HwaseongHaenggung/Prefabs/SM_M_WoodLog.prefab`, `SM_M_WoodenBox.prefab`; `Korea_TreasureProps/Prefabs/SM_052_Pot.prefab` | 실제 사용 장소와 기능이 있는 소규모 군집. 중복 콜라이더·상호작용 표적 가림·출입 폭 침범 검사. 시대/용도를 확인하지 않은 유물은 작업 장비로 쓰지 않음 |
| 청림 수관·하층 식생 | `SeyeonjeongPavilion/Prefabs/SM_Henonis_1/2`, `SM_UlmusDavidiana_Summer_2`, `SM_PinusDensiflora_Spring_2`, `SM_Deparia_1`, `SM_Grass` | 기존 Dressing의 소유 메시/텍스처·LOD·인스턴싱을 우선 사용. 대나무류·교목·양치류를 공간 층으로 구성. 파일명만으로 수종의 역사적 정확성 확정 안 함 |
| 산길 암석 | 같은 가족의 `SM_Rock_K.prefab`, `SM_Rock_L.prefab` | 높은 쪼개진 바위와 낮고 긴 바위를 구분. 표면에 반쯤 박고 지지점을 검사. 산 전체를 새 바위 프리팹으로 채우지 않음 |
| 도성 문루·성벽 | `HwaseongForteressGate/Prefabs/SM_B_GateHouse_Column`, `_Crossbeam`, `_Rafter`, `_Roof_001/002`, `SM_BarbicanWall_001...`, `SM_FortificationWall` | 검토 원장은 이들을 부재로 분류함. 문루 전체 프리팹으로 오인하지 않음. 기둥→보→서까래→기와의 실제 접합, 문 사이 통로와 단순 충돌체를 조립 |
| 궁궐·관아 방향 | `HwaseongHaenggung/Prefabs/SM_Bongsudang.prefab`, `SM_Byeolchu.prefab`; 이미 제작된 `Landmarks/Models/LM_*_LOD*.fbx` | 공급자 고폴리 원형 다량 배치 금지. 기존 Palace의 지붕·회랑 실루엣을 재사용. 원경에는 실제 낮은 LOD를 사용하고 가까워지는 구역은 LOD 전환 확인 |

Bongsudang 원본은 기존 제작 보고서의 파일 측정 기준 1,857,720tris, 파생 LOD0 143,178 / LOD1 35,055 / LOD2 35,055이다. LOD2는 안정된 LOD1 사본으로 12k가 아니다. Byeolchu는 44,494 / 14,364 / 14,364이다. GateHouse Roof001은 원장상 88,006tris로, 가벼운 다른 부재와 동일 비용으로 취급하지 않는다. 새 원경 파생본이 필요하면 기존 형상·UV를 보존한 로컬 파생 작업으로 만들고 실제 내보낸 tris·재질 수를 기록한다.

추가로 확인한 경량 후보는 `HwaseongForteressGate/Prefabs/SM_CW.prefab`(120tris, 약7.815×5.597×2.178m), `SM_FortificationArch.prefab`(600tris, 약7.815×7.815×1.601m), `HwaseongHaenggung/Prefabs/SM_Naeposa.prefab`(9,113tris)다. 원장은 부재별 실제 렌더 크기이며 완성된 성문 배치 규격이 아니다. 공급자 prefab 총계와 Blender 내보낸 파생 메시 tris는 측정 범위가 다르므로 한 수치로 합치지 않는다.

큐브·캡슐·단순 면은 보이지 않는 충돌체, 기존 지형 접속 기단, 열린 스캔의 구조용 뒷면처럼 목적이 명확한 보조 용도로만 쓴다. 그런 형상이 건물·석탑·나무·갱도·도성의 대표 외형을 대신하면 미술 단계 완료가 아니다. 신규 유료 생성 요청·에셋 구매·재시도는 이번 계획에서 실행하지 않는다.

## 5. 코드·데이터·미술의 접합 계약

새 기능은 작은 데이터/도구로 분리한다. 다음 이름은 이번 구현용 제안이며 이미 구현된 API라는 뜻이 아니다.

| 소유자 | 파일/자료 | 책임 |
|---|---|---|
| 데이터 담당 | 새 `Scripts/Data/World/WorldMacroVisualCorridorSO.cs`; `Art/World/WorldMacro/Playtest/VisualCorridor/VisualCorridor.asset` | 단계 ID, 실제 anchor/content/route ID, source prefab GUID+mesh subasset ID, 파생 경로, position/rotation/scale, preserved bounds, support surface 역할, 캡처 카메라/방향/FOV, 단계 적용 버전 |
| Editor 담당 | 새 `WorldMacroVisualCorridorAuthoring`와 단계별 partial/helper | Survey → Stage 적용 → 검증 → 저장. 현재 Playtest 씬/Edit 모드/기준 해시 검증. 해당 소유 루트와 ID만 갱신. 재실행 중복 금지 |
| 환경 미술 담당 | `Playtest_VisualCorridor` 아래 `MinePath`, `Cheongrim`, `CapitalApproach`, `CapitalExterior`; 소유 메시/재질/프리팹 | 원형 보존, 접합, LOD·단순 충돌체, 셀 단위 정적 배치, 역할 있는 소품 군집 |
| 식생 담당 | Playtest 전용 Dressing 사본과 기존 `WorldMacroDressingRenderer.Sheet` | 기존 48개 원형·셀·생성 시드 재사용. 국소 PreservedAreas / StoryClusters / FixedPlacements만 보완. 다른 강토/원본 씬의 공유 Dressing 변경 방지 |
| 캐릭터 담당 | Playtest 전용 시각 prefab/controller/profile 및 작은 visual adapter | 기존 PlayerMotor·카메라·작도·저장 보존. 실제 상태를 받아 모델 표시/동작만 갱신 |
| 검수 담당 | VisualCorridor의 manifest / validation / images / performance / REPORT / REVIEW | 원본 및 기능 diff, 현재 revision의 증거, 통과/실패/미검증 분리 |

기존 `WorldMacroPlaytestAuthoring.Execute`의 파일 명령 큐에 `VisualCorridor` 분기만 연결한다. CLI는 기존 `Tools/Unity/world_macro_playtest.py`를 그대로 운반 수단으로 쓴다. `Survey`, `Apply:<stage>`, `Validate:<stage>`, `Capture:<shot>`처럼 현재 단계만 처리하는 명령을 만든다. 새 전체 월드 생성 경로는 만들지 않는다.

현재 `WorldMacroPlaytestAssetReuse`의 통합 메시들은 수정 후 원형 조각의 transform만 바꿔도 보이는 통합본이 자동 갱신되지 않는다. 수정할 공간 그룹만 원장→파생 메시→재질별 통합을 다시 만들고 범위별 삼각형/Renderer 수를 기록한다. `117→17 renderer` 같은 과거 로컬 감소를 전체 드로콜·FPS로 표현하지 않는다.

현재 경로의 `Surface()`는 Terrain/Bridge/Cave_Walkable/Cave_Entrance_Stone_Ramp/Playtest_Inn_Floor 이름만 지지면으로 허용한다. 새 도시 마당·계단·접근부는 새 도구의 명시적 `WalkableSupport` 역할/허용 집합으로 검사한다. 임의의 지붕·소품 윗면을 지면으로 받아들이는 전역 raycast 완화로 해결하지 않는다. 데이터의 실제 표면을 검사하되 자동 CharacterController.Move 완주는 하지 않는다.

`WorldMacroDressingAuthoring.InstallRegion`은 공유 시트 경로와 fingerprint 전제에 연결되어 있으므로 현재 상태에서 그대로 지역 재설치를 호출하지 않는다. Playtest 사본에 필요한 국소 변경만 적용하거나 별도 시트 인자를 받는 작은 scoped 경로를 만든다. Terrain/river geometry의 fingerprint 불일치를 무시하고 진행하지 않는다.

검토 카메라 눈높이는 1.62m지만 현재 전투 Walker와 CameraPivot은 1.55m다. 비교 촬영은 실제 게임 카메라의 저장된 값을 고정하며, 미술 작업 중 카메라 높이·노출·FOV를 바꿔 전후 차이를 숨기지 않는다. 캡슐은 1.75m×지름0.56m, 발 기준·scale1을 유지한다.

## 6. 단계별 실행과 수락 기준

### P0 — 기준 확보와 작업 공간 분리

현재 씬, Playtest/ContentPositions/Dressing 참조, 지형·하늘·수면·원본 공급자·C02·소환수 프로필의 해시를 저장한다. 기존 검사 결과와 최종 AssetReuse 정지 이미지를 기준으로 삼고 실제 플레이 카메라의 A/B/C/D 대표 구도를 먼저 저장한다. 작업용 원장은 원형 출처와 현재 씬 오브젝트를 연결한다. 기존 83개 콘텐츠 ID, 3개 실제 조우, 5개 첫 구간 상호작용, 저장 슬롯 `world-macro-playtest-v2`를 함께 추출한다.

**수락:** 재실행 가능한 국소 단계·소유 루트·되돌릴 씬 사본·원본 해시 목록·촬영 좌표가 존재한다. 원본 지형·루트 재생성 없이 첫 수정에 들어갈 수 있다.

### P1 — 산길·폐광·주막을 첫 완성 구간으로

기존 동굴 입구의 같은 암면 반복, 경사로 측면/이음, 열린 스캔 뒷면, 조사 바구니/작업등 접지, 주막 처마/마루 이음을 눈높이 구도에서 해결한다. 비대칭 암반 군집과 기존 산자락을 연결하고 길의 전경·중경·원경을 만든다. 산길 양옆 식생은 통로를 같은 간격으로 둘러싸는 울타리처럼 만들지 않는다. 조사점·적 공격 예고·갈림길·주막 문이 나뭇잎과 기둥 뒤로 사라지지 않게 한다.

**수락:** 폐광 시작/입구/산길 갈림/주막 외부/주막 내부의 1080p 정지 이미지에서 대표 큐브 대체물·열린 틈·명백한 부유·지형 관통·검은/분홍 재질이 없다. 기존 MainPath/BranchPath와 5개 상호작용 접근 검사, 3개 국소 NavMesh 검사 통과. 기능 diff는 설명 가능한 미술/충돌 접합 범위이고 보상/반경/저장 계약을 바꾸지 않는다.

### P2 — 청림 숲의 층과 생활 흔적

외림, 벌목 공터, 심부 방향의 세 분위기를 기존 지리 위에 이어 붙인다. 수관의 큰 덩어리→관목/대나무→양치/풀→바위·낙목 순서로 화면을 구성한다. 외림은 길과 하늘이 보이고, 심부는 수관이 높이 겹쳐 어둑해지되 이동·전투 시야는 남긴다. 광맥 등 기존 허용 인력 외 오염·덩굴을 지속 발광시키지 않는다. 벌목장은 실제 목재 적재와 공터·도구/소지품으로 읽히게 하고 단순 모듈 던전의 바깥 노출을 정리한다.

역참의 정담과 객주 분소의 왕소 위치는 각각 `PostStation`과 `MerchantBranch`에 유지한다. 장식 때문에 이들을 금표 주막으로 합치거나 금표 비석·국 예고를 새로운 필수 통과 게이트로 바꾸지 않는다.

**수락:** 외림/벌목장/심부 방향/역참·객주 가도 네 구도에서 지역 변화와 이동 여백이 읽힌다. 기존 수목 LOD·billboard가 갑자기 벽처럼 뜨거나 원경 숲이 동일 크기 카드 줄로 보이는 문제를 검수한다. 출입·도로·조우 제외영역 침범과 새 고정 배치 지지 누락 0. 원본 Dressing/다른 강토/수면 해시 보존.

### P3 — 황경 접근부, 보이는 도성, 직접 걷는 주요 외부

가도 고개·성저 진입·남문 전방에서 같은 도성의 크기와 지붕 층위가 연속적으로 보이게 만든다. 먼저 현재 SouthGate/GateStructure 대체형과 `03_SettlementAndLandmark_Massing`의 보이는 상자형 실루엣을 목록화한다. 기단·기둥·보·서까래·지붕·성벽 모듈을 현 지형에 맞춰 조립하고, 실제 Palace와 물리적으로 무관한 공중 궁궐 복제품을 원경에 띄우지 않는다.

도성의 먼 지붕은 낮은 비용의 실제 건축 파생 메시 군집으로 구성한다. 접근 가능한 성저 마당·남문 통로·대표 거리·궁궐 외곽 마당에는 보행 지지와 단순 충돌체를 둔다. 가까운 집은 외벽·처마·기단이 읽혀야 한다. 아직 만들지 않은 실내 문을 인터랙션으로 열 수 있다고 표시하지 않는다. 부차 골목과 궁성 내부의 미완성 경계는 보이는 건축의 자연스러운 닫힌 문·담장으로 정리하고 실제 테스트 범위를 안내문에 명시한다.

**수락:** 가도 원경/성저 중경/남문 눈높이/대표 거리/Palace 외곽의 다섯 구도에서 도성이 같은 목적지로 읽힌다. 주요 실루엣에 최종 외형을 대신하는 큐브가 남지 않는다. 명시한 외부 경로의 캡슐 폭·머리 공간·지면·계단 접합 검사 실패 0. 남문전·화물 인도·후반 NPC·피날레는 실제 연결하지 않았다면 미구현으로 남는다.

### P4 — 플레이어와 소환수 실제 플레이 표시 통합

환경 A→B→C/D 적용 순서를 먼저 완료한 뒤 캐릭터 통합으로 넘어간다. 별도 담당은 환경 작업 동안 C02와 소환수의 파일·코드 준비를 병행할 수 있지만 같은 Editor에서 임포트·씬 저장을 병행하지 않는다.

플레이어 후보는 `Assets/_Project/Art/C02_RigFaceLab/Models/Integrated_B2_C3.fbx`와 `Animations/Integrated_B2_C3_{Idle,Walk,Run,Attack}.anim`이다. 실험 `Integrated_B2_C3_LabRig.prefab`은 카메라/UI/LabSkinningScope를 가지므로 통째로 배치하지 않는다. 기존 `PF_DosaVisual`의 `PlayerVisualRig` 계약을 따르는 Playtest 전용 파생 prefab/profile/controller를 만들고, inline `Macro_CombatPlayerRig`에 `PlayerVisualDriver`를 연결한다. motor 아래 발 원점 기준 시각 자식으로 붙이고 기존 캡슐 표시만 교체한다. `CameraRigController`의 몸 렌더러 목록도 갱신하되 기존 NearArms/붓을 보존한다. 기존 모터 위치·회피·패링·작도 판정·카메라를 유지하며 root motion을 끈다. locomotion, idle 복귀, 공격/작도, 사망·휴식·탑승에서 시각 상태를 실제 상태 소유자로부터 받는다.

실험 Animator에는 런타임 매개변수가 없다. 기존 `AC_DosaCourier`의 매개변수·draw/dodge 상태를 보존한 사본을 사용하고, B2 Idle/Walk/Run을 대체하되 없는 strafe/피격/회피 동작은 기존 Humanoid 리타게팅을 검수한다. B2 Attack은 현재 게임 트리거와 연결되어 있지 않으므로 임의의 공격 이벤트를 만들지 않는다. 천/보조 물리 복원은 이번 필수 범위로 추가하지 않는다.

C02는 52,784tris, 자체 Humanoid/24개 변형 본, 눈꺼풀 2채널이다. B2 Idle 들뜸은 개선됐지만 게임속도4.5m/s와 원본 Run 추정5.36m/s 차이, 의복/지면 접촉, 손가락·붓 파지, 시선·입 구조는 잔여 과제다. 동작 재생 속도와 시각 전이를 먼저 맞추고 이동 규칙을 모델에 맞춰 바꾸지 않는다. 모든 다섯 번째 작은 웨이트가 Unity에 남는다고 쓰지 않으며 FourBones/Unlimited 차이도 로컬 시각 설정에서 검토한다. 전역 QualitySettings를 실험값으로 저장하지 않는다. 숄더뷰 가림·작도 시 몸/팔 침범·기존 붓과 중복·발 부유가 외부 시험을 방해하면 해당 항목을 고친 뒤 전달한다.

`WorldMacroCombatWalker.Visuals`는 `Suspend()`/`Resume()`에서 탑승 시 렌더러를 숨기고 복원한다. 새 몸·눈꺼풀 렌더러 전체를 이 목록에 연결한다. 한 렌더러만 연결해 얼굴이나 눈꺼풀이 떠 있는 상태로 남지 않게 한다.

| 글자 | 보유 최종 정적 외형 | 우선 확인할 표시 경로 |
|---|---|---|
| 곰 | `SpellVFX120/WoodDeer/PF_WoodDeer_Static.prefab` | WoodDeerVfx / WoodDeerPresentation |
| 놈 | `SpellVFX120/FireHaetae/PF_FireHaetae_Static.prefab` | FireHaetaeVfx / FireHaetaePresentation |
| 몸 | `SpellVFX120/StoneJangseung/PF_StoneJangseung_Static.prefab` | 기존 몸의 정적 표시 경로와 실제 프로필 참조 확인 |
| 솜 | `SpellVFX120/MetalTiger/PF_MetalTiger_Static.prefab` | MetalTigerVfx / MetalTigerPresentation |
| 옴 | `SpellVFX120/WaterTurtle/PF_WaterTurtle_Static.prefab` | WaterTurtleVfx / WaterTurtlePresentation |

기존 `Vfx120Effect`는 표시 옵션에 따라 전방4m 지면 고정·4.6초 검토 수명을 쓰는 전용 경로로 빠질 수 있다. 파일 존재나 C2 진단 재생 성공만으로 실제 소환 전투와 연결됐다고 보지 않는다. 배우 담당의 후속 조사에서 **`SpellVisualSet`에는 다섯 소환 글자 매핑이 있으나 `SpellBook_Proto`에 해당 항목이 없고, `SpellKind`도 AttackSingle/Area/Parry만 있어 실작도 commit이 실패하며 BrushStrokeFeedAdapter가 VFX를 억제하는 연결 공백**을 확인했다. 따라서 외형만 붙이는 것으로 P4는 끝나지 않는다.

High 통합 담당은 기존 어휘·DECISIONS #67의 개별 소환 행동 TBD 범위를 먼저 대조해, Playtest 전용 book/profile과 명시적인 Summon dispatch를 좁게 연결한다. 최소 표시 연결은 정지 위치·상한 있는 수명·재시전/종료 정리·zero-aggro/zero-groggy이며, 미정 행동의 공격력/공격 방식/이동 AI를 발명하지 않는다. 공격 술식 AttackSingle로 위장해 소환 VFX를 통과시키지 않는다. 공유 CSV의 글자 배정이나 기존 SpellBook 자산의 의미를 덮어쓰지 않고, 필요 enum/API 확장은 기존값 직렬화 보존·공유 씬 회귀와 함께 검토한다. 원장에 단순 정적 소환의 실제 제공 기능과 미정 전투 행동을 명시한다.

실제 시전 어댑터부터 생성→활성 유지→구현된 효과→취소/만료/씬 종료까지 추적해 gameplay Duration과 표시 수명, 고정 위치와 실제 효과 위치를 일치시킨다. 탱킹/어그로 금지 규칙을 유지한다. static mesh를 늘이거나 미끄러뜨려 리깅 보행이 구현된 것처럼 보이게 하지 않는다.

**수락:** 실제 게임 카메라에서 플레이어 모델이 보이고 이동/정지/회피·작도·피격·탑승복원 시 치명적 가림·부유·분리·T-pose가 없다. 다섯 글자를 실제 시전 경로로 만들 때 각 외형이 올바른 지점·시간에 나타나고 게임 효과와 분리되어 남지 않는다. 풀 재사용·재시전·0.2배 시간·취소·사망·씬 종료 뒤 표시/입자 누수가 없다. 리깅·소환수 이동 AI를 구현하지 않았다면 정적 소환 외형 연결로 명시한다. 플레이어·소환수 미연결 상태는 외부 빌드 진행 조건을 충족하지 못한다.

### P5 — 최종 미술 회귀, 실측, 테스트 빌드

환경·플레이어·소환수까지 갖춘 씬을 기준으로 대표 구도와 상태 검사를 다시 실행한다. 83개 예약 캡슐·제작 표식은 외부 시험의 최종 화면에서 노출되지 않도록 시각 표시 소유권을 분리한다. 원본 콘텐츠 ID·배치·아직 필요한 조사/NPC 표시를 지워 숨기지 않는다. 첫 구간의 실제 NPC/적이 임시 외형이면 안내문과 검사표에 그대로 기록하고, 근거 없는 새 유료 캐릭터 생성으로 범위를 확대하지 않는다.

Editor 고정 1080p 측정은 기존 `WorldMacroPlaytestPerformance`의 30 warmup+120 samples를 출발점으로 쓴다. 폐광 교전, 청림 수관, 성저/남문 스카이라인, 플레이어+소환수 표시의 무거운 구도를 각각 측정한다. CPU·GPU 가용 샘플 수·중앙/P95·할당/예약 메모리·시스템 커밋·draw call/visible tris를 기록한다. GPU 미수집을0ms로 쓰지 않는다. 측정이 예산을 넘으면 시야 밖 상세·원경 LOD·머티리얼/그림자·식생 overdraw부터 줄이고 핵심 구도의 모델을 제거해 목표를 맞추지 않는다.

미술 및 통합 P1–P4를 완료한 뒤, Playtest 전용 Windows 테스트 빌드를 생성한다. 현재 일반 build settings에는 활성 씬11개의 마지막에 Macro가 있어 그대로 빌드하면 원하는 시작 씬을 보장하지 않는다. 이 출력의 명시적인 scene list를 Playtest 첫/단일 씬으로 주거나 필요한 bootstrap 경로를 검증한다. 출력은 날짜/revision이 분리된 새 폴더에 두고 기존 빌드를 덮어쓰지 않는다. 빌드 씬 목록·플랫폼·development/profiler 옵션·해상도를 원장에 남긴다. 실행 파일에서 첫 시작·작도/명중·휴식/사망/회수·종료/재실행·격리 저장을 검사하고 최종 빌드에서 성능을 다시 측정한다. 기존 Editor 결과를 빌드120fps로 승계하지 않는다.

**수락:** 실행 가능한 빌드·검토 이미지·짧은 조작/범위 안내·알려진 문제·검사표·revision/hash·의존성 크레딧이 함께 있다. `120fps 달성` 표기는 명시한 실제 최종 측정 조건이 뒷받침할 때만 쓴다. 사용자 직접 완주와 최종 미술 판정은 실제 피드백 전까지 미검증으로 남긴다.

## 7. 병렬 담당과 Editor 단일 소유

| 작업 난도 | 독립 작업 | Editor 권한 |
|---|---|---|
| Low | 기존 원장 조회, source GUID/part/tri/material 집계, 캡처 목록·해시·문서 작성 | 없음 |
| Medium | 데이터 SO, 단계별 배치/검사 도구, 로컬 파생 자산 준비, 정해진 부재 조립 코드 | 코드/파일 준비까지만; 적용 요청은 통합 담당에게 전달 |
| High | 지형/건물 접합·시각 구성, 실제 입력/카메라·소환 수명 통합, 성능 병목 수정·회귀 검수 | 지정된 단일 통합 담당만 순차 실행 |
| Ultra | 본 계획, 범위 충돌·저장/진행 계약 변경 위험 등 해결되지 않는 설계 판단 | 상시 제작자로 사용하지 않고 필요한 판단에 한정 |

동시에 여러 에이전트가 Unity 파일 명령을 보내거나 씬/공유 asset을 저장하지 않는다. 통합 담당은 현재 큐 응답·컴파일 종료·씬 clean 상태·시스템 커밋을 확인한 뒤 다음 패키지를 적용한다. 각 패키지는 소유 파일, 변경 의도, 원형 목록, 실행 명령, 기대 검증을 인계한다. 검수 담당은 저장된 revision의 결과만 검사한다.

환경 코드/자산 준비, 배우 연결 코드 준비, 원장/회귀 검토는 병렬화할 수 있다. 실제 Unity 환경 적용→환경 캡처→배우 적용→게임 상태 검사→최종 캡처/성능→빌드는 순차다. 무거운 Blender 작업은 필요할 때 저장된 Unity 상태와 메모리를 확인해 겹치지 않게 진행한다.

## 8. 일정과 작업 종료 기준

| 시점 | 목표 | 다음 단계로 넘어가는 조건 |
|---|---|---|
| 9월13일 | P0, P1, P2의 외림/벌목장 및 가도 연결 준비 | 첫 구간 비주얼·기능 회귀, 청림 대표 구도 확보 |
| 9월14일 전반 | P2 마무리, P3 성저·남문·스카이라인·대표 외부 | 외부 보행 범위·핵심 실루엣과 접속 검사 완료 |
| 9월14일 후반 | P4 플레이어·소환수 실제 경로 통합, 최종 미술 회귀 | 캐릭터 두 필수 항목 완료, 핵심 조작·표현 문제 해결 |
| 9월15일 | P5 성능 수정·테스트 빌드·실행/재시작 검사·전달 | 검토 가능한 결과물과 사실에 맞는 검사표 제공 |

일정은 작업 순서와 중간 목표이며 아직 수행하지 않은 결과의 약속이 아니다. 지연 시 먼저 줄이는 것은 부차 골목, 보이지 않는 건물 뒷면 상세, 비필수 소품 종류와 주변 군집 수다. 산길/폐광→청림→황경의 핵심 구도, 실제 외부 통로, 플레이어 모델, 다섯 소환수 표시, 저장 안정성을 제거해서 날짜만 맞추지 않는다. 미완료 항목은 즉시 결과표에 반영하고, 미술이 갖춰지지 않은 바이너리를 완료본으로 전달하지 않는다.

## 9. 이미지·원본 보존·최종 검사표

이미지는 **1920×1080 PNG만**, 단계별 동일 카메라의 전후 비교로 순차 촬영한다. 영상·자동 보행·자동 차량 완주는 제작하지 않는다. 각 이미지에 JSON으로 camera transform/FOV, scene revision, stage, mode(Editor/Play/build), effect age, 시스템 커밋을 남긴다. 주 검토 화면은 실제 게임 카메라이고 조감은 배치 설명용 보조다. 진단 재생을 실제 작도 캡처로 표기하지 않는다.

시스템 커밋 **85% 이상이면 새 캡처·무거운 새 로드를 중단**하고 현재 상태·실패 원인을 저장한다. RenderTexture/카메라/임시 모델은 단계마다 정리한다. 이전 성공 이미지가 있다는 이유로 현재 revision 검증을 통과로 올리지 않는다.

실행 산출물 제안 경로는 `Art/World/WorldMacro/Playtest/VisualCorridor/`다. `REPORT.md`, `REVIEW.html`, `asset_manifest.tsv`, `source_hashes_before/after.json`, `gameplay_diff.json`, 단계별 `validation*.json`, `Images/*.png/json`, `performance*.json`, `build_manifest.json`으로 나눈다. 기존 AssetReuse/Polish/Landmarks/Dressing/RegionalSky/WaterSurface 기록은 유지한다.

외부 전달 시 다음 항목을 각기 통과·실패·미검증으로 기록한다.

1. 현재 revision의 산길·폐광·청림·성저/남문·도시 대표 외부 미술과 자료.
2. 원본 공급자·기존 매크로 씬·지형/하늘/수면·C2/Prologue·공용 PlayerRig 보존.
3. 새 구간 접지·폭·경사·머리 공간, 기존 첫 구간 3적/5표적 기능 회귀.
4. 플레이어 모델 및 다섯 소환수의 실제 시전/상태 연결, 종료 정리와 카메라 가시성.
5. 별도 저장 슬롯, 휴식·사망·드롭·일회 획득·실제 앱 종료/재실행.
6. 1080p 최종 빌드 CPU/GPU·프레임타임·메모리 실측과 알려진 미달 구도.
7. 실제 사용자의 보행/차량 검수·미술 판단. 에이전트의 정적/API 검사와 별도로 표시.

## 근거 파일

- `Docs/BIBLE_INDEX.md`, `CLAUDE.md`, `Docs/오행부_LDB_v0_6.md`의 LDB-REALMS/LDB-HWANGGYEONG/LDB-EA, `Docs/오행부_Narrative_Bible_v0_1.md`의 NARR-STORYLINE/NARR-FINALE.
- `Docs/Specs/SPEC-WORLD-MACRO.md`, `SPEC-WORLD-MACRO-CONTENT.md`, `SPEC-WORLD-MACRO-PLAYTEST.md`; 이전 연결 순서는 `Docs/Plans/PLAN-WORLD-MACRO-FIRST-PLAYABLE.md`.
- `Docs/Assets/ModelCatalog.reviewed.json`; `Art/World/WorldMacro/Playtest/AssetReuse/REPORT.md`, `Landmarks/REPORT.md`, `Dressing/REPORT.md`, `Content/placement.json`.
- `Assets/_Project/Scripts/Editor/WorldMacro/WorldMacroSeed.cs`, `WorldMacroLandmarkAuthoring.cs`, `WorldMacroDressingAssets.cs`, `WorldMacroDressingAuthoring.cs`, `WorldMacroPlaytestAuthoring.cs`, `WorldMacroPlaytestBuild.cs`, `WorldMacroPlaytestAssetReuse.cs`, `WorldMacroPlaytestPerformance.cs`.
- `Assets/_Project/Scripts/Data/World/WorldMacroDressingSheetSO.cs`, `WorldMacroContentSheetSO.cs`; `Assets/_Project/Scripts/App/World/WorldMacroCombatWalker.cs`; 각 대응 Playtest/WorldMacro serialized asset.
- `Art/PlayerPhase1/C02_RigFaceLab/FINAL_REPORT.md`, `Unity/UNITY_REPORT.md`; `Art/SpellVFX120/{WoodDeer,FireHaetae,StoneJangseung,MetalTiger,WaterTurtle}/REPORT.md`; `Assets/_Project/Scripts/App/SpellVFX120/Vfx120Effect.cs`.

위 `Assets/...` 코드는 저장소 안 `Oheangbu/Assets/...`에 있다. 최종 구현 전에 실제 파일/씬 참조를 다시 대조한다.


## 사용자 후속 확정 — 소환수 범위

2026-09-13: 사용자는 5종 모두 실제 시전과 등장·소멸을 연결하고, 이동·공격은 이후 진행하는 쪽을 선택했다. 이번 소환수 구현은 기존 외형 5종의 작도 승인·먹 처리·표시·정리까지다. AI·추종·공격·어그로·피해 기여를 새로 붙이지 않는다. 검토 수명은 표현용 TEST 값이며 본편 소환 능력치 확정이 아니다.
