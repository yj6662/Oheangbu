"""Compare the pre-replacement open-scene snapshot by Unity object IDs.

Does not modify Unity files or assets. Child lists/render bindings may change;
pre-existing geography transforms, terrain collider bindings and game fields
are independently reported so visual replacement cannot masquerade as a reset.
"""
from pathlib import Path
import re, json, difflib
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Compact/KcisaReplacement'
def read(path):
    text=path.read_text(encoding='utf-8-sig')
    matches=list(re.finditer(r'^--- !u!(\d+) &(-?\d+).*\n',text,re.M))
    return {m[2]:(int(m[1]),text[m.end():matches[i+1].start() if i+1<len(matches) else len(text)]) for i,m in enumerate(matches)}
old=read(OUT/'before_open.unity')
new=read(ROOT/'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity')
def field(body,name):
    m=re.search(r'^  '+re.escape(name)+r': (.*)$',body,re.M)
    return m[1] if m else None
transforms=[];terrain=[];behaviours=[];deleted=[]
for key,(kind,before) in old.items():
    if key not in new:
        deleted.append({'id':key,'kind':kind});continue
    after=new[key][1]
    if kind==4:
        changes={k:[field(before,k),field(after,k)] for k in ('m_LocalPosition','m_LocalRotation','m_LocalScale','m_Father') if field(before,k)!=field(after,k)}
        if changes:transforms.append({'id':key,'changes':changes})
    if kind==64:
        gid=re.search(r'm_GameObject: \{fileID: (-?\d+)',before)
        name=field(old.get(gid[1],(0,''))[1],'m_Name') if gid else ''
        if name and name.startswith('Terrain_'):
            fields=('m_Mesh','m_Enabled','m_IsTrigger','m_Convex')
            terrain.append({'id':key,'name':name,'unchanged':all(field(before,k)==field(after,k) for k in fields),'mesh':field(before,'m_Mesh')})
    if kind==114 and before!=after:
        delta=[l for l in difflib.unified_diff(before.splitlines(),after.splitlines(),n=0) if l[:1] in ('+','-') and not l.startswith(('+++','---'))]
        behaviours.append({'id':key,'script':field(before,'m_Script'),'changes':delta})
result=dict(preexistingObjects=len(old),currentObjects=len(new),deletedObjects=deleted,
            preexistingTransformChanges=transforms,terrainColliders=terrain,changedMonoBehaviours=behaviours,
            terrainUnchanged=all(t['unchanged'] for t in terrain),
            scope='Scene serialized data only. External asset byte hashes and full Play integration are not inferred.')
(OUT/'preservation_checks.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if not isinstance(v,list)},ensure_ascii=False))
print('deleted',len(deleted),'changed transforms',len(transforms),'terrain',len(terrain),'changed behaviours',len(behaviours))
print(json.dumps(behaviours[:4],ensure_ascii=False,indent=2))
