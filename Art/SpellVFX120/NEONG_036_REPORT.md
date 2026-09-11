# 넝 · 넝 · 곁불씨

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 세 불씨에 독립적인 대기·발사·명중 상태를 구성했다.
- LaunchFirePellet·ConfirmFirePelletHit 신호로 각각 제어한다. 중복 발사·명중과 발사 전 명중을 거절한다.
- KTP Pattern_136을 대기 불씨와 각 명중 위치에 사용했다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System21개/입자용량 총336개, 문양3개/6tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 2.700000047683716 |
| destroyedAt | 2.7016615867614746 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| firePelletPeak | 192 |
| firePelletLaunches | 3 |
| firePelletContacts | 3 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 9a634f1f-6818-4f01-ac07-4261e2d06768. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 자동 표적 선정·공격 보조·피해 연결은 미구현이다. 외부 발사·명중 신호를 받는 VFX다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.6·0.9·1.2초 발사,1.05·1.35·1.65초 명중 진단 신호. 별도 검사에서 명중 없는 도착과 중복 신호 거절·종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B11D.json, fire_companions_036_build.json, neong59_scope_check.json.
