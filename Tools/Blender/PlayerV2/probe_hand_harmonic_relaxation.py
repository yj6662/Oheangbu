"""Local, contact-locked harmonic surface fairing; every candidate is independently audited."""
from pathlib import Path
exec(compile(Path(__file__).with_name('repair_hand_self_folds.py').read_text().split('movable=')[0], 'hand_setup', 'exec'))
pose('grip_down');base=points();M=matrices();inverse=np.linalg.inv(M[:,:3,:3]);edge=np.array([(i,j) for i,row in enumerate(neighbors) for j in row]);degree=np.bincount(edge[:,0],minlength=n)
samples=[];best=None
for cap in [.006,.010,.015]:
 posed=base.copy()
 for iteration in range(1,401):
  mean=np.zeros_like(posed);np.add.at(mean,edge[:,0],posed[edge[:,1]]);mean/=np.maximum(degree,1)[:,None]
  proposed=posed+(mean-posed)*.45*amount[:,None]
  disp=proposed-base;length=np.linalg.norm(disp,axis=1);disp*=np.minimum(1,cap/np.maximum(length,1e-15))[:,None];posed=base+disp
  if iteration not in [10,25,50,100,200,400]:continue
  candidate=original+np.einsum('vij,vj->vi',inverse,posed-base);candidate[lock]=original[lock];set_corrective(candidate)
  row={'iterations':iteration,'capMeters':cap,'maxRestDelta':float(np.linalg.norm(candidate-original,axis=1).max())}
  for pid in ['grip_down','grip_up']:pose(pid);row[pid]=len(crossings(points()))
  samples.append(row);score=row['grip_down']+row['grip_up']
  if best is None or score<best[0]:best=(score,candidate.copy(),row)
set_corrective(best[1]);pose('rest')
for o in brush_objects:bpy.data.objects.remove(o,do_unlink=True)
dest=ART/'DosaV2_HandsHarmonicProbe.blend';bpy.ops.wm.save_as_mainfile(filepath=str(dest))
report={'samples':samples,'best':best[2],'candidate':str(dest)};(OUT/'harmonic-probe.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
