"""Read-only original motion diagnostics in disposable Blender; writes only Motion JSON."""
import bpy, json, math, hashlib
import numpy as np
from pathlib import Path

ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'
OUT=LAB/'Motion'

def assign(action,frame):
    rig=bpy.data.objects['Armature']
    for p in rig.pose.bones:p.matrix_basis.identity()
    rig.animation_data_create();rig.animation_data.action=action
    if action.slots:rig.animation_data.action_slot=action.slots[0]
    rig.data.pose_position='POSE';bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
    return rig

def world_vertices(obj):
    ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
    v=np.empty((len(m.vertices),3),dtype=np.float64);m.vertices.foreach_get('co',v.ravel())
    mat=np.array(ev.matrix_world);v=v@mat[:3,:3].T+mat[:3,3]
    ev.to_mesh_clear();return v

def diagnose():
    OUT.mkdir(parents=True,exist_ok=True)
    rig=bpy.data.objects['Armature'];mesh=bpy.data.objects['C02_Mesh_0'];fps=bpy.context.scene.render.fps/bpy.context.scene.render.fps_base
    rig.animation_data_clear();rig.data.pose_position='REST';bpy.context.view_layer.update()
    rest=world_vertices(mesh);sole={}
    for side in ('Left','Right'):
        group=mesh.vertex_groups.get(side+'Foot')
        ids=np.array([v.index for v in mesh.data.vertices if any(g.group==group.index and g.weight>=.7 for g in v.groups)],dtype=int)
        cutoff=float(np.percentile(rest[ids,2],15));ids=ids[rest[ids,2]<=cutoff]
        sole[side]=ids
    report={'kind':'baseline_original_motion_diagnostic','source':bpy.data.filepath,'source_sha256':hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest(),
            'blender_version':bpy.app.version_string,'fps':fps,'render_triangles':len(mesh.data.polygons),'bone_names':list(rig.data.bones.keys()),
            'finger_bones':[n for n in rig.data.bones.keys() if any(s in n.lower() for s in ('thumb','index','middle','ring','pinky','finger'))],
            'sole_probe_method':'Rest lowest 15% vertices weighted >=0.7 to matching Foot; centroid probes. Contact inferred from probe height, not measured pressure.',
            'sole_vertices':{k:len(v) for k,v in sole.items()},'game_move_speed_mps':4.5,
            'game_speed_source':'Oheangbu/Assets/_Project/Data/World/WorldScale_Cheongrim.asset walkSpeedMps (CombatConfig mirror); actual Unity locomotion not run',
            'forward_world_axis':[0,-1,0],'clips':[],'production_actions_modified':False,'paid_requests':0}
    tracks={}
    for name in ('C02_Idle','C02_Walk','C02_Run','C02_Attack'):
        a=bpy.data.actions[name];start,end=[int(round(v)) for v in a.frame_range];samples=[];rots=[]
        for frame in range(start,end+1):
            assign(a,frame);v=world_vertices(mesh)
            samples.append({'frame':frame,'hips':list(rig.matrix_world@rig.pose.bones['Hips'].head),
                'feet':{side:{'centroid':np.mean(v[ids],axis=0).tolist(),'minimum_z':float(np.min(v[ids,2]))} for side,ids in sole.items()},
                'bone_heads':{p.name:list(rig.matrix_world@p.head) for p in rig.pose.bones}})
            rots.append({p.name:p.matrix.to_quaternion().copy() for p in rig.pose.bones})
        hp=np.array([s['hips'] for s in samples]);period=(end-start)/fps
        joint_seams={n:math.degrees(rots[0][n].rotation_difference(rots[-1][n]).angle) for n in rots[0]}
        row={'action':name,'frames':[start,end],'fps':fps,'duration_seconds':period,'sampled_integer_frames':len(samples),
             'hips_net_m':(hp[-1]-hp[0]).tolist(),'hips_horizontal_excursion_m':np.ptp(hp[:,:2],axis=0).tolist(),
             'in_place_classification':'in_place_or_local_sway' if np.linalg.norm(hp[-1,:2]-hp[0,:2])<.08 else 'translated_root',
             'loop_endpoint_bone_rotation_delta_degrees':joint_seams,
             'worst_endpoint_rotation_degrees':max(joint_seams.values()),'feet':{}}
        native=[]
        for side in sole:
            pos=np.array([s['feet'][side]['centroid'] for s in samples]);low=np.array([s['feet'][side]['minimum_z'] for s in samples]);vel=np.gradient(pos,1/fps,axis=0)
            # A repeatable candidate support window, not a final physical-contact verdict.
            support=(low<=np.min(low)+.025)&(np.abs(vel[:,2])<.55)
            speed=float(np.median(vel[support,1])) if np.any(support) else None
            if speed is not None:native.append(speed)
            row['feet'][side]={'floor_min_z_m':float(low.min()),'floor_max_z_m':float(low.max()),'support_frames':[samples[i]['frame'] for i in np.flatnonzero(support)],
                              'stance_derived_forward_speed_mps':speed,'seam_centroid_jump_m':float(np.linalg.norm(pos[-1]-pos[0])),
                              'stance_speed_mad_mps':float(np.median(np.abs(vel[support,1]-speed))) if speed is not None else None,
                              'support_probe_slip_at_game_4_5_mps_m_per_second':float(np.median(np.abs(vel[support,1]-4.5))) if speed is not None else None}
        row['estimated_native_forward_speed_mps']=float(np.median(native)) if native else None
        report['clips'].append(row);tracks[name]=samples
    report['finger_open_close']={'status':'NOT_ATTEMPTED','reason':'No finger deformation bones in source' if not report['finger_bones'] else 'Existing finger bones require actual skin tests'}
    report['weapon_grip']={'status':'NOT_ATTEMPTED','reason':'Original Attack is unarmed; finger skin contact and existing brush not tested'}
    report['animation_naturalness']={'status':'UNVERIFIED','reason':'Diagnostic probes and loop deltas only; before-after continuous rendered comparison pending'}
    (OUT/'baseline_motion_diagnostic.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    (OUT/'baseline_motion_samples.json').write_text(json.dumps(tracks,ensure_ascii=False),encoding='utf8')
    print('MOTION_BASELINE_DIAGNOSTIC',json.dumps({'fps':fps,'finger_bones':report['finger_bones'],'clips':[{k:r[k] for k in ('action','duration_seconds','hips_net_m','worst_endpoint_rotation_degrees','estimated_native_forward_speed_mps')} for r in report['clips']]}))

if __name__=='__main__':diagnose()
