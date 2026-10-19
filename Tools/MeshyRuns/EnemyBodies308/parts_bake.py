"""D308-119 boss parts: Meshy part source.glb -> decimated FBX + 1K base color for Unity (Creatures308 parts[]).

blender -b --factory-startup -P parts_bake.py -- <source.glb> <out.fbx> <out_albedo.png> <triangles> [flat]
The part keeps its UVs (collapse decimate), is centred on its bounds and exported in metres with +Y up (Unity FBX axes).
Creatures308 scales a part to its row length and turns it by euler, so the export size does not matter.
"""
import sys
from pathlib import Path

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
src, out_fbx, out_png, tris = args[:4]
out_png = str(Path(out_png).resolve())   # Blender saves a relative image path against its own folder, not the shell's
flat = "flat" in args[4:]   # faceted (crystal): the Meshy smooth normals made the cluster read as round lumps
tris = int(tris)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
mod = obj.modifiers.new("decimate", "DECIMATE")
mod.ratio = min(1.0, tris / max(1, before))
bpy.ops.object.modifier_apply(modifier=mod.name)
after = sum(len(p.vertices) - 2 for p in obj.data.polygons)
if flat:
    if obj.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_flat()
# centre on the bounds (the part is held by Creatures308 at grip01 along its longest side)
xs = [v.co for v in obj.data.vertices]
lo = [min(c[i] for c in xs) for i in range(3)]
hi = [max(c[i] for c in xs) for i in range(3)]
mid = [(lo[i] + hi[i]) / 2 for i in range(3)]
for v in obj.data.vertices:
    v.co.x -= mid[0]; v.co.y -= mid[1]; v.co.z -= mid[2]
obj.name = Path(out_fbx).stem
# base color: the image feeding the Principled base color, scaled to 1024
img = None
for slot in obj.material_slots:
    if slot.material and slot.material.use_nodes:
        bsdf = next((n for n in slot.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        link = bsdf and next((l for l in slot.material.node_tree.links if l.to_node == bsdf and l.to_socket.name == "Base Color"), None)
        if link and link.from_node.type == "TEX_IMAGE":
            img = link.from_node.image
            break
assert img is not None, "no base color image"
img = img.copy()
img.scale(min(1024, img.size[0]), min(1024, img.size[1]))
img.filepath_raw = out_png
img.file_format = "PNG"
img.save()
Path(out_fbx).parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"MESH"}, apply_unit_scale=True, axis_forward="-Z", axis_up="Y",
                         path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE", add_leaf_bones=False)
size = [hi[i] - lo[i] for i in range(3)]
print("PART", Path(out_fbx).name, "tris", before, "->", after, "size", [round(s, 3) for s in size])
