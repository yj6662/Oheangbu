"""Chunked UV raster -> nearest source surface -> source UV texture transfer.

No ray casting, render bake, topology edits, or execution on import.
begin_nearest_bake(); step_nearest_bake() repeatedly; finish_nearest_bake().
All numeric state persists in bpy.app.driver_namespace between MCP calls.
The captured meshes are BASE/rest geometry, not arbitrary animated evaluations.
"""
import json
import math
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


NB_KEY = 'FitRigV3_NearestBake'
NB_OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')


def nb_mesh_data(obj, uv_name):
    mesh = obj.data
    layer = mesh.uv_layers.get(uv_name)
    if layer is None:
        raise RuntimeError(obj.name + ' is missing UV layer ' + uv_name)
    mesh.calc_loop_triangles()
    coords = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get('co', coords)
    coords = coords.reshape(-1, 3)
    matrix = np.asarray(obj.matrix_world, dtype=np.float64)
    coords = coords @ matrix[:3, :3].T + matrix[:3, 3]
    indices = np.array([tuple(t.vertices) for t in mesh.loop_triangles], dtype=np.int32)
    loops = np.array([tuple(t.loops) for t in mesh.loop_triangles], dtype=np.int32)
    uv_values = np.empty(len(layer.data) * 2, dtype=np.float64)
    layer.data.foreach_get('uv', uv_values)
    uvs = uv_values.reshape(-1, 2)[loops]
    return coords, indices, uvs


def nb_image_pixels(name):
    image = bpy.data.images.get(name)
    if image is None:
        raise RuntimeError('Missing source image ' + name)
    width, height = image.size
    if width <= 0 or height <= 0:
        raise RuntimeError('Source image has no readable pixels: ' + name)
    values = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(values)
    pixels = values.reshape(height, width, 4)
    # Blender's byte-backed sRGB buffers expose encoded values through pixels;
    # convert only those RGB channels before sampling into the linear float output.
    # Float images are already linear, and Non-Color roughness must stay unchanged.
    if not image.is_float and image.colorspace_settings.name == 'sRGB':
        rgb = pixels[:, :, :3]
        rgb[:] = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    return pixels


