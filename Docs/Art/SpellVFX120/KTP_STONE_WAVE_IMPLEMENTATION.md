# 막·뭄·오 — 원본 자산을 사용한 본체 수정

2026-09-09. **막과 뭄은 보유 석재 모델로 교체했고, 오는 기존 곡면 물마루에 실제 KTP 수파 문양을 연결했다.** 19차 양 시점 30장과 영상 원본 36장을 검토했고, 별도 실제 입력 재생에서 등록 술식 15종의 인식·시전·VFX 종료를 관측했다. 전체 미술 완성이나 실제 적 피해 검증을 뜻하지 않는다. 경로는 저장소 루트 기준이다.

## 실제 빌드와 원본 보존

[stone_body_build.json](../../../Art/SpellVFX120/stone_body_build.json)의 실행 시각은 **09:21:12 UTC**이며, 상태는 `BUILT_STRUCTURAL_CHECKS_ONLY_VISUAL_RUNTIME_UNVERIFIED`, `sourceFilesUnchanged=true`다. 원본 프리팹·FBX·재질·텍스처와 각 `.meta`의 SHA-256 전후값, 프로필 전후 JSON, 실제 정점 bounds와 변환 행렬을 남겼다.

| 술식 | 사용 원형과 LOD | Unity 정점 / 실제 삼각형 | 파생 메시의 최종 배치 크기 X×Y×Z |
|---|---|---:|---|
| 막 `050_B9C9` | `SM_Rock_K.fbx` / `SM_Rock_K_LOD2` | **704 / 1,006** | **1.45×1.85×0.85m**, 1개 |
| 뭄 `070_BB44` | `SM_StoneBridge_1.fbx` / `SM_StoneBridge_1_LOD3` | **2,892 / 3,375** | **1.65×0.65×4.70m**, 1개 |

두 모델의 원본은 `Oheangbu/Assets/SeyeonjeongPavilion/Mesh/Rock/`에 있고, 선택은 같은 팩 `Prefabs/`의 해당 MeshFilter에서 수행했다. 모두 1 submesh, 스키닝·바인드포즈 없음, UV0와 UV1은 각각 2차원이다. 원본의 UV와 노멀, 삼각형 연결을 유지했다. **원본 탄젠트는 둘 다 0개**였으므로 첫 시도의 사전검사가 중단됐다. 수정한 빌더는 파생 메시의 UV·노멀·인덱스에서만 탄젠트를 계산했고, 각각 704개·2,892개가 생성됐음을 기록했다. 원본 탄젠트를 보존했다고 표현하지 않는다.

파생 메시의 중심은 수치 오차 범위에서 0, 각 축 min/max는 −0.5/+0.5다. 석교의 원래 장축 +X는 −90° Y 회전으로 +Z에 맞췄다. **축별 정규화 후 서로 다른 PartScale을 적용하므로 원본 비율은 의도적으로 달라졌다.** 노멀은 역전치로 변환하며, UV를 새로 만들거나 석교 아틀라스를 임의의 판에 붙이지 않았다. `Count=1`, `Size=1`로 전체 석교를 한 번 사용한다.

출력은 `Oheangbu/Assets/_Project/Art/SpellVFX120/StoneBodies/{Meshes,Materials}/`이다. `MI_Rock_K`, `MI_StoneBridge_1`의 실제 BaseMap·NormalMap을 공유 파생 재질에 연결했다. 셰이더는 `Oheangbu/VFX120/BotanicalSurface`, TintStrength=0, Saturation=0.6, 프로필 Pigment=(0.75,0.72,0.66)이다. 해석되지 않은 원본 AO는 복사하지 않았다. 원본 임포터·메시·재질을 수정하거나 런타임마다 재질을 복제하지 않는다.

## 배치·등장·소멸 표현

