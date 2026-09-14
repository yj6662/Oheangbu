"""Inspect/refine the generated pack in a separate Blender background process; never writes raw sources."""
import argparse
import hashlib
import json
import math
import shutil
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion, kdtree

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / 'Art/PlayerV2/MeshySources/backpack-01/source_glb.glb'
OUT = ROOT / 'Art/PlayerV2/Staging/Backpack'


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def import_source():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(meshes) != 1: raise RuntimeError('Inspect multi-mesh source before proceeding.')
    return meshes[0]


def inspect_source(obj):
    OUT.mkdir(parents=True, exist_ok=True); inspection = OUT / 'Inspect'; inspection.mkdir(exist_ok=True)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
    bm.verts.ensure_lookup_table(); bm.verts.index_update()
    pending = set(bm.verts); components = []
    while pending:
        seed = pending.pop(); found = {seed}; stack = [seed]
        while stack:
            vertex = stack.pop()
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in pending: pending.remove(other); found.add(other); stack.append(other)
        points = np.array([tuple(v.co) for v in found])
        faces = set(f for v in found for f in v.link_faces)
        components.append({'vertices': len(found), 'faces': len(faces), 'bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()]})
    bm.free(); components.sort(key=lambda c: c['faces'], reverse=True)
    points = np.array([tuple(v.co) for v in obj.data.vertices]); obj.data.calc_loop_triangles()
    report = {'source_sha256': digest(SOURCE), 'vertices': len(obj.data.vertices), 'triangles': len(obj.data.loop_triangles),
              'bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()], 'welded_components': components,
              'materials': [m.name for m in obj.data.materials]}
    (inspection / 'source-detail.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    import importlib.util
    spec = importlib.util.spec_from_file_location('brush_qa_helpers', Path(__file__).with_name('build_brush.py'))
    helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(helpers)
    scene = bpy.context.scene; camera = helpers.lighting(scene)
    center = (points.min(axis=0) + points.max(axis=0)) * .5
    scale = float(points[:, 2].max() - points[:, 2].min()) * 1.12
    helpers.render(scene, camera, inspection / 'Source_MinusY.png', center, scale, width=1024, height=1536)
    helpers.render(scene, camera, inspection / 'Source_PlusY.png', center, scale, azimuth=math.pi, width=1024, height=1536)
    helpers.render(scene, camera, inspection / 'Source_Side.png', center, scale, azimuth=math.pi / 2, width=1024, height=1536)
    helpers.render(scene, camera, inspection / 'Source_Lower_MinusY.png', (center[0], center[1], -.45), 1.2, width=1200, height=1000)
    print(json.dumps({'inspection': str(inspection), 'triangles': report['triangles'], 'components': len(components),
                      'largest': components[:6], 'bounds': report['bounds']}))


def semantic_part(point):
    x, y, z = point
    # Reviewed MinusY and lower-front render coordinates. Jars take priority over nearby suspension straps.
    boxes = [
        ('Bottle_Black_L', (-.43, -.245, -.105, -.365, -.085)),
        ('Bottle_Black_M', (-.265, -.080, -.095, -.415, -.055)),
        ('Bottle_Black_R', (.015, .225, -.075, -.450, -.110)),
        ('Bottle_Brown', (.185, .405, -.070, -.565, -.220)),
        ('Bottle_Gold_Low', (.180, .435, .180, -.895, -.605)),
    ]
    for name, (xmin, xmax, ymax, zmin, zmax) in boxes:
        if xmin <= x <= xmax and y < ymax and zmin <= z <= zmax: return name
    if -.96 < z < -.62 and -.02 < x < .07: return 'Cord_Center'
    if -.97 < z < -.68 and .125 < x < .235: return 'Cord_Right'
    if -.94 < z < -.65 and -.63 < x < -.515: return 'Cord_Left'
    if -.92 < z < -.545 and -.59 < x < -.34: return 'Tube_Low_L'
    if -.85 < z < -.53 and -.345 < x < -.195: return 'Tube_Low_M'
    if -.77 < z < -.535 and -.24 < x < -.075: return 'Pendant'
    if -.65 < z < -.03 and abs(x - (.55 - .105 * z)) < .06 and x > .52 - .10 * z and y < .11: return 'Tube_Outer_R'
    if -.90 < z < .65 and abs(x - (.44 - .10 * z)) < .095 and y < .115: return 'Tube_Long_R'
    if -.64 < z < .62 and abs(x - (-.46 + .17 * z)) < .117 and y < .145: return 'Tube_Long_L'
    if -.25 < z < .85 and -.18 < x < .225 and y < -.055: return 'BrushBundle'
    return 'Backpanel'


def material_audit(obj):
    material = obj.data.materials[0]; node = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    def find_image(socket):
        for link in socket.links:
            upstream = link.from_node
            if upstream.type == 'TEX_IMAGE': return upstream.image
            for input_socket in upstream.inputs:
                image = find_image(input_socket)
                if image: return image
    arrays = {}
    for channel in ['Base Color', 'Roughness', 'Metallic']:
        image = find_image(node.inputs[channel])
        if image:
            values = np.empty(len(image.pixels), dtype=np.float32); image.pixels.foreach_get(values)
            arrays[channel] = values.reshape(image.size[1], image.size[0], 4)
    rows = []
    for face in obj.data.polygons:
        uv = np.mean([tuple(obj.data.uv_layers.active.data[i].uv) for i in face.loop_indices], axis=0)
        samples = {}
        for channel, array in arrays.items():
            samples[channel] = array[min(array.shape[0] - 1, int(uv[1] * array.shape[0])), min(array.shape[1] - 1, int(uv[0] * array.shape[1]))].tolist()
        rows.append({'index': face.index, 'center': list(face.center), 'part': semantic_part(face.center), 'samples': samples})
    (OUT / 'Inspect/face-material-samples.json').write_text(json.dumps(rows), encoding='utf-8')
    print(json.dumps({'roughness_links': [str(l) for l in node.inputs['Roughness'].links], 'rows': len(rows)}))


def vessel_envelope(name, point):
    profiles = {
        'Bottle_Black_L': (-.320, -.148, [(-.35,.030),(-.30,.075),(-.235,.079),(-.19,.045),(-.10,.024),(-.075,.025)]),
        'Bottle_Black_M': (-.171, -.181, [(-.40,.025),(-.34,.085),(-.275,.085),(-.205,.040),(-.075,.025),(-.045,.026)]),
        'Bottle_Black_R': (.117, -.181, [(-.455,.025),(-.395,.082),(-.335,.094),(-.255,.055),(-.15,.028),(-.09,.03)]),
        'Bottle_Brown': (.280, -.164, [(-.52,.030),(-.465,.080),(-.40,.089),(-.335,.062),(-.28,.043),(-.215,.026)]),
        'Bottle_Gold_Low': (.294, -.11, [(-.845,.028),(-.79,.077),(-.735,.098),(-.69,.080),(-.635,.054),(-.555,.025)]),
    }
    cx, cy, profile = profiles[name]; x, y, z = point
    if z < profile[0][0] - .012 or z > profile[-1][0] + .012: return False
    radius = float(np.interp(z, [p[0] for p in profile], [p[1] for p in profile])) + .022
    return abs(x - cx) < radius and abs(y - cy) < radius


def segment_preview(obj):
    detail_path = OUT / 'Inspect/source-detail.json'
    detail = json.loads(detail_path.read_text(encoding='utf-8'))
    if digest(SOURCE) != detail['source_sha256']: raise RuntimeError('Inspected source changed.')
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
    bm.faces.ensure_lookup_table(); bm.faces.index_update()
    layer = bm.faces.layers.int.new('SemanticPart')
    names = ['Backpanel', 'BrushBundle', 'Bottle_Black_L', 'Bottle_Black_M', 'Bottle_Black_R', 'Bottle_Brown', 'Bottle_Gold_Low',
             'Tube_Long_L', 'Tube_Long_R', 'Tube_Outer_R', 'Tube_Low_L', 'Tube_Low_M', 'Pendant', 'Cord_Center', 'Cord_Right', 'Cord_Left']
    for face in bm.faces: face[layer] = names.index(semantic_part(face.calc_center_median()))
    # The generated ORM distinguishes low-roughness glazed vessels from the fused coarse cloth/straps.
    # Keep actual source glaze plus only immediately adjoining wrap geometry, not rectangular background islands.
    material_rows = json.loads((OUT / 'Inspect/face-material-samples.json').read_text(encoding='utf-8'))
    tube_index = names.index('Tube_Long_L')
    tube_candidates = [face for face in bm.faces if face[layer] == tube_index]
    tube_surface = [face for face in tube_candidates if material_rows[face.index]['samples']['Roughness'][1] < .5
                    or np.mean(material_rows[face.index]['samples']['Base Color'][:3]) > .40]
    tube_vertices = list(set(v for face in tube_surface for v in face.verts))
    tube_tree = kdtree.KDTree(len(tube_vertices))
    for vertex_index, vertex in enumerate(tube_vertices): tube_tree.insert(vertex.co, vertex_index)
    tube_tree.balance()
    for face in tube_candidates:
        if face not in tube_surface and not all(tube_tree.find(v.co)[2] < .034 for v in face.verts): face[layer] = names.index('Backpanel')
    for name in names:
        if not name.startswith('Bottle'): continue
        index = names.index(name); candidates = [f for f in bm.faces if f[layer] == index]
        glaze = [f for f in candidates if material_rows[f.index]['samples']['Roughness'][1] < .5]
        seed_vertices = list(set(v for face in glaze for v in face.verts))
        tree = kdtree.KDTree(len(seed_vertices))
        for vertex_index, vertex in enumerate(seed_vertices): tree.insert(vertex.co, vertex_index)
        tree.balance()
        for face in candidates:
            close_wrap = all(tree.find(v.co)[2] < .026 for v in face.verts)
            x, y, z = face.calc_center_median()
            brown_neck = name == 'Bottle_Brown' and -.335 < z < -.205 and abs(x - .277) < .067 and y < -.11
            if face not in glaze and not close_wrap and not brown_neck: face[layer] = names.index('Backpanel')
    parts = []
    for index, name in enumerate(names):
        part_bm = bm.copy(); copy_layer = part_bm.faces.layers.int.get('SemanticPart')
        remove = [f for f in part_bm.faces if f[copy_layer] != index]
        bmesh.ops.delete(part_bm, geom=remove, context='FACES')
        dangling = [v for v in part_bm.verts if not v.link_faces]
        if dangling: bmesh.ops.delete(part_bm, geom=dangling, context='VERTS')
        if not part_bm.faces: raise RuntimeError('Empty semantic part: ' + name)
        if name.startswith('Bottle'):
            scraps = [face for face in part_bm.faces if any(not vessel_envelope(name, vertex.co) for vertex in face.verts)]
            if scraps: bmesh.ops.delete(part_bm, geom=scraps, context='FACES')
        if name.startswith('Bottle') or name.startswith('Tube') or name == 'Pendant':
            pending = set(part_bm.faces); islands = []
            while pending:
                seed = pending.pop(); island = {seed}; stack = [seed]
                while stack:
                    face = stack.pop()
                    for edge in face.edges:
                        for neighbour in edge.link_faces:
                            if neighbour in pending: pending.remove(neighbour); island.add(neighbour); stack.append(neighbour)
                islands.append(island)
            largest = max(islands, key=len)
            detached = [face for face in part_bm.faces if face not in largest]
            if detached: bmesh.ops.delete(part_bm, geom=detached, context='FACES')
        caps = bmesh.ops.holes_fill(part_bm, edges=[e for e in part_bm.edges if e.is_boundary], sides=0).get('faces', [])
        uv = part_bm.loops.layers.uv.active
        for face in caps:
            for loop in face.loops:
                peers = [other for other in loop.vert.link_loops if other.face not in caps]
                if uv and peers: loop[uv].uv = peers[0][uv].uv
        unused = [vertex for vertex in part_bm.verts if not vertex.link_faces]
        if unused: bmesh.ops.delete(part_bm, geom=unused, context='VERTS')
        bmesh.ops.recalc_face_normals(part_bm, faces=list(part_bm.faces))
        mesh = bpy.data.meshes.new('Pack_' + name); part_bm.to_mesh(mesh); part_bm.free(); mesh.update()
        part = bpy.data.objects.new('Pack_' + name, mesh); bpy.context.scene.collection.objects.link(part)
        for material in obj.data.materials: mesh.materials.append(material)
        mesh.calc_loop_triangles(); points = np.array([tuple(v.co) for v in mesh.vertices])
        parts.append({'object': part, 'name': name, 'caps': len(caps), 'bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()],
                      'triangles': len(mesh.loop_triangles)})
    bm.free(); bpy.data.objects.remove(obj, do_unlink=True)
    report = {'status': 'SEGMENTATION_PREVIEW_NOT_FINAL', 'source_sha256': digest(SOURCE),
              'parts': [{k: v for k, v in p.items() if k != 'object'} for p in parts]}
    (OUT / 'segmentation-preview.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    import importlib.util
    spec = importlib.util.spec_from_file_location('brush_qa_helpers', Path(__file__).with_name('build_brush.py'))
    helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(helpers)
    scene = bpy.context.scene; camera = helpers.lighting(scene)
    qa = OUT / 'QA'; qa.mkdir(exist_ok=True)
    helpers.render(scene, camera, qa / 'Segmentation_Assembled.png', (0, 0, 0), 2.15, width=1024, height=1536)
    for selected in parts:
        if selected['name'] in ['Backpanel', 'BrushBundle'] or selected['name'].startswith('Cord'): continue
        for other in parts: other['object'].hide_render = other != selected
        lo, hi = map(Vector, selected['bounds']); center = (lo + hi) * .5
        helpers.render(scene, camera, qa / (selected['name'] + '_Isolated.png'), center, max(hi.z - lo.z, hi.x - lo.x) * 1.45,
                       width=768, height=768)
    for part in parts: part['object'].hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'segmentation-preview.blend'))
    print(json.dumps({'preview': str(OUT / 'segmentation-preview.json'), 'parts': [(p['name'], p['triangles']) for p in parts]}))


def build_final():
    preview_report = json.loads((OUT / 'segmentation-preview.json').read_text(encoding='utf-8'))
    detail = json.loads((OUT / 'Inspect/source-detail.json').read_text(encoding='utf-8'))
    if digest(SOURCE) != preview_report['source_sha256']: raise RuntimeError('Source changed since reviewed segmentation.')
    bpy.ops.wm.open_mainfile(filepath=str(OUT / 'segmentation-preview.blend'))
    for obj in list(bpy.context.scene.objects):
        if obj.type != 'MESH': bpy.data.objects.remove(obj, do_unlink=True)
    objects = {part['name']: bpy.data.objects['Pack_' + part['name']] for part in preview_report['parts']}
    bpy.ops.object.select_all(action='DESELECT')
    decimation = {}; topology_cleanup = {}
    for name, obj in objects.items():
        ratio = .63 if name == 'Backpanel' else .60 if name == 'BrushBundle' else .90 if name in ['Tube_Long_L', 'Tube_Long_R'] else 1
        if ratio < 1:
            obj.data.calc_loop_triangles(); before = len(obj.data.loop_triangles)
            bpy.context.view_layer.objects.active = obj; obj.select_set(True)
            modifier = obj.modifiers.new('LOD0Budget', 'DECIMATE'); modifier.ratio = ratio; modifier.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=modifier.name); obj.select_set(False)
            obj.data.calc_loop_triangles(); decimation[name] = {'input_triangles': before, 'output_triangles': len(obj.data.loop_triangles), 'ratio': ratio}
        triangulated = bmesh.new(); triangulated.from_mesh(obj.data)
        bmesh.ops.triangulate(triangulated, faces=list(triangulated.faces))
        zero_area = [face for face in triangulated.faces if face.calc_area() < 1e-10]
        if zero_area: bmesh.ops.delete(triangulated, geom=zero_area, context='FACES')
        unused = [vertex for vertex in triangulated.verts if not vertex.link_faces]
        if unused: bmesh.ops.delete(triangulated, geom=unused, context='VERTS')
        triangulated.to_mesh(obj.data); triangulated.free(); obj.data.update()
        # Triangulated cut caps can overlap an existing triangle. FBX import
        # rejects those duplicate faces, so remove them here before recording
        # the authored topology and exporting the reproducible mesh.
        before_validation = len(obj.data.polygons)
        validated = obj.data.validate(verbose=False, clean_customdata=False)
        obj.data.update()
        topology_cleanup[name] = {'zero_area_faces_removed': len(zero_area),
                                  'native_validation_changed': validated,
                                  'duplicate_or_invalid_faces_removed': before_validation - len(obj.data.polygons)}
        for face in obj.data.polygons: face.use_smooth = True
    minimum, maximum = map(Vector, detail['bounds'])
    scale = .64 / (maximum.z - minimum.z)
    anchor = Vector(((minimum.x + maximum.x) * .5, maximum.y, 0))
    rotation = Matrix.Rotation(math.pi, 3, 'Z')
    for name, obj in objects.items():
        obj.name = 'DosaPackV2_' + name
        for vertex in obj.data.vertices: vertex.co = rotation @ (vertex.co - anchor) * scale
        obj.data.update()
    rig_data = bpy.data.armatures.new('DosaPackV2_Skeleton')
    rig = bpy.data.objects.new('DosaPackV2_Rig', rig_data); bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig; rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    root = rig_data.edit_bones.new('PackRoot'); root.head = (0, 0, 0); root.tail = (0, 0, .06)
    records = []
    for name, obj in objects.items():
        points = np.array([tuple(v.co) for v in obj.data.vertices])
        highest = float(points[:, 2].max()); lowest = float(points[:, 2].min())
        top = points[points[:, 2] > highest - .008]
        pivot = Vector((float(np.median(top[:, 0])), float(np.median(top[:, 1])), highest))
        bone_names = []
        if name == 'Backpanel': bone_names = ['PackRoot']
        elif name.startswith('Cord'):
            bottom = points[points[:, 2] < lowest + .008]
            tail = Vector((float(np.median(bottom[:, 0])), float(np.median(bottom[:, 1])), lowest))
            previous = root
            for index in range(3):
                bone = rig_data.edit_bones.new(f'PackAux_{name}_{index + 1:02d}')
                bone.head = pivot.lerp(tail, index / 3); bone.tail = pivot.lerp(tail, (index + 1) / 3)
                bone.parent = previous; bone.use_connect = index > 0; previous = bone; bone_names.append(bone.name)
        else:
            bone = rig_data.edit_bones.new('PackAux_' + name); bone.head = pivot
            bone.tail = pivot - Vector((0, 0, min(.06, max(.012, (highest - lowest) * .25))))
            bone.parent = root; bone_names = [bone.name]
        records.append({'name': name, 'object': obj.name, 'bones': bone_names, 'pivot_local': list(pivot),
                        'kind': 'skinned_cord_3_bones' if name.startswith('Cord') else 'fixed' if name in ['Backpanel','BrushBundle'] else 'rigid_dangle',
                        'bounds': [points.min(axis=0).tolist(), points.max(axis=0).tolist()]})
    bpy.ops.object.mode_set(mode='OBJECT')
    for record in records:
        obj = objects[record['name']]
        if record['kind'] == 'skinned_cord_3_bones':
            obj.parent = rig
            groups = [obj.vertex_groups.new(name=name) for name in record['bones']]
            start = rig_data.bones[record['bones'][0]].head_local
            end = rig_data.bones[record['bones'][-1]].tail_local
            line = end - start
            for vertex in obj.data.vertices:
                t = min(2, max(0, (vertex.co - start).dot(line) / line.length_squared * 3))
                lo, hi = int(t), min(2, int(t) + 1); alpha = t - lo
                if lo == hi: groups[lo].add([vertex.index], 1, 'REPLACE')
                else:
                    if 1-alpha > 0: groups[lo].add([vertex.index], 1-alpha, 'REPLACE')
                    if alpha > 0: groups[hi].add([vertex.index], alpha, 'REPLACE')
            modifier = obj.modifiers.new('CordSkin', 'ARMATURE'); modifier.object = rig
        else:
            matrix = obj.matrix_world.copy(); obj.parent = rig; obj.parent_type = 'BONE'; obj.parent_bone = record['bones'][0]
            bpy.context.view_layer.update(); obj.matrix_world = matrix
        obj.data.calc_loop_triangles(); record['vertices'] = len(obj.data.vertices); record['triangles'] = len(obj.data.loop_triangles)
    bpy.context.view_layer.update()
    triangles = sum(record['triangles'] for record in records)
    if triangles > 10000: raise RuntimeError(f'Pack LOD0 exceeds 10k triangles: {triangles}')
    material = next(iter(objects.values())).data.materials[0]; material.name = 'M_DosaPackV2_Source'
    textures = OUT / 'Textures'; textures.mkdir(exist_ok=True)
    texture_outputs = []
    for source, label in [('base_color','BaseColor'),('normal','Normal'),('metallic','Metallic'),('roughness','Roughness')]:
        destination = textures / f'T_DosaPackV2_{label}.png'; shutil.copy2(SOURCE.parent / f'T_0_{source}.png', destination)
        texture_outputs.append({'path': str(destination), 'sha256': digest(destination)})
    bpy.ops.file.pack_all()
    import importlib.util
    spec = importlib.util.spec_from_file_location('brush_qa_helpers', Path(__file__).with_name('build_brush.py'))
    helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(helpers)
    scene = bpy.context.scene; camera = helpers.lighting(scene); qa = OUT / 'QA'
    helpers.render(scene, camera, qa / 'Pack_Rest_Front.png', (0, .075, 0), .75, azimuth=math.pi, width=1024, height=1536)
    helpers.render(scene, camera, qa / 'Pack_Rest_Side.png', (0, .075, 0), .75, azimuth=math.pi/2, width=1024, height=1536)
    helpers.render(scene, camera, qa / 'Pack_Rest_Lower.png', (0, .08, -.15), .5, azimuth=math.pi, width=1200, height=1024)
    rest_matrices = {obj.name: obj.matrix_world.copy() for obj in objects.values()}
    checks = []
    for angle, label in [(10, 'Swing_Right'), (-10, 'Swing_Left'), (15, 'Lift_Out')]:
        for record in records:
            if record['kind'] == 'fixed': continue
            for index, bone_name in enumerate(record['bones']):
                pose = rig.pose.bones[bone_name]
                axis = Vector((1,0,0)) if label == 'Lift_Out' else Vector((0,0,1))
                degrees = angle * (.6 if record['name'].startswith('Tube_Long') else 1) / len(record['bones'])
                pose.rotation_mode = 'QUATERNION'; pose.rotation_quaternion = Quaternion(axis, math.radians(degrees))
        bpy.context.view_layer.update()
        worst_edge_drift = 0; worst_joint_drift = 0
        for record in records:
            obj = objects[record['name']]
            if record['kind'] != 'skinned_cord_3_bones':
                for edge in obj.data.edges:
                    a, b = (obj.data.vertices[index].co for index in edge.vertices)
                    before = (rest_matrices[obj.name] @ a - rest_matrices[obj.name] @ b).length
                    after = (obj.matrix_world @ a - obj.matrix_world @ b).length
                    worst_edge_drift = max(worst_edge_drift, abs(after-before))
            for bone_name in record['bones']:
                pose = rig.pose.bones[bone_name]
                worst_joint_drift = max(worst_joint_drift, abs((pose.tail-pose.head).length-pose.bone.length))
        checks.append({'pose': label, 'degrees': angle, 'max_rigid_edge_drift_m': worst_edge_drift, 'max_bone_length_drift_m': worst_joint_drift})
        if worst_edge_drift > .00001 or worst_joint_drift > .00001: raise RuntimeError('Rigid pack geometry or aux bone stretched.')
        helpers.render(scene, camera, qa / (label + '.png'), (0, .08, -.13), .55, azimuth=math.pi, width=1200, height=1024)
        for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
    blend_path = ROOT / 'Art/PlayerV2/DosaBackpackV2.blend'; bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True)
    for obj in objects.values(): obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    fbx_path = OUT / 'SM_DosaBackpackV2.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'ARMATURE','MESH'}, add_leaf_bones=False,
                            use_armature_deform_only=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
                            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False, path_mode='COPY', embed_textures=True)
    all_points = [obj.matrix_world @ v.co for obj in objects.values() for v in obj.data.vertices]
    report = {'status': 'STATIC_PACK_CANDIDATE_NOT_CHARACTER_RIG_PASS', 'source_sha256': digest(SOURCE),
              'segmentation_source': str(OUT / 'segmentation-preview.json'), 'source_anchor': list(anchor), 'source_scale': scale,
              'visible_loaded_face_blender': '+Y', 'back_contact_plane_y': 0, 'attachment_bone': 'PackRoot',
              'root_attachment_note': 'Attach PackRoot/rig at upper-back anchor; source center Z=0. Parent chooses final Spine01/02 attachment.',
              'height_m': max(p.z for p in all_points)-min(p.z for p in all_points),
              'overall_width_m': max(p.x for p in all_points)-min(p.x for p in all_points),
              'overall_depth_m': max(p.y for p in all_points)-min(p.y for p in all_points),
              'triangles': triangles, 'parts': records, 'decimation': decimation, 'topology_cleanup': topology_cleanup, 'direct_pose_checks': checks,
              'bone_count': len(rig_data.bones), 'actions': len(bpy.data.actions), 'texture_outputs': texture_outputs,
              'outputs': [{'path': str(p), 'sha256': digest(p)} for p in [blend_path, fbx_path]],
              'recipe_sha256': digest(Path(__file__)),
              'source_surface_changes': 'Generated surface sections, UV retained. Vessel separation uses original ORM glaze and close-wrap adjacency; off-object scraps discarded. Hidden cut boundaries capped. Fixed pack and brush bundle decimated; no primitive bottle replacement.',
              'validation_limits': 'Direct static pose and shape checks only; secondary-motion simulation, collision/attachment and Unity import remain separate.'}
    (OUT / 'backpack-build-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'report': str(OUT / 'backpack-build-report.json'), 'triangles': triangles, 'parts': len(records), 'bones': len(rig_data.bones)}))


def validate_export():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    report_path = OUT / 'backpack-build-report.json'
    build_report = json.loads(report_path.read_text(encoding='utf-8'))
    fbx_path = OUT / 'SM_DosaBackpackV2.fbx'
    bpy.ops.import_scene.fbx(filepath=str(fbx_path), automatic_bone_orientation=False, use_anim=False)
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    objects = {record['name']: bpy.data.objects[record['object']] for record in build_report['parts']}
    triangles = 0; weight_error = 0; mesh_checks = []
    for record in build_report['parts']:
        obj = objects[record['name']]; obj.data.calc_loop_triangles(); triangles += len(obj.data.loop_triangles)
        modifiers = [mod for mod in obj.modifiers if mod.type == 'ARMATURE']
        if record['kind'] == 'skinned_cord_3_bones':
            if len(modifiers) != 1: raise RuntimeError('Cord lost its skin: ' + record['name'])
            for vertex in obj.data.vertices:
                weight_error = max(weight_error, abs(sum(group.weight for group in vertex.groups) - 1))
                if len(vertex.groups) > 2: raise RuntimeError('Unexpected cord skin influences.')
            for i, bone_name in enumerate(record['bones']):
                if i and rig.data.bones[bone_name].parent.name != record['bones'][i - 1]: raise RuntimeError('Cord chain broke.')
        elif modifiers or obj.parent != rig or obj.parent_type != 'BONE' or obj.parent_bone != record['bones'][0]:
            raise RuntimeError('Rigid part lost independent bone parenting: ' + record['name'])
        mesh_checks.append({'object': obj.name, 'triangles': len(obj.data.loop_triangles), 'skin': bool(modifiers),
                            'parent_bone': obj.parent_bone, 'has_uv': bool(obj.data.uv_layers), 'materials': [m.name for m in obj.data.materials]})
        if not obj.data.uv_layers: raise RuntimeError('Source UV was lost in FBX.')
    if triangles > 10000 or triangles != build_report['triangles']:
        changes = [(record['name'], record['triangles'], mesh_checks[index]['triangles']) for index, record in enumerate(build_report['parts'])
                   if record['triangles'] != mesh_checks[index]['triangles']]
        raise RuntimeError(f"FBX triangle count drifted: {triangles} vs {build_report['triangles']}; {changes}")
    if weight_error > 1e-4: raise RuntimeError('Cord weights lost normalisation.')
    rigid_matrices = {obj.name: obj.matrix_world.copy() for obj in objects.values()}
    moving = [record for record in build_report['parts'] if record['kind'] == 'rigid_dangle']
    results = []
    for record in moving:
        pose = rig.pose.bones[record['bones'][0]]
        pose.rotation_mode = 'QUATERNION'; pose.rotation_quaternion = Quaternion(Vector((1,0,0)), math.radians(15))
        bpy.context.view_layer.update()
        obj = objects[record['name']]; rest = rigid_matrices[obj.name]
        movement = max((obj.matrix_world @ vertex.co - rest @ vertex.co).length for vertex in obj.data.vertices)
        stationary = max((other.matrix_world.translation - rigid_matrices[other.name].translation).length
                         for other in objects.values() if other != obj)
        drift = 0
        for edge in obj.data.edges:
            a, b = (obj.data.vertices[i].co for i in edge.vertices)
            drift = max(drift, abs((obj.matrix_world @ a-obj.matrix_world @ b).length-(rest @ a-rest @ b).length))
        results.append({'part': record['name'], 'actual_vertex_motion_m': movement, 'other_part_origin_motion_m': stationary,
                        'rigid_edge_drift_m': drift})
        if movement < .0001 or stationary > .00001 or drift > .00001: raise RuntimeError('Independent rigid pivot contract failed: ' + record['name'])
        pose.matrix_basis = Matrix.Identity(4); bpy.context.view_layer.update()
    all_points = [obj.matrix_world @ v.co for obj in objects.values() for v in obj.data.vertices]
    panel_back = min(v.y for v in all_points)
    bottle_front = np.mean([objects[r['name']].matrix_world @ v.co for r in moving if r['name'].startswith('Bottle') for v in objects[r['name']].data.vertices], axis=0)
    if bottle_front[1] < panel_back + .015: raise RuntimeError('Loaded face is not oriented +Y away from back mounting plane.')
    validation = {'status': 'STATIC_EXPORT_CHECKS_PASS_NOT_CHARACTER_RIG_PASS', 'fbx_sha256': digest(fbx_path),
                  'triangles': triangles, 'meshes': len(objects), 'bones': len(rig.data.bones), 'weight_sum_error': weight_error,
                  'height_m': max(p.z for p in all_points)-min(p.z for p in all_points), 'back_contact_min_y_m': panel_back,
                  'bottle_average_y_m': float(bottle_front[1]), 'mesh_checks': mesh_checks, 'independent_rigid_pose_checks': results,
                  'actions': len(bpy.data.actions), 'no_unity_validation_claim': True}
    (OUT / 'backpack-fbx-validation.json').write_text(json.dumps(validation, indent=2), encoding='utf-8')
    print(json.dumps({'validation': str(OUT / 'backpack-fbx-validation.json'), 'triangles': triangles, 'independent_rigid_parts': len(results)}))


def main():
    import sys
    parser = argparse.ArgumentParser(); parser.add_argument('--inspect-only', action='store_true'); parser.add_argument('--segment-preview', action='store_true'); parser.add_argument('--material-audit', action='store_true'); parser.add_argument('--build', action='store_true'); parser.add_argument('--validate-export', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.build: build_final(); return
    if args.validate_export: validate_export(); return
    obj = import_source()
    if args.inspect_only: inspect_source(obj)
    elif args.segment_preview: segment_preview(obj)
    elif args.material_audit: material_audit(obj)
    else: raise RuntimeError('Review source component/semantic part segmentation before a build.')


if __name__ == '__main__': main()
