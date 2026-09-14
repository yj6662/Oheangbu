"""Read-only derivative render of remaining floating scanned cloth fragments."""
import bpy, importlib.util, json, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Seams'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_ClothSeamRepair.blend'))
report=json.loads((OUT/'seam-repair-report.json').read_text());obj=bpy.data.objects['DosaV2_Robe_Combined']
palette=[(.05,.9,.1,1),(.9,.05,.9,1),(.05,.3,1,1),(1,.1,.02,1)]
for record,color in zip([r for r in report['components'] if not r['pinnedVertices']],palette):
 mat=bpy.data.materials.new('Floating_'+str(record['componentId']));mat.use_nodes=True
 mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=color
 mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.6
 obj.data.materials.append(mat);selected=set(record['members'])
 for p in obj.data.polygons:
  if all(i in selected for i in p.vertices):p.material_index=len(obj.data.materials)-1
spec=importlib.util.spec_from_file_location('render_helpers',Path(__file__).with_name('build_brush.py'))
helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
scene=bpy.context.scene;camera=scene.camera or helper.lighting(scene)
for label,angle in [('front',math.pi*.18),('back',math.pi*.85)]:
 helper.render(scene,camera,OUT/('fragments_'+label+'.png'),(0,.035,.7),.85,angle,width=1050,height=1050)
