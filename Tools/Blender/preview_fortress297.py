"""Blender preview of a #297 compound layout on its real terrain (look/composition review before Unity).

blender -b --factory-startup -P Tools/Blender/preview_fortress297.py -- <compound dir> <out dir>
Terrain = #295 height field with the compound's terrain ops (cut/fill pads) applied, like the Unity Surface297 pass.
Placeholders stand in for #296 buildings that are not part of the kit (e.g. the armoury hall).
"""
import bpy, json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import preview_hanok297 as ph

ROOT = Path(__file__).resolve().parents[2]
H = np.fromfile(ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295/Surface/height.bytes', '<f4').reshape(1501, 1001).astype(np.float64)


def sample(x, z):
    gx = np.clip(x / 4, 0, 999.999); gz = np.clip(z / 4, 0, 1499.999); i = np.floor(gx).astype(int); j = np.floor(gz).astype(int)
    fx = gx - i; fz = gz - j
    return H[j, i] * (1 - fx) * (1 - fz) + H[j, i + 1] * fx * (1 - fz) + H[j + 1, i] * (1 - fx) * fz + H[j + 1, i + 1] * fx * fz


def terrain(x0, x1, z0, z1, ops, step=2.0):
    xs = np.arange(x0, x1 + .1, step); zs = np.arange(z0, z1 + .1, step); X, Z = np.meshgrid(xs, zs); Y = sample(X, Z)
    for op in ops:
        if op['type'] != 'pad': continue
        cx, cz = op['centre']; a = math.radians(op['yaw']); ca, sa = math.cos(a), math.sin(a)
        u = (X - cx) * ca - (Z - cz) * sa; v = (X - cx) * sa + (Z - cz) * ca
        dx = np.maximum(np.abs(u) - op['half'][0], 0); dz = np.maximum(np.abs(v) - op['half'][1], 0); d = np.hypot(dx, dz)
        w = np.clip(1 - d / op['blend'], 0, 1) ** 2
        Y = np.where(Y > op['top'], Y * (1 - w) + op['top'] * w, Y)
    verts = [(float(x), float(z), float(y)) for x, y, z in zip(X.ravel(), Y.ravel(), Z.ravel())]
    nx = len(xs); faces = [(j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i) for j in range(len(zs) - 1) for i in range(nx - 1)]
    me = bpy.data.meshes.new('terrain'); me.from_pydata(verts, [], faces); me.update()
    for p in me.polygons: p.use_smooth = True
    ob = bpy.data.objects.new('terrain', me); bpy.context.collection.objects.link(ob)
    m = bpy.data.materials.new('terrain'); m.use_nodes = True; nt = m.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    geo = nt.nodes.new('ShaderNodeNewGeometry'); sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Normal'], sep.inputs[0])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position = .72; ramp.color_ramp.elements[0].color = (.30, .29, .26, 1)
    ramp.color_ramp.elements[1].position = .9; ramp.color_ramp.elements[1].color = (.23, .27, .19, 1)
    nt.links.new(sep.outputs['Z'], ramp.inputs[0]); nt.links.new(ramp.outputs[0], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = .95
    me.materials.append(m)
    return ob


def place(ob, pos, yaw):
    ob.location = (pos[0], pos[2], pos[1]); ob.rotation_euler = (0, 0, -math.radians(yaw))


def shoot(cam, eye, target, path, lens=30):
    cam.location = (eye[0], eye[2], eye[1]); from mathutils import Vector
    cam.rotation_euler = (Vector((target[0], target[2], target[1])) - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
    bpy.context.scene.render.filepath = str(path); bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:]; src = Path(argv[0]); dst = Path(argv[1])
    src = src if src.is_absolute() else ROOT / src; dst = dst if dst.is_absolute() else ROOT / dst; dst.mkdir(parents=True, exist_ok=True)
    L = json.loads((src / 'layout.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True); cam = ph.setup(dst)
    ground = bpy.data.objects.get('ground')
    if ground: bpy.data.objects.remove(ground)
    bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = 1600, 900
    view = L.get('preview', {})
    terrain(*view.get('terrain', [540, 860, 3330, 3700]), L.get('terrainOps', []))
    for w in L['walls'] + L['gates'] + L.get('paths', []):
        f = src / 'Meshes' / f"{w['name']}_LOD0.json"
        if f.exists(): ph.load(f, w['name'])
    for b in L['buildings']:
        f = src / 'Meshes' / f"{b['name']}_LOD0.json"
        if f.exists(): place(ph.load(f, b['name']), b['position'], b['yaw'])
    for pb in view.get('placeholders', []):
        bpy.ops.mesh.primitive_cube_add(size=1); o = bpy.context.active_object; o.name = pb['name']
        o.scale = (pb['size'][0], pb['size'][2], pb['size'][1]); o.location = (pb['centre'][0], pb['centre'][2], pb['centre'][1] + pb['size'][1] / 2)
        o.rotation_euler = (0, 0, -math.radians(pb.get('yaw', 0))); o.data.materials.append(ph.material('wood_red'))
    for k, v in enumerate(view.get('cameras', [])):
        shoot(cam, v['eye'], v['target'], dst / f"{k:02d}-{v['name']}.png", v.get('lens', 30))
    print('fortress previews ->', dst)
