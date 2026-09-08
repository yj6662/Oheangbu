# C02 Rig/Face Lab — Unity B1 검사

**PARTIAL_UNVERIFIED.** 독립 실험씬에서 임포트·표정·실제 재생은 실행했지만, Humanoid Idle의 원본 동작 재현은 실패했다. B2 원본 수정 검사를 기다리는 B1 보존 보고서다.

| 검사 | 결과 | 근거 |
|---|---|---|
| Humanoid structure | PASS | Own valid human Avatar; 24 deform bones, 52784 triangles, 3 renderers and 2 Blink channels. Structure does not imply faithful motion. `Integrated_B1_C3_import_audit.json` |
| Humanoid Idle authored-pose reproduction | FAIL | At Idle normalized 0 Generic direct sampling has y=[0.001281,1.909360]m; Human original and source-Y-baked branches both [0.160730,1.783523]m. Root and floor offset=0. Parent source diagnosis: Idle Hips scale=1.1764704 while Run=1.0. B2 source repair pending. `root_height_and_optimization_audit.json` |
| Generic source coordinates | PASS | Idle0 bounds match Blender source within export precision; Generic is not claimed as completed Humanoid path. `root_height_and_optimization_audit.json` |
| Five-influence CPU evaluation | PASS | Actual imported 297 fifth-influence vertices affect CPU output; 16 poses, maximum FourBones vs Unlimited delta 13.1345mm; finite all. `Integrated_B1_C3_skin_quality_comparison.json` |
| All 722 source fifth-influence retention | FAIL | Importer request minBoneWeight=0 persists as 0.001 in this Editor. 425 source fifth weights below .001 removed, 297 retained. Pose error against all722 original vertices is UNVERIFIED. `Integrated_B1_C3_import_audit.json` |
| BlendShape values and ownership | PASS | 0..1 controls map to 0..100; two Blink channels numeric 5-value tests finite, neutral returns to same coordinates, runtime overwriteCount0. Existing 8 raw constant-face bindings stripped only from four own body .anim copies; original FBX takes retained. `Integrated_B1_C3_face_numeric.json` |
| Face appearance and general expressions | UNVERIFIED | Existing eye skins cover eyes; hair obstructs parts of the close camera. Only two partial blink channels, no eyeball/look/jaw/emotion channels. Three presets .35/.65/1 are partial eyelid closure, not emotion. `Final_Blink_Close` |
| OptimizeGameObjects + Optimal compression | MEASURED_NOT_ACCEPTED | Separate exact-source import branch retained 2 shapes and 52784tris. 16 poses maximum same-order vertex delta17.3945mm vs OFF. Final asset remains both OFF; per-feature isolation, all-frame effect and GPU performance are UNVERIFIED. `root_height_and_optimization_audit.json` |
| Full motion and face playback | PASS_TECHNICAL | Idle4s->Run6 cycles->Idle4s->Attack3s with .18s transitions; 1x30fps actual Play, 444frames/14.8sec; all overwriteCounts0. Logical target state differs from current state during crossfade; actual Run normalizedTime crosses6 during outgoing crossfade. `Final_Sequence_Full_FixedGround/playback_events.json` |
| Ground and camera framing | MEASURED | Ground fixed worldY0 from source ground, root unchanged. The Human Idle gap is visible. Game-distance camera is isolated reference using project camera distances/FOV, not actual gameplay or collision. `Integrated_B1_C3_camera_calibration.json` |
| Gameplay, physics, collision, performance | UNVERIFIED | No canonical controller, C2 runtime, Cloth or gameplay collision test; capture CPU timing is offscreen encoding throughput, not gameplay FPS. `` |

## 실제 Unity 영상

- [Final_Blink_Close](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/Final_Blink_Close/Final_Blink_Close.mp4): 180 frames, 1280×720, 30fps. 인코딩 TECHNICAL_PASS; 아트 품질 미검증.
- [Final_Sequence_Full_FixedGround](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/Final_Sequence_Full_FixedGround/Final_Sequence_Full_FixedGround.mp4): 444 frames, 1280×720, 30fps. 인코딩 TECHNICAL_PASS; 아트 품질 미검증.
- [Final_Sequence_GameReference](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/Final_Sequence_GameReference/Final_Sequence_GameReference.mp4): 444 frames, 1280×720, 30fps. 인코딩 TECHNICAL_PASS; 아트 품질 미검증.

QualitySettings는 임시 평가 설정 저장을 발견해 작업 전 SHA256와 정확히 같은 bytes로 복구했다. C2·공용 PlayerRig·공용 Animator는 수정하지 않았다.

재현용 코드는 `Assets/_Project/Art/C02_RigFaceLab/Editor`와 `Tools/Unity/C02RigFaceLab`에 있다. 본문 수치는 `unity_validation_B1.json` 및 개별 감사 JSON을 따른다.
