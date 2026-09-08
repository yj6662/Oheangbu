"""Append only verified facial additions to a compatible independently saved body.
This file is definitions-only; the owning body task chooses when/where to save.
No original mesh, weights, rest bones or source Action is altered.
"""
from pathlib import Path
import bpy,json
from mathutils import Matrix
ROOT=Path('C:/Users/yj666/Oheangbu')
FACE=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face'

def merge_face_c3(target_rig_name):
    target=bpy.data.objects[target_rig_name]
    if target.type!='ARMATURE' or 'Head' not in target.data.bones:raise ValueError('Compatible target Head bone required')
    source=FACE/'C3/Face_C3.blend'
    if not source.is_file():raise FileNotFoundError(source)
    if any(bpy.data.objects.get(name) for name in ['FaceLid_Left','FaceLid_Right']):raise ValueError('Face additions already exist; refusing duplicates')
    before=set(bpy.data.objects)
    with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
        wanted=['FaceLid_Left','FaceLid_Right']
        if not all(name in available.objects for name in wanted):raise ValueError('Source eyelid objects missing')
        loaded.objects=wanted
    additions=list(loaded.objects);source_rigs={m.object for o in additions for m in o.modifiers if m.type=='ARMATURE'}
    for source_rig in source_rigs:
        if source_rig is None:raise ValueError('Missing source armature')
        for name in ['Hips','neck','Head']:
            a=source_rig.data.bones[name].matrix_local;b=target.data.bones[name].matrix_local
            if max(abs(a[i][j]-b[i][j]) for i in range(4) for j in range(4))>1e-5:
                raise ValueError('Body rest skeleton changed at '+name+'; facial binding requires revalidation')
    for o in additions:
        bpy.context.scene.collection.objects.link(o)
        for m in o.modifiers:
            if m.type=='ARMATURE':m.object=target
        for k in list(o.data.shape_keys.key_blocks)[1:]:k.value=0
    # Remove only newly loaded, unlinked dependency rigs. Existing scene data is protected.
    for o in list(set(bpy.data.objects)-before):
        if o not in additions and not o.users_collection and o.users==0:bpy.data.objects.remove(o)
    return {'source':str(source),'objects':[o.name for o in additions],'added_triangles':1024,'extra_bones':0,
      'source_body_geometry_changed':False,'target_body_geometry_changed':False,'target_body_weights_changed':False,
      'requires':'Re-export complete selected body and both lids; validate with final body animation in Unity.'}