def begin_nearest_bake(target_name='Durumagi', source_name='Preserved_Meshy_Robe_BakeSource',
                       target_uv='FitRigV3_BakedUV', source_uv='UVMap',
                       base_image='Image_0.010', roughness_image='Image_1.006',
                       size=2048, reset=False):
    """Capture independent source/target rest geometry and Blender linear pixel buffers."""
    previous = bpy.app.driver_namespace.get(NB_KEY)
    if previous and not previous.get('finished') and not reset:
        raise RuntimeError('A bake is already in progress; continue it or explicitly reset=True.')
    target, source = bpy.data.objects[target_name], bpy.data.objects[source_name]
    if target.type != 'MESH' or source.type != 'MESH':
        raise RuntimeError('Source and target must both be meshes.')
    target_v, target_t, target_uvs = nb_mesh_data(target, target_uv)
    source_v, source_t, source_uvs = nb_mesh_data(source, source_uv)
    if not len(source_t) or not len(target_t):
        raise RuntimeError('Cannot transfer an empty mesh.')
    if not all(np.isfinite(value).all() for value in (target_v, target_uvs, source_v, source_uvs)):
        raise RuntimeError('Source/target rest coordinates or UVs contain nonfinite values.')
    source_tree = BVHTree.FromPolygons([Vector(p) for p in source_v],
                                     [tuple(t) for t in source_t], all_triangles=True)
    base = nb_image_pixels(base_image)
    rough = nb_image_pixels(roughness_image)
    s_tri = source_v[source_t]
    edge0, edge1 = s_tri[:, 1] - s_tri[:, 0], s_tri[:, 2] - s_tri[:, 0]
    d00 = np.einsum('ij,ij->i', edge0, edge0)
    d01 = np.einsum('ij,ij->i', edge0, edge1)
    d11 = np.einsum('ij,ij->i', edge1, edge1)
    # Non-island texels start with source means, preventing black mip backgrounds.
    result_base = np.empty((size, size, 4), dtype=np.float32)
    result_base[:] = base.reshape(-1, 4).mean(axis=0)
    result_base[:, :, 3] = 1
    result_rough = np.full((size, size), float(rough[:, :, 1].mean()), dtype=np.float32)
    state = {
        'target': target, 'source': source, 'target_uv': target_uv, 'source_uv': source_uv,
        'target_mesh_pointer': target.data.as_pointer(), 'target_vertex_count': len(target.data.vertices),
        'target_polygon_count': len(target.data.polygons), 'target_matrix': target.matrix_world.copy(),
        'target_xyz': target_v[target_t], 'target_uvs': target_uvs,
        'source_tree': source_tree, 'source_triangles': s_tri, 'source_uvs': source_uvs,
        'source_edge0': edge0, 'source_edge1': edge1, 'd00': d00, 'd01': d01, 'd11': d11,
        'denominator': d00 * d11 - d01 * d01,
        'base_pixels': base, 'rough_pixels': rough, 'source_base_image': base_image,
        'source_roughness_image': roughness_image, 'size': int(size),
        'result_base': result_base, 'result_rough': result_rough,
        'mask': np.zeros((size, size), dtype=bool), 'triangle_cursor': 0,
        'tile_row': None, 'triangle_info': None, 'pending': None,
        'pixels_sampled': 0, 'pixels_failed': 0, 'degenerate_uv_triangles': 0,
        'outside_uv_triangles': 0, 'maximum_source_distance_m': 0.0,
        'sum_source_distance_m': 0.0, 'samples_distance_over_20mm': 0,
        'samples_distance_over_100mm': 0, 'finished': False, 'started': time.time(),
    }
    bpy.app.driver_namespace[NB_KEY] = state
    result = {'status': 'READY', 'target_triangles': len(target_t),
              'source_triangles': len(source_t), 'resolution': [size, size]}
    print(json.dumps(result))
    return result


def nb_next_tile(state, rows=16):
    """Generate at most rows scanlines of covered UV pixel centers, not a full large island."""
    size = state['size']
    while state['triangle_cursor'] < len(state['target_uvs']):
        index = state['triangle_cursor']
        if state['triangle_info'] is None:
            uv = state['target_uvs'][index] * size - 0.5
            origin = uv[0]
            a, b = uv[1] - origin, uv[2] - origin
            determinant = a[0] * b[1] - a[1] * b[0]
            if not math.isfinite(determinant) or abs(determinant) < 1e-10:
                state['degenerate_uv_triangles'] += 1
                state['triangle_cursor'] += 1
                continue
            x0 = max(0, int(math.ceil(float(uv[:, 0].min()))))
            x1 = min(size - 1, int(math.floor(float(uv[:, 0].max()))))
            y0 = max(0, int(math.ceil(float(uv[:, 1].min()))))
            y1 = min(size - 1, int(math.floor(float(uv[:, 1].max()))))
            if x0 > x1 or y0 > y1:
                state['outside_uv_triangles'] += 1
                state['triangle_cursor'] += 1
                continue
            state['triangle_info'] = (origin, a, b, determinant, x0, x1, y1)
            state['tile_row'] = y0
        origin, a, b, determinant, x0, x1, y1 = state['triangle_info']
        y0 = state['tile_row']
        yend = min(y1 + 1, y0 + rows)
        xx, yy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, yend))
        dx, dy = xx - origin[0], yy - origin[1]
        w1 = (dx * b[1] - dy * b[0]) / determinant
        w2 = (a[0] * dy - a[1] * dx) / determinant
        valid = (w1 >= -1e-7) & (w2 >= -1e-7) & (w1 + w2 <= 1 + 1e-7)
        state['tile_row'] = yend
        if yend > y1:
            state['triangle_cursor'] += 1
            state['triangle_info'], state['tile_row'] = None, None
        if not np.any(valid):
            continue
        weights = np.stack((1 - w1[valid] - w2[valid], w1[valid], w2[valid]), axis=1)
        points = weights @ state['target_xyz'][index]
        return {'x': xx[valid], 'y': yy[valid], 'points': points, 'cursor': 0}
    return None


