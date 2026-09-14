import argparse
import ctypes
import json
import os
import sys
from datetime import datetime, timezone

import bpy
from mathutils import Vector


class PerformanceInfo(ctypes.Structure):
    _fields_ = [
        ("cb", ctypes.c_uint),
        ("CommitTotal", ctypes.c_size_t),
        ("CommitLimit", ctypes.c_size_t),
        ("CommitPeak", ctypes.c_size_t),
        ("PhysicalTotal", ctypes.c_size_t),
        ("PhysicalAvailable", ctypes.c_size_t),
        ("SystemCache", ctypes.c_size_t),
        ("KernelTotal", ctypes.c_size_t),
        ("KernelPaged", ctypes.c_size_t),
        ("KernelNonpaged", ctypes.c_size_t),
        ("PageSize", ctypes.c_size_t),
        ("HandleCount", ctypes.c_uint),
        ("ProcessCount", ctypes.c_uint),
        ("ThreadCount", ctypes.c_uint),
    ]


def commit_ratio():
    info = PerformanceInfo()
    info.cb = ctypes.sizeof(info)
    if not ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(info), info.cb):
        raise RuntimeError("GetPerformanceInfo failed")
    return float(info.CommitTotal) / float(info.CommitLimit)


def require_headroom(stage):
    ratio = commit_ratio()
    if ratio >= 0.85:
        raise RuntimeError(f"Stopped at {stage}: system commit {ratio:.1%} >= 85%")
    print(f"COMMIT {stage}: {ratio:.1%}", flush=True)
    return ratio


def triangles(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def world_bounds(obj):
    points = [obj.matrix_world @ Vector(obj.bound_box[i]) for i in range(8)]
    minimum = [min(p[axis] for p in points) for axis in range(3)]
    maximum = [max(p[axis] for p in points) for axis in range(3)]
    return {
        "min": minimum,
        "max": maximum,
        "size": [maximum[i] - minimum[i] for i in range(3)],
        "center": [(maximum[i] + minimum[i]) * 0.5 for i in range(3)],
    }


def import_fbx(path):
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
    except AttributeError:
        bpy.ops.wm.fbx_import(filepath=path)
    return [obj for obj in bpy.data.objects if obj not in before]


def export_fbx(path):
    try:
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH"},
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            bake_anim=False,
            apply_unit_scale=True,
            use_space_transform=True,
            axis_forward="-Z",
            axis_up="Y",
            path_mode="AUTO",
        )
    except AttributeError:
        bpy.ops.wm.fbx_export(filepath=path, export_selected_objects=True)


def main():
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--target", required=True, type=int)
    args = parser.parse_args(raw)

    source = os.path.abspath(args.input)
    output = os.path.abspath(args.output)
    if not os.path.isfile(source):
        raise FileNotFoundError(source)
    if os.path.exists(output):
        raise FileExistsError(f"Refusing to overwrite {output}")

    started_commit = require_headroom("before import")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    imported = import_fbx(source)
    require_headroom("after import")
    meshes = [obj for obj in imported if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("FBX contains no mesh objects")

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    house = bpy.context.view_layer.objects.active
    house.name = os.path.splitext(os.path.basename(output))[0]
    source_triangles = triangles(house.data)
    source_bounds = world_bounds(house)
    source_materials = [slot.material.name if slot.material else "<NULL>" for slot in house.material_slots]
    if args.target <= 0 or args.target >= source_triangles:
        raise ValueError(f"Target {args.target} must be below source {source_triangles}")

    current = source_triangles
    for attempt in range(3):
        require_headroom(f"before decimate pass {attempt + 1}")
        modifier = house.modifiers.new(name=f"SourceDerivedDecimate_{attempt + 1}", type="DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = min(1.0, (args.target / current) * (0.985 if attempt == 0 else 0.975))
        modifier.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = house
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        current = triangles(house.data)
        print(f"DECIMATE pass={attempt + 1} tris={current} target={args.target}", flush=True)
        if current <= args.target:
            break
    if current > args.target:
        raise RuntimeError(f"Decimation did not meet target: {current} > {args.target}")
    if not house.data.uv_layers:
        raise RuntimeError("Decimated mesh lost all UV layers")
    if not house.material_slots:
        raise RuntimeError("Decimated mesh lost all material slots")

    final_bounds = world_bounds(house)
    require_headroom("before export")
    os.makedirs(os.path.dirname(output), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    house.select_set(True)
    bpy.context.view_layer.objects.active = house
    export_fbx(output)
    if not os.path.isfile(output) or os.path.getsize(output) < 1024:
        raise RuntimeError("FBX export was not written")

    metadata = {
        "utc": datetime.now(timezone.utc).isoformat(),
        "source": source,
        "output": output,
        "method": "Blender collapse decimate on joined disconnected source meshes; source transforms, material slots and UV layers retained",
        "sourceTriangles": source_triangles,
        "targetTriangles": args.target,
        "outputTriangles": current,
        "sourceMeshObjects": len(meshes),
        "materialSlots": source_materials,
        "uvLayers": [layer.name for layer in house.data.uv_layers],
        "sourceBounds": source_bounds,
        "outputBounds": final_bounds,
        "commitBefore": started_commit,
        "commitAfter": require_headroom("after export"),
    }
    with open(output + ".json", "w", encoding="utf-8") as stream:
        json.dump(metadata, stream, indent=2, ensure_ascii=False)
    print(json.dumps(metadata, indent=2, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
