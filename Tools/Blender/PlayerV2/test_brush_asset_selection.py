"""Standalone staging contract probes; fixtures retained under Inspect for review."""
import copy, json, hashlib
from datetime import datetime
from pathlib import Path
from brush_asset_selection import apply_selected_brush, sha

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/BrushMaterial/StagingFixtures'/datetime.now().strftime('%Y%m%d_%H%M%S_%f')
OUT.mkdir(parents=True)
def write(path, obj):
    path.parent.mkdir(parents=True,exist_ok=True)
    if isinstance(obj,bytes):path.write_bytes(obj)
    else:path.write_text(json.dumps(obj,indent=2))
def fixture(name):
    art=OUT/name;stage=art/'Staging'
    for relative,value in [('BrushRefined/model.fbx',b'new actual fbx'),('Brush/SM_DosaBrushV2.fbx',b'original actual fbx'),
                           ('BrushRefined/Textures/base.png',b'new paired base'),('BrushRefined/Textures/normal.png',b'new paired normal'),
                           ('Brush/Textures/base.png',b'old handle base')]:write(stage/relative,value)
    write(art/'Original.blend',b'original blend');write(art/'Refined.blend',b'refined blend')
    entry={'name':'DosaBrushV2_Bristles','baseColor':'BrushRefined/Textures/base.png','normal':'BrushRefined/Textures/normal.png','metallicFallback':0,'roughnessFallback':.85}
    write(stage/'BrushRefined/fragment.json',{'materials':[entry],'rendererMaterials':[{'renderer':'DosaBrushV2_Bristles','materials':['DosaBrushV2_Bristles']}]})
    build={'outputFbxSha256':sha(stage/'BrushRefined/model.fbx'),'outputBlendSha256':sha(art/'Refined.blend'),
           'textures':[{'path':str(stage/entry[c]),'sha256':sha(stage/entry[c])} for c in ['baseColor','normal']]}
    write(stage/'BrushRefined/build.json',build);write(stage/'BrushRefined/validation.json',{'fbx_sha256':build['outputFbxSha256']})
    choice={'fbx':'BrushRefined/model.fbx','fbxSha256':build['outputFbxSha256'],
            'selectedBlend':'Refined.blend','selectedBlendSha256':build['outputBlendSha256'],
            'preservedOriginalBlend':'Original.blend','preservedOriginalBlendSha256':sha(art/'Original.blend'),
            'materialFragment':'BrushRefined/fragment.json','materialFragmentSha256':sha(stage/'BrushRefined/fragment.json'),
            'buildReport':'BrushRefined/build.json','validationReport':'BrushRefined/validation.json'}
    write(stage/'BrushRefined/active-selection.json',choice)
    manifest={'materials':[{'name':'Unrelated','roughnessFallback':.42},{'name':'DosaBrushV2','baseColor':'Brush/Textures/base.png'}],
              'rendererMaterials':[{'renderer':'Body','materials':['Unrelated']},{'renderer':'DosaBrushV2_Bristles','materials':['DosaBrushV2']}]}
    return stage,manifest

checks=[]
stage,m=fixture('valid');original=sha(stage/'Brush/SM_DosaBrushV2.fbx');original_m=copy.deepcopy(m)
first=apply_selected_brush(m,stage)
assert sha(stage/'Brush/SM_DosaBrushV2.fbx')==sha(stage/'BrushRefined/model.fbx')
assert any(x['sha256']==original and Path(x['path']).is_file() for x in first['historicalCopies'])
assert m['materials'][0]==original_m['materials'][0] and m['materials'][1]==original_m['materials'][1]
checks.append('Selected FBX activated only after preserving old bytes; unrelated and handle materials retained.')
once=copy.deepcopy(m);second=apply_selected_brush(m,stage)
assert m==once and first['historicalCopies']==second['historicalCopies']
checks.append('Repeated export is idempotent without duplicate material/mapping/archive entries.')
regenerated=copy.deepcopy(m)
next(x for x in regenerated['rendererMaterials'] if x['renderer']=='DosaBrushV2_Bristles')['materials']=['DosaBrushV2']
apply_selected_brush(regenerated,stage)
assert regenerated==once
checks.append('Character exporter rebuilding the old bristle mapping is repaired to the selected paired material.')
for case,relative,mutation in [('wrong_fbx','BrushRefined/model.fbx',b'changed fbx'),('wrong_map','BrushRefined/Textures/base.png',b'wrong UV atlas')]:
    s,manifest=fixture(case);old=sha(s/'Brush/SM_DosaBrushV2.fbx');unchanged=copy.deepcopy(manifest);write(s/relative,mutation)
    try:apply_selected_brush(manifest,s)
    except ValueError:pass
    else:raise AssertionError(case+' accepted')
    assert manifest==unchanged and sha(s/'Brush/SM_DosaBrushV2.fbx')==old
    checks.append(case+' rejected before active FBX or manifest mutation.')
s,manifest=fixture('missing_map');choice=json.loads((s/'BrushRefined/active-selection.json').read_text())
fragment=json.loads((s/'BrushRefined/fragment.json').read_text());fragment['materials'][0]['normal']='BrushRefined/Textures/missing.png'
write(s/'BrushRefined/fragment.json',fragment);choice['materialFragmentSha256']=sha(s/'BrushRefined/fragment.json');write(s/'BrushRefined/active-selection.json',choice)
try:apply_selected_brush(manifest,s)
except ValueError:checks.append('A non-paired/missing texture path is rejected before activation.')
else:raise AssertionError('missing map accepted')
report={'checks':checks,'passed':len(checks),'fixtureRoot':str(OUT),'realAssetsEdited':False}
(OUT/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
