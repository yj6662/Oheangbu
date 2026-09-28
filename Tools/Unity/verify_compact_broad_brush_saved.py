"""Compare stable serialized geometry and source assets after Editor restart."""
from pathlib import Path
import re,json,hashlib,base64
root=Path(__file__).resolve().parents[2]
out=root/'Art/World/WorldMacro/Compact/InkLandscape/BroadBrush'
b=json.loads((out/'baseline.json').read_text(encoding='utf-8-sig'))
changed=[]
for row in b['hashes']:
    p,digest=row.rsplit('|',1)
    h=hashlib.sha256()
    with (root/'Oheangbu'/p).open('rb') as f:
        for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
    if base64.b64encode(h.digest()).decode()!=digest:changed.append(p)
paths=[out/'before_open.unity',root/'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity']
scenes=[]
classes={1:'GameObject',4:'Transform',33:'MeshFilter',54:'Rigidbody',64:'MeshCollider',65:'BoxCollider',135:'SphereCollider',136:'CapsuleCollider',154:'TerrainCollider'}
for p in paths:
    s=p.read_text(encoding='utf-8-sig')
    matches=list(re.finditer(r'(?m)^--- !u!(\d+) &([^\s]+).*$',s));records={}
    for i,m in enumerate(matches):
        kind=int(m[1])
        if kind in classes:records[(kind,m[2])]=s[m.start():matches[i+1].start() if i+1<len(matches) else len(s)]
    scenes.append(records)
changes=[f'{classes[k[0]]}:{k[1]}' for k in set(scenes[0])|set(scenes[1]) if scenes[0].get(k)!=scenes[1].get(k)]
result={'status':'PASS' if not changed and not changes else 'FAIL','sourceHashCount':len(b['hashes']),'changedSources':changed,'stableSerializedRecordCount':len(scenes[0]),'changedRecords':changes,'scope':list(classes.values()),'phase':'after normal editor restart and repeated isolated Play measurement'}
(out/'saved_scene_checks.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result));raise SystemExit(0 if result['status']=='PASS' else 1)
