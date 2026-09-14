"""Refit ONLY the existing three-island regular cloth proxy to the revised clothes.

Definitions only. Load, then call refit_proxy(). Rebuild secondary bone definitions
and weights afterwards; previous proxy-based binds are obsolete after this operation.

Optional bounded diagnostic:
  probe_stability('ArmsUp', motion_frames=24, physics=False)
  probe_stability('ArmsUp', motion_frames=24, physics=True)

No Body/InnerTop/Durumagi geometry, core bones, source actions or Surface Deform
binds are edited. The dedicated collision collection and proxy settings are edited.
No .blend is automatically saved. No rendering or FBX export is performed.
"""
import bpy, json, math, importlib.util
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Art/PlayerPhase1/FitRigV3'

def _h():
    spec = importlib.util.spec_from_file_location('_fitrig_secondary_helpers', ROOT / 'Tools/Blender/PlayerPhase1/FitRigV3/bake_secondary.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module

def _write(name, report):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_text(json.dumps(report, ensure_ascii=False, indent=2, default=lambda v:v.item() if isinstance(v,np.generic) else str(v)), encoding='utf-8')

def _raw_points(obj, rig):
    matrix = rig.matrix_world.inverted_safe() @ obj.matrix_world
    return np.asarray([matrix @ v.co for v in obj.data.vertices], dtype=np.float64)

def _tree(obj, rig, evaluated=False):
    target = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()) if evaluated else obj
    mesh = target.to_mesh() if evaluated else target.data
    matrix = rig.matrix_world.inverted_safe() @ target.matrix_world
    try:
        mesh.calc_loop_triangles()
        return BVHTree.FromPolygons([matrix @ v.co for v in mesh.vertices],
                                   [tuple(t.vertices) for t in mesh.loop_triangles], all_triangles=True)
    finally:
        if evaluated: target.to_mesh_clear()

def _free_cache(proxy, cloth):
    cache = cloth.point_cache
    if cache.is_baked:
        with bpy.context.temp_override(object=proxy, active_object=proxy, point_cache=cache):
            bpy.ops.ptcache.free_bake()
    if cache.is_baked:
        raise RuntimeError('Baked cloth cache could not be freed; geometry was not refitted.')
    bpy.context.scene.frame_set(0)

def _islands(proxy, points, h):
    groups = [g for g in h._components(proxy.data) if len(g) > 50]
    if len(groups) != 3 or len(points) != 1707:
        raise RuntimeError('Expected unchanged 1707-vertex / three-island proxy.')
    torso = min(groups, key=lambda ids: float(points[ids, 2].min()))
    sleeves = [g for g in groups if g is not torso]
    return {'torso': torso,
            'Left': max(sleeves, key=lambda ids: float(points[ids, 0].mean())),
            'Right': min(sleeves, key=lambda ids: float(points[ids, 0].mean()))}

def _set_if_present(owner, name, value, report):
    if hasattr(owner, name):
        try:
            setattr(owner, name, value)
            actual = getattr(owner, name)
            report[name] = actual.name if hasattr(actual, 'name') else actual
            return True
        except (TypeError, ValueError, AttributeError) as exc:
            report[name] = {'not_set': repr(exc)}
    else:
        report[name] = 'UNAVAILABLE_IN_THIS_BLENDER'
    return False

