# 놋 · 놋 · 달아오른 갑주

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- KTP Pattern_126을 영역과 명중 문양에 사용했다.
- 명중 불티에 아래 방향 속도와 중력을 적용했다.
- 명중 문양의 가장자리가 소멸하도록 침식 값을 구동한다.
- 다른119개 프로필은 보존했다.

## 실제 비용

Particle System7개/입자용량432개, 문양4tris. 공유 재질4개. KTP 시전 효과 비용 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.200000047683716 |
| destroyedAt | 3.2026169300079346 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 273 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.9938850402832031 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: f98f4e57-58f9-4e7a-90d6-f2ebd070043e. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 갑옷 변형·방어력 감소·피해 연결은 미구현이다. 외부 명중 신호를 받는 표현이다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps. 진단용 명중 신호3회를 제공한다. 반복 명중 문양과 입자·수명 종료 정리를 검사했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B18B.json, fire_melt_041_build.json, not64_scope_check.json.
