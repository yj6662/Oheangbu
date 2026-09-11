# 넉 · 넉 · 둘러 타는 화염

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 반경1.5m의 얇은 원형 방출 영역에 상승 화염·불티·연기를 배치했다.
- KTP Pattern_156을 바닥과 명중 지점에 사용했다.
- SignalFireAuraHit로 반복 명중의 위치와 문양 반응을 전달한다. 명중이 없으면 타격 연출은 발생하지 않는다.
- 기존 NativeField·NativeImpact 중복을 제거했다. 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양2개/4tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.200000047683716 |
| destroyedAt | 3.2002787590026855 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 273 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.9929285049438477 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: f42b5009-c2ef-4023-997d-4222f4d48bfd. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 지속 피해 게임 규칙과 실제 적 명중 연결은 미구현이다. 외부 명중 신호를 받을 수 있는 VFX를 제작했다.
- 명중 표현은 최근 접점 한 슬롯을 재사용한다. 동시 다수 접점의 개별 지속 표시는 제공하지 않는다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.6·1.3·2.0초에 진단용 명중 신호를 제공한다. 별도 검사에서 신호 없는 상태, 반복 명중, 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B109.json, fire_aura_032_build.json, neok55_scope_check.json.
