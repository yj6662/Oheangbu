"""Meshy brush source inspection/build. Run in a separate --factory-startup Blender process.

The source GLB and source-audit JSON are read-only. No production animation is authored.
"""
import argparse
import hashlib
import json
import math
import shutil
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Art/PlayerV2/MeshySources/brush-01/source_glb.glb"
SOURCE_AUDIT = SOURCE.parent / "blender-source-audit.json"
OUT = ROOT / "Art/PlayerV2/Staging/Brush"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def aim(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def lighting(scene):
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.world.color = (.2, .2, .2)
    for name, loc, energy, size in [("Key", (-2, -3, 3), 550, 3), ("Fill", (2, -1, 1), 220, 2), ("Rim", (1, 2, 2), 450, 2)]:
        data = bpy.data.lights.new(name, 'AREA'); data.energy = energy; data.shape = 'DISK'; data.size = size
        obj = bpy.data.objects.new(name, data); scene.collection.objects.link(obj); obj.location = loc; aim(obj, (0, 0, 0))
    camera_data = bpy.data.cameras.new("QA_Camera")
    camera = bpy.data.objects.new("QA_Camera", camera_data); scene.collection.objects.link(camera)
    camera_data.type = 'ORTHO'; scene.camera = camera
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    return camera


def render(scene, camera, path, point, scale, azimuth=0, width=768, height=1280):
    center = Vector(point)
    camera.location = center + Vector((math.sin(azimuth) * 4, -math.cos(azimuth) * 4, 0))
    aim(camera, center); camera.data.ortho_scale = scale
    scene.render.resolution_x = width; scene.render.resolution_y = height; scene.render.resolution_percentage = 100
    scene.render.filepath = str(path); bpy.ops.render.render(write_still=True)


def import_source():
    if not SOURCE_AUDIT.exists():
        raise RuntimeError("A source inspection audit is required before model operations.")
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(meshes) != 1:
        raise RuntimeError("Inspect the new source first: expected one audited mesh.")
    obj = meshes[0]; obj.name = 'Meshy_Brush_Source_Copy'
    audit = json.loads(SOURCE_AUDIT.read_text(encoding='utf-8-sig'))[0]
    obj.data.calc_loop_triangles()
    if len(obj.data.vertices) != audit['vertices'] or len(obj.data.loop_triangles) != audit['triangles']:
        raise RuntimeError("Source does not match the audited topology.")
    return obj


def inspect_source(obj):
    inspection = OUT / 'Inspect'; inspection.mkdir(parents=True, exist_ok=True)
    mesh = obj.data
    positions = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
    material = mesh.materials[0]
    principal = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    base_link = principal.inputs['Base Color'].links[0]
    image = base_link.from_node.image
    pixels = np.empty(len(image.pixels), dtype=np.float32); image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(image.size[1], image.size[0], 4)
    uv = mesh.uv_layers.active.data
    triangles = []
    for face in mesh.polygons:
        uvs = np.array([tuple(uv[i].uv) for i in face.loop_indices])
        center = uvs.mean(axis=0)
        rgb = pixels[min(image.size[1] - 1, max(0, int(center[1] * image.size[1]))),
                     min(image.size[0] - 1, max(0, int(center[0] * image.size[0]))), :3].tolist()
        points = positions[list(face.vertices)]
        triangles.append({'index': face.index, 'zmin': float(points[:, 2].min()), 'zmax': float(points[:, 2].max()),
                          'z': float(points[:, 2].mean()), 'rgb': rgb})
    bands = []
    for z in np.arange(-.96, .96, .04):
        faces = [t for t in triangles if z <= t['z'] < z + .04]
        if not faces: continue
        bands.append({'zrange': [float(z), float(z + .04)], 'triangles': len(faces),
                      'base_color_mean': np.mean([t['rgb'] for t in faces], axis=0).tolist()})
    report = {'source': str(SOURCE), 'sha256': digest(SOURCE), 'audit_sha256': digest(SOURCE_AUDIT),
              'vertices': len(mesh.vertices), 'triangles': len(mesh.loop_triangles),
              'bounds': [positions.min(axis=0).tolist(), positions.max(axis=0).tolist()], 'bands': bands,
              'crossing_planes': [{'z': z, 'faces': [t['index'] for t in triangles if t['zmin'] < z < t['zmax']]}
                                  for z in [-.40, -.42, -.44, -.46, -.48, -.50, -.52]]}
    (inspection / 'source-detail.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    camera = lighting(bpy.context.scene)
    render(bpy.context.scene, camera, inspection / 'Source_Front.png', (0, 0, 0), 2.1)
    render(bpy.context.scene, camera, inspection / 'Source_BristleRoot.png', (0, 0, -.43), .47, width=1024, height=1024)
    render(bpy.context.scene, camera, inspection / 'Source_BristleSide.png', (0, 0, -.57), .75,
           azimuth=math.pi / 2, width=1024, height=1024)
    print(json.dumps({'inspection': str(inspection), 'vertices': report['vertices'], 'triangles': report['triangles']}))


def split_at_ferrule(obj, cut_z, keep_upper, name):
    result = obj.copy(); result.data = obj.data.copy(); result.name = name
    bpy.context.scene.collection.objects.link(result)
    bm = bmesh.new(); bm.from_mesh(result.data)
    # glTF duplicates vertices for UV seams. Welding positions retains per-loop UVs and permits a clean cut cap.
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.000001)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                          plane_co=(0, 0, cut_z), plane_no=(0, 0, 1), dist=0.000001,
                          clear_inner=keep_upper, clear_outer=not keep_upper)
    boundary = [e for e in bm.edges if e.is_boundary and all(abs(v.co.z - cut_z) < .00001 for v in e.verts)]
    caps = bmesh.ops.holes_fill(bm, edges=boundary, sides=0).get('faces', []) if boundary else []
    uv = bm.loops.layers.uv.active
    for face in caps:
        # Caps are hidden inside the ferrule; copy the neighbouring surface UV instead of sampling an unrelated atlas region.
        for loop in face.loops:
            peers = [other for other in loop.vert.link_loops if other.face not in caps]
            if uv and peers: loop[uv].uv = peers[0][uv].uv
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(result.data); bm.free(); result.data.update()
    for polygon in result.data.polygons: polygon.use_smooth = True
    return result, len(caps), len(boundary)


