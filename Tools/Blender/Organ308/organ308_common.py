# #308 organ-art shared helpers (Blender 5.0 headless). Read-only on every source asset.
import bpy
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:/Users/yj666/Oheangbu")
ASSETS = ROOT / "Oheangbu/Assets/_Project"
FOLK = ASSETS / "Art/Characters/Folklore298"
OUT = ROOT / "Art/Characters/Organ308"

# The FBX each PF_<species>.prefab actually references (GUID-matched 2026-10-02).
SPECIES_FBX = {
    "dokkaebi": "Models/dokkaebi/dokkaebi-rig_result_rigged_character_fbx_url.fbx",
    "agwi": "Models/agwi/agwi-rig_result_rigged_character_fbx_url.fbx",
    "changgui": "Models/changgui/changgui-rig_result_rigged_character_fbx_url.fbx",
    "bulgasari": "Models/bulgasari/run-05-cleanup_creature.fbx",
    "fox_spirit": "Models/fox_spirit/run-07-cleanup_creature.fbx",
    "imugi": "Models/imugi/run-09_creature.fbx",
}


def species_fbx(species):
    return FOLK / SPECIES_FBX[species]


def species_prefab(species):
    return FOLK / "Prefabs" / ("PF_" + species + ".prefab")


def r(v, n=5):
    return [round(float(c), n) for c in v]


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_species(species):
    """Import the species FBX exactly as shipped (no bone re-orientation). Returns (armature, meshes)."""
    reset()
    bpy.ops.import_scene.fbx(filepath=str(species_fbx(species)), automatic_bone_orientation=False,
                             ignore_leaf_bones=False, use_anim=False)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    return (arms[0] if arms else None), meshes


def evaluated_points(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for m in meshes:
        ev = m.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        pts.extend(mw @ v.co for v in me.vertices)
        ev.to_mesh_clear()
    return pts


def bounds(pts):
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


# ---------------------------------------------------------------- review rendering (Workbench, light)
PAPER = (0.968, 0.945, 0.894)  # 한지 #F7F1E4


def setup_render(w, h):
    sc = bpy.context.scene
    try:
        sc.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    sh.show_cavity = True
    sh.cavity_type = 'BOTH'
    sh.show_object_outline = True
    sh.object_outline_color = (0.165, 0.149, 0.133)  # 먹 #2A2622
    sh.show_specular_highlight = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    sc.world.color = PAPER
    sc.display.render_aa = '8'
    try:
        sc.view_settings.view_transform = 'Standard'
    except TypeError:
        pass


def flat_material(name, rgba, metallic=0.0, roughness=0.85):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = rgba
    m.metallic = metallic
    m.roughness = roughness
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    for key in ("Emission Color", "Emission"):
        if key in bsdf.inputs and bsdf.inputs[key].type == 'RGBA':
            bsdf.inputs[key].default_value = (0, 0, 0, 1)
    if "Emission Strength" in bsdf.inputs:
        bsdf.inputs["Emission Strength"].default_value = 0.0
    return m


def _camera():
    cam = bpy.data.objects.get("Organ308Cam")
    if cam is None:
        cam = bpy.data.objects.new("Organ308Cam", bpy.data.cameras.new("Organ308Cam"))
        bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def render_view(path, center, radius, azimuth_deg, elevation_deg, front=Vector((0, -1, 0))):
    """Orthographic view; azimuth 0 = from the model's front (Blender -Y), +az turns toward +X (model's left)."""
    import math
    cam = _camera()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.0 * radius
    cam.data.clip_start = radius * 0.01
    cam.data.clip_end = radius * 40
    a, e = math.radians(azimuth_deg), math.radians(elevation_deg)
    f = front.normalized()
    side = Vector((-f.y, f.x, 0)).normalized()  # front rotated +90 about Z
    d = (f * math.cos(a) + side * math.sin(a)) * math.cos(e) + Vector((0, 0, math.sin(e)))
    cam.location = center + d * radius * 8
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
