# 진단 — 디스크 용량 · 코드 부채 (2026-09-18)

실측 기반 진단서.

> **2026-09-18 정리 실행 완료 — 221GB → 101.5GB (약 120GB 회수).** 사용자 승인 하에 `Builds/` 전체, `Oheangbu/Library`, `Art/`의 검증용 이미지·영상을 삭제했다. 상세는 §4.

---

## 1. 디스크 용량 — 전체 221GB

| 위치 | 용량 | 성격 |
|---|---|---|
| `Oheangbu/Library` | **70 GB** | Unity 임포트 캐시 — **재생성 가능** |
| `Oheangbu/Assets` | **60 GB** | 에셋 본체(유료 팩 다수) |
| `Art/` | **56 GB** | 작업 검증용 렌더 이미지 |
| `Builds/` | **25 GB** | 과거 빌드 산출물 |
| `Oheangbu/Screenshots/` | **8.5 GB** | 검증 캡처 — 이미 `.gitignore` 처리됨 |
| `Tools/` | 1.9 GB | 스크립트·런 로그 |
| `Docs/` | 47 MB | 문서 |

### 1-1. `Library` 69.9GB — 즉시 회수 가능, 위험 없음

Unity가 에셋을 임포트하며 만든 캐시다. 지워도 Unity가 다시 만든다(재임포트에 시간이 걸릴 뿐). git에도 추적되지 않는다.

**단, 지금 지우면 안 된다** — Unity가 실행 중이고 백드랍 작업 씬이 열려 있다. Unity 종료 후에 지워야 한다.

### 1-2. `Art/` 56GB — 실제 낭비의 핵심

| 하위 | 용량 |
|---|---|
| `PlayerV2/` | 20 GB (그중 `Inspect/` **13 GB**) |
| `SpellVFX120/` | 17 GB |
| `PlayerPhase1/` | 9.4 GB |
| `World/` | 6.0 GB |
| 나머지 | ~4 GB |

**구성: PNG 50,476개(15.3GB) + JPG 29,532개(7.3GB) + MP4 678개(0.3GB).**

성격은 작업 중 전후 비교 렌더다. 예: `Art/PlayerV2/Inspect/ArmLiningClearance/after_elbow_120.png` — 팔꿈치 120도 굽힘 시 옷 간섭을 확인한 이미지. 장당 평균 2.3MB의 고해상도 PNG이며, `PlayerV2/Inspect/` 한 폴더에만 5,639개가 있다.

**`Art/`는 git 추적되지 않으며 `.gitignore`에도 없다** — 즉 `git status`에 `?? Art/`로 계속 뜨지만 커밋된 적은 없다(현재 추적 0개).

### 1-3. git 저장소 846MB — 과거에 커밋된 이미지·영상

```
pack   696.6 MB
loose  149.2 MB   ← gc 미실행 상태
garbage 18.9 MB   ← tmp_obj_* 16개 잔해
```

**2026-09-18 실행 결과 — 기대보다 회수량이 적었다.**

`git gc` 후: loose 149MB → 0, 그러나 pack이 696.6 → 833.8MB로 증가. **총량은 846 → 834MB로 12MB만 감소.** loose 객체는 버려진 쓰레기가 아니라 아직 pack에 들어가지 않은 **실제 이력 데이터**였고, gc는 그것을 삭제한 게 아니라 압축해 pack에 편입시켰다.

`tmp_obj_*` 16개(18.9MB)는 별도로 제거했다 — 파일명이 SHA 형식이 아니라 git이 객체로 인식조차 하지 못하는, 중단된 write의 잔해다. `git fsck --connectivity-only` 통과.

**결론: git 저장소의 실질 회수는 약 31MB(12 + 19)에 그친다.** 나머지 834MB를 줄이려면 이력 재작성밖에 없다.

**이력 전체의 확장자별 누적:**

