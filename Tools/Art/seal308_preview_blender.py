"""#308 Seal308 palisade - headless Blender preview of the generated meshes (offline art check, not the in-game look).

  "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup -t 4 --python Tools/Art/seal308_preview_blender.py
  -> Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/preview/seal308_{north,south,top}_{closed,open}.png (Workbench, 1100x560)

Reads Out/Seal308/unity.json + Meshes/*_LOD0.json (seal308_palisade.py) and a 120 x 100 m patch of the offline height field.
Unity (x, y, z) -> Blender (x, z, y) around the panel; flat colours stand in for the #297 wood / iron materials. One Blender
process, modest resolution (the PC can blue-screen under load).
"""
import bpy, json, math
import numpy as np
from mathutils import Vector
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = ROOT / 'Art/World/Compact/Rebuild/Enclosure305/Out/Seal308'
M = json.loads((OUT / 'unity.json').read_text(encoding='utf-8'))
gate = next(e for e in M['meshes'] if e['kind'] == 'panel'); O = np.array(gate['position'])     # view origin = closed panel pivot
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
mats = {}
for k, c in {'wood_dark': (0.16, 0.12, 0.09, 1), 'wood_board': (0.42, 0.36, 0.28, 1), 'iron': (0.09, 0.09, 0.09, 1), 'ground': (0.55, 0.52, 0.45, 1)}.items():
    m = bpy.data.materials.new(k); m.diffuse_color = c; mats[k] = m


def place(entry, name, offset=(0.0, 0.0, 0.0)):
    d = json.loads((OUT / f"Meshes/{entry['name']}_LOD0.json").read_text(encoding='utf-8'))
    v = np.array(d['v']).reshape(-1, 3); yaw = math.radians(entry['yaw']); cy, sy = math.cos(yaw), math.sin(yaw)
    p = np.array(entry['position']) + np.array(offset)
    wx = p[0] + v[:, 0] * cy + v[:, 2] * sy; wy = p[1] + v[:, 1]; wz = p[2] - v[:, 0] * sy + v[:, 2] * cy
    verts = [(float(a - O[0]), float(c - O[2]), float(b - O[1])) for a, b, c in zip(wx, wy, wz)]
    faces, mi = [], []
    for k, sub in enumerate(d['sub']):
        t = sub['t']; faces += [tuple(t[i:i + 3]) for i in range(0, len(t), 3)]; mi += [k] * (len(t) // 3)
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    for sub in d['sub']: me.materials.append(mats.get(sub['m'], mats['iron']))
    for poly, k in zip(me.polygons, mi): poly.material_index = k
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob); return ob


for e in M['meshes']:
    if e['kind'] != 'panel': place(e, e['name'])
closed = place(gate, 'gate_closed'); opened = place(gate, 'gate_open', M['gate']['OpenOffset'])
h = np.fromfile(ROOT / 'Oheangbu/Assets/_Project/Art/World/Finish297/Surface/height.bytes', '<f4').reshape(1501, 1001)
xs = np.arange(O[0] - 64, O[0] + 64, 4.0); zs = np.arange(O[2] - 52, O[2] + 52, 4.0)
verts = [(float(x - O[0]), float(z - O[2]), float(h[int(z / 4), int(x / 4)] - O[1])) for z in zs for x in xs]
nx = len(xs); faces = [(j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i) for j in range(len(zs) - 1) for i in range(nx - 1)]
me = bpy.data.meshes.new('ground'); me.from_pydata(verts, [], faces); me.update(); me.materials.append(mats['ground'])
sc.collection.objects.link(bpy.data.objects.new('ground', me))
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
sc.display.shading.show_shadows = True; sc.render.resolution_x = 1100; sc.render.resolution_y = 560
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
(OUT / 'preview').mkdir(exist_ok=True)


def shot(loc, look, fn, lens):
    cam.location = Vector(loc); d = Vector(look) - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
    sc.render.filepath = str(OUT / 'preview' / fn); bpy.ops.render.render(write_still=True)


for state in ('closed', 'open'):
    closed.hide_render = state != 'closed'; opened.hide_render = state != 'open'
    shot((-6, 46, 9), (0, 0, 4), f'seal308_north_{state}.png', 32)      # from 상경 가도 (north, eye ~9 m up the slope)
    shot((8, -44, 16), (0, 0, 5), f'seal308_south_{state}.png', 32)     # from 적로 (south)
    shot((0, 0.01, 75), (0, 0, 0), f'seal308_top_{state}.png', 30)
