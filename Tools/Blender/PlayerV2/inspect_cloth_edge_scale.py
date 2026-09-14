"""Read-only edge-scale inventory after upper-cloth attachment authoring."""
import bpy,bmesh,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rows=[]
for obj in bpy.context.scene.objects:
 if obj.type!='MESH' or not obj.name.startswith(('DosaV2_Robe_','DosaV2_SleeveOuter_')):continue
 bm=bmesh.new();bm.from_mesh(obj.data);layer=bm.verts.layers.float_color.get('ClothMobility');counts={};short=[]
 for edge in bm.edges:
  length=edge.calc_length();free=all(v[layer][0]>0 for v in edge.verts)
  kind='both_free' if free else ('both_pinned' if all(v[layer][0]==0 for v in edge.verts) else 'mixed')
  if length<.005:
   key=kind+('_interior' if not edge.is_boundary and all(not v.is_boundary for v in edge.verts) else '_boundary')
   counts[key]=counts.get(key,0)+1
   short.append({'edge':edge.index,'vertices':[v.index for v in edge.verts],'length':length,'kind':kind,'boundary':edge.is_boundary})
 rows.append({'mesh':obj.name,'under5mm':counts,'minimum':sorted(short,key=lambda r:r['length'])[:10]});bm.free()
out=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Final9f4cd412PinAudit/edge-scale-inventory.json'
out.write_text(json.dumps({'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'surfaces':rows},indent=2),encoding='utf-8')
print(json.dumps([{'mesh':r['mesh'],'under5mm':r['under5mm']} for r in rows],indent=2))
