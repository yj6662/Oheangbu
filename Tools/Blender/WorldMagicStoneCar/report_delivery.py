"""Write the handoff after reading static render images; does not run Blender."""
from pathlib import Path
import json,hashlib,datetime
ROOT=Path(__file__).resolve().parents[3]
ART=ROOT/'Art/World/WorldMacro/MagicStoneCar';FOLDER=ART/'Blender'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldMacro/MagicStoneCar'
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def write(p,d):p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
parts=['Cabin','Roof','Engine','Wheel'];requested=[16000,8000,8000,3000];rows=[]
for name,request in zip(parts,requested):
    p=FOLDER/(name+'.json');d=read(p)
    if name=='Engine':
        d['core']['verifiedSemanticCore']=True
        d['core']['status']='PASS_ORIGINAL_CENTRAL_LENS_SURFACE'
        d['core']['inspectionEvidence']=['Images/EngineCore_isolated.png','Images/EngineCore_region.png']
        d['core']['scope']='Central cyan lens faces only; original UV. No glow material/runtime claim.'
    d['visualQuality']='STATIC_SHAPE_REVIEWED_NOT_USER_APPROVED';write(p,d)
    raw=read(FOLDER/(name+'_raw.json'))['raw'];pbr=read(FOLDER/(name+'_PBR.json'))
    rows.append({'part':name,'requestedMeshyPolycount':request,'rawMeshyTriangles':raw['triangles'],'exportedLodTriangles':[v['roundtrip']['actual']['triangles'] for v in d['levels']],
        'sourcePreserved':d['sourcePreserved'],'roundtripPass':all(v['roundtrip']['status']=='PASS_GEOMETRY' for v in d['levels']),'actualTextureSizes':pbr['actualSourceSizes']})
assembly=read(FOLDER/'Assembly.json');inspection=read(FOLDER/'StaticInspection.json')
inspection.update(visualInspection='PARTIAL_STATIC_REVIEW',coreSemanticInspection='PASS_ORIGINAL_CENTRAL_LENS_SURFACE',finalSeatImage='UNVERIFIED_AFTER_FINAL_TWO_FACE_CABIN_EDIT',
    finalSeatNote='Previous seated render exposed a narrow near-camera Cabin face. Final two-face opening patch is exported/roundtrip tested; re-render stopped at 85.59% commit. Unity capture must confirm removal.',
    exteriorInspection='Final front/side assembly reviewed; no user style approval',runtime='UNVERIFIED')
write(FOLDER/'StaticInspection.json',inspection)
guard=read(FOLDER/'memory_guard_event.json');guard.update(firstEventResumedAtRootCommitRatio=.7716,lastEvent={'stage':'Final seated capture, after successful final Cabin export and assembly','observedCommitRatio':.8559,'status':'CAPTURE_BLOCKED_BEFORE_RENDER'},finalModelsReady=True);write(FOLDER/'memory_guard_event.json',guard)
delivery={'generatedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Blender rigid asset handoff. Unity assembly/drive remains a separate verification.',
    'status':'READY_FOR_UNITY_WITH_FINAL_SEATED_VISUAL_CHECK','parts':rows,'blenderOnlyLodTriangles':assembly['manifest']['blenderOnlyLodTriangles'],
    'withTwoRuntimeAxlesLodTriangles':assembly['manifest']['lodTriangles'],'runtimeAxlesTriangles':64,'bounds':assembly['manifest']['actualBounds'],
    'blenderMaterialCount':len(assembly['levels'][0]['actual']['materials']),'blenderMeshInstanceCount':assembly['levels'][0]['actual']['meshCount'],
    'supportTriangles':[s['triangles'] for s in assembly['manifest']['support']],'supportRoundtripPass':all(s['roundtrip']['status']=='PASS_GEOMETRY' for s in assembly['manifest']['support']),
    'staticForwardRayCheck':{'clear':15,'total':15,'scope':'Fixed pose forward yaw±20°, pitch±10°; not full camera sweep'},
    'coreSemanticSurfacePass':True,'coreSocketBoundsCenterErrorM':inspection['socketDistanceFromSurfaceBoundsCenterM'],
    'sourcePaths':['Meshy/'+p+'/image/model_urls_glb.glb' for p in parts],
    'unverified':['Final seated image after last two-face edit','Unity emission appearance','Actual boarding/drive/collision/physics/camera comfort','LOD switching at distance','120fps'],
    'fileHashes':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ASSET/'Models').glob('*.fbx')}}
