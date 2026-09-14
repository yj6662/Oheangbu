# 모델 에셋 수령·분류·복구 결과

추가 수령(2026-09-07): House_1/House_2 패키지의 초가집 2종을 4방향으로 확인하고 검토 원장에 추가했다. 아래 9팩/1,591항목 수치는 최초 감사 기록이다. 추가 원본·선택·수치=[HouseAssetIntake.json](C:/Users/yj666/Oheangbu/Docs/Assets/HouseAssetIntake.json), C2 배치 검증=SPEC-DEV-CODEX-WORLD §13.

2026-09-07 · SPEC-ASSET-INTAKE: TEST · 원장 전수 감사와 시각 분류 기록 완료. 불확실한 용도와 원본 누락은 아래에 별도로 남긴다. 이 결과는 예준의 최종 미술 PASS나 실제 씬 배치·물리·성능 검증을 뜻하지 않는다.

**후속 씬 배치 후보를 찾을 때는 검토 반영 원장을 우선 사용한다:** [ModelCatalog.reviewed.json](C:/Users/yj666/Oheangbu/Docs/Assets/ModelCatalog.reviewed.json) · [ModelCatalog.reviewed.csv](C:/Users/yj666/Oheangbu/Docs/Assets/ModelCatalog.reviewed.csv). `category`·`label`은 시각 검토의 교정을 반영하며 `automaticCategory`·`automaticLabel`은 Unity 자동 분류 원값이다. `reviewStatus`·`notes`와 참조 복구 기록을 함께 읽는다. 원본 감사 데이터 [ModelCatalog.json](C:/Users/yj666/Oheangbu/Docs/Assets/ModelCatalog.json)은 보존한다.

[오프라인 모델 도감](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/AssetCatalog/index.html)에서 팩·분류·이름으로 검색하고, 모델을 눌러 원본 경로·LOD·삼각형·재질/참조 상태·검토 메모를 확인할 수 있다. 추가 촬영된 모델은 모달의 시점 버튼으로 6방향 이미지를 확인한다.

## 검사 범위와 결과

| 항목 | 실측 결과 |
|---|---|
| 수령 팩 | 9개 |
| 프리팹 / 모델 파일 | 1,580개 / 1,478개 |
| 도감 원장 | 1,591항목 = 프리팹 1,580 + 프리팹에서 사용하지 않는 모델 파일 11 |
| 실제 기하 이미지 | 1,581항목 |
| 추가 시점 | 75항목 × 6방향 = 450 PNG |
| 시각 분류 | 1,525 `VISUALLY_REVIEWED`, 56 `UNCERTAIN` |
| 형상 확인 불가 | 2 `UNRESOLVED_SOURCE` — 원본 메시 누락 |
| 애니메이션 전용 파일 | 8 `ANIMATION_SOURCE_VERIFIED` — 정적 이미지 비대상 |
| 현재 존재하는 재질 | 1,426개(내장 재질 391 포함), 셰이더 상태 모두 `OK` |
| 현재 참조 누락 | Missing script 0 / Mesh 2 / Collider mesh 0 / Material slot 2 |
| 셰이더·재질 복구 | 재질 342개 변환 + 별도 셰이더 소스 39개 오류 수정 |
| 프리팹 참조 복구 | 5개 복구, 원본 부재로 3개 잔여 |

프리팹과 모델 파일은 서로 참조하는 집합이므로 합쳐서 모델 수로 세지 않는다. 기존 재질 1,426개의 셰이더가 정상이라는 결과와, 원본 자체가 없는 재질 슬롯 2개의 문제는 서로 다른 검사다. 원장과 [VisualReview.json](C:/Users/yj666/Oheangbu/Docs/Assets/VisualReview.json)의 index를 대조했고 모든 항목에 검토 결과가 있다. 시각 검토는 Codex가 실제 Unity 렌더 이미지를 확인한 결과이며, 사용자의 미술 승인을 대신하지 않는다.

## 팩별 분류와 사용 후보

| 팩 | 원장 항목 | 확인된 자산 계열과 장면 후보 |
|---|---:|---|
| BillemotdonggulLavaTubePack | 104 | 동굴 벽·천장·바닥·낙석·암반. 폐광과 소던전의 암반/갱도 조립 후보 |
| HwaseongForteressGate | 287 | 성문·문루·석축·여장·기와·목구조 부재. 관문·성벽·관영 구조물 후보 |
| HwaseongHaenggung | 549 | 행궁 전각·문채·문/창호·단청·마루·기단·조경. 관아·역참·황경 건축과 소형 부재 조립 후보 |
| JejumokGwana | 98 | 관아 건축과 지붕·담장·기둥 부재. 관아/목조 건축 조립 후보 |
| Korea_TreasureProps | 170 | 그릇·금속/도자 유물·불교/의례 장식. 사찰·서사 진열·의미 있는 보상 소품 후보 |
| KoreanTraditionalFestival | 85 | 농기구·벼/짚·직물·깃발·복식·생활 도구·가축. 벌목 마을 주변 생활 공간·농경/축제 흔적 후보 |
| KTinteractiveProp | 35 | 수납 가구·상자와 개폐 애니메이션 원본. 주막·객주 가구 후보, 실제 동작 검수는 후속 |
| SeyeonjeongPavilion | 228 | 누정 부재·수목/관목·초본/수생 식물·바위·석축. 주막 주변 조경·정자·수로·산길 후보 |
| YongmeoriCoast | 35 | 절벽·해안 암반·큰 바위. 대형 암벽 조형 후보, 야외 배치 전 LOD와 비용 검토 필요 |

