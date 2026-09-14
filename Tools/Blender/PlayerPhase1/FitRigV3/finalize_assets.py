"""Read-only Blender asset inspection + lossless filesystem texture collection.

Definitions only; call finalize_assets() explicitly. No image pixels, node links,
materials, mesh coordinates, skin weights, or transforms are changed.
Packed byte images are written as their exact existing encoded bytes, never re-encoded.
"""
import hashlib
import json
import re
from pathlib import Path

import bpy
import numpy as np


FA_OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')
FA_PARTS = ('Body', 'InnerTop', 'Durumagi')


def fa_safe_name(name):
    result = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name).rstrip(' .')
    return result or 'unnamed'


def fa_socket_match(sockets, socket, source_sockets):
    # Group interface sockets preserve identifier across the instance and node tree.
    match = next((s for s in sockets if s.identifier == socket.identifier), None)
    if match is not None:
        return match
    match = next((s for s in sockets if s.name == socket.name), None)
    if match is not None:
        return match
    try:
        index = list(source_sockets).index(socket)
        return sockets[index] if index < len(sockets) else None
    except ValueError:
        return None


def fa_upstream_images(material):
    """Walk only live upstream connections of active Material Output nodes.

    Group output/input interfaces are followed by the actually used socket, including
    external inputs to a group. Images on disconnected nodes/unused group outputs
    are excluded. This is graph connectivity, not runtime constant-branch evaluation.
    """
    if material is None or not material.use_nodes or material.node_tree is None:
        return []
    tree = material.node_tree
    outputs = [n for n in tree.nodes if n.type == 'OUTPUT_MATERIAL']
    active = [n for n in outputs if getattr(n, 'is_active_output', False)]
    outputs = active or outputs
    found, visited = {}, set()

    def walk_input(socket, contexts):
        for link in socket.links:
            if getattr(link, 'is_valid', True) and not getattr(link, 'is_muted', False):
                walk_output(link.from_socket, contexts)

    def walk_output(socket, contexts):
        node = socket.node
        key = (node.as_pointer(), socket.identifier,
               tuple(group.as_pointer() for group in contexts))
        if key in visited:
            return
        visited.add(key)
        # Muted nodes use internal bypass links, rather than executing their images.
        if node.mute:
            for link in node.internal_links:
                if link.to_socket == socket:
                    walk_input(link.from_socket, contexts)
            return
        if node.type == 'GROUP' and node.node_tree:
            group_outputs = [n for n in node.node_tree.nodes if n.type == 'GROUP_OUTPUT']
            chosen = [n for n in group_outputs if getattr(n, 'is_active_output', False)]
            for group_output in chosen or group_outputs:
                inner = fa_socket_match(group_output.inputs, socket, node.outputs)
                if inner is not None:
                    walk_input(inner, contexts + (node,))
            return
        if node.type == 'GROUP_INPUT':
            if contexts:
                instance = contexts[-1]
                outer = fa_socket_match(instance.inputs, socket, node.outputs)
                if outer is not None:
                    walk_input(outer, contexts[:-1])
            return
        if node.type in ('TEX_IMAGE', 'TEX_ENVIRONMENT') and node.image is not None:
            image = node.image
            item = found.setdefault(image.as_pointer(), {'image': image, 'connections': []})
            location = '/'.join([material.name, *(n.name for n in contexts), node.name])
            connection = {'node_path': location, 'output_socket': socket.name}
            if connection not in item['connections']:
                item['connections'].append(connection)
        for input_socket in node.inputs:
            if input_socket.is_linked:
                walk_input(input_socket, contexts)

    for output in outputs:
        for socket in output.inputs:
            if socket.is_linked:
                walk_input(socket, ())
    return list(found.values())


def fa_existing_image_path(image):
    if not image.filepath:
        return None
    try:
        value = Path(bpy.path.abspath(image.filepath, library=image.library))
        return value if value.is_file() else None
    except (ValueError, OSError):
        return None


