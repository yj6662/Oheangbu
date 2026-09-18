# 계획 — WorldMacro asmdef 분리 · 외부 에셋 참조 조사 (2026-09-18)

**목적: 컴파일 시간 단축.** 용량이 아니다.

> **2026-09-18 분리 실행 완료 — 컴파일 오류 0건.** 단일 어셈블리 442파일 95,462줄이 **EditorTools 222파일 45,397줄 + WorldMacro 220파일 50,067줄**로 나뉘었다. 실행 경위와 실패한 첫 시도는 §4.
>
> 외부 에셋은 조사만 했고 **변경하지 않았다.**

---

## 배경 — 진짜 병목

| 어셈블리 | 파일 | 줄 |
|---|---|---|
| **`Oheangbu.EditorTools`** | **442** | **95,462** |
| `BuildReportTool` | 71 | 41,099 |
| `Oheangbu.App` | 249 | 38,344 |
| `Aura2_Core` | 114 | 23,604 |

`Scripts/Editor/` 전체가 **asmdef 하나**다(하위 asmdef 없음). 따라서 **파일 하나만 고쳐도 95,462줄이 재컴파일**된다.

앞서 검증 스크립트 43개를 지워 6,107줄을 줄였지만 전체의 6%라 체감 효과가 없다. **삭제로는 해결되지 않는 구조적 문제다.**

---

## 1. WorldMacro asmdef 분리

### 분리 가능성 — 실측 결과

| 검사 | 결과 |
|---|---|
| WorldMacro 네임스페이스 | **219파일 전부 `Oheangbu.EditorTools.WorldMacro`** (+ `WorldCompact` 1) |
| **역방향 의존** (바깥 → WorldMacro) | **0건** — `using`/한정 참조 모두 없음 |
| 정방향 의존 (WorldMacro → 바깥) | 5개 클래스 |

순환이 없으므로 분리 가능하다.

### 정방향 의존 5개

| 클래스 | WorldMacro 사용 | 선언 위치 |
|---|---|---|
| **`PrologueAudit`** | **80파일** | `Prologue/PrologueAudit.cs` |
| `DevSceneKit` | 13파일 | `DevSceneKit.cs` |
| `PrologueBuilder` | 2파일 | `PrologueBuilder.cs` |
| `TextureManifest` | 2파일 | `DosaV2PlayerBuilder.cs` |
| `FixedWardReview` | 1파일 | `FixedWardReview.cs` |

`PrologueAudit`은 `CommitRatio()` — 메모리 85% 초과 시 작업을 중단시키는 공용 안전 가드다(전체 91파일이 158회 호출).

### 권장 구조 — 공용 부품을 3번째 asmdef로

```
Oheangbu.EditorTools.Shared   ← PrologueAudit 등 공용 부품 (작음)
        ↑                ↑
Oheangbu.EditorTools    Oheangbu.EditorTools.WorldMacro
  (약 43,000줄)              (약 52,200줄)
```

의존이 한 방향으로 정리되고, WorldMacro 수정 시 **52,200줄만 재컴파일**된다(현재 95,462줄).

대안(WorldMacro asmdef가 기존 EditorTools를 참조)은 더 간단하지만, **EditorTools를 고치면 WorldMacro도 함께 재컴파일**되어 효과가 반감된다.

### 예상 효과

| 작업 대상 | 현재 | 분리 후 |
|---|---|---|
| WorldMacro 수정 | 95,462줄 | **약 52,200줄** (−45%) |
| 그 외 Editor 수정 | 95,462줄 | **약 43,000줄** (−55%) |

**측정하지 않은 추정이다.** 실제 시간은 파일 수·참조 패키지·머신에 따라 달라진다.

### 위험