| 확장자 | 누적 | 개수 |
|---|---|---|
| `.png` | **449.7 MB** | 2,147 |
| `.mp4` | **235.0 MB** | 475 |
| `.prefab` | 91.0 MB | 432 |
| `.asset` | 89.9 MB | 536 |
| `.json` | 85.6 MB | 2,260 |
| `.unity` | 35.2 MB | 37 |
| `.cs` | 9.3 MB | 755 |

**PNG+MP4가 685MB로 전체의 약 82%.** 그중 `Art/SpellVFX120/` 한 경로가 **695MB / 2,756개**로 사실상 전부다. 이 경로는 **현재 추적되지 않으므로**, 과거에 커밋했다가 나중에 추적 해제한 것이다 — 파일은 사라졌지만 이력에는 영구히 남아 있다.

단일 최대 blob: `Dressing_CaveAware.asset` 20.2MB, `Dressing_Dense.asset` 20.2MB, `W_WorldMacro_Playtest.unity` 20.0MB. 각 1회씩이라 누적 문제는 아니다.

### 1-4. 회수 가능량 요약

| 조치 | 회수 | 되돌릴 수 있나 |
|---|---|---|
| `Library` 삭제 (Unity 종료 후) | **~70 GB** | 예 — 자동 재생성 |
| `git gc --prune` | ~150 MB | 예 — 잔해 정리일 뿐 |
| `Builds/` 과거 빌드 정리 | ~20 GB | 아니오 |
| `Art/` 종료 트랙 렌더 정리 | ~40 GB | **아니오 — 검증 증거 소실** |
| git 이력에서 PNG/MP4 제거 | ~685 MB | **아니오 — 전 커밋 해시 변경** |

---

## 2. 코드 부채 — 825파일 / 153,295줄

### 2-1. 구조 규약은 대체로 건강하다

CLAUDE.md의 아키텍처 절대 규칙을 실측한 결과, **위반이 거의 없다.**

| 항목 | 결과 |
|---|---|
| 전역 싱글턴 | **1건** (`App/World/UI/PlaytestUiRoot.cs`) |
| Odin 런타임 침범 | **0건** |
| 런타임 `FindObjectOfType` | **5건** (App 레이어) |
| 코루틴 사용 | 10파일 |

`FindObjectOfType` 240건 중 **235건은 Editor 도구**다. 에디터 스크립트가 씬을 훑는 것은 정상 용법이므로 부채가 아니다.

### 2-2. 실질적 부채 — 에디터 툴의 헬퍼 중복

`Scripts/Editor/`에 같은 코드가 반복 구현돼 있다.

| 패턴 | 재구현 파일 수 |
|---|---|
| `JsonUtility.ToJson(..., true)` 리시트 직렬화 | **338** |
| `EditorApplication.isPlaying` 가드 | **280** |
| `PrologueAudit.CommitRatio()` 85% 중단 | **100** |
| `SHA256.Create()` 다이제스트 | **78** |
| `Bounds.Encapsulate` 렌더러 경계 합산 | **34** |

같은 계약(가드 → 실행 → 리시트)을 338곳이 각자 구현한다. 공통 베이스 하나로 묶으면 규약 변경 시 한 곳만 고치면 된다. **다만 이 코드들은 MCP로만 호출되고 자동 테스트가 없어, 묶는 순간 회귀를 즉시 감지할 수단이 없다.**

### 2-2b. 하드코딩 전수 스캔 (2026-09-18 추가)

**레이어별 규모**

| 레이어 | 파일 | 줄 |
|---|---|---|
| Editor | 478 | **101,569** |
| App | 249 | 38,344 |
| Presentation | 18 | 5,426 |
| Data | 38 | 3,640 |
| Combat | 19 | 2,118 |
| Drawing | 5 | 937 |
| BrushRender | 5 | 758 |
| Core | 11 | 302 |

Editor가 전체의 2/3이고 런타임 핵심(Core·Drawing·Spellcraft)은 매우 얇다. 구조적으로 건강한 분포다.

#### 위반이 아닌 것 — 오해하기 쉬운 항목

