import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path('C:/Users/yj666/Oheangbu')
bpy.ops.wm.open_mainfile(filepath=str(root/'Art/SpellVFX120/MeshySummons/Blender/MeshySummons_Source.blend'))
data=[]
for o in bpy.data.objects:
 if o.type!='MESH':continue
 row=dict(name=o.name,dimensions=list(o.dimensions),location=list(o.location),rotation=list(o.rotation_euler),scale=list(o.scale),materials=[m.name for m in o.data.materials])
 if '016' in o.name:
  v=[o.matrix_world@x.co for x in o.data.vertices]
  row['slices']=[dict(z=z,points=[list(x) for x in v if abs(x.z-z)<.01][::max(1,len([x for x in v if abs(x.z-z)<.01])//16)]) for z in [.03,.15,.8,1.2,1.4,1.5,1.8,2.1]]
 data.append(row)
out=root/'Art/SpellVFX120/WoodDeer';out.mkdir(exist_ok=True)
(out/'source_inspect.json').write_text(json.dumps(data,indent=2))
print(json.dumps(data))
