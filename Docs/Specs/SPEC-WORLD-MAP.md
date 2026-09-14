# SPEC-WORLD-MAP — 걷는 수묵 강토 (청림 개활 1 「금표의 길」 첫 슬라이스)

> **2026-09-11 새 전체 지형에 대한 우선 적용 — DECISIONS #205 / SPEC-WORLD-MACRO.** 본문의 배치 수량·거리·방위·보행 시간·고정 POI·경로 knots는 새 5강토 전체 지도에서 큰 방향을 위한 지침으로 사용한다. 자연스러운 산계·수계·분지를 먼저 만들고 그 안에 정착지와 길을 배치하며, 규약을 맞추려고 지형을 늘이거나 단거리 우회를 강제하지 않는다. 약8×12km는 규모 참고값으로서 외곽은 불규칙하게 구성한다. 주요 가도는14m/s 가마 검토용 TEST, 도보는 기존4.5m/s이며 이번에는 실제 가마 시스템을 구현하지 않는다. 검토는 조감도·플레이 시점 스크린샷만 제출한다. C2와 프롤로그, 기존 입력·전투·진행·재화 규칙은 보존한다. 아래 기존 S2 구현·검사 계약은 이력과 기존 자산 해석용으로 유지하며 새 전체 지형의 좌표·형상 게이트로 사용하지 않는다.

