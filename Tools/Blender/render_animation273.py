import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');args=sys.argv[sys.argv.index('--')+1:];family=args[0]
sinmok=family=='Sinmok';folder=ROOT/('Art/Characters/Sinmok272/rig' if sinmok else 'Art/Characters/Animation273');src=folder/('Sinmok273.blend' if sinmok else family+'_273.blend');out=folder/('Motion' if sinmok else family);out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src));scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE');meshes=[o for o in scene.objects if o.type=='MESH']
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=960;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('MotionReview');scene.world.color=(.2,.21,.2);scene.view_settings.view_transform='AgX'
def bounds():
 dep=bpy.context.evaluated_depsgraph_get();ps=[]
 for o in meshes:
  e=o.evaluated_get(dep);m=e.to_mesh();ps.extend(e.matrix_world@v.co for v in m.vertices);e.to_mesh_clear()
 return Vector([min(p[i] for p in ps)for i in range(3)]),Vector([max(p[i] for p in ps)for i in range(3)])
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update();lo,hi=bounds();h=hi.z-lo.z;focus=(lo+hi)/2
bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=max(h*1.65,(hi.x-lo.x)*1.2)
cam.location=focus+Vector((h*.6,-h*3,h*.15));cam.rotation_euler=(focus-cam.location).to_track_quat('-Z','Y').to_euler()
for pos,energy in [((1,-2,2),140),((-1,-1,1),100),((0,2,2),140)]:
 bpy.ops.object.light_add(type='AREA',location=focus+Vector(pos)*h);o=bpy.context.object;o.data.energy=energy*h*h;o.data.size=h;o.rotation_euler=(focus-o.location).to_track_quat('-Z','Y').to_euler()
stats=[];video=len(args)>1 and args[1]=='video';frame_index=0
actions=list(bpy.data.actions)
for a in actions:
 if a.name.startswith(('Sinmok_','A273_')):
  rig.animation_data.action=a
  if a.slots:rig.animation_data.action_slot=a.slots[0]
  start,end=a.frame_range
  if video and a.name not in ['Sinmok_Idle','Sinmok_LeftSlam','Sinmok_LeftSweep','Sinmok_SupportRelease']:continue
  duration=(end-start)/30
  for u in ([i/max(1,round(duration*15)) for i in range(round(duration*15))] if video else [0,.5,.65,.85] if sinmok else [.5]):
   frame=start+(end-start)*u;scene.frame_set(int(frame),subframe=frame%1);lo,hi=bounds();stats.append({'clip':a.name,'u':u,'min':list(lo),'max':list(hi)})
   filename=('video_'+str(frame_index).zfill(4)+'.png') if video else a.name+'_'+str(round(u*100))+'.png';frame_index+=1
   scene.render.filepath=str(out/filename);bpy.ops.render.render(write_still=True)
(out/'bounds.json').write_text(json.dumps(stats,indent=2))
