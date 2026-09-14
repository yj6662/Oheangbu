"""Definitions only. Local, provenance-checked candidate motion preparation.

prepare_motion_tests('C02') runs on the CURRENT isolated candidate scene after
the owning task has completed its raw/rig inspection and downloaded all sources.
No network, credits, generation, rig repair, retargeting, rendering or canonical edits.
The eight completed numeric audits do not grant a visual/rig/Unity acceptance pass.
"""
import ast
import hashlib
import json
import math
import re
import struct
import traceback
from pathlib import Path

import bpy
import numpy as np


def cp_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def cp_write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    temporary.replace(path)


def cp_sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def cp_local_path(base, relative):
    # Ledger paths are relative to AutoPlayerV1, with Windows separators.
    candidate = Path(str(relative).replace('\\', '/'))
    result = candidate.resolve() if candidate.is_absolute() else (base / candidate).resolve()
    if not result.is_relative_to(base.resolve()):
        raise ValueError('Source path escapes the current production directory')
    return result


def cp_request(base, ledger, name, kind):
    rows = [r for r in ledger.get('requests', []) if r.get('name') == name]
    if len(rows) != 1:
        raise ValueError('Expected one ledger entry: ' + name)
    row = rows[0]
    if row.get('kind') != kind or row.get('status') != 'SUCCEEDED' or not row.get('task_id'):
        raise ValueError('Ledger task is missing, wrong kind or not SUCCEEDED: ' + name)
    path = base / 'Candidates' / name / 'Source' / 'manifest.json'
    manifest = cp_json(path)
    for key in ('task_id', 'kind', 'config', 'consumed_credits'):
        if manifest.get(key) != row.get(key):
            raise ValueError('Ledger/source manifest mismatch: ' + name + '/' + key)
    consumed = row.get('consumed_credits')
    if not isinstance(consumed, (int, float)) or not math.isfinite(consumed) or consumed < 0:
        raise ValueError('Actual consumed credits must be recorded: ' + name)
    return {'ledger': row, 'manifest': manifest, 'manifest_path': path}


def cp_source(base, request, basename):
    manifest_rows = [r for r in request['manifest'].get('files', [])
                     if Path(r.get('path', '').replace('\\', '/')).name == basename]
    if len(manifest_rows) != 1:
        raise ValueError('Expected one downloaded source: ' + basename)
    recorded = manifest_rows[0]
    path = cp_local_path(base, recorded['path'])
    ledger_rows = [r for r in request['ledger'].get('downloads', [])
                   if cp_local_path(base, r.get('path', '')) == path]
    if len(ledger_rows) != 1:
        raise ValueError('Source is absent/ambiguous in ledger downloads: ' + basename)
    ledger_row = ledger_rows[0]
    if any(recorded.get(key) != ledger_row.get(key) for key in ('sha256', 'bytes')):
        raise ValueError('Ledger/source hash or byte-size mismatch: ' + basename)
    actual_hash = cp_sha(path)
    if path.stat().st_size != recorded.get('bytes') or actual_hash != recorded.get('sha256'):
        raise ValueError('Downloaded file content differs from recorded source: ' + basename)
    return {'path': str(path), 'sha256': actual_hash, 'bytes': path.stat().st_size,
            'task_id': request['ledger']['task_id'], 'manifest': str(request['manifest_path'])}


