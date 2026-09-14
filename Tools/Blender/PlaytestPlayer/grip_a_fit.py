"""A grasp: bounded flexed distal joints, opposed thumb, actual weighted skin/shaft."""
from pathlib import Path
# Reuse the exact existing skinning and actual-shaft estimator, not its previous pose.
source=Path(__file__).with_name('calibrate_contact.py').read_text()
source=source.split('initial=np.array')[0].replace("Art/PlayerPhase1/PlaytestReRig","Art/PlaytestPolish/Hands")
exec(compile(source,'calibrate_contact_numerical_kernel','exec'))
old=json.loads(Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/PlaytestReRig/Validation/contact_fit.json').read_text())
target=np.array([25,90,50,25,90,45,30,80,45,30,75,40,30,15,12,50,old['parameters'][16],old['parameters'][17]],float)
lo=np.array([5,55,25]*4+[8,8,5,25,-.808,1.316]);hi=np.array([65,110,80]*4+[60,50,40,85,-.767,1.357])
def objective(par,detail=False):
 xyz,_=evaluate(par);dist=distance(xyz,par);penetration=np.maximum(0,-dist-.00015)
 # A wraps the shaft. Penalize a fingertip hovering away as well as penetration;
 # require visibly flexed distal joints instead of the old nearly straight index.
 gaps=np.array([np.quantile(dist[p],.12)if len(p)else 1 for p in patch])
 prior=np.mean(((par[:16]-target[:16])/60)**2)
 value=16000*np.mean(penetration**2)+55*np.mean((gaps-.0006)**2)+.000020*prior
 if detail:return {'objective':float(value),'max_penetration':float(np.maximum(0,-dist).max()),'pad_gaps':gaps.tolist(),'anatomical_prior':float(prior)}
 return value
start=time.monotonic();rng=np.random.default_rng(2026091401);winner=None;winning=1e6
for attempt in range(5):
 p=np.clip(target.copy()if attempt==0 else np.array(old['parameters']),lo,hi)
 if attempt>1:p[:16]+=rng.uniform(-8,8,16);p=np.clip(p,lo,hi)
 best=objective(p)
 for step in [10,5,2,1,.4]:
  for iteration in range(12):
   improved=False
   for i in range(18):
    delta=step*(.00022 if i>=16 else 1)
    for sign in [-1,1]:
     q=p.copy();q[i]=np.clip(q[i]+sign*delta,lo[i],hi[i]);score=objective(q)
     if score<best:p=q;best=score;improved=True
   if not improved:break
 if best<winning:winning=best;winner=p.copy()
 print('A_FIT',attempt,best,time.monotonic()-start,flush=True)
p=winner;xyz,extra=evaluate(p)
report={'status':'A_GRIP_NUMERIC_ESTIMATE_REQUIRES_SURFACE_CHECK','selection':'Grip_A.png','parameters':p.tolist(),'target_parameters':target.tolist(),
 'before':objective(np.array(old['parameters']),True),'after':objective(p,True),'seconds':time.monotonic()-start,
 'method':'Existing fixed bind skeleton; new constrained wrap pose from exact weighted hand vertices and angular/depth-binned actual shaft. Exact triangle sampling remains required.',
 'bone_matrices':{names[i]:m.tolist()for i,m in extra.items()}}
(OUT/'Validation/contact_fit.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items()if k!='bone_matrices'},indent=2))
