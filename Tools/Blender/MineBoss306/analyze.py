# #306 #11 mine boss (Meshy brute-b): import, weld, measure, and print slice landmarks. Headless: blender -b --python analyze.py
import bpy, bmesh, json, sys
from mathutils import Vector
from pathlib import Path
ROOT = Path(r"C:/Users/yj666/Oheangbu")
SRC = ROOT / "Art/Characters/MineBoss306/Source/brute-b/source.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SRC))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print("objects", [(o.name, o.type, len(o.data.vertices)) for o in bpy.context.scene.objects])
o = meshes[0]
mw = o.matrix_world
bm = bmesh.new(); bm.from_mesh(o.data)
before = len(bm.verts); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); after = len(bm.verts)
vs = [mw @ v.co for v in bm.verts]
zmin = min(v.z for v in vs); zmax = max(v.z for v in vs)
xmin = min(v.x for v in vs); xmax = max(v.x for v in vs); ymin = min(v.y for v in vs); ymax = max(v.y for v in vs)
H = zmax - zmin
out = dict(weld=[before, after], bounds=dict(x=[xmin, xmax], y=[ymin, ymax], z=[zmin, zmax]), H=H, slices=[])
for f in [i / 20 for i in range(1, 20)]:
    z = zmin + H * f; band = [v for v in vs if abs(v.z - z) < H * .012]
    xs = sorted(v.x for v in band)
    # gaps in x reveal separate limbs at this height
    segs = []; start = xs[0] if xs else 0; prev = start
    for x in xs[1:]:
        if x - prev > H * .02: segs.append([round(start, 3), round(prev, 3)]); start = x
        prev = x
    if xs: segs.append([round(start, 3), round(prev, 3)])
    ys = [v.y for v in band]
    out["slices"].append(dict(f=f, z=round(z, 3), segs=segs, y=[round(min(ys), 3), round(max(ys), 3)] if ys else None))
# extreme points (hands in A-pose)
lx = min(vs, key=lambda v: v.x); rx = max(vs, key=lambda v: v.x)
out["xmin_pt"] = [round(c, 3) for c in lx]; out["xmax_pt"] = [round(c, 3) for c in rx]
print("ANALYSIS " + json.dumps(out))