def candidate_sources(candidate, base_dir='C:/Users/yj666/Oheangbu/Art/PlayerPhase1/AutoPlayerV1'):
    """Read-only preflight; fail closed on missing/unverified task chain or file hashes."""
    if not re.fullmatch(r'C\d{2}', candidate):
        raise ValueError('Expected candidate code such as C02')
    base = Path(base_dir).resolve()
    ledger_path = base / 'cost_ledger.json'
    ledger = cp_json(ledger_path)
    requests = {
        'character': cp_request(base, ledger, candidate, 'character'),
        'rig': cp_request(base, ledger, candidate + '_Rig', 'rigging'),
        'idle': cp_request(base, ledger, candidate + '_Idle', 'animation'),
        'attack': cp_request(base, ledger, candidate + '_Attack', 'animation')}
    character, rig = requests['character']['ledger'], requests['rig']['ledger']
    if character['config'].get('ai_model') != 'meshy-7' or character['config'].get('model_type') != 'standard':
        raise ValueError('This candidate must use Meshy-7 standard generation')
    if rig['config'].get('input_task_id') != character['task_id']:
        raise ValueError('Rig task was not generated from this character task')
    if requests['idle']['ledger']['config'].get('action_id') != 0:
        raise ValueError('Idle provenance does not identify official action_id 0')
    for name in ('idle', 'attack'):
        if requests[name]['ledger']['config'].get('rig_task_id') != rig['task_id']:
            raise ValueError(name + ' animation does not belong to this rig task')
        if not isinstance(requests[name]['ledger']['config'].get('action_id'), int):
            raise ValueError(name + ' requires an explicit official library action_id')
    sources = {
        'raw': cp_source(base, requests['character'], 'model_urls_glb.glb'),
        'rig_rest': cp_source(base, requests['rig'], 'result_rigged_character_glb_url.glb'),
        'Walk': cp_source(base, requests['rig'], 'result_basic_animations_walking_glb_url.glb'),
        'Run': cp_source(base, requests['rig'], 'result_basic_animations_running_glb_url.glb'),
        'Idle': cp_source(base, requests['idle'], 'result_animation_glb_url.glb'),
        'Attack': cp_source(base, requests['attack'], 'result_animation_glb_url.glb')}
    for label in ('Walk', 'Run', 'Idle', 'Attack'):
        sources[label]['rig_task_id'] = rig['task_id']
        sources[label]['kind'] = 'RIGGING_INCLUDED_REAL_CLIP' if label in ('Walk', 'Run') else 'PAID_LIBRARY_REAL_CLIP'
    sources['Idle']['action_id'] = requests['idle']['ledger']['config']['action_id']
    sources['Attack']['action_id'] = requests['attack']['ledger']['config']['action_id']
    return {'candidate': candidate, 'base_dir': str(base), 'ledger': str(ledger_path),
            'ledger_sha256_at_read': cp_sha(ledger_path), 'rig_task_id': rig['task_id'],
            'character_task_id': character['task_id'], 'sources': sources,
            'budget_credits': ledger.get('budget_credits'),
            'candidate_consumed_credits': sum(r['ledger']['consumed_credits'] for r in requests.values()),
            'requests': [{'name': r['ledger']['name'], 'task_id': r['ledger']['task_id'],
                          'kind': r['ledger']['kind'], 'consumed_credits': r['ledger']['consumed_credits'],
                          'manifest': str(r['manifest_path']), 'manifest_sha256': cp_sha(r['manifest_path'])}
                         for r in requests.values()],
            'validation': 'LEDGER_MANIFEST_TASK_CHAIN_AND_LOCAL_SHA256_MATCH'}


def cp_load_tool(tools_dir, filename):
    path = Path(tools_dir) / filename
    source = path.read_text(encoding='utf-8')
    tree = ast.parse(source, filename=str(path))
    allowed = (ast.Import, ast.ImportFrom, ast.FunctionDef)
    if any(not isinstance(node, allowed) and not (
            isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant)) for node in tree.body):
        raise ValueError('Tool has module-level executable operations: ' + filename)
    namespace = {'__name__': 'AutoPlayerCandidateTool_' + path.stem, '__file__': str(path)}
    exec(compile(tree, str(path), 'exec'), namespace)
    return namespace, {'path': str(path), 'sha256': cp_sha(path)}


