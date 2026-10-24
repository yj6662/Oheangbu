"""Debug: zoomed orthographic views of the assembled (unrigged) creature with a metre grid, for tuning placements.

  blender -b --factory-startup --python fitview.py -- cx cy cz scale name [stages-to-run-after-assemble: none]
  views: front (camera -Y), side (camera +X, creature faces left), top
"""
import sys, os, math
import numpy as np
sys.path.insert(0, r"C:\Users\yj666\Oheangbu\Tools\Blender\Jujak308")
import bpy
import build_jujak308 as J
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
cx, cy, cz, SC = float(a[0]), float(a[1]), float(a[2]), float(a[3])
name = a[4]
OUT = str(J.SCRATCH)       # Art/Characters/EnemyBodies308/Jujak308/work
J.stage_load()
J.stage_grade(True)
J.stage_assemble()
if len(a) > 5 and a[5] == "rig":
    J.stage_rig(); J.stage_weights()
cam = J.setup_render(900, 900)
cam.data.type = "ORTHO"
cam.data.ortho_scale = SC
cam.data.clip_start, cam.data.clip_end = 0.1, 200
S = 900
ppu = S / SC
imgs = []
for k, (loc, proj) in {"front": ((cx, cy - 40, cz), lambda p: (p[0] - cx, p[2] - cz)),
                       "side": ((cx + 40, cy, cz), lambda p: (p[1] - cy, p[2] - cz)),
                       "top": ((cx, cy, cz + 40), lambda p: (p[0] - cx, p[1] - cy))}.items():
    J.aim(cam, loc, (cx, cy, cz))
    if k == "top":
        cam.rotation_euler = (0, 0, 0)
    fp = os.path.join(OUT, "tmp_fit.png")
    J.render_to(fp)
    img = bpy.data.images.load(fp)
    arr = np.empty(S * S * 4, dtype=np.float32)
    img.pixels.foreach_get(arr)
    arr = arr.reshape(S, S, 4)[::-1].copy()
    bpy.data.images.remove(img)
    lo = -SC / 2
    off = {"front": (cx, cz), "side": (cy, cz), "top": (cx, cy)}[k]
    # grid every 0.25 m (labels by position: thick every 1 m); absolute coordinates
    for axis in (0, 1):
        v0 = math.ceil((off[axis] - SC / 2) / 0.25) * 0.25
        v = v0
        while v < off[axis] + SC / 2:
            p = int(round(S / 2 + (v - off[axis]) * ppu))
            thick = abs(v - round(v)) < 1e-6
            col = np.array([0, 0.3, 1, 1]) if thick else np.array([0.5, 0.7, 1, 1])
            if 0 <= p < S:
                if axis == 0:
                    arr[:, p] = arr[:, p] * 0.65 + col * 0.35
                else:
                    arr[S - 1 - p, :] = arr[S - 1 - p, :] * 0.65 + col * 0.35
            v += 0.25
    imgs.append(arr)
sheet = np.concatenate(imgs, axis=1)[::-1]
h, w = sheet.shape[:2]
im = bpy.data.images.new("sheet", w, h, alpha=False)
im.pixels.foreach_set(sheet[..., :4].reshape(-1).astype(np.float32))
im.filepath_raw = os.path.join(OUT, f"fit_{name}.png")
im.file_format = "PNG"
im.save()
print("FIT saved", im.filepath_raw)
