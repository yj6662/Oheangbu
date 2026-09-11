# Runtime VFX 재질 증가의 소유권 검토 — 2026-09-09

**관측된 신규 재질 1개의 Editor TextCore 폰트 아틀라스·fallback 캐시 참조는 실제 ID로 확인됐다. 누수 없음 판정은 하지 않는다.** 기존 RuntimeAudit의 증가 수치와 `COMPLETED_WITH_FINDINGS`를 그대로 유지한다. 출처 검사 전체 상태는 검사 중 자원 집합이 달라져 미검증이다.

## 검토 자료

- [실제 RuntimeAudit](C:/Users/yj666/Oheangbu/Art/SpellVFX120/runtime_audit.json)
- [종료 직후 출처 스냅샷](C:/Users/yj666/Oheangbu/Art/SpellVFX120/resource_attribution.json), 촬영 UTC `2026-09-09T02:16:52.9073227Z`, Unity `6000.3.9f1`, Play frame `14654`
- RuntimeAudit SHA256: `0d83dfef91adce60cf5acfc9a15a80e4bb1a8dea834848d036d780aed3e38f4b`
- 출처 스냅샷 SHA256: `de1334493f1561b921f656278aa16f35fbefccbabd93712bfb7b5ae4407bbee6`

JSON 파일은 후속 실행에서 덮어쓸 수 있으므로 이 문서의 수치·ID는 위 해시의 실행에 한정된다. Instance ID는 현재 Unity 프로세스의 식별값이며 영구 에셋 GUID가 아니다.

## 관측된 소유권

검사는 이름으로 후보를 고른 것이 아니라 `newResources`에서 얻은 **명시적 Material ID `-1428682`**를 전달했다. 다음 연결이 두 보고서와 일치한다.

| 대상 | 실제 관측값 | 의미 |
|---|---|---|
| 신규 Material | ID `-1428682`, `MALGUN Atlas Material + MALGUN Atlas 1`, nonpersistent, `HideAndDontSave` | RuntimeAudit에서 증가한 바로 그 객체 |
| 셰이더 | `Hidden/TextCore/Editor/Distance Field SSD` | Editor TextCore 폰트용 셰이더를 사용 |
| 재질 `_MainTex` | Texture2D ID `-1250248`, `MALGUN Atlas 1`, 1024×1024, nonpersistent | 아래 폰트의 atlas index 1과 같은 객체 |
| 소유 폰트 아틀라스 | FontAsset ID `14624`, `Malgun Gothic SDF`, `UnityEngine.TextCore.Text.FontAsset` | 경로 `Library/unity editor resources`, persistent |
| 폰트 내부 설정 | family `Malgun Gothic`, style `Regular`, `DynamicOS`, `IsEditorFont=True`, `InternalDynamicOS=True` | 이름 추정과 별개로 기존 필드에서 Editor 폰트라는 정보를 읽음 |
| 폰트의 기본 Material | ID `14636`, `MALGUN Atlas Material`, persistent | 신규 fallback 자체를 직접 가리키지는 않음. `directMaterialReference=false` |
| TextCore 캐시 | `UnityEngine.TextCore.Text.MaterialManager.s_FallbackMaterials` | 조회 성공, 항목 1개·매칭 1개 |
| 캐시 값 | `fallbackMaterial=-1428682`, `sourceMaterialId=14636`, `textureId=-1250248` | 신규 재질·기본 재질·atlas가 모두 동일 ID로 연결됨 |
| 캐시 키 | `62865435061304` | `(14636 << 32) \| uint(-1250248)`와 일치 |

따라서 이 스냅샷의 구체적인 관찰은 **“Editor FontAsset의 두 번째 atlas를 사용하는 신규 재질을 TextCore fallback 캐시가 참조하고 있다”**다. 폰트 이름이 비슷해서 Editor 재질로 추정한 것이 아니다. 보고서의 `EDITOR_FONT_ATLAS_AND_FALLBACK_CACHE_ID_MATCH`와 일치한다.

