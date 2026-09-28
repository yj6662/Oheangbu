"""Read-only Hyeongang bay-mouth search; runtime Physics remains authoritative."""
from pathlib import Path
from datetime import datetime, timezone
import sys, hashlib, json, math
import numpy as np
from build_watershed295 import triangle_sample, sample, v3
from find_watershed295_mum_sites import normal_y

WORK=Path(__file__).resolve().parents[2]/'Art/World/Compact/Rebuild/Watershed295'
FOLDER=WORK/'Generated'


def main():
 h=np.fromfile(FOLDER/'height.bytes','<f4').reshape(1501,1001)
 water=np.fromfile(FOLDER/'waterlevel.bytes','<f4').reshape(h.shape)
 wet=(abs(water-150)<.001)&(h<150);near=np.zeros_like(wet)
 for dz in range(-5,6):
  for dx in range(-5,6):
   near[max(0,dz):min(1501,1501+dz),max(0,dx):min(1001,1001+dx)]|=wet[max(0,-dz):min(1501,1501-dz),max(0,-dx):min(1001,1001-dx)]
 grid=float(sys.argv[1]) if len(sys.argv)>1 else 2.
 if grid>0:
  xs,zs=np.meshgrid(np.arange(1050,2751,grid),np.arange(4100,5521,grid));q=np.column_stack([xs.ravel(),zs.ravel()])
  y=triangle_sample(h,q[:,0],q[:,1]);n=normal_y(h,q)
  valid=(y>=150.03)&(y<=155)&(n>=math.cos(math.radians(20)))&(sample(near.astype(float),q[:,0],q[:,1])>.1)
  q,y=q[valid],y[valid];print('bank grid candidates',len(q),flush=True)
  cells={}
  for i,key in enumerate(np.floor(q/40).astype(int)):cells.setdefault(tuple(key),[]).append(i)
  chunks=[]
  for (cx,cz),group in cells.items():
   nearby=[j for dz in [-1,0,1] for dx in [-1,0,1] for j in cells.get((cx+dx,cz+dz),[])]
   left=np.array(group)[:,None];right=np.array(nearby)[None,:]
   d=q[left]-q[right];keep=(left<right)&(np.sum(d*d,axis=2)<=1600)
   rows,cols=np.where(keep)
   if len(rows):chunks.append(np.column_stack([left[:,0][rows],right[0][cols]]))
  pairs=np.concatenate(chunks);a,b=q[pairs[:,0]],q[pairs[:,1]]
 else:
  previous=json.loads((WORK/'Analysis/mum-hyeongang-lake-necks.json').read_text(encoding='utf8'))
  rng=np.random.default_rng(29504647);aa=[];bb=[]
  for site in previous['nearestLandingRejected']:
   aa.append(np.array([site['start']['x'],site['start']['z']])+rng.uniform(-4,4,(40000,2)))
   bb.append(np.array([site['end']['x'],site['end']['z']])+rng.uniform(-4,4,(40000,2)))
  a,b=np.concatenate(aa),np.concatenate(bb);q=np.concatenate([a,b]);y=triangle_sample(h,q[:,0],q[:,1]);pairs=np.column_stack([np.arange(len(a)),np.arange(len(a))+len(a)])
 delta=b-a;length=np.linalg.norm(delta,axis=1);rise=abs(y[pairs[:,0]]-y[pairs[:,1]])
 mid=(a+b)/2;my=triangle_sample(h,mid[:,0],mid[:,1]);mw=sample(water,mid[:,0],mid[:,1])
 valid=(length>=4)&(length<=40)&(rise<=2)&(rise<=length*math.tan(math.radians(8)))&(abs(mw-150)<.001)&(my<150-.2)
 pairs=pairs[valid];a,b=a[valid],b[valid];length=length[valid];direction=(b-a)/length[:,None];side=np.column_stack([-direction[:,1],direction[:,0]])
 print('opposite-bank pairs',len(pairs),flush=True)
 valid=np.ones(len(pairs),bool);minnormal=np.ones(len(pairs));spread=np.zeros(len(pairs));lowest=np.full(len(pairs),1e9)
 for bank,idx,sign in [(a,pairs[:,0],-1),(b,pairs[:,1],1)]:
  for across in [-1.3,0,1.3]:
   for back in [0,.6]:
    p=bank+side*across+direction*sign*back;py=triangle_sample(h,p[:,0],p[:,1]);pn=normal_y(h,p)
    valid&=(pn>=math.cos(math.radians(20)))&(abs(py-y[idx])<=.18)&(py>=150.025)
    minnormal=np.minimum(minnormal,pn);spread=np.maximum(spread,abs(py-y[idx]))
    lowest=np.minimum(lowest,py)
 ids=np.where(valid)[0];print('six-sample landing pairs',len(ids),flush=True)
 passed=[];rejected=[]
 for i in ids:
  L=length[i];steps=np.arange(0,L+.001,.1);steps=np.unique(np.r_[steps,L,.6,L-.6,2.5,L-2.5]);steps=steps[(steps>=0)&(steps<=L)]
  p=a[i]+direction[i]*steps[:,None];deck=y[pairs[i,0]]+.025+(y[pairs[i,1]]-y[pairs[i,0]])*steps/L
  top=np.full(len(steps),-1e9)
  for across in [-1.37,0,1.37]:
   at=p+side[i]*across;top=np.maximum(top,triangle_sample(h,at[:,0],at[:,1])-deck)
  landing=(steps<.6)|(steps>L-.6);middle=(steps>=min(2.5,L/4))&(steps<=L-min(2.5,L/4))
  topbad=float(np.max(top[~landing]));endbad=float(np.max(top[landing]));under=float(np.min(-top[middle]-.45))
  record=dict(id=f'hyeongang_lake_neck_{i}',realm='Hyeongang',start=v3(a[i,0],float(y[pairs[i,0]]),a[i,1]),end=v3(b[i,0],float(y[pairs[i,1]]),b[i,1]),length=float(L),endDelta=float(abs(y[pairs[i,0]]-y[pairs[i,1]])),minimumBankNormalY=float(minnormal[i]),maximumLandingSpread=float(spread[i]),interiorGroundAboveDeck=topbad,endpointGroundAboveDeck=endbad,centralUndersideClearance=under)
  if topbad<=0 and endbad<=.18 and under>=0:passed.append(record)
  else:rejected.append(record)
 passed.sort(key=lambda x:(-x['centralUndersideClearance'],x['maximumLandingSpread']))
 rejected.sort(key=lambda x:max(x['interiorGroundAboveDeck'],x['endpointGroundAboveDeck']-.18,-x['centralUndersideClearance']))
 score=np.maximum.reduce([math.cos(math.radians(20))-minnormal,spread-.18,150.025-lowest]);best=np.argsort(score)[:5]
 landingRejects=[dict(start=v3(a[i,0],float(y[pairs[i,0]]),a[i,1]),end=v3(b[i,0],float(y[pairs[i,1]]),b[i,1]),normal=float(minnormal[i]),spread=float(spread[i]),lowestSupport=float(lowest[i])) for i in best]
 result=dict(checkedUtc=datetime.now(timezone.utc).isoformat(),sourceHeightSha256=hashlib.sha256((FOLDER/'height.bytes').read_bytes()).hexdigest(),grid=grid,bankGridCandidates=len(q),oppositeBankPairs=len(pairs),landingPairs=len(ids),sites=passed[:30],nearestRejected=rejected[:30],nearestLandingRejected=landingRejects,method='Endpoint grid around level150 lake; exact4m terrain triangles; 4..40m span, <=2m rise/8degrees; six landing samples across+-1.3m/back.6m, normal cos20/.18m spread; width2.74m/.1m longitudinal fullspan top; only first/last.6m landing tolerance.18m, strict.45m underside outside2.5m end embed (clampedL/4). Actual scene props/Physics unverified.')
 filename='mum-hyeongang-lake-necks.json' if grid>0 else 'mum-hyeongang-lake-refinement.json'
 (WORK/'Analysis'/filename).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
 print('strict passes',len(passed),'closest rejects',json.dumps(rejected[:3]),flush=True)


if __name__=='__main__':main()
