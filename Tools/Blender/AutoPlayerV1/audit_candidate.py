"""Read-only candidate data/deformation audit. Definitions only; no execution on load.

audit_candidate(rig_name, mesh_names, action_name, output_path, provenance={...})
Samples EVERY integer frame in the selected action, at unchanged scene FPS.
No geometry/UV/weights/rest skeleton edits, added bones, cloth, or contact corrections.
Numerical results never automatically grant DEFORMATION_PASS.
"""
import json
import math
from pathlib import Path

import bpy
import numpy as np


def ac_assign(rig, action, slot=None):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None and len(getattr(action, 'slots', ())):
        choices = [s for s in action.slots if s.target_id_type == 'OBJECT']
        if not choices:
            raise RuntimeError('Action has no Object slot')
        rig.animation_data.action_slot = slot or next(
            (s for s in choices if rig.name in s.identifier), choices[0])


def ac_points(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    try:
        values = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get('co', values)
        local = values.reshape(-1, 3)
        matrix = np.asarray(evaluated.matrix_world, dtype=np.float64)
        world = local @ matrix[:3, :3].T + matrix[:3, 3]
        mesh.calc_loop_triangles()
        return world, len(mesh.loop_triangles)
    finally:
        evaluated.to_mesh_clear()


def ac_weights(obj, rig):
    bone_names = set(rig.data.bones.keys())
    influences, invalid = [], []
    unassigned = nonnormalized = negative = nonfinite = overfour = 0
    max_sum_error = 0.0
    for vertex in obj.data.vertices:
        row = [{'bone': obj.vertex_groups[g.group].name, 'weight': float(g.weight)}
               for g in vertex.groups if obj.vertex_groups[g.group].name in bone_names]
        bad = [r for r in row if not math.isfinite(r['weight']) or r['weight'] < 0]
        negative += sum(r['weight'] < 0 for r in row if math.isfinite(r['weight']))
        nonfinite += sum(not math.isfinite(r['weight']) for r in row)
        positive = [r for r in row if math.isfinite(r['weight']) and r['weight'] > 1e-8]
        total = sum(r['weight'] for r in positive)
        error = abs(total - 1)
        max_sum_error = max(max_sum_error, error)
        unassigned += not positive
        nonnormalized += error > .0001
        overfour += len(positive) > 4
        if bad or not positive or error > .0001 or len(positive) > 4:
            invalid.append(vertex.index)
        influences.append(positive)
    summary = {'vertices': len(influences), 'unassigned': int(unassigned),
               'negative_weights': int(negative), 'nonfinite_weights': int(nonfinite),
               'non_normalized_vertices': int(nonnormalized),
               'max_sum_error': max_sum_error,
               'max_influences': max(map(len, influences), default=0),
               'vertices_over_four_weights': int(overfour), 'invalid_vertex_ids': invalid,
               'status': 'PASS' if not invalid else 'FAIL'}
    return influences, summary


def ac_edge_detail(obj, edge_index, edges, rest, posed, weights, frame):
    i, j = map(int, edges[edge_index])
    old = float(np.linalg.norm(rest[j] - rest[i]))
    new = float(np.linalg.norm(posed[j] - posed[i]))
    return {'object': obj.name, 'frame': int(frame), 'edge_index': int(edge_index),
            'vertex_ids': [i, j], 'vertex_bone_weights': [weights[i], weights[j]],
            'rest_length_m': old, 'posed_length_m': new,
            'ratio': new / old if old > 1e-12 else None,
            'rest_world_xyz_m': [rest[i].tolist(), rest[j].tolist()],
            'posed_world_xyz_m': [posed[i].tolist(), posed[j].tolist()],
            'qualified_by_5mm_rest_length': old >= .005}


def audit_candidate(rig_name, mesh_names, action_name, output_path,
                    provenance=None, source_fps=None, source_frame_range=None):
    """provenance should identify actual source file/task and real_clip vs diagnostic_pose.

    Does not import or retarget a motion; the owning task must establish that correspondence.
    A same-named FitRigV3 action is not evidence that a required real motion was tested.
    """
    rig = bpy.data.objects[rig_name]
    meshes = [bpy.data.objects[n] for n in mesh_names]
    action = bpy.data.actions[action_name]
    scene = bpy.context.scene
    if bpy.context.mode != 'OBJECT':
        raise RuntimeError('Run from Object Mode; no automatic mode or geometry changes are made.')
    ad = rig.animation_data_create()
    saved = {'action': ad.action, 'slot': getattr(ad, 'action_slot', None),
             'frame': scene.frame_current, 'subframe': scene.frame_subframe,
             'pose_position': rig.data.pose_position, 'use_nla': ad.use_nla,
             'nla': [(t, t.mute, t.is_solo) for t in ad.nla_tracks],
             'pose': {p.name: p.matrix_basis.copy() for p in rig.pose.bones}}
    start, end = map(float, action.frame_range)
    frames = list(range(math.ceil(start), math.floor(end) + 1))
    fps = scene.render.fps / scene.render.fps_base
    report = {'status': 'INCOMPLETE', 'rig': rig_name, 'action': action_name,
              'provenance': provenance or {}, 'provenance_status': 'DECLARED' if provenance else 'UNVERIFIED',
              'fps': fps, 'action_frame_range': [start, end], 'duration_seconds': (end - start) / fps,
              'required_integer_frames': frames, 'sampled_integer_frames': [],
              'source_fps': source_fps, 'source_frame_range': source_frame_range,
              'timing_status': 'UNVERIFIED' if source_fps is None or source_frame_range is None else
                  ('PASS' if abs(fps - source_fps) <= 1e-6 and np.allclose([start, end], source_frame_range, atol=1e-6) else 'FAIL'),
              'bone_count': len(rig.data.bones),
              'unexpected_old_helper_bones': [b.name for b in rig.data.bones if b.name.startswith(
                  ('Cloth_', 'SleeveFit_', 'Correct_'))],
              'thresholds': {'minimum_rest_edge_m': .005, 'review_ratio': 2.0, 'structural_suspicion_ratio': 4.0},
              'method': 'Evaluated rest/posed endpoints in the same world coordinate system. '
                  'Every integer action frame, unchanged FPS. Edge indices and vertex skin influences retained.',
              'deformation': 'UNVERIFIED', 'penetration': 'UNVERIFIED',
              'limitations': ['Numeric stretch warnings require visual judgement; no automatic deformation pass.',
                  'Integrated garments may lack a separate inner body; no fictional zero-penetration metric is emitted.',
                  'Short/zero-length rest edges are separately reported, not silently omitted.',
                  'Source clip provenance and retarget correctness must be validated by the owning task.'],
              'meshes': {}, 'frames': []}
    destination = Path(output_path)
    try:
        if not frames:
            raise RuntimeError('Action has no integer sample frame.')
        for obj in meshes:
            enabled = [m for m in obj.modifiers if m.show_viewport]
            if any(m.type != 'ARMATURE' for m in enabled):
                raise RuntimeError(obj.name + ': active non-skin modifier invalidates this basic-skinning audit.')
            arms = [m for m in enabled if m.type == 'ARMATURE']
            if len(arms) != 1 or arms[0].object != rig:
                raise RuntimeError(obj.name + ': exactly one Armature targeting the candidate rig is required.')
        ad.use_nla = False
        for track in ad.nla_tracks:
            track.mute, track.is_solo = True, False
        ac_assign(rig, action)
        scene.frame_set(frames[0])
        rig.data.pose_position = 'REST'
        bpy.context.view_layer.update()
        baseline = {}
        for obj in meshes:
            rest, tris = ac_points(obj)
            if len(rest) != len(obj.data.vertices):
                raise RuntimeError(obj.name + ': evaluated/base vertex correspondence differs.')
            edges = np.array([tuple(e.vertices) for e in obj.data.edges], dtype=np.int32).reshape(-1, 2)
            lengths = np.linalg.norm(rest[edges[:, 1]] - rest[edges[:, 0]], axis=1)
            weights, stats = ac_weights(obj, rig)
            baseline[obj.name] = (rest, edges, lengths, weights)
            report['meshes'][obj.name] = {'vertices': len(rest), 'triangles_rest_evaluated': tris,
                'weights': stats, 'rest_finite': bool(np.isfinite(rest).all()),
                'edge_count': len(edges), 'short_rest_edge_count': int(np.count_nonzero(lengths < .005)),
                'zero_rest_edge_ids': np.flatnonzero(lengths <= 1e-12).tolist(),
                'worst_qualified_edge': None, 'worst_short_edge': None,
                'warn_edges_over_2_unique': [], 'warn_edges_over_4_unique': []}
        warnings2 = {o.name: set() for o in meshes}
        warnings4 = {o.name: set() for o in meshes}
        rig.data.pose_position = 'POSE'
        for frame in frames:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            row = {'frame': frame, 'meshes': {}}
            for obj in meshes:
                rest, edges, old, weights = baseline[obj.name]
                points, tris = ac_points(obj)
                finite = bool(np.isfinite(points).all())
                if len(points) != len(rest):
                    raise RuntimeError(obj.name + ': evaluated vertex count changed at frame ' + str(frame))
                result = {'finite': finite, 'triangles_evaluated': tris}
                if finite and len(edges):
                    lengths = np.linalg.norm(points[edges[:, 1]] - points[edges[:, 0]], axis=1)
                    ratio = np.divide(lengths, old, out=np.zeros_like(old), where=old > 1e-12)
                    qualified = old >= .005
                    over2 = np.flatnonzero(qualified & (ratio > 2))
                    over4 = np.flatnonzero(qualified & (ratio > 4))
                    warnings2[obj.name].update(map(int, over2))
                    warnings4[obj.name].update(map(int, over4))
                    result.update(over_2_edge_ids=over2.tolist(), over_4_edge_ids=over4.tolist(),
                                  zero_rest_edges_opened_over_5mm=np.flatnonzero((old <= 1e-12) & (lengths > .005)).tolist())
                    for label, selection in (('qualified', qualified), ('short', (old < .005) & (old > 1e-12))):
                        ids = np.flatnonzero(selection)
                        if len(ids):
                            worst = int(ids[np.argmax(ratio[ids])])
                            detail = ac_edge_detail(obj, worst, edges, rest, points, weights, frame)
                            result['worst_' + label + '_edge'] = detail
                            previous = report['meshes'][obj.name]['worst_' + label + '_edge']
                            if previous is None or detail['ratio'] > previous['ratio']:
                                report['meshes'][obj.name]['worst_' + label + '_edge'] = detail
                row['meshes'][obj.name] = result
            report['frames'].append(row)
            report['sampled_integer_frames'].append(frame)
        for obj in meshes:
            report['meshes'][obj.name]['warn_edges_over_2_unique'] = sorted(warnings2[obj.name])
            report['meshes'][obj.name]['warn_edges_over_4_unique'] = sorted(warnings4[obj.name])
        report['triangles_total_rest_evaluated'] = sum(m['triangles_rest_evaluated'] for m in report['meshes'].values())
        report['triangle_budget'] = 'PASS' if report['triangles_total_rest_evaluated'] <= 60000 else 'FAIL'
        data_ok = all(m['rest_finite'] and m['weights']['status'] == 'PASS' for m in report['meshes'].values())
        finite_ok = all(m['finite'] for r in report['frames'] for m in r['meshes'].values())
        report['data_status'] = 'PASS' if data_ok and finite_ok and report['triangle_budget'] == 'PASS' else 'FAIL'
        report['status'] = ('FAIL_NUMERICAL_DATA' if report['data_status'] == 'FAIL' else
                            'WARNINGS_REQUIRE_VISUAL_REVIEW' if any(warnings2.values()) else 'NUMERICAL_CHECKS_PASS_ONLY')
    except Exception as exc:
        report['error'] = repr(exc)
    finally:
        ac_assign(rig, saved['action'], saved['slot'])
        ad.use_nla = saved['use_nla']
        for track, mute, solo in saved['nla']:
            track.mute, track.is_solo = mute, solo
        rig.data.pose_position = saved['pose_position']
        scene.frame_set(saved['frame'], subframe=saved['subframe'])
        for p in rig.pose.bones:
            p.matrix_basis = saved['pose'][p.name]
        bpy.context.view_layer.update()
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'frames': len(report['sampled_integer_frames']),
                      'report': str(destination)}, ensure_ascii=False))
    return report
