import bpy, json, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import rig_creatures as rig
p=Path(__file__).resolve().parents[3]/'Art/Characters/Folklore298'
bpy.ops.wm.open_mainfile(filepath=str(p/'Derivatives/cheongryong/run-14/FAILED-debug.blend'))
r=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
rig.assign(r,bpy.data.actions['Death']);rig.reset(r);bpy.context.scene.frame_set(52);bpy.context.view_layer.update()
m=next(o for o in bpy.context.scene.objects if o.type=='MESH')
v=rig.world_vertices(m,True);names={g.index:g.name for g in m.vertex_groups}
print(json.dumps({'low':[{'i':i,'p':list(v[i]),'weights':{names[g.group]:g.weight for g in m.data.vertices[i].groups}}for i in sorted(range(len(v)),key=lambda i:v[i].z)[:6]],'bodyheads':{b.name:list(b.matrix.translation)for b in r.pose.bones if b.name in ['Head','Body_01','Body_04','Body_12','Body_20','Body_24']}}))
