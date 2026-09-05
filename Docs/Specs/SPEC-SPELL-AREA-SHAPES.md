# SPEC-SPELL-AREA-SHAPES — 광역 실판정 완결 (원형·경로·다연발)

| 항목 | 값 |
|---|---|
| 상태 | **TEST** (2026-09-03 창설 — 예준 선택 "광역 실판정 후속". #137의 후속 페이즈: 고·소·오·모 기하 실판정 + 판정 계획→연출 동기) |
| 작성 | 2026-09-03 |
| 선행 | SPEC-SPELL-FIDELITY PASS(§4.1 cone 실판정·§4.3 탄속=판정·연출 동일 시계) · SPEC-DEV-TEST-HUB PASS(이펙트 실험실·사격장=검증 자리) |
| 근거 | SPELL-VOCAB(CSV 효과문 LOCKED: 고 「지정 영역 일제 솟음」·소 「다연발 송곳 속사」·오 「전진하는 느린 파도·경로 타격」·모 「직선 경로 모래폭풍」) · COMBAT-ATTACK · #137(광역 실판정 개시) · #138(전용 구현) · #141(기하 3종 의미) |

## 1. 목적

노(cone)로 열린 §9-1 절단면 해제를 나머지 광역 4자에 완결한다. 연출이 그리는 영역을 판정이 따라가고,
**판정이 만든 계획(중심·방향·속도·발 단위 시각)을 연출이 받아 같은 시계로 달린다** — 탄속 분화(SPELL-FIDELITY §4.3)의 확장.

## 2. 형상 의미 (DECISIONS #141 — 전부 [TEST])

| 글자 | 형상 | 판정 | 판정 시각 | 연출 동기 |
|---|---|---|---|---|
| 고 | **Circle** | 중심=락온/자유 조준 대상 위치(없으면 조준 전방 `AreaLength`), 반경 `AreaRadius` 안 생존 적 전수 | 커밋 + `AreaImpactDelay` | ThornRise 중심=같은 점(허공 착탄점 대체) |
| 오·모 | **Path** | 시작=시전자 발치, 방향=대상(없으면 전방), 복도 반폭 `AreaRadius`·길이 `AreaLength` | 적별 = 커밋 + 차오름 `AreaImpactDelay` + (경로 위 거리 ÷ `AreaSpeed`) — 전선이 닿는 순간 | GroundWave가 계획의 시작점·방향·길이·속도·차오름을 받아 전진 |
| 소 | **Volley** | 후보=전방 수평 부채꼴(`AreaAngle`·`AreaLength`) 생존 적, 각 오름차순. `VolleyShots`발을 순환 배분, 발당 위력=위력÷발수 | 발 i = 커밋 + 기준 `AreaImpactDelay` + i×`VolleyInterval` + (거리 ÷ `AreaSpeed`) | SpikeVolley가 발 단위 계획(대상·시각)을 받아 그 대상으로 그 시각에 닿게 발사. 후보 0=허공 속사(먹만 소모) |
| 노 | Cone | (기존) CombatConfig 반각·사거리 | 커밋 + 딜레이 | FlameJet이 Cone 계획(Direction·Length·**Angle**·Delay)을 소비(2026-09-03 FX-REWORK — Angle은 CombatConfig 반각의 읽기 사본, 판정은 읽지 않음) |

## 3. 아키텍처

- **데이터**(`SpellBookSO.Entry`, 미러 — 임포터 대체 시 CSV 파생 열): `AreaShape`(None/Cone/**Circle/Path/Volley**) ·
  `AreaAngle` · `AreaRadius` · `AreaLength` · `AreaSpeed` · `AreaImpactDelay`(0=CombatConfig 기본) · `VolleyShots` · `VolleyInterval`.
  `SpellCast.Area`(AreaSpec)로 전달.
- **기하**(`Combat/AreaGeometry` 정적·순수): `InCone` · `InCircle` · `InCorridor`(경로 위 거리·측방 거리) — 수평 판정. 배선과 하네스가 같은 함수를 쓴다.
- **판정 계획**(`App/AreaImpactPlan`): 형상·점·방향·반경·길이·속도·딜레이 + `Shots`(발 단위 대상·시각·위력).
  `CombatLoopWiring`이 형상별 Resolve에서 만들고 ① `PendingCast`(피해=착탄 시계) ② 어댑터 `SetPatternAreaPlan` → `SpellSequenceEffect.SetAreaPlan`(연출)
  ③ `CastPlanned` 이벤트(읽기 전용 재방송 — 하네스 계측) 세 곳에 준다. 시각은 하나뿐이다.
- **연출**: `GroundWaveEffect.SetAreaPlan`(시작점·방향·길이·속도·차오름 덮어씀 — 직렬 값은 감사 씬 폴백; 2026-09-03 FX-REWORK로 오=`WaterWaveEffect`·모=`SandStormEffect`가
  같은 계약을 승계, 노=`FlameJetEffect`가 Cone 계획 소비 — 계획에 `Angle`(Cone 반각 사본) 추가) ·
  `SpikeVolleyEffect.SetAreaPlan`(발마다 대상·발사 시각=착탄 시각−비행시간, 무계획이면 기존 무작위 지터) · ThornRise 무변경(중심=대상/계획점).
- **하네스**: `SpellRangeDirector`가 `CastPlanned`를 받아 계획(대상·예정 시각)을 로그하고, 실제 착탄과 대조. Circle/Path는 계획의
  중심·방향으로 `AreaGeometry` 독립 기대 집합도 계산해 일치 여부를 남긴다.

## 4. 수치 [TEST] (SpellBook_Proto)

| 글자 | 형상 | 값 |
|---|---|---|
| 고 | Circle | 반경 2.5m(연출 산개 1.6+적 반경) · 조준 거리 10m · 딜레이 0.3s(솟음 0.16~0.32s) |
| 오 | Path | 반폭 1.5m · 길이 14m · 속도 6m/s · 차오름 0.45s (연출 프리팹 값 동일) |
| 모 | Path | 반폭 1.2m · 길이 12m · **속도 7m/s · 차오름 0.4s** (2026-09-03 FX-REWORK: 예준 「더 느리게」 — 12→7·0.3→0.4, 연출 SandStormEffect가 같은 값 수신. 초판 12m/s·0.3s의 §6.1-4 계측은 그 값 기준) |
| 소 | Volley | 반각 25° · 사거리 16m · 탄속 32m/s · 기준 1.45s(형성 0.22+홀드 1.1+당김 0.09) · 9발 · 간격 0.1s |

## 5. Non-Goals

- 소 착탄 산포(1.1m) 자체의 판정 — 발은 배정 대상에게만 닿는다
- 경로의 높이 판정(수평만) · 가림(장애물) 판정 · 고 「지정 영역」의 자유 지정(대상/전방 고정)
- 위력 수식·발광·아트(#134)

## 6. Validation (사전 선언)

| # | 기준 |
|---|---|
| 1 | 컴파일 클린 · 실험실/사격장/전투 루프 플레이 예외 0 · 노·단일·패링 회귀 없음(사격장 §6 2·3 재현) |
| 2 | 고: 계획 중심=락온 대상 · 반경 안 과녁만 착탄(+0.30s) · 독립 기하 기대 집합과 일치 |
| 3 | 오: 복도 안 과녁만 착탄, 시각=0.45+경로거리/6 · 복도 밖(측방>1.5m) 무피해 · 연출 전선 도달과 일치(육안) |
| 4 | 모: 같은 규칙으로 0.3+거리/12 · 연출이 계획 속도로 전진 |
| 5 | 소: 후보 순환 배분 — 9발 시각 오름차순·발당 위력=위력/9 · 후보 2체(사격장 D1 8m+D6 15m)면 5/4 배분 · 후보 0=무피해 |
| 6 | 판정 계획 로그(하네스)와 실제 착탄 집합·시각 일치(±1프레임) |
| 7 | 예준 실플레이 — 연출과 판정의 일치감(파도가 닿을 때 맞는가, 송곳이 여럿에 흩뿌려지는가) |

### 6.1 검증 기록 (2026-09-03 — 사격장 원격 계측: 가상 시전, 플레이모드 예외 0)

| # | 결과 |
|---|---|
| 1 | **VALIDATED** — 컴파일 클린 · 사격장 플레이 예외 0 · 단일/cone 경로 코드 불변(분기 추가만) |
| 2 | **VALIDATED** — 고: 계획 중심 (0, 2)=D1 위치·반경 2.5m → D1 +0.30s(예정 0.30) · 계획·기하 기대 모두 일치, D2(4.5m) 밖 |
| 3 | **VALIDATED**(판정) — 오: D1 +1.78s(=0.45+8/6) · 복도 밖 5체 무피해 · 계획·기하 일치. 연출 전선 일치 육안=예준 |
| 4 | **VALIDATED**(판정) — 모: D1 +0.97s(=0.3+8/12, 초판 12m/s·0.3s) → **재계측 2026-09-04: D1 +1.54s(=0.4+8/7, FX-REWORK 감속값)** · 복도 밖 무피해·계획/기하 일치. 연출(SandStormEffect) 계획 수신은 감사 씬 확인, 실작도 육안=예준 |
| 5 | **VALIDATED** — 소: 후보 D1(0°·8m)·D6(20°·15m) → D1×5 / D6×4, 시각 1.70·1.90·2.10·2.30·2.50 / 2.02·2.22·2.42·2.62(=1.45+0.1i+거리/32), 발당 피해 0.5(=4.6/9) · D2·D3(30°) 밖 |
| 6 | **VALIDATED** — 4자 전부 「계획과 일치 · 기하 기대와 일치」(하네스 CastPlanned 대조) |
| 7 | 예준 게이트 대기 — 연출 계획 수신(파도 전선·송곳 발 배정)은 실작도에서만 발화(가상 시전은 문양 미생성) |

## 7. Temporary Exceptions

1. 연출 프리팹의 직렬 운동 수치(GroundWave 속도·길이·차오름, SpikeVolley 형성 시간)는 **폴백** — 정본은 SpellBook. 형성 시간(소 1.41s)은
   프리팹 값과 `AreaImpactDelay`의 수동 동기(Cleanup: 임포터 시 단일 출처).
2. Circle 무대상 중심=조준 전방 고정 거리(자유 지정 없음).
3. Cone(노)의 반각·사거리는 CombatConfig 유지(FIDELITY §8-1 승계) — Entry `AreaImpactDelay`만 덮어쓸 수 있다.
