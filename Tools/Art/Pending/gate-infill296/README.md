# 고정 아치 상부 채움

## 원인과 범위

원래 평문은 폭8.4m 개구를 연속해서 채운다. 오른쪽 검은 초승달을 메시 폭 부족으로 판단하지 않았다. 실제 결함은 평문 최고점8.20482m와 아치 최고9.94901m 사이의 상부 개구다. 문을 아치형으로 키우면 낮은 측면 천장과 회전 중 간섭할 수 있어 기존 평문과 힌지는 그대로 두고 고정 목재 상부 채움·수평 인방을 추가한다.

- KCISA 보유 `SM_G_MetalDoor_001` 메시/재질/UV를 절단·이동한다. 새로운 외부 자산을 사용하지 않는다.
- 실제 아치 MeshFilter 삼각형을 문 좌표로 옮겨87개 천장 표본을 얻는다. 원본 곡선에0.04m 매입하여 맞춘다.
- 하단은 평문 최고점+0.11m다. 고정 수평 인방은 동일 원본을90도 돌려 목재 결을 수평으로 둔다. 절단된 아래면은 원본 경계 UV로 닫는다.
- 상부 시각물은 문 root의 고정 child다. 기존 움직이는 문, 힌지, BoxCollider, NavMeshObstacle, 애니메이션은 편집하지 않는다. 새 Collider나 걷는 면을 추가하지 않는다.
- 각 관문에서 재질별 합본 renderer와1단 저부하 LOD를 만들고 정확한 mesh bounds를 기록한다. 4개 실제 문이 있는 관문에만 적용한다.
- canonical `Gates296` 끝 `GateInfill296(report)`가 같은 결과를 재생성한다.

## 명령

- 좁은 적용: `CompactRebuildAuthoring.RefreshGateInfill296()`
- 닫힘: `CompactRebuildAuthoring.CaptureGateInfill296(false)`
- 열림: `CompactRebuildAuthoring.CaptureGateInfill296(true)`
- 캡처는 기존16번 시점과 같은 pose로 `Captures/GateInfill/closed.png`, `opened.png`를 생성하고 저장씬으로 복원한다.

## 근거 구분

`source-preflight.json`은 원본 FBX와 저장 아치 메시를 읽은 오프라인 검사다. 995개 상부 표본의 원본 목재 coverage 누락0, 수직 회전 여유0.11m를 확인했다. 코드 전체 WorldMacro 컴파일 exit0. 이것을 실제 Unity 적용·카메라 검증으로 간주하지 않는다.

실제 적용 영수증 `gate-infill-refresh.json`은 씬 전체 Collider SHA, 문 serialized 설정/힌지/Nav 상태 전후 SHA와42개 회전 포즈, 생성 메시 coverage/bounds/삼각형 수를 기록한다. 실제 시각 평가는 별도로 수행한다.


## 첫 실제 표시 실패와 교정

첫 `gate-infill-refresh`는4개×2,502삼각형, 각997 coverage, 모든 Collider/문 설정 SHA 동일을 기록했다. 하지만 닫힘/열림에서 상부 목재가 전혀 보이지 않아 완료로 인정하지 않았다. 강제LOD0, LOD/occlusion해제, 양면Unlit, gate부모분리 모두 표시되지 않았다. 저장 메시의 winding/normal/불투명 재질·카메라 투영·시선 삼각형 교차를 대조해 위치/가림을 배제했다. 같은 배열을 Mesh native setter로 구성한05만 즉시 보였다.

생성기는 `EditorUtility.CopySerialized` 대신 저장 Mesh에 Clear/indexFormat/vertices/normals/UV/tangents/triangles/bounds를 직접 쓰고 `UploadMeshData(false)`를 호출한다. 원본 메시·재질·물리 범위는 바뀌지 않는다. 최종 전체 생성 뒤 실제 원재질 닫힘/열림 캡처를 다시 판정해야 한다.

거절된 최초 이미지와 진단은 `History/gate-infill-before-native-upload`에 보존했다. 이 시점의 `gate-infill-refresh.json` 물리SHA는 해당 좁은 적용 단계의 증거이고, 뒤의 전체build 결과와 구분한다.
