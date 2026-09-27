"""Read saved296/source295 YAML and independently audit the73 legacy replacements."""
from pathlib import Path
import collections
import hashlib
import json
import re
import struct
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
UNITY=ROOT/'Oheangbu'
OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296'
PRIVATE='Assets/_Project/Art/World/Architecture296/'

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def scene(path):
    text=path.read_text(encoding='utf-8-sig');parts=re.split(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$',text,flags=re.M)
    return {int(parts[i+1]):(int(parts[i]),parts[i+2]) for i in range(1,len(parts),3)}

def ref(text,key):
    match=re.search(r'^  '+key+r': \{fileID: (-?\d+)',text,re.M)
    return int(match[1]) if match else 0

def vector(text,key):
    match=re.search(r'^  '+key+r': \{([^}]+)\}',text,re.M)
    return {k:float(v) for k,v in re.findall(r'([xyzw]): ([^,]+)',match[1])}

def components(doc,go):return [int(x) for x in re.findall(r'- component: \{fileID: (-?\d+)\}',doc[go][1])]

def transform_matrix(doc,key,cache):
    if key in cache:return cache[key]
    t=doc[key][1];p=vector(t,'m_LocalPosition');q=vector(t,'m_LocalRotation');s=vector(t,'m_LocalScale');x,y,z,w=(q[k] for k in 'xyzw')
    m=np.eye(4);m[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])@np.diag([s[k] for k in 'xyz']);m[:3,3]=[p[k] for k in 'xyz']
    parent=ref(t,'m_Father');cache[key]=transform_matrix(doc,parent,cache)@m if parent else m
    return cache[key]

def object_name(doc,go):
    match=re.search(r'^  m_Name: (.*)$',doc[go][1],re.M)
    return match[1] if match else ''

def path_of(doc,transform):
    t=doc[transform][1];name=object_name(doc,ref(t,'m_GameObject'));parent=ref(t,'m_Father')
    return path_of(doc,parent)+'/'+name if parent else name

def mesh_uv(path):
    text=path.read_text(encoding='utf-8-sig');n=int(re.search(r'm_VertexCount: (\d+)',text)[1]);raw=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-fA-F]+)',text)[1]);stride=len(raw)//n
    channels=re.findall(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)',text)
    stream,offset,fmt,dimension=map(int,channels[4]);assert stream==0 and fmt==0 and dimension==2
    return collections.Counter(raw[i*stride+offset:i*stride+offset+8] for i in range(n)),text

