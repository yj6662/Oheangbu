# C02 Rig/Face Lab — Unity 최종 B2·C3 검증

독립 Unity 씬과 프리팹에 B2 몸·C3 눈꺼풀을 연결하고 실제 재생을 확인했다. B1 Idle의 약16cm 들뜸은 원본 Hips 스케일 문제를 수정한 B2에서 첫 표본4.074mm로 줄었다. **전체 판정은 UNVERIFIED. 기술 통합과 명시한 수치 검사는 실행했으며, 캐릭터 전체 미술·물리·게임플레이 품질은 미검증이다.**

## 최종 파일

- [실험 씬](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Scenes/Integrated_B2_C3_Review.unity)
- [실험 프리팹](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Prefabs/Integrated_B2_C3_LabRig.prefab)
- [수치·근거 JSON](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/unity_validation.json)

## 실제 검증 결과

| 항목 | 판정 | 확인 범위와 제한 |
|---|---|---|
| 최종 모델·스켈레톤 임포트 | PASS | Own valid human Avatar; 24 deform bones, 52784tris=51760 body+512+512 lids. 3 renderers, 87788 imported vertices, 3 material slots, 2 shapes. Source FBX SHA bd68dc1305eb1404050a95b60ee5c382cd9ba4baf168e59fc15d8f106c485616. No extra bones. |
| B2 Idle 들뜸 수정 | PASS | B1 imported Humanoid Idle0 minY160.730mm versus Generic source1.281mm. Source diagnosis: Idle Hips scale1.1764704. Source-Y bake alone failed. B2 sets Hips scale1 and authors pelvis/leg pose at fixed root/ground. Final Human Idle0 minY4.074097mm, maxY1.626909m; body-only B2 probe agrees. Ground/root remainY0. Whole-motion foot contact and artistic naturalness are not implied. |
| 5개 웨이트의 실제 평가 | PASS | 16 actual final-model pose samples: 297 retained fifth-influence vertices differ between FourBones and Unlimited, finite all; max delta13.1344mm. Temporary Unlimited runtime scope restores FourBones. |
| 원본 722개 다섯 번째 웨이트 완전 보존 | FAIL | Source/Blender roundtrip retains722. Unity requested minBoneWeight0, importer persists0.001; retains297, prunes425 belowthreshold. Coordinate error versus unpruned source722 remains UNVERIFIED. Final model deliberately documents this limit; maximumweight sumerror1.1921e-7, unweighted/nonfinite0. |
| 원본 텍스처·URP 재질 | PASS | Body and both eyelids use same originalPNG SHA146f6acb6d30310961e862fe1df5eae930bfd6de5e747f386d1cf9811973c032. Both importedbasecolor files byte-identical. URP/Lit, smoothness0.3. No final roughness/normal source was supplied; those channels are not claimed restored. Hyper3D textures not mixed. |
| 표정 수치·Animator 권한 분리 | PASS | BlinkLeft/Right sliders0..1 map to0..100. 0/.25/.5/.75/1 finite CPU BakeMesh, neutral return0. 17raw takes retained; eight constant-zero facebindings removed only from four own animation copies. Final runtime180/444frames show overwriteCount0. |
| D1 프리셋·검수 UI | UNVERIFIED | Neutral0, GentleSquint.35/.35, FocusedSquint.65/.65, EyesClosed1/1. Existing partial eyelid closure only; not emotion/gaze/jaw. Sliders and presetbuttons, manual4clips, six-cycle sequence, threecamera controls are in isolated profile/UI. Automated button-click interaction is UNVERIFIED; same value route exercised by capture/numeric tests. |
| 실제 1배속 6주기/전환 재생 | PASS | Idle4s -> Run6cycles -> Idle4s -> Attack3s, transitions.18s. Actual PlayMode444frames/14.8s at30fps, rootmotionOFF, overwrite0. Logical targetstate in capture can differ from current Animatorstate during crossfade; actual Run time crosses6 during outgoing crossfade. |
| 눈/손/의복의 미술·접촉 품질 | UNVERIFIED | Eyes can be obscured by preserved hair; only two added eyelid skins, no independent eyeball/look/jaw/emotion rig. Mesh faceting, hand detail, full-frame clothing/self-contact quality are not accepted by these technical tests. Face C3 parent report retains skin sampling residual penetration up to0.287mm. |
| 최적화/압축 전후 시험 | UNVERIFIED | B1 identicalsource branches with OptimizeGameObjects+Optimal retained shapes2/tris52784 but maximumsame-order vertex delta17.3945mm at16poses. Final B2 keeps optimizeGameObjectsOFF and animation/meshcompressionOFF. Mesh vertexoptimization+welding remain importer defaultsON. Combined test does not isolate each optimization; allframeB2/GPU/CPU performance unverified. |
| 게임 거리 참고 카메라 | PASS | One actual1280x720 PlayModePNG; cameraFOV60, rootrelativeoffset(0,1.25,-2.8), fixedground0. Reference from existinggamecamera distances only; not actual gameplay, follow/collision/shoulder-controller validation. |
| 게임 규칙·Cloth·충돌·성능 | UNVERIFIED | No canonicalC2/controller/PlayerRig application; no actualgameplay, clothphysics, collisions or inputregression. CaptureCPU render/encoding time is not gameplayperformance/FPS. |
| 동일 Idle0 실제 Play 높이 | PASS | 같은 시작 상태를 실제 Play에서 재현해 월드 메시 최저Y **4.074126mm**, 바닥Y=0, 루트Y=0을 측정했다. 정적4.074097mm와 일치하고 다음 프레임은4.094452mm였다. 전체메시 최솟값이며 양발 전체접촉 승인은 아니다. shadowBias=.05, normalBias=.4를 기록했다. 그림자 분리의 바이어스 영향은 추정이며0바이어스 대조는 하지 않았다. `B2_Play_Ground_Diagnostic/capture.json` |

