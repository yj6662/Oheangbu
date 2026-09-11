# 섬082 단독 수정 기록

2026-09-09. 한 술식씩 순차 제작하며 비주얼 판정은 사용자가 맡는다.

## 변경

- 기존 넓은 리본 본체4개와 보조 리본2개를 끝이 가늘어지는 비대칭 열린 곡선 한 쌍으로 교체했다. 두 곡선은 한 메시다.
- 400 정점·792 tris, 유지 구간 크기 0.56×0.22×0.014m. KTP 입자 삼각형은 이 수치에 포함하지 않는다.
- 전달된 목표 위치 양옆에 표식을 놓는다. 등장 직후 가로 폭116%에서100%로 정착하고, 수명 끝에 축소·소멸한다. 실제 궤적 입력이 없어 임의 예측선은 그리지 않는다.
- 프로필 변경은 BodyMesh, Count, RibbonCount, PartScale. 기존 재질·KTP 금 속성 발동 문양·Pattern_236·3.9초 수명은 유지했다.
- 손087·석080 프로필 해시는 변경되지 않았다. 원래 섬 프로필은 MetalDetailOriginals/082_C12C.asset.txt에 보존했다.

## 확인 결과

| 항목 | 결과 | 근거 |
|---|---|---|
| 컴파일·단독 실제 Update | 통과 | single_play_C12C.json |
| 수명·잔존 객체 | 통과 | 3.90106초에 제거, rootGone·childrenGone true |
| 오류·셰이더 오류 | 통과 | 각각0건 |
| C2 씬·카메라·원본 참조 복귀 | 통과 | restored·sceneUnchanged·sourceUnchanged true |
| 기본·근접 영상 파일 | 통과 | ClipsKTPIntegrated24/ClipsKTPExternal24 각각 encoding_report.json, 한 종 전체 디코드·시간 검증 |
| 형태·가독성·미감 | 사용자 검토 대기 | SEOM_082_REVIEW.html |
| 실제 심안 버프·적 속성·투사체 궤적 연동 | 미완료 | 이번 변경은 카탈로그 표식 시안 |
| 전체 게임 회귀·목표 FPS | 미검증 | 단독 수명 검사를 성능 인증으로 사용하지 않음 |

App MVID: 9d4b416d-adb6-4e24-a51c-dbf7303ebbed. 캡처는1280×720·24fps의 진단용 C2 재생으로 실제 플레이어 입력 영상이 아니다. 수정 전21은D3D12, 수정 후24는 Editor 복구용D3D11에서 촬영했다. 실제 Play 감사의 modelVisibleFrames는 별도 Meshy 모델 경로의 지표이며, 이번 절차형 본체의 화면 가독성을 검사하지 않는다.
