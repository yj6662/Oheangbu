# SPEC-PLAYER-DOSA — 도사 플레이어와 전신 작도

| 항목 | 값 |
|---|---|
| State | LEGACY |
| Lifecycle | ARCHIVED |
| Updated | 2026-09-08 |
| Implementation | 사용자 요청으로 플레이 연결 롤백, 캡슐 초기화 |

2026-09-08 #184: 사용자가 기존 캐릭터 작업을 롤백하고 새 모델로 시작하도록 지시했다. C2·공용 PlayerRig에서 V1/V2 표현 연결을 제거하고 원래 Body 캡슐을 복구했다. 아래 제작·검수 수치는 폐기된 시도의 이력이며 새 모델의 기본값·승인·검수 결과로 승계하지 않는다. 원본과 제작 코드는 참고용으로 보존하지만 플레이에서 실행하지 않는다. 이동·카메라·작도 게임 기능은 유지한다.

## 1. 근거와 목표

예준 승인 계획의 구현 Spec. V1 결정은 #176, 2026-09-08 승인한 V2 재구축·천 시뮬레이션·리깅 선행 검수는 #177이다. 근거: PROD-RIG/PROD-PIPELINE/PROD-AIASSET, ART-COLOR/ART-INK/ART-SILHOUETTE, SPELL-RECOGNITION, COMBAT-DEFENSE, SPEC-DRAWING-INPUT, SPEC-COMBAT-CORE-LOOP §12.

현재 작업은 Model7 기반 도사 V2의 메시·리그·옷 움직임 재구축이다. 사용자가 선택한 **Unity 기본 Cloth + 직접 작성한 보조 본**을 사용하며 유료 천 플러그인은 도입하지 않는다. 초판의 천 시뮬레이션 제외는 V2에서 해제한다. 1인칭·숄더뷰, 입력·전투 규칙과 절제된 실시간 전신 작도는 유지하며 얼굴 표정은 이번 범위에 추가하지 않는다. V2 승격 전까지 기존 C2·공유 PlayerRig를 정본으로 유지하고, V1 결과는 §5의 이력으로 보존한다.

## 2. 자산 계약

폴리곤 절반 감축(2026-09-08 사용자 요청): 현재 승인 외형의 별도 파생본에서 전신 약37k·근접 팔 약13k삼각형을 목표로 한다. Blender의 부분별 감축과 손 파지 보호를 사용한다. 세계 Cloth3개의 기존 메시/고정점과 원본 인덱스를 쓰는 충돌·부착점 자료는 유지하고, 감축된 표시 메시를 기존 골격에 연결한다. 낮춘 손에도 동일 이름의 파지 보정 블렌드셰이프를 전달한다. 원본 Blender/FBX·기존 손/천/프리팹을 보존하며 새 폴리곤 수·웨이트·바인드 기준·실제 플레이 화면을 확인한다. 이는 자동/부분 감축이며 전체 수작업 쿼드 리토폴로지 완료로 부르지 않는다.

폴리곤 감축 #182 적용 완료: 전신74,225→37,670삼각형(-49.25%), 근접 팔25,221→12,858(-49.02%). 전신 Cloth3개·기존 골격·물리 인덱스 원본·붓은 유지했다. 손은 접촉 부위를 보호해 감축하고 같은 보정 셰이프를 전달했다. C2·공유 PlayerRig의 플레이용 프리팹에 적용했으며 실제 이동·작도·숄더 복귀 COMPLETE/오류0/원시80점 변경0/붓끝 최대1.666px를 확인했다. 자동 부분 감축이며 수작업 쿼드 리토폴로지나 RIG_PASS가 아니다. FPS 교대 비교는 후반 하락을 포함해 일관되지 않아 지속 향상은 미확정이다. [감축 기록](C:/Users/yj666/Oheangbu/Art/PlayerV2/HalfPoly/README.md).

