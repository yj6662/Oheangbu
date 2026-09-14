# 선택 A — 손 파지 사본

현재 적용값은 **DrawingBrushRoll −162°**다. 사용자가 선택한 것은 A 파지의 외형 방향이며, −162°와 −192°의 Unity 비교에서 엄지가 반대편을 감싸는 모습이 더 드러나는 −162°를 루트 작업자가 제작 판단으로 적용했다. 이 각도와 실제 게임 외형의 최종 사용자 승인은 아직 받지 않았다.

적용 증거: `Validation/Unity/RollComparison/20260914-012334-306_roll_minus162_693676/roll_comparison.json` — `APPLIED_REVIEWED_ROLL_ONLY`, 2026-09-14 01:23:34 UTC. DrawingBrushRoll만 −72°→−162°로 바꿨고 나머지 프로필 필드는 보존했다. 적용 후 프로필 SHA256은 `1dc52552ab619c7818c72bed8177cff5a263e55b527c5eb63c17275de1f51798`다.

선택한 A 이미지에 맞춰 검지 끝마디를 더 굽히고 엄지를 대립시킨 감싸 쥐는 자세를 제작했습니다. 새 손 자세와 붓 파지 기준을 실제 C02 메시에서 보정했습니다. 얼굴·몸·의상·54본 골격은 유지했습니다.

기존 파지 결과를 이름만 바꾼 것이 아닙니다. 검지 끝마디는 기존 1°에서 약 27°로, 엄지 두 마디는 0°에서 8°·5°로 바꿨으며 나머지 손가락도 다시 최적화했습니다. 오른손의 접촉 정점만 최대 0.541mm 수정했습니다.

| 검사 | 결과 | 근거 |
|---|---|---|
| 골격·몸·얼굴·의상 보존 | PASS_PRESERVATION | 54본 bind 행렬 및 오른손 밖 정점 변화 0 |
| 실제 붓대 정점 접촉 | HAND_CONTACT_NUMERIC_PASS | 최대 관통 0.000mm; 국부 수정 33정점, 최대 0.541mm |
| 삼각형 내부 접촉 | PASS_SAMPLED_STATIC_SURFACE | 0.5mm 이하 샘플 간격, 847,134점, 최대 관통 0.217mm |
| FBX 빈 장면 재임포트 | PASS_FBX_MARKER_VERTEX_CONTACT | 54본, 오른손 마커 15개, 1,817개 정점 샘플 |
| Unity 새 모델 임포트 | 적용 및 런타임 바인딩 확인 | 초기 임포트 원장의 QA_PENDING 뒤 아래 최종 검사를 실행. 새 FBX·Humanoid·30손가락·근접 메시 |
| 현재 플레이 씬 적용 | 최종 파생 모델·프로필 확인 | 메시·파지 프로필과 이동 controller/profile 조합을 최종 check/contact 의존성 원장으로 확인 |
| Unity −162°/−192° 중앙 구도 비교 | 통과 — 한 자세의 합성 표현 수치 | 각 5/5, 붓끝 화면 오차 0.0458px, 상태 복원 true. 실제 입력·표면 접촉 검사가 아님 |
| −162° 프로필 적용 | 적용 완료·외형 사용자 미승인 | 위 적용 기록의 프로필 해시 및 다른 필드 보존 true |
| 최종 −162° Unity 손 정점 접촉 | PASS_CURRENT_POSE_VERTEX_CONTACT_ONLY | 02:21:13 UTC, 실제 현재 자세의 2,582정점, 최대 관통 0mm. 연속 삼각형 표면·모든 동작 검사는 아님 |
| 최종 −162° Unity 중앙·가장자리 및 상태 검사 | PASS_SYNTHETIC_PRESENTATION_ONLY — 60/60 | 02:20:48 UTC, 실제 런타임 파지 솔버의 합성 자세 검사. 한 Editor 명령 안의 동기 평가이며 실제 연속 프레임·네이티브 작도 입력은 미검증 |

**범위 제한:** 삼각형 검사는 유한한 정지 자세 샘플이며 모든 연속 표면·모든 모션의 무관통 증명이 아닙니다. 천 물리, 좌식 전환과 지형 접촉, 실제 키 입력, 프레임별 시전 연속성은 실행된 별도 증거가 없으면 미검증입니다. 생성 참조 이미지와 실제 런타임 결과의 외형 합격은 사용자가 판단합니다.

실제 예산: 몸 59,246 tris, 근접 오른팔 6,900 tris, 기존 붓 4,680 tris. 근접 팔과 월드 몸은 동일 골격 자세를 사용하며 두 번째 Animator를 추가하지 않습니다.

새 A 전용 프로필의 DrawingBrushRoll은 현재 −162°이며, 일반 carry는 기존 값을 유지합니다. 앉을 때는 무릎 위를 향한 wrist/brush/pole 값으로 Posture01에 따라 보간합니다. 기존 프로필의 좌식 보정 기본값은 비활성이므로 영향을 주지 않습니다. 최신 좌식 캡처는 01:43:37 UTC의 합성 자세이며 최종 착석 전환·천·지형 검증을 뜻하지 않습니다.

