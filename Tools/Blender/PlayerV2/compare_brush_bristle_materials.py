"""Material-only candidates; no mesh, UV, bone, shape, or socket mutation."""
import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/BrushMaterial/Comparisons';OUT.mkdir(parents=True,exist_ok=True);SOURCE=ART/'DosaBrushV2.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaBrushV2_Rig'];hair=bpy.data.objects['DosaBrushV2_Bristles'];handle=bpy.data.objects['DosaBrushV2_Handle']
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
hair.data.shape_keys.key_blocks['BristleSplay'].value=0;bpy.context.view_layer.update();original=hair.data.materials[0]
variants=[('source',original)]
for name,tint in [('dielectric',(1.,1.,1.)),('muted',(.68,.66,.62))]:
 m=original.copy();m.name='M_DosaBrushV2_Bristles_'+name;bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 for key in ['Metallic','Roughness']:
  for link in list(bs.inputs[key].links):m.node_tree.links.remove(link)
 bs.inputs['Metallic'].default_value=0;bs.inputs['Roughness'].default_value=.85
 if tint!=(1.,1.,1.):
  source=bs.inputs['Base Color'].links[0].from_socket;mix=m.node_tree.nodes.new('ShaderNodeMixRGB');mix.name='BristleBaseTint';mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=(*tint,1);m.node_tree.links.new(source,mix.inputs[1]);m.node_tree.links.new(mix.outputs[0],bs.inputs['Base Color'])
 variants.append((name,m))
for o in list(scene.objects):
 if o.type in ['CAMERA','LIGHT']:bpy.data.objects.remove(o,do_unlink=True)
for o in scene.objects:
 if o.type=='MESH':o.hide_render=o.name not in ['DosaBrushV2_Handle','DosaBrushV2_Bristles']
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.resolution_x=850;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX';scene.view_settings.exposure=0
scene.world.use_nodes=True;background=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
cam=bpy.data.objects.new('BristleMaterialCamera',bpy.data.cameras.new('BristleMaterialCamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.355;center=Vector((0,0,.597))
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'candidateContract':{'metallic':0,'roughness':.85,'sameBaseColorNormalUV':True,'tints':{'dielectric':[1,1,1],'muted':[.68,.66,.62]}},'renders':[],'canonicalMutation':False}
for condition,lights,world in [('neutral',[((-1,-1.5,2),180,(1,1,1),1.3),((1,-.8,1),55,(1,1,1),1.2)],(.12,.12,.12,.4)),('cool_rim',[((.3,.6,1),100,(.66,.80,1),.15),((-.4,-.8,.9),8,(1,1,1),1.0)],(.05,.065,.09,.15)),('warm_dim',[((-.6,-.8,1.1),22,(1,.62,.28),.65),((.5,.6,.8),3,(.55,.68,1),1.)],(.025,.030,.04,.12))]:
 for o in list(scene.objects):
  if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
 background.inputs['Color'].default_value=(*world[:3],1);background.inputs['Strength'].default_value=world[3]
 for i,(position,energy,color,size) in enumerate(lights):
  light=bpy.data.lights.new('MaterialQA'+str(i),'AREA');light.energy=energy;light.color=color;light.size=size;ob=bpy.data.objects.new(light.name,light);scene.collection.objects.link(ob);ob.location=position;ob.rotation_euler=(center-ob.location).to_track_quat('-Z','Y').to_euler()
 for view in ['front','side'] if condition=='neutral' else ['front']:
  cam.location=center+Vector((1,0,0) if view=='side' else (0,-1,0));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
  for name,material in variants:
   hair.data.materials[0]=material;path=OUT/(condition+'_'+view+'_'+name+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);report['renders'].append({'condition':condition,'view':view,'candidate':name,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()});(OUT/'report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'renders':len(report['renders'])}))
