"""Bounded deterministic coordinate search on the actual hand skinning, no rendering."""
import numpy as np,json,math,time
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/PlaytestReRig')
d=np.load(OUT/'Validation/contact_input.npz');v=d['vertices'];weights=d['weights'];rest=d['rest'];parents=d['parents'];names=list(d['names']);inverse=np.linalg.inv(rest)
restrel=np.array([inverse[p]@rest[i]if p>=0 else rest[i]for i,p in enumerate(parents)])
hom=np.concatenate([v,np.ones((len(v),1))],axis=1)
spec=json.loads((OUT/'Validation/hand_rig_draft.json').read_text())['specs'];fingers=['Index','Middle','Ring','Pinky','Thumb'];indices=[[names.index('RightHand'+f+str(i))for i in range(1,4)]for f in fingers]
axes={i:np.linalg.inv(rest[i,:3,:3])@np.array([0.,-1.,0.])for row in indices for i in row};opp=np.linalg.inv(rest[indices[-1][0],:3,:3])@np.array([-1.,0.,0.])
def rot(axis,a):
 a=np.deg2rad(a);axis=axis/np.linalg.norm(axis);x,y,z=axis;c=np.cos(a);s=np.sin(a);C=1-c
 return np.array([[x*x*C+c,x*y*C-z*s,x*z*C+y*s,0],[y*x*C+z*s,y*y*C+c,y*z*C-x*s,0],[z*x*C-y*s,z*y*C+x*s,z*z*C+c,0],[0,0,0,1.]])
def evaluate(params):
 mats=np.empty_like(rest);extra={i:rot(axes[i],params[f*3+j])for f,row in enumerate(indices)for j,i in enumerate(row)};extra[indices[-1][0]]=rot(opp,params[15])@extra[indices[-1][0]]
 for i,p in enumerate(parents):mats[i]=(mats[p]@restrel[i]if p>=0 else restrel[i])@extra.get(i,np.eye(4))
 sk=mats@inverse;xyz=np.zeros((len(v),3))
 for i in np.where(weights.max(0)>0)[0]:xyz+=((hom@sk[i].T)[:,:3])*weights[:,i,None]
 return xyz*.01,extra
# Contact patch is the distal skin of each original finger, not a socket.
patch=[]
for f,row in zip(fingers,indices):
 s=spec['Right'+f];a=np.array(s['start']);z=np.array(s['end']);axis=(z-a)/np.linalg.norm(z-a);u=((v-a)@axis)/np.linalg.norm(z-a)
 member=weights[:,row].sum(1)>.7
 patch.append(np.where(member&(u>.78)&(v[:,2]<np.median(v[member,2])))[0])
shaft=np.load(OUT/'Validation/shaft_input.npz')['vertices'];rad=np.linalg.norm(shaft[:,:2],axis=1)
# Actual shaft surface samples in the grip neighborhood; angular/longitudinal bins.
grid=np.zeros((18,24));zs=np.linspace(-.1,.1,18);angles=np.linspace(-math.pi,math.pi,24,endpoint=False);theta=np.arctan2(shaft[:,1],shaft[:,0])
for zi,z in enumerate(zs):
 for ai,a in enumerate(angles):
  ds=((shaft[:,2]-z)/.012)**2+(np.arctan2(np.sin(theta-a),np.cos(theta-a))/.27)**2
  ii=np.argpartition(ds,3)[:4];ww=1/np.maximum(ds[ii],.01);grid[zi,ai]=(rad[ii]*ww).sum()/ww.sum()
def distance(xyz,par):
 # Brush +Z -> world -Y, +X->world +X, +Y->world+Z.
 x=xyz[:,0]-par[16];y=xyz[:,2]-par[17];z=-(xyz[:,1]+.025)
 a=np.arctan2(y,x);ti=(a+math.pi)/(2*math.pi)*24;ii=np.floor(ti).astype(int)%24;fr=ti-np.floor(ti)
 zi=np.clip((z+.1)/.2*17,0,16.999);jj=zi.astype(int);fz=zi-jj
 radius=(grid[jj,ii]*(1-fr)+grid[jj,(ii+1)%24]*fr)*(1-fz)+(grid[jj+1,ii]*(1-fr)+grid[jj+1,(ii+1)%24]*fr)*fz
 return np.sqrt(x*x+y*y)-radius
initial=np.array([60,80,50]*4+[20,30,20,35,-.779,1.345],float)
def objective(par,details=False):
 xyz,_=evaluate(par);dist=distance(xyz,par);penetration=np.maximum(0,-dist-.0002)
 gaps=np.array([np.quantile(dist[p],.18)if len(p) else 1 for p in patch])
 value=12000*np.mean(penetration**2)+30*np.mean(gaps**2)+.000000001*np.sum((par[:16]-initial[:16])**2)
 if details:return {'objective':value,'min_signed_distance':float(dist.min()),'pad_gaps':gaps.tolist(),'max_penetration':float(np.maximum(0,-dist).max()),'patch_counts':[len(p)for p in patch]}
 return value
lo=np.array([0,15,0]*4+[-40,0,0,-80,-.825,1.313]);hi=np.array([85,110,100]*4+[90,100,90,100,-.77,1.365])
start=time.monotonic();rng=np.random.default_rng(20260914);winner=None;winning=1e6
for attempt in range(14):
 p=initial.copy()
 if attempt:
  p[:12]=rng.uniform([5,45,25]*4,[60,95,80]*4)
  p[12:16]=rng.uniform([-10,0,0,0],[40,45,45,70]);p[16]=rng.uniform(-.802,-.776);p[17]=rng.uniform(1.330,1.35)
 best=objective(p)
 for step in [15,8,4,2,1,.4]:
  for it in range(20):
   improved=False
   for i in list(range(16))+list(range(16,18)):
    delta=step*(.00022 if i>=16 else 1)
    for sign in [-1,1]:
     q=p.copy();q[i]=np.clip(q[i]+sign*delta,lo[i],hi[i]);score=objective(q)
     if score<best:p=q;best=score;improved=True
   if not improved:break
 if best<winning:winning=best;winner=p.copy()
p=winner
xyz,extra=evaluate(p)
report={'status':'CALIBRATED_RADIAL_ESTIMATE_NOT_RIG_PASS','parameters':p.tolist(),'before':objective(initial,True),'after':objective(p,True),'seconds':time.monotonic()-start,'method':'Actual weighted hand vertices and angular/depth-binned actual shaft mesh. Final exact BVH inspection required.','bone_matrices':{names[i]:m.tolist()for i,m in extra.items()}}
(OUT/'Validation/contact_fit.json').write_text(json.dumps(report,indent=2));np.save(OUT/'Validation/grip_vertices.npy',xyz);print(json.dumps({k:v for k,v in report.items()if k!='bone_matrices'},indent=2))
