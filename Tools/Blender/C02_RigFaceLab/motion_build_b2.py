"""One scoped B2 Idle correction: unit Hips scale, grounded pelvis/leg poses, fixed root."""
import bpy,json,hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Motion/B2'
ns={};p=ROOT/'Tools/Blender/C02_RigFaceLab/motion_build_b1.py';exec(compile(p.read_text(encoding='utf8'),str(p),'exec'),ns)
OUT.mkdir(parents=True,exist_ok=True)
if (OUT/'B2.json').exists():raise RuntimeError('B2 already recorded; no silent extra candidate')
rig=bpy.data.objects['Armature'];obj=bpy.data.objects['C02_Mesh_0'];root_matrix=np.array(rig.matrix_world);mesh_matrix=np.array(obj.matrix_world)
source=Path(bpy.data.filepath);sourcehash=hashlib.sha256(source.read_bytes()).hexdigest()
rig.animation_data_clear();rig.data.pose_position='REST';bpy.context.view_layer.update();rest=ns['vertices'](obj);shoe={}
for side in ('Left','Right'):
    groups={obj.vertex_groups[n].index for n in (side+'Foot',side+'ToeBase',side+'Leg')}
    shoe[side]=np.array([v.index for v in obj.data.vertices if rest[v.index,2]<.20 and sum(g.weight for g in v.groups if g.group in groups)>.5])
original=bpy.data.actions['LAB_B1_Idle'];count=int(original.frame_range[1]);normalized=[]
for frame in range(1,count+1):
    ns['set_action'](rig,original,frame);rig.pose.bones['Hips'].scale=(1,1,1);bpy.context.view_layer.update();v=ns['vertices'](obj)
    normalized.append({s:float(v[ids,2].min()) for s,ids in shoe.items()})
pelvis_dz=.005-float(np.median([min(row.values()) for row in normalized]))
poses=[];rows=[];ik=[]
for frame in range(1,count+1):
    ns['set_action'](rig,original,frame);hips=rig.pose.bones['Hips'];original_scale=list(hips.scale);hips.scale=(1,1,1);bpy.context.view_layer.update()
    matrix=hips.matrix.copy();matrix.translation+=rig.matrix_world.inverted().to_3x3()@Vector((0,0,pelvis_dz));hips.matrix=matrix;bpy.context.view_layer.update()
    before=ns['vertices'](obj)
    for side,ids in shoe.items():ik.append(ns['foot_ik'](rig,side,(0,0,.005-float(before[ids,2].min()))))
    poses.append(ns['capture'](rig));points=ns['vertices'](obj)
    rows.append({'frame':frame,'hips_scale':list(hips.scale),'source_hips_scale':original_scale,'hips_world':list(rig.matrix_world@hips.head),
                 'shoe_min_z_m':{s:float(points[ids,2].min()) for s,ids in shoe.items()},'mesh_min_z_m':float(points[:,2].min()),'mesh_max_z_m':float(points[:,2].max())})
poses[-1]={bone:(loc.copy(),q.copy(),sc.copy()) for bone,(loc,q,sc) in poses[0].items()}
idle=ns['materialize'](rig,poses,'LAB_B2_Idle')
recipe=json.loads((LAB/'Motion/B1/sequence_recipe.json').read_text(encoding='utf8'));sets={'Idle':poses}
for label in ('Run','Walk','Attack'):
    a=bpy.data.actions['LAB_B1_'+label];sets[label]=[]
    for frame in range(1,int(a.frame_range[1])+1):ns['set_action'](rig,a,frame);sets[label].append(ns['capture'](rig))
sequence=[]
for r in recipe:
    a=sets[r['a']][r['fa']]
    if r['b']:
        b=sets[r['b']][r['fb']];w=r['weight'];a={k:(a[k][0].lerp(b[k][0],w),a[k][1].slerp(b[k][1],w),a[k][2].lerp(b[k][2],w)) for k in a}
    sequence.append(a)
