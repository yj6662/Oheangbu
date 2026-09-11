# 누 · 누 · 잔존 화막 결계

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 바닥 KTP Pattern_156 위에 원형 화벽을 구성했다.
- 화염의 상승 속도와 수명을 늘려 높이를 확보하고 연기와 불티를 분리했다.
- 접촉 피드백은 외부 신호로 받는다. 너의 패링 효과로 분류되지 않는지 검사했다.

## 실제 비용

Particle System 7개, 입자 용량 432개, 문양 4tris. 공유 재질 사용. KTP 시전 효과 비용 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.0 |
| destroyedAt | 4.000091075897217 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 343 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.9922852516174316 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: bfd0a33b-3e7d-4416-9f66-b46660732a08. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 결계 피해 경감·공격 충돌 연결은 미구현입니다. 패링 판정·보상은 추가하지 않았습니다.
- 다중 시전 성능과 실제 전투는 미검증입니다. 동시 접촉 표시는 최신 한 곳을 사용합니다.

1280×720·24fps 두 시점. 진단용 접촉 3회와 수명 종료 정리를 확인합니다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B204.json, fire_ward_043_build.json, nu66_scope_check.json.
