"""C2 rock kit from reviewed Seyeonjeong source geometry; run inside Blender MCP.

The original scene and vendor files are preserved. Output is a derived licensed asset,
not a Meshy generation. Coordinates are baked in metres, Z-up, bottom-centred.
"""
import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Oheangbu/Assets/_Project/Art/CodexWorld/RockKit'
SOURCE = ROOT / 'Oheangbu/Assets/SeyeonjeongPavilion/Mesh/Rock'
VARIANTS = ['A', 'C', 'D', 'F', 'I', 'K', 'L', 'N']


def build_variant(letter):
    scene = bpy.context.scene
    before = set(scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE / ('SM_Rock_' + letter + '.fbx')))
    imported = set(scene.objects) - before
    source = next(o for o in imported if o.type == 'MESH' and '_LOD0' in o.name)
    points = [source.matrix_world @ v.co for v in source.data.vertices]
    faces = [list(p.vertices) for p in source.data.polygons]
    low = Vector([min(p[a] for p in points) for a in range(3)])
    high = Vector([max(p[a] for p in points) for a in range(3)])
    divisor = max(high.x-low.x, high.y-low.y)
    pivot = Vector(((low.x+high.x)*.5, (low.y+high.y)*.5, low.z))
    mesh = bpy.data.meshes.new('InkRock_' + letter + '_Source')
    mesh.from_pydata([(p-pivot)/divisor for p in points], [], faces)
    mesh.update()
    obj = bpy.data.objects.new('InkRock_' + letter + '_LOD0', mesh)
    scene.collection.objects.link(obj)
    for old in imported:
        bpy.data.objects.remove(old, do_unlink=True)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00003)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    sub = obj.modifiers.new('Weathered edge continuity', 'SUBSURF')
    sub.levels = 1
    sub.render_levels = 1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    # Re-seat the base after subdivision; only uniform scale preserves rock proportions.
    zmin = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= zmin
    for face in obj.data.polygons:
        face.use_smooth = True
    tri = obj.modifiers.new('Stable triangulation', 'TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=tri.name)
    if len(obj.data.polygons) > 12000:
        budget = obj.modifiers.new('Near-view silhouette budget', 'DECIMATE')
        budget.ratio = 12000 / len(obj.data.polygons)
        bpy.ops.object.modifier_apply(modifier=budget.name)
    outputs = [obj]
    for level, ratio in [(1, .28), (2, .075)]:
        lod = obj.copy()
        lod.data = obj.data.copy()
        lod.name = 'InkRock_' + letter + '_LOD' + str(level)
        scene.collection.objects.link(lod)
        bpy.context.view_layer.objects.active = lod
        dec = lod.modifiers.new('Silhouette LOD', 'DECIMATE')
        dec.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=dec.name)
        outputs.append(lod)
    # Convex proxy from the coarsest silhouette, separate from rendered geometry.
    bm = bmesh.new()
    for v in outputs[-1].data.vertices:
        bm.verts.new(v.co)
    result = bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    unused = set(result.get('geom_interior', []) + result.get('geom_unused', []))
    bmesh.ops.delete(bm, geom=[v for v in unused if isinstance(v, bmesh.types.BMVert)], context='VERTS')
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    colmesh = bpy.data.meshes.new('InkRock_' + letter + '_COL')
    bm.to_mesh(colmesh)
    bm.free()
    col = bpy.data.objects.new(colmesh.name, colmesh)
    scene.collection.objects.link(col)
    bpy.context.view_layer.objects.active = col
    proxy = col.modifiers.new('Static collision budget', 'DECIMATE')
    proxy.ratio = min(1.0, 160 / max(1, len(colmesh.polygons)))
    bpy.ops.object.modifier_apply(modifier=proxy.name)
    outputs.append(col)
    bpy.ops.object.select_all(action='DESELECT')
    for output in outputs:
        output.select_set(True)
    bpy.context.view_layer.objects.active = obj
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(OUT / ('InkRock_' + letter + '.fbx')),
        use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
    record = {'variant':letter, 'source':str(SOURCE / ('SM_Rock_'+letter+'.fbx')),
        'sourceWorldDimensions':list(high-low), 'normalizationMetres':divisor,
        'output':str(OUT / ('InkRock_'+letter+'.fbx')),
        'meshes':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)} for o in outputs]}
    for output in outputs[1:]:
        output.hide_set(True)
    obj.location.x = VARIANTS.index(letter) * 1.5
    return record


def build_all():
    scene = bpy.data.scenes.new('Oheangbu_Derived_RockKit')
    bpy.context.window.scene = scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    records = [build_variant(letter) for letter in VARIANTS]
    (OUT / 'RockKit.provenance.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
    blend = ROOT / 'Art/Blender/C2_RockKit.blend'
    blend.parent.mkdir(parents=True, exist_ok=True)
    # Copy saves the workshop without redirecting the user's original working file.
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), copy=True)
    print(json.dumps(records, ensure_ascii=False))


if __name__ == '__main__':
    build_all()
