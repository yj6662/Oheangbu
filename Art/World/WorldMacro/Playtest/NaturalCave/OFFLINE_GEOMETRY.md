# 자연 동굴 형상 원본

`geometry.json`은 Unity Cave 루트의 로컬 좌표(미터)로 작성된 최종 저감 메시이다. `Natural_Cave_Source.blend`에는 숨긴 원본 SDF 메시와 별도 Runtime 메시를 함께 보존했다. 월드 지형과의 결합은 이 파일에서 수행하지 않는다.

- 내부 암반: 원본 128,446 → 최종 **71,552 tris**.
- 보행 바닥: 원본 15,506 → 최종 **1,213 tris**.
- 최종 합계 **72,765 tris**. 원본 추출 통계는 `geometry_manifest.json`, 최종 저감 통계는 `blender_statistics.json`에 기록했다.
- 입구는 로컬 `z=-26`에서 열려 있으며 바닥은 `y=0.03`이다.
- 바닥 외곽선은 연결된 하나의 폐곡선이다. 가변 단면은 선택한 경로점에서 폭 16.2~36.65m, 천장 높이 8.28~16.68m이다.
- 내부 표면은 공기 쪽을 향한다. 산 외부를 덮는 외벽 메시가 아니므로 산 지형 커버를 별도로 유지해야 한다.
- 개별 암석 스캔, 광맥, 지지목, 조명, 작업 흔적과 최종 재질은 Unity 결합 단계의 책임이다.

재현 순서:

1. `python Tools/Art/build_natural_cave.py`
2. `blender --background --python Tools/Art/natural_cave_blender.py`

첫 명령은 원본 JSON을 다시 만들고, 두 번째 명령은 Blender 원본과 저감 JSON을 만든다. 두 번째 명령만 반복하면 이미 저감한 파일이 다시 입력되므로 위 순서를 유지한다.

`offline_inside.png`와 `offline_floor_footprint.png`는 1920×1080의 중립 재질 진단 렌더이며 실제 Unity 플레이 화면이 아니다. 그림 안에 텍스트나 라벨을 넣지 않았다. 경로의 머리 위치를 약 25cm 간격으로 샘플링했을 때 공기 경계에 대한 최소 부호 거리 여유는 2.006m였다. 실제 월드의 지면·지붕 관통, 충돌 이동, Unity 최종 재질은 이 오프라인 검사로 확인하지 않았다.
