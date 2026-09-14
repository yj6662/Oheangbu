# 플레이테스트 보행·달리기·점프·앉기 구현 기록

## 범위

기존 C02 플레이어 모델·골격·손 리깅을 유지하고 별도 보행 프로필로만 새 이동을 활성화한다. 기본 C2 및 프로필이 없는 PlayerMotor는 기존 이동을 사용한다. 월드 지형 재생성, 소환수 수정, 전투 규칙 변경, 빌드 생성은 실행하지 않았다.

## 구현

- WASD 보행 2.2m/s, Ctrl 달리기 5.5m/s. Shift 회피·Tab 락온을 유지한다. 전후좌우 입력은 길이를 제한하여 대각선 가속을 방지한다.
- 가속 16m/s², 감속 22m/s². 실제 변위에서 로컬 속도를 측정하여 전후좌우 Walk/Run 블렌드에 전달한다.
- 지상 Space: 높이 0.75m 점프. 수직 이동은 `v*dt + 0.5*g*dt²` 적분을 사용한다. 작도·갈무리·회피·앉기 중에는 시작하지 않는다. 공중에서 작도·갈무리·회피를 시작하지 않으며 착지 후 새 입력이 필요하다.
- 정지 상태 C: 바닥 앉기. C 또는 이동 입력으로 일어난다. 회복·저장·보상을 호출하지 않는다. 실피해 또는 사망은 앉기를 취소한다. 서 있을 공간이 막히면 낮은 충돌체와 앉은 표현을 유지한다.
- 입력은 기존 Gameplay 맵에 Sprint(Ctrl), Jump(Space), Sit(C)를 추가했다. 차량 Space 제동은 탑승 시 Motor가 비활성화되는 기존 소유권 구조를 이용한다. UI 입력 해제 대기는 C와 Ctrl도 포함한다.
- 메뉴 입력 차단 시 직전 수평 속도를 비워 메뉴 종료 후 입력 없이 미끄러지는 현상을 방지한다. 일시정지 중 점프 수직 상태와 앉기 진행은 보존한다.
- 지면 법선·경사에 맞춰 이동하고 최대 0.24m 지면 붙임을 사용한다. 발은 실제 스킨 발바닥 높이와 수평 발 궤적으로 지지/스윙을 구분한다. 발목 보정 상한 7.5cm, 시각 골반 보정은 아래 3.5cm·위 4cm 이내이며 게임플레이 루트와 카메라를 발 IK로 움직이지 않는다. 발을 경사면으로 회전시킨 뒤 실제 스킨 높이를 다시 확인한다.
- 앉기·일어서기 및 회피의 클립 시각은 Motor의 자세 비율과 기존 DodgeAction 진행 비율을 사용한다. 모션 재생 때문에 회피 시간이나 무적 규칙을 변경하지 않는다.

## 모션 원천

Dosa/Animations의 기존 방향 모션 원천을 재사용한다. 최초 직접 Humanoid 복제는 현재 C02에서 걷기 발바닥이 0.713m까지 들리고 유효 지지 표본이 2개만 남아 실패했다. 실패값을 통과 처리하지 않고 별도 증거로 보존했다. 걷기 4개는 원본 FBX 해시와 대조한 기존 정규화 발 궤적을, 달리기 4개는 보폭이 긴 기존 RunForward의 궤적을 진행 방향에 맞게 회전하여 C02의 고정 길이 다리에 연결한다. 짧은 원본 RunBack/Left/Right를 3.49~4.07배로 재생해야 했던 중간안은 최종 시안으로 승인하지 않았다. 옆 달리기는 좌우 발의 앞뒤 경로를 20cm 분리한다. C02 상체 모션을 사용하며 원본 FBX·가져오기 설정과 플레이어/카메라의 방향 규칙은 수정하지 않는다.

