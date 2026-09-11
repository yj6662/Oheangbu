# 농 · 농 · 화염 뒤 수증기

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 화염 방출은1초에 멈추고 수증기는 더 오래 남도록 구분했다.
- 밝은 회색 입자가 부풀고 상승하다 옅어지는 수증기를 제작했다.
- KTP Pattern_126을 영역과 명중 문양에 사용했다.
- 다른119개 프로필은 보존했다.

## 실제 비용

Particle System7개/입자용량432개, 문양4tris. 공유 재질4개. KTP 시전 효과 비용 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.599999904632568 |
| destroyedAt | 4.6015520095825195 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 286 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.991483211517334 |
| steamOnlySeen | True |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 544c9e2b-6a85-4261-86e5-28e27ec1de01. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 명중률 감소·시야 판정·피해 연결은 미구현이다. 수증기는 입자 표현이며 유체 시뮬레이션이 아니다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps. 진단용 명중 신호3회.2.5초 이후 화염 입자0·수증기 존재 상태와 수명 종료 정리를 검사했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B18D.json, fire_steam_042_build.json, nong65_scope_check.json.