수치 리터럴은 많지만 **대부분 규약 위반이 아니다.**

- **Combat 128개** — 거의 전부 `CombatConfigSO.cs`의 `[SerializeField]` 기본값이며 `[Min]`/`[Range]` 검증이 붙어 있다. SO 기본값이지 하드코딩이 아니다.
- **App 1,429개** — VFX 이펙트에 집중(`ThornRiseEffect` 180, `HarvestInkStreamEffect` 159, `SandStormEffect` 117…). 전부 `[SerializeField]`이고, `ThornRiseEffect` 헤더에 **"판정 무접촉: AreaImpactPlan은 읽기만 — 연출≠실판정"**이 명시돼 있다. 시각 연출 파라미터이지 밸런스가 아니다.
- **런타임 에셋 경로 하드코딩 2건** — 매우 양호.
- **TODO/FIXME/HACK 0건.**

#### 실제 위반 — 승인된 설계 수치가 `const`로 박힘

`[SerializeField]`가 아닌 순수 상수 중, **DECISIONS/Spec에 기록된 게임 규칙 수치**가 코드에 고정돼 있다.

| 위치 | 상수 | 문서 근거 |
|---|---|---|
| `App/Demo/FieldSpellService.cs:18` | `MaximumHeight = 2.4f` | PROJECT_STATUS "국2.4m승강" |
| `App/Demo/FieldSpellService.cs:19` | `HoldSeconds = 20f` | DECISIONS "승인된 활동20초" |
| `App/Demo/DemoEscortState.cs:71` | `MaximumInteractionDistance = 4.5f`, `MaximumCompanionDistance = 8f` | 호송 규칙 |
| `App/World/WorldMacroPlaytestSession.EscortResume.cs:11` | `MaximumEscortResumeDistance = 32` | 회수 거리 |
| `App/World/UI/MapDiscovery.cs:10-11` | `CellSize = 32f`, `RevealRadius = 96f` | 지도 공개 규칙 |

CLAUDE.md의 "수치·밸런스는 코드에 하드코딩하지 않고 SO/CSV로" 조항에 어긋난다. 밸런스 조정 시 코드 수정·재컴파일이 필요하고, 문서와 코드가 따로 움직일 수 있다.

**반례로 잘 된 것:** `CameraRigController.cs:13`의 `BoomRadius`와 `EnemyController.cs:46`의 `ParriedFlashDuration`은 주석에 **"기술 상수(밸런스 아님)"**이라 명시해 의도를 방어했다. 이 관행을 위 목록에도 적용하거나, 밸런스면 SO로 옮기는 것이 맞다.

#### 실제 위반 — 씬 경로 문자열 중복

`WorldMacroCompactAuthoring.TargetScene` 상수가 있고 **34곳이 올바르게 참조**하는데, **10곳은 같은 경로를 문자열로 직접 쓴다**(`InkPaintingFormMapBaker`, `InkPaintingMountainApplication`, `InkPaintingNavigationUpdate` 등). 씬 이름이 바뀌면 이 10곳만 조용히 깨진다.

`C2_CodexWorld.unity`는 23곳에 흩어져 있으며 상수가 없다.

#### 기타

- **GUID 하드코딩 7건** (`DevSceneKit.cs`, `InkPaintingLeafPadding.cs`, `WorldMacroCompactAuthoring.Audit.cs`) — 에셋이 재생성되면 끊긴다.
- **런타임 `Debug.Log` 23건** — 빌드에 남는다.
- **빈 catch 2건** — 예외를 조용히 삼킨다.

### 2-3. 대형 파일 (분할 후보)

| 파일 | 줄 |
|---|---|
| `Editor/Validation/DosaV2ClothDiagnostics.cs` | 1,393 |
| `App/SpellVFX120/Vfx120Effect.cs` | **1,327** ← 런타임 |
| `Editor/DosaV2SecondaryBuilder.cs` | 1,291 |
| `Editor/WorldMacro/PlaytestMenuReview.cs` | 1,139 |
| `App/BrushStrokeFeedAdapter.cs` | **1,087** ← 런타임 |
| `App/ThornRiseEffect.cs` | **1,056** ← 런타임 |

