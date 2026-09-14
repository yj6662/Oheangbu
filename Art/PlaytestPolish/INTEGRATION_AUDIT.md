# 플레이테스트 통합 정적 감사 — 초안

2026-09-14. 이 문서는 현재 소스와 저장된 씬·에셋 참조를 읽은 결과다. 이 감사에서는 Unity 실행, 네이티브 입력, 캡처, 빌드를 수행하지 않았다. 작업 중인 최종 이동 모션·손 검사와 최종 빌드의 결과를 대신하지 않는다.

## 확인한 연결

| 항목 | 상태 | 실제 근거 |
|---|---|---|
| C 착석, Space 점프, 양쪽 Ctrl 달리기 | 통과 — 직렬화·소스 | `Oheangbu/Assets/InputSystem_Actions.inputactions:1243`부터 Gameplay Sprint/Jump/Sit 바인딩. 저장된 `W_WorldMacro_Playtest.unity:554714`, `:554842`에서 Drawing/Motor가 같은 입력 GUID를 참조하며 `:554851`에 별도 이동 프로필이 연결됨 |
| 기존 Shift 회피·Tab 락온과 보행/차량 분리 | 통과 — 소스 | `WorldMacroCombatWalker.Suspend/Resume`이 Drawing/Motor/CameraRig/CombatLoopWiring의 소유권을 넘기며, `WorldMacroPalanquinSeat.Update`는 탑승 중 Space를 제동에만 사용. 바닥 착석은 Motor 상태이며 `Walker.Seated`인 차량 탑승과 별개 |
| 공중·착석 작도 진입 차단과 새 입력 | 통과 — 소스 | `WorldMacroCombatWalker.cs:19`의 EntryAllowed → Motor.CanBeginDrawing. `DrawingInputController.cs:162`에서 차단 중 누른 Q는 해제를 요구하고 미완성 작도를 조용히 취소. 실제 동시 입력 순서는 별도 실행 검사 대상 |
| 메뉴 시간·입력 차단 | 통과 — 소스 | `PauseCoordinator.Begin`이 작도 감속을 취소한 뒤 정지하고 차량 입력을 0으로 만듦. Gate는 C/양쪽 Ctrl/Space와 전투·차량 키를 포함해 2개 중립 프레임을 기다림. 메뉴 닫기가 차량 하차를 호출하지 않음 |
| 새 먹 셰이더의 빌드 참조 | 통과 — 저장된 에셋 연결 | 씬 `:555170` → `Audio/PlaytestPolish/AudioInk/InkFlow.asset` → `InkFlow.mat` → `Shaders/HarvestInkFlow.shader`. 런타임 Shader.Find만으로 로드하는 구조가 아님 |
| 지도 셰이더의 빌드 참조 | 통과 — Resources 경로 | `Resources/WorldMap/PaperMapSurface.shader`, `MiniPaper.shader`, `DiscoveryLines.shader`가 존재하며 `WorldMapPresenter.cs:267,280,310`의 Resources.Load 경로와 일치 |
| 식생 셰이더·재질의 빌드 참조 | 통과 — 저장된 씬 연결 | 씬 `:229763`의 Sheet GUID가 `VegetationPolish/Dressing_Dense.asset`과 일치. 시트의 파생 메시·재질이 렌더러의 직렬화 의존성. 식생 최적화 후에는 최종 저장본으로 다시 확인 필요 |
| 로비/플레이 씬의 빌드 목록 | 통과 — 소스 | `WorldMacroPlaytestRelease.cs:291`이 Title와 Playtest 두 씬을 명시. `PlaytestUiRoot.cs:19–20`의 로드 이름과 일치. 기존 전체 BuildSettings는 finally에서 복원 |
| Runtime의 Editor API 분리 | 통과 — 소스 검색 범위 | `_Project/Scripts`의 Editor 폴더 밖 UnityEditor/AssetDatabase 참조를 조사함. AreaLoader, DevSceneFlow, PatternAuditSpawner, PoiSlot, PlaytestUiRoot의 사용부는 UNITY_EDITOR 조건 안에 있음. `Oheangbu.EditorTools.asmdef`는 Editor 플랫폼 전용 |
| 차량 지도 위치 | 통과 — 소스 | `WorldMapPresenter.cs:602`가 탑승 중 차량 transform을 사용. 1인칭/외부 카메라의 위치차로 지도 발견 중심이 이동하지 않음 |

## 확인된 잔여 동작과 문서 불일치

1. **차량 이동 후 이어하기 위치가 마지막 보행 지점이다.** `WorldMacroPlaytestSession.cs:156–158`은 차량 탑승 중 lastSafe를 갱신하지 않고, `:247`에서 항상 lastSafe를 저장한다. 차량으로 멀리 이동한 뒤 종료하면 현재 차량 위치가 아닌 탑승 전 마지막 안전 보행 위치로 돌아온다. 이번 변경 이전부터의 보행 저장 정책이며 차량 transform/탑승 상태 저장은 구현되지 않았다. 이를 차량 이동 위치 저장 완료로 표현하면 안 된다. 현 정책을 테스트 제한으로 명시하거나 별도 차량 안전 하차 저장 정책이 필요하다.
2. **이동 보고서의 motorcheck 실행 안내가 오래되었다.** `Locomotion/REPORT.md:47`은 Edit 실행을 설명하지만 현재 `WorldMacroLocomotionMotorQa.cs:66`은 Edit에서 `NOT_RUN_REQUIRES_PLAY_MODE`를 기록한다. 실제 합성 버튼 검사는 Play의 Input System Dynamic update에서만 실행한다. 이것은 검사 도구의 문서 불일치이며 게임 동작 실패를 뜻하지 않는다.

이 읽기 범위에서 새 C/Space/Ctrl 연결을 차단하거나 새 셰이더를 반드시 누락시키는 확정 결함은 발견하지 못했다. 이는 실행 합격이나 무결함 보증이 아니다.

## 최종 전달 전에 구분할 검사

- 최종 모션·손·프로필 파일이 확정된 후 해당 정확한 SHA에 대한 Grip A 런타임 검사/접촉 보고서 및 release gate를 다시 생성해야 한다. 이전 해시의 PASS는 최신 에셋의 PASS가 아니다.
- 실제 OS 입력으로 메뉴 중 이동·작도·차량 입력이 없는지, C 착석/기립과 Space 점프·차량 제동이 기대대로 동작하는지는 이 감사에서 미검증이다.
- 차량 탑승 중 종료/재실행, 저장 오류·복원, 지도 탐험 데이터의 앱 재실행은 이 감사에서 미검증이다.
- Shader stripping/그래픽 드라이버 호환성과 패키징은 최종 standalone 빌드/실행 결과로 확인해야 한다. 정적 참조 존재만으로 해당 검사를 통과 처리하지 않는다.
- 소환수는 기존 승인 범위인 등장·유지·소멸만 연결되며 이동·공격·AI는 제외 상태를 유지한다.

소스 읽기와 경로 확인만 수행했다. 별도 C# 컴파일은 이 감사에서 수행하지 않았다. 최종 이동 소스가 다른 담당자에 의해 변경 중이므로 이 초안은 최종 빌드 보고서와 함께 갱신해야 한다.
