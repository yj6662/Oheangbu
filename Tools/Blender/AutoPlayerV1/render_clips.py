"""Definitions only: async full-frame clip renders in an isolated temporary scene.

start_render_jobs(rig_name, mesh_names, jobs, output_dir, engine='EEVEE')
jobs = [{'action': 'Walk', 'views': ['front', 'side']},
        {'action': 'Attack', 'views': ['front', 'threequarter']}]

Every integer SOURCE frame is rendered, numbered 1..N in each output folder.
Source scene must already be 30 fps: this utility does not retime any animation.
One timer callback performs one bounds sample OR one synchronous PNG render;
start returns immediately, but Blender is busy for the duration of each PNG.
No original geometry, weights, rest skeleton, poses, Action keys or settings edits.
MP4 encoding is a separate, explicitly called ffmpeg function after PNG completion.
"""
import json
import math
import re
import subprocess
import time
import traceback
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


def rc_state():
    return bpy.app.driver_namespace.get('AutoPlayerV1_RenderClips')


def rc_write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    # Windows readers may briefly deny replacing an open progress file.
    # Keep the previous complete JSON intact and retry this atomic step only.
    for attempt in range(11):
        try:
            temporary.replace(path)
            return
        except PermissionError:
            if attempt == 10:
                raise
            time.sleep(.02)


def rc_label(value):
    label = re.sub(r'[^\w.-]+', '_', value, flags=re.UNICODE).strip('._')
    if not label:
        raise ValueError('Empty output label')
    return label


def rc_action_slot(action, explicit=None):
    slots = [s for s in getattr(action, 'slots', ()) if s.target_id_type == 'OBJECT']
    if explicit is not None:
        slots = [s for s in slots if s.identifier == explicit]
    if len(slots) > 1:
        raise ValueError('Ambiguous Object slots: supply slot_identifier for ' + action.name)
    if explicit is not None and not slots:
        raise ValueError('Requested action slot does not exist')
    return slots[0] if slots else None


def rc_assign(rig, job):
    # Reset only the disposable rig so a preceding clip cannot leave unkeyed bones posed.
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    rig.animation_data_create()
    rig.animation_data.action = job['action_data']
    slot = job['action_slot']
    if slot is not None:
        rig.animation_data.action_slot = slot


