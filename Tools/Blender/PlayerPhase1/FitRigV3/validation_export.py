"""Explicit FBX export and empty-scene round-trip checks for FitRigV3.

Load definitions in Blender 5.0; neither loading nor importing this file runs work.
Call export_selected(path, action_names=[...]), then roundtrip(path, action_names=[...]).
The caller must first apply/replace Blender-only deformation with exportable skinning.
No source actions, objects, weights, mesh data, or bind transforms are deleted/changed.
Round-trip sampling validates serialization, NOT anatomical quality, collisions, or Unity.
"""
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree


VE_PARTS = ('Body', 'InnerTop', 'Durumagi')
VE_RIG = 'Dosa_Phase1_Rig'
VE_OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')
VE_VERTEX_TOLERANCE_M = 0.0005
VE_WEIGHT_TOLERANCE = 0.0001


def ve_write(path, report):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False),
                    encoding='utf-8')
    return str(path)


def ve_actions(names=None):
    names = list(names) if names is not None else sorted(
        a.name for a in bpy.data.actions if a.name.startswith('DIAG_'))
    if not names:
        raise RuntimeError('No diagnostic actions were specified or discovered.')
    if len(names) != len(set(names)):
        raise RuntimeError('Duplicate requested action names.')
    missing = [n for n in names if bpy.data.actions.get(n) is None]
    if missing:
        raise RuntimeError('Missing source actions: ' + ', '.join(missing))
    return [bpy.data.actions[n] for n in names]


def ve_slot(rig, action):
    """Select the actual Object slot whose channels resolve on this rig (Blender 5)."""
    slots = list(getattr(action, 'slots', ()))
    if not slots:
        return None
    for slot in sorted(slots, key=lambda x: rig.name not in x.identifier):
        if getattr(slot, 'target_id_type', 'OBJECT') != 'OBJECT':
            continue
        curves = []
        for layer in getattr(action, 'layers', ()):
            for strip in layer.strips:
                for bag in getattr(strip, 'channelbags', ()):
                    if bag.slot == slot:
                        curves.extend(bag.fcurves)
        if not curves:
            continue
        try:
            for curve in curves:
                rig.path_resolve(curve.data_path)
        except (ValueError, KeyError):
            continue
        return slot
    raise RuntimeError('No compatible populated action slot: ' + action.name)