검사한 Renderer 142개의 `sharedMaterials`와 TMP 기존 필드에서 이 재질의 consumer는 기록되지 않았다. TMP FontAsset·TMP component 로드 수는 각각 0이며, TMP 캐시는 기존 Canvas callback 초기화 증거가 없어 **읽지 않고 미검증**으로 남았다. `consumers=[]`는 네이티브 Editor UI·UI Toolkit까지 포함해 참조가 전혀 없다는 뜻이 아니다. TextCore 캐시 자체의 참조는 이미 기록돼 있다.

## 원래 측정값과 검사 중 변화

| 구분 | 시작 → 종료 | 판정 |
|---|---|---|
| RuntimeAudit의 nonpersistent Material | 3723 → 3724 | 증가 1개, ID `-1428682` |
| RuntimeAudit의 nonpersistent Mesh | 431 → 431 | 이 관측 창에서 증가 0개 |
| 실제 120개 수명 검사 | 완료 120, 실패 0, 오류 0 | `PASS_120_REAL_UPDATE_DESTRUCTION` |
| 기존 리소스 판정 | `GROWTH_OBSERVED_REVIEW_EDITOR_INTERFERENCE_OR_LEAK` | 변경하지 않음 |
| 출처 검사 중 추가 관측 | Material 29개, 모두 persistent | Editor 내장 4개, KTP 기존 에셋 25개. 사라진 ID 0개 |
| 출처 검사 전체 상태 | `RESOURCE_SET_CHANGED_DURING_INSPECTION_ATTRIBUTION_UNVERIFIED` | `resourceSetUnchangedByInspection=false`, `leakPass=false` |

추가 29개의 경로는 `Library/unity editor resources` 또는 `Assets/KoreanTraditionalPattern_Effect/...`다. 이들은 **출처 검사 구간에 처음 열거된 persistent 자원**이며 원래 RuntimeAudit가 센 nonpersistent +1과 동일한 항목들이 아니다. 로그만으로 각 에셋이 어떤 조회 때문에 로드됐는지까지 확정하지 않는다. 이를 신규 VFX 실행 중 생성된 동적 재질 29개로 해석해서도 안 된다.

출처 검사에는 약 178.55ms가 걸렸고 기존 측정·판정 확정 후 호출됐다. 이 시간을 VFX 정상 프레임 비용에 섞지 않는다. 전체 검사가 자원 집합 불변을 입증하지 못했으므로, 개별 ID 연결 관찰을 보존하되 검사 전체를 PASS로 바꾸지 않는다.

## 누수 판정의 한계

- 이 기록은 현재 **참조하는 대상**을 보여준다. `RuntimeAudit_한글_번호` 이름을 표시한 Hierarchy, Review GUI 또는 다른 Editor 창 중 무엇이 atlas 확장을 촉발했는지는 입증하지 않았다.
- font atlas index 1이 언제 생겼는지, fallback이 언제 최초 생성됐는지, 정상적으로 제한된 캐시인지, 같은 조건을 반복하면 끝없이 늘어나는지는 이 스냅샷만으로 판정하지 않는다.
- TextCore 캐시 reference count는 보고서에서 `-1`로서 **사용 가능한 값이 없다는 표시**다. 음수 참조 수나 해제 실패를 관측한 값이 아니다.
- 신규 재질의 소유권이 Editor 폰트라는 이유만으로 원래 증가를 삭제·무시하거나 `PASS_NO_NATIVE_MATERIAL_MESH_GROWTH_AFTER_WARMUP`로 바꾸지 않는다. 다른 VFX 자원의 누수 부재까지 입증한 것도 아니다.
- 실제 빌드·독립 실행 환경, 장시간 반복, 다른 씬·폰트·언어, UI 내부의 최종 사용자와 해제 정책은 미검증이다.

이번 작업은 저장된 보고서 두 개의 ID·키·수치 확인과 문서 작성만 수행했다. Unity 실행, 소스/검사 도구 변경, 캐시 정리, 추가 측정은 하지 않았다.
