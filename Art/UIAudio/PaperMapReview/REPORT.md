# 종이 지도 검토 근거

생성 시각: 2026-09-13T19:48:31.964867+00:00

최신 실행: `20260913-194416-061-69aa84` · 기술 상태: **PASS** · 원본 상태: `PASS`

사람의 시각 검토: **UNVERIFIED**. 실제 펼침의 형태·가림·질감을 사람이 판단해야 하며 자동 통과로 바꾸지 않습니다.

[오프라인 화면 검토](REVIEW.html)

[최신 실행 원본 JSON](Validation/20260913-194416-061-69aa84/paper_review.json)

## 증거 범위

실제 Unity Game View의 정지 화면을 표시합니다. 각 목표는 새로운 실제 unscaled-time 전환에서 따로 캡처했으며, 연속 촬영 영상이 아닙니다. 이미지를 합성하거나 임의의 모델 포즈로 대체하지 않았습니다.

Actual 1920x1080 uGUI Game View PNGs from sequential ScreenCapture calls. Each still uses a fresh real unscaled-time transition. Separate interaction checks directly dispatch synthetic uGUI PointerEventData; no OS/InputSystem events, arbitrary geometry pose, video, camera render, walking, or teleport. The QA harness holds world timeScale at zero between menus and restores it afterward.

Diagnostic setup and technical checks are not native input evidence, human visual approval, or GPU/device performance. PNG issuance follows a rendered game frame; actual issue/next-frame progress is recorded instead of claiming an exact requested pose. Focus-owned input blocking may persist when the application is unfocused.

## 실제 캡처

| 단계 | 목표 / 발행 / 다음 프레임 | 전환 경과 | 원본 |
|---|---|---|---|
| 01 · 접힌 종이 등장 | 0.100 / 0.102 / 0.123 | 0.135 s | [PNG](Screenshots/20260913-194416-061-69aa84/01_open_packet_010_1920x1080.png) |
| 02 · 세로 접힘 펼치기 | 0.300 / 0.308 / 0.324 | 0.355 s | [PNG](Screenshots/20260913-194416-061-69aa84/02_open_vertical_030_1920x1080.png) |
| 03 · 가로 접힘 펼치기 | 0.650 / 0.650 / 0.666 | 0.732 s | [PNG](Screenshots/20260913-194416-061-69aa84/03_open_horizontal_065_1920x1080.png) |
| 03b · 인쇄면이 드러나는 순간 | 0.850 / 0.856 / 0.874 | 0.961 s | [PNG](Screenshots/20260913-194416-061-69aa84/03b_open_printed_085_1920x1080.png) |
| 04 · 펼침 완료 | 1.000 / 1.000 / 1.000 | 1.114 s | [PNG](Screenshots/20260913-194416-061-69aa84/04_open_flat_100_1920x1080.png) |
| 05 · 다시 접기 | 0.500 / 0.489 / 0.465 | 0.346 s | [PNG](Screenshots/20260913-194416-061-69aa84/05_close_half_050_1920x1080.png) |
| 06 · 동작 줄이기 — 열기 | 1.000 / 1.000 / 1.000 | 0.021 s | [PNG](Screenshots/20260913-194416-061-69aa84/06_reduced_open_1920x1080.png) |
| 07 · 동작 줄이기 — 닫기 | 0.000 / 0.000 / 0.000 | 0.013 s | [PNG](Screenshots/20260913-194416-061-69aa84/07_reduced_closed_1920x1080.png) |

## 변경 전 화면

- [이전 여섯 패널 기준: map_fold_1920x1080.png](Before/map_fold_1920x1080.png)
- [이전 여섯 패널 기준: map_1920x1080.png](Before/map_1920x1080.png)

## 구현과 연동 범위

가로 한 번, 세로 한 번 접은 연속 3D 종이를 40 × 32 격자(1,353 표면 정점)로 평가합니다. 곡률을 가진 접힘, 네 겹의 분리, 주름과 가장자리 굴곡을 유지합니다. 앞·뒷면 2,706 UI 정점과 2,560개 보이는 삼각형을 깊이 순서로 uGUI에 투영하며, 이 종이 렌더러는 추가 Camera/RenderTexture를 사용하지 않습니다.

