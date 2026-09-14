"""Apply validated local waist separation to both source blends, stage model FBX.

Does not copy into Unity Assets. Parent owns coordinated final staging copy.
Seventeen existing animation FBXs, bone/rest, hand skin and feet are invariants.
"""
import bpy,json,hashlib,struct,datetime
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu'); ART=ROOT/'Art/Player'
UNITY=ROOT/'Oheangbu/Assets/_Project/Art/Characters/Dosa'
STAGE=ART/'ModelStaging'
def digest(value):return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def filehash(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def weights(mesh, indices):
    return [[i,[[mesh.vertex_groups[g.group].name,g.weight] for g in mesh.data.vertices[i].groups]] for i in indices]
def protected(mesh,rig,original_count):
    hands={i for p in mesh.data.polygons if p.material_index==1 for i in p.vertices}
    feet=[v.index for v in mesh.data.vertices[:original_count] if v.co.z<.72]
    return {'original_positions':digest([list(v.co) for v in mesh.data.vertices[:original_count]]),
            'hand_weights':digest(weights(mesh,sorted(hands))),
            'foot_weights':digest(weights(mesh,feet)),
            'rig_rest':digest([[b.name,list(b.head_local),list(b.tail_local),list(sum((tuple(row) for row in b.matrix_local),()))] for b in rig.data.bones])}
def mesh_signature(mesh):
    mesh.data.calc_loop_triangles()
    return {'vertices':len(mesh.data.vertices),'triangles':len(mesh.data.loop_triangles),
            'geometry_and_weights_sha256':digest({'vertices':[list(v.co) for v in mesh.data.vertices],'weights':weights(mesh,range(len(mesh.data.vertices))),'faces':[list(p.vertices) for p in mesh.data.polygons]}),
            'uv_sha256':digest([[list(v.uv) for v in layer.data] for layer in mesh.data.uv_layers])}
def face_uv(mesh, count):
    atlas=mesh.data.uv_layers['uv']
    return {tuple(sorted(p.vertices)):sorted((mesh.data.loops[li].vertex_index,list(atlas.data[li].uv)) for li in p.loop_indices)
            for p in mesh.data.polygons if all(i<count for i in p.vertices)}
def actions_signature():
    result={}
    for action in bpy.data.actions:
        if not action.name.startswith('Dosa_'):continue
        channels=[]
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        channels.append([fc.data_path,fc.array_index,[[list(k.co),list(k.handle_left),list(k.handle_right),k.interpolation] for k in fc.keyframe_points]])
        result[action.name]=digest(channels)
    return result
animation_hashes={p.name:filehash(p) for p in (UNITY/'Animations').glob('*.fbx')}
assert len(animation_hashes)==17
checks=[]
for name in ['Dosa_Refined','Dosa_Animated']:
    bpy.ops.wm.open_mainfile(filepath=str(ART/(name+'.blend')))
    rig=bpy.data.objects['Dosa_Rig']; mesh=bpy.data.objects['Dosa_Body']; count=len(mesh.data.vertices)
    before=protected(mesh,rig,count); original_uv=face_uv(mesh,count)
    before_actions=actions_signature(); active=rig.animation_data.action if rig.animation_data else None
    saved_frame=bpy.context.scene.frame_current
    exec(compile((ROOT/'Tools/Blender/Player/separate_waist_sleeve_bridges.py').read_text(),'separate','exec'),{})
    assert protected(mesh,rig,count)==before, 'Protected source data changed'
    surviving_uv=face_uv(mesh,count)
    assert all(original_uv[k]==v for k,v in surviving_uv.items()), 'Surviving original face UV changed'
    exec(compile((ROOT/'Tools/Blender/Player/export_models.py').read_text(),'export_models','exec'),{'DOSA_EXPORT_ROOT':str(STAGE),'DOSA_SAVE_REFINED':False})
    if active:
        rig.animation_data_create();rig.animation_data.action=active
        if len(active.slots):rig.animation_data.action_slot=active.slots[0]
        bpy.context.scene.frame_set(saved_frame)
    assert actions_signature()==before_actions, 'Action data changed'
    assert protected(mesh,rig,count)==before, 'Export altered protected source data'
    arms=bpy.data.objects['Dosa_Arms']
    checks.append({'file':name,'protected_original_data_unchanged':True,'surviving_original_face_uv_unchanged':True,'actions_unchanged':True,
                   'signatures':before,'body':mesh_signature(mesh),'arms':mesh_signature(arms)})
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/(name+'.blend')))
assert checks[0]['body']==checks[1]['body'] and checks[0]['arms']==checks[1]['arms'], 'Source meshes differ'
assert animation_hashes=={p.name:filehash(p) for p in (UNITY/'Animations').glob('*.fbx')}
report={'updated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'staged_only':True,'animation_fbx_unchanged_count':17,
        'animation_fbx_sha256':animation_hashes,'blend_checks':checks,'stage_models':[str(p) for p in (STAGE/'Models').glob('*.fbx')],
        'scope':'Local scan-bridge face removal and source-UV inner sleeve addition; surviving original UV/positions retained; no hand, foot, rest or action change.'}
(ART/'waist-sleeve-export-validation.json').write_text(json.dumps(report,indent=2))
model_report=json.loads((ART/'export-models-report.json').read_text())
model_report['models']=[str(UNITY/'Models'/p.name) for p in (STAGE/'Models').glob('*.fbx')]
(ART/'export-models-report.json').write_text(json.dumps(model_report,indent=2))
print('STAGED_VALIDATED',json.dumps({'body':checks[0]['body'],'arms':checks[0]['arms'],'animation_unchanged':17}))
