# 술식 VFX 기하 — Blender 후속 검수

2026-09-09. 별도 `Art/SpellVFX120/Blender/SpellVFX120_MeshLab.blend`에서 Blender MCP로 수행했다. 플레이어 C02 장면은 열려 있던 상태를 읽기만 했으며 dirty=false를 확인한 뒤 새 빈 작업 파일로 전환했다. Meshy 등 추가 유료 요청은 없다.

## 실제 확인과 변경

Unity가 내보낸 `MeshSources/*.obj` 16개를 읽었다. 첫 내보내기에서 15개에는 유효 기하가 있었고 Ribbon에는 헤더만 있었다. 첫 OBJ들의 유한 좌표는 전수 확인했다. Ribbon 누락은 root가 Factory 끝점의 fractional power/float 오차 문제를 수정하고 재생성 중이므로 이 보고서에서 완성으로 세지 않는다. 최초 OBJ 임포트 수치는 `unity_mesh_import.json`, 실물 배열은 `UnityFactory_15_SourceShapes.png`와 `source_shape_grid.json`에 남긴다.

Beast 원형을 같은 16:9 카메라로 실제 렌더하니 둥근 완구형 얼굴, 몸통 위 이음새, 어깨·무릎의 갈라진 면, 뒤로 뻗은 막대 같은 꼬리가 보였다. 이 원형의 데이터는 `SOURCE_UnityFactory_ReadOnly` 컬렉션에 보존했다.

새 작업본에서 몸·머리·네 다리·발·꼬리 부피를 다시 구성하고 Blender Voxel Remesh → 국부 부피 정돈 → Decimate로 연결된 기본 형태를 만들었다. 곡선 프레임을 연속 회전시켜 원형 다리의 프레임 전환 이음새를 없앴다. 얼굴에는 볼·둥근 귀 안쪽·눈·코·눈썹·수염·발가락 구분선과 먹 줄무늬를 추가했다. B1 렌더에서 이마 선이 머리 안에 묻힌 것을 확인하고, B2에서는 실제 스컬프 표면에 Ray Cast로 투영했다. 눈은 크게 튀어나온 형태에서 더 작고 둥근 동공으로 바꿨다.

토 거인용으로는 호랑이를 재색칠하지 않고 직립한 별도 StoneGuardian을 만들었다. 두 발, 넓은 어깨, 무거운 주먹, 긴 돌기둥 얼굴, 눈썹·긴 코·입·돌 이빨과 짧고 넓은 모자를 구성했다. 장승형 얼굴 문법을 참고한 신규 VFX 형상이며 특정 유물의 복제나 역사적 고증 완료 모델은 아니다.

## 전달 모델

| 후보 | Blender 정점 | 실제 삼각형 | 재질 슬롯 | 골격 | 색 전달 |
|---|---:|---:|---:|---:|---|
| Beast_FolkTiger_B2 | 2,352 | 4,500 | 1 | 0 | `Color` 정점색 9종 |
| StoneGuardian_Jangseung_B1 | 1,448 | 2,720 | 1 | 0 | `Color` 정점색 768톤 |

가족별 단순 원형의 2,000 tris 권고보다 높은 두 예외다. 이는 완성 캐릭터가 아닌 작은 수의 소환 VFX에 쓰는 정적 메시이며 실제 동시 개체 수·성능은 Unity에서 확인해야 한다. 전체를 고해상도 캐릭터 제작으로 확대하지 않았다. 별도 텍스처가 없고 단일 머티리얼이므로 눈·코·줄무늬를 보이게 하려면 Unity 셰이더가 Mesh Vertex Color를 읽어야 한다.

파일은 모두 `Art/SpellVFX120/Blender/`에 있다.

- 편집본: `SpellVFX120_MeshLab.blend` (원형·진행 후보·최종 후보 분리).
- 최종 교환: `Beast_FolkTiger_B2.fbx`, `StoneGuardian_Jangseung_B1.fbx`.
- 보조 OBJ: 같은 이름 `.obj`. 좌표는 Unity Y-up/+Z-forward이며 확장 v RGB를 포함한다. 표준 OBJ 임포터가 정점 RGB를 무시할 수 있으므로 **색을 포함한 정본 교환은 FBX**다.
- 수치 원장: `spirit_export_manifest.json`, `spirit_fbx_roundtrip.json`.
- 원본/변경 비교: `Beast_Before_ThreeQuarter.png`, `Beast_B2_ThreeQuarter.png`, `Beast_B2_Front.png`.
- 돌 거인: `StoneGuardian_B1_ThreeQuarter_Full.png`, `StoneGuardian_B1_Front_Full.png`.
- 최초 돌 거인 이미지 두 장은 발이 잘린 카메라 실수 자료다. `Full` 접미사가 최종 구도이며 원본은 진단 이력으로만 남긴다.

## 교환 검증

각 최종 FBX를 별도 **빈 Blender 장면**에 다시 가져왔다. 정점 색 해독 방식은 명시적으로 LINEAR를 사용했다.

| 항목 | Beast | StoneGuardian |
|---|---:|---:|
| 임포트 메시 수 | 1 | 1 |
| 실제 삼각형 유지 | 4,500 | 2,720 |
| 최대 월드 정점 오차 | 9.55e-8 m 이하 | 8.73e-8 m 이하 |
| 전체 RGBA 최대 오차 | 0 | 0 |
| 색 속성 누락 | 0 | 0 |

두 메시의 피벗은 경계 중심이고 최대 축은 1이다. Unity 예상 XYZ 크기는 Beast `(0.35965, 0.56200, 1.0)`, StoneGuardian `(0.90961, 1.0, 0.35694)`다. 종축/앞은 +Z, 위는 +Y다. **발바닥 피벗이 아니므로** 지면 배치에서 하단 bounds 보정을 적용해야 한다. FBX 임포터의 축 변환과 모델 하위 Transform을 무시하고 Mesh만 복사하면 방향이 달라질 수 있어 Unity 측 확인이 필요하다.

## 판정과 한계

- **통과 — 한정:** Blender 실제 모델 생성·원본 보존, 유한 좌표·별도 몸/머리/발/꼬리 가독성, 서로 다른 호랑이/돌 거인 실루엣, 단일 메시/재질, 빈 장면 FBX 좌표·삼각형·정점색 왕복.
- **검토 후보:** 민화 영물과 장승형 돌 얼굴의 양식화. 단순한 형태와 과장된 얼굴이 남으며 전문 민화 작가 수준의 독자적인 조형·사용자 미술 승인을 뜻하지 않는다. 실제 수묵 술식 재질·카메라 거리에서 다시 판단한다.
- **미검증:** Unity 최종 색/축/크기/그림자·동시 소환 비용·게임플레이 가독성. 해당 검증은 root의 실제 술식 캡처 작업이 소유한다.
- **이번에 하지 않음:** 동물 이동 리그·관절 애니메이션·물리·머리카락/천·플레이어 모델 변경·유료 Meshy 생성.

KTP 원본은 문양 형태 참고에만 열었으며 새 두 모델에 유료 텍스처를 복사하거나 외부에 업로드하지 않았다. C02 캐릭터 파일 보존 SHA-256은 `D3DD7DCB3FE1776025146E76F0391DACF8BD2E55252C80ECAD93C1C8C87572AC`다.