def run():
    ledger_path=OUT/'Legacy/replacements.json';ledger=json.loads(ledger_path.read_text(encoding='utf-8-sig'))
    inventory_path=OUT/'Baseline/architecture-inventory295.json';inventory=json.loads(inventory_path.read_text(encoding='utf-8-sig'))
    target=UNITY/(PRIVATE+'W_Demo_Compact_Architecture296.unity');source=UNITY/inventory['Scene'];target_sha=sha(target);source_sha=sha(source)
    old=scene(source);new=scene(target);checks=[]
    def check(name,passed,details):checks.append({'name':name,'passed':bool(passed),'details':details})
    check('immutable source scene and baseline inventory hashes',source_sha==inventory['SceneSha256Before']==inventory['SceneSha256After'] and sha(inventory_path)==ledger['BaselineInventorySha256'],source_sha)
    check('exact reviewed scope and retained lantern cap count',len(ledger['Items'])==73 and ledger['FoundationStones']==45 and ledger['HouseholdTimbers']==28 and ledger['RetainedLanternCaps']==40,len(ledger['Items']))
    holders=[]
    for key,(kind,body) in new.items():
        if kind==4 and ref(body,'m_GameObject') in new and object_name(new,ref(body,'m_GameObject'))=='Architecture296_LegacySurface':holders.append(key)
    check('one managed replacement child per original object',len(holders)==73 and len({ref(new[k][1],'m_Father') for k in holders})==73,len(holders))
    failures=[];matches=[];cache={}
    for key in holders:
        parent=ref(new[key][1],'m_Father');t=new[parent][1];go=ref(t,'m_GameObject');name=path_of(new,parent);world=transform_matrix(new,parent,cache)[:3,3]
        rows=[r for r in ledger['Items'] if r['ScenePath']==name and np.max(np.abs(world-np.array([r['Position'][k] for k in 'xyz'])))<.002]
        ok=parent in old and len(rows)==1
        if not ok:failures.append(name);continue
        row=rows[0];matches.append(row['Id'])
        for field in ('m_LocalPosition','m_LocalRotation','m_LocalScale'):
            ok &= vector(t,field)==vector(old[parent][1],field)
        ok &= ref(t,'m_Father')==ref(old[parent][1],'m_Father')
        cs=components(new,go);prior=components(old,go)
        renderers=[c for c in cs if new[c][0]==23];colliders=[c for c in cs if new[c][0]==65]
        ok &= cs==prior and len(renderers)==1 and len(colliders)==1
        ok &= re.search(r'^  m_Enabled: 0$',new[renderers[0]][1],re.M) is not None
        ok &= all(new[c]==old[c] for c in colliders)
        children=[k for k,(kind,body) in new.items() if kind==4 and ref(body,'m_Father')==key]
        descendants=[c for child in children for c in components(new,ref(new[child][1],'m_GameObject'))]
        ok &= len(children)==3 and not any(new[c][0] in (64,65,135,136,143,146,154) for c in descendants)
        ok &= row['ColliderBefore']==row['ColliderAfter'] and row['TerrainBefore']==row['TerrainAfter'] and row['TransformUnchanged'] and row['CollidersUnchanged'] and row['OriginalRendererDisabled']
        if not ok:failures.append(name+' '+row['Id'])
    check('saved original transforms and complete BoxCollider YAML unchanged; only renderer disabled',not failures and len(set(matches))==73,failures)
    bad=[];mesh_paths=sorted({p for r in ledger['Items'] for p in r['Meshes']});triangles={}
    for name in mesh_paths:
        path=UNITY/name;uv,text=mesh_uv(path);kind='HouseholdTimber' if 'HouseholdTimber' in path.name else 'FoundationStone';level=int(re.search(r'_LOD(\d)',path.name)[1]);factor=14 if kind=='HouseholdTimber' else 1
        srcname='Pier289_planks_0' if factor==14 else 'Floor_stone_2';original_uv,_=mesh_uv(UNITY/(PRIVATE+f'Meshes/Crossings/SourceLods/{srcname}_part0_LOD{level}.asset'))
        expected=collections.Counter({k:v*factor for k,v in original_uv.items()});triangles[name]=sum(int(x) for x in re.findall(r'^    indexCount: (\d+)',text,re.M))//3
        if not name.startswith(PRIVATE) or uv!=expected:bad.append(name)
    check('six private source LOD meshes retain exact UV bit patterns',len(mesh_paths)==6 and not bad,bad)
    check('ledger LOD triangle counts match actual serialized meshes',all(r['Triangles']==[triangles[p] for p in r['Meshes']] for r in ledger['Items']),{p:triangles[p] for p in mesh_paths})
    # Caps live inside nested ThatchedInn prefab instances, so their source
    # component records are not expanded into the scene YAML. Compare complete
    # instance overrides except the known candidate material remapping.
    inn=UNITY/'Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab';guid=re.search(r'guid: (\w+)',inn.with_suffix('.prefab.meta').read_text())[1]
    prefab=scene(inn);cap_count=sum(kind==1 and object_name(prefab,k)=='Timber_Cap' for k,(kind,_) in prefab.items())
    instances={k:b for k,(kind,b) in old.items() if kind==1001 and 'm_SourcePrefab: {fileID: 100100000, guid: '+guid in b}
    clones=json.loads((OUT/'cloned-data.json').read_text(encoding='utf-8-sig'))
    material_guids={re.search(r'guid: (\w+)',(UNITY/r['target']).with_suffix('.mat.meta').read_text())[1]
                    for r in clones if r['target'].startswith(PRIVATE) and r['target'].endswith('.mat')}
    material_overrides=[m for k in instances for m in re.findall(r'    - target:.*?\n(?=    - target:|    m_RemovedComponents:)',new[k][1],re.S) if 'm_Materials.' in m]
    unknown_overrides=[m for m in material_overrides if 'objectReference: {fileID: 0}' not in m and not any('guid: '+g+',' in m.split('objectReference:',1)[-1] for g in material_guids)]
    def without_materials(body):
        return re.sub(r'    - target:.*?\n(?=    - target:|    m_RemovedComponents:)',lambda m:'' if 'm_Materials.' in m[0] else m[0],body,flags=re.S)
    changed_inns=[k for k,b in instances.items() if k not in new or without_materials(b)!=without_materials(new[k][1])]
    check('lantern prefab source and complete instance overrides retained except candidate material bindings',cap_count==4 and len(instances)>=10 and not changed_inns and not unknown_overrides,{'sourceCapsPerInn':cap_count,'instancesIncludingInactive':len(instances),'changedInstances':changed_inns,'nonPrivateMaterialOverrides':len(unknown_overrides)})
    check('terrain and water bytes unchanged',ledger['HeightSha256Before']==ledger['HeightSha256After']=='6b5264879d73d85bb6ebe92c7d1c00f4020514dd44250ba5db152401de5f71d5' and ledger['WaterSha256Before']==ledger['WaterSha256After']=='285d91325478df1241839ee7845ad2450e80347e2851ebb59460bcda34bb1f83',{'height':ledger['HeightSha256After'],'water':ledger['WaterSha256After']})
    check('original selected source files unchanged',all(sha(UNITY/r['Path'])==r['Sha256'] for r in ledger['Sources']),len(ledger['Sources']))
    check('saved inputs remained stable during independent read',sha(target)==target_sha and sha(source)==source_sha,{'candidateScene':target_sha,'sourceScene':source_sha})
    return {'passed':all(c['passed'] for c in checks),'scope':'Independent saved scene YAML, original collider/transform, source UV bit patterns and ledger audit; no live Physics or manual art approval.','ledgerSha256':sha(ledger_path),'checks':checks}

if __name__=='__main__':
    result=run();path=OUT/'Legacy/independent-checks.json';path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(f"{sum(c['passed'] for c in result['checks'])}/{len(result['checks'])} PASS; {path}");raise SystemExit(0 if result['passed'] else 1)
