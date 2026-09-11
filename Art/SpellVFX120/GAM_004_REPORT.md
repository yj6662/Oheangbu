# 감 · 덩굴 봉인 씨방

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 감 전용 납작한 씨방과 열린 덩굴 매듭 메시를 제작했다. 부착 동안 매듭만 작게 수축한다.
- KTP Pattern_3 인문과 발동·격발 연출, 보유 수피·잎 텍스처를 재사용했다.
- AttachSeedPod는 지정한 부착점과 표면 방향을 대상의 로컬 좌표로 보존한다. DetonateSeedPod를 받기 전에는 KTP 격발을 재생하지 않는다.
- 몸체를 따라가는 부착과 중복 격발 거부를 검사했다. 다른119개 프로필은 변경하지 않았다.

## 실제 비용

씨방320tris + 네 매듭608tris + 잎획64tris + 인문2tris = 총994tris. 메시 렌더러10개, 런타임 메시 복제0개. KTP 파티클 별도.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 3.0999999046325684 |
| destroyedAt | 3.100966691970825 |
| castPeak | 156 |
| impactPeak | 153 |
| nativeSystemsPeak | 13 |
| capacityPeak | 312 |
| podAttached | True |
| podDetonated | True |
| podVisibleFrames | 421 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: a2195dc8-fa5c-4545-b216-74f9ac13e791. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 시연용 부착·격발 신호로 촬영했다. 실제 공격 진 명중·속성 피해·그로기 연동은 미연결이다.
- 문양 크기와 격발의 비주얼 판정은 사용자 검토 대기다.
- 실제 적 표면 변형과 다중 동시 부착 성능은 미검증이다.

1280×720·24fps. 부착0.55초, 격발1.65초. 외부 카메라는 씨방과 매듭을 가까이 보여준다. 시연 실루엣은 적 AI가 아니다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_AC10.json, seed_pod_004_build.json, gam38_scope_check.json.
