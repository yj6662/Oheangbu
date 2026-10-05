"""#308 D308-3b Seal308 관문 성벽 - headless Blender preview of the generated meshes (offline art check, not the in-game look).

  "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup -t 4 --python Tools/Art/seal308_wall_preview_blender.py
  -> Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/preview/seal308_wall_<view>_<closed|open>.png (Workbench, 1280x720)

Reads Out/Seal308/unity.json + Meshes/*_LOD0.json (seal308_wall.py), a 200 x 150 m patch of the offline height field (2 m,
bilinear), the road centreline (routes.json, 6 m strip) and segments305.json. Unity (x, y, z) -> Blender (x, z, y) around the gate.
Flat colours stand in for the #297 materials. The dark cones along the seam runs are PROXIES for the N6 seal forest (the real forest is
built by the editor Enclosure305 build-scene) - they only show where the wall ends meet the forest. One Blender process, -t 4.
"""
import bpy, json, math
import numpy as np
from mathutils import Vector
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ENC = ROOT / 'Art/World/Compact/Rebuild/Enclosure305'
OUT = ENC / 'Out/Seal308'
M = json.loads((OUT / 'unity.json').read_text(encoding='utf-8'))
LV = M['leaves']; O = np.array(LV['position'])                                     # view origin = gate pivot
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
COL = {'stone_dressed': (0.56, 0.54, 0.50), 'stone_rough': (0.47, 0.45, 0.42), 'stone_plain': (0.63, 0.61, 0.57), 'wood_dark': (0.20, 0.15, 0.11),
       'wood_board': (0.42, 0.34, 0.25), 'iron': (0.08, 0.08, 0.08), 'tile': (0.17, 0.18, 0.20), 'tile_dark': (0.13, 0.14, 0.15), 'ridge': (0.72, 0.71, 0.68),
       'gable': (0.30, 0.25, 0.20), 'fascia': (0.30, 0.22, 0.16), 'soffit': (0.36, 0.30, 0.24), 'ceiling': (0.40, 0.34, 0.27), 'floor_wood': (0.40, 0.33, 0.25),
       'plaster': (0.80, 0.78, 0.72), 'wood_red': (0.42, 0.18, 0.12), 'ground': (0.55, 0.53, 0.46), 'road': (0.68, 0.63, 0.53), 'proxy': (0.20, 0.24, 0.19)}
mats = {}
for k, c in COL.items():
    m = bpy.data.materials.new(k); m.diffuse_color = (*c, 1.0); mats[k] = m


def rot_y(v, yaw_deg):
    a = math.radians(yaw_deg); c, s = math.cos(a), math.sin(a)
    return np.stack([v[:, 0] * c + v[:, 2] * s, v[:, 1], -v[:, 0] * s + v[:, 2] * c], -1)


def mesh_obj(name, fn, pos, yaw, pre=None):
    """Load a KitMesh JSON (object-local), optionally pre-transform it (hinge), place it at pos with Euler(0, yaw, 0)."""
    d = json.loads((OUT / 'Meshes' / fn).read_text(encoding='utf-8'))
    v = np.array(d['v']).reshape(-1, 3)
    if pre is not None: v = pre(v)
    w = rot_y(v, yaw) + np.asarray(pos)
    verts = [(float(a - O[0]), float(c - O[2]), float(b - O[1])) for a, b, c in w]
    faces, mi = [], []
    for k, sub in enumerate(d['sub']):
        t = sub['t']; faces += [tuple(t[i:i + 3]) for i in range(0, len(t), 3)]; mi += [k] * (len(t) // 3)
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    for sub in d['sub']: me.materials.append(mats.get(sub['m'], mats['stone_plain']))
    for poly, k in zip(me.polygons, mi): poly.material_index = k
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob); return ob


for p in M['pieces']: mesh_obj(p['name'], p['name'] + '_LOD0.json', p['position'], p['yaw'])
leaf_objs = {}
for state, deg in (('closed', 0.0), ('open', LV['openDegrees'])):
    objs = []
    for side in ('left', 'right'):
        L = LV[side]; a = deg * L['openSign']; hinge = np.array(L['hinge'])
        objs.append(mesh_obj(f"{L['name']}_{state}", L['name'] + '_LOD0.json', LV['position'], LV['yaw'], pre=lambda v, a=a, h=hinge: rot_y(v, a) + h))
    leaf_objs[state] = objs
# ground (2 m, bilinear on the 4 m field)
h = np.fromfile(ROOT / 'Oheangbu/Assets/_Project/Art/World/Finish297/Surface/height.bytes', '<f4').reshape(1501, 1001).astype(float)


def H(x, z):
    j = np.clip(np.asarray(x, float) / 4, 0, 999.999); i = np.clip(np.asarray(z, float) / 4, 0, 1499.999); j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int)
    fj = j - j0; fi = i - i0
    return h[i0, j0] * (1 - fj) * (1 - fi) + h[i0, j0 + 1] * fj * (1 - fi) + h[i0 + 1, j0] * (1 - fj) * fi + h[i0 + 1, j0 + 1] * fj * fi


