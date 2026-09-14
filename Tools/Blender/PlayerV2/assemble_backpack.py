"""Assemble the reviewed pack into a separate static character branch; never edits either source."""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector, Quaternion
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
BODY = ART / 'DosaV2_Refined.blend'
PACK = ART / 'DosaBackpackV2.blend'
OUT = ART / 'Inspect/PackIntegrated'
PACK_TRANSLATION = (0, .118, 1.165)
PACK_TILT_DEGREES = 10


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def mesh_record(obj):
    obj.data.calc_loop_triangles()
    points = np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices])
    return {'name': obj.name, 'vertices': len(points), 'triangles': len(obj.data.loop_triangles),
            'hide_render': obj.hide_render, 'bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()],
            'materials': [m.name for m in obj.data.materials]}


def inspect():
    bpy.ops.wm.open_mainfile(filepath=str(BODY))
    rig = bpy.data.objects['DosaV2_Rig']
    records = [mesh_record(obj) for obj in bpy.context.scene.objects if obj.type == 'MESH']
    back_samples = {}
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or obj.name == 'DosaV2_SourceSurface': continue
        pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
        area = [p for p in pts if abs(p.x) < .17 and 1.0 < p.z < 1.48 and p.y > 0]
        if area:
            back_samples[obj.name] = {'max_y': max(p.y for p in area),
                                      'percentile_95_y': float(np.quantile([p.y for p in area], .95))}
    report = {'source': str(BODY), 'source_sha256': digest(BODY), 'meshes': records,
              'rig_name': rig.name, 'rig_matrix': [list(row) for row in rig.matrix_world],
              'bones': [{'name': b.name, 'head': list(b.head_local), 'tail': list(b.tail_local),
                         'parent': b.parent.name if b.parent else None} for b in rig.data.bones],
              'back_samples': back_samples}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'body-attachment-inspection.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'inspection': str(OUT / 'body-attachment-inspection.json'), 'back_samples': back_samples}))


