# 식생 밀도 보완 — 실행 인계

원본 보존 완료. C# 정적 컴파일 통과. Unity 적용·실제 렌더·성능 검증은 주 작업에서 수행합니다.

진입점: `Oheangbu.EditorTools.WorldMacro.WorldMacroVegetationPolish.Run(command)`

1. 기존 `W_WorldMacro_Playtest` 씬의 Edit 모드에서 `prepare` 실행.
2. `status`가 `COMPLETED`가 될 때까지 대기. 각 Editor update에서 한 원형만 만들고, 85% 메모리 가드에서 중단합니다. 도중 중단되면 `prepare` 재실행으로 완료 원형을 재사용합니다.
3. 청림 우선 적용은 `cheongrim`. 지역별 순차 확대는 `region:Jeokro`, `region:Cheolong`, `region:Hyeongang`, `region:Hwanggyeong`. 전체 일괄 큐는 `all`이며 내부적으로 위 순서로 한 셀씩 처리합니다.
4. 적용 완료 후 `validate`. `validation.json`과 `TECHNICAL_REPORT.md` 생성.
5. 주 작업에서 스크린샷 및 1080p 성능을 순차 확인합니다. 중간 빌드/영상은 만들지 않습니다.

## 바뀐 구조

- 파생 Sheet와 재질/카드/패치는 `Assets/_Project/Art/World/WorldMacro/Playtest/VegetationPolish`에 생성.
- 풀: 보유 에셋 4묶음 메시 → 4방향 카드 → 보유 풀의 윗면을 촬영한 낮은 피복. 기본 전환 50m / 180m / 420m. 물과 급경사에는 지면 피복을 제외.
- 나무: 가까운/먼 population에 같은 ID, 위치, 크기, 종류 사용. 4방향 카드, 1.6km 이후에도 같은 나무의 원경 수관 카드를 256m 군락 단위로 컬링해 3.2km까지 유지. 원경 카드를 별도의 합친 수관 메시로 베이크한 것은 아님.
- 길: 모든 풀까지 제거하던 넓은 AABB를 실제 회전 사각형/경로 폭으로 변경. 큰 줄기/관목/낮은 풀의 여유 분리. 실제 전투 공간에는 낮은 풀 유지.
- 단순 GPU 인스턴싱 유지. 카메라 회전마다 전체 배열을 다시 만드는 대신 가시 셀 집합 및 8m 이동 구간에 따라 갱신. 확장된 거리 여유로 캐시 구간의 LOD 누락 방지.
- 수동 Corridor 나무의 화면비 LOD와 미터 기반 재질 페이드 충돌 제거.
- 고사리, 풀, 관목은 SeyeonjeongPavilion/HwaseongHaenggung/YongmeoriCoast 보유 프리팹에서 가져옴. 계절·지역·고도·물가·민가 조건은 기존 군락 저작 데이터와 신규 최대 고도/경사 조건을 함께 사용.

## 남은 실제 검증

컴파일 외 화면 결과는 미검증입니다. 방향별 카드의 전환, 지면 패치의 언덕 접지, 새 후보의 실제 크기, 5지역 경계, 길·계단·차량 공간, 충돌 풀 포화, 메모리 및 8.33ms 프레임 목표를 Unity에서 확인해야 합니다. 정적 population 검사는 실제 통행 및 미술 합격을 뜻하지 않습니다.
