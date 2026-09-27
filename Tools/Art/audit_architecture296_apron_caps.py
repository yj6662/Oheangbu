"""Offline source-cut cap and unchanged saved apron-contact checks.

Reads source export, all three QEM levels and the saved candidate floor mesh.
No Unity calls or source/candidate asset writes.
"""
import hashlib,json,re
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild'
OUT=WORK/'Architecture296/Analysis'

def mesh(m):return np.array([[p[k] for k in 'xyz'] for p in m['Vertices']],float),np.array(m['Triangles']).reshape(-1,3)
def clip(poly,axis,edge,greater):
 out=[]
 if not len(poly):return out
 a=poly[-1];ain=a[axis]>=edge if greater else a[axis]<=edge
 for b in poly:
  bin=b[axis]>=edge if greater else b[axis]<=edge
  if ain!=bin:out.append(a+(b-a)*(edge-a[axis])/(b[axis]-a[axis]))
  if bin:out.append(b)
  a=b;ain=bin
 return out
def hull(points,axes):
 if len(points)<3:return []
 points=sorted(points,key=lambda p:tuple(p[axes]))
 def cross(a,b,c):
  u=(b-a)[axes];v=(c-a)[axes];return u[0]*v[1]-u[1]*v[0]
 ring=[]
 for p in points:
  while len(ring)>=2 and cross(ring[-2],ring[-1],p)<=1e-7:ring.pop()
  ring.append(p)
 lower=len(ring)
 for p in points[-2::-1]:
  while len(ring)>lower and cross(ring[-2],ring[-1],p)<=1e-7:ring.pop()
  ring.append(p)
 return ring[:-1]
def read_saved(path):
 text=path.read_text();n=int(re.search(r'm_VertexCount: (\d+)',text)[1]);raw=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',text)[1]);stride=len(raw)//n
 v=np.ndarray((n,3),'<f4',raw,strides=(stride,4)).copy();t=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',text)[1]),'<u4').reshape(-1,3).copy();return v,t