def rc_make_scene(state, source_scene, source_rig, source_meshes):
    """Clone objects/armature data; share read-only meshes, materials and Action data."""
    scene = bpy.data.scenes.new('AutoPlayerV1_ClipRender')
    state['scene'] = scene
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.render.resolution_percentage = 100
    scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1.0
    scene.render.fps, scene.render.fps_base = 30, 1.0
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'
    scene.render.image_settings.compression = 15
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.render.use_compositing = False
    scene.render.use_sequencer = False
    for attr in ('view_transform', 'look', 'exposure', 'gamma'):
        try:
            setattr(scene.view_settings, attr, getattr(source_scene.view_settings, attr))
        except (AttributeError, TypeError):
            pass
    if state['engine'] == 'WORKBENCH':
        scene.render.engine = 'BLENDER_WORKBENCH'
        scene.display.shading.light = 'STUDIO'
        scene.display.shading.color_type = 'TEXTURE'
        scene.display.shading.show_shadows = True
        scene.display.shading.background_type = 'WORLD'
        scene.display.shading.background_color = (.15, .15, .15)
    else:
        for name in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
            try:
                scene.render.engine = name
                break
            except TypeError:
                continue
        else:
            raise RuntimeError('No EEVEE render engine available')
        eevee = getattr(scene, 'eevee', None)
        for attr in ('taa_render_samples', 'taa_samples'):
            if eevee is not None and hasattr(eevee, attr):
                setattr(eevee, attr, state['samples'])
        if eevee is not None and hasattr(eevee, 'use_raytracing'):
            eevee.use_raytracing = False
    state['engine_actual'] = scene.render.engine
    state['samples_actual'] = {attr: getattr(scene.eevee, attr)
        for attr in ('taa_render_samples', 'taa_samples')
        if hasattr(scene, 'eevee') and hasattr(scene.eevee, attr)}
    originals = set([source_rig] + source_meshes)
    for obj in list(originals):
        parent = obj.parent
        while parent is not None:
            originals.add(parent)
            parent = parent.parent
    mapping = {}
    for original in originals:
        clone = original.copy()
        scene.collection.objects.link(clone)
        state['created_objects'].append(clone)
        if original.type == 'ARMATURE':
            clone.data = original.data.copy()
            state['created_data'].append(('armatures', clone.data))
        clone.hide_render = original not in ([source_rig] + source_meshes)
        clone.hide_viewport = False
        if clone.animation_data:
            for track in clone.animation_data.nla_tracks:
                track.mute = True
        mapping[original] = clone
    for original, clone in mapping.items():
        clone.parent = mapping.get(original.parent)
        clone.matrix_parent_inverse = original.matrix_parent_inverse.copy()
        clone.matrix_basis = original.matrix_basis.copy()
        for modifier in clone.modifiers:
            if modifier.type == 'ARMATURE':
                if modifier.object not in mapping:
                    raise ValueError('Mesh references an external Armature: ' + original.name)
                modifier.object = mapping[modifier.object]
        if len(clone.constraints) or (clone.type == 'ARMATURE' and any(
                len(pb.constraints) for pb in clone.pose.bones)):
            raise ValueError('Constrained object needs explicit dependency cloning: ' + original.name)
    state['rig'], state['meshes'] = mapping[source_rig], [mapping[o] for o in source_meshes]
    camera_data = bpy.data.cameras.new('ClipRenderCamera')
    camera = bpy.data.objects.new('ClipRenderCamera', camera_data)
    scene.collection.objects.link(camera)
    state['created_objects'].append(camera)
    state['created_data'].append(('cameras', camera_data))
    scene.camera = camera
    camera_data.type = 'ORTHO'
    camera_data.clip_start, camera_data.clip_end = .01, 1000.0
    world = bpy.data.worlds.new('ClipRenderWorld')
    state['created_data'].append(('worlds', world))
    world.use_nodes = True
    world.color = (.15, .15, .15)
    background = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    background.inputs[0].default_value = (.18, .18, .18, 1)
    background.inputs[1].default_value = .55
    scene.world = world
    state['lights'] = []
    for label, offset, energy, size in (
            ('Key', (-2, -3, 3), 450, 2.5), ('Fill', (2, -2, 2), 200, 2.5),
            ('Rim', (0, 2, 3), 350, 2.0)):
        light_data = bpy.data.lights.new('ClipRender' + label, 'AREA')
        light_data.shape = 'DISK'
        light = bpy.data.objects.new(light_data.name, light_data)
        scene.collection.objects.link(light)
        state['created_objects'].append(light)
        state['created_data'].append(('lights', light_data))
        state['lights'].append((light, offset, energy, size))


def rc_sample_bounds(state, frame):
    scene = state['scene']
    scene.frame_set(frame)
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        depsgraph = bpy.context.evaluated_depsgraph_get()
        depsgraph.update()
        low, high = np.full(3, np.inf), np.full(3, -np.inf)
        for original in state['meshes']:
            evaluated = original.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            try:
                coords = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
                mesh.vertices.foreach_get('co', coords)
                coords = coords.reshape(-1, 3)
                matrix = np.asarray(evaluated.matrix_world, dtype=np.float64)
                world = coords @ matrix[:3, :3].T + matrix[:3, 3]
                if not len(world) or not np.isfinite(world).all():
                    raise ValueError('Non-finite or empty evaluated mesh at frame ' + str(frame))
                low, high = np.minimum(low, world.min(axis=0)), np.maximum(high, world.max(axis=0))
            finally:
                evaluated.to_mesh_clear()
    return low, high


