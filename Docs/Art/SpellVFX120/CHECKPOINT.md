# 술식 VFX120 — 2026-09-09 후속 체크포인트

상태: **TEST / 작업 중간 보존**. 현재 구현과 검사 당시 증거를 보존한다. 최종 미술 승인이나 모든 술식의 게임 규칙 구현 완료를 뜻하지 않는다.

## 포함 범위

- CSV 120칸의 프로필·프리팹·공유 메시/재질. 100개 배정과 20개 의도적 공백으로 구성하며 공백은 Reserve 후보 표현으로 유지한다.
- 기존 15자용 VisualSet과 신규 VFX 어댑터 분기. 공용 `PlayerRig`의 `_visualSet`을 신규 자산에 연결한다. 로컬 C2에서 인식된 이벤트 이후 실제 시전 경로를 검사했다.
- 독립 검수 씬, 2차 600장·3차 600장·목 계열 부분 재촬영 40장, Blender 소환수 원본/FBX, 검수 갤러리와 영상 인코딩 도구.
- 곡·곤·공·국의 개별 동작, 강화 효과·소환수 형상과 위치 보완, 잘못된 파티클 곡선 모드 및 검수 UI 폰트 정리 수정.
- 기존 C02 실험은 PR #13에 병합되어 있다. 이전 캐릭터·월드·카메라 수정과 기존 Spike 이동은 이 PR 범위에 포함하지 않는다. PlayerRig에서는 VisualSet 연결 한 줄만 반영했다.

## 검증 상태

| 항목 | 확인한 결과 | 한계 / 남은 작업 |
|---|---|---|
| 카탈로그 빌드 | 120칸, null 0, 셰이더 오류 0, 비정상 메시 0 | 로컬 Unity 및 유료 문양 팩이 있는 환경 |
| 실제 Play 수명 재검사 | 120/120 생성·소멸 통과, 실패·오류 0, 메시 증가 0 | 폰트 atlas 재질 1개 증가. 폰트 복원·정리 수정 후 재실행은 미검증 |
| C2 시전 검사 | 15자 인정 이벤트→실제 어댑터 생성 통과, 예정/실제 피해 26회 일치, 구형 효과 중복 0, 추적한 정리 잔여 0 | 임시 표적·채널을 사용하는 런타임 fixture. 물리 입력·인식기·기존 플레이어 전체 DI·적 AI·피격 패링은 미검증 |
| 시전 시계 | 전달 오차 0, 최대 관측 착탄 지연 약 6.18ms | 해당 실행에서 관측한 값이며 고정 프레임레이트 보증이 아님 |
| 수치·공간 검사 | 120종 111,560 Sample 및 Circle/Path/Cone/Volley 계약 통과 | 검사 당시 결과. 실제 30/60/120fps 플레이나 최신 변경 전체 재검사 아님 |
| 3차 시각 검토 | 120종 600장 확인. 곡·곤·공·국은 부분 재촬영 후 VFX TEST 수용 | 최신 넉·넌·넘·소환수·무 등의 수정은 재촬영 전. 최종 미술 승인 아님 |
| 게임 카메라 캡처 | `GameplayCapture` 옵션 구현 | 실제 출력 미실행 |
| 영상 인코더 | 합성 시퀀스로 정상·캐시·손상·누락 조건 검사 | 120종 실제 최종 영상은 미제작 |
| 성능 | 로컬 Editor에서 단일 및 동시 16개 CPU 비용 기록 | Editor/검수 도구 비용이 섞인 측정. 150fps 또는 성능 PASS를 뜻하지 않음 |

체크포인트 소스의 정적 컴파일 결과는 `Art/SpellVFX120/checkpoint_compile_followup.json`에 별도로 기록한다. 첫 체크포인트의 컴파일 기록은 `checkpoint_compile.json`에 보존한다.

검사 JSON과 이미지는 **각 검사 당시의 결과**다. 로컬 C2의 런타임 결과를 main 기반 이 체크포인트 전체의 Play PASS로 일반화하지 않는다. 3차 캡처 일부는 이전 DLL로 생성되었으며 곡·곤·공·국은 갱신 후 다시 촬영했다. 최신 캡처 도구에는 소스보다 DLL이 오래된 경우 실행을 거절하는 검사를 추가했다.

## 자료와 재현

- [제작 계약](../../Specs/SPEC-SPELL-VFX120.md), [미술 방향](VISUAL_DIRECTION.md), [3차 목·화 검토](THIRD_PASS_WOOD_FIRE.md).
- [검수 갤러리](../../../Art/SpellVFX120/REVIEW.html), [3차 렌더](../../../Art/SpellVFX120/ThirdPass/), [부분 재촬영](../../../Art/SpellVFX120/FourthPassSubset/), [Blender 제작 자료](../../../Art/SpellVFX120/Blender/).
- `gameplay_audit.json`·`gameplay_connection_summary.json`은 C2 fixture 결과, `runtime_audit.json`은 곡선 모드 수정 후 Play 결과다. 첫 오류 기록도 별도 파일로 보존한다.
- Unity `Assets/_Project/Art/SpellVFX120/Scenes/SpellVFX120_Review.unity`에서 개별 효과를 확인한다. `Tools/SpellVFX120/unity_queue.py`는 Build / Audit / Review / Capture / RuntimeAudit / GameplayAudit 등의 명령을 전달한다.
- 유료 `KoreanTraditionalPattern_Effect` 텍스처 원본은 저장소에 포함하지 않는다. [출처 목록](ASSET_AUDIT.md)의 동일 팩을 로컬로 임포트해야 문양 참조가 복원된다. 패키지가 없는 새 checkout의 완전한 시각 재현은 미검증이다.
- 임시 큐 request/response, 빌드 캐시 및 라이선스 원본은 제외한다. 추가 Meshy 요청·소비는 **0**이다.

남은 작업: 최신 소스의 전수 렌더·미술 보완, 폰트 정리 후 실제 Play 재검사, 게임 카메라 촬영과 120종 최종 영상, 실전 입력·인식·전환 및 성능 검증.
