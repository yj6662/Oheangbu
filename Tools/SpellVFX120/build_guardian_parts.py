"""Run build() through Blender MCP in the separate SpellVFX120 lab, not a player scene.

Preserves the original object/file and partitions complete disconnected components.
No new geometry, automatic weights, topology reduction or production animation.
Unity must import ALL named meshes with their mesh-local pivot coordinates intact;
the current ExternalMesh/GetComponentInChildren/whole-mesh recenter path is unsuitable.
"""
from pathlib import Path
import hashlib
import json
import math

ROOT = Path('C:/Users/yj666/Oheangbu')
OUTPUT = ROOT / 'Art/SpellVFX120/Blender/GuardianParts'
NAMES = ['Pelvis', 'Torso', 'Head', 'LeftArm', 'LeftFist', 'RightArm',
         'RightFist', 'LeftLeg', 'LeftFoot', 'RightLeg', 'RightFoot']
PARENTS = [-1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9]
# Original author's Y-up/+Z-face coordinates, before full-body normalization.
PIVOTS = [(0, .30, .015), (0, .42, 0), (0, .94, .018),
          (-.40, .81, .005), (-.50, .46, .04), (.40, .81, .005),
          (.50, .46, .04), (-.18, .23, .03), (-.18, -.015, .10),
          (.18, .23, .03), (.18, -.015, .10)]


def classify(center):
    x, y, z = center
    if y > .92:
        return 2
    if abs(x) > .38 and y < .72:
        return (3 if x < 0 else 5) if y > .44 else (4 if x < 0 else 6)
    if abs(x) > .08 and y < .23:
        return (7 if x < 0 else 9) if y > .015 else (8 if x < 0 else 10)
    return 0 if y < .42 else 1


