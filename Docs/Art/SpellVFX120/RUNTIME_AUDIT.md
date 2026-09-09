# 술식 VFX 120 런타임 교체 접점 감사

2026-09-09. 현재 저장된 코드·SO·CSV를 읽은 결과다. 이 감사에서 Unity/Blender를 실행하거나 게임 코드를 변경하지 않았다. 아래 실행 검사들은 아직 수행하지 않았으며 **미검증**이다.

## 결론

인식·피해·패링·먹 소비를 건드리지 않고 신규 연출을 넣는 접점은 **`BrushStrokeFeedAdapter`의 글자별 시각 선택과 `SpawnPattern`**이다. 기존 15개 게임 술식은 동일 판정 문맥을 신규 VFX에 전달할 수 있다. 다만 **CSV는 120슬롯, 실제 규칙 SO와 시각 SO는 각각 15슬롯**이다. 나머지 105슬롯을 검수하는 독립 시각 재생기를 만들어야 하며, 시각 카탈로그 120개를 게임 효과 120개 구현으로 보고해서는 안 된다.

CSV의 20슬롯은 의도적인 `공백/LOCKED`다. 120개 신규 시각을 제작하더라도 이 20개는 **Reserved의 독립 아트 미리보기**로 식별하고 게임에서는 기존 불성립을 유지한다. 게임 효과를 새로 배정하는 것은 표현 교체와 다른 규칙 변경이다.

새 연출은 새 메시·텍스처·재질·배치·타임라인을 가진 120개 레시피로 제작한다. 공용 렌더링/풀/궤적 계산 코드는 공유해도 되지만 기존 `SpellFx_*`, 구매 문양, 생성 모델, 기존 파생 Effect의 연출 구성을 새 작품으로 세어 재사용하지 않는다.

## 보존할 경계와 실제 연결

| 접점 | 현재 동작과 보존 계약 | 근거 |
|---|---|---|
| 입력/인식 | Point 입력은 프레임당 한 곳에서 수집. 원시 `StrokePoint.Time`은 unscaled. 성공 시 DrawnLetter 방송 후 Committed 순서를 유지한다. | [DrawingInputController.cs:153](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Drawing/DrawingInputController.cs:153), [242](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Drawing/DrawingInputController.cs:242), [274](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Drawing/DrawingInputController.cs:274) |
| 규칙 해석 | `SpellResolver.TryResolve`가 `SpellBookSO` 등재를 확인하고 필세를 곱한 위력·속성·형상을 확정한다. 미등재는 false. VFX 제작을 위해 이 메서드나 Book을 넓히지 않는다. | [SpellResolver.cs:42](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Spellcraft/SpellResolver.cs:42), [SpellBookSO.cs:8](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Spellcraft/SpellBookSO.cs:8) |
| 효과 판정 | `CombatLoopWiring`이 먹 지불, 패링 창, 목표/기하, PendingCast를 소유한다. `Time.time`의 ImpactTime에 피해를 적용한다. VFX 충돌/애니메이션 완료 이벤트로 피해를 재발행하지 않는다. | [CombatLoopWiring.cs:157](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:157), [198](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:198), [223](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:223), [445](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:445) |
| 표현 이벤트 큐 | 입력 시점의 scaled/unscaled 시각·순서를 저장하고 LateUpdate에서 순서대로 소비한다. 판정 채널을 다시 발행하지 않는다. | [BrushStrokeFeedAdapter.cs:214](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:214), [248](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:248) |
| 글자→VFX | `SpellVisualSetSO.Entry`는 Letter/FxPrefab/ScaleMul/ArcHeight. 게임 규칙 SO와 분리되어 있다. `_pendingFxPrefab`은 매 커밋마다 초기화/조회된다. | [SpellVisualSetSO.cs:14](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/SpellVisualSetSO.cs:14), [BrushStrokeFeedAdapter.cs:455](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:455) |
| 최종 생성 | 글자의 월드 bounds 중심과 실제 크기를 계산하여 Instantiate한다. 현 자체 Sequence 호출은 `_pendingAttack`일 때만 실행된다. 패링에 신규 Sequence를 넣기만 하면 Begin이 호출되지 않는다. | [BrushStrokeFeedAdapter.cs:546](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:546), [564](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:564), [605](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:605) |
| 판정 시계 전달 | `SetImpactClock(duration)`와 `SetAreaPlan(plan)`을 Begin 직전에 호출한다. Cone.Point는 시전자 기점이므로 전방 끝점으로 환산하는 기존 분기를 보존한다. | [SpellSequenceEffect.cs:11](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/SpellSequenceEffect.cs:11), [BrushStrokeFeedAdapter.cs:572](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:572) |
| 광역 문맥 | AreaImpactPlan의 Point/Direction/Radius/Length/Speed/Delay와 Shots의 개별 Target/ImpactTime은 읽기 전용으로 소비한다. 클래스가 mutable이므로 신규 연출이 원본 계획을 수정하지 않도록 값 사본을 받는 편이 안전하다. | [AreaImpactPlan.cs:8](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/AreaImpactPlan.cs:8), [18](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/AreaImpactPlan.cs:18) |

