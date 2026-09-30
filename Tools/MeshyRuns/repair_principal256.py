"""Retain approved geometry and weights; adapt stride for clothing clearance.
Original Meshy downloads remain untouched. Outputs require independent motion review.
"""
import bpy,bmesh,sys,json
from pathlib import Path
from array import array
name=sys.argv[sys.argv.index('--')+1];root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal256')/name
out=root/'prepared';out.mkdir(exist_ok=True);report=[]
for label,src in [('Body','result_rigged_character_glb_url.glb'),('Walk','result_basic_animations_walking_glb_url.glb'),('Run','result_basic_animations_running_glb_url.glb')]:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(root/'rig'/src))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>0];arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
 for o in list(bpy.context.scene.objects):
  if o not in meshes+arms:bpy.data.objects.remove(o,do_unlink=True)
 count=removed=0
 for o in meshes:
  for face in o.data.polygons:face.use_smooth=True
 # Shorten the native stride for skirt clearance; retain the original mesh and weights.
 if label!='Body':
  from mathutils import Quaternion
  arm=arms[0];act=arm.animation_data.action;start,end=map(int,act.frame_range)
  sampled=[]
  for frame in range(start,end+1):
   bpy.context.scene.frame_set(frame)
   sampled.append((frame,[(b.name,b.location.copy(),b.rotation_quaternion.copy(),b.scale.copy()) for b in arm.pose.bones]))
  arm.animation_data.action=bpy.data.actions.new(label+'_ShortStride256')
  for track in arm.animation_data.nla_tracks: track.mute=True
  bpy.data.actions.remove(act)
  for frame,values in sampled:
   for bone_name,loc,rot,scale in values:
    b=arm.pose.bones[bone_name];b.rotation_mode='QUATERNION'
    if 'Leg' in bone_name or 'Foot' in bone_name or 'Toe' in bone_name:rot=Quaternion((1,0,0,0)).slerp(rot,(.38 if label=='Walk' else .40) if name=='wangso' else .50)
    if label=='Run' and ('Arm' in bone_name or 'Shoulder' in bone_name):rot=Quaternion((1,0,0,0)).slerp(rot,.65)
    b.location=loc;b.rotation_quaternion=rot;b.scale=scale
    b.keyframe_insert('location',frame=frame);b.keyframe_insert('rotation_quaternion',frame=frame);b.keyframe_insert('scale',frame=frame)
  bpy.context.scene.frame_start=start;bpy.context.scene.frame_end=end;bpy.context.scene.frame_set(start)
 bpy.ops.object.select_all(action='SELECT')
 bpy.ops.export_scene.gltf(filepath=str(out/(label+'.glb')),export_format='GLB',use_selection=True,export_animations=label!='Body',export_animation_mode='ACTIVE_ACTIONS')
 bpy.ops.export_scene.fbx(filepath=str(out/(label+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=label!='Body',bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,axis_forward='-Z',axis_up='Y')
 report.append(dict(mode=label,reweighted_cloth_vertices=count,removed_occluded_leg_faces=removed,leg_motion_scale=((.38 if label=='Walk' else .40) if name=='wangso' else .50) if label!='Body' else 1,mesh_geometry_and_weights_unchanged=True))
(out/'repair.json').write_text(json.dumps(report,indent=2))