- `PrologueAudit` 등 5개를 옮기면 **바깥 91파일의 참조도 함께 깨진다.** `Shared`를 양쪽이 참조하도록 asmdef references를 정확히 설정해야 한다.
- **partial class가 어셈블리 경계를 넘을 수 없다.** 오늘 삭제 작업에서 partial을 놓쳐 컴파일 오류 13건을 냈으므로 이번에는 먼저 확인했다 — WorldMacro의 partial 타입 20개 중 **바깥에 조각을 가진 것은 0건**이다. 이 장애물은 없다.
- 첫 컴파일은 오히려 느리다(전체 재빌드).

---

## 2. 외부 에셋 참조 조사

### 방법

`_Project`의 모든 `.unity`·`.prefab`·`.mat`·`.asset`·`.controller`에서 참조 GUID **5,574개**를 수집한 뒤, 각 외부 에셋 폴더의 `.meta` GUID와 대조했다.

### 결과 — 참조 있음 (보존 대상)

| 폴더 | 참조 / 전체 |
|---|---|
| KoreanTraditionalPattern_Effect | 158 / 4,070 |
| HwaseongHaenggung | 67 / 1,516 |
| HwaseongForteressGate | 35 / 850 |
| SeyeonjeongPavilion | 28 / 1,074 |
| House_2 | 24 / 39 |
| House_1 | 22 / 36 |
| Korea_TreasureProps | 16 / 1,108 |
| KoreanTraditionalFestival | 9 / 658 |
| BillemotdonggulLavaTubePack | 8 / 1,017 |
| KTinteractiveProp | 6 / 214 |
| YongmeoriCoast | 4 / 393 |
| JejumokGwana | 2 / 446 |

### 결과 — 참조 0건 (총 19.42 GB)

| 폴더 | 용량 | 성격 |
|---|---|---|
| KTP_Normal | 4.56 GB | 노멀맵 텍스처 |
| KoreanTraditional_SmartMaterials Vol.5 | 3.38 GB | 스마트 머티리얼 |
| KT_SmartMat_Vol_6 | 3.10 GB | 스마트 머티리얼 |
| KoreanTraditional_SmartMaterials Vol.1 | 3.05 GB | 스마트 머티리얼 |
| KTP_Normal_Vol.2 | 2.58 GB | 노멀맵 텍스처 |
| KoreanTraditional_SmartMaterials Vol.3 | 1.47 GB | 스마트 머티리얼 |
| KoreanTraditional_SmartMaterials Vol.2 | 0.66 GB | 스마트 머티리얼 |
| KTP_Decal | 0.41 GB | 데칼 |
| PolyOne | 0.14 GB | — |
| GabrielAguiarProductions | 0.04 GB | VFX |
| Aura 2 | 0.02 GB | 볼류메트릭 라이팅 |
| Hierarchy Designer | 0.00 GB | 에디터 도구 |
| BuildReport | 0.00 GB | 에디터 도구 |

### 거짓 양성 확인 완료

코드 검색에서 `Aura` 18곳·`BuildReport` 8곳이 걸렸으나 **둘 다 무관**했다:
- `Aura` → `FireAura`(화염 오라 술식)의 부분 일치
- `BuildReport` → `DosaV2PlayerBuilder.cs`가 자체 선언한 중첩 클래스

`_Project`의 asmdef 중 이들을 `references`하는 것은 **없다.**

### 그럼에도 남는 판단 — 사용자 몫

**참조 0건이 "안 쓴다"를 뜻하지는 않는다.**

1. **스마트 머티리얼(총 11.6GB)** 은 Substance 계열 소재로, 텍스처를 **굽는 재료**다. 구운 결과물만 씬에 들어가므로 원본은 참조 0건이 정상이다. 지우면 재작업 시 다시 구울 수 없다.
2. **`KTP_Normal`·`KTP_Normal_Vol.2`(7.1GB)** 는 노멀맵 세트다. 현재 미사용이라도 앞으로 쓸 계획이 있을 수 있다.
3. **유료 에셋이면 재구매 전까지 복구 불가**다. 나는 구매 이력을 모른다.
4. `Aura 2`는 볼류메트릭 라이팅 플러그인이다. **발광 상한 규칙**(ArtAudio:29 LOCKED)상 이 프로젝트와 방향이 맞지 않아 보이나, 단정하지 않는다.