def fa_image_extension(data, source_path=''):
    if data.startswith(b'\x89PNG\r\n\x1a\n'):
        return '.png'
    if data.startswith(b'\xff\xd8\xff'):
        return '.jpg'
    if data.startswith((b'II*\x00', b'MM\x00*')):
        return '.tif'
    if data.startswith(b'v/1\x01'):
        return '.exr'
    if data.startswith(b'BM'):
        return '.bmp'
    if data.startswith(b'RIFF') and data[8:12] == b'WEBP':
        return '.webp'
    extension = Path(source_path).suffix.lower()
    return extension if extension in ('.png', '.jpg', '.jpeg', '.tga', '.tif', '.tiff', '.exr', '.hdr', '.bmp', '.webp') else '.bin'


def fa_write_exact(directory, part, image_name, data, source_path=''):
    digest = hashlib.sha256(data).hexdigest()
    extension = fa_image_extension(data, source_path)
    target = directory / (fa_safe_name(part + '__' + image_name) + extension)
    if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() != digest:
        target = target.with_name(target.stem + '_' + digest[:10] + target.suffix)
    if not target.exists():
        target.write_bytes(data)
    result_digest = hashlib.sha256(target.read_bytes()).hexdigest()
    if result_digest != digest:
        raise RuntimeError('Written texture differs from source bytes: ' + str(target))
    return target, digest


def collect_used_textures(output_dir=None):
    """Collect only connected material images, preserving encoded byte payloads exactly."""
    out = Path(output_dir) if output_dir else FA_OUT
    folder = out / 'Textures'
    folder.mkdir(parents=True, exist_ok=True)
    report = {
        'status': 'PASS', 'method': 'Active Material Output upstream graph, including used group '
                   'interfaces. Packed byte buffers copied bit-for-bit; no pixel conversion/re-encoding.',
        'source_blend': bpy.data.filepath, 'parts': {},
        'scope_note': 'Connected texture nodes are collected even if a runtime constant shader branch may disable them.',
        'images_or_materials_modified': False,
    }
    cache = {}
    for part in FA_PARTS:
        obj = bpy.data.objects.get(part)
        if obj is None:
            report['parts'][part] = {'status': 'FAIL', 'reason': 'Missing object'}
            report['status'] = 'FAIL'
            continue
        rows = []
        for slot_index, slot in enumerate(obj.material_slots):
            material = slot.material
            for item in fa_upstream_images(material):
                image = item['image']
                cache_key = (part, image.as_pointer())
                if cache_key not in cache:
                    existing = fa_existing_image_path(image)
                    packed = image.packed_file
                    row = {'image': image.name, 'source': image.source,
                           'size': list(image.size), 'is_float': image.is_float,
                           'colorspace': image.colorspace_settings.name,
                           'source_filepath': image.filepath,
                           'packed': packed is not None}
                    try:
                        final_robe = (part == 'Durumagi' and image.name.startswith(
                            ('T_Durumagi_FitRigV3_BaseColor', 'T_Durumagi_FitRigV3_Roughness')))
                        if final_robe and existing:
                            # The owner already saved and corrected these final images: reuse verbatim.
                            data = existing.read_bytes()
                            row.update(status='PASS', method='REUSED_FINAL_EXTERNAL_FILE',
                                       delivered_path=str(existing), bytes=len(data),
                                       sha256=hashlib.sha256(data).hexdigest())
                        elif packed is not None and not image.is_float:
                            data = bytes(packed.data)
                            path, digest = fa_write_exact(folder, part, image.name, data, image.filepath)
                            row.update(status='PASS', method='EXACT_PACKED_ENCODED_BYTES',
                                       delivered_path=str(path), bytes=len(data), sha256=digest)
                        elif existing:
                            data = existing.read_bytes()
                            path, digest = fa_write_exact(folder, part, image.name, data, str(existing))
                            row.update(status='PASS', method='EXACT_EXISTING_EXTERNAL_BYTES',
                                       delivered_path=str(path), bytes=len(data), sha256=digest)
                        else:
                            row.update(status='UNVERIFIED', reason='No external encoded file or packed byte image; '
                                       'refused to save/re-encode or modify this image.')
                    except Exception as exc:
                        row.update(status='FAIL', error=repr(exc))
                    cache[cache_key] = row
                row = dict(cache[cache_key])
                row['material'] = material.name
                row['material_slot'] = slot_index
                row['connections'] = item['connections']
                rows.append(row)
        status = 'PASS' if all(r['status'] == 'PASS' for r in rows) else 'INCOMPLETE'
        report['parts'][part] = {'status': status, 'textures': rows}
        if status != 'PASS' and report['status'] != 'FAIL':
            report['status'] = 'INCOMPLETE'
    report['unique_part_images'] = len(cache)
    report['delivered_paths'] = sorted({r['delivered_path'] for r in cache.values() if 'delivered_path' in r})
    path = out / 'used_texture_manifest.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'images': len(cache), 'manifest': str(path)}))
    return report


