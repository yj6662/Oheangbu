# 곡 · 낮게 엮는 넝쿨

비주얼 상태: **사용자 검토 대기**. 한 술식만 제작·촬영한 기록이다.

## 변경

- 12개 줄기·24개 잎·4개 열린 문양 조각을 새로 구성했다. 수직 가시와 구분되는 낮은 덩굴망이다.
- 줄기와 잎은 보유 BrassicaNapus 색·노멀 텍스처, 열린 문양 조각은 KTP Pattern_33을 재사용한다. KTP LeafBloom 발동도 유지했다. Meshy 추가 요청은 없다.
- 메시 하나에 줄기·잎·문양의 세 재질 구역을 두었다. 생성 시 25개 지면 높이 표본으로 피팅하며 매 프레임 지면 검사를 반복하지 않는다. 소멸은 외곽부터 마르는 방식이다.
- SignalVineFootContact는 이미 확인된 발 위치만 받아 0.55초 동안 표시한다. 영역 밖·형성 전 신호를 거절하고 동시 표시는 최대3개다. ReleaseVineField는 한 번만 받아들인다.
- 첫 검사에서 발견한 편집기용 임시 메시 잔존을 전용 소유 컴포넌트로 수정했다. 같은 실패 검사에서 남은 미참조 메시1개도 정리했으며 재검사를 통과했다.
- 원본 프로필은 WoodDetailOriginals/014_ACE1.asset.txt에 보존했다. 다른119개 프로필은 변경하지 않았다.

## 실제 비용

정점2,328개·줄기3,552tris·잎384tris·문양160tris = 총4,096tris. 메시 렌더러1개(서브메시3개), 지면 피팅용 런타임 메시1개. 접촉 선3개(각13점 이하)와 KTP 파티클은 별도 비용이다.

## 기술 확인

단독 C2 실제 Update/Destroy 검사 통과. 오류 0, 셰이더 오류 0.

| 항목 | 관측값 |
|---|---:|
| life | 4.199999809265137 |
| destroyedAt | 4.201521396636963 |
| castPeak | 155 |
| impactPeak | 0 |
| nativeSystemsPeak | 10 |
| capacityPeak | 312 |
| vineMaxSpread | 1.0 |
| vineContacts | 2 |
| vineMaxActiveContacts | 1 |
| vineReleased | True |
| vineVisibleFrames | 1078 |
| vineMeshGone | True |

씬·카메라·참조 자산 복귀: True. root·children 제거: True.

App MVID: 028e2c6a-cf1e-44d2-8286-50d764af3be4. 네 촬영과 단독 Play의 DLL 일치 확인. 두 영상의 전체 디코드·24fps 시간 검사 통과.

## 미완료·미검증

- 카탈로그 시연용 원형 계획과 발 접촉 신호를 사용했다. 실제 적 탐색·감속·장판 시전은 아직 연결되지 않았다.
- 형태·색·KTP 문양·걸림 획의 가독성은 사용자 검토 대기다.
- 급격한 지형 단차·복층·이동하는 적 발 추종·30/60/120fps 감속 조건·동시 장판 성능은 미검증이다.

1280×720·24fps. 반경1.75m, 형성 시작0.38초 뒤1.1초 동안 확산한다. 시연 접촉1.6/2.45초, 해제3.5초. 임시 대상 실루엣은 실제 적 AI가 아니며 접촉점은 별도로 공급했다.

이전21은 D3D12, 현재 영상은 Editor 복구용 D3D11이다. 미술·전체 성능 PASS를 의미하지 않는다.

근거: single_play_ACE1.json, vine_field_014_build.json, gok35_scope_check.json.