런타임 3개가 1,000줄을 넘는다. 에디터 툴은 partial 분할 관행이 이미 있으나(`InkPaintingMountainForm`은 7개 partial), 런타임은 단일 파일이다.

### 2-4. 이번 세션 코드 (CompactBackdropRing, 1,298줄)

시행착오의 잔해가 남아 있다.

- `CompactBackdropRingProfile.MidAirRange` — 선언했으나 실제 경로에서 미사용
- `CompactBackdropRing.Build.cs`의 `SkirtTopY` / `RequiredSummitClearance` — static 가변 필드로 두어 호출 순서에 의존(`Build()`가 먼저 채워야 `IntersectsForbidden`이 동작). 파라미터로 넘기는 편이 안전
- bounds 측정이 `probe`와 `build`에 각각 구현됨 — 한 헬퍼로 묶을 수 있음
- `ProbeRow.achievedSizeDirect` 계열 — 축 매핑이 확정된 지금은 진단 흔적. 남길지 정리할지 판단 필요
- `ViewPose.fieldOfView` 등 일부 필드는 캡처 경로에서만 쓰여 SO로 뺄 여지

---

## 3. 권고

### 즉시 (위험 없음)

**1·2는 2026-09-18에 실행 완료.**

1. ~~`.gitignore`에 `/Art/`·`/Builds/` 추가~~ — **완료.** 규칙이 없어 언제든 다시 커밋될 수 있었다(과거에 그렇게 685MB가 박혔다). 파일 자체는 REPORT 문서가 상대경로로 참조하므로 지우지 않았다.
2. ~~`git gc` + `tmp_obj_*` 제거~~ — **완료. 약 31MB 회수(기대보다 적음, §1-3 참조).**
3. **Unity 종료 후 `Library` 삭제** — 약 70GB 회수, 자동 재생성. **미실행** — Unity가 실행 중이고 백드랍 씬이 열려 있어 지금 지우면 안 된다.

3번이 위험 없는 회수의 대부분(**약 70GB**)이다.

