"""Clay preview of the Highlands293 section meshes + designed terrain (separate background Blender).

"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup -P Tools/Blender/preview_highland293.py
Writes Variants/<id>/preview-<n>.png (Workbench, no fog/post; shape check only).
"""
import bpy, json, math
from pathlib import Path
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
VAR = ROOT / 'Art/World/Compact/Rebuild/Highlands293/Variants'


def load(name, path, color):
    d = json.loads(path.read_text())
    verts = [(v['x'], -v['z'], v['y']) for v in d['vertices']]
    t = d['triangles']; faces = [tuple(t[i:i + 3]) for i in range(0, len(t), 3)]
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    mat = bpy.data.materials.new(name); mat.diffuse_color = color; ob.data.materials.append(mat)
    return ob


def terrain(folder, color, stride=1):
    z = np.load(folder / 'terrain.npz')
    T = z['T'][::stride, ::stride]; x0 = float(z['x0']); z0 = float(z['z0']); step = stride * 1.0
    rows, cols = T.shape
    verts = [(x0 + j * step, -(z0 + i * step), float(T[i, j])) for i in range(rows) for j in range(cols)]
    faces = [(i * cols + j, i * cols + j + 1, (i + 1) * cols + j + 1, (i + 1) * cols + j) for i in range(rows - 1) for j in range(cols - 1)]
    me = bpy.data.meshes.new('Terrain'); me.from_pydata(verts, [], faces); me.update()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Terrain', me); bpy.context.collection.objects.link(ob)
    mat = bpy.data.materials.new('Terrain'); mat.diffuse_color = color; ob.data.materials.append(mat)
    return ob


def shoot(path, eye, target, lens=28):
    cam = bpy.data.cameras.new('cam'); cam.lens = lens; cam.clip_end = 3000
    co = bpy.data.objects.new('cam', cam); bpy.context.collection.objects.link(co)
    co.location = eye; co.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = co
    bpy.context.scene.render.filepath = str(path); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co, do_unlink=True)


scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True; scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
for folder in sorted(p for p in VAR.iterdir() if (p / 'section.json').exists()):
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
    info = json.loads((folder / 'section.json').read_text(encoding='utf8'))
    terrain(folder, (.62, .60, .52, 1))
    load('Band', folder / 'Meshes/Band_LOD1.json', (.45, .45, .47, 1))
    load('Apron', folder / 'Meshes/Apron_LOD1.json', (.40, .42, .48, 1))
    for f in sorted((folder / 'Meshes').glob('Trail_*.json')):
        load(f.stem, f, (.72, .45, .30, 1))
    pts = np.array([[p['x'], p['y'], p['z']] for p in info['profile']['points']])
    mid = pts[len(pts) // 2]; a, b = pts[0], pts[-1]
    t = (b - a); t[1] = 0; t /= np.linalg.norm(t); right = np.array([t[2], 0, -t[0]]) * info['profile']['upSign']
    B = lambda p: (float(p[0]), float(-p[2]), float(p[1]))
    views = [(mid - right * 55 + np.array([0, 22, 0]) - t * 10, mid + np.array([0, 4, 0])),   # from the valley
             (a - t * 6 + np.array([0, 1.7, 0]) - right * .2, a + t * 25 + np.array([0, 3, 0])),  # walking up
             (mid - right * 30 + np.array([0, 60, 0]), mid)]                                      # high oblique
    for n, (eye, target) in enumerate(views):
        shoot(folder / ('preview-%d.png' % n), B(eye), B(target), 24 if n == 1 else 30)
print('previews done')
