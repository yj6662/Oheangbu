# 식생 밀도와 원거리 표현 보완

범위: 보유 에셋을 이용한 식생 파생본. 지형·건물·수면·게임 규칙은 재생성하지 않습니다.

- 나무: 동일 위치·크기·종류 유지, 근거리 메시 → 4방향 카드 → 원경 수관 카드 군락. 마지막 거리는 3200m.
- 풀: 보유 풀 4개로 만든 지면 패치, 50m 근거리 메시 / 180m 방향별 카드 / 420m 낮은 지면 피복.
- 수목/관목/풀 격자: 7.5 / 4.5 / 1.6m. 각 후보는 군락·지역·고도·경사·실제 물과 경로 조건으로 다시 선택합니다.
- 256m 셀 인스턴싱. 멀리 보이는 나무는 같은 배치의 카드를 셀 군락으로 제출합니다. 카메라 회전만으로 매번 전체 제출 배열을 다시 만들지 않습니다.
- 실제 길 폭과 풀·관목·줄기 여유를 구분합니다. 전투 공간에는 낮은 풀을 남기며 큰 식생을 제외합니다.
- 풀·관목에는 충돌체를 추가하지 않습니다. 큰 줄기/바위의 기존 주변 풀을 유지합니다.

## 검사

실행된 정적 검사는 validation.json에 PASS/FAIL로 기록됩니다. Unity 실행 전 또는 파일이 없으면 미검증입니다. 성능 120fps 달성, 실제 보행, 카메라 통과, 언덕 접지, 미술 합격은 이 코드와 정적 검사만으로 판정하지 않습니다. 화면 캡처와 성능은 주 작업에서 순차 진행합니다. 영상·중간 빌드는 만들지 않습니다.

## 보존·재개

Baseline에는 수정 전 코드와 두 배치 Sheet가 보존됩니다. 생성 에셋은 별도 VegetationPolish 폴더이며 공급자 파일을 변경하지 않습니다. prepare/region 명령은 완료 에셋을 재사용합니다. 시스템 커밋 85% 이상이면 다음 제작 작업을 중단합니다.

## 최종 실행 자료 — 2026-09-14

위 검사 안내는 최초 제작 때의 범위 설명이다. 이후 5강토 적용, 실제 화면 촬영, 제한된 Editor 성능 측정을 실행했다. 아래 결과는 각 원장의 범위와 시각을 따른다. 사용자 보행·미술 승인·독립 빌드 성능까지 통과한 것은 아니다.

| 항목 | 실제 결과와 근거 |
|---|---|
| 배치·원본·셰이더 정적 검사 | [validation.json](validation.json), 02:00:20 UTC, `passed: true`. 73개 프로토타입, 공급자 해시 보존, 5강토 결정적 배치와 동일 수목의 근·원거리 식별, 길·시설 제외 및 셰이더 컴파일 검사 통과 |
| 풀의 넓고 불투명한 잎판 교체 | [grass_fine_source_replacement.json](grass_fine_source_replacement.json). 보유 세연정의 가는 풀·알파 재질로 교체하고 배치 시드·인덱스·크기·확정 위치 보존. 최초 요약의 풀 원형 4개 대신 최종 팔레트는 두 풀 원형을 사용 |
| 풀 투명도 | [grass_texture_repair.json](grass_texture_repair.json), `passed: true`. 원본 RGB를 보존하고 별도 불투명도 맵의 빨강 채널을 파생 텍스처 알파로 옮김 |
| 흰 용머리 관목 재질 복구 | [plant_material_repair.json](plant_material_repair.json), 01:24:56 UTC, `passed: true`. 적로·철옹의 색·노멀·불투명도 연결 누락 12슬롯을 복구해 누락 0. 원본 재질·텍스처 보존 |
| 무거운 관목 카드 LOD | [shrub_lod.json](shrub_lod.json), 01:42:19 UTC, 4개 지역 프로토타입 통과. 화성행궁 관목 40,788tris, 용머리 관목 44,516tris의 근·중거리 메시를 보존하고 각각 2tris·4방향 원형 아틀라스 카드를 추가. 배치·밀도·원본 해시 보존 |
| 실제 정지 화면 | 5강토, 폐광 출구, 청림 접근부와 지역 경계의 1920×1080 화면 확보. 파일별 `CAPTURED`는 촬영 성공이며 미술 합격·통행 검증을 뜻하지 않음 |

청림은 [숲 접근부](Screenshots/Polish_forest_approach_after.png), 나머지는 [적로](Screenshots/Polish_Jeokro_ground_after.png) · [철옹](Screenshots/Polish_Cheolong_ground_after.png) · [현강](Screenshots/Polish_Hyeongang_ground_after.png) · [황경](Screenshots/Polish_Hwanggyeong_ground_after.png)에 있다. [경계](Screenshots/Polish_boundary_1_after.png)와 [폐광 출구](Screenshots/Polish_mine_exit_after.png)도 보존했다. 촬영 시점은 대응하는 JSON에 기록했으며, 최신 철옹 자료는 02:15:29 UTC다. 서로 다른 시점의 화면이므로 모두 동일한 실행 상태에서 찍었다고 간주하지 않는다.

### 성능 측정 결과

[Performance/forest_comparison.json](Performance/forest_comparison.json)은 동일한 청림 숲 접근부 위치·방향·1920×1080 오프스크린 타깃에서 Editor Play 상태로 각각 180프레임을 기록한 비교다. 이전 배치도 현재 렌더러로 표시했다. 비활성 검토 카메라를 사용했던 0 draw 측정은 [무효 원장](INVALID_disabled_camera_performance.json)으로 분리했다.

| 상태 | CPU 중앙값 | GPU 중앙값 | 제출 part-instance |
|---|---:|---:|---:|
| 이전 배치 | 6.06ms | 3.21ms | 6,325 |
| 고밀도·최적화 전 | 19.66ms | 19.55ms | 97,307 |
| 고밀도·관목 카드·가시성·그림자 조정 후 | 9.53ms | 5.96ms | 58,815 |

최적화 후 측정 시각은 01:47:06 UTC다. CPU p95는 11.68ms, GPU p95는 6.11ms, Unity 할당량은 약 4,701MiB, 시스템 커밋은 약 59%였다. 고밀도 전후 상주 셀 445개와 상주 배치 인스턴스 1,384,682개는 동일하며, 제출 비용을 낮췄다. 상주 인스턴스 수는 개별 GameObject 수가 아니다.

**120fps의 8.33ms 목표는 이 측정의 CPU 기준으로 미달**이다. 다른 지역의 최악값, 이동 중 스트리밍·LOD 전환, 전체 GPU 오버드로, 장시간 메모리 누적, 독립 빌드 성능은 미검증이다. 정적 검사 원장의 공통 `unverified` 문구에 성능이 남아 있어도 위 한 지점의 별도 측정까지 미실행이라는 뜻은 아니다. 실제 보행·차량 통행, 언덕과 수변 접지의 전 구간 검사, 자연스러운 경로 유도와 최종 외형 평가는 사용자 검토로 남긴다.