검토 반영 대분류는 건축 부재 834, 건축 완성물 41, 건축 조립체 38, 유물 168, 식생 154, 암석·지형 148, 생활 소품 87, 표면 장식 61, 도구·무기 30, 복식 16, 애니메이션 8, 기타 6항목이다. 이는 소유 파일 수가 아니라 원장 항목 분류다. 후보 장소는 모델의 최종 배치 승인이 아니며 크기·시대·동선·의미색과 실제 장면을 대조해 선택한다.

시각 교정 예시: `SM_W_Mountain`은 산이 아닌 석재 벽판, `SM_GateGuardPost`는 문루 완성물이 아닌 석축 플랫폼, `SM_PinusDensiflora_3`은 독립 수목이 아닌 잎·가지 묶음이다. `SM_StoneBridge_2`는 흙을 채운 석축 교대, `SM_Stonewall_7`은 원형 화단/기단으로 분류했다. 파일명만 믿고 배치하면 생기는 오분류를 검토 반영 원장에 교정했다.

## 복구 내용과 남은 원본 문제

재질 342개는 Standard 209개와 Billemotdonggul의 Unreal 계열 133개를 현재 URP에서 표시되도록 변환했다. 원본 텍스처·노멀·마스크·투명/컷아웃 관련 의미와 매핑을 [MaterialRepairs.json](C:/Users/yj666/Oheangbu/Docs/Assets/MaterialRepairs.json)에 기록했다. Billemotdonggul의 커스텀 마스크 계산은 해당 소스의 식을 따라 처리했다. YongmeoriCoast의 셰이더 소스 39개는 중복 키워드 선언 오류를 수정했으며, 정상 URP 재질을 추가로 일괄 변환한 수에 포함하지 않는다. 수령 팩 전체를 수묵 셰이더로 덮어쓰지 않았다.

프리팹 5개는 실제 FBX 노드·이름표와 같은 원본 내장 자산을 근거로 참조를 복구했다. 두 농기구/생활 소품은 원본에 존재하는 LOD0와 Collider를 연결하고 유실된 가상 LOD 참조를 정리했다. 세연정 기둥 1개와 지형 2개는 참조 연결을 복구했지만 원본 Unreal WorldGrid/지형 레이어의 외형까지 복원한 것은 아니다. 자세한 근거·백업 경로·한계는 [PrefabReferenceRepairs.json](C:/Users/yj666/Oheangbu/Docs/Assets/PrefabReferenceRepairs.json)과 [참조 복구 설명](C:/Users/yj666/Oheangbu/Docs/Assets/PrefabReferenceRepairNotes.md)에 있다.

수정 전 백업은 [Art/AssetIntakeBackups/20260907](C:/Users/yj666/Oheangbu/Art/AssetIntakeBackups/20260907)에 보존했다. 수령 vendor 팩은 기존 Git ignore 대상이라 팩 내부의 복구 수정이 자동으로 Git 추적되는 것은 아니다. 기존 ignore를 강제로 바꾸거나 `force-add`하지 않았다. 다른 작업 환경으로 옮길 때는 복구 원장·백업과 실제 팩 파일도 함께 확인해야 한다.

| index | 원본 | 잔여 문제와 처리 |
|---:|---|---|
| 165 | `HwaseongForteressGate/Prefabs/SM_Bastion_Parapet.prefab` | WorldGridMaterial 원본이 없어 재질 슬롯 1개가 누락됐다. 석조 여장 형상은 시각 확인했지만 온전한 외형 복구 완료로 세지 않는다 |
| 876 | `HwaseongHaenggung/Prefabs/SM_SkySphere.prefab` | 원본 메시와 재질이 없다. 임의 구체로 대체하지 않고 하늘 유틸리티의 원본 누락으로 기록, 배치 제외 |
| 1317 | `SeyeonjeongPavilion/Prefabs/Plane.prefab` | 수면용 원본 메시가 없다. 다른 팩의 Plane을 같은 원본으로 간주하지 않고 배치 제외 |

56개 `UNCERTAIN`은 실제 렌더를 본 뒤에도 세부 기능·접합 위치·소스 외형·장면 역할 등을 확정하지 못한 항목이다. 모두 렌더 실패라는 뜻은 아니다. 해당 모델의 메모를 읽고 필요한 용도/시점/원본 정보를 보강한 뒤 배치한다. 자동 분류 원장이나 첫 시트의 이름으로 이 보류를 덮어쓰지 않는다.

## 애니메이션 원본 검사

