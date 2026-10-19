"""D308-120 clouded eye parts (마석 곰 눈): a UV sphere with a milky, ink-veined albedo - a blind, wet, non-emissive eye -
and the lid ring that grows it out of the fur (a torus around part +Z, dark flesh-grey).

blender -b --factory-startup -P eye_part.py -- <out.fbx> <out_albedo.png> eye|lid
The eye texture is uniform around the ball (no iris to aim), so the ball needs no orientation; the lid's ring axis is part +Z
(turn it onto the skin normal: euler x = -asin(n.y), y = atan2(n.x, n.z) in bone space). Creatures308 parts[] scales them by
length and gives them the ink look (albedo + value / saturation / smoothness).
"""
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
out_fbx, out_png = argv[0], argv[1]
kind = argv[2] if len(argv) > 2 else "eye"
rng = np.random.default_rng(120)
bpy.ops.wm.read_factory_settings(use_empty=True)
if kind == "lid":   # ring outer radius .5 (longest side 1), tube .13: the ball shows through a .24 opening
    bpy.ops.mesh.primitive_torus_add(major_segments=36, minor_segments=12, major_radius=.37, minor_radius=.13)
    me = bpy.context.active_object.data   # swollen, uneven flesh: the tube thickness wanders around the ring (1 tube unit = .13)
    for v in me.vertices:
        a = np.arctan2(v.co.y, v.co.x)
        ring = np.array([np.cos(a) * .37, np.sin(a) * .37, 0])
        d = np.array(v.co) - ring
        k = 1 + .28 * np.sin(3 * a + .7) + .16 * np.sin(7 * a + 2.1) + rng.normal(0, .05)
        v.co = ring + d * k
else:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=14, radius=.5)
ball = bpy.context.active_object
ball.name = "Lid308" if kind == "lid" else "Eye308"
bpy.ops.object.shade_smooth()

N = 512
u, v = np.meshgrid(np.linspace(0, 1, N, endpoint=False), np.linspace(0, 1, N, endpoint=False))


def noise(scale, octaves=4):
    out = np.zeros((N, N))
    for o in range(octaves):
        f = scale * 2 ** o
        g = rng.random((int(f) + 2, int(f) + 2))
        g[:, -1] = g[:, 0]   # wrap around u
        x, y = u * f, v * f
        i, j = x.astype(int), y.astype(int)
        fx, fy = x - i, y - j
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = g[j, i] * (1 - fx) + g[j, i + 1] * fx
        b = g[j + 1, i] * (1 - fx) + g[j + 1, i + 1] * fx
        out += (a * (1 - fy) + b * fy) / 2 ** o
    return out / (2 - 2 ** (1 - octaves))


cloud = noise(4)
veins = np.abs(noise(6, 3) - .5)          # ridged noise: thin dark lines where the field crosses .5
veins = np.clip(1 - veins / .035, 0, 1) ** 2
milk = np.array([.86, .84, .77])
base = milk[None, None] * (.82 + .22 * cloud)[..., None]
ink = np.array([.12, .10, .09])
img = base * (1 - .75 * veins[..., None]) + ink * .75 * veins[..., None]
if kind == "lid":   # dark wet flesh-grey with creases, the fur's tone
    img = np.array([.38, .32, .30])[None, None] * (.75 + .4 * cloud)[..., None] * (1 - .35 * veins[..., None])
rgba = np.concatenate([np.clip(img, 0, 1), np.ones((N, N, 1))], axis=2)
im = bpy.data.images.new(ball.name + "_albedo", N, N)
im.pixels.foreach_set(rgba.astype(np.float32).ravel())
im.filepath_raw = out_png
im.file_format = "PNG"
im.save()

mat = bpy.data.materials.new(ball.name)
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = im
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
ball.data.materials.append(mat)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=False, axis_up="Y", axis_forward="-Z", path_mode="STRIP",
                         apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE")
print("PART", kind, out_fbx, out_png, len(ball.data.polygons))
