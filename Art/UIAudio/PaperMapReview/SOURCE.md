# 한지 지도 제작 출처

2026-09-14 · 상태 TEST. 한지 비트맵의 출처, 기하 제작 방식, 확인한 준비 결과를 기록한다. 새 지도 런타임 검증 결과는 해당 실행의 `Validation/` 보고서에 별도로 남긴다.

## 이미지

- 생성 도구: **ImageGen**. 요청 모드: **generate**.
- 원본: [생성 PNG](C:/Users/yj666/.codex/generated_images/01a079b8-3c10-7d33-aa15-2f833e39fbef/exec-b8c442f3-9233-4501-9019-edcf08c2af5f.png).
- 프로젝트 사본: [HanjiWorn.png](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Resources/WorldMap/Paper/HanjiWorn.png).
- 두 파일의 SHA-256은 동일하다: `797d2cc2c4f9caee7779145192376e14d73f6a2455fbebd5952b6d65b6c961ee`. 생성 비트맵을 그대로 복사했으며 별도 사진이나 지도 도안 출처를 사용하지 않았다.
- 요청 내용: 글자와 지도 인쇄가 없는 낡은 한국 한지, 가로·세로 중심 접힘 한 번씩, 구김과 섬유, 닳은 가장자리, **종이 밖의 실제 투명 배경**. 지도·안내 문자는 게임이 별도로 그린다.

실제 생성 요청문:

> Use case: photorealistic-natural. Asset type: game texture for a Korean hanji paper map sheet. Generate ONE top-down orthographic scanned sheet of old handmade Korean mulberry hanji, completely blank without any writing, map, symbols or artwork. Landscape sheet aspect ratio about 1.38:1 centered and nearly filling the image, with 5 percent transparent margin. Genuine transparent background outside the sheet. Four quadrants with exactly ONE horizontal and ONE vertical central fold crease crossing at the exact center, no accordion folds, no grid. The sheet has been folded twice then reopened and gently crumpled many times: visible soft natural branching fine crumple creases and real translucent mulberry fibers, warm muted ivory-gray paper with subtle age variation, slightly grubby handled folds. Frayed deckled irregular worn edges with tiny chips and thin feathered fibers, a few shallow edge tears, curled but basically flat corners, not dramatically burnt or fantasy parchment. Soft even scan lighting with gentle crease relief, no cast shadow outside, no desk or scene. Keep the central 80 percent readable and softly textured for overlaying a game map later. No text, no labels, no logos, no border decoration. This is a texture, not a mockup or UI screenshot.

## 임포트와 기하

[paper_prepare.json](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Validation/20260913-192646-571-e4eebc/paper_prepare.json)은 Edit Mode 준비 검사 **PASS**다. 원본 알파 채널이 있고 실제 크기는 **1473×1068**이다. 알파 250 미만 픽셀은 266,938개, 250 초과 픽셀은 1,299,178개다. Clamp·무압축·최대2048·MipMap 없음·`alphaIsTransparency=true`로 임포트했고 검사 후 CPU 읽기를 해제했다. 이 결과는 텍스처 준비와 OBJ 출력에 대한 검사다.

종이 형상은 [WorldMapPaperGeometry.cs](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapPaperGeometry.cs)의 연속 3차원 표면이다. 두 중심 접힘을 공유하는 40×32칸에서 1,353정점·2,560삼각형을 계산한다. 좁은 곡면 접힘과 약한 구김·가장자리 굴곡을 포함한다. [WorldMapPaperGraphic.cs](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapPaperGraphic.cs)는 이 좌표를 원근 투영해 uGUI로 표시한다. 앞면의 지도 인쇄와 뒷면의 빈 종이를 구분한다.

다음 OBJ는 작은 접힘 삼각형의 법선 정규화를 수정한 뒤 다시 내보낸 기하의 실제 출력이며, 정점·UV·법선·삼각형 면을 포함한다. 진행도별 정적 형상 자료로 제공한다. 최초 준비 실행 `20260913-192137-360-6a8529`의 보고서와 OBJ는 이력으로 보존한다.

- [접힌 상태 0](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Models/20260913-192646-571-e4eebc/HanjiPaper_p000.obj)
- [세로 접힘 진행 0.30](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Models/20260913-192646-571-e4eebc/HanjiPaper_p030.obj)
- [가로 접힘 진행 0.65](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Models/20260913-192646-571-e4eebc/HanjiPaper_p065.obj)
- [펼친 상태 1](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Models/20260913-192646-571-e4eebc/HanjiPaper_p100.obj)

이번 한지 제작에는 **Blender·Meshy를 사용하지 않았다**. 런타임 동작은 정해진 두 접힘과 표면 변형을 비스케일 시간으로 계산하며 **천 물리 시뮬레이션을 사용하지 않는다**. 생성된 질감이나 정적 OBJ만으로 실제 개폐의 부드러움·입력 동작·GPU 성능·최종 미술 승인을 주장하지 않는다.

## 이전 자료와 실행 증거

`Before/`에는 재설계 전 `WorldMapPresenter.cs`, `WorldMapFoldPanel.cs`, `map_1920x1080.png`, `map_fold_1920x1080.png`를 보존했다. 기존 메뉴 보고서는 당시 구현의 증거다.

재설계 검토에서 기존 지형 비트맵의 약한 음영 알파를 도로·하천 잉크의 알파에도 곱하면 선이 지나치게 옅어지는 문제를 확인했다. 지형의 낮은 알파는 원래 음영 농도이며 탐험 여부가 아니다. 후속 셰이더는 종이 앞면·탐험 셀·인쇄 영역을 계속 검사하면서, 지형 바깥 여부와 지형 음영 농도를 분리해 선의 가독성을 보완했다. 이 변경은 발견 데이터를 추가하거나 미탐험 셀을 공개하지 않는다. 실제 반영 이미지와 실행 판정은 후속 실행 보고서에서 확인한다.

새 정지 이미지는 `Screenshots/<실행 ID>/`, 실행 검사는 `Validation/<실행 ID>/paper_review.json`에 저장한다. 각 이미지는 실제 비스케일 개폐를 새로 시작한 뒤 Game View에서 순차 촬영하며, 요청 진행도와 발급 시점·다음 프레임의 실제 진행도를 함께 기록한다. 진단 기하 OBJ와 실제 시간에 따른 정지 이미지의 증거 범위를 구분한다.

최종 [실행 보고서 `20260913-194416-061-69aa84`](C:/Users/yj666/Oheangbu/Art/UIAudio/PaperMapReview/Validation/20260913-194416-061-69aa84/paper_review.json)는 **40개 검사 PASS·실제 1920×1080 정지 이미지 8장**이다. 초기 법선 실패와 이후 닫힘 시간 실패 보고서·이미지를 보존한 상태에서 수정 후 재검사했다. 직전 `193736-528-35e75f`는 기술 PASS였지만 정지 이미지에 겹친 종이의 삼각형 쐐기 모양 오류가 남아 있었다. 네 구획을 먼저 묶어 정렬하도록 고친 최종 이미지에서 해당 오류가 제거된 것을 확인했으며 종이 기하·OBJ는 그대로다. 캔버스 배율별 이동은 합성 uGUI `OnDrag` 검사이며 실제 마우스·휠 조작이 아니다. 이 결과를 사용자 보행 완주, GPU 성능, 최종 미술 승인이나 독립 실행 후보의 포장·실행 통과로 확대하지 않는다.
