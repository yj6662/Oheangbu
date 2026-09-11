# 술식 VFX120 — 2026-09-09 체크포인트

상태: **TEST / 제작·검수 진행 중**. 사용자 요청에 따라 현재 구현과 검증 자료를 Draft PR #14에 보존한다. 최종 미술 승인이나 120개 게임 규칙 완성을 뜻하지 않는다.

## 현재 구현

- CSV 120칸의 프로필·프리팹·공유 메시/재질: 배정 100개와 의도적 공백 20개. 공백은 Reserve 시각 후보다.
- 기존 15자 시전은 전용 어댑터와 PlayerRig의 VisualSet으로 연결했다. 나머지 배정 85개에는 새 게임 규칙을 연결하지 않았다.
- 화염·모래·물·무기·환경 표현, 만의 착탄 후 8개 파편, 망의 지면 재도약, 입자 알파를 보완했다. 환경 표면·절단·정화의 실제 월드 규칙 연결은 미구현이다.
- 리본 비가시 원인을 `EditorUtility.CopySerialized` 이후 native 정점 버퍼 0으로 확인했다. Mesh setter로 생성 코드를 고치고 Ribbon 자산을 동일 GUID·형상으로 복구했다. [원인과 검증](NATIVE_MESH_BUFFER_FIX.md).
- 장승을 11개 강체 부품으로 분리하고 이동·타격 표현 시제품을 연결했다. 총 2,720 tris를 유지했으나 **타격 자세는 미완성**이다. [시제품 기록](GUARDIAN_PROTOTYPE_CHECKPOINT.md).
- C02 실험은 PR #13으로 병합됐다. 기존 캐릭터·월드·카메라 변경과 Spike 이동은 이 PR 범위 밖이며 PlayerRig 변경은 VisualSet 연결 한 줄이다.

## 검증 결과와 한계

| 항목 | 확인한 결과 | 한계 |
|---|---|---|
| PR 정적 컴파일 | 9개 어셈블리·102개 소스, 오류 0·기존 경고 14 | Unity 임포트·셰이더·Play·빌드 검증을 대신하지 않음 |
| 리본 복구 | 공유 메시 20개 중 Ribbon 1개 복구. 17종 85장 추가 촬영, 지정 7종 전후 70장 비교에서 가시성 회복 | 최종 동작·게임 맥락 판정은 별도 |
| C2 실제 입력 | 가·노·머를 가상 Mouse/Keyboard로 입력하여 실제 인식→기존 DI 시전→VFX 생성·종료 확인. 3/3, 오류·실패·잔여 효과 0. 입력 필터·바인딩·장치 등 복원 관측 | 적 0개인 검사. 실제 피해·AI·일반 필기·전체 15자·하드웨어 입력·성능은 미검증 |
| 최신 수치 검사 | MotionAudit 120종·111,560 표본, AreaAudit 8/8 통과 | Edit 분석이며 장승 데모 타격의 자연스러움이나 실제 프레임률을 검증하지 않음 |
| 장승 FBX | Blender/Unity 왕복 형상 검사 통과, 11개·2,720 tris | 타격 때 머리 위로 뜨는 부품·어색한 팔이 남음. 관절 접점 감사 도구는 작성·컴파일만 완료 |
| 이전 전수 자료 | 스틸 600장, MP4 120개(1280×720·24fps·9,191프레임), 명암 비교 120쌍 | 리본 복구 이전 자료. 전체 영상의 미술 판정 및 최신 전수 재촬영 미완료 |
| 이전 Play 수명 검사 | 120/120, 실패·오류·Mesh 증가 0 | 재질 +1은 Editor TextCore 폰트 캐시 참조를 확인했으나 누수 없음은 미확정. 최신 전체 변경 후 재검사 미완료 |
| 이전 C2 카메라 자료 | 실제 카메라 45장·진단 카메라 45장, 피해 콜백 26회 | 임시 표적·채널 fixture 검사. 최신 3종 실제 입력 검사와 서로 다른 범위 |

검증 JSON은 검사 시점의 증거이며 이후 변경까지 자동으로 승인하지 않는다. 촬영 비용이 포함된 실행은 성능 측정으로 사용하지 않는다. 최신 정적 컴파일은 [checkpoint_compile_latest.json](../../../Art/SpellVFX120/checkpoint_compile_latest.json)에 기록한다.

## 남은 작업

1. 장승 타격 자세·접점과 몽의 대비·내부 흐름 수정.
2. 엄의 그릇 외피, 옷·옹의 연속 흐름, 상의 실제 요격 맥락 검수.
3. 최신 만·망 및 전체 120종 영상 검수, 전체 Play 재검사와 C2 노·모 재촬영.
4. 입력·전환 전체 회귀, 환경의 실제 게임 신호 연결, 빌드/GPU·프레임률 검증.

## 자료

- [리본 복구](NATIVE_MESH_BUFFER_FIX.md), [복구 전후 미술 검토](BUFFER_REPAIR_REVIEW.md), [장승 시제품](GUARDIAN_PROTOTYPE_CHECKPOINT.md).
- [검수 갤러리](../../../Art/SpellVFX120/REVIEW.html), [이전 120개 영상](../../../Art/SpellVFX120/Clips/), [복구 후 85장](../../../Art/SpellVFX120/BufferRepairedStills/).
- [이전 가독성 체크포인트](READABILITY_CHECKPOINT.md), [토 영상 검토](VIDEO_EARTH_REVIEW.md), [제작 계약](../../Specs/SPEC-SPELL-VFX120.md).
- Unity 검수 씬: `Assets/_Project/Art/SpellVFX120/Scenes/SpellVFX120_Review.unity`. 로컬 명령 도구: `Tools/SpellVFX120/unity_queue.py`.
- 유료 `KoreanTraditionalPattern_Effect` 원본은 제외했으며 [출처 목록](ASSET_AUDIT.md)의 동일 팩이 로컬에 필요하다. 추가 Meshy 소비는 0이다.
- 원본 연속 PNG 전체·중복 revision·임시 큐·캐시는 로컬에 보존한다. PR에는 MP4, 주요 이미지, 촬영 메타데이터와 보고서를 포함한다.
