# 회피·시선·웅크림 구르기

2026-09-14 사용자 요청에 따른 별도 PlaytestDodgeTurn 파생본.

- 선 상태 Shift: Mixamo Standing Dodge Forward/Backward/Left/Right를 C02에 리타깃. 기존 3m·0.25초 이동, 0.3초 무적, 0.6초 재사용 설정 유지.
- C 웅크림 중 Shift: Stand To Roll의 0.5~1.8초를 사용한 방향 구르기. TEST 이동 시간 0.8초, 거리 3m, 무적 0.3초. 종료 후 웅크림 유지. 캡슐·입력·카메라는 기존 Motor가 관리한다.
- 정지: 시선과 몸 방향 차이 90도까지 머리·목으로 바라본다. 90도 초과 시 90도 몸 회전을 한 번 시작하며 완료 전 재시작하지 않는다. 각속도 기반 반복 회전은 제거한다.
- 선 상태는 기존 Left Turn/Right Turn 클립, 낮은 자세의 회전은 기존 Crouch Walk 좌우 클립을 회전 스텝으로 활용한다. 별도 웅크림 제자리 회전 원본은 아니다.
- 기본 대기 클립의 발 재배치를 없애기 위해 별도 IdleStill 파생본을 사용한다. 이동·작도·갈무리·회피 중에는 시선 분리보다 해당 동작 방향을 우선한다.
- 손: ThumbWrap 버전 임시 채택. `PLAYER-HAND-FOLLOWUP-20260914.md`의 후속 개선 항목 유지.
- 폐광: 조사·교전·휴식·저장 연결을 가진 플레이테스트 임시 공간. 완성된 던전 내부·최종 미술로 보고하지 않는다.

검증과 스크린샷: `Art/PlaytestRecovery/DodgeTurn/REVIEW.html`, `REPORT.md`.
빌드·영상·자동 전체 보행은 이번 작업에 포함하지 않는다.
