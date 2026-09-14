"""Match manifest slots to actually used source material indices; no geometry export."""
import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[3]/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(root/'DosaV2_Assembled.blend'))
path=root/'Staging/texture-manifest.json'
manifest=json.loads(path.read_text(encoding='utf-8-sig'))
changes=[]
for row in manifest['rendererMaterials']:
    obj=bpy.data.objects.get(row['renderer'])
    if obj is None or obj.type!='MESH':continue
    used={p.material_index for p in obj.data.polygons}
    names=['DosaV2_Source' if m.name=='Material_0' else m.name for i,m in enumerate(obj.data.materials) if i in used]
    if names!=row['materials']:changes.append({'renderer':obj.name,'before':row['materials'],'after':names})
    row['materials']=names
path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps({'usedSlotChanges':changes}))