C02 원본 `Art/PlayerPhase1/PlaytestReRig/Work/Player_C02_ReRig.blend`를 열고 별도 파일에서 SitDown, SitIdle, StandUp, JumpRise, JumpFall, Land 6개 모션을 제작했다. 다리 길이를 유지한 두 관절 회전으로 자세를 만들고, 게임의 점프 높이는 애니메이션 루트가 아닌 CharacterController가 담당한다. Unity의 앉기 정지 자세 렌더는 확인했으며, 실제 입력으로 앉기·일어서기를 반복하는 전환 검수는 남아 있다.

Blender MCP는 연결할 수 없었으므로 설치된 Blender 5.0.1의 background Python을 사용했다. 이를 MCP 실행으로 보고하지 않는다.

## 보존·실행 증거

- `Baseline/manifest.json`: 변경 전 코드·입력·프로필과 SHA-256 기록.
- 재생성 순서: `build_actions.py` → `refine_pose_upperbody.py` → `retarget_directional.py` → `refine_seated_lowerbody.py` → `refine_pose_upperbody.py` → `export_actions_rest.py` → `export_motion_support.py` → `export_authored_bones.py`. 총 14개 행동의 독립 파생본을 만든다.
- `Scripts/refine_pose_upperbody.py`: C02 Idle의 실제 Armature 슬롯을 명시해 앉기·점프의 T-pose 팔을 교체하고 앉은 왼손을 무릎 쪽으로 배치한다. 오른손 붓 자세는 별도 런타임 파지 경로가 담당한다.
- `Scripts/export_actions_rest.py`, `diagnose_export_units.py`: 애니메이션 전용 FBX는 활성 행동을 해제하고 휴식 행렬에서 내보낸다. 원본과 54개 본의 휴식 행렬 최대 차이 2.03e-6을 확인했다. 앉은 자세가 기본 골격으로 내보내진 실패 원본은 Baseline에 보존한다.
- `Scripts/export_motion_support.py`: 현재 FBX 해시와 연결된 실제 C02 발바닥 표본 121개/클립 및 검증된 원본 지지 구간을 기록한다.
- `Scripts/export_authored_bones.py`: 54개 본의 실제 Blender 휴식 변환과 클립별 121개 자세를 기록한다. Unity는 원본 C02 스킨의 바인드 행렬과 축을 대조한 후, 이 변형을 기존 Avatar의 HumanPoseHandler로 변환한다. 별도 Humanoid T-pose 추론과 Generic Animator의 기본 자세를 거치지 않는다.
- `Scripts/refine_seated_lowerbody.py`: 양 허벅지가 반대 방향으로 회전해 중앙 옷자락이 -141mm까지 접히던 문제를 무릎이 전방을 보는 앉기 자세로 수정한다. 메시·웨이트·뼈 길이는 유지한다. Blender 검사에서 64개 앉기/일어서기 프레임의 최대 관통은 1.48mm, 유지 자세의 전체 스킨 최저점은 +4.07mm, 엉덩이 표본 최저점은 +39.23mm다. Unity 렌더 검사는 별도다.
- `Blender/Player_Locomotion_Derivative.blend`: 편집용 독립 작업본.
- Unity `Assets/_Project/Art/Characters/PlaytestLocomotion/Player_Locomotion_Actions.fbx`: 14개 행동, 동일 54개 본.
- `Reports/blender_actions.json`: 입력 원본·출력 FBX 해시와 클립별 표본.
- `Reports/blender_roundtrip.json`: 빈 Blender 장면 FBX 재임포트, 14개 행동·54개 본·유한 변환 통과.
- `Reports/static_checks.json`: 입력 구조·원본 해시 보존·30/60/120fps 및 0.2배속 점프 적분 확인.

## Unity 적용·검사 진입점

`Oheangbu.EditorTools.WorldMacro.WorldMacroLocomotionAuthoring.Execute(command)`

