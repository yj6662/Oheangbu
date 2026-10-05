# #308 organ-art: plain species previews (front / side / 3/4 / top-of-region) used to choose organ placement.
# Headless: blender -b --factory-startup -t 4 --python preview_species.py -- <species> [focus_bone] [radius]
import bpy, sys
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import organ308_common as C  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
species = args[0]
arm, meshes = C.import_species(species)
pts = C.evaluated_points(meshes)
lo, hi = C.bounds(pts)
dst = C.OUT / "Analysis" / "previews"
dst.mkdir(parents=True, exist_ok=True)
C.setup_render(448, 448)
body_mat = C.flat_material("Preview_Body", (0.78, 0.76, 0.72, 1))
for m in meshes:
    m.data.materials.clear(); m.data.materials.append(body_mat)
if len(args) >= 3:
    center = arm.matrix_world @ arm.pose.bones[args[1]].head
    radius = float(args[2])
else:
    center = (lo + hi) * 0.5
    radius = max((hi - lo).length * 0.55, 0.05)
tag = species + ("_" + args[1] if len(args) >= 3 else "")
for name, az, el in (("front", 0, 5), ("side", 90, 5), ("q34", 40, 15), ("back", 180, 10)):
    C.render_view(dst / (tag + "_" + name + ".png"), center, radius, az, el)
print("PREVIEW", tag, "bounds", C.r(lo), C.r(hi))
