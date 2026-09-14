"""Current character, identical materials/cameras, only lining angular count varies."""
import bpy,math,json,hashlib,importlib.util
from pathlib import Path
from mathutils import Matrix,Euler,Vector
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'NearLiningBudget10';VIEW=OUT/'Views';VIEW.mkdir(exist_ok=True)
SOURCE=BASE/'TorsoSupportedPanel/SeamLineCandidate/DosaV2_FreeHemPanel_Candidate.blend'
variants=[('World24',None),('Near18',BASE/'ShoulderComplete/NearOverrides/DosaV2_NearArmLining18.blend'),('Near10',OUT/'DosaV2_NearArmLining10.blend')]
spec=importlib.util.spec_from_file_location('near10_view_helpers',Path(__file__).with_name('build_brush.py'));helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
defs=json.loads((BASE/'FoldedGusset/Inputs/static-pose-definitions.json').read_text())['poses'];rows=[]
for label,override in variants:
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
    if override:
        names=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right'];targets={n:bpy.data.objects[n] for n in names}
        with bpy.data.libraries.load(str(override),link=False) as (src,dst):dst.objects=list(names)
        for name,obj in zip(names,dst.objects):
            target=targets[name];assert [g.name for g in target.vertex_groups]==[g.name for g in obj.vertex_groups]
            target.data=obj.data;bpy.data.objects.remove(obj,do_unlink=True)
    for o in scene.objects:
        if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
        if o.type=='MESH':o.hide_set(False)
    camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
    for poseid in ['rest','forearm_plus90','combined_reach']:
        for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
        d=next(d for d in defs if d['id']==poseid)
        for r in d['boneRotations']:rig.pose.bones[r['bone']].matrix_basis=Euler([math.radians(r[c]) for c in ['x','y','z']],'XYZ').to_matrix().to_4x4()
        bpy.context.view_layer.update()
        for mode in ['Isolated','Clothed']:
            for o in scene.objects:
                if o.type!='MESH':continue
                o.hide_render=o.name not in ['DosaV2_ArmLining_Left','DosaV2_ShoulderLining_Left','DosaV2_BodyLining'] if mode=='Isolated' else not(o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface')
            center=(rig.pose.bones['LeftArm'].head+rig.pose.bones['LeftForeArm'].head)*.5
            if mode=='Clothed':center=(rig.pose.bones['LeftArm'].head+rig.pose.bones['LeftHand'].head)*.5
            file=VIEW/(label+'-'+poseid+'-'+mode+'.png');helper.render(scene,camera,file,center,.35 if mode=='Isolated' else .58,math.pi*.08,width=1000,height=800)
            rows.append({'variant':label,'pose':poseid,'mode':mode,'path':str(file.relative_to(ROOT)),'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),
              'bodySourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'overrideSha256':hashlib.sha256(override.read_bytes()).hexdigest() if override else None})
            print('NEAR10_RENDER '+file.name,flush=True)
(OUT/'render-report.json').write_text(json.dumps({'status':'SAME_CURRENT_BODY_AND_MATERIALS_STATIC_VIEW_COMPARISON','scope':'Each variant starts from frozen93f74. Only two ArmLining mesh-data blocks change; all other geometry, pose, lighting and cameras are identical. This is not cloth simulation.','renders':rows},indent=2),encoding='utf-8')
