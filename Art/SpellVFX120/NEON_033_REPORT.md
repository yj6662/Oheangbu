# 넌 · 넌 · 응축한 불씨의 강화

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- KTP Pattern_136의 준비 문양과 작은 원형 화염을 구성했다.
- ConsumeFireEmpower와 ConfirmFireEmpowerHit를 분리했다. 강화 소비만으로 타격 문양을 만들지 않는다.
- 소비와 명중은 각각 한 번만 수락한다. 종료 시 입자·문양을 정리한다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양2개/4tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 2.5999999046325684 |
| destroyedAt | 2.6015195846557617 |
| castPeak | 123 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 105 |
| fireAuraContacts | 1 |
| fireAuraMarkPeak | 0.9914312362670898 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 66fb12a3-2253-4320-8032-7402ff5972cb. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 다음 공격의 위력 증가와 장비 소켓 연결은 미구현이다. 준비 위치는 VFX 생성 원점이다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.1.2초 소비,1.4초 명중 진단 신호. 별도 검사에서 빗나간 소비의 타격 연출 억제, 중복 신호 거절, 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B10C.json, fire_empower_033_build.json, neon56_scope_check.json.
