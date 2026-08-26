# CLAUDE.md — 오행부(五行符) 개발 규약

1인칭 한글 작도 소울라이크 (Unity 6 / URP / C#). 팀 미적(微跡) 1인 개발 + AI 파이프라인.

## 문서 라우팅 (모든 작업의 시작)

1. **진입점 = `Docs/BIBLE_INDEX.md`** — 문서 상태·소유·핵심 Section ID의 유일한 정본.
2. 작업 흐름: **TASK → BIBLE_INDEX → 관련 Bible(Section ID로 참조) → Spec → 기존 코드 → 구현 → Validation.**
3. 참조는 장 번호가 아니라 **Section ID**(예: COMBAT-PARRY, SPELL-BRUSH, LDB-GATING) 우선.
4. 상태어는 5종만: **LOCKED / TEST / PROPOSED / TBD / LEGACY.** LOCKED 변경은 코드로 하지 않는다 — 설계 문답(DECISIONS.md 기록)이 먼저다.
5. **글자 효과의 유일 정본 = `오행부_작도어휘_v0_1.csv`** (UTF-8-BOM, 120행). 바이블 내 글자 표는 열람용 미러 — 수정은 CSV에서만.
6. 모든 설계 전이는 `DECISIONS.md`에 사유와 함께 기록돼 있다 — "왜 이렇게 됐지?"는 여기서 찾는다.

## 아키텍처 절대 규칙

- **금지: 전역 싱글턴 / God Manager / "The Last One" 구조** — 구 문서·구 코드에서 발견 시 폐기 대상.
- DI = **VContainer** (LifetimeScope 단위). 비동기 = **UniTask** (코루틴 금지 방향).
- 통신 = **ScriptableObject 이벤트 채널**. 상태 = 상태머신 패턴.
- 모듈 경계 = **asmdef** — Core는 어디에도 의존하지 않는다.
- **데이터 주도**: 수치·밸런스는 코드에 하드코딩하지 않고 SO/CSV(DTO 임포트)로.
- **Odin Inspector는 에디터/인스펙터 계층 한정** — 런타임 핵심 로직은 Odin-독립. Odin은 유료 에셋: 퍼블릭 리포에 포함 금지.
- MIT 의존성(VContainer·UniTask) 크레딧 고지 의무(릴리스 시).

## 게임 시스템 절대 규칙 (구현 시 위반 금지)

- **인식 불가침**: 작도 인식 로직은 비주얼(갈필·Ribbon Mesh·Noise Cutout)과 완전 분리 — 렌더러 교체가 인식에 영향을 주면 버그다.
- **필세 = 형(정확도) × 세(속도)** — 갈필 등 비주얼 상태는 위력에 불개입.
- **발광 상한**: "빛은 전구가 아니라 먹이다" — 지속 Emission은 인력 광원(광맥·등불·봉수·성황당 촛불)만. 오염은 빛나지 않는다.
- **재화 분리·마석 용어 이원**: 조선통보=유일 화폐 / **마석(원석)**=세계의 광물(연료·소재·먹의 원료) / **오행 마석**=장착 5속성 성장 아티팩트(통보로 강화·등급=성장). 마석류는 화폐 취급 금지.
- **HUD 화이트리스트(4)**: 락온 레티클 · HP · 먹 미터(투명 먹병 액체) · 상호작용 프롬프트. 추가=개헌 사항.
- 피격 보상 금지·응보 허용 / 소환수는 탱킹·어그로 금지 / 그로기 원천 삼원(패링·상합·상극 검격).

## AI 에셋 파이프라인

- Meshy(3D) · Recraft(UI/2D/SVG) · Suno(음악 — 릴리스 음원은 human-in-the-loop 필수). 전부 유료 플랜 상업권.
- 리깅 규격 = Production Bible PROD-RIG: 인간형=표준 Humanoid+리타게팅 / 용=Spline Chain / 기계=강체 부품(무스키닝) / 헝겊=Magica Cloth 2.
- 반복 지형·배치=자동화 대상, 랜드마크·보스 아레나·손맛=사람 영역.

## 문서 맵

| 파일 | 소유 |
|---|---|
| BIBLE_INDEX.md | 라우팅·동적 상태 |
| 오행부_Constitution_v0_1.md | 게임 정의·불변 조항·슬롯·HUD 화이트리스트 |
| 오행부_Spellcraft_Bible_v0_1.md | 작도 문법·필세·인식 철학 |
| 오행부_작도어휘_v0_1.csv | **글자 효과 정본(120행)** |
| 오행부_Combat_Bible_v0_1.md | 전투·적·보스·오상·경제 |
| 오행부_Narrative_Bible_v0_1.md | 세계관·연표·인물·스토리라인 |
| 오행부_LDB_v0_6.md | 공간 문법·게이팅·인력·EA 배치 시트 |
| 오행부_ArtAudio_Bible_v0_1.md | 수묵 룩·색 어휘·발광·UI·국악 |
| 오행부_Production_Bible_v0_1.md | 파이프라인·리깅·도구·스코프 |
| DECISIONS.md | 전이 이력(사유 포함) |

## 현재 개발 단계 · 요청 해석 규칙

- 문서 정본화: **헌법 + 하위 바이블 6권 완료**(Spellcraft·Combat·Narrative·LDB·A&A·Production) / Spatial=PLANNED(EA 실측 동반 착수 — 미창설이 정상 상태).
- 다음 작업 = **스파이크 3종**: ① 작도 인식($1 Unistroke C# 포트 — 최고 위험 가정) ② 붓 획 Ribbon+Noise Cutout ③ 먹 셰이더·한지 머티리얼 → 이후 버티컬 슬라이스=프롤로그 폐광(슬롯 1+2 검수: "아무 지시 없이 색만 따라 첫 주막에 도착하는가").
- **요청 해석 규칙**: 구현 요청 → Spec 작성부터(PROD-PIPELINE — 바로 코드로 가지 않는다) / 설계 변경 요청 → 문답·DECISIONS 기록부터(LOCKED은 코드로 우회 금지) / 모호하면 **기획 검토로 간주하고 확인**한다.
