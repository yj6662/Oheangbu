# 곤 · 뿌리의 들어올림

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- Seyeonjeong의 실제 뿌리 메시·UV·수피를 재사용했다. 각의 결박 배치와 달리 세 뿌리가 짧게 치솟고 바로 접힌다.
- KTP Pattern_192 지면 잎흔적과 발동·타격을 사용했다. 원본 뿌리 자산은 변경하지 않았다.
- Circle 중심·반경·Delay를 복사해 사용한다. 올라오는 중 메시 크기는 고정하고 아래 지면 절단을 적용한다. 지면 탐침은 생성 때 세 번만 실행한다.
- 공간 계획 없는 경우 조용히 유지하고 구형 형상을 되살리지 않는다. 검사기 네임스페이스 누락은 고친 뒤 재컴파일했다. 다른119개 프로필은 변경하지 않았다.

## 실제 비용

뿌리3305tris ×3 + 지면 문양6tris = 총9921tris. MeshRenderer6개, 공유 메시 사용, 런타임 메시 복제0개. KTP 파티클 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 1.5499999523162842 |
| destroyedAt | 1.5519311428070068 |
| castPeak | 155 |
| impactPeak | 153 |
| nativeSystemsPeak | 23 |
| capacityPeak | 515 |
| rootLiftMaxRise | 1.0 |
| rootLiftWithdrawn | True |
| rootLiftVisibleFrames | 184 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: e2ee47e5-2a3a-4800-90d3-43e1b0a60b4b. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 영상은 카탈로그의 원형 계획으로 촬영했다. 실제 적 쳐올리기·인터럽트·피해는 미연결이다.
- 뿌리의 접촉감과 들어올리는 인상은 사용자 검토 대기다.
- 급경사·복층 지형·다중 동시 사용 성능은 미검증이다.

1280×720·24fps. 전달된 Delay까지 치솟고0.12초 뒤 접기 시작,0.58초 뒤 철수한다. 카탈로그 반경을 실제 게임 판정으로 사용하지 않는다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_ACE4.json, root_lift_015_build.json, gon41_scope_check.json.
