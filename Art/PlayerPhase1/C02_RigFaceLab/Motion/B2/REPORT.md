# B2 — Humanoid에서 소실되는 Idle 확대 본 스케일 수정

**최종 실험 후보로 B2를 선택했다. B 시도 2/3이며 전체 자연스러움은 UNVERIFIED다. 최종 Unity 연속 캡처와 전체 디코딩 검증을 완료했다.**


실제 Unity Play Mode에서 원속도 전환·Run 6주기 시퀀스 **444프레임/30fps/14.8초**, 눈꺼풀 근접 **180프레임/30fps**를 기록하고 전체 디코딩을 통과했다. 두 캡처의 표정 덮어쓰기0, 고정 root/바닥0에서 Idle 첫 두 표본 minY=4.074/4.094mm다. [최종 전신 영상](../../Unity/B2_Final_Sequence_Full/B2_Final_Sequence_Full.mp4) · [눈꺼풀 근접 영상](../../Unity/B2_Final_Blink_Close/B2_Final_Blink_Close.mp4) · [Unity 최종 보고](../../Unity/UNITY_REPORT.md). 기술 캡처 완료를 전체 자연스러움·접촉·캐릭터 품질 승인으로 취급하지 않는다.

조기 Unity Humanoid Idle0에서 고정 루트·지면을 유지한 실제 minY=**0.004073918m**, maxY=**1.626909256m**를 확인했다. 이전 스케일 소실에 따른 부유 원인이 이 한 자세에서 수정됐다는 범위의 증거다. [Unity 측정 원장](../../Unity/B2_BodyProbe_camera_calibration.json)과 [Humanoid 임포트 기록](../../Unity/B2_BodyProbe_import_audit.json)을 따른다. 이 수치로 전체 품질이나 모든 동작의 접지를 합격 처리하지 않는다.

원본과 B1 Idle은 `Hips.scale≈1.1764704`로 몸을 확대했다. Unity Humanoid에서 이 본 스케일이 재현되지 않는 원인이 확인되어, 새 `LAB_B2_Idle`만 단위 스케일로 제작했다. 원본·B1 Idle은 비교용으로 보존했다.

Armature 오브젝트와 메시 transform, 정지 골격·기하·UV·웨이트, 지면 z=0을 고정했다. 확대를 제거한 실제 신발 밑면에서 골반 애니메이션 높이 보정 **−0.154563m**를 산출하고, 각 프레임의 다리 회전 IK로 양발 접지를 다시 계산했다. 임의 플레이어 루트 이동이나 지면 이동을 사용하지 않았다. 본 길이도 늘리지 않았다.

| 새 Idle 프레임 | Hips scale | 신발/전체 메시 최소높이 | 전체 메시 최대높이 |
|---|---:|---:|---:|
| 1 | 약1.0 | 약0.00500m | 1.62691m |
| 61 | 약1.0 | 약0.00500m | 1.61770m |
| 121 | 약1.0 | 약0.00500m | 1.62691m |

121개 정수 프레임의 실제 신발·메시 높이를 [B2.json](B2.json)에 기록했다. 신발 표본은 rest 높이0.20m 아래의 같은 쪽 Foot/ToeBase/Leg 웨이트 합계>0.5인 모든 정점이며, 이전의 제한된 밑면 프로브에서 빠진 신발 정점을 포함한다. 상체 동작·관찰 회전은 유지했다.

- [Motion_B2.blend](Motion_B2.blend) · [몸/동작 FBX](C02_B2_Body_Motions.fbx)
- 새 Idle=`LAB_B2_Idle`, 새 331프레임 전환 시퀀스=`LAB_B2_Sequence`
- Walk/Run/Attack=`LAB_B1_Walk/Run/Attack` 그대로 유지
- [첫 프레임](Previews/B2_Idle_001.png) · [중간 프레임](Previews/B2_Idle_061.png): 1280×720 실제 Blender 렌더

새 Idle의 전체 시퀀스 영상을 반복 제작하지 않았다. B1의 6주기 영상은 변경하지 않은 Run과 이전 B1 비교의 근거이며, B2 Idle의 Unity 검증으로 취급하지 않는다. B1 Run의 전체 메시 지면 아래24.32mm와 Attack 지지 추정창 잔여 이동은 이번 좁은 수정에서 해결한 것으로 보고하지 않는다. 의복 보조 움직임·손가락·붓 파지 작업은 추가하지 않았다. 추가 유료 사용0.

재실행 도구: `Tools/Blender/C02_RigFaceLab/motion_build_b2.py`. B1 편집본을 별도 headless Blender로 열어 실행하며 기존 B2 후보를 조용히 덮어쓰지 않는다.
