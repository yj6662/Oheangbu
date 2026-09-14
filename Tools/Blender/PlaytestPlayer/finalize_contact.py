"""Calibrate a real brush grip on the C02 hand skin. Small rest-skin correction only."""
import bpy,json,math,hashlib,numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_FingerRig.blend'))
r=bpy.data.objects['Armature'];r.animation_data_clear();r.data.pose_position='POSE';body=bpy.data.objects['C02_Mesh_0']
fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters'];spec=json.loads((O/'Validation/hand_rig_draft.json').read_text())['specs']
for p in r.pose.bones:p.matrix_basis.identity()
for name,m in fit['bone_matrices'].items():r.pose.bones[name].matrix_basis=Matrix(m)
bpy.context.view_layer.update();original={v.index:v.co.copy()for v in body.data.vertices if v.co.x<-69}
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'))
new=[o for o in bpy.data.objects if o not in before];tx=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X')
for o in new:
 if not o.parent:o.matrix_world=tx@o.matrix_world
bpy.context.view_layer.update();h=bpy.data.objects['DosaBrushV2_Handle'];h.data.calc_loop_triangles()
bvh=BVHTree.FromPolygons([h.matrix_world@v.co for v in h.data.vertices],[t.vertices[:]for t in h.data.loop_triangles],all_triangles=True)
def surface(p):
 axis=Vector((par[16],p.y,par[17]));rad=p-axis;n=rad.normalized()
 hit,normal,idx,dist=bvh.ray_cast(axis+n*.08,-n,.16)
 if hit is None:return None,None,None
 return (p-axis).length-(hit-axis).length,hit,n
def skin_matrix(v):
 m=Matrix(((0,0,0,0),)*4)
 for g in v.groups:
  name=body.vertex_groups[g.group].name
  if name in r.pose.bones:m+=r.pose.bones[name].matrix@r.data.bones[name].matrix_local.inverted()*g.weight
 return body.matrix_world@m
def posed():
 bpy.context.view_layer.update();ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();out={i:body.matrix_world@me.vertices[i].co for i in original};ev.to_mesh_clear();return out
patches={}
for f in ['Thumb','Index','Middle','Ring','Pinky']:
 sp=spec['Right'+f];a=Vector(sp['start']);b=Vector(sp['end']);ids=[]
 for i,co in original.items():
  influence=sum(g.weight for g in body.data.vertices[i].groups if body.vertex_groups[g.group].name in sp['names'])
  u=(co-a).dot((b-a).normalized())/(b-a).length
  if influence>.7 and u>.75:ids.append(i)
 patches[f]=ids
for iteration in range(3):
 points=posed()
 for i,p in points.items():
  distance,hit,n=surface(p)
  if distance is not None and distance<.00025:
   target=hit+n*.0003;delta=skin_matrix(body.data.vertices[i]).inverted()@target-original[i]
   # Hard cap prevents hiding anatomical defects through large skin edits.
   if delta.length>.35:continue
   body.data.vertices[i].co=original[i]+delta
 body.data.update()
 # Bring the closest continuous pad patch to 0.6 mm on each digit.
 points=posed()
 for f,ids in patches.items():
  rows=[(surface(points[i])[0],i)for i in ids if surface(points[i])[0]is not None]
  rows.sort()
  if not rows:continue
  gap,centerid=rows[0]
  if gap>.0006 and gap<.003:
   center=points[centerid]
   for i in ids:
    d,hit,n=surface(points[i]);fade=max(0,1-(points[i]-center).length/.006)
    if fade>0:
     target=points[i]-n*(gap-.0006)*fade
     candidate=skin_matrix(body.data.vertices[i]).inverted()@target
     if (candidate-original[i]).length<=.35:body.data.vertices[i].co=candidate
 body.data.update()
