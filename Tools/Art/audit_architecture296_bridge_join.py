"""Read-only reconstruction of the candidate shared bridge approach revision.

Checks the exact generated ribbon top triangles, not Unity physics or vehicle
movement. Source terrain and route assets are never modified.
"""
import json, math
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'Art/World/Compact/Rebuild'

def vec(p):return np.array([p[k] for k in 'xyz'],float)
def unit(p):return p/max(np.linalg.norm(p),1e-10)
def sample(a,d):
 for p,q in zip(a[:-1],a[1:]):
  delta=q-p;length=np.linalg.norm(delta)
  if d<=length:return p+delta*np.clip(d/length,0,1),unit(delta)
  d-=length
 return a[-1],unit(a[-1]-a[-2])
def main():
 source=json.loads((WORK/'Watershed295/Generated/crossings.json').read_text())['Crossings']
 old=json.loads((WORK/'Architecture296/crossings.json').read_text())['Bridges'][0]['Routes']
 canon=next(x for x in source if x['RouteId']=='capital_center__palace');other=next(x for x in source if x['RouteId']=='hyeongang__capital_center')
 a=np.array([vec(p) for p in canon['Points']]);b=np.array([vec(p) for p in other['Points']])
 h=np.fromfile(WORK/'Watershed295/Generated/height.bytes','<f4').reshape(1501,1001)
 def ground(p):
  x,z=p[0]/4,p[2]/4;ix,iz=int(x),int(z);u,v=x-ix,z-iz
  aa,bb,cc,ee=h[iz,ix],h[iz,ix+1],h[iz+1,ix],h[iz+1,ix+1]
  return float(aa+(bb-aa)*u+(cc-aa)*v if u+v<=1 else ee+(cc-ee)*(1-u)+(bb-ee)*(1-v))
 i=np.linalg.norm(a-vec(canon['Start']),axis=1).argmin();j=np.linalg.norm(a-vec(canon['End']),axis=1).argmin();deck=a[min(i,j):max(i,j)+1][::-1]
 length=np.linalg.norm(np.diff(deck,axis=0),axis=1).sum();join,jf=sample(deck,min(9,length*.2));start=b[0];fd=unit(b[1]-start);fd[1]=jf[1]=0
 distance=np.linalg.norm(start-join);handle=min(22,max(12,distance*.45));c1=start+unit(fd)*handle;c2=join-unit(jf)*handle
 result=[]
 for t in np.linspace(0,1,max(4,math.ceil(distance*2))+1):
  p=(1-t)**3*start+3*(1-t)**2*t*c1+3*(1-t)*t*t*c2+t**3*join
  p[1]=max(start[1]+(join[1]-start[1])*(t*t*(3-2*t)),ground(p)+.025)
  d=a[1:]-a[:-1];dx=d[:,[0,2]];ts=np.clip(np.sum((p[[0,2]]-a[:-1,[0,2]])*dx,axis=1)/np.sum(dx*dx,axis=1),0,1);qs=a[:-1]+ts[:,None]*d;ds=np.linalg.norm(qs[:,[0,2]]-p[[0,2]],axis=1);k=ds.argmin()
  t2=np.clip((ds[k]-3)/3,0,1);blend=1-t2*t2*(3-2*t2);p[1]=max(ground(p)+.025,p[1]+(qs[k,1]-p[1])*blend);result.append(p)
 at=np.linalg.norm(a-join,axis=1).argmin()
 while at>0 and np.dot(a[at]-join,jf)<0:at-=1
 for p in a[at::-1]:
  if np.linalg.norm(result[-1]-p)>.01:result.append(p.copy())
 new=np.array(result)
 def triangles(paths,miter):
  all=[]
  for path in paths:
   rights=[]
   for i,p in enumerate(path):
    f=path[min(len(path)-1,i+1)]-path[max(0,i-1)];f[1]=0;r=unit(np.cross([0,1,0],unit(f)))
    t=path[i]-path[i-1] if i==len(path)-1 else path[i+1]-path[i];t[1]=0;sr=unit(np.cross([0,1,0],unit(t)));rights.append(r/max(.8,np.dot(r,sr)))
   for i,(p,q) in enumerate(zip(path[:-1],path[1:])):
    r=unit(np.cross([0,1,0],q-p));r0,r1=(rights[i],rights[i+1]) if miter else (r,r)
    al=p-r0*3+[0,.04,0];ar=p+r0*3+[0,.04,0];bl=q-r1*3+[0,.04,0];br=q+r1*3+[0,.04,0]
    all.extend([[al,bl,br],[al,br,ar]])
  return np.array(all)
 def audit(paths,miter):
  tri=triangles(paths,miter);o=tri[:,0];u=tri[:,1]-o;v=tri[:,2]-o;den=u[:,0]*v[:,2]-u[:,2]*v[:,0];good=abs(den)>1e-9;o,u,v,den=o[good],u[good],v[good],den[good]
  result=[]
  for ri,path in enumerate(paths):
   pathlen=np.linalg.norm(np.diff(path,axis=0),axis=1).sum();dist=np.arange(0,pathlen+.001,.2);rows=[]
   for lateral in [0,-2,2]:
    previous=None;fails=[];worst=0;missing=0
    for s in dist:
     p,f=sample(path,s);r=unit(np.cross([0,1,0],f));p=p+r*lateral;w=p-o
     uu=(w[:,0]*v[:,2]-w[:,2]*v[:,0])/den;vv=(u[:,0]*w[:,2]-u[:,2]*w[:,0])/den;mask=(uu>=-1e-6)&(vv>=-1e-6)&(uu+vv<=1+1e-6)
     yy=(o[:,1]+uu*u[:,1]+vv*v[:,1])[mask]
     if len(yy)==0:missing+=1;previous=None;continue
     y=float(yy.max())
     if previous is not None:
      rise=abs(y-previous[1]);worst=max(worst,rise)
      if rise>.22:fails.append(dict(distance=round(float(s),3),rise=round(rise,4),xz=p[[0,2]].tolist()))
     previous=(s,y)
    rows.append(dict(lateral=lateral,samples=len(dist),missing=missing,maxRisePer20cm=worst,discontinuities=fails[:20],discontinuityCount=len(fails)))
   result.append(dict(route=ri,rows=rows))
  return result
 oldpaths=[np.array([vec(p) for p in route['Points']]) for route in old]
 out=dict(scope='Offline reconstructed top triangles at .2m intervals, centre and +/-2m. No props, Unity raycasts, CC or wheels; not a live acceptance result.',old=audit(oldpaths,False),proposed=audit([a,new],True),newBranchPoints=len(new),newEnd=new[-1].tolist())
 path=WORK/'Architecture296/Analysis/shared-bridge-join.json';path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(out,indent=2),encoding='utf8')
 print(json.dumps(out,indent=2))
if __name__=='__main__':main()
