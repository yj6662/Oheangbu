# 9월 15일 플레이테스트 시각 통합 — TEST

작성 UTC: 2026-09-13T15:56:07.659057+00:00

## 이번 적용

- 별도 `W_WorldMacro_Playtest` 씬에서 폐광→주막 산길, 숲과 상경 가도의 길 표면·주변 식생을 보완했다. 전역 지형 재생성은 하지 않았다.
- 황경 남문·성곽·한옥·정자·생활 소품을 보유 에셋으로 구성했다. 건물 48개의 흰 대체 외형을 교체했다. 궁궐 원본의 전각·테라스는 보존했다.
- C02 A2/B2/C3 기존 모델을 숄더뷰 플레이어로 연결했다. 높이 1.75m, 충돌 캡슐 지름 0.56m, 52,784삼각형. 실제 이동 속도로 대기·보행·달리기를 구동하며 Root Motion은 껐다.
- 곰·놈·몸·솜·옴을 Playtest 전용 SpellBook에서 실제 시전 승인·먹 소비·고정 위치 표시·자연 만료에 연결했다. 소환 이동·공격·피해·방어·어그로는 추가하지 않았다. 4.6초는 TEST 표현 수명이다.
- 예약 콘텐츠 표시를 외부 플레이에서 숨기고 실제 조사·대화 대상과 콘텐츠 ID는 유지했다.

## 검사 결과

| 항목 | 결과 | 근거와 범위 |
|---|---|---|
| 원본/공용 파일 보존 | 통과 | 3,995개 SHA-256 비교, 변경 0개. `preservation.json` |
| 숲·상경 연결로 | 통과 | 3,404개 정적 캡슐 표본, 막힘 0, 지지 없음 0. 자동 보행이 아니다. |
| 궁궐 접근 | 통과 | 109개 표본, 단차 위반 0, 캡슐 막힘 0. `palace_access.json` |
| 플레이어 구조 | INSTALLED_TECHNICAL_PASS | Humanoid·프로필 로딩·원본 텍스처·신장·렌더러 소유권. `../PlayerAppearance/validation.json` |
| 플레이어 정지 런타임 진단 | 통과 | Play 모드 13개 검사, 임시 검토 카메라의 전/후 숄더 구도. 대기 상태만 관찰했고 이동 중 도포/발 미끄러짐과 손 파지는 미검증. |
| 소환수 5종 | PASS | 각 1회 승인·먹 소비, 적 HP/방어 상태 불변, 종료 등록 객체 0개. 인식 완료 이벤트를 주입한 실제 서비스 검사다. 직접 손글씨 인식 검사는 아니다. |
| 한옥 LOD | HOUSE_LODS_TECHNICAL_PASS | 40채. LOD0 86,400, LOD1 23,443, LOD2 7,683 tris/채. 공유 메시, 원본 LOD0 보존. |
| 렌더 자산 누락 | 통과 | 메시 0, 재질/셰이더 0개 누락. |
| 빌드 | BUILD_CANDIDATE_CREATED_NOT_QA_COMPLETE | Succeeded, 오류 0, 경고 581 |
| 직접 보행·차량 완주·최종 미술 | 미검증 | 사용자가 판단할 항목. 영상이나 자동 완주는 만들지 않았다. |

## 성능과 보존

주막 Editor Play 1920×1080, 120표본: 프레임 중앙값 12.443ms, P95 14.528ms, CPU 중앙값 12.566ms, GPU 중앙값 5.154ms. 이 값은 마지막 한옥 LOD 적용 전이며, 최종 빌드 전체 구간 120fps 달성을 증명하지 않는다. 정적 시작점 빌드 측정은 아래 별도로 기록한다.

새 배치 438개, LODGroup 438개. `allLodTriangles`는 동시에 그려지는 삼각형 수가 아니라 모든 LOD 자산을 더한 수치다. 캐시의 소스 해시 불일치 검사와 재질의 GUID+local file ID 식별을 추가했다. 일반 닫힌 건물은 뒷면을 그리지 않으며, 지상 시점에서 안쪽이 보이는 남문 기와 시트만 양면 예외로 두었다.

씬 소유 식생 제외 데이터를 사용해 실제 길·건물·출입 공간을 비웠다. 원본 팔레트, C2, 공용 PlayerRig, 원래 SpellBook, 공급자 모델/텍스처를 보존했다. 신규 Meshy 요청과 추가 유료 생성은 0건이다.

## 범위와 남은 항목

- 첫 폐광→금표 주막의 조사·전투·휴식·저장 체계는 유지한다. 이후 황경까지는 외부 경관과 연결로 검토 범위이며, 본편 퀘스트/도시 내부 콘텐츠 완료가 아니다.
- C02에는 손가락 본이 없다. 이번 결과는 자연스러운 붓 파지·전신 작도 IK·천 물리 완성을 주장하지 않는다. 측면 이동은 기존 전진 모션을 재사용한다.
- 소환수는 기존 승인 외형의 정적 등장·소멸이다. 적/NPC는 기존 임시 외형이다.
- 지형의 넓은 빈 공간, 마을 밀도, 산의 근거리 표현은 스크린샷으로 최종 판단할 사항이다. 건물 외관 교체를 최종 미술 승인으로 취급하지 않는다.
- 정적 경로 표본 통과는 사용자의 전 구간 완주나 차량 통과를 대신하지 않는다.
- 실행물은 BUILD CANDIDATE이며 다음 사용자 플레이 결과를 반영할 기준 빌드다.

