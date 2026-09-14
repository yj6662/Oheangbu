"""Inspect both anatomical forearm wraps with their original overlapping scanned sleeves."""
import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/WrappingInnerWall/BindingViews';OUT.mkdir(parents=True,exist_ok=True)
defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text());report={'renders':[],'status':'MANUAL_REVIEW_PENDING'}
for label,source in [('before',ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'),('after',ART/'DosaV2_WrappingForearmProbe.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(source));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
 for o in list(scene.objects):
  if o.type in ['CAMERA','LIGHT']:bpy.data.objects.remove(o,do_unlink=True)
 scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True;scene.world.color=(.15,.15,.15);scene.render.resolution_x=1000;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 for name,location,energy in [('Key',(-3,-4,4),450),('Fill',(3,-1,2),250),('Rim',(1,3,3),350)]:
  d=bpy.data.lights.new('WrapQA'+name,'AREA');d.energy=energy;d.size=3;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=location;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
 camera=bpy.data.objects.new('WrapQACamera',bpy.data.cameras.new('WrapQACamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=.40
 for pid in ['grip_down','elbow_120','forearm_minus90','forearm_plus90','combined_reach']:
  d=next(p for p in defs['poses'] if p['id']==pid)
  for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
  for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
  bpy.context.view_layer.update()
  for side,short in [('Left','L'),('Right','R')]:
   visible={'DosaV2_SleeveInner_'+short,'DosaV2_ArmLining_'+side,'DosaV2_Hands','DosaV2_SleeveOuter_'+short}
   for o in scene.objects:
    if o.type=='MESH':o.hide_render=o.name not in visible
   matrix=rig.matrix_world@rig.pose.bones[side+'ForeArm'].matrix;center=matrix@Vector((0,.08,0));offset=matrix.to_quaternion()@Vector((.20,-.12,.50));camera.location=center+offset;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
   path=OUT/(label+'_'+pid+'_'+side+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);report['renders'].append({'source':str(source),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'poseId':pid,'side':side,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()});(OUT/'report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'renders':len(report['renders'])}))