def compare_original_body(original_settings=None, output_dir=None):
    """Use EXACT original_settings.json Body geometry hash algorithm from fit_work.py.

    Hash = SHA256(float32 local vertex xyz bytes + int32 tessellated triangle index bytes).
    This verifies base geometry/order; it does NOT prove original weights are identical,
    because original_settings.json stores only aggregate skin-weight statistics.
    """
    out = Path(output_dir) if output_dir else FA_OUT
    original_path = Path(original_settings) if original_settings else FA_OUT / 'original_settings.json'
    out.mkdir(parents=True, exist_ok=True)
    report = {'status': 'UNVERIFIED', 'baseline': str(original_path),
              'method': 'SHA256(float32 local xyz + int32 loop-triangle indices), '
                        'identical to fit_work.record_original().',
              'mesh_content_modified': False, 'exact_skin_weight_comparison': 'UNVERIFIED',
              'skin_weight_limit': 'Baseline contains aggregate statistics only, not exact per-vertex weights.'}
    try:
        baseline = json.loads(original_path.read_text(encoding='utf-8'))['objects']['Body']
        body = bpy.data.objects['Body']
        body.data.calc_loop_triangles()  # Refreshes a derived tessellation cache only.
        coords = np.array([list(v.co) for v in body.data.vertices], dtype=np.float32)
        indices = np.array([list(t.vertices) for t in body.data.loop_triangles], dtype=np.int32)
        digest = hashlib.sha256(coords.tobytes() + indices.tobytes()).hexdigest()
        report['baseline_geometry_sha256'] = baseline['geometry_sha256']
        report['current_geometry_sha256'] = digest
        report['geometry_status'] = 'PASS' if digest == baseline['geometry_sha256'] else 'FAIL'
        report['baseline_vertices'], report['current_vertices'] = baseline['vertices'], len(coords)
        report['baseline_triangles'], report['current_triangles'] = baseline['tris'], len(indices)
        report['current_parent'] = body.parent.name if body.parent else None
        report['parent_matches'] = report['current_parent'] == baseline['parent']
        report['world_matrix_max_change'] = float(np.max(np.abs(
            np.asarray(body.matrix_world) - np.asarray(baseline['matrix_world']))))
        report['parent_inverse_matrix_max_change'] = float(np.max(np.abs(
            np.asarray(body.matrix_parent_inverse) - np.asarray(baseline['matrix_parent_inverse']))))
        rig = bpy.data.objects['Dosa_Phase1_Rig']
        deform = {g.index for g in body.vertex_groups if g.name in rig.data.bones}
        weights = [[g.weight for g in v.groups if g.group in deform and g.weight > 0]
                   for v in body.data.vertices]
        current_weights = {'unassigned': sum(not w for w in weights),
                           'max_influences': max(map(len, weights), default=0),
                           'max_sum_error': max((abs(sum(w) - 1) for w in weights), default=0)}
        report['baseline_weight_statistics'] = baseline['weights']
        report['current_weight_statistics'] = current_weights
        report['weight_statistics_match'] = (current_weights['unassigned'] == baseline['weights']['unassigned']
            and current_weights['max_influences'] == baseline['weights']['max_influences']
            and abs(current_weights['max_sum_error'] - baseline['weights']['max_sum_error']) <= 1e-12)
        report['status'] = 'PASS_BASE_GEOMETRY' if report['geometry_status'] == 'PASS' else 'FAIL_BASE_GEOMETRY'
    except Exception as exc:
        report['error'] = repr(exc)
    path = out / 'Body_preservation_final.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'report': str(path)}))
    return report


def finalize_assets(output_dir=None, original_settings=None):
    textures = collect_used_textures(output_dir)
    body = compare_original_body(original_settings, output_dir)
    return {'textures': textures['status'], 'body_geometry': body['status']}