def nb_bilinear(pixels, uv):
    """Blender-compatible repeated UV sampling at pixel centers; buffers remain linear."""
    height, width = pixels.shape[:2]
    x = np.mod(uv[:, 0], 1.0) * width - 0.5
    y = np.mod(uv[:, 1], 1.0) * height - 0.5
    fx, fy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    tx, ty = (x - fx)[:, None], (y - fy)[:, None]
    x0, x1 = fx % width, (fx + 1) % width
    y0, y1 = fy % height, (fy + 1) % height
    return (pixels[y0, x0] * (1 - tx) * (1 - ty) + pixels[y0, x1] * tx * (1 - ty)
            + pixels[y1, x0] * (1 - tx) * ty + pixels[y1, x1] * tx * ty)


def nb_sample_points(state, x, y, points):
    queries = [state['source_tree'].find_nearest(Vector(p)) for p in points]
    good = np.array([q[0] is not None for q in queries], dtype=bool)
    state['pixels_failed'] += int(np.count_nonzero(~good))
    if not np.any(good):
        return
    records = [q for q in queries if q[0] is not None]
    indices = np.array([q[2] for q in records], dtype=np.int32)
    closest = np.array([tuple(q[0]) for q in records], dtype=np.float64)
    distances = np.array([q[3] for q in records], dtype=np.float64)
    delta = closest - state['source_triangles'][indices, 0]
    d20 = np.einsum('ij,ij->i', delta, state['source_edge0'][indices])
    d21 = np.einsum('ij,ij->i', delta, state['source_edge1'][indices])
    den = state['denominator'][indices]
    usable = np.abs(den) > 1e-20
    v = np.zeros(len(indices), dtype=np.float64)
    w = np.zeros(len(indices), dtype=np.float64)
    v[usable] = ((state['d11'][indices] * d20 - state['d01'][indices] * d21)[usable]
                 / den[usable])
    w[usable] = ((state['d00'][indices] * d21 - state['d01'][indices] * d20)[usable]
                 / den[usable])
    bary = np.clip(np.stack((1 - v - w, v, w), axis=1), 0, 1)
    bary /= np.maximum(bary.sum(axis=1, keepdims=True), 1e-12)
    uv = np.einsum('ij,ijk->ik', bary, state['source_uvs'][indices])
    color = nb_bilinear(state['base_pixels'], uv)
    rough = nb_bilinear(state['rough_pixels'], uv)[:, 1]
    xx, yy = x[good], y[good]
    state['result_base'][yy, xx, :3] = color[:, :3]
    state['result_base'][yy, xx, 3] = 1
    state['result_rough'][yy, xx] = rough
    state['mask'][yy, xx] = True
    state['maximum_source_distance_m'] = max(state['maximum_source_distance_m'], float(distances.max()))
    state['sum_source_distance_m'] += float(distances.sum())
    state['samples_distance_over_20mm'] += int(np.count_nonzero(distances > .02))
    state['samples_distance_over_100mm'] += int(np.count_nonzero(distances > .1))


