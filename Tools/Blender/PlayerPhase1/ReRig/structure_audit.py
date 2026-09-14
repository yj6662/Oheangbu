import bpy,json,math
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');a=bpy.data.objects['Dosa_Phase1_Rig'];s=bpy.context.scene
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
bpy.context.view_layer.update();rows=[]
for name in ['Body','InnerTop','Durumagi']:
 o=bpy.data.objects[name];o.data.calc_loop_triangles();bad=0;unweighted=0;max_error=0;max_weights=0;lower_arm=0
 for v in o.data.vertices:
  gs=[g for g in v.groups if g.weight>0];max_weights=max(max_weights,len(gs));unweighted+=not gs
  max_error=max(max_error,abs(sum(g.weight for g in gs)-1))
  bad+=sum(not math.isfinite(g.weight) or g.weight<0 for g in v.groups)
  if name=='Durumagi' and v.co.z<1.02:lower_arm+=any(('Arm' in o.vertex_groups[g.group].name or 'Hand' in o.vertex_groups[g.group].name) for g in gs)
 rows.append({'part':name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials),'unweighted':unweighted,'bad_weights':bad,'max_influences':max_weights,'max_weight_sum_error':max_error,'lower_robe_arm_influences':lower_arm})
max_scale_error=0;max_connection_error=0;nan_transforms=0
for act in [bpy.data.actions.get('DIAG_'+x) for x in ['Walk','Run','ArmsUp','Squat','BrushSwing','HandFlex']]:
 if not act:continue
 a.animation_data.action=act
 for f in range(1,int(act.frame_range[1])+1):
  s.frame_set(f);bpy.context.view_layer.update()
  for p in a.pose.bones:
   max_scale_error=max(max_scale_error,max(abs(v-1) for v in p.matrix.to_scale()))
   nan_transforms+=sum(not math.isfinite(v) for row in p.matrix for v in row)
   if p.bone.use_connect and p.parent:max_connection_error=max(max_connection_error,(p.head-p.parent.tail).length)
result={'meshes':rows,'triangles':sum(r['triangles'] for r in rows),'bones':len(a.data.bones),'finger_bones':sum('Hand' in b.name and b.name[-1:].isdigit() for b in a.data.bones),'robe_bones':sum(b.name.startswith('Cloth_') for b in a.data.bones),'max_pose_scale_error':max_scale_error,'max_connected_joint_gap_m':max_connection_error,'nonfinite_transform_elements':nan_transforms,'actions_checked':6,'mesh_status':'PASS' if all(r['unweighted']==0 and r['bad_weights']==0 and r['max_influences']<=4 and r['max_weight_sum_error']<=.0001 and r['lower_robe_arm_influences']==0 for r in rows) else 'FAIL'}
(OUT/'rig_structure_audit.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
