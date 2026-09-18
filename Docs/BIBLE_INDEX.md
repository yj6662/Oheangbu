# BIBLE_INDEX — 오행부 문서 라우팅 인덱스

모든 작업의 진입점. **문서의 동적 상태(버전·생성 여부·정본 규칙)는 이 파일이 단독 소유한다** — 헌법 7.1은 역할·권한만 규정(2026-08-17 개정). 라우팅: Task → BIBLE_INDEX → 해당 Bible의 Section ID → (필요 시) DECISIONS 이력 → Spec → Code.

## 규약

- 어휘 분리: **내용 상태(State) = 5종**(LOCKED/TEST/PROPOSED/TBD/LEGACY — 헌법 CONST-STATUS) / **파일 생명주기(Lifecycle) = ACTIVE·PLANNED·ARCHIVED.** 혼용 금지.
- 동적 숫자(행수·진행률)는 INDEX에 기록하지 않는다 — 드리프트 방지. 정본은 각 파일.
- Section ID는 장 단위 고정 ID — 장 번호가 바뀌어도 ID는 불변. 참조는 ID 우선.
- 전이(상태 변경) 기록은 DECISIONS.md 단독 소유.
- 향후 규약(예고 — 아직 미시행): 고의존 섹션의 Requires/Affects 메타데이터 블록 · TBD 총람의 ID 링크화(자동 생성) · Implementation Spec의 /Specs 분리 · Narrative_Workbench 분리.

## Core

### SPEC-COMPACT-INK-LANDSCAPE — Specs/SPEC-COMPACT-INK-LANDSCAPE.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 축소 맵 전용 근거리 재료/원경 먹 농담, 지역색20%, 군락을 보존하는 열린 지면 배치와 비교 검증.

### SPEC-COMPACT-BACKDROP-RING — Specs/SPEC-COMPACT-BACKDROP-RING.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 축소본 경계 밖 원경 산 백드랍 기하(콜라이더 없는 미지정 익명 원산)와 지형 동일 셰이더의 백드랍 전용 대기 밴드. 플레이 지형·LDB:99 지배 랜드마크·LDB:124 산군은 범위 밖이며 선점하지 않는다.


### SPEC-DEMO-SUMMON-COMBAT — Specs/SPEC-DEMO-SUMMON-COMBAT.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 데모 전투 소환의 준비/비용/교체, 독립 활동 수명, 이동·공격·외형 시계와 피해 출처 연결. 기존 정적 표현은 보존한다. 실제 연결 종과 미완료 동작은 `../Art/Demo/Summons/REPORT.md`에서 구분한다.

### SPEC-DEMO-FOUNDATION — Specs/SPEC-DEMO-FOUNDATION.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 별도 데모 씬·저장과 단계별 의뢰 이벤트, 적별 그로기/급소창·피해 출처, 통보 정비 거래와 런타임 능력치 연결.
- 승인 범위: `Plans/DEMO-CAMPAIGN-20260915.md`. 최종 모델은 후속 교체하며, 현재 구현과 예약 콘텐츠는 `../Art/Demo/Foundation/REPORT.md`에서 구분한다. 기존 플레이테스트 정적 소환 완료를 전투 AI 완료로 해석하지 않는다.

### SPEC-WORLD-MACRO-PLAYTEST — Specs/SPEC-WORLD-MACRO-PLAYTEST.md
- Lifecycle: ACTIVE · State: TEST
- 외부 테스트 외형 확장: C02 플레이어, 정적 소환 5종의 실제 시전 경로, 산길·청림·황경 외곽 에셋 배치. 실행 계획은 `Plans/PLAYTEST-VISUAL-CORRIDOR-20260915.md`, 결과는 `Art/World/WorldMacro/Playtest/VisualCorridor/REPORT.md`. 본편 해금·소환수 AI 완료로 해석하지 않는다.
- Owns: 별도 매크로 플레이테스트 씬의 폐광 조사·첫 교전·지선·금표 주막 연결, 보행/차량 입력 소유권, 격리 저장과 복구 정책. 실행 근거와 검사 한계는 Art/World/WorldMacro/Playtest/REPORT.md를 따른다. 사용자 보행·실제 입력·비주얼 검수는 미완료이며 기존 Bible 결정 전이가 아니다.