def build():
    import bpy
    import bmesh
    from mathutils import Matrix, Vector
    if 'SpellVFX120' not in bpy.data.filepath:
        raise RuntimeError('Open only the separate SpellVFX120 lab before running this script.')
    source = bpy.data.objects.get('StoneGuardian_Jangseung_B1')
    if source is None or source.type != 'MESH':
        raise RuntimeError('Missing untouched StoneGuardian_Jangseung_B1 source mesh.')
    if bpy.data.collections.get('GuardianParts_11') is not None:
        raise RuntimeError('Derived GuardianParts_11 already exists. Reload the preserved source lab before rebuilding.')
    if source.modifiers or any(abs(source.matrix_world[i][j] - (1 if i == j else 0)) > 1e-5 for i in range(4) for j in range(4)):
        raise RuntimeError('Unexpected source transform/modifier; refusing to apply or bake it blindly.')
    stats_path = ROOT / 'Art/SpellVFX120/Blender/stone_guardian_stats.json'
    stats = json.loads(stats_path.read_text(encoding='utf-8-sig'))
    if len(source.data.vertices) != stats['vertices']:
        raise RuntimeError('Source vertex count differs from audited original. Re-audit partition before proceeding.')
    scale = stats['normalization_scale']
    offset = Vector(stats['normalization_offset_blender'])
    b2u = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))
    u2b = b2u.inverted()
    source.data.calc_loop_triangles()
    source_triangles = len(source.data.loop_triangles)
    if source_triangles != 2720:
        raise RuntimeError('Expected original 2720 triangles; refusing guessed component grouping.')
    parent = list(range(len(source.data.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for polygon in source.data.polygons:
        for vertex in polygon.vertices[1:]:
            parent[find(vertex)] = find(polygon.vertices[0])
    components = {}
    for i in range(len(parent)):
        components.setdefault(find(i), []).append(i)
    if len(components) != 44:
        raise RuntimeError('Expected 44 original closed/disconnected components; no geometric cuts are permitted.')
    groups = [[] for _ in NAMES]
    component_report = []
    for ids in components.values():
        base = [b2u @ ((source.data.vertices[i].co - offset) / scale) for i in ids]
        low = Vector(tuple(min(p[k] for p in base) for k in range(3)))
        high = Vector(tuple(max(p[k] for p in base) for k in range(3)))
        center = (low + high) * .5
        group = classify(center)
        groups[group].extend(ids)
        component_report.append({'vertices': len(ids), 'source_center_y_up': list(center), 'part': NAMES[group]})
    if any(not ids for ids in groups) or sum(map(len, groups)) != len(source.data.vertices):
        raise RuntimeError('Partition omitted or duplicated source vertices.')

    OUTPUT.mkdir(parents=True, exist_ok=True)
    collection = bpy.data.collections.new('GuardianParts_11')
    bpy.context.scene.collection.children.link(collection)
    objects = []
    report = []
    # bmesh preserves all UV/color/material custom data while deleting only entire
    # disconnected components. The source mesh data is never edited.
    for index, ids in enumerate(groups):
        mesh = source.data.copy()
        mesh.name = 'GuardianPart_' + NAMES[index]
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        original_index = bm.verts.layers.int.new('guardian_source_vertex')
        for vertex in bm.verts:
            vertex[original_index] = vertex.index
        original_face = bm.faces.layers.int.new('guardian_source_face')
        for face in bm.faces:
            face[original_face] = face.index
        keep = set(ids)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
        pivot_b = u2b @ Vector(PIVOTS[index]) * scale + offset
        for vertex in bm.verts:
            vertex.co -= pivot_b
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        mesh.calc_loop_triangles()
        ob = bpy.data.objects.new(mesh.name, mesh)
        collection.objects.link(ob)
        ob.location = pivot_b
        ob['guardian_part_index'] = index
        ob['guardian_parent_index'] = PARENTS[index]
        ob['guardian_source'] = source.name
        # These are flat export nodes; hierarchy is evaluated analytically by the
        # C# helper using the common rest pivots recorded below.
        objects.append(ob)
        attr = mesh.attributes.get('guardian_source_vertex')
        if attr is None:
            raise RuntimeError('Source vertex identity attribute was lost.')
        maximum_error = 0.0
        for vertex, identity in zip(mesh.vertices, attr.data):
            error = (vertex.co + pivot_b - source.data.vertices[identity.value].co).length
            maximum_error = max(maximum_error, error)
        if maximum_error > 1e-6:
            raise RuntimeError('Partition changed source geometry.')
        face_ids = mesh.attributes['guardian_source_face']
        uv_error = color_error = 0.0
        for polygon in mesh.polygons:
            source_polygon = source.data.polygons[face_ids.data[polygon.index].value]
            if polygon.material_index != source_polygon.material_index:
                raise RuntimeError('Source material assignment changed.')
            source_loops = {source.data.loops[li].vertex_index: li for li in source_polygon.loop_indices}
            for li in polygon.loop_indices:
                vi = mesh.loops[li].vertex_index
                original_vi = attr.data[vi].value
                original_li = source_loops[original_vi]
                for layer in source.data.uv_layers:
                    a = mesh.uv_layers[layer.name].data[li].uv
                    b = layer.data[original_li].uv
                    uv_error = max(uv_error, (a - b).length)
                for layer in source.data.color_attributes:
                    new_index, old_index = (vi, original_vi) if layer.domain == 'POINT' else (li, original_li)
                    a = mesh.color_attributes[layer.name].data[new_index].color
                    b = layer.data[old_index].color
                    color_error = max(color_error, max(abs(x - y) for x, y in zip(a, b)))
        if uv_error > 1e-6 or color_error > 1e-6:
            raise RuntimeError('UV/vertex-color partition preservation failed.')
        report.append({'index': index, 'name': NAMES[index], 'object': ob.name,
                       'parent_index': PARENTS[index], 'pivot_unity': list(b2u @ pivot_b),
                       'pivot_blender': list(pivot_b), 'vertices': len(mesh.vertices),
                       'triangles': len(mesh.loop_triangles), 'material_slots': len(mesh.materials),
                       'uv_layers': [x.name for x in mesh.uv_layers],
                       'color_attributes': [{'name': x.name, 'domain': x.domain, 'data_type': x.data_type} for x in mesh.color_attributes],
                       'max_rest_vertex_error_m': maximum_error,
                       'max_uv_error': uv_error, 'max_color_channel_error': color_error})
    if sum(p['triangles'] for p in report) != source_triangles:
        raise RuntimeError('Triangle preservation failed.')
    # Preserve and restore selection/visibility so the currently open source lab is
    # not saved accidentally. Only the distinct GuardianParts working file is saved.
    selected = list(bpy.context.selected_objects)
    active = bpy.context.view_layer.objects.active
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    fbx = OUTPUT / 'StoneGuardian_Jangseung_Parts_B2.fbx'
    visibility = [(ob, ob.hide_render, ob.hide_get()) for ob in bpy.context.scene.objects if ob.type == 'MESH' and ob not in objects]
    try:
        bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH'},
                                use_mesh_modifiers=False, mesh_smooth_type='FACE', colors_type='LINEAR',
                                axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                                apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=False,
                                bake_anim=False, add_leaf_bones=False, path_mode='AUTO')
        for ob, _, _ in visibility:
            ob.hide_render = True
            ob.hide_set(True)
        bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT / 'GuardianParts_Working.blend'), copy=True)
    finally:
        for ob, rendered, hidden in visibility:
            ob.hide_render = rendered
            ob.hide_set(hidden)
        for ob in objects:
            ob.select_set(False)
        for ob in selected:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = active
    manifest = {'status': 'PARTITION_NUMERIC_CHECK_ONLY', 'source_object': source.name,
                'source_blend': bpy.data.filepath, 'source_triangles': source_triangles,
                'exported_triangles': sum(p['triangles'] for p in report),
                'source_components': len(components), 'parts': report, 'components': component_report,
                'full_body_normalized_height': 1.0, 'forward': 'Unity +Z', 'up': 'Unity +Y',
                'right_fist_contact_unity': list(b2u @ (u2b @ Vector((.595, .32, .344)) * scale + offset)),
                'source_preserved': True, 'new_vertices_added': 0, 'triangles_removed': 0,
                'fbx': str(fbx), 'fbx_sha256': hashlib.sha256(fbx.read_bytes()).hexdigest(),
                'unity_import': 'Import every named MeshFilter. Keep per-part pivot-local vertices; do not center/normalize each part. Use pivot_unity in shared body coordinates.',
                'unverified': ['FBX empty-scene roundtrip', 'Unity import', 'joint intersection during motion',
                               'foot support during walking', 'visual quality', 'UV/color numeric roundtrip']}
    (OUTPUT / 'guardian_parts_manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'status': manifest['status'], 'parts': len(report),
                      'triangles': manifest['exported_triangles'], 'output': str(OUTPUT)}, ensure_ascii=False))
    return manifest
