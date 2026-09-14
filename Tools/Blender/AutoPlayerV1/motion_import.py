"""Import animations only after same-task, skeleton, transform and rest-geometry checks.

Definitions only; no import/API/file mutation on load. Call import_same_rig_motion().
No mesh replacement, skeleton retargeting, rest edits, scale fixes or paid requests.
"""
import hashlib
import json
import math
import re
import struct
from pathlib import Path

import bpy
import numpy as np


def read_glb_timing(filepath):
    """Read actual glTF animation key times. glTF has seconds, NOT an intrinsic FPS field."""
    data = Path(filepath).read_bytes()
    magic, version, length = struct.unpack_from('<4sII', data)
    if magic != b'glTF' or version != 2 or length != len(data):
        raise RuntimeError('Not a complete GLB 2.0 file.')
    offset, doc, binary = 12, None, None
    while offset + 8 <= len(data):
        size, kind = struct.unpack_from('<II', data, offset)
        chunk = data[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode('utf-8').rstrip('\x00 '))
        elif kind == 0x004E4942:
            binary = chunk
        offset += 8 + size
    if doc is None:
        raise RuntimeError('GLB JSON chunk missing.')
    rows = []
    for animation in doc.get('animations', []):
        arrays = []
        for index in sorted({s['input'] for s in animation.get('samplers', [])}):
            accessor = doc['accessors'][index]
            if accessor.get('sparse') or accessor.get('componentType') != 5126 or accessor.get('type') != 'SCALAR':
                raise RuntimeError('Unsupported sparse/non-float animation time accessor.')
            view = doc['bufferViews'][accessor['bufferView']]
            if view.get('buffer', 0) != 0 or binary is None or doc['buffers'][0].get('uri'):
                raise RuntimeError('This timing reader requires embedded GLB time buffers.')
            start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
            stride = view.get('byteStride', 4)
            values = np.ndarray((accessor['count'],), dtype='<f4', buffer=binary,
                                offset=start, strides=(stride,)).astype(np.float64)
            if not np.isfinite(values).all() or np.any(np.diff(values) < 0):
                raise RuntimeError('Invalid animation time samples.')
            arrays.append(values)
        all_times = np.unique(np.concatenate(arrays)) if arrays else np.array([])
        deltas = np.diff(all_times)
        deltas = deltas[deltas > 1e-7]
        median = float(np.median(deltas)) if len(deltas) else None
        inferred = 1 / median if median else None
        rows.append({'name': animation.get('name'), 'time_start_seconds': float(all_times[0]) if len(all_times) else None,
                     'time_end_seconds': float(all_times[-1]) if len(all_times) else None,
                     'duration_seconds': float(all_times[-1] - all_times[0]) if len(all_times) else None,
                     'unique_key_times': len(all_times), 'median_key_interval_seconds': median,
                     'inferred_sample_rate_hz': inferred,
                     'sampling_intervals_uniform': bool(np.max(np.abs(deltas - median)) <= 1e-5) if median else None,
                     'fps_is_inferred_not_container_metadata': True})
    return {'file': str(filepath), 'sha256': hashlib.sha256(data).hexdigest(), 'animations': rows}


def mi_leaf(name):
    return name.rsplit('|', 1)[-1].rsplit(':', 1)[-1]


def mi_bone_mapping(reference, imported):
    a, b = set(reference.data.bones.keys()), set(imported.data.bones.keys())
    if a == b:
        return {n: n for n in b}, 'exact_names'
    def unique(names):
        result = {}
        for name in names:
            leaf = mi_leaf(name)
            if leaf in result:
                raise RuntimeError('Ambiguous namespace leaf: ' + leaf)
            result[leaf] = name
        return result
    aa, bb = unique(a), unique(b)
    if set(aa) != set(bb):
        raise RuntimeError('Skeleton bone sets differ beyond namespace prefixes.')
    return {bb[key]: aa[key] for key in aa}, 'unique_namespace_leaf_names_then_numeric_rest_verification'


def mi_compare_rigs(reference, imported, tolerance=1e-5):
    mapping, method = mi_bone_mapping(reference, imported)
    rows = []
    for source, destination in mapping.items():
        a, b = reference.data.bones[destination], imported.data.bones[source]
        pa = a.parent.name if a.parent else None
        pb = mapping[b.parent.name] if b.parent else None
        local_error = float(np.max(np.abs(np.asarray(a.matrix_local) - np.asarray(b.matrix_local))))
        world_error = float(np.max(np.abs(np.asarray(reference.matrix_world @ a.matrix_local)
                                          - np.asarray(imported.matrix_world @ b.matrix_local))))
        rows.append({'source_bone': source, 'reference_bone': destination,
                     'parent_matches': pa == pb, 'local_rest_matrix_max_error': local_error,
                     'world_rest_matrix_max_error': world_error})
    world_error = float(np.max(np.abs(np.asarray(reference.matrix_world) - np.asarray(imported.matrix_world))))
    scale_error = float(np.max(np.abs(np.asarray(reference.matrix_world.to_scale())
                                    - np.asarray(imported.matrix_world.to_scale()))))
    okay = world_error <= tolerance and scale_error <= tolerance and all(
        r['parent_matches'] and r['local_rest_matrix_max_error'] <= tolerance
        and r['world_rest_matrix_max_error'] <= tolerance for r in rows)
    return mapping, {'status': 'PASS' if okay else 'FAIL', 'mapping_method': method,
                     'world_matrix_max_error': world_error, 'world_scale_max_error': scale_error,
                     'tolerance': tolerance, 'bones': rows}