## 최종 검증 영상

- [B2_Final_Blink_Close](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/B2_Final_Blink_Close/B2_Final_Blink_Close.mp4): 실제 Unity 180프레임, 1280×720, 30fps. 인코딩 검증 통과. 전체 아트 품질 승인과 구분한다.
- [B2_Final_Sequence_Full](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/B2_Final_Sequence_Full/B2_Final_Sequence_Full.mp4): 실제 Unity 444프레임, 1280×720, 30fps. 인코딩 검증 통과. 전체 아트 품질 승인과 구분한다.
- [게임 거리 참고 정지 화면](C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Unity/B2_Final_GameDistance_Reference/frame_000000.png): 실제 게임플레이로 표기하지 않는다.

검수바닥은 고정 Y=0이다. 캐릭터 루트를 옮기거나 프레임별 최솟값을 따라 바닥을 움직이지 않았다. B1 실패·Generic 대조·압축 비교와 이전 영상은 별도로 보존했다.

QualitySettings는 마지막 검사에서 작업 전 SHA256 `90a6246b84df30886fb817936db3934edd391241d003785606137c0e27014856`와 정확히 같다. 임시 검사 설정이 분기 임포트 때 저장된 사실을 발견해, 단일255→4 변경이 원본bytes를 복원하는지 먼저 검증한 뒤 복구했다. helper는 임포트/자산 생성 밖의 평가 구간에만 Unlimited를 적용하도록 수정했다.

C2·공용 PlayerRig·공용 Animator는 변경하지 않았다. 현재 Unity는 최종 실험씬 Edit Mode, dirty=false다.

공식 API에서 [lockRootHeightY](https://docs.unity3d.com/cn/6000.0/ScriptReference/ModelImporterClipAnimation-lockRootHeightY.html)는 Y 루트 운동의 본 베이크, [keepOriginalPositionY](https://docs.unity3d.com/cn/6000.0/ScriptReference/ModelImporterClipAnimation-keepOriginalPositionY.html)는 원본 높이 유지로 설명한다. 이 설정만 바꾼 B1 분기가 실패했다는 수치와 B2 원본 스케일 수정 결과를 구분했다.