def rc_frame_camera(state, job):
    low, high = np.asarray(job['bounds_min']), np.asarray(job['bounds_max'])
    center = Vector((low + high) / 2)
    directions = {'front': (0, -1, 0), 'side': (1, 0, 0), 'left': (1, 0, 0),
                  'right': (-1, 0, 0), 'back': (0, 1, 0), 'threequarter': (1, -2, .15)}
    direction = Vector(job.get('camera_direction', directions[job['view']])).normalized()
    if direction.length < .99:
        raise ValueError('Camera direction must be nonzero')
    camera = state['scene'].camera
    diagonal = float(np.linalg.norm(high - low))
    camera.location = center + direction * max(3, diagonal * 3)
    camera.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    rotation = camera.rotation_euler.to_matrix().transposed()
    corners = [rotation @ (Vector((x, y, z)) - center)
               for x in (low[0], high[0]) for y in (low[1], high[1]) for z in (low[2], high[2])]
    width = max(p.x for p in corners) - min(p.x for p in corners)
    height = max(p.y for p in corners) - min(p.y for p in corners)
    # Blender camera ortho_scale is horizontal width in a landscape output.
    camera.data.ortho_scale = max(width, height * (1280 / 720), .1) * state['margin']
    body_height = max(high[2] - low[2], .1)
    scale = body_height / 1.75
    for light, offset, energy, size in state['lights']:
        light.location = center + Vector(offset) * scale
        light.rotation_euler = (center - light.location).to_track_quat('-Z', 'Y').to_euler()
        light.data.energy, light.data.size = energy * scale * scale, size * scale
    job['camera'] = {'location': list(camera.location), 'rotation_euler': list(camera.rotation_euler),
                     'ortho_scale': camera.data.ortho_scale, 'target': list(center),
                     'fixed_for_entire_clip': True, 'bounds_method': 'Evaluated vertices at every integer frame'}


def rc_public_job(job):
    return {k: v for k, v in job.items() if k not in ('action_data', 'action_slot')}


def rc_progress(state):
    report = {'status': state['status'], 'phase': state['phase'], 'job_index': state['job_index'],
              'elapsed_seconds': time.monotonic() - state['started'],
              'engine': state.get('engine_actual'), 'samples_requested': state['samples'],
              'samples_available': state.get('samples_actual'), 'fps': 30,
              'resolution': [1280, 720], 'source_scene': state['source_scene_name'],
              'source_rig': state['source_rig_name'], 'source_geometry_modified': False,
              'jobs': [rc_public_job(job) for job in state['jobs']],
              'error': state.get('error')}
    rc_write_json(Path(state['output_dir']) / 'progress.json', report)
    return report


def rc_cleanup(state):
    for obj in reversed(state.get('created_objects', [])):
        if obj.name in bpy.data.objects:
            bpy.data.objects.remove(obj, do_unlink=True)
    scene = state.get('scene')
    if scene is not None and scene.name in bpy.data.scenes:
        bpy.data.scenes.remove(scene)
    for category, block in reversed(state.get('created_data', [])):
        collection = getattr(bpy.data, category)
        if block.name in collection and block.users == 0:
            collection.remove(block)
    state['scene'] = None
    state['created_objects'], state['created_data'] = [], []


def rc_tick():
    state = rc_state()
    if state is None or state['status'] != 'RUNNING':
        return None
    try:
        if state['job_index'] >= len(state['jobs']):
            state['status'], state['phase'] = 'COMPLETE', 'complete'
            rc_cleanup(state)
            rc_progress(state)
            return None
        job = state['jobs'][state['job_index']]
        if state['phase'] == 'prepare':
            rc_assign(state['rig'], job)
            job['bounds_min'], job['bounds_max'] = [math.inf] * 3, [-math.inf] * 3
            job['sampled_frames'], job['rendered_frames'] = 0, 0
            cache_key = (job['action'], job['action_slot'].identifier if job['action_slot'] else None)
            cached = state['bounds_cache'].get(cache_key)
            if cached:
                job['bounds_min'], job['bounds_max'] = list(cached[0]), list(cached[1])
                job['sampled_frames'] = job['frame_count']
                job['bounds_reused_from_same_action'] = True
                rc_frame_camera(state, job)
                state['phase'], job['status'] = 'render', 'RENDERING'
            else:
                state['phase'], job['status'] = 'bounds', 'SAMPLING_BOUNDS'
        if state['phase'] == 'bounds':
            frame = job['source_frame_start'] + job['sampled_frames']
            low, high = rc_sample_bounds(state, frame)
            job['bounds_min'] = np.minimum(job['bounds_min'], low).tolist()
            job['bounds_max'] = np.maximum(job['bounds_max'], high).tolist()
            job['sampled_frames'] += 1
            if job['sampled_frames'] == job['frame_count']:
                cache_key = (job['action'], job['action_slot'].identifier if job['action_slot'] else None)
                state['bounds_cache'][cache_key] = (list(job['bounds_min']), list(job['bounds_max']))
                rc_frame_camera(state, job)
                state['phase'], job['status'] = 'render', 'RENDERING'
        elif state['phase'] == 'render':
            index = job['rendered_frames'] + 1
            frame = job['source_frame_start'] + index - 1
            scene = state['scene']
            scene.frame_set(frame)
            scene.render.filepath = str(Path(job['frames_dir']) / ('frame_%06d.png' % index))
            with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
                bpy.ops.render.render(write_still=True, scene=scene.name)
            if not Path(scene.render.filepath).is_file():
                raise RuntimeError('PNG was not written: ' + scene.render.filepath)
            job['rendered_frames'] = index
            job['last_source_frame'] = frame
            if index == job['frame_count']:
                job['status'] = 'PNG_COMPLETE_MP4_NOT_ENCODED'
                rc_write_json(Path(job['frames_dir']).parent / 'clip.json', rc_public_job(job))
                state['job_index'] += 1
                state['phase'] = 'prepare'
        rc_progress(state)
        return state['interval']
    except Exception:
        state['status'], state['phase'] = 'FAILED', 'failed'
        state['error'] = traceback.format_exc()
        try:
            rc_cleanup(state)
        finally:
            rc_progress(state)
        return None


