# AutoPlayerV1 재검사 도구

이 실행은 후보 3개 한도에 도달하여 신규 유료 요청이 중단되었다. 현재 모델을 합격으로 승격하거나 후보별 보정 횟수를 초기화하지 않는다. `.blend`에 메시·재질·검수 Action이 들어 있으며 이미지도 패킹되어 있다.

## 기존 결과 조회·재사용

프로젝트 루트 `C:/Users/yj666/Oheangbu`에서 `C:/Python314/python.exe Tools/MeshyRuns/AutoPlayerV1/run.py status`로 원장을 읽는다. `poll C03_Rig`처럼 이미 존재하는 이름을 조회하거나 `download C03_Rig`로 완료 결과를 재사용할 수 있다. 비밀키는 기존 설정에서 읽으며 출력하지 않는다. 종료 이후 새 유료 POST는 거절한다.

## Blender MCP에서 재검사

원본을 보존하고 대상 후보의 최신 `.blend`를 연 뒤 아래 스크립트를 `exec(compile(open(path, encoding='utf-8').read(), path, 'exec'))`로 읽는다. 모두 `Tools/Blender/AutoPlayerV1` 아래 있다.

- `audit_candidate.py`: `audit_candidate('Armature', ['C03_Mesh_0'], 'C03_Run', 새_JSON_경로, source_fps=30)`으로 해당 동작의 모든 정수 프레임을 검사한다. 실제 동작 출처는 후보의 기존 import/pipeline JSON에서 전달한다.
- `render_clips.py`: `start_render_jobs`에 리그·메시·실존 Action과 시점을 전달한다. 기존 결과와 다른 출력 폴더에 720p/30fps의 모든 프레임을 렌더한다. 렌더 중에는 대상 메시·웨이트를 수정하지 않는다. `progress.json`의 COMPLETE를 확인한 뒤 `encode_rendered_jobs`에 실제 FFmpeg 경로를 전달한다.
- `verify_videos.py`: 셸에서 영상 폴더를 인자로 실행한다. FFmpeg 전체 디코딩·프레임 수·PTS·720p·30fps를 검사한다. 영상 기술 검사는 캐릭터 변형 합격과 별개다.
- `candidate_pipeline.py`: 새 후보의 task/manifest/파일 SHA를 대조한 뒤 동일 골격의 원본 동작을 연결한다. 이미 준비된 후보에는 다시 실행하지 않는다. 기존 `.blend`에서 검사만 재개한다.

검사 도구 버그와 후보 보정을 구분한 기록은 `Art/PlayerPhase1/AutoPlayerV1/tool_corrections.json`에 있다. 이 경로의 자동화는 게임의 PlayerRig·C2를 바꾸지 않는다.

## 보고서 갱신

`finalize_run.py`는 명시적인 상태·이유를 받아 로컬 보고서만 기록한다. 이후 `Tools/MeshyRuns/AutoPlayerV1/sync_manifest.py`, `build_review.py` 순서로 실행한다. `FINAL_REPORT.md`와 `final_summary.json`이 최신 판정이며, 중간 audit는 이력이다. 디자인 시안의 채택이나 다음 유료 실행은 이 도구가 자동으로 결정하지 않는다.
