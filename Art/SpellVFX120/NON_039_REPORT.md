# 논 · 논 · 방사 뒤 역화

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 전방 화염과 종료 후 후방 폭발을 분리했다.
- EndFireBackblast 신호를 한 번만 받아 방출을 중단하고 지정한 후방 위치에서 폭발한다.
- KTP Pattern_156을 발동구와 큰 폭발 문양에 사용했다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양2개/4tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 2.4000000953674316 |
| destroyedAt | 2.4009158611297607 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 311 |
| fireAuraContacts | 1 |
| fireAuraMarkPeak | 0.9930009841918945 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 85824104-6e9d-4617-8882-5c67b0ef0610. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 술식 종료 이벤트·후방 피해 연결은 미구현이다. 외부 종료 신호를 받는 표현이다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.1.2초에 진단용 종료 신호와 후방 폭발 위치를 전달한다. 별도 검사에서 종료 전 폭발 없음과 중복 종료 거절·수명 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B17C.json, fire_backblast_039_build.json, non62_scope_check.json.