구현상 조작은 M, 열기 1.10초, 닫기 0.65초이며 unscaled time을 사용합니다. 동작 줄이기는 즉시 전환합니다. 이 캡처의 동작 줄이기 닫기는 진단 presenter API 검사이며, 저장된 옵션과 실제 M 키 입력의 연속 흐름을 검증하지 않습니다.

기존 지도 좌표, 탐험 안개, 현재 위치, 마커, 사용자 핀, 내부 상세 경로를 이어 쓰도록 구현했습니다. 위 설명은 구현 범위이며, 기능별 회귀 성공은 아래 명시 검사 근거만 따릅니다.

[실제 생성 종이 텍스처 HanjiWorn.png](../../../Oheangbu/Assets/_Project/Resources/WorldMap/Paper/HanjiWorn.png) · SHA-256 `797d2cc2c4f9caee7779145192376e14d73f6a2455fbebd5952b6d65b6c961ee`

[SOURCE.md · 자산 출처와 구현 근거](SOURCE.md)

## 모델

- [HanjiPaper_p000.obj](Models/20260913-192137-360-6a8529/HanjiPaper_p000.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p030.obj](Models/20260913-192137-360-6a8529/HanjiPaper_p030.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p065.obj](Models/20260913-192137-360-6a8529/HanjiPaper_p065.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p100.obj](Models/20260913-192137-360-6a8529/HanjiPaper_p100.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p000.obj](Models/20260913-192646-571-e4eebc/HanjiPaper_p000.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p030.obj](Models/20260913-192646-571-e4eebc/HanjiPaper_p030.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p065.obj](Models/20260913-192646-571-e4eebc/HanjiPaper_p065.obj): 1,353 vertices / 2,560 faces.
- [HanjiPaper_p100.obj](Models/20260913-192646-571-e4eebc/HanjiPaper_p100.obj): 1,353 vertices / 2,560 faces.

## 명시 기술 검사

