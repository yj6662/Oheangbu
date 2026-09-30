"""Derive a head-prioritized game mesh and short-stride clips from the approved regeneration.
Original Meshy downloads remain untouched. Outputs require independent motion review.
"""
import bpy,bmesh,sys,json
from pathlib import Path
from array import array
name=sys.argv[sys.argv.index('--')+1];root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal257')/name
out=root/'prepared';out.mkdir(exist_ok=True);report=[];hair_indices=None
for label,src in [('Body','result_rigged_character_glb_url.glb'),('Walk','result_basic_animations_walking_glb_url.glb'),('Run','result_basic_animations_running_glb_url.glb')]:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(root/'rig'/src))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>0];arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
 for o in list(bpy.context.scene.objects):
  if o not in meshes+arms:bpy.data.objects.remove(o,do_unlink=True)
 # Split only for density allocation; head keeps a much larger share than uniform decimation.
 # Modifiers operate on copies of downloaded rigs. Bone weights and animation hierarchy remain.
 for o in list(meshes):
  if len(o.data.polygons)<=180000:continue
  bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
  coords=[o.matrix_world@v.co for v in o.data.vertices];zmin=min(v.z for v in coords);zmax=max(v.z for v in coords)
  cut=zmax-(zmax-zmin)*.15
  head=o.copy();head.data=o.data.copy();bpy.context.collection.objects.link(head)
  for target_obj,keep_head in [(head,True),(o,False)]:
   bm=bmesh.new();bm.from_mesh(target_obj.data)
   faces=[f for f in bm.faces if all((target_obj.matrix_world@v.co).z>cut for v in f.verts)!=keep_head]
   bmesh.ops.delete(bm,geom=faces,context='FACES')
   loose=[v for v in bm.verts if not v.link_faces]
   if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
   bm.to_mesh(target_obj.data);bm.free()
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>0]
 for o in meshes:
  coords=[o.matrix_world@v.co for v in o.data.vertices]
  is_head=min(v.z for v in coords)>1.0
  target=60000 if is_head else 100000
  o.name='WangsoHead257' if is_head else 'WangsoBody257'
  if len(o.data.polygons)>180000:
   bpy.context.view_layer.objects.active=o
   modifier=o.modifiers.new('BodyDensityHeadProtected257','DECIMATE');modifier.ratio=target/len(o.data.polygons);modifier.use_collapse_triangulate=True
   while o.modifiers.find(modifier.name)>0:bpy.ops.object.modifier_move_up(modifier=modifier.name)
   bpy.ops.object.modifier_apply(modifier=modifier.name)
 count=removed=0
 for o in meshes:
  for face in o.data.polygons:face.use_smooth=True
  # UV/normal splits must not create hard triangular seams over the face.
  from collections import defaultdict
  from mathutils import Vector
  groups=defaultdict(list)
  for v in o.data.vertices:groups[tuple(round(float(x),6) for x in v.co)].append(v.index)
  normals=[Vector((0,0,0)) for v in o.data.vertices]
  for face in o.data.polygons:
   for vi in face.vertices:normals[vi]+=face.normal*face.area
  merged=[Vector((0,0,0)) for v in o.data.vertices]
  for indices in groups.values():
   normal=sum((normals[i] for i in indices),Vector((0,0,0))).normalized()
   for i in indices:merged[i]=normal
  for edge in o.data.edges:edge.use_edge_sharp=False
  o.data.normals_split_custom_set_from_vertices(merged)
 # Rigging rebakes materials, so restore the dark tie treatment on the final rig UVs.
 for o in meshes:
  mat=o.data.materials[0];pn=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  for link in list(pn.inputs['Emission Color'].links):mat.node_tree.links.remove(link)
  pn.inputs['Emission Strength'].default_value=0
  tex=pn.inputs['Base Color'].links[0].from_node.image
  from array import array
  px=array('f',[0])*(len(tex.pixels));tex.pixels.foreach_get(px);tw,th=tex.size
  dark=bpy.data.materials.new('HairTieDark257');dark.use_nodes=True;dark.node_tree.nodes.clear()
  bs=dark.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=dark.node_tree.nodes.new('ShaderNodeOutputMaterial');dark.node_tree.links.new(bs.outputs[0],output.inputs['Surface']);bs.inputs['Base Color'].default_value=(.018,.016,.014,1);bs.inputs['Roughness'].default_value=.85
  o.data.materials.append(dark);mi=len(o.data.materials)-1;uv=o.data.uv_layers.active.data
  evaluated=o.evaluated_get(bpy.context.evaluated_depsgraph_get());posed=evaluated.to_mesh()
  selected=[]
  for f in o.data.polygons:
   if hair_indices is not None:
    if f.index in hair_indices:f.material_index=mi
    continue
   co=evaluated.matrix_world@posed.polygons[f.index].center
   side=(.036<abs(co.x)<.093 and -.01<co.y<.08 and 1.43<co.z<1.54)
   tie=(abs(co.x)<.085 and co.y>.075 and 1.53<co.z<1.64)
   if not (side or tie):continue
   if min((evaluated.matrix_world@posed.vertices[i].co).z for i in posed.polygons[f.index].vertices)<1.475:continue
   st=sum((uv[i].uv for i in f.loop_indices),Vector((0,0)))/len(f.loop_indices)
   ix=4*(max(0,min(th-1,int(st.y*th)))*tw+max(0,min(tw-1,int(st.x*tw))))
   rgb=px[ix:ix+3]
   if min(rgb)>.6 and max(rgb)-min(rgb)<.055:f.material_index=mi;selected.append(f.index)
  evaluated.to_mesh_clear()
  if hair_indices is None:hair_indices=set(selected)
 # Shorten the native stride for skirt clearance; retain the original mesh and weights.
 if label!='Body':
  from mathutils import Quaternion
  arm=arms[0];act=arm.animation_data.action;start,end=map(int,act.frame_range)
  sampled=[]
  for frame in range(start,end+1):
   bpy.context.scene.frame_set(frame)
   sampled.append((frame,[(b.name,b.location.copy(),b.rotation_quaternion.copy(),b.scale.copy()) for b in arm.pose.bones]))
  arm.animation_data.action=bpy.data.actions.new(label+'_ShortStride257')
  for track in arm.animation_data.nla_tracks: track.mute=True
  bpy.data.actions.remove(act)
  for frame,values in sampled:
   for bone_name,loc,rot,scale in values:
    b=arm.pose.bones[bone_name];b.rotation_mode='QUATERNION'
    if 'Leg' in bone_name or 'Foot' in bone_name or 'Toe' in bone_name:rot=Quaternion((1,0,0,0)).slerp(rot,(.5 if label=='Walk' else .55) if name=='wangso' else .50)
    if label=='Run' and ('Arm' in bone_name or 'Shoulder' in bone_name):rot=Quaternion((1,0,0,0)).slerp(rot,.65)
    b.location=loc;b.rotation_quaternion=rot;b.scale=scale
    b.keyframe_insert('location',frame=frame);b.keyframe_insert('rotation_quaternion',frame=frame);b.keyframe_insert('scale',frame=frame)
  bpy.context.scene.frame_start=start;bpy.context.scene.frame_end=end;bpy.context.scene.frame_set(start)
 bpy.ops.object.select_all(action='SELECT')
 bpy.ops.export_scene.gltf(filepath=str(out/(label+'.glb')),export_format='GLB',use_selection=True,export_animations=label!='Body',export_animation_mode='ACTIVE_ACTIONS')
 bpy.ops.export_scene.fbx(filepath=str(out/(label+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=label!='Body',bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,axis_forward='-Z',axis_up='Y')
 report.append(dict(mode=label,reweighted_cloth_vertices=count,removed_occluded_leg_faces=removed,leg_motion_scale=((.5 if label=='Walk' else .55) if name=='wangso' else .50) if label!='Body' else 1,source_downloads_unchanged=True, game_mesh_decimated=True, skeleton_preserved=True, normals_smoothed_across_coincident_vertices=True, vertices=sum(len(o.data.vertices) for o in meshes), triangles=sum(len(o.data.polygons) for o in meshes)))
(out/'repair.json').write_text(json.dumps(report,indent=2))
