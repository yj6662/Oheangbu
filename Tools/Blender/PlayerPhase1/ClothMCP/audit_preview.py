"""Read-only geometry audit + preview renders. Load definitions, then call one function.

run_action('Walk') / Run / ArmsUp / Squat / BrushSwing
static_report() renders neutral front/side after ten sequential settling frames.
No production actions, meshes, rig weights, or modifier definitions are edited.
Temporary action/reference objects are removed. Cloth cache is intentionally reset.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ClothMCP')
LABELS = ('Walk', 'Run', 'ArmsUp', 'Squat', 'BrushSwing')

def _write(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def _objects():
    return (bpy.data.objects['Dosa_Phase1_Rig'],
            bpy.data.objects['ClothProxy_Durumagi'], bpy.data.objects['Body'])

def _assign(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action and hasattr(action, 'slots') and len(action.slots):
        slots = [s for s in action.slots if getattr(s, 'target_id_type', 'OBJECT') == 'OBJECT']
        slot = next((s for s in slots if rig.name in s.identifier), slots[0] if slots else action.slots[0])
        rig.animation_data.action_slot = slot

def _curves(action):
    result = []
    if hasattr(action, 'fcurves'):
        result.extend(action.fcurves)
    if not result and hasattr(action, 'layers'):
        for layer in action.layers:
            for strip in layer.strips:
                for bag in getattr(strip, 'channelbags', []):
                    result.extend(bag.fcurves)
    return result

def _prepare_action(source):
    action = source.copy()
    action.name = '__AUDIT_ONLY_' + source.name
    start, end = source.frame_range
    scale = 35.0 / max(1.0, end - start)
    curves = _curves(action)
    if not curves:
        bpy.data.actions.remove(action)
        raise RuntimeError('No accessible action curves: cannot validate action playback.')
    for curve in curves:
        for key in curve.keyframe_points:
            for handle in (key.co, key.handle_left, key.handle_right):
                handle.x = 11.0 + (handle.x - start) * scale
        curve.extrapolation = 'CONSTANT'
        curve.update()
    return action

def _cache_reset(proxy, end=46):
    cloth = next(m for m in proxy.modifiers if m.type == 'CLOTH')
    cache = cloth.point_cache
    if cache.is_baked:
        with bpy.context.temp_override(object=proxy, active_object=proxy, point_cache=cache):
            bpy.ops.ptcache.free_bake()
    if cache.is_baked:
        raise RuntimeError('Cloth cache is still baked; refused to audit stale simulation.')
    cache.frame_start = 1
    cache.frame_end = end
    bpy.context.scene.frame_set(0)
    return cloth

def _pin_reference(proxy):
    ref = proxy.copy()
    ref.name = '__AUDIT_PIN_REFERENCE'
    ref.data = proxy.data  # mesh is never edited
    bpy.context.scene.collection.objects.link(ref)
    ref.hide_render = True
    ref.hide_viewport = False
    after = False
    for mod in list(ref.modifiers):
        if mod.type == 'CLOTH':
            after = True
        if after:
            ref.modifiers.remove(mod)
    return ref

def _world_vertices(obj, dg):
    evaluated = obj.evaluated_get(dg)
    mesh = evaluated.to_mesh()
    try:
        return [evaluated.matrix_world @ v.co for v in mesh.vertices]
    finally:
        evaluated.to_mesh_clear()

def _body_tree(body, dg):
    obj = body.evaluated_get(dg)
    mesh = obj.to_mesh()
    try:
        mesh.calc_loop_triangles()
        return BVHTree.FromPolygons([obj.matrix_world @ v.co for v in mesh.vertices],
            [tuple(t.vertices) for t in mesh.loop_triangles], all_triangles=True)
    finally:
        obj.to_mesh_clear()

def _inside_candidates(points, tree):
    depths = []
    for p in points:
        if not all(math.isfinite(c) for c in p):
            continue
        co, normal, index, distance = tree.find_nearest(p)
        if co is not None and distance < .1:
            signed = (p - co).dot(normal)
            if signed < -.002:
                depths.append(-signed)
    return {'candidate_vertices': len(depths), 'max_candidate_depth_m': max(depths, default=0)}

def _render(filename, position=(2.4, -4.5, 2.1)):
    scene = bpy.context.scene
    camera = scene.camera
    if camera is None:
        return {'status': 'UNVERIFIED', 'reason': 'No scene camera'}
    folder = OUT / 'Previews'
    folder.mkdir(parents=True, exist_ok=True)
    previous = (camera.matrix_world.copy(), camera.data.type, camera.data.ortho_scale,
                scene.render.resolution_x, scene.render.resolution_y,
                scene.render.resolution_percentage, scene.render.filepath,
                scene.render.image_settings.file_format)
    old_samples = scene.cycles.samples if scene.render.engine == 'CYCLES' else None
    try:
        camera.location = position
        camera.rotation_euler = (Vector((0, 0, 1.0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera.data.type = 'ORTHO'
        camera.data.ortho_scale = 3.65
        scene.render.resolution_x = 960
        scene.render.resolution_y = 720
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        if old_samples is not None:
            scene.cycles.samples = 8
        scene.render.filepath = str(folder / filename)
        bpy.ops.render.render(write_still=True)
        return {'status': 'RENDERED', 'path': scene.render.filepath, 'frame': scene.frame_current}
    finally:
        (camera.matrix_world, camera.data.type, camera.data.ortho_scale,
         scene.render.resolution_x, scene.render.resolution_y,
         scene.render.resolution_percentage, scene.render.filepath,
         scene.render.image_settings.file_format) = previous
        if old_samples is not None:
            scene.cycles.samples = old_samples

def run_action(label):
    """10 held source-start frames, then 36 sequential samples of one diagnostic.
    Source motion is time-remapped to 36 samples: this is NOT original-speed validation.
    """
    if label not in LABELS:
        raise ValueError(label)
    rig, proxy, body = _objects()
    scene = bpy.context.scene
    old_action = rig.animation_data.action if rig.animation_data else None
    old_slot = getattr(rig.animation_data, 'action_slot', None) if rig.animation_data else None
    old_matrices = {p.name: p.matrix_basis.copy() for p in rig.pose.bones}
    nla_states = [(track, track.mute) for track in rig.animation_data.nla_tracks] if rig.animation_data else []
    source = bpy.data.actions['DIAG_' + label]
    action = ref = None
    report = {'label': label, 'status': 'RUNNING', 'source_action': source.name,
        'source_frame_range': list(source.frame_range), 'settling_frames': 10,
        'sample_frames': 36, 'scene_fps': scene.render.fps / scene.render.fps_base,
        'timing': 'Source range resampled to frames 11..46; frames 1..10 hold first pose.',
        'penetration_method': 'Nearest actual Body surface normal, signed distance < -2mm and distance <100mm. Candidates only; not watertight or triangle-intersection proof.',
        'rows': []}
    try:
        for track, mute in nla_states:
            track.mute = True
        for p in rig.pose.bones:
            p.matrix_basis = Matrix.Identity(4)
        action = _prepare_action(source)
        _assign(rig, action)
        cloth = _cache_reset(proxy)
        ref = _pin_reference(proxy)
        pin = proxy.vertex_groups.get('SimPin')
        pinned = [] if pin is None else [v.index for v in proxy.data.vertices
            if any(g.group == pin.index and g.weight >= .999 for g in v.groups)]
        report['fully_pinned_vertices'] = len(pinned)
        report['pin_group_configured'] = cloth.settings.vertex_group_mass == 'SimPin'
        report['proxy_gravity_weight'] = cloth.settings.effector_weights.gravity
        report['scene_gravity'] = list(scene.gravity)
        dg = bpy.context.evaluated_depsgraph_get()
        for frame in range(1, 47):
            scene.frame_set(frame)
            dg.update()
            if frame <= 10:
                continue
            points = _world_vertices(proxy, dg)
            expected = _world_vertices(ref, dg)
            finite = all(all(math.isfinite(c) for c in p) for p in points)
            pin_error = max(((points[i] - expected[i]).length for i in pinned
                if i < len(points) and i < len(expected)), default=0)
            tree = _body_tree(body, dg)
            row = {'frame': frame, 'finite_coordinates': finite,
                   'max_fully_pinned_error_m': pin_error,
                   'max_bone_scale_error': max((abs(c - 1) for p in rig.pose.bones for c in p.scale), default=0),
                   'proxy_body_candidates': _inside_candidates(points, tree)}
            if frame in (11, 17, 23, 29, 35, 41, 46):
                row['render_robe_body_candidates'] = _inside_candidates(_world_vertices(bpy.data.objects['Durumagi'], dg), tree)
            report['rows'].append(row)
            if frame == 29:
                report['representative_render'] = _render(label + '.png')
        report['finite_coordinates'] = all(r['finite_coordinates'] for r in report['rows'])
        report['max_fully_pinned_error_m'] = max(r['max_fully_pinned_error_m'] for r in report['rows'])
        report['max_bone_scale_error'] = max(r['max_bone_scale_error'] for r in report['rows'])
        report['max_proxy_body_candidates'] = max(r['proxy_body_candidates']['candidate_vertices'] for r in report['rows'])
        report['status'] = 'MEASURED_NOT_RIG_PASS'
    except Exception as exc:
        report['status'] = 'INCOMPLETE'
        report['error'] = repr(exc)
    finally:
        if ref is not None:
            bpy.data.objects.remove(ref, do_unlink=True)
        _assign(rig, old_action)
        if old_action is not None and old_slot is not None:
            rig.animation_data.action_slot = old_slot
        for p in rig.pose.bones:
            p.matrix_basis = old_matrices[p.name]
        for track, mute in nla_states:
            track.mute = mute
        if action is not None:
            bpy.data.actions.remove(action)
        _write('audit_' + label + '.json', report)
    print(json.dumps({k: v for k, v in report.items() if k != 'rows'}, ensure_ascii=False))
    return report

def static_report():
    rig, proxy, body = _objects()
    scene = bpy.context.scene
    old_action = rig.animation_data.action if rig.animation_data else None
    old_slot = getattr(rig.animation_data, 'action_slot', None) if rig.animation_data else None
    matrices = {p.name: p.matrix_basis.copy() for p in rig.pose.bones}
    nla_states = [(track, track.mute) for track in rig.animation_data.nla_tracks] if rig.animation_data else []
    report = {'status': 'MEASURED_NOT_RIG_PASS', 'meshes': [], 'renders': [],
              'limits': ['No self-collision proof', 'No production-speed motion pass',
                         'No Unity/runtime verification', 'Render inspection must be performed separately']}
    try:
        for track, mute in nla_states:
            track.mute = True
        _assign(rig, None)
        for p in rig.pose.bones:
            p.matrix_basis = Matrix.Identity(4)
        _cache_reset(proxy, 46)
        dg = bpy.context.evaluated_depsgraph_get()
        for frame in range(1, 11):
            scene.frame_set(frame)
            dg.update()
        for name in ('Body', 'InnerTop', 'Durumagi', 'ClothProxy_Durumagi', 'BodyCollisionProxy'):
            obj = bpy.data.objects.get(name)
            if obj is None:
                report['meshes'].append({'name': name, 'status': 'MISSING'})
                continue
            obj.data.calc_loop_triangles()
            evaluated = obj.evaluated_get(dg)
            mesh = evaluated.to_mesh()
            try:
                mesh.calc_loop_triangles()
                report['meshes'].append({'name': name, 'vertices': len(obj.data.vertices),
                    'base_triangles': len(obj.data.loop_triangles), 'evaluated_triangles': len(mesh.loop_triangles),
                    'modifier_order': [{'name': m.name, 'type': m.type,
                                        'viewport': m.show_viewport, 'render': m.show_render} for m in obj.modifiers],
                    'finite_coordinates': all(math.isfinite(c) for v in mesh.vertices for c in v.co)})
            finally:
                evaluated.to_mesh_clear()
        report['render_character_triangles'] = sum(m.get('evaluated_triangles', 0) for m in report['meshes'] if m['name'] in ('Body', 'InnerTop', 'Durumagi'))
        report['renders'].append(_render('Rest_front.png', (0, -4, 1.1)))
        report['renders'].append(_render('Rest_side.png', (4, 0, 1.1)))
    except Exception as exc:
        report['status'] = 'INCOMPLETE'
        report['error'] = repr(exc)
    finally:
        _assign(rig, old_action)
        if old_action is not None and old_slot is not None:
            rig.animation_data.action_slot = old_slot
        for p in rig.pose.bones:
            p.matrix_basis = matrices[p.name]
        for track, mute in nla_states:
            track.mute = mute
        _write('audit_static.json', report)
    print(json.dumps(report, ensure_ascii=False))
    return report