`SpellKind`는 AttackSingle/AttackArea/Parry 세 종류뿐이다. CSV에 있는 버프·설치·소환·필드가 전부 실행된다고 가정할 수 없다. 현재 미등재 글자는 `NotifyCastFailed`를 통해 일반 불발 표현으로 간다([CombatLoopWiring.cs:202](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:202), [BrushStrokeFeedAdapter.cs:485](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:485)).

## 120슬롯 열거와 기존 실행 범위

정본 [오행부_작도어휘_v0_1.csv](C:/Users/yj666/Oheangbu/Docs/오행부_작도어휘_v0_1.csv:1)는 실제 120행이며 키는 중복 없는 완성형 한 글자다. 아래 순서로 안정적인 **0~119 slot index**를 만들되 저장 키는 글자/U+코드로 유지한다. `elementIndex × 24 + medialIndex × 6 + finalIndex`: 초성 ㄱ·ㄴ·ㅁ·ㅅ·ㅇ, 중성 ㅏ·ㅓ·ㅗ·ㅜ, 종성 무·ㄱ·ㄴ·ㅁ·ㅅ·ㅇ. CSV 순서를 그대로 검증하며 임의 Unicode 전수 열거는 하지 않는다.

| 슬롯 | 속성/중성 | 무 | ㄱ | ㄴ | ㅁ | ㅅ | ㅇ | 기존 규칙 실행 |
|---|---|---|---|---|---|---|---|---|
| 0–5 | 목/ㅏ | 가 | 각 | 간 | 감 | 갓 | 강 | 가 |
| 6–11 | 목/ㅓ | 거 | 걱 | 건 | 검 | 것 | 겅 | 거 |
| 12–17 | 목/ㅗ | 고 | 곡 | 곤 | 곰 | 곳 | 공 | 고 |
| 18–23 | 목/ㅜ | 구 | 국 | 군* | 굼* | 굿* | 궁* | 없음 |
| 24–29 | 화/ㅏ | 나 | 낙 | 난 | 남 | 낫 | 낭 | 나 |
| 30–35 | 화/ㅓ | 너 | 넉 | 넌 | 넘 | 넛 | 넝 | 너 |
| 36–41 | 화/ㅗ | 노 | 녹 | 논 | 놈 | 놋 | 농 | 노 |
| 42–47 | 화/ㅜ | 누 | 눅* | 눈 | 눔* | 눗* | 눙* | 없음 |
| 48–53 | 토/ㅏ | 마 | 막 | 만 | 맘 | 맛 | 망 | 마 |
| 54–59 | 토/ㅓ | 머 | 먹 | 먼 | 멈 | 멋 | 멍 | 머 |
| 60–65 | 토/ㅗ | 모 | 목 | 몬 | 몸 | 못 | 몽 | 모 |
| 66–71 | 토/ㅜ | 무 | 묵* | 문* | 뭄 | 뭇* | 뭉* | 없음 |
| 72–77 | 금/ㅏ | 사 | 삭 | 산 | 삼 | 삿 | 상 | 사 |
| 78–83 | 금/ㅓ | 서 | 석 | 선 | 섬 | 섯 | 성 | 서 |
| 84–89 | 금/ㅗ | 소 | 속 | 손 | 솜 | 솟 | 송 | 소 |
| 90–95 | 금/ㅜ | 수 | 숙* | 순* | 숨* | 숫 | 숭* | 없음 |
| 96–101 | 수/ㅏ | 아 | 악 | 안 | 암 | 앗 | 앙 | 아 |
| 102–107 | 수/ㅓ | 어 | 억 | 언 | 엄 | 엇 | 엉 | 어 |
| 108–113 | 수/ㅗ | 오 | 옥 | 온 | 옴 | 옷 | 옹 | 오 |
| 114–119 | 수/ㅜ | 우 | 욱* | 운* | 움* | 웃* | 웅 | 없음 |