### SPEC-PLAYTEST-UI-AUDIO — Specs/SPEC-PLAYTEST-UI-AUDIO.md
- Lifecycle: ACTIVE · State: TEST
- Owns: `W_WorldMacro_Playtest` 전용 Recraft HUD와 ElevenLabs 효과음의 제작 범위, 런타임 연결 경계, 사용량 원장, 청음·화면 검수 규칙. 생성·변환 산출물과 런타임 검증을 구분하며 결과는 `Art/UIAudio/PlaytestFeedback/REVIEW.html` 및 `REPORT.md`에서 확인한다.

### SPEC-PLAYTEST-MENUS — Specs/SPEC-PLAYTEST-MENUS.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 외부 플레이테스트 타이틀/일시정지/소지품/기록/술식 도감/옵션, 탐험 공개형 미니맵과 펼친 한지 지도, 저장 v2→v3 이행·백업/보관 정책, 다섯 석경 묶음 20글자의 수집·도감 기록 경계. 강화 경제와 본편 해금을 완료한 것으로 해석하지 않는다.

### SPEC-WORLD-MACRO-CONTENT — Specs/SPEC-WORLD-MACRO-CONTENT.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 기존 5강토 지형 위 적·NPC·조사·이벤트·던전의 위치 초안, 세션 내 F 확인, 검토용 모듈과 출처. 실제 전투·퀘스트·보상·저장을 새로 완성한 것으로 취급하지 않는다. 결정 #212 및 Content 검증 보고서를 따른다.

### SPEC-WORLD-MACRO — Specs/SPEC-WORLD-MACRO.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 한양–개성의 지형 관계를 재해석한 5강토 전체 지리, 불규칙한 외곽, 산계·수계·분지, 도시·고개·도하점·가도·도보망과 거친 3D 검토 씬.
- 새 전체 지도는 자연 지형을 먼저 설계한다. 기존 공간 방위·거리·수량·보행 시간·고정 좌표는 지침으로 적용하며 지형을 강제하지 않는다. 약8×12km 참고 규모, 가도14m/s TEST·도보4.5m/s, 조감도·플레이 시점 스크린샷만 제작한다. C2·프롤로그·게임 규칙 보존. 결정 #205를 따른다.

### SPEC-EA-PROLOGUE — Specs/SPEC-EA-PROLOGUE.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 폐광→금표 주막의 분리 제작 씬, 전체 EA 경로 연결 예약, 수묵 Skybox·절제된 후처리, 자연적 경로 유도, 첫 구간 저장·휴식·조사·대화.
- C2 기준 씬은 보존한다. 이번 사용자 결정은 새 제작 씬의 하늘·후처리를 허용하며 기존 단색 씬을 소급 변경하지 않는다. 결정 #204와 Art/World/Prologue/REPORT.md를 따른다.

### SPEC-SPELL-VFX120 — Specs/SPEC-SPELL-VFX120.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 새 술식 VFX 카탈로그·표현 계약·기존 시전 어댑터 연결과 전수 검수. 글자 효과 배정은 CSV, 설계 변경 사유는 DECISIONS #189가 소유한다.
- 제작·미술·런타임 검수 현황은 PROJECT_STATUS 및 Art/SpellVFX120의 실제 보고서로 확인한다.

### CONSTITUTION — 오행부_Constitution_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1
- Owns: 게임 정의 · 기둥 · 절대 규칙 · 안티-비전 · 타깃 경험 슬롯 · 상태 표준 · 바이블 권한 · 용어 헌장 · 스코프
- Key: CONST-DEF · CONST-PILLARS · CONST-RULES · CONST-ANTIVISION · CONST-SLOTS · CONST-STATUS · CONST-BIBLES · CONST-GLOSSARY · CONST-SCOPE · CONST-APPX-A(이관 대기소) · CONST-APPX-B(풍부화 대기)

