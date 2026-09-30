"""Build candidate-only KCISA part LODs; run with Blender --background --python.

Inputs come from ExportCrossingLods296, in neutral Unity coordinates. Each level
starts from the original material part. UVs remain corner attributes throughout
QEM, and output triangles retain their own corner UV and normal (no UV weld).
"""
import hashlib
import json
from pathlib import Path

import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296/CrossingLods"
INPUT = OUT / "sources.json"
raw = json.loads(INPUT.read_text(encoding="utf-8"))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)


def budgets(source):
    if "Parapet" in source:
        return (240, 72, 24)
    if "SM_CW.prefab" in source:
        return (160, 48, 20)
    if "StonePillar" in source:
        return (160, 48, 16)
    if "Floor_stone" in source:
        return (160, 48, 12)
    if "Column" in source:
        return (120, 36, 16)
    return (120, 32, 12)


def export(mesh):
    mesh.calc_loop_triangles()
    vs, ns, us, ts = [], [], [], []
    layer = mesh.uv_layers.active
    for triangle in mesh.loop_triangles:
        for loop_index in triangle.loops:
            vert = mesh.vertices[mesh.loops[loop_index].vertex_index]
            normal = mesh.corner_normals[loop_index].vector
            uv = layer.data[loop_index].uv
            vs.append(dict(x=vert.co.x, y=vert.co.y, z=vert.co.z))
            ns.append(dict(x=normal.x, y=normal.y, z=normal.z))
            us.append(dict(x=uv.x, y=uv.y))
            ts.append(len(ts))
    if not ts:
        raise RuntimeError("QEM produced an empty part")
    return dict(Vertices=vs, Normals=ns, UV=us, Triangles=ts)


rows = []
audit = []
for source in raw["Sources"]:
    data = source["Mesh"]
    verts = np.array([[v[k] for k in "xyz"] for v in data["Vertices"]])
    uv = np.array([[v[k] for k in "xy"] for v in data["UV"]])
    triangles = np.array(data["Triangles"]).reshape(-1, 3)
    low, high = verts.min(axis=0), verts.max(axis=0)
    bounds_size = high - low
    name = Path(source["Source"]).stem + "_part" + str(source["Part"])
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts.tolist(), [], triangles.tolist())
    mesh.update()
    layer = mesh.uv_layers.new(name="OriginalSourceUV")
    for polygon in mesh.polygons:
        for index in polygon.loop_indices:
            layer.data[index].uv = uv[mesh.loops[index].vertex_index]
    # Unity source vertices split at UV and hard-normal seams. Weld geometry
    # alone before QEM; Blender keeps each face corner's separate UV attribute.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.000001)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    source_count = len(triangles)
    levels, actual, drifts, adjustments = [], [], [], []
    for level, limit in enumerate(budgets(source["Source"])):
        bpy.ops.object.select_all(action="DESELECT")
        lod = obj.copy()
        lod.data = mesh.copy()
        bpy.context.collection.objects.link(lod)
        bpy.context.view_layer.objects.active = lod
        lod.select_set(True)
        if source_count > limit:
            modifier = lod.modifiers.new("Independent_source_QEM", "DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = limit / source_count
            modifier.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        tri = lod.modifiers.new("ExplicitTriangles", "TRIANGULATE")
        bpy.ops.object.modifier_apply(modifier=tri.name)
        points = np.array([tuple(v.co) for v in lod.data.vertices])
        if not len(points):
            raise RuntimeError(name + " collapsed entirely")
        new_low, new_high = points.min(axis=0), points.max(axis=0)
        pre_drift = float(max(abs(new_low - low).max(), abs(new_high - high).max()))
        new_size = new_high - new_low
        if np.any((bounds_size > 0.0001) & (new_size < bounds_size * 0.4)):
            raise RuntimeError(name + " LOD" + str(level) + " lost a source dimension")
        # Restore the exact outside dimensions after QEM. This bounds correction
        # is recorded; no source file, texture or UV coordinate is modified.
        fixed = points.copy()
        for axis in range(3):
            if bounds_size[axis] > 0.0001:
                fixed[:, axis] = low[axis] + (points[:, axis] - new_low[axis]) * bounds_size[axis] / new_size[axis]
            else:
                fixed[:, axis] = low[axis]
        adjustment = float(np.linalg.norm(fixed - points, axis=1).max())
        for vert, co in zip(lod.data.vertices, fixed):
            vert.co = co
        lod.data.update()
        exported = export(lod.data)
        levels.append(exported)
        actual.append(len(exported["Triangles"]) // 3)
        drifts.append(float(max(abs(fixed.min(axis=0) - low).max(), abs(fixed.max(axis=0) - high).max())))
        adjustments.append(adjustment)
        audit.append(dict(Part=name, Level=level, SourceTriangles=source_count,
                          Triangles=actual[-1], PreBoundsDrift=pre_drift,
                          FinalBoundsDrift=drifts[-1], MaximumBoundsCorrection=adjustment,
                          SourceSize=bounds_size.tolist()))
        lod.name = name + "_LOD" + str(level)
        lod.select_set(False)
    rows.append(dict(Source=source["Source"], Material=source["Material"],
                     SourceSha256=source["SourceSha256"], Part=source["Part"],
                     Levels=levels, ActualTriangles=actual, BoundsDrift=drifts))
result = dict(Method="Independent Blender collapse QEM per original KCISA material part. Original face-corner UVs survive QEM; output is split by triangle corner to preserve seams. Exact source outer bounds restored with measured corrections. All three levels start from source, never from the prior LOD. No texture bake or source mutation.",
              ExportSha256=hashlib.sha256(INPUT.read_bytes()).hexdigest(), Sources=rows)
(OUT / "lods.json").write_text(json.dumps(result, separators=(",", ":")), encoding="utf-8")
(OUT / "lod-audit.json").write_text(json.dumps(dict(Blender=bpy.app.version_string,
    ExportSha256=result["ExportSha256"], Parts=audit), indent=2), encoding="utf-8")
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "source-lods296.blend"))
print("CROSSING296", json.dumps(dict(Parts=len(rows),
    SourceTriangles=sum(len(s["Mesh"]["Triangles"]) // 3 for s in raw["Sources"]),
    ActualTriangles=[sum(s["ActualTriangles"][level] for s in rows) for level in range(3)],
    MaximumCorrection=max(a["MaximumBoundsCorrection"] for a in audit))))
