"""Static source-only topology QA; no Cloth solve or Actions."""
import bpy,importlib.util,math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Topology5mm'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_ClothTopologyRefined.blend'))
def module(name,file):
 spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
physics=module('cloth_physics','diagnose_cloth_blender.py');helper=module('cloth_render','build_brush.py')
scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig'];camera=scene.camera or helper.lighting(scene)
for case,angle in [('rest_settle',math.pi*.15),('rest_back',math.pi*.85),('raised_arms_settle',math.pi*.15),('grip_settle',math.pi*.15)]:
 physics.direct_pose(rig,'rest_settle' if case=='rest_back' else case)
 helper.render(scene,camera,OUT/('static_'+case+'.png'),(0,.05,.88),2.02,angle,width=800,height=1050)