`*`는 CSV의 의도적 공백 20개다. CSV 분류 실제 집계는 공격 50, 버프 25, 공백 20, 패링/방벽/상합 설치/소환/필드 각 5개다. 신규 카탈로그에는 `GameplayAvailable`, `CsvReserved`, `PreviewOnly`를 구분하여 120개가 전투 완성품처럼 표시되지 않게 한다.

현재 [SpellBook_Proto.asset:16](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Data/Configs/SpellBook_Proto.asset:16)과 [SpellVisualSet_Proto.asset:16](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Data/Configs/SpellVisualSet_Proto.asset:16)는 동일한 15글자만 갖는다. 새 SO 120개 매핑은 가능하나 게임 실행 시 위 15개만 성공하고 나머지는 기존 불발 규칙을 따른다. 데이터 검사는 `120 unique keys / 120 non-null new recipes / 120 unique recipe IDs / 15 runtime mappings / 20 reserved`를 각각 보고한다.

## 기존 수명/틴트/풀 문제

- **풀 없음:** 해당 프로젝트 App 경로에서 VFX용 Pool/ObjectPool은 확인되지 않았다. `InkPool`은 먹 자원 경제이며 오브젝트 풀이 아니다. 생성은 `BrushStrokeFeedAdapter.cs:555`의 Instantiate, 종료는 [PatternEffectLifetime.cs:271](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternEffectLifetime.cs:271)의 Destroy다.
- `PatternEffectLifetime.Create`는 실행 때 컴포넌트를 AddComponent하고 Projectile/Explosion/Boulder 이름을 찾으며, 자식 Animator를 끈다([71](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternEffectLifetime.cs:71)). 기존 형식에 새 전면 연출을 억지로 맞추면 불필요한 이름·자식 수명 의존이 생긴다.
- **다색을 지우는 경로:** `TintHierarchy`가 모든 ParticleSystem의 startColor를 단일 오행색으로 만들고 colorOverLifetime RGB를 백색화한다. 또한 `Renderer.material`로 매 인스턴스 재질을 만든다([PatternEffectLifetime.cs:161](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternEffectLifetime.cs:161), [196](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternEffectLifetime.cs:196)). 새 색 확장 레시피는 이 경로에 넣으면 안 된다.
- 기존 Sequence는 부모 문양에서 분리하여 자체 수명으로 Destroy한다. 예: [ThornLanceEffect.cs:40](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/ThornLanceEffect.cs:40), [GroundWaveEffect.cs:45](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/GroundWaveEffect.cs:45). Instantiate만 pool.Get으로 바꾸면 부모/분리된 자식/지연 Destroy가 서로 다른 세대의 풀 객체를 없앨 위험이 있다.
- Volley는 개별 `PlannedHit.ImpactTime`에서 flight를 빼서 발사 시각을 역산한다([SpikeVolleyEffect.cs:265](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/SpikeVolleyEffect.cs:265)). 신규 연출도 이 **계약의 의미**를 보존해야 하며, 과거 가시 메시·파편 구성을 복사할 필요는 없다.

## 권고하는 최소 결합 교체

