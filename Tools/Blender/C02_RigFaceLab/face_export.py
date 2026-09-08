"""Export an isolated facial experiment, then import into an empty Blender scene.
Serialization results do not imply anatomical/art acceptance.
"""
import bpy,json,sys,hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
candidate=args[0] if args else 'C1';folder=OUT/candidate
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
rig.animation_data_clear();rig.data.pose_position='POSE'
for p in rig.pose.bones:p.matrix_basis.identity()
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and (o.get('candidate_mesh') or o.get('face_lab_role'))]
textures=OUT/'Textures';textures.mkdir(exist_ok=True)
texture_records=[]
used_materials={m for o in meshes for m in o.data.materials if m}
used_images={n.image for m in used_materials if m.use_nodes for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image}
for i in used_images:
    if i.type!='IMAGE':continue
    safe=''.join(c if c.isalnum() or c in '._-' else '_' for c in i.name)
    if not safe.lower().endswith('.png'):safe+='.png'
    destination=textures/safe
    if i.packed_file:
        destination.write_bytes(bytes(i.packed_file.data))
        i.unpack(method='REMOVE')
    else:
        original_file=Path(bpy.path.abspath(i.filepath))
        if original_file.is_file() and not i.is_dirty:
            # Read before changing the path: unloaded images otherwise resolve a
            # stale destination file when save() first asks for their pixels.
            source_bytes=original_file.read_bytes()
            if original_file.resolve()!=destination.resolve():destination.write_bytes(source_bytes)
        else:
            _=i.pixels[:]
            i.filepath_raw=str(destination);i.file_format='PNG';i.save()
    i.filepath=str(destination);i.filepath_raw=str(destination)
    texture_records.append({'image':i.name,'path':str(destination),'size':list(i.size),'sha256':hashlib.sha256(destination.read_bytes()).hexdigest()})
def set_keys(value):
    for o in meshes:
        if o.data.shape_keys:
            o.data.shape_keys.animation_data_clear()
            for k in list(o.data.shape_keys.key_blocks)[1:]:k.value=value
    bpy.context.view_layer.update()
def points(o):
    e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();p=[tuple(e.matrix_world@v.co) for v in m.vertices];e.to_mesh_clear();return p
reference={}
for value in (0,.5,1):
    set_keys(value);reference[str(value)]={o.name:points(o) for o in meshes}
set_keys(0)
inventory=[]
for o in meshes:
    o.data.calc_loop_triangles();inventory.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'keys':list(o.data.shape_keys.key_blocks.keys()) if o.data.shape_keys else []})
bpy.ops.object.select_all(action='DESELECT')
selected=set(meshes+[rig])
for o in list(selected):
    p=o.parent
    while p:selected.add(p);p=p.parent
for o in selected:o.select_set(True)
bpy.context.view_layer.objects.active=rig
fbx=folder/f'Face_{candidate}.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},use_mesh_modifiers=False,
    add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=str(folder/f'Face_{candidate}.blend'))
bones=[b.name for b in rig.data.bones]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True)
checks=[]
for value in (0,.5,1):
    for name,expected in reference[str(value)].items():
        o=bpy.data.objects.get(name)
        if o is None:checks.append({'mesh':name,'value':value,'status':'FAIL_MISSING_MESH'});continue
        if o.data.shape_keys:
            for k in list(o.data.shape_keys.key_blocks)[1:]:k.value=value
        bpy.context.view_layer.update();actual=points(o)
        tree=KDTree(len(expected))
        for i,p in enumerate(expected):tree.insert(Vector(p),i)
        tree.balance();max_error=max(tree.find(Vector(p))[2] for p in actual)
        checks.append({'mesh':name,'value':value,'import_vertices':len(actual),'source_vertices':len(expected),'max_nearest_world_error_m':max_error,'status':'PASS' if max_error<1e-5 else 'FAIL'})
import_rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
result={'candidate':candidate,'status':'PASS_SERIALIZATION_ONLY' if all(c['status']=='PASS' for c in checks) and set(bones)==set(b.name for b in import_rig.data.bones) else 'FAIL',
 'fbx':str(fbx),'source_blend':str(folder/f'Face_{candidate}.blend'),'checks':checks,'source_bones':len(bones),'imported_bones':len(import_rig.data.bones),
 'source_inventory':inventory,'total_triangles':sum(x['triangles'] for x in inventory),'textures':texture_records,
 'method':'Empty-scene reimport. Every imported vertex is compared with its nearest reference vertex in world space at simultaneous channel values 0/.5/1, with equal vertex counts and matching bone names. UV-split ordering and a bijective correspondence are not assumed.',
 'unity':'UNVERIFIED','anatomical_quality':'UNVERIFIED'}
(folder/'fbx_roundtrip.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(folder/f'Face_{candidate}_FBX_Roundtrip.blend'))
print('FACE_EXPORT_RESULT',json.dumps(result))
