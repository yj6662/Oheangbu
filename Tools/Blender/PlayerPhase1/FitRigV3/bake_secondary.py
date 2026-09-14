"""Fit the existing regular cloth driver to Unity-compatible helper bone animation.

Definitions only; load through Blender MCP, then call in this order:
  add_helper_bones()
  bind_render_weights()
  cache_and_bake_action('Walk')  # also Run, ArmsUp, Squat, BrushSwing

Owns 48 Cloth_XX_J hem bones, adds 24 SleeveFit_<side>_<axial>_<sector>
bones, and modifies only Durumagi skin weights/modifier enablement. Core
Humanoid bones, Body geometry/weights, InnerTop geometry/weights and original
DIAG actions are never edited. No file is loaded or .blend saved automatically.

InnerTop deliberately retains its fitted body skin: an OUTER sleeve simulation
is not a valid physical target for the tight inner sleeve.

The result approximates cloth with rigid patches. It is NOT a validated rig,
an exact cloth reconstruction, or a Unity runtime cloth implementation.
"""
import bpy, json, math, re
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Art/PlayerPhase1/FitRigV3'
CONFIG = OUT / 'secondary_patch_definition.json'
LABELS = ('Walk', 'Run', 'ArmsUp', 'Squat', 'BrushSwing')

def _write(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def _scene():
    return bpy.context.scene, bpy.data.objects['Dosa_Phase1_Rig'], bpy.data.objects['ClothProxy_Durumagi']

def _smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)

