"""Re-import Jujak308.fbx and report what Unity will see (run headless, standalone)."""
import bpy, numpy as np
from pathlib import Path
fbx = Path(__file__).resolve().parents[3] / "Oheangbu" / "Assets" / "_Project" / "Art" / "Characters" / "Creatures308" / "Jujak308" / "Jujak308.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
print("CHK armatures", [a.name for a in arms], "bones", [len(a.data.bones) for a in arms])
allco = []
tris = 0
for o in meshes:
    me = o.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(n, 3)
    mw = np.array(o.matrix_world)
    co = co @ mw[:3, :3].T + mw[:3, 3]
    allco.append(co)
    me.calc_loop_triangles()
    tris += len(me.loop_triangles)
    mx = max((len([g for g in v.groups if g.weight > 1e-5]) for v in me.vertices), default=0)
    print("CHK mesh", o.name, "verts", n, "tris", len(me.loop_triangles), "mats", [m.name for m in me.materials], "max influences", mx,
          "scale", tuple(round(s, 3) for s in o.scale), "rot", tuple(round(r, 3) for r in o.rotation_euler))
allco = np.concatenate(allco)
print("CHK tris total", tris)
print("CHK bbox min", allco.min(0).round(3), "max", allco.max(0).round(3), "extent", (allco.max(0) - allco.min(0)).round(3))
print("CHK actions", [(a.name, [int(x) for x in a.frame_range]) for a in bpy.data.actions])
for m in bpy.data.materials:
    imgs = [n.image.filepath for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image] if m.use_nodes else []
    em = [n.inputs["Emission Strength"].default_value for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"] if m.use_nodes else []
    print("CHK material", m.name, "images", [Path(i).name for i in imgs], "emission strength", em)
# beak direction: head vertex furthest along -Y of the body mesh at crest height
body = [o for o in meshes if "Body" in o.name][0]
n = len(body.data.vertices)
co = np.empty(n * 3)
body.data.vertices.foreach_get("co", co)
co = co.reshape(n, 3)
mw = np.array(body.matrix_world)
co = co @ mw[:3, :3].T + mw[:3, 3]
print("CHK beak (min y)", co[np.argmin(co[:, 1])].round(2), "tail tip (max y)", co[np.argmax(co[:, 1])].round(2))
