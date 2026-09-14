"""Read the saved Unity wall/floor assets and test enclosure with Blender's BVH.

Run with Blender --background --factory-startup --python this_file.py.
Read-only: does not open a scene, export a new mesh, or change Unity assets.
The explicit X=-43 mouth is an allowed opening; no claim about runtime rendering.
"""
import re,json,math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np
root=Path(__file__).resolve().parents[2];folder=root/'Oheangbu/Assets/_Project/Art/World/WorldMacro/Playtest/NaturalCave/Meshes'
def read(p):
 s=p.read_text();n=int(re.search(r'm_VertexCount: (\d+)',s)[1]);raw=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',s)[1]);stride=len(raw)//n
 v=np.ndarray((n,3),'<f4',raw,strides=(stride,4)).copy();f=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',s)[1]),'<u4' if 'm_IndexFormat: 1' in s else '<u2').reshape(-1,3).copy();return v,f
v,f=read(folder/'Natural_Cave_EmbeddedInterior.asset');vf,ff=read(folder/'Natural_Cave_InnerFloor.asset');ff+=len(v);v=np.concatenate([v,vf]);f=np.concatenate([f,ff]);tree=BVHTree.FromPolygons(v.tolist(),f.tolist(),all_triangles=True)
ys=[1.1,1.65,2.35];rays=hits=back=0;escaped=[];origins=[];near=[]
for x in range(-160,-46,6):
 for z in range(-16,25,4):
  hit,n,ix,d=tree.ray_cast(Vector((x,1,z)),Vector((0,-1,0)),2)
  if hit is None:continue
  for y in ys:
   o=Vector((x,y,z));q,n,ix,d=tree.find_nearest(o)
   if d<.7:continue
   origins.append(tuple(o))
   for pitch in [-70,-45,-20,0,20,45,70,90]:
    for a in range(0,360,15):
     rays+=1;a=math.radians(a);p=math.radians(pitch);dir=Vector((math.sin(a)*math.cos(p),math.sin(p),math.cos(a)*math.cos(p)))
     h,n,ix,d=tree.ray_cast(o,dir,250)
     if h is not None:
      hits+=1
      if n.dot(dir)>0:back+=1
     else:
      t=(-43-x)/dir.x if dir.x>0 else -1
      ep=o+dir*t
      if t<=0 or ep.y<-.01 or ep.y>15 or ep.z< -10 or ep.z>21:
       escaped.append(dict(origin=list(o),direction=list(dir),end=list(ep)))
out=dict(rays=rays,hits=hits,geometricBackHits=back,origins=len(origins),unexpectedEscapeCount=len(escaped),unexpectedEscapes=escaped[:100])
(root/'Art/World/WorldMacro/Playtest/NaturalCave/shell_offline_rays.json').write_text(json.dumps(out,indent=2));print(json.dumps({k:v for k,v in out.items() if k!='unexpectedEscapes'}))
