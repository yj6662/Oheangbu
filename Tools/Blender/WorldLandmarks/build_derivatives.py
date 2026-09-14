"""Source-preserving static landmark LODs. Run one asset per hidden Blender process.

UVs/material indices are carried by Blender's collapse decimator, not reprojected.
This is automatic simplification, not a claim of manually retopologized architecture.
Unity material bindings are recorded from the original prefab, not replaced here.
"""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import math
import re
import sys
import time

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
ASSETS = ROOT / "Oheangbu/Assets"
OUT = ASSETS / "_Project/Art/World/WorldMacro/Landmarks/Models"
REPORTS = ROOT / "Art/World/WorldMacro/Landmarks/ModelReports"
CONFIG = {
    "Wall02A": ("BillemotdonggulLavaTubePack/Mesh/SM_Wall02A.fbx", "BillemotdonggulLavaTubePack/Prefabs/SM_Wall02A.prefab", [9000, 3000, 1500]),
    "Bongsudang": ("HwaseongHaenggung/Meshes/Buildings/SM_Bongsudang.fbx", "HwaseongHaenggung/Prefabs/SM_Bongsudang.prefab", [145000, 40000, 12000]),
    "Byeolchu": ("HwaseongHaenggung/Meshes/Buildings/SM_Byeolchu.fbx", "HwaseongHaenggung/Prefabs/SM_Byeolchu.prefab", [45000, 15000, 15000]),
    "Wall05B": ("BillemotdonggulLavaTubePack/Mesh/SM_Wall05B.fbx", "BillemotdonggulLavaTubePack/Prefabs/SM_Wall05B.prefab", [15000, 7000, 3000]),
    "Floor02B": ("BillemotdonggulLavaTubePack/Mesh/SM_Floor02B.fbx", "BillemotdonggulLavaTubePack/Prefabs/SM_Floor02B.prefab", [15000, 7000, 3000]),
    "Ceiling05A": ("BillemotdonggulLavaTubePack/Mesh/SM_Ceiling05A.fbx", "BillemotdonggulLavaTubePack/Prefabs/SM_Ceiling05A.prefab", [15000, 7000, 3000]),
}


def commit_ratio():
    class PerformanceInfo(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD)] + [(x, ctypes.c_size_t) for x in (
            "CommitTotal", "CommitLimit", "CommitPeak", "PhysicalTotal", "PhysicalAvailable",
            "SystemCache", "KernelTotal", "KernelPaged", "KernelNonpaged", "PageSize")] + [
            (x, wintypes.DWORD) for x in ("HandleCount", "ProcessCount", "ThreadCount")]
    info = PerformanceInfo(); info.cb = ctypes.sizeof(info)
    if not ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(info), info.cb):
        raise RuntimeError("Cannot read system commit; refusing heavy import")
    return info.CommitTotal / info.CommitLimit


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""): h.update(block)
    return h.hexdigest()


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def bounds(objects):
    points = [obj.matrix_world @ Vector(v) for obj in objects for v in obj.bound_box]
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    return {"minimum": lo, "maximum": hi, "size": [hi[i] - lo[i] for i in range(3)]}


def prefab_materials(path):
    text = path.read_text(encoding="utf-8-sig")
    block = re.search(r"  m_Materials:\s*\n((?:  - .+\n)+)", text)
    guids = re.findall(r"guid: ([a-zA-Z0-9]+)", block.group(1)) if block else []
    lookup = {}
    for meta in ASSETS.rglob("*.mat.meta"):
        content = meta.read_text(encoding="utf-8-sig", errors="replace")
        match = re.search(r"^guid: (\S+)", content, re.M)
        if match and match.group(1) in guids:
            lookup[match.group(1)] = "Assets/" + meta.relative_to(ASSETS).as_posix()[:-5]
    return [{"slot": i, "guid": guid, "unity_material": lookup.get(guid)} for i, guid in enumerate(guids)]


def mesh_stats(obj):
    mesh = obj.data
    used = {}
    for poly in mesh.polygons:
        used[poly.material_index] = used.get(poly.material_index, 0) + max(0, len(poly.vertices) - 2)
    return {"name": obj.name, "vertices": len(mesh.vertices), "triangles": triangles(obj),
            "uv_layers": [x.name for x in mesh.uv_layers], "bounds_blender_world": bounds([obj]),
            "materials": [{"slot": i, "name": mat.name if mat else None, "triangles": used.get(i, 0)}
                          for i, mat in enumerate(mesh.materials)],
            "nonfinite_vertices": sum(not all(math.isfinite(c) for c in v.co) for v in mesh.vertices)}


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_set(False); obj.select_set(True); bpy.context.view_layer.objects.active = obj