def refit_proxy():
    h = _h(); scene, rig, proxy = h._scene(); saved = h._state(rig)
    body = bpy.data.objects['Body']; top = bpy.data.objects['InnerTop']; robe = bpy.data.objects['Durumagi']
    collider = bpy.data.objects['BodyCollisionProxy']
    cloth = next(m for m in proxy.modifiers if m.type == 'CLOTH')
    original = _raw_points(proxy, rig); points = original.copy()
    islands = _islands(proxy, points, h)
    old_topology = (len(proxy.data.vertices), len(proxy.data.edges), len(proxy.data.polygons))
    report = {'status': 'REFITTING', 'sleeves': [], 'settings': {},
              'surface_deform_rebind_required': True,
              'secondary_patch_definition_rebuild_required': True}
    try:
        _free_cache(proxy, cloth)
        h._neutral(rig)
        bpy.context.view_layer.update()
        top_points = _raw_points(top, rig); robe_points = _raw_points(robe, rig)
        body_tree = _tree(body, rig)
        collision_tree = _tree(collider, rig, evaluated=True)
        inner_tree = _tree(top, rig)
        pin = proxy.vertex_groups.get('SimPin') or proxy.vertex_groups.new(name='SimPin')
        pinvalues = np.zeros(len(points), dtype=float)
        for side in ('Left', 'Right'):
            ids = islands[side]; sign = 1 if side == 'Left' else -1
            # Existing rings have identical X coordinates; retain their vertex order/topology.
            rings = {}
            for i in ids:
                rings.setdefault(round(abs(float(points[i, 0])), 5), []).append(i)
            ordered = sorted(rings.items())
            if len(ordered) != 15 or any(len(v) != 20 for x, v in ordered):
                raise RuntimeError(f'{side}: expected 15 rings of 20 vertices; refused a topology guess.')
            arm = rig.data.bones[side + 'Arm']
            head = np.array(arm.head_local); tail = np.array(arm.tail_local)
            ring_data = []
            for j, (ax, ring_ids) in enumerate(ordered):
                x = sign * ax
                alpha = (x - head[0]) / (tail[0] - head[0])
                axis_center = head + (tail - head) * alpha
                def section(cloud):
                    dx = np.abs(cloud[:, 0] - x)
                    radial = np.linalg.norm(cloud[:, 1:3] - axis_center[1:3], axis=1)
                    return cloud[(dx < .026) & (cloud[:, 2] > 1.13) & (radial < .265)]
                outer = section(robe_points); inner = section(top_points)
                if len(outer) < 12:
                    raise RuntimeError(f'{side} ring {j}: insufficient revised robe samples ({len(outer)}).')
                ylo, yhi = np.quantile(outer[:, 1], [.05, .95])
                zlo, zhi = np.quantile(outer[:, 2], [.05, .95])
                if len(inner) >= 12:
                    iylo, iyhi = np.quantile(inner[:, 1], [.08, .92])
                    izlo, izhi = np.quantile(inner[:, 2], [.08, .92])
                    ylo = min(ylo, iylo - .006); yhi = max(yhi, iyhi + .006)
                    zlo = min(zlo, izlo - .006); zhi = max(zhi, izhi + .006)
                cy, cz = (ylo + yhi) / 2, (zlo + zhi) / 2
                ry, rz = max(.038, (yhi - ylo) / 2), max(.040, (zhi - zlo) / 2)
                ring_data.append([cy, cz, ry, rz])
            ring_data = np.asarray(ring_data)
            smoothed = ring_data.copy()
            for j in range(1, len(ordered) - 1):
                smoothed[j] = .25 * ring_data[j - 1] + .5 * ring_data[j] + .25 * ring_data[j + 1]
            for j, ((ax, ring_ids), (cy, cz, ry, rz)) in enumerate(zip(ordered, smoothed)):
                for k, index in enumerate(sorted(ring_ids)):
                    angle = 2 * math.pi * k / 20
                    points[index] = (sign * ax, cy + ry * math.cos(angle), cz + rz * math.sin(angle))
                    root_pin = 1 - h._smooth(0, 2, j)
                    seam_pin = h._smooth(.45, .85, math.sin(angle))
                    pinvalues[index] = max(root_pin, seam_pin)
            report['sleeves'].append({'side': side, 'rings': len(ordered),
                'maximum_center_shift_m': float(np.max(np.linalg.norm(smoothed[:, :2] - ring_data[:, :2], axis=1))),
                'median_y_radius_m': float(np.median(smoothed[:, 2])),
                'median_z_radius_m': float(np.median(smoothed[:, 3]))})
        moved_torso = 0; ray_misses = 0
        for index in islands['torso']:
            x, y, z = points[index]
            pinvalues[index] = 1 if z >= 1.065 else (.55 if z > 1.02 else 0)
            if z < 1.0:
                continue
            # Radial exit from the body keeps each horizontal torso row level.
            radial = Vector((x, y, 0))
            radius = radial.length
            if radius < .015: continue
            radial.normalize(); origin = Vector((0, 0, z))
            required = radius
            hits = 0
            for tree, gap in ((body_tree, .012), (collision_tree, .012), (inner_tree, .006)):
                co, normal, face, distance = tree.ray_cast(origin, radial, 1.0)
                if co is not None:
                    hits += 1
                    projected = (co - origin).dot(radial)
                    if 0 < projected < .40:
                        required = max(required, projected + gap)
            if not hits: ray_misses += 1
            delta = min(.08, max(0.0, required - radius))
            if delta > 0:
                points[index] += np.asarray(radial) * delta; moved_torso += 1
        # Final local clearance for pinned points; strict fixed-point collision exclusion
        # below prevents immovable anchors fighting the contact solver.
        nudged = 0
        for index in np.flatnonzero(pinvalues >= .999):
            p = Vector(points[index])
            for tree in (body_tree, collision_tree):
                co, normal, face, distance = tree.find_nearest(p)
                if co is not None and distance < .035:
                    signed = (p - co).dot(normal)
                    if signed < .009:
                        delta = normal * min(.025, .009 - signed)
                        # Do not recreate a raised shoulder cap while making the driver safe.
                        delta.z = max(-.005, min(.005, delta.z))
                        p += delta; nudged += 1
            points[index] = p
        if not np.isfinite(points).all():
            raise RuntimeError('Nonfinite refit result; refused mesh write.')
        inverse = (rig.matrix_world.inverted_safe() @ proxy.matrix_world).inverted_safe()
        for v in proxy.data.vertices:
            v.co = inverse @ Vector(points[v.index])
        for v in proxy.data.vertices:
            if pinvalues[v.index] > 0:
                pin.add([v.index], float(pinvalues[v.index]), 'REPLACE')
            else:
                pin.remove([v.index])
        proxy.data.update()
        exclude = proxy.vertex_groups.get('SimCollisionExclude') or proxy.vertex_groups.new(name='SimCollisionExclude')
        exclude.remove(list(range(len(points))))
        full_pins = np.flatnonzero(pinvalues >= .999).tolist()
        if full_pins: exclude.add(full_pins, 1.0, 'REPLACE')
        cs = cloth.settings; cc = cloth.collision_settings
        cs.vertex_group_mass = 'SimPin'; cs.quality = 12
        cs.use_dynamic_mesh = False  # fixed material rest lengths; only anchor positions animate
        cs.effector_weights.gravity = 1.0
        scene.gravity = (0, 0, -9.81)
        cc.use_collision = True; cc.distance_min = .004; cc.collision_quality = 8
        cc.use_self_collision = False
        _set_if_present(cc, 'impulse_clamp', .5, report['settings'])
        collection = bpy.data.collections.get('FitRigV3_ClothColliders')
        if collection is None:
            collection = bpy.data.collections.new('FitRigV3_ClothColliders')
            scene.collection.children.link(collection)
        for other in list(collection.objects):
            if other != collider: collection.objects.unlink(other)
        if collider.name not in collection.objects: collection.objects.link(collider)
        _set_if_present(cc, 'collection', collection, report['settings'])
        # Verify RNA wording rather than guessing whether the vertex group means
        # inclusion or exclusion in this Blender version.
        property_name = 'vertex_group_object_collisions'
        prop = cc.bl_rna.properties.get(property_name)
        description = prop.description if prop else ''
        report['settings']['collision_group_rna_description'] = description
        if prop and any(word in description.lower() for word in ('not used', 'exclude', 'excluded')):
            _set_if_present(cc, property_name, 'SimCollisionExclude', report['settings'])
        else:
            report['settings'][property_name] = 'NOT_ASSIGNED: exclusion semantics unconfirmed; inspect RNA/UI before use.'
        if collider.collision:
            collider.collision.thickness_outer = .004
            collider.collision.thickness_inner = .001
        modifiers = [m for m in proxy.modifiers]
        order = [m.type for m in modifiers]
        if 'ARMATURE' not in order or order.index('ARMATURE') > order.index('CLOTH'):
            raise RuntimeError('Expected Armature before Cloth; modifier order was not automatically rewritten.')
        report.update({'status': 'REFITTED_REQUIRES_STABILITY_TEST',
            'topology_before': old_topology,
            'topology_after': (len(proxy.data.vertices), len(proxy.data.edges), len(proxy.data.polygons)),
            'max_proxy_rest_displacement_m': float(np.linalg.norm(points - original, axis=1).max()),
            'torso_vertices_expanded': moved_torso, 'torso_ray_misses': ray_misses,
            'pinned_local_clearance_nudges': nudged, 'fully_pinned_vertices': len(full_pins),
            'collision_distance_m': .004, 'collider_outer_thickness_m': .004,
            'effective_nominal_contact_separation_m': .008,
            'dynamic_mesh': False, 'cloth_quality': 12, 'collision_quality': 8,
            'proxy_modifier_order': order,
            'note': 'Fixed anchors excluded only when RNA confirms group exclusion. Signed-normal clearance is approximate; run rest/no-cloth/physics diagnostics.'})
        cloth.point_cache.frame_start = 1; cloth.point_cache.frame_end = 250
        scene.frame_set(0)
        _write('proxy_refit_report.json', report)
        print(json.dumps(report, ensure_ascii=False,default=lambda v:v.item() if isinstance(v,np.generic) else str(v))); return report
    finally:
        h._restore(rig, saved)

