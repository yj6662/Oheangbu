"""Read-only gate source/profile preflight. Writes evidence outside Assets only."""
from pathlib import Path
import sys,json,hashlib,re,numpy as np
ROOT=Path(__file__).resolve().parents[4];sys.path.insert(0,str(ROOT/'Tools/Art'))
from audit_architecture296_gate_leaf import fbx_nodes
from audit_architecture296_apron_caps import read_saved
from audit_architecture296_legacy import scene,ref,components,transform_matrix,path_of
U=ROOT/'Oheangbu';OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296';target=U/'Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity'
doc=scene(target);cache={};tr={}
for k,(kind,b) in doc.items():
 if kind!=4 or ref(b,'m_GameObject') not in doc:continue
 try:tr[path_of(doc,k)]=k
 except KeyError:pass
gate=tr['CapitalSouthGate253/VictoryGate253'];gm=transform_matrix(doc,gate,cache);inv=np.linalg.inv(gm)
leaf=[];arch=[];folder=U/'Assets/_Project/Art/World/WorldMacro/Playtest/VisualCorridor/Meshes'
for name,k in tr.items():
 if not name.startswith('CapitalSouthGate253/'):continue
 if 'CAP_GATE_ARCH/Surface_' in name:
  body=next(doc[c][1] for c in components(doc,ref(doc[k][1],'m_GameObject')) if doc[c][0]==33)
  guid=re.search(r'm_Mesh:.*guid: (\w+)',body)[1]
  path=next(m.with_suffix('') for m in folder.glob('abc00000000000823680420976672398_L0_M*.asset.meta') if 'guid: '+guid in m.read_text())
  v,t=read_saved(path);m=inv@transform_matrix(doc,k,cache);v=v@m[:3,:3].T+m[:3,3];arch.append((v,t))
 if name.endswith('/SM_G_MetalDoor_001/SM_G_MetalDoor_001'):leaf.append(inv@transform_matrix(doc,k,cache))
nodes=fbx_nodes(U/'Assets/HwaseongForteressGate/Models/Gate/SM_G_MetalDoor_001.fbx')
def find(nodes):
 for n in nodes:
  if n['name']=='Geometry':yield n
  yield from find(n['children'])
g=next(find(nodes));v=np.array(next(c['props'][0] for c in g['children'] if c['name']=='Vertices')).reshape(-1,3)*np.array([-.01,.01,.01]);idx=next(c['props'][0] for c in g['children'] if c['name']=='PolygonVertexIndex');tri=[];ring=[]
for i in idx:
 ring.append(i if i>=0 else -i-1)
 if i<0:
  for j in range(1,len(ring)-1):tri.append([ring[0],ring[j],ring[j+1]])
  ring=[]
t=np.array(tri);leaves=[v@m[:3,:3].T+m[:3,3] for m in leaf];leaves.sort(key=lambda a:a[:,0].min());top=max(a[:,1].max() for a in leaves);bottom=top+.11
xs=np.linspace(-4.3,4.3,87);ceiling=np.full(len(xs),np.inf)
for v,t in arch:
 for a,b,c in v[t]:
  mask=(xs>=min(a[0],b[0],c[0])-1e-8)&(xs<=max(a[0],b[0],c[0])+1e-8)
  ii=np.where(mask)[0]
  yy=np.full(len(ii),np.inf)
  for p,q in [(a,b),(b,c),(c,a)]:
   if abs(q[0]-p[0])<1e-6:continue
   u=(xs[ii]-p[0])/(q[0]-p[0]);valid=(u>=-1e-5)&(u<=1+1e-5)
   yy[valid]=np.minimum(yy[valid],p[1]+(q[1]-p[1])*u[valid])
  okay=yy>5.5;ceiling[ii[okay]]=np.minimum(ceiling[ii[okay]],yy[okay])
panels=[a+np.array([0,2.1,0]) for a in leaves];a=leaves[0];lintel=np.column_stack([a[:,1]-4.1,-(a[:,0]+2.1)+bottom+.12,a[:,2]*1.3]);tris=np.concatenate([a[t] for a in panels]);linteltris=lintel[t]
def covered(x,y,tt):
 a,b,c=tt[:,0,:2],tt[:,1,:2],tt[:,2,:2];p=np.array([x,y]);cross=lambda a,b:a[:,0]*b[:,1]-a[:,1]*b[:,0]
 area=cross(b-a,c-a);mask=abs(area)>1e-7;a,b,c,area=a[mask],b[mask],c[mask],area[mask];u=cross(b-p,c-p)/area;v=cross(p-a,c-a)/area;return bool(np.any((u>=-1e-5)&(v>=-1e-5)&(1-u-v>=-1e-5)))
samples=0;miss=[]
for i in range(1,86):
 for y in np.arange(bottom+.02,ceiling[i]-.04,.075):
  samples+=1;ok=(y<=bottom+.24 and covered(xs[i],y,linteltris)) or (y>=bottom+.2 and covered(xs[i],y,tris))
  if not ok:miss.append([float(xs[i]),float(y)])
result={'scope':'Offline source FBX and saved arch triangles. Live generated mesh/UV/physics not yet verified.','sceneSha':hashlib.sha256(target.read_bytes()).hexdigest(),'leafMaximumY':float(top),'fixedMinimumY':float(bottom),'verticalSwingClearanceAllYaw':.11,'profile87':ceiling.tolist(),'archCrown':float(max(ceiling)),'coverageSamples':samples,'coverageMisses':miss,'passed':np.all(np.isfinite(ceiling)) and len(miss)==0}
Path(__file__).with_name('source-preflight.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='profile87'}));raise SystemExit(0 if result['passed'] else 1)
