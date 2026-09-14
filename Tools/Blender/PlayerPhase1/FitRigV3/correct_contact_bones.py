"""Conservative contact correction using ONLY helper-bone location animation keys.

Definitions only. Example: correct_contacts('FIT_Run', frames=[11, 12, 13]).
parts and helperprefixes support owner-created Correct_* clothing helpers.
Never edits Body, mesh coordinates/weights, core bones, bind matrices, or rotation/scale keys.
Regularized LBS translation fit is bounded to <=20mm per iteration and <=4 iterations.
This is a diagnostic animation correction, NOT a runtime cloth or collision solver.
"""
import hashlib
import json
import math
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


CC_OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')
CC_DIRECTIONS = tuple(Vector(v).normalized() for v in
                      ((1, .137, .271), (.173, 1, .319), (.113, .237, 1)))


def cc_inside(tree, point):
    """Same majority-of-three ray parity convention as final_audit.py."""
    votes = 0
    for direction in CC_DIRECTIONS:
        current, hits = point.copy(), 0
        for _ in range(30):
            co, normal, index, distance = tree.ray_cast(current, direction, 4)
            if co is None:
                break
            hits += 1
            current = co + direction * .00001
        votes += hits % 2
    return votes >= 2


def cc_assign(rig, action, slot=None):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None and len(getattr(action, 'slots', ())):
        if slot is None:
            choices = [s for s in action.slots if s.target_id_type == 'OBJECT']
            slot = next((s for s in choices if rig.name in s.identifier), choices[0])
        rig.animation_data.action_slot = slot


def cc_points(obj, with_tree=False):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        coords = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get('co', coords)
        coords = coords.reshape(-1, 3)
        matrix = np.asarray(evaluated.matrix_world, dtype=np.float64)
        points = coords @ matrix[:3, :3].T + matrix[:3, 3]
        if not np.isfinite(points).all():
            raise RuntimeError('Nonfinite evaluated geometry: ' + obj.name)
        if not with_tree:
            return points
        mesh.calc_loop_triangles()
        tree = BVHTree.FromPolygons([Vector(p) for p in points],
                                   [tuple(t.vertices) for t in mesh.loop_triangles], all_triangles=True)
        return points, tree
    finally:
        evaluated.to_mesh_clear()


def cc_contacts(tree, points, weights, clearance=.003):
    ids, depths, deltas, safe = [], [], [], []
    signed_candidates = pinned = unsafe = 0
    for index, point in enumerate(points):
        p = Vector(point)
        co, normal, tri, distance = tree.find_nearest(p)
        if co is None or distance >= .1:
            continue
        signed = (p - co).dot(normal)
        if signed >= -.002:
            continue
        signed_candidates += 1
        if not cc_inside(tree, p):
            continue
        ids.append(index)
        depths.append(-signed)
        goal = co + normal * clearance
        # A nearby INTERNAL overlapping body surface is not a safe exit target.
        outward_safe = not cc_inside(tree, goal)
        controllable = float(weights[index].sum()) > 1e-7
        pinned += not controllable
        unsafe += not outward_safe
        safe.append(outward_safe and controllable)
        deltas.append(tuple(goal - p))
    return {'ids': np.asarray(ids, dtype=np.int32), 'depths': np.asarray(depths),
            'deltas': np.asarray(deltas, dtype=np.float64).reshape(-1, 3),
            'safe': np.asarray(safe, dtype=bool), 'signed_candidates': signed_candidates,
            'uncontrollable_vertices': int(pinned), 'unsafe_nearest_exit_vertices': int(unsafe)}


def cc_summary(contacts):
    depths = contacts['depths']
    return {'inside_vertices': len(depths),
            'max_depth_mm': float(depths.max() * 1000) if len(depths) else 0.0,
            'squared_depth_sum_m2': float(depths @ depths),
            'uncontrollable_vertices': contacts['uncontrollable_vertices'],
            'unsafe_nearest_exit_vertices': contacts['unsafe_nearest_exit_vertices'],
            'signed_candidates': contacts['signed_candidates']}