xs = np.arange(O[0] - 100, O[0] + 100.1, 2.0); zs = np.arange(O[2] - 70, O[2] + 80.1, 2.0)
XX, ZZ = np.meshgrid(xs, zs); YY = H(XX, ZZ)
verts = [(float(x - O[0]), float(z - O[2]), float(y - O[1])) for x, y, z in zip(XX.ravel(), YY.ravel(), ZZ.ravel())]
nx = len(xs); faces = [(j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i) for j in range(len(zs) - 1) for i in range(nx - 1)]
me = bpy.data.meshes.new('ground'); me.from_pydata(verts, [], faces); me.update(); me.materials.append(mats['ground'])
sc.collection.objects.link(bpy.data.objects.new('ground', me))
# road strip (6 m) on the ground, lifted 5 cm
R = json.loads((ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json').read_text(encoding='utf-8-sig'))['routes']
road = next(r for r in R if r['id'] == 'capital_center__jeokro'); RP = np.array([(p['x'], p['z']) for p in road['points']], float)
RP = np.concatenate([np.linspace(a, b, max(2, int(np.hypot(*(b - a)) / 2.0) + 1)) for a, b in zip(RP[:-1], RP[1:])])
RP = RP[(np.abs(RP[:, 0] - O[0]) < 98) & (np.abs(RP[:, 1] - O[2]) < 75)]
if len(RP) > 2:
    dd = np.gradient(RP, axis=0); dd /= np.linalg.norm(dd, axis=1, keepdims=True); nn = np.stack([-dd[:, 1], dd[:, 0]], -1)
    rv, rf = [], []
    for k, (p, q) in enumerate(zip(RP, nn)):
        for sgn in (-1, 1):
            x, z = p + q * 3.0 * sgn; rv.append((float(x - O[0]), float(z - O[2]), float(H(x, z) + .05 - O[1])))
        if k: rf.append((2 * k - 2, 2 * k - 1, 2 * k + 1, 2 * k))
    me = bpy.data.meshes.new('road'); me.from_pydata(rv, [], rf); me.update(); me.materials.append(mats['road'])
    sc.collection.objects.link(bpy.data.objects.new('road', me))
# proxy seal forest (N6 stand-in): cones on the blocked side of the seam runs near the wall ends
segs = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))['segments']
rng = np.random.default_rng(308); cones = []
for s in segs:
    if s['id'] not in ('E305_084', 'E305_056', 'E305_055', 'E305_038'): continue
    P = np.asarray(s['points'], float)
    P = np.concatenate([np.linspace(a, b, max(2, int(np.hypot(*(b - a)) / 4.5) + 1)) for a, b in zip(P[:-1], P[1:])])
    for k in range(1, len(P)):
        p = P[k]
        if np.hypot(p[0] - O[0], p[1] - O[2]) > 90: continue
        d_ = P[k] - P[k - 1]; L_ = np.hypot(*d_)
        if L_ < 1e-6: continue
        nb = np.array([d_[1], -d_[0]]) / L_ * s['block']
        for off in (1.5, 5.0, 8.5):
            q = p + nb * (off + rng.uniform(-.8, .8)); cones.append((q[0], q[1], rng.uniform(8, 13)))
for i, (x, z, ht) in enumerate(cones):
    bpy.ops.mesh.primitive_cone_add(vertices=7, radius1=2.0, depth=ht, location=(x - O[0], z - O[2], H(x, z) - O[1] + ht / 2))
    ob = bpy.context.active_object; ob.data.materials.append(mats['proxy'])
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = True
sc.render.resolution_x = 1280; sc.render.resolution_y = 720
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.clip_end = 2000
(OUT / 'preview').mkdir(exist_ok=True)
t = np.array(M['frame']['t']); n = np.array(M['frame']['n'])


def eye(dx_t, dz_n, up_from_ground):
    """Camera point dx along the wall, dz north of the gate pivot, up_from_ground metres above the offline ground there (Blender coords)."""
    x, z = O[0] + t[0] * dx_t + n[0] * dz_n, O[2] + t[1] * dx_t + n[1] * dz_n
    return (x - O[0], z - O[2], float(H(x, z)) + up_from_ground - O[1])


def shot(loc, look, fn, lens):
    cam.location = Vector(loc); d = Vector(look) - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
    sc.render.filepath = str(OUT / 'preview' / fn); bpy.ops.render.render(write_still=True)


views = [('road_north', eye(0.5, 42, 1.7), (0, 0, 4.5), 30, ('closed', 'open')),       # player on 상경 가도, 42 m north of the gate
         ('gate_near', eye(1.0, 16, 1.7), (0, 0, 4.0), 24, ('closed', 'open')),        # at the inner mouth
         ('north_high', eye(28, 55, 22), (0, 0, 2), 28, ('closed',)),                  # 3/4 overview from the north-east
         ('a_end', eye(-18, 34, 6), tuple(np.array(eye(-36, 3, 4))), 26, ('closed',)),  # A end (치 A) meeting the seam forest, from the player side
         ('b_end', eye(30, 26, 5), tuple(np.array(eye(18, 3, 4))), 26, ('closed',)),    # B end (치 B)
         ('south', eye(-6, -46, 6), (0, 0, 5), 28, ('closed', 'open')),               # from 적로 (uphill)
         ('top', (0.0, -1.0, 95.0), (0.0, 0.0, 0.0), 30, ('closed', 'open'))]         # north up (eye 1 m south of the target)
for name, loc, look, lens, states in views:
    for state in states:
        for st, objs in leaf_objs.items():
            for ob in objs: ob.hide_render = st != state
        shot(loc, look, f'seal308_wall_{name}_{state}.png', lens)