def cp_fingerprint(rig, mesh):
    """Geometry/UV/weights/rest data only; animation poses and packed-image flags excluded."""
    mesh.data.calc_loop_triangles()
    xyz = np.empty(len(mesh.data.vertices) * 3, dtype=np.float32)
    mesh.data.vertices.foreach_get('co', xyz)
    triangles = np.asarray([tuple(t.vertices) for t in mesh.data.loop_triangles], dtype=np.int32)
    uv = hashlib.sha256()
    for layer in mesh.data.uv_layers:
        values = np.empty(len(layer.data) * 2, dtype=np.float32)
        layer.data.foreach_get('uv', values)
        uv.update(layer.name.encode('utf-8')); uv.update(values.tobytes())
    weights = hashlib.sha256()
    weights.update(json.dumps([g.name for g in mesh.vertex_groups], ensure_ascii=False).encode('utf-8'))
    for vertex in mesh.data.vertices:
        for group in vertex.groups:
            weights.update(struct.pack('<IIf', vertex.index, group.group, group.weight))
    rest = [{'name': b.name, 'parent': b.parent.name if b.parent else None,
             'matrix_local': [list(row) for row in b.matrix_local],
             'head_local': list(b.head_local), 'tail_local': list(b.tail_local),
             'use_deform': b.use_deform} for b in rig.data.bones]
    return {'mesh_name': mesh.name, 'rig_name': rig.name, 'armature_data_name': rig.data.name,
            'rig_object_basis': [list(row) for row in rig.matrix_basis],
            'rig_object_world': [list(row) for row in rig.matrix_world],
            'mesh_object_basis': [list(row) for row in mesh.matrix_basis],
            'mesh_object_world': [list(row) for row in mesh.matrix_world],
            'vertices': len(mesh.data.vertices), 'triangles': len(mesh.data.loop_triangles),
            'positions_sha256': hashlib.sha256(xyz.tobytes()).hexdigest(),
            'triangles_sha256': hashlib.sha256(triangles.tobytes()).hexdigest(),
            'uv_sha256': uv.hexdigest(), 'weights_sha256': weights.hexdigest(),
            'rest_bones_sha256': hashlib.sha256(json.dumps(rest, sort_keys=True).encode('utf-8')).hexdigest(),
            'bone_names': [b.name for b in rig.data.bones],
            'bone_count': len(rig.data.bones), 'base_positions_finite': bool(np.isfinite(xyz).all())}


def cp_snapshot(scene, rig):
    rig.animation_data_create()
    ad = rig.animation_data
    return {'scene_name': scene.name, 'frame': scene.frame_current, 'subframe': scene.frame_subframe,
            'start': scene.frame_start, 'end': scene.frame_end,
            'fps': scene.render.fps, 'fps_base': scene.render.fps_base,
            'action': ad.action, 'slot': getattr(ad, 'action_slot', None), 'use_nla': ad.use_nla,
            'nla': [(t, t.mute, t.is_solo) for t in ad.nla_tracks],
            'pose_position': rig.data.pose_position,
            'rig_rotation_mode': rig.rotation_mode, 'rig_matrix_basis': rig.matrix_basis.copy(),
            'pose': {p.name: (p.rotation_mode, p.matrix_basis.copy()) for p in rig.pose.bones},
            'selected': list(bpy.context.selected_objects), 'active': bpy.context.view_layer.objects.active}


def cp_compare_fingerprints(before, after):
    matrices = ('rig_object_basis', 'rig_object_world', 'mesh_object_basis', 'mesh_object_world')
    structural_equal = {k: v for k, v in before.items() if k not in matrices} == {
        k: v for k, v in after.items() if k not in matrices}
    errors = {key: float(np.max(np.abs(np.asarray(before[key]) - np.asarray(after[key]))))
              for key in matrices}
    return {'geometry_skin_uv_rest_names_exact': structural_equal,
            'object_transform_max_errors': errors, 'transform_tolerance': 1e-6,
            'unchanged': structural_equal and all(v <= 1e-6 for v in errors.values())}


def cp_restore(scene, rig, saved):
    ad = rig.animation_data
    ad.action = saved['action']
    if saved['action'] is not None and saved['slot'] is not None:
        ad.action_slot = saved['slot']
    ad.use_nla = saved['use_nla']
    for track, mute, solo in saved['nla']:
        track.mute, track.is_solo = mute, solo
    scene.render.fps, scene.render.fps_base = saved['fps'], saved['fps_base']
    scene.frame_start, scene.frame_end = saved['start'], saved['end']
    rig.data.pose_position = saved['pose_position']
    scene.frame_set(saved['frame'], subframe=saved['subframe'])
    for bone in rig.pose.bones:
        bone.rotation_mode, bone.matrix_basis = saved['pose'][bone.name]
    rig.rotation_mode, rig.matrix_basis = saved['rig_rotation_mode'], saved['rig_matrix_basis']
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in saved['selected']:
        if obj.name in bpy.context.view_layer.objects:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = saved['active']
    bpy.context.view_layer.update()


