# Ring · Rock 기하 수정

2026-09-09. `Vfx120MeshFactory.cs`의 두 실제 렌더 결함을 수정했다. 기존 Tree, WaveCrest 및 Ember/Ribbon fractional-power clamp 수정은 유지했다. Unity 직접 호출은 하지 않았다.

## 확인한 원인

**Ring:** 공통 `Tube`가 매 행의 접선과 월드 up 내적을 검사하여 기준 축을 up/right로 바꿨다. 원이 세로 접선 영역을 지날 때 단면 기준이 갑자기 회전해 대응 정점 사이의 면이 꼬였다. 닫힌 경로의 0/1 접선도 한쪽 차분을 사용해 동일하지 않았고, 서로 다른 UV seam 정점의 노멀은 따로 계산됐다. 실제 원본 렌더에서는 두 군데 검은 접힘과 단면 방향 변화가 확인됐다.

**Rock:** `Ellipsoid`가 극점에도 경도별 정점을 여러 개 만들면서 경도에 의존하는 roughness를 그대로 적용했다. 원래 같은 극점이어야 할 정점들의 높이가 서로 달라져 방사형 면이 벌어졌다. 원본 OBJ의 북극/남극 정점 높이 범위는 각각 0.166566 / 0.197160이었다. 이 수치는 최대 축 1로 정규화한 원형 공간의 길이이며 게임 월드의 고정 미터값이 아니다.

## 변경

- Ring을 전용 analytic torus로 구성했다. 원의 radial 방향과 고정 +Z 단면 방향을 사용하므로 참조 축 전환이 없다. UV 마지막 행·열은 modulo로 첫 위치를 정확히 재사용한다.
- UV 경계 자체는 유지하되 각 경계의 중복 정점들이 같은 평균 노멀을 갖도록 `Builder.SmoothNormalSeam`을 추가했다. 네 겹 모서리는 한 그룹으로 계산한다. 노멀 보정 후 tangent를 재계산한다.
- Ellipsoid의 북극·남극은 각각 하나의 정점으로 만들고 삼각형 fan으로 연결했다. 경도 roughness는 `sin(latitude)^2`로 극점에 접근하며 사라지게 했다. 경도 UV seam은 같은 위치·노멀로 닫았다.
- 공통 Ellipsoid를 사용하는 Blade·Lotus·기본 Beast도 동일한 극점 수정을 받는다. 해당 세 가족의 삼각형 수는 변하지 않고 중복 정점 수만 줄었다. 공통 open Tube의 다른 사용처까지 새 프레임 방식으로 바꾸지는 않았다.

## 수치 검사

`Tools/Art/SpellVFX120/MeshRepairCheck`는 **실제 수정된 Factory C# 소스**와 설치된 Unity 6000.3.9f1의 수학 구조체를 사용한다. Mesh만 CPU 데이터 수집기로 대체하여 Unity 실행 없이 기하를 만든다. 이는 Unity Editor 전체 컴파일·엔진 노멀 구현·렌더링 검사를 대신하지 않는다.

| 항목 | Ring | Rock |
|---|---:|---:|
| 정점, 전 → 후 | 455 → 455 | 117 → 93 |
| 삼각형, 전 → 후 | 768 → 768 | 168 → 168 |
| 위치 1e-6 반올림 기준 열린 경계, 전 → 후 | 12 → 0 | 56 → 0 |
| 같은 기준 비정상 edge 사용 수, 수정 후 | 0 | 0 |
| 퇴화 삼각형, 수정 후 | 0 | 0 |
| 표면 외향 검사 실패 면, 수정 후 | 0 | 0 |
| UV 중복 위치의 노멀 최대 차이, 수정 후 | 0 | 0 |

18개 가족 모두 좌표 유한성 검사를 통과했다. Ribbon은 정상 46 vertices / 44 tris로 생성되며 Tree 1,602 vertices / 2,368 tris, WaveCrest 989 vertices / 1,848 tris도 정상 생성됐다. 열린 잎·종이·파도 등의 의도된 경계를 폐곡면 실패로 해석하지 않았다. 두 수정 대상 이외의 노멀·자기교차 전수 통과를 주장하지 않는다.

원장: `Art/SpellVFX120/Blender/MeshRepair/factory_repair_checks.json`, `source_before_checks.json`.

## 실제 렌더 검수

별도 Blender 5.0.1 배경 프로세스에서 VFX lab 사본만 열어 전후 모델을 렌더했다. 원본은 실제 Unity OBJ, 수정본은 위 C# 실행에서 나온 OBJ다. 내보낸 노멀을 사용했으며 같은 카메라·조명·중립 재질을 적용했다. 이미지에는 텍스트·라벨이 없다.

- `Art/SpellVFX120/Blender/MeshRepair/Ring_Before.png`
- `Art/SpellVFX120/Blender/MeshRepair/Ring_After.png`
- `Art/SpellVFX120/Blender/MeshRepair/Rock_Before.png`
- `Art/SpellVFX120/Blender/MeshRepair/Rock_After.png`
- `Art/SpellVFX120/Blender/MeshRepair/Rock_Before_Top.png`
- `Art/SpellVFX120/Blender/MeshRepair/Rock_After_Top.png`

Ring 정면과 Rock 윗면의 전후 4장을 실제로 열어 비교했다. Ring의 두 검은 접힘과 Rock의 별 모양 극점 틈이 사라졌다. 삼각형 수를 늘리지 않았으므로 원형의 다각형 외곽은 유지된다.

검수 사본: `Art/SpellVFX120/Blender/MeshRepair/Ring_Rock_Repair_Comparison.blend`. 기존 C02 캐릭터 파일과 live Blender 파일은 변경하지 않았다.

**통과:** 위 C# 수치 검사 및 Blender 전후 시각 검사. **미검증:** 이 담당 범위의 Unity Editor 재생성·최종 술식 재질·게임플레이 화면. Unity 검수는 root가 수행한다.
