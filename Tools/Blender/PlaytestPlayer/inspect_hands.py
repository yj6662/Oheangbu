import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Work/Player_ReRig_Work.blend'))
rig=bpy.data.objects['Armature'];rig.animation_data_clear();rig.data.pose_position='REST'
o=bpy.data.objects['C02_Mesh_0'];s=bpy.context.scene
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
out={'bounds':[[min((o.matrix_world@v.co)[i] for v in o.data.vertices),max((o.matrix_world@v.co)[i] for v in o.data.vertices)]for i in range(3)]}
for side in ['Right','Left']:
 b=rig.data.bones[side+'Hand'];center=rig.matrix_world@b.head_local+Vector((-.07 if side=='Right' else .07,0,0))
 pts=[list(o.matrix_world@v.co) for v in o.data.vertices if any(o.vertex_groups[g.group].name==side+'Hand' and g.weight>.5 for g in v.groups)]
 out[side]={'points':pts,'wrist':list(rig.matrix_world@b.head_local),'tail':list(rig.matrix_world@b.tail_local)}
 for view,direction in [('palm',(0,-1,0)),('top',(0,0,1)),('side',(1,0,0))]:
  s.camera.location=center+Vector(direction)*3;s.camera.rotation_euler=(center-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.type='ORTHO';s.camera.data.ortho_scale=.48
  s.render.engine='CYCLES';s.cycles.samples=8;s.cycles.use_denoising=True;s.render.resolution_x=1920;s.render.resolution_y=1080;s.render.resolution_percentage=100
  s.render.image_settings.file_format='PNG';s.render.filepath=str(OUT/'Images'/f'before_{side}_{view}.png');bpy.ops.render.render(write_still=True)
(OUT/'Validation/hand_surface.json').write_text(json.dumps(out),encoding='utf8')
