import bpy,json,math,statistics
from pathlib import Path
from mathutils import Vector,Matrix
R=Path('C:/Users/yj666/Oheangbu'); O=R/'Art/PlaytestRecovery/NaturalLocomotion'; O.mkdir(parents=True,exist_ok=True)
s=Path(__file__).with_name('recovery_retarget.py').read_text()
exec(s[:s.index("report={'status'")])
out={'targetRest':{n:list(rest[n].translation) for n in ['Hips','LeftUpLeg','RightUpLeg','LeftLeg','RightLeg','LeftFoot','RightFoot']},'clips':[]}
for path in sorted((R/'Art/PlaytestRecovery/NaturalLocomotion/Sources').glob('*.fbx')):
 existing=set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=str(path),automatic_bone_orientation=False)
 added=set(bpy.data.objects)-existing;src=next(o for o in added if o.type=='ARMATURE');a=src.animation_data.action
 by={b.name.split(':')[-1]:b for b in src.pose.bones}; sr={b.name.split(':')[-1]:src.matrix_world@b.matrix_local for b in src.data.bones}
 convert=targetFrame@frame_of(sr).inverted(); scale=(rest['Hips'].translation.z-rest['LeftFoot'].translation.z)/(sr['Hips'].translation.z-sr['LeftFoot'].translation.z)
 rows=[];start,end=a.frame_range
 for i in range(41):
  f=start+(end-start)*i/40;scene.frame_set(math.floor(f),subframe=f%1)
  sm={n:src.matrix_world@b.matrix for n,b in by.items()}
  row={'frame':f,'hip':list(sm['Hips'].translation),'left':list(sm['LeftFoot'].translation),'right':list(sm['RightFoot'].translation)}
  row['sourceWidthScaled']=abs((convert@(sm['LeftFoot'].translation-sm['RightFoot'].translation)).x)*scale
  for corrected in [False,True]:
   pose={}
   for b in bones:
    n=b.name;sn=alias.get(n,n)
    q=(convert@(sm[sn].to_quaternion()@sr[sn].to_quaternion().inverted())@convert.inverted())@rest[n].to_quaternion() if sn in sm else rest[n].to_quaternion()
    child=n.replace('UpLeg','Leg') if n.endswith('UpLeg') else n.replace('Leg','Foot') if n.endswith('Leg') else n.replace('Foot','ToeBase') if n.endswith('Foot') else None
    if corrected and child and child in sm:
     old=q@rest[n].to_quaternion().inverted()@(rest[child].translation-rest[n].translation)
     new=convert@(sm[child].translation-sm[sn].translation)
     q=old.rotation_difference(new)@q
    head=rest[n].translation if not b.parent else pose[b.parent.name]@rest[b.parent.name].inverted()@rest[n].translation
    pose[n]=Matrix.LocRotScale(head,q,Vector((1,1,1)))
   row['correctedWidth' if corrected else 'oldWidth']=abs(pose['LeftFoot'].translation.x-pose['RightFoot'].translation.x)
  rows.append(row)
 out['clips'].append({'file':path.name,'range':[start,end],'duration':(end-start)/60,'scale':scale,'medianWidths':{n:statistics.median(r[n] for r in rows) for n in ['sourceWidthScaled','oldWidth','correctedWidth']},'samples':rows})
 for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
 print('AUDIT',path.name,out['clips'][-1]['medianWidths'],flush=True)
(R/'Art/PlaytestRecovery/NaturalLocomotion/source_audit.json').write_text(json.dumps(out,indent=2))
