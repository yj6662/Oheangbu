# 국 · 신목의 디딤

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 꼬인 나무줄기·발판 지지 가지·불규칙한 나무 발판 메시를 새로 제작했다. 보유 수피·잎 텍스처를 재사용한다.
- KTP 잎흔적과 발동 연출을 사용하고 비전투용이므로 공격 타격은 연결하지 않았다.
- ConfigureWoodLift로 기반 위치·지원 높이 상한을 받고 SetWoodLiftHeight가 공급한 현재 높이만 표시한다. 2.4m 길이의 고정 메시를 지하에서 올리며 늘리지 않는다. 최대 지원 높이는2.4m다.
- 콜라이더·플레이어 이동·피해를 만들지 않는다. 높이 범위·계획 전 비활성·해제 중복 방지·수명을 검사했고 다른119개 프로필을 보존했다.

## 실제 비용

줄기·지지 가지1572tris + 발판80tris + 잎48tris + KTP 흔적2tris = 총1702tris. MeshRenderer6개, 런타임 메시 복제0개. KTP 파티클 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.400000095367432 |
| destroyedAt | 4.4025092124938965 |
| castPeak | 155 |
| impactPeak | 0 |
| nativeSystemsPeak | 10 |
| capacityPeak | 312 |
| woodLiftPlan | True |
| woodLiftMaxHeight | 2.0999999046325684 |
| woodLiftReleased | True |
| woodLiftVisibleFrames | 1107 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 63901a42-5a0b-40f2-8481-d7afacc6ccf9. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 영상은2.1m 높이 변화를 공급한 카탈로그 시연이다. 실제 비전투 시전·플랫폼 충돌·캐릭터 수직 이동은 미연결이다.
- 줄기·발판·잎 동작의 비주얼은 사용자 검토 대기다.
- 2.4m 초과 높이·동적 지형·동시 사용 성능은 미검증이다.

1280×720·24fps. 시연 높이는0.35~1.8초에2.1m로 상승하고3.5초에 해제된다. 이 높이 곡선은 검수 도우미가 공급하며 플레이어를 움직이지 않는다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_AD6D.json, wood_lift_020_build.json, guk43_scope_check.json.
