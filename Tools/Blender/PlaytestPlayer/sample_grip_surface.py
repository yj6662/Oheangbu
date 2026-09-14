"""Dense triangle samples at <=0.5mm spacing; selected static grip, not all motion."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig';bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_ReRig.blend'))
r=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0'];fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters'];r.animation_data_clear()
for p in r.pose.bones:p.matrix_basis.identity()
for n,m in fit['bone_matrices'].items():r.pose.bones[n].matrix_basis=Matrix(m)
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'));tx=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X')
for o in [o for o in bpy.data.objects if o not in before and not o.parent]:o.matrix_world=tx@o.matrix_world
bpy.context.view_layer.update();handle=bpy.data.objects['DosaBrushV2_Handle'];handle.data.calc_loop_triangles();bvh=BVHTree.FromPolygons([handle.matrix_world@v.co for v in handle.data.vertices],[t.vertices[:]for t in handle.data.loop_triangles],all_triangles=True)
ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();mesh.calc_loop_triangles();world=[body.matrix_world@v.co for v in mesh.vertices];total=misses=0;worst=0.;worstp=None;triangles=0
for t in mesh.loop_triangles:
 if not all(body.data.vertices[i].co.x<-69 for i in t.vertices):continue
 a,b,c=[world[i]for i in t.vertices]
 # Skip distant wrist skin only when the entire face bound is beyond the shaft neighborhood.
 if min(p.x for p in (a,b,c))>par[16]+.08 or max(p.x for p in (a,b,c))<par[16]-.08:continue
 if min(p.z for p in (a,b,c))>par[17]+.08 or max(p.z for p in (a,b,c))<par[17]-.08:continue
 steps=max(1,math.ceil(max((a-b).length,(b-c).length,(c-a).length)/.0005));triangles+=1
 for i in range(steps+1):
  for j in range(steps+1-i):
   p=a+(b-a)*(i/steps)+(c-a)*(j/steps);axis=Vector((par[16],p.y,par[17]));rad=p-axis;n=rad.normalized();hit,normal,index,distance=bvh.ray_cast(axis+n*.08,-n,.16);total+=1
   if hit is None:misses+=1;continue
   penetration=max(0,(hit-axis).length-rad.length)
   if penetration>worst:worst=penetration;worstp=list(p)
ev.to_mesh_clear();report={'status':'PASS_SAMPLED_STATIC_SURFACE'if worst<=.0005 and misses==0 else'FAIL_SAMPLED_STATIC_SURFACE','sourceFbxSha256':hashlib.sha256((O/'Exports/Player_C02_ReRig.fbx').read_bytes()).hexdigest(),'sampling_step_m':.0005,'triangles':triangles,'samples':total,'ray_misses':misses,'maximum_sampled_penetration_m':worst,'worst_point':worstp,'scope':'Current fitted right-hand grasp. Triangle interior grid with <=0.5mm edge step and actual shaft radial ray; finite sampling, not analytic intersection proof or future-motion validation.'}
(O/'Validation/hand_surface_sampling.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
