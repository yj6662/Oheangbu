# 곰 · 숲을 부르는 사슴

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 기존 Meshy 7 사슴과 Blender 정리본을 보존했다. 새 유료 생성은 요청하지 않았다.
- 현재 소환 프로필을 최신 공통 런타임에서 다시 촬영하고 단독 Update·수명을 확인했다.
- KTP 소환진·발동과 연결된 정적 MeshRenderer를 사용한다. 리그나 보행·공격 애니메이션은 포함하지 않는다.

## 실제 비용

사슴 실측12,543tris·15,132정점·MeshRenderer1개. 스키닝·Animator0개. KTP 소환진·발동은 별도 비용이다. 이번 마무리의 Meshy 추가 소비0크레딧.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.699999809265137 |
| destroyedAt | 4.70125150680542 |
| castPeak | 155 |
| impactPeak | 0 |
| nativeSystemsPeak | 35 |
| capacityPeak | 712 |
| modelVisibleFrames | 1245 |
| modelRendererCount | 1 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 63901a42-5a0b-40f2-8481-d7afacc6ccf9. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 정적 소환 VFX다. 소환수 보행·공격·전투 AI·스키닝은 미구현이다.
- 사슴과 소환진의 비주얼 최종 판정은 사용자 검토 대기다.
- 여러 소환수 동시 사용 성능은 미검증이다.

1280×720·24fps. 현재 프로필의 등장을 두 시점으로 다시 촬영했다. 이전21과 현재44는 같은 생성 모델이며 공통 런타임과 촬영 시점이 다르다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_ACF0.json, meshy_unity_import.json, gom44_scope_check.json.
