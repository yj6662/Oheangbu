# SPEC-DEV-SPELL-RANGE — 술식 사격장 (실플레이 게이트용 테스트 환경)

| 항목 | 값 |
|---|---|
| 상태 | **PASS** (2026-09-03 승격 — PR #11 머지 + 예준 테스트 완료 선언[§6-5]. 창설 같은 날: 예준 지시 "테스트환경 구축". 개발 도구 — 게임 규칙 무접촉) |
| 작성 | 2026-09-03 |
| 선행 | SPEC-SPELL-FIDELITY TEST(§6-2 cone 실측·§6-4 탄속 실측·§6-8 실플레이 게이트) · SPEC-COMBAT-CORE-LOOP TEST(§6 실플레이 7종) · SPEC-DEV-TEST-HUB TEST(2026-09-03 허브 Room 편입 — 리그 프리팹·선택대·귀환 포탈) |
| 근거 | PROD-PIPELINE(Validation) · 헌법 HUD 화이트리스트(게임 HUD 무추가 — 하네스 라벨은 월드 텍스트) · 싱글턴 금지(씬 배선) |

## 1. 목적

실플레이 게이트를 **재현 가능한 조건**에서 돌리기 위한 전용 개발 씬. 현행 `C1_CombatLoop`는 적 1체가
반격하는 루프 씬이라 ① cone 안/밖 다중 판정 ② 거리별 탄속 비교 ③ 반격 없는 상태의 연출 관찰이 불가하다.
사격장은 **판정을 바꾸지 않고 읽기만 한다** — 계측·안내 코드는 게임 규칙에 손대지 않는다.

Production Bible의 「Validation 정의(자동 테스트·수동 체크리스트 구분) TBD」는 그대로다 — 본 Spec은
수동 체크리스트 환경 1종을 제공할 뿐 정의를 확정하지 않는다.

## 2. 구성 (`Scenes/Dev/C1_SpellRange.unity` — C1_CombatLoop 복제 기반)

플레이어 시작점 = (0, −6) · 전방 +Z. 바닥 안내선은 `CombatConfigSO`의 cone 수치(반각·사거리)를
런타임에 그린다 — 수치를 바꾸면 선도 따라간다.

| 과녁 | 시작점 기준 | cone(40°/10m) 판정 | 용도 |
|---|---|---|---|
| D1 | 정면 8m | IN | 탄속 근거리 기준·랜스·바위 (가 0.34s 예상) |
| D2 | 좌 30° 9m | IN | cone 다중 명중 |
| D3 | 우 30° 9m | IN | cone 다중 명중 |
| D4 | 좌 60° 8m | OUT(각) | cone 각 경계 밖 |
| D5 | 우 60° 8m | OUT(각) | cone 각 경계 밖 |
| D6 | 우 20° 15m | OUT(사거리) | 탄속 원거리 비교(사 0.46s / 아 1.39s 예상) |

(2026-09-03 재배치 — 예준: 간격 벌리기+멀리. 초판 6/7/12m 배치의 계측 기록은 §6.1에 그대로 둔다.)
| 실적 | 좌 67° 7.6m | OUT | 패링·갈무리·그로기 — **시작은 잠듦 — 선택대(F)로 깨움** |
| 선택대 | (−4, −6.5) | — | `Pedestal_Live`(TEST-HUB `DevEnemyWakePedestal`) — 실적 깨우기/재우기 |
| 귀환 포탈 | (3, −9) | — | `Portal_Return` → `C1_TestHub` |

- 과녁 = `EnemyVitals` + `SpellRangeDummy`(하네스) — `EnemyController` 없음(반격 없음). 설정은
  `CombatConfig_RangeDummy`(EnemyMaxHp 100000 — 사실상 불사, 그 외 Default와 동일).
- 실적 = C1_CombatLoop의 적 그대로(Default 설정·락온 대상·`_enemyVitals` 단일 참조).
- `CombatLoopWiring._enemies` = 과녁 6 + 실적 1 (cone 실판정 대상 전수 — 씬 배선).
- 하네스 오브젝트 `RangeDirector`(`SpellRangeDirector` + `DevInteractor`) — 채널 구독·계측·안내선 / 키·상호작용(TEST-HUB 이관).
- 플레이어 리그 = `PlayerRig` 프리팹 인스턴스(TEST-HUB §3, 2026-09-03 교체) — seam(`_enemies` 7 등)은 인스턴스 오버라이드.

## 3. 조작

| 키 | 동작 |
|---|---|
| 기존 전부 | WASD·마우스·Q 작도·Shift 회피·Tab 락온(실적)·LMB 갈무리·V 카메라 실험 |
| **R** | 씬 재시작 — 위치·HP·먹·과녁 전부 초기화(작도 감속 중이어도 정상 속도로). 키 리더=`DevInteractor`(TEST-HUB 공통) |
| **F** | 상호작용 — 선택대: 실적 AI 깨우기/재우기(패링 검증 때만 깨운다) · 귀환 포탈: 허브로 |

## 4. 계측 출력 (콘솔 `[Range]` — 시간은 게임 시계, 작도 감속 포함)

- 시전: 글자·종류 → **판정 계획**(배선 `CastPlanned` 재방송: 형상 수치·과녁별 예정 시각·발수) + **기하 기대 집합**(계획의 중심·방향으로
  `AreaGeometry` 독립 호출 — 2026-09-03 SPELL-AREA-SHAPES 확장)
- 착탄: 과녁별 `+지연 s (예상 s) 피해` — 예상 = 거리/(18×배율) 또는 cone 딜레이
- 집계(시전 +2.5s): cone은 명중/미명중 집합 + 기하 예상과 일치 여부 · 단일은 명중 과녁·지연
- 과녁 라벨: 캡션(각·거리·IN/OUT) + 명중 수·누적 피해 (피격 시 0.15s 백색 플래시)

## 5. 코드 접촉 (게임 계층 — 규칙 무변경)

- `CombatLoopWiring.FindFreeAimTarget`: 후보를 단일 적에서 **단일 적 + `_enemies` 전수**로 일반화
  (원뿔 안에서 시선에 가장 가까운 생존 적 1체). 규칙 동일 — 후보 집합만 넓어진다. 적 1체 씬은 결과 불변.
- `Oheangbu.App.asmdef` + `Unity.InputSystem` 참조(하네스 키 직결용).
- 하네스: `Scripts/App/Dev/SpellRangeDummy.cs` · `SpellRangeDirector.cs` — 읽기 전용(채널 구독·`HpChanged`).
- 에디터 도구 `Scripts/Editor/SpellRangeSceneBuilder.cs`: 메뉴 `Oheangbu/Dev/사격장 배치 재구성` — §2 배치표의
  단일 출처(재실행 가능). 원격 검증 보조 `FireLetter`/`FireLetterFromPose`(가상 시전 — 인식 계층을 **우회**해 글자
  채널만 올린다: 판정·계측 검증용, 표현(Committed)은 발화하지 않음. 자세 리셋 동반형이 결정적 — 마우스 룩 드리프트 배제).

## 6. Validation (사전 선언)

| # | 기준 |
|---|---|
| 1 | 컴파일 클린·플레이 진입 예외 0 · C1_CombatLoop 동작 불변(자유 조준 회귀 없음) |
| 2 | 노 시전(시작점·정면) → 집계가 IN 3(D1·D2·D3)/OUT 3(D4·D5·D6)로 기하 예상과 일치 — SPELL-FIDELITY §6-2 증거 |
| 3 | 사·아 시전(D6) → 착탄 지연이 예상(0.37s/1.11s)과 ±1프레임 — SPELL-FIDELITY §6-4 증거 |
| 4 | R 재시작 후 과녁·먹·HP 초기화, 선택대 F로 실적 텔레그래프 개시/정지 |
| 5 | 예준 실플레이 — 사격장이 게이트 판정에 쓸 만한가(안내선·라벨 가독·조작) |

### 6.1 검증 기록 (2026-09-03 — 가상 시전 원격 계측, 플레이모드 예외 0)

| # | 결과 |
|---|---|
| 1 | 컴파일 클린 · 플레이 진입 예외 0 · C1_CombatLoop 불변은 코드 논증(후보 집합 [단일 적]=동일 결과) — 실플레이 미실행 |
| 2 | **VALIDATED** — `노` @ 시작점 정면: 착탄 D1·D2·D3 +0.40s(예상 0.40) / 미명중 D4·D5·D6 · 「기하 예상과 일치」 |
| 3 | **VALIDATED** — `사`→D6 +0.37s(예상 0.37) · `아`→D6 +1.11s(예상 1.11) · `가`→D1 +0.26s(예상 0.26) — 자유 조준 일반화가 yaw 15°에서 D6 선택 |
| 4 | 부분 — 선택대 깨움/재움은 원격 상호작용으로 VALIDATED(2026-09-03, 리그 프리팹 교체 후 재현 포함) · R/F 키 자체는 예준 실플레이 |
| 5 | **PASS** — 예준 테스트 완료 선언(2026-09-03). 원격 컷: 안내선·라벨 가독 확인(라벨 크기 0.08→0.045 축소 — 겹침 교정) |

재배치(8/9/15m) 재계측(2026-09-03): 노 → D1·D2·D3 +0.40s / D4·D5·D6 미명중 · 기하 일치 · 사 → D6 15m +0.46s(예상 0.46) ·
Tab 락온이 과녁을 잡는다(#139 — 정면 D1·우30° D3·우20° D6·좌67° 실적).

## 7. Non-Goals

- 자동 테스트(EditMode/PlayMode) 도입 — Validation 정의 TBD의 해소는 별도 문답
- 다수 락온·다수전 AI(LockOn 단일 대상 유지 — 프로토 절단면)
- 과녁 부활 API(`EnemyVitals` 무변경 — 재시작으로 대체)

## 8. Temporary Exceptions

1. 하네스 키 직결(`Keyboard.current` R/F) — SPEC-DEV-TEST-HUB §9-1로 통합(`DevInteractor` 단일 지점, 2026-09-03). 실적 AI 토글은
   선택대 상호작용으로 이관 — `SpellRangeDirector`는 계측만 남는다.
2. `CombatConfig_RangeDummy` = Default 복제본 — 사격장 전용이며 밸런스 정본 아님. Default 개정 시 재복제.
