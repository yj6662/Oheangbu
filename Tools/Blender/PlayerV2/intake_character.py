"""Inspect the new Meshy character without touching the existing player sources."""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Art/PlayerV2'
QA = OUT / 'Inspect'
QA.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
s = bpy.context.scene
s.name = 'DosaV2_Character_Workshop'
s.unit_settings.system = 'METRIC'
bpy.ops.import_scene.gltf(filepath=str(OUT / 'MeshySources/character-01/source_glb.glb'))
meshes = [o for o in s.objects if o.type == 'MESH']
points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
low = min(p.z for p in points)
high = max(p.z for p in points)
scale = 1.75 / (high - low)
normalization = Matrix.Diagonal(Vector((scale, scale, scale, 1))) @ Matrix.Translation((0, 0, -low))
for o in meshes:
    world = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
    o.data.transform(normalization @ world)
    o.name = 'DosaV2_SourceSurface'
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.000002)
    bm.to_mesh(o.data)
    bm.free()
    for p in o.data.polygons:
        p.use_smooth = True
    o.data.update()

reports = []
for o in meshes:
    mesh = o.data
    adjacency = [[] for v in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].append(b)
        adjacency[b].append(a)
    unseen = set(range(len(mesh.vertices)))
    components = []
    while unseen:
        start = unseen.pop()
        stack = [start]
        indices = [start]
        while stack:
            for nxt in adjacency[stack.pop()]:
                if nxt in unseen:
                    unseen.remove(nxt)
                    indices.append(nxt)
                    stack.append(nxt)
        pts = [mesh.vertices[i].co for i in indices]
        components.append({'vertices': len(indices),
            'min': [min(p[i] for p in pts) for i in range(3)],
            'max': [max(p[i] for p in pts) for i in range(3)],
            'indices': indices})
    components.sort(key=lambda x: x['vertices'], reverse=True)
    (QA / 'character-components.json').write_text(json.dumps(components), encoding='utf-8')
    mesh.calc_loop_triangles()
    reports.append({'name': o.name, 'vertices_after_uv_seam_weld': len(mesh.vertices),
        'triangles': len(mesh.loop_triangles), 'components': len(components),
        'largest_components': [{k: v for k, v in c.items() if k != 'indices'} for c in components[:35]],
        'height': 1.75, 'scale_from_source': scale,
        'textures': [{'name': n.image.name, 'size': list(n.image.size)}
            for m in mesh.materials if m and m.use_nodes for n in m.node_tree.nodes
            if n.type == 'TEX_IMAGE' and n.image]})
(QA / 'character-topology.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')

s.render.engine = 'CYCLES'
s.cycles.samples = 24
s.cycles.use_denoising = True
s.render.resolution_percentage = 100
s.world = bpy.data.worlds.new('DosaV2_InspectionWorld')
s.world.use_nodes = True
s.world.node_tree.nodes['Background'].inputs[0].default_value = (.11, .115, .12, 1)
s.world.node_tree.nodes['Background'].inputs[1].default_value = .45
s.view_settings.view_transform = 'AgX'
def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
for name, loc, energy, size in [('Key', (2, -3, 3), 380, 3), ('Fill', (-2, -1, 2), 240, 3),
                                ('Back', (0, 3, 3), 440, 3)]:
    light = bpy.data.lights.new(name, 'AREA')
    light.energy = energy
    light.shape = 'DISK'
    light.size = size
    obj = bpy.data.objects.new(name, light)
    s.collection.objects.link(obj)
    obj.location = loc
    aim(obj, (0, 0, .9))
camera = bpy.data.cameras.new('DosaV2_InspectionCamera')
camera.type = 'ORTHO'
cam = bpy.data.objects.new(camera.name, camera)
s.collection.objects.link(cam)
s.camera = cam
views = [('Front', (0, -4, .9), (0, 0, .9), 2.0, 1500, 1500),
         ('Back', (0, 4, .9), (0, 0, .9), 2.0, 1500, 1500),
         ('ThreeQuarter', (2.8, -4, 1.6), (0, 0, .9), 2.0, 1500, 1500),
         ('HandPositiveX', (.79, -1, 1.68), (.735, 0, 1.30), .28, 1200, 1200),
         ('HandNegativeX', (-.79, -1, 1.68), (-.735, 0, 1.30), .28, 1200, 1200)]
for name, loc, target, zoom, width, height in views:
    cam.location = loc
    aim(cam, target)
    camera.ortho_scale = zoom
    s.render.resolution_x = width
    s.render.resolution_y = height
    s.render.filepath = str(QA / ('Character_' + name + '.png'))
    bpy.ops.render.render(write_still=True)
cam.location = (2.8, -4, 1.6)
aim(cam, (0, 0, .9))
camera.ortho_scale = 2.0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'DosaV2_Intake.blend'))
print(json.dumps(reports))
