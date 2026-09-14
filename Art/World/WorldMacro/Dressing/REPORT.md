# 5강토 환경 검토

<!-- FINAL_COMPARISON:START -->
[최종 전달·동일 구도 성능 요약](FINAL_STATUS.md) · [최종 비교·원본 해시](performance_summary.json)

최종 비교는 `entry_final_baseline` / `entry_optimized` / `entry_uncached` 세 기록만 사용한다. 이전 `entry_before` / `entry_after`는 카메라 위치·방향이 다른 탐색 측정이며 최종 기록과 직접 비교하지 않는다. 정지한 한 지점의 120표본으로 전체 맵 120fps를 보장하지 않는다.
<!-- FINAL_COMPARISON:END -->


기존 지형·길·수면·건축물 보존. 영상 없이 1920×1080 정지 이미지. 비주얼 합격은 사용자 판단.

[촬영 위치·구도](capture_plan.json) · [저작 데이터·원본 보존 검사](authoring_audit.json) · [선택 LOD 접지 보정 기록](grounding_geometry.json)

## 구현 범위

256m 셀, 4m 수면·길·건물 제외 마스크, 64m 군락 기준 지역 혼합, 원본 저밀도 LOD 및 빌보드, 풀 80m/관목 220m/나무 800m, 원경 군락 3.2km, 근접 줄기·바위·소품 충돌체 최대384개. 생활 소품은 확정 좌표로 보존되며 퀘스트·보상 연결은 이번 범위가 아님.

조감 촬영에 한해 거리 담채 강도 .10과 원경 식생 확장을 적용한다. 전후 쌍에는 동일한 촬영 조건을 사용하며 이후 런타임 설정을 복원한다. 지상 구도는 실제 플레이어를 옮긴 보행 검사가 아니다. 개별 JSON의 aerialWashOverride·위치·방향·화각을 근거로 구분한다.

- 청림 조감: [전 이미지](Cheongrim_aerial_before.png) / [후 이미지](Cheongrim_aerial_after.png) · [전 기록](Cheongrim_aerial_before.json) / [후 기록](Cheongrim_aerial_after.json). 비주얼 판정 미검증.
- 청림 어깨 높이 지상 구도: [전 이미지](Cheongrim_ground_before.png) / [후 이미지](Cheongrim_ground_after.png) · [전 기록](Cheongrim_ground_before.json) / [후 기록](Cheongrim_ground_after.json). 비주얼 판정 미검증.
- 적로 조감: [전 이미지](Jeokro_aerial_before.png) / [후 이미지](Jeokro_aerial_after.png) · [전 기록](Jeokro_aerial_before.json) / [후 기록](Jeokro_aerial_after.json). 비주얼 판정 미검증.
- 적로 어깨 높이 지상 구도: [전 이미지](Jeokro_ground_before.png) / [후 이미지](Jeokro_ground_after.png) · [전 기록](Jeokro_ground_before.json) / [후 기록](Jeokro_ground_after.json). 비주얼 판정 미검증.
- 철옹 조감: [전 이미지](Cheolong_aerial_before.png) / [후 이미지](Cheolong_aerial_after.png) · [전 기록](Cheolong_aerial_before.json) / [후 기록](Cheolong_aerial_after.json). 비주얼 판정 미검증.
- 철옹 어깨 높이 지상 구도: [전 이미지](Cheolong_ground_before.png) / [후 이미지](Cheolong_ground_after.png) · [전 기록](Cheolong_ground_before.json) / [후 기록](Cheolong_ground_after.json). 비주얼 판정 미검증.
- 현강 조감: [전 이미지](Hyeongang_aerial_before.png) / [후 이미지](Hyeongang_aerial_after.png) · [전 기록](Hyeongang_aerial_before.json) / [후 기록](Hyeongang_aerial_after.json). 비주얼 판정 미검증.
- 현강 어깨 높이 지상 구도: [전 이미지](Hyeongang_ground_before.png) / [후 이미지](Hyeongang_ground_after.png) · [전 기록](Hyeongang_ground_before.json) / [후 기록](Hyeongang_ground_after.json). 비주얼 판정 미검증.
- 황경 조감: [전 이미지](Hwanggyeong_aerial_before.png) / [후 이미지](Hwanggyeong_aerial_after.png) · [전 기록](Hwanggyeong_aerial_before.json) / [후 기록](Hwanggyeong_aerial_after.json). 비주얼 판정 미검증.
- 황경 어깨 높이 지상 구도: [전 이미지](Hwanggyeong_ground_before.png) / [후 이미지](Hwanggyeong_ground_after.png) · [전 기록](Hwanggyeong_ground_before.json) / [후 기록](Hwanggyeong_ground_after.json). 비주얼 판정 미검증.
- 강토 경계 군락 혼합 1: [전 이미지](boundary_1_before.png) / [후 이미지](boundary_1_after.png) · [전 기록](boundary_1_before.json) / [후 기록](boundary_1_after.json). 비주얼 판정 미검증.
- 강토 경계 군락 혼합 2: [전 이미지](boundary_2_before.png) / [후 이미지](boundary_2_after.png) · [전 기록](boundary_2_before.json) / [후 기록](boundary_2_after.json). 비주얼 판정 미검증.

