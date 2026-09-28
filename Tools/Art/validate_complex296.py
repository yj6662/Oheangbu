"""Independent offline shape/index checks. This does not replace Physics/Nav/Play acceptance."""
from pathlib import Path
import json,hashlib,numpy as np,shutil,datetime,gc
ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild/Architecture296/Complex'
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as f:
  for chunk in iter(lambda:f.read(4*1024*1024),b''):h.update(chunk)
 return h.hexdigest()
def parts(p):
 text=p.read_text(encoding='utf-8-sig');at=text.index('"Parts":')+len('"Parts":');at=text.index('[',at)+1;decoder=json.JSONDecoder()
 while True:
  while text[at]in' \r\n\t,':at+=1
  if text[at]==']':break
  value,at=decoder.raw_decode(text,at);yield value
results=[];receipts=json.loads((WORK/'Reduced/receipts.json').read_text())
for receipt in receipts:
 p=next(p for p in(WORK/'Source').glob('*.json')if receipt['source']in p.read_text(encoding='utf-8-sig')[:1000]) if False else None
 # Only read source metadata prefix, without retaining all source vertices beside reduced meshes.
 for candidate in(WORK/'Source').glob('*.json'):
  with candidate.open(encoding='utf-8-sig')as f:head=f.read(3000)
  if receipt['source']in head:p=candidate;meta=json.loads(head[:head.index('"Parts"')]+'"Parts":[]}');break
 if p is None:raise RuntimeError('Source missing')
 r=WORK/'Reduced'/p.name;levels=[dict(level=i,triangles=0,vertices=0,finite=True,degenerate=0,invalidIndices=0,min=[1e99]*3,max=[-1e99]*3)for i in[1,2]]
 for part in parts(r):
  assert len(part['Levels'])==2
  for out,mesh in zip(levels,part['Levels']):
   v=np.array([[v[k]for k in'xyz']for v in mesh['Vertices']],float);uv=np.array([[v[k]for k in'xy']for v in mesh['UV']],float);t=np.array(mesh['Triangles'],np.int64).reshape(-1,3);out['triangles']+=len(t);out['vertices']+=len(v);out['finite']=bool(out['finite']and np.isfinite(v).all()and np.isfinite(uv).all());out['invalidIndices']+=int(((t<0)|(t>=len(v))).sum())
   if len(t):
    tri=v[t];area=np.linalg.norm(np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]),axis=1)*.5;out['degenerate']+=int((area<1e-12).sum());out['min']=np.minimum(out['min'],v.min(0)).tolist();out['max']=np.maximum(out['max'],v.max(0)).tolist()
 for i,out in enumerate(levels):assert out['triangles']==receipt['levels'][i+1]and out['finite']and out['invalidIndices']==0
 c=json.loads((WORK/'Collision'/p.name).read_text());v=np.array([[v[k]for k in'xyz']for v in c['Mesh']['Vertices']],float);t=np.array(c['Mesh']['Triangles'],np.int64).reshape(-1,3);tri=v[t];area=np.linalg.norm(np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]),axis=1)*.5
 row=dict(source=receipt['source'],sourcePrefabSha256=sha(ROOT/'Oheangbu'/receipt['source']),expectedSourcePrefabSha256=meta['SourceSha256'],sourceExportSha256=sha(p),reducedSha256=sha(r),near=dict(method='Original prefab referenced vertices, normals and UV, no reduction',triangles=receipt['levels'][0]),reducedLevels=levels,collision=dict(triangles=len(t),vertices=len(v),finite=bool(np.isfinite(v).all()),degenerate=int((area<1e-12).sum()),min=v.min(0).tolist(),max=v.max(0).tolist(),sha256=sha(WORK/'Collision'/p.name)))
 assert row['sourcePrefabSha256']==row['expectedSourcePrefabSha256']and row['sourceExportSha256']==receipt['sourceExportSha256']and row['reducedSha256']==receipt['reducedSha256'];results.append(row);print(receipt['source'],[o['triangles']for o in levels],row['collision']['triangles'],flush=True);del c,v,t,tri,area;gc.collect()
old=WORK/'validation.json';history=WORK/'History/rejected-whole-building-qem';history.mkdir(parents=True,exist_ok=True)
if old.exists()and not(history/'validation.json').exists():shutil.copy2(old,history/'validation.json')
proof=dict(version=2,utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Offline source hashes, compact-index integrity and finite geometry. Structural visual coverage was separately checked against the original with the same camera. Actual Physics/Nav/Play remains separate.',visualEvidence='../Captures/after/11-hyeongang-eye.png',rejectedVisualEvidence='../Analysis/NativeVisual/08-lod0-flat.png',assets=results)
old.write_text(json.dumps(proof,indent=2),encoding='utf-8')
