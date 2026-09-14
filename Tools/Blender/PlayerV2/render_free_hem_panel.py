"""Static visual review only. Native cloth drape remains a separate gate."""
import bpy,json,math,importlib.util,hashlib,os
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';VARIANT=os.environ.get('DOSA_PANEL_VARIANT','original31');OUT=BASE/'FreeHemPanel' if VARIANT=='original31' else BASE/('TorsoSupportedPanel/SeamLineCandidate' if VARIANT=='torso10line' else 'TorsoSupportedPanel/Candidate');VIEW=OUT/'Views';VIEW.mkdir(exist_ok=True)
SOURCE=OUT/'DosaV2_FreeHemPanel_Candidate.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
def load(name,file):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
helper=load('freehem_render','build_brush.py');physics=load('freehem_pose','diagnose_cloth_blender.py')
defs=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset/Inputs/static-pose-definitions.json').read_text())['poses']
for o in scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
    if o.type=='MESH':o.hide_render=not(o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface');o.hide_set(False)
camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
def pose(name):
    physics.direct_pose(rig,'rest_settle')
    if name.startswith('fixture_'):physics.direct_pose(rig,name.replace('fixture_',''))
    else:
        d=next(d for d in defs if d['id']==name)
        for r in d['boneRotations']:rig.pose.bones[r['bone']].matrix_basis=Euler([math.radians(r[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update()
renders=[]
for name in ['rest','open_hand','grip_down','combined_reach','fixture_raised_arms_settle']:
    pose(name);path=VIEW/(name+'-front.png');helper.render(scene,camera,path,(0,-.01,1.2),1.,math.pi*.12,width=1000,height=800)
    renders.append({'poseId':name,'path':str(path.relative_to(ROOT)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'handHeads':{n:list(rig.pose.bones[n].head) for n in ['LeftHand','RightHand']}})
    print('FREE_HEM_RENDER '+name,flush=True)
pose('rest')
for side in ['L','R']:
    name='DosaV2_SleeveOuter_'+side;o=bpy.data.objects[name];m=bpy.data.materials.new('DiagnosticFreeHem_'+side);m.use_nodes=True;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.025,.34,.37,1);o.data.materials.clear();o.data.materials.append(m)
for name in ['rest','fixture_raised_arms_settle']:
    pose(name);path=VIEW/(name+'-outer-separation.png');helper.render(scene,camera,path,(0,-.01,1.2),1.,math.pi*.12,width=1000,height=800)
    renders.append({'poseId':name,'path':str(path.relative_to(ROOT)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'kind':'Diagnostic cyan outer sleeve with original inner wraps/lining'})
(VIEW/'render-report.json').write_text(json.dumps({'status':'DIRECT_STATIC_SKINNING_NOT_NATIVE_CLOTH_DRAPE','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'renders':renders},indent=2),encoding='utf-8')