# Vertex clearance alone misses a long face cutting across the curved shaft.
# Solve the interior of each nearby triangle, moving its skin locally through
# the inverse skinning matrix. No changes to the brush, finger lengths or pose.
body.data.calc_loop_triangles()
triangles=[tuple(t.vertices) for t in body.data.loop_triangles if all(i in original for i in t.vertices)]
inverse_skin={i:skin_matrix(body.data.vertices[i]).inverted() for i in original}
surface_iterations=[]
for iteration in range(10):
 points=posed();pushes={};worst=0.;bad=0
 for ids in triangles:
  a,b,c=[points[i] for i in ids]
  if min(p.x for p in (a,b,c))>par[16]+.05 or max(p.x for p in (a,b,c))<par[16]-.05:continue
  if min(p.z for p in (a,b,c))>par[17]+.05 or max(p.z for p in (a,b,c))<par[17]-.05:continue
  steps=max(1,math.ceil(max((a-b).length,(b-c).length,(c-a).length)/.001))
  deepest=0.;direction=None
  for u in range(steps+1):
   for v in range(steps+1-u):
    p=a+(b-a)*(u/steps)+(c-a)*(v/steps);distance,hit,n=surface(p)
    if distance is not None and distance<deepest:deepest=distance;direction=n
  worst=max(worst,-deepest)
  if deepest<-.00025:
   bad+=1;push=direction*(-deepest+.0003)*.85
   for i in ids:
    if i not in pushes or pushes[i].length<push.length:pushes[i]=push
 surface_iterations.append({'iteration':iteration,'penetration_m':worst,'faces_corrected':bad})
 print('SURFACE_SOLVE',surface_iterations[-1],flush=True)
 if not pushes:break
 for i,push in pushes.items():
  candidate=inverse_skin[i]@(points[i]+push)
  if (candidate-original[i]).length<=.60:body.data.vertices[i].co=candidate
 body.data.update()
points=posed();signed={i:surface(p)[0]for i,p in points.items()if surface(p)[0]is not None}
gaps={f:min(abs(signed[i])for i in ids if i in signed)for f,ids in patches.items()}
maxpen=max(0,-min(signed.values()));changes=[(v.co-original[i]).length*.01 for i in original for v in [body.data.vertices[i]]]
report={'status':'HAND_CONTACT_NUMERIC_PASS'if maxpen<=.0005 and all(gaps[f]<=.0015 for f in ['Thumb','Index','Middle'])else'HAND_CONTACT_FAIL','method':'Actual shaft mesh radial ray intersections; hand skin after linear blend skinning. Closest distal skin contact, not socket distance. Separate dense triangle sampling report is required.','max_penetration_m':maxpen,'closest_pad_gap_m':gaps,'max_rest_skin_correction_m':max(changes),'corrected_vertices':sum(d>1e-7 for d in changes),'surface_correction_iterations':surface_iterations,'shaft_scale':[1,1,1],'source_fbx':str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx')}
(O/'Validation/hand_contact.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
# Update the exported relative pose markers through Blender's own parent transforms.
for p in r.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
for name,m in fit['bone_matrices'].items():
 ob=bpy.data.objects.get('POSE_Grip_'+name)
 if ob:ob.matrix_world=r.matrix_world@r.data.bones[name].matrix_local@Matrix(m)
g=bpy.data.objects['GripReference_R'];g.matrix_world=bpy.data.objects['GripSocket'].matrix_world.copy()
# Mirror the calibrated local finger gestures to the other anatomical hand.
for f in ['Index','Middle','Ring','Pinky','Thumb']:
 for j in range(1,4):
  right='RightHand'+f+str(j);left='LeftHand'+f+str(j)
  rb=r.data.bones[right].matrix_local.to_3x3();lb=r.data.bones[left].matrix_local.to_3x3();reflect=Matrix.Diagonal(Vector((-1,1,1)))
  world=rb@Matrix(fit['bone_matrices'][right]).to_3x3()@rb.inverted();local=lb.inverted()@reflect@world@reflect@lb
  ob=bpy.data.objects['POSE_Grip_'+left];ob.matrix_world=r.matrix_world@r.data.bones[left].matrix_local@local.to_4x4()
for obj in new:bpy.data.objects.remove(obj,do_unlink=True)
bpy.context.view_layer.update();bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(O/'Work/Player_C02_ReRig.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in before:
 if o.type in ['ARMATURE','MESH','EMPTY'] and o.name not in ['Ground','Floor']:o.select_set(True)
bpy.context.view_layer.objects.active=r
bpy.ops.export_scene.fbx(filepath=str(O/'Exports/Player_C02_ReRig.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
print('FINAL_CANDIDATE_EXPORTED',hashlib.sha256((O/'Exports/Player_C02_ReRig.fbx').read_bytes()).hexdigest(),flush=True)