### 판단 필요 (되돌릴 수 없음)
4. `Art/` 종료 트랙 렌더 정리 — 약 40GB. **어느 트랙이 끝났는지는 사용자만 안다.** REPORT/REVIEW 문서가 참조 중인 이미지가 섞여 있어, 지우면 검토 페이지가 깨진다.
5. `Builds/` 과거 빌드 — 약 20GB. 최신 후보만 남기는 것이 일반적.
6. git 이력 재작성 — 685MB. **모든 커밋 해시가 바뀐다.** 원격·협업자가 있으면 위험하고, 이 저장소는 이미 PR 이력(#11 등)이 있다.

### 리팩토링
7. 에디터 툴 공통 베이스 추출 — 효과는 크지만 **자동 테스트가 없어 회귀 감지 수단이 없다**. 한 모듈(예: WorldMacro Compact 계열)로 좁혀 시범 적용 후 확대하는 편이 안전하다.
8. 이번 세션 코드 정리 — 범위가 작고 `build`/`probe` 재실행으로 즉시 검증 가능하다.

---

## 4. 정리 실행 기록 (2026-09-18)

Unity 종료 확인 후 실행했다.

| 대상 | 회수 | 비고 |
|---|---|---|
| `Oheangbu/Library` | **69.85 GB** | Unity가 다음 실행 시 자동 재생성 |
| `Builds/` | **24.35 GB** | 과거 빌드 3,096파일 |
| `Art/` 이미지·영상 | **22.79 GB** | PNG·JPG·MP4 80,663개 |
| 빈 폴더 정리 | — | 876개 |
| **합계** | **약 120 GB** | 221 GB → **101.5 GB** |

### 삭제하지 않고 보존한 것

**`Art/`는 스크린샷 전용 폴더가 아니었다.** 삭제 직전 확장자별로 열어보니 최대 항목이 이미지가 아니라 **`.blend` 309개(17.9GB)** 였다 — `DosaV2_Assembled.blend`(475MB) 등 Blender 편집 원본이다. Unity `Assets/`에는 FBX 1,637개만 있고 `.blend`는 **0개**이므로, 이 원본을 지웠다면 캐릭터·소환수 재편집이 영구히 불가능해졌을 것이다.

사용자 확인을 거쳐 이미지만 삭제하고 다음을 보존했다:

- `.blend` 309개 + `.blend1` 70개 (21.5GB) — 유일한 편집 원본
- `.fbx` 273개 · `.glb` 83개 · `.zip` 9개 — 3D 중간 산출물
- `.json` 22,606개 (1.9GB) — 리시트·검사 기록
- **`MeshySources/` 하위 이미지 27개** — `T_0_base_color.png` 같은 **실제 모델 텍스처**가 섞여 있어 경로째 제외했다. 미리보기 PNG와 텍스처가 같은 폴더에 있어, 확장자만 보고 지웠다면 에셋이 깨졌을 것이다.

### 되돌릴 수 없는 손실

`Art/`의 전후 비교 렌더 80,663장이 사라졌다. REPORT·REVIEW 문서가 상대경로로 참조하던 이미지가 포함되므로 **과거 검토 페이지의 이미지는 깨진다.** 문서의 서술과 수치는 그대로 남아 있다. 사용자가 이 손실을 인지하고 승인했다.

`Builds/` 24.35GB도 복구 불가다. 필요하면 재빌드해야 한다.

### 다음 Unity 실행 시

`Library`가 없으므로 **전체 재임포트가 일어난다.** Assets 60GB 규모라 상당한 시간이 걸리며, 이는 정상 동작이지 오류가 아니다.

---

## 5. 검증 스크립트 정리 (2026-09-18)

### 삭제 전에 드러난 함정

"검증용 스크립트 전부 삭제" 지시를 그대로 실행했다면 **프로젝트가 컴파일 실패했을 것이다.**

이름에 `Audit`/`Review`/`Validation`/`Diagnos`/`Smoke`가 들어간 파일은 137개였지만:

- **58개가 서로 참조**한다.
- **`PrologueAudit`은 91개 파일이 158회 호출**한다. 이름과 달리 검증 도구가 아니라 `CommitRatio()` — 시스템 메모리 85% 초과 시 작업을 중단시키는 **공용 안전 가드**다. 모든 저작 도구의 `Guard()`가 이것에 의존한다.
- 참조 0건인 44개조차 **전부 최근 30일 내 수정**됐다(가장 오래된 것이 9/15).

### 실행 내역

사용자 확인을 거쳐 **참조 0건 + 종료 트랙**만 삭제했다.

| 항목 | 값 |
|---|---|
| 삭제 | **43파일** (+ `.meta` 43개) |
| Editor 규모 | 478파일 101,569줄 → **435파일 93,065줄** (−8,504줄) |
| 백업 | `tmp/editor-validation-backup-20260918/` (742KB, 42파일) |

삭제된 트랙: Dosa 리그 검증 13, PlayerPhase1 2, NaturalCave 3, Palanquin 3, VisualCorridor 2, AssetIntake 2, Playtest 계열 5 등. STATUS 최종 언급일이 9/9~9/15로, 현재 진행 중인 산 아트 작업과 무관하다.

### 보존한 것

- **`PrologueAudit`** — 공용 안전 가드(위 참조).
- **`CodexMountainAudit` / `CodexMountainLODAudit`** — `CodexMountain` 셰이더를 쓰는 재질은 0개지만 `C2_CodexWorld.unity` 씬이 아직 존재해 제외했다.
- 서로 참조되는 58개 및 진행 중 작업 도구.

### 검증 — 1차 실패, 원인과 교정

**삭제 직후의 정적 검사는 "코드 참조 0건"으로 통과를 보고했으나 이는 틀렸다.** Unity 컴파일에서 **오류 13개**가 발생했다.

```
WorldMacroVisualCorridorAuthoring.cs(46,39): CS0103 'ValidateGeometry' 없음
WorldMacroPlaytestAuthoring.cs(44,97):  CS0117 'WorldMacroPlayerAppearanceAuthoring'에 'Poll' 정의 없음
WorldMacroPlayerReRigGait.cs(117,43):   CS0103 'Finite' 없음
... 외 10건
```

**원인: `partial class`를 고려하지 않은 검사.** 삭제한 파일들은 독립 클래스가 아니라, 살아있는 파일과 **같은 클래스를 나눠 갖는 partial 조각**이었다. 검사는 *클래스명*이 다른 곳에서 쓰이는지만 확인했는데, 실제 참조는 그 조각이 제공하던 *메서드명*(`ValidateGeometry`, `Poll`, `Finite` 등)으로 일어난다. 클래스명 기준으로는 "고립"으로 보였다.

**교정:** 백업에서 7개를 복원해 의존을 닫았다.

| 복원 파일 | 제공하던 멤버 |
|---|---|
| `WorldMacroVisualCorridorMaterialReview.cs` | `ValidateGeometry`, `OptimizeMaterials` |
| `WorldMacroVisualCorridorValidation.cs` | `InstallClearance` |
| `WorldMacroPlayerAppearanceReview.cs` | `Poll`, `BeginRuntimeReview` |
| `WorldMacroPlayerReRigReview.cs` | `RuntimeInspection`, `ValidateGestureMath`, `ValidateSkinningLease`, `CaptureCandidate` |
| `PlaytestPolishVegetationReview.cs` | `Poll` |
| `DosaV2SecondaryCollisionDiagnostics.cs` | `Finite` |
| `UnityEditorValidationV2StaticPoseAudit.cs` | `BlenderRotationDelta` — **백업에 없어 git `96ddbf48`에서 복원** |

복원 스크립트의 경로 버그로 `Editor/c/Users/...` 중첩 폴더가 생겼던 것도 제거했다.

### 최종 상태

| 항목 | 값 |
|---|---|
| 삭제 유지 | **36파일** |
| Editor 규모 | 478파일 101,569줄 → **442파일 95,462줄** (−6,107줄) |
| 고아 `.meta` | 0개 |
| **컴파일·플레이** | **사용자가 직접 확인 — 문제 없음** |

git 추적분은 `FlameJetAudit.cs` 1개뿐이며 `git checkout`으로 복구 가능하다. 나머지 35개는 미추적이므로 `tmp/editor-validation-backup-20260918/`이 유일한 복구 수단이다.

### 교훈

C# partial class가 쓰이는 코드베이스에서 **클래스명 기준 참조 검사는 삭제 안전성을 증명하지 못한다.** 다음에 같은 작업을 한다면 (a) 파일이 선언한 멤버를 추출해 멤버명으로 검사하거나, (b) 한 번에 하나씩 지우고 매번 컴파일하는 편이 옳다. 정적 검사의 "통과"를 컴파일 통과로 보고해서는 안 된다.

### 복구

삭제 대상 44개 중 **43개가 git 미추적**이었다(커밋된 적 없음). 따라서 `tmp/editor-validation-backup-20260918/`이 **유일한 복구 수단**이다. 이 백업을 지우면 영구 소실된다.

---

## 측정 방법

- 디스크: `du -sh` (Git Bash) 및 `Get-ChildItem -Recurse | Measure-Object Length -Sum` (PowerShell)
- git: `git count-objects -vH`, `git rev-list --objects --all | git cat-file --batch-check`
- 코드: `grep -rn --include="*.cs"`, `wc -l`

`Library`/`Assets` 수치는 PowerShell 실측이며 `du`는 타임아웃으로 미완료였다. 두 방법의 차이는 대조하지 않았다.