def cc_curves(action):
    result = []
    for layer in getattr(action, 'layers', ()):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', ()):
                result.extend(bag.fcurves)
    if not result and hasattr(action, 'fcurves'):
        result.extend(action.fcurves)
    return result


def cc_protected_hash(action, helpers):
    permitted = {f'pose.bones["{name}"].location' for name in helpers}
    digest = hashlib.sha256()
    for curve in cc_curves(action):
        if curve.data_path in permitted:
            continue
        digest.update((curve.data_path + ':' + str(curve.array_index)).encode())
        for key in curve.keyframe_points:
            digest.update(np.asarray((*key.co, *key.handle_left, *key.handle_right), dtype=np.float64).tobytes())
            digest.update(key.interpolation.encode())
    return digest.hexdigest()


def cc_bind_hash(rig):
    digest = hashlib.sha256()
    for bone in rig.data.bones:
        digest.update(bone.name.encode())
        digest.update(np.asarray(bone.matrix_local, dtype=np.float64).tobytes())
        digest.update((bone.parent.name if bone.parent else '').encode())
    return digest.hexdigest()


def cc_rotation_state(p):
    if p.rotation_mode == 'QUATERNION':
        return 'rotation_quaternion', p.rotation_quaternion.copy()
    if p.rotation_mode == 'AXIS_ANGLE':
        return 'rotation_axis_angle', tuple(p.rotation_axis_angle)
    return 'rotation_euler', p.rotation_euler.copy()


def cc_apply_world_offsets(rig, helpers, baseline_world, rotation_scale, offsets):
    """Parents first; explicitly restore each child's intended world pose after parent changes.

    A child with zero requested world displacement receives compensating LOCAL translation
    if its parent moves. Consequently W maps independent helper WORLD shifts, even in chains.
    Rotation and scale channels are restored exactly after Blender converts each matrix.
    """
    world_inverse = rig.matrix_world.inverted_safe()
    last_depth = None
    for index, name in enumerate(helpers):
        p = rig.pose.bones[name]
        depth, parent = 0, p.parent
        while parent:
            depth += 1
            parent = parent.parent
        # Siblings are independent: update once per hierarchy level, not once per bone.
        if last_depth is not None and depth != last_depth:
            bpy.context.view_layer.update()
        last_depth = depth
        desired = baseline_world[name].copy()
        desired.translation += Vector(offsets[index])
        p.matrix = world_inverse @ desired
        rotation_channel, rotation, scale = rotation_scale[name]
        setattr(p, rotation_channel, rotation)
        p.scale = scale
    bpy.context.view_layer.update()


def cc_preflight(rig, objects, helperprefixes):
    names = {b.name for b in rig.data.bones if b.name.startswith(tuple(helperprefixes))}
    if not names:
        raise RuntimeError('No helper bones match the requested prefixes.')
    def depth(name):
        result, parent = 0, rig.data.bones[name].parent
        while parent:
            result += 1
            parent = parent.parent
        return result
    helpers = sorted(names, key=lambda n: (depth(n), n))
    for bone in rig.data.bones:
        if bone.name not in names:
            parent = bone.parent
            while parent:
                if parent.name in names:
                    raise RuntimeError('Core/non-helper bone descends from moved helper: ' + bone.name)
                parent = parent.parent
    for name in helpers:
        if rig.pose.bones[name].constraints:
            raise RuntimeError('Helper pose constraints must be explicitly resolved first: ' + name)
    for obj in [bpy.data.objects['Body'], *objects]:
        if obj.name == 'Body':
            if any(any(obj.vertex_groups[g.group].name in names and g.weight > 1e-8 for g in v.groups)
                   for v in obj.data.vertices):
                raise RuntimeError('Body is weighted to the movable clothing helpers.')
        else:
            visible = [m for m in obj.modifiers if m.show_viewport]
            if len(visible) != 1 or visible[0].type != 'ARMATURE' or visible[0].object != rig:
                raise RuntimeError(obj.name + ' must be Armature-only for this linear skin correction.')
            if visible[0].use_deform_preserve_volume:
                raise RuntimeError(obj.name + ' uses dual-quaternion skinning; linear weight fitting is invalid.')
    return helpers