ns['materialize'](rig,sequence,'LAB_B2_Sequence')
assert np.array_equal(root_matrix,np.array(rig.matrix_world)) and np.array_equal(mesh_matrix,np.array(obj.matrix_world))
report={'stage':'B','attempt':2,'source_blend':str(source),'source_sha256':sourcehash,'status':'UNIT_SCALE_GROUNDED_IDLE_AWAITING_UNITY',
        'hypothesis':'Source Idle Hips scale 1.1764704 is discarded by Unity Humanoid. Normalize only new Idle to unit Hips scale and recalculate grounded pelvis/leg pose.',
        'method':'Fixed armature object and fixed ground z=0. Constant anatomical pelvis animation correction derived from median actual shoe minima after unit scale, then rotational two-bone foot IK per source frame; no root or mesh offset.',
        'root_transform_changed':False,'ground_z':0,'mesh_uv_weights_rest_changed':False,'new_bones':0,'new_clothing_bones':0,
        'source_scale':rows[0]['source_hips_scale'],'pelvis_animation_z_correction_m':pelvis_dz,'shoe_probe_method':'All rest vertices below 0.20m with same-side Foot/ToeBase/Leg influence sum>0.5; includes previously omitted shoe sole vertices',
        'shoe_probe_counts':{s:len(ids) for s,ids in shoe.items()},'ik_max_error_m':max(ik),'frames':rows,
        'clips':{'Idle':'LAB_B2_Idle','Walk':'LAB_B1_Walk','Run':'LAB_B1_Run','Attack':'LAB_B1_Attack','Sequence':'LAB_B2_Sequence'},
        'original_and_B1_idle_preserved':True,'sequence_frames':len(sequence),'additional_paid_credits':0,
        'animation_naturalness':{'status':'UNVERIFIED','reason':'One scoped source-scale correction, numerical grounding and two rendered samples; Unity Humanoid validation pending'},
        'secondary_motion':{'status':'NOT_ATTEMPTED','reason':'No cloth auxiliary changes'},'blend':'Motion/B2/Motion_B2.blend'}
ns['set_action'](rig,idle,1);bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=count;bpy.context.scene.render.fps=30
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Motion_B2.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);obj.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=OUT/'C02_B2_Body_Motions.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',
    use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,
    bake_anim_step=1,bake_anim_simplify_factor=0,path_mode='COPY',embed_textures=True,use_armature_deform_only=False)
report['fbx']='Motion/B2/C02_B2_Body_Motions.fbx';report['fbx_sha256']=hashlib.sha256(fbx.read_bytes()).hexdigest()
(OUT/'B2.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('B2_READY',json.dumps({'fbx':str(fbx),'pelvis_dz_m':pelvis_dz,'frames':[rows[i] for i in (0,count//2,count-1)]}),flush=True)
# Two actual isolated previews. No source save after presentation-only setup.
scene=bpy.context.scene
for engine in ('BLENDER_EEVEE_NEXT','BLENDER_EEVEE'):
    try:scene.render.engine=engine;break
    except TypeError:continue
for attr in ('taa_render_samples','taa_samples'):
    if hasattr(scene.eevee,attr):setattr(scene.eevee,attr,8)
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
camera=scene.camera;target=Vector((0,0,.80));camera.location=target+Vector((.7,-5,1));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=3.3
if not any(o.type=='MESH' and o!=obj and 'ground' in o.name.lower() for o in scene.objects):
    bpy.ops.mesh.primitive_plane_add(size=20,location=(0,0,0));ground=bpy.context.object;ground.name='B2_PreviewGround_z0'
(OUT/'Previews').mkdir(exist_ok=True)
for frame in (1,61):
    ns['set_action'](rig,idle,frame);scene.render.filepath=str(OUT/'Previews'/f'B2_Idle_{frame:03d}.png');bpy.ops.render.render(write_still=True)
print('B2_PREVIEWS_COMPLETE',flush=True)