| 상태 | 검사 | 기록된 세부 내용 |
|---|---|---|
| PASS | mesh-topology-bounded | vertices=1353; triangles=2560; all indices valid |
| PASS | geometry-finite-projected-surface | All sampled vertices project finitely; normals remain unit length. |
| PASS | both-centerlines-continuous | largest near-seam physical gap=0.000020 normalized widths |
| PASS | paper-local-stretch-bounded | largest physical/source triangle edge ratio=1.0639 |
| PASS | expanded-sheet-proportions | expanded width=0.996; height=0.722; center=(0.00, 0.00, 0.00) |
| PASS | 01_open_packet_010-open-timing | observed natural opening=1.120s; configured=1.100s; world timeScale=0 |
| PASS | whole-map-aspect-and-world-bounds | world ratio=0.666667; printed ratio=0.666667; world UV=(0.00, 0.00, 1.00, 1.00) |
| PASS | current-focus-restores-window | Whole then Current returns to the opening view and paper print layout. |
| PASS | synthetic-ugui-pan-scale-1.0 | Synthetic PointerEventData OnDrag; canvas scale=1.000; requested screen pixels=(48.00, 24.00); world landmark screen displacement=(48.00, 24.00); UV bounded=True. Not native mouse evidence. |
| PASS | synthetic-ugui-pan-scale-1.2 | Synthetic PointerEventData OnDrag; canvas scale=1.200; requested screen pixels=(48.00, 24.00); world landmark screen displacement=(48.00, 24.00); UV bounded=True. Not native mouse evidence. |
| PASS | ink-width-independent-of-orientation | Isolated actual ink raster at2:3 print aspect; half-alpha stroke thickness horizontal=8.00px, vertical=8.00px; requested8px. |
| PASS | interaction-fixture-restored | Restored Current view, print layout and canvas scale; progress unchanged. No click, pin, settings persistence or save operation issued. |
| PASS | 01_open_packet_010-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 01_open_packet_010-close-timing | observed close lifecycle=0.683s; configured=0.650s; PNG wait can extend this observation |
| PASS | 02_open_vertical_030-open-timing | observed natural opening=1.115s; configured=1.100s; world timeScale=0 |
| PASS | 02_open_vertical_030-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 02_open_vertical_030-close-timing | observed close lifecycle=0.664s; configured=0.650s; PNG wait can extend this observation |
| PASS | 03_open_horizontal_065-open-timing | observed natural opening=1.110s; configured=1.100s; world timeScale=0 |
| PASS | 03_open_horizontal_065-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 03_open_horizontal_065-close-timing | observed close lifecycle=0.674s; configured=0.650s; PNG wait can extend this observation |
| PASS | 03b_open_printed_085-open-timing | observed natural opening=1.133s; configured=1.100s; world timeScale=0 |
| PASS | 03b_open_printed_085-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 03b_open_printed_085-close-timing | observed close lifecycle=0.660s; configured=0.650s; PNG wait can extend this observation |
| PASS | 04_open_flat_100-open-timing | observed natural opening=1.107s; configured=1.100s; world timeScale=0 |
| PASS | 04_open_flat_100-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 04_open_flat_100-close-timing | observed close lifecycle=0.663s; configured=0.650s; PNG wait can extend this observation |
| PASS | 05_close_half_050-open-timing | observed natural opening=1.116s; configured=1.100s; world timeScale=0 |
| PASS | 05_close_half_050-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 05_close_half_050-close-timing | observed close lifecycle=0.661s; configured=0.650s; PNG wait can extend this observation |
| PASS | 06_reduced_open-instant | Reduced motion OpenPage resolves the presenter in the initiating call. |
| PASS | 06_reduced_open-instant-close | Diagnostic presenter SetExpanded(false) completes in its initiating call; unsaved reduced-motion preview. |
| PASS | 06_reduced_open-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=True |
| PASS | 07_reduced_closed-instant-close | Diagnostic presenter SetExpanded(false) completes in its initiating call; unsaved reduced-motion preview. |
| PASS | 07_reduced_closed-closed-cleanup | page empty; pause depth=0; expanded root inactive; focus=True; gate release pending=False |
| PASS | sequential-stills-complete | written=8/8 |
| PASS | paper-cycle-resource-growth-bounded | material delta=0; RenderTexture delta=0; allocated byte delta=1606524; allocation is diagnostic, not a GPU timing claim |
| PASS | isolated-runtime-progress-restored | Restored original in-memory progress; synchronized save snapshot cache before restoring original file bytes. |
| PASS | player-not-moved | No walk, teleport, or camera motion was requested. |
| PASS | qa-save-files-restored | Primary/backup/temporary suffixed-save file bytes and timestamps restored. Production slot never opened for writing. |
| PASS | settings-restored | Only unsaved reduced-motion preview was used. |
| PASS | review-png-evidence-available | Latest run: 8/8 planned PNGs are recorded written and have a valid PNG header. Missing captures are not technical passes. |
| UNVERIFIED | human-visual-judgment | 사람이 실제 펼침의 형태·가림·질감을 판단해야 합니다. 자동 기술 검사로 시각적 만족을 승인하지 않습니다. |
| UNVERIFIED | native-M-input-and-audio | 이 캡처는 진단 API의 실제 시간 전환입니다. M 키 입력, 사람의 소리 청취, 실제 기기 GPU 성능을 증명하지 않습니다. |

## 사람의 시각 검토 — UNVERIFIED

- [ ] 닫힌 종이가 네 겹으로 읽히고, 여섯 개의 세로 패널처럼 보이지 않는가
- [ ] 바깥 세로 접힘 이후 안쪽 가로 접힘이 펼쳐지는 순서가 자연스러운가
- [ ] 종이의 뒷면·주름·접힌 선·닳은 가장자리가 실제 화면에서 설득력 있게 보이는가
- [ ] 겹친 종이의 가림 순서와 교차 접힘 중심에 뚫림·깜빡임이 없는가
- [ ] 완전히 펼쳤을 때 지도, 탐험 안개, 현재 위치, 마커와 라벨이 맞게 정렬되는가
- [ ] M으로 열고 닫는 속도와 종이 소리가 플레이 흐름에 맞는가

## 원본 검사·빌드 파일

PASS/FAIL은 파일 내부의 명시 판정만 표시합니다. RUNNING, 판정 없음, 읽을 수 없는 파일은 UNVERIFIED입니다. 이전 실행의 성공을 최신 실행의 성공으로 사용하지 않습니다.

