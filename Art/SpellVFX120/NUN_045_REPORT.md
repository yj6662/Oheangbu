# 눈 · 눈 · 덩굴 연소

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- BeginFireBurnPath로 전달받은 시작점과 끝점 사이를 1.8초에 걸쳐 태우는 표현을 제작했다.
- 경로가 없으면 불길을 방출하지 않으며 중복 시작을 거부한다.
- 작은 화염·상승 연기·불티를 분리하고 KTP Pattern_156으로 연소점과 완료 지점을 표시한다.

## 실제 비용

Particle System 7개, 입자 용량 432개, 문양 4tris. KTP 시전 효과 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.4000000953674316 |
| destroyedAt | 3.4008588790893555 |
| castPeak | 122 |
| impactPeak | 0 |
| nativeSystemsPeak | 8 |
| capacityPeak | 194 |
| fireBurnProgress | 1.0 |
| fireAuraParticlePeak | 251 |
| fireAuraContacts | 1 |
| fireAuraMarkPeak | 1.0 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 57426fe7-f1b5-4e9d-9998-d45f89fb37dd. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 비주얼 판정은 사용자 검토 대기입니다.
- 영상은 빈 진단 경로의 VFX입니다. 실제 덩굴 메시의 탄화·소멸, 길 열림·충돌 제거는 미연결입니다.
- 비전투 표현만 추가했습니다. 실제 필드 상호작용과 다중 효과 성능은 미검증입니다.

1280×720·24fps 두 시점. 명시적 경로 시작·중복 거부·완료 진행률·문양·종료 정리를 검사했습니다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_B208.json, fire_burn_045_build.json, nun67_scope_check.json.
