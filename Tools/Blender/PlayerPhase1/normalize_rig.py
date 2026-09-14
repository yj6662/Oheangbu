import bpy,json
from mathutils import Matrix,Vector
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1')
a=bpy.data.objects['Dosa_Phase1_Rig'];o=bpy.data.objects['char1']
for p in a.pose.bones:p.custom_shape=None
for x in list(bpy.context.scene.objects):
 if x.type=='MESH' and len(x.data.vertices)==42:bpy.data.objects.remove(x,do_unlink=True)
bpy.context.view_layer.objects.active=a;a.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for b in a.data.edit_bones:b.tail=b.head+(b.tail-b.head)*0.01
bpy.ops.object.mode_set(mode='OBJECT')
o.data.transform(o.matrix_world);o.parent=None;o.matrix_world=Matrix.Identity(4)
a.data.transform(a.matrix_world);a.matrix_world=Matrix.Identity(4)
o.parent=a;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_basis=Matrix.Identity(4)
bpy.data.objects['Body'].name='Source_Body_Unrigged';o.name='Body'
bpy.context.view_layer.update()
lo=min(v.co.z for v in o.data.vertices)
# Meshy height is already 1.75m. Translate bone and geometry together to ground.
shift=Matrix.Translation((0,0,-lo));o.data.transform(shift);a.data.transform(shift)
o.data.calc_loop_triangles()
print(json.dumps({'height':max(v.co.z for v in o.data.vertices),'triangles':len(o.data.loop_triangles),'bones':len(a.data.bones),'scale':list(a.scale)}))
bpy.context.scene.render.filepath=str(OUT/'Previews/Body_rigged.png');bpy.ops.render.render(write_still=True)