**에디터 도구 2개(`BuildReport`·`Hierarchy Designer`)** 는 용량이 0에 가까워 지울 실익이 없다.

> **2026-09-18 정정 — `BuildReport`는 실제 사용 중이다.** GUID 참조 0건은 이 도구가 씬에 배치되는 에셋이 아니라 **에디터 메뉴에서 실행되는 도구**이기 때문이며, "미사용"을 뜻하지 않는다. `C:\Users\yj666\Documents\UnityBuildReports\`에 **리포트 39개**가 생성돼 있다(2026-09-08 26회, **2026-09-14 13회**). 빌드를 낼 때마다 사용된 흔적이다. 제거 후보가 아니다.
>
> 또한 `BuildReportTool`은 **별도 asmdef**라 다른 코드를 수정할 때 재컴파일되지 않는다 — 평소 컴파일 시간에 주는 영향은 사실상 없다. 컴파일 단축의 실효는 WorldMacro 분리 쪽이다.

---

## 3. 권고 순서

1. ~~partial class 경계 확인~~ — **완료. 위반 0건.**
2. **WorldMacro + Shared asmdef 분리** — 컴파일 시간에 가장 큰 효과. 선행 조건은 모두 확인됐다.
3. 외부 에셋은 **사용자가 목록을 보고 판단.** 스마트 머티리얼·노멀맵은 "원본 소재" 성격이라 신중히.
4. `BuildReportTool`(41,099줄) 제거는 컴파일 관점에서 검토 가치가 있다.

### 분리 실행 절차 (권장)

컴파일을 매번 확인할 수 있도록 단계를 쪼갠다.

1. `Shared/` 폴더 생성 → `PrologueAudit`·`DevSceneKit`·`PrologueBuilder`·`FixedWardReview` 이동 + `Oheangbu.EditorTools.Shared.asmdef` 생성 → **컴파일 확인**
2. 기존 `Oheangbu.EditorTools.asmdef`에 `Shared` 참조 추가 → **컴파일 확인**
3. `WorldMacro/Oheangbu.EditorTools.WorldMacro.asmdef` 생성(참조: Shared + 기존 references 중 필요한 것) → **컴파일 확인**

`TextureManifest`는 `DosaV2PlayerBuilder.cs` 안에 선언돼 있어 그대로 옮길 수 없다 — 별도 파일로 추출하거나, WorldMacro 쪽 2파일의 사용처를 확인해 처리한다.

---

## 4. 분리 실행 기록 (2026-09-18)

### 첫 시도 실패 — "공용 부품을 Shared로" 전략은 성립하지 않았다

권장했던 3-asmdef 구조(`Shared` ← `EditorTools`, `WorldMacro`)를 시도했으나 **의존 사슬 때문에 불가능**했다.

```
PrologueAudit → PrologueBuilder → CodexWorldSceneBuilder / DevSceneKit
```

`PrologueAudit`을 `Shared`로 옮기자 `PrologueBuilder`를 14곳에서 찾지 못해 실패했다. `PrologueBuilder`도 함께 옮기자 이번엔 그것이 `CodexWorldSceneBuilder`·`DevSceneKit`을 참조해 실패했다. 공용 가드 하나만 떼어내려 해도 `CodexWorld` 폴더까지 끌려오는 구조다.

**전량 원복했다**(파일 위치·asmdef 참조 모두). 컴파일 0건 확인.

### 채택 — WorldMacro가 EditorTools를 참조

```
Oheangbu.EditorTools  ←  Oheangbu.EditorTools.WorldMacro
   222파일 45,397줄          220파일 50,067줄
