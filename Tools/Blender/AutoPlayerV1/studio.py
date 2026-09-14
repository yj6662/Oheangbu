"""Isolated candidate staging, geometry inspection, and real-frame render helpers."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/AutoPlayerV1')
def save_json(p,x):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,indent=2),encoding='utf-8')
def models():return [o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('candidate_mesh')]
def bounds():
 pts=[o.matrix_world@Vector(c) for o in models() for c in o.bound_box]
 return Vector(tuple(min(p[i] for p in pts) for i in range(3))),Vector(tuple(max(p[i] for p in pts) for i in range(3)))
def studio():
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=8;s.cycles.use_denoising=True
 s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.fps=30
 s.world=bpy.data.worlds.new('AutoPlayer_Studio');s.world.use_nodes=True;bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.18,.18,.18,1);bg.inputs[1].default_value=.55
 lo,hi=bounds();h=hi.z-lo.z;ctr=(lo+hi)/2
 for name,off,power,size in [('Key',(-2,-3,3),450,2.5),('Fill',(2,-2,2),200,2.5),('Rim',(0,2,3),350,2)]:
  l=bpy.data.lights.new(name,'AREA');l.energy=power*(h/1.75)**2;l.shape='DISK';l.size=size*(h/1.75);o=bpy.data.objects.new(name,l);s.collection.objects.link(o);o.location=ctr+Vector(off)*(h/1.75);o.rotation_euler=(ctr-o.location).to_track_quat('-Z','Y').to_euler()
 cam=bpy.data.objects.new('ReviewCamera',bpy.data.cameras.new('ReviewCamera'));s.collection.objects.link(cam);s.camera=cam
 s.view_settings.view_transform='AgX'
def inspect_raw(candidate,path,stage='Raw'):
 if bpy.data.is_dirty:
  bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'preserved_open_session.blend'),copy=True)
 new=bpy.data.scenes.new('AutoPlayer_'+candidate);bpy.context.window.scene=new
 for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
 for scene in list(bpy.data.scenes):
  if scene!=new:bpy.data.scenes.remove(scene)
 for a in list(bpy.data.actions):bpy.data.actions.remove(a)
 bpy.ops.outliner.orphans_purge(do_recursive=True)
 bpy.ops.import_scene.gltf(filepath=str(path))
 shape_objects={p.custom_shape for r in new.objects if r.type=='ARMATURE' for p in r.pose.bones if p.custom_shape}
 for i,o in enumerate([o for o in new.objects if o.type=='MESH' and o not in shape_objects]):o.name=candidate+'_Mesh_'+str(i);o['candidate_mesh']=True
 new['candidate_id']=candidate
 stats=[]
 for o in models():
  o.data.calc_loop_triangles();stats.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'materials':[m.name for m in o.data.materials],'finite':all(math.isfinite(c) for v in o.data.vertices for c in v.co),'modifiers':[m.type for m in o.modifiers]})
 lo,hi=bounds();r={'candidate':candidate,'source':str(path),'triangles':sum(m['triangles'] for m in stats),'meshes':stats,'raw_bounds':[list(lo),list(hi)],'raw_height':hi.z-lo.z,'rigging_geometry_modified':False,'generation':'UNVERIFIED','deformation':'UNVERIFIED'}
 save_json(OUT/'Candidates'/candidate/(stage.lower()+'_geometry.json'),r);studio();save_candidate(stage);print(json.dumps(r))
def render_view(label,view='front',target=None,scale=None):
 s=bpy.context.scene;lo,hi=bounds();h=hi.z-lo.z;ctr=Vector(target) if target is not None else (lo+hi)/2
 dirs={'front':(0,-1,0),'back':(0,1,0),'left':(1,0,0),'right':(-1,0,0),'threequarter':(1,-2,.15)}
 c=s.camera;c.location=ctr+Vector(dirs[view]).normalized()*h*3;c.rotation_euler=(ctr-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=scale or max(h*1.85,(hi.x-lo.x)*1.1)
 folder=OUT/'Candidates'/s['candidate_id']/'Previews';folder.mkdir(parents=True,exist_ok=True);s.render.filepath=str(folder/(label+'.png'));bpy.ops.render.render(write_still=True)
def save_candidate(stage):
 p=OUT/'Candidates'/bpy.context.scene['candidate_id']/(stage+'.blend');p.parent.mkdir(parents=True,exist_ok=True);bpy.ops.wm.save_as_mainfile(filepath=str(p))
