"""Record the completed manual review; never edits production assets."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Art/PlayerV2/Inspect/LODDeformationRepair'
OUT=BASE/'FinalRigidParts'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def read(p):return json.loads(Path(p).read_text())
one=read(BASE/'Final/character-lods.json')['lods'][0]
two=read(OUT/'character-lods.json')['lods'][-1]
one['output']=str(OUT/'SM_DosaV2_LOD1.fbx')
assert one['lod']==1 and two['lod']==2
reports=[(BASE/'FinalRoundTrip/report.json','lod1'),(BASE/'AcceptedRoundTrip/report.json','lod2')]
models=[];renders=[]
for path,label in reports:
    result=read(path);m=next(x for x in result['models'] if x['label']==label)
    assert m['sha256']==sha(OUT/('SM_DosaV2_'+label.upper()+'.fbx'))
    models.append(m)
    for r in result['renders']:
        if r['label']!=label:continue
        assert sha(r['path'])==r['sha256']
        r=dict(r);r['manuallyOpened']=True
        r['review']='Directly viewed: repaired palm/cuff has no long fold wedge. Far-LOD faceting remains.' if 'Hand' in r['view'] else (
            'Camera is partly occluded by trousers; use the separate boot comparison for sole shape.' if r['view']=='boot' else
            'Directly viewed: face/whole-body silhouette is retained; this is not a cloth-intersection or animation gate.')
        renders.append(r)
contact=[x for m in models for p in m['poses'] for x in p.get('actualFbxContact',{}).values()]
assert len(contact)==8 and all(x['pass'] for x in contact)
assert all(p.get('handProperSelfCrossings',{}).get('count',0)==0 for m in models for p in m['poses'])
assert all(v==0 for m in models for p in m['poses'] for v in p['wrappingProperCrossings'].values())
for r in [one,two]:assert all(p['properCrossings']==0 for p in r['handCorrectiveRepair']['poses']+r['handCorrectiveRepair']['transition'])
comparison=[]
for folder in ['BootComparison','Actual120Comparison']:
    report=read(BASE/folder/'report.json')
    for r in report['renders']:
        assert sha(r['path'])==r['sha256']
        comparison.append(dict(r,manuallyOpened=True,comparisonSet=folder))
source=ROOT/'Art/PlayerV2/History/LOD/458-before-cuff-hand-repair/DosaV2_Assembled.blend'
assert sha(source)==one['sourceSha256']==two['sourceSha256']
ledger={'scope':'LOD-only geometry and static FBX round-trip. No original model, Unity asset, controller or production action was changed.',
    'source':str(source),'sourceSha256':sha(source),'rigPass':False,
    'status':'BOUNDED_LOD_HAND_CUFF_REPAIR_REVIEWED_FOR_WORLD_DISTANCES',
    'lods':[one,two],
    'selectedFiles':[{'path':str(OUT/name),'sha256':sha(OUT/name)} for name in ['SM_DosaV2_LOD1.fbx','DosaV2_LOD1.blend','SM_DosaV2_LOD2.fbx','DosaV2_LOD2.blend']],
    'code':[{'path':str(p),'sha256':sha(p)} for p in [Path(__file__).with_name('build_character_lods.py'),Path(__file__).with_name('lod_deformation_protection.py')]],
    'actualFbxReports':[str(p) for p,_ in reports],
    'actualFbxModels':models,
    'contactSummary':{'passed':len(contact),'total':8,'maximumWholeSkinPenetrationM':max(c['maximumWholeSkinPenetrationM'] for c in contact),
        'primaryGapLimitM':.0015,'penetrationLimitM':.0005,
        'sampling':'Actual FBX posed triangle skin versus unchanged irregular brush handle; .5mm edge step, at most48 subdivisions per triangle; same prior contact rule.'},
    'manualRenders':renders,'comparisonRenders':comparison,
    'reviewFindings':[
        {'id':'HAND_CUFF','result':'resolved_in_tested_range','details':'Original LOD1/2 grip self-crossings and LOD2 long cuff spike are removed. Two named hand correctives survive actual FBX. 22 unique static poses plus21 grip fractions have zero proper hand crossings. Actual FBX13poses/cuff have zero proper cuff crossings.'},
        {'id':'FAR_LOD_APPEARANCE','result':'acceptable_at_measured_world_sizes','details':'Direct comparison of LOD0 and final LOD2 at exactly120px height, both front and rear, retains the character/pack outline. LOD1 is also viewed at240px. LOD2 hand facets and accessory UV stretching are visible in850px macro views, so these meshes must remain world-distance LODs.'},
        {'id':'REAR_BUDGET','result':'repaired_with_tradeoff','details':'Rejected voxel Backpanel lost broad surface; rejected whole-panel hull occluded bottles. Final Backpanel keeps original1514tri QEM shape. Nine small independent tubes/pouches use source-envelope geometry48tri each, source triangle UV/weights reprojection; far labels/concavities lose detail. Fixed BrushBundle uses4mm voxel reconstruction, then budget reduction.'},
        {'id':'BOOT','result':'no_new_failure_observed','details':'18 additional LOD0/1/2 sole/rear views show connected soles in left/right rest,knee,ankle poses. Earlier front boot shots were trouser-occluded. LOD2 remains angular. BootComparison uses the preceding frozen candidate; protected BodyCore target and method were not changed in final accessory reallocation. It is supporting comparison, not proof of all body intersection freedom.'},
        {'id':'SOURCE_LINING','result':'outside_this_repair','details':'White plane behind isolated near hand shots belongs to frozen458 lining/sleeve geometry, not the reconstructed cuff. Parent is separately replacing source lining/cloth. No 38-pose cloth gate or near-view suitability is implied.'}],
    'rejectedCandidates':[{'folder':'Hybrid3100Budget','reason':'LOD2 voxel rear panel loses broad opaque shell.'},{'folder':'Final','reason':'LOD2 whole-backpanel hull fills intended bottle spaces and stretches UV; LOD1 from this run is retained unchanged.'}],
    'nextIntegration':'Parent may regenerate with current Assembled using build_character_lods.py. New source requires new source-bound review; current evidence belongs only to458. Hand source must remain certified f94/8745vertices and cuff source24-sample rings. Native Cloth is LOD0-only.'}
(OUT/'handoff.json').write_text(json.dumps(ledger,indent=2),encoding='utf-8')
(OUT/'character-lods.json').write_text(json.dumps({'status':ledger['status'],'lods':[one,two],'rigPass':False},indent=2),encoding='utf-8')
lines=['# LOD 손·감개 수정 인계','',
    '동결 source458에서 파생한 월드 LOD 수정입니다. 원본 모델·Unity 에셋·생산 애니메이션은 변경하지 않았습니다. 전체 RIG_PASS는 선언하지 않습니다.','',
    '| 항목 | LOD1 | LOD2 |','|---|---:|---:|',f'| 내보낸 삼각형 | {one["triangles"]:,} | {two["triangles"]:,} |',
    '| 손 proper 자기교차 | 22포즈+21구간 모두0 | 22포즈+21구간 모두0 |',
    '| actual FBX 양손 penDown/Up | 4/4 PASS | 4/4 PASS |','',
    f'실제 삼각면 피부와 불규칙한 붓대의 최대 침투는 {ledger["contactSummary"]["maximumWholeSkinPenetrationM"]*1000:.6f}mm입니다. 기준은 침투0.5mm/엄지·검지·중지 간격1.5mm 그대로입니다. 본98개·두 grip corrective·4weight 상한을 보존했습니다.','',
    '감개는 원본 원주 링을 보존해 안팎 벽을 다시 구성했습니다. 손은 LOD1에서 파지면, LOD2에서 rest면을 줄인 뒤 원래 파지 목표를 역스키닝해 두 corrective에 옮겼습니다. 이후 접촉 부근을 고정하고 남은 근원부 접힘만 국소 수정했습니다. LOD2 rest 정점6개는 최대0.026759mm 이동했습니다.','',
    'LOD2 가방 판은 원래1514tri를 유지합니다. 앞선 voxel 판과 가방 전체 hull은 실제 후면 확대에서 결함이 보여 폐기했습니다. 예산은 작은 독립 통·주머니9개를48tri씩 줄여 확보했습니다. 얼굴·BodyCore/부츠·손의 보호 하한은 더 줄이지 않았습니다.','',
    '실제120px 정면·후면 원본 비교에서 외곽과 가방 구성이 유지됩니다. 확대하면 LOD2 손의 각짐과 작은 소품의 UV 늘어짐이 보입니다. 근접 팔에 이 모델을 사용하면 안 됩니다. 원경 외형 판정이며 cloth/피부의 모든 교차를 통과했다는 뜻이 아닙니다.','',
    '선택 파일은 이 폴더의 SM_DosaV2_LOD1/2.fbx 및 DosaV2_LOD1/2.blend입니다. SHA와24개 최종 뷰/24개 보조 비교 PNG 원장은 handoff.json에 있습니다. generator는 기본 두 LOD를 만들고 --source/--out/--levels=2 인자를 지원합니다. 다음 source에서 재생성하면 그 source로 다시 검수해야 합니다.','',
    '검수 근거: FinalRoundTrip/report.json의LOD1, AcceptedRoundTrip/report.json의LOD2, BootComparison/report.json, Actual120Comparison/report.json. 실제로 열린 PNG만 수동 검수 원장에 포함했습니다.']
(OUT/'handoff.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps({'triangles':[one['triangles'],two['triangles']],'contact':ledger['contactSummary'],'selectedFiles':ledger['selectedFiles'],'manualRenders':len(renders),'comparisonRenders':len(comparison)},indent=2))
