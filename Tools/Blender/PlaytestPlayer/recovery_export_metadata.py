import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Motion'
bpy.ops.wm.open_mainfile(filepath=str(O/'Player_C02_AttachedMotions.blend'))
rig=bpy.data.objects['Armature'];scene=bpy.context.scene;names=[b.name for b in rig.data.bones]
def transform(m):
 p,q,s=m.decompose();return [*p,q.x,q.y,q.z,q.w]
sha=hashlib.sha256((O/'Player_C02_AttachedMotions.fbx').read_bytes()).hexdigest()
data={'fbxSha256':sha,'boneNames':names,'restPose':sum([transform(rig.matrix_world@b.matrix_local) for b in rig.data.bones],[]),'clips':[]}
skins=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
sole={}
for side in ['Left','Right']:
 rows=[]
 for skin in skins:
  group=skin.vertex_groups.get(side+'Foot')
  if group:
   rows.extend([((skin.matrix_world@v.co).z,skin.name,v.index) for v in skin.data.vertices if sum(g.weight for g in v.groups if g.group==group.index)>=.7])
 rows.sort();cut=rows[math.ceil(len(rows)*.15)][0];sole[side]=[r for r in rows if r[0]<=cut]
support={'fbxSha256':sha,'method':'C02 evaluated skinned sole vertices; support estimated from low-height samples and signed stance travel. Native terrain validation required.','clips':[]}
for a in sorted((a for a in bpy.data.actions if a.name.startswith('PT_')),key=lambda a:a.name):
 rig.animation_data.action=a
 if a.slots:rig.animation_data.action_slot=a.slots[0]
 start,end=a.frame_range;poses=[];samples=[]
 for i in range(121):
  f=start+(end-start)*i/120;scene.frame_set(math.floor(f),subframe=f%1);bpy.context.view_layer.update()
  for name in names:poses.extend(transform(rig.matrix_world@rig.pose.bones[name].matrix))
  dg=bpy.context.evaluated_depsgraph_get();meshes={}
  for s in skins:
   obj=s.evaluated_get(dg);meshes[s.name]=(obj,obj.to_mesh())
  row={'time':(f-start)/60}
  for side in ['Left','Right']:
   row[side.lower()]=min((meshes[n][0].matrix_world@meshes[n][1].vertices[idx].co).z for _,n,idx in sole[side])
   row[side.lower()+'Foot']=list((rig.matrix_world@rig.pose.bones[side+'Foot'].matrix).translation)
  samples.append(row)
  for obj,mesh in meshes.values():obj.to_mesh_clear()
 for side in ['left','right']:
  low=min(s[side] for s in samples)
  direction=Vector((1,0,0)) if a.name=='PT_CrouchLeft' else Vector((-1,0,0)) if a.name=='PT_CrouchRight' else Vector((0,1,0)) if a.name=='PT_CrouchBack' else Vector((0,-1,0))
  moving=a.name in ['PT_WalkForward','PT_RunForward','PT_CrouchForward','PT_CrouchBack','PT_CrouchLeft','PT_CrouchRight']
  dt=(end-start)/60/120
  for i,s in enumerate(samples):
   j=max(0,i-1);k=min(120,i+1);velocity=(Vector(samples[k][side+'Foot'])-Vector(samples[j][side+'Foot']))/max(.0001,(k-j)*dt)
   s[side+'Support']=s[side]<low+.035 and (not moving or velocity.dot(direction)<-.05)
   s[side+'SignedSupportVelocity']=velocity.dot(direction)
 data['clips'].append({'name':a.name[3:],'duration':(end-start)/60,'poses':poses})
 support['clips'].append({'name':a.name[3:],'duration':(end-start)/60,'samples':samples})
 print('METADATA',a.name,min(s['left'] for s in samples),min(s['right'] for s in samples),flush=True)
(O/'AuthoredBoneMotion.json').write_text(json.dumps(data,separators=(',',':')))
(O/'AuthoredMotionSupport.json').write_text(json.dumps(support,separators=(',',':')))
