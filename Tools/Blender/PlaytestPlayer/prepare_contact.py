import bpy,json,numpy as np
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Work/Player_C02_FingerRig.blend'));r=bpy.data.objects['Armature'];o=bpy.data.objects['C02_Mesh_0']
# Preserve all other authored corner normals, smooth exposed skin only.
normals=[n.vector.copy()for n in o.data.corner_normals]
for l in o.data.loops:
 if abs(o.data.vertices[l.vertex_index].co.x)>70:normals[l.index]=o.data.vertices[l.vertex_index].normal.copy()
o.data.normals_split_custom_set(normals)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Work/Player_C02_FingerRig.blend'))
bones=list(r.data.bones);names=[b.name for b in bones];ids=[v.index for v in o.data.vertices if v.co.x<-69]
v=np.asarray([o.data.vertices[i].co[:] for i in ids]);w=np.zeros((len(ids),len(bones)))
for j,i in enumerate(ids):
 for g in o.data.vertices[i].groups:
  n=o.vertex_groups[g.group].name
  if n in names:w[j,names.index(n)]=g.weight
o.data.calc_loop_triangles();mapping={old:new for new,old in enumerate(ids)}
tris=np.asarray([[mapping[i]for i in t.vertices]for t in o.data.loop_triangles if all(i in mapping for i in t.vertices)])
np.savez(OUT/'Validation/contact_input.npz',vertices=v,weights=w,rest=np.asarray([b.matrix_local for b in bones]),parents=np.asarray([names.index(b.parent.name)if b.parent else -1 for b in bones]),triangles=tris,names=np.array(names))
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'));h=bpy.data.objects['DosaBrushV2_Handle'];h.data.calc_loop_triangles()
np.savez(OUT/'Validation/shaft_input.npz',vertices=np.asarray([h.matrix_world@v.co for v in h.data.vertices]),triangles=np.asarray([t.vertices[:] for t in h.data.loop_triangles]))
print('CONTACT_INPUT_READY')