def probe_stability(label='ArmsUp', motion_frames=24, physics=True):
    """No renders. Ten settling frames, then the first N original-speed motion frames.
    Detects proxy-edge growth and particle jumps, not a proof of bone or cloth quality.
    Stops early on clear divergence and leaves a numeric report.
    """
    h = _h(); scene, rig, proxy = h._scene(); saved = h._state(rig)
    cloth = next(m for m in proxy.modifiers if m.type == 'CLOTH')
    old_view = cloth.show_viewport; old_range = (cloth.point_cache.frame_start, cloth.point_cache.frame_end)
    source = bpy.data.actions['DIAG_' + label]
    action = None; report = {'label': label, 'physics': bool(physics), 'status': 'RUNNING', 'rows': [],
        'thresholds': {'max_edge_stretch_ratio': 3.0, 'max_particle_step_m': .30, 'max_rest_displacement_m': 2.0},
        'limitations': 'Proxy edge strain/particle diagnostics; not final garment collision or rig-pass proof.'}
    try:
        _free_cache(proxy, cloth); h._neutral(rig)
        action = source.copy(); action.name = '__PROXY_STABILITY_' + label
        start, source_end = source.frame_range
        offset = 11 - start
        count = min(int(motion_frames), int(source_end - start + 1)); end = 10 + count
        if count < 1: raise ValueError('motion_frames must be positive.')
        for curves in h._curve_collections(action):
            for curve in curves:
                for key in curve.keyframe_points:
                    key.co.x += offset; key.handle_left.x += offset; key.handle_right.x += offset
                curve.extrapolation = 'CONSTANT'
                for modifier in curve.modifiers:
                    if modifier.type == 'CYCLES': modifier.mute = True
                curve.update()
        h._assign(rig, action)
        cloth.show_viewport = bool(physics)
        cloth.point_cache.frame_start = 1; cloth.point_cache.frame_end = end
        rest = _raw_points(proxy, rig)
        edges = np.asarray([tuple(e.vertices) for e in proxy.data.edges], dtype=int)
        baseline = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
        valid = baseline > .003
        edges = edges[valid]; baseline = baseline[valid]
        previous = None; dg = bpy.context.evaluated_depsgraph_get()
        for frame in range(1, end + 1):
            scene.frame_set(frame); dg.update()
            obj = proxy.evaluated_get(dg); mesh = obj.to_mesh()
            matrix = rig.matrix_world.inverted_safe() @ obj.matrix_world
            try: points = np.asarray([matrix @ v.co for v in mesh.vertices])
            finally: obj.to_mesh_clear()
            finite = points.shape == rest.shape and np.isfinite(points).all()
            if finite:
                ratios = np.linalg.norm(points[edges[:, 0]] - points[edges[:, 1]], axis=1) / baseline
                step = float(np.linalg.norm(points - previous, axis=1).max()) if previous is not None else 0
                displacement = float(np.linalg.norm(points - rest, axis=1).max())
                maximum = float(ratios.max())
            else:
                maximum = step = displacement = None
            divergent = not finite or maximum > 3 or step > .30 or displacement > 2.0
            row = {'frame': frame, 'phase': 'settle' if frame <= 10 else 'motion',
                   'finite': finite, 'max_edge_stretch_ratio': maximum,
                   'max_particle_step_m': step, 'max_rest_displacement_m': displacement,
                   'divergence': divergent}
            report['rows'].append(row)
            if divergent:
                report['status'] = 'DIVERGENCE_DETECTED'; break
            previous = points.copy()
        if report['status'] == 'RUNNING': report['status'] = 'NO_GROSS_DIVERGENCE_IN_SAMPLED_FRAMES'
        report['motion_frames_requested'] = count
        report['simulation_frames_completed'] = len(report['rows'])
    except Exception as exc:
        report.update({'status': 'INCOMPLETE', 'error': repr(exc)})
    finally:
        cloth.show_viewport = old_view
        cloth.point_cache.frame_start, cloth.point_cache.frame_end = old_range
        h._restore(rig, saved)
        if action is not None: bpy.data.actions.remove(action)
        scene.frame_set(0)
        _write('proxy_stability_' + label + ('_physics' if physics else '_armature') + '.json', report)
    print(json.dumps({k: v for k, v in report.items() if k != 'rows'}, ensure_ascii=False)); return report
