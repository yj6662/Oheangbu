# SPEC-DEV-TEST-HUB — 작도 테스트 허브 (룸 포탈 체계)

| 항목 | 값 |
|---|---|
| 상태 | **PASS** (2026-09-03 승격 — PR #11 머지 + 예준 테스트 완료 선언[§7-11]. 창설 같은 날: 예준 지시 "각종 작도 테스트 Room·접촉 설명 UI·F 진입". 개발 도구 — 게임 규칙 무접촉) |
| 작성 | 2026-09-03 |
| 선행 | SPEC-DEV-SPELL-RANGE TEST(사격장 → Room 편입) · SPEC-COMBAT-CORE-LOOP TEST(§6 실플레이 7종) · SPEC-SPELL-FIDELITY TEST(§6-8 게이트) |
| 근거 | PROD-PIPELINE(Validation) · 헌법 HUD 화이트리스트(하네스 안내=월드 텍스트, HUD 무추가) · ART-INK 광원 3등급(소품 Emission 금지 — 명도 대비만) · 싱글턴 금지(씬 배선) · 바인딩=데이터(개발 씬 키 직결 예외) |

## 1. 목적

실플레이 게이트(COMBAT-CORE-LOOP §6 · SPELL-FIDELITY §6-8 · SPELL-RANGE §6-5)를 **한 동선에서** 돌린다. 허브에 방(Room)
포탈이 서 있고, 포탈에 다가가면 그 방이 무엇을 시험하는지 설명이 뜨며, F로 들어간다. 방 = 목적별 조건이 고정된 독립 씬.
하네스는 **판정을 바꾸지 않고 읽기만 한다**. Production Bible 「Validation 정의 TBD」는 그대로다.

## 2. 구성

| 씬 | 역할 | 신설/편입 |
|---|---|---|
| `C1_TestHub` | 허브 — 포탈 4 + 조작 안내판 | 신설 |
| `C1_EffectLab` | 이펙트 실험실 — 불사 과녁 3체, 공격 글자 10자의 실제 연출 관찰(락온·유도·광역) | 신설 |
| `C1_ParryRange` | 패링장 — 원거리 적 5속성, 선택대로 하나씩 깨워 패링 3단 표본 | 신설 |
| `C1_SpellRange` | 사격장 — cone 판정·탄속 계측(SPEC-DEV-SPELL-RANGE) | 편입(선택대·귀환 포탈) |
| `C1_CombatLoop` | 전투 루프 — 원본 프로토 씬 | 편입(귀환 포탈만) |

공통 규약: 시작점 **(0, 1.1, −6)·전방 +Z**(모든 씬 동일 — 가상 시전 원격 검증 재사용) · 귀환 포탈 **(3, 0, −9)**
(시작점 뒤오른쪽, 사격 cone 밖) · 플레이어 리그 = 프리팹 1종(§3) · 그레이박스 소품은 Emission 0.
배치표의 단일 출처 = `Scripts/Editor/TestHubSceneBuilder.cs`(신설 씬은 전체 재생성 가능).

허브: 포탈 4가 시작점 기준 반경 7m 호 −36°/−12°/+12°/+36°(이펙트 실험실·사격장·패링장·전투 루프), 안내판 (−3.5, 0, −4).
이펙트 실험실: `L1` 정면 6m(락온 대상) · `L2` 우12° 12m · `L3` 좌30° 7m(cone 2체째) — `CombatConfig_RangeDummy`(불사), 어휘판(−5.5, 0, −5).
패링장: 적 5체 8m 호 −40°/−20°/0°/+20°/+40° 상생 순(목·화·토·금·수), `CombatConfig_ParryRange`(원거리 전용·불사),
선택대 5는 시작점 앞 1.5m 한 줄(x −4…+4) — 깨우면서 8m 사격 위치를 유지한다. 정답표(−6.5, 0, −3)·판정판(6.5, 0, −3).

## 3. 리그 프리팹 규약 — `Assets/_Project/Prefabs/Rig/PlayerRig.prefab`

```
PlayerRig                      루트(빌더가 시작 자세로 배치 — yaw만)
├─ Player  [이름 고정]         CharacterController·PlayerMotor·Dodge·PlayerVitals·LockOn·Harvest·CameraRig
│  ├─ CameraPivot → Main Camera[MainCamera] → DrawingRig
│  ├─ Body · HarvestStream · BrushProp(Handle·Tip·BrushTip)
└─ CombatSystems               HudController·CombatLoopWiring·CombatLifetimeScope
```
- 씬 인스턴스 오버라이드는 **seam 3필드만**: `CombatLoopWiring._enemy/_enemyVitals/_enemies` (+ 루트 트랜스폼). 락온 후보는
  배선부가 이 집합에서 `LockOn`에 넘긴다(#139 — 구 `LockOn._target` seam 폐지, 빌더가 잔존 오버라이드를 정리).
  리그 내부 수정은 프리팹 스테이지에서만 — 메뉴 「리그 오버라이드 점검」이 허용 집합 밖을 경고한다.
- `GlobalVolume_InkLook`은 리그 밖(환경 — 씬마다 빌더가 생성). 로드된 씬 집합에 리그는 항상 1벌(`Camera.main` 참조 17곳).
- 방 쪽 적은 `EnemyController._player/_playerVitals`로 리그 인스턴스를 가리킨다(같은 씬 — 허용).

## 4. 조작

| 키 | 동작 |
|---|---|
| 기존 전부 | WASD·마우스·Q 작도·LMB 획/갈무리·Tab 락온·LShift 회피·V 카메라 실험 |
| **F** | 상호작용 — 반경 안 최근접 대상: 포탈=진입/귀환 · 선택대=실적 깨우기/재우기. **작도 중 무효** |
| **R** | 현재 씬 재시작(전 상태 초기화·작도 감속 중이어도 정상 속도로) — 모든 하네스 씬 공통 |

키 리더는 씬당 `DevInteractor` 하나(§9-1).

## 5. 안내·계측 출력

- 상호작용 대상 라벨: 제목(상시·0.045) / 설명 2~3줄 + 힌트 `F: 진입|귀환|깨우기|재우기`(반경 2m 안에서만·0.03) / 바닥 고리(근접 시 명도 상승).
- 허브 안내판(조작 요약) · 이펙트 실험실 어휘판(SpellBook·CombatConfig에서 수치 합성 + 문법 문구=CSV 미러) ·
  패링장 정답표(`ElementRelations`·`CombatConfig`에서 런타임 합성 — 성공=상극·실패=상생·그 외 반성공·창 밖=블록·방어막 없음=피격) ·
  판정판(속성별 집계 + 최근 4건).
- 콘솔 접두: `[Hub]`(이동·깨움) · `[Range]`(시전/착탄/집계 — SPELL-RANGE) · `[Parry]`(판정 기록).

## 6. 코드 접촉

게임 계층(규칙 무접촉 — 배선·표현 측만):
- `CombatLoopWiring`: ① 컨트롤러를 `_enemy`+`_enemies`에서 파생(새 직렬 필드 없음)해 **전수**에 `Init(_judge)`·텔레그래프 색(적 자신의
  원거리 속성 초성 → 팔레트 기본색; 화 적은 'ㄴ'으로 현행과 동일)·`StunEnded` 구독·만개 스턴을 적용 — 적 1체 씬은 결과 불변.
  ② `ParryResolved` 이벤트 재방송(`OnParryImpactResolved` 첫 줄, 분기 무변경) — 판정판의 유일한 정보원.
- `EnemyController.OnDisable`: 잠듦(`enabled=false`)·씬 해제 시 진행 중 공격을 버린다(투사체 비활성·스턴 정리·Idle) —
  재활성 프레임의 유령 착탄 방지. CombatLoop는 적을 비활성화하지 않으므로 불변.
- 데이터: `CombatConfig_ParryRange`(Default 복제 + `_enemyMeleePreferRange 0.5`=근접 도달 불가 + `_enemyMaxHp 100000`).
- 검수 중 전이(2026-09-03, DECISIONS #139): `LockOn` 대상 선정 규칙 신설(사거리·화면·중앙 최근접) — 게임 규칙 변경이며 SPEC-COMBAT-CORE-LOOP
  §10.1에 등재. `CombatLoopWiring`이 후보 집합(`_enemyVitals`+`_enemies`)을 `SetCandidates`로 넘긴다.

하네스(`Scripts/App/Dev/`): `DevLabel`(월드 텍스트·고리·무광 재질) · `DevSceneFlow`(씬 로드/재시작) · `DevInteractable`(추상) ·
`DevScenePortal` · `DevEnemyWakePedestal` · `DevInteractor` · `DevInfoBoard` · `DevElement` · `EffectLabBoard` · `ParryRangeDirector`.
`SpellRangeDummy` 캡션 직렬화·라벨 유틸 위임 / `SpellRangeDirector` 키·실적 토글 제거(계측 불변).
에디터(`Scripts/Editor/`): `DevSceneKit`(씬 템플릿·리그 인스턴스·seam·소품·재질·Build Settings) · `PlayerRigPrefabTool`(추출·교체·점검) ·
`TestHubSceneBuilder`(배치표) · `SpellRangeSceneBuilder` 개정(선택대·귀환·인터랙터). `Oheangbu.EditorTools.asmdef` + `Unity.RenderPipelines.Core.Runtime`.

## 7. Validation (사전 선언)

| # | 기준 |
|---|---|
| 1 | 컴파일 클린 · 5개 씬 플레이 진입 예외 0 · `C1_CombatLoop` 동작 불변(리그 추출·컨트롤러 일반화 후 텔레그래프 색·화염구·패링 버스트 동일) |
| 2 | 리그 프리팹: 에셋의 seam 4필드 null · 씬 인스턴스 오버라이드 ⊆ 허용 집합 · 씬마다 wiring 1·MainCamera 1·AudioListener 1 |
| 3 | 허브: 포탈 4 — 2m 밖=제목만 · 안=설명+힌트+고리 명도 상승 · 이탈 시 숨김(경계 플리커 없음) |
| 4 | F 진입: 4개 방 모두 로드 · 각 방 귀환 F → 허브 · 왕복 예외 0 · 복귀 후 `timeScale` 1·커서 잠김 · 작도 중 F 무효 |
| 5 | R: 모든 하네스 씬 재시작(작도 감속 중 포함) |
| 6 | 이펙트 실험실: 락온 L1 · 단일 5자→L1 명중 · 노→L1·L3 IN·L2 OUT · `[Range]` 로그 · 어휘판 수치=SO 일치 |
| 7 | 패링장: 선택대 F → 해당 적만 활동(나머지 잠듦) · 텔레그래프 색 5종=팔레트 · 근접 0 · 판정 기록이 정답표와 일치(속성별 성공·실패 각 1 이상) · 만개 시 활동 적만 스턴 |
| 8 | 사격장: 선택대 F로 실적 깨움/재움(유령 피해 없음) · SPELL-RANGE §6 2·3 재현 불변 |
| 9 | 규정: `HudController` 무변경(추가 HUD 0) · 소품 재질 Emission 0·지속 광원 0 · 전역 탐색/싱글턴 0 |
| 10 | 상태 드리프트 0 · Temporary Exceptions 전부 §9 기재 |
| 11 | 예준 게이트 — 허브 동선·라벨 가독(2m 설명·7m 제목)·설명 충분성·방별 목적 성립 |

### 7.1 검증 기록 (2026-09-03 — 원격 계측: 가상 시전·원격 상호작용·플레이어 이동 헬퍼, 플레이모드 예외 0)

| # | 결과 |
|---|---|
| 1 | **VALIDATED** — 컴파일 클린 · 5개 씬 플레이 진입 예외 0 · C1_CombatLoop 추출 전/후 플레이 동일(적 공격·HP 감소, 예외 0). 패링 버스트 동일성은 코드 논증(화 적='ㄴ' 색 경로 불변) |
| 2 | **VALIDATED** — 에셋 seam null · 인스턴스 오버라이드=seam+루트 트랜스폼(+m_Name)만 · 5개 씬 전부 wiring 1·MainCamera 1·AudioListener 1 (seam 3필드로 축소 후 재확인) |
| 3 | **VALIDATED**(원격) — 2m 안 진입 시 설명 3줄+`F: 진입`+고리 명도 상승, 이웃 포탈 고리는 먹빛 유지(컷). 이탈 플리커는 키 입력 없이 미실측 |
| 4 | **VALIDATED** — 허브→실험실→허브→패링장→허브 3전환 예외 0, 하네스 Start 로그 씬마다 정상. F 키 자체(작도 중 무효 포함)는 예준 실플레이 |
| 5 | 미실측(키 입력) — `DevSceneFlow.Restart`는 포탈 로드와 같은 경로 |
| 6 | **VALIDATED** — `가`→L1 +0.26s(예상 0.26) · `노`→L1·L3 +0.40s, L2 OUT, 기하 일치 · 어휘판 SO 합성 표시 |
| 7 | **부분 VALIDATED** — 선택대 F(원격)로 화 깨움→수 깨움 시 화 자동 잠듦 · 화 텔레그래프=팔레트 주홍(컷) · 근접 0(원거리 전용 설정) · 판정판/콘솔: 방어막 없음=피격 9건, `어` 방어막 vs 화 → **블록**(창 밖 1.6s — 규칙대로) 기록. 성공/반성공/실패 표본은 창 0.9s 타이밍이라 원격 불가 → **예준 게이트**. 플레이어 HP는 8피격이면 소진(R로 초기화) |
| 8 | **VALIDATED** — 리그 교체 후 `노` IN 3/OUT 3 재현 · 선택대 깨움/재움 예외 0 |
| 9 | **VALIDATED** — HudController 무변경 · 소품 재질 Emission 0(`M_DevInk/M_DevStone/M_DevElem_*` Lit·EmissionColor 0) · 하네스는 씬 배선만 |
| 10 | **VALIDATED** — 드리프트 0 · §9 기재 |
| 11 | **PASS** — 예준 테스트 완료 선언(2026-09-03, PR #11 머지 후). 라벨 크기는 원격 컷 기준 1차 조정치(포탈 제목 0.06·선택대 0.035·판 0.03, 판 폭 4.2~4.6m) 유지 |

락온 대상 선정(#139) 원격 검증(2026-09-03, 사격장 재배치 후): 정면→D1(8m) · 우30°→D3 · 우20°→D6(15m, 10° 벗어난 D3보다
중앙 우선) · 좌67°→실적 · 뒤돌아섬→락온 없음 — 5례 전부 규칙과 일치, 레티클 컷 확보. 재배치 사격장 재계측: 노 IN D1·D2·D3 /
OUT D4·D5·D6 기하 일치 · 사→D6 15m +0.46s(예상 0.46).

구현 중 교정 2건(빌더): ① 씬 생성(`NewScene`)이 미참조 에셋을 언로드해 먼저 얻은 SO 참조가 죽은 객체(fileID 0)로 박힘 —
설정 에셋은 씬 생성 뒤에 얻는다 ② 원격 검증 첫 시도의 cone 0체 명중=마우스 룩 드리프트(자세 리셋 헬퍼로 해결, SPELL-RANGE 선례).

## 8. Non-Goals

다수 락온(LockOn 단일 유지) · `EnemyController` Sleep/Wake API(`enabled` 토글 유지) · 정식 Interact 액션 승격 · 부트스트랩 씬/
RootLifetimeScope 활성화 · 런타임 리그 생성 · `C1_FxAudit` 포탈 · 자동 테스트.

## 9. Temporary Exceptions

1. `Keyboard.current` F/R 직결 — `DevInteractor` 단일 지점·개발 씬 전용. Exit: 정식 Interact 액션 도입 시 그 액션을 읽도록 승격.
   (SPEC-DEV-SPELL-RANGE §8-1을 여기로 통합)
2. 개발 씬 Build Settings 등재(허브 첫 번째) — 씬 로드 빌드 분기 유효화. 부트스트랩 씬 도입 시 재정리.
3. TextMesh 레거시 폰트(`LegacyRuntime.ttf`+OS 한글 폴백) — 하네스 한정.
4. 어휘판 문법 문구 = CSV 수동 미러(수치는 SO 합성). 임포터 도입 시 대체.
5. `CombatConfig_ParryRange` 복제본 — 패링장 전용, 밸런스 정본 아님(Default 개정 시 재복제).
6. 패링장 적은 불사(100000 HP) → `Died` 미발화·처치 검증 불가(전투 루프 몫).
