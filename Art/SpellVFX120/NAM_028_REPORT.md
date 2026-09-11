# 남 · 남 · 봉인 불씨의 격발

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 남의 부착·유지·격발을 분리했다. 명중 시각만으로 폭발을 자동 실행하지 않는다.
- DetonateEmberCharge 표현 신호를 한 번만 받으며 부착 이전·중복·수명 밖 요청을 거절한다.
- KTP Pattern_136의 열린 매듭 테두리를 작은 봉인과 격발 문양에 사용하고 기존 화염·불티·연기와 결합했다.
- 다른119개 프로필과 원본 에셋은 보존했다.

## 실제 비용

전용 Particle System8개/입자용량464개, 문양1개/2tris. 공유 재질4개. KTP 동반 효과 비용 별도, 런타임 메시·재질 복제0.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.200000047683716 |
| destroyedAt | 3.2024810314178467 |
| castPeak | 122 |
| impactPeak | 185 |
| nativeSystemsPeak | 17 |
| capacityPeak | 285 |
| fireParticlePeak | 99 |
| firePatternPeak | 1.0 |
| fireImpact | True |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 474aed9f-26f2-417e-b507-ac95104dac90. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 남의 표현 시안이며 비주얼 판정은 사용자 검토 대기입니다.
- 공격 진 명중에 따른 실제 C4 격발·피해·그로기 연결은 미구현이다. 영상과 단독 Play는1.8초에 진단용 격발 신호를 제공한다.
- 실제 전투·동시 시전 성능은 미검증이다.

1280×720·24fps.0.6초 부착과1.8초 격발 시연. 편집기 검사에서는 격발 없이 유지되는 상태와 중복 요청 거절도 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B0A8.json, ember_charge_028_build.json, nam49_scope_check.json.