1. **새 표현 데이터:** `SpellVfx120CatalogSO` 또는 새 SpellVisualSet 자산에 120개 신규 entry를 둔다. 각각 글자·CSV 분류·가용성·신규 레시피·색 조합·실루엣/시간 구조·검수 상태를 소유한다. 공유 코드 위에 120개 독립 레시피를 놓으며 기존 8개 연출 색갈이로 120개를 채우지 않는다. 한국적 미감과 다색 구성은 레시피의 고유 팔레트/보조색을 유지하고, 오행색은 필요한 신호 레이어에만 사용한다.
2. **새 표현 실행기/단일 수명:** 신규 전용 root runner가 spawn→준비→전달받은 impact→잔광→풀 반환을 전부 소유한다. adapter의 prefab선택/생성 지점에 신규 분기만 추가해 attack/parry 모두 문맥을 전달한다. 신규 root에는 기존 `AttachBloom`/`TintHierarchy`를 중복 적용하지 않는다. 기존 미대체 prefab 경로는 별도로 남겨 회귀 대조 가능하게 한다.
3. **문맥 사본:** 글자, bounds 중심/스케일, 카메라 방향, commit scaled time, target/fallback, impact duration, AreaPlan 사본, GuardDuration/ParryWindow를 전달한다. 런타임 VFX는 EnemyVitals·InkPool·인식기 호출과 Collider 기반 피해 콜백을 갖지 않는다. 타격 시각/반경/발수는 레시피가 덮어쓸 수 없다. 의미 없는 프리뷰는 프리뷰 전용 가상 문맥을 만든다.
4. **풀과 자료 공유:** 새 runner만 prefab/recipe별 제한된 풀에서 대여한다. 정적 메시·재질을 공유하고 renderer별 값은 MPB 또는 ParticleSystem 고유 설정으로 제어한다. 재대여 전 입자/트레일/타깃/예약 시각/transform/자식 활성/색을 초기화한다. 지연 Destroy와 프레임별 재질 생성은 사용하지 않는다. 최대 동시 수·최대 입자 수·프리웜/반환 정책은 표현 설정에 둔다. LOD/화면 밖 예산 축소는 가능하지만 impact marker 시각을 바꾸지 않는다.
5. **두 검수 경로:** 기존 성공 15자는 실제 Drawing→Combat 경로로 신규 VFX를 확인한다. 전체 120자는 별도 하네스에서 동일 runner에 프리뷰 문맥을 넣고 재생한다. 105개 preview와 15개 gameplay 결과를 분리하며 Reserved 20개에 가짜 피해나 버프를 발생시키지 않는다.

핵심 수정 파일 범위는 신규 SO/runner/pool/카탈로그 빌더/검수 하네스 + `BrushStrokeFeedAdapter`의 표현 생성 분기 정도다. `SpellResolver`, `SpellBook_Proto`, Drawing 인식/샘플링, `CombatLoopWiring`의 예약/피해, ParryJudge를 바꾸는 작업은 이 설계에 필요하지 않다.

## 카메라·검수 씬·회귀 증거

- 실제 공용 [PlayerRig.prefab:694](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab:694)는 FOV 60. [955](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab:955)에서 기존 기본 문양/시각 SO를 참조하며, 작도면 깊이는 1m, projectionCamera 미지정은 Camera.main 경로다.
- [CombatConfig_Default.asset:25](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Data/Configs/CombatConfig_Default.asset:25)의 평시 숄더 (0,0.55,−2.8), 작도 (0.35,0.25,−0.2), 작도 FOV 52. [CameraRigController.cs:72](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Combat/Player/CameraRigController.cs:72)는 unscaled 카메라 블렌드/벽 SphereCast 수축을 한다. VFX는 카메라 프레임 확정 뒤의 bounds를 그대로 사용해야 한다.
- [BrushStrokeFeedAdapter.cs:390](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:390)의 작도 깊이 보정과 [399](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:399)의 화면 크기 등가를 새 연출이 다시 계산해서 덮어쓰지 않는다.
- 실제 게임 회귀 장소는 `C1_SpellRange.unity`: [SpellRangeSceneBuilder.cs:35](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/SpellRangeSceneBuilder.cs:35)의 6과녁(각도·거리 IN3/OUT3)이 있다. 빌더는 활성 사격장 배치를 재생성하므로 신규 120 하네스 생성에 그대로 호출하지 않는다.
- [SpellRangeDirector.cs:122](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/Dev/SpellRangeDirector.cs:122)가 `CastPlanned`의 예정 대상·시각을 수집하고 [218](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/Dev/SpellRangeDirector.cs:218)에서 실제 착탄을 기록한다. [240](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/Dev/SpellRangeDirector.cs:240)의 기존 최종 판정은 대상 집합/발수 중심이므로 **새 동기 회귀에서는 예정−실제 시각 오차도 수치로 비교해야 한다.** 현재 코드를 자동 timing assertion이 이미 있다고 보고하면 안 된다.
- `C1_FxAudit.unity`/`PatternAuditSpawner`는 프리팹 직접 생성·가상 AreaPlan에 적합한 기존 표현 감사 예시다([PatternAuditSpawner.cs:143](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternAuditSpawner.cs:143), [182](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/PatternAuditSpawner.cs:182)). 다만 이것도 Instantiate/Destroy 방식이며 120 신규 연출과 가용성 보고는 새 하네스로 분리하는 편이 작다.

