# 넘 · 넘 · 흔들리지 않는 화인

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- KTP Pattern_126을 유지·피격 문양으로 사용했다.
- 몸 주변 반경0.48m에 낮은 화염을 두어 넉의 광역 화염과 구분했다.
- 반복 피격 신호가 유지 화염을 중단하거나 소모하지 않는다.
- 다른119개 프로필은 보존했다.

## 실제 비용

전용 Particle System7개/입자용량432개, 문양2개/4tris. 공유 재질4개. KTP 시전 효과 비용 별도. 추가 물리 질의 없음.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.200000047683716 |
| destroyedAt | 3.2008755207061768 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireAuraParticlePeak | 211 |
| fireAuraContacts | 3 |
| fireAuraMarkPeak | 0.9929237365722656 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: cbd0263b-be64-40ca-acc4-36857cdb28f7. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 실제 피격 중단 억제 규칙·몸 소켓 연결은 미구현이다. 생성 원점과 외부 피격 신호를 사용하는 표현이다.
- 실제 전투·다중 동시 시전 성능은 미검증이다.

1280×720·24fps.0.6·1.3·2.0초에 진단용 피격 신호를 제공한다. 별도 검사에서 반복 피격 이후 유지 화염과 수명 종료 정리를 확인했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B118.json, fire_resolve_034_build.json, neom57_scope_check.json.