def mi_geometry(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    values = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
    matrix = np.asarray(obj.matrix_world, dtype=np.float64)
    world = values @ matrix[:3, :3].T + matrix[:3, 3]
    triangles = np.array([tuple(t.vertices) for t in mesh.loop_triangles], dtype=np.int32)
    # Triangle winding/order can differ; index connectivity must still agree.
    triangles = np.sort(triangles, axis=1)
    if len(triangles):
        triangles = triangles[np.lexsort(triangles.T[::-1])]
    return world, triangles


def mi_compare_geometry(reference_meshes, imported_meshes, tolerance=1e-5):
    rows, used = [], set()
    for source in imported_meshes:
        points, triangles = mi_geometry(source)
        matches = []
        for target in reference_meshes:
            if target in used:
                continue
            target_points, target_triangles = mi_geometry(target)
            if points.shape != target_points.shape or triangles.shape != target_triangles.shape:
                continue
            error = float(np.max(np.linalg.norm(points - target_points, axis=1))) if len(points) else 0
            connectivity = bool(np.array_equal(triangles, target_triangles))
            if error <= tolerance and connectivity:
                matches.append((target, error))
        if len(matches) != 1:
            rows.append({'imported_mesh': source.name, 'status': 'FAIL',
                         'vertices': len(points), 'triangles': len(triangles),
                         'reason': 'No unique identical indexed rest geometry/connectivity match',
                         'matches': [t.name for t, error in matches]})
        else:
            target, error = matches[0]
            used.add(target)
            rows.append({'imported_mesh': source.name, 'reference_mesh': target.name, 'status': 'PASS',
                         'vertices': len(points), 'triangles': len(triangles), 'max_world_vertex_error_m': error})
    return {'status': 'PASS' if rows and len(used) == len(reference_meshes) and all(
        r['status'] == 'PASS' for r in rows) else 'FAIL', 'rows': rows,
        'method': 'Strict same vertex order/world positions and triangle index connectivity. '
                  'No approximate nearest-surface match or silent geometry replacement.'}


def mi_curves(action):
    return [fc for layer in getattr(action, 'layers', ()) for strip in layer.strips
            for bag in getattr(strip, 'channelbags', ()) for fc in bag.fcurves]


def mi_character_meshes(rig, candidates):
    """Exclude only objects actually referenced as this rig's bone display shapes."""
    shape_users = {}
    for bone in rig.pose.bones:
        if bone.custom_shape is not None:
            shape_users.setdefault(bone.custom_shape, []).append(bone.name)
    meshes = [obj for obj in candidates if obj.type == 'MESH' and obj not in shape_users]
    excluded = [{'object': obj.name, 'reason': 'pose_bone_custom_shape',
                 'referencing_bones': shape_users[obj]}
                for obj in candidates if obj.type == 'MESH' and obj in shape_users]
    return meshes, excluded


def import_same_rig_motion(filepath, reference_rig_name, motion_name,
                           reference_rig_task_id, motion_rig_task_id, report_path,
                           reference_mesh_names=None, tolerance=1e-5):
    """Task IDs are caller-provided provenance, checked for equality, not queried remotely.

    On any data mismatch the motion is rejected and imported duplicate objects removed.
    Only success creates/connects a copied Action. Existing same-named Actions are protected.
    """
    if not reference_rig_task_id or reference_rig_task_id != motion_rig_task_id:
        raise RuntimeError('Rig task provenance missing or mismatched.')
    if bpy.data.actions.get(motion_name):
        raise RuntimeError('Action already exists; refusing silent replacement: ' + motion_name)
    reference = bpy.data.objects[reference_rig_name]
    if reference.get('meshy_rig_task_id') and reference['meshy_rig_task_id'] != reference_rig_task_id:
        raise RuntimeError('Reference rig stored provenance differs from caller task ID.')
    refs = [bpy.data.objects[n] for n in reference_mesh_names] if reference_mesh_names else [
        o for o in bpy.context.scene.objects if o.type == 'MESH' and any(
            m.type == 'ARMATURE' and m.object == reference for m in o.modifiers)]
    refs, reference_display_shapes = mi_character_meshes(reference, refs)
    report = {'status': 'INCOMPLETE', 'reference_rig': reference.name, 'motion_name': motion_name,
              'rig_task_id': reference_rig_task_id, 'provenance_method': 'Caller-declared matching rig task IDs',
              'file': str(filepath), 'reference_meshes': [o.name for o in refs],
              'excluded_reference_display_shapes': reference_display_shapes,
              'geometry_replaced': False, 'retargeted': False}
    before = {key: set(getattr(bpy.data, key)) for key in ('objects', 'meshes', 'armatures', 'actions', 'materials', 'images')}
    old_action = reference.animation_data.action if reference.animation_data else None
    old_slot = getattr(reference.animation_data, 'action_slot', None) if reference.animation_data else None
    saved_frame = bpy.context.scene.frame_current
    kept = None
    try:
        report['glb_timing'] = read_glb_timing(filepath)
        bpy.ops.import_scene.gltf(filepath=str(filepath), merge_vertices=False)
        added = set(bpy.data.objects) - before['objects']
        rigs = [o for o in added if o.type == 'ARMATURE']
        meshes = [o for o in added if o.type == 'MESH']
        if len(rigs) != 1:
            raise RuntimeError('Expected exactly one imported rig, got ' + str(len(rigs)))
        source = rigs[0]
        meshes, report['excluded_imported_display_shapes'] = mi_character_meshes(source, meshes)
        mapping, report['skeleton_comparison'] = mi_compare_rigs(reference, source, tolerance)
        report['geometry_comparison'] = mi_compare_geometry(refs, meshes, tolerance)
        if report['skeleton_comparison']['status'] != 'PASS' or report['geometry_comparison']['status'] != 'PASS':
            raise RuntimeError('Rejected: rest geometry, hierarchy, rest matrices or world scale differ.')
        source_action = source.animation_data.action if source.animation_data else None
        if source_action is None:
            raise RuntimeError('Imported rig has no active action; multi-action selection must be explicit.')
        animations = report['glb_timing']['animations']
        if len(animations) != 1:
            raise RuntimeError('Expected exactly one GLB animation; refusing an ambiguous clip selection.')
        fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
        duration = (source_action.frame_range[1] - source_action.frame_range[0]) / fps
        expected = animations[0]['duration_seconds']
        report['import_timing'] = {'scene_fps': fps, 'action_range': list(source_action.frame_range),
                                   'duration_seconds': duration, 'GLB_duration_seconds': expected}
        if expected is None or abs(duration - expected) > .0001:
            raise RuntimeError('Imported playback duration differs from source GLB key-time duration.')
        kept = source_action.copy()
        kept.name = motion_name
        curves = mi_curves(kept)
        if not curves:
            raise RuntimeError('Imported action has no accessible populated channels.')
        for curve in curves:
            def remap(match):
                old = json.loads('"' + match.group(1) + '"')
                if old not in mapping:
                    raise RuntimeError('Motion references an unverified bone: ' + old)
                return 'pose.bones["' + bpy.utils.escape_identifier(mapping[old]) + '"]'
            curve.data_path = re.sub(r'pose\.bones\["((?:\\.|[^"\\])*)"\]', remap, curve.data_path)
            reference.path_resolve(curve.data_path)
        reference.animation_data_create()
        reference.animation_data.action = kept
        populated = [bag.slot for layer in kept.layers for strip in layer.strips
                     for bag in strip.channelbags if len(bag.fcurves)]
        if len({slot.handle for slot in populated}) != 1:
            raise RuntimeError('Copied action contains multiple populated slots; explicit selection required.')
        reference.animation_data.action_slot = populated[0]
        kept.use_fake_user = True
        kept['meshy_rig_task_id'] = reference_rig_task_id
        kept['source_file'] = str(filepath)
        kept['source_sha256'] = report['glb_timing']['sha256']
        kept['motion_import_method'] = report['skeleton_comparison']['mapping_method']
        report['bone_mapping'] = mapping
        report['status'] = 'SAME_RIG_ACTION_CONNECTED_REQUIRES_PLAYBACK_AUDIT'
    except Exception as exc:
        report['status'], report['error'] = 'REJECTED', repr(exc)
        reference.animation_data_create()
        reference.animation_data.action = old_action
        if old_action is not None and old_slot is not None:
            reference.animation_data.action_slot = old_slot
        if kept is not None:
            bpy.data.actions.remove(kept)
            kept = None
    finally:
        for obj in list(set(bpy.data.objects) - before['objects']):
            bpy.data.objects.remove(obj, do_unlink=True)
        for key in ('meshes', 'armatures', 'actions', 'materials', 'images'):
            collection = getattr(bpy.data, key)
            for block in list(set(collection) - before[key]):
                if block != kept and block.users == 0:
                    collection.remove(block)
        bpy.context.scene.frame_set(saved_frame)
        bpy.context.view_layer.update()
        destination = Path(report_path)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'report': str(report_path)}, ensure_ascii=False))
    return report
