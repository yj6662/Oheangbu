"""Read-only numerical comparisons of frozen B1 actions; probe limits stay explicit."""
import bpy,json,math,hashlib
import numpy as np
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Motion/B1'
ns={};p=ROOT/'Tools/Blender/C02_RigFaceLab/motion_build_b1.py';exec(compile(p.read_text(encoding='utf8'),str(p),'exec'),ns)
rig=bpy.data.objects['Armature'];obj=bpy.data.objects['C02_Mesh_0'];baseline=json.loads((LAB/'Motion/baseline_motion_samples.json').read_text(encoding='utf8'))
diagnostic=json.loads((LAB/'Motion/baseline_motion_diagnostic.json').read_text(encoding='utf8'))
source_hash=hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest();rig.animation_data_clear();rig.data.pose_position='REST';bpy.context.view_layer.update();rest=ns['vertices'](obj);probes={}
for side in ('Left','Right'):
    group=obj.vertex_groups[side+'Foot'];ids=np.array([v.index for v in obj.data.vertices if any(g.group==group.index and g.weight>=.7 for g in v.groups)])
    probes[side]=ids[rest[ids,2]<=np.percentile(rest[ids,2],15)]
result={'source_blend':bpy.data.filepath,'source_sha256':source_hash,'kind':'frozen_B1_motion_numeric_comparison','frame_rate':30,'game_config_changed':False,
        'probe_limit':'Rest lowest 15% of matching Foot-weight >=.7 vertices; these shoe probes do not establish all cloth/body contact or clinical foot contact. Support windows fixed from original height/vertical speed heuristic.',
        'clips':[],'original_bone_heads_max_difference_from_baseline_m':0,'scale_max_difference_from_original':0,
        'animation_naturalness':{'status':'UNVERIFIED','reason':'Numerical changes measured; final continuous visual/Unity behavior judgment separate'},
        'secondary_motion':{'status':'NOT_ATTEMPTED','reason':'No validated stable integrated-cloth region selected'},'paid_requests':0}
for label in ('Idle','Walk','Run','Attack'):
    orig_name='C02_'+label;new_name='LAB_B1_'+label;proto=next(c for c in diagnostic['clips'] if c['action']==orig_name)
    data={}
    for name in (orig_name,new_name):
        a=bpy.data.actions[name];rows=[]
        for frame in range(int(a.frame_range[0]),int(a.frame_range[1])+1):
            ns['set_action'](rig,a,frame);points=ns['vertices'](obj)
            row={'frame':frame,'hips':np.array(rig.matrix_world@rig.pose.bones['Hips'].head),'minimum_mesh_z':float(points[:,2].min()),
                 'feet':{s:{'centroid':points[ids].mean(0),'min_z':float(points[ids,2].min())} for s,ids in probes.items()},
                 'bone_heads':{p.name:np.array(rig.matrix_world@p.head) for p in rig.pose.bones},'scales':{p.name:np.array(p.scale) for p in rig.pose.bones}}
            if name==orig_name:
                old=baseline[orig_name][frame-1]
                for bone,point in row['bone_heads'].items():result['original_bone_heads_max_difference_from_baseline_m']=max(result['original_bone_heads_max_difference_from_baseline_m'],float(np.linalg.norm(point-np.array(old['bone_heads'][bone]))))
            rows.append(row)
        data[name]=rows
    native=proto['estimated_native_forward_speed_mps'] if label in ('Walk','Run') else 0.
    clip={'original':orig_name,'edited':new_name,'duration_seconds':proto['duration_seconds'],'native_translation_speed_mps':native,'support_probe_results':{}}
    for side in probes:
        support=np.array(proto['feet'][side]['support_frames'])-1
        groups=[g for g in np.split(support,np.flatnonzero(np.diff(support)>1)+1) if len(g)>1]
        for which,name in [('original',orig_name),('edited',new_name)]:
            rows=data[name];positions=np.array([r['feet'][side]['centroid'] for r in rows]);low=np.array([r['feet'][side]['min_z'] for r in rows]);positions[:,1]-=native*np.arange(len(rows))/30
            slips=[float(np.linalg.norm(np.ptp(positions[g,:2],axis=0))) for g in groups]
            vals={'maximum_support_window_horizontal_extent_m':max(slips,default=None),'support_window_extents_m':slips,
                  'minimum_sole_z_m':float(low.min()),'maximum_support_sole_z_m':float(low[support].max()) if len(support) else None,
                  'loop_endpoint_foot_delta_m':float(np.linalg.norm(rows[0]['feet'][side]['centroid']-rows[-1]['feet'][side]['centroid']))}
            clip['support_probe_results'].setdefault(side,{})[which]=vals
    clip['original_hips_net_m']=(data[orig_name][-1]['hips']-data[orig_name][0]['hips']).tolist();clip['edited_hips_net_m']=(data[new_name][-1]['hips']-data[new_name][0]['hips']).tolist()
    clip['original_mesh_min_z_m']=min(r['minimum_mesh_z'] for r in data[orig_name]);clip['edited_mesh_min_z_m']=min(r['minimum_mesh_z'] for r in data[new_name])
    for a,b in zip(data[orig_name],data[new_name]):
        for bone in a['scales']:result['scale_max_difference_from_original']=max(result['scale_max_difference_from_original'],float(np.max(np.abs(a['scales'][bone]-b['scales'][bone]))))
    result['clips'].append(clip)
result['original_motion_preservation']='PASS' if result['original_bone_heads_max_difference_from_baseline_m']<1e-5 else 'FAIL'
result['scale_preservation']='PASS' if result['scale_max_difference_from_original']<1e-5 else 'FAIL'
result['no_finger_bones']=not any(any(term in b.name.lower() for term in ('thumb','index','middle','ring','pinky','finger')) for b in rig.data.bones)
obj.data.calc_loop_triangles();result['actual_triangles']=len(obj.data.loop_triangles);result['actual_bones']=len(rig.data.bones);result['maximum_vertex_influences']=max(sum(g.weight>1e-6 for g in v.groups) for v in obj.data.vertices)
(OUT/'motion_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print('B1_VALIDATION',json.dumps({k:result[k] for k in ('original_motion_preservation','original_bone_heads_max_difference_from_baseline_m','scale_preservation','scale_max_difference_from_original','actual_triangles','actual_bones','maximum_vertex_influences')}))
