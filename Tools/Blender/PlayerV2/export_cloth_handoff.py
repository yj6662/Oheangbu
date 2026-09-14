"""Freeze a merge manifest for the bounded coat/prop topology repair."""
import bpy,hashlib,json
from datetime import datetime,timezone
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Topology5mm'
SOURCE=OUT/'DosaV2_ClothTopologyRefined.blend'
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
source_hash=digest(SOURCE);bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
names=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R',
       'DosaV2_WaistTubeSide_L','DosaV2_WaistTubeBack_R','DosaV2_WaistTube_C',
       'DosaV2_WaistVial_L','DosaV2_WaistScroll_L','DosaV2_WaistPouch_R','DosaV2_TrousersSource']
rows=[]
for name in names:
 o=bpy.data.objects[name];o.data.calc_loop_triangles()
 rows.append({'name':name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),
  'materialSlots':[m.name for m in o.data.materials],'armatureTarget':[m.object.name for m in o.modifiers if m.type=='ARMATURE' and m.object],
  'maxWeightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in o.data.vertices),
  'mobilityAttribute':'ClothMobility' if o.data.color_attributes.get('ClothMobility') else None})
total=0
for o in bpy.context.scene.objects:
 if o.type=='MESH' and not o.hide_render and not o.name.startswith('SourceSurface'):
  o.data.calc_loop_triangles();total+=len(o.data.loop_triangles)
report={'status':'GEOMETRY_FROZEN_PHYSICS_FINAL_SOURCE_PENDING','frozenAtUtc':datetime.now(timezone.utc).isoformat(),
 'source':str(SOURCE),'source_sha256':source_hash,'mergeObjects':rows,
 'removeBeforeMerge':['DosaV2_Robe_Back_L','DosaV2_Robe_Back_R','DosaV2_Robe_Front_L','DosaV2_Robe_Front_R','DosaV2_Robe_Side_L','DosaV2_Robe_Side_R'],
 'preflightCharacterTotalTriangles':total,'clothTriangles':sum(r['triangles'] for r in rows[:3]),'triangleReductionVsPreflight':536,
 'bones':len(bpy.data.objects['DosaV2_Rig'].data.bones),'actions':len(bpy.data.actions),
 'images':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':digest(p)} for p in sorted(OUT.glob('static_*.png'))],
 'integration':'Append only the listed ten meshes/materials, repoint Armature modifiers and parents to the final DosaV2_Rig without changing matrices/weights. Do not append the old hand/body/lining meshes from this preflight-derived source. Rebuild LODs after final merge.',
 'physics':'Native LINEAR bending and physical areal mass resolve prior Angular bending numerical noise. Rest preflight passes numeric strain/settled limits with diagnostic capsules, but real anatomy collider coverage plus four final-source cases remain required. No overall RIG_PASS.'}
(OUT/'final-cloth-handoff.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2));assert source_hash==digest(SOURCE)
