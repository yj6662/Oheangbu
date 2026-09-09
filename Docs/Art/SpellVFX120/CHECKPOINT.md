# 술식 VFX120 — 2026-09-09 현재 작업 체크포인트

상태: **TEST / 작업 중간 보존**. 최종 미술 승인이나 120종 게임 규칙 구현 완료를 뜻하지 않는다. 기존 Draft PR #14에 후속 작업을 추가한다.

## 포함 범위

- CSV 120칸의 프로필·프리팹·공유 메시/재질. 100개 배정과 20개 의도적 공백이며 공백은 Reserve 시각 후보로 유지한다.
- 기존 15자용 VisualSet, 신규 VFX 어댑터 분기와 PlayerRig 연결. 나머지 85개 배정 자산에는 새 게임 규칙을 연결하지 않았다.
- 독립 검수 씬, Blender 소환수 원본/FBX, 이전 검수 자료 및 후속 Stills 600장, C2 실제 카메라 캡처 45장, 검토 보고서와 갤러리.
- 물 계열 온·옷·옹·엄·우의 개별 전개, 다섯 속성의 무기 부착 표현, 산의 낙인·상의 요격 후 표현, 노의 입체 화염과 모의 모래 높이·대비 및 금속 명암 보완. 이 소스 수정분은 재촬영 전이다.
- 밝고 어두운 배경 비교, C2 진단 카메라, 폰트 재질 출처 검사 도구. 도구 구현과 실제 검사 완료를 구분한다.
- C02 실험은 PR #13으로 병합됐다. 기존 캐릭터·월드·카메라 수정과 Spike 이동은 이번 PR에 포함하지 않는다. PlayerRig에서는 VisualSet 연결 한 줄만 반영했다.

## 검증 상태

| 항목 | 확인한 결과 | 한계 / 남은 작업 |
|---|---|---|
| 카탈로그 참조 검사 | 120칸, null 0, 셰이더 오류 0, 비정상 메시 0 | 로컬 유료 문양 팩이 있는 환경. 미술 판정 아님 |
| 폰트 정리 후 실제 Play 재검사 | 120/120 생성·Update·소멸, 실패·오류 0, 메시 증가 0, 씬·설정 복원 | MALGUN atlas 재질 1개 증가가 남아 `COMPLETED_WITH_FINDINGS`. 출처·누수 여부 미확정 |
| C2 시전·실제 카메라 촬영 | 15자 × 3장 = 45장, 1280×720. 예정/실제 피해 26회, 오류 0, 정리 잔여 0 | 인식된 이벤트 이후의 임시 표적·채널 fixture. 물리 입력·인식기·기존 플레이어 전체 DI·적 AI 검사는 아님 |
| C2 화면 검토 | 45장 확인. 노의 판형 화염, 모의 지면 대비, 사·서의 약한 경계 식별 | 입구·캡슐·진단 획 가림도 확인. 이후 코드 보완의 실제 효과는 재촬영 필요 |
| 후속 Stills 검토 | 120종 600장 열람. 넉·넌·넘·소환수·무 등의 이전 보완 확인 | 물·무기·금 낙인/요격·화염/모래 보완 전 촬영. 최신 전체 소스의 미술 PASS 아님 |
| 수치·공간 검사 | 기존 120종 111,560 Sample 및 Circle/Path/Cone/Volley 계약 검사 통과 | 이전 실행 결과. 이번 화염 범위·후속 표현 변경 후 재검사는 미실행 |
| 후속 진단 도구 | ContrastAudit, GameplayDiagnosticCapture, ResourceAttribution 구현 | 실제 실행 미검증. ResourceAttribution의 런타임 감사 자동 연결도 미완료 |
| 영상·성능 | 인코더는 합성 시퀀스 검증, 이전 Editor CPU 기록 보존 | 120종 실제 연속 영상 미제작. 실제 30/60/120fps, 빌드/GPU 성능 및 150fps 미검증 |

현재 PR 소스의 정적 C# 컴파일은 9개 어셈블리·96개 소스에서 오류 0개, 기존 경고 14개이며, `Art/SpellVFX120/checkpoint_compile_current.json`을 따른다. 앞선 두 컴파일 기록은 별도 파일로 보존한다. 정적 컴파일은 Unity 임포트·셰이더·런타임·미술 검증을 대신하지 않는다.

검사 JSON과 이미지는 **각 검사 당시의 결과**다. 후속 Stills 메타데이터의 로드 DLL 시각은 2026-09-09 01:05:20 UTC이며, 이후 소스 변경 전체를 검증한 자료가 아니다. C2 캡처는 정상 Update 중 Camera.main으로 촬영했지만 PNG 렌더/인코딩 비용이 포함되므로 성능 측정으로 사용하지 않는다. 갤러리에는 촬영 버전과 미제작 영상을 표시한다.

## 자료와 재현

- [제작 계약](../../Specs/SPEC-SPELL-VFX120.md), [미술 방향](VISUAL_DIRECTION.md), [목·화 검토](STILLS_WOOD_FIRE.md), [토·금·수 검토](STILLS_EARTH_METAL_WATER.md), [C2 화면 검토](C2_CAPTURE_REVIEW.md).
- [검수 갤러리](../../../Art/SpellVFX120/REVIEW.html), [후속 Stills](../../../Art/SpellVFX120/Stills/), [C2 촬영](../../../Art/SpellVFX120/GameplayCapture/20260909-012702-870/), [Blender 자료](../../../Art/SpellVFX120/Blender/).
- `runtime_audit_font_cleanup.json`은 폰트 정리 후 재검사, `gameplay_audit.json`은 C2 실제 카메라 촬영 실행이다. 첫 검사 기록도 보존한다.
- Unity의 `Assets/_Project/Art/SpellVFX120/Scenes/SpellVFX120_Review.unity`에서 개별 효과를 확인한다. `Tools/SpellVFX120/unity_queue.py`로 검사·촬영 명령을 전달한다.
- 유료 `KoreanTraditionalPattern_Effect` 원본은 저장소에 포함하지 않는다. [출처 목록](ASSET_AUDIT.md)의 동일 팩이 로컬에 필요하다. 패키지가 없는 새 checkout의 완전한 시각 재현은 미검증이다.
- 임시 큐 request/response와 빌드 캐시는 제외한다. 추가 Meshy 요청·소비는 0이다.

남은 작업은 최신 코드의 공간·수명 검사와 전수 재촬영, 폰트 재질 출처 조사, 열린 C2 구도의 재검토, 120종 연속 영상, 실제 입력·전환 및 성능 검증이다.