이전 이력 — 모델 구조 간소화 #181 적용 결과: 전신 활성 렌더러54→11, 비어 있지 않은 재질 그리기 단위66→11. 렌더링 파생 메시7개를 생성했으며 원본74,225삼각형·UV·본·독립 장식 피벗·Cloth3개·손/근접 팔/붓은 유지했다. 세 진단 자세222,660정점 비교의 최대 위치 차이는0.000000614m이고 기존27장식/7붓 충돌 비교도 통과했다. C2·공유 PlayerRig가 참조하는 플레이용 프리팹에 적용했으며 원본은 백업했다. 실제9초 이동/작도 확인은 오류0·원시85점 변경0·붓끝 최대0.000563px·숄더 복귀 확인이다. 안정된 교대 반복 구간은84.26→87.08fps였으며 첫 구간70→88fps는 워밍업/환경 변동을 포함하므로 지속 향상률로 주장하지 않는다. 이번 변경은 렌더링 구조 간소화이며 폴리곤 감축이나 저장 용량 절감이 아니다. [검증 보고서](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/render-batching-validation.json).

모델 구조 간소화(2026-09-08, 사용자 추가 요청): 전신의 같은 불투명 재질을 사용하는 비-Cloth 부품을 렌더링용 SkinnedMesh로 묶는다. 원본 FBX·UV·삼각형·골격과 독립 장식 피벗, 충돌/부착점 원본 인덱스는 보존하고 렌더링 파생 자산을 별도로 생성한다. Cloth와 블렌드셰이프가 있는 손, 근접 팔·붓은 제외한다. 원본 렌더러는 계산용 참조로 유지하되 표시/스키닝을 중복 실행하지 않는다. 합친 메시의 정지·관절 회전 후 정점 위치를 원본과 비교하고, C2 실제 이동/작도와 같은 세션 성능을 확인한 뒤 공용 플레이어 프리팹에 적용한다.

추가 최적화(2026-09-08): 숨긴 근접 팔은 컨트롤러 시간을 유지하면서 Humanoid 뼈 갱신을 생략한다. 최종 카메라에서 팔이 드러나면 같은 프레임에 자세를 평가한 뒤 IK를 적용한다. 단단한 붓 자루 반경을 별도로 재사용하고, 사용처가 없는 PC Opaque Texture 복사를 껐다. 해상도·그림자·SSAO·천/장식 빈도는 유지했다. [교대 비교](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/performance-remaining-comparison.json)는 이전82.88/89.43fps →88.33/92.59fps(약3.5~6.6%), 붓 프록시0.41~0.44ms →0.34~0.35ms. 회전·이동·균일/비균일 배율4표본 각3,622원본점의 반경 차이는 최대0.000000190m이고 근접 팔 즉시 복귀 검사도 통과했다. 실제9초 이동·작도 확인은 오류0/원시83점 변경0/붓끝 최대1.80px/숄더 복귀 확인이다. 에디터 동일 시점 측정이며 빌드나 지속 전투 성능 보장은 아니다. [상세 검증](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/performance-remaining-validation.json).

#180 후속 최적화: 동일 캡슐 접촉 결과를 유지하면서 경계 상자로 먼 충돌쌍을 제외하고, 본 행렬·신체 캡슐이 동일한 질의 결과를 재사용한다. 붓 내부 자세·소스·배율이 그대로이면 반경 피팅을 재사용하되 끝점은 매 프레임 추종한다. 이전 계산 경로와 같은 세션에서 번갈아 측정하고 접촉 결과·붓 충돌 반경을 비교한다. 물리 빈도와 외형 품질을 추가로 낮추지 않는다.

후속 실측: [동일 세션 교대 비교](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/performance-cache-comparison.json)에서 이전73.03/79.79fps → 최적화84.06/89.17fps, 장식 충돌1.75~1.87ms →0.69ms. 실제27부품의 접촉 깊이 차이 최대0.000000140m, 붓 루트47° 회전 후7캡슐의 반경 차이 최대0.00000000932m. 붓 자세가 매 프레임 갱신돼 붓 반경 캐시의 평균 비용 개선은 관측되지 않았으며, 개선의 주원인은 장식 충돌이다. [후속 실제 플레이 확인](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/playable-smoke-20260908_144657_575/summary.json)은 COMPLETE/오류0/원시 획85점 변경0/표본 붓끝 최대0.00118px. 검수 범위는 동일 에디터 시점·측정 자세의 접촉 비교·짧은 실제 입력이며 전체 빌드/장기 물리 검수가 아니다.

