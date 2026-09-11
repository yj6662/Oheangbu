# 녹 · 녹 · 화염 뒤 회복 문양

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- KTP Pattern_156을 발동·타격·회복 문양으로 사용했다.
- ConfirmFireHealingHit에 활성 대상과 접촉·지면 위치를 전달해야 회복 문양을 생성한다.
- 일반 명중 시계와 회복 확인을 분리했다. 대상 없는 요청은 거절한다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양3개/6tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.4000000953674316 |
| destroyedAt | 3.4018030166625977 |
| castPeak | 123 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 309 |
| fireAuraContacts | 1 |
| fireAuraMarkPeak | 0.9899935722351074 |
| fireHealingPeak | 1.0 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: c40e034f-cfaa-4614-a5a6-37df3a5bc976. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 적 판별·피해·HP 회복 연결은 미구현이다. 호출자가 적 명중을 확인한 뒤 표현 API를 호출해야 한다.
- 영상과 Play 검사에서는 진단용 대상을 전달한다. 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.8초에 진단용 대상 명중을 제공한다. 별도 검사에서 명중 시계만 있는 허공 시전과 null 대상은 회복 문양을 만들지 않는 것을 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B179.json, fire_healing_038_build.json, nok61_scope_check.json.
