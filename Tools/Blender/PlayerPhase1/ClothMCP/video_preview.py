"""Sequential cloth motion previews. Loading this file never starts a render.

One call: render_motion_preview('BrushSwing')
Split calls (reload this file each time if necessary):
    setup('BrushSwing'); render_slice(11, 22)
    render_slice(23, 34)
    render_slice(35, 46); finish()

State survives independent MCP namespaces in bpy.app.driver_namespace.
finish() restores the previous action/slot, pose, NLA mute flags, camera,
render settings and frame. Preview generation replaces the transient cloth cache.
No source action, mesh, rig, modifier definition or saved .blend is edited.
"""
import bpy, importlib.util, json, subprocess
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Art/PlayerPhase1/ClothMCP'
STATE_KEY = 'Oheangbu.ClothMCP.VideoPreview.v1'
FFMPEG = Path('C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')

def _helpers():
    path = ROOT / 'Tools/Blender/PlayerPhase1/ClothMCP/audit_preview.py'
    spec = importlib.util.spec_from_file_location('_cloth_preview_audit_helpers', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def _state():
    state = bpy.app.driver_namespace.get(STATE_KEY)
    if not state:
        raise RuntimeError('No preview session. Call setup(label) first.')
    return state

def setup(label='BrushSwing'):
    if STATE_KEY in bpy.app.driver_namespace:
        raise RuntimeError('A preview session is active. Call finish() before setup().')
    h = _helpers()
    if label not in h.LABELS:
        raise ValueError(label)
    rig, proxy, body = h._objects()
    scene = bpy.context.scene
    camera = scene.camera
    if camera is None:
        raise RuntimeError('Scene camera is required.')
    source = bpy.data.actions['DIAG_' + label]
    cloth = next(m for m in proxy.modifiers if m.type == 'CLOTH')
    rig.animation_data_create()
    folder = OUT / 'Previews' / ('Motion_' + label)
    folder.mkdir(parents=True, exist_ok=True)
    state = {
        'label': label, 'rig': rig, 'proxy': proxy, 'camera': camera,
        'old_action': rig.animation_data.action,
        'old_slot': getattr(rig.animation_data, 'action_slot', None),
        'pose': {p.name: p.matrix_basis.copy() for p in rig.pose.bones},
        'nla': [(track, track.mute) for track in rig.animation_data.nla_tracks],
        'frame': scene.frame_current, 'subframe': scene.frame_subframe,
        'cache_range': (cloth.point_cache.frame_start, cloth.point_cache.frame_end),
        'camera_matrix': camera.matrix_world.copy(),
        'camera_type': camera.data.type, 'camera_scale': camera.data.ortho_scale,
        'render': {
            'resolution_x': scene.render.resolution_x,
            'resolution_y': scene.render.resolution_y,
            'resolution_percentage': scene.render.resolution_percentage,
            'filepath': scene.render.filepath,
            'engine': scene.render.engine,
            'use_file_extension': scene.render.use_file_extension,
        },
        'image_format': scene.render.image_settings.file_format,
        'samples': scene.cycles.samples,
        'folder': str(folder), 'action': None, 'next_frame': 11, 'files': [],
        'manifest': {
            'label': label, 'source_action': source.name,
            'source_range': list(source.frame_range), 'settling_frames': 10,
            'motion_range': [11, 46], 'simulation_frames': [], 'rendered_frames': [],
            'resolution': [640, 480], 'cycles_samples': 4,
            'timing': 'Source action resampled to 36 frames; frame 1..10 holds first pose. Not original-speed validation.',
            'source_fps': scene.render.fps / scene.render.fps_base,
        },
    }
    bpy.app.driver_namespace[STATE_KEY] = state
    try:
        for track, muted in state['nla']:
            track.mute = True
        for pose_bone in rig.pose.bones:
            pose_bone.matrix_basis = Matrix.Identity(4)
        state['action'] = h._prepare_action(source)
        h._assign(rig, state['action'])
        h._cache_reset(proxy, 46)
        camera.location = (2.4, -4.5, 2.1)
        camera.rotation_euler = (Vector((0, 0, 1.0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera.data.type = 'ORTHO'
        camera.data.ortho_scale = 3.65
        scene.render.engine = 'CYCLES'
        scene.cycles.samples = 4
        scene.render.resolution_x = 640
        scene.render.resolution_y = 480
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene.render.use_file_extension = True
        dg = bpy.context.evaluated_depsgraph_get()
        for frame in range(1, 11):
            scene.frame_set(frame)
            dg.update()
        return {'status': 'READY', 'label': label, 'next_frame': 11, 'folder': str(folder)}
    except Exception as exc:
        state['manifest']['error'] = repr(exc)
        finish(encode=False)
        raise

def render_slice(start, end):
    """Advance every simulation frame; render only 11,13,...45 (18 images total)."""
    state = _state()
    start, end = int(start), int(end)
    if start != state['next_frame']:
        raise ValueError('Cannot skip or rewind cloth frames: expected start=' + str(state['next_frame']))
    if not 11 <= start <= end <= 46:
        raise ValueError('Slice must stay within frames 11..46.')
    scene = bpy.context.scene
    dg = bpy.context.evaluated_depsgraph_get()
    try:
        for frame in range(start, end + 1):
            scene.frame_set(frame)
            dg.update()
            state['manifest']['simulation_frames'].append(frame)
            state['next_frame'] = frame + 1
            if (frame - 11) % 2 == 0:
                index = (frame - 11) // 2 + 1
                path = Path(state['folder']) / ('%04d.png' % index)
                scene.render.filepath = str(path)
                bpy.ops.render.render(write_still=True)
                state['files'].append(str(path))
                state['manifest']['rendered_frames'].append(frame)
        result = {'status': 'SLICE_RENDERED', 'last_simulation_frame': end,
                  'next_frame': state['next_frame'], 'images': len(state['files'])}
        print(json.dumps(result))
        return result
    except Exception as exc:
        state['manifest']['error'] = repr(exc)
        finish(encode=False)
        raise

def finish(encode=True):
    """Restore session state even after partial rendering; optionally encode completed frames."""
    state = _state()
    scene = bpy.context.scene
    h = _helpers()
    report = dict(state['manifest'])
    report['files'] = list(state['files'])
    complete = state['next_frame'] == 47 and len(state['files']) == 18 and 'error' not in report
    report['status'] = 'RENDERED' if complete else 'INCOMPLETE'
    restore_errors = []
    try:
        rig = state['rig']
        h._assign(rig, state['old_action'])
        if state['old_action'] is not None and state['old_slot'] is not None:
            rig.animation_data.action_slot = state['old_slot']
        for track, muted in state['nla']:
            track.mute = muted
        for pose_bone in rig.pose.bones:
            pose_bone.matrix_basis = state['pose'][pose_bone.name]
        if state['action'] is not None:
            bpy.data.actions.remove(state['action'])
        cloth = next(m for m in state['proxy'].modifiers if m.type == 'CLOTH')
        cloth.point_cache.frame_start, cloth.point_cache.frame_end = state['cache_range']
        # Rewind invalidates this preview's transient simulation before restoring time.
        scene.frame_set(0)
        scene.frame_set(state['frame'], subframe=state['subframe'])
        # Preserve the previously inspected pose, including unkeyed transforms.
        for pose_bone in rig.pose.bones:
            pose_bone.matrix_basis = state['pose'][pose_bone.name]
    except Exception as exc:
        restore_errors.append('animation/cache: ' + repr(exc))
    finally:
        try:
            camera = state['camera']
            camera.matrix_world = state['camera_matrix']
            camera.data.type = state['camera_type']
            camera.data.ortho_scale = state['camera_scale']
            for name, value in state['render'].items():
                setattr(scene.render, name, value)
            scene.render.image_settings.file_format = state['image_format']
            scene.cycles.samples = state['samples']
        except Exception as exc:
            restore_errors.append('camera/render: ' + repr(exc))
        bpy.app.driver_namespace.pop(STATE_KEY, None)
    if restore_errors:
        report['restore_errors'] = restore_errors
    if complete and encode:
        if FFMPEG.is_file():
            path = OUT / 'Previews' / ('Motion_' + state['label'] + '.mp4')
            # Every second simulation frame -> half the scene FPS.
            fps = max(.5, report['source_fps'] / 2)
            command = [str(FFMPEG), '-y', '-loglevel', 'error', '-framerate', str(fps),
                       '-i', str(Path(state['folder']) / '%04d.png'), '-frames:v', '18',
                       '-c:v', 'libx264', '-crf', '20', '-pix_fmt', 'yuv420p',
                       '-movflags', '+faststart', str(path)]
            try:
                result = subprocess.run(command, capture_output=True, text=True, timeout=45,
                                        creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
                if result.returncode == 0:
                    report['video'] = str(path)
                    report['video_fps'] = fps
                else:
                    report['encode_error'] = result.stderr[-2000:]
            except Exception as exc:
                report['encode_error'] = repr(exc)
        else:
            report['encode_error'] = 'Bundled FFmpeg missing; PNG frames remain available.'
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / ('motion_preview_' + state['label'] + '.json')).write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('files', 'simulation_frames')}, ensure_ascii=False))
    return report

def render_motion_preview(label='BrushSwing'):
    setup(label)
    try:
        render_slice(11, 46)
        return finish()
    except Exception:
        if STATE_KEY in bpy.app.driver_namespace:
            finish(encode=False)
        raise