def correct_contacts(actionname, frames=None, parts=('Durumagi',),
                     helperprefixes=('Cloth_', 'SleeveFit_', 'Correct_'),
                     iterations=4, max_step_m=.020, clearance_m=.003,
                     anchor_weight=.035, ridge_weight=.08, temporal_weight=.05):
    """Correct only helper location keys on the requested action/frames; original action backed up.

    Use small explicit frame batches when calling over MCP. Existing copied FIT_* or
    FITTEST_* diagnostic actions are accepted; original DIAG_* actions are protected.
    Every provided integer frame is tested. Unprovided frames and interpolation remain unverified.
    """
    if not (actionname.startswith('FIT_') or actionname.startswith('FITTEST_')):
        raise RuntimeError('Refused to modify a non-FIT diagnostic action: ' + actionname)
    if bpy.context.mode != 'OBJECT':
        raise RuntimeError('Run in Object Mode.')
    if iterations < 1 or iterations > 4 or max_step_m <= 0 or max_step_m > .020:
        raise ValueError('Correction limit is 1..4 iterations, at most 20mm per iteration.')
    rig = bpy.data.objects['Dosa_Phase1_Rig']
    action = bpy.data.actions[actionname]
    objects = [bpy.data.objects[name] for name in parts]
    helpers = cc_preflight(rig, objects, helperprefixes)
    hindex = {name: i for i, name in enumerate(helpers)}
    weights, ranges, offset = [], {}, 0
    for obj in objects:
        w = np.zeros((len(obj.data.vertices), len(helpers)), dtype=np.float64)
        for vertex in obj.data.vertices:
            for g in vertex.groups:
                name = obj.vertex_groups[g.group].name
                if name in hindex:
                    w[vertex.index, hindex[name]] = g.weight
        weights.append(w)
        ranges[obj.name] = (offset, offset + len(w))
        offset += len(w)
    weights = np.concatenate(weights)
    anchor_gram = weights.T @ weights
    scene = bpy.context.scene
    ad = rig.animation_data_create()
    snapshot = {'action': ad.action, 'slot': getattr(ad, 'action_slot', None),
                'use_nla': ad.use_nla, 'frame': scene.frame_current, 'subframe': scene.frame_subframe,
                'pose_position': rig.data.pose_position,
                'nla': [(t, t.mute, t.is_solo) for t in ad.nla_tracks],
                'pose': {p.name: p.matrix_basis.copy() for p in rig.pose.bones}}
    chosen_frames = sorted(set(int(f) for f in frames)) if frames is not None else list(
        range(math.ceil(action.frame_range[0]), math.floor(action.frame_range[1]) + 1))
    if not chosen_frames:
        raise RuntimeError('No frames requested.')
    backup = action.copy()
    backup.name = 'BEFORE_CONTACT_' + actionname
    backup.use_fake_user = True
    protected_before, bind_before = cc_protected_hash(action, helpers), cc_bind_hash(rig)
    report = {
        'status': 'RUNNING', 'action': actionname, 'backup_action': backup.name,
        'parts': list(parts), 'helpers': helpers, 'requested_frames': chosen_frames,
        'max_step_m': max_step_m, 'iterations_limit': iterations, 'clearance_m': clearance_m,
        'anchor_weight': anchor_weight, 'ridge_weight': ridge_weight, 'temporal_weight': temporal_weight,
        'method': 'Actual Body BVH signed depth < -2mm within 100mm, >=2/3 parity rays. '
                  'Exit must be outside by parity. Regularized helper-weight linear least squares; '
                  '20mm-limited world translations with decreasing-step objective acceptance. '
                  'Parent-first absolute world targets compensate child inheritance; only location keys change.',
        'limits': ['Open/overlapping Body surfaces can make ray parity ambiguous.',
                   'Helper-weight-zero vertices are reported, never moved through core bones.',
                   'Only Body contact is solved; garment self-contact and layer contact are unverified.',
                   'Per-call helper world displacement is bounded; repeated calls can accumulate corrections.',
                   'Unsampled frames, visual quality and Unity/runtime remain unverified.'], 'rows': []}
    path = CC_OUT / ('contactsolve_' + actionname + '.json')
    started = time.time()
    previous_offset = np.zeros((len(helpers), 3))
    previous_frame = None
    try:
        ad.use_nla = False
        for track in ad.nla_tracks:
            track.mute, track.is_solo = True, False
        rig.data.pose_position = 'POSE'
        cc_assign(rig, action)
        for frame in chosen_frames:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            baseline_world = {n: rig.matrix_world @ rig.pose.bones[n].matrix for n in helpers}
            rotation_scale = {n: (*cc_rotation_state(rig.pose.bones[n]), rig.pose.bones[n].scale.copy())
                              for n in helpers}
            body_before, tree = cc_points(bpy.data.objects['Body'], True)
            points = np.concatenate([cc_points(o) for o in objects])
            if len(points) != len(weights):
                raise RuntimeError('Evaluated topology changed; skin weight index correspondence is invalid.')
            contacts = cc_contacts(tree, points, weights, clearance_m)
            row = {'frame': frame, 'before': cc_summary(contacts), 'iterations': []}
            total_offset = np.zeros((len(helpers), 3))
            for iteration in range(iterations):
                use = contacts['safe']
                if not np.any(use):
                    break
                ids = contacts['ids'][use]
                wc = weights[ids]
                goals = contacts['deltas'][use].copy()
                lengths = np.linalg.norm(goals, axis=1)
                goals *= np.minimum(1.0, max_step_m / np.maximum(lengths, 1e-12))[:, None]
                # Deep contacts have modest priority, never unconstrained enormous displacements.
                priority = np.clip(contacts['depths'][use] / .010, 1, 3)
                wc_weighted = wc * np.sqrt(priority)[:, None]
                goals_weighted = goals * np.sqrt(priority)[:, None]
                contact_gram = wc_weighted.T @ wc_weighted
                diagonal = np.maximum(np.diag(contact_gram), 1.0)
                active_temporal = temporal_weight if previous_frame == frame - 1 else 0.0
                lhs = contact_gram + anchor_weight * anchor_gram + np.diag(
                    diagonal * (ridge_weight + active_temporal))
                rhs = wc_weighted.T @ goals_weighted
                if active_temporal:
                    rhs += (diagonal * active_temporal)[:, None] * (previous_offset - total_offset)
                proposed = np.linalg.solve(lhs, rhs)
                # Helpers with no contact influence receive zero, apart from local compensation of ancestry.
                active = np.any(wc > 1e-8, axis=0)
                proposed[~active] = 0
                lengths = np.linalg.norm(proposed, axis=1)
                proposed *= np.minimum(1, max_step_m / np.maximum(lengths, 1e-12))[:, None]
                if not np.isfinite(proposed).all():
                    raise RuntimeError('Nonfinite least-squares correction.')
                old_objective = float(contacts['depths'] @ contacts['depths'])
                old_max = float(contacts['depths'].max()) if len(contacts['depths']) else 0
                accepted = None
                for scale in (1.0, .5, .25):
                    candidate = total_offset + scale * proposed
                    cc_apply_world_offsets(rig, helpers, baseline_world, rotation_scale, candidate)
                    candidate_points = np.concatenate([cc_points(o) for o in objects])
                    next_contacts = cc_contacts(tree, candidate_points, weights, clearance_m)
                    objective = float(next_contacts['depths'] @ next_contacts['depths'])
                    maximum = float(next_contacts['depths'].max()) if len(next_contacts['depths']) else 0
                    if objective < old_objective - 1e-12 and maximum <= old_max + .002:
                        accepted = (candidate, candidate_points, next_contacts, scale)
                        break
                if accepted is None:
                    cc_apply_world_offsets(rig, helpers, baseline_world, rotation_scale, total_offset)
                    row['iterations'].append({'iteration': iteration + 1, 'status': 'REJECTED_NO_SAFE_IMPROVEMENT'})
                    break
                total_offset, points, contacts, scale = accepted
                row['iterations'].append({'iteration': iteration + 1, 'status': 'ACCEPTED',
                                           'step_scale': scale, 'after': cc_summary(contacts)})
            changed = bool(np.max(np.linalg.norm(total_offset, axis=1)) > 1e-9)
            if changed:
                for name in helpers:
                    rig.pose.bones[name].keyframe_insert(data_path='location', frame=frame,
                                                       group='ContactCorrection')
                permitted = {f'pose.bones["{name}"].location' for name in helpers}
                for curve in cc_curves(action):
                    if curve.data_path in permitted:
                        for key in curve.keyframe_points:
                            if abs(key.co.x - frame) < 1e-5:
                                key.interpolation = 'LINEAR'
            body_after = cc_points(bpy.data.objects['Body'])
            body_error = float(np.linalg.norm(body_after - body_before, axis=1).max())
            if body_error > 1e-7:
                raise RuntimeError('Unexpected Body motion from clothing correction: ' + str(body_error))
            row['after'] = cc_summary(contacts)
            row['maximum_helper_world_offset_mm'] = float(np.linalg.norm(total_offset, axis=1).max() * 1000)
            row['Body_max_change_m'] = body_error
            row['parts_after'] = {}
            for name, (lo, hi) in ranges.items():
                selected = (contacts['ids'] >= lo) & (contacts['ids'] < hi)
                depths = contacts['depths'][selected]
                row['parts_after'][name] = {'inside_vertices': len(depths),
                    'max_inside_mm': float(depths.max() * 1000) if len(depths) else 0,
                    'inside_vertex_ids': (contacts['ids'][selected] - lo).tolist()}
            row['status'] = 'PASS_SAMPLED_BODY_CONTACT' if not len(contacts['ids']) else 'FAIL_RESIDUAL_CONTACT'
            report['rows'].append(row)
            previous_offset, previous_frame = total_offset.copy(), frame
            CC_OUT.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        report['protected_action_channels_unchanged'] = cc_protected_hash(action, helpers) == protected_before
        report['rest_bind_unchanged'] = cc_bind_hash(rig) == bind_before
        report['status'] = ('PASS_SAMPLED_BODY_CONTACT_ONLY' if report['rows'] and all(
            r['status'] == 'PASS_SAMPLED_BODY_CONTACT' for r in report['rows']) else 'FAIL_RESIDUAL_CONTACT')
        if not report['protected_action_channels_unchanged'] or not report['rest_bind_unchanged']:
            report['status'] = 'FAIL_PROTECTED_DATA_CHANGED'
    except Exception as exc:
        report['status'], report['error'] = 'INCOMPLETE', repr(exc)
    finally:
        cc_assign(rig, snapshot['action'], snapshot['slot'])
        ad.use_nla = snapshot['use_nla']
        for track, mute, solo in snapshot['nla']:
            track.mute, track.is_solo = mute, solo
        rig.data.pose_position = snapshot['pose_position']
        scene.frame_set(snapshot['frame'], subframe=snapshot['subframe'])
        if snapshot['action'] != action:
            for p in rig.pose.bones:
                p.matrix_basis = snapshot['pose'][p.name]
        bpy.context.view_layer.update()
        report['elapsed_seconds'] = time.time() - started
        CC_OUT.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'frames': len(report['rows']),
                      'report': str(path), 'backup_action': backup.name}, ensure_ascii=False))
    return report


# Descriptive alias for the owner's requested naming.
contactsolve = correct_contacts
