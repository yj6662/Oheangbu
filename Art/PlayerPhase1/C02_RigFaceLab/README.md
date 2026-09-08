# C02 리깅·얼굴 실험 체크포인트

이 폴더는 A2 웨이트 + B2 동작 + C3 눈꺼풀의 **코드·검증 기록 체크포인트**다. 전체 캐릭터 품질은 UNVERIFIED이며 C2·공용 PlayerRig에 적용하지 않았다. 결과와 잔여 문제는 [FINAL_REPORT.md](FINAL_REPORT.md), 최신 수치 판정은 [final_summary.json](final_summary.json)을 따른다. 추가 유료 요청은 0회·0크레딧이다.

## 공개 Git에 포함한 것

- 실험 Spec, 결정 #188, 단계별 보고서와 수치 감사 JSON.
- `Tools/Blender/C02_RigFaceLab` 및 공통 AutoPlayerV1 검사 도구 3개.
- `Tools/Unity/C02RigFaceLab`, 별도 Unity 코드·씬·프리팹·컨트롤러·프로필·재질 및 `.meta`.
- 원본 C02의 작업 ID/해시와 필요한 기존 Repair2 감사 입력. 다운로드 서명 URL은 제거했다.

## 로컬에서 별도 보존하는 것

`SPEC-SPELL-FX-ASSETS §4.7`의 생성물 비커밋 정책에 따라 Blender·FBX·텍스처·생성 동작 `.anim`·영상·렌더 프레임은 공개 저장소에 올리지 않는다. **이 커밋만으로 모델과 영상을 백업한 것은 아니다.** 실제 파일은 원래 작업 폴더 `C:/Users/yj666/Oheangbu/`에 보존했다.

최종본과 Unity에 필요한 로컬 파일 목록·SHA-256은 [local_payload_manifest.json](local_payload_manifest.json)에 있다. 원본 Meshy task ID로는 로컬에서 수정한 A2/B2/C3 결과를 재생성할 수 없다. 해당 `.blend`/FBX는 별도의 비공개 파일 보존이 필요하다. 다른 컴퓨터에서는 이 파일들을 같은 상대 경로에 복원하고 체크인된 `.meta`를 유지해야 한다. 복원 전 검수 씬의 모델·텍스처·모션 참조 누락은 예상되는 상태다.

## 검수 재개

1. 위 로컬 패키지와 원본 `Art/PlayerPhase1/AutoPlayerV1/Candidates/C02/Candidate_C02_Repaired.blend`를 복원한다. 원본 SHA는 `preservation_before.json`에 있다. C02 `Baseline.blend`도 같은 바이트다.
2. Blender 5.0.1, Unity 6000.3.9f1/URP 17.3, Python+NumPy/Pillow 및 FFmpeg가 사용됐다. 실험 스크립트의 `ROOT`와 FFmpeg 기본 경로는 원래 PC 경로이므로 다른 환경에서는 맞춰야 한다. Blender 스크립트는 지정 입력과 별도 결과 폴더를 사용하는 진단/제작 도구다.
3. Blender 최선본은 `Final/Integrated_B2_C3.blend`; Unity 씬은 `Assets/_Project/Art/C02_RigFaceLab/Scenes/Integrated_B2_C3_Review.unity`다. 임포트는 자체 Humanoid, 압축/OptimizeGameObjects OFF, 표정은 별도 컨트롤러로 제어한다.
4. `python Tools/Blender/C02_RigFaceLab/build_lab_review.py`로 로컬 `REVIEW.html`을 재생성한다. 전체 미디어 검수와 `verify_handoff.py`는 복원한 실제 파일을 필요로 한다. 저장된 PASS는 원래 PC에서의 시험 기록이며 새 checkout의 자동 합격이 아니다.
5. Unity 요청에는 `unity_queue.py`를 사용할 수 있다. `unity_call.py`의 Temp MCP bridge는 로컬 인증 설정에 의존하며 비공개·비이식 대상이다. bridge/인증정보를 Git에 추가하지 않는다.

## 확인된 결과와 남은 제한

52,784삼각형, 24본, 3렌더러, 2BlendShape. 31조건 FBX 왕복 검사와 Unity 실제 444프레임 전신/180프레임 얼굴 캡처를 수행했다. B2 Play Idle 바닥 간격은 약 4mm로 개선됐다.

전체 의복·동작 자연스러움은 미검증이다. Run/공격 접지 결함, 눈꺼풀 중간 접촉·거친 패치가 남는다. 시선 후보는 실패했고 턱·손가락·보조 물리는 구현하지 않았다. Unity는 722개 중 297개의 다섯 번째 웨이트를 보존한다. 정본 교체·게임플레이·성능 합격을 뜻하지 않는다.