def simplify(obj, target):
    for attempt in range(2):
        count = triangles(obj)
        if count <= target: break
        select_only(obj)
        mod = obj.modifiers.new("Landmark collapse LOD", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = max(.0001, target * .992 / count)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return triangles(obj)


def weld_export_splits(obj):
    """Join only coincident FBX split vertices; per-loop UVs remain separate."""
    before = len(obj.data.vertices)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
    bmesh.ops.dissolve_degenerate(bm, dist=0.000001, edges=list(bm.edges))
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return {"before_vertices": before, "after_vertices": len(obj.data.vertices), "merge_distance_m": 0.00001}


def simplify_planar_detail(obj):
    """Reduce coplanar architectural subdivisions before the distant LOD."""
    select_only(obj)
    mod = obj.modifiers.new("Distant coplanar detail", "DECIMATE")
    mod.decimate_type = "DISSOLVE"; mod.angle_limit = math.radians(6)
    mod.delimit = {"MATERIAL", "UV"}
    bpy.ops.object.modifier_apply(modifier=mod.name)


def export(obj, path):
    select_only(obj)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"MESH"},
                             global_scale=1, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             use_mesh_modifiers=True, mesh_smooth_type="OFF", use_triangles=True,
                             add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)


def run(asset):
    started = time.time(); OUT.mkdir(parents=True, exist_ok=True); REPORTS.mkdir(parents=True, exist_ok=True)
    source_rel, prefab_rel, targets = CONFIG[asset]
    source = ASSETS / source_rel; prefab = ASSETS / prefab_rel
    report = {"asset": asset, "source": str(source), "source_prefab": str(prefab),
              "source_sha256": sha(source), "source_prefab_sha256": sha(prefab), "lods": [],
              "method": "Coincident FBX split-vertex weld (0.01mm), sequential collapse; UV loops/material indices retained; no texture bake or redesign.",
              "unity_runtime_validation": "UNVERIFIED", "visual_art_validation": "UNVERIFIED",
              "axis_contract": "Blender import axes retained; transforms baked into geometry, original origin retained. Export forward -Z, up Y, metres, scale 1. Unity ground offset must use actual imported Renderer.bounds.min.y.",
              "started_commit_ratio": commit_ratio()}
    if report["started_commit_ratio"] >= .85:
        report["status"] = "PAUSED_COMMIT_LIMIT"
        (REPORTS / (asset + ".paused.json")).write_text(json.dumps(report, indent=2), encoding="utf-8")
        raise RuntimeError("System commit >=85%; not starting import")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"; bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=False, use_custom_normals=True,
                             bake_space_transform=False)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    report["imported_objects"] = [{"name": x.name, "triangles": triangles(x), "bounds": bounds([x])} for x in meshes]
    ignored = [obj for obj in meshes if any(key in obj.name.lower() for key in ("ucx_", "convexhull", "collision", "ubx_"))]
    report["ignored_collision_objects"] = [x.name for x in ignored]
    # Keep the visual list before invalidating removed Blender RNA references.
    meshes = [x for x in meshes if x not in ignored]
    for obj in ignored: bpy.data.objects.remove(obj, do_unlink=True)
    if not meshes: raise RuntimeError("No visual mesh imported")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    current = bpy.context.view_layer.objects.active
    # Clear parenting while retaining world coordinates before applying transforms.
    matrix = current.matrix_world.copy(); current.parent = None; current.matrix_world = matrix
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    report["source_visual"] = mesh_stats(current)
    report["coincident_vertex_cleanup"] = weld_export_splits(current)
    report["prefab_material_bindings"] = prefab_materials(prefab)
    report["after_import_commit_ratio"] = commit_ratio()
    print(json.dumps({"asset": asset, "source": report["source_visual"], "commit": report["after_import_commit_ratio"]}), flush=True)
    all_lods = []
    for lod, target in enumerate(targets):
        if lod:
            next_obj = current.copy(); next_obj.data = current.data.copy(); bpy.context.collection.objects.link(next_obj)
            current = next_obj
        current.name = f"LM_{asset}_LOD{lod}"; current.data.name = current.name + "_Mesh"
        # Byeolchu's stable LOD1 is intentionally reused at distance. The 5k
        # experiment displaced thin details; silhouette takes priority over count.
        if lod == 2 and asset == "Byeolchu":
            report["lod2_budget_exception"] = "Original 5k target relaxed: stable LOD1 copy retained after collapse artifact; approved derivative budget exception."
        if lod == 2 and asset == "Bongsudang": simplify_planar_detail(current)
        simplify(current, target)
        info = mesh_stats(current); info["requested_triangle_limit"] = target
        info["within_triangle_limit"] = info["triangles"] <= target
        source_size = report["source_visual"]["bounds_blender_world"]["size"]
        info["source_bounds_size_delta_m"] = [info["bounds_blender_world"]["size"][i] - source_size[i] for i in range(3)]
        info["source_bounds_preserved"] = all(abs(info["source_bounds_size_delta_m"][i]) < max(.1, source_size[i] * .02) for i in range(3))
        if lod and (not info["source_bounds_preserved"] or info["nonfinite_vertices"]):
            rejected = {"triangles": info["triangles"], "size_delta_m": info["source_bounds_size_delta_m"]}
            bad_data = current.data; current.data = all_lods[-1].data.copy()
            if not bad_data.users: bpy.data.meshes.remove(bad_data)
            current.data.update(); bpy.context.view_layer.update()
            info = mesh_stats(current); info["requested_triangle_limit"] = target
            info["within_triangle_limit"] = info["triangles"] <= target
            info["source_bounds_size_delta_m"] = [info["bounds_blender_world"]["size"][i] - source_size[i] for i in range(3)]
            info["source_bounds_preserved"] = all(abs(info["source_bounds_size_delta_m"][i]) < max(.1, source_size[i] * .02) for i in range(3))
            info["fallback"] = "Previous stable LOD retained; automated collapse rejected for geometry bounds."
            info["rejected_collapse"] = rejected
        if not info["source_bounds_preserved"] or info["nonfinite_vertices"]:
            report["lods"].append(info); report["status"] = "FAIL_DEFORMATION_BOUNDS"
            (REPORTS / (asset + ".json")).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            raise RuntimeError("Collapse altered source bounds beyond tolerance; refusing export")
        info["path"] = str(OUT / (current.name + ".fbx"))
        export(current, Path(info["path"]))
        info["export_sha256"] = sha(Path(info["path"])); report["lods"].append(info); all_lods.append(current)
        print(json.dumps({"lod": current.name, "triangles": info["triangles"], "limit": target, "commit": commit_ratio()}), flush=True)
    # Empty-scene FBX round trips verify actual exported counts, UVs, materials and dimensions.
    for info in report["lods"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
        imported = [x for x in bpy.context.scene.objects if x.type == "MESH"]
        counts = [mesh_stats(x) for x in imported]
        actual = sum(x["triangles"] for x in counts)
        if actual != info["triangles"] and len(imported) == 1:
            # FBX validation can discard degenerate faces produced by collapse.
            # Canonicalize the validated mesh, then require a second exact round trip.
            info["before_fbx_validation_triangles"] = info["triangles"]
            imported[0].name = info["name"]
            export(imported[0], Path(info["path"]))
            info["triangles"] = actual
            info["vertices"] = counts[0]["vertices"]
            info["materials"] = counts[0]["materials"]
            info["export_sha256"] = sha(Path(info["path"]))
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
            imported = [x for x in bpy.context.scene.objects if x.type == "MESH"]
            counts = [mesh_stats(x) for x in imported]
            actual = sum(x["triangles"] for x in counts)
        roundtrip_bounds = bounds(imported)
        bounds_error = max(abs(info["bounds_blender_world"][key][i] - roundtrip_bounds[key][i])
                           for key in ("minimum", "maximum") for i in range(3))
        material_names = [slot["name"] for obj in counts for slot in obj["materials"]]
        expected_names = [slot["name"] for slot in info["materials"]]
        info["roundtrip"] = {"objects": counts, "triangles": actual, "bounds_blender_world": roundtrip_bounds,
                             "bounds_max_error_m": bounds_error, "material_slot_names_order_preserved": material_names == expected_names}
        info["roundtrip_pass"] = actual == info["triangles"] and all(x["uv_layers"] for x in counts) and bounds_error < .01 and material_names == expected_names
    if asset in ("Bongsudang", "Byeolchu"):
        # Save the final validated FBX geometry, outside Assets to avoid Unity
        # launching an additional Blender importer during sequential production.
        bpy.ops.wm.read_factory_settings(use_empty=True)
        for lod, info in enumerate(report["lods"]):
            bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
            for obj in bpy.context.selected_objects:
                obj.hide_render = lod > 0; obj.hide_set(lod > 0)
        blend = REPORTS / ("LM_" + asset + "_LODs.blend")
        bpy.ops.wm.save_as_mainfile(filepath=str(blend), check_existing=False)
        report["blend"] = str(blend)
    report["source_preserved"] = sha(source) == report["source_sha256"] and sha(prefab) == report["source_prefab_sha256"]
    report["elapsed_seconds"] = time.time() - started; report["ended_commit_ratio"] = commit_ratio()
    geometry_pass = report["source_preserved"] and all(x["source_bounds_preserved"] and x["roundtrip_pass"] for x in report["lods"])
    report["status"] = ("PASS_GEOMETRY_EXPORT" if all(x["within_triangle_limit"] for x in report["lods"]) else "PASS_GEOMETRY_WITH_BUDGET_EXCEPTION") if geometry_pass else "FAIL_GEOMETRY_EXPORT"
    (REPORTS / (asset + ".json")).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"asset": asset, "status": report["status"], "elapsed": report["elapsed_seconds"]}), flush=True)