## 최종 Unity 검사

- [합성 파지 검사](Validation/Unity/runtime_gesture_qa.json): 2026-09-14 02:20:48 UTC, 60개 통과·실패 0. 카메라·시간 배율·Raw 획·리소스 상태 복원 true, 작도·시전 이벤트 0. 30/60/120Hz 항목은 응답 수식의 검사이며 실제 프레임 속도 조건에서 입력·카메라를 재생한 검사는 아니다.
- [현재 손 정점 접촉](Validation/Unity/current_skin_contact.json): 02:21:13 UTC, `BakeMesh(useScale=true)`와 실제 붓대 1,224삼각형으로 손 2,582정점을 검사. 레이 누락 0, 최대 관통 0mm, 손가락 자세·파지 소켓 오차 0, 품질 설정 복원과 임시 메시 해제 true. 현재 한 자세의 정점 샘플이며 연속 표면 증명은 아니다.
- [check 의존성](Validation/Unity/evidence_stamp_check.json)과 [contact 의존성](Validation/Unity/evidence_stamp_contact.json)의 각 76개 경로·SHA256을 문서 갱신 시 실제 파일과 비교해 불일치 0을 확인했다. 모델 SHA256은 아래 내보낸 FBX와 같고, 프로필 SHA256은 위 −162° 적용값과 같다.
- [붓털 고정점 수정 기록](Validation/stable_bristle_pose_fix.json): 큰 월드 좌표를 거쳐 본 로컬 자세를 쓰던 과정의 실제 약 0.122mm 흔들림을 부모 로컬 변환으로 수정했다. 이 기록 자체의 `UNITY_REGRESSION_PENDING`은 수정 직후 상태다. 이후 위 60/60 검사와 접촉 검사를 별도로 실행했다. 본 길이와 원본 골격을 바꾸어 검사 문턱에 맞추지 않았다.

[최신 중앙 화면](Validation/Unity/Screenshots/20260914-022240-214_synthetic_center.png)은 02:22:40 UTC로 고정점 수정 후 촬영했다. [착석 휴대 화면](Validation/Unity/Screenshots/20260914-014337-656_synthetic_sit-carry.png)은 이전 01:43:37 UTC의 정지 자세 참고다. 실제 입력, 피격 중단, 갈무리 입력, 전체 천 물리와 모든 모션의 무관통은 미검증이다.

## Unity 회전 비교

- [−162° 비교 이미지](Validation/Unity/RollComparison/20260914-011908-005_roll_minus162_cb9955/Screenshots/synthetic_center_roll_minus162.png) — 루트 작업자가 선택한 각도. 2026-09-14 01:19:08 UTC, 임시 각도 대입 후 원래 설정 복원.
- [−192° 비교 이미지](Validation/Unity/RollComparison/20260914-011926-953_roll_minus192_446f10/Screenshots/synthetic_center_roll_minus192.png) — 비교용 각도. 2026-09-14 01:19:26 UTC, 임시 각도 대입 후 원래 설정 복원.
- [선택 A 참조](Grip_A.png) — 사용자가 선택한 생성 참조 이미지. 실제 Unity 결과나 접촉 검사 이미지가 아니다.

두 비교는 설치된 A 메시를 임시 1920×1080 카메라의 중앙 작도 자세로 평가한 실제 Unity 스크린샷이다. 실제 gameplay camera·Raw 획·시전·피해·보상은 바꾸지 않았다. 촬영 중 각도는 각 `roll_comparison.json`의 requestedDrawingBrushRoll로 기록되며, 당시 디스크 프로필 해시 자체에는 임시 각도가 반영되지 않는다. −162° 적용은 비교 이후 별도 기록이며 최종 수치 검사는 위 02:20/02:21 원장을 사용한다.

## 파일

- `Work/Player_C02_GripA.blend` — 중립 골격과 FBX용 파지 마커
- `Work/Player_C02_GripA_Assembly.blend` — 실제 붓과 연결한 편집용 사본
- `Exports/Player_C02_GripA.fbx` — 중립 모델·스킨·마커, 새 생산용 모션 없음
- `Textures/` — 기존 모델/붓의 원본 텍스처 사본; `Validation/textures.json`에 출처 해시
- `Validation/Unity/` — 새 A 런타임 검사/순차 스크린샷(확보된 파일만 존재)

내보낸 FBX SHA256: `dc7ec359628e5cdd0a6da3d853927b68f867efea269022c8a66159e15a749e2f`

Unity: `WorldMacroPlayerReRigAuthoring.ExecuteGripA("import" | "apply")`; 런타임 `ExecuteGripARuntime("check" | "contact" | "capture:center" | "capture:top-left" | "capture:bottom-right" | "capture:sit-carry")`. 작업 중 빌드와 영상은 제작하지 않았습니다.