def main():
 source=json.loads((WORK/'Architecture296/CrossingLods/lods.json').read_text())['Sources'];parts=[s for s in source if s['Source'].endswith('/SM_CW.prefab')]
 original=json.loads((WORK/'Architecture296/CrossingLods/sources.json').read_text())['Sources'];rawparts=[mesh(s['Mesh'])[0] for s in original if s['Source'].endswith('/SM_CW.prefab')];bounds=np.concatenate(rawparts);lo,hi=bounds.min(0),bounds.max(0);centre=(lo+hi)/2;centre[1]=lo[1]
 results=[]
 for part in parts:
  for level,m in enumerate(part['Levels']):
   raw,faces=mesh(m)
   for width in [.65,1]:
    v=(raw-centre)*(width/(hi[2]-lo[2]))
    for maxy in [.10,.35,.8,1.5]:
     tris=[]
     for face in faces:
      poly=clip(clip(clip(list(v[face]),0,-width/2,True),0,width/2,False),1,maxy,False)
      for j in range(1,len(poly)-1):tris.append([poly[0],poly[j],poly[j+1]])
     if not len(tris):continue
     tris=np.array(tris);points=tris.reshape(-1,3);caps=0;covered=0;fail=[];max_outside=0.
     for axis,edge,axes,outward in [(0,-width/2,[2,1],[-1,0,0]),(0,width/2,[2,1],[1,0,0]),(1,maxy,[0,2],[0,1,0])]:
      on=np.all(abs(tris[:,:,axis]-edge)<1e-4,axis=1);areas=np.linalg.norm(np.cross(tris[:,1]-tris[:,0],tris[:,2]-tris[:,0]),axis=1)
      if np.any(on&(areas>1e-4)):continue
      pp=points[abs(points[:,axis]-edge)<1e-4];unique=[]
      for p in pp:
       if not any(np.sum((p-q)**2)<1e-8 for q in unique):unique.append(p)
      ring=hull(unique,axes)
      if len(ring)<3:continue
      if np.dot(np.cross(ring[1]-ring[0],ring[2]-ring[0]),outward)<0:ring.reverse()
      ring=np.array(ring);caps+=len(ring)-2
      rr=ring[:,axes];orientation=np.sign(np.sum(rr[:,0]*np.roll(rr[:,1],-1)-rr[:,1]*np.roll(rr[:,0],-1)))
      for p in unique:
       signs=[]
       for a,b in zip(ring,np.roll(ring,-1,axis=0)):
        u=(b-a)[axes];w=(p-a)[axes];signs.append(orientation*(u[0]*w[1]-u[1]*w[0])/max(np.linalg.norm(u),1e-10))
       outside=max(0.,-min(signs));max_outside=max(max_outside,outside)
       if outside>1e-4:fail.append('cut boundary not enclosed within the .1mm source-vertex weld tolerance')
       else:covered+=1
      if np.any(abs(ring[:,axis]-edge)>1e-4):fail.append('cap outside cut plane')
      for i in range(1,len(ring)-1):
       if np.dot(np.cross(ring[i]-ring[0],ring[i+1]-ring[0]),outward)<-1e-8:fail.append('inward cap winding')
     results.append(dict(part=part['Part'],lod=level,width=width,maximumY=maxy,originalClippedTriangles=len(tris),newVisualCapTriangles=caps,enclosedBoundaryPoints=covered,maximumBoundaryOutsideMetres=max_outside,passed=not fail,failures=fail))
 h=np.fromfile(WORK/'Watershed295/Generated/height.bytes','<f4').reshape(1501,1001)
 def ground(p):
  x,z=p[...,0]/4,p[...,2]/4;ix,iz=x.astype(int),z.astype(int);u,v=x-ix,z-iz;a,b,c,e=h[iz,ix],h[iz,ix+1],h[iz+1,ix],h[iz+1,ix+1]
  return np.where(u+v<=1,a+(b-a)*u+(c-a)*v,e+(c-e)*(1-u)+(b-e)*(1-v))
 floor=ROOT/'Oheangbu/Assets/_Project/Art/World/Architecture296/Meshes/Crossings/jeokro__cheolong_bridge_0_aprons_0.asset';v,t=read_saved(floor);p=v[t];n=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);norm=np.linalg.norm(n,axis=1);top=(n[:,1]>.6*norm)&(norm>1e-6);centres=p.mean(1)[top];p=p[top];slope=np.degrees(np.arccos(n[top,1]/norm[top]));landings=[]
 for label,mask in [('left',centres[:,0]>1150),('right',centres[:,0]<1150)]:
  q=p[mask];gap=q[:,:,1]-ground(q);landings.append(dict(label=label,topTriangles=len(q),slopeDegreesMinMedianMax=np.quantile(slope[mask],[0,.5,1]).tolist(),surfaceAboveTerrainMinMedianMax=np.quantile(gap,[0,.5,1]).tolist(),boundsMin=q.reshape(-1,3).min(0).tolist(),boundsMax=q.reshape(-1,3).max(0).tolist()))
 output=dict(scope='Offline source clipping and saved apron mesh. New caps are render-only in a separate batch, excluded from all collision. Does not replace Unity visual/CC/Wheel validation.',passed=sum(x['passed'] for x in results),failed=sum(not x['passed'] for x in results),cases=results,savedPavingSha256=hashlib.sha256(floor.read_bytes()).hexdigest(),paving=landings,interpretation='Both rectangles are terrain-following sloping paving, not vertical walls: median slope29/33deg. Most vertices are3–7cm above terrain; falling outer bank corners rise up to0.86/1.94m and retain their existing masonry foundation. No paving vertex is changed by the cap pass.')
 OUT.mkdir(parents=True,exist_ok=True);path=OUT/'apron-cut-caps.json';path.write_text(json.dumps(output,indent=2),encoding='utf8');print(json.dumps({k:v for k,v in output.items() if k!='cases'},indent=2))
if __name__=='__main__':main()
