"""C2: local real eyeballs with exportable gaze shapes; no body/face source edits."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face';folder=OUT/'C2';folder.mkdir(exist_ok=True)
ns={'__name__':'face_helpers'};exec(compile((ROOT/'Tools/Blender/C02_RigFaceLab/face_inspect.py').read_text(encoding='utf-8'),'face_inspect.py','exec'),ns)
body=bpy.data.objects['C02_Mesh_0'];rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');rig.animation_data_clear();rig.data.pose_position='REST'
inv=body.matrix_world.inverted()
def material(name,color,roughness):
 m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=roughness
 return m
materials=[material('Eye_Sclera',(.53,.47,.39),.28),material('Eye_Iris',(.033,.022,.013),.34),material('Eye_Pupil',(.003,.002,.0015),.2)]
eyes=[];radius=.0112
centers={'Left':Vector((.02664062567,-.11456018686+.0095,1.597148418)), 'Right':Vector((-.02749999985,-.11551129818+.0095,1.597148418))}
for side,center in centers.items():
 name='FaceEye_'+side
 if name in bpy.data.objects:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
 angles=[.06,.12,.19,.255,.34,.52,.75,1.0,1.3,1.65,2.0,2.4,2.8];segments=32
 coords=[center+Vector((0,-radius,0))]
 for theta in angles:
  for i in range(segments):
   phi=2*math.pi*i/segments;coords.append(center+Vector((math.sin(theta)*math.cos(phi),-math.cos(theta),math.sin(theta)*math.sin(phi)))*radius)
 coords.append(center+Vector((0,radius,0)));faces=[];face_mats=[]
 for i in range(segments):faces.append((0,1+i,1+(i+1)%segments));face_mats.append(2)
 for j in range(len(angles)-1):
  for i in range(segments):
   a=1+j*segments+i;b=1+j*segments+(i+1)%segments;c=b+segments;d=a+segments
   faces.append((a,d,c,b));face_mats.append(2 if angles[j+1]<=.12 else 1 if angles[j+1]<=.255 else 0)
 last=len(coords)-1;offset=1+(len(angles)-1)*segments
 for i in range(segments):faces.append((last,offset+(i+1)%segments,offset+i));face_mats.append(0)
 mesh=bpy.data.meshes.new(name);mesh.from_pydata([inv@p for p in coords],[],faces);mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(o);o.matrix_world=body.matrix_world.copy()
 for m in materials:mesh.materials.append(m)
 for p,idx in zip(mesh.polygons,face_mats):p.material_index=idx;p.use_smooth=True
 g=o.vertex_groups.new(name='Head');g.add(list(range(len(coords))),1,'REPLACE');mod=o.modifiers.new('Armature','ARMATURE');mod.object=rig
 o.shape_key_add(name='Basis')
 for channel,axis,degrees in [('GazeLeft','Z',12),('GazeRight','Z',-12),('GazeUp','X',-10),('GazeDown','X',10)]:
  key=o.shape_key_add(name=channel);rotation=Matrix.Rotation(math.radians(degrees),3,axis)
  for v,p in zip(key.data,coords):v.co=inv@(center+rotation@(p-center))
 o['face_lab_role']='eyeball';o['face_lab_candidate']='C2';eyes.append(o)
 # Fit both eyelid endpoint shapes over the actual globe with interpolation margin.
 lid=bpy.data.objects['FaceLid_'+side]
 for key in lid.data.shape_keys.key_blocks:
  for v in key.data:
   p=lid.matrix_world@v.co;dx,dz=p.x-center.x,p.z-center.z;r2=radius*radius-dx*dx-dz*dz
   if r2>0:p.y=min(p.y,center.y-math.sqrt(r2)-.00065)
   v.co=inv@p
 lid['face_lab_candidate']='C2'
rig.data.pose_position='POSE';bpy.context.view_layer.update()
all_meshes=[body]+[bpy.data.objects['FaceLid_'+s] for s in centers]+eyes
def reset():
 for o in all_meshes:
  if o.data.shape_keys:
   for k in list(o.data.shape_keys.key_blocks)[1:]:k.value=0
def apply(values):
 reset()
 for o in all_meshes:
  if o.data.shape_keys:
   for name,value in values.items():
    if name in o.data.shape_keys.key_blocks:o.data.shape_keys.key_blocks[name].value=value
for label,values in [('neutral',{}),('blink',{'BlinkLeft':1,'BlinkRight':1}),('gaze_left',{'GazeLeft':1}),('gaze_up',{'GazeUp':1})]:
 apply(values);ns['render']('C2_'+label+'_front',(0,-1,0))
reset();stats=[]
for o in all_meshes:o.data.calc_loop_triangles();stats.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles)})
manifest={'candidate':'C2','stage':'C','attempt':2,'method':'Two independent low-poly eyeball spheres with dark iris/pupil material regions and skinned gaze shape rotations; additive lids refitted to actual globe surface.',
 'body_mesh_modified':False,'body_vertex_changes':[],'body_weights_modified':False,'new_bones':0,'mesh_stats':stats,
 'added_triangles':sum(s['triangles'] for s in stats)-51760,'total_triangles':sum(s['triangles'] for s in stats),
 'channels':['BlinkLeft','BlinkRight','GazeLeft','GazeRight','GazeUp','GazeDown'],'range_blender':[0,1],'range_unity':[0,100],
 'face_blink':'UNVERIFIED','face_gaze':'UNVERIFIED','face_jaw':{'status':'NOT_ATTEMPTED','reason':'Source mouth remains a closed fused shell; opening requires local topology experiment'},
 'limitations':['Original static painted eye surface remains behind the inserted eyeballs; surrounding eye aperture has not been cut.', 'Gaze shape interpolation is a geometric approximation to rotation, at most 12 degrees.','Not an art acceptance.'],
 'centers_world_m':{k:list(v) for k,v in centers.items()},'radius_m':radius,'paid_requests':0}
assert manifest['total_triangles']<=60000 and manifest['added_triangles']<=6000
(folder/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'Face_C2.blend'));print('FACE_C2_COMPLETE',json.dumps(manifest))
