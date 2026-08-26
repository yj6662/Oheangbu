# SPEC-DRAWING-INPUT

| 항목 | 값 |
|---|---|
| Spec ID | SPEC-DRAWING-INPUT |
| Version | 0.1 |
| Type | FEATURE (+ 레거시 REFACTOR 이식) |
| State | LOCKED — 구조·규칙 확정 (수치류는 개별 [TEST] 유지) |
| Lifecycle | ACTIVE |
| 제정 | 2026-08-20 · 입력 스킴 문답 3건 확정 반영 |
| Result | **PASS** — IMPLEMENTED → VALIDATED(2026-08-20 예준: Q 흐름 작도·합성 인식·불발·피격·어댑터 off 회귀 전 항목) → PASS |

## 1. Authority

- CONST-RULES 3조 1항(작도는 직접 획 입력만) · SPELL-GRAMMAR(한 글자=하나의 진) · SPELL-RECOGNITION(판정 3단 골격) · SPELL-BRUSH(필세·감쇠 정의) · COMBAT-ATTACK(감속 전역 상수·피격=작도 중단) · COMBAT-PARRY(판정점=글자 완성 시점) · CLAUDE.md 인식 불가침 · SPEC-MIG-BRUSH-STROKE §9.1(단일 Raw 입력 구조)

## 2. Goal

작도 입력의 정식 골격을 세운다:

> **Q 키 다운 동안 작도 모드 — 자음+모음이 합쳐진 하나의 글자를 그리고 — Q 키를 떼면 해당 글자가 커밋(인식·발동 요청)된다.**

동시에 SPEC-MIG-BRUSH-STROKE §9.1이 예약한 **입력 이중 수집 해소**를 이행한다: 단일 Raw 입력이 인식과 표현 양쪽의 유일한 원천이 된다.

## 3. 확정 규칙 (2026-08-20 문답 — 예준)

| # | 항목 | 확정 |
|---|---|---|
| 1 | **세(勢) 구간** | 첫 획 시작 → 마지막 획 종료 (글씨 속도만 잰다) |
| 2 | **감쇠 구간** | Q 진입 → Q 릴리즈 (모드 체류 전체 — 꾸물거림·홀드 저글링 벌점) |
| 3 | **미인식 릴리즈** | **불발** — 먹만 소모, 아무 일 없음. (약발동 폴백 TBD는 SPELL-RECOGNITION에 유지 — 본 규칙은 스파이크 임시 규칙이자 현행 기본값) |
| 4 | **피격 시** | 그리던 **글자만 소멸, Q 모드는 유지** — 즉시 다시 긋기 시작 가능. 넘(부동심)은 소멸 자체를 막는다 |