**막**은 바위 아래쪽 bounds를 기준으로 착지 위치를 계산하고 하단을 지면에 **0.16m** 넣는다. 진입 표현 이후 소멸 구간에는 완성 크기를 유지하고 `_Visibility`로 사라지게 했다. 19차 양 시점에서 18차의 중심 축소·재부유 인상이 해소됐고 밑단 가까운 투영 그림자를 확인했다.

**뭄**은 한 개의 석교를 완성 크기로 유지한다. 아래쪽을 시전자 쪽 지면 +0.015m에 맞추고, 가까운 끝은 전방 0.45m에서 시작한다. `_Formation=SmoothStep(0,1,Age/0.95)`로 local Z −0.5→+0.5 방향을 드러낸다. 이는 표현상의 형성 진행이며, 실제 통행·양안 연결 판정이나 지속 다리의 게임 상태를 새로 만든 것이 아니다.

두 돌 본체는 KTP 발동 크기를 `NativeScale=0.32`, 타격 크기의 추가 배율을 `NativeImpactScale=0.45`로 낮췄다. `BotanicalSurface`의 새 ShadowCaster는 색상 패스와 같은 지면·Visibility·Formation 클리핑을 사용한다. 아직 나타나지 않은 부분이나 사라진 돌 전체의 그림자가 남지 않도록 한 구현이다. 렌더러의 그림자 켜기는 **막·뭄의 Body_ 본체에만** 적용하며 기존 식물 본체의 그림자 Off 설정은 유지한다. 19차에서 두 돌의 실제 그림자를 확인했지만 뭄의 받침 아래가 약간 떠 보이는 인상은 남는다.

**오 `109_C624`**는 기존 WaveCrest 곡면과 KTP 발동·접촉 원형을 유지한다. 파생 `Materials/M_Body_KTP_WaterWave.mat`에 실제 `KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_204.png`를 연결하고 `_WaterMotif=1`로 수파의 **alpha를 물마루 거품에 사용**한다. RGB 흰색을 불투명 사각 판으로 사용하는 방식이 아니다. PartScale.y는 **3.1**이며, 이는 프로필 배율이지 실제 물마루 높이 3.1m를 측정했다는 뜻은 아니다. 발동 `NativeScale=0.30`, 타격 추가 배율 `0.36`으로 본체보다 앞서 보이던 공통 효과를 줄였다. [water_wave_tuning16.json](../../../Art/SpellVFX120/water_wave_tuning16.json)에 전후 설정이 있다.

이 변경은 Presentation·Editor·셰이더 범위다. 입력 수집, 인식, 게임 피해·충돌체, 원래의 Flight/Duration·AreaPlan 시계는 변경하지 않았다. 0.95초 형성과 디더 소멸을 게임 판정의 새 시간으로 사용하지 않는다.

## 검증 결과와 남은 경계

| 항목 | 확인 상태 |
|---|---|
| 두 LOD의 실제 삼각형·UV/노멀 개수, 파생 탄젠트·native mesh buffer, 원본 파일 해시 | **통과 — 실제 Build 수치** |
| 최신 120개 카탈로그 Edit Preview 계약 | **범위 한정 통과**: 120/120, 오류 0, sourceUnchanged/cleanupObserved=true |
| 18차 실제 화면 | **개선과 잔여 미달을 기록**. 그림자·소멸 크기 최종 수정 전 결과 |
| 후속 19차 정지 화면·영상 표본 | **부분 개선 확인**. 30장·영상 원본 36장: 막 재부유 해소·두 돌 그림자·뭄 순차 형성 확인. 뭄 부유감·오 물막 양감 미달 잔존 |
| Life 이후 영상 종료 | **6개 최종 원본에서 본체·그림자 없음**. 시작/마지막 RGB 차이 0. 전체 디코드·시간 검사도 6/6 통과 |
| 등록 15종 실제 입력·게임 시전 경로 | **범위 한정 통과**. 가상 장치로 실제 획 수집→인식→시전→VFX 종료 15/15. 실행 MVID는 보고서에 없어 버전 결합 미검증 |
| 실제 엄폐·다리 통행·충돌·피해, 전체 성능 | **미검증**, 이번 모델 제작만으로 보장하지 않음 |

