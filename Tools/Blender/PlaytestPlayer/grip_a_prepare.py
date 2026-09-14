"""Read the accepted C02 derivative and export only numerical input for the A grasp."""
import bpy,json,hashlib,numpy as np
from pathlib import Path
R=Path('C:/Users/yj666/Oheangbu'); OLD=R/'Art/PlayerPhase1/PlaytestReRig'; OUT=R/'Art/PlaytestPolish/Hands'
for sub in ['Work','Exports','Validation','Images','Textures']:(OUT/sub).mkdir(parents=True,exist_ok=True)
source=OLD/'Work/Player_C02_ReRig.blend';bpy.ops.wm.open_mainfile(filepath=str(source))
r=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0'];r.animation_data_clear()
for p in r.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
names=[b.name for b in r.data.bones]; ids=[v.index for v in body.data.vertices if v.co.x<-69]
weights=np.zeros((len(ids),len(names)))
for row,i in enumerate(ids):
 for g in body.data.vertices[i].groups:
  name=body.vertex_groups[g.group].name
  if name in names:weights[row,names.index(name)]=g.weight
np.savez(OUT/'Validation/contact_input.npz',vertices=np.array([body.data.vertices[i].co[:]for i in ids]),weights=weights,
 rest=np.array([b.matrix_local for b in r.data.bones]),parents=np.array([names.index(b.parent.name)if b.parent else -1 for b in r.data.bones]),names=np.array(names))
(OUT/'Validation/hand_rig_draft.json').write_bytes((OLD/'Validation/hand_rig_draft.json').read_bytes())
(OUT/'Validation/shaft_input.npz').write_bytes((OLD/'Validation/shaft_input.npz').read_bytes())
manifest={'source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'bone_count':len(names),
 'mesh_vertices':len(body.data.vertices),'right_hand_vertices':len(ids),'selection':'Grip_A.png','intent':'Wrapped fingers with thumb opposition; existing skeleton, body, face and outfit preserved.'}
(OUT/'Validation/source.json').write_text(json.dumps(manifest,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Work/Player_C02_GripA_Work.blend'))
print(json.dumps(manifest),flush=True)
