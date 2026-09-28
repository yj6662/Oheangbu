"""Offline coverage of source-masonry facing added below unchanged bridge aprons."""
import json,hashlib
from pathlib import Path
import numpy as np
from audit_architecture296_apron_caps import clip,mesh

ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild'
def unit(a):return a/max(np.linalg.norm(a),1e-12)
def main():
 h=np.fromfile(WORK/'Watershed295/Generated/height.bytes','<f4').reshape(1501,1001)
 def ground(p):
  x,z=p[...,0]/4,p[...,2]/4;ix,iz=x.astype(int),z.astype(int);u,v=x-ix,z-iz;a,b,c,e=h[iz,ix],h[iz,ix+1],h[iz+1,ix],h[iz+1,ix+1];return np.where(u+v<=1,a+(b-a)*u+(c-a)*v,e+(c-e)*(1-u)+(b-e)*(1-v))
 records=json.loads((WORK/'Architecture296/CrossingLods/lods.json').read_text())['Sources'];parts=[s for s in records if s['Source'].endswith('/SM_CW.prefab')]
 raw=json.loads((WORK/'Architecture296/CrossingLods/sources.json').read_text())['Sources'];vv=np.concatenate([mesh(s['Mesh'])[0] for s in raw if s['Source'].endswith('/SM_CW.prefab')]);lo,hi=vv.min(0),vv.max(0);origin=(lo+hi)/2;origin[1]=lo[1];course=(hi[1]-lo[1])*.65*.8
 floor=next(s for s in raw if s['Source'].endswith('/Floor_stone_2.prefab'));vf,_=mesh(floor['Mesh']);slab=(vf[:,1].max()-vf[:,1].min())/3
 bridge=next(b for b in json.loads((WORK/'Architecture296/crossings.json').read_text())['Bridges'] if b['Id']=='jeokro__cheolong_bridge');points=np.array([[v[k]for k in 'xyz']for v in bridge['Routes'][0]['Points']]);rows=[]
 for side,index,nextindex in [('start',0,1),('end',-1,-2)]:
  end=points[index]+[0,.04,0];forward=points[nextindex]-end;forward[1]=0;forward=unit(forward);right=np.cross([0,1,0],forward)
  def surface(p):
   along=(p-end)@forward;t=np.clip((along+6)/6,0,1);base=ground(p)+.03;return np.maximum(base,base+(end[1]-base)*(t*t*(3-2*t)))
  corners=np.array([end-forward*6-right*3,end-forward*6+right*3,end+right*3,end-right*3]);footprint=end-forward*3
  for edge,(a,b) in enumerate(zip(corners,np.roll(corners,-1,axis=0))):
   tangent=unit(b-a);tangent[1]=0;out=np.cross(tangent,[0,1,0]);out=out if np.dot(out,(a+b)*.5-footprint)>0 else -out;localright=np.cross([0,1,0],out)
   length=np.linalg.norm((b-a)[[0,2]]);pieces=int(np.ceil(length/.5));step=length/pieces
   for level in range(3):
    triangles=[]
    for i in range(pieces):
     centre=a+(b-a)*(i+.5)/pieces+out*.008;q=centre+np.array([-.53,0,.53])[:,None]*tangent*step;bottom=float(ground(q).min()-.16);top=float((surface(q)-slab-.012).max())
     if top<=bottom+.12:continue
     y=bottom
     while y<top:
      maxy=min(course,top-y)
      for part in parts:
       v,f=mesh(part['Levels'][level]);v=(v-origin)*.65
       for face in f:
        poly=clip(clip(clip(list(v[face]),0,-(step+.035)/2,True),0,(step+.035)/2,False),1,maxy,False)
        for j in range(1,len(poly)-1):
         tr=np.array([poly[0],poly[j],poly[j+1]]);normal=np.cross(tr[1]-tr[0],tr[2]-tr[0]);nn=np.linalg.norm(normal)
         if nn<1e-4 or normal[2]/nn<.55:continue
         world=centre+tr[:,0,None]*localright+np.c_[np.zeros(3),tr[:,1]+y-centre[1],np.zeros(3)];world[:,1]=np.minimum(world[:,1],surface(world)-slab-.012);xy=np.c_[(world-a)@tangent,world[:,1]];triangles.append(xy)
      y+=course-.025
    misses=[];tested=0
    if triangles:
     tri=np.array(triangles);o=tri[:,0];u=tri[:,1]-o;v=tri[:,2]-o;den=u[:,0]*v[:,1]-u[:,1]*v[:,0];good=abs(den)>1e-10;o,u,v,den=o[good],u[good],v[good],den[good]
     for distance in np.arange(.0125,length,.025):
      p=a+tangent*distance+out*.008;low=float(ground(p))+.03;high=float(surface(p))-slab-.032
      if high<=low:continue
      for height in np.linspace(low,high,7):
       w=np.array([distance,height])-o;s=(w[:,0]*v[:,1]-w[:,1]*v[:,0])/den;t=(u[:,0]*w[:,1]-u[:,1]*w[:,0])/den;hit=np.any((s>=-1e-5)&(t>=-1e-5)&(s+t<=1+1e-5));tested+=1
       if not hit:misses.append(dict(distance=float(distance),height=float(height)))
    rows.append(dict(side=side,edge=edge,lod=level,triangles=len(triangles),sampledCavityPoints=tested,misses=len(misses),examples=misses[:5]))
 result=dict(scope='Offline exact source-face clipping/projection on all four apron edges, 2.5cm longitudinal spacing and seven heights through visible cavity. New faces are visual only; no physics/surface changes.',passed=sum(r['misses']==0 for r in rows),failed=sum(r['misses']>0 for r in rows),samples=sum(r['sampledCavityPoints']for r in rows),rows=rows)
 path=WORK/'Architecture296/Analysis/apron-continuous-facing.json';path.write_text(json.dumps(result,indent=2),encoding='utf8');print(json.dumps({k:v for k,v in result.items()if k!='rows'},indent=2));print([r for r in rows if r['misses']])
if __name__=='__main__':main()