def step_nearest_bake(max_triangles=2000, max_pixels=100000):
    """Bound work by BOTH triangle progress and covered pixels (large triangles split)."""
    state = bpy.app.driver_namespace[NB_KEY]
    if state['finished']:
        return {'status': 'ALREADY_FINISHED'}
    target = state['target']
    if (target.data.as_pointer() != state['target_mesh_pointer']
            or len(target.data.vertices) != state['target_vertex_count']
            or len(target.data.polygons) != state['target_polygon_count']):
        raise RuntimeError('Target mesh data changed during transfer; restart against the final geometry.')
    started = time.time()
    start_tri = state['triangle_cursor']
    processed = 0
    while processed < max_pixels and state['triangle_cursor'] - start_tri < max_triangles:
        if state['pending'] is None:
            state['pending'] = nb_next_tile(state)
        pending = state['pending']
        if pending is None:
            break
        start = pending['cursor']
        end = min(len(pending['x']), start + max_pixels - processed)
        nb_sample_points(state, pending['x'][start:end], pending['y'][start:end], pending['points'][start:end])
        processed += end - start
        state['pixels_sampled'] += end - start
        pending['cursor'] = end
        if end == len(pending['x']):
            state['pending'] = None
    complete = state['triangle_cursor'] >= len(state['target_uvs']) and state['pending'] is None
    result = {'status': 'READY_TO_FINISH' if complete else 'RUNNING',
              'triangles_processed': state['triangle_cursor'], 'triangles_total': len(state['target_uvs']),
              'pixels_this_call': processed, 'pixels_sampled_total': state['pixels_sampled'],
              'seconds': round(time.time() - started, 3)}
    print(json.dumps(result))
    return result


def nb_padding(base, rough, mask, radius):
    """Propagate island edge values without wraparound; remaining background uses source mean."""
    height, width = mask.shape
    filled = mask.copy()
    for _ in range(radius):
        before = filled.copy()
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            dst_y = slice(max(0, dy), min(height, height + dy))
            dst_x = slice(max(0, dx), min(width, width + dx))
            src_y = slice(max(0, -dy), min(height, height - dy))
            src_x = slice(max(0, -dx), min(width, width - dx))
            candidates = ~filled[dst_y, dst_x] & before[src_y, src_x]
            if np.any(candidates):
                base[dst_y, dst_x][candidates] = base[src_y, src_x][candidates]
                rough[dst_y, dst_x][candidates] = rough[src_y, src_x][candidates]
                filled[dst_y, dst_x][candidates] = True
        if np.array_equal(before, filled):
            break


