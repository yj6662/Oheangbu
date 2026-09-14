# C02 손·상체 리깅 검토

갱신 UTC: 2026-09-13T22:01:31+00:00

현재 판정: **HAND_RIG_PASS — hands_and_upper_body 한정**. 전체 캐릭터·의복·보행 완성 판정은 아니다.
표면 검사: **PASS_SAMPLED_STATIC_SURFACE**. 삼각형 내부 870,801개 표본 / 간격 0.500mm / 최대 관통 0.192mm. 현재 FBX 해시 일치=True. 유한 표본이며 모든 연속 표면·미래 자세의 무관통 증명이 아님.
gate 원장에는 `HAND_RIG_PASS`가 기록되어 있어도 현재 삼각형 내부 검사 FAIL을 덮지 않는다.
손·상체 런타임 보정은 설치되어 있으며 실제 플레이 및 사용자 미술 검토는 남아 있다. 중간 빌드·영상은 제작하지 않는다. 작업 완료 후 최종 빌드를 한 번 구성한다.

합성 표현 진단 60개 통과 / 0개 실패. 1080p 9개 목표점 붓끝 투영 오차 최대 0.336px. 실제 필기·시전 입력이나 실측 프레임률 검사가 아니다.

120개 달리기 진단 자세의 발바닥 최소 높이: 좌 -19.46 → 2.60mm, 우 -34.51 → 2.61mm. 평면 y=0 기준이며 전체 메시의 최저점은 여전히 -32.16mm다. 도포 등 잔여 관통 부위의 식별·시각 검수와 실제 경사·계단은 미완료다.

## 변경 범위

- 기존 C02의 얼굴·체형·의상 디자인과 텍스처를 유지한 별도 사본이다. 손 부위만 세분화하고 양손 손가락 30개 본을 추가했다.
- 중립 자세 높이를 기준으로 1.75m를 다시 맞췄다. 평상시 오른팔을 내려 붓을 들고, 작도 시 손목·팔꿈치·어깨·가슴을 연결하며 갈무리 효과의 시작점을 실제 붓끝에 연결했다.
- 기존 대기·걷기·달리기를 새 Humanoid에 재사용했다. 발바닥이 평면 아래로 들어가는 구간에만 길이를 유지한 다리 보정을 추가했으며, 새 방향별 이동·회피·피격 클립을 제작한 것은 아니다.
- 기존 본의 꼬리 길이가 관절 간격보다 100배 큰 문제를 기존 로컬 축 방향으로 보정했다. 관절 머리 위치와 축·바인드 기준을 유지하는 방식이다. 캐릭터 전체를 100배 축소한 변경이 아니다.
- 붓대 접촉을 위해 휴식 메시의 손 정점 48개를 최대 2.361mm 보정했다. 붓 자루 스케일은 [1, 1, 1]다.
- 정점당 최대 5개 웨이트를 유지한다. 런타임 Unlimited 스키닝은 활성 동안 lease로 관리한다. 전역 품질 설정을 영구적으로 바꾸는 합격 근거는 아니다.
- HAND_RIG_PASS는 손·상체 정적 변형 및 임포트의 한정 판정이다. 정점 접촉 검사만으로 합격시키지 않으며 삼각형 내부 표면 검사도 현재 FBX에 대해 통과해야 한다. 전체 보행·천 물리·카메라 전환·실제 손글씨 입력의 합격을 포함하지 않는다.

## 실제 메시 통계

| 대상 | 삼각형 |
|---|---:|
| 전신·눈꺼풀 | 59,246 |
| 근접 팔 | 6,900 |
| 붓 | 4,680 |
| 자산 단순 합계 | 70,826 |

본 54개, 손가락 본 30개. 최대 웨이트 5개. 자산 합계는 실측 렌더 비용이 아니다.

## 접촉 검사

Thumb 0.300mm / Index 0.900mm / Middle 0.955mm / Ring 0.300mm / Pinky 0.300mm

정점 표본 최대 관통 0.000mm. 최대 휴식 메시 보정 2.361mm, 정점 48개. 정점 표본 검사이며 단독 합격 기준이 아니다. 삼각형 내부 검사: 삼각형 내부 870,801개 표본 / 간격 0.500mm / 최대 관통 0.192mm. 현재 FBX 해시 일치=True. 유한 표본이며 모든 연속 표면·미래 자세의 무관통 증명이 아님.

## 저장된 검사