최신 [traditional_catalog_audit.json](../../../Art/SpellVFX120/traditional_catalog_audit.json)은 **09:37:18 UTC**, App MVID `5710c3e1-f131-449f-af21-e14aaa10eeb9`, `PASS_ASSIGNED_EDIT_PREVIEW_CONTRACTS_ONLY`다. 공급한 진단 시계로 검사한 계약 결과이며 보고서 자체도 runtimeUpdate·미술·성능은 UNVERIFIED로 구분한다. [19 미술·영상 보고서](../../../Art/SpellVFX120/KTP_STONE_WAVE_REVIEW19.md)의 6개 영상은 C2 Editor 진단 촬영이며 실제 작도 플레이 녹화와 구분한다.

[15종 실제 입력 집계](../../../Art/SpellVFX120/player_input_batches16.json)는 **09:38:16–09:44:17 UTC**, 일반 C2 Play 진입 3회로 측정했다. 가·나·마·사·아, 고·노·모·소·오, 거·너·머·서·어를 각각 5개씩 기존 XML 획과 가상 장치로 입력했다. 인식 결과나 먹을 주입하지 않았다. 배치별 기존 먹은 1→약0.25, 1→약0.25, 1→약0.5로 소비됐고, 3개 모두 입력·효과 정리를 확인했다. 대상 0이므로 실제 적 피해·패링 성공은 미검증이다. 막·뭄은 등록 게임 15종에 속하지 않으므로 이 입력 결과를 두 효과의 게임 기능 검증으로 확대하지 않는다. 실행 MVID가 기록되지 않아 19 촬영 MVID와 같은 실행본이었다는 직접 증거도 없다.

앞선 [source_memory_attempt18](../../../Art/SpellVFX120/traditional_catalog_audit_source_memory_attempt18.json)은 행별 120개가 통과했지만 전체 `FAIL`, `sourceUnchanged=false`였고 그대로 보존했다. 기록된 디스크 해시·의존성 해시·dirty 상태에는 변화가 없었으나 당시 메모리 비교의 불일치 원인은 분류되지 않았다. 이후 `SourceMemoryAfter` 진단을 추가한 재검사에서 불일치가 재현되지 않았다는 사실은 **첫 실패의 원인이 해결됐거나 무해함을 입증하지 않는다**. [18 미술 보고서](../../../Art/SpellVFX120/KTP_STONE_WAVE_REVIEW18.md) 역시 후속 19 결과로 덮어쓰지 않는다.

## 물마루 20 형상 실험 — 반려·19 복원

한 겹 물막을 닫힌 비대칭 단면으로 바꾸는 후속 실험도 실제 Unity에서 생성했다. 후보는 **2,305정점 / 4,512삼각형**, 1 submesh, 열린 경계·비다양체·퇴화 삼각형 0이며, 배치 크기 3.5×1.429×1.35m다. [실측](../../../Art/SpellVFX120/water_roll20_build.json)과 [원본 10장 비교](../../../Art/SpellVFX120/KTP_WATER_ROLL_REVIEW20.md)를 남겼다. 양감은 커졌지만 짙은 가로 띠·둥근 차양 외곽·늘어난 문양 때문에 물보다 천막이나 방패처럼 보여 **반려**했다.

[10:07:52 UTC 복원](../../../Art/SpellVFX120/water_roll20_restore.json)에서 오의 BodyMesh·BodyMaterial·PartScale만 19 값으로 되돌리고 원본 해시·나머지 프로필 불변을 확인했다. 20 영상은 만들지 않았고 검수 페이지도 19를 유지한다. 재현용 빌더·셰이더는 `Tools/SpellVFX120/Experiments/Vfx120WaterRollBuilder20.cs.txt`, `InkPigmentWaterRoll20.shader.txt`로 보존했으며 활성 빌더·큐 명령·실험 셰이더 분기는 제거했다. 후보 메시·재질은 미연결 증거로만 남긴다.
