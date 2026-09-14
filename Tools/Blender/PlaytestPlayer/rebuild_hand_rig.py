"""C02-only hand subdivision and anatomical rig. Never overwrites approved inputs."""
import bpy,bmesh,json,math,hashlib
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Work/Player_ReRig_Work.blend'))
rig=bpy.data.objects['Armature'];o=bpy.data.objects['C02_Mesh_0'];rig.animation_data_clear();rig.data.pose_position='POSE'
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
# Imported tail lengths are 100x the anatomical head spacing. Shorten along the
# existing local Y axis, preserving the bind orientation and every joint head.
before={b.name:b.matrix_local.copy() for b in rig.data.bones}
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for b in rig.data.edit_bones:b.length*=.01
specs={};axes={}
for side,sign in [('Right',-1),('Left',1)]:
 shift=0 if side=='Right' else .0026
 entries=[('Index',(.783,-.057,1.372),(.862,-.056,1.374)),('Middle',(.779,-.037,1.374),(.874,-.036,1.374)),('Ring',(.775,-.017,1.374),(.864,-.016,1.374)),('Pinky',(.766,.003,1.375),(.840,.005,1.369)),('Thumb',(.730,-.056,1.354),(.796,-.102,1.344))]
 for finger,a,z in entries:
  a=Vector((a[0]*sign,a[1],a[2]+shift))*100;z=Vector((z[0]*sign,z[1],z[2]+shift))*100
  times=[0,.46,.76,1] if finger!='Thumb' else [0,.35,.69,1]
  names=[]
  for i in range(3):
   name=f'{side}Hand{finger}{i+1}';b=rig.data.edit_bones.new(name);b.head=a.lerp(z,times[i]);b.tail=a.lerp(z,times[i+1]);b.parent=rig.data.edit_bones[side+'Hand'] if i==0 else rig.data.edit_bones[names[-1]];b.use_connect=i>0
   b.align_roll(Vector((0,0,1)));names.append(name)
  specs[side+finger]={'side':side,'finger':finger,'names':names,'start':list(a),'end':list(z),'joints':times}
bpy.ops.object.mode_set(mode='OBJECT')
bind_error=max(max(abs(before[n][i][j]-rig.data.bones[n].matrix_local[i][j])for i in range(4)for j in range(4))for n in before)
# Refine only exposed hands, including UV-seam duplicates. No body/robe weight edits.
bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table()
handverts=[v for v in bm.verts if abs(v.co.x)>70.0]
bmesh.ops.remove_doubles(bm,verts=handverts,dist=.00005)
for _ in range(2):
 edges=[e for e in bm.edges if min(abs(v.co.x)for v in e.verts)>72.5 and e.calc_length()>.55]
 bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True)
for f in bm.faces:
 if all(abs(v.co.x)>70 for v in f.verts):f.smooth=True
bm.to_mesh(o.data);bm.free();o.data.update()
def near_segment(p,a,b):
 t=max(0,min(1,(p-a).dot(b-a)/(b-a).length_squared));return (p-a.lerp(b,t)).length,t
counts={};changed=0
for v in o.data.vertices:
 if abs(v.co.x)<72.0:continue
 side='Left' if v.co.x>0 else 'Right';p=v.co
 choices=[]
 for k,s in specs.items():
  if s['side']!=side:continue
  a,z=Vector(s['start']),Vector(s['end']);dist,t=near_segment(p,a,z)
  choices.append((dist,k,t))
 distance,k,t=min(choices);sp=specs[k];a,z=Vector(sp['start']),Vector(sp['end']);axis=(z-a).normalized();along=(p-a).dot(axis)
 # Each finger's proximal influence fades into the palm, avoiding knuckle steps.
 alpha=max(0,min(1,(along+1.0)/1.65));alpha=alpha*alpha*(3-2*alpha)
 if sp['finger']=='Thumb':alpha=max(0,min(1,(along+.2)/1.2));alpha=alpha*alpha*(3-2*alpha)
 if alpha<=0:continue
 length=(z-a).length;u=along/length
 vals={side+'Hand':1-alpha}
 joints=sp['joints'];band=.055
 if u<joints[1]-band:vals[sp['names'][0]]=alpha
 elif u>joints[2]+band:vals[sp['names'][2]]=alpha
 elif joints[1]+band<u<joints[2]-band:vals[sp['names'][1]]=alpha
 else:
  j=0 if u<=joints[1]+band else 1
  w=max(0,min(1,(u-joints[j+1]+band)/(band*2)));w=w*w*(3-2*w)
  vals[sp['names'][j]]=alpha*(1-w);vals[sp['names'][j+1]]=alpha*w
 for g in list(v.groups):o.vertex_groups[g.group].remove([v.index])
 for name,w in vals.items():
  if w>1e-6:(o.vertex_groups.get(name)or o.vertex_groups.new(name=name)).add([v.index],w,'REPLACE')
 changed+=1;counts[k]=counts.get(k,0)+1
# Store rest-relative gestures as child transforms so FBX handles handedness.
poses={}
for name,s in specs.items():
 side=s['side'];finger=s['finger'];sign=-1 if side=='Right' else 1
 for i,bn in enumerate(s['names']):
  b=rig.data.bones[bn];axis=b.matrix_local.to_3x3().inverted()@Vector((0,sign,0))
  angles=([47,71,51] if finger!='Thumb' else [17,31,35])
  q=Quaternion(axis,math.radians(angles[i]))
  if finger=='Thumb' and i==0:
   opp=b.matrix_local.to_3x3().inverted()@Vector((sign,0,0));q=Quaternion(opp,math.radians(34))@q
  poses[bn]=list(q)
  for kind,angq in [('Grip',q),('Relax',Quaternion(axis,math.radians([9,14,8][i])))]:
   ob=bpy.data.objects.new('POSE_'+kind+'_'+bn,None);bpy.context.scene.collection.objects.link(ob);ob.parent=rig;ob.parent_type='BONE';ob.parent_bone=bn
   ob.matrix_world=rig.matrix_world@b.matrix_local@angq.to_matrix().to_4x4()
# Grip frame: +Y follows the shaft across the palm, +Z out through palm.
g=bpy.data.objects.new('GripReference_R',None);bpy.context.scene.collection.objects.link(g);g.parent=rig;g.parent_type='BONE';g.parent_bone='RightHand';g.matrix_world=Matrix.Translation(Vector((-.807,-.025,1.337)))
bpy.context.view_layer.update();bpy.context.scene.render.fps=30
for obj in bpy.data.objects:
 if obj.type=='MESH':obj.data.calc_loop_triangles()
tris=sum(len(ob.data.loop_triangles)for ob in bpy.data.objects if ob.type=='MESH' and ob.name in ['C02_Mesh_0','FaceLid_Left','FaceLid_Right'])
report={'status':'DRAFT_RIG_NOT_PASS','existing_bind_matrix_max_error':bind_error,'source_sha256':hashlib.sha256((ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Final/Integrated_B2_C3.blend').read_bytes()).hexdigest(),'bones':len(rig.data.bones),'triangles':tris,'hand_weight_vertices':changed,'finger_counts':counts,'specs':specs,'grip_quaternions_blender':poses}
(OUT/'Validation/hand_rig_draft.json').write_text(json.dumps(report,indent=2),encoding='utf8')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Work/Player_C02_FingerRig.blend'))
print(json.dumps({k:v for k,v in report.items() if k not in ['specs','grip_quaternions_blender']}))