- 역할 분리 사유(#1·#2): 세=글씨 속도, 감쇠=체류 벌점 — 이중 벌점 없음. 다 그려놓고 릴리즈를 고르는 여유는 감쇠가 자연 조입한다.

## 4. 파생 해석 [승인 — 2026-08-20 예준 · COMBAT-PARRY 주석 등재 대기]

- **패링의 "글자 완성 시점" = Q 릴리즈 순간**으로 해석한다("떼면 발동"의 자연 귀결 — 릴리즈가 곧 완성 선언). 임팩트 직전 창 판정의 기준점이 릴리즈가 되므로, **패링은 "맞춰 떼는" 기술**이 된다. Combat Bible 소유 사항이라 예준 확인 후 COMBAT-PARRY 주석 등재.

## 5. Flow

```
Q 다운 ──► 작도 모드 진입
            · 시간 감속 시작(전역 상수) · 감쇠 타이머 시작
            · 커서 해방·시점 잠금(글루 계층 — StarterAssets류 직접 참조 금지)
  획 긋기(여러 획) ──► RawStrokePoint 기록 (position·timestamp·strokeId)
Q 업 ──► 커밋
            ① 획 그룹 분할 → 자모 매칭(JamoMatcher) → 초성+중성(+종성) 조합
            ② 성공: 글자 + 필세 측정치(형·세)·감쇠 계수 → 이벤트 채널 발행
               (발동·효과는 Spellcraft/Combat 소유 — 본 Spec 밖)
            ③ 실패: 불발 — 먹 소모 이벤트만
피격 ──► 현재 획·기록 소멸(글자만) · 모드 유지 · 넘이면 무효
```

## 6. Target Architecture

```
DrawingInputController (Oheangbu.Drawing — Q 상태머신·Raw 수집의 유일 지점)
        │
   RawStrokePoint[]  (Core.Domain StrokePoint/StrokeData 기반 — 입력 사실만)
        ├─► RecognitionPipeline (Drawing): 분할 → JamoMatcher → 자모 조합 → 글자
        └─► Render Adapter (App 글루): BrushRender에 점 공급 (표현 계약으로 변환)
```

- **인식 불가침**: RecognitionPipeline은 RawStrokePoint만 소비. BrushRender는 어댑터 경유 수신 전용 — 렌더러 on/off·교체가 인식 입력에 닿을 경로 없음(기존 asmdef 강제 유지).
- SPEC-MIG-BRUSH-STROKE의 `BrushStrokePoint` 분리 [TEST]가 본 구조(Raw/인식/표현 3계층)로 승격되는 자리 — 채택 확정 시 Production/DECISIONS 승격 후보.

## 7. Dependencies · asmdef

- 요구: `Oheangbu.Drawing`에 **Unity.InputSystem 참조 추가** — asmdef 표 개정 사항(엔진 패키지 참조 — Presentation의 URP 선례와 동층). 입력 수집의 소유 모듈이 Drawing이므로 정식 참조(Temporary 아님).
- 글루(커서·시점·이동 잠금)는 App 또는 전용 글루 계층 — 레거시 ProtoGlue 패턴 승계(직접 참조 격리).
- Q 키 바인딩 = 데이터/설정 [TEST] — 코드 하드코딩 금지, InputSystem_Actions 액션맵으로.

## Temporary Exceptions

- **`Oheangbu.Core.asmdef`·`Oheangbu.Drawing.asmdef` — `autoReferenced=true` [TEST ONLY]**
  - Reason: Spike 하네스(S3 — Assembly-CSharp)가 DrawingInputController·이벤트 채널 타입에 접근해야 함(BrushRender·PDollar 선례).
  - Cleanup Gate: 작도 스파이크 종료 시 ① S3 하네스 삭제 ② 두 asmdef `autoReferenced=false` 복귀(BrushRender 예외와 동일 게이트).
  - **Exit Blocking: 복귀하지 않으면 Production 승격 불가.**

## 8. Legacy Source (REFACTOR — 복사 금지)

- `MandateOfInk .../Spellcraft/DrawingInputController.cs`(536줄): 모드 상태머신·획 수집·분할 문법 분석 원천. 약발동·도면(Diagram) 결합부는 제거 대상(도면 개념 LEGACY 유력 — SPELL-TBD).
- S2 하네스: 샘플 간격(2px)·Y 뒤집기 좌표 규약 승계.

## 9. Non-Goals

- 진 발동·효과 실행(Spellcraft/Combat) · 필세 수식·수치 확정(데이터 TEST) · 종성 획득 게이팅 · 렌더 셰이더 · 패링 창 수치 · 글자→효과 매핑(작도어휘 CSV 임포트는 별도 Spec)

## 10. Validation

- Functional: Q 홀드→다획 작도→릴리즈 커밋 흐름 / 미인식=불발 / 피격=글자만 소멸.
- Bible: 인식 불가침(렌더러 on/off 무관 동일 인식 — S2 Lab 회귀) / 감속·감쇠 구간이 §3 정의와 일치.
- 인식률: S2 하네스 측정 문법을 Q 흐름 위에서 재측정 — 기존 인식률 대비 회귀 없음(합성 글자 분할 인식은 신규 측정 [TEST]).
- 종료 보고는 IMPLEMENTED → VALIDATED → PASS 3단(spec-validation-discipline).

## 11. Exit Criteria — 충족 (2026-08-20)

- [x] 단일 Raw 수집 구조 성립 — **이중 수집 소멸 집행 완료**: BrushStrokeTestDriver 삭제·S2 씬 리그 제거·`Oheangbu.BrushRender` autoReferenced=false 복귀(SPEC-MIG-BRUSH-STROKE의 Temporary Exception 해소)
- [x] Q 흐름 합성 글자 인식 — 예준 검증 통과(정량 인식률 곡선은 필세 스파이크에서 계측 [TEST])
- [x] Validation 전 항목 + 예준 손맛 확인

잔존 Temporary Exception: Core·Drawing autoReferenced=true — S3 Lab이 후속 스파이크(필세·갈필)에서 계속 쓰이므로 유지, 작도 스파이크 전체 종료 시 정리(Exit Blocking 유효).