1. `prepare`: 해시가 연결된 FBX·Blender 자세 데이터의 54개 휴식 위치와 실제 발바닥 궤적을 검사한 뒤, 기존 C02 Avatar에서 8개 이동·6개 자세 클립을 베이크하고 기존 회피 4개를 복제한다. 현재 모델 크기로 지지 발 속도를 측정하여 재생 속도를 계산한다. 실패한 자세를 현재 `.anim`에 덮어쓰지 않도록 각 소스 변환은 메모리에서 먼저 검사한다. 모든 18개 클립이 통과하기 전에는 적용하지 않는다.
2. `math`: Unity의 JumpSpeed·벡터 정규화와 기존 프로필의 비활성 상태를 검사한다. 실제 입력/충돌 검사와 구분한다.
3. `apply`: 별도 W_WorldMacro_Playtest에 새 프로필과 Controller를 연결하고 씬을 dirty 상태로 남긴다. 자동 저장·빌드·지형 재생성은 하지 않는다.
4. `validate`: 실제 씬의 프로필·18개 Humanoid 클립·루트 모션 OFF·발 보정 연결을 검사한다.
5. `motorcheck`: Play 모드의 실제 Dynamic 입력 갱신에서 임시 CharacterController·바닥·전용 가상 장치를 사용한다. 명시적 시간 간격으로 점프·착지·갈무리 해제·이동 속도·앉기·메뉴 차단을 검사한다. 현재 52개 검사는 통과했다. 실제 플레이어, 저장, 피해/보상 또는 OS 입력을 사용하지 않으며 네이티브 입력 검증으로 표시하지 않는다. Edit에서는 실행하지 않는다. 이전 Edit 가상 입력 실패는 도구 실패로 보존했다.
6. `footcheck`: 독립 C02 복제본을 30/60/120fps·0.2배속과 평면/12도 진단 평면에서 이동시키며 실제 스킨 발바닥과 런타임 발 보정의 길이·관통·지지 미끄러짐을 측정한다. 실제 지형 Raycast나 네이티브 보행으로 표시하지 않는다.
7. `recalibrate-forward`: 현재 클립의 확정 지지 구간에서 발 이동 거리/지지 시간을 적분해 WalkForward 재생률만 갱신한다. 최종값은 1.779315배에서 2.010459배로 변경했다. 이동 속도 2.2m/s, 다른 클립, 모델, 손, 프로필과 씬은 변경하지 않는다. 갱신 후 `footcheck`를 다시 실행한다.

중간 구현의 독립 Humanoid/Generic 전환은 축·기본 자세를 다르게 해석해 폐기했다. 최종 14개 행동은 실제 Blender 변형을 원래 Avatar로 베이크하며, Unity 검사에서 바인드 위치 차이 최대 0.575µm·선택한 발바닥 표본 차이 최대 1.00µm로 원천 일치를 통과했다. 복제된 RootT.y의 최종 높이 보정은 최대 13.8mm, 잔차는 0.223µm였다. `Reports/human_bake_*.json`과 `height_conversion_*.json`에 값을 남긴다. 이는 표본 일치 검사이며 전신 옷 관통 검사를 대신하지 않는다. 재생 속도는 원본의 실제 지지 구간에서 부호를 보존하여 계산한다. 낮게 되돌아오는 스윙을 지지 구간으로 섞거나 절댓값으로 방향 오류를 숨기지 않는다.

각 실행 결과는 `Reports/unity_*.json`에 기록한다. Unity 실행과 최종 씬 저장은 루트 작업자가 순차 처리한다.

## 최종 제한 검사 결과 — 2026-09-14 02:10 UTC

`Reports/unity_synthetic_feet.json`은 30개 합성 조건에서 **지지 중 미끄러짐 100mm 이하·발 스킨 지면 관통 20mm 이하라는 사용자 기준을 모두 충족**했다. 최대 미끄러짐은 55.172mm, 최대 관통은 18.928mm다. 감속 WalkForward의 이전 최대 미끄러짐 130.45mm는, 중앙값으로 정한 재생 속도와 전체 지지 구간의 이동 거리가 일치하지 않았기 때문이다. 발 길이나 IK 상한을 늘리지 않고 지지 거리/시간으로 전진 보행 재생률을 보정했다.