## 근거 파일

`validation.json`, `asset_manifest.json`, `preservation.json`, `material_optimization.json`, `palace_access.json`, `HouseLOD/house_lod_report.json`, `../PlayerAppearance/RuntimeReview/runtime_review.json`, `../SummonCast/runtime_review.json`. 계획은 `Docs/Plans/PLAYTEST-VISUAL-CORRIDOR-20260915.md`.

실행 파일: `C:\Users\yj666\Oheangbu\Builds\Playtest-20260915\20260913T154956Z\Oheangbu_Playtest.exe`

배포 ZIP: `C:\Users\yj666\Oheangbu\Builds\Playtest-20260915\Oheangbu_Playtest_20260913T154956Z.zip`

압축 CRC 검사: True

숨김 창 실행: 렌더링 여부가 확인되지 않아 아래 update-loop 속도와 CPU/GPU 0값을 게임 FPS/렌더 비용으로 사용하지 않는다.

### 실행 파일 검사 — smoke_restart.json

상태: `PASS_RESTART_SAVE_RENDER_PERFORMANCE_UNVERIFIED`

```json
{
  "goal": "Diagnostic stationary standalone sample: uncapped, vSync off, 30 warmup frames, then 180 measured frames; user goal 120 FPS. This does not certify the full route.",
  "width": 1920,
  "height": 1080,
  "fullScreenMode": "Windowed",
  "warmupFrames": 30,
  "measuredFrames": 180,
  "measuredVSyncCount": 0,
  "measuredTargetFrameRate": -1,
  "measuredRenderInterval": 1,
  "elapsedSeconds": 0.26472609999999985,
  "actualFps": 679.9480670776327,
  "meanFrameMs": 1.4791739085922018,
  "p95FrameMs": 5.465795285999775,
  "frameTimingFeatureEnabled": false,
  "cpuTimingSamples": 0,
  "gpuTimingSamples": 0,
  "meanCpuFrameMs": 0.0,
  "p95CpuFrameMs": 0.0,
  "meanGpuFrameMs": 0.0,
  "p95GpuFrameMs": 0.0,
  "meanCpuMainThreadMs": 0.0,
  "meanCpuRenderThreadMs": 0.0,
  "goalMet": false,
  "renderedSampleValid": false,
  "renderingStatus": "UNVERIFIED_RENDERING: hidden/minimized window; update-loop timing is not rendered FPS.",
  "managedMemoryBytes": 33050624,
  "totalAllocatedMemoryBytes": 356533349,
  "totalReservedMemoryBytes": 451674112,
  "peakAllocatedMemoryBytes": 356533349,
  "settingsRestored": true
}
```

숨김 창 실행: 렌더링 여부가 확인되지 않아 아래 update-loop 속도와 CPU/GPU 0값을 게임 FPS/렌더 비용으로 사용하지 않는다.

### 실행 파일 검사 — smoke_start.json

상태: `PASS_STARTUP_SAVE_RENDER_PERFORMANCE_UNVERIFIED`

```json
{
  "goal": "Diagnostic stationary standalone sample: uncapped, vSync off, 30 warmup frames, then 180 measured frames; user goal 120 FPS. This does not certify the full route.",
  "width": 1920,
  "height": 1080,
  "fullScreenMode": "Windowed",
  "warmupFrames": 30,
  "measuredFrames": 180,
  "measuredVSyncCount": 0,
  "measuredTargetFrameRate": -1,
  "measuredRenderInterval": 1,
  "elapsedSeconds": 0.2680693999999999,
  "actualFps": 671.467910921575,
  "meanFrameMs": 1.4950577932823863,
  "p95FrameMs": 5.5112033151090145,
  "frameTimingFeatureEnabled": false,
  "cpuTimingSamples": 0,
  "gpuTimingSamples": 0,
  "meanCpuFrameMs": 0.0,
  "p95CpuFrameMs": 0.0,
  "meanGpuFrameMs": 0.0,
  "p95GpuFrameMs": 0.0,
  "meanCpuMainThreadMs": 0.0,
  "meanCpuRenderThreadMs": 0.0,
  "goalMet": false,
  "renderedSampleValid": false,
  "renderingStatus": "UNVERIFIED_RENDERING: hidden/minimized window; update-loop timing is not rendered FPS.",
  "managedMemoryBytes": 32993280,
  "totalAllocatedMemoryBytes": 356777525,
  "totalReservedMemoryBytes": 451674112,
  "peakAllocatedMemoryBytes": 356816549,
  "settingsRestored": true
}
```