2026-09-08 성능 수정(#180): 플레이용 V2는 장식의 원본 삼각형 반복 충돌 대신 본을 따라가는37개 캡슐 충돌을 사용한다. 신체 충돌체도 최초 피팅 후 주요 본을 따라가며 매 프레임 메시를 다시 피팅하지 않는다. 정밀 원본 충돌은 기존 검수 경로에 보존한다. 보조 스프링60Hz·최대4서브스텝, 충돌 보정은 프레임당 한 차례(최대3반복), 천120Hz, 천 충돌체 선택20Hz를 별도 플레이용 SO에 둔다. 충돌체 자체는 매 프레임 본을 따라가고 이동·파지·작도 입력은 유지한다. 근사 충돌의 통과를 원본 삼각형 전수 검수 통과로 해석하지 않는다.

실행 확인: 같은 C2 에디터 시점에서 기존27.6fps/36.27ms → 변경 후65.3~74.8fps/13.38~15.32ms. 장식 충돌 포함 보조 구간17.80ms →1.84~1.96ms. [측정 전](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/performance-before.json), [측정 후](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/performance-after.json). 캡슐 피팅3072점 포함·퇴화 축 유한값 검사를 통과했고 [실제 플레이 확인](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosaV2/playable-smoke-20260908_143002_370/summary.json)은 이동·작도·숄더 복귀 COMPLETE, 오류0, 원시 획88점 변경0, 표본 붓끝 최대1.93px였다. 실제 이동/작도 외부 화면을 확인했으며 장기 천 안정성·전수 피부 관통·빌드FPS 검수로 확대하지 않는다. 근사 캡슐의 잔여 겹침은 그대로 보고하며 실제 메시 관통 수치로 해석하지 않는다.

- V1의 레거시 SM_DosaCourier_Rigged·대응2048 베이스 컬러와 구현 자산을 보존한다. V2의 Model7 원본·텍스처·실제 소스 경로·해시는 별도 제작 원장에 기록하고, 원본 UV와 맞지 않는 텍스처를 섞지 않는다. V2 작업 산출물은 `Art/PlayerV2/`에서 구분한다.
- Blender에서 Humanoid 축/단위/바닥 피벗(키 1.75 m 초기값), 어깨·무릎·도포 웨이트, 양손 손가락 본·파지를 보강한다. 전신과 같은 외형의 근접 팔 메시를 별도 내보낸다.
- Humanoid 주 골격과 손가락·옷/장비 보조 본을 직접 정리하고, 천 메시의 고정 영역·움직이는 영역·신체 충돌체를 명시한다. 자동 리깅 또는 Humanoid 인식 성공만으로 제작 리그를 통과시키지 않는다.
- RIG_PASS 이후 대기/전투 준비2, 걷기/달리기 각각 전후좌우8, 전후좌우 짧은 회피4와 작도 준비/유지/복귀를 제작·적용한다. 대각선은 혼합한다. 기존17모션을 V2에 재타게팅하는 것도 리깅 선행 게이트의 대상이다.
- V2 신규 Meshy 비용 상한은 **200크레딧**이며 V1의 실제35/45크레딧과 별도 집계한다. [V2 Meshy 원장](C:/Users/yj666/Oheangbu/Art/PlayerV2/MeshySources/ledger.json)의 캐릭터35+붓30+배낭30으로 **95/200크레딧 사용**, 계정 잔고921→826을 확인했다. 세 생성 작업은 SUCCEEDED이며 다운로드를 완료했다. 생성 성공은 리깅·천 검수 통과를 뜻하지 않는다. Meshy 모션 요청은 RIG_PASS 후에만 수행하며 실패·재시도도 비용 원장에 포함한다.
- 붓 치수와 축은 해당 버전의 실제 메시·본·소켓 원장을 따른다. `TipSocket +Y 0.28m / 자루 뒤쪽 -Y 0.14m`는 V1 초기값이며 새 V2 붓에 그대로 적용하지 않는다. V2는 실물 붓끝을 포함한 굽힘 검사를 수행하고 자루·본 길이를 임의로 늘려 화면 오차를 숨기지 않는다. 실제 자산/치수 출처=PlayerV2 README와 붓 제작 원장.

## 3. 런타임 계약

- #179: V2의 비작도 휴대 자세는 오른팔을 몸 옆으로 내리고 붓을 전방 아래로 기울인다. 이동 모션의 작은 흔들림을 유지하고 작도 자세와 부드럽게 전환한다. 파지는 손가락·엄지가 자루를 감싸는 닫힌 형태이며 캐릭터 바인드 기준으로 평가한다. 비작도 시 숄더뷰를 유지하고 작도 종료 시 숄더뷰로 복귀한다.

- CharacterController가 이동 단독 소유, Root Motion OFF. 실제 로컬 평면 속도→2D 이동 혼합. 기존 이동 속도/회피 거리/무적 시간/갈무리 수급은 변경하지 않는다.
- DrawingInputController의 기존 Point 액션을 단일 수집하고 현재 포인터·작도 상태를 읽기 전용 노출한다. Raw 좌표/시간/2px 샘플링/인식/필세를 표현에서 수정하지 않는다.
- App 어댑터가 읽기 상태를 Presentation에 전달한다. PlayerVisualProfileSO=이동·발 접촉, DrawingPoseProfileSO=파지·IK·몸통 참여·전환. Core/Drawing에서 Presentation으로 역의존하지 않는다.
- 입력 이벤트를 순서대로 처리하되 표현 투영은 최종 CameraRigController 포즈 이후 수행한다. 같은 프레임 start/point/end/commit을 누락하지 않으며 커밋 판정 시점은 지연하지 않는다.
- 붓끝 XY는 현재 화면 획 끝에 즉시 일치한다. 깊이·어깨 표현 앵커만 보정하며 기존 눈앞 1m 작도면과 입력 영역을 유지한다. 관성은 몸통/팔꿈치에 한정한다.
- 작은 획=손목·팔, 큰 획=어깨·척추·골반·반대팔 균형. 정지는 발을 유지하고 이동 중 하체 보행 위에 작도를 혼합한다. 플레이어 루트와 카메라를 리그가 움직이지 않는다.
- 작도 진입부터 근접 팔·붓을 표시하고 월드 전신은 ShadowsOnly. 근접 리그는 그림자를 중복 생성하지 않는다. 월드 전신 자세는 계속 갱신하며 외부 검수뷰에서 확인한다. 근접/월드 위치의 완전한 3D 일치는 보장하지 않고 화면 일치와 정상 관절을 우선한다.
- 획 사이 붓 들기/추종, 성공·불발·피격 중단·종료/재진입을 처리한다. 준비·복귀 애니메이션이 입력/시전을 막지 않는다. 피격은 글자만 중단하고 Q 작도 모드는 기존대로 유지한다.
- 최종 TipSocket을 갈무리 효과 앵커로 연결한다. 기존 떠 있는 BrushProp은 새 파지 붓으로 대체한다.
- V2의 Unity Cloth와 보조 본은 표시 계층에서만 옷/장비를 움직인다. 신체 스키닝·천 고정점·충돌체·보조 본의 역할을 데이터로 구분하고 같은 영역에 독립된 2차 운동을 중복 적용하지 않는다. 몸/붓 관통·과도한 늘어남·상태 복귀 시 폭주를 격리 씬에서 검사한다.

## 4. 적용과 검증

V2는 **모델·리그·정적 Cloth/장식 진단 → RIG_PASS(수치와 시각 모두) → 제작 애니메이션·동적 격리 검수 → C2_CodexWorld → 공유 PlayerRig** 순서다. RIG_PASS 전에는 제작용 애니메이션·재타게팅·Meshy 모션을 시작하지 않는다. 직접 본을 회전시키는 임시 리깅 검사와 정적 Unity 임포트, 천의 직접 포즈 정착·리셋 검사는 선행 단계에서 수행한다. 기존 C2와 공유 프리팹은 검수 결과가 준비되기 전 교체하지 않는다. 재생성 시 같은 승인 소스·설정·프리팹을 재사용한다.

검증 항목:

1. 1080p 붓끝-현재 획 끝 오차 ≤2px: 중앙/4모서리/변, 직선/원/급회전/다획.
2. 1인칭 ±80도·숄더 제한, 진입 첫 프레임, 카메라 블렌드·붐 수축·락온 회전·이동, 피격 후 재작도/종료. 30/60/120fps와 감속.
3. 파지 이탈·팔꿈치 뒤집힘·소매 관통·붓 신축·이중 팔/그림자 없음. 작은/큰 획의 몸통 참여 차이.
4. 방향별 걷기/달리기/회피·정지/재출발·C2 경사/주막 계단. 평지 지지 구간 발 미끄러짐 ≤10cm, 지면 관통 ≤2cm 초기 기준.
5. 동일 입력 재생에서 표현 on/off와 무관하게 Raw/인식/시전 시점 동일. 갈무리/회피/장면 전환 회귀.
6. 실제 플레이뷰와 외부 전신 비교 영상, 수정 .blend/FBX/모션/프리팹, 출처/크레딧 기록 제공.

## 5. V1 구현·검증 이력 — V2 승격 전 현재 정본

2026-09-08 기술 구현과 영상 전달 검증을 완료했다. 최종 모델을 C2와 공유 PlayerRig에 적용하고 Unity 재시작 후 리그·발 검사와 실제 입력 Full·주막 왕복 캡처를 수행했다. 실제 손가락 파지와 전신 작도 자세도 확인했다. **State는 후속 아트 조정·사용자 외형 판정을 위해 TEST로 유지한다.**

- 최종 전신48,166tris·근접 팔19,834tris, 양쪽 Humanoid·56본·17모션. 잘못 연결된 스캔 면469개 제거, 정점1,166개 웨이트 교정, 원본 옷감 UV의 안감480정점/864tris 추가를 반영했다. 기존 정점 위치·남은 원본 면 UV/법선·손/발·본 rest·action·17모션 FBX SHA를 보존했다. [변경 원장](C:/Users/yj666/Oheangbu/Art/Player/waist-sleeve-separation-report.json), [export 검증](C:/Users/yj666/Oheangbu/Art/Player/waist-sleeve-export-validation.json).
- 원본 Meshy 소스를 보존하고 기존 외형 리깅·모션에35/45크레딧을 사용했다(잔고956→921). [출처/비용](C:/Users/yj666/Oheangbu/Art/Player/PLAYER_ASSET_PROVENANCE.json), [자산 인계](C:/Users/yj666/Oheangbu/Art/Player/PLAYER_ASSET_HANDOFF.md).
- 실제 CC 속도·작도 상태를 읽어 전신과 근접 팔을 평가한다. 최종 카메라 뒤 획 투영→골격/소켓→갈무리 끝점 순서로 처리하며, Raw 좌표·시간·2px 샘플링·인식·이동/회피 규칙은 유지한다. 근접 팔은 URP overlay, 전신은 작도 중 ShadowsOnly다. 표시 자식만 skinWidth에 맞춰 내리고 CC 형상은 유지한다.
- 손가락별 관절각과 엄지 opposition으로 파지를 보정했다. 카메라 스택·driver·갈무리 TipSocket 참조를 공유 리그에 연결했고, 씬 전환/재시작 시 새 인스턴스 참조를 검증했다.

| 검사 | 최종 확보 결과 | 근거와 범위 |
|---|---|---|
| 리그·붓끝 | 2,250/2,250 PASS, 17클립·85표본 자세. 팁 최대0.03739288px·GripSocket0m·뼈 길이 변화0.0000034m | [rig-validation.json](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/rig-validation.json). 최종 모델·파지 보정 및 Unity 재시작 후 재검사 |
| C2 실제 입력 Full | COMPLETE·복원=true, 460프레임/460쌍 PNG·30fps. 작도113프레임, 팁 최대0.00193664px·근접 GripSocket0m·월드 GripSocket 최대0.000000677m, Raw102변조0·인식5차이0·커밋5·중단1 | [최종 Full summary](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/20260908_003745_268_full_visuals/summary.json), [프레임 계측](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/20260908_003745_268_full_visuals/frames.jsonl). 팁2px 초과0·도달 불가0. 실제 입력과 근접/외부 전신 캡처 확인 |
| 발 접지 | 48/48 PASS, 최대 미끄럼0.05028208m·관통0.01726837m·접촉 간격0.02939130m | [foot-validation.json](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/foot-validation.json). 걷기1.6m/s·달리기4.5m/s×8방향×30/60/120Hz. 기존 한계0.10/0.02/0.03m 유지, 접촉 정점 전환 변위도 포함 |
| 피부·붓대 접촉 | 근접/전신 down/up4표본의 관통 삼각형0. down 손가락 간격0.49206~0.88211mm; up 엄지·검지·중지 근접 접촉 유지, 약지·소지5.06839~5.44960mm 이격 | [접촉 원장](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/grip-contact-validation.json), [down 컷](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/Grip/calibrated_pen_down.png), [up 컷](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/Grip/calibrated_pen_up.png). 실제 스킨 삼각형과 유한 원통 붓대의 간격 검사 |
| 기본 양팔 Animator | 기본 controller와 레이어0·단일 Idle·작도 해제 자세 차이0m, SampleAnimation 최대0.000000715m. 총8비교·errors0 | [animator-idle-audit.json](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/animator-idle-audit.json). 빈 상체 레이어 기본 weight=0, 런타임 drawWeight 적용 |
| 표현 ON/OFF | 336프레임 PASS, Raw204변조0·인식15차이0·동일 Raw OFF5·커밋10같은 프레임·복원=true | [최종 parity summary](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/20260907_235555_913_parity_visuals/summary.json). 별도 실행 타임스탬프97개 차이·최대0.04790306s를 그대로 보존. 같은 절대 시각이나 완전히 동일한 시간열을 증명한 검사가 아님 |
| 벽 붐·락온·갈무리 | 6,275프레임 PASS, 벽 적중504·락온 작도842·갈무리 끝점908표본. 팁0.00244446px·리본0.00106593px·갈무리 끝점0.000000322m, 복원=true | [context-validation.json](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/context-validation.json). 실제 입력·실제 경과 시간. 첫 점 투영 즉시 검사, 메시 존재는 기존 최소2점 계약 |
| 실제 주막 계단 왕복 | COMPLETE/PASS·복원=true, 302프레임/302쌍 PNG·30fps. 마루/귀환 도착=true, 지지면 누락0·종료 grounded=true. 오차0.03303617/0.04525074m·높이 변화1.366617m | [최종 Inn summary](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/20260908_003922_195_inn_visuals/summary.json). 최초 검수 위치 배치 후 실제 PlayerMotor 입력으로 왕복. 초기 안정화20프레임 외 접지269/비접지13프레임 기록 |
| C2 충돌 경로 | 424/424경유점 PASS, Move1,935회·측면 접촉2·지지면425표본 누락0 | [물리 probe](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/CodexWorld/traversal-validation.txt). 같은 규격의 임시 CC 검사; 실제 플레이어 변위0m |
| 공유 리그·씬 수명 | Hub→EffectLab→C2→C2 재시작4전환 PASS. 이전 body/overlay 소멸, 새 rig/driver/overlay 각1개, 참조·시간 배율·빈 입력 확인 | [scene-transition-validation.json](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/scene-transition-validation.json). 출발 씬 작도 표현 평가 후 실제 DevSceneFlow 실행. 실제 획·갈무리 유지 중 전환 전체를 뜻하지 않음 |
| 최종 Edit 참조 | C2·검수 씬 각각 visual/driver/armsCamera1개, 깨진 참조0·missing script0. 양쪽 Humanoid·48,166/19,834tris 확인 | [C2 감사](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/c2-scene-edit-audit.json), [검수 씬 감사](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/validation-scene-edit-audit.json). 종료 시 C2 Edit·IsDirty=false, 최근 Error0/Exception0 |

30/60/120Hz는 평가 간격이며 GPU 성능이나 실제 빌드 FPS 측정값이 아니다. 근접/월드의 화면·3D 정렬 범위는 §3의 계약을 따른다. 외부 비교뷰는 몸체 검수를 위해 화면 작도 리본만 숨기며, 실제 플레이뷰는 유지한다. 이 차이는 영상 라벨에도 표시한다.

전달 영상: [C2 이동·작도 비교](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/Dosa_C2_Comparison.mp4)(460프레임·15.333초), [주막 계단 왕복](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/Dosa_Inn_Traversal.mp4)(302프레임·10.067초). 둘 다 H.264·3840×1080·30fps이며 전체 디코딩 exit0·원본 manifest 일치 PASS다. [영상 검증/해시](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/delivery-video-validation.json). 검사 상세·측정 보완·에디터 복구 이력은 [런타임 검수 노트](C:/Users/yj666/Oheangbu/Oheangbu/Screenshots/PlayerDosa/runtime-validation-notes.md)에 보존한다.

## 6. V2 리그 선행 게이트와 제작 기록

2026-09-08 최신 실행 지시(#178): 정밀 전수 검수보다 실제 플레이 확인을 우선하여 V2 모델을 C2/공용 PlayerRig에 플레이용 초안으로 연결한다. 이 초안에 한해 기존 Humanoid 컨트롤러 재사용을 허용한다. 새로운 제작 클립·유료 모션 생성은 시작하지 않으며, 아래 최종 RIG_PASS 증거는 미충족 항목을 그대로 남긴다. 기존 V1 자산과 직전 연결의 복구본을 보존한다.

**Implementation: IN PROGRESS. V2 RIG_PASS·제작 모션·정본 교체·최종 플레이 영상은 미완료다.** Model7 캐릭터(ULTRA·4k PBR)35크레딧·붓30·배낭30, 총95크레딧으로3건의 생성/다운로드를 완료했고 수정 손·붓·LOD의 Unity 임포트까지 진행했다. 아래는 검수 계약이며 V1의 통과 수치를 V2 결과로 복사하지 않는다. 버전별 실제 수치·실패·검사 범위와 재구축 절차는 [PlayerV2 README](C:/Users/yj666/Oheangbu/Art/PlayerV2/README.md)를 따른다.

### 6.1 RIG_PASS에 필요한 증거

RigPass는 **같은 메시·본·웨이트·export/import 설정 해시에 대한 수치 PASS AND 시각 PASS**다. 검사 전 허용치와 표본 포즈를 고정하고 실패 후 결과에 맞춰 완화하지 않는다. 아래 새 수치 허용치는 V2 초기 검수 기준 [TEST]이며 실행 설정과 결과에 함께 기록한다.

| 검사 | 제출할 증거·초기 기준 |
|---|---|
| 소스·재구축 | 원본/작업본 분리, 모델·텍스처·UV 대응, 소스/출력 SHA-256, 도구 버전·명령/스크립트·import 설정. 삭제/추가 면·정점·웨이트 변경은 실제 수와 이유 기록 |
| 기하·웨이트 | NaN/Inf·범위 밖 인덱스·필수 본/재질 참조 누락·의도치 않은 미가중 정점0. 정점별 웨이트 합 `1±0.0001`, 실제 Unity 영향 본 제한과 잘림 유무 기록. 퇴화 면·뒤집힌 면·잘못 붙은 손/소매/몸통 경계를 검사 |
| 주 골격·보조 본 | 축·미터 단위·피벗·선택한 키·좌우·본 계층·Humanoid 매핑 확인. static import에서 양쪽 모델 Avatar 유효. 보조 본과 원본/rest의 대응표 제출 |
| 직접 본 포즈 검사 | rest/open hand/grip, 어깨 상승·팔꿈치 굽힘·팔 비틀기·손목·몸통·고관절/무릎/발목을 독립 및 조합 검사. 예시 각도는 어깨0/45/90/120도·팔꿈치0/45/90/120도·전완±90도. 제작 클립을 재타게팅하지 않고 임시 본 포즈로 수행하며 실제 표본을 원장에 기록 |
| 수치 변형 검사 | 직접 포즈에서 뼈 길이/루트의 불필요 변화와 측정 정밀도를 검사하며 허용치는 검사 전에 설정·해시 고정한다. 승인된 실제 파지 기준은 피부 접촉 간격≤1.5mm·최대 관통 깊이≤0.5mm다. 관통 삼각형 수는 깊이와 함께 진단값으로 기록하며 개수0을 별도 승인 기준으로 추가하지 않는다. 천/몸 경계와 소매 안쪽의 비정상 연결·관통은 표본별 기록 |
| 시각 검사 | Blender와 Unity의 같은 포즈를 전후좌우·위/아래·관절/소매/손 확대뷰로 확인. 손가락 분리·엄지 파지·겨드랑이/팔꿈치/무릎 체적·도포와 허리 장비 분리·옷 안쪽·원본 무늬/실루엣을 검사. 미확인/불확실 항목은 통과로 처리하지 않음 |
| Cloth 정적 선행 검사 | 천 고정점·자유 영역·보조 본 역할·신체/붓 충돌체를 저작하고 **제작 모션 없는 직접 포즈의 정착·중단/리셋·몸/붓 충돌 검사를 RIG_PASS 전에 수행**한다. 동일 해시의 Blender/Unity 증거와 사전 고정 한계를 제출한다. 제작 모션을 이용한 동적 안정성 검사는 RigPass 이후 별도 수행 |

수치 결과 파일만 있거나 대표 한 장만 확인한 상태로 RIG_PASS를 기록하지 않는다. 검사자는 사용한 해시·포즈·시점·이미지·실패/수정 이력·최종 판정 근거를 남긴다. 검증용 직접 포즈는 리깅 검사 도구이며 제작용 animation clip 또는 Meshy 모션 요청을 앞당기는 우회로 사용하지 않는다.

### 6.2 게이트 이후 승격

1. **RigPass 확정:** 위 수치/시각 증거가 모두 연결된 동일 버전만 다음 단계로 보낸다. 메시·웨이트·본 rest·의미 있는 import 설정이 바뀌면 해당 판정은 만료하고 다시 검사한다.
2. **모션·동적 격리 검수:** RIG_PASS 소스에만 기존 모션 재타게팅/신규 모션 제작·Meshy 요청을 허용한다. 선행 검사한 Unity 기본 Cloth와 보조 본에 제작 모션을 적용하고 걷기/달리기/회피·작도·붐·락온·갈무리·중단/재진입·시점 전환을 검사한다. 천의 몸/붓 관통·신축·안정화/리셋·성능을 실제 Play에서 확인한다. 기능 평가 Hz와 실제 성능 측정은 구분한다.
3. **C2 적용:** 격리 검수 통과 버전을 C2 인스턴스에 적용하고 실제 지형·계단·영상·입력 불변 회귀를 재검사한다. 직전 정본 복구 경로를 보존한다.
4. **공유 PlayerRig:** C2 결과가 통과한 같은 버전을 공용 프리팹으로 승격하고 override·카메라 stack·driver/TipSocket·씬 이동/재시작 참조를 확인한다. 기존 정본의 자동 덮어쓰기와 RIG_PASS 이전 승격은 금지한다.

현재 실제 비용은 원장 기준95/200크레딧이며 원본3건 생성/다운로드를 완료했다. 현재 후보의 삼각형 수와 수입 결과는 측정값이지만 V2 최종 채택과 RIG_PASS는 미확정이다. 남은 승인 예산105크레딧과 계정 잔고826은 다른 값이다. 생성 완료·비용 지출·임포트 성공만으로 게이트를 통과시키지 않는다.

## C2 성능 후속 #183 (2026-09-08) [TEST]

플레이용 `SecondaryMotion_DosaV2_Playable`의 Cloth solver를120→60Hz로 변경한다. 기존 천/장식·골격·소켓·붓털·인식은 유지한다. 같은 값을 갖는 붓 Collider geometry setter만 생략한다. 원본120Hz 설정과 코드는 Art/Performance/Backups/Cpu_20260908에 보존했다. 실제 이동·작도 smoke COMPLETE, 원시86점 변경0/콘솔 오류0/붓끝1.404px, 27장식·7붓 충돌 비교 통과. 전체 의복 변형 검수·RIG_PASS가 아니다. 성능은 SPEC-C2-PERFORMANCE와 Art/Performance/README.md가 소유한다.
