# SPEC-MIG-BRUSH-STROKE

| 항목 | 값 |
|---|---|
| Spec ID | SPEC-MIG-BRUSH-STROKE |
| Version | 0.2 (2026-08-20 리뷰 반영 — 0.1 제정 동일자) |
| Type | MIGRATION |
| State | TEST (구현 완료 · 검증 중 — CONST-STATUS) |
| Lifecycle | ACTIVE |
| Migration Result | **PASS** — 잔여 조건(Profiler GC) 실측 완료로 승격(2026-08-27 예준 · §14) |

## 1. Authority

- SPELL-BRUSH · ART-INK · PROD-TOOLS · CLAUDE.md 인식 불가침

## 2. Goal

레거시 InkStroke에서 재사용 가능한 획 메시 생성 로직을 신 프로젝트로 이식한다.
최종 목표는 현 정본인 Ribbon Mesh + Noise Cutout 기반 붓 렌더러의 기초 Geometry 계층 확보.

## 3. Legacy Source

- MandateOfInk `Assets/_Project/Scripts/Spellcraft/InkStroke.cs` (분석·참고만 — 복사 금지)
- T_InkStrokeAtlas → **Assets/Spike/Reference/ 격리 보관(REFERENCE ONLY — NOT PRODUCTION).** 원본은 레거시에 존치. 프로덕션 폴더(_Project) 반입 금지 — 미래 에이전트의 "사용 중 에셋" 오판 방지.

## 4. Migration Classification

REFACTOR — 직접 작도·절차 메시 개념은 유효, 현 정본의 Ribbon Mesh / 인식-렌더링 분리 규격으로 구조 변경.

## 5~7. Keep / Rewrite / Remove (실행 결과)

- Keep: 점별 폭·농도 리본 수학(수직 확장 2정점 띠·이웃 평균 방향·진행률 UV) · dirty 리빌드 · MarkDynamic · 수필(收筆) taper.
- Rewrite: `Oheangbu.BrushRender` asmdef 편입 · BrushStrokePoint 데이터 계약 · 머티리얼 주입(Configure) · Input System 기반 테스트 드라이버(Spike).
- Remove: InkStrokeFader/글로우(Spell 판정 연출) · 아틀라스 행 선택 · Shader.Find 결합 · 구 Manager/Spell 의존.

## 8. Non-Goals (유지)

자모 인식 · 필세 수식 · Noise Shader · 갈필 미감 확정 · Spell 발동 연결.

## 9. Target Architecture

구현된 계층: `BrushStrokeData(점 계약) → RibbonMeshBuilder(순수 수학) → BrushStrokeRenderer(MonoBehaviour)`.

### 9.1 입력 구조 — 스파이크 한정 vs 정식 [중요]

현재 테스트 환경은 **S2(인식)와 드라이버(표현)가 같은 물리 입력을 각자 수집**한다.
이것은 **스파이크 한정 구조**다 — 샘플링 간격·Update 순서·필터링이 갈라지는 순간
"보이는 획 ≠ 인식한 획" 문제가 생긴다. 정식 Drawing 계층 Spec에서 반드시 다음으로 통합한다:

```
DrawingInput → RawStrokePoint[](입력 사실: position·timestamp)
                 ├─ Recognition Adapter (인식용 전처리 표현)
                 └─ Render Adapter     (표현용: position·width·ink)
```

- `BrushStrokePoint`/`StrokePoint` 분리는 [TEST] — 3계층(Raw/Recognition/Render) 채택 시
  Production·DECISIONS로 승격 검토. 입력 원천의 이중 수집은 그때 폐지된다.

## Temporary Exceptions

- ~~`Oheangbu.BrushRender.asmdef` — `autoReferenced=true` [TEST ONLY]~~ → **해소(2026-08-20)**: SPEC-DRAWING-INPUT의 단일 Raw 입력 구조(App 어댑터)가 드라이버를 대체 — ① BrushStrokeTestDriver 삭제 ② S2 씬 리그 제거 ③ `autoReferenced=false` 복귀 완료.

## 13. Validation 현황 (2026-08-20)

| 항목 | 상태 |
|---|---|
| 컴파일 · asmdef 분리(BrushRender→인식 참조 0) | PASS (자동) |
| PlayMode 스모크(씬 로드·진입·퇴장 예외 0) | PASS (자동) |
| GC | **명시적 반복 할당 제거만 완료.** 실제 GC Alloc 0 B 여부는 Profiler 실측 대기 [TEST] — 측정 전 "GC 0" 단정 금지 |
| 실제 획 시각 확인 / Profiler / On-Off 회귀 | 미검증 (아래 조건) |

## 14. Exit Criteria — ~~PASS WITH CONDITIONS~~ → **PASS (2026-08-27 예준 승격 — 잔여 조건 전부 해소)**

IMPLEMENTED ✅ → AUTOMATED SMOKE ✅ → HUMAN VISUAL ⏳ → PERFORMANCE ⏳

잔여 조건 (전부 완료 시 PASS 승격 — 완료 판정은 예준):

- [x] 실제 드래그로 Ribbon 출력 확인 — 2026-08-20 예준 육안 확인
- [x] 렌더러 on/off 인식 불변 — 구조 개정으로 승계 검증: SPEC-DRAWING-INPUT의 「어댑터 off 회귀」로 대체(2026-08-20 예준 검증, R 토글·구 드라이버는 소멸)
- [x] Unity Profiler GC Alloc 측정 — **실측 완료(2026-08-27)**: ProfilerRecorder(GC Allocated In Frame)로
  작도 스트레스(연속 리빌드) vs 유휴 비교 — 리빌드 루프의 상시 GC 할당 관찰되지 않음(유휴 배경치와 동일,
  차이는 노이즈 이내). 수치·방법·한계는 SPEC-ART-INK-LOOK §9.4가 정본. **PASS 승격 판정은 예준.**
- [x] Mesh 정상 종료·taper 확인 — 2026-08-20 예준 육안 확인(Geometry 확인에 포함)

## 15. 후속 순서 (합의)

PASS 승격 → SPEC-SPIKE-BRUSH-RENDERER(Width·Taper·UV·Ink) → SPEC-SPIKE-INK-LOOKDEV(Noise Cutout·갈필·한지).
Geometry를 사람 눈으로 검증하기 전에 Shader를 얹지 않는다 — 문제 계층 구별 불가.
