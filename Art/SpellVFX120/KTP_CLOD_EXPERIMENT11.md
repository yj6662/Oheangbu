# 모 — 토괴 선두 실험 11

판정: **미술 반려 / 현재 프로필에서 제외**. 최종은 기존 KTP 입자 본체로 복귀했고, 복귀 후 `KTPIntegratedStills12`·`KTPExternalStills12`를 촬영했다. 이 실험을 적용된 개선으로 세지 않는다.

몸체가 흐린 흙먼지로만 보이는 문제를 해결하기 위해 C2에서 사용 중인 `InkRock_L_LOD2`와 `InkRock_K_LOD2`를 재사용했다. 원본은 Seyeonjeong 바위에서 정리된 메시이며, 이번에 Meshy를 새로 호출하지 않았다. 원본 바위와 재질은 변경하지 않았다.

- L 540 tris, K 900 tris를 별도 메시로 복제하고 크기를 정규화했다. 선두에는 L/K/L 세 방출기를 배치했다.
- 한 방출기당 4개가 설정 상한이므로 세 종류의 방출기에 각각 한 입자가 있을 때 1,980 tris, 작성된 입자 상한에서 이론상 7,920 tris이다. 이는 GPU 실측이나 매 프레임 실제 삼각형 수가 아니다.
- 메시에는 UV가 없어 기존 텍스처 재질을 억지로 붙이지 않고 기존 InkPigment 셰이더와 입자 수명 알파를 사용했다. KTP 먼지와 합친 작성 예산은 10 PS / 226 particles였다.
- 실제 정면·외부 각 5장과 정면 42%·64% Life 확대를 확인했다. 토사 파도보다 **갈색 바위 세 개의 행렬**로 읽히고 단단한 외곽선이 강조되어 채택하지 않았다. 영상·실제 Play·별도 성능 검사는 수행하지 않았다.

보존 파일:

- `KTPIntegratedStills11/061_BAA8_0.png`~`_4.png`, `KTPExternalStills11/061_BAA8_0.png`~`_4.png` 및 촬영 메타데이터.
- `element_wash_build_clods11.json`.
- `Oheangbu/Assets/_Project/Art/SpellVFX120/Traditional/Bodies/PF_KTP_SandFront_ClodExperiment.prefab`와 그 메시·재질. 비교용이며 카탈로그에 연결하지 않는다.
- `Tools/SpellVFX120/Experiments/ElementWashClods11.cs.txt`: 재현용 실험 빌더 사본. Unity에서 컴파일하지 않는다.

현재 적용한 입자 본체는 7 PS / 214 particles 작성 예산으로 되돌렸다. 이것이 모래파도의 양감까지 미술 검수를 통과했다는 뜻은 아니다.
