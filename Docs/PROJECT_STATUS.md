# PROJECT_STATUS — 오행부

Last Updated: 2026-08-30 (SPEC-SPELL-FX-ASSETS **PASS 승격** · 아트 전면 개편 방침·Bottom 범용화 방향 등재 — DECISIONS #133~135)

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

**SPEC-COMBAT-CORE-LOOP: TEST** — PR #6 머지(검수 1~7차: 잔존 방어막·갈무리 홀드·문양 개화/투사체·
착탄 동기화·불발 통일). 검수 전이는 DECISIONS #128~132로 등재 완료. 잔여 관문 = §6 실플레이 검증 7종·재미 메모
**SPEC-SPELL-FX-ASSETS: PASS** (2026-08-30 승격 — DECISIONS #133) — 공격 작도 모델·VFX 파이프라인 goal 런
완결: P0~P5 전 페이즈(PR #8·#9 머지), 10자 어휘별 시각 분화+갈무리 먹 추출 연출, §6 8항 전수 통과
(결과 표=런 로그 P5), 크레딧 −20(잔액 1006). **단서: 현행 아트=프로토타입 자리표시자 — 릴리스 전
전면 개편 예정(#134). Bottom 문양=바닥 전용 해제·범용 소재 재정의(#135, 갈래 문답 후 Spec)**
**갈무리 먹 추출 연출** — 검수 12차 왕복으로 확정(2026-08-30, 표현 계층·판정 무접촉): 붓 프롭(BrushTip
앵커)+단일 스파인 다발(중앙 주줄기+보조 5, 예준 튜닝 확정치)+생애주기(얇게 뻗음→굵음→얇게 소멸)+
역동화(유동·장력·순단·몸부림)+맥동·촉 맺힘. HarvestAction은 읽기 신호 2개만 노출(프레임 기반 —
오버플로 상시 참 버그·도메인 리로드 침묵 결함 교정 포함)
**카메라 실증 실험(§12)** — 숄더뷰+작도 클로즈업(V 토글, 결정 3의 검증 — 채택 시 DECISIONS 선행). 예준 실플레이 판정 대기
비고: 상태 드리프트 자동 검사 도입(Tools/Check-StatusDrift.ps1, SessionStart 훅 — 2026-08-27)

## 4. Blockers

- 없음

## 5. Next

1. SPEC-COMBAT-CORE-LOOP §6 실플레이 검증 + 카메라 실험(§12) 판정 — 채택/철회/부분채택
2. **Bottom 문양 범용 활용** (#135) — 갈래 후보: 커밋 문양 속성/어휘 분화(1소견)·적 텔레그래프(신규 Spec)·
   인력 공간 문법(LDB)·전투 상태 연출·개화 중첩 확장. 갈래 문답 → **착수는 Spec부터(PROD-PIPELINE)**
3. 스파이크 Exit 정리 — asmdef autoReferenced 복귀 + Spike 폴더 제거 (**Exit Blocking**, 전투 개발 씬 확보됨 — 착수 가능)
4. 프롤로그 폐광 그레이박스 → 버티컬 슬라이스

## 6. Deferred

- **아트 전면 개편**(릴리스 전) — 현행 아트 산출물 전부=프로토타입 자리표시자(#134). 살아남는 자산=구조(팔레트·매핑 SO·표현 계층 분리·임포터 수칙)
- Spatial Bible → EA 실측 동반 착수(미창설이 정상)
- CSV DTO 임포터 → ElementPalette 수동 미러 대체(INK-LOOKDEV §10 예외 1)
- 필세 위력 공식(Spellcraft) → 플래시 위력 근사 대체(INK-LOOKDEV §10 예외 2)
- ㅇ 템플릿 3→5종 보강(정확도 여지, 비긴급)
- 분기 원장 v0.4 개정 → 원본 CSV 업로드 선행

## 7. Latest Validation

- 술식 FX P5 §6 전수(2026-08-30): 발광 상한 정적+런타임 · 그레이스케일 실루엣 판독 · 인식 코드 계층
  증명(변경 전수=표현 계층) · 폴리 동시 15,410 최대(≤30k) · 프레임 4.23ms 최대(1440p·감사 씬 과부하) ·
  게임 런타임 예외 0 — 상세=런 로그 P5
- 전투 검수 3~7차(2026-08-28): 매 라운드 컴파일 에러 0 · 플레이모드 예외 0 · 상태 드리프트 0.
  시각 최종 판정(카메라 채택·문양 룩)은 실플레이 몫
- 인식(3차 측정): 정확도 97.27%(477표본) · 8획 처리 76.56→3.77ms(-95%) · p95 9.57ms — 기준(5ms) 미달이나 예준 수용, 기준 사후 이동 아님(SPEC §19.3)
- 먹 룩: 셰이더 컴파일 클린 · 정점색/갈필/플래시/증발/모티프 스크린샷 검증 · 기능 작동 예준 확인(2026-08-27)
- 성능·GC(에디터 실측): 작도 중 리빌드의 상시 GC 할당 관찰 안 됨(배경치와 동일) · 프레임 평균 2.13ms — SPEC-ART-INK-LOOK §9.4

## 8. Risks

- **HIGH**: 작도 손맛(붓 감각) · Core Combat 재미
- **MEDIUM**: AI 에셋 캐릭터 일관성 · 오픈필드 제작 비용
- **RETIRED**: 한글 작도 인식의 기술 성립(스파이크 ①로 소거)
