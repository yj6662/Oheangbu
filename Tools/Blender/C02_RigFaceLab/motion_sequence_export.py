"""Prepare phase-continuous B1 comparison and FBX from the independent Motion copy."""
import bpy,json,hashlib
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Motion/B1'
ns={};p=ROOT/'Tools/Blender/C02_RigFaceLab/motion_build_b1.py';exec(compile(p.read_text(encoding='utf8'),str(p),'exec'),ns)
rig=bpy.data.objects['Armature'];obj=bpy.data.objects['C02_Mesh_0'];sets={}
for prefix in ('C02_','LAB_B1_'):
    sets[prefix]={}
    for label in ('Idle','Run','Walk','Attack'):
        a=bpy.data.actions[prefix+label];poses=[]
        for frame in range(int(a.frame_range[0]),int(a.frame_range[1])+1):
            ns['set_action'](rig,a,frame);poses.append(ns['capture'](rig))
        sets[prefix][label]=poses
phases={label:0 for label in ('Idle','Run','Walk','Attack')};recipe=[]
def sample(clip):
    n=len(sets['C02_'][clip]);idx=min(phases[clip],n-1) if clip=='Attack' else phases[clip]%(n-1)
    phases[clip]+=1;return idx
def hold(clip,count):
    for _ in range(count):recipe.append({'a':clip,'fa':sample(clip),'b':None,'weight':0})
def transition(a,b,count):
    for i in range(count):
        weight=(i+1)/count;weight=weight*weight*(3-2*weight)
        recipe.append({'a':a,'fa':sample(a),'b':b,'fb':sample(b),'weight':weight})
hold('Idle',30);transition('Idle','Run',12);run_start=len(recipe)+1
hold('Run',6*(len(sets['C02_']['Run'])-1));run_end=len(recipe)
transition('Run','Idle',12);hold('Idle',30);transition('Idle','Attack',10)
hold('Attack',len(sets['C02_']['Attack'])-phases['Attack']);transition('Attack','Idle',12);hold('Idle',30)
for prefix,name in [('C02_','LAB_Original_Sequence'),('LAB_B1_','LAB_B1_Sequence')]:
    old=bpy.data.actions.get(name)
    if old:bpy.data.actions.remove(old)
    result=[]
    for r in recipe:
        a=sets[prefix][r['a']][r['fa']]
        if r['b']:
            b=sets[prefix][r['b']][r['fb']];w=r['weight']
            a={k:(a[k][0].lerp(b[k][0],w),a[k][1].slerp(b[k][1],w),a[k][2].lerp(b[k][2],w)) for k in a}
        result.append(a)
    ns['materialize'](rig,result,name)
report=json.loads((OUT/'B1.json').read_text(encoding='utf8'))
report['sequence'].update({'frames':len(recipe),'run_unblended_frames':[run_start,run_end],'phase_continuity':'Per-clip phase advances through transitions; Attack remainder continues after its blend-in; no phase reset at a transition boundary'})
(OUT/'sequence_recipe.json').write_text(json.dumps(recipe),encoding='utf8')
ns['set_action'](rig,bpy.data.actions['LAB_B1_Idle'],1);scene=bpy.context.scene;scene.frame_start=1;scene.frame_end=121;scene.render.fps=30
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Motion_B1.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);obj.select_set(True);bpy.context.view_layer.objects.active=rig
export=OUT/'C02_B1_Body_Motions.fbx'
bpy.ops.export_scene.fbx(filepath=str(export),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
    axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,bake_anim_step=1.0,bake_anim_simplify_factor=0.0,path_mode='COPY',embed_textures=True,
    use_armature_deform_only=False)
report['fbx']='Motion/B1/C02_B1_Body_Motions.fbx';report['fbx_sha256']=hashlib.sha256(export.read_bytes()).hexdigest()
report['fbx_export']={'all_compatible_actions':True,'new_clip_names':['LAB_B1_Idle','LAB_B1_Walk','LAB_B1_Run','LAB_B1_Attack','LAB_B1_Sequence','LAB_Original_Sequence'],
                      'original_and_diagnostic_actions_included':True,'source_weights_max':5,'no_weight_truncation_requested':True,'animation_sample_step':1,'simplification':0,'axis_forward':'-Z','axis_up':'Y','textures_embedded':True}
report['fbx_roundtrip']={'status':'UNVERIFIED','reason':'Export completed; independent FBX reimport/Unity tests still required'}
(OUT/'B1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('B1_FBX_READY',json.dumps({'fbx':str(export),'frames':len(recipe),'run_unblended_frames':[run_start,run_end],'bytes':export.stat().st_size}))