| 항목 | 값 |
|---|---|
| 상태 | **TEST** (2026-09-06 창설 — 예준 문답 16건 완료: D1~D8(#158~#165) + §12 8건(#166~#169·검수 규칙). 구조(P0·P1)는 스파이크 G2와 **병행 착수** / **지형 구축(P2~) 착수 게이트 = SPEC-SPIKE-WORLD-LOOKDEV PASS**(D1)) |
| 작성 | 2026-09-06 |
| 선행 | SPEC-SPIKE-WORLD-LOOKDEV **TEST**(P1 IMPLEMENTED·G2 대기 — §5-1 InkWorld 4패스 계약·§5-2 InkLightSource·§5-3 포스트 규약·§5-8 Driver `.linear` 경로·§5-11 P2 골짜기 heightfield = 본 Spec 지리의 **시드**·§6 B1/B3/B4/B6/B10/C 계측 도구 승계) · SPEC-DEV-TEST-HUB **PASS**(빌더 배치표 단일 출처·귀환 포탈·리그 1벌) · SPEC-SPELL-FX-ASSETS **PASS**(§4.2 무텍스처·§4.7 GUID 참조·Meshy 비추적) · SPEC-ART-INK-LOOK **PASS**(팔레트 단일 출처) · SPEC-COMBAT-CORE-LOOP(이동 4.5 m/s·CC 2/0.5/45°/0.3 — 소비 계약) |
| 근거 | Constitution 기둥 2·3(`:22-23`) · 제3조 4~7항(`:30-35`) · 안티-비전 2·7(`:42,47`) · 슬롯 1+2(`:55`) · Spatial 소유(`:84,100`) · CONST-SCOPE(`:136-138`) / **LDB-CHARTER 원칙 7**(`LDB:13-23`) · 강토 방위(`:27-34`)·청림 상세(`:38`)·예산(`:44-45`) · **청림 배치 시트 LOCKED**(`:47-57`) · LDB-GATING(`:80-89`) · LDB-ATTRACTION(`:95-101`) · LDB-CHECKPOINT(`:105-110`) · LDB-EA(`:128-130`) · LDB-LENSES(`:132-136`) / NARR 서막(`Narrative:179-183`)·1막 J1/W1(`:187-188`) / Combat 지형 파괴 이원(`:85`)·시야 교란 기각(`:87`)·리셋(`:89`)·죽음·체크포인트(`:126-127`) / ArtAudio 팔레트·의미 5축(`:19-23`)·발광 상한(`:29-32`)·Skybox(`:37-39`)·실루엣(`:44`)·강토 경계(`:60-62`) / Production 맵 방침(`:46`)·스코프·EA 실측 게이트(`:49-53`)·TBD(`:57`) / DECISIONS #134·#136·#142·#144·#147·#150~#157 · **예준 결정 D1~D8(2026-09-06) = DECISIONS #158~#165** / 원천 `참고_오행부_맵표현생성가이드.md` §8~§12·§16~§17·§21·§25(**가설** — #157 보류분, §6에서 검증) / 레거시 MandateOfInk(TEST 시드만, `LDB:147` LEGACY) |
| 슬라이스 | **S2** = 청림 개활 1 「금표의 길」 전체(폐광 출구 → 금표 주막 → 역참 → 버려진 벌목 마을 → 벌목장 입구 · 금표 비석 · 심부 갈림 성황당). 도보 10~15분 [TEST] |
| 보고 | IMPLEMENTED → VALIDATED → PASS 3단. PASS = §6 A~E 전수 + G(예준 육안 + 슬롯 1+2 블라인드) + PR 머지. F는 기록 |

**본 Spec 안의 모든 수치는 [TEST]** — Spatial Placement Bible이 EA 실측 후 승격한다(`Constitution:84` · `BIBLE_INDEX:63-64`). 본문에 LOCKED 선언 없음(§6 A9가 기계로 검사).

## 바이블 추적표 (절대 제약 → 본 Spec 반영 지점)

| 제약 | 출처 | 반영 |
|---|---|---|
| 세계 변경=작도 독점 — 점프·등반·수영·레버 금지 · 지형 수단=필드 5자 | `Constitution:22` · `PlayerMotor.cs:7-9` | §3 A3 런타임 Terrain 불변 · §4 층 5 「단차 = 도보 경사 아니면 국」 · §4 층 7 여울/심연 기하 · §6 A3·C3·C4 |
| 게이트 5종 · 청림 본편 게이트 0 · 재방문 국 3~4 · 예고 게이트(폐광 국) | `LDB:23,53,80-89,130` | §4 층 5 게이트 키트(스킨만 교체) · §5 게이트 슬롯 · **§4 층 5 일방 낙차턱** · §6 A1·C6·D4 |
| 길은 인력으로(마커 0) · 인력 3층 · 크리티컬 최강 · 확정 인력원 광맥·등불 · 봉수 | `LDB:17,95-100` · `Constitution:30-31` | §4 층 10 · §5-3 시점별 인력 체인표 · §6 D1~D6(중경 = 직선 ≤270 m 감사) |
| 채색=의미 · 환경 무채+갈색 · 발광=상등 광원 4종만 · 오염 무발광 | `Constitution:33` · `ArtAudio:20-21,29-32` | §4 층 3·7·9 재질 InkWorld 계약(Emission 부재) · 광원 4종(광맥·등불·봉수·촛불) `InkLightSource` · §6 A4·A5 |
| 열려 보이되 경로는 설계 · 보이지 않는 벽 금지 · 갈래길 끝 보상 | `Constitution:34-35` · `LDB:21` | §4 층 3 경계=경사 띠 · 층 7 심연=턱·난간 기하 · §6 A2·C7·D4 |
| 패스트 트래블 없음 · 마석 자동차 한 줄 | `Constitution:42` | §3 A1 EA 예약(`WorldScaleSO.gado*` 필드만) · §6 C10 기록 |
| 컷신·내레이션 없음 | `Constitution:47` | §5 V1 = 스폰 그대로 1인칭 · §6 D3 |
| 슬롯 1+2 검수 「색만 따라 첫 주막」 · 폐광 출구=아침의 강토 | `Constitution:55` · `Narrative:180` | §5 MineExit→Inn 체인 · §6 G 블라인드 |
| 실배치 수치=Spatial · Spec 내 LOCKED 금지 | `Constitution:84,100` · `BIBLE_INDEX:63-64` | 전 수치 [TEST] · §3 D4 승격 절차 · §6 A9 |
| 배치 문법=LDB · 사람/자동 비대칭 · 인터페이스=데이터 | `LDB:13-14` · `Production:11-15` | §7 역할표 · §4 층 2 시트 SO |
| 청림 배치 시트(금표의 길·벌목장·금표 주막·성황당 4~5·신목 원경·빛의 뼈대·J1 역참·W1 객주 분소) | `LDB:47-57` · `Narrative:187` | §5 POI 전량(역참·객주 분소 = 주막·마을과 **별개 POI**) · 성황당 이름 3(진입로·벌목장 앞·심부 갈림) 불변 |
| 온기 희소(주막 2~3) · 성황당=난소 직전 30초 · 지름길이 간격을 접는다 · 서사 기본 침묵 | `LDB:107-110` | §5 체크포인트 간격 ≤3.5분 · 벌목장 앞→입구 ≤30 s · 절벽 상단 루프 · `narrativeSlot=""` |
| 죽음=통보 현장 드롭·회수 런(적 배치가 긴장) · **낙사 = 자유 낙하 ≥6 m 사망, 드롭 = 마지막 접지점**(#166) · HUD 4(드롭 마커 0) | `Combat:126-127` · `Constitution:31` | §3 A6·A7 회수 런=체크포인트 간격 · 드롭 표식=디제틱 슬롯 · §6 C4·C5 |
| 지형 파괴 이원(기계=땅·오행=먹 덮음) | `Combat:85` · `LDB:20` | §4 층 3 `_OhMarkMask` 런타임 RT 훅(저작 텍스처 0) |
| 시야 교란 기각 · 원근=포스트 단독 · #151 4조건 | `Combat:87` · `LDB:115` · `DECISIONS:307` | §4 층 1 페이드 비율(최원경 기준) · 안개 0 · §6 B3·B4 |
| 강토 Skybox · 대기 시드(청림=숲 바닥) · 전역 앵커 | `ArtAudio:37-39` · `LDB:99` | §3 A2 하늘=소지 · `RealmSheetSO.fadeEndTint` 예약 · §12 |
| 강토 경계=Skybox·씻김 종점·음악·소리 인력 전환점 — 「강토 영역」 데이터 하나 | `ArtAudio:61-62` | §4 층 2 `RealmSheetSO` |
| 무텍스처·색 단일 출처·Emission 0·인식 무접촉·HUD 4·모든 월드 씬 Driver | `DECISIONS:311` · 스파이크 §4·§5-8 | §3 D5 · §4 불가침 · §6 A4~A6 |
| Terrain 중심+Cliff 하이브리드(착수 시 승격) | `Production:46` | §3 D2 · §14 승격 |
| 수직의 의미론 — 인간의 건축은 수평, 높이는 인간 밖의 것만(TBD 방향) | `LDB:101` | §3 A16 높이 규칙 **보류**(적재탑 8 m = 배치값 [TEST]) · 적재탑 = 2순위 인력 |
| 이동 4.5 m/s · CC 2/0.5/45°/0.3 · 눈높이 1.8 | `CombatConfigSO.cs:11` · `PlayerRig.prefab:155-161` · `WorldLookAudit.cs:17` | §4 층 1 계수 270 m/분 · §6 A9 미러 감사 · §6 C |
| 스파이크 거리 규약 15%/65%(최원경 기준)·keep 0.55·법선 엣지 140·far 1000 계약 | 스파이크 §5-11·§2 | §3 D7 · §4 층 1 · §10-3 |
| Meshy 비추적·preview ≤2/세션 ≤12·유료 팩 미커밋·한글 Tooltip 금지 | FX-ASSETS §4.2·§4.7 · `.gitignore` · 스파이크 §4 | §4 층 4·5 · §6 A8 · §10 |
| MCP 수칙(tests-run 금지·script-execute 불능·정적 string 메서드·빌더 에셋 참조는 NewScene 뒤) | `run-20260830.md §MCP` · `run-20260906.md §MCP` | §7 빌더/감사 진입점 |
| 가이드=가설, 통과분만 승격 | #157 · `BIBLE_INDEX:75-77` | §4 층 3 경사 3분류·층 8 실루엣 겹수 「가설」 표기 + §6 검증 항목 |

## 1. 목적

「산·언덕·개울이 있는 걷는 지형이 가까이서는 도보 소울라이크의 땅이고, 멀리서는 수묵담채 원경으로 읽힌다」를 **청림 개활 1 「금표의 길」 한 구역**에서 실증하고, 그 과정에서 강토 전체로 복제 가능한 **데이터 스키마·결정론 빌더·Blender 랜드마크 계약·Terrain 봉인·씬 구조·인력 시야선 검증**을 확정한다.

### Goals (검수 가능 목표)
1. **걷는 땅 + 그림**: 본선·지선 전 구간이 CC 규격(경사 ≤45°·턱 ≤0.3)으로 걸리고(§6 C3), 컷 V1이 승인 컷2 밴드 안에 들어온다(§6 B1).
2. **첫 실측 3건**: 폐광 출구→금표 주막 도보 분(D4 시드 → Spatial 입력) · 개활지 저작 단가(PROJECT_STATUS Risk MEDIUM) · 슬롯 1+2 블라인드(「색만 따라 첫 주막」).
3. **결정론**: 같은 시트·시드 → 같은 하이트맵·같은 배치(§6 A11). 사람 저작물은 `.blend`와 시트 SO에만 산다.
4. **Blender 랜드마크 계약**이 헤드리스 검사 → Unity 컨폼 → 이음 감사까지 왕복 1회로 닫힌다(§6 C8·C9·B14).
5. **Terrain 봉인**과 스파이크 계약 무손상(§6 A7·A10) — 텍스처 0·Emission 0·색 단일 출처.
6. **크레딧**: Meshy preview ≤2 · Recraft ≤1 · 외부 소스 0.

산출물(살아남는 자산 = 구조, #134):
- Data SO 5종: `WorldScaleSO` · `RealmSheetSO` · `AreaSheetSO` · `LandmarkMeshSO` · `TerrainSynthSO`(수치 전부 [TEST]) + `AreaIdEventChannelSO`
- 셰이더 2종: `InkTerrain.shader`(InkWorld 4패스 이식 + TerrainInstancing + 픽셀 법선 + 홀) · `InkRiver.shader`
- `WorldMapBuilder`(결정론 — 입력 해시=출력) · `WorldMapAudit`(§6 항목별 `public static string`) · `LandmarkMeshPostprocessor` · `Tools/Blender/landmark_check.py`
- 씬 2종: 상주 `W_Cheongrim_FarSet` + 개활 `W_Cheongrim_GeumpyoRoad`(Additive) · NavMesh 데이터

## 2. 범위 · Non-Goals

**범위(S2 = D3)**: 금표의 길 전체(`LDB:47`). Terrain 타일 1장(+예비 1) + 절벽/바위 키트 + 원경 세트(능선 4겹+Terrain 가장자리=5겹·신목 실루엣·봉수) + 길 스플라인 + 개울 1(여울 3·심연 1) + 희소 식생 + 게이트/POI/체크포인트/역참/객주/국 예고 슬롯 + NavMesh + 계측 + 저작 단가 실측.

**소비하는 계약(변경 금지 — 스파이크 PASS 전)**: `InkWorld.shader` 프래그먼트 산술 · `InkLightSource` 마커 · `InkWorldPost` 씻김·면제(P2) · `WorldLookDriver` `_Oh*` `.linear` · `VP_InkLook`(#156) · `ElementPaletteSO` · `PlayerRig.prefab`(FOV 60·far 1000 — far는 §4 층 1 Driver가 **런타임에만** 덮어씀, D7) · `DevSceneKit` 빌더 패턴 · MCP 수칙.

**Non-Goals(후속으로 이월)**: 물든 심부·벌목장 내부·뿌리 굴·신목 영역·청룡 성역(접속점 슬롯만) / 프롤로그 갱도 내부(Stage 1 — 출구 포탈·홀·국 예고 슬롯 ID만) / 게이트 **기능**(국 상승 판정·열림 상태·세이브 — Spellcraft/Combat) / 적 배치·리스폰·적 AI NavMesh 소비 / 동행 NPC(상경 가도) / 마석 자동차 구현 / `InkSkyProfile`·강토 틴트 그라데이션·구름(SPEC-SKY) / 물 반사·오염 수역·투명 물·흐름 애니메이션(SPEC-WATER) / 밀식 캐노피·GRD·임포스터·크로스페이드(SPEC-VEG) / 환경 담채 열(스파이크 G2 종속 팔레트 문답) / 경사·고도 먹 재도입(**금지 유지**, #151) / Addressables · 세이브 구조 · 디제틱 지도 소품 · 오디오 실체 / 바닥 흔적 실제 그리기(훅만) / 한지 질감 · 유료 팩 룩.

## 3. 결정 (D1~D8 = 예준 2026-09-06 구속 · A = 본 Spec 가정 [TEST]/[TBD])

| # | 결정 | 본 Spec 반영 | DECISIONS |
|---|---|---|---|
| **D1** | 별도 Spec. 스파이크 P2~P4가 룩 층 규약(포스트 페이드·능선 실루엣·계측) 공급, P2 골짜기 heightfield(z 0~140·x ±80) = 프롤로그 첫 야외 지리의 **시드**. 구조·결정·검증은 G2 병행, **지형 구축 게이트 = 스파이크 PASS** | §8 P1(구조 — 셰이더·Terrain 무접촉) 즉시 / P2~(InkTerrain 이식부터) 게이트 · `TerrainSynthSO.spikeSeedImport` | #158 |
| **D2** | C 하이브리드: 근·중경 Unity Terrain 타일 + 무텍스처 `InkTerrain`(4패스 이식 + TerrainInstancing + 픽셀 법선 + 홀 clip) + 절벽/바위 키트 + 게이트 프리팹 / 원경 = 능선 폴리라인 압출 실루엣 3~5겹(콜라이더 0·그림자 0) / 최원경 = 소지 하늘. **Terrain 텍스처 기능(레이어·디테일·나무 빌보드·베이스맵) 봉인** | §4 층 3·5·8 · §6 A7 봉인 감사 | #159 |
| **D3** | 첫 슬라이스 = S2 금표의 길 전체(도보 10~15분 TEST) | §2 · §5 | #160 |
| **D4** | 스케일 캐논 시드: 폐광 출구→금표 주막 **2~3분 @4.5 m/s ≈ 540~810 m(경로 길이)**. `WorldScaleSO` [TEST] 선언 → 프롤로그 실측 후 Spatial 승격 | §4 층 1 · §5 · §6 C1(실측 밖이면 SO가 아니라 **시트 knots**를 고친다) | #161 |
| **D5** | 전면 무텍스처(지형·식생·데칼): 먹 램프 + 월드 노이즈만 · 식생 = 절차 실루엣 카드/로우폴리 산포 · 바닥 흔적 = 월드 XZ 투영 마스크(런타임 RT). 예외는 후속 룩 게이트로만 | §4 층 3·6(길=리본 메시, 굽는 마스크 0)·9 · §6 A4·A8 | #162 |
| **D6** | 물 = S2 개울 1 = **불투명 `InkRiver` 리본**(중경 물길 인력). 여울 도보 가능 / 심연은 경사·바위 기하로 차단(보이지 않는 벽 0). 반사·오염 수역 = SPEC-WATER | §4 층 7 · §6 C4b·D5 | #163 |
| **D7** | far clip = `WorldScaleSO` 강토별 [TEST](금표의 길 2500~3000). 페이드 15%/65%는 **최원경(L4 반경) 기준** 비율, 능선 반경 동일 SO 파생. 스파이크 씬 far 1000 유지(「far 무변경」 계약은 스파이크 씬으로 재범위) | §4 층 1 · §6 A10·B4b · §10-3 | #164 |
| **D8** | 손맛 영역(랜드마크·비스타·보스 아레나 바닥) = **예준이 Blender 메시**로(Terrain 브러시 아님). 파이프라인 = Blender 계약(단위·피벗·FBX `FBX_SCALE_UNITS`·콜라이더) + Terrain 컨폼(올림/파냄/홀) + 이음 규칙 + 헤드리스 `blender --background --python` 검사 | §4 층 4 · §7 · §6 C8·C9 · **Terrain 브러시 손질 금지**(결정론) | #165 |

### 3-1. 검수 결정 기록 (2026-09-06 · 예준)
문답 2회 8건(관계·기술·범위·스케일 / 텍스처·물·far·저작 도구) = D1~D8 → DECISIONS **#158~#165**. §12 문답 8건(2026-09-06 2차) → **#166 낙사 규칙(Combat 11장) · #167 등불 파생색 · #168 보상 인력=비색 분리 · #169 역참·객주 별개 POI(높이 규칙 보류)** + 검수 규칙(§6 G 예준 단독)·P0 chore 전부·스파이크 연동(§10-4 요청 승인·통합 별도 chore·Recraft 컷 참고) = Spec 갱신. 잔여 가정은 [TEST]/[TBD] 유지 · 기술 편의 = §10 Temporary Exceptions.

### 가정 (예준 미결 — [TEST]/[TBD]로 세우고 §12 문답 대기)
| # | 가정 | 상태 | 반영 |
|---|---|---|---|
| A1 | 마석 자동차 = **EA 예약**: 길 클래스 `Gado` 규격 필드(폭 ≥6 m·종단 ≤12°·회전 반경 ≥12 m, 레거시 VC 14 m/s 시드)만. S2 길은 `Trail`. 구현 0 | [TEST] | `WorldScaleSO.gado*` · §6 C10 |
| A2 | 하늘 = 소지 SolidColor · 강토 틴트는 **페이드 종점 색 1개**(`RealmSheetSO.fadeEndTint`, 청림 = Paper) — 「어둑한 녹음」은 숲 바닥(`ArtAudio:38`·#154). `InkSkyProfile` = SPEC-SKY | [TEST] | 층 2·8 |
| A3 | **런타임 Terrain 편집 없음** — 게이트=프리팹 상태 스왑/볼륨, 흔적=XZ 런타임 마스크. 콜라이더 재빌드·NavMesh 갱신 0 | [TEST] | 층 3·5 · §6 A9 |
| A4 | S2 식생 = 희소 실루엣 ≤1500 인스턴스(인스턴싱은 스파이크 PASS 후 §10-1) — 밀식 = SPEC-VEG | [TEST] | 층 9 · §6 E |
| A5 | 씬 = 개활 Additive + 상주 원경 세트. Addressables 없음 | [TEST] | 층 11 |
| A6 | **낙사 = 사망**(#166 · Combat 11장 [TEST]): 자유 낙하 **≥6 m** 사망 → 통보 드롭 = **떨어지기 직전 마지막 접지점**(회수 런 도달 100% 자동) · 2~6 m 무피해. S2 설계: 본선·지선 코리도 ±3 m 안 에지 자유 낙하 ≤2.0 m(**실수 낙사 0**) · 치명 에지(≥6 m)는 코리도 밖·보이지 않는 벽 0(경고 = 실루엣·절벽 키트) · 절벽 상단은 국으로만 오르고, 하산 = 도보 내리막 끝의 **일방 낙차턱**(0.35<h≤2.0)으로 본길 합류(층 5). 낙사 판정·드롭 구현 = Combat(본 Spec은 지형만) | [TEST] | §6 C4·C6 |
| A7 | 회수 런 상한 = 인접 체크포인트(주막 포함 `Combat:127`) 도보 ≤3.5분(945 m) — 드롭 마커 0이라 회수 런은 「왔던 길」. 드롭 표식 = 디제틱 슬롯(먹 웅덩이 — Combat 구현) | [TEST] | §5 · §6 C5 |
| A8 | 휴식 리셋 단위 = **TBD**(`Combat:89`는 「일반만 리셋」뿐 — 「구역 경계」 문구 정본 없음). `AreaTransitionVolume`은 리셋 훅 0 | [TBD] | 층 11 |
| A9 | 동행 NPC = S2 밖(상경 가도). NavMesh 에이전트 타입 `Humanoid_Oheangbu` 1종만 | [TBD] | 층 12 |
| A10 | 폐광 갱도 국 예고(`LDB:130`)는 Stage 1 소유. S2는 `GateSlot.id="MinePreview"` ID만 예약, 재방문 보상 시야는 절벽 상단 슬롯 연동 | 슬롯 | §5 |
| A11 | 역참(정담 J1)·객주 분소(왕소 W1) = **주막·마을과 별개 POI**(#169 — `LDB:56`·`Narrative:187` 별개 항목) — 온기 희소(주막 2~3)에 산입 안 함(등불 0·촛불 0). 서사 침묵 기본(`LDB:110`) | [TEST] | §5 |
| A12 | 성능 예산 = 본 Spec [TEST] 사전 선언 → EA 실측 PASS 시 Production 승격. 절대 HW 미기록(상대) | [TEST] | §6 E |
| A13 | 등불 색 = 팔레트 **파생 함수** `GetLanternColor() = lerp(WoodColor, PaperColor, 0.30)`(#167 — 계산치 (171,135,104) → HSV S≈0.39·H≈28°, 최대 채널 0.67 LDR — `ArtAudio:23` 「갈색의 난색 연장」, 저장 색 0) — 착색 실측 대기. B11 임계(S ≥0.30·H 20~40°)를 **계산치로 먼저 통과시킨 뒤** 실측 · 실패 시 무채 폴백(§10-9) | [TEST] | 층 10 · §6 B11 |
| A14 | 국 게이트 물리 규격 부재(`LDB:80`) → 배치 규격 「국 1회 = 수직 단차 5 m」를 게이트 프리팹 필드로만(코드 0) · Blender 절벽은 ±2 m 여유 | [TEST] | 층 5 |
| A15 | 밝음→어둑(`LDB:57`)은 **식생 밀도·절벽 그늘·계곡 폭**(배치)으로 — 셰이더 경사·고도·조도 볼륨 항 재도입 없음(#151). §6 B12 실패 시에만 InkTerrain 한정 조도 볼륨 [가설] 문답 | [TEST] | 층 9 · §5 |
| A16 | 인간 구조물 높이 규칙 = **보류**(예준 2026-09-06 — `LDB:101` 수직의 의미론 문답 때 함께). S2 배치값: 적재탑 8 m(국 단차 5 m 충족)·초가 ≤6 m [TEST] | [TBD]+[TEST] | 층 5 · §5-3 |

## 4. 설계 (12층 · 런타임 커스텀 렌더 코드 0줄 · 빌더/감사 = EditorTools)

**불가침(위반 = 버그)**: Drawing·Core·Combat·Spellcraft·Presentation 무변경 / 환경 재질 Emission 프로퍼티 0 / 머티리얼·씬 색 저장 0(`_Oh*` 전역만) / 저작 텍스처 0(Terrain 레이어·디테일·빌보드·스플랫·Alpha Cutout 0 — 엔진 내부 `_TerrainHeightmapTexture`·`_TerrainNormalmapTexture`·`_TerrainHolesTexture`와 런타임 RT `_OhMarkMask`만 허용, 목록 고정) / 보이지 않는 벽 0 / 런타임 `TerrainData` 쓰기 API 0 / 미니맵·마커·컷신 0 / 한글 `[Tooltip]` 0(**셰이더 한정** — C# `[Tooltip]`은 한글 허용) / 유료 팩·Meshy 생성물 커밋 0 / 코드 미감 상수 0(수치 = SO·재질·프리팹) / **스파이크 PASS 전 `InkWorld.shader` 무접촉**.

**asmdef** — `Oheangbu.Core` 무변경(프로젝트 모듈 의존 0 유지) / `Oheangbu.Data`(Core만 참조): SO 5종 + enum + `AreaIdEventChannelSO` — **Combat 참조 금지**(Combat이 Data를 참조하므로 순환) → 이동 속도는 **값 미러**(§6 A9 동치 감사) / `Oheangbu.App`: `WorldScaleDriver`·`AreaLoader`·`RealmLifetimeScope`·`AreaLifetimeScope`·`PoiSlot`·`GateSlot`·`AreaTransitionVolume`·`WorldBuildStamp` / `Oheangbu.EditorTools`(Editor 전용) + `Unity.AI.Navigation`(런타임 — `NavMeshSurface`·`NavMeshModifierVolume` 생성)·`Unity.AI.Navigation.Editor`(베이크)·`Unity.Splines`·`Unity.Splines.Editor`·`Unity.ProBuilder`(키트 원형 검사) — **`Unity.TerrainTools.Editor`는 호출 API가 없으므로 참조하지 않는다**(§10-2). 런타임(App·Data) 참조 0 / Presentation·Drawing·Combat·Spellcraft 변경 0. 셰이더 = `Assets/_Project/Shaders/`.

**패키지(직접 의존 신설 — P0 chore 커밋)**: `Packages/manifest.json`에 `com.unity.terrain-tools` 5.3.2 · `com.unity.splines` 2.8.4 · `com.unity.probuilder` 6.0.8 **직접 등재** — 현재 셋은 `com.unity.feature.worldbuilding`·MCP 패키지 경유 **간접(depth 1)** → 피처/MCP 제거 시 함께 사라진다. `com.unity.ai.navigation` 2.0.10은 직접. Cinemachine·formats.fbx 추가 없음(무컷신·FBX 내장 임포트). 빌더 1단계 `EnsureManifestDeps()`가 실행 시 재확인(§6 A8).

### 층 1 — `WorldScaleSO` (Data · 강토당 1 · 전 필드 [TEST])
`Scripts/Data/World/WorldScaleSO.cs` → `Data/World/WorldScale_Cheongrim.asset`

| 필드 | 값 | 파생/근거 |
|---|---|---|
| `walkSpeedMps` | 4.5 | **미러**(`CombatConfigSO.cs:11`) — §6 A9 동치 감사. `MetersPerMinute = ×60 = 270` |
| `eyeHeight` | 1.8 | `WorldLookAudit.cs:17`(0.7+1.1) |
| `cameraFar` | 2800 | D7 2500~3000 중앙 · 최원경 1800 + 여유 |
| `farSetRatio` | 0.64 | 스파이크 640/1000 → `FarSetOuterRadius = 2800×0.64 ≈ 1800` |
| `paperFadeStartRatio / EndRatio` | 0.15 / 0.65 | 스파이크 §5-11 **최원경 기준** → **270 / 1170 m** |
| `paperKeep / paperStrength` | 0.55 / 0.9 | 스파이크 승계 |
| `normalEdgeFadeDistance` | 140(절대) | 근·중경 속성 — 강토 규모 무관 |
| `midAttractionMaxDistance` | = PaperFadeStart(270, 읽기 전용) | #151(i) · §6 D1 「layer==Mid ⇒ 직선 ≤270」 |
| `ridgeLayerCount` | 4(+Terrain 가장자리 = 5겹, 승인 컷 정합 — 가이드 §21 **가설**) | `ridgeRadiusRatios[] = {0.50,0.67,0.83,1.00}` → **900/1200/1500/1800 m**(L1 900 > 타일 반대각 724 — 모서리 관통 0) · **`ridgeElevationDeg[] = {6, 7.5, 9, 10.5}`**(바깥 겹일수록 높게 — 먼 산이 화면 위에 쌓이는 심원 문법, 기준점 = 구역 중심) → h = r·tanθ = **95/158/238/334 m** · `ridgeNoiseAmp` 0.25(높이 ±25%, 층별 시드) |
| `sinmokNotchDeg / sinmokNotchScale` | ±8° / 0.75 | 신목 방위 노치(L2~L4 높이 ×0.75) — `ArtAudio:44` 「한 번에 분리」 |
| `rimElevationMaxDeg` | 4 | 타일 4변 림 상단 앙각 ≤4°(본선 표본 전부 — 300 m 거리면 ≈21 m, 150 m면 ≈10.5 m). 림 높이는 값이 아니라 파생 |
| `checkpointMaxWalkMinutes` | 3.5(=945 m) | A7 |
| `bossShrineSeconds` | 30(=135 m) | `LDB:109` — S2는 벌목장 입구(난소)에 적용 |
| `innTargetWalkMinutes` | 2.5(범위 2~3) | D4 |
| `areaWalkMinutes` | min 10 / max 15 | D3 |
| `trailCorridorWidth / trailMaxGradeDeg / trailCrossGradeDeg / rampMaxGradeDeg / rampMaxLengthM` | 6 / 15 / 5 / 30 / 20 | 레거시 통로 6 m·carve 25~34° 교훈 · CC 45°의 2/3 |
| `gadoMinWidth / gadoMaxGradeDeg / gadoMinTurnRadius` | 6 / 12 / 12 | A1 |
| `maxWalkableDropM / oneWayDropMinM` | 2.0 / 0.35 | A6 · CC step 0.3 초과 = 못 오름 |
| `cliffSlopeDeg / boundarySlopeDeg / boundaryBandM` | 55 / 60 / 40 | 가이드 §10 3분류(**가설**) · 경계 벽 대신 경사 띠(림 **안쪽** 사면) |
| `fordMaxDepth / deepMinDepth / sillMinHeight / parapetMinHeight` | 0.3 / 1.5 / 0.6 / 0.8 | D6 · step 0.3의 2배 |
| `sightlineMinAngularDeg` | 0.8(≈15 px @1080p·FOV 60 ≈18 px/°) | §6 D1 |
| `beaconFarPlateM` | 25 | 1800 m에서 ≈0.8°≈15 px(B10 표본 전제) |
| `loggingTowerHeightM` | 8 | 배치값 [TEST](높이 규칙 보류 A16) |
| `vegetationMaxInstances` | 1500 | A4 |

`Scripts/App/World/WorldScaleDriver.cs`(`[ExecuteAlways]`, FarSet 씬 상주, **직렬화 `WorldScaleSO` 필드 = 정본**(에디터 모드 동작), 플레이 시 LifetimeScope가 같은 SO를 `Inject`로 덮어쓸 수 있음 — 싱글턴 0): `camera.farClipPlane = cameraFar`(**플레이 모드 한정** — 에디터 모드에서는 리그 프리팹 인스턴스의 far 1000을 건드리지 않는다, §10-3·A10) · `Shader.SetGlobalFloat(_OhPaperFadeStart/_OhPaperFadeEnd/_OhPaperKeep/_OhPaperStrength/_OhNormalEdgeFade)`(에디터 모드 포함) · `_OhScaleDriven = 1`; OnDisable → 0. **스파이크 P2 인터페이스 요청**: `InkWorldPost`가 `_OhScaleDriven > 0.5`면 전역을, 아니면 재질값(90/420)을 쓴다 — 스파이크 씬은 Driver 없음 = far 1000·재질값 유지(D7). 미반영 시 폴백 = §10-4(플레이 모드 한정).

### 층 2 — 배치 시트 SO (Data · 사람 입력의 유일 창구 · 씬 타입 참조 0)
- **`RealmSheetSO`** `Data/World/RealmSheet_Cheongrim.asset` — 「강토 영역 데이터 하나」(`ArtAudio:61-62`)의 실체: `realmId(enum RealmId)` · `element='ㄱ'`(木 → 광맥·봉수 색 `GetRawVeinColor('ㄱ')` — 스파이크 빌더 `BeaconInitial='ㄴ'`은 스파이크 한정) · `worldScale` · `areas[](AreaSheetSO)` · `dominantLandmarkId="Sinmok"`(`LDB:52,99`) · **`sinmokPosition`(타일 좌표 (1450, 1400) [TEST] — 중심에서 ≈1290 m, 링 2~3 사이, 심부 방위)** · `attractionBaton[]`(광맥→신목→도성 `LDB:69`) · `gateSkin(enum)` · `musicPaletteId`·`soundAttractionId`(문자열 슬롯, 구현 0 `ArtAudio:60-62`) · `fadeEndTint`(enum `FadeEndTint{Paper}` — 색 저장 0, 청림=Paper A2) · `farSetSeed` · **원경 세트 정책 필드(P1 추가 — A9 리터럴 0의 귀결, 전부 [TEST])**: `sinmokHeightM` 140 · `sinmokTrunkRatio` 0.1 · `sinmokCrownStartRatio` 0.4 · `sunEuler` (18,160,0) · `ridgeAmbientLevels[4]` 0.10/0.15/0.20/0.25 · `sinmokAmbientLevel` 0.15 · `standinAmbientLevel` 0.35 · `returnPortalBehindM` 3.
- **`AreaSheetSO`** `Data/World/AreaSheet_GeumpyoRoad.asset` — `areaId` · `scenePath` · `tiles[]{originXZ, sizeM=1024, heightM=300, heightmapRes=1025}`(`holesResolution`은 엔진 파생 = heightmapRes−1, 읽기 전용 · `alphamapRes/baseMapRes`는 봉인 상수 16 — 시트 필드 아님) · `synth(TerrainSynthSO)` · `paths[]{id, class(Trail|Gado), knots Vector3[], rewardSlot}` · `streams[]{id, knots Vector3[], width, segments[]{t0,t1,kind(Ford|Deep)}}` · `landmarks[]{landmark(LandmarkMeshSO), position, yawDeg}` · `pois[]{id, kind(PoiKind), position, yaw, padRadius, targetWalkMin, narrativeSlot=""}` · `checkpoints[]{id, kind(Inn|Shrine), poiId}` · `gates[]{id, kind(Guk|Nun|Mum|Sut|Ung), mode(Preview|Revisit|MainPath), position, stepHeight=5, rewardSlot}` · `attraction[]{fromPoi, toPoi, layer(Far|Mid|Near), source(Lantern|Candle|Vein|Beacon|Water|Silhouette), yaw, maxStraightM}` · `sightlines[]{fromPoi, target, yaw, minAngularDeg}`(§6 D1 입력 — 목표 = 씬 오브젝트 `Renderer.bounds`) · `walkTargets[]{from,to,minutes,tolerance}`(§6 C1 입력) · `cuts[]{id, rule}`(§6 컷 포즈 규칙) · `vegetation{zones[]{poly, density}, exclusionPolys[], noVinePolys[], seed}` · `cliffKitSet`. **규칙: Data는 Splines/Terrain/씬 오브젝트를 참조하지 않는다** — knots = `Vector3[]`, 씬 `SplineContainer`는 빌더 산출. §5 표는 이 SO의 열람 미러.
- **`LandmarkMeshSO`** `Data/World/Landmarks/LM_<Name>.asset` — `meshGuid(string)`(FX-ASSETS §4.7 관행) · `footprint Vector2[]`(XZ 다각형, `footprint.json` 임포트) · `groundOffset` · `conformMode(Raise|Carve|Hole|None)` · `blendBandM=4` · `collider(UCX|Mesh|None)` · `triBudget=8000` · `blenderSource="Art/Blender/LM_<Name>.blend"` · `contractVersion` · `checkReportHash`.
- **`TerrainSynthSO`** `Data/World/TerrainSynth_GeumpyoRoad.asset` — `seed` · `macroAmplitude` · `macroWavelengthM` 400(P1 추가 — 주파수 리터럴 0) · `ridgedOctaves=5, lacunarity=2.0, gain=0.5, domainWarp=0.35` · `thermalIterations=16, talusDeg=35`(레거시 16 적정·22 과마모) · `erosionMaskEnabled`(절벽 예정 셀·경계 띠·림·컨폼 발자국 제외) · `valleyFloorY=100` · `valleyAxisKnots` · `valleyHalfWidthM` 80(P1 추가) · `spikeSeedImport=true` · `spikeSeedBlendM` 20 · `spikeSeedYawDeg` 0(**0 고정이 규약** — 데이터 필드지만 바꾸려면 `Sun.y = 160 + stampYaw` 문답을 함께; 골짜기 축 = +Z(북), 심부=북동은 타일 위 배치로. Sun (18,160,0) 무변경 = 승인 컷2와 동일 역광 관계 보존).
- **`AreaIdEventChannelSO`** `Data/World/Events/` — `EventChannelSO<AreaId>` 파생(Core 제네릭 부재 시 Data 자체 정의 — Core 무변경). `AreaTransitionVolume`이 발화만, 구독자 0(A8).

### 층 3 — Terrain 타일 + `InkTerrain` 계약 + 봉인 목록
- 타일 `T_GeumpyoRoad_00` 1024×300×1024 m · heightmap 1025(≈1 m/px — 레거시 「3 m/px는 원경까지만」) · `TerrainData` `Scenes/World/TerrainData/T_GeumpyoRoad_00.asset`(빌더 Ensure, 재실행 시 heights만 덮음) · 예비 `_01`(심부 방향, 미생성) · 경계 = 4변 림(상단 앙각 ≤4°) **안쪽 사면** 40 m 안 경사 ≥60°(보이지 않는 벽 0 + 원경 가림 0).
- **`Shaders/InkTerrain.shader`** `"Oheangbu/InkTerrain"`: 프래그먼트 = InkWorld 산술의 **복제**(PASS 전 InkWorld 무접촉 — PASS 후 `InkWorldCore.hlsl` 공유로 통합 §10-1 Exit Blocking). Terrain 계약(참조 `TerrainLit.shader:45,51,97` · `TerrainLitInput.hlsl:71-94,143-170`): Tags `"TerrainCompatible"="True"` · `"Queue"="Geometry-100"` · 패스 4종 `UniversalForwardOnly / ShadowCaster / DepthOnly / DepthNormals` · SubShader 스코프 `#pragma multi_compile_fragment __ _ALPHATEST_ON`(`TerrainLit.shader:45` 동일 미러 — local 변형 금지) · 패스별 `#pragma multi_compile_instancing` + `instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap` + `#pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL` · 정점 첫 줄 `TerrainInstancing(positionOS, normalOS, uv)` · **픽셀 법선 `_TerrainNormalmapTexture` 샘플(Forward·DepthNormals 양쪽)** — 패치 LOD 경계 셀 램프 튐(먹선 지글) 방지 · 홀 `ClipHoles(uv)` 4패스 전부 · `_BaseMap/_Splat*/_Mask*`·`Dependency` **없음**. 프로퍼티 = InkWorld 동일 무색 수치. 재질 `Art/Materials/M_InkTerrain.mat`(`_AmbientLevel` 0.35 = Rock 승계).
- **봉인 목록(빌더 `TerrainSealer.Seal()` 강제 · §6 A7 검증)**: `terrainLayers.Length==0` · `detailPrototypes==0` · `treePrototypes==0` · `alphamapResolution=16` · `baseMapResolution=16` · **`basemapDistance=20000`**(커스텀 셰이더는 `Dependency "BaseMapShader"` 부재라 폴백 자체가 없어야 함[지식] → A7b Frame Debugger 확인) · `materialTemplate=M_InkTerrain` · **`M_InkTerrain.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL")` 명시**(CustomEditor 부재 — `TerrainLitShaderGUI.cs:91` 역할 대행; `drawInstanced=false` 폴백 시 키워드 해제 — `_TerrainNormalmapTexture`는 `UNITY_INSTANCING_ENABLED` 안에서만 선언 `TerrainLitInput.hlsl:75-78`) · `drawInstanced=true` · `heightmapPixelError=5`(§10-5) · `drawTreesAndFoliage=false` · `treeDistance=0` · `allowAutoConnect=false`+`SetNeighbors` 명시(null 안전) · `shadowCastingMode=On`(RPAsset ShadowDistance 50 무변경) · `reflectionProbeUsage=Off` · 홀 = 빌더 `SetHoles`만(PaintHoles GUI 금지) · Terrain Holes 지원 on(`PC_RPAsset.asset:25`). **MCP 봉인**: `terrain-add-layer / paint-layer / remove-layer / place-trees / set-tree-prototypes / set-detail-prototypes` 호출 금지.
- **경사·고도·조도 먹 없음**(#151·A15) — 흙/바위/절벽 구분은 형상(키트)뿐. 흔적 훅: `_OhMarkMask`(R8 RT, 런타임 생성)·`_OhMarkBounds` 전역 선언, S2 미바인딩.

### 층 4 — Blender 랜드마크 계약 + Terrain 컨폼 (D8)
- **소스** `Art/Blender/LM_<Name>.blend`(**리포 루트 — Unity Assets 밖**, Assets 안이면 Blender 임포터가 .blend를 모델로 중복 임포트). 예준 자작 = 커밋. Blender 5.0.1(실측 설치) Metric·`unit_scale=1.0`(1 BU=1 m)·Z-up. 오브젝트 3종: `LM_<Name>`(렌더 ≤8000 tris) · `LM_<Name>_col`(콜라이더: `UCX_` 볼록 조각 다수 또는 단일 ≤2000 tris) · `LM_<Name>_fp`(발자국: z=0 평면 폐다각형 1개, 정점 z = 컨폼 목표). **피벗 = 발자국 XZ 중심·z=0 접지** · 스커트 min z ≤ −0.5 m(이음 은닉) · 트랜스폼 Apply(scale 1·rot 0) · 매니폴드·ngon ≤4 · 노멀 30° 스무스 · UV·정점색 없음 · 머티리얼 슬롯 1개 이름 `InkWorld_Rock`/`InkWorld_Wood`(색 0). 국 게이트 지형은 단차 5 m ±2 m 여유(A14).
- **헤드리스 검사·내보내기** `Tools/Blender/landmark_check.py` — `"$BLENDER_EXE" --background <blend> --python landmark_check.py -- --out <dir>`(`BLENDER_EXE` = `Tools/Blender/blender_path.local`(gitignore) → env → 기본 `C:/Program Files/Blender Foundation/Blender 5.0/blender.exe`). 규칙 전수(단위·Apply·3종 존재·피벗·스커트·트라이·콜라이더·매니폴드·슬롯 이름·UV/정점색 부재) → 하나라도 FAIL이면 FBX를 쓰지 않고 exit 1. 산출: `LM_<Name>.fbx` + `LM_<Name>.footprint.json{polygon[], groundOffset, contractVersion, tris, sha256}` + `LM_<Name>.report.json`. **P1 실측 메모**: 검사기가 `_fp` 루프를 z≈0으로 강제(footprint_planar)하므로 `groundOffset`은 평균 z(≈0)만 전한다 — 「정점 z = 컨폼 목표」 문면은 P3 첫 실물 랜드마크에서 재정의 · Blender XY → Unity XZ 축 대응은 P0 FBX 반입 실측(§13) 전까지 미확정(더미는 대칭이라 무영향). FBX 옵션 고정: `axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='OFF', path_mode='STRIP'`(리포 선례 `run-20260830.md`는 `FBX_SCALE_ALL` — 둘 다 1:1이나 루트 scale·fileScale 실측은 P0 스모크가 §13에 박는다). 래퍼 `Tools/Invoke-LandmarkCheck.ps1 [-Name]`(전 .blend 순회·요약·실패 exit 1). 목적지 `Assets/_Project/Art/Models/Landmarks/`. Blender MCP는 대화형 확인용, 산출물 경로는 스크립트 1개(결정론).
- **Unity 임포트 `LandmarkMeshPostprocessor`**(EditorTools, 경로 필터): `OnPreprocessModel`: `useFileScale=true`·`globalScale=1`·**`materialImportMode=ImportStandard`**·`readable=false`·`importNormals=Import`·`importTangents=None`·`addCollider=false`·애니 0 / `OnAssignMaterialModel(Material m, Renderer r)` → `m.name`(`InkWorld_Rock`/`InkWorld_Wood`)으로 `Art/Materials/M_InkWorld_*` 반환(재질 에셋 생성 0) / `OnPostprocessModel`: `_col`·`UCX_*` 자식 → `MeshCollider convex` + Renderer 제거 · 루트 scale (1,1,1)·rot 0 아니면 FAIL 로그(레거시 Meshy 100/270° 함정) + `footprint.json` → `LandmarkMeshSO` 갱신(GUID·다각형·해시).
- **컨폼 `TerrainConform.Apply()`**: `Raise/Carve` = 발자국 안 지면 = `landmark.y + groundOffset − 0.05`, 밖 `blendBandM` 4 m smoothstep / `Hole` = 발자국 안 홀 + 밖 1 m 링 유지(갱 입구). 순서 = §7 ④~⑤. 이음: 발자국 안 |지면−발자국면| ≤0.05 m · 스커트 지면 아래 ≥0.3 m · 밑면 노출 0(§6 C8·C9) · 접합선 이중 먹선 0(§6 B14).
- S2 랜드마크(예준 Blender, 우선순위 순): `LM_MineExitCliff`(출구 암벽+포탈 프레임, Hole) · `LM_GeumpyoInnPlatform`(주막 터·축대, Raise) · `LM_PreviewCliff`(절벽 상단 보상 평탄 + 일방 낙차턱, Raise, 사면 ≥60°) · `LM_GeumpyoStele`(비석·대좌, Raise) · `LM_VillagePlatform`(마을 터, Raise) · `LM_JunctionShrinePlatform`(Raise). 4~6번은 매싱 대체 허용(§10-8). 건물 본체 = 매싱 박스 `Prefabs/Kit/Mass_*`(ProBuilder, 모듈러 키트 TBD `Production:57`).

### 층 5 — 절벽/바위 키트 + 게이트 키트 + **일방 낙차턱**
- `Prefabs/Kit/Cliff_{Face8m,Face16m,Boulder2m,Boulder4m,Parapet1m,Sill,DropLedge}`(ProBuilder 블록아웃 → Meshy 교체는 예산 안·비추적·GUID 참조) · `M_InkWorld_Rock` · MeshCollider. `CliffKitScatter`: 경사 ≥`cliffSlopeDeg` 셀(침식 후 림·valley 벽 적용에서만 생긴다 — §7 ④) 시드 지터 순회 · 법선 정렬 · **매입 ≥0.4 m** · 변주 ≥3종 순환(레거시 톱니 반복) · 밑면 노출 검사(C8).
- `Prefabs/Gate/Gate_{Guk,Nun,Mum,Sut,Ung}_<Skin>` — 기하 + `GateSlot`(App: `kind, mode, stepHeight, rewardSlot, skin`). 강토별 **스킨만** 교체(청림 국=수직 절벽 `LDB:38` / 적로 덩굴·철옹 바위 `LDB:39-40`). 런타임 열림 = 프리팹 상태 스왑(A3, Spellcraft 이벤트 채널 구독 슬롯만).
- **일방 낙차턱 `DropLedge`(절벽 상단 루프의 핵심)**: 상단 보상 슬롯 → 뒤편 도보 내리막(≤30°) → 본길 합류 직전에 **수직 턱 h ∈ [0.35, 2.0] m**. 위→아래는 떨어질 수 있고(≤`maxWalkableDropM`), 아래→위는 CC step 0.3·slope 45°로 불가 → 국 없이 상단 도달 = 구조적으로 불가능. NavMesh: 상단 = **별도 섬**(`NavMeshLink`·`OffMeshLink` 0) · 감사 C6 = 「하단→상단 경로 부재 ∧ 상단→하단 낙차 ≤2.0」 동시 성립. S2 인스턴스: `Guk_CliffTop`(Preview → 청룡전 후 Revisit 승격) · `Guk_LoggingTower`(벌목장 적재탑 **8 m** — A16) · `MinePreview`(ID 예약). **본편 경로 위 게이트 0**(`LDB:53,82`) → §6 A1.

### 층 6 — 길 스플라인 + 길 먹
정본 = `AreaSheetSO.paths[].knots` → 빌더가 `SplineContainer`(Catmull-Rom) 생성 → `PathCarver`: 코리도 6 m(중앙 4 m 평탄·어깨 1 m ≤25°) · 종단 ≤15°(램프 ≤30°·≤20 m) · 횡단 ≤5° · 페더 4 m. **보행면 = Terrain 자체**. 표현 = 리본 메시 `Road_<id>`(폭 4 m·지면 +0.03·콜라이더 0·`M_InkWorld_Road` `_AmbientLevel` 0.5 — 「길=여백」 승인 컷 정합, 굽는 마스크 텍스처 0 = D5 무예외). S2: `Trail_Main`(출구→심부 갈림) · `Trail_Stele`(주막→비석 지선) · `Trail_Logging`(마을→벌목장 입구) · 지선 a/b/c/d(§5).

### 층 7 — 개울 `InkRiver` + 여울 규칙 (D6)
`Shaders/InkRiver.shader` = InkWorld 계약(4패스·불투명·ZWrite·DepthNormals 기록 — 투명이면 뒤 배경 거리로 씻기는 레거시 함정) + 흐름 방향 Fbm 먹선(**정적** `_FlowTime` 0, #151-iii; 방향 = 리본 정점 `TEXCOORD1` 스플라인 접선) · Emission 0 · 색 = 무채 램프(현색 탁화는 SPEC-WATER) · `M_InkRiver` `_AmbientLevel` 0.45(물길이 중경에서 밝은 리본으로 읽힘 `LDB:96`). 기하: `StreamCarver` — `Ford` 하상 −0.25 m(≤0.3, CC가 하상을 걷고 리본은 콜라이더 0)·폭 ≤8·양안 ≤20° / `Deep` 하상 ≥1.5 m + **양안 `Parapet1m`(≥0.8 m) 연속 + 여울↔심연 경계 `Sill` 턱 ≥0.6 m 수직면**. 하이트맵은 60°도 표현하나(1.73 m/px) **폭 1~2 m의 둑은 보간으로 뭉개진다** → 여울↔심연 경계·못 둘레는 키트 기하(`Sill`·`Parapet1m`)가 기본. 리본 = 하상 +0.05. S2: `Stream_Geumpyo` 1개, 여울 F1(주막 아래 본길 횡단)·F2(마을 진입)·F3(비석 지선), 심연 D1(주막 동쪽 못 30×20 m — 출구→비석 직행 지름길을 기하로 막고 물소리 인력 슬롯).

### 층 8 — 원경 세트 + 하늘 (상주 씬)
`FarSetBuilder`: 구역 중심 기준 링 4겹(층 1 반경·고각·노이즈) 폴리라인 압출 · 층별 시드 1D 노이즈 · 진경산수 문법(기암 0 `Constitution:23`) · `castShadows Off`·콜라이더 0·LODGroup 0(팝 0) · **겹별 재질 `M_InkWorld_Ridge_L1..L4` `_AmbientLevel` 0.10/0.15/0.20/0.25**(전부 페이드 종점 1170 밖이라도 keep×darkness 항으로 ΔL 분리 — B6 인접 ΔL ≥0.06의 재질 근거) · **압출 하단 Y=0(바닥 −100 — 슬랩 띠 기하 불가)** · 360° 폐합. **신목 노치**: 신목 방위 ±8°에서 L2~L4 높이 ×0.75. 앵커 `Sinmok_Silhouette`(전역 가지 실루엣, `RealmSheetSO.sinmokPosition`, 높이 140 m [TEST]) · `Beacon_Far`(산정, 판 **25 m** [TEST] → 1800 m에서 ≈0.8°, `InkLightSource` `_UseVeinColor 0` → `_OhBeaconColor = GetRawVeinColor('ㄱ')` **목**) · `Beacon_Mid` 슬롯(심부 갈림에서 ≤270 m 중경 인력 `LDB:100` — 원경/중경 2종). 하늘 = 카메라 SolidColor `_OhPaperColor`(A2) · 안개 0 · Buto 미등록. 3단 원근 밴드 재선언 = **0~270 / 270~1170 / 1170~<far**(§6 B3). far 확장 함정: 깊이 소벨이 정규화 깊이를 쓰면 근경 임계가 2.8배 수축 → §6 B4b 패리티. 감사 `RidgeStacking()`: 본선 표본 × yaw 5°에서 각 인접 쌍 L(k+1) 상단 앙각 > L(k) 상단 앙각인 방위 비율 ≥60%(해석적 — 링 함수 재평가, 콜라이더 0).

### 층 9 — 식생 산포 (희소, A4·A15)
`Prefabs/Veg/Tree_Sil_{A,B,C}`(줄기 6 tri + 수관 기하 카드 2~4 tri, 알파 0) · `Bush_Sil_{A,B}` · `M_InkWorld_Veg`(0.25). 마스크: 경사 <35° · 길 코리도 밖 ≥3 m · `exclusionPolys`(주막·역참·마을·비석 패드) · `noVinePolys`(벌목장 입구 반경 60 m — 「쇠 있는 곳 덩굴 없음」 `LDB:55` 선행 학습 씨앗). 시드 고정·간격 4 m·상한 1500. **빛의 뼈대 = 존별 밀도**: Z1 출구~주막 0.2 / Z2 주막~마을 0.4 / Z3 마을~벌목장 0.6 / Z4 벌목장~심부 갈림 0.8(상대 밀도, 협곡 폭·절벽 그늘 병용). 인스턴싱 pragma는 PASS 후 §10-1 — 그 전엔 64 m 셀 `StaticBatchingUtility.Combine`.

### 층 10 — 인력 체인 + 광원
광원 프리팹(지속 발광은 이 4종뿐 `ArtAudio:31`): `Prefabs/Light/Lantern`(주막 등불 — `_UseVeinColor 0` + 백색 Point, 색 = Driver `_OhLanternColor = GetLanternColor()` A13) · `Candle`(성황당 촛불, 소형) · `VeinBand`/`VeinOutcrop`(광맥 — 스파이크 승계, 출구 안쪽·심부 갈림 지선 노두) · `Beacon`(봉수). `attraction[]`·`sightlines[]`는 데이터 — §6 D1이 시야선·각크기·채도·**중경 직선거리 ≤270**을 검증. 우선순위(`LDB:96`) = 광원 개수·`_SourceIntensity`(크리티컬 등불 ×4·1.0 / 이면 촛불 ×1·0.7). 드롭 표식 = 디제틱 `InkPuddle` 슬롯(Combat 구현).

### 층 11 — 씬 구조 (A5)
| 씬 | 내용 | 소유 |
|---|---|---|
| `Scenes/World/W_Cheongrim_FarSet.unity`(**상주**) | `WorldLookDriver`·`WorldScaleDriver`(합산 정확히 1) · Sun (18,160,0) 승계 · Global Volume `VP_InkLook` · 원경 세트·앵커·봉수 · **`RealmLifetimeScope`(부모)** · 리그 1벌(`DevSceneKit.InstantiateRig`) · **귀환 포탈 `DevSceneKit.AddReturnPortal(스폰 뒤 3 m, 리그 방향)`(TEST-HUB 규약)** · `AreaLoader` | 강토 |
| `Scenes/World/W_Cheongrim_GeumpyoRoad.unity`(**Additive**) | Terrain·키트·길·개울·랜드마크·POI/게이트/체크포인트 슬롯·광원·식생·`NavMeshSurface`·`AreaLifetimeScope`(자식, `EnqueueParent`)·`WorldBuildStamp{inputHash, heightsHash(SetHeights 직후 float[,] SHA-256), holesHash, builderVersion, utc, cuts[]}`·`AreaTransitionVolume`(심부 방향 — `AreaIdEventChannelSO` 발화만, 리셋 훅 0 A8) | 구역 |
| 진입 | `C1_TestHub` 포탈 → FarSet(Single) → `AreaLoader.LoadAsync(areaId)`(UniTask Additive) → 리그를 `pois[MineExit]` yaw 지정 | Dev |
싱글턴 0 · 전역 정적 상태 0 · Build Settings = `DevSceneKit.RegisterBuildScenes` + `SaveAssets` 플러시.

### 층 12 — NavMesh
`NavMeshSurface`(ai.navigation 2.0.10) 구역 씬 루트: 에이전트 `Humanoid_Oheangbu`(r 0.5·h 2·slope 45·step 0.3 = CC 미러) = **P0 chore로 Navigation 창 등재**(`ProjectSettings/NavMeshAreas.asset` 커밋) → 빌더는 `NavMesh.GetSettingsCount/GetSettingsByIndex/GetSettingsNameFromID`로 이름→`agentTypeID` 조회(없으면 FAIL string) · 수집 = 레이어 `WorldGround`(Terrain·키트·랜드마크 `_col`·게이트 기하), 식생·리본 제외 · `NavMeshModifierVolume(NotWalkable)` = 심연 · 베이크 = 빌더 단계 `NavMeshBaker.Bake()` · 데이터 커밋(§10-6). **용도(S2)** = 검증(도보 분·섬·게이트 미통과·지선 끝) + 후속(동행 NPC·적 AI) 예약 — 런타임 소비자 0. 레이어(P0 chore, `TagManager`): `WorldGround`·`WorldRidge`·`WorldAnchor`(신목)·`WorldLight`(광원 4종)·`WorldVeg`·`WorldRibbon`.

## 5. 금표의 길 배치표 (사람 입력 = `AreaSheetSO` 정본 · 전부 [TEST] · 서사 슬롯 기본 공란)

좌표계: 타일 원점 (0,0)~(1024,1024) · 폐광 출구 E0 = 타일 (200, 150) · X 동 · Z 북 · 심부 = 북동 · 황경(상경 가도) = 서(`LDB:30` 청림=東·木 / 상경 가도 동→중앙, `Constitution:21` 오방=문법). 고도 = `floorY` 100 기준 상대. 도보 분 = NavMesh 경로 길이 ÷ 270.

### 5-1. POI · 체크포인트 · 슬롯
| ID | 종류 | 본선 누적 도보(경로 m) | 상대 고도 | 슬롯 규칙 · 근거 |
|---|---|---|---|---|
| `MineExit` P0 | `LM_MineExitCliff`(Hole) + `Mine_Portal` 접합 · 「아침의 강토」 비스타 V1 | 0:00 | +60 | 스폰 그대로 1인칭(컷신 0 §6 D3) `Narrative:180` · 안쪽 `VeinBand`×1 · 스폰 yaw 0(골짜기 축 +Z — 주막 연기 좌중앙·신목 우측) · `MinePreview` ID 예약(A10) |
| `Shrine_Entry` | 성황당「진입로」 + `Candle` | **1:15**(340) | +50 | `LDB:51` · 회수 런 시작점 · 출구→여기 30 m 하강, 스위치백 1 |
| `F1` | 여울 1(본길 횡단) | 1:25 | +45 | 깊이 0.25·폭 8 |
| `Inn_Geumpyo` P1 | 금표 주막(`LM_GeumpyoInnPlatform`) — **첫 주막 = 슬롯 1+2** | **2:25**(≈650 · 범위 540~810) | +25 | D4 · `LDB:50` · `Lantern`×4·연기 기둥 20 m(정적 메시) · 매싱 박스 · 완전 화톳불(`Combat:127`) |
| `Stele_Geumpyo` | 금표 비석(`LM_GeumpyoStele`, 지선 `Trail_Stele` 끝) | 2:55(지선 왕복 1:00) | +30 | `LDB:47,54` 정사 보조 시선 · 여울 F3 횡단 · 지선 끝 보상 = 시선 매체 · 봉산 경계 = Z2→Z3 |
| `Yeokcham_Geumpyo` | **역참**(정담 J1 슬롯 — 관영, `Narrative:187` 「길목」) | **3:20**(≈900, 주막→마을 사이 본선 곁) | +15 | `LDB:56` 별개 항목(A11) · 온기 산입 0(등불 0·촛불 0 — 빛 없는 공영 건물) · `narrativeSlot=""` · 매싱 ×1 |
| `Village_Logging` P3 | 버려진 벌목 마을(`LM_VillagePlatform`, 매싱 ×6~8, 높이 ≤6 m) | **4:40**(1260) | 0 | `LDB:47` · 개울 곁·못 D1 비스타 · 빛 0(온기 없음) · `Gaekju_Village` = 객주 분소(왕소 W1, 마을 건물 1동, A11) |
| `Gate_Guk_CliffTop` | 국 **예고**(Preview) 절벽 상단(`LM_PreviewCliff`+`DropLedge`) | 마을·진입로에서 가시(직선 ≤180 m) | +75 | `LDB:53` 보상 시야 · NavMesh 별도 섬 · 하산 = 뒤편 내리막 → 낙차턱 → 지선 b 합류(재방문 지름길 루프 `LDB:108`) |
| `Shrine_Logging` | 성황당「벌목장 앞」 | **5:25**(1460) | +40 | `LDB:51` · 주막→여기 **3:00 ≤3.5** · 촛불 1 |
| `Entry_LoggingCamp` | 벌목장 입구(소던전 접속 슬롯) + `Gate_Guk_LoggingTower`(8 m) + `noVinePolys` | **5:50**(1580) | +45 | `LDB:48,53,55` · 성황당→입구 **0:25 ≤30 s**(`LDB:109`) · 포탈 = 창고 키트 이월 |
| `F2` | 여울 2 | 6:10 | +50 | — |
| `Shrine_Junction` P7 | 성황당「심부 갈림」 + `AreaTransitionVolume(Depth)` + `Beacon_Mid`(≤270 m 산정) | **7:10**(1940) | +70 | `LDB:51` · 벌목장 앞→여기 1:45 · 다음 구역 예고 = 밀식(Z4 0.8)+신목 최대 · 지선 d = 광맥 노두 `VeinOutcrop` |
| `Slot_Beacon_Far` | 봉수(원경 L2~L3 사이, 목 청록 탁광, 판 25 m) | — | 링 | `LDB:100` · #152 |
| `Sinmok_Silhouette` | 신목 실루엣(원경 앵커, 높이 140 m [TEST]) — **위치 = `RealmSheetSO.sinmokPosition`** | — | 링 2~3 | `LDB:52,69` · 앙각 수치는 표에 두지 않는다 — §6 D2가 V1→Inn→Junction 앙각을 **계산해 단조 증가만 판정**(atan(h/d)) · 노치 규칙 |

지선(끝마다 보상·정보·서사 슬롯 `Constitution:35`): **a** 진입로→절벽 하단 예고 시점 0.6분·물증 슬롯 / **b** 마을→여울 F3→절벽 하산 발치 0.9분·약초꾼 물증(재방문 루프 출구) / **c** 마을→잘린 거목 그루터기 비스타 0.5분·「쇠도끼 자리 덩굴 없음」 예고 / **d** 심부 갈림→광맥 노두 0.6분. 본선 7:10 + 지선 왕복(a 1.2 + b 1.8 + c 1.0 + d 1.2 + 비석 1.0 = **6.2분**) → **S2 총 ≈13.4분**(D3 10~15 안 · C2 기준값). 체크포인트 간격: 진입로 1:15 → 주막 1:10 → 벌목장 앞 3:00 → 심부 갈림 1:45 — 최대 3:00 ≤3.5(A7), LOCKED 성황당 이름 3 불변, 5번째(신목 앞 검토)는 S3 소유.

### 5-2. 빛의 뼈대 (`LDB:57` 밝음→어둑 · 구현 = 식생 밀도·절벽 그늘·계곡 폭, A15)
| 존 | 구간 | 식생 밀도 | 계곡 폭 | 컷 지형 luma 중앙값 목표(포스트 off) |
|---|---|---|---|---|
| Z1 | 출구~주막 「아침 골짜기」 | 0.2 | 넓음(트인 사면) | 0.55 |
| Z2 | 주막~마을 「밝음」 | 0.4 | 개울·평지 | 0.50 |
| Z3 | 마을~벌목장 「어둑 시작」 | 0.6 | 좁아짐 | 0.40 |
| Z4 | 벌목장~심부 갈림 「어둑」 | 0.8 | 협곡·절벽 그늘 | 0.30(→심부 0.10 예고) |
`M_InkTerrain._AmbientLevel` 0.35 단일 재질(타일 1장 = 재질 1개). Directional (18,160,0) 무변경.

### 5-3. 인력 체인 — 시점별 1순위 (눈높이 1.8 · 방위 = 북 0° 시계 · 크기 @1080p·FOV 60 ≈18 px/°)
| 시점(yaw) | 1순위(층) | 방위/직선 | 기대 크기 | 2순위 | 배경 규칙 |
|---|---|---|---|---|---|
| V1 MineExit(골짜기 축) | **주막 등불 ×4 + 연기 20 m — 원경 밴드(560 m > `midAttractionMaxDistance` 270) · #151-ii 상등 광원 마커 면제로 성립(B10 계측 대상)** | 축 −8° / 560 | 등불 군집 ≈10 px·연기 ≈2° | 중경 1순위(≤270 m 보증) = 여울 F1 밝은 리본 + Shrine_Entry 촛불(직선 ≤270 m — knots 제약) · 신목 실루엣(원경 ≈1300 m) · 봉수 | 등불 배경 = 능선 먹(하늘 아님) luma ≤0.5 |
| Shrine_Entry | 주막 등불(중경 260 m) | / 260 | ≈14 px | 여울 F1 밝은 리본 | — |
| Inn 앞 | 비석(근경 135 m, 지선 유혹 — 이면 0.7) vs 물길(본선) | / 135 | 비석 ≈40 px | 역참·마을 실루엣(중경 250·400 m) | 크리티컬(물길) > 이면(비석) §6 D6 |
| Village | **절벽 상단 예고(180 m, 근·중경) → 못 D1 물길** | / 180 | ≈18 px | 적재탑(≤8 m, 고립·이질 `LDB:135`, 300 m = 원경 밴드) · 마을 빛 0 = 어둑 예고 | — |
| Shrine_Logging | 적재탑(근경 120 m) → 입구 문틈 빛 | / 120 | — | 신목 | — |
| Shrine_Junction | **`Beacon_Mid` 청록 탁광**(중경 250 m 오르막) | / 250 | — | 신목 · 노두 색 얼룩(근경 120 m) | 원경 봉수는 씻김 후 채도 ≥80%이되 Mid보다 작게 |
| 지선 a 끝 | 절벽 상단 보상 슬롯(예고 — 보이지만 못 감) | / 180 | ≈18 px | — | 슬롯 색 = **비색**(#168 — 술법 청록과 채도·명도로 분리, 값은 이면 보상 Spec의 팔레트 파생) |
| 전 구간 | 신목(Far, 링 2~3, 노치 규칙) | 1200~1500 m | 본선 표본 ≥70% 가시 | 봉수 Far(≤1800, 채도 보존 ≥80%) | — |

### 5-4. 개울 · 여울
`Stream_Geumpyo`: 상류(심부 갈림 +30) → 마을(0) → 주막 동쪽 못 D1(30×20 m, `Parapet1m` 링 + `Sill` 양끝) → 하류(타일 남동 경계 −5, 경계 띠 안에서 심연으로 사라짐). 여울 F1·F2·F3 폭 5~8 m. 폭 3~8 m 리본 `M_InkRiver`.

## 6. Validation (사전 선언 · 측정 후 이동 금지 · PNG sRGB · 보고 IMPLEMENTED→VALIDATED→PASS)

도구 = `Scripts/Editor/World/WorldMapAudit.cs`(전부 `public static string`, 1호출=1프레임, `reflection-method-call`) + 캡처 3종: `CaptureFar(x,y,z,yaw,pitch,name,post, float far)`(= `WorldLookAudit.Capture` 오버로드, 기존 서명은 far 1000 유지 — 스파이크 A10 무손상; 월드 컷은 `WorldScaleSO.cameraFar`) · `CaptureDepthR32(pose, far)`(RFloat RT → 선형 눈 깊이 m float32 raw `.r32` — 8-bit 정규화는 far 2800에서 1단계≈11 m라 B4b 0~25 m 밴드 분리 불가) · `CaptureLayerMask(pose, layerName, far)`(포스트 off · cullingMask=해당 레이어 · 배경=센티널 (255,0,255) → 마스크 = 센티널 아님). 픽셀 통계 = PIL+numpy(`Tools/LookAudit/*.py` — 스파이크 P3 산출 우선, 미도착 시 월드 P4 소유). `LookdevWalker`(스파이크 P3) 실보행.

컷 3장(결정적 포즈 — 시트 `cuts[]{id, rule}`): **V1** = `pois[MineExit]` 스폰 위치+눈높이 1.8, yaw 0, pitch 0 · **V2** = `WalkMinutes(Shrine_Entry, Inn_Geumpyo)` NavMesh 경로 호 길이 50% 지점, yaw = 경로 접선, pitch 0 · **V3** = `Shrine_Junction` 위치, yaw = 신목 방위(`RealmSheetSO.sinmokPosition`), pitch 0. 좌표는 빌드 시 `WorldBuildStamp.cuts[]`에 기록되어 이후 이동 금지. 검증 도구 작성 시간은 §8 P1·P4에 산정.

### A. 캐논 게이트
| # | 항목 | 기준 | 도구 |
|---|---|---|---|
| A1 | 청림 본편 게이트 0 | `MineExit→Shrine_Junction` NavMesh 경로가 `GateSlot` 볼륨 0개 통과 · `mode==MainPath` 0 · 예고 슬롯 CliffTop·LoggingTower·MinePreview 존재 · 재방문 국 ≤4 | `CanonGates()` |
| A2 | 보이지 않는 벽 0 | 렌더러 없는 비트리거 콜라이더 = Terrain·`_col`·키트뿐 · 타일 4변 림 안쪽 경계 띠 경사 ≥60° 비율 ≥95% | `InvisibleWalls()` |
| A3 | 점프·등반·수영 0 | 이동 컴포넌트 census · 본길·지선 CC 규격 단일 섬 · 여울 하상 ≤0.3 · 심연 도달 불가 | `NavIslands()`+`Fords()` |
| A4 | 발광 화이트리스트 | 지속 발광 셰이더 = `InkLightSource` 1종 · 인스턴스 = **Lantern/Candle/Vein(Band·Outcrop)/Beacon** 4종만 · 환경 재질(InkTerrain·InkRiver·InkWorld 변종) Emission 프로퍼티 0 | `MaterialGate()` |
| A5 | 색 단일 출처·무텍스처 | 씬·재질 색 저장 0 · `_Oh*` = 팔레트 `.linear` · 등불색 = `GetLanternColor()` · **월드 씬 2종(`W_Cheongrim_*`)이 참조하는 재질**의 텍스처 참조 0(엔진 내부 3종·`_OhMarkMask` RT 제외 목록 고정) | `PaletteProbe()`+`TextureGate()` |
| A6 | HUD·인식 무접촉 | Drawing/Core/Combat/Spellcraft git diff 0 · HUD 오브젝트 추가 0 · 월드 씬 8획 작도 픽셀 포스트 on/off 차분 0 | git+`Capture` 쌍 |
| A7 | **Terrain 봉인** | 층 3 봉인 전 항 일치(키워드 포함) · 품질 오버라이드 켜지면 FAIL 표시 · **A7b** Frame Debugger: 1000 m 패치도 `InkTerrain` 패스(베이스맵 폴백 0) · 픽셀 법선 on/off 셀 램프 튐 차분 기록 | `Sealing()`+육안(도구) |
| A8 | 에셋·의존 규약 | manifest 직접 의존 4종 · `TagManager` 레이어 6종 · 에이전트 타입 존재 · Meshy/유료 팩 GUID 참조 0 · Blender FBX 루트 scale 1·rot 0 · 한글 `[Tooltip]` 0 · `*.report.json` 전 PASS | `AssetGate()` |
| A9 | 상태어·미러·경계 | SO·씬 문자열 `LOCKED` 0 · `WorldScaleSO.walkSpeedMps == CombatConfigSO.MoveSpeed` · 런타임 asmdef에 `TerrainData.SetHeights/SetHoles`·Splines 참조 0 · 빌더·carver·Driver 소스의 float 리터럴 중 화이트리스트 {0, 0.5, 1, 2, −1, PI, 1e-4} 밖 **및** SO 필드값 집합과 일치하는 값 0(정규식 grep `\d+\.?\d*f`) · 싱글턴 0 · Driver 합산 정확히 1 | `StatusWords()`+`ScaleMirror()`+grep |
| A10 | **스파이크 계약 불변** | `PlayerRig.prefab` far 1000·FOV 60 무변경 · `C1_WorldLookdev` 컷1·컷2: **같은 커밋·같은 에디터 세션**에서 ① 신선 로드 캡처 → ② `W_Cheongrim_FarSet` 로드·플레이·언로드 → ③ 재캡처 → diff 0(전역 `_OhScaleDriven`·far·페이드 누출 검증) · `M_InkWorldPost.mat` git diff 0 · `InkWorld.shader` PASS 전 diff 0 | `SpikeRegression()`+diff |
| A11 | 결정론 | 같은 시트·시드 2회 빌드 → `heightsHash` 동일 ∧ max|Δh|=0 · 키트/식생 인스턴스 Δ 0 · **감사 시점 `TerrainData.GetHeights` 재해시 ≠ `heightsHash` → 「브러시/외부 편집 감지」 경고** | `Determinism()` |
| A12 | 서사 침묵 | `narrativeSlot` 기본 공란 · POI 텍스트 0(주막 방(榜) 슬롯 1 제외) | census |

### B. 룩 (스파이크 §6 승계 · far 2800 재계측)
| # | 항목 | 기준 |
|---|---|---|
| B1 | 승인 컷 밴드 | **V1만**: 소지 0.163 · 먹 0.423 · 저채도 0.476 · Q 0.10/0.32/0.75 각 ±0.15(스파이크 §6 B1 고정) — V1은 컷2와 같은 「문턱→아침 산수」 구도. V2·V3는 히스토그램 **기록만**(개활 구도라 컷2 밴드 부적용). P0 Recraft 청림 비스타 컷을 예준이 승인하면 **P4 측정 전** V2 밴드로 고정·명기(승인 전 측정 시 V2는 기록) |
| B3 | 3단 원근(재선언) | 깊이 밴드 0~270 / 270~1170 / 1170~<far(하늘 제외, `.r32` 선형 깊이) luma σ 단조 감소 · 최원 밴드 vs 소지 ΔE<16 |
| B4a | 먹선·하늘 가드 | 밴드별 엣지 비율 단조 감소 · far 픽셀 엣지 0 |
| B4b | **far 확장 근경 패리티** | 같은 갱 입구 포즈 far 1000(스파이크 씬) vs 2800(월드) 근경 0~25 m 엣지 비율 차 ≤20% — 실패 → 소벨을 선형 눈 깊이(m)로 재정규화(PASS 후 포스트 수정 문답) |
| B6 | 실루엣 분리 | L4 능선 vs 하늘 ΔL ≥0.12 · 인접 겹 ΔL ≥0.06(`WorldRidge` 마스크 + 겹별 재질) · **신목 상단 vs 하늘 ΔL ≥0.12**(노치 효과) |
| B10 | 인력 면제 | 봉수 Far(≤1800, `WorldLight` 마스크 픽셀 ≥8 전제 — 미달 = FAIL)·주막 등불(560 m) 씻김 후 채도 보존 ≥80%(HSV S, 포스트 on/off 비) |
| B11 | 등불 착색 [신규·미검증] | 등불 영역 HSV S ≥0.30·H 20~40° · 최대 채널 ≤0.97 · 환경 S<0.15 비율 ≥0.70 유지 — 실패 → §10-9 무채 폴백 |
| B12 | **빛의 뼈대** | 본선 30 m 간격 전방 30° 원뿔 지형 luma 중앙값(`WorldGround` 마스크): Z1·Z2 평탄 → 이후 **단조 비증가** · 인접 시점 차 ≤0.03(밴딩 가드) · Z4/Z1 ≤0.55 |
| B13 | **지평선 폐합** | 본선 40 표본 × yaw 5°: **해석적** — 림 앙각 ≤4° ∧ 그 방위의 L1~L4 상단 앙각 max > 림 앙각 ∧ 압출 하단 Y=0 → 슬랩 띠 기하 불가 100% · 원경 렌더러 활성 토글 0 |
| B14 | 이음 먹선 | 랜드마크/지형·키트/지형 접합선(발자국 에지 3 px 대역, `WorldGround` 마스크 경계) 엣지 폭 ≤2 px · 이중선 0 — 정량 / Z-fight = 리본 +0.03/+0.05·스커트 ≥0.5 기하로 회피, **육안(도구)** 항목 |
| C-look | 보행 플리커 | `Trail_Main` 12 웨이포인트 4.5 m/s 실보행: 프레임간 엣지 마스크(`_DebugView 1`) XOR 비율 **≤0.02** [TEST — 스파이크 C가 먼저 선언되면 그 값을 채택, 아니면 본값 고정] · Terrain 패치 LOD 전환 프레임 별도 기록 · 픽셀 법선 on/off A/B |

### C. 통행·스케일
| # | 항목 | 기준 |
|---|---|---|
| C1 | **D4 캐논 시드** | `WalkMinutes(MineExit, Inn_Geumpyo)` NavMesh 경로/270 ∈ **[2.0, 3.0]** · `LookdevWalker` 실보행(4.5 m/s) 오차 ≤10% · 밖이면 **시트 knots 수정**(SO 불변) |
| C2 | 구역 총 도보 | 본선+지선 왕복 ∈ [10, 15]분(기준값 13.4) · 표 5-1 누적 각 ±20% |
| C3 | 경사 | 본선·지선 종단 ≤15°(램프 ≤30°·≤20 m) · 횡단 ≤5° · NavMesh 삼각형 ≤45° · ≥55° 셀 = 키트 매입 또는 경계 |
| C4 | **실수 낙사 0** | 본선·지선 코리도 ±3 m 안 NavMesh 경계 에지 자유 낙하 ≤2.0 m · 치명 에지(자유 낙하 ≥6 m)는 코리도 경계에서 ≥3 m · 낙사 드롭 = 마지막 접지점이므로 회수 런 도달 100%(구조적) · **C4b** 심연 양안 `Parapet` ≥0.8 연속(틈 0) + `Sill` ≥0.6 수직 |
| C5 | 회수 런 상한 | 인접 체크포인트(주막 포함) 도보 ≤3.5분 · 벌목장 앞→입구 ≤135 m |
| C6 | **절벽 상단 일방** | 상단 슬롯 = 별도 섬(하단→상단 NavMesh 경로 부재) ∧ 상단→낙차턱→하단 낙차 ∈ [0.35, 2.0] ∧ `NavMeshLink`·`OffMeshLink`(obsolete) census 0 · 적재탑 상부 동일 |
| C7 | 갈래길 끝 | 차수 1 노드 전부 `PoiSlot`/`rewardSlot` 반경 10 m 안(`Constitution:35`) |
| C8 | 밑면 노출 0 | 키트·랜드마크 발자국 에지 0.5 m 표본, 지면 −0.3 m 상향 레이 — **감사 중 렌더 메시에 임시 비볼록 `MeshCollider`(레이어 `~Audit`) 부착 → 적중 100% → 제거**(`_col` 볼록 껍질은 렌더 메시보다 커서 증명 불가) |
| C9 | 이음 | 발자국 안 \|지면−발자국면\| ≤0.05 m · 블렌드 밴드 밖 하이트맵 불변 · FBX 바운드 = Blender 치수 ±1% |
| C10 | 차량 예약(기록만) | `class==Gado` 0 → SKIP · 규격 필드 존재 확인 |
| C11 | 타일 경계 | 예비 타일 미생성 시 `SetNeighbors` null 안전 · **림 앙각 ≤4°(본선 표본 100%)** · 경계 띠 ≥60° 비율 ≥95% |

### D. 인력 시야선 (무컷신 · 눈높이 1.8)
| # | 항목 | 기준 |
|---|---|---|
| D1 | 체인 링크 가시 | 표 5-3 각 시점(시트 `sightlines`): 1순위 목표 `CaptureLayerMask(목표 레이어)` 픽셀 ≥15(≈0.8°) ∧ 같은 포즈 `WorldGround`+`WorldVeg`+`WorldRidge` 마스크가 목표 마스크 화면 영역을 ≤10% 가림 · 마커 광원 채도 ≥0.30 · **`attraction[]` 중 `layer==Mid`의 fromPoi→toPoi 직선거리 ≤ `midAttractionMaxDistance` 100%(위반 = FAIL, 층 라벨 이동 금지)** |
| D2 | 신목 연속 가시 | `Trail_Main` **20 m** 표본(≈97 캡처) ≥70%에서 `WorldAnchor` 마스크 픽셀 ≥8 · V1→Inn→Junction 앙각 단조 증가(계산) · 원경 겹 차폐 = 해석적 링 함수(콜라이더 0) |
| D3 | **컷신 0** | `PlayableDirector`·`CinemachineBrain`·카메라 트랜스폼 스크립트 0 · V1 = 리그 스폰 포즈 캡처(능선 ≥3겹·하늘 ≥25%·등불 마커 성립) |
| D4 | 예고 게이트 | CliffTop·LoggingTower 보상 슬롯: 지정 시점에서 D1 규칙 가시 ∧ C6 별도 섬 |
| D5 | 물길 인력 | Inn→Village 표본 ≥50%에서 `WorldRibbon` 마스크 가시 · ≤270 m에서 luma 지형 대비 ≥0.08 |
| D6 | 우선순위 | V2·Inn 앞에서 크리티컬 광원 영역 채도·luma > 이면(비석·촛불) 100% |

### E. 성능 (사전 선언 [TEST] · 상대 · 정본=빌드 프로파일 #147)
| 항목 | 기준 |
|---|---|
| tris(V1~V3 최대) | ≤800k(Terrain pixelError 5 ≈200k · 키트 ≤300k · 식생 ≤100k · 원경 ≤20k) |
| drawCalls / SetPass | ≤600 / ≤150(식생 셀 병합 전제, 인스턴싱 후 ≤400 재선언) |
| GPU / 포스트 | @1080p 에디터 스파이크 D 씬 ×≤3(상대) · 포스트 ≤1.2 ms · 빌드 GPU ms 기록 |
| CPU | 메인 ≤6 ms · MeshCollider 쿠킹 합 ≤200 ms(기록) |
| 빌더 | `BuildArea` ≤120 s · NavMesh ≤60 s · 에디터 로드 ≤3 s |
| 에셋 | `TerrainData` ≤8 MB/타일 · NavMesh ≤2 MB · 씬 YAML ≤3 MB(메시 에셋화) |

### F. 저작 단가 (Risk MEDIUM 실측 — 기록 항목, PASS 조건 아님 · 후퇴선 있음)
`AuthoringLog()` → `Tools/WorldRuns/run-<date>.md`: 층별 시간(SO 입력/Blender 랜드마크/빌더 반복/키트/튜닝/**검증 도구 작성**) · Blender 왕복 수 · 빌더 재실행 수 · Meshy 크레딧(기대 0~2). **목표 S2 ≤12 작업일 [TEST] · 15일 초과 시 S2→S1(골짜기+주막) 축소, 잔여는 S3 병합.**

### G. 예준 육안 · 블라인드
「걸을 수 있는 땅이 그림으로도 읽히는가 / 원경이 안개가 아니라 여백인가 / 밝음→어둑이 숲으로 읽히는가 / 신목이 부르는가 / 절벽이 조선의 산인가」 + **슬롯 1+2 블라인드 = 예준 단독**(2026-09-06 결정 — 지시 0·HUD 4만, 스폰→금표 주막 도착·첫 도착 ≤4분·되돌아감 ≤1회 기록. 설계자 본인 판정의 한계는 §13에 기록, 테스터가 생기면 추가 기록).

**PASS = A~E 전수 + G + PR 머지.** F는 기록. 실패 항목은 임계 이동 없이 §13에 기각 기록.

## 7. 파이프라인 · 자동화 (사람 ↔ 자동 인터페이스 = 데이터, `LDB:14`·`Production:46`)

| 층 | 사람(예준) | 자동(빌더·MCP·Blender headless) | 인터페이스 |
|---|---|---|---|
| 스케일·배치 | SO 값 결정(POI·도보 분·인력·시야선·knots·제외 다각형) | 검증 §6 A9·C1·C5·D1 | `WorldScaleSO`·`AreaSheetSO`(인스펙터 — Odin 허용, 런타임 무관) |
| Terrain 본체 | **없음(브러시 미사용 — D8·결정론)** | 합성·침식·carve·컨폼·홀·SetHeights | `float[,]` 코드 |
| 랜드마크·비스타 | **Blender 조각** `LM_*` | check→export→컨폼·이음 검사 | Blender 계약(층 4) |
| 절벽·바위 | 키트 원형 승인(ProBuilder/Meshy ≤2 preview) | ≥55° 매입·변주·밑면 검사 | `CliffKitScatter` |
| 게이트·POI·광원 | 좌표·방향 승인 | 프리팹 배치·섬 검사 | 시트 행 |
| 식생·원경 | 존 밀도·노치 방위 | 산포·압출·봉수 배치 | 시트·`WorldScaleSO` |
| 룩 | 컷 육안·재질 무색 수치 | 계측 A~F | `WorldMapAudit` |
**Terrain 브러시 수정 금지** — 손질은 Blender 메시 또는 시트 knots로만. 브러시로 만진 하이트맵은 다음 빌드에서 덮이며 `heightsHash` 불일치 경고(§6 A11). 브러시 델타 층은 D8 개정 문답 없이는 도입하지 않는다.

**`WorldMapBuilder.BuildArea(string sheetPath) → string`**(EditorTools · 결정론 · 재실행 가능 · 메뉴 `Oheangbu/World/금표의 길 조립`): ① `EnsureManifestDeps` + 레이어·에이전트 타입 확인 ② 시트·스케일·합성 SO 로드 → 입력 해시(SO JSON + 빌더 버전 + `checkReportHash`) ③ `DevSceneKit.NewEmptyScene()` **먼저**(에셋 참조는 뒤 — fileID 0 함정, `run-20260906.md §MCP`) ④ `TerrainSynth.Generate(sheet, synth) → float[,]`(순수 C#·`System.Random(seed)` — `UnityEngine.Random`/`Time` 금지) = RidgedFbm·도메인 워프·`valleyProfile` → **열 침식 16(talus 35°, `erosionMask` = 절벽 예정 셀·경계 띠·림·컨폼 발자국 제외)** → 림·경계 띠 ≥60°·valley 벽 ≥55°(침식 후 적용 — 절벽 셀은 여기서만 생긴다) → P2 시드 스탬프(yaw 0) ⑤ `PathCarver` → `StreamCarver` → `TerrainConform`(마지막 우선) → `Holes` ⑥ `TerrainData` Ensure → `SetHeights`·`SetHoles`(직후 `heightsHash`) → `Terrain.CreateTerrainGameObject` → **`TerrainSealer.Seal`** → `SetNeighbors` → 레이어 `WorldGround` ⑦ `CliffKitScatter.Place` + `Parapet/Sill/DropLedge` ⑧ `SplineContainer` 생성 → `RibbonMesh.Build`(길 +0.03·개울 +0.05, 메시 에셋 `Scenes/World/Meshes/`) ⑨ 랜드마크 인스턴스(GUID→프리팹)·POI/게이트/체크포인트 슬롯·광원 ⑩ `VegetationScatter.Place` ⑪ `NavMeshBaker.Bake` ⑫ `WorldBuildStamp{inputHash, heightsHash, holesHash, builderVersion, utc, cuts[]}` · `MarkAllScenesDirty` · 저장 · `RegisterBuildScenes`+`SaveAssets` ⑬ 요약 string. `BuildFarSet(string realmSheetPath)`: 원경 링·앵커·봉수·드라이버·Volume·LifetimeScope·리그·귀환 포탈·`AreaLoader`.

**Blender 헤드리스(D8)**: 층 4 스크립트 2종 + `Tools/Invoke-LandmarkCheck.ps1` · 결과 JSON을 `LandmarkSeamScan`이 Unity 바운드와 대조(C9) · Blender 미설치 = P3 착수 차단.

**MCP**: 허용 `terrain-create/get/set-size/set-heightmap-resolution/sample-heights/set-neighbors/modify-component`(봉인 대조) · `splines-*`(평가·확인) · `probuilder-*`(키트 원형) · `navigation-*` · `screenshot-*`·`profiler-*` · `reflection-method-call`(빌더·감사) / **금지** Terrain 텍스처 계열 6종 · `tests-run` · `script-execute`(불능) · 대용량 `terrain-set-heights`(in-process `SetHeights`) · 정지→refresh→재생·인스턴스 ID 재탐색(`run-20260830.md §MCP`).

**감사 메서드**(`WorldMapAudit`): `CanonGates` · `InvisibleWalls` · `NavIslands` · `Fords` · `MaterialGate` · `PaletteProbe` · `TextureGate` · `Sealing` · `AssetGate` · `StatusWords` · `ScaleMirror` · `Determinism` · `SpikeRegression` · `WalkMinutes(from,to)` · `SlopeMap` · `DropAudit` · `OneWayLedge` · `Connectivity` · `LandmarkSeamScan` · `HorizonClosure` · `RidgeStacking` · `LumaAlongPath` · `SightlineCheck(poi,target)` · `AttractionOrder(poi)` · `RenderStatsAt(poi)` · `AuthoringLog(layer,hours)` · 캡처 = `CaptureFar`/`CaptureDepthR32`/`CaptureLayerMask`.

## 8. 구현 계획 (게이트 · 후퇴)

| P | 내용 | 게이트 | 후퇴 |
|---|---|---|---|
| 0 | 본 Spec 문답(§12) → 가정 정리 · **chore**: manifest 직접 의존 3종 · `TagManager` 레이어 6종 · NavMesh 에이전트 타입 `Humanoid_Oheangbu`(`NavMeshAreas.asset`) · ECS 패키지(charactercontroller·entities·physics) 제거 · Next 3 스파이크 Exit(autoReferenced·`Assets/Spike/`) · Blender 헤드리스 스모크 + FBX 스케일 조합(루트 scale·fileScale) 실측 → §13 · (선택) Recraft 청림 비스타 ≤1장(참고) | **G0** = 문답 완료 + chore 머지 | 미결 항목 → §12 잔류·슬롯 공란 |
| 1 **구조(S5 · G2 병행 · 셰이더·Terrain 무접촉)** | Data SO 5종 + 채널 + 에셋 4개 · `WorldScaleDriver`·`AreaLoader`·LifetimeScope 2종 · `W_Cheongrim_FarSet` 스켈레톤(원경 링·Driver·Volume·리그·귀환 포탈 — **Terrain 0**) · `TerrainSealer`·`TerrainSynth.Generate` 순수 C# 단위 검증(float[,] 해시만) · `WorldMapAudit` 서명 전수 + A8·A9·A11(합성 해시)·A12 구현 · `landmark_check.py` + 포스트프로세서 + 더미 .blend 1종 왕복 · P0 chore 검증(레이어·에이전트 타입) | **G1** = A8·A9·A11·A12 · Blender 더미 PASS · A10 diff 0 · **AND 스파이크 PASS(D1 — Terrain 인스턴스화는 P2부터)** — 월드 측 6항 **충족(2026-09-07, §13)**, 스파이크 PASS 대기 | 스파이크 「담채 아니다」 → 팔레트 문답 선행, P2 **정지**(구조만 유지) |
| 2 **InkTerrain 이식 실증** | `Scenes/Dev/C2_TerrainPort.unity`: P2 골짜기 heightfield를 Mesh(InkWorld) vs Terrain(InkTerrain) 나란히 · 인스턴싱·픽셀 법선·홀·4패스·인스펙터 수용 · `WorldScaleDriver` far 2800 + 전역 페이드(**`_OhScaleDriven` 인터페이스 반영이 착수 전제** — 미반영이면 정지) | **G2** = 패리티 평균 **ΔE<2·엣지 IoU ≥0.9** · A7·A7b · C-look 지글 · A10 | ΔE 2~5 → 램프 상수 1회 / 인스턴싱 실패 → `drawInstanced=false`(§10-7) / 픽셀 법선 실패 → 정점 법선+지글 기록(Exit Blocking) / ΔE>5 지속 → **옵션 B(메시 청크) 결정 문답** |
| 3 **지형 그레이박스** | 합성(P2 시드)·침식·림·길/개울 carve·립·키트·랜드마크 3종(Blender: 출구 암벽·주막 터·예고 절벽, 나머지 매싱)·컨폼·홀·NavMesh·Additive 전환 | **G3** = C1~C11 · A1~A3 · 예준 보행 「길이 몸에 맞는가」(§13) | C1 밖 → knots 수정 · 낙차 → 키트 난간 · 이음 → 블렌드 6 m 1회 · 단가 예측 초과 → S1 축소 선언 |
| 4 **원경·인력·룩** | 원경 4겹·노치·신목·봉수 2종·등불/촛불·InkRiver·희소 식생·매싱·컷 V1~V3·검증 도구 잔여(캡처 3종·PIL) | **G4 정지컷** = B1·B3·B4a/b·B6·B10·B11·B12·B13·B14 · D1~D6 · 예준 육안(#136) | B 실패 → 스파이크 규약 재검(수치 이동 0, 씬만) · far 2800→2000 1회 · 신목 가림 → 노치 확대 · D1 미달 → 등불 군집/knots |
| 5 **계측·검수·보고** | E 빌드 프로파일 · F 단가 · G 블라인드 · HTML 비교판 · 3단 보고 · PROJECT_STATUS 갱신 · PR · Spatial 승격 후보 표 | **G5 = PASS** | G 실패 → 인력 1단 조정(등불 range·연기·길 여백) 1회 재검, 2회 실패 = 배치 재설계 문답 · E 초과 → 식생 캡↓·pixelError↑ |

작업량 ≈ P1 2일 · P2 1.5일 · P3 3일 · P4 3~4일(Blender 포함) · P5 1.5일 ≈ **11~12 작업일** [TEST — F가 실측, 상한 15]. 크레딧 Meshy ≤2 preview · Recraft ≤1.

## 9. 수치 [TEST] 요지 (전부 SO/재질/프리팹 소유 · 코드 상수 0)
- **스케일** 270 m/분(4.5×60) · 눈높이 1.8 · CC 2/0.5/45°/0.3 · 출구→주막 2.5분(540~810 m) · 본선 ≈1940 m(7:10) · S2 ≈13.4분 · 체크포인트 ≤3.5분(945 m) · 난소 직전 ≤30 s(135 m)
- **far·페이드** far 2800 · 최원경 1800(0.64) · 페이드 **270/1170**(최원경 15%/65%) · keep 0.55 · strength 0.9 · 법선 엣지 140 · 중경 인력 상한 270 · 3단 밴드 0~270/270~1170/1170~far
- **원경 4겹+가장자리** r 900/1200/1500/1800 · 고각 6/7.5/9/10.5°(h 95/158/238/334) · 노이즈 ±25% · 노치 ±8° ×0.75 · 신목 (1450,1400)·140 m · 겹별 Ridge 0.10/0.15/0.20/0.25 · 림 앙각 ≤4° · 봉수 판 25 m
- **Terrain** 1024×300×1024 · 1025 res · pixelError 5 · basemapDistance 20000 · 림 안쪽 경계 띠 40 m ≥60° · 침식 16회·talus 35°(마스크 제외) · Fbm 5옥타브·warp 0.35
- **길** 코리도 6(평탄 4) · 종단 ≤15°(램프 ≤30°·≤20 m) · 횡단 ≤5° · 리본 +0.03 · Road 0.5 · 가도 예약 6 m/≤12°/r12
- **개울** 폭 3~8 · 여울 −0.25(≤0.3) · 심연 ≥1.5 · Sill ≥0.6 · Parapet ≥0.8 · 리본 +0.05 · River 0.45 · `_FlowTime` 0
- **낙차·게이트** 코리도 안 자유 낙하 ≤2.0 · 낙사 임계 6(Combat #166) · 일방 턱 0.35~2.0 · 절벽 ≥55° · 국 단차 5(±2) · 적재탑 8·초가 ≤6(배치값) · 하산 ≤30°
- **랜드마크** ≤8000 tris · 콜라이더 ≤2000 · 스커트 ≥0.5 · 매입 ≥0.3 · 컨폼 ≤0.05 · 블렌드 4 m · 키트 매입 ≥0.4 · 변주 ≥3
- **식생** ≤1500 · 간격 4 · 길 이격 3 · 경사 <35° · 밀도 0.2/0.4/0.6/0.8 · Veg 0.25 · Terrain 0.35
- **광원** 등불 ×4 `_SourceIntensity` 1.0 · 촛불 0.7 · 봉수 Far/Mid · 등불색 = lerp(Wood, Paper, 0.30) · 광맥 = 목 탁화 0.25
- **시야선** ≥0.8°(≈15 px) · 신목 가시 ≥70% · 물길 ≥50% · 채도 보존 ≥80% · 중경 직선 ≤270
- **예산** tris ≤800k · drawCalls ≤600 · SetPass ≤150 · 포스트 ≤1.2 ms · CPU ≤6 ms · 빌더 ≤120 s · S2 ≤12일(상한 15)

## 10. Temporary Exceptions
1. `InkTerrain`이 `InkWorld` 프래그먼트를 **복제**(공유 hlsl 아님) — Reason: 스파이크 PASS 전 InkWorld 무접촉(A10) / Cleanup Gate: 스파이크 PASS 직후 **별도 chore**(예준 2026-09-06)로 `Shaders/InkWorldCore.hlsl` 추출·InkWorld/InkTerrain/InkRiver 통합 + 스파이크 씬 컷1·컷2 픽셀 diff 0 + 식생 `multi_compile_instancing` 추가 / **Exit Blocking**(램프 코드 2벌 잔존 금지)
2. `Oheangbu.EditorTools.asmdef` +`Unity.AI.Navigation`·`Unity.AI.Navigation.Editor`·`Unity.Splines`·`Unity.Splines.Editor`·`Unity.ProBuilder` — Editor 전용, 런타임(App·Data) 참조 0 / Exit Blocking 아님. 런타임 스플라인이 불가피해지면 App 참조 추가 = Exit Blocking 격상. `Unity.TerrainTools.Editor`는 참조하지 않는다(패키지 직접 의존은 MCP terrain-* 안정성 사유로만 유지)
3. `PlayerRig.prefab` far 1000 무변경 · `WorldScaleDriver`가 런타임에 `farClipPlane` 덮어씀 — Reason: 스파이크 §2 계약 보존(D7 재범위) / Cleanup Gate: PASS 후 스파이크 §2 문구 갱신·리그 far 소유 일원화 문답 / Exit Blocking 아님
4. 포스트 전역 페이드(`_OhScaleDriven`) 분기 = **스파이크 P2 작업 항목으로 예준 승인(2026-09-06)** → 폴백 불필요가 기본. 스파이크 P2가 늦으면 임시 폴백 = **플레이 모드 한정**(`Application.isPlaying`) Driver가 FSPRF 재질 프로퍼티를 기입하고 OnDisable에서 원값(90/420) 복원 — 에디터 모드에서는 기입 0 · A10에 `M_InkWorldPost.mat` git diff 0 추가 / Gate: 스파이크 P2 머지 시 전역 경로로 교체(**월드 P2 착수 전제**) / Exit Blocking 아님
5. `QualitySettings.terrainQualityOverrides`는 현재 **0(미적용)**(`QualitySettings.asset:106-107` — terrainPixelError 1은 죽은 값). 빌더는 `Terrain.heightmapPixelError=5`; A7은 「오버라이드가 켜지면 FAIL 표시」 가드만 / Gate: E 실측 후 PC 프로필 값 확정(chore)
6. `TerrainData`·NavMesh·리본 메시 바이너리 커밋(LFS 없음) — Reason: 결정론이라 재생성 가능하나 리뷰·씬 열림 편의 / Cleanup Gate: 타일 ≥3장 시 LFS 또는 「빌드 산출물 미추적+빌더 필수」 재결정
7. Terrain `drawInstanced=false` 폴백 — Reason: TerrainInstancing 이식 실패 시 / 채택 시 E 재선언·정점 법선(키워드 해제) / Gate: 재시도 1회 / Exit Blocking 아님(기록)
8. 랜드마크 4~6번(비석·마을 터·심부 갈림 터)·건물 = ProBuilder 매싱(`Prefabs/Kit/Mass_*`) — Reason: 1인 병목·모듈러 키트 TBD(`Production:57`) / Gate: 키트 Spec 착수 시 교체
9. 등불 색 `_OhLanternColor` 파생(A13) 미검증 — B11 실패 시 등불 = 무채 강도만 폴백 / 채택 = 팔레트 `GetLanternColor` 신설 문답 / Exit Blocking 아님
10. `Tools/Blender/blender_path.local`·`BLENDER_EXE` 로컬 경로(gitignore) — 머신별 / Cleanup 없음
11. 절대 프레임·HW 미기록(상대 예산만) — Gate: Production 성능 목표 신설 시 절대치 병기
12. (승계) asmdef `autoReferenced` → **P0 집행(2026-09-06)**: Core/Drawing/App/BrushRender `autoReferenced=false` · `Assets/Spike/`는 `Tools/SpikeArchive/Assets_Spike/`로 이동(S4 승격 원본) — 해소. Buto 임베디드·미등록 유지 — 기록만
13. `TerrainSealer`의 봉인 상수(alphamap 16·basemap 16·basemapDistance 20000·pixelError 5·treeDistance 0)는 `[SealingConstants]` 표시 클래스 안의 리터럴로 둔다 — Reason: 봉인은 Spec 상수(§4 층 3)이지 튜닝값이 아니다 / A9 `StatusWords`는 이 클래스를 **명시 출력하며** 건너뛴다(무음 제외 금지) / Cleanup Gate 없음 · 다른 리터럴은 전부 SO(RealmSheetSO 원경 정책 필드) / Exit Blocking 아님
14. `WorldMapAudit.CaptureFar`가 `WorldLookAudit.Capture`의 복제(카메라·RT 설정 2벌) — Reason: 스파이크 PASS 전 `WorldLookAudit` 무접촉 / Cleanup Gate: §10-1 램프 통합 chore 때 `WorldLookAudit.Capture(far)` 오버로드로 위임 / Exit Blocking 아님
15. 오프라인 컴파일 헬퍼 `Tools/Build/Compile-Assembly.ps1`(MSBuild + Unity csproj 복제)와 `Library/ScriptAssemblies` dll 수동 갱신 — Reason: MCP 브리지 단절 세션의 검증 대체 / Gate: 브리지 복구 시 `assets-refresh`가 정본(csproj 재생성·컴파일) → **Gate 통과(2026-09-07)**: csproj 재생성·컴파일 클린(에디터 시작 시 stale 컴파일 상태는 `CompilationPipeline.RequestScriptCompilation()`으로 풀어야 했다 — run-20260906 §MCP ⑧). 헬퍼는 단절 대비 도구로 존치 / Exit Blocking 아님

## 11. 위험 (실패 양상 → 조기 신호 → 후퇴)
| # | 위험 | 실패 양상 | 조기 신호 | 후퇴 |
|---|---|---|---|---|
| R1 | **InkTerrain 이식**(URP 에디터 C# [지식]) | 인스턴스 모드 평판 렌더 · LOD 경계 셀 램프 튐 · 홀 미관통 · 인스펙터 재질 거부 · 키워드 미활성 | P2 G2 패리티·A7b(첫 이틀) | drawInstanced off → 정점 법선 → 옵션 B 문답 |
| R2 | 스파이크 G2 기각(「담채 아니다」) | 지형 재질 계약 변경 | G1 대기 | P1만 진행, P2 정지 — InkTerrain은 PASS 후라 재작업 0 |
| R3 | **far 확장** | 깊이 소벨 수축 · 타일 안 원근 소실 · L4 씻김 · 슬랩 띠 · 팝 | B4b·B3·B6·B13 | 선형 깊이 재정규화(PASS 후) · far 2000 1회 · 실패 시 far 1000+축소 실루엣 문답 |
| R4 | **Blender 이음** | 루트 100/270° · 밑면 노출 · 접합 이중 먹선 | C8·C9·B14·A8 | 헤드리스 exit 1 · 블렌드 6 m · 스커트 |
| R5 | 절벽 상단 시퀀스 브레이크 | 하산 경사로 국 없이 등정 | C6 | `DropLedge` 턱 높이 재조정(0.35~2.0) |
| R6 | 성능 | 패치 폭증 · 식생 1500 드로우 · 콜라이더 쿠킹 | E · P3 말 첫 RenderStats | pixelError↑·셀 병합·캡↓·원경 −1겹 |
| R7 | 제작 단가(Risk MEDIUM) | 12일 초과 · Blender 학습 지연 | F 일일 로그 · P3 말 예측 | 매싱 대체(§10-8) · S2→S1 축소 |
| R8 | 스케일 체감 오판 | 2~3분이 길다/짧다 | C1·G | knots만 수정, SO 이동 0 · 예준 판정 1회 |
| R9 | 인력 실패 | 등불이 560 m에서 안 읽힘 · 식생이 시야선 가림 · 비석이 크리티컬보다 강함 | B10·D1~D6 | 군집·range·마스크 슬롯 |
| R10 | 원경 겹이 신목·봉수·예고 절벽 가림 · 겹 스태킹 실패 | `LDB:52,69` 위반 · B6 ΔL 미달 | D2·D4·`RidgeStacking` | 노치 확대·앵커 링 고정·고각 재선언 1회 |
| R11 | 봉인 붕괴·패키지 유실 | 브러시 실수 레이어 페인트 · 피처 제거 시 splines 소멸 | A7·A11 매 빌드 · A8 | 감사 되돌림 · manifest 직접 |
| R12 | 낙사·물·리셋 규정 부재 | 후속 Combat 결정과 충돌 | §12 TBD | 지형만 조정(턱·여울 폭) — 코드 0 |
| R13 | 스파이크 계약 회귀 | 전역 페이드가 C1 씬에 새어 듦 · 포스트 재질 오염 | A10(같은 세션 전후 diff) | 씬 격리·OnDisable 복원·플레이 모드 한정 폴백 |
| R14 | NavMesh가 홀·Modifier 오처리 | A3·C6 오판 | 베이크 후 섬 수·홀 주변 표본 | 수동 표본 재검 |

## 12. 미해결 · TBD · 문답 대기
**문답 완료(2026-09-06 2차, 8건)** — ① 낙사 = 사망 ≥6 m·드롭 = 마지막 접지점(#166, Combat 11장 [TEST]) ② 등불 = 파생 lerp(갈색, 소지, 0.30)(#167) ③ 보상 인력 = 비색, 술법 청록과 분리(#168) ④ 역참·객주 별개 POI, 높이 규칙 보류(#169) ⑤ 블라인드 = 예준 단독 · 단가 ≤12/15 · 성능 상대 ⑥ P0 chore 전부 ⑦ 스파이크 P2 `_OhScaleDriven` 요청 승인 ⑧ 램프 통합 = PASS 직후 별도 chore · Recraft 청림 컷 = 참고만(B1은 승인 컷2 고정).

**P1 산출 확인 대기(예준)**: ① 개울·여울 배치 편차 — 단일 개울·하상 단조(심부→마을→못→남동 경계) 제약 때문에 시드 시트는 F1을 주막 아래(하상 −3, 누적 ≈540 m)·F2를 마을 진입·F3를 비석 지선에 뒀다(§5-1 「F1 +45·1:25」 문면과 다름) → 승인 시 §5-1/§5-4 문면 정정, 아니면 knots 재배치 ② `AreaSheet.landmarks[]` 공란(Blender 실물은 P3) ③ `TerrainSynth` 절벽 셀 분류가 침식 전 골짜기 프로파일에서 선행(§7 ④ 「침식 후 valley 벽 ≥55°」와 순서 차이) — P3 착수 시 §7 순서로 정렬 ④ `C1_TestHub` → `W_Cheongrim_FarSet` 포탈 배선·`AreaLoader.LoadAreaAsync` 호출자 = P2 항목 ⑤ 봉수 앵커(`Beacon_Far/Mid`)는 P4 — 시트의 인력 행이 그때까지 미해결 대상 참조 ⑥ 입력 해시의 `checkReportHash`(FBX sha256) = Blender 내보내기 비결정 → P2에서 footprint·tris 해시로 교체 ⑦ `Trail_Stele` 경로 길이 vs walkTarget(P3 `WalkMinutes`) ⑧ **남향 능선 백화**(G1 실측): Sun (18,160,0) 역광 + #153 무 hue 설계에서 남·남동 링 안쪽 면이 태양 정면 → 소지와 동화(yaw 180 컷 지평선 위 비소지 171 px) — V-컷·B13 HorizonClosure는 북향 한정으로 판정하고, 남측 실루엣이 필요하면 링 재질 `_AmbientLevel`/램프 문답 ⑨ 신목 십자면 스탠드인은 서향 면이 태양에 0.33 조명되어 크라운 반쪽이 밝다(luma 0.84) — P3 Blender 실물 교체 또는 P4 V1 판정 때 면 방위 결정 ⑩ `DevSceneKit.RegisterBuildScenes`가 `W_Cheongrim_FarSet`을 빌드 인덱스 0에 넣는다(기존 규약 — 이전 0은 `C1_TestHub`) — 진입 씬 규약은 P2 AreaLoader 배선 문답에서.

**TBD(정본 부재 — 후속 문답·Spec)**: 물 진입·익사 규칙(D6 지형만 — 낙사 규칙과의 동형 여부) · 휴식 리셋 단위(A8) · 세이브 범위·드롭 표식 형태 · 폐광 국 예고 위치·재방문 루프(A10) · 마석 자동차 지위(A1) · 동행 NPC 이동(A9) · 국 게이트 물리 규격(A14) · 지름길 장치(빗장·사다리) vs 기둥 2 — S2는 낙차턱만 · 인간 구조물 높이 규칙(A16, `LDB:101` 수직의 의미론과 함께) · 성황당 5번째(신목 앞 검토, S3) · 환경 담채 열(G2 종속) · 목표 HW·fps 절대치 · 강토 경계 전환(연속 vs 로딩·페이드) · 디제틱 지도 소품 · 밤 가도 vs 시간대 폐기 · Addressables 시점 · 모듈러 건물 키트 착수 · Blender 버전 고정·.blend 추적 정책 · `TerrainData` LFS · 적 AI NavMesh·무리 슬롯 소유 · Spatial Bible 창설 시점.

## 13. 구현 기록 · 육안 검수 절차 (IMPLEMENTED → VALIDATED → PASS · F 공수표 포함)

**착수 전 기입분 — G3 (P3 말 보행)**: 씬 `Assets/_Project/Scenes/World/W_Cheongrim_FarSet.unity` 열고 플레이(C1_TestHub 포탈 또는 직접) → `AreaLoader`가 `W_Cheongrim_GeumpyoRoad` Additive 로드, 스폰 = MineExit. 재조립 = 메뉴 `Oheangbu/World/금표의 길 조립`. 판정 3문 — ① 출구→주막 2~3분이 몸에 맞는가 ② 경사·턱이 「걷는 땅」인가(멈춤·미끄러짐 0) ③ 절벽 상단이 「가고 싶은데 못 가는 곳」으로 읽히는가. 조정 슬롯(코드 아님): 경로 = `AreaSheet_GeumpyoRoad.paths[].knots` · 도보 분 = `walkTargets[]` · 경사 상한 = `WorldScale_Cheongrim.trailMaxGradeDeg/rampMaxGradeDeg` · 턱 = `oneWayDropMinM/maxWalkableDropM` · 재빌드 후 `WorldMapAudit.WalkMinutes("MineExit","Inn_Geumpyo")`로 재계측.
**G4·G (P4·P5)**: 컷 = `CaptureFar` V1~V3 post/raw 쌍 + HTML 비교판(승인 컷2 ↔ V1) · 블라인드 = 지시 0·HUD 4·스폰→주막 도착 시간·되돌아감 횟수 기록. 판정 5문은 §6 G.

**P1 IMPLEMENTED (2026-09-06, 오프라인 — MCP 브리지 단절)** — 워크플로 3회(구현 6 에이전트 → 리뷰 7건 → 수정 2갈래 → 검증):
- Data(`Scripts/Data/World/`): `RealmId`·`WorldEnums`(PathClass·StreamSegmentKind·PoiKind·CheckpointKind·GateKind·GateMode·GateSkin·AttractionLayer·AttractionSource·ConformMode·ColliderMode)·`WorldScaleSO`(44 필드 + 파생 속성)·`RealmSheetSO`(+원경 정책 필드 8·`fadeEndTint` enum)·`AreaSheetSO`(중첩 레코드 15종)·`LandmarkMeshSO`·`TerrainSynthSO`·`AreaId`·`Events/AreaIdEventChannelSO` · 에셋 `Data/World/{WorldScale_Cheongrim,RealmSheet_Cheongrim,AreaSheet_GeumpyoRoad,TerrainSynth_GeumpyoRoad}.asset`·`Events/EC_AreaId.asset`(전부 손으로 쓴 YAML — **에디터 역직렬화 확인 대기**). 시드 시트 산술: 본선 누적 §5-1 대비 −2~−14%(±20% 안), 출구→주막 637 m = 2.36분, 최대 경사 13.8°, Mid 링크 직선 ≤264 m, S2 총 ≈11.8분.
- App(`Scripts/App/World/`): `WorldScaleDriver`(far = 플레이 모드 한정, 전역 float 5종 + `_OhScaleDriven`)·`AreaLoader`(UniTask Additive)·`RealmLifetimeScope`·`AreaLifetimeScope`(EnqueueParent)·`PoiSlot`·`GateSlot`(Closed/Open 스왑)·`AreaTransitionVolume`(채널 발화만)·`WorldBuildStamp{inputHash,heightsHash,holesHash,builderVersion,utc,cuts[]}`.
- EditorTools(`Scripts/Editor/World/`): `TerrainSynth`(순수 C#·System.Random·SHA-256 heightsHash)·`TerrainSealer`(봉인 전 항 + `[SealingConstants]`)·`FarSetBuilder`(링 4겹 압출·노치·신목 실루엣·해석적 `RingTopElevationDeg`)·`WorldMapBuilder`(`BuildArea` P1 = 의존 확인·시트 검증·합성·입력 해시(온디스크 YAML) / `BuildTerrainP2` 게이트 잠금 / `BuildFarSet` = NewScene 먼저 → 에셋 생성 → **경로 재로드** → 배선)·`ManifestDeps`·`WorldMapAudit`(전 메서드 서명 + A8·A9(리터럴·런타임 소스·드라이버 수)·A11·A12·CaptureFar 구현, 나머지 NOT_IMPLEMENTED(P<n>))·`LandmarkMeshPostprocessor`(UCX만 convex, `_col` 비볼록 ≤2000). asmdef +Navigation·Splines·ProBuilder(TerrainTools.Editor 제외).
- Blender: `Tools/Blender/landmark_check.py`(계약 21항 검사·FAIL=exit 1 무출력)·`make_dummy_landmark.py`·`Tools/Invoke-LandmarkCheck.ps1` · `Art/Blender/LM_Dummy.blend`(리포 루트) → `Assets/_Project/Art/Models/Landmarks/LM_Dummy.{fbx,footprint.json,report.json}` **왕복 PASS**(192 tris, 21/21).
- 검증(오프라인): `Compile-Assembly.ps1` Data·App·EditorTools **COMPILE_OK ×3**(오류 0) · 리뷰 위반 7건 전부 수정 확인 · 91 파일 BOM 0·LF·meta 전수·GUID 중복 0.
- ~~에디터 확인 대기(브리지 복구 후 첫 세션)~~ → **G1 에디터 확인 완료(2026-09-07 — 아래 기록)**.
- 관찰: Blender FBX 내보내기는 바이트 비결정(sha256이 실행마다 다름) → `checkReportHash`를 입력 해시에 넣으면 재내보내기마다 스탬프가 갈린다 — P2에서 footprint·tris 기반 해시로 교체 결정 · `Trail_Stele` 경로 0.91분 vs walkTarget 0.5±0.1(직선 하한만 검사) → P3 `WalkMinutes` 감사에서 knots 재조정.
**G1 에디터 확인 기록(2026-09-07, MCP 브리지 복구 후)** — 월드 측 조건 전부 충족, **G1 = 스파이크 PASS 대기(AND)**:
- 컴파일: 에디터 시작 시 Bee `MovedFromExtractor`(MCP Animation dll) 단계 실패로 `scriptCompilationFailed=true`·리로드 거부(새 타입이 리플렉션에 안 보이고 SO `assets-get-data` "Asset not found") → `CompilationPipeline.RequestScriptCompilation()` 강제 → 클린. csproj 재생성으로 §10-15 Gate 통과.
- SO 역직렬화: `WorldScale_Cheongrim`·`RealmSheet_Cheongrim`(`element` char ㄱ=12593·중첩 배열·참조)·`AreaSheet_GeumpyoRoad`·`TerrainSynth_GeumpyoRoad`·`EC_AreaId` 전부 OK.
- 감사: **A8 AssetGate PASS**(직접 의존 4·레이어 6·에이전트 1479372276·셰이더 한글 Tooltip 0·report PASS·FBX 루트 scale (1,1,1)/rot 0) · **A9 StatusWords PASS**(LOCKED 0·float 리터럴 0/14파일·런타임 소스 0/17·`[SealingConstants]` 명시 skip·Driver census Look 1+Scale 1 = FarSet 로드 시) · **A9 ScaleMirror PASS**(4.5 m/s·270 m/분) · **A11 Determinism PASS**(1025²·hashA==hashB `3ce5c9c6…`·maxAbsDelta 0·1.50/1.46 s) · **A12 NarrativeSilence PASS**(pois 15·slots 0).
- Blender 왕복: `LM_Dummy.fbx` 재임포트 → `Data/World/Landmarks/LM_Dummy.asset` 생성(meshGuid = FBX GUID `c4b78c4f…`·footprint 4점·groundOffset 0·contract 1·reportHash) — `OnPostprocessAllAssets` 경로(P1의 `delayCall`은 임포트 직후 리로드에 유실 → 정정) · 콜라이더 오브젝트(`_col`/`UCX_`)는 재질 계약 대상에서 제외(Blender 무재질 → Unity 'No Name' 오탐 정정).
- `BuildFarSet` OK: rings 4·sinmok (1450,1400)·farSetRoot (512,0,512)·spawn (200,161.1,150)·yaw 0·**nullRefs 0**·리그 단일 · 씬 YAML `_*: {fileID: 0}` 0 · 메시 에셋 5(`Scenes/World/Meshes/`, 재빌드 시 GUID 안정) · 재질 6(`M_InkWorld_Ridge_L1~4/Sinmok/Rock`) · 콘솔 오류 0 · `EditorBuildSettings` 등재.
- `BuildArea` P1 OK: tile 1025² · inputHash `e561820a…` · heightsHash `3ce5c9c6…`(A11과 동일) · cliffCells 12,949 · erosionMaskCells 168,338 · 1.6 s.
- 플레이 왕복(FarSet): 예외 0 · Main Camera far **2800**·FOV 60·배경 소지 · `_OhScaleDriven` 1 · 페이드 268.8/1164.8 m(= 0.15/0.65 × L4 반경 1792) · 언로드 후 `_OhScaleDriven` 0.
- **A10 수동 실행(`SpikeRegression()` 자동화는 P2 그대로)**: 같은 세션에서 `C1_WorldLookdev` 컷1·컷2 신선 캡처 → FarSet 로드·플레이·언로드 → 재캡처 → **diff 0 픽셀 ×2** · `PlayerRig.prefab` far 1000·FOV 60 git diff 0 · `InkWorld.shader` 09-06 02:03 이후 무변경(`InkWorldPost.shader`·`M_InkWorldPost.mat`은 스파이크 P2 미착수라 부재).
- **G1 실측 결함 2건 수정(`FarSetBuilder`)**: ① `AddQuad` 감김 판정 반전 — Unity 앞면 법선 = `cross(b−a, c−a)`(내장 Quad 0,1,2 → (0,0,−1)로 확인)인데 같은 쪽일 때 뒤집어 링 앞면이 바깥을 향함 → 안에서 전부 컬링(첫 컷 지평선 위 비소지 픽셀 **0/864,000**) → 수정 후 239,267 ② 신목 십자면 양면이 한 법선을 공유해 태양 정면 조명 → 소지색으로 하늘에 묻힘 → 면별 정점·법선 분리. 오프라인 컴파일은 둘 다 못 잡는다 — 첫 렌더 컷이 잡았다.
- 원경 첫 실측(raw·post 0·far 2800·`Screenshots/World/g1_farset_spawn*_raw.png` gitignore): 북향(yaw 0) 링 L1~L4 luma 중앙값 **0.225/0.323/0.327/0.433**(ridgeAmbientLevels 0.10/0.15/0.20/0.25 — L2·L3 분리 약함, P4 튜닝 항목) · 남향(yaw 180) 링 = 태양 정면 → 소지 동화(지평선 위 비소지 171 px) · 신목 크라운 = 남향 반쪽 먹·서향 반쪽 0.84 → §12 ⑧⑨⑩.
**P0 스모크 기록(2026-09-06)**: Blender 5.0.1 헤드리스 `Tools/Blender/smoke_fbx_scale.py` — METRIC·scale_length 1.0·1 m 큐브 → `FBX_SCALE_UNITS`+`bake_space_transform` 내보내기 OK(11,756 B, `Tools/Blender/_smoke/` gitignore). Unity 반입 실측(2026-09-07, `LM_Dummy.fbx`): 루트 scale (1,1,1)·rot (0,0,0)(A8 AssetGate) · 임포터 useFileScale 1·globalScale 1 — `FBX_SCALE_UNITS`+`bake_space_transform` 규약 확정(이 P0 세션은 브리지 단절이라 컴파일은 `Tools/Build/Compile-Assembly.ps1`(MSBuild 오프라인)로 대신했다).
**P0 chore 기록(2026-09-06)**: `manifest.json` 직접 의존 +terrain-tools 5.3.2·splines 2.8.4·probuilder 6.0.8 / −charactercontroller 1.4.2(entities·physics 동반 제거 — `_Project` 참조 0 실측) · `TagManager` 레이어 6~11 = WorldGround·WorldRidge·WorldAnchor·WorldLight·WorldVeg·WorldRibbon · `NavMeshAreas.asset` 에이전트 `Humanoid_Oheangbu`(ID 1479372276, r 0.5·h 2·slope 45·climb 0.3) · 스파이크 Exit = `Assets/Spike/` → `Tools/SpikeArchive/Assets_Spike/`(git mv, S4 벤치마크 승격 원본 보존) + Core/Drawing/App/BrushRender `autoReferenced=false`(Assembly-CSharp 잔여 = Aura 2·Hierarchy Designer·TutorialInfo — 우리 어셈블리 미참조 실측). 패키지 해석·컴파일의 에디터 측 확인은 브리지 복구 후.

## 14. 승격 절차 (PASS 후)
`WorldScaleSO` 실측치(도보 분·m·far·페이드·능선 반경·체크포인트 간격) → **Spatial Placement Bible 창설 입력**(EA 실측 동반 `BIBLE_INDEX:63-64`, LOCKED 판정은 그곳에서만) / Terrain+Cliff 하이브리드·봉인 목록·InkTerrain 계약 → PROD-AIASSET 「착수 시 승격」 문면 확정(DECISIONS) / 성능 예산 → Production 성능 목표 신설 / B3·B6·B10·B13 통과분 → SPEC-ART-WORLD-LOOK(스파이크 §14 합류) / 가이드 §10 경사 3분류·§21/§25 겹수 → 통과 시 승격, 실패 시 원천 잔류(#157).
