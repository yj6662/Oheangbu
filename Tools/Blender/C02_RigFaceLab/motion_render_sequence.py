"""One unlabeled 1280x720 comparison sequence, 30fps, independent headless Blender."""
import bpy,json,hashlib,subprocess,shutil
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Motion/B1'
FFMPEG=Path('C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
sourcehash=hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest();recipe=json.loads((OUT/'sequence_recipe.json').read_text(encoding='utf8'))
progress_path=OUT/'render_progress_final.json'
if progress_path.exists() and json.loads(progress_path.read_text(encoding='utf8')).get('source_blend_sha256')!=sourcehash:raise RuntimeError('Render source changed; never mix existing frames')
frames=OUT/'Frames_Final';frames.mkdir(exist_ok=True);previews=OUT/'Previews';previews.mkdir(exist_ok=True)
scene=bpy.context.scene;original_rig=bpy.data.objects['Armature'];original_mesh=bpy.data.objects['C02_Mesh_0']
for o in list(scene.objects):
    if o not in (original_rig,original_mesh):bpy.data.objects.remove(o,do_unlink=True)
rig2=original_rig.copy();rig2.data=original_rig.data.copy();scene.collection.objects.link(rig2)
mesh2=original_mesh.copy();scene.collection.objects.link(mesh2)
for modifier in mesh2.modifiers:
    if modifier.type=='ARMATURE':modifier.object=rig2
roots=[]
for rig,mesh,x,action in [(original_rig,original_mesh,-.93,'LAB_Original_Sequence'),(rig2,mesh2,.93,'LAB_B1_Sequence')]:
    root=bpy.data.objects.new('MotionLabTravel',None);scene.collection.objects.link(root)
    for o in (rig,mesh):
        world=o.matrix_world.copy();o.parent=root;o.matrix_world=world
    root.location.x=x;roots.append(root)
    for p in rig.pose.bones:p.matrix_basis.identity()
    rig.animation_data_create();rig.animation_data.action=bpy.data.actions[action]
    if rig.animation_data.action.slots:rig.animation_data.action_slot=rig.animation_data.action.slots[0]
    rig.data.pose_position='POSE';rig.hide_render=False;mesh.hide_render=False
for engine in ('BLENDER_EEVEE_NEXT','BLENDER_EEVEE'):
    try:scene.render.engine=engine;break
    except TypeError:continue
else:raise RuntimeError('No EEVEE available')
for name in ('taa_render_samples','taa_samples'):
    if hasattr(scene.eevee,name):setattr(scene.eevee,name,8)
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100;scene.render.fps=30
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8';scene.render.image_settings.compression=15
scene.world=bpy.data.worlds.new('MotionLabWorld');scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.24,.25,.25,1);bg.inputs[1].default_value=.45
scene.view_settings.view_transform='AgX'
bpy.ops.mesh.primitive_plane_add(size=140,location=(0,-20,0));ground=bpy.context.object;ground.name='MotionLabGround'
mat=bpy.data.materials.new('MotionLabGround');mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links
bs=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Roughness'].default_value=.85
coords=nodes.new('ShaderNodeNewGeometry');checker=nodes.new('ShaderNodeTexChecker');checker.inputs['Color1'].default_value=(.21,.23,.23,1);checker.inputs['Color2'].default_value=(.31,.33,.33,1);checker.inputs['Scale'].default_value=4
links.new(coords.outputs['Position'],checker.inputs['Vector']);links.new(checker.outputs['Color'],bs.inputs['Base Color']);ground.data.materials.append(mat)
camera=bpy.data.objects.new('MotionLabCamera',bpy.data.cameras.new('MotionLabCamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=4.35
up=Vector((0,1.2,6)).normalized();bounds=[float('inf'),float('-inf'),float('inf'),float('-inf')]
for frame in range(1,len(recipe)+1):
    scene.frame_set(frame);bpy.context.view_layer.update()
    for mesh in (original_mesh,mesh2):
        ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        for corner in ev.bound_box:
            point=ev.matrix_world@Vector(corner);vertical=(point-Vector((0,0,.93))).dot(up)
            bounds[0]=min(bounds[0],point.x);bounds[1]=max(bounds[1],point.x);bounds[2]=min(bounds[2],vertical);bounds[3]=max(bounds[3],vertical)
camera.data.ortho_scale=max(bounds[1]-bounds[0],(bounds[3]-bounds[2])*1280/720)*1.12
center_x=(bounds[0]+bounds[1])/2;center_z=.93+(bounds[2]+bounds[3])/(2*up.z)
light_data=[]
for name,loc,power,size in [('Key',(-3,-4,5),900,4),('Fill',(3,-2,3),500,3),('Rim',(1,2,4),650,2.5)]:
    d=bpy.data.lights.new('MotionLab'+name,'AREA');d.energy=power;d.size=size;o=bpy.data.objects.new('MotionLab'+name,d);scene.collection.objects.link(o);light_data.append((o,Vector(loc)))
speeds={'Idle':0.,'Run':5.362285039688777,'Walk':1.5537527974691157,'Attack':0.};distance=0.
progress={'source_blend':bpy.data.filepath,'source_blend_sha256':sourcehash,'source_renderer':'Blender 5.0.1 EEVEE, 8 samples (whole sequence; interrupted Cycles frames not mixed)','status':'RENDERING','resolution':[1280,720],'fps':30,'expected_frames':len(recipe),'completed_frames':0,
          'framing':{'method':'Both evaluated mesh bounding boxes at all 331 integer frames, fixed 12% margin, same constant orthographic scale and center for both versions','camera_ortho_scale':camera.data.ortho_scale,'projected_bounds':bounds,'center_xz':[center_x,center_z]},
          'left_panel':'Unmodified original C02 clips on A2 weights; generated comparison transitions','right_panel':'B1 cloned clips on same A2 weights; identical transition recipe','run_original_speed_cycles':6,'floor_square_m':.25,
          'camera_follows_native_speed_mps':speeds,'labels_inside_video':False,'quality_verdict':'UNVERIFIED'}
for index,r in enumerate(recipe,1):
    speed=speeds[r['a']]
    if r['b']:speed=speed*(1-r['weight'])+speeds[r['b']]*r['weight']
    distance+=speed/30
    for root in roots:root.location.y=-distance
    target=Vector((center_x,-distance,center_z));camera.location=target+Vector((0,-6,1.2));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    for light,location in light_data:
        light.location=location+Vector((0,-distance,0));light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
    scene.frame_set(index);bpy.context.view_layer.update();output=frames/f'{index:05d}.png'
    if not output.exists():scene.render.filepath=str(output);bpy.ops.render.render(write_still=True)
    if index in (1,82,242):shutil.copyfile(output,previews/f'B1_Comparison_{index:03d}.png')
    progress['completed_frames']=index
    if index%10==0 or index==len(recipe):
        progress_path.write_text(json.dumps(progress,ensure_ascii=False,indent=2),encoding='utf8');print('MOTION_RENDER_FRAME',index,flush=True)
video=OUT/'B1_Original_vs_Edited_Sequence.mp4'
subprocess.run([str(FFMPEG),'-hide_banner','-loglevel','error','-y','-framerate','30','-start_number','1','-i',str(frames/'%05d.png'),'-frames:v',str(len(recipe)),'-c:v','libx264','-preset','fast','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
progress.update({'status':'VIDEO_ENCODED','mp4':str(video),'mp4_sha256':hashlib.sha256(video.read_bytes()).hexdigest()});progress_path.write_text(json.dumps(progress,ensure_ascii=False,indent=2),encoding='utf8')
print('MOTION_VIDEO_COMPLETE',str(video),flush=True)