### DECISIONS — DECISIONS.md
- Lifecycle: ACTIVE · Owns: 상태 전이 이력(사유 포함) 단독 소유 · 서술형 허용

### PROJECT_STATUS — PROJECT_STATUS.md
- Lifecycle: ACTIVE (2026-08-27 창설) · **설계 Authority 없음** — 실행 상태판(Now)
- Owns: 현재 Phase · 마일스톤 체크 · Active Work · Blocker · Next · 최근 검증 요약
- 역할 구분: BIBLE_INDEX=지식 라우터("무엇이 정본인가") / PROJECT_STATUS=실행 대시보드("지금 어디까지 왔나").
  "다음에 뭘 하지?"는 PROJECT_STATUS, 실제 구현은 BIBLE_INDEX → Bible Section → Spec 경로.
- 충돌 시 Spec/Bible/Git 실제 상태 우선. 설계 내용 복제 금지 — 참조만.

## Bibles

### Spellcraft — 오행부_Spellcraft_Bible_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1 · 어휘 완주(배정 100 + 공백 20)
- Owns: 작도 문법(3층) · 어휘 공간 · 필세 정의 · 인식 철학 · 글자 설계 사유
- **정본 규칙: 글자 효과의 유일 정본 = 오행부_작도어휘_v0_1.csv. 바이블 내 글자 표는 열람용 미러**
- Key: SPELL-CHARTER · SPELL-VOCAB · SPELL-GRAMMAR · SPELL-INITIAL · SPELL-PARRY(참조) · SPELL-FINAL · SPELL-COL-G/N/S/NG/M · SPELL-BUFF · SPELL-FIELD · SPELL-BRUSH · SPELL-RECOGNITION · SPELL-TBD · SPELL-LEGACY

### Combat — 오행부_Combat_Bible_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1
- Owns: 전투 문법 · 패링/그로기/상합/갈무리 · 적·보스 설계 · 오상 · 성장/경제/죽음
- Key: COMBAT-CHARTER · COMBAT-DEFENSE · COMBAT-PARRY · COMBAT-GROGGY · COMBAT-INSTALL · COMBAT-ATTACK · COMBAT-HARVEST · COMBAT-ENEMY · COMBAT-BOSS · COMBAT-OSANG · COMBAT-ECONOMY · COMBAT-LEGACY · COMBAT-TBD

### Narrative — 오행부_Narrative_Bible_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1
- Owns: 세계 진실 · 연표 · 세력 · 인물 · 전승 시선(시선들) · 결말 · 스토리라인 · 서사 훅
- Key: NARR-CHARTER · NARR-TRUTH · NARR-TIMELINE · NARR-OHAENGBU · NARR-FACTIONS · NARR-CHARACTERS · NARR-LENSES · NARR-ENDINGS · NARR-STORYLINE · NARR-FINALE · NARR-HOOKS · NARR-TBD · NARR-LEGACY

### Production — 오행부_Production_Bible_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1 (2026-08-17 창설)
- Owns: 개발 파이프라인 프로토콜 · 리깅·구현 규격 · 도구 스택 · AI 에셋 파이프라인 · 스코프 운용(제안 포함)
- Key: PROD-CHARTER · PROD-PIPELINE · PROD-RIG · PROD-TOOLS · PROD-AIASSET · PROD-SCOPE · PROD-TBD

### LDB — 오행부_LDB_v0_6.md
- Lifecycle: ACTIVE · Version: 0.6 (2026-08-17 갈아엎기 신규 창설 — v0.5 전체 LEGACY)
- Owns: 공간 문법 · 게이팅 · 강토 원칙 · 오상/종성 강토 배정 · 필드 게이트 배치 · 보스 아레나 · 체크포인트 배치 문법 · 인력(색의 공간 적용) · 시선 매체 배치
- Key: LDB-CHARTER · LDB-REALMS · LDB-PACING · LDB-GATING · LDB-ATTRACTION · LDB-CHECKPOINT · LDB-HWANGGYEONG · LDB-BOSSPLACEMENT · LDB-EA · LDB-LENSES · LDB-TBD · LDB-LEGACY