def nb_save_image(name, pixels, path, color):
    height, width = pixels.shape[:2]
    image = bpy.data.images.new(name, width=width, height=height, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
    image.pixels.foreach_set(np.ascontiguousarray(pixels, dtype=np.float32).ravel())
    image.update()
    image.file_format = 'PNG'
    image.filepath_raw = str(path)
    image.save()
    image.pack()
    return image


def nb_connect(target, uv_name, base, rough):
    copies = {}
    if not len(target.data.materials):
        target.data.materials.append(bpy.data.materials.new('FitRigV3_Robe'))
    for index, original in enumerate(list(target.data.materials)):
        if original is None:
            original = bpy.data.materials.new('FitRigV3_Robe_EmptySlot')
        if original not in copies:
            material = original.copy()
            material.name = original.name + '_NearestFitBake'
            material.use_nodes = True
            nodes, links = material.node_tree.nodes, material.node_tree.links
            principled = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if principled is None:
                principled = nodes.new('ShaderNodeBsdfPrincipled')
                output = next((n for n in nodes if n.type == 'OUTPUT_MATERIAL'), None) or nodes.new('ShaderNodeOutputMaterial')
                links.new(principled.outputs['BSDF'], output.inputs['Surface'])
            uv = nodes.new('ShaderNodeUVMap')
            uv.uv_map = uv_name
            uv.label = 'Final fitted UV'
            for image, socket in ((base, 'Base Color'), (rough, 'Roughness')):
                tex = nodes.new('ShaderNodeTexImage')
                tex.image = image
                tex.interpolation = 'Linear'
                links.new(uv.outputs['UV'], tex.inputs['Vector'])
                links.new(tex.outputs['Color'], principled.inputs[socket])
            # Old tangent-space normals no longer correspond to this final mesh/UV basis.
            for link in list(principled.inputs['Normal'].links):
                links.remove(link)
            copies[original] = material
        target.data.materials[index] = copies[original]
    return [m.name for m in copies.values()]


def finish_nearest_bake(output_dir=None, padding=16, connect_material=True):
    state = bpy.app.driver_namespace[NB_KEY]
    if state['triangle_cursor'] < len(state['target_uvs']) or state['pending'] is not None:
        raise RuntimeError('Raster transfer is incomplete; call step_nearest_bake again.')
    if state['finished']:
        return state['report']
    destination = Path(output_dir) if output_dir else NB_OUT / 'Textures'
    destination.mkdir(parents=True, exist_ok=True)
    coverage = int(np.count_nonzero(state['mask']))
    if coverage == 0:
        raise RuntimeError('No UV texels received source data.')
    nb_padding(state['result_base'], state['result_rough'], state['mask'], padding)
    rough_rgba = np.empty_like(state['result_base'])
    rough_rgba[:, :, :3] = state['result_rough'][:, :, None]
    rough_rgba[:, :, 3] = 1
    base_path = destination / 'T_Durumagi_FitRigV3_BaseColor.png'
    rough_path = destination / 'T_Durumagi_FitRigV3_Roughness.png'
    base_image = nb_save_image('T_Durumagi_FitRigV3_BaseColor', state['result_base'], base_path, True)
    rough_image = nb_save_image('T_Durumagi_FitRigV3_Roughness', rough_rgba, rough_path, False)
    materials = nb_connect(state['target'], state['target_uv'], base_image, rough_image) if connect_material else []
    target_layer = state['target'].data.uv_layers.get(state['target_uv'])
    if target_layer is not None:
        state['target'].data.uv_layers.active_index = state['target'].data.uv_layers.find(state['target_uv'])
        target_layer.active_render = True
    report = {
        'status': 'TRANSFERRED_REQUIRES_RENDER_REVIEW' if not state['pixels_failed'] else 'FAIL_PARTIAL_TRANSFER',
        'method': 'Target UV pixel centers -> barycentric target rest position -> BVH nearest source '
                  'triangle -> nearest-surface barycentric source UV -> bilinear linear Blender image buffer.',
        'source': state['source'].name, 'target': state['target'].name,
        'source_uv': state['source_uv'], 'target_uv': state['target_uv'],
        'source_base_image': state['source_base_image'], 'source_roughness_image': state['source_roughness_image'],
        'roughness_channel': 'G', 'normal_map': 'Not transferred; stale Principled normal links removed on material copies.',
        'base_color_path': str(base_path), 'roughness_path': str(rough_path),
        'images_packed': True, 'resolution': [state['size'], state['size']],
        'covered_uv_texels': coverage, 'covered_fraction': coverage / state['size'] ** 2,
        'pixels_sampled_including_shared_edges': state['pixels_sampled'],
        'failed_nearest_queries': state['pixels_failed'], 'padding_pixels': padding,
        'degenerate_uv_triangles': state['degenerate_uv_triangles'],
        'no_pixel_center_or_outside_uv_triangles': state['outside_uv_triangles'],
        'maximum_source_distance_m': state['maximum_source_distance_m'],
        'mean_source_distance_m': state['sum_source_distance_m'] / max(1, state['pixels_sampled'] - state['pixels_failed']),
        'samples_distance_over_20mm': state['samples_distance_over_20mm'],
        'samples_distance_over_100mm': state['samples_distance_over_100mm'],
        'new_target_materials': materials, 'elapsed_seconds': time.time() - state['started'],
        'limits': ['Nearest geometry is unambiguous computationally but can select a nearby inner face or opposite layer.',
                   'Inspect lapels, shoulder seams and front overlap visually before accepting the texture.',
                   'No normal/detail-direction rebake or Unity material validation is performed.']}
    (destination / 'nearest_surface_bake.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    state['finished'], state['report'] = True, report
    print(json.dumps({k: report[k] for k in ('status', 'base_color_path', 'roughness_path', 'covered_uv_texels')}))
    return report
