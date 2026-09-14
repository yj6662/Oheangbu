# MagicStoneCar 제작 전달 계약 — 준비 단계

2026-09-12 · TEST. 생성 원형·Blender 실행과 실제 검증 결과는 별도 기록이다. 이 문서는 완료 보고서가 아니다. 무거운 Blender 실행은 제작 담당의 메모리 허용 후에만 시작한다. 기존 Palanquin은 수정하지 않는다.

- Unity +Y 위·+Z 앞·scale1. Blender 작업 +Z 위·-Y 앞. FBX 임포트 root/child 변환을 초기화하지 않고 별도 배치 anchor에 SocketManifest 위치·회전만 적용한다.
- 모델 이름: `Models/MagicStoneCar_{Cabin,Roof,Engine,Wheel}_LOD{0,1,2}.fbx`. 강체 보완은 `MagicStoneCar_Support_LOD{0,1,2}.fbx`.
- LOD0 파트 상한:16k/8k/8k/2.5k. 바퀴4개·보완4k 포함46k 목표, 전체50k 상한. LOD1 전체24k, LOD2 전체12k. 요청 Meshy polycount와 실제 FBX tris는 별도 집계한다.
- 고정 물리 바퀴 중심 FL/FR/RL/RR `(±1.075,.74,±1.25)`, 반지름.6m·X축. 기존 Unity 추종 축2개64tris 유지. Blender Support는 축을 중복 제작하지 않으며 최종 합계에64tris를 더한다. 기존 임시 수직 지지대4개는 새 Frame으로 대체한다.
- Cabin은 뒤로.4~.5m 배치, Engine은 앞쪽 z1.3~2.1m가 읽히게 조합한다. 실제 파트 정리 후 치수·좌석·노출 위치를 확정한다. 전체 약4.6L×3.5H×2.4W는 이미지 기반 목표다.
- Support 내부: `Frame_Rigid`, `Steering_Rigid`, `Lantern{FL,FR,RL,RR}_Frame`, `MagicStoneLanternGlass_{FL,FR,RL,RR}`. 연결된 고정부품은 합치고 등롱4개는 주소 지정 가능하게 유지한다.
- Engine에서 실제 마석 표면을 확인한 후 가능한 경우 `MagicStoneCore` MeshObject로 분리한다. 얼굴에 해당하는 물리 표면 없이 공중 발광 구를 만들지 않는다. 색 기반 emission mask는 후보만 만들며 `emissionMaskVerified=false`에서는 자동 적용하지 않는다.

## 전달 JSON

`TextureManifest.json`의 `entries`는 기본4파트 각1행과 Support2행이다. 기본행: `part`, `material`, `baseColor`, `normal`, `metallicSmoothness`, `occlusion`, `engineEmissionMask`, `emissionMaskVerified`. Source glTF B=금속·G=거칠기를 Unity R=금속·A=1-거칠기로 변환하고 원본 scalar factor를 반영한다. 존재하지 않는 AO는 중립 흰색이다. 원본4K 요청과 실제 이미지 해상도는 PBR 보고서에 따로 남긴다.

Support행은 텍스처 없이 `useConstantMaterial=true`, `constantBaseColor:{r,g,b,a}`, `metallic`, `smoothness`를 제공한다. 재질은 `Support_DarkBrass`, `MagicStoneLanternGlass`. 전체 엔진/장식에 발광을 적용하지 않고 실제 코어·등롱 Renderer만 사용한다.

`SocketManifest.json`: `parts:[{part,localPosition,localEuler}]`, `seatLocalPosition`, `seatLocalEuler`, `engineCoreLocalPosition`, `patternLocalPosition`, `ventLocalPositions`, `lanterns`, `wheelColliderCenters`, `wheelRadius`, `leftWheelVisualEuler`, `rightWheelVisualEuler`, `actualBounds`, `lodTriangles`, `blenderOnlyLodTriangles`, `runtimeAxleTriangles`.

`lodTriangles`에는 Unity 축2개64tris가 포함되고 `blenderOnlyLodTriangles`에는 제외된다. 실제 Unity 배치 수치는 설치 담당이 재측정한다. 모델 왕복·정적 좌석 ray 결과를 실탑승·주행·승차감 통과로 간주하지 않는다.

## 준비된 도구

- `Tools/Blender/WorldMagicStoneCar/build_parts.py`: inspect → 방향 확인 후 build → assemble. 원형 보존·LOD/FBX 왕복·소켓·강체 보완·실제 반복 배치 예산.
- `Tools/Blender/WorldMagicStoneCar/pack_pbr.py`: 한 파트씩 PBR 채널 패킹. 원본 픽셀·UV 보존, 업스케일·재생성 없음.

실행하지 않은 항목은 미검증이며 자동 전 구간 완주·영상 제작은 하지 않는다.
