# 064 몸 — 반려 Meshy 장승의 Blender 구제 실험

2026-09-09. **얼굴 재사용과 기둥형 실루엣 복구는 가능했다. 이번 결과의 최종 미술 판정은 반려이며, 정적 형상 실험본으로만 보존한다.** 추가 Meshy 요청은 0건, 추가 비용은 0크레딧이다. Unity에 적용하지 않았다.

실제 Blender MCP로 원본 메시를 조사하고 별도 씬 `Jangseung_Salvage_Experiment01`에서 수정했다. 원본 GLB와 기존 `MeshySummons_Source.blend`의 작업 전후 SHA-256이 동일하다. 종료 후 기존 Blender 파일과 `MeshySummonsLab` 씬을 다시 열었으며 `dirty=false`를 확인했다.

## 실제 재사용 범위와 조형

Retry02 원본은 14,605정점·12,391삼각형의 메시 1개였다. UV 경계 중복 정점을 진단용으로 용접하면 6,189정점의 연결 성분 1개가 된다. 얼굴·갑옷·팔·장화가 이어져 있어 단순히 오브젝트를 분리하는 방식으로는 원하는 파츠만 사용할 수 없다.

- **Meshy에서 실제 재사용:** 눈·눈썹·코·입·치아를 포함한 중앙 얼굴 부조. 원본에서 절단 추출한 단계의 실제 수치는 2,773삼각형이다. 가로폭을 줄이고 세로를 늘렸으며 돌기둥과 만나는 가장자리의 깊이를 국부적으로 낮췄다.
- **Blender에서 새로 제작:** 높이 2.2m의 연속된 돌기둥, 뒤·옆면·상단, 기둥의 약한 폭 변화와 모서리. 원본 병사의 몸통을 늘린 것이 아니며, 이 부분을 Meshy 생성물이라고 부르지 않는다.
- **제외:** 투구·뿔·귀, 갑옷 흉부, 단추 돌기, 팔·손, 허리띠·치마, 다리·장화. 기둥에 문자·태극·장식 이미지를 붙여 정체성을 대신하지 않았다.

얼굴 절단부의 열린 경계를 닫은 뒤 기둥과 복셀 결합하고, 최소 평활화와 감면을 적용했다. 처음의 열린 표면 결합은 얼굴이 거의 사라져 실패했으며 채택하지 않았다. 마지막 결합은 눈·코·입이 실제 렌더에서 남는 것을 확인했다. 재메시 이후에는 원본 얼굴과 새 기둥에 기원별 삼각형 수를 정확히 나눌 수 없으므로, 2,773이라는 수치는 **결합 전 추출량**이며 최종 메시의 Meshy 기원 삼각형 수라고 주장하지 않는다.

작업본에는 원본 몸, 절단 얼굴, 새 기둥 및 폐기한 결합 실험이 숨겨진 상태로 보존되어 있다. 최종 렌더 대상은 `SM_064_Jangseung_Salvage01` 한 개다. 검수 바닥·카메라·조명은 모델 통계와 FBX에서 제외했다.

## 실제 렌더 판정

중성 회색 조명과 단일 무광 clay 재질로 1280×720, 16:9 정면·후면·양 측면 4장을 렌더하고 각각 직접 열어 보았다. 사선 1장도 확인했다. 이미지 내부에는 텍스트나 라벨을 넣지 않았다.

**개선된 부분:** 정면과 양 측면에서 길고 좁은 하나의 기둥으로 읽힌다. 복식·장화·사람 팔다리가 사라졌다. 뒤와 옆은 끊어진 연결부나 빈 내부 없이 이어지며 얼굴과 기둥 사이에 분리된 틈이 보이지 않는다.

**최종 반려 이유:** 정면과 사선에서 눈썹·입술이 좁은 판에 얹힌 귀면 부조처럼 보인다. 머리 전체가 길게 조형된 장승 얼굴로 읽히기에는 얼굴 아래의 수평 턱선과 부조 외곽이 남아 있다. 옆·후면의 돌기둥도 아직 너무 매끈한 기본 형상이다. 기존 병사 모델의 큰 문제는 제거했지만, 이 모습으로 새 소환수 미술 완료를 선언하지 않는다. 이번 한 번의 실험에서 더 세밀한 조각이나 유료 재생성을 이어가지 않았다.

## 실측과 검증 상태

최종 수치의 기준 파일은 `final_mesh_checks.json`이다. `construction_stats.json`은 첫 결합 시도의 중간 기록이며 최종 수치로 사용하지 않는다.

| 검사 | 상태 | 실제 근거 |
|---|---|---|
| 원본 파일·기존 Blender 작업 복원 | 통과 | 두 원본 해시 동일, 원래 씬 복귀, dirty=false |
| Meshy 얼굴 재사용 | 통과 | 원본에서 실제 2,773tris 추출, 얼굴 중간 진단 렌더 |
| 사람 복식·장화 제거 / 긴 기둥 외곽 | 통과 | 정면·후면·양 측면 실물 렌더 확인 |
| 최종 삼각형 / 정점 | 통과 | **14,000 tris / 7,000 vertices**, 최종 Blender 메시 실측 |
| 크기 | 통과 | X 0.65553m × Y 0.66648m × Z 2.20002m |
| 연결·경계·퇴화면 | 통과 | 연결 성분 1, 열린 경계 0, 비매니폴드 경계 0, 영면적 면 0, 비정상 좌표 0 |
| 장승 최종 미술 | **실패** | 귀면 부조의 붙인 느낌, 매끈한 기둥 표면이 남음 |
| UV·PBR·게임용 재질 | 미검증 | 단색 검수 재질만 사용, 새 토폴로지용 UV/베이크 미제작 |
| 리깅·애니메이션 | 미진행 | Armature 0, Animation 0 |
| FBX 왕복 | 미검증 | 최종 메시만 FBX로 내보냈으나 재임포트 검사는 하지 않음 |
| Unity 임포트·실제 플레이·성능 | 미검증 | Unity 파일·프로필·씬을 변경하지 않음 |

기존 `build_manifest.json`의 몸은 바위·흙 거인 소환 표현이다. 이번 장승은 그 후속 미술 후보를 시험한 것이며 기존 게임 효과·프로필·소환 동작을 바꾸지 않았다.

## 결과 파일

- [편집용 Blender 실험본](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/SM_064_Jangseung_Salvage01.blend)
- [정적 FBX — 왕복 미검증](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/SM_064_Jangseung_Salvage01.fbx)
- [정면](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/01_salvage_front.png) · [후면](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/02_salvage_back.png) · [좌측](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/03_salvage_left.png) · [우측](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/04_salvage_right.png)
- [사선](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/05_salvage_oblique.png)
- `source_mesh_inspection.json`, `construction_stats.json`, `final_mesh_checks.json`, `preservation_before.json`, `preservation_after.json`.

![실험본 사선 검수](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/Renders/05_salvage_oblique.png)
