import bpy,json
from pathlib import Path
from mathutils.kdtree import KDTree
r=Path('C:/Users/yj666/Oheangbu');out=r/'Art/SpellVFX120/WoodDeer'
report=json.loads((out/'model_report.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(out/'WoodDeer_Working.blend'))
body=bpy.data.objects['WoodDeer_Body'];tree=KDTree(len(body.data.vertices))
for i,v in enumerate(body.data.vertices):tree.insert(v.co,i)
tree.balance()
with bpy.data.libraries.load(str(r/'Art/SpellVFX120/MeshySummons/Blender/MeshySummons_Source.blend'),link=False) as (data,target):target.objects=['SM_Meshy_016_ACF0']
source=target.objects[0];scale=report['scale'];error=max(tree.find(v.co*scale)[2] for v in source.data.vertices)
images={kind:list(bpy.data.images['T_WoodDeer_'+kind].size) for kind in ['BaseColor','Normal']}
result=dict(status='PASS' if error<.00001 else 'FAIL',maxBodyPositionErrorM=error,sourceVertexCount=len(source.data.vertices),deliveryBodyVertices=len(body.data.vertices),images=images,scope='Nearest-position check after uniform shoulder normalization and duplicate-vertex welding. Not a rig or animation check.')
(out/'body_preservation.json').write_text(json.dumps(result,indent=2));print(result)
