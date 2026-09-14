import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Vector,Matrix
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig';fbx=O/'Exports/Player_C02_ReRig.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx),automatic_bone_orientation=False)
rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];rows=[];points=[]
for o in bpy.data.objects:
 if o.type!='MESH':continue
 o.data.calc_loop_triangles();points.extend(o.matrix_world@v.co for v in o.data.vertices)
 sums=[sum(g.weight for g in v.groups)for v in o.data.vertices]
 rows.append({'name':o.name,'tris':len(o.data.loop_triangles),'vertices':len(o.data.vertices),'armature_count':sum(m.type=='ARMATURE'for m in o.modifiers),'max_weights':max(len(v.groups)for v in o.data.vertices),'weight_sum_error':max(abs(s-1)for s in sums),'unassigned':sum(s<=0 for s in sums)})
r=rigs[0];fingers=[b.name for b in r.data.bones if any(f in b.name for f in ['Thumb','Index','Middle','Ring','Pinky'])];marks=[o.name for o in bpy.data.objects if o.name.startswith('POSE_')]
height=max(p.z for p in points)-min(p.z for p in points);total=sum(row['tris']for row in rows)
passed=len(rigs)==1 and len(fingers)==30 and total==59246 and 1.73<height<1.76 and all(row['armature_count']==1 and row['unassigned']==0 and row['weight_sum_error']<.0001 for row in rows)
report={'status':'PASS'if passed else'FAIL','sourceFbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),'height_m':height,'total_tris':total,'parts':rows,'armatures':len(rigs),'bones':len(r.data.bones),'finger_bones':len(fingers),'pose_markers':len(marks),'grip_reference':bpy.data.objects.get('GripReference_R')is not None,'animation_actions':len(bpy.data.actions),'scope':'FBX empty-scene structural reimport; runtime gestures are intentionally code-driven and not FBX animation actions.'}
(O/'Validation/fbx_roundtrip.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
bpy.ops.wm.save_as_mainfile(filepath=str(O/'Validation/Player_C02_Reimport_Check.blend'))
