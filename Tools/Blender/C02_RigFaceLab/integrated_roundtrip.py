"""Empty-scene FBX readback with original motion poses, blink values and five-weight checks."""
import bpy,json,math
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Final'
NAMES=['C02_Mesh_0','FaceLid_Left','FaceLid_Right']

def points(obj):
    e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=e.to_mesh()
    a=np.array([tuple(e.matrix_world@v.co) for v in mesh.vertices],dtype=np.float64)
    e.to_mesh_clear();return a

def pose(rig,action,frame,lids,value):
    rig.animation_data_create();rig.animation_data.action=action
    if action.slots:rig.animation_data.action_slot=action.slots[0]
    rig.data.pose_position='POSE'
    for p in rig.pose.bones:p.matrix_basis.identity()
    for o in lids:
        if o.data.shape_keys:
            o.data.shape_keys.animation_data_clear()
            for k in list(o.data.shape_keys.key_blocks)[1:]:k.value=value
    bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()

def run(tag='B1_C3'):
    source=OUT/('Integrated_'+tag+'.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source))
    source_scene=bpy.context.scene;rig=bpy.data.objects['Armature'];meshes={n:bpy.data.objects[n] for n in NAMES}
    cases=[]
    idle='LAB_B2_Idle' if tag.startswith('B2') else 'LAB_B1_Idle'
    sequence='LAB_B2_Sequence' if tag.startswith('B2') else 'LAB_B1_Sequence'
    for name,frames in [('C02_Run',[1,10,20]),('C02_Idle',[1,37,121]),('C02_Attack',[1,20,50,91]),('C02_Walk',[1,16,32]),('LAB_B1_Run',[1,10,20]),(sequence,[1,42,43,75,156,198,208,289,331])]:
        cases.extend((name,f,0.) for f in frames)
    cases.extend((idle,1,v) for v in (0.,.25,.5,.75,1.,0.))
    refs=[]
    for name,frame,value in cases:
        pose(rig,bpy.data.actions[name],frame,list(meshes.values())[1:],value)
        refs.append({n:points(o) for n,o in meshes.items()})
    bone_names=set(rig.data.bones.keys());source_actions={n:bpy.data.actions[n] for n in {c[0] for c in cases}}
    source_key_names={n:list(o.data.shape_keys.key_blocks.keys())[1:] if o.data.shape_keys else [] for n,o in meshes.items()}
    # This readback is run in its own background Blender process, never the live MCP editor.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps=30
    bpy.ops.import_scene.fbx(filepath=str(OUT/('Integrated_'+tag+'.fbx')),use_anim=True)
    imported=list(bpy.data.objects);imported_actions=list(bpy.data.actions)
    irig=next(o for o in imported if o.type=='ARMATURE')
    imesh={name:next(o for o in imported if o.type=='MESH' and o.name.split('.')[0]==name) for name in NAMES}
    rows=[]
    for case,ref in zip(cases,refs):
        name,frame,value=case
        matches=[a for a in imported_actions if (a.name.endswith(name) or a.name.split('.')[0].endswith(name)) and any(s.target_id_type=='OBJECT' for s in a.slots)]
        if len(matches)!=1:raise RuntimeError('Ambiguous/missing imported action '+name+': '+str([a.name for a in matches]))
        ia=matches[0];pose(irig,ia,frame,list(imesh.values())[1:],value)
        for mesh_name,obj in imesh.items():
            actual=points(obj);expected=ref[mesh_name]
            tree=KDTree(len(expected))
            for i,p in enumerate(expected):tree.insert(Vector(p),i)
            tree.balance()
            error=max(tree.find(Vector(p))[2] for p in actual)
            rows.append({'action':name,'imported_action':ia.name,'frame':frame,'blink':value,'mesh':mesh_name,
              'source_vertices':len(expected),'imported_vertices':len(actual),'max_world_nearest_error_m':error,
              'finite':bool(np.isfinite(actual).all()),'status':'PASS' if error<1e-5 and len(actual)==len(expected) else 'FAIL'})
    stats=[]
    for name,o in imesh.items():
        o.data.calc_loop_triangles();counts={}
        for v in o.data.vertices:
            count=sum(g.weight>1e-8 for g in v.groups);counts[str(count)]=counts.get(str(count),0)+1
        stats.append({'mesh':name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'influence_counts':counts,
          'keys':list(o.data.shape_keys.key_blocks.keys())[1:] if o.data.shape_keys else []})
    source_images=[bpy.data.images.get('texture_0'),bpy.data.images.get('texture_0.001')]
    image_note={}
    if all(source_images):
        a=np.array(source_images[0].pixels[:]);b=np.array(source_images[1].pixels[:]);image_note={'equal_size':a.shape==b.shape,'pixel_max_abs_diff':float(np.max(np.abs(a-b))) if a.shape==b.shape else None}
    report={'status':'PASS_SERIALIZATION_ONLY' if all(r['status']=='PASS' for r in rows) and set(irig.data.bones.keys())==bone_names else 'FAIL',
      'source':'Final/Integrated_'+tag+'.blend','fbx':'Final/Integrated_'+tag+'.fbx','method':'Import into new empty scene. Same original/body-edited action poses and simultaneous blink strengths. Nearest world-point comparison, equal counts, no assumed FBX vertex-index correspondence.',
      'limitations':['Nearest-neighbor comparison is not a bijective vertex proof.','Representative motion frames only; original skinning audit covers all 578 integer source/diagnostic frames separately.','Serialization is not art, ground contact or naturalness acceptance.'],
      'cases':len(cases),'mesh_samples':len(rows),'source_bones':len(bone_names),'import_bones':len(irig.data.bones),'same_bone_names':set(irig.data.bones.keys())==bone_names,
      'mesh_statistics':stats,'checks':rows,'source_texture_pixel_comparison':image_note,'quality':'UNVERIFIED'}
    (OUT/('fbx_roundtrip.json' if tag=='B1_C3' else 'fbx_roundtrip_'+tag+'.json')).write_text(json.dumps(report,indent=2),encoding='utf8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('Integrated_'+tag+'_FBX_Roundtrip.blend')))
    print(json.dumps({'status':report['status'],'cases':len(cases),'maximum_error_m':max(r['max_world_nearest_error_m'] for r in rows),'statistics':stats,'textures':image_note}))

if __name__=='__main__':
    import sys
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    run(args[0] if args else 'B1_C3')
