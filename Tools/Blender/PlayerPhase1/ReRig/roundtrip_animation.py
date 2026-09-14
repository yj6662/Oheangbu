import bpy,json,numpy as np
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');s=bpy.context.scene;source=bpy.data.objects['Dosa_Phase1_Rig'];dg=bpy.context.evaluated_depsgraph_get()
before=set(bpy.data.objects);before_actions=set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=str(OUT/'Dosa_Phase1_Rerig.fbx'))
objects=set(bpy.data.objects)-before;actions=set(bpy.data.actions)-before_actions;target=next(o for o in objects if o.type=='ARMATURE');meshes={n:next(o for o in objects if o.type=='MESH' and o.name.startswith(n)) for n in ['Body','InnerTop','Durumagi']}
def points(o):
 e=o.evaluated_get(dg);me=e.to_mesh();result=np.array([e.matrix_world@v.co for v in me.vertices]);e.to_mesh_clear();return result
rows=[]
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing','HandFlex']:
 source.animation_data.action=bpy.data.actions['DIAG_'+label];end=int(source.animation_data.action.frame_range[1])
 found=[ac for ac in actions if ('|DIAG_'+label) in ac.name]
 if len(found)!=1:rows.append({'action':label,'status':'MISSING_OR_AMBIGUOUS','matches':len(found)});continue
 target.animation_data.action=found[0];target.animation_data.action_slot=found[0].slots[0]
 for frame in [1,max(2,end//2),end]:
  s.frame_set(frame);dg.update();parts={}
  for name,o in meshes.items():
   p=points(bpy.data.objects[name]);q=points(o);error=np.linalg.norm(p-q,axis=1)
   parts[name]={'max_vertex_error_m':float(error.max()),'mean_vertex_error_m':float(error.mean())}
  rows.append({'action':label,'frame':frame,'parts':parts,'status':'PASS' if max(v['max_vertex_error_m'] for v in parts.values())<.0005 else 'FAIL'})
result={'method':'Corresponding evaluated mesh vertices in world space after FBX re-import; start/middle/end of all six clips. Tolerance 0.5mm.','actions':len(actions),'results':rows,'status':'PASS' if len(rows)==18 and all(r['status']=='PASS' for r in rows) else 'FAIL'}
(OUT/'animation_roundtrip.json').write_text(json.dumps(result,indent=2));print(json.dumps({'status':result['status'],'rows':len(rows),'max_error':max((max(v['max_vertex_error_m'] for v in r.get('parts',{}).values()) for r in rows if r.get('parts')),default=0)}))
for o in objects:bpy.data.objects.remove(o,do_unlink=True)
for ac in actions:bpy.data.actions.remove(ac)
source.animation_data.action=None
for p in source.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