검사 자체의 더 엄격한 관통 기준인 **15mm 미만은 26/30 통과, 4/30 실패**다. 원본 결과의 `FAILED_LIMITS`를 유지했으며 기준을 완화하지 않았다. 실패 조건은 모두 12도 오르막 RunForward로, 0.2배속 30/60/120fps에서 각각 16.332/18.044/18.928mm, 정상 속도 120fps에서 15.504mm 관통이다. 뼈 길이·로컬 스케일·75mm 발목 보정·골반 상하한·지지 표본 수와 주기는 전 조건에서 통과했다.

검사는 전후좌우 Walk/Run의 60fps 평지와, 전진 Walk/Run의 30/60/120fps·정상/0.2배속·평지/12도 조합으로 구성한다. 각 경우 원본 주기를 2회 이상 평가했다. 보정용 발바닥 표본 외에 Foot와 Toes의 가중치 합이 0.5 이상인 전체 스킨 정점을 별도로 측정했다. 실제 씬을 걷는 검사나 실시간 BlendTree 전환 검사는 아니다. CharacterController의 실제 지면 간격, 비평면 지형 이음, 급격한 방향 전환과 정지 시의 발 접촉은 미검증으로 남긴다. 이번 제한 검사의 통과를 전체 리깅·전신 옷 관통 통과로 확대하지 않는다.

## 상태와 남은 검증

| 항목 | 상태 | 근거 |
|---|---|---|
| 원본 Blender 보존 | 통과 | 해시 일치 |
| 파생 Blender·FBX 및 6개 행동 제작 | 통과 | 실제 Blender 저장/내보내기 |
| Blender FBX 빈 장면 재임포트 | 통과 | 54개 본, 14개 행동, 표본 변환 유한 |
| 점프 적분 30/60/120fps·감속 | 통과(수식) | 최고점 오차 최대 약 0.518mm; 충돌 포함 실제 플레이 검사가 아님 |
| 키 중복·액션 ID 및 기존 Shift/Tab 보존 | 통과(직렬화) | static_checks.json |
| Unity 컴파일·Humanoid 소스 자세 베이크·적용 | 통과 | 14개 원천 자세 일치, 기존 회피 4개 포함 18개 클립; unity_prepare/apply 및 human_bake 보고서 |
| 발 접촉 사용자 기준 100mm/20mm | 통과(격리 합성 조건) | 30/30, 최대 미끄러짐 55.172mm·관통 18.928mm |
| 발 접촉의 엄격한 15mm 관통 기준 | 실패 | 26/30 통과; 12도 전진 달리기 4개 조건 15.504~18.928mm |
| 실제 보행·급정지·방향 전환·지면 이음 | 미검증 | 사용자가 수행할 보행과 별도의 제한 검사 필요 |
| 앉기 정지 자세·옷/바닥·붓 구도 | 통과(제한 표본/정지 이미지) | Blender 64개 앉기/일어서기 프레임 최대 관통 1.48mm; Unity 20260914-014337-656_synthetic_sit-carry.png에서 무릎·소매·붓 구도 확인 |
| 앉기 전환의 실입력·지형 충돌 | 미검증 | 정지 이미지와 Blender 표본은 실입력 전환 검사를 대신하지 않음 |
| 점프·착지·공중 작도 금지·메뉴 상태 | 통과(격리 합성 입력) | Play Dynamic 입력 및 실제 CharacterController, 52개 검사; OS 입력·실제 플레이 경로는 미검증 |
| 플레이테스트 빌드 | 미실행 | 작업 종료 후 한 번에 생성하는 사용자 요청 준수 |

직접 Humanoid 실패 원문: `Reports/failed_direct_humanoid_WalkForward.json`. 방향 궤적 보정 출처·해시·베이크 골반 하강량: `Reports/directional_retarget.json`. 수식/Blender 결과는 Unity 실제 실행 결과와 구분한다.