def section_points(mesh, z):
    points = []
    for edge in mesh.edges:
        a, b = (mesh.vertices[i].co for i in edge.vertices)
        if (a.z - z) * (b.z - z) > 0 or abs(b.z - a.z) < 1e-9: continue
        t = (z - a.z) / (b.z - a.z)
        if 0 <= t <= 1: points.append(tuple(a.lerp(b, t)))
    return np.unique(np.round(np.array(points), 7), axis=0)


def socket(name, rig, position):
    obj = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(obj)
    obj.parent = rig; obj.location = position; obj.rotation_euler = (math.pi / 2, 0, 0)
    obj.empty_display_type = 'ARROWS'; obj.empty_display_size = .03
    return obj


def build(obj):
    detail_path = OUT / 'Inspect/source-detail.json'
    if not detail_path.exists(): raise RuntimeError("Run --inspect-only and review its renders before building.")
    detail = json.loads(detail_path.read_text(encoding='utf-8'))
    if detail['sha256'] != digest(SOURCE): raise RuntimeError("Inspected source hash changed.")
    # These coordinates follow the reviewed Source_BristleRoot/Side renders and original-UV colour bands.
    cut_z, grip_z, total_length = -.407, .62, .9
    points = section_points(obj.data, grip_z)
    xy = points[:, :2]
    fit = np.linalg.lstsq(np.column_stack([2 * xy, np.ones(len(xy))]), (xy * xy).sum(axis=1), rcond=None)[0]
    grip_source = Vector((float(fit[0]), float(fit[1]), grip_z))
    factor = total_length / (detail['bounds'][1][2] - detail['bounds'][0][2])
    rotation = Matrix.Rotation(math.pi, 4, 'X')
    def normalise(point): return rotation.to_3x3() @ (Vector(point) - grip_source) * factor
    handle, handle_caps, handle_border = split_at_ferrule(obj, cut_z, True, 'DosaBrushV2_Handle')
    bristles, bristle_caps, bristle_border = split_at_ferrule(obj, cut_z, False, 'DosaBrushV2_Bristles')
    tip_source = min(obj.data.vertices, key=lambda v: v.co.z).co.copy()
    root_section = section_points(obj.data, cut_z)
    root_source = Vector((float(np.mean(root_section[:, 0])), float(np.mean(root_section[:, 1])), cut_z))
    root, tip = normalise(root_source), normalise(tip_source)
    for mesh_obj in [handle, bristles]:
        for vertex in mesh_obj.data.vertices: vertex.co = normalise(vertex.co)
        mesh_obj.data.update()
    bpy.data.objects.remove(obj, do_unlink=True)
    rig_data = bpy.data.armatures.new('DosaBrushV2_Skeleton')
    rig = bpy.data.objects.new('DosaBrushV2_Rig', rig_data); bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    shaft = rig_data.edit_bones.new('BrushHandle'); shaft.head = (0, 0, 0); shaft.tail = (0, 0, .1)
    previous = shaft
    for i in range(6):
        bone = rig_data.edit_bones.new(f'Bristle_{i + 1:02d}')
        bone.head = root.lerp(tip, i / 6); bone.tail = root.lerp(tip, (i + 1) / 6)
        bone.parent = previous; bone.use_connect = i > 0; previous = bone
    bpy.ops.object.mode_set(mode='OBJECT')
    handle.parent = rig; bristles.parent = rig
    groups = [bristles.vertex_groups.new(name='BrushHandle')] + [bristles.vertex_groups.new(name=f'Bristle_{i + 1:02d}') for i in range(6)]
    line = tip - root; length = line.length; direction = line.normalized()
    for vertex in bristles.data.vertices:
        t = max(0, min(1, (vertex.co - root).dot(direction) / length))
        # Hidden root/cap and first 2% remain rigid. Fade into the root bristle bone before the second joint.
        root_blend = min(1, max(0, (t - .02) / .06))
        if root_blend < 1: groups[0].add([vertex.index], 1 - root_blend, 'REPLACE')
        f = min(5, t * 6); lo = int(f); hi = min(5, lo + 1); alpha = f - lo
        weights = {lo: 1 - alpha}
        weights[hi] = weights.get(hi, 0) + alpha
        for index, weight in weights.items():
            if root_blend * weight > 0: groups[index + 1].add([vertex.index], root_blend * weight, 'REPLACE')
    modifier = bristles.modifiers.new('BristleSkin', 'ARMATURE'); modifier.object = rig
    bristles.shape_key_add(name='Basis'); splay = bristles.shape_key_add(name='BristleSplay')
    for vertex, deformed in zip(bristles.data.vertices, splay.data):
        t = max(0, min(1, (vertex.co - root).dot(direction) / length))
        center = root + direction * (t * length)
        radial = vertex.co - center
        # Ends and hidden root stay fixed; only radial width changes. No bone scale or length compensation.
        fade = math.sin(math.pi * t) ** 2 * min(1, max(0, (t - .04) / .08))
        deformed.co = vertex.co + radial * (.35 * fade)
    grip = socket('GripSocket', rig, Vector((0, 0, 0)))
    tip_socket = socket('TipSocket', rig, tip)
    # Keep the endpoint as a child of the last deform bone, while preserving its explicit world pose.
    bpy.context.view_layer.update(); tip_matrix = tip_socket.matrix_world.copy()
    tip_socket.parent_type = 'BONE'; tip_socket.parent_bone = 'Bristle_06'
    bpy.context.view_layer.update(); tip_socket.matrix_world = tip_matrix
    bpy.context.view_layer.update()
    for mesh_obj in [handle, bristles]:
        mesh_obj.data.calc_loop_triangles()
    triangles = sum(len(o.data.loop_triangles) for o in [handle, bristles])
    if triangles > 5000: raise RuntimeError(f"Brush exceeds 5k triangle budget: {triangles}")
    textures = OUT / 'Textures'; textures.mkdir(parents=True, exist_ok=True)
    files = {'T_0_base_color.png': 'T_DosaBrushV2_BaseColor.png', 'T_0_normal.png': 'T_DosaBrushV2_Normal.png',
             'T_0_metallic.png': 'T_DosaBrushV2_Metallic.png', 'T_0_roughness.png': 'T_DosaBrushV2_Roughness.png'}
    for source, target in files.items(): shutil.copy2(SOURCE.parent / source, textures / target)
    material = handle.data.materials[0]; material.name = 'M_DosaBrushV2_Source'
    nodes = material.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].links[0].from_node.image.name = 'T_DosaBrushV2_BaseColor'
    for image in bpy.data.images:
        if image.name == 'Render Result' or not image.has_data: continue
        image.pack()
    report = {'source': str(SOURCE), 'source_sha256': digest(SOURCE), 'source_audit': str(SOURCE_AUDIT),
              'inspection': str(detail_path), 'blender_version': bpy.app.version_string,
              'status': 'STATIC_RIG_CANDIDATE_NOT_RIG_PASS', 'production_actions': 0,
              'source_cut_z': cut_z, 'source_grip_center': list(grip_source), 'scale_factor': factor,
              'total_length_m': total_length, 'grip_to_tip_vector_blender_local': list(tip),
              'grip_to_tip_distance_m': tip.length, 'bristle_root_blender_local': list(root),
              'bristle_arc_length_m': length, 'segment_lengths_m': [length / 6] * 6,
              'rigid_handle_triangles': len(handle.data.loop_triangles), 'bristle_triangles': len(bristles.data.loop_triangles),
              'triangles': triangles, 'rigid_handle_vertices': len(handle.data.vertices), 'bristle_vertices': len(bristles.data.vertices),
              'cut_cap_faces': {'handle': handle_caps, 'bristles': bristle_caps},
              'cut_boundary_edges': {'handle': handle_border, 'bristles': bristle_border},
              'grip_radial_profile_m': {str(percentile): float(np.percentile(np.linalg.norm(xy - fit[:2], axis=1) * factor, percentile))
                                      for percentile in [0, 10, 50, 90, 100]},
              'shaft_skinning': 'none; rigid MeshRenderer', 'bristle_influences_max': max(len(v.groups) for v in bristles.data.vertices),
              'bones': ['BrushHandle'] + [f'Bristle_{i + 1:02d}' for i in range(6)], 'splay_shape': 'BristleSplay',
              'axis_contract': 'Brush mesh points Blender +Z / exported Unity +Y. GripSocket local +Y points toward tip.',
              'scope': 'Source UV and textures preserved on surviving/cut surface. Original UV seams welded; hidden cut caps added. No generated texture or production animation.'}
    bpy.ops.file.pack_all()
    scene = bpy.context.scene; camera = lighting(scene)
    qa = OUT / 'QA'; qa.mkdir(parents=True, exist_ok=True)
    center = (Vector((0, 0, -((detail['bounds'][1][2] - grip_z) * factor))) + tip) * .5
    render(scene, camera, qa / 'Brush_Rest_Front.png', center, 1.0)
    render(scene, camera, qa / 'Brush_Rest_Bristles.png', root.lerp(tip, .5), length * 1.25, width=1024, height=1024)
    poses = []
    for degrees, angle, spread, label in [(28, 0, .65, 'Bend_Right'), (28, math.pi / 2, .65, 'Bend_Forward'),
                                          (28, math.pi, .65, 'Bend_Left'), (0, 0, 1, 'Splay_Max')]:
        bend_axis = Vector((math.cos(angle), math.sin(angle), 0)).cross(direction).normalized()
        position = root.copy()
        for i in range(6):
            bone = rig_data.bones[f'Bristle_{i + 1:02d}']; pose = rig.pose.bones[bone.name]
            q = Quaternion(bend_axis, math.radians(degrees) * (i / 5) ** 1.7)
            pose.matrix = Matrix.Translation(position) @ q.to_matrix().to_4x4() @ bone.matrix_local.to_quaternion().to_matrix().to_4x4()
            bpy.context.view_layer.update()
            position += q @ (line / 6)
        splay.value = spread; bpy.context.view_layer.update()
        actual = tip_socket.matrix_world.translation
        drift = max(abs((rig.pose.bones[f'Bristle_{i + 1:02d}'].tail - rig.pose.bones[f'Bristle_{i + 1:02d}'].head).length - length / 6) for i in range(6))
        poses.append({'pose': label, 'bend_degrees': degrees, 'splay': spread, 'max_segment_drift_m': drift,
                      'tip_socket_vs_computed_endpoint_m': (actual - position).length})
        if drift > .00001 or (actual - position).length > .00001:
            raise RuntimeError(f'Static bone/tip pose contract failed: {poses[-1]}, actual={tuple(actual)}, computed={tuple(position)}')
        render(scene, camera, qa / (label + '.png'), root.lerp(tip, .5), length * 1.4, width=1024, height=1024)
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    splay.value = 0; bpy.context.view_layer.update()
    report['static_pose_checks'] = poses
    blend_path = ROOT / 'Art/PlayerV2/DosaBrushV2.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    bpy.ops.object.select_all(action='DESELECT')
    for export_object in [rig, handle, bristles, grip, tip_socket]: export_object.select_set(True)
    bpy.context.view_layer.objects.active = rig
    fbx_path = OUT / 'SM_DosaBrushV2.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
        add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False, path_mode='COPY', embed_textures=True)
    report['outputs'] = [{'path': str(p), 'sha256': digest(p)} for p in [blend_path, fbx_path]]
    if digest(SOURCE) != detail['sha256']: raise RuntimeError('Source unexpectedly changed.')
    (OUT / 'brush-build-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'brush_report': str(OUT / 'brush-build-report.json'), 'triangles': triangles,
                      'grip_to_tip_m': tip.length, 'grip_radius_median_m': report['grip_radial_profile_m']['50']}))


