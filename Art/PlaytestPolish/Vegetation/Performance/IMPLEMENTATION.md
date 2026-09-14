# 식생 렌더 비용 수정

2026-09-14. 실제 forest_approach 기준이며, 과거 0 draw 측정은 근거에서 제외한다.

- 보존된 비교값: 이전 CPU 6.0618ms / GPU 3.209984ms, 고밀도 적용 후 CPU 19.6622ms / GPU 19.549952ms. 동일 1920×1080 Editor 정지 시점이며 독립 빌드 성능은 아니다.
- 고밀도 적용 후 97,307 part-instance, 129 제출 draw, 1,384,682 resident instance. Resident 수는 실제 화면에 그린 수와 다르다.
- 실제 선택 submesh 기준 SM_Bush는 LOD0/1 모두 40,788 tris(190+40,598), YMC_Bush는 모두 44,516 tris(13,726+24,690+6,100). 작은 관목인데 기존 75~220m 구간에도 이 메시가 사용되었다.
- 종전 가시성 판정은 256m 셀 단위였다. 카메라 방향이 바뀌어도 보이는 셀 집합이 같으면 같은 제출 목록을 재사용했다.

수정은 DenseVegetation 파생 씬에만 적용한다. 기존 배치 ID·시드·밀도·충돌·거리 설정 및 공급자 원본은 유지한다.

1. 원형/바람/카드 외곽을 포함한 보수적인 개체별 frustum 판정. 캐시는 이동 0.4m·회전 0.75° 이내만 재사용하며 그 범위를 가시성 여유에 포함한다.
2. 가까운 원본 외형은 유지. 실제 2,612-tris YMC 풀은 22m부터 기존 2-tris 카드로 전환한다. 16-tris 낮은 풀의 기존 근거리 표시는 유지한다.
3. 관목 LOD2 카드가 별도 authoring으로 준비되면 고밀도 작은 관목은 16m / 24m 구간에서 하위 메시·카드로 순차 전환한다. 낮은 관목은 18m / 36m, 높은 대나무·갈대는 32m / 70m를 사용한다. 준비 전에는 원래 LOD 경로를 유지한다.
4. 그림자는 나무 48m, 관목 14m, 바위·소품 45m 근거리로 제한한다. 화면 밖 가까운 줄기는 실제 기존 하위 메시로 ShadowsOnly 제출하여 화면 안으로 들어오는 그림자를 남긴다.
5. LOD의 shader fade는 해당 DrawMeshInstanced의 MaterialPropertyBlock으로만 보정한다. 공유 재질 파일·지역 색·전체 조명은 수정하지 않는다.
6. 실제 Observer 캐시의 프로토타입/LOD/선택 submesh 삼각형·그림자·카드·가시성 제외·packet build 시간을 기록한다. 이 수치는 GPU pass별 실제 호출 수나 시간과 구분한다.

명시적 읽기 진단: `Oheangbu.EditorTools.WorldMacro.WorldMacroDressingRenderCostReview.Execute("forest_optimized")`.

현재 단계: 소스 수정 완료. Unity 컴파일·실제 최적화 후 측정·화면 비교는 root가 순차 실행하며, 실행 전에는 성능 달성이나 외형 합격으로 보고하지 않는다. 기존 소스 사본은 `Before`에 보존했다.
