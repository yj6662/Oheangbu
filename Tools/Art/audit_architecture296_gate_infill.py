"""Independent saved transom geometry/bounds/scene audit; no Unity calls."""
from pathlib import Path
import sys,json,re,hashlib,numpy as np
from audit_architecture296_apron_caps import read_saved
from audit_architecture296_legacy import scene,ref,components,path_of,object_name
ROOT=Path(__file__).resolve().parents[2];U=ROOT/'Oheangbu';OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296'
historical=json.loads((OUT/'gate-infill-refresh.json').read_text());r=json.loads((OUT/'gate-infill.json').read_text());folder=U/'Assets/_Project/Art/World/Architecture296/Meshes/GateInfill';records=[]
for path in sorted(folder.glob('*.asset')):
 v,t=read_saved(path);s=path.read_text();m=re.search(r'^  m_LocalAABB:\s+m_Center: \{([^}]+)\}\s+m_Extent: \{([^}]+)\}',s,re.M)
 assert m,path
 xyz=lambda s:np.array([float(x) for x in re.findall(r'[xyz]: ([^,}]+)',s)])
 c,e=xyz(m[1]),xyz(m[2]);outside=float(np.max(np.maximum(c-e-v,v-c-e)));area=np.linalg.norm(np.cross(v[t[:,1]]-v[t[:,0]],v[t[:,2]]-v[t[:,0]]),axis=1)*.5
 records.append({'path':str(path.relative_to(U)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'triangles':len(t),'vertices':len(v),'min':v.min(0).tolist(),'max':v.max(0).tolist(),'minimumTriangleArea':float(area.min()),'maximumOutsideBounds':outside,'finite':bool(np.isfinite(v).all()),'strictBoundsPass':outside<=0})
scene_path=U/'Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity';d=scene(scene_path);roots=[]
for k,(kind,b) in d.items():
 if kind==4 and ref(b,'m_GameObject') in d and object_name(d,ref(b,'m_GameObject'))=='FixedArchInfill296':roots.append(k)
scene_records=[]
for root in roots:
 path=path_of(d,root);desc=[k for k,(kind,b) in d.items() if kind==4 and ref(b,'m_Father')==root]
 cs=[c for k in [root]+desc for c in components(d,ref(d[k][1],'m_GameObject'))]
 scene_records.append({'path':path,'children':len(desc),'rendererCount':sum(d[c][0]==23 for c in cs),'lodGroupCount':sum(d[c][0]==205 for c in cs),'colliderCount':sum(d[c][0] in [64,65,135,136,143,146,154] for c in cs)})
source=U/'Assets/HwaseongForteressGate/Models/Gate/SM_G_MetalDoor_001.fbx';source_sha=hashlib.sha256(source.read_bytes()).hexdigest()
checks={'fourFixedRoots':len(roots)==4,'sixteenPrivateMeshes':len(records)==16,'actualTriangleCountsMatchReceipt':sum(x['triangles'] for x in records)==sum(g['Triangles'] for g in r['Gates']),'strictAllVertexBounds':all(x['strictBoundsPass'] for x in records),'finiteVertices':all(x['finite'] for x in records),'noAddedCollider':all(x['colliderCount']==0 for x in scene_records),'oneLodFourRenderersPerGate':all(x['rendererCount']==4 and x['lodGroupCount']==1 for x in scene_records),'sourceFBXUnchanged':source_sha=='3aa48c6f10712630f7ab2c8db2168b5b8d7cb3a677ec591727f833c3db8221bd','historicalNarrowHelperCollidersAndControlUnchanged':historical['CollidersUnchanged'] and historical['DoorConfigurationUnchanged'],'receiptCoverageNoMiss':all(x['CoverageMisses']==0 for x in r['Gates']),'receiptSwingClearanceAtLeast10cm':all(x['MinimumRotationClearance']>=.1 for x in r['Gates'])}
result={'scope':'Saved private mesh vertices/indices/bounds and scene component audit, source FBX hash. Physical before/after and swing coverage are explicitly live helper receipt claims. No final art verdict.','passed':all(checks.values()),'checks':checks,'sourceSha256':source_sha,'meshes':records,'scene':scene_records,'sceneSha256':hashlib.sha256(scene_path.read_bytes()).hexdigest(),'generationUtc':r['Utc'],'historicalNarrowHelperUtc':historical['Utc'],'historicalPhysicsProofScope':'Before/after SHA of the first narrow visual update; later full builds are independently verified by the parent and are not covered by this older whole-scene SHA'}
path=OUT/'Analysis/gate-infill-independent.json';path.write_text(json.dumps(result,indent=2));print(str(sum(checks.values()))+'/'+str(len(checks))+' PASS; '+str(path));raise SystemExit(0 if result['passed'] else 1)
