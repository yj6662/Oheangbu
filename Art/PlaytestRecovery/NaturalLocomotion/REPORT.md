# Mixamo 걷기·달리기·점프 교체 결과

새 파생본을 W_WorldMacro_Playtest 씬에 연결하고 저장했다. 손·회피·웅크린 구르기·제자리 회전의 기존 소스는 보존했다. 빌드와 영상은 만들지 않았다.

## 변경

- Mixamo Locomotion Pack의 걷기·달리기·양옆 이동·점프, 별도 후진 걷기·후진 달리기를 사용했다. 다운로드 원본과 SHA-256은 source_ledger.json에 남겼다.
- C02의 넓은 휴식 다리 방향이 남던 회전 델타 전사를 다리 관절 방향 재구성으로 보완했다. 뼈 길이·바인드·외형 메시를 바꾸지 않았다.
- 방향별 왼발 주기를 정렬하고 C02 지지 발 이동으로 재생률을 보정했다. 첫 후진 걷기 후보는 짧은 보폭 때문에 제외했다.
- 점프 source 46–64프레임 도약 /64–80 낙하 /80–120 착지. 긴 사전 준비는 제외했고 소스의 공중 루트 상승을 제거했다. Motor 수직 속도로 도약·낙하 자세를, 실제 접지로 착지 전환을 제어한다.
- 새 선 상태 모션에서는 교정된 뒤꿈치→발끝 움직임을 보존하며 발목 고정을 적용하지 않는다. 지면 법선 차이·관통 보정은 유지한다. 도달 범위 클램프가 발목을 과하게 들어올리지 않도록 수평 도달 범위를 해당 높이에서 제한한다.

## 실제 Play 결과

| 항목 | 이전 | 최종 |
|---|---:|---:|
|전진 걷기 좌우 발목 간격 중앙값|33.6cm|20.4cm|
|전진 달리기 좌우 발목 간격 중앙값|33.4cm|14.5cm|

간격은 같은 입력 구간 전체의 월드 발목 위치를 플레이어 좌우축으로 투영한 중앙값이다. 발 길이나 보폭 길이를 의미하지 않는다. 이전 실행 후반에는 적이 개입하여 일부 화면을 가렸다. 최종 실행에서는 진단 범위에서 적을 일시 정지하고 복원했다.
실제 제자리 점프 상승 0.728m / 이동 점프 상승 0.730m. 설정 목표 0.75m 유지.

## 검사

|상태|항목|
|---|---|
|통과|전진 시 좌우 발 벌어짐 감소|
|통과|walk_forward 실제 이동 및 상태|
|통과|walk_back 실제 이동 및 상태|
|통과|walk_left 실제 이동 및 상태|
|통과|walk_right 실제 이동 및 상태|
|통과|run_forward 실제 이동 및 상태|
|통과|run_back 실제 이동 및 상태|
|통과|run_left 실제 이동 및 상태|
|통과|run_right 실제 이동 및 상태|
|통과|diagonal 실제 이동 및 상태|
|통과|reverse_diagonal 실제 이동 및 상태|
|통과|jump 도약·낙하·실제 접지|
|통과|walk_jump 도약·낙하·실제 접지|
|통과|웅크림·구르기 연결 유지|
|통과|발 보정의 뼈 길이 유지|
|통과|합성 지면 30조건|
|통과|C02 바인드·소스 자세 변환|
|통과|진단 입력·적·위치 복원|
|통과|최종 캡처 1080p·유효 픽셀|

합성 지면 검사는 전진 걷기·달리기에 30/60/120fps × 1배/0.2배 시간 × 0/12도 경사를 적용했고, 60fps·1배·평지에서는 전후좌우 총8개 동작을 검사했다. 총30조건이다. 실제 월드 경사 주행 통과를 의미하지 않는다.
최대 합성 지지 발 미끄러짐 6.68cm, Foot+Toes 가중치 메시 관통 1.69mm.

## 미검증·후속

- 미검증: 사용자 직접 키보드·마우스 체감 검수와 최종 외형 승인
- 미검증: 실제 경사 지형·계단 전 구간과 전투 피격·사망·차량·메뉴 복원 회귀
- 미검증: 30/60/120fps 및 감속의 실제 전체 Play 루프: 이 조합은 합성 지면에서 검사
- 미검증: 새 점프의 모든 옷 관통·팔/붓과 얼굴의 충돌. 빠른 동작에서 소매·자락은 후속 비주얼 검토 필요
- 미검증: 빌드 검증: 이번에는 빌드를 만들지 않음

## 에셋 대응

|Unity 클립|Mixamo 소스|
|---|---|
|WalkForward|walking.fbx|
|WalkBack|Walking Backwards Standard.fbx|
|WalkLeft|left strafe walking.fbx|
|WalkRight|right strafe walking.fbx|
|RunForward|running.fbx|
|RunBack|Running Backward.fbx|
|RunLeft|left strafe.fbx|
|RunRight|right strafe.fbx|
|IdleStill|idle.fbx|
|JumpRise|jump.fbx|
|JumpFall|jump.fbx|
|Land|jump.fbx|
|StartForward|walking.fbx의 정렬된 첫 0.2초|

Blender: Motion/Player_C02_NaturalLocomotion.blend
FBX: Motion/Player_C02_NaturalLocomotion.fbx
Unity: Assets/_Project/Art/Characters/PlaytestNaturalLocomotion
입력 기록: Live/20260914-114809/live_input.json