def ve_assign(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None:
        slot = ve_slot(rig, action)
        if slot is not None:
            rig.animation_data.action_slot = slot


def ve_snapshot(scene, rig):
    ad = rig.animation_data
    return {
        'scene': scene, 'frame': scene.frame_current, 'subframe': scene.frame_subframe,
        'action': ad.action if ad else None,
        'slot': getattr(ad, 'action_slot', None) if ad else None,
        'pose': {p.name: p.matrix_basis.copy() for p in rig.pose.bones},
        'pose_position': rig.data.pose_position,
        'nla': [(t, t.mute, t.is_solo) for t in ad.nla_tracks] if ad else [],
        'use_nla': ad.use_nla if ad else True,
        'selected': list(bpy.context.selected_objects),
        'active': bpy.context.view_layer.objects.active,
        'object_matrix': rig.matrix_basis.copy(),
    }


def ve_restore(snapshot, rig):
    bpy.context.window.scene = snapshot['scene']
    ve_assign(rig, snapshot['action'])
    if snapshot['action'] is not None and snapshot['slot'] is not None:
        rig.animation_data.action_slot = snapshot['slot']
    rig.animation_data.use_nla = snapshot['use_nla']
    for track, mute, solo in snapshot['nla']:
        track.mute, track.is_solo = mute, solo
    rig.data.pose_position = snapshot['pose_position']
    snapshot['scene'].frame_set(snapshot['frame'], subframe=snapshot['subframe'])
    rig.matrix_basis = snapshot['object_matrix']
    for p in rig.pose.bones:
        p.matrix_basis = snapshot['pose'][p.name]
    for obj in list(bpy.context.selected_objects):
        obj.select_set(False)
    for obj in snapshot['selected']:
        if obj.name in bpy.context.view_layer.objects and not obj.hide_select:
            obj.select_set(True)
    active = snapshot['active']
    if active is not None and active.name in bpy.context.view_layer.objects:
        bpy.context.view_layer.objects.active = active
    bpy.context.view_layer.update()


def ve_neutral(rig):
    ve_assign(rig, None)
    rig.data.pose_position = 'POSE'
    rig.animation_data.use_nla = False
    for track in rig.animation_data.nla_tracks:
        track.mute = True
        track.is_solo = False
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def ve_preflight(rig, parts):
    problems = []
    for obj in parts:
        arms = [m for m in obj.modifiers if m.type == 'ARMATURE' and m.show_viewport]
        if len(arms) != 1 or arms[0].object != rig:
            problems.append(obj.name + ': needs exactly one enabled Armature targeting ' + rig.name)
        unsupported = [m.name + ':' + m.type for m in obj.modifiers
                       if m.type != 'ARMATURE' and (m.show_viewport or m.show_render)]
        if unsupported:
            problems.append(obj.name + ': apply/bake/remove export-unsupported modifiers first: '
                            + ', '.join(unsupported))
        if obj.constraints:
            problems.append(obj.name + ': object constraints require an explicit export bake')
        if len(obj.data.vertices) == 0:
            problems.append(obj.name + ': empty mesh')
        if obj.parent and obj.parent != rig:
            problems.append(obj.name + ': unselected parent transform must be resolved explicitly')
    if rig.parent:
        problems.append(rig.name + ': unselected parent is not part of this four-object export')
    if bpy.context.mode != 'OBJECT':
        problems.append('Switch to Object Mode before export; this routine will not alter edit data.')
    if bpy.context.window is None:
        problems.append('A Blender window context is required for empty-scene isolation.')
    return problems


def ve_geometry(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        mesh.calc_loop_triangles()
        values = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get('co', values)
        local = values.reshape((-1, 3))
        matrix = np.asarray(evaluated.matrix_world, dtype=np.float64)
        world = local @ matrix[:3, :3].T + matrix[:3, 3]
        finite = bool(np.isfinite(world).all())
        bounds = (world.min(axis=0), world.max(axis=0)) if len(world) and finite else (None, None)
        stats = {
            'vertices': len(mesh.vertices), 'polygons': len(mesh.polygons),
            'triangles': len(mesh.loop_triangles),
            'material_slots': len(mesh.materials),
            'uv_layers': [layer.name for layer in mesh.uv_layers],
            'finite_coordinates': finite,
            'world_bounds_min_m': bounds[0].tolist() if bounds[0] is not None else None,
            'world_bounds_max_m': bounds[1].tolist() if bounds[1] is not None else None,
            'world_dimensions_m': (bounds[1] - bounds[0]).tolist() if bounds[0] is not None else None,
            'shape_keys': [k.name for k in obj.data.shape_keys.key_blocks]
                          if obj.data.shape_keys else [],
        }
        return world, stats
    finally:
        evaluated.to_mesh_clear()


def ve_weights(obj, rig):
    bones = {b.name for b in rig.data.bones if b.use_deform}
    indices = {g.index for g in obj.vertex_groups if g.name in bones}
    unassigned = bad = negative = nonfinite = over_four = 0
    maximum_influences = 0
    maximum_sum_error = 0.0
    for vertex in obj.data.vertices:
        values = [float(g.weight) for g in vertex.groups if g.group in indices]
        nonfinite += sum(not math.isfinite(x) for x in values)
        negative += sum(x < 0 for x in values if math.isfinite(x))
        positive = [v for v in values if math.isfinite(v) and v > 1e-8]
        count = len(positive)
        maximum_influences = max(maximum_influences, count)
        over_four += count > 4
        unassigned += count == 0
        error = abs(sum(positive) - 1)
        maximum_sum_error = max(maximum_sum_error, error)
        bad += error > VE_WEIGHT_TOLERANCE
    return {
        'method': 'Only positive vertex weights naming deform bones; non-skin pin groups excluded.',
        'unassigned_vertices': unassigned, 'non_normalized_vertices': bad,
        'negative_weights': negative, 'nonfinite_weights': nonfinite,
        'vertices_over_four_weights': over_four, 'maximum_influences': maximum_influences,
        'maximum_sum_error': maximum_sum_error,
        'status': 'PASS' if not any((unassigned, bad, negative, nonfinite, over_four)) else 'FAIL',
    }


def export_selected(exportpath=None, action_names=None):
    """Export exactly the three final meshes + shared rig, with specified diagnostic takes.

    Blender-only deformers are rejected, never silently frozen into one arbitrary pose.
    NLA tracks are temporary and export only requested actions. Custom rig control shapes
    are deliberately not exported: they are authoring UI, not character render meshes.
    Skin weights, mesh UV/materials and shape-key definitions use the FBX exporter.
    """
    path = Path(exportpath) if exportpath else VE_OUT / 'Dosa_Phase1_FitRigV3.fbx'
    report_path = path.with_suffix('.export.json')
    report = {'status': 'INCOMPLETE', 'export_path': str(path),
              'scope': [*VE_PARTS, VE_RIG], 'runtime_physics': 'UNVERIFIED',
              'custom_bone_shapes': 'Authoring-only; excluded from the four-object FBX.'}
    snapshot = None
    hidden = []
    temp_tracks = []
    rig = bpy.data.objects.get(VE_RIG)
    try:
        if rig is None:
            raise RuntimeError('Missing ' + VE_RIG)
        parts = [bpy.data.objects[n] for n in VE_PARTS]
        actions = ve_actions(action_names)
        problems = ve_preflight(rig, parts)
        if problems:
            raise RuntimeError('; '.join(problems))
        snapshot = ve_snapshot(bpy.context.scene, rig)
        ve_neutral(rig)
        snapshot['scene'].frame_set(1)
        bpy.context.view_layer.update()
        report['source_meshes'] = {obj.name: ve_geometry(obj)[1] for obj in parts}
        report['source_weights'] = {obj.name: ve_weights(obj, rig) for obj in parts}
        total = sum(r['triangles'] for r in report['source_meshes'].values())
        report['actual_source_evaluated_triangles'] = total
        report['triangle_budget'] = {'target': 50000, 'hard_maximum': 60000,
                                     'status': 'PASS' if total <= 60000 else 'FAIL'}
        if total > 60000:
            raise RuntimeError('Actual source triangle count exceeds 60,000.')
        if any(v['status'] != 'PASS' for v in report['source_weights'].values()):
            raise RuntimeError('Source deform weights failed the declared normalization/4-weight gate.')
        for obj in list(bpy.context.selected_objects):
            obj.select_set(False)
        for obj in [*parts, rig]:
            hidden.append((obj, obj.hide_get(), obj.hide_viewport, obj.hide_select))
            obj.hide_set(False)
            obj.hide_viewport = False
            obj.hide_select = False
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        rig.animation_data.use_nla = True
        report['actions'] = []
        for action in actions:
            start, end = map(float, action.frame_range)
            track = rig.animation_data.nla_tracks.new()
            temp_tracks.append(track)
            track.name = '__FITRIGV3_EXPORT_' + action.name
            strip = track.strips.new(action.name, int(math.floor(start)), action)
            slot = ve_slot(rig, action)
            if slot is not None and hasattr(strip, 'action_slot'):
                strip.action_slot = slot
            strip.name = action.name
            strip.action_frame_start, strip.action_frame_end = start, end
            strip.frame_start, strip.frame_end = start, max(start + 1e-4, end)
            strip.extrapolation = 'NOTHING'
            strip.blend_type = 'REPLACE'
            strip.influence = 1.0
            track.mute, track.is_solo = False, False
            report['actions'].append({'name': action.name, 'source_range': [start, end],
                                      'export_take_name': strip.name})
        path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.export_scene.fbx(
            filepath=str(path), use_selection=True, object_types={'MESH', 'ARMATURE'},
            use_mesh_modifiers=False, use_triangles=True, mesh_smooth_type='OFF',
            use_tspace=True, add_leaf_bones=False, use_armature_deform_only=True,
            bake_anim=True, bake_anim_use_all_bones=True,
            bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
            bake_anim_step=0.25, bake_anim_simplify_factor=0.0,
            bake_anim_force_startend_keying=True, path_mode='COPY', embed_textures=True,
            axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False)
        if not path.exists() or path.stat().st_size == 0:
            raise RuntimeError('Exporter returned without a non-empty FBX file.')
        report['file_bytes'] = path.stat().st_size
        report['status'] = 'EXPORTED_AWAITING_ROUNDTRIP'
        report['source_scene_unit_scale'] = snapshot['scene'].unit_settings.scale_length
        report['fps'] = snapshot['scene'].render.fps / snapshot['scene'].render.fps_base
    except Exception as exc:
        report['error'] = repr(exc)
    finally:
        if rig is not None and rig.animation_data:
            for track in temp_tracks:
                rig.animation_data.nla_tracks.remove(track)
        for obj, hide, viewport, select in hidden:
            obj.hide_set(hide)
            obj.hide_viewport, obj.hide_select = viewport, select
        if snapshot is not None:
            ve_restore(snapshot, rig)
        ve_write(report_path, report)
    print(json.dumps({'status': report['status'], 'report': str(report_path)}, ensure_ascii=False))
    return report


def ve_rest_bones(rig):
    result = {}
    for bone in rig.data.bones:
        world = rig.matrix_world @ bone.matrix_local
        result[bone.name] = {
            'parent': bone.parent.name if bone.parent else None,
            'use_deform': bone.use_deform,
            'head_world_m': list(rig.matrix_world @ bone.head_local),
            'rest_matrix_world': [list(row) for row in world],
        }
    return result


def ve_bone_comparison(source, target):
    expected = {n for n, b in source.items() if b['use_deform']}
    missing = sorted(expected - set(target))
    common = sorted(expected & set(target))
    rows = []
    for name in common:
        a, b = source[name], target[name]
        head_error = float(np.linalg.norm(np.asarray(a['head_world_m']) - b['head_world_m']))
        ma, mb = Matrix(a['rest_matrix_world']), Matrix(b['rest_matrix_world'])
        angle = ma.to_quaternion().rotation_difference(mb.to_quaternion()).angle
        parent_a = a['parent']
        while parent_a and parent_a not in expected:
            parent_a = source[parent_a]['parent']
        parent_b = b['parent']
        while parent_b and parent_b not in expected and parent_b in target:
            parent_b = target[parent_b]['parent']
        rows.append({'name': name, 'head_error_m': head_error,
                     'local_axis_rotation_difference_deg': math.degrees(angle),
                     'deform_parent_matches': parent_a == parent_b})
    maximum = max((r['head_error_m'] for r in rows), default=0)
    return {
        'source_deform_bones': len(expected), 'imported_bones': len(target),
        'missing_deform_bones': missing, 'maximum_rest_head_error_m': maximum,
        'rows': rows,
        'method': 'World-space rest heads and deform-bone hierarchy. Bone basis-axis changes '
                  'are reported, not presumed erroneous; evaluated skinning is checked separately.',
        'status': 'PASS' if common and not missing and maximum <= VE_VERTEX_TOLERANCE_M
                  and all(r['deform_parent_matches'] for r in rows) else 'FAIL',
    }


def ve_nearest_indices(query, points):
    if not len(query) or not len(points):
        raise RuntimeError('Empty point cloud cannot be compared.')
    tree = KDTree(len(points))
    for index, p in enumerate(points):
        tree.insert(Vector(p), index)
    tree.balance()
    matches = [tree.find(Vector(p)) for p in query]
    return np.asarray([m[1] for m in matches], dtype=np.int64), np.asarray([m[2] for m in matches])


def ve_correspondence(source, target):
    if source.shape == target.shape:
        error = np.linalg.norm(source - target, axis=1)
        if float(error.max()) <= 0.00001:
            return {'method': 'same_vertex_indices_verified_at_rest', 'source_to_target': None,
                    'target_to_source': None, 'rest_max_error_m': float(error.max())}
    source_to_target, a = ve_nearest_indices(source, target)
    target_to_source, b = ve_nearest_indices(target, source)
    maximum = float(max(a.max(), b.max()))
    return {
        'method': 'bidirectional_nearest_rest_position_mapping' if maximum <= 0.0001
                  else 'bidirectional_nearest_evaluated_positions_each_sample',
        'source_to_target': source_to_target, 'target_to_source': target_to_source,
        'rest_max_error_m': maximum,
    }


def ve_compare_points(source, target, mapping):
    if not np.isfinite(source).all() or not np.isfinite(target).all():
        return {'status': 'FAIL', 'reason': 'Nonfinite evaluated coordinates'}
    method = mapping['method']
    if method == 'same_vertex_indices_verified_at_rest':
        errors = np.linalg.norm(source - target, axis=1)
    elif method == 'bidirectional_nearest_rest_position_mapping':
        a = np.linalg.norm(source - target[mapping['source_to_target']], axis=1)
        b = np.linalg.norm(target - source[mapping['target_to_source']], axis=1)
        errors = np.concatenate((a, b))
    else:
        _, a = ve_nearest_indices(source, target)
        _, b = ve_nearest_indices(target, source)
        errors = np.concatenate((a, b))
    bound_error = float(max(np.max(np.abs(source.min(axis=0) - target.min(axis=0))),
                            np.max(np.abs(source.max(axis=0) - target.max(axis=0)))))
    maximum = float(errors.max())
    return {'status': 'PASS' if max(maximum, bound_error) <= VE_VERTEX_TOLERANCE_M else 'FAIL',
            'method': method, 'source_vertices': len(source), 'imported_vertices': len(target),
            'max_position_error_m': maximum, 'mean_position_error_m': float(errors.mean()),
            'p95_position_error_m': float(np.percentile(errors, 95)),
            'max_bounds_coordinate_error_m': bound_error}


def ve_match_action(source_name, imported):
    def normalized(name):
        if len(name) > 4 and name[-4] == '.' and name[-3:].isdigit():
            name = name[:-4]
        return name
    matches = [a for a in imported if normalized(a.name) == source_name
               or normalized(a.name).endswith('|' + source_name)]
    return matches[0] if len(matches) == 1 else None, [a.name for a in matches]


def roundtrip(exportpath=None, action_names=None, reportpath=None):
    """Import FBX into a NEW empty scene, compare rest and start/middle/end of each take.

    Returns to the original scene and removes only data blocks created by this import.
    Both scenes use direct action playback with NLA muted, no physics approximation.
    This function does not overwrite the .blend or export file.
    """
    path = Path(exportpath) if exportpath else VE_OUT / 'Dosa_Phase1_FitRigV3.fbx'
    report_path = Path(reportpath) if reportpath else path.with_suffix('.roundtrip.json')
    report = {'status': 'INCOMPLETE', 'export_path': str(path),
              'position_tolerance_m': VE_VERTEX_TOLERANCE_M,
              'sample_method': 'Source-frame start, midpoint, end; 1:1 timing, no resampling.',
              'limitations': [
                  'Sampling is serialization evidence, not exhaustive animation validation.',
                  'A nearest-rest mapping can be ambiguous at coincident UV seam vertices.',
                  'A per-sample nearest fallback proves geometry proximity, not vertex identity.',
                  'Unity Humanoid/runtime, collision quality, cloth gravity and visual QA are UNVERIFIED.'
              ], 'action_samples': []}
    original_scene = bpy.context.scene
    snapshot = imported_scene = None
    rig = bpy.data.objects.get(VE_RIG)
    collections = ('objects', 'meshes', 'armatures', 'actions', 'materials', 'images', 'textures')
    prior = {name: set(getattr(bpy.data, name)) for name in collections}
    try:
        if not path.exists():
            raise RuntimeError('FBX does not exist: ' + str(path))
        if rig is None:
            raise RuntimeError('Missing source rig')
        parts = {name: bpy.data.objects[name] for name in VE_PARTS}
        actions = ve_actions(action_names)
        problems = ve_preflight(rig, list(parts.values()))
        if problems:
            raise RuntimeError('; '.join(problems))
        snapshot = ve_snapshot(original_scene, rig)
        ve_neutral(rig)
        original_scene.frame_set(1)
        bpy.context.view_layer.update()
        source_rest = {name: ve_geometry(obj) for name, obj in parts.items()}
        source_bones = ve_rest_bones(rig)
        imported_scene = bpy.data.scenes.new('__FitRigV3_EmptyFBXRoundtrip')
        imported_scene.render.fps = original_scene.render.fps
        imported_scene.render.fps_base = original_scene.render.fps_base
        imported_scene.unit_settings.system = original_scene.unit_settings.system
        imported_scene.unit_settings.scale_length = original_scene.unit_settings.scale_length
        bpy.context.window.scene = imported_scene
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True,
                                 automatic_bone_orientation=False)
        imported = set(bpy.data.objects) - prior['objects']
        imported_actions = set(bpy.data.actions) - prior['actions']
        report['imported_action_names'] = sorted(a.name for a in imported_actions)
        armatures = [o for o in imported if o.type == 'ARMATURE']
        meshes = [o for o in imported if o.type == 'MESH']
        if len(armatures) != 1 or len(meshes) != 3:
            raise RuntimeError('Expected one armature and three meshes; imported '
                               + str((len(armatures), len(meshes))))
        target = armatures[0]
        target_parts = {}
        for name in VE_PARTS:
            matches = [o for o in meshes if o.name == name or o.name.startswith(name + '.')]
            if len(matches) != 1:
                raise RuntimeError('Cannot uniquely identify imported mesh: ' + name)
            target_parts[name] = matches[0]
        ve_neutral(target)
        imported_scene.frame_set(1)
        bpy.context.view_layer.update()
        target_rest = {name: ve_geometry(obj) for name, obj in target_parts.items()}
        mappings = {name: ve_correspondence(source_rest[name][0], target_rest[name][0])
                    for name in VE_PARTS}
        report['rest_geometry'] = {name: ve_compare_points(source_rest[name][0], target_rest[name][0],
                                                          mappings[name]) for name in VE_PARTS}
        report['source_meshes'] = {name: value[1] for name, value in source_rest.items()}
        report['imported_meshes'] = {name: value[1] for name, value in target_rest.items()}
        report['imported_weights'] = {name: ve_weights(obj, target) for name, obj in target_parts.items()}
        report['bind_skeleton'] = ve_bone_comparison(source_bones, ve_rest_bones(target))
        total = sum(value[1]['triangles'] for value in target_rest.values())
        report['actual_fbx_triangles'] = total
        report['triangle_budget'] = {'target': 50000, 'maximum': 60000,
                                     'status': 'PASS' if total <= 60000 else 'FAIL'}
        report['dimensions_orientation'] = {
            'source_body_dimensions_m': source_rest['Body'][1]['world_dimensions_m'],
            'imported_body_dimensions_m': target_rest['Body'][1]['world_dimensions_m'],
            'method': 'World-coordinate geometry and bind heads after standard FBX -Z-forward/Y-up '
                      'export and automatic-orientation-disabled re-import into Blender Z-up.',
            'status': report['rest_geometry']['Body']['status']}
        report['shape_key_names'] = {
            name: {'source': source_rest[name][1]['shape_keys'],
                   'imported': target_rest[name][1]['shape_keys'],
                   'status': 'PASS' if source_rest[name][1]['shape_keys'] == target_rest[name][1]['shape_keys']
                             else 'FAIL'} for name in VE_PARTS}
        for source_action in actions:
            target_action, matches = ve_match_action(source_action.name, imported_actions)
            if target_action is None:
                report['action_samples'].append({'action': source_action.name, 'status': 'FAIL',
                                                'reason': 'Missing/ambiguous imported take', 'matches': matches})
                continue
            start, end = map(float, source_action.frame_range)
            tstart, tend = map(float, target_action.frame_range)
            timing_error = max(abs(start - tstart), abs(end - tend))
            for frame in sorted(set((start, (start + end) / 2, end))):
                bpy.context.window.scene = original_scene
                ve_neutral(rig)
                ve_assign(rig, source_action)
                original_scene.frame_set(math.floor(frame), subframe=frame % 1)
                bpy.context.view_layer.update()
                source_values = {name: ve_geometry(obj)[0] for name, obj in parts.items()}
                bpy.context.window.scene = imported_scene
                ve_neutral(target)
                ve_assign(target, target_action)
                imported_scene.frame_set(math.floor(frame), subframe=frame % 1)
                bpy.context.view_layer.update()
                values = {name: ve_compare_points(source_values[name], ve_geometry(obj)[0], mappings[name])
                          for name, obj in target_parts.items()}
                report['action_samples'].append({
                    'action': source_action.name, 'imported_action': target_action.name,
                    'source_range': [start, end], 'imported_range': [tstart, tend],
                    'range_error_frames': timing_error, 'frame': frame, 'parts': values,
                    'status': 'PASS' if timing_error <= 0.001 and all(
                        p['status'] == 'PASS' for p in values.values()) else 'FAIL'})
        checks = [report['bind_skeleton'], report['triangle_budget'],
                  *report['rest_geometry'].values(), *report['imported_weights'].values(),
                  *report['shape_key_names'].values(), *report['action_samples']]
        report['status'] = 'PASS_SERIALIZATION_ONLY' if checks and all(
            row['status'] == 'PASS' for row in checks) else 'FAIL'
    except Exception as exc:
        report['error'] = repr(exc)
    finally:
        bpy.context.window.scene = original_scene
        if snapshot is not None:
            ve_restore(snapshot, rig)
        # Cleanup is restricted to exact newly created datablock identities, never source data.
        for obj in list(set(bpy.data.objects) - prior['objects']):
            bpy.data.objects.remove(obj, do_unlink=True)
        if imported_scene is not None:
            bpy.data.scenes.remove(imported_scene)
        for name in collections[1:]:
            collection = getattr(bpy.data, name)
            for block in list(set(collection) - prior[name]):
                if block.users == 0:
                    collection.remove(block)
        ve_write(report_path, report)
    print(json.dumps({'status': report['status'], 'samples': len(report['action_samples']),
                      'report': str(report_path)}, ensure_ascii=False))
    return report