def _assign(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action and hasattr(action, 'slots') and len(action.slots):
        slots = [s for s in action.slots if getattr(s, 'target_id_type', 'OBJECT') == 'OBJECT']
        slot = next((s for s in slots if rig.name in s.identifier), slots[0] if slots else action.slots[0])
        rig.animation_data.action_slot = slot

def _curve_collections(action):
    if hasattr(action, 'fcurves'):
        try:
            if len(action.fcurves):
                return [action.fcurves]
        except RuntimeError:
            pass
    return [bag.fcurves for layer in getattr(action, 'layers', [])
            for strip in layer.strips for bag in getattr(strip, 'channelbags', [])]

def _state(rig):
    ad = rig.animation_data
    return {'action': ad.action if ad else None,
            'slot': getattr(ad, 'action_slot', None) if ad else None,
            'pose': {p.name: p.matrix_basis.copy() for p in rig.pose.bones},
            'nla': [(t, t.mute) for t in ad.nla_tracks] if ad else [],
            'frame': bpy.context.scene.frame_current,
            'subframe': bpy.context.scene.frame_subframe}

def _neutral(rig):
    _assign(rig, None)
    for t in rig.animation_data.nla_tracks:
        t.mute = True
    for p in rig.pose.bones:
        p.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()

def _restore(rig, saved):
    _assign(rig, saved['action'])
    if saved['action'] and saved['slot']:
        rig.animation_data.action_slot = saved['slot']
    for t, muted in saved['nla']:
        t.mute = muted
    for p in rig.pose.bones:
        p.matrix_basis = saved['pose'].get(p.name, Matrix.Identity(4))
    bpy.context.view_layer.update()

def _rest_proxy(rig, proxy):
    transform = rig.matrix_world.inverted_safe() @ proxy.matrix_world
    return np.asarray([transform @ v.co for v in proxy.data.vertices], dtype=np.float64)

def _components(mesh):
    adjacent = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacent[a].append(b); adjacent[b].append(a)
    unseen = set(range(len(adjacent))); result = []
    while unseen:
        first = unseen.pop(); group = [first]; queue = [first]
        while queue:
            for other in adjacent[queue.pop()]:
                if other in unseen:
                    unseen.remove(other); queue.append(other); group.append(other)
        result.append(sorted(group))
    return result

def _definition(rig, proxy):
    """Use three connected islands, not fragile absolute vertex offsets."""
    rest = _rest_proxy(rig, proxy)
    groups = [g for g in _components(proxy.data) if len(g) > 50]
    if len(groups) != 3:
        raise RuntimeError('Expected torso/hem + two sleeve islands; topology changed.')
    torso = min(groups, key=lambda g: float(rest[g, 2].min()))
    sleeves = [g for g in groups if g is not torso]
    sides = {'Left': max(sleeves, key=lambda g: float(rest[g, 0].mean())),
             'Right': min(sleeves, key=lambda g: float(rest[g, 0].mean()))}
    patches = []
    hem_ids = np.asarray([i for i in torso if rest[i, 2] < 1.075], dtype=int)
    if len(hem_ids) < 100:
        raise RuntimeError('Insufficient hem samples below z=1.075m; check units.')
    # Match the known elliptical open skirt parameterization approximately.
    z = rest[hem_ids, 2]
    t = np.clip((1.065 - z) / .885, 0, 1)
    theta = np.arctan2(rest[hem_ids, 0] / (.205 + .105 * t),
                       -rest[hem_ids, 1] / (.165 + .13 * t))
    heights = (.9525, .75, .545, .32)
    for i in range(12):
        angle = 2 * math.pi * i / 12
        da = np.arctan2(np.sin(theta - angle), np.cos(theta - angle))
        for j, center_z in enumerate(heights):
            weights = np.exp(-.5 * ((da / .40) ** 2 + ((z - center_z) / .145) ** 2))
            use = weights > .035
            ids, w = hem_ids[use], weights[use]
            if len(ids) < 6:
                raise RuntimeError('Hem patch has too few samples.')
            patches.append({'name': f'Cloth_{i:02d}_{j}', 'kind': 'hem', 'parent': 'Hips',
                            'indices': ids.tolist(), 'weights': (w / w.sum()).tolist(),
                            'axis': [0, 0, -1], 'roll': [math.sin(angle), -math.cos(angle), 0]})
    for side, ids_list in sides.items():
        ids = np.asarray(ids_list, dtype=int)
        points = rest[ids]
        x = np.abs(points[:, 0])
        xlo, xhi = float(x.min()), float(x.max())
        if xhi - xlo < .15:
            raise RuntimeError('Sleeve axial extent invalid; inspect proxy units/topology.')
        # Local cross-section centers follow the actual rest proxy, including refits.
        cy = np.empty(len(ids)); cz = np.empty(len(ids))
        for k, px in enumerate(x):
            ring = np.abs(x - px) < max(.009, (xhi - xlo) / 20)
            cy[k] = np.mean(points[ring, 1]); cz[k] = np.mean(points[ring, 2])
        theta = np.arctan2(points[:, 2] - cz, points[:, 1] - cy)
        for axial in range(3):
            xc = xlo + (axial + .5) * (xhi - xlo) / 3
            for sector in range(4):
                angle = 2 * math.pi * sector / 4
                da = np.arctan2(np.sin(theta - angle), np.cos(theta - angle))
                weights = np.exp(-.5 * (((x - xc) / ((xhi - xlo) / 3 * .75)) ** 2 + (da / .85) ** 2))
                use = weights > .06
                selected, w = ids[use], weights[use]
                if len(selected) < 6:
                    raise RuntimeError('Sleeve patch has too few samples.')
                patches.append({'name': f'SleeveFit_{side}_{axial}_{sector}', 'kind': side,
                                'parent': side + 'Arm', 'indices': selected.tolist(),
                                'weights': (w / w.sum()).tolist(),
                                'axis': [1 if side == 'Left' else -1, 0, 0], 'roll': [0, 0, 1]})
    for patch in patches:
        patch['center'] = np.sum(rest[patch['indices']] * np.asarray(patch['weights'])[:, None], axis=0).tolist()
    return {'version': 1, 'proxy': proxy.name, 'proxy_vertex_count': len(rest),
            'rest_proxy_rig_space': rest.tolist(), 'patches': patches,
            'islands': {'torso': torso, **sides}, 'limitations':
            ['Rigid patch approximation, not exact cloth reconstruction.',
             'InnerTop remains on its fitted original body skin.',
             'Body/core bones are not edited.']}

def _load_checked(rig, proxy):
    data = json.loads(CONFIG.read_text(encoding='utf-8'))
    rest = _rest_proxy(rig, proxy)
    expected = np.asarray(data['rest_proxy_rig_space'])
    if rest.shape != expected.shape or np.max(np.abs(rest - expected)) > 1e-6:
        raise RuntimeError('Proxy rest coordinates changed after helper bind. Run add_helper_bones() and bind_render_weights() again before baking.')
    return data

def add_helper_bones():
    """Reframe only cloth helper bones in a neutral bind, add 24 sleeve helpers."""
    scene, rig, proxy = _scene(); saved = _state(rig)
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        raise RuntimeError('Switch to Object mode before this operation.')
    data = _definition(rig, proxy)
    names = {p['name'] for p in data['patches']}
    core_before = {b.name: np.asarray(b.matrix_local).copy() for b in rig.data.bones if b.name not in names}
    # Fail early if any core body skin accidentally depends on a cloth helper.
    body = bpy.data.objects['Body']
    bad = sum(any(body.vertex_groups[g.group].name in names and g.weight > 1e-8 for g in v.groups) for v in body.data.vertices)
    if bad:
        raise RuntimeError(f'Body has {bad} cloth-helper-weighted vertices; refused to edit helpers.')
    old_active = bpy.context.view_layer.objects.active
    old_selected = list(bpy.context.selected_objects)
    old_hidden = rig.hide_get()
    try:
        _neutral(rig)
        rig.hide_set(False)
        for obj in bpy.context.selected_objects: obj.select_set(False)
        rig.select_set(True); bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode='EDIT')
        for patch in data['patches']:
            bone = rig.data.edit_bones.get(patch['name']) or rig.data.edit_bones.new(patch['name'])
            bone.use_connect = False
            bone.parent = rig.data.edit_bones[patch['parent']]
            bone.head = Vector(patch['center'])
            bone.tail = bone.head + Vector(patch['axis']) * .075
            bone.align_roll(Vector(patch['roll']))
            bone.use_deform = True
        bpy.ops.object.mode_set(mode='OBJECT')
        for patch in data['patches']:
            pb = rig.pose.bones[patch['name']]
            pb.rotation_mode = 'QUATERNION'; pb.matrix_basis = Matrix.Identity(4)
        core_error = max((float(np.max(np.abs(np.asarray(rig.data.bones[n].matrix_local) - m))) for n, m in core_before.items()), default=0)
        if core_error > 1e-7:
            raise RuntimeError(f'Unexpected core rest matrix change {core_error}; inspect backup before continuing.')
        data['core_rest_matrix_max_change'] = core_error
        data['bones_after'] = len(rig.data.bones)
        data['bone_rest_matrices'] = {p['name']: [list(row) for row in rig.data.bones[p['name']].matrix_local] for p in data['patches']}
        _write(CONFIG.name, data)
        result = {'helper_bones': len(names), 'hem_bones': 48, 'sleeve_bones': 24,
                  'total_bones': len(rig.data.bones), 'core_rest_matrix_max_change': core_error}
        print(json.dumps(result)); return result
    finally:
        if rig.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
        _restore(rig, saved)
        rig.hide_set(old_hidden)
        for obj in bpy.context.selected_objects: obj.select_set(False)
        for obj in old_selected:
            if obj.name in bpy.context.view_layer.objects: obj.select_set(True)
        bpy.context.view_layer.objects.active = old_active

