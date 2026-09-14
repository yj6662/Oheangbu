# 회피·시선 회전·웅크림 구르기 — 2026-09-14

현재 손은 임시 채택 상태로 보존했다. [손 후속 기록](../../../Docs/Plans/PLAYER-HAND-FOLLOWUP-20260914.md).

## 적용
- Mixamo 공식 로그인 세션에서 Standing Dodge Forward / Backward / Left / Right, Stand To Roll을 내려받았다. Without Skin, FBX Binary,60fps, 키프레임 축소 없음. 원본 SHA256은 SOURCE_LEDGER.json.
- 별도 Blender·FBX 및 PlaytestDodgeTurn Unity 에셋. C02의 얼굴·몸·손 메시와 기존 이동 원본을 교체하지 않았다.
- 회피 3m /0.25초 유지. 웅크림 Shift는 3m /0.8초 구르기이며 기존 무적 0.3초 유지. 공중·작도·갈무리·메뉴 중 시작 금지. 완료 후 웅크림 유지.
- Stand To Roll 원본 0.5~1.8초를 사용. 실제 C02 몸 표면이 바닥을 뚫지 않도록 전신 높이 보정(최대 약36.6cm)을 베이크했다. 팔이나 본 길이를 늘리지 않았다. 긴 두루마기의 모든 동적 관통을 해결했다는 뜻은 아니다.
- 선 상태는 Left/Right Turn, 웅크림 제자리 회전은 보유 Crouch Walk 좌우를 회전 스텝으로 활용했다. 몸–시선 차이90도에서 머리·목 분담, 초과 시90도 몸 회전. 기본 Idle의 발 재배치를 없앤 별도 IdleStill 사용.
- 오른쪽 방향으로 구르면 모델이 이동 방향을 향한다. 게임 루트와 카메라는 소스 모션의 루트 회전·이동을 추가로 적용하지 않는다.

## 검사
실제 Unity 씬의 정상 InputSystem·PlayerMotor·Animator에 가상 키보드/마우스 입력을 넣은 짧은 국부 검사다. 사람의 직접 플레이 또는 전체 경로 완주 증거로 취급하지 않는다.

| 항목 | 결과 | 근거 |
|---|---|---|
| 실제 Play 입력 루프·정리 | 통과 | Virtual keyboard/mouse injected through real InputSystem and live scene frames; local tests, no native-user or full-route evidence. |
| 45도 시선 / 발 유지 | 통과 | 양 발 최대 변위 0.000mm, 몸 방향 고정, Turn 미진입 |
| 89도 시선 / 발 유지 | 통과 | 양 발 최대 변위 0.000mm, 몸 방향 고정, Turn 미진입 |
| look100 | 통과 | TurnRight, 회전 누적 1회 |
| look_left | 통과 | TurnLeft, 회전 누적 2회 |
| crouch_look | 통과 | CrouchTurnRight, 회전 누적 3회 |
| dodge_forward | 통과 | 새 방향 클립을 사용하는 Dodge 상태 진입 |
| dodge_back | 통과 | 새 방향 클립을 사용하는 Dodge 상태 진입 |
| dodge_left | 통과 | 새 방향 클립을 사용하는 Dodge 상태 진입 |
| dodge_right | 통과 | 새 방향 클립을 사용하는 Dodge 상태 진입 |
| roll_forward | 통과 | 구르기 → Crouch 복귀, 높이 1.25m 유지 |
| roll_right | 통과 | 구르기 → Crouch 복귀, 높이 1.25m 유지 |
| 구르기 이후 유령 회피 없음 | 통과 | 서기 전환에서 남은 Dodge 트리거 없음 |
| 메뉴 입력 차단·복원 | 통과 | 메뉴 중 W·Shift·C 차단, 닫은 뒤 시간 배율 1 / 입력 복원 |
| C02 리타깃·Unity 클립 | 통과 | 5클립 ×121자세,54본 원본 바인드 비교. 실제 지형 관통 합격과 구분 |

초기 검사에서 발견한 기본 Idle의 발 이동과 구르기 뒤 잔여 회피 트리거는 수정 후 재검사했다. 이전 기록은 Live 하위에 보존했다. 외부 스크린샷은 플레이 카메라가 벽 때문에 가까워져 그림자 전용으로 바뀐 월드 모델을 촬영 순간에만 표시한 뒤 원래 상태를 복원한다. 빈 캐릭터 화면은 외형 검증 자료로 사용하지 않는다.

## 미검증·후속
- 사용자 직접 입력의 동작 만족도와 최종 외형 승인
- 30·60·120fps 강제 조건 및 감속 전체 조합
- 계단·급경사·낮은 천장 구르기와 전투 피격·탑승 교차 검사
- 모든 메시 면 단위 의상·손·붓 관통 검사

폐광은 조사·첫 교전·휴식·저장 기능을 가진 플레이테스트 임시 공간이다. 내부 공간 구성과 상세 미술은 완성 던전이 아니며 이번 작업에서 변경하지 않았다.

빌드·영상·자동 전체 보행 없음. 캡처는1920×1080 한 장씩, 커밋85% 이상이면 중단하도록 설정했다. 최종 정리에서 Play 종료·검사 저장 슬롯 해제·씬 저장 완료(dirty=false)를 확인했다. 종료 시 커밋 사용률58.4%.
