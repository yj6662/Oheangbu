"""Render the actual new tuft under three lights and static bending; no asset save."""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';SOURCE=ART/'DosaBrushV2_TuftRefined.blend';OUT=ART/'Staging/BrushRefined/QA'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaBrushV2_Rig'];hair=bpy.data.objects['DosaBrushV2_Bristles']
root=rig.data.bones['Bristle_01'].head_local.copy();tip=rig.data.bones['Bristle_06'].tail_local.copy();line=tip-root;center=root.lerp(tip,.5)
for o in list(scene.objects):
 if o.type in ['CAMERA','LIGHT']:bpy.data.objects.remove(o,do_unlink=True)
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.render.resolution_x=850;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX';scene.view_settings.exposure=0
scene.world.use_nodes=True;bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
cam=bpy.data.objects.new('TuftQA',bpy.data.cameras.new('TuftQA'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.355
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'renders':[],'productionActions':0,'rigPass':False}
for condition,lights,world in [('neutral',[((-1,-1.5,2),180,(1,1,1),1.3),((1,-.8,1),55,(1,1,1),1.2)],(.12,.12,.12,.4)),('cool_rim',[((.3,.6,1),100,(.66,.8,1),.15),((-.4,-.8,.9),8,(1,1,1),1)],(.05,.065,.09,.15)),('warm_dim',[((-.6,-.8,1.1),22,(1,.62,.28),.65),((.5,.6,.8),3,(.55,.68,1),1)],(.025,.030,.04,.12))]:
 for o in list(scene.objects):
  if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
 bg.inputs['Color'].default_value=(*world[:3],1);bg.inputs['Strength'].default_value=world[3]
 for i,(pos,energy,color,size) in enumerate(lights):
  d=bpy.data.lights.new('QA'+str(i),'AREA');d.energy=energy;d.color=color;d.size=size;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
 for pose,view,angle,splay in [('rest','front',0,0),('rest','side',0,0),('bend_right','front',28,.65),('bend_left','front',-28,.65),('splay','front',0,1)] if condition=='neutral' else [('rest','front',0,0)]:
  for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
  position=root.copy();axis=Vector((1,0,0)).cross(line.normalized()).normalized()
  for j in range(6):
   b=rig.data.bones[f'Bristle_{j+1:02d}'];p=rig.pose.bones[b.name];q=Quaternion(axis,math.radians(angle)*(j/5)**1.7)
   p.matrix=Matrix.Translation(position)@q.to_matrix().to_4x4()@b.matrix_local.to_quaternion().to_matrix().to_4x4();bpy.context.view_layer.update();position+=q@(line/6)
  hair.data.shape_keys.key_blocks['BristleSplay'].value=splay;bpy.context.view_layer.update()
  cam.location=center+Vector((1,0,0) if view=='side' else (0,-1,0));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
  path=OUT/(condition+'_'+pose+'_'+view+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
  report['renders'].append({'condition':condition,'pose':pose,'view':view,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()});(OUT/'render-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'renders':len(report['renders'])}))