write(FOLDER/'DELIVERY.json',delivery)
table='\n'.join(f"| {r['part']} | {r['requestedMeshyPolycount']:,} | {r['rawMeshyTriangles']:,} | "+' / '.join(f'{v:,}' for v in r['exportedLodTriangles'])+' |' for r in rows)
report=f'''# 마석 자동차 Blender 제작 전달

네 Meshy 원형을 보존하고 별도 강체 모델·LOD·조립 원본을 만들었다. 실제 FBX 왕복에서 삼각형 수, UV 존재, 축·크기, 유한 좌표가 통과했다. 이 문서는 Blender 정적 검수 결과이며 Unity 주행 완료 보고서가 아니다.

| 파트 | Meshy 요청 polycount | 내려받은 원형 tris | FBX LOD0 / LOD1 / LOD2 tris |
|---|---:|---:|---:|
{table}

- 바퀴 네 개를 포함한 Blender 합계: **39,761 / 21,421 / 10,972 tris**. 정적 강체 18개, 원본·보완 재질 6개다. 발광 분리 후 Unity 재질 수는 별도 집계한다.
- 기존 Unity 추종 축 2개(64 tris)를 더한 예상 설치 합계: **39,825 / 21,485 / 11,036 tris**. LOD 상한 50k / 24k / 12k 이내다. Unity 실제 인스턴스 수는 설치 후 재검사한다.
- 보완 프레임·조향대·등롱은 LOD별 1,348 / 948 / 748 tris다. 기존 임시 지지대 4개는 중복 사용하지 않는다.
- 실제 전체 경계는 약 **폭 2.400 × 높이 3.430 × 길이 4.430m**. 바퀴 반지름 0.6m, 축 X, 기존 물리 축간·윤거를 유지했다.

## 수정과 검수

Cabin·Roof 긴 축을 차량 진행 방향으로 돌렸다. 승객실 전면에 생성된 닫힌 패널과 눈 위치를 가로지르던 면을 개구부 범위에서 제거했다. 측면 창살·기둥·벤치·장식과 원본 UV를 유지했고, 지붕 폭은 처마가 Cabin을 덮도록 맞췄다. 엔진은 원형 비례를 유지하며 전방에 놓았다. LOD 감소 후 중복·퇴화 면을 명시적으로 제거해 Blender와 실제 FBX 수량을 맞췄다.

**통과:** 중앙 청록 마석은 원본 표면을 `MagicStoneCore`로 분리했다. 격리 이미지와 단색 영역 이미지에서 중앙 렌즈이며 황동 테두리·양옆 실린더가 아님을 확인했다. LOD0 35 tris, 소켓은 실제 경계 중심에서 0.014mm 이내이며 경계 내부다. 전체 엔진을 발광시키지 않는다. `EmissionCandidateMask`는 미검증 후보로 남기며 사용하지 않는다.

**통과:** 12개 원형 파트 FBX와 3개 Support FBX의 빈 장면 왕복. 원본 GLB 해시 보존. 단위·방향·크기 오차 1mm 이내, UV 존재, 삼각형 수 일치. 정적 전방 15개 ray가 통과했다. 이는 카메라 전 범위 또는 실제 승차감 판정이 아니다.

**미검증:** 마지막 두 면 개구부 수정 뒤 운전석 재촬영은 시스템 커밋 85.59%에서 중단됐다. 현재 `Assembly_seated.png`와 `Assembly_threequarter.png`는 그 마지막 수정 전 비교 자료다. 최종 정면·후면·측면·상면 `Assembly_LOD0_*`는 최신 모델이다. 게임 화면에서 작은 잔여 면 제거 여부를 확인해야 한다. 실제 승하차·주행·충돌·발광·거리별 LOD 전환·120fps는 Unity 단계에서 별도 검수한다.

## 텍스처와 파일

각 파트의 실제 BaseColor·Normal은 4096², 원본 packed MR은 2048²다. 원본 요청 4K와 실제 MR 크기를 구분한다. MR의 B금속/G거칠기를 Unity R금속/A매끄러움으로 변환했다. 원본 AO가 없어 중립 흰색을 제공하며 AO를 측정했다고 주장하지 않는다.

- 편집 원본: `MagicStoneCar_{{Part}}_LOD{{0,1,2}}.blend`, `MagicStoneCar_Assembly_LOD{{0,1,2}}.blend` (텍스처 packed).
- Unity: 소유 폴더 `Models/`의 15개 FBX, `Textures/`, `SocketManifest.json`, `TextureManifest.json`.
- 수치: `DELIVERY.json`, 각 파트 JSON·LOD roundtrip JSON, `Assembly.json`, `StaticInspection.json`.
- 이미지: `Images/EngineCore_isolated.png`, `EngineCore_region.png`, `Assembly_LOD0_*`, `Assembly_LOD2_*` 및 원형 사면도. 이미지 내부 라벨 없음, 영상 없음.

한 번에 Blender 프로세스 하나를 사용했다. 커밋 85% guard 중단 이력은 `memory_guard_event.json`에 남겼다. 기존 Palanquin 모델·스クリپ트와 공급자 원본은 변경하지 않았다.
'''.replace('스クリپ트','스크립트')
(FOLDER/'REPORT.md').write_text(report,encoding='utf-8')
print(json.dumps({'status':delivery['status'],'lodTriangles':delivery['withTwoRuntimeAxlesLodTriangles'],'fbxFiles':len(delivery['fileHashes'])}))
