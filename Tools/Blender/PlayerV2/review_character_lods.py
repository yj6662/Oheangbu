"""Read-only source LOD material review: identical pose, light and camera per comparison.

Run in a separate background Blender process. Never saves the input blend files.
"""
import bpy, hashlib, json, math, argparse, sys
from pathlib import Path
from mathutils import Matrix, Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
parser = argparse.ArgumentParser()
parser.add_argument('--labels', default='')
parser.add_argument('--output', default=str(ART/'Inspect/LodMaterialComparison'))
parser.add_argument('--distance-heights', default='')
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
OUT = Path(args.output)
OUT.mkdir(parents=True, exist_ok=True)
report = {'scope': 'Actual Cycles material renders of isolated source blends using identical fixed cameras, illumination and direct bone poses. No Unity bone remapping, production actions or source saves.',
          'rigGate': 'NOT_GRANTED', 'renders': [], 'sources': []}
sources = [('old_lod1', ART/'ReviewHistory/LodUniform9f4c/DosaV2_LOD1.blend'),
           ('new_lod1', ART/'DosaV2_LOD1.blend'),
           ('old_lod2', ART/'ReviewHistory/LodUniform9f4c/DosaV2_LOD2.blend'),
           ('new_lod2', ART/'DosaV2_LOD2.blend')]
if args.distance_heights:
    sources.insert(0, ('lod0', ART/'DosaV2_Assembled.blend'))
    bpy.ops.wm.open_mainfile(filepath=str(ART/'DosaV2_Assembled.blend'))
    for bone in bpy.data.objects['DosaV2_Rig'].pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    z_values = []
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH' and obj.name.startswith(('DosaV2_', 'DosaPackV2_')) and obj.name != 'DosaV2_SourceSurface':
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            z_values.extend((evaluated.matrix_world @ vertex.co).z for vertex in mesh.vertices)
            evaluated.to_mesh_clear()
    baseline_height = max(z_values)-min(z_values)
    baseline_center = (0., 0., (max(z_values)+min(z_values))*.5)
    report['distanceComparison'] = {'baselineHeightMeters': baseline_height, 'baselineCenter': baseline_center,
        'targetPixelHeights': [int(v) for v in args.distance_heights.split(',')],
        'scope': 'Same orthographic camera per target height, derived from actual evaluated LOD0 geometry in a1920x1080 render. Fixed rest pose. These are actual raster renders, not resized earlier full-body screenshots.'}
if args.labels:
    requested = set(args.labels.split(','))
    assert requested.issubset({label for label, _ in sources}), requested
    sources = [(label, source) for label, source in sources if label in requested]
for label, source in sources:
    before_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    scene = bpy.context.scene
    rig = bpy.data.objects['DosaV2_Rig']
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    for obj in list(scene.objects):
        if obj.type in {'LIGHT', 'CAMERA'}:
            bpy.data.objects.remove(obj, do_unlink=True)
    meshes = []
    for obj in scene.objects:
        if obj.type == 'MESH':
            show = obj.name.startswith(('DosaV2_', 'DosaPackV2_')) and obj.name != 'DosaV2_SourceSurface'
            obj.hide_render = not show
            if show:
                meshes.append(obj)
                if obj.data.shape_keys:
                    for key in obj.data.shape_keys.key_blocks:
                        key.value = 0.
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.world = bpy.data.worlds.new('LODComparisonWorld')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.12, .12, .12, 1.)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .5
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.
    scene.view_settings.gamma = 1.
    scene.render.image_settings.file_format = 'PNG'
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    for name, location, power, size in [('Key', (-3,-4,4), 400, 4), ('Fill', (3,-1,2), 220, 3), ('Rim', (1,3,3), 330, 3)]:
        light = bpy.data.lights.new('LODCompare'+name, 'AREA')
        light.energy, light.size = power, size
        obj = bpy.data.objects.new(light.name, light)
        scene.collection.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0,0,1.1))-obj.location).to_track_quat('-Z','Y').to_euler()
    camera = bpy.data.objects.new('LODCompareCamera', bpy.data.cameras.new('LODCompareCamera'))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = 'ORTHO'
    counts = []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        counts.append({'mesh': obj.name, 'triangles': len(obj.data.loop_triangles), 'customNormals': obj.data.has_custom_normals,
                       'uvLayers': [layer.name for layer in obj.data.uv_layers], 'materials': [m.name if m else None for m in obj.data.materials]})
    used_images = set()
    for obj in meshes:
        for material in obj.data.materials:
            if material and material.use_nodes:
                used_images.update(node.image.name for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image)
    image_status = [{'name': name, 'packed': bool(bpy.data.images[name].packed_file),
                     'size': list(bpy.data.images[name].size), 'path': bpy.data.images[name].filepath} for name in sorted(used_images)]
    report['sources'].append({'label': label, 'path': str(source), 'sha256': before_hash, 'meshCounts': counts, 'textures': image_status})
    views = [
        ('rest_front', (0,0,.95), (0,-5,0), 2.05, (900,1050)),
        ('posed_front', (0,0,.95), (0,-5,0), 2.05, (900,1050)),
        ('posed_face', (0,-.01,1.57), (0,-4,0), .61, (1000,1000)),
        ('posed_feet', (0,-.02,.19), (0,-4,.08), .57, (1000,1000))]
    if args.distance_heights:
        # With the default horizontal sensor fit, Blender's orthographic scale spans
        # the image width in landscape output. Verify the actual projected extent below.
        views = [('distance_height_'+str(height), baseline_center, (0,-5,0), baseline_height*1920/height, (1920,1080))
                 for height in [int(v) for v in args.distance_heights.split(',')]]
    for view, center, offset, scale, size in views:
        if view.startswith('posed_'):
            # Static35-degree arm raising from the actual imported rest basis, never an Action/clip.
            for name, angle in [('LeftArm', -35), ('RightArm', 35)]:
                bone = rig.pose.bones[name]
                rest = rig.data.bones[name].matrix_local
                desired = Matrix.Translation(rest.translation) @ Matrix.Rotation(math.radians(angle),4,'Y') @ Matrix.Translation(-rest.translation) @ rest
                parent = rig.data.bones[name].parent
                relative = parent.matrix_local.inverted() @ rest
                bone.matrix_basis = relative.inverted() @ parent.matrix_local.inverted() @ desired
        bpy.context.view_layer.update()
        center, offset = Vector(center), Vector(offset)
        camera.location = center+offset
        camera.rotation_euler = (center-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale = scale
        scene.render.resolution_x, scene.render.resolution_y = size
        projected_height = None
        if args.distance_heights:
            bpy.context.view_layer.update()
            top = world_to_camera_view(scene,camera,center+Vector((0,0,baseline_height*.5)))
            bottom = world_to_camera_view(scene,camera,center-Vector((0,0,baseline_height*.5)))
            projected_height = abs(top.y-bottom.y)*size[1]
            assert abs(projected_height-int(view.rsplit('_',1)[1])) < .01, (view,projected_height)
        path = OUT/(label+'_'+view+'.png')
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        report['renders'].append({'label': label, 'view': view, 'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                                  'cameraLocation': list(camera.location), 'cameraTarget': list(center), 'orthoScale': scale, 'resolution': list(size),
                                  'actualBaselineProjectedHeightPixels': projected_height})
        (OUT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    assert hashlib.sha256(source.read_bytes()).hexdigest() == before_hash
report['status'] = 'RENDERED_PENDING_VISUAL_REVIEW'
(OUT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'status':report['status'],'renders':len(report['renders']),'path':str(OUT)}))