def _normalized(weights, limit=4):
    best = sorted(((n, float(w)) for n, w in weights.items() if w > 1e-9), key=lambda x: -x[1])[:limit]
    total = sum(w for n, w in best)
    return {n: w / total for n, w in best} if total else {}

def bind_render_weights():
    """Keep collar/shoulder body weights, smoothly blend free outer cloth into helpers.
    Only Durumagi is rebound. InnerTop intentionally remains unchanged.
    """
    scene, rig, proxy = _scene(); data = _load_checked(rig, proxy)
    robe = bpy.data.objects['Durumagi']; body = bpy.data.objects['Body']
    saved = _state(rig); original_coords = [v.co.copy() for v in robe.data.vertices]
    helper_names = {p['name'] for p in data['patches']}
    original = [{robe.vertex_groups[g.group].name: g.weight for g in v.groups
                 if robe.vertex_groups[g.group].name in rig.data.bones} for v in robe.data.vertices]
    # Save the exact pre-bind weights for inspection/restoration by the owning task.
    _write('durumagi_weights_before_secondary.json', {'mesh': robe.name, 'vertices': len(original), 'weights': original})
    proxy_rest = np.asarray(data['rest_proxy_rig_space'])
    kd = KDTree(len(proxy_rest))
    for i, p in enumerate(proxy_rest): kd.insert(Vector(p), i)
    kd.balance()
    kinds = {i: k for k, ids in data['islands'].items() for i in ids}
    pin = proxy.vertex_groups.get('SimPin')
    pinvalues = [sum(g.weight for g in v.groups if pin and g.group == pin.index) for v in proxy.data.vertices]
    patch_by_kind = {k: [p for p in data['patches'] if p['kind'] == k] for k in ('hem', 'Left', 'Right')}
    body_transform = rig.matrix_world.inverted_safe() @ body.matrix_world
    body_kd = KDTree(len(body.data.vertices))
    for v in body.data.vertices: body_kd.insert(body_transform @ v.co, v.index)
    body_kd.balance()
    robe_transform = rig.matrix_world.inverted_safe() @ robe.matrix_world
    changed = 0; regions = {'body_only': 0, 'hem': 0, 'Left': 0, 'Right': 0}
    try:
        _neutral(rig)
        for vertex in robe.data.vertices:
            point = robe_transform @ vertex.co
            neighbors = kd.find_n(point, 3)
            denom = sum(1 / max(.005, dist) ** 2 for co, i, dist in neighbors)
            pinweight = sum(pinvalues[i] / max(.005, dist) ** 2 for co, i, dist in neighbors) / denom
            nearest_kind = kinds[neighbors[0][1]]
            if point.z < 1.10:
                kind = 'hem'
                blend = (1 - _smooth(1.00, 1.10, point.z)) * (1 - pinweight)
            elif nearest_kind in ('Left', 'Right') and point.z > 1.12:
                kind = nearest_kind
                # Keep the collar, shoulder root and pinned upper seam on body skin.
                blend = _smooth(.19, .31, abs(point.x)) * (1 - pinweight)
            else:
                kind = None; blend = 0
            core = {n: w for n, w in original[vertex.index].items() if n not in helper_names}
            if not core:
                for co, i, dist in body_kd.find_n(point, 3):
                    for g in body.data.vertices[i].groups:
                        name = body.vertex_groups[g.group].name
                        if name in rig.data.bones and name not in helper_names:
                            core[name] = core.get(name, 0) + g.weight / max(.008, dist) ** 2
            core = _normalized(core) or {'Hips': 1.0}
            if blend <= 1e-5 or kind is None:
                result = core; regions['body_only'] += 1
            else:
                choices = sorted(((float((point - Vector(p['center'])).length), p['name'])
                                  for p in patch_by_kind[kind]))[:4]
                secondary = _normalized({name: 1 / max(.012, distance) ** 2 for distance, name in choices})
                if blend >= .999:
                    result = secondary
                else:
                    # Explicit 2+2 support preserves the transition amount under the four-weight cap.
                    core2 = _normalized(core, 2); secondary2 = _normalized(secondary, 2)
                    result = {n: w * (1 - blend) for n, w in core2.items()}
                    for n, w in secondary2.items(): result[n] = w * blend
                    result = _normalized(result)
                regions[kind] += 1
            for group_index in [g.group for g in vertex.groups]:
                if robe.vertex_groups[group_index].name in rig.data.bones:
                    robe.vertex_groups[group_index].remove([vertex.index])
            for name, weight in result.items():
                (robe.vertex_groups.get(name) or robe.vertex_groups.new(name=name)).add([vertex.index], weight, 'REPLACE')
            changed += 1
        arms = [m for m in robe.modifiers if m.type == 'ARMATURE']
        arm = arms[0] if arms else robe.modifiers.new('FitRigV3 Skin', 'ARMATURE')
        arm.object = rig; arm.show_viewport = True; arm.show_render = True
        arm.use_deform_preserve_volume = False
        for m in robe.modifiers:
            if m.type in ('SURFACE_DEFORM', 'CLOTH') or (m.type == 'ARMATURE' and m != arm):
                m.show_viewport = False; m.show_render = False
        bpy.context.view_layer.update()
        evaluated = robe.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        try:
            neutral_error = max(((mesh.vertices[i].co - p).length for i, p in enumerate(original_coords)), default=0) if len(mesh.vertices) == len(original_coords) else None
        finally:
            evaluated.to_mesh_clear()
        max_weights = max(sum(g.weight > 1e-8 and robe.vertex_groups[g.group].name in rig.data.bones for g in v.groups) for v in robe.data.vertices)
        result = {'mesh': robe.name, 'vertices': changed, 'regions': regions,
                  'max_deform_weights': max_weights, 'neutral_bind_max_local_error_m': neutral_error,
                  'inner_top': 'Original fitted body skin unchanged',
                  'status': 'BOUND_REQUIRES_DYNAMIC_VALIDATION'}
        _write('secondary_binding_report.json', result)
        print(json.dumps(result)); return result
    finally:
        _restore(rig, saved)

