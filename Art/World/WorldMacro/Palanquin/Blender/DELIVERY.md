# 가마차 파츠 Blender 전달 기록

기존 Meshy 완료 결과 3개를 정리했다. 추가 생성 요청은 없으며 원본 GLB를 수정하지 않았다. 이번 단계는 정적 파츠·조립·내보내기 검증이다. 실제 운전, 서스펜션, 탑승 카메라와 지형 충돌은 Unity 검증 항목이다.

| 파츠 | 원본 실제 tris | 요청 상한 | 최종 FBX 실제 tris | 최종 Unity 크기 X·Y·Z(m) |
|---|---:|---:|---:|---|
| Cabin | 15,462 | 18,000 | 15,462 | 1.65501 · 1.55039 · 3.00000 |
| Roof | 8,025 | 8,000 | 7,935 | 2.30000 · 0.94246 · 3.40000 |
| Wheel | 5,155 | 3,000 | 2,975 | 0.26000 · 1.19954 · 1.19984 |

바퀴 4개를 포함한 실제 합계는 **35,297 tris**, 재질은 파츠별 1개로 총 3개다. 별도 LOD는 이번에 만들지 않았다.

## 형태·좌표 정리

- Unity +Z 전방/+Y 위, Blender -Y 전방/+Z 위다. 캐빈·지붕은 XZ 중앙·바닥 원점, 바퀴는 X축 중심 원점이다.
- 캐빈 원형을 균일 배율로 맞췄다. raw +X가 열린 전면임을 이미지로 확인하고 Blender Z축 -90도로 정렬했다. 실제 전면에는 불투명 막이 없으므로 임의로 면을 제거하지 않았다.
- 지붕 원형은 폭/길이 비율이 좁아 균일 배율만으로는 캐빈을 덮지 못했다. 길이 3.4m와 높이 약0.942m를 유지하고 가로 폭만 1.47647배 늘려 2.3m로 맞췄다. 캐빈 바닥은 지면+1m, 지붕 바닥은 캐빈 상단-0.035m, 조립 최상단은 약3.45784m다.
- 바퀴는 PCA로 축을 정렬하고 외측 림의 방사형 요철을 보정했다. 감소 연산 뒤에도 최대 반지름을 0.6m, 축 두께를 0.26m로 재고정했다. UV와 허브·살 구조를 유지한다. 원본 옥 장식은 양면에 있지만 확인한 원본 -Y 면을 최종 +X 바깥쪽으로 정했다. 좌측은 시각 모델에 Unity Y축180도, 우측은 0도를 사용한다.
- Blender 조립은 캐빈/지붕/4개 바퀴를 분리한다. 바퀴 메시 데이터는 공유한다. 애니메이션·리깅·물리 설정은 포함하지 않는다.

## 검증 상태

| 항목 | 상태 | 근거 / 범위 |
|---|---|---|
| 원본 GLB 보존 | 통과 | 각 파츠 보고서의 생성 전후 SHA-256 일치 |
| FBX 빈 장면 재임포트 | 통과 | 각 파츠 1개 메시, UV 레이어·재질 이름 유지, 유효 정점, 실제 tris 상한 이하 |
| 왕복 크기·축 | 통과 | 좌표별 바운드 오차 1μm 미만 |
| 캐빈 착좌 전방 시야 | 통과 | 실제 캐빈 바운드 내부 (0,2.18,0.65)m에서 좌우±20°/상하±10° 총15개 정적 메시 ray가 5m까지 모두 clear |
| 전면 막 제거 | 해당 없음 | raw 네 방향 이미지와 ray 검사에서 전면 시야막 없음 |
| UV·생성 텍스처 | 통과 | 원래 glTF UV 유지, BaseColor/Normal/packed MR 복사 및 개별 파일 해시 기록 |
| 최종 조립 시각 / 실제 탑승 카메라 | 미검증 | Unity에서 배치와 운전 시점 검토 필요. 정적 캐빈 ray와 구분 |
| 바퀴 회전·서스펜션·가감속·언덕 | 미검증 | Unity 구현/주행 검사 범위 |
| LOD 전환 | 미검증 | 이번 단계에 별도 LOD 없음 |

## 파일

- 편집용 조립: `Art/World/WorldMacro/Palanquin/Blender/Palanquin_Assembly.blend`
- 편집용 파츠: 같은 폴더의 `Palanquin_Cabin.blend`, `Palanquin_Roof.blend`, `Palanquin_Wheel.blend`
- FBX: `Oheangbu/Assets/_Project/Art/World/WorldMacro/Palanquin/Models/Palanquin_<Part>.fbx`
- 텍스처: 같은 Palanquin의 `Textures/<Part>_Image_0.jpg`(BaseColor), `_Image_2.jpg`(Normal), `_Image_1.jpg`(packed MR)
- Unity 명시 연결: `TextureManifest.json`, `SocketManifest.json`
- 파츠 수치와 해시: 이 폴더의 `Cabin.json`, `Roof.json`, `Wheel.json`, `Assembly.json`
- 방향 확인 이미지: 이 폴더의 `Images/`. 최종 사용자 검토용 Unity 스크린샷과 구분한다.
- 재현 도구: `Tools/Blender/WorldPalanquin/build_parts.py`

BaseColor는 sRGB, Normal/MR은 Non-Color이다. packed MR의 roughness는 G, metallic은 B 채널이다. Unity에서 diffuse/normal만 쓰는 재질은 MR을 적용하지 않으므로 파일 보존과 실제 셰이더 적용을 구분해야 한다. 메타·재질·씬 편집은 이 Blender 도구가 수행하지 않는다.