def start_render_jobs(rig_name, mesh_names, jobs, output_dir, engine='EEVEE',
                      samples=8, interval=.05, margin=1.12):
    """Schedule jobs; return immediately. Existing output frame sets are protected."""
    previous = rc_state()
    if previous is not None and previous['status'] == 'RUNNING':
        raise RuntimeError('Render jobs already running; call stop_render_jobs first')
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    if abs(fps - 30) > 1e-6:
        raise ValueError('Source scene must already be 30 fps; refusing implicit retiming')
    rig = bpy.data.objects[rig_name]
    if rig.type != 'ARMATURE' or rig.data.pose_position != 'POSE':
        raise ValueError('Expected a rig already in POSE display mode')
    meshes = [bpy.data.objects[name] for name in mesh_names]
    shapes = {p.custom_shape for p in rig.pose.bones if p.custom_shape is not None}
    if not meshes or any(o.type != 'MESH' or o in shapes for o in meshes):
        raise ValueError('Provide actual character meshes, excluding bone display shapes')
    if engine not in ('EEVEE', 'WORKBENCH') or samples < 1 or margin < 1:
        raise ValueError('Invalid engine/samples/margin')
    expanded, labels = [], set()
    output = Path(output_dir).resolve()
    for specification in jobs:
        action = bpy.data.actions[specification['action']]
        slot = rc_action_slot(action, specification.get('slot_identifier'))
        first, last = map(float, action.frame_range)
        if abs(first - round(first)) > 1e-5 or abs(last - round(last)) > 1e-5:
            raise ValueError('Fractional clip endpoints require an explicit sampling policy: ' + action.name)
        first, last = round(first), round(last)
        count = last - first + 1
        if count < 1:
            raise ValueError('Empty action: ' + action.name)
        views = specification.get('views') or ['front', 'threequarter' if 'attack' in action.name.lower() else 'side']
        for view in views:
            if view not in ('front', 'back', 'side', 'left', 'right', 'threequarter'):
                raise ValueError('Unsupported view: ' + view)
            label = rc_label(specification.get('label', action.name) + '_' + view)
            if label in labels:
                raise ValueError('Duplicate job folder: ' + label)
            labels.add(label)
            folder = output / label / 'frames'
            if folder.exists() and any(folder.glob('frame_*.png')):
                raise ValueError('Existing frames protected; select a fresh output directory: ' + str(folder))
            mp4 = output / (label + '.mp4')
            command = ['ffmpeg', '-hide_banner', '-loglevel', 'warning', '-n',
                       '-framerate', '30', '-start_number', '1', '-i', str(folder / 'frame_%06d.png'),
                       '-frames:v', str(count), '-an', '-c:v', 'libx264', '-preset', 'fast',
                       '-crf', '18', '-pix_fmt', 'yuv420p', '-r', '30', '-movflags', '+faststart', str(mp4)]
            expanded.append({'action': action.name, 'action_data': action, 'action_slot': slot,
                             'view': view, 'label': label, 'status': 'QUEUED',
                             'source_frame_start': first, 'source_frame_end': last,
                             'output_frame_start': 1, 'output_frame_end': count, 'frame_count': count,
                             'fps': 30, 'source_key_interval_seconds': (last - first) / 30,
                             'encoded_duration_seconds': count / 30, 'last_sample_hold_seconds': 1 / 30,
                             'timing_note': 'All integer frames inclusive. Last sample has one full 1/30s display interval.',
                             'frames_dir': str(folder), 'mp4_path': str(mp4), 'ffmpeg_argv': command})
    if not expanded:
        raise ValueError('No render jobs')
    state = {'status': 'RUNNING', 'phase': 'prepare', 'job_index': 0, 'jobs': expanded,
             'output_dir': str(output), 'engine': engine, 'samples': int(samples),
             'interval': max(.01, float(interval)), 'margin': float(margin),
             'started': time.monotonic(), 'created_objects': [], 'created_data': [],
             'bounds_cache': {},
             'source_scene_name': scene.name, 'source_rig_name': rig.name}
    bpy.app.driver_namespace['AutoPlayerV1_RenderClips'] = state
    try:
        for job in expanded:
            Path(job['frames_dir']).mkdir(parents=True, exist_ok=True)
        rc_make_scene(state, scene, rig, meshes)
        rc_progress(state)
        bpy.app.timers.register(rc_tick, first_interval=.2, persistent=False)
    except Exception:
        state['status'], state['phase'], state['error'] = 'FAILED', 'failed', traceback.format_exc()
        rc_cleanup(state)
        rc_progress(state)
        raise
    return {'status': 'SCHEDULED', 'jobs': len(expanded),
            'total_png_frames': sum(j['frame_count'] for j in expanded),
            'progress_path': str(output / 'progress.json')}