def _fit_rigid(source, target, weights):
    """Column-vector rigid transform; no scaling, no reflection."""
    w = np.asarray(weights, dtype=np.float64); w = w / w.sum()
    xbar = np.sum(source * w[:, None], axis=0)
    ybar = np.sum(target * w[:, None], axis=0)
    x = source - xbar; y = target - ybar
    u, sigma, vt = np.linalg.svd((x * w[:, None]).T @ y)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0:
        vt[-1] *= -1
        rotation = vt.T @ u.T
    translation = ybar - rotation @ xbar
    matrix = np.eye(4); matrix[:3, :3] = rotation; matrix[:3, 3] = translation
    residual = np.linalg.norm(source @ rotation.T + translation - target, axis=1)
    return matrix, float(np.sqrt(np.sum(w * residual ** 2))), float(residual.max())

def cache_and_bake_action(label):
    """10 settling frames + original-speed DIAG motion, copied into FIT_<label>.
    Records proxy targets BEFORE keying helpers. Output NPZ contains rig-space
    proxy points for every frame, rigid transforms and per-patch approximation errors.
    Rendering, actual garment error/collision tests and FBX export are root-owned.
    """
    if label not in LABELS: raise ValueError(label)
    scene, rig, proxy = _scene(); data = _load_checked(rig, proxy)
    saved = _state(rig)
    source = bpy.data.actions['DIAG_' + label]
    name = 'FIT_' + label
    if bpy.data.actions.get(name):
        raise RuntimeError(name + ' already exists; inspect/remove only that generated action before an intentional rebake.')
    helpers = [p['name'] for p in data['patches']]
    # A feedback loop would invalidate captured physics after helper keying.
    feedback = sum(any(proxy.vertex_groups[g.group].name in helpers and g.weight > 1e-8 for g in v.groups) for v in proxy.data.vertices)
    if feedback:
        raise RuntimeError('Cloth proxy is weighted to fitted helpers; refused feedback loop.')
    cloth = next(m for m in proxy.modifiers if m.type == 'CLOTH')
    cache = cloth.point_cache
    old_cache_range = (cache.frame_start, cache.frame_end)
    old_cloth_view = cloth.show_viewport
    action = None
    report = {'label': label, 'status': 'RUNNING', 'source_action': source.name,
              'source_range': list(source.frame_range), 'settle_frames': 10,
              'fps': scene.render.fps / scene.render.fps_base,
              'timing': 'Original action speed retained; source first frame maps to 11. Helper settling is keyed on frames 1..10.',
              'limitations': ['Patch error is not final rendered garment error.',
                             'Collision and FBX roundtrip validation are separate tasks.',
                             'Cloth remains a clip bake, not Unity runtime simulation.']}
    try:
        _neutral(rig)
        action = source.copy(); action.name = name
        source_start, source_end = source.frame_range
        if abs(source_start - round(source_start)) > 1e-5 or abs(source_end - round(source_end)) > 1e-5:
            raise RuntimeError('Non-integer source action bounds require an explicit sampling policy.')
        offset = 11 - source_start
        end = int(source_end + offset)
        collections = _curve_collections(action)
        if not collections: raise RuntimeError('No accessible source FCurves.')
        for curves in collections:
            for curve in list(curves):
                match = re.search(r'pose\.bones\["([^"]+)"\]', curve.data_path)
                if match and match.group(1) in helpers:
                    curves.remove(curve); continue
                for key in curve.keyframe_points:
                    key.co.x += offset; key.handle_left.x += offset; key.handle_right.x += offset
                curve.extrapolation = 'CONSTANT'
                for modifier in curve.modifiers:
                    if modifier.type == 'CYCLES': modifier.mute = True
                curve.update()
        _assign(rig, action)
        if cache.is_baked:
            with bpy.context.temp_override(object=proxy, active_object=proxy, point_cache=cache):
                bpy.ops.ptcache.free_bake()
        if cache.is_baked: raise RuntimeError('Could not release stale cloth cache.')
        cloth.show_viewport = True; cache.frame_start = 1; cache.frame_end = end
        scene.frame_set(0)
        dg = bpy.context.evaluated_depsgraph_get()
        rest = np.asarray(data['rest_proxy_rig_space'], dtype=np.float64)
        targets = []; transforms = []; rms_rows = []; max_rows = []
        for frame in range(1, end + 1):
            scene.frame_set(frame); dg.update()
            obj = proxy.evaluated_get(dg); mesh = obj.to_mesh()
            try:
                transform = rig.matrix_world.inverted_safe() @ obj.matrix_world
                points = np.asarray([transform @ v.co for v in mesh.vertices], dtype=np.float64)
            finally:
                obj.to_mesh_clear()
            if points.shape != rest.shape or not np.isfinite(points).all():
                raise RuntimeError(f'Invalid proxy topology/coordinates at frame {frame}.')
            matrices = []; rms_values = []; max_values = []
            for patch in data['patches']:
                ids = patch['indices']
                matrix, rms, maximum = _fit_rigid(rest[ids], points[ids], patch['weights'])
                matrices.append(matrix); rms_values.append(rms); max_values.append(maximum)
            targets.append(points.astype(np.float32)); transforms.append(matrices)
            rms_rows.append(rms_values); max_rows.append(max_values)
        transforms = np.asarray(transforms)
        OUT.mkdir(parents=True, exist_ok=True)
        npz = OUT / ('secondary_targets_' + label + '.npz')
        np.savez_compressed(npz, frames=np.arange(1, end + 1), proxy_rest=rest,
                            proxy_targets=np.asarray(targets), rigid_transforms=transforms,
                            patch_rms=np.asarray(rms_rows), patch_max=np.asarray(max_rows),
                            helper_names=np.asarray(helpers))
        # Re-evaluate only skeleton while keying cached transforms; do not rerun cloth.
        cloth.show_viewport = False
        previous_quaternions = {}
        for index, frame in enumerate(range(1, end + 1)):
            scene.frame_set(frame); bpy.context.view_layer.update()
            for h, patch in enumerate(data['patches']):
                bone = rig.pose.bones[patch['name']]
                bone.rotation_mode = 'QUATERNION'
                bone.matrix = Matrix(transforms[index, h].tolist()) @ rig.data.bones[patch['name']].matrix_local
                bone.scale = (1, 1, 1)
                q = bone.rotation_quaternion.copy()
                if patch['name'] in previous_quaternions and q.dot(previous_quaternions[patch['name']]) < 0:
                    q.negate(); bone.rotation_quaternion = q
                previous_quaternions[patch['name']] = q
                bone.keyframe_insert(data_path='location', frame=frame, group='Secondary_' + patch['kind'])
                bone.keyframe_insert(data_path='rotation_quaternion', frame=frame, group='Secondary_' + patch['kind'])
                bone.keyframe_insert(data_path='scale', frame=frame, group='Secondary_' + patch['kind'])
        for curves in _curve_collections(action):
            for curve in curves:
                match = re.search(r'pose\.bones\["([^"]+)"\]', curve.data_path)
                if match and match.group(1) in helpers:
                    for key in curve.keyframe_points: key.interpolation = 'LINEAR'
        action.use_fake_user = True
        action['secondary_bake'] = 'Cloth proxy weighted rigid patches; original-speed diagnostic only'
        action['motion_first_frame'] = 11; action['settling_frames'] = 10
        report.update({'status': 'BAKED_REQUIRES_GARMENT_AND_FBX_VALIDATION',
                       'output_action': action.name, 'output_range': [1, end], 'motion_range': [11, end],
                       'target_cache': str(npz), 'helpers': len(helpers),
                       'max_patch_weighted_rms_m': float(np.max(rms_rows)),
                       'max_patch_point_residual_m': float(np.max(max_rows)),
                       'mean_patch_weighted_rms_m': float(np.mean(rms_rows))})
    except Exception as exc:
        report.update({'status': 'INCOMPLETE', 'error': repr(exc)})
        if action is not None:
            action.name = 'INCOMPLETE_' + name
            action.use_fake_user = True
        raise
    finally:
        cloth.show_viewport = old_cloth_view
        cache.frame_start, cache.frame_end = old_cache_range
        _restore(rig, saved)
        # Returning to the previous time replaces the transient preview cache,
        # but the authoritative captured targets are retained in NPZ.
        scene.frame_set(0)
        scene.frame_set(saved['frame'], subframe=saved['subframe'])
        _restore(rig, saved)
        _write('secondary_bake_' + label + '.json', report)
    print(json.dumps(report, ensure_ascii=False)); return report
