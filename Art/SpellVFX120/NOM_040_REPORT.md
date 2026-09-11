# 놈 · 놈 · 화염 소환수

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 기존 Meshy7 모델과 재질을 재사용했다. 원본 작업 ID01a08490-ecd3-7058-b976-6405be0a1bdd 기록을 보존했다.
- KTP Pattern_156 소환진을 모델의 지면 위치에 맞췄다.
- 발치 화염과 몸 주변 불꽃·불티를 추가했다. 명중 표현은 외부 신호를 받는다.
- 다른119개 프로필과 소환수 원본을 보존했다. 추가 Meshy 크레딧0.

## 실제 비용

추가 Particle System7개/입자용량432개, 문양4tris. Meshy 원본12,532tris(원본 통계 기준). KTP 시전 효과 비용 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.599999904632568 |
| destroyedAt | 4.602537155151367 |
| castPeak | 123 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 273 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.993714451789856 |
| modelRendererCount | 1 |
| modelVisibleFrames | 1249 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: e90c97db-6e38-47e2-887d-5410088938c4. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 기존 소환수는 정적 모델이며 새 보행·공격 애니메이션이나 AI·피해 규칙은 구현하지 않았다.
- 실제 전투·다중 동시 소환 성능은 미검증이다.

1280×720·24fps. 기존 소환·소멸 표현을 유지하며 진단용 명중 신호3회를 제공한다. Meshy 모델 구성, 화염과 문양, 종료 정리를 확인한다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B188.json, fire_summon_040_build.json, nom63_scope_check.json.
