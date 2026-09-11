# 간 · 옮겨 심는 씨눈

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 짧은 굽은 가시와 세 역가시를 별도 메시로 제작했다. 씨눈은 두 개의 두께 있는 껍질로 구성했다.
- 대나무 수피와 KTP Pattern_192 잎눈, 기존 KTP 발동·명중 연출을 재사용한다.
- RequestSeedTransfer는 지정된 수신자만 읽는다. 타깃 탐색·사망·만료 판단과 피해는 실행하지 않는다. ConfirmSeedTransferHit 이후 껍질이 열린다.
- 자산 원본을 보존하고 다른119개 프로필의 불변을 확인했다.

## 실제 비용

가시140tris + 씨눈 껍질640tris + KTP 표식2tris = 총782tris. 메시 렌더러4개, 런타임 메시 복제0개. KTP 파티클 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 2.5999999046325684 |
| destroyedAt | 2.6017205715179443 |
| castPeak | 155 |
| impactPeak | 153 |
| nativeSystemsPeak | 23 |
| capacityPeak | 515 |
| transferRequested | True |
| transferHit | True |
| transferVisibleFrames | 604 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 5adf3ed9-0e12-47c5-a505-30002079c343. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 카탈로그 시연용 전이·명중 신호를 사용했다. 실제 적 사망·씨앗 만료·가까운 적 탐색·전이 피해는 미연결이다.
- 비주얼 최종 판정은 사용자 검토 대기다.
- 움직이는 적 사이 전이와 다중 동시 사용 성능은 미검증이다.

1280×720·24fps. 전이1.3초, 도착 확인1.85초. 다음 대상은 고정된 진단 지점이며 화면 속 실루엣을 실제 적 AI로 취급하지 않는다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_AC04.json, seed_transfer_003_build.json, gan37_scope_check.json.
