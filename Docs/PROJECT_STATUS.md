# PROJECT_STATUS — 오행부

Last Updated: 2026-08-27 (PR #3 merge · 성능/GC 실측 완료)

> **이 문서는 Authority가 없다.** 프로젝트의 실행 상태 스냅샷(Now)일 뿐이며,
> 상태가 충돌하면 해당 Spec/Bible/Git의 실제 상태가 우선한다.
> 설계 내용(게임 규칙·기능 설계·알고리즘·LOCKED 결정)은 여기 쓰지 않는다 — 참조만 한다.
> 과거 이력은 Git history가 정본이다. 갱신은 덮어쓰기(쌓지 않는다).
> 갱신 시점: Spec PASS · PR merge · 마일스톤 변경 · Blocker 발생/해소 · Next 우선순위 변경.

---

## 1. Current Phase

**PRE-PRODUCTION — 기술 리스크 소거(스파이크)**

목표: 작도 입력·인식·표현의 기술 성립을 검증한 뒤 버티컬 슬라이스(프롤로그 폐광)로 진입한다.

## 2. Milestones

### M0 — Project Foundation · **DONE**
- [x] Constitution + 하위 바이블 6권 + BIBLE_INDEX + DECISIONS
- [x] 리포 부트스트랩 · asmdef 10종 아키텍처 · VContainer/UniTask
- [x] 레거시(MandateOfInk) 1급 이식(PDollar·JamoMatcher·템플릿)

### M1 — Drawing Technology · **IN PROGRESS (막바지)**
- [x] 획 기하 이식 — SPEC-MIG-BRUSH-STROKE: **PASS** (2026-08-27 승격 — 잔여 조건 전부 해소)
- [x] 작도 입력 — SPEC-DRAWING-INPUT: **PASS / LOCKED**
- [x] 인식 — SPEC-SPIKE-SPELL-RECOGNITION: **PASS / LOCKED** (최고 위험 가정 해소)
- [x] 붓 획 렌더러 — SPEC-SPIKE-BRUSH-RENDERER: **TEST** (형태 언어 4축 구현·관성 폐기 확정·GC 실측 완료 — 문서 판정만 잔여)
- [x] 먹 룩(셰이더) — SPEC-SPIKE-INK-LOOKDEV: **PASS** (2026-08-27 승격)
- [x] 수묵담채 룩 정본화 — SPEC-ART-INK-LOOK: **PASS** (2026-08-27 승격 — 모티프 소재 보강은 백로그)

### M2 — Vertical Slice (프롤로그 폐광) · **NOT STARTED**
- 검수 기준: 슬롯 1+2 — "아무 지시 없이 색만 따라 첫 주막에 도착하는가"

## 3. Active Work

**NONE** — 룩 스파이크 마감(3건 PASS 승격 2026-08-27). 다음 = SPEC-COMBAT-CORE-LOOP 초안.
비고: 상태 드리프트 자동 검사 도입(Tools/Check-StatusDrift.ps1, SessionStart 훅 — 2026-08-27)

## 4. Blockers

- 없음

## 5. Next

1. **SPEC-COMBAT-CORE-LOOP 초안** — Core Combat 프로토타입의 Spec(작도×전투 첫 결합)
2. 스파이크 Exit 정리 — asmdef autoReferenced 복귀 + Spike 폴더 제거 (**Exit Blocking**, 전투 개발 씬 확보 후)
3. 프롤로그 폐광 그레이박스 → 버티컬 슬라이스

부기(사람 몫): DECISIONS 등재 후보 5건 대기(Q홀드/세·감쇠/불발/피격/패링 판정점 — 기록=예준) · COMBAT-PARRY 주석 등재.

## 6. Deferred

- Spatial Bible → EA 실측 동반 착수(미창설이 정상)
- CSV DTO 임포터 → ElementPalette 수동 미러 대체(INK-LOOKDEV §10 예외 1)
- 필세 위력 공식(Spellcraft) → 플래시 위력 근사 대체(INK-LOOKDEV §10 예외 2)
- ㅇ 템플릿 3→5종 보강(정확도 여지, 비긴급)
- 분기 원장 v0.4 개정 → 원본 CSV 업로드 선행

## 7. Latest Validation

- 인식(3차 측정): 정확도 97.27%(477표본) · 8획 처리 76.56→3.77ms(-95%) · p95 9.57ms — 기준(5ms) 미달이나 예준 수용, 기준 사후 이동 아님(SPEC §19.3)
- 먹 룩: 셰이더 컴파일 클린 · 정점색/갈필/플래시/증발/모티프 스크린샷 검증 · 기능 작동 예준 확인(2026-08-27)
- 성능·GC(에디터 실측): 작도 중 리빌드의 상시 GC 할당 관찰 안 됨(배경치와 동일) · 프레임 평균 2.13ms — SPEC-ART-INK-LOOK §9.4

## 8. Risks

- **HIGH**: 작도 손맛(붓 감각) · Core Combat 재미
- **MEDIUM**: AI 에셋 캐릭터 일관성 · 오픈필드 제작 비용
- **RETIRED**: 한글 작도 인식의 기술 성립(스파이크 ①로 소거)