def validate_export():
    """Re-import exported FBX in a factory scene; test actual skinned endpoint, not only socket coincidence."""
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    fbx_path = OUT / 'SM_DosaBrushV2.fbx'
    source_report = json.loads((OUT / 'brush-build-report.json').read_text(encoding='utf-8'))
    bpy.ops.import_scene.fbx(filepath=str(fbx_path), automatic_bone_orientation=False, use_anim=False)
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    handle = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and 'Handle' in o.name)
    bristles = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and 'Bristles' in o.name)
    grip = bpy.data.objects.get('GripSocket'); tip = bpy.data.objects.get('TipSocket')
    if grip is None or tip is None: raise RuntimeError('FBX lost GripSocket or TipSocket objects.')
    chain = [rig.pose.bones[f'Bristle_{i + 1:02d}'] for i in range(6)]
    rest = [p.bone.matrix_local.copy() for p in chain]
    root = rest[0].translation.copy()
    endpoint = rig.matrix_world.inverted() @ tip.matrix_world.translation
    line = endpoint - root
    rest_lengths = [(chain[i + 1].head - chain[i].head).length if i < 5 else (endpoint - chain[i].head).length for i in range(6)]
    shape = bristles.data.shape_keys.key_blocks.get('BristleSplay') if bristles.data.shape_keys else None
    if shape is None: raise RuntimeError('FBX lost BristleSplay shape.')
    if any(p.parent != chain[i - 1] for i, p in enumerate(chain) if i): raise RuntimeError('FBX bristle chain is not direct.')
    if any(mod.type == 'ARMATURE' for mod in handle.modifiers): raise RuntimeError('Rigid handle became skinned.')
    bristles_to_rig = rig.matrix_world.inverted() @ bristles.matrix_world
    tip_index = min(range(len(bristles.data.vertices)), key=lambda i: (bristles_to_rig @ bristles.data.vertices[i].co - endpoint).length)
    if (bristles_to_rig @ bristles.data.vertices[tip_index].co - endpoint).length > 1e-5:
        raise RuntimeError('TipSocket no longer matches a physical bristle vertex after export.')
    weight_error = max(abs(sum(g.weight for g in v.groups) - 1) for v in bristles.data.vertices)
    max_weights = max(len(v.groups) for v in bristles.data.vertices)
    if weight_error > 1e-4 or max_weights > 4: raise RuntimeError('Exported bristle weights failed.')
    shape_tip_delta = (shape.data[tip_index].co - bristles.data.vertices[tip_index].co).length
    if shape_tip_delta > 1e-6: raise RuntimeError('Splay moves the physical tip off its centerline.')
    root_indices = [v.index for v in bristles.data.vertices if (bristles_to_rig @ v.co - root).dot(line.normalized()) < .003]
    root_shape_delta = max((shape.data[i].co - bristles.data.vertices[i].co).length for i in root_indices)
    if root_shape_delta > 1e-6: raise RuntimeError('Splay moves bristle root vertices.')
    points = [o.matrix_world @ v.co for o in [handle, bristles] for v in o.data.vertices]
    measured_total = max(p.z for p in points) - min(p.z for p in points)
    if abs(measured_total - .9) > .00001: raise RuntimeError('FBX changed total physical size.')
    axis_dot = (grip.matrix_world.to_3x3() @ Vector((0, 1, 0))).normalized().dot((tip.matrix_world.translation - grip.matrix_world.translation).normalized())
    if axis_dot < .999: raise RuntimeError('GripSocket +Y is not directed toward the tip.')
    checks = []
    for i in range(8):
        direction = Vector((math.cos(i * math.pi / 4), math.sin(i * math.pi / 4), 0))
        axis = direction.cross(line.normalized()).normalized()
        position = root.copy()
        for j, pose in enumerate(chain):
            q = Quaternion(axis, math.radians(28) * (j / 5) ** 1.7)
            pose.matrix = Matrix.Translation(position) @ q.to_matrix().to_4x4() @ rest[j].to_quaternion().to_matrix().to_4x4()
            bpy.context.view_layer.update()
            segment = rest[j + 1].translation - rest[j].translation if j < 5 else endpoint - rest[j].translation
            position += q @ segment
        shape.value = .65; bpy.context.view_layer.update()
        evaluated = bristles.evaluated_get(bpy.context.evaluated_depsgraph_get()); evaluated_mesh = evaluated.to_mesh()
        skin_tip = evaluated.matrix_world @ evaluated_mesh.vertices[tip_index].co
        actual_tip = tip.matrix_world.translation
        drift = max(abs((chain[j + 1].head - chain[j].head).length - rest_lengths[j]) if j < 5
                    else abs((rig.matrix_world.inverted() @ actual_tip - chain[j].head).length - rest_lengths[j]) for j in range(6))
        checks.append({'direction_degrees': i * 45, 'skin_tip_to_socket_m': (skin_tip - actual_tip).length,
                       'socket_to_computed_m': (actual_tip - rig.matrix_world @ position).length, 'max_segment_drift_m': drift})
        evaluated.to_mesh_clear()
        if checks[-1]['skin_tip_to_socket_m'] > .00002 or checks[-1]['socket_to_computed_m'] > .00002 or drift > .00002:
            raise RuntimeError(f'Exported skin/bone/tip mismatch: {checks[-1]}')
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    shape.value = 0; bpy.context.view_layer.update()
    handle_in_grip = grip.matrix_world.inverted() @ handle.matrix_world
    grip_points = [handle_in_grip @ v.co for v in handle.data.vertices]
    capsule_sections = []
    for longitudinal in np.linspace(-.05, .05, 13):
        radii = []
        for edge in handle.data.edges:
            a, b = (grip_points[index] for index in edge.vertices)
            if (a.y - longitudinal) * (b.y - longitudinal) > 0 or abs(b.y - a.y) < 1e-9: continue
            t = (longitudinal - a.y) / (b.y - a.y)
            if 0 <= t <= 1:
                point = a.lerp(b, t); radii.append(math.sqrt(point.x ** 2 + point.z ** 2))
        if radii:
            capsule_sections.append({'grip_local_y_m': float(longitudinal), 'radial_min_m': min(radii),
                                     'radial_median_m': float(np.median(radii)), 'radial_max_m': max(radii)})
    triangles = 0
    for obj in [handle, bristles]: obj.data.calc_loop_triangles(); triangles += len(obj.data.loop_triangles)
    if triangles != source_report['triangles'] or triangles > 5000: raise RuntimeError('FBX topology budget drifted.')
    report = {'fbx_sha256': digest(fbx_path), 'blender_version': bpy.app.version_string, 'status': 'STATIC_EXPORT_CHECKS_PASS_NOT_CHARACTER_RIG_PASS',
              'model_dimensions_m': measured_total, 'total_triangles': triangles, 'grip_up_dot_tip_direction': axis_dot,
              'bones': [b.name for b in rig.data.bones], 'objects': [{'name': o.name, 'type': o.type} for o in bpy.context.scene.objects],
              'bristle_shape_names': [s.name for s in bristles.data.shape_keys.key_blocks], 'weight_sum_error': weight_error,
              'max_bristle_weights': max_weights, 'splay_physical_tip_delta_m': shape_tip_delta, 'splay_root_delta_m': root_shape_delta,
              'physical_endpoint_vertex': tip_index, 'direct_pose_checks': checks, 'actions': len(bpy.data.actions),
              'grip_surface_sections': capsule_sections,
              'grip_capsule_enclosing_radius_m': max(s['radial_max_m'] for s in capsule_sections),
              'grip_capsule_cylinder_interval_y_m': [-.05, .05],
              'recipe_sha256': digest(Path(__file__)),
              'no_unity_import_claim': True}
    (OUT / 'brush-fbx-validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'fbx_validation': str(OUT / 'brush-fbx-validation.json'), 'triangles': triangles,
                      'worst_skin_tip_error_m': max(c['skin_tip_to_socket_m'] for c in checks)}))


def main():
    import sys
    parser = argparse.ArgumentParser()
    parser.add_argument('--inspect-only', action='store_true')
    parser.add_argument('--validate-export', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.validate_export:
        validate_export(); return
    obj = import_source()
    if args.inspect_only:
        inspect_source(obj)
    else:
        build(obj)


if __name__ == '__main__':
    main()