필요한 실제 검사: 120슬롯 누락/중복/Reserved·게임 가용성 검사, 기존 자산 dependency 재사용 검사, 각 120개 동일 조명/카메라의 전체 수명 MP4와 peak 정지 이미지, 밝은/어두운 배경·C2 원경 대비, 카메라 양 모드/벽 수축/가까운 발사/화면 가장자리, 30/60/120fps 및 작도 감속, 누적 동시 시전과 풀 100회 재대여의 잔상/GC/객체·재질 수, VFX ON/OFF 동일 입력의 원시 획·인식 결과·소모·대상·피해·패링·예약 시각 비교. 관찰·숫자가 없는 성능/미술/게임 회귀 항목은 미검증으로 남긴다.

## 후속 배정에 따른 어댑터 구현 기록

위 감사 이후 root가 별도로 승인한 구현 범위에서 [BrushStrokeFeedAdapter.cs:564](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:564)에 신규 `SpellVFX120.Vfx120Effect` 분기만 추가했다. attack은 기존 target/duration/AreaPlan/Cone fallback을 전달하고, parry는 GuardDuration/ParryWindow를 전달한다. Begin 직후 return하여 기존 PatternEffectLifetime을 붙이지 않는다. 기존 경로는 보존했다. 일부 뒤쪽 링크 줄번호는 감사 당시 교체 전 기준이며, 인식·규칙·피해 파일은 바꾸지 않았다.

`_visualSet`의 직접 직렬 참조는 공용 `Prefabs/Rig/PlayerRig.prefab:956` 한 곳이다. 해당 프리팹(GUID `7ead43b83b10ff04f98b1964f46f7ddf`)을 C1_CombatLoop/EffectLab/SpellRange/ParryRange/WorldLookdev/TestHub, C2_PlayerValidation/C2_CodexWorld, W_Cheongrim_FarSet의 9씬이 참조한다. 신규 SO 자산 연결은 root의 Editor builder 소유이며 이 하위 작업에서 씬·프리팹을 수정하지 않았다.

[Vfx120AdapterAudit.cs:33](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120AdapterAudit.cs:33)의 `Oheangbu.EditorTools.Vfx120AdapterAudit.Run()`은 실제 adapter와 실제 Vfx120Effect를 임시 객체로 호출한다. 신규 단일 clock·miss·Cone·Circle·Path·Volley·parry·other 및 기존 guard의 9건 경계 회귀를 확인하고 JSON을 반환한다. source Profile 없이도 남는 신규 컴포넌트의 실제 수신 속성을 사용한다. 실행은 root의 라이브 Unity 검수 단계에서 수행하며 이 문서 작성 시점에는 **미실행**이다. 이 검사를 통과해도 인식·실제 피해 타이밍·120개 미술·성능 검사의 대체가 아니다.

[Vfx120MotionAudit.cs](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120MotionAudit.cs)의 `Oheangbu.EditorTools.Vfx120MotionAudit.Run()`은 실제 120 Profile을 별도 임시 PreviewScene에서 30/60/120fps로 Sample한다. 모든 프레임의 유한 좌표·스케일·리본, 동일 시각 및 역순 재샘플의 일치, Life의 alpha/리본 종료, 실제 부품 상한, 원본 메시 버퍼·재질·Profile SHA 불변을 확인하여 `Art/SpellVFX120/motion_audit.json`에 기록한다. 반복 ParticleSystem.Simulate·렌더·Play는 수행하지 않는다. 이 문서 작성 시점에는 **미실행**이며, 실제 Update/Destroy·파티클 완주·동시 객체 누수·미술·피해 시계·프레임 비용은 별도 **미검증** 항목이다.
