import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/VerifiedAttachments'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Inputs/Assembled-d979b9bc.blend'))
report={}
for name in ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    o=bpy.data.objects[name];m=o.data;report[name]={'attributes':[(a.name,a.domain,a.data_type,a.is_internal) for a in m.attributes],'uvs':[u.name for u in m.uv_layers],'colors':[c.name for c in m.color_attributes],'shapeKeys':m.shape_keys is not None,'customNormals':m.has_custom_normals,'materialNames':[x.name for x in m.materials]}
(OUT/'transfer-attribute-inventory.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2),flush=True)
