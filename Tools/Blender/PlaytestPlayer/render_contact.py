import bpy,json,math
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_FingerRig.blend'))
r=bpy.data.objects['Armature'];r.animation_data_clear();r.data.pose_position='POSE'
fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters']
for p in r.pose.bones:p.matrix_basis.identity()
for name,m in fit['bone_matrices'].items():r.pose.bones[name].matrix_basis=Matrix(m)
bpy.context.view_layer.update()
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'))
new=[o for o in bpy.data.objects if o not in before]
tx=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X')
for o in new:
 if not o.parent:o.matrix_world=tx@o.matrix_world
bpy.context.view_layer.update()
h=bpy.data.objects['DosaBrushV2_Handle'];h.data.calc_loop_triangles()
bvh=BVHTree.FromPolygons([h.matrix_world@v.co for v in h.data.vertices],[t.vertices[:]for t in h.data.loop_triangles],all_triangles=True)
body=bpy.data.objects['C02_Mesh_0'];ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
items=[]
for v in body.data.vertices:
 if v.co.x>=-69:continue
 p=body.matrix_world@mesh.vertices[v.index].co;pos,n,idx,dist=bvh.find_nearest(p)
 if pos is not None:items.append({'index':v.index,'signed_distance':dist if (p-pos).dot(n)>=0 else -dist,'world':list(p)})
ev.to_mesh_clear();items.sort(key=lambda t:t['signed_distance'])
(O/'Validation/contact_exact_draft.json').write_text(json.dumps({'status':'DRAFT_CONTACT','min_signed_distance':items[0]['signed_distance'],'worst':items[:30]},indent=2))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.use_denoising=True;s.render.resolution_x=1920;s.render.resolution_y=1080;s.render.resolution_percentage=100
cam=s.camera;cam.data.type='ORTHO';cam.data.ortho_scale=.37;center=Vector((-.78,-.038,1.35));cam.location=center+Vector((-1,-2,1)).normalized()*4;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(O/'Images/actual_grip_draft.png');bpy.ops.render.render(write_still=True)
print('CONTACT_DRAFT',items[0]['signed_distance'],flush=True)