def stop_render_jobs():
    """Stop between callbacks, remove temporary scene; completed PNGs remain reviewable."""
    state = rc_state()
    if state is None:
        return {'status': 'NOT_STARTED'}
    if bpy.app.timers.is_registered(rc_tick):
        bpy.app.timers.unregister(rc_tick)
    if state['status'] == 'RUNNING':
        state['status'], state['phase'] = 'STOPPED', 'stopped'
        rc_cleanup(state)
    return rc_progress(state)


def encode_rendered_jobs(progress_path, ffmpeg_executable='ffmpeg'):
    """Separate synchronous encoding; can run outside Blender with this function copied.

    Explicit call only, never from the timer. No shell interpolation; no overwrite.
    Reports command result and expected timing; encoded file timing is not ffprobed here.
    """
    manifest = json.loads(Path(progress_path).read_text(encoding='utf-8'))
    results = []
    for job in manifest['jobs']:
        if job['status'] != 'PNG_COMPLETE_MP4_NOT_ENCODED':
            results.append({'label': job['label'], 'status': 'SKIPPED_INCOMPLETE_PNGS'})
            continue
        missing = [i for i in range(1, job['frame_count'] + 1)
                   if not (Path(job['frames_dir']) / ('frame_%06d.png' % i)).is_file()]
        if missing:
            results.append({'label': job['label'], 'status': 'FAILED_MISSING_PNGS', 'missing': missing})
            continue
        command = list(job['ffmpeg_argv'])
        command[0] = str(ffmpeg_executable)
        completed = subprocess.run(command, capture_output=True, text=True, check=False)
        results.append({'label': job['label'], 'status': 'ENCODED' if completed.returncode == 0 else 'FAILED',
                        'returncode': completed.returncode, 'stderr': completed.stderr[-6000:],
                        'mp4_path': job['mp4_path'], 'expected_frames': job['frame_count'],
                        'expected_duration_seconds': job['encoded_duration_seconds'],
                        'actual_file_timing': 'UNVERIFIED_REQUIRES_FFPROBE'})
    destination = Path(progress_path).with_name('encoding_report.json')
    rc_write_json(destination, results)
    return results