def cp_pack_used_images(mesh):
    visited, images = set(), set()
    def visit(tree):
        if tree is None or tree in visited:
            return
        visited.add(tree)
        for node in tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image is not None:
                images.add(node.image)
            if node.type == 'GROUP':
                visit(node.node_tree)
    for material in mesh.data.materials:
        if material and material.use_nodes:
            visit(material.node_tree)
    rows = []
    for image in images:
        if image.source not in ('FILE', 'GENERATED'):
            raise ValueError('Unsupported candidate texture source for packing: ' + image.name)
        was_packed = bool(image.packed_file)
        if not was_packed:
            image.pack()
        if not image.packed_file:
            raise ValueError('Texture packing did not succeed: ' + image.name)
        rows.append({'image': image.name, 'dimensions': list(image.size),
                     'colorspace': image.colorspace_settings.name, 'already_packed': was_packed,
                     'packed': True, 'packed_bytes': len(image.packed_file.data)})
    if not rows:
        raise ValueError('Candidate has no texture images to preserve')
    return rows


def prepare_motion_tests(candidate,
                         base_dir='C:/Users/yj666/Oheangbu/Art/PlayerPhase1/AutoPlayerV1',
                         tools_dir='C:/Users/yj666/Oheangbu/Tools/Blender/AutoPlayerV1'):
    """Synchronous local preparation. Progress JSON is written after every stage.

    Fresh candidate only: existing RigRest/Checked files or named test Actions are
    refused, never overwritten. Four actual source clips and four diagnostic clips
    remain available after completion. Original active pose, FPS and selection return.
    Geometry/rest mismatches stop import; numerical/visual warnings are never repaired.
    """
    provenance = candidate_sources(candidate, base_dir)
    scene = bpy.context.scene
    if bpy.context.mode != 'OBJECT':
        raise ValueError('Run from Object Mode; do not change geometry edit state automatically')
    if scene.get('candidate_id') != candidate:
        raise ValueError('Current scene candidate_id must match the requested candidate')
    if abs(scene.render.fps / scene.render.fps_base - 30) > 1e-6:
        raise ValueError('Current source scene must be 30 fps before motion import')
    rigs = [o for o in scene.objects if o.type == 'ARMATURE']
    meshes = [o for o in scene.objects if o.type == 'MESH' and o.get('candidate_mesh')]
    if len(rigs) != 1 or len(meshes) != 1:
        raise ValueError('Exactly one current-scene Armature and one candidate_mesh required')
    rig, mesh = rigs[0], meshes[0]
    shapes = {p.custom_shape for p in rig.pose.bones if p.custom_shape is not None}
    if mesh in shapes:
        raise ValueError('candidate_mesh is a bone display shape, not character geometry')
    modifiers = [m for m in mesh.modifiers if m.show_viewport]
    if len(modifiers) != 1 or modifiers[0].type != 'ARMATURE' or modifiers[0].object != rig:
        raise ValueError('Candidate must use exactly one active Armature modifier to its sole rig')
    if rig.get('meshy_rig_task_id') and rig['meshy_rig_task_id'] != provenance['rig_task_id']:
        raise ValueError('Live rig stored provenance conflicts with the downloaded sources')
    folder = Path(provenance['base_dir']) / 'Candidates' / candidate
    rest_file, checked_file = folder / 'RigRest.blend', folder / ('Candidate_' + candidate + '_Checked.blend')
    report_file = folder / 'candidate_pipeline.json'
    names = [candidate + '_' + label for label in ('Walk', 'Run', 'Idle', 'Attack')]
    diag_names = ['DIAG_' + label for label in ('ArmsDown', 'DeepElbow', 'Overhead', 'Squat')]
    if rest_file.exists() or checked_file.exists() or any(bpy.data.actions.get(n) for n in names + diag_names):
        raise ValueError('Existing candidate files/test Actions protected; this is a fresh-candidate pipeline')
    raw_report, rig_report = cp_json(folder / 'raw_geometry.json'), cp_json(folder / 'rig_original_geometry.json')
    for inspection, label in ((raw_report, 'raw'), (rig_report, 'rig_rest')):
        inspected_source = cp_local_path(Path(provenance['base_dir']), inspection.get('source', ''))
        verified_source = provenance['sources'][label]
        # Meshy may return model_url and model_urls_glb as byte-identical aliases.
        # Validate the inspected bytes rather than require a particular alias name.
        if (inspection.get('candidate') != candidate or not inspected_source.is_file()
                or inspected_source.stat().st_size != verified_source['bytes']
                or cp_sha(inspected_source) != verified_source['sha256']):
            raise ValueError('Prior raw/rig inspection source does not match verified provenance')
    initial = cp_fingerprint(rig, mesh)
    if initial['triangles'] != rig_report.get('triangles') or len(rig_report.get('meshes', [])) != 1:
        raise ValueError('Current character geometry count differs from prior rig inspection')
    inspected_mesh = rig_report['meshes'][0]
    if inspected_mesh.get('vertices') != initial['vertices']:
        raise ValueError('Current vertex count differs from prior rig inspection')
    imported, import_tool = cp_load_tool(tools_dir, 'motion_import.py')
    diagnostic, diagnostic_tool = cp_load_tool(tools_dir, 'diagnostic_poses.py')
    audited, audit_tool = cp_load_tool(tools_dir, 'audit_candidate.py')
    if 'mi_character_meshes' not in imported:
        raise ValueError('Motion import tool lacks the corrected bone-display-shape exclusion')
    saved = cp_snapshot(scene, rig)
    report = {'candidate': candidate, 'status': 'IN_PROGRESS', 'stage': 'preflight_complete',
              'live_scene': scene.name, 'live_rig': rig.name, 'live_mesh': mesh.name,
              'provenance': provenance, 'initial': initial,
              'tools': [import_tool, diagnostic_tool, audit_tool], 'imports': {}, 'audits': {},
              'rig_rest_file': str(rest_file), 'checked_file': str(checked_file),
              'acceptance': {'generation': 'UNVERIFIED', 'deformation': 'UNVERIFIED',
                             'serialization': 'UNVERIFIED', 'grip': 'UNVERIFIED', 'unity_runtime': 'UNVERIFIED'},
              'candidate_repair_count_increment': 0, 'remote_calls': 0,
              'limits': ['Source counts and SHA256 do not replace visual raw-generation acceptance.',
                         'Four DIAG clips are joint tests, not replacement production motion.',
                         'No automatic RIG_PASS/DEFORMATION_PASS or canonical replacement.',
                         'No render, FBX roundtrip, physical brush, cloth or Unity runtime test is performed here.']}
    cp_write(report_file, report)
    try:
        # Preserve an actual bind/rest display copy without modifying rest transforms.
        rig.data.pose_position = 'REST'
        bpy.context.view_layer.update()
        bpy.ops.wm.save_as_mainfile(filepath=str(rest_file), copy=True)
        cp_restore(scene, rig, saved)
        report['rig_rest_sha256'] = cp_sha(rest_file)
        report['stage'] = 'rig_rest_preserved'
        cp_write(report_file, report)
        for label in ('Walk', 'Run', 'Idle', 'Attack'):
            source = provenance['sources'][label]
            action_name = candidate + '_' + label
            output = folder / (label.lower() + '_import.json')
            result = imported['import_same_rig_motion'](
                source['path'], rig.name, action_name, provenance['rig_task_id'],
                source['rig_task_id'], str(output), reference_mesh_names=[mesh.name])
            report['imports'][action_name] = {'status': result['status'], 'report': str(output),
                                               'timing': result.get('glb_timing')}
            report['stage'] = 'import_' + label
            cp_write(report_file, report)
            if result['status'] != 'SAME_RIG_ACTION_CONNECTED_REQUIRES_PLAYBACK_AUDIT':
                raise ValueError('Exact same-rig import rejected for ' + action_name)
            action = bpy.data.actions[action_name]
            action['source_task_id'] = source['task_id']
            action['source_kind'] = source['kind']
            action['fps'] = 30
            # Clear active/NLA residue before selecting the next source; no key edits.
            cp_restore(scene, rig, saved)
        diag_path = folder / 'diagnostic_poses.json'
        generated = diagnostic['create_diagnostic_poses'](rig.name, [mesh.name], str(diag_path))
        if generated.get('tool_revision', 0) < 2:
            raise ValueError('Diagnostic tool must use anatomical child-head axes, not display bone tails')
        actual_diags = [generated['actions'][label]['action'] for label in ('ArmsDown', 'DeepElbow', 'Overhead', 'Squat')]
        if actual_diags != diag_names:
            raise ValueError('Unexpected diagnostic action identities')
        report['diagnostic_report'], report['stage'] = str(diag_path), 'diagnostics_created'
        cp_write(report_file, report)
        for action_name in names + actual_diags:
            action = bpy.data.actions[action_name]
            output = folder / 'BaselineAudit' / (action_name + '_audit.json')
            if action_name.startswith(candidate + '_'):
                label = action_name[len(candidate) + 1:]
                clip_provenance = dict(provenance['sources'][label])
                timing = report['imports'][action_name]['timing']['animations'][0]
                inferred = timing.get('inferred_sample_rate_hz')
                # glTF has times, not an FPS flag. Record the inference separately.
                clip_provenance['sample_rate_hz_inferred_from_GLTF_keys'] = inferred
                clip_provenance['sample_rate_is_container_FPS'] = False
                source_fps = 30 if inferred is not None and abs(inferred - 30) < .002 else None
                source_range = [timing['time_start_seconds'] * 30, timing['time_end_seconds'] * 30]
            else:
                clip_provenance = {'kind': 'PROCEDURAL_JOINT_DIAGNOSTIC',
                                   'source_report': str(diag_path), 'tool_revision': generated['tool_revision'],
                                   'production_motion': False, 'rig_task_id': provenance['rig_task_id']}
                source_fps, source_range = 30, [1, 60]
            result = audited['audit_candidate'](rig.name, [mesh.name], action_name, str(output),
                provenance=clip_provenance, source_fps=source_fps, source_frame_range=source_range)
            summary = {'status': result['status'], 'report': str(output),
                       'data_status': result.get('data_status'), 'timing_status': result.get('timing_status'),
                       'frame_count': len(result['sampled_integer_frames']),
                       'required_frame_count': len(result['required_integer_frames']),
                       'actual_action_range': list(action.frame_range),
                       'tris_evaluated': result.get('triangles_total_rest_evaluated'),
                       'error': result.get('error'), 'meshes': {}}
            for name, row in result['meshes'].items():
                summary['meshes'][name] = {key: row.get(key) for key in (
                    'vertices', 'triangles_rest_evaluated', 'weights', 'worst_qualified_edge',
                    'worst_short_edge', 'warn_edges_over_2_unique', 'warn_edges_over_4_unique')}
            report['audits'][action_name] = summary
            report['stage'] = 'audit_' + action_name
            cp_write(report_file, report)
        cp_restore(scene, rig, saved)
        final = cp_fingerprint(rig, mesh)
        report['final'] = final
        report['preservation_comparison'] = cp_compare_fingerprints(initial, final)
        report['source_geometry_skin_rest_unchanged'] = report['preservation_comparison']['unchanged']
        report['scene_name_unchanged'] = scene.name == saved['scene_name']
        if not report['source_geometry_skin_rest_unchanged'] or not report['scene_name_unchanged']:
            raise ValueError('Unexpected mutation of candidate geometry, weights, UV, names or rest skeleton')
        report['textures'] = cp_pack_used_images(mesh)
        report['all_eight_clips_fully_sampled'] = len(report['audits']) == 8 and all(
            row['frame_count'] == row['required_frame_count'] and row['frame_count'] > 0 and not row['error']
            for row in report['audits'].values())
        numerical_failure = any(row['data_status'] == 'FAIL' for row in report['audits'].values())
        report['status'] = ('CHECKS_INCOMPLETE_REQUIRES_REVIEW' if not report['all_eight_clips_fully_sampled'] else
                            'CHECKS_RECORDED_WITH_NUMERICAL_FAILURES' if numerical_failure else
                            'CHECKS_RECORDED_REQUIRES_VISUAL_REVIEW')
        report['stage'] = 'saving_checked_copy'
        cp_write(report_file, report)
        bpy.ops.wm.save_as_mainfile(filepath=str(checked_file), copy=True)
        report['checked_file_sha256'] = cp_sha(checked_file)
        report['stage'] = 'finished'
    except Exception:
        report['status'], report['error'] = 'PIPELINE_STOPPED', traceback.format_exc()
        report['partial_actions_retained'] = [name for name in names + diag_names if bpy.data.actions.get(name)]
    finally:
        cp_restore(scene, rig, saved)
        report['final_live_scene'], report['final_live_rig'], report['final_live_mesh'] = scene.name, rig.name, mesh.name
        cp_write(report_file, report)
    print(json.dumps({'status': report['status'], 'report': str(report_file),
                      'clips_audited': len(report['audits'])}, ensure_ascii=False))
    return report
