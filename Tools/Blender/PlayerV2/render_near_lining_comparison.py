"""Compare actual world/near lining angular surfaces in matching static views."""
import bpy,math,json,importlib.util,hashlib
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete';DEST=OUT/'NearOverrides'
spec=importlib.util.spec_from_file_location('near_render_helpers',Path(__file__).with_name('build_brush.py'));helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
defs=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset/Inputs/static-pose-definitions.json').read_text())['poses'];rows=[]
for label,path in [('World24',OUT/'DosaV2_ShoulderComplete_Candidate.blend'),('Near18',DEST/'DosaV2_NearArmLining18.blend')]:
    bpy.ops.wm.open_mainfile(filepath=str(path));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
    for o in scene.objects:
        if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
        if o.type=='MESH':o.hide_render=o.name not in ['DosaV2_ArmLining_Left','DosaV2_ShoulderLining_Left','DosaV2_BodyLining'];o.hide_set(False)
    camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
    for name in ['rest','forearm_plus90']:
        for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
        d=next(d for d in defs if d['id']==name)
        for r in d['boneRotations']:rig.pose.bones[r['bone']].matrix_basis=Euler([math.radians(r[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
        bpy.context.view_layer.update();center=(rig.pose.bones['LeftArm'].head+rig.pose.bones['LeftForeArm'].head)*.5
        file=DEST/(label+'-'+name+'.png');helper.render(scene,camera,file,center,.38,math.pi*.08,width=1000,height=800)
        rows.append({'variant':label,'pose':name,'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'render':str(file.relative_to(ROOT)),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(DEST/'render-report.json').write_text(json.dumps({'renders':rows,'scope':'Same camera, lighting and authored material; hidden outer garment isolates the small angular LOD change. World collision anatomy and hands are unchanged.'},indent=2),encoding='utf-8')