### Art & Audio — 오행부_ArtAudio_Bible_v0_1.md
- Lifecycle: ACTIVE · Version: 0.1 (2026-08-17 창설 — 확정 3건 정본화)
- Owns: 수묵 룩 · 색 어휘 · VFX 규칙 · 디제틱 UI · 국악·소리 어휘
- Key: ART-CHARTER · ART-COLOR · ART-INK(발광 상한: "빛은 전구가 아니라 먹이다") · ART-SKY · ART-SILHOUETTE · ART-UI · ART-AUDIO · ART-TBD · ART-LEGACY

### Spatial
- Lifecycle: PLANNED — LDB 실배치 수치 표준. EA 실측과 함께 착수.

## 현재 월드·에셋 구현 라우팅

### 정본 월드 씬 — Specs/SPEC-DEV-CODEX-WORLD.md
- Lifecycle: ACTIVE · State: TEST
- Owns: `Assets/_Project/Scenes/Dev/C2_CodexWorld.unity`의 월드 룩·배치 구현과 검수. 예준이 이후 제작 기준으로 채택한 정본 씬(DECISIONS #170).
- 기존 `C1_WorldLookdev`·Claude 원경 씬은 과거 기술 검증·비교 자료다. 산 농도·면 경계 개선과 미술 판정은 이 Spec에서 이어간다. 현재 원경은 §9의 사실적 산세·공통 재질·실제 거리 대기 방식이다(DECISIONS #172). 바위 전면 교체·Blender 파생 키트·배치 검증은 §10에서 이어간다(#173). 기준 씬 선택은 기존 WORLD-LOOKDEV/WORLD-MAP의 전체 검증 완료나 S2 스케일 완성을 뜻하지 않는다.

### 모델 수령·분류·복구 — Specs/SPEC-ASSET-INTAKE.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 수령 모델 전수 원장·실제 기하 시각 검수·의미 분류·셰이더 복구·씬 배치 후보 기록. 공통 제작 순서=PROD-AIASSET, 결정 근거=DECISIONS #171.
- 산출물 경로: `Docs/Assets/` 원장·보고서, `Oheangbu/Screenshots/AssetCatalog/` Unity 렌더 썸네일. 검사 결과·수치는 해당 산출물과 PROJECT_STATUS에서 확인한다.
- **씬 배치 후보 검색 우선 원장:** [ModelCatalog.reviewed.json](C:/Users/yj666/Oheangbu/Docs/Assets/ModelCatalog.reviewed.json) / [CSV](C:/Users/yj666/Oheangbu/Docs/Assets/ModelCatalog.reviewed.csv). 시각 검토가 반영된 분류·라벨·상태·메모를 읽는다. `ModelCatalog.json`은 자동 감사 원본이며 자동 분류 값은 검토 원장의 `automaticCategory`·`automaticLabel`에도 보존한다.
- 결과와 한계: [AssetIntakeReport.md](C:/Users/yj666/Oheangbu/Docs/Assets/AssetIntakeReport.md). 오프라인 [모델 도감](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/AssetCatalog/index.html)은 실물·다각도 검토용이며 미술 PASS를 뜻하지 않는다.

## Data

### C2 성능 — Specs/SPEC-C2-PERFORMANCE.md
- Lifecycle: ACTIVE · State: TEST
- Owns: C2 성능 계측·렌더/물리 최적화 비교와 제외 조건.150fps는 목표이며 달성 미확인. #183, Art/Performance/README.md 참조.

### 신규 플레이어 1차 — Specs/SPEC-PLAYER-PHASE1.md
- Lifecycle: ACTIVE · State: TEST
- 몸·속상의·두루마기 신규 Meshy7 생성, 공통 골격 피팅과 모의 동작 검증. C2 캡슐 교체는 범위 밖. 산출물: Art/PlayerPhase1.
- 위 분리 제작은 이전 실행 기록이다. 현재 AutoPlayerV1은 아래 별도 Spec으로 라우팅하며 FitRigV3 실패 자료를 보존한다.

### 고정 복장 플레이어 자동 제작 — Specs/SPEC-AUTO-PLAYER-V1.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 최신 외형의 Meshy7 착의 통합 후보 생성·기본 스키닝·실제 동작 선별·장비 파지·별도 Unity 검증. 근거=PROD-AIASSET/PROD-RIG, DECISIONS #187. 이번 실행의 이전 모션 선행 게이트·분리 옷·Cloth 정책 대체와 비용 한도는 해당 Spec이 소유한다.
- 산출물: `Art/PlayerPhase1/AutoPlayerV1/`. 실제 최종 판정은 `FINAL_REPORT.md`·`final_summary.json`, 실행·비용은 각 원장. Body·FitRigV3·C2·공용 PlayerRig를 보존한다.

### C02 리깅·동작·얼굴 독립 실험 — Specs/SPEC-C02-RIG-FACE-LAB.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 사용자가 후속 대상으로 선택한 C02의 몸·의복 개선 → 동작/제한된 보조 움직임 → 기본 얼굴 기능 → 표정·별도 Unity 통합 검사. 근거=PROD-RIG/PROD-AIASSET, DECISIONS #188. C02 선택은 품질 승인이나 정본 채택이 아니다.
- 이번 실험만 이전 2회 보정·국부 재구성/보조 본 금지·얼굴 제외를 사용자 지시에 따라 대체한다. 추가 유료 0, 단계별 A4/B3/C3/D3, 새 의복 변형 본 ≤12, 실제 착의 전신 ≤60k삼각형. 세부 계약은 해당 Spec이 소유한다.
- 산출물: `Art/PlayerPhase1/C02_RigFaceLab/` 및 별도 Unity 검수 폴더. AutoPlayerV1/C02 원본·판정·비용, FitRigV3·C2·공용 PlayerRig는 보존한다. 항목별 실제 검수 결과와 사용자 미술 판단을 구분한다.

### 도사 플레이어·전신 작도 — Specs/SPEC-PLAYER-DOSA.md
- Lifecycle: ARCHIVED · State: LEGACY
- #184: 사용자 요청으로 캐릭터 작업 롤백. 현재 C2·공용 PlayerRig는 캡슐. 아래 소유 범위는 이전 제작 이력이며 새 모델은 새 Spec에서 시작한다.
- Owns: 도사 Humanoid/손 파지 자산, 방향별 이동, 전신 작도와 근접 팔 표현, PlayerRig 배선·검수. V2는 기본 Cloth·보조 본과 제작 모션 전 수치/시각 리그 게이트를 포함한다. 근거=PROD-RIG/ART-COLOR/SPELL-RECOGNITION, DECISIONS #176·#177. 인식·전투 규칙은 기존 Spec이 계속 소유한다.

### 작도 어휘 — 오행부_작도어휘_v0_1.csv
- Lifecycle: ACTIVE · **글자 효과의 유일 정본** · 120행 전량 LOCKED(배정 100 + 공백 20 — 공백은 "배정하지 않기로 결정된" LOCKED 상태, 재배정은 개정 절차)

### 분기 원장 — 오행부_분기플래그원장_v0.3.csv
- Lifecycle: ACTIVE · State: TEST
- Note: 일부 행 LEGACY(제3세력 플래그) — v0.4 개정 대기(FLAG_공작_인지 신설·피날레 변주 세트·엔딩표 4→3). **원본 CSV 미업로드 — v0.4 개정 착수 전 업로드 선행**(헌법 부록 A 잔여 이관 대기와 연동)

## 참고 자료 (원천 — 확정분만 바이블 승격)

- 참고_오행부_맵표현생성가이드.md → Art & Audio · Production 원천
- 참고_codex_리깅애니메이션대화.md → Production 원천

- 2026-09-15 데모 역참·객주·벌목장: [구간 보고서](../Art/Demo/Chapter2/REPORT.md), [검토 화면](../Art/Demo/Chapter2/REVIEW.html). 임시 배우·7단계 연결과 실제 Play API 검사를 전체 데모 완료로 해석하지 않는다.
