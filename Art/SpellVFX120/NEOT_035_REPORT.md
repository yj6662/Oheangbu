# 넛 · 넛 · 불검

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 길이1.25m 중심 칼날과 표면을 따라 방출하는 화염·불티를 제작했다.
- SetFireSwordGrip으로 위치·회전을 받는다. 영상은 진단용 궤적으로 검을 휘두른다.
- KTP Pattern_110을 검의 작은 문양과 명중 문양으로 사용했다. 명중 문양은 검 회전과 분리된다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양4tris·칼날8tris. 공유 재질5개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 2.4000000953674316 |
| destroyedAt | 2.4027955532073975 |
| castPeak | 123 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 193 |
| fireAuraContacts | 1 |
| fireAuraMarkPeak | 0.9863078594207764 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 53490c38-c561-4b11-b0d8-6a2852f88f95. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 플레이어 장비 교체·손 소켓·공격 판정 연결은 미구현이다. 외부 위치·회전·명중 신호를 받는 표현을 제작했다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.5~1.1초에 진단용 휘두르기,0.85초 명중 신호. 별도 검사에서 파지 위치 오차1mm 이하와 명중 전 타격 없음, 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B11B.json, fire_sword_035_build.json, neot58_scope_check.json.
