# 낫 · 낫 · 갈라지는 초승달 화염

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 제공된 명중 시각 이후에만 좌우 초승달 방출기를 전개한다.
- 초승달 면에서 화염 입자를 방출하고 좌우 32도 방향으로 이동시켜 두 갈래를 구분했다.
- KTP Pattern_110 명중 문양과 KTP 화염 아틀라스·불티·연기를 결합했다.
- 다른119개 프로필은 보존했다.
- 초승달 속도 세 축의 곡선 모드를 통일해 Play 모드 오류를 수정했다.

## 실제 비용

전용 Particle System9개/입자용량592개. 문양2tris, 공유 방출용 초승달 메시48tris(표면 방출 계산용). 공유 재질4개. KTP 동반 효과 비용 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 1.7999999523162842 |
| destroyedAt | 1.8027421236038208 |
| castPeak | 123 |
| impactPeak | 185 |
| nativeSystemsPeak | 25 |
| capacityPeak | 479 |
| fireParticlePeak | 205 |
| firePatternPeak | 1.0 |
| fireSplitPeak | 3.386181354522705 |
| fireImpact | True |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: cfb13185-f177-464b-9782-f426afe3d8ac. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 2차 화염은 표현이며 추가 피해·충돌 판정은 연결하지 않았다.
- 실제 전투 입력과 다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.6초에 진단용 명중 시각을 제공한다. 편집기 검사에서는 명중 전 분리 없음과 명중 후 두 방출기의 분리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B0AB.json, flame_crescent_029_build.json, nat51_scope_check.json.