| 항목 | 원장 판정 | 범위·한계 |
|---|---|---|
| [원본 보존](Validation/source_inspection.json) | 통과 | 현재 원본 .blend SHA-256을 source_inspection 기록과 비교. 얼굴·체형·의상 원형과 텍스처는 기존 C02 사용. |
| [정적 메시·웨이트](Validation/static_checks.json) | NUMERIC_PASS | 전체 59,246 tris, 미할당·음수·비정상 웨이트는 원장 참조. 최대 5개, 합계 최대 오차 5.364418e-07. |
| [실제 붓대·손 피부 접촉](Validation/hand_contact.json) | HAND_CONTACT_NUMERIC_PASS | 변형 후 손 정점과 실제 붓대 표면의 수치 검사. 연속 삼각형 충돌 또는 모든 동작의 무관통 증명은 아님. |
| [손 삼각형 내부 표면 검사](Validation/hand_surface_sampling.json) | PASS_SAMPLED_STATIC_SURFACE | 삼각형 내부 870,801개 표본 / 간격 0.500mm / 최대 관통 0.192mm. 현재 FBX 해시 일치=True. 유한 표본이며 모든 연속 표면·미래 자세의 무관통 증명이 아님. |
| [FBX 빈 장면 재임포트](Validation/fbx_roundtrip.json) | PASS | 골격 54개, 손가락 30개. Armature 1개, 진단 포즈 표식 포함. 런타임 동작은 FBX 애니메이션이 아님. |
| [Unity Humanoid 임포트](Unity/import_inspection.json) | PASS_REST_IMPORT_ONLY | Avatar valid=True, human=True, 손가락 매핑=30. 휴식 자세 임포트 검사. |
| [손·상체 한정 종합 판정](Exports/rig_gate.json) | HAND_RIG_PASS | gate 원장=HAND_RIG_PASS. 정점 접촉 PASS만으로 합격시키지 않는다. 현재 FBX의 내부 표면 검사와 원장 해시가 일치해야 HAND_RIG_PASS를 표시한다. 하체·천 물리는 별도 범위. |
| [Playtest 전용 설치](Unity/installation.json) | INSTALLED_HAND_RIG_DERIVATIVE | 별도 모델·프리팹·프로필. Root Motion OFF, 근접 팔에 별도 Animator 없음. |
| [회전 계산 검사](Unity/gesture_math.json) | PASS_PURE_MATH_ONLY | 축 147개, 손가락 복귀 100회. 순수 수학 검사이며 실제 동작 자연스러움의 증거는 아님. |
| [현재 런타임 스냅샷](Unity/runtime_snapshot.json) | RUNTIME_SNAPSHOT | UTC 2026-09-13T22:01:01.5314292Z. skinWeights=Unlimited, active lease=1. 한 상태 표본을 전체 기능 PASS로 취급하지 않음. |
| [런타임 리그 합성 진단](Unity/runtime_gesture_qa.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 60 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. |
| [현재 Unity 피부 접촉](Unity/current_skin_contact.json) | PASS_CURRENT_POSE_VERTEX_CONTACT_ONLY | 현재 런타임 스키닝 표면과 붓 접촉 검사. 정적 Blender 접촉과 구분. |
| [격리 보행 검사](Unity/isolated_gait.json) | MEASURED_ISOLATED_GAIT_NOT_ART_APPROVAL | Isolated preview scene, existing imported Idle/Walk/Run clips sampled directly with a manual AnimationClipPlayable. 120 poses per clip, no live-player movement, gameplay input, capture, clip editing or build. No gesture override in this gait-only probe. |
| [isolated_gait_foot_clearance](Unity/isolated_gait_foot_clearance.json) | MEASURED_ISOLATED_GAIT_NOT_ART_APPROVAL | Isolated preview scene, existing imported Idle/Walk/Run clips sampled directly with a manual AnimationClipPlayable. 120 poses per clip, no live-player movement, gameplay input, capture, clip editing or build. No gesture override in this gait-only probe. Optional foot clearance is sampled against an explicit flat y=0 diagnostic plane, without gameplay Physics queries. Whole-mesh minima may include the robe; sole minima are reported separately. |
| [runtime_capture_bottom-right](Unity/runtime_capture_bottom-right.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 5 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored. |
| [runtime_capture_center](Unity/runtime_capture_center.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 5 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored. |
| [runtime_capture_top-left](Unity/runtime_capture_top-left.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 5 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored. |
| [runtime_capture_world-drawing](Unity/runtime_capture_world-drawing.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 5 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. External world-body still: near renderers hidden and world shadow-only renderers shown solely for capture, then restored. Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored. |
| [runtime_capture_world-harvest](Unity/runtime_capture_world-harvest.json) | PASS_SYNTHETIC_PRESENTATION_ONLY | PASS 4 / FAIL 0 / UNVERIFIED 0. Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved. External world-body still: near renderers hidden and world shadow-only renderers shown solely for capture, then restored. Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored. |
| [skinning_lease](Unity/skinning_lease.json) | PASS_SKINNING_LEASE_ONLY | Temporary runtime lease objects only. Refcount, repeated binding, disable/re-enable, destroy and prior-setting restoration; not a deformed mesh comparison. |

## 이미지

- 손 전후: before_Right_palm/side 및 grip_palm_final/grip_oblique_final. 포즈·카메라가 달라 픽셀 정합 비교가 아니다.
- 상체: neutral_front, arms_down, arms_raised, elbow_flex. Blender 진단용 정지 자세다.
- Unity: 시점별 최신 저장 이미지 8개. 촬영 JSON에 당시 시간·시점·크기·자원 정리 범위가 기록된다.
- 현재 오프라인 [검토 페이지](REVIEW.html)에서 원본 이미지를 열 수 있다. 사용자 비주얼 승인은 미완료다.

## 미검증·범위 밖

- 120개 달리기 진단 자세의 발바닥 최소 높이: 좌 -19.46 → 2.60mm, 우 -34.51 → 2.61mm. 평면 y=0 기준이며 전체 메시의 최저점은 여전히 -32.16mm다. 도포 등 잔여 관통 부위의 식별·시각 검수와 실제 경사·계단은 미완료다.
- 원형에서 이어진 손 피부와 소매의 낮은 해상도·거친 표면은 남아 있다. 손가락 본과 파지 수치 통과는 근접 외형의 최종 완성을 뜻하지 않는다.
- 걷기·달리기·회피·좌우 이동 중 발 미끄러짐, 전체 도포/소매 관통과 자연스러움: 이 손·상체 한정 판정으로는 미검증.
- 모든 미래 자세의 피부–붓 연속 삼각형 충돌: 미검증. 정점 표본 및 0.5mm 간격 삼각형 내부 표본은 해당 정적 자세의 유한 샘플이다.
- 천 물리의 중력 정착·복원·런타임 비용과 전체 하체 리그: 이번 HAND_RIG_PASS 범위 밖.
- Unity 상태 전환·작도·갈무리·화면 끝 추종은 해당 런타임 원장이 있을 때에만 그 범위로 읽는다. 정지 이미지 하나로 실제 플레이 완료를 판단하지 않는다.
- 최종 얼굴·손·파지 인상과 움직임의 자연스러움은 사용자 검토 대기. 전체 작업 완료나 미술 승인으로 표기하지 않는다.

## 결과 파일

- [몸·손·붓 조립 Blender](Work/Player_C02_Assembly.blend)
- [수정 Blender 원본](Work/Player_C02_ReRig.blend)
- [최종 내보내기 FBX](Exports/Player_C02_ReRig.fbx)
- [FBX 빈 장면 재임포트 Blender](Validation/Player_C02_Reimport_Check.blend)
- [보존한 C02 원본 Blender](../C02_RigFaceLab/Final/Integrated_B2_C3.blend)
- [Unity 프리팹](../../../Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/PF_Player_C02_ReRig.prefab)
- [Unity 손·몸 동작 프로필](../../../Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/PlayerGesture_C02_ReRig.asset)
- [C02 원본 베이스 컬러 0](../../../Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Textures/Integrated_B2_C3_M0_BaseColor.png)
- [C02 원본 베이스 컬러 1](../../../Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Textures/Integrated_B2_C3_M1_BaseColor.png)

## 원본·내보내기 식별

- 원본 .blend SHA-256: `d3dd7dcb3fe1776025146e76f0391dacf8bd2e55252c80ecad93c1c8c87572ac` / 현재 파일 일치: True
- 내보낸 FBX SHA-256: `db1599d97b36051b19458a4704673ce602aecf345a26158d404cf5f6650bed1b` / gate 기록 일치: True

## 갱신

`python Tools/Unity/update_rerig_review.py`

이 스크립트는 저장된 파일만 읽는다. 공급자 호출·Unity/Blender 실행·스크린샷·빌드·영상·자동 보행은 수행하지 않는다.
