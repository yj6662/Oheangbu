# SPEC-SPELL-FIDELITY — 술식 정본 충실화 (연출·판정·탄속)

| 항목 | 값 |
|---|---|
| 상태 | **PASS** (2026-09-03 승격 — §6 1~7 원격 계측·§6-8 예준 테스트 완료 선언(테스트 허브에서), PR #11 머지. 수치는 [TEST] 유지 — 손맛 튜닝 피드백은 후속. 창설 2026-09-02: 전수 감사 문답, 미비 5건 채택 + 방침 2건[DECISIONS #137·#138]) |
| 작성 | 2026-09-02 |
| 선행 | SPEC-COMBAT-CORE-LOOP TEST(판정 시계·명중 규칙) · SPEC-SPELL-FX-ASSETS PASS(프리팹 규약·§3.1 전방 형성 문법·임포터 수칙) |
| 근거 | SPELL-VOCAB(CSV 정본 — 효과문 LOCKED) · COMBAT-ATTACK · ART-INK(발광 상한) · #134(아트=프로토타입) · #137(광역 실판정 개시) · #138(전용 구현 방침) |

## 1. 목적

전수 감사(2026-09-02)에서 확인된 **CSV 정본 효과문 대 구현의 괴리 5건**을 해소한다.
아트 완성이 아니라 **효과 문법의 정합** — "방사라 쓰인 것은 방사로, 최속이라 쓰인 것은 최속으로".

| # | 대상 | CSV 정본(LOCKED) | 현행 괴리 |
|---|---|---|---|
| 1 | 노 | 지정 영역에 **화염 방사**(즉발) | 투사체 비행→착탄 폭발(방사 문법 부재) — **판정도 광역화**(#137) |
| 2 | 패링 5자 | 속성 방벽·**잔존 방어막** | 방어막 4s 중 2.2s 이후 시각 전무·속성 문양 무구분 |
| 3 | 사·아 | 사=**즉발급 최속** / 아=**느린** 유도탄 | 전 글자 공통 탄속 18 |
| 4 | 가 | 곧게 **뻗는** 생목 가시(근중거리) | 문양 판 투사(뻗는 문법 부재) |
| 5 | 마 | 포물선 **바위** | 문양 판 비행(실체 부재) |

## 2. 방침 (전이 2건 — DECISIONS 등재)

- **#138 전용 구현**: 효과 문법을 기존 프리팹 재조합으로 대체하지 않는다(노 사례 — 예준:
  "전부 재조합으로 만들면 안 된다"). 허용selector: 문양 중첩(의례 언어)·메시/텍스처 자원 재사용.
  금지: 문법 자체를 짜깁기로 근사.
- **#137 광역 실판정 개시**: §9-1 절단면(연출≠실판정)의 부분 해제 — 노(cone)부터.
  고(원형)·소(다연발)·오·모(경로)는 기하가 달라 후속 페이즈(§5).

## 3. 범위 (Goals)

1. 광역 판정 기반 — 전방 cone 다중 히트(`AttackArea` 분기 신설, 노 적용)
2. 노 = FlameJetEffect 전용 신설(전방 형성 문양→cone 화염 방사 — §3.1 문법)
3. 탄속 분화 — `SpellBookSO.Entry.ProjectileSpeedMul`(규칙 계층, 판정·연출 동일 시계)
4. 가 = ThornLanceEffect 전용 신설(뻗는 가시 랜스)
5. 마 = 바위 실체(Meshy 생성 1건 — SPELL-FX 런 규율 준용)
6. 패링 = 방어막 잔존 가독(수명 4s 동기+2단 밝기+페이드) + 5자 속성 문양 등재 + 아웃컴 소피드백

**전제(불변)**: 인식·필세 불가침. 피해=착탄(판정 시각) 동기화 유지 — 탄속 분화도 같은 시계를
나눠 쓴다. 발광 상한(순간만).

## 4. 아키텍처

### 4.1 광역 cone 판정 (Combat)

- `CombatLoopWiring`: `EnemyVitals[] _enemies` 직렬화 배열(씬 배선 — 전역 싱글턴 금지 준수).
  `ResolveAttack`에서 `cast.Kind == AttackArea && IsConeLetter(cast)`면 cone 분기:
  플레이어 전방 수평 cone(각·거리=§7)에 든 **모든 생존 적**에게 PendingCast 예약
  (`ImpactTime = now + AreaImpactDelay` — 연출 분사 시작과 동기). 대상 0체=먹만 소모(허공 방사).
- 1차 cone 적용=노만(글자 판별은 `cast.Letter == '노'` 하드 분기 대신 **SpellBookSO Entry에
  `AreaShape` enum(None/Cone) 추가** — 데이터 주도, 임포터 대체 대상 미러의 자연 확장).
- 락온·자유조준 무관 — cone은 대상 불요. 단일(AttackSingle)은 현행 유지.

### 4.2 노 — FlameJetEffect (SpellSequenceEffect 파생)

시퀀스: 형성(문양 판이 시전자 전방에 수직으로 선다 — 커밋 문양 자리·§3.1, FormTime) →
방사(문양 중심에서 전방 cone 화염 분사 JetDuration — 판정 딜레이와 개시 동기) → 감쇠 소멸.
화염=코드 구성 전용 ParticleSystem(#138): cone emission·스트레치드 빌보드·틴트(팔레트)·
colorOverLifetime 백→틴트→먹빛 침전·순간 Light 1(수명 내 소멸). SpellFx_No 재구성=문양 Bloom
유지+FlameJet 자식(Begin 시그니처 준수 — 어댑터 무수정 편입).

**2026-09-03 개정(SPEC-SPELL-FX-REWORK §3.1)**: 화염 실체=불혀 메시 풀 30(+밑불 6) 항력 방사(`InkFlame` 불투명 clip 셰이더 —
경계·먹 침전·끝부터 침식), 파티클 경로 폐기. 연출은 Cone 계획(Direction·Length·Angle·Delay)을 소비해 형성 딜레이·반각·도달이 판정과
같은 값(§8 예외 1 해소), 점화=판정−`_sweepLead`(REWORK §8-9). Bottom06 FlameBurst 중첩 제거. 어댑터 Cone fallback=전방 끝점
(AREA-SHAPES 작업의 방향 회귀 교정). 수치 정본=REWORK §7.

### 4.3 탄속 분화 (규칙 계층)

`SpellBookSO.Entry.ProjectileSpeedMul`(0 이하=1 취급) → `SpellCast.SpeedMul` →
`ResolveAttack: duration = dist / (SpellProjectileSpeed × mul)` — 어댑터는 배선이 밀어준
duration만 쓰므로 연출 자동 동기. 시각 규칙 소유=Spellcraft(탄속=게임플레이 수치).

### 4.4 가 — ThornLanceEffect / 4.5 마 — 바위

- 가: 글자 자리→대상으로 가시 세그먼트 순차 성장 랜스(§7 수치). 메시=CrystalSpike_So 재사용
  (자원 재사용 — #138 허용면; 2026-09-03 정정 — 초판 「ThornCluster_Go」는 프리팹 실측과 불일치). SpellFx_Ga 신설. 판정=현행 단일(도달 시계=탄속 mul과 일치).
- 마: Meshy preview 1건(바위 ≤1k tris·크레딧 예산 ≤10 — 썸네일 게이트·Blender 반입 검수 5항·
  런 로그 §4.6 전부 SPELL-FX 준용). 비행체 부착(법선 정렬+슬로 스핀)+착탄 흙 파편(SpikeImpactShard
  재사용). SpellFx_Ma 신설·ArcHeight 2.5 유지.

### 4.6 패링 방어막 가독

- `ResolveParry` 경로의 문양 수명=GuardDuration(4s) 전달(AttachBloom lifetime 인자 — 배선만).
- 2단 가독: 패링 창(ParryWindow 0.9s) 동안 파티클 진하게 → 이후 은은 → 마지막 FadeTail(1s)
  스케일·방출 감쇠(남은 방어 시간이 보인다) — PatternEffectLifetime Bloom 모드에 가드 프로파일 추가.
- 패링 5자 SpellVisualSet 등재(속성별 Fly 분화 — P1 감사 컷 모티프 기준, 매핑=재조합 아님).
- 아웃컴 소피드백: Success=현행 버스트 / Half=버스트 축소(0.5배) / Block=버스트 없이 문양 둔탁
  펄스 [TEST].

## 5. Non-Goals

- 고·소·오·모의 실판정 광역화(원형·경로 기하 — 후속 페이즈, #137에 명시) → **SPEC-SPELL-AREA-SHAPES(2026-09-03)에서 착수**
- 가 "근중거리" 사거리 제한(판정 사거리 분화 — 임포터 트랙)
- 종성 변형 어휘·상합·버프 / 사운드 / 정식 필세 수식

## 6. Validation (사전 선언)

| # | 기준 |
|---|---|
| 1 | 컴파일 클린·게임 런타임 예외 0 |
| 2 | cone 판정 실측 — 적이 cone 안/밖 두 케이스에서 TakeDamage 발생/부재(로그) |
| 3 | 노 방사 컷 — 전방 형성 문양→cone 화염 분사→감쇠(판정 시각과 분사 개시 일치) |
| 4 | 탄속 실측 — 사·아 duration이 1.8x/0.6x 비율(로그) + 연출 착탄 일치 육안 |
| 5 | 가 랜스·마 바위 컷 / 마 폴리·검수 5항(런 로그) |
| 6 | 패링 잔존 컷 3장(창 진함·잔존 은은·페이드) — 4s 동기 |
| 7 | 발광 상한 — 신설 화염·라이트 수명 내 소멸 |
| 8 | 예준 실플레이 게이트 — 항목별(방사감·최속/느림 체감·뻗는 감·바위·방어막 가독) |

검증 기록(2026-09-03, 사격장 `C1_SpellRange` — SPEC-DEV-SPELL-RANGE §6.1): **§6-2 VALIDATED**(cone 안 3체 착탄
+0.40s·밖 3체 무피해, 기하 예상 일치) · **§6-4 판정 시계 VALIDATED**(사 0.37s/아 1.11s/가 0.26s = 거리/(18×배율)
정확 일치 — 연출 착탄 일치 육안은 게이트 몫). §6-3·5·6·7·8 = 예준 테스트 완료 선언(2026-09-03, 테스트 허브 실험실·패링장·사격장에서) → PASS.
글자별 손맛 튜닝(방사감·탄속 체감·방어막 가독 수치)은 피드백이 오면 §7 수치 개정으로 받는다.

## 7. 수치 (전부 [TEST] 시작값)

- cone: 각 40°(반각) · 거리 10m · AreaImpactDelay 0.4s
- FlameJet: FormTime 0.3s · JetDuration 1.0s · cone 각 25° · 도달 8m · 파티클 rate ~140 ·
  Light intensity 2.5→0
- 탄속 mul: 사 1.8 · 아 0.6 · 가 1.3 · 나머지 1.0
- ThornLance: 세그먼트 6 · 성장 0.25s · 잔존 0.4s · 침강 0.3s
- 마 바위: 폴리 ≤1k · 크기 ~0.5m · 스핀 90°/s
- 패링: 문양 수명=GuardDuration 4s · 창 밝기 1.0/잔존 0.55 · FadeTail 1s

## 8. Temporary Exceptions

1. ~~cone 판정의 형성 딜레이 고정치(0.4s) — 연출 FormTime과 수동 동기~~ → 해소(2026-09-03 FX-REWORK: FlameJet이 계획 Delay 소비)
2. AreaShape enum은 미러(SpellBookSO) 확장 — 임포터 대체 시 CSV 파생 열로 승계
3. 아트 최종성 없음(#134 승계)
