import bpy,json,math,sys
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Work/Player_C02_FingerRig.blend'))
rig=bpy.data.objects['Armature'];rig.animation_data_clear();rig.data.pose_position='POSE';rep=json.loads((OUT/'Validation/hand_rig_draft.json').read_text())
for p in rig.pose.bones:p.matrix_basis.identity()
for name,q in rep['grip_quaternions_blender'].items():rig.pose.bones[name].rotation_mode='QUATERNION';rig.pose.bones[name].rotation_quaternion=Quaternion(q)
bpy.context.view_layer.update()
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'))
new=[o for o in bpy.data.objects if o not in before]
br={'objects':[]}
for o in new:
 br['objects'].append({'name':o.name,'type':o.type,'loc':list(o.matrix_world.translation),'dimensions':list(o.dimensions),'parent':o.parent.name if o.parent else None})
print(json.dumps(br));(OUT/'Validation/brush_inspect.json').write_text(json.dumps(br,indent=2))
for o in new:o.hide_render=True
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.use_denoising=True
s.render.resolution_x=1920;s.render.resolution_y=1080;s.render.resolution_percentage=100
center=Vector((-.775,-.038,1.355));cam=s.camera;cam.data.type='ORTHO';cam.data.ortho_scale=.37
for view,d in [('grip_top',(0,0,1)),('grip_palm',(0,0,-1)),('grip_oblique',(-1,-2,1))]:
 cam.location=center+Vector(d).normalized()*4;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(OUT/'Images'/(view+'.png'));bpy.ops.render.render(write_still=True)
# Export just neutral rig/model/pose markers for Unity bind verification, without production motion.
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT')
for o in before:
 if o.type in ['ARMATURE','MESH','EMPTY'] and o.name not in ['Ground','Floor']:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'Exports/Player_C02_ReRig.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
print('EXPORTED_DRAFT',flush=True)
