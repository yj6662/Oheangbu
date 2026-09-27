"""Read-only saved gate transforms and binary FBX geometry; no Unity calls."""
from pathlib import Path
import hashlib,json,re,struct,zlib
import numpy as np
from audit_architecture296_legacy import scene,ref,vector,components,transform_matrix,object_name,path_of
ROOT=Path(__file__).resolve().parents[2];U=ROOT/'Oheangbu';OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296'

def fbx_nodes(path):
 b=path.read_bytes();version=struct.unpack_from('<I',b,23)[0];wide=version>=7500
 def node(pos):
  end,n,size=struct.unpack_from('<QQQ' if wide else '<III',b,pos);pos+=24 if wide else 12
  l=b[pos];pos+=1;name=b[pos:pos+l].decode();pos+=l;props=[]
  if end==0:return None,0
  for _ in range(n):
   t=chr(b[pos]);pos+=1
   if t in 'fdilbc':
    count,encoding,length=struct.unpack_from('<III',b,pos);pos+=12;raw=b[pos:pos+length];pos+=length
    if encoding:raw=zlib.decompress(raw)
    props.append(np.frombuffer(raw,dtype={'f':'<f4','d':'<f8','i':'<i4','l':'<i8','b':'u1','c':'u1'}[t]).tolist())
   elif t in 'SR':
    length=struct.unpack_from('<I',b,pos)[0];pos+=4;raw=b[pos:pos+length];pos+=length;props.append(raw.decode(errors='replace') if t=='S' else str(raw))
   else:
    fmt={'Y':'<h','C':'?','I':'<i','F':'<f','D':'<d','L':'<q'}[t];props.append(struct.unpack_from(fmt,b,pos)[0]);pos+=struct.calcsize(fmt)
  children=[]
  while pos<end-(25 if wide else 13):
   child,following=node(pos)
   if child is None:break
   children.append(child);pos=following
  return dict(name=name,props=props,children=children),end
 out=[];p=27
 while p<len(b):
  n,p=node(p)
  if n is None:break
  out.append(n)
 return out

def main():
 target=U/'Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity';doc=scene(target);cache={};records=[]
 for key,(kind,body) in doc.items():
  if kind!=4 or ref(body,'m_GameObject') not in doc:continue
  name=object_name(doc,ref(body,'m_GameObject'))
  if name not in ('VictoryGate253','LeftHinge','RightHinge','SM_G_MetalDoor_001','CAP_GATE_ARCH'):continue
  path=path_of(doc,key)
  if not path.startswith('CapitalSouthGate253/'):continue
  world=transform_matrix(doc,key,cache);records.append(dict(path=path,position=world[:3,3].tolist(),matrix=world.tolist(),localRotation=vector(body,'m_LocalRotation'),localScale=vector(body,'m_LocalScale')))
 p=U/'Assets/HwaseongForteressGate/Models/Gate/SM_G_MetalDoor_001.fbx';nodes=fbx_nodes(p);meshes=[]
 def walk(ns):
  for n in ns:
   if n['name']=='Geometry':
    v=next((c['props'][0] for c in n['children'] if c['name']=='Vertices'),None)
    if v:
     a=np.asarray(v).reshape(-1,3);meshes.append(dict(name=n['props'][1],vertices=len(a),min=a.min(0).tolist(),max=a.max(0).tolist(),topXs=a[a[:,1]>a[:,1].max()-.05].min(0).tolist(),topXe=a[a[:,1]>a[:,1].max()-.05].max(0).tolist()))
   walk(n['children'])
 walk(nodes)
 result=dict(sceneSha256=hashlib.sha256(target.read_bytes()).hexdigest(),source=str(p.relative_to(U)),sourceSha256=hashlib.sha256(p.read_bytes()).hexdigest(),records=records,sourceMeshes=meshes,scope='Saved scene transforms and raw FBX coordinates only; no live mesh or physics changes. Raw FBX axes/units need importer interpretation.')
 (OUT/'Analysis/gate-leaf-geometry.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))
if __name__=='__main__':main()
