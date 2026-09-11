# 낭 · 낭 · 관통하는 열기

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 명중 이후 화염 방출기가 0.36초 동안 2.16m 전진하며 관통 열기를 표현한다.
- 작고 긴 화염 꼬리와 제한된 흔들림으로 일반 불덩이와 구분했다.
- KTP Pattern_110 명중 문양은 이동하는 화염과 분리해 접촉 위치에 둔다.
- 명중 신호 없는 시전은 관통·명중 문양을 만들지 않는다. 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양1개/2tris. 공유 재질4개. KTP 동반 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 1.7999999523162842 |
| destroyedAt | 1.8031631708145142 |
| castPeak | 123 |
| impactPeak | 185 |
| nativeSystemsPeak | 25 |
| capacityPeak | 479 |
| fireParticlePeak | 198 |
| firePatternPeak | 1.0 |
| firePenetrationPeak | 2.1599998474121094 |
| fireImpact | True |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 0b61c3b4-2b13-4937-a2c0-318bc51d3033. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 관통 연출이며 방어력 무시·피해·다중 타격 규칙은 구현하지 않았다.
- 실제 전투 입력과 다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.6초에 진단용 명중 시각을 제공한다. 별도 검사에서 명중 없는 경우의 관통 연출 억제와 수명 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B0AD.json, piercing_flame_030_build.json, nang53_scope_check.json.