def evaluated_points(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    result = np.array([tuple(evaluated.matrix_world @ v.co) for v in mesh.vertices])
    evaluated.to_mesh_clear()
    return result


def body_signature(obj):
    data = bytearray()
    for vertex in obj.data.vertices:
        data.extend(np.asarray(vertex.co, dtype=np.float32).tobytes())
        for group in vertex.groups:
            data.extend(f'{group.group}:{group.weight:.10g};'.encode())
    for poly in obj.data.polygons:
        data.extend(np.asarray(poly.vertices, dtype=np.int32).tobytes())
    for uv in obj.data.uv_layers:
        for corner in uv.data: data.extend(np.asarray(corner.uv, dtype=np.float32).tobytes())
    return hashlib.sha256(data).hexdigest()


def assemble():
    OUT.mkdir(parents=True, exist_ok=True)
    source_hashes = {str(p): digest(p) for p in [BODY, PACK]}
    bpy.ops.wm.open_mainfile(filepath=str(BODY))
    rig = bpy.data.objects['DosaV2_Rig']
    if rig.animation_data or bpy.data.actions: raise RuntimeError('Static assembly must not load animation data.')
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    body_meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    original_mesh_signatures = {o.name: body_signature(o) for o in body_meshes}
    original_bone_matrices = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    with bpy.data.libraries.load(str(PACK), link=False) as (source, target):
        target.objects = [name for name in source.objects if name == 'DosaPackV2_Rig' or name.startswith('DosaPackV2_')]
    for obj in target.objects:
        if obj is not None: bpy.context.scene.collection.objects.link(obj)
    pack_rig = bpy.data.objects['DosaPackV2_Rig']
    pack_meshes = [obj for obj in target.objects if obj is not None and obj.type == 'MESH']
    if len(pack_meshes) != 16 or len(pack_rig.data.bones) != 22: raise RuntimeError('Reviewed pack topology changed.')
    # Fit uses the actual rear panel, not its minimum-Y projecting straps.
    # The small lean clears the original waist/sash and seats the top at the shoulders.
    placement = Matrix.Translation(PACK_TRANSLATION) @ Matrix.Rotation(math.radians(PACK_TILT_DEGREES), 4, 'X')
    pack_rig.matrix_world = placement
    bpy.context.view_layer.update()
    before_points = {obj.name: evaluated_points(obj) for obj in pack_meshes}
    def surface_tree(obj):
        return BVHTree.FromPolygons([obj.matrix_world @ v.co for v in obj.data.vertices],
                                    [list(face.vertices) for face in obj.data.polygons])
    body_tree = surface_tree(bpy.data.objects['DosaV2_BodyCore'])
    panel_tree = surface_tree(bpy.data.objects['DosaPackV2_Backpanel'])
    fit_samples = []
    for z in [1.03, 1.10, 1.17, 1.24, 1.31, 1.38]:
        for x in [-.08, 0, .08]:
            body_hit = body_tree.ray_cast(Vector((x, .6, z)), Vector((0, -1, 0)), 1)[0]
            panel_hit = panel_tree.ray_cast(Vector((x, -.5, z)), Vector((0, 1, 0)), 1.2)[0]
            if body_hit is not None and panel_hit is not None:
                fit_samples.append({'x': x, 'z': z, 'body_y': body_hit.y, 'panel_y': panel_hit.y,
                                    'gap_m': panel_hit.y - body_hit.y})
    before_world = {obj.name: obj.matrix_world.copy() for obj in pack_meshes}
    before_parent_bones = {obj.name: obj.parent_bone for obj in pack_meshes}
    transform = rig.matrix_world.inverted() @ pack_rig.matrix_world
    bone_records = []
    for bone in pack_rig.data.bones:
        if bone.name in rig.data.bones: raise RuntimeError('Existing body bone collides with pack: ' + bone.name)
        bone_records.append({'name': bone.name, 'head': transform @ bone.head_local,
                             'tail': transform @ bone.tail_local, 'z_axis': transform.to_3x3() @ bone.z_axis,
                             'parent': bone.parent.name if bone.parent else 'Spine02',
                             'connected': bone.use_connect, 'deform': bone.use_deform})
    bpy.ops.object.select_all(action='DESELECT'); rig.hide_set(False); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
    for record in bone_records:
        bone = rig.data.edit_bones.new(record['name']); bone.head = record['head']; bone.tail = record['tail']
        bone.align_roll(record['z_axis']); bone.use_deform = record['deform']
    for record in bone_records:
        bone = rig.data.edit_bones[record['name']]; bone.parent = rig.data.edit_bones[record['parent']]
        bone.use_connect = record['connected']
    bpy.ops.object.mode_set(mode='OBJECT'); bpy.context.view_layer.update()
    material = pack_meshes[0].data.materials[0].copy(); material.name = 'DosaV2_Backpack'
    for obj in pack_meshes:
        matrix = before_world[obj.name]
        obj.parent = rig
        obj.parent_bone = before_parent_bones[obj.name]
        for modifier in obj.modifiers:
            if modifier.type == 'ARMATURE': modifier.object = rig
        bpy.context.view_layer.update(); obj.matrix_world = matrix
        obj.data.materials.clear(); obj.data.materials.append(material)
        obj['dosa_part_category'] = 'backpack'
        obj['dosa_optional_part'] = True
    bpy.context.view_layer.update()
    merge_drift = max(float(np.linalg.norm(evaluated_points(obj) - before_points[obj.name], axis=1).max()) for obj in pack_meshes)
    if merge_drift > .000002: raise RuntimeError(f'Pack shifted while merging rigs: {merge_drift}')
    bpy.data.objects.remove(pack_rig, do_unlink=True)
    for obj in body_meshes:
        if body_signature(obj) != original_mesh_signatures[obj.name]: raise RuntimeError('Body geometry/weights/UV modified: ' + obj.name)
    for name, matrix in original_bone_matrices.items():
        if max(abs(value) for row in rig.data.bones[name].matrix_local - matrix for value in row) > .0000001:
            raise RuntimeError('Original rest bone changed: ' + name)
    render_meshes = [o for o in body_meshes if not o.hide_render] + pack_meshes
    total_triangles = sum(mesh_record(obj)['triangles'] for obj in render_meshes)
    if total_triangles > 75000: raise RuntimeError(f'Combined LOD0 exceeds 75k: {total_triangles}')
    for obj in list(bpy.context.scene.objects):
        if obj.type in {'CAMERA', 'LIGHT'}: bpy.data.objects.remove(obj, do_unlink=True)
    spec = importlib.util.spec_from_file_location('brush_qa_helpers', Path(__file__).with_name('build_brush.py'))
    helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(helpers)
    scene = bpy.context.scene; camera = helpers.lighting(scene)
    for light in [o for o in scene.objects if o.type == 'LIGHT']:
        light.location.z += 1.1; helpers.aim(light, (0, .1, 1.1))
    qa = OUT / 'QA'; qa.mkdir(exist_ok=True)
    for view, azimuth in [('Back', math.pi), ('Side', math.pi / 2), ('BackQuarter', math.pi * .72)]:
        helpers.render(scene, camera, qa / f'Rest_{view}.png', (0, .07, .91), 2.05, azimuth, width=1000, height=1350)
    # Direct static lowering verifies shoulder/pack clearance without producing actions.
    for side, sign in [('Left', 1), ('Right', -1)]:
        bone = rig.pose.bones[side + 'Arm']; matrix = bone.matrix.copy(); current = matrix.to_quaternion()
        delta = (current @ Vector((0, 1, 0))).rotation_difference(Vector((sign * .08, 0, -1)).normalized())
        desired = (delta @ current).to_matrix().to_4x4(); desired.translation = matrix.translation
        bone.matrix = desired; bpy.context.view_layer.update()
    for view, azimuth in [('Back', math.pi), ('Side', math.pi / 2), ('BackQuarter', math.pi * .72)]:
        helpers.render(scene, camera, qa / f'Lowered_{view}.png', (0, .07, 1.2), 1.10, azimuth, width=1100, height=1100)
    lowered_matrices = {side + 'Arm': [list(row) for row in rig.pose.bones[side + 'Arm'].matrix] for side in ['Left', 'Right']}
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    # Parent-spine follow is checked on evaluated geometry after merging, not just bone names.
    pivot = rig.pose.bones['Spine02']; original_rotation_mode = pivot.rotation_mode; pivot.rotation_mode = 'QUATERNION'
    pivot.rotation_quaternion = Quaternion((0, 0, 1), math.radians(10)); bpy.context.view_layer.update()
    inherited_motion = {obj.name: float(np.linalg.norm(evaluated_points(obj) - before_points[obj.name], axis=1).max()) for obj in pack_meshes}
    if min(inherited_motion.values()) < .001: raise RuntimeError('A pack part does not follow the chest.')
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    pivot.rotation_mode = original_rotation_mode
    bpy.context.view_layer.update(); bpy.ops.file.pack_all()
    destination = ART / 'DosaV2_PackIntegrated.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(destination))
    points = np.concatenate([evaluated_points(obj) for obj in pack_meshes])
    unchanged = all(digest(path) == value for path, value in source_hashes.items())
    if not unchanged: raise RuntimeError('An input source changed during assembly.')
    report = {'status': 'STATIC_ASSEMBLY_CANDIDATE_NOT_CHARACTER_RIG_PASS', 'source_sha256': source_hashes,
              'sources_unchanged': unchanged, 'output': str(destination), 'output_sha256': digest(destination),
              'recipe_sha256': digest(Path(__file__)), 'character_rig': rig.name, 'pack_parent_bone': 'Spine02',
              'pack_root_bone': 'PackRoot', 'pack_bones': [record['name'] for record in bone_records],
              'pack_meshes': [o.name for o in pack_meshes], 'material': material.name,
              'material_texture_directory': 'Art/PlayerV2/Staging/Backpack/Textures',
              'placement_translation_m': list(PACK_TRANSLATION), 'placement_rotation_x_degrees': PACK_TILT_DEGREES,
              'body_to_rear_panel_ray_samples': fit_samples,
              'pack_world_bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()],
              'world_triangles': total_triangles, 'pack_triangles': 8864,
              'merged_rig_bones': len(rig.data.bones), 'pre_post_merge_vertex_drift_m': merge_drift,
              'original_mesh_geometry_weights_uv_unchanged': True, 'original_bone_rest_unchanged': True,
              'spine_follow_vertex_motion_m': inherited_motion, 'static_lowered_arm_matrices': lowered_matrices,
              'actions': len(bpy.data.actions),
              'limitations': 'No production animation, no Cloth/secondary simulation or Unity collision verification. Refined body is a branch snapshot; lower-body repair must be merged separately. LOD1/2 deferred until final body merge.'}
    (OUT / 'pack-assembly-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(destination), 'triangles': total_triangles, 'pack_merge_drift_m': merge_drift, 'sources_unchanged': unchanged}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--inspect-only', action='store_true')
    args = parser.parse_args(__import__('sys').argv[__import__('sys').argv.index('--') + 1:] if '--' in __import__('sys').argv else [])
    if args.inspect_only: inspect()
    else: assemble()