```

계획 단계에서 이 방식을 "효과 반감"이라며 낮게 봤으나, **실제 수정 분포를 재보니 반대였다.**

| 최근 7일 수정 | 파일 수 |
|---|---|
| **WorldMacro** | **220** |
| 나머지 EditorTools | 36 |

작업은 거의 전부 WorldMacro에서 일어난다. 이 방향이면 **WorldMacro를 고칠 때 나머지 45,397줄은 재컴파일되지 않는다** — 흔한 경우가 빨라지는 배치다. 반대 방향(나머지를 고칠 때)은 연쇄가 일어나지만 7일간 36파일로 드물다.

### 변경 내역

1. `WorldMacro/Oheangbu.EditorTools.WorldMacro.asmdef` **신규** — 기존 EditorTools의 references를 승계하고 `Oheangbu.EditorTools`를 추가.
2. `WorldMacro/PlaytestAudioInkOutputReview.cs:56` — `SpellVFX120.FixedWardReview.Memory()` → `Prologue.PrologueAudit.CommitRatio()`. **두 구현은 동일**하다(`GetPerformanceInfo`의 CommitTotal/CommitLimit). WorldMacro가 SpellVFX120에 의존하지 않게 하려는 유일한 코드 수정.

`TextureManifest`는 조사에서 외부 의존으로 잡혔으나 **WorldMacro 자체 선언**이었다(거짓 양성). 조치 불필요.

### 검증

- **컴파일 오류 0건** — 로그를 비우고 `ForceUpdate` 재임포트 후 재확인.
- **DLL 분리 확인** — `Library/ScriptAssemblies/`에 `Oheangbu.EditorTools.dll`(2,280KB)과 `Oheangbu.EditorTools.WorldMacro.dll`(2,891KB)이 각각 생성됐다.
- 삭제한 `Shared` asmdef의 잔재 `Oheangbu.EditorTools.Shared.csproj` 제거.

### 미검증

- **실제 컴파일 시간 단축폭 — 측정하지 않았다.** 줄 수는 절반이 됐으나 체감 시간은 참조 패키지 로드·도메인 리로드가 함께 좌우하므로 비례하지 않는다. 다음 작업에서 체감으로 확인해야 한다.
- 플레이 모드 동작 — 에디터 전용 어셈블리라 런타임 영향은 없어야 하나 확인하지 않았다.

---

## 5. 플레이 모드 진입 단축 (2026-09-18)

### 원인 — 컴파일이 아니라 도메인 리로드

Editor.log 실측에서 리프레시마다 **`compile time=0~2ms`**, **`domain reload time=6,047~34,369ms`** 였다. 앞선 asmdef 분리는 컴파일을 겨냥했으나 **플레이 진입이 느린 원인과는 무관했다.**

설정도 어긋나 있었다: `m_EnterPlayModeOptionsEnabled: 1`인데 `m_EnterPlayModeOptions: 0` — 기능 체크박스만 켜고 하위 옵션(도메인·씬 리로드 끄기)은 아무것도 끄지 않은 상태라 **효과가 전혀 없었다.**

### 변경

1. `ProjectSettings/EditorSettings.asset` — `m_EnterPlayModeOptions: 0 → 1` (**DisableDomainReload**). 에디터 API로 설정 후 `AssetDatabase.SaveAssets()`로 저장, 파일 변경 1줄.
2. **씬 리로드는 유지**했다. 측정된 병목이 아니고, 끄면 `[ExecuteAlways]` 스크립트의 편집 모드 상태가 섞이는 별도 위험이 있어 범위에서 뺐다.
3. 런타임 정적 상태 4곳에 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 리셋 추가:

| 파일 | 필드 | 이유 |
|---|---|---|
| `Combat/AttackProvenance.cs` | `_sequence` | 공격 ID가 세션마다 1부터 시작하던 동작 복원. 구조체라 중첩 정적 클래스에 둠 |
| `App/Demo/CheongryongColliderPoseSync.cs` | `_lastGlobalSyncFrame` | 이전 세션 프레임 번호와 우연히 겹쳐 동기화를 건너뛰는 것 방지 |
| `App/SpellVFX120/Vfx120Effect.BambooGuard.cs` | `_activeBambooGuard` | 이전 세션의 파괴된 가드 참조 제거 |
| `App/SpellVFX120/Vfx120Effect.FireGuard.cs` | `_activeFireGuard` | 동일 |

### 정적 상태 감사 결과 — 리셋 불필요로 판정한 것

- `WorldMacroPlayerSkinningLease` — **이미 `SubsystemRegistration` 리셋이 있었다**(주석: "도메인 리로드 비활성 진입도 처리"). 사전 대비돼 있었다.
- `DemoEscortNavigationSeam.Active` — `OnEnable` 추가/`OnDisable` 제거 짝.
- `PlaytestUiRoot.Instance` — `OnDestroy`에서 `null`. `DiagnosticSuffix`는 `SessionState`에서 계산(상태 없음).
- `PlaytestUiBuildSmoke.started` — `Application.isEditor`면 즉시 반환 → 에디터 무관.
- `QuadMesh._value` — `!= null` 검사로 파괴 시 재생성. `TimeId` — 매 `OnEnable` 재할당.
- 상수 조회 배열(도감 카탈로그 등)·스크래치 버퍼(`s_groundHits`) — 내용물이 누적되지 않음.
- **SO 이벤트 채널** — `Subscribe`/`Unsubscribe` 6파일 전부 짝 일치.
- **Unity 정적 이벤트**(`sceneLoaded`·`activeSceneChanged`·`logMessageReceived`·`beginCameraRendering`) — 전부 `+=`/`-=` 짝 일치.

### 실측 결과

| | 변경 전 | 변경 후 |
|---|---|---|
| 플레이 진입 도메인 리로드 | **12,637ms** | **0회** |
| 씬 로드 (유지) | — | 2,105ms / 2,584ms |
| 진입 요청 → 플레이 확인 | — | 약 3.6초 (MCP 왕복 포함 상한) |
| 플레이 종료 시 도메인 리로드 | — | 0회 |

**두 번 연속 진입**해 정적 상태 이월을 시험했다. 두 세션 모두 오류·예외 0건, `encounter agent 37개 바인딩`·그림자 아틀라스 경고까지 동일한 초기화 로그가 나왔다. 컴파일 오류 0건.

### 미검증

- **추가한 리셋 4개가 실제로 효과를 내는 흐름은 플레이하지 않았다.** 대나무·화염 가드 패링, 청룡 충돌 동기화, 공격 ID 연속성은 해당 술식·전투를 실제로 거쳐야 드러난다. 두 번 진입 시험은 "진입 자체가 깨지지 않음"을 보인 것이지 각 리셋의 동작 증명이 아니다.
- **에디터 스크립트의 정적 상태는 감사하지 않았다**(442파일). 이제 플레이 진입으로 초기화되지 않으므로, 에디터 도구의 정적 작업 상태가 플레이 모드를 넘어 유지된다. MCP 다단계 작업에는 오히려 유리하지만, 매 진입 초기화를 전제한 도구가 있다면 달라진다.
- 기준값 12,637ms는 1회 측정이다. 과거 로그상 6~34초로 편차가 크다.

### 앞으로의 규약

**새 런타임 정적 필드를 추가할 때는 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 리셋을 함께 둔다.** 도메인 리로드가 꺼져 있어 정적 필드는 플레이 세션 사이에 초기화되지 않는다. 이상 동작 시 Editor Settings > Enter Play Mode Options에서 Reload Domain을 다시 켜서 원인을 가를 수 있다.

---

## 미검증 (외부 에셋)

- 외부 에셋의 구매 이력·향후 사용 계획 — 알 수 없다.
- 참조 조사는 GUID 기반이다. `Resources.Load` 등 **문자열 경로 로드는 잡지 못한다**(코드 검색으로 보완했으나 완전하지 않다). `BuildReport`가 이 한계의 실례였다 — GUID 0건이었으나 실제로는 사용 중이었다.