- **PASS** [Validation/20260913-192137-360-6a8529/paper_prepare.json](Validation/20260913-192137-360-6a8529/paper_prepare.json) — 원본 상태: `PASS`
- **FAIL** [Validation/20260913-192256-188-55227d/paper_review.json](Validation/20260913-192256-188-55227d/paper_review.json) — 원본 상태: `FAIL`
- **PASS** [Validation/20260913-192646-571-e4eebc/paper_prepare.json](Validation/20260913-192646-571-e4eebc/paper_prepare.json) — 원본 상태: `PASS`
- **PASS** [Validation/20260913-192726-451-22ffed/paper_review.json](Validation/20260913-192726-451-22ffed/paper_review.json) — 원본 상태: `PASS`
- **FAIL** [Validation/20260913-193506-774-07af9f/paper_review.json](Validation/20260913-193506-774-07af9f/paper_review.json) — 원본 상태: `FAIL`
- **PASS** [Validation/20260913-193736-528-35e75f/paper_review.json](Validation/20260913-193736-528-35e75f/paper_review.json) — 원본 상태: `PASS`
- **PASS** [Validation/20260913-194416-061-69aa84/paper_review.json](Validation/20260913-194416-061-69aa84/paper_review.json) — 원본 상태: `PASS`
- **UNVERIFIED** [Validation/build_candidate.json](Validation/build_candidate.json) — 원본 상태: `명시 판정 없음`

## 빌드 후보

[후보 매니페스트](Validation/build_candidate.json)

| 항목 | 상태 | 파일 / 명시 판정 |
|---|---|---|
| 빌드 후보 폴더 | 파일 있음 | [20260913T194504Z](../../../Builds/Playtest-20260915/20260913T194504Z) · 파일 존재는 실행 성공이나 사람의 승인을 뜻하지 않음 |
| 실행 파일 | 파일 있음 | [Oheangbu_Playtest.exe](../../../Builds/Playtest-20260915/20260913T194504Z/Oheangbu_Playtest.exe) · 파일 존재는 실행 성공이나 사람의 승인을 뜻하지 않음 |
| 압축 패키지 | 파일 있음 | [Oheangbu_UI_Playtest_20260913T194504Z.zip](../../../Builds/Playtest-20260915/Oheangbu_UI_Playtest_20260913T194504Z.zip) · 파일 존재는 실행 성공이나 사람의 승인을 뜻하지 않음 |
| 빌드 검사 | PASS | [release_build_report.json](../../../Builds/Playtest-20260915/20260913T194504Z/release_build_report.json) · 빌드 생성만: buildResult=Succeeded; totalErrors=0; totalWarnings=27; qaComplete=False; 원본 status=BUILD_CANDIDATE_CREATED_NOT_QA_COMPLETE |
| 실행 스모크 검사 | UNVERIFIED | [ui_smoke.json](../../../Builds/Playtest-20260915/20260913T194504Z/ui_smoke.json) · 23통과 · 1미검증 · 0실패; outcome=PASS_WITH_UNVERIFIED; 일부 미검증이 남아 전체 QA 통과로 표시하지 않음 |
| 패키지 검사 | PASS | [package_report.json](../../../Builds/Playtest-20260915/20260913T194504Z/package_report.json) · 패키지 CRC 검사만: crcVerified=True; files=283; sha256=a1d2b72c88ab6c152bc982d12ce0ad8e8343541f2ed63d2dcf03b4eda24be919; qaComplete=False |

빌드 PASS는 buildResult=Succeeded 및 totalErrors=0일 때의 생성 성공만 뜻합니다. 스모크는 outcome과 passed/failed/unverified를 함께 읽으며 일부 미검증이 남으면 통과 개수를 표시하고 전체 상태를 UNVERIFIED로 유지합니다. 패키지 PASS는 crcVerified=true, SHA-256 기록 및 양수 파일 수가 있는 CRC 검사만 뜻합니다. qaComplete=false를 전체 QA 승인으로 바꾸지 않으며 알 수 없는 형식은 UNVERIFIED입니다.

## 재생성

저장소 루트에서 `python Tools/PlaytestFeedback/build_paper_map_review.py`를 실행하면 기존 캡처·모델·검사 파일을 다시 읽습니다. Unity 프로젝트나 원본 이미지를 변경하지 않습니다.