애니메이션 전용 소스 8개와 각각 짝을 이루는 기하/애니메이션 소스, 총 FBX 16개를 Unity의 실제 임포트된 클립으로 검사했다. 8/8 쌍에 양의 재생 길이와 커브 데이터가 존재했고 임포트 데이터 문제는 0건이었다. 정적 메시가 없는 Open/Hide 파일의 빈 이미지는 정상이다.

`SK_HalfChest` 쌍은 설정 take 이름(`Unreal Take`)과 기본 take 이름(`Take 001`)이 다른 점을 기록했다. 실제로 임포트된 클립이 있어 이름 차이만으로 자동 수정하지 않았다. **Animator 재생·씬 인스턴스 바인딩·이벤트 실행·눈으로 본 동작 검수는 하지 않았다.** `ANIMATION_SOURCE_VERIFIED`는 원본 데이터 확인을 뜻한다. 근거는 [AnimationSources.json](C:/Users/yj666/Oheangbu/Docs/Assets/AnimationSources.json)이다.

## 씬에 넣기 전에 확인할 것

최고 상세 LOD가 250,000 tris를 넘는 항목은 116개다. 최댓값은 #1558 `SM_YMC_Cliff03A`의 4,999,926 tris이고 해당 항목의 LOD 상세 목록은 없다. #1560은 4,997,412 tris, #457 지형판은 3,834,054 tris다. 도감의 250,000 기준은 검토 대상을 찾는 표시이며 제작 예산이나 불합격 상한이 아니다. 실제 가시 거리·화면 점유율·동시 배치량에 맞춰 LOD/단순화/콜라이더를 준비하고 비용을 측정한다. 폴리 수가 많다는 이유만으로 원경에 그대로 넣지 않는다.

애니메이션 전용 파일을 제외한 1,134항목은 Collider가 없다. 장식 전용 원경에는 정상일 수 있으나 플레이어가 접근하는 바위·건물·가구는 접지·통과 가능성·이음새·문 개폐 공간을 장면에서 검수해야 한다. 이번 자산 감사는 1,591항목의 실제 보행/물리 테스트를 수행한 결과가 아니다.

## 정본 월드의 원경·암반 개선

`C2_CodexWorld`에 더 짙은 산4계층 농도와 바위/산의 부드러운 기하·공유 법선을 적용하고 저장했다. 소유 메시22개, 산 재질4개와 바위 재질2개를 갱신했으며 기존 배치와 C1 씬은 보존했다. 고정5컷을 모두 다시 확인해 원경 농도가 짙어지고 면 경계가 크게 줄어든 것을 관찰했다. 전경 블러/AA로 감춘 결과가 아니다.

씬의 Missing 참조/script0, 경로 높이111점과 Play 이동421/421경유점 검사 통과, 접지422표본 누락0, 최종 최근5분 Error/Exception0이다. 전체 인스턴스 삼각형은507,480→999,444로 늘었다. 실제FPS·빌드성능은 측정하지 않았고 예준의 최종 미술PASS는 별도다. [입구](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut2_threshold.png)·[야외](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut3_valley.png)·[상부 산길](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/cut5_upper_path.png) 캡처와 상세 [검증 기록](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-DEV-CODEX-WORLD.md)을 남겼다.

이 결과는 기존 C2 소유 자산 개선을 검증한 것이다. 수령 모델1,591항목을 C2에 모두 배치하거나 물리 검증한 결과로 사용하지 않는다.

## 이후 작업 절차

검토 반영 원장과 도감에서 필요한 역할의 기존 자산을 먼저 선택하고, 프로젝트 소유 씬용 변형에서 크기·피벗·충돌·LOD·수묵 재질을 조정한다. 실제 공백이 남을 때만 Meshy 생성 → Blender MCP 정리 → Unity 임포트·같은 전수 검사 절차를 따른다. 이번 수령 에셋 분류/복구 과정에서 신규 Meshy 모델을 생성하거나 Blender MCP 모델링을 수행했다고 기록하지 않는다.

갱신 시 Unity 원장 감사 및 시각 검토 기록을 저장한 다음 `python Tools/Build-AssetCatalog.py`를 실행한다. 도감과 검토 반영 JSON/CSV가 생성되며 자동 분류 원장은 유지된다. 검토 연결은 GUID를 우선 사용하고, GUID가 없는 과거 기록만 경로 일치를 허용한다. 식별 정보 없이 index만 있는 기록이나 중복 식별자는 오류로 처리한다. 새 에셋 삽입으로 index가 이동하는 경우에도 현재 1,591개 검토가 올바른 모델에 유지되는 것을 확인했으며, 인덱스 이동·경로 변경·GUID 불일치 등 15개 검증을 통과했다.

최종 생성 결과는1,591항목/1,581 PNG/원본 메시 누락2/애니메이션8, 검토 index 고아0, 시트25장이다. 검토 반영 JSON/CSV 파일 생성을 확인했다. 실제 브라우저에서 한글 UI, `SM_R_Sign_4` 검색2건, 모달 검토 메모·6방향 선택/확대, 애니메이션 필터8건과 이미지 비대상 안내의 동작을 확인했다. 이 검증은 도감 사용성에 관한 것이며 에셋 미술 판정과 구분한다.