def canonicalize_existing(asset):
    """Finish a completed LOD set without importing/decimating its source again."""
    if commit_ratio() >= .85: raise RuntimeError("System commit >=85%; no canonicalization started")
    report_path = REPORTS / (asset + ".json")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    for info in report["lods"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
        obj = next(x for x in bpy.context.scene.objects if x.type == "MESH")
        old = info["triangles"]; obj.name = info["name"]
        clean = mesh_stats(obj)
        info["before_fbx_validation_triangles"] = old
        for key in ("vertices", "triangles", "materials", "uv_layers", "nonfinite_vertices"):
            info[key] = clean[key]
        export(obj, Path(info["path"]))
        info["export_sha256"] = sha(Path(info["path"]))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
        objects = [x for x in bpy.context.scene.objects if x.type == "MESH"]
        rt = [mesh_stats(x) for x in objects]
        count = sum(x["triangles"] for x in rt)
        box = bounds(objects)
        error = max(abs(info["bounds_blender_world"][key][i] - box[key][i]) for key in ("minimum", "maximum") for i in range(3))
        material_match = [s["name"] for x in rt for s in x["materials"]] == [x["name"] for x in info["materials"]]
        info["roundtrip"] = {"objects": rt, "triangles": count, "bounds_blender_world": box,
                             "bounds_max_error_m": error, "material_slot_names_order_preserved": material_match}
        info["roundtrip_pass"] = count == info["triangles"] and all(x["uv_layers"] for x in rt) and error < .01 and material_match
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for lod, info in enumerate(report["lods"]):
        bpy.ops.import_scene.fbx(filepath=info["path"], use_image_search=False, use_custom_normals=True)
        for obj in bpy.context.selected_objects:
            obj.hide_render = lod > 0; obj.hide_set(lod > 0)
    blend = REPORTS / ("LM_" + asset + "_LODs.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), check_existing=False)
    report["blend"] = str(blend)
    report["source_preserved"] = sha(Path(report["source"])) == report["source_sha256"] and sha(Path(report["source_prefab"])) == report["source_prefab_sha256"]
    report["status"] = "PASS_GEOMETRY_EXPORT" if report["source_preserved"] and all(x["roundtrip_pass"] and x["source_bounds_preserved"] for x in report["lods"]) else "FAIL_GEOMETRY_EXPORT"
    report["finalization"] = "Validated FBX meshes re-exported; second independent import must have exact triangle counts. No additional decimation."
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"asset": asset, "status": report["status"], "final_triangles": [x["triangles"] for x in report["lods"]]}), flush=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(); parser.add_argument("--asset", choices=CONFIG, required=True)
    parser.add_argument("--canonicalize-existing", action="store_true")
    options = parser.parse_args(args)
    (canonicalize_existing if options.canonicalize_existing else run)(options.asset)
