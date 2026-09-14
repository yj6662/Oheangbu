"""Before/after posed hand volumes with real brush and the complete surrounding sleeves."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds/Views';OUT.mkdir(parents=True,exist_ok=True)
defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text());report={'renders':[],'rigGate':'NOT_GRANTED'}
for label,source in [('before',ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'),('after',ART/'DosaV2_HandsHarmonicProbe.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands'];scene=bpy.context.scene
 with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[n for n in src.objects if n.startswith('DosaBrushV2_') or n in ['GripSocket','TipSocket']]
 for o in dst.objects:
  if o and not o.users_collection:scene.collection.objects.link(o)
 brush=bpy.data.objects['DosaBrushV2_Rig'];grip=bpy.data.objects['GripSocket']
 for o in list(scene.objects):
  if o.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(o,do_unlink=True)
 for o in scene.objects:
  if o.type=='MESH':o.hide_render=not o.name.startswith(('DosaV2_','DosaPackV2_','DosaBrushV2_')) or o.name=='DosaV2_SourceSurface'
 scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True;scene.world.color=(.12,.12,.12);scene.render.resolution_x=1000;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 for name,location,energy,size in [('Key',(-3,-4,4),400,4),('Fill',(3,-1,2),220,3),('Rim',(1,3,3),330,3)]:
  d=bpy.data.lights.new('HandsQA'+name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=location;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
 camera=bpy.data.objects.new('HandsQACamera',bpy.data.cameras.new('HandsQACamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO'
 for pid in (['combined_reach'] if label=='before' else ['rest','grip_down','grip_up','combined_reach']):
  d=next(p for p in defs['poses'] if p['id']==pid)
  for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
  for side in ['Right','Left']:hand.data.shape_keys.key_blocks['GripPalmRelax_'+side].value=d.get('handGripCorrectives',{}).get(side.lower(),0.)
  for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
  bpy.context.view_layer.update()
  for side in ['Right','Left']:
   brush.matrix_world=(rig.matrix_world@rig.pose.bones[side+'BrushGrip'].matrix)@grip.matrix_world.inverted()@brush.matrix_world;bpy.context.view_layer.update()
   matrix=rig.matrix_world@rig.pose.bones[side+'Hand'].matrix;rotation=matrix.to_quaternion();center=matrix@Vector((0,.048,-.008));dorsal=rotation@Vector((0,0,1));width=rotation@Vector((1,0,0))
   for view,offset in [('palm',-dorsal*.35+width*.07)]+([('side',width*.35-dorsal*.03)] if pid=='combined_reach' else []):
    camera.location=center+offset;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=.165
    path=OUT/(label+'_'+pid+'_'+side+'_'+view+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
    report['renders'].append({'source':str(source),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'poseId':pid,'side':side,'view':view,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()});(OUT/'report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'renders':len(report['renders'])}))
