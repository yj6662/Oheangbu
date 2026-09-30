import bpy,json,math
from pathlib import Path
root=Path('C:/Users/yj666/Oheangbu');out=root/'Art/Demo/Summons/DokkaebiClub'
bpy.ops.wm.open_mainfile(filepath=str(out/'DokkaebiClub_Combat.blend'))
rig=bpy.data.objects['DokkaebiClub_CombatRig'];body=bpy.data.objects['DokkaebiClub_Body']
rig.animation_data.action=bpy.data.actions['DC_Swing'];rig.animation_data.action_slot=rig.animation_data.action.slots[0]
bpy.context.scene.frame_set(29,subframe=.5);bpy.context.view_layer.update()
obj=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=obj.to_mesh();rows=[]
for edge in body.data.edges:
    a,b=edge.vertices;before=(body.data.vertices[a].co-body.data.vertices[b].co).length;after=(mesh.vertices[a].co-mesh.vertices[b].co).length
    if after-before>.10:
        rows.append({'increase':after-before,'before':before,'after':after,'verts':[{'id':i,'source':list(body.data.vertices[i].co),'posed':list(mesh.vertices[i].co),'weights':{body.vertex_groups[g.group].name:g.weight for g in body.data.vertices[i].groups}} for i in (a,b)]})
rows.sort(key=lambda x:-x['increase']);obj.to_mesh_clear()
(out/'skin_stretch.json').write_text(json.dumps(rows[:80],indent=2),encoding='utf-8')
print(json.dumps(rows[:5]))
