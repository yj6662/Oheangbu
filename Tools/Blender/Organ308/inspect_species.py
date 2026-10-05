# #308 organ-art: read-only inspection of one Folklore298 species FBX (bones, rest/pose, mesh bounds).
# Headless: blender -b --factory-startup -t 4 --python inspect_species.py -- <species>
import bpy, json, sys
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import organ308_common as C  # noqa: E402

species = sys.argv[sys.argv.index("--") + 1]
arm, meshes = C.import_species(species)
out = dict(species=species, fbx=str(C.species_fbx(species)), objects=[], bones={})
for o in bpy.context.scene.objects:
    out["objects"].append(dict(name=o.name, type=o.type, parent=o.parent.name if o.parent else None,
                               loc=list(o.location), rot=list(o.rotation_euler), scale=list(o.scale)))
if arm is not None:
    mw = arm.matrix_world
    for b in arm.data.bones:
        pb = arm.pose.bones[b.name]
        out["bones"][b.name] = dict(parent=b.parent.name if b.parent else None,
                                    rest_head=C.r(mw @ b.head_local), rest_tail=C.r(mw @ b.tail_local),
                                    pose_head=C.r(mw @ pb.head), pose_tail=C.r(mw @ pb.tail),
                                    rest_matrix=[C.r(row, 6) for row in (mw @ b.matrix_local)],
                                    pose_matrix=[C.r(row, 6) for row in (mw @ pb.matrix)])
pts = C.evaluated_points(meshes)
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
out["mesh"] = dict(count=len(pts), min=C.r(lo), max=C.r(hi), tris=sum(len(m.data.polygons) for m in meshes))
dst = C.OUT / "Analysis" / (species + "_inspect.json")
dst.write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
print("INSPECT", species, "bones", len(out["bones"]), "mesh", out["mesh"])
