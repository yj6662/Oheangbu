import bpy,json,math,hashlib,ctypes
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_ReRig.blend'))
r=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0'];r.animation_data_clear();fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters']
base=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'));new=[o for o in bpy.data.objects if o not in base];brushroots=[o for o in new if not o.parent];brushmatrices={o:o.matrix_world.copy()for o in brushroots}
grip=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X');handrest=r.data.bones['RightHand'].matrix_local.copy()
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.use_denoising=True;s.render.resolution_x=1920;s.render.resolution_y=1080;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';cam=s.camera;cam.data.type='ORTHO'
def setrot(name,worldaxis,degrees):
 b=r.data.bones[name];axis=b.matrix_local.to_3x3().inverted()@Vector(worldaxis);p=r.pose.bones[name];p.rotation_mode='QUATERNION';p.rotation_quaternion=Quaternion(axis,math.radians(degrees))
def reset(grasp=True):
 for p in r.pose.bones:p.matrix_basis.identity()
 if grasp:
  for n,m in fit['bone_matrices'].items():r.pose.bones[n].matrix_basis=Matrix(m)
 for side in ['Left']:
  for f in ['Thumb','Index','Middle','Ring','Pinky']:
   for i in range(1,4):setrot(side+'Hand'+f+str(i),(0,1,0),[8,14,8][i-1])
def brush_follow():
 bpy.context.view_layer.update();tx=r.matrix_world@r.pose.bones['RightHand'].matrix@handrest.inverted()@r.matrix_world.inverted()@grip
 for o in brushroots:o.matrix_world=tx@brushmatrices[o]
 bpy.context.view_layer.update()
def capture(name,center,direction,scale):
 # GetPerformanceInfo commit gate, identical threshold to Unity review policy.
 class PI(ctypes.Structure):_fields_=[('cb',ctypes.c_ulong)]+[(n,ctypes.c_size_t)for n in ['CommitTotal','CommitLimit','CommitPeak','PhysicalTotal','PhysicalAvailable','SystemCache','KernelTotal','KernelPaged','KernelNonpaged','PageSize']]+[(n,ctypes.c_ulong)for n in ['HandleCount','ProcessCount','ThreadCount']]
 p=PI();p.cb=ctypes.sizeof(p);ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(p),p.cb)
 if p.CommitLimit and p.CommitTotal/p.CommitLimit>=.85:raise RuntimeError('Capture stopped: system commit >=85%')
 center=Vector(center);cam.data.ortho_scale=scale;cam.location=center+Vector(direction).normalized()*5;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(O/'Images'/f'{name}.png');bpy.ops.render.render(write_still=True)
reset();brush_follow()
for name,d in [('grip_oblique_final',(-1,-2,1)),('grip_palm_final',(0,0,-1))]:capture(name,(-.775,-.035,1.35),d,.35)
for pose in ['arms_down','arms_raised','elbow_flex']:
 reset()
 if pose=='arms_down':setrot('RightArm',(0,1,0),-68);setrot('LeftArm',(0,1,0),68);setrot('RightForeArm',(1,0,0),-20);setrot('LeftForeArm',(1,0,0),-12)
 elif pose=='arms_raised':setrot('RightArm',(0,1,0),60);setrot('LeftArm',(0,1,0),-60);setrot('RightForeArm',(1,0,0),-18)
 else:setrot('RightArm',(0,1,0),-30);setrot('LeftArm',(0,1,0),30);setrot('RightForeArm',(0,0,1),100);setrot('LeftForeArm',(0,0,1),-100)
 brush_follow();capture(pose,(0,0,1.02),(0,-1,.12),4.1)
reset(False)
for o in new:o.hide_render=True
capture('neutral_front',(0,0,.87),(0,-1,.03),3.35)
# Static numerical checks include exported modifier results, not requested polygon counts.
rows=[]
for o in [body,bpy.data.objects['FaceLid_Left'],bpy.data.objects['FaceLid_Right']]:
 o.data.calc_loop_triangles();unassigned=negative=nonfinite=0;maxsum=0;maxweights=0
 for v in o.data.vertices:
  weights=[g.weight for g in v.groups if o.vertex_groups[g.group].name in r.data.bones];unassigned+=sum(weights)<=0;negative+=any(x<0 for x in weights);nonfinite+=any(not math.isfinite(x)for x in weights);maxweights=max(maxweights,len(weights));maxsum=max(maxsum,abs(1-sum(weights)))
 rows.append({'part':o.name,'tris':len(o.data.loop_triangles),'vertices':len(o.data.vertices),'max_weights':maxweights,'unassigned':unassigned,'negative':negative,'nonfinite':nonfinite,'weight_sum_max_error':maxsum})
report={'status':'NUMERIC_PASS'if sum(x['tris']for x in rows)<=60000 and all(x['unassigned']==0 and x['negative']==0 and x['nonfinite']==0 and x['weight_sum_max_error']<.0001 for x in rows)else'FAIL','parts':rows,'total_tris':sum(x['tris']for x in rows),'bone_count':len(r.data.bones),'diagnostic_poses_only':True,'visual_review':'PENDING','source_preserved_sha256':hashlib.sha256((R/'Art/PlayerPhase1/C02_RigFaceLab/Final/Integrated_B2_C3.blend').read_bytes()).hexdigest()}
(O/'Validation/static_checks.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
