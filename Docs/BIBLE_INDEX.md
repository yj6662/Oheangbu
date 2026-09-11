# BIBLE_INDEX — 오행부 문서 라우팅 인덱스

모든 작업의 진입점. **문서의 동적 상태(버전·생성 여부·정본 규칙)는 이 파일이 단독 소유한다** — 헌법 7.1은 역할·권한만 규정(2026-08-17 개정). 라우팅: Task → BIBLE_INDEX → 해당 Bible의 Section ID → (필요 시) DECISIONS 이력 → Spec → Code.

## 술식 VFX120 체크포인트 [TEST]

- [범위·표현 계약](Specs/SPEC-SPELL-VFX120.md) · [검증·미완료 항목](Art/SpellVFX120/CHECKPOINT.md) · [미술 방향](Art/SpellVFX120/VISUAL_DIRECTION.md).

## 규약

- 어휘 분리: **내용 상태(State) = 5종**(LOCKED/TEST/PROPOSED/TBD/LEGACY — 헌법 CONST-STATUS) / **파일 생명주기(Lifecycle) = ACTIVE·PLANNED·ARCHIVED.** 혼용 금지.
- 동적 숫자(행수·진행률)는 INDEX에 기록하지 않는다 — 드리프트 방지. 정본은 각 파일.
- Section ID는 장 단위 고정 ID — 장 번호가 바뀌어도 ID는 불변. 참조는 ID 우선.
- 전이(상태 변경) 기록은 DECISIONS.md 단독 소유.
- 향후 규약(예고 — 아직 미시행): 고의존 섹션의 Requires/Affects 메타데이터 블록 · TBD 총람의 ID 링크화(자동 생성) · Implementation Spec의 /Specs 분리 · Narrative_Workbench 분리.

## Core

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

### C02 리깅·동작·얼굴 독립 실험 — Specs/SPEC-C02-RIG-FACE-LAB.md
- Lifecycle: ACTIVE · State: TEST
- Owns: 사용자가 후속 대상으로 선택한 C02의 몸·의복 개선 → 동작/제한된 보조 움직임 → 기본 얼굴 기능 → 표정·별도 Unity 통합 검사. 근거=PROD-RIG/PROD-AIASSET, DECISIONS #188. C02 선택은 품질 승인이나 정본 채택이 아니다.
- 이번 실험만 이전 2회 보정·국부 재구성/보조 본 금지·얼굴 제외를 사용자 지시에 따라 대체한다. 추가 유료 0, 단계별 A4/B3/C3/D3, 새 의복 변형 본 ≤12, 실제 착의 전신 ≤60k삼각형. 세부 계약은 해당 Spec이 소유한다.
- 산출물: `Art/PlayerPhase1/C02_RigFaceLab/` 및 별도 Unity 검수 폴더. AutoPlayerV1/C02 원본·판정·비용, FitRigV3·C2·공용 PlayerRig는 보존한다. 항목별 실제 검수 결과와 사용자 미술 판단을 구분한다.

## Data

### 작도 어휘 — 오행부_작도어휘_v0_1.csv
- Lifecycle: ACTIVE · **글자 효과의 유일 정본** · 120행 전량 LOCKED(배정 100 + 공백 20 — 공백은 "배정하지 않기로 결정된" LOCKED 상태, 재배정은 개정 절차)

### 분기 원장 — 오행부_분기플래그원장_v0.3.csv
- Lifecycle: ACTIVE · State: TEST
- Note: 일부 행 LEGACY(제3세력 플래그) — v0.4 개정 대기(FLAG_공작_인지 신설·피날레 변주 세트·엔딩표 4→3). **원본 CSV 미업로드 — v0.4 개정 착수 전 업로드 선행**(헌법 부록 A 잔여 이관 대기와 연동)

## 참고 자료 (원천 — 확정분만 바이블 승격)

- 참고_오행부_맵표현생성가이드.md → Art & Audio · Production 원천
- 참고_codex_리깅애니메이션대화.md → Production 원천
