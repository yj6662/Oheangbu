# 강 · 틈을 찾는 젖은 뿌리

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 가는 가시를 새로 제작하고 어두운 수피와 제한된 젖은 광택을 적용했다. 파편 폭발은 추가하지 않았다.
- KTP Pattern_33을 뿌리선의 끊어진 결로, Pattern_192를 작은 침윤 자국으로 사용한다. KTP 발동·명중은 축소해 유지했다.
- SignalWetRootContact는 확정된 세 표면 경로를 복사해 대상의 로컬 좌표로 보존한다. 모델과 입력 배열을 변경하지 않으며 매 프레임 충돌 검사를 수행하지 않는다.
- 경로 복사·표면 추종·신호 전 비활성·흡수 소멸을 검사했다. 검사기의 Renderer 배열 타입 오류는 수정한 뒤 재컴파일했다. 다른119개 프로필 불변을 확인했다.

## 실제 비용

가시44tris + 먹 자국2tris = 고정 메시46tris, MeshRenderer2개. 표면 선3개는 각16점(총48점) 이하이며 캡·모서리를 포함한 LineRenderer 생성 삼각형과 KTP 파티클은 별도 비용이다. 런타임 Mesh 객체 복제0개.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 1.5 |
| destroyedAt | 1.5006773471832275 |
| castPeak | 155 |
| impactPeak | 153 |
| nativeSystemsPeak | 23 |
| capacityPeak | 515 |
| wetContact | True |
| wetMaxPaths | 3 |
| wetVisibleFrames | 311 |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 8ae50962-adb9-40f3-9d77-6e847e9a76eb. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 영상의 표면 경로는 고정 시연 데이터다. 실제 갑주 틈 탐색·방어력 무시 피해는 미연결이다.
- 젖은 질감·선 가독성은 사용자 검토 대기다.
- 스킨 메시 변형 표면과 동시 사용 성능은 미검증이다.

1280×720·24fps. 0.6초 접촉 뒤0.3초 동안 뻗고 다음0.5초 동안 흡수한다. 외부 카메라는 접촉면을 가까이 보여준다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_AC15.json, wet_root_006_build.json, gang40_scope_check.json.
