"""Isolated static comparison of contact-locked LBS versus volume-preserving target."""
from pathlib import Path
exec(compile(Path(__file__).with_name('repair_hand_self_folds.py').read_text().split('movable=')[0], 'hand_setup', 'exec'))
mod=next(m for m in hand.modifiers if m.type=='ARMATURE')
pose('grip_down');lbs=points();M=matrices();inverse=np.linalg.inv(M[:,:3,:3]);mod.use_deform_preserve_volume=True;bpy.context.view_layer.update();dq=points()
raw_dq={}
for pid in ['rest','open_hand','grip_down','grip_up','combined_reach']:
 pose(pid);raw_dq[pid]=len(crossings(points()))
mod.use_deform_preserve_volume=False;bpy.context.view_layer.update();delta=np.einsum('vij,vj->vi',inverse,dq-lbs)
results=[];best=None
for strength in [.25,.5,.75,1.,1.25,1.5]:
 candidate=original+delta*amount[:,None]*strength;set_corrective(candidate);row={'strength':strength,'maximumDelta':float(np.linalg.norm(candidate-original,axis=1).max())}
 for pid in ['grip_down','grip_up']:
  pose(pid);row[pid]=len(crossings(points()))
 results.append(row)
 score=row['grip_down']+row['grip_up']
 if best is None or score<best[0]:best=(score,candidate.copy(),row)
set_corrective(best[1]);pose('rest')
for o in brush_objects:bpy.data.objects.remove(o,do_unlink=True)
dest=ART/'DosaV2_HandsVolumeProbe.blend';bpy.ops.wm.save_as_mainfile(filepath=str(dest))
report={'rawDQ':raw_dq,'contactLockedDQBlend':results,'best':best[2],'candidate':str(dest)}
(OUT/'volume-probe.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
