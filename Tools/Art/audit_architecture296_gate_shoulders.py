"""Read-only reconstruction of the shared gate approach height field.

Uses deterministic #295 routes and the same south-gate redirect. This is an
offline profile audit; actual .3m-step CharacterController checks remain live.
"""
import json,math
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild'
def unit(a):return a/max(np.linalg.norm(a),1e-12)
def smooth(t):t=np.clip(t,0,1);return t*t*(3-2*t)
def points(row):return np.array([[v[k]for k in 'xyz']for v in row['points']])
def main():
 h=np.fromfile(WORK/'Watershed295/Generated/height.bytes','<f4').reshape(1501,1001)
 def ground(p):
  x,z=p[0]/4,p[2]/4;ix,iz=int(x),int(z);u,v=x-ix,z-iz;a,b,c,e=h[iz,ix],h[iz,ix+1],h[iz+1,ix],h[iz+1,ix+1];return float(a+(b-a)*u+(c-a)*v if u+v<=1 else e+(c-e)*(1-u)+(b-e)*(1-v))
 routes={r['id']:r for r in json.loads((WORK/'Watershed295/Generated/routes.json').read_text())['routes']};ledger=json.loads((WORK/'Architecture296/gates.json').read_text())['Loops'][0]['Gates'];gates={g['Id']:g for g in ledger}
 def redirect(a,gate):
  centre=np.array([gate['Centre'][k]for k in 'xyz']);inward=np.array([gate['Inward'][k]for k in 'xyz']);side=(a-centre)@inward;cross=next(i for i in range(1,len(a))if side[i-1]*side[i]<=0);first,last=cross-1,cross;distance=0
  while first>0 and distance<55:distance+=np.linalg.norm(a[first]-a[first-1]);first-=1
  distance=0
  while last<len(a)-1 and distance<55:distance+=np.linalg.norm(a[last]-a[last+1]);last+=1
  forward=inward*np.sign(side[last]-side[first]);before=centre-forward*8;after=centre+forward*8;before[1]=max(centre[1],ground(before)+.025);after[1]=max(centre[1],ground(after)+.025);out=[]
  def curve(p,dp,q,dq):
   dp=dp.copy();dq=dq.copy();dp[1]=dq[1]=0;length=np.linalg.norm((q-p)[[0,2]]);handle=min(20,length*.36);c=p+unit(dp)*handle;d=q-unit(dq)*handle;n=max(4,math.ceil(length*2))
   for i in range(0 if not out else 1,n+1):
    t=i/n;u=1-t;r=u*u*u*p+3*u*u*t*c+3*u*t*t*d+t*t*t*q;r[1]=max(ground(r)+.025,p[1]+(q[1]-p[1])*smooth(t));out.append(r)
  curve(a[first],a[first+1]-a[first],before,forward)
  for i in range(1,33):out.append(before+(after-before)*i/32)
  curve(after,forward,a[last],a[last]-a[last-1]);return np.concatenate([a[:first],out,a[last+1:]])
 def approaches(gate):
  centre=np.array([gate['Centre'][k]for k in 'xyz']);out=[]
  for rid in gate['Routes']:
   if rid not in routes:continue
   a=points(routes[rid]);a=redirect(a,gate) if gate['Id']=='south_gate' and rid=='capital_center__jeokro' else a
   near=np.linalg.norm(a-centre,axis=1).argmin();first=last=near;length=0
   while first>0 and length<60:length+=np.linalg.norm(a[first]-a[first-1]);first-=1
   length=0
   while last<len(a)-1 and length<60:length+=np.linalg.norm(a[last]-a[last+1]);last+=1
   a=a[first:last+1];ds=np.r_[0,np.cumsum(np.linalg.norm(np.diff(a[:,[0,2]],axis=0),axis=1))];out.append((a,ds,min(gate['Width']-.8,max(2.6,routes[rid]['width']))))
  return out
 profiles={gid:approaches(gates[gid])for gid in ['south_gate','capital_exit_2']}
 def surface(gid,p):
  gate=gates[gid];centre=np.array([gate['Centre'][k]for k in 'xyz']);inward=np.array([gate['Inward'][k]for k in 'xyz']);base=ground(p)+.035;support=base
  for a,ds,width in profiles[gid]:
   d=a[1:]-a[:-1];d[:,1]=0;t=np.clip(np.sum((p-a[:-1])*d,axis=1)/np.maximum(1e-4,np.sum(d*d,axis=1)),0,1);q=a[:-1]+(a[1:]-a[:-1])*t[:,None];dd=np.linalg.norm((q-p)[:,[0,2]],axis=1);j=dd.argmin();along=ds[j]+(ds[j+1]-ds[j])*t[j];edge=smooth((width/2-dd[j])/min(1.5,width*.4));ends=smooth(min(along,ds[-1]-along)/2.5);support=max(support,base+(max(base,q[j,1]+.01)-base)*edge*ends)
  blend=1-smooth((abs(np.dot(p-centre,inward))-1.25)/8.75);return max(support,support+(centre[1]+.025-support)*blend)
 sites=[('south_gate','south_gate__capital_center',[2001.205,89.559,2585.952],[2000.615,90.060,2585.861]),('south_gate','capital_center__jeokro',[1983.244,95.123,2507.345],[1983.682,95.613,2506.485]),('capital_exit_2','hyeongang__capital_center',[2228.705,73.152,3432.479],[2229.091,73.603,3432.485])];rows=[]
 for gid,rid,start,end in sites:
  start=np.array(start);end=np.array(end);direction=unit(np.r_[end[0]-start[0],0,end[2]-start[2]]);right=np.cross([0,1,0],direction);run=np.linalg.norm((end-start)[[0,2]]);rowsides=[]
  for across in [-.3,0,.3]:
   pts=start+np.linspace(0,run,101)[:,None]*direction+right*across;ys=np.array([surface(gid,p)for p in pts]);rises=np.diff(ys);rowsides.append(dict(lateral=across,startY=ys[0],targetY=ys[-1],maximumSlopeDegrees=float(np.degrees(np.arctan(np.max(abs(rises))/(run/100)))),maxRisePerSample=float(abs(rises).max())))
  rows.append(dict(route=rid,previousReportedDy=float(end[1]-start[1]),newProfiles=rowsides))
 out=dict(scope='Offline common height field at101 samples through each recorded failure, centre and +/-actual .3m controller radius. Preserves deterministic295-based rebuild. Exact source triangles, props and live CC are separate checks.',rows=rows)
 path=WORK/'Architecture296/Analysis/gate-approach-shoulders.json';path.write_text(json.dumps(out,indent=2),encoding='utf8');print(json.dumps(out,indent=2))
if __name__=='__main__':main()
