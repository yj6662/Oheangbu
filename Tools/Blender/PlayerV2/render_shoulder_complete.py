"""Direct static-pose review of the isolated shoulder connection; saves no source."""
import bpy,json,math,importlib.util,hashlib
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete'
SOURCE=OUT/'DosaV2_ShoulderComplete_Candidate.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
def load(name,file):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
helper=load('render_shoulder_helpers','build_brush.py');physics=load('render_shoulder_pose','diagnose_cloth_blender.py')
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
def render(name,path,point,scale,azimuth):
    helper.render(scene,camera,path,point,scale,azimuth,width=1000,height=800)
    renders.append({'poseId':name,'path':str(path.relative_to(ROOT)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'posedBoneHeads':{n:list(rig.pose.bones[n].head) for n in ['LeftArm','LeftForeArm','RightArm','RightForeArm']}})
for name in ['rest','open_hand','grip_down','combined_reach','fixture_raised_arms_settle']:
    pose(name);render(name,OUT/(name+'-front.png'),(0,-.01,1.2),1.00,math.pi*.12);print('SHOULDER_RENDER '+name,flush=True)
for o in scene.objects:
    if o.type=='MESH':o.hide_render='Lining' not in o.name
for name in ['DosaV2_ShoulderLining_Left','DosaV2_ShoulderLining_Right']:
    obj=bpy.data.objects[name];m=bpy.data.materials.new('DiagnosticShoulderAmber');m.diffuse_color=(.36,.15,.025,1);m.use_nodes=True;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.36,.15,.025,1);obj.data.materials.clear();obj.data.materials.append(m)
for name in ['rest','combined_reach','fixture_raised_arms_settle']:
    pose(name);point=(rig.pose.bones['LeftArm'].head+rig.pose.bones['Spine02'].head)*.5
    render(name,OUT/(name+'-anatomy.png'),point,.37,math.pi*.08)
(OUT/'render-report.json').write_text(json.dumps({'status':'DIRECT_POSE_VISUAL_REVIEW_NOT_CLOTH_SOLVE','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'productionActions':len(bpy.data.actions),'renders':renders},indent=2),encoding='utf-8')