## 기술 검사

저작 검사 UTC: 2026-09-11T22:40:26.3593735Z

- PASS geography, actual water meshes, landmark placement and staging fingerprint
- PASS unique 256m cells
- PASS finite terrain lattice and full habitat/region arrays
- PASS shared meshes and instancing materials
- PASS reviewed source prefab paths retained
- PASS source prefabs, meshes and material/texture dependencies unchanged
- PASS all selected LOD meshes use standalone copies; no retained source FBX subassets
- PASS native-import texture copies at most 1024px (grass 512px)
- PASS texture copy colour/normal space and unreadable runtime settings
- PASS vegetation shader compile
- PASS source prefab renderers are not instantiated in the world
- PASS bounded distance and collider settings
- PASS read-only sampled actual water/route/building/manual exclusion rebake
- PASS persistent fixed placement IDs and source prototype links
- PASS fixed props on measured dry terrain

셀 1225, 원형 설정 48, 군락 11, 확정 소품 112. 이 수치는 배치 데이터의 검사이며 사용자 주행/카메라 검수를 대체하지 않음.

[접지 보정 상세](grounding_geometry.json) · 접지 보정 UTC: 2026-09-11T22:16:31.5718111Z. 선택 LOD의 캐시 메시 밑면·접지 표본에 대한 보정 기록이다. 실제 월드 전 구간의 도보·차량 접촉 검사는 미검증이며, 앞선 저작 검사와 검사 범위·시점을 구분한다.

## 성능

Unity Editor의 같은 1080p 렌더 타깃·고정 카메라 비교. 빌드 성능 보증이 아니며 GPU 미수집은 0으로 간주하지 않음.

120fps의 프레임 예산은 약 8.33ms다. 아래는 저장된 개별 측정 기록이며, 구현이 바뀐 경우 변경 전 기록과 최종 기록을 구분해 평가한다. 한 시점의 짧은 측정으로 전체 맵 목표 달성을 확정하지 않는다.

- [entry_after](entry_after_performance.json): MEASURED, UTC 2026-09-11T22:29:55.7489476Z, 120프레임, 중앙/P95 9.80/13.17ms, CPU 9.82ms, GPU MEASURED, 풀 초과 0.
- [entry_before](entry_before_performance.json): MEASURED, UTC 2026-09-11T22:29:23.1589728Z, 120프레임, 중앙/P95 2.85/5.02ms, CPU 2.79ms, GPU MEASURED, 풀 초과 0.
- [entry_final_baseline](entry_final_baseline_performance.json): MEASURED, UTC 2026-09-11T22:38:51.7226134Z, 120프레임, 중앙/P95 2.77/5.28ms, CPU 2.75ms, GPU MEASURED, 풀 초과 0.
- [entry_optimized](entry_optimized_performance.json): MEASURED, UTC 2026-09-11T22:37:59.0583503Z, 120프레임, 중앙/P95 4.40/5.81ms, CPU 4.36ms, GPU MEASURED, 풀 초과 0.
- [entry_uncached](entry_uncached_performance.json): MEASURED, UTC 2026-09-11T22:38:25.7815935Z, 120프레임, 중앙/P95 6.73/8.29ms, CPU 6.78ms, GPU MEASURED, 풀 초과 0.

## 잔여 검증

실제 도보·차량 완주, 카메라 경계 통과, 순간 이동 직후 충돌, 수목 LOD 전환의 시각 품질, 최종 지역별 밀도·색상은 이 보고서의 정적 검사만으로 통과 처리하지 않음.
