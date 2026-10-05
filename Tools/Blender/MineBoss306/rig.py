# #306 #11 mine boss (Meshy brute-b) — weld, scale to 2.5 m (feet at origin, front -Y), humanoid armature with Mixamo bone names
# fitted from the slice landmarks (analyze.py), automatic weights, emission dropped (ART-INK: nothing glows), FBX for Unity Humanoid.
# Headless: blender -b --python rig.py -- [check|export]
import bpy, bmesh, sys, json, math
from mathutils import Vector
from pathlib import Path
ROOT = Path(r"C:/Users/yj666/Oheangbu")
SRC = ROOT / "Art/Characters/MineBoss306/Source/brute-b/source.glb"
OUT = ROOT / "Art/Characters/MineBoss306/Rig"
OUT.mkdir(parents=True, exist_ok=True)
MODE = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "check"
TARGET_H = 2.5
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SRC))
body = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
body.name = "MineBoss306_Body"
for o in list(bpy.context.scene.objects):
    if o != body and o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
body.parent = None
# weld + bake transform: feet at 0, height TARGET_H
bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bm = bmesh.new(); bm.from_mesh(body.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bm.to_mesh(body.data); bm.free()
zs = [v.co.z for v in body.data.vertices]; z0, z1 = min(zs), max(zs); s = TARGET_H / (z1 - z0)
for v in body.data.vertices: v.co = Vector((v.co.x * s, v.co.y * s, (v.co.z - z0) * s))
body.data.update()
for p in body.data.polygons: p.use_smooth = True

# landmarks in the analyzed (normalized, z0=-0.95) frame -> scaled frame
def P(x, y, z): return Vector((x * s, y * s, (z - z0) * s))
L = {
 "Hips": P(0, .01, -.08), "Spine": P(0, .01, .07), "Spine1": P(0, .0, .24), "Spine2": P(0, -.005, .41),
 "Neck": P(0, -.01, .60), "Head": P(0, -.03, .69), "HeadTop": P(0, -.03, .949),
 "Shoulder": P(.07, .0, .58), "Arm": P(.35, .0, .595), "ForeArm": P(.502, .025, .274), "Hand": P(.64, -.05, .064), "HandEnd": P(.722, -.11, -.055),
 "UpLeg": P(.13, .01, -.11), "Leg": P(.19, .0, -.46), "Foot": P(.245, .04, -.84), "Toe": P(.255, -.08, -.925), "ToeEnd": P(.26, -.13, -.94),
}
def mirror(v): return Vector((-v.x, v.y, v.z))
arm = bpy.data.armatures.new("MineBoss306_Rig"); rig = bpy.data.objects.new("MineBoss306_Rig", arm)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
E = arm.edit_bones
def bone(name, head, tail, parent=None, connect=False, roll=0.0):
    b = E.new("mixamorig:" + name); b.head = head; b.tail = tail; b.roll = roll
    if parent: b.parent = E["mixamorig:" + parent]; b.use_connect = connect
    return b
bone("Hips", L["Hips"], L["Spine"])
bone("Spine", L["Spine"], L["Spine1"], "Hips", True)
bone("Spine1", L["Spine1"], L["Spine2"], "Spine", True)
bone("Spine2", L["Spine2"], L["Neck"], "Spine1", True)
bone("Neck", L["Neck"], L["Head"], "Spine2", True)
bone("Head", L["Head"], L["HeadTop"], "Neck", True)
for side, f in (("Left", 1), ("Right", -1)):
    m = (lambda v: v) if f == 1 else mirror
    # Mixamo convention: character's left = +X when the character faces -Y (Blender front view)
    bone(side + "Shoulder", m(L["Shoulder"]), m(L["Arm"]), "Spine2")
    bone(side + "Arm", m(L["Arm"]), m(L["ForeArm"]), side + "Shoulder", True)
    bone(side + "ForeArm", m(L["ForeArm"]), m(L["Hand"]), side + "Arm", True)
    bone(side + "Hand", m(L["Hand"]), m(L["HandEnd"]), side + "ForeArm", True)
    bone(side + "UpLeg", m(L["UpLeg"]), m(L["Leg"]), "Hips")
    bone(side + "Leg", m(L["Leg"]), m(L["Foot"]), side + "UpLeg", True)
    bone(side + "Foot", m(L["Foot"]), m(L["Toe"]), side + "Leg", True)
    bone(side + "ToeBase", m(L["Toe"]), m(L["ToeEnd"]), side + "Foot", True)
bpy.ops.armature.select_all(action='SELECT'); bpy.ops.armature.calculate_roll(type='GLOBAL_POS_Y')
bpy.ops.object.mode_set(mode='OBJECT')

report = dict(height=round(max(v.co.z for v in body.data.vertices), 3), verts=len(body.data.vertices), bones=len(arm.bones))
if MODE == "check":
    # joint markers + front/side workbench renders for a visual landmark check
    for name, p in L.items():
        for q in ([p, mirror(p)] if abs(p.x) > 1e-4 else [p]):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=.035, location=q); m = bpy.context.active_object; m.name = "J_" + name
            mat = bpy.data.materials.get("J") or bpy.data.materials.new("J"); mat.diffuse_color = (1, .1, .05, 1); m.data.materials.append(mat)
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_xray = True; sc.display.shading.xray_alpha = .55
    sc.render.resolution_x, sc.render.resolution_y = 700, 900
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.9
    body.data.materials.clear(); mb = bpy.data.materials.new("B"); mb.diffuse_color = (.75, .72, .66, 1); body.data.materials.append(mb)
    for view, loc, rot in (("front", (0, -6, 1.25), (math.radians(90), 0, 0)), ("side", (6, 0, 1.25), (math.radians(90), 0, math.radians(90)))):
        cam.location = loc; cam.rotation_euler = rot
        sc.render.filepath = str(OUT / f"check_{view}.png"); bpy.ops.render.render(write_still=True)
    print("RIGCHECK " + json.dumps(report))
else:
    # automatic weights
    body.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    groups = {g.index: g.name for g in body.vertex_groups}
    unweighted = sum(1 for v in body.data.vertices if not any(g.weight > 1e-4 for g in v.groups))
    report["unweighted"] = unweighted; report["groups"] = len(groups)
    # drop emission (ART-INK): keep base colour / normal / roughness only
    for mat in body.data.materials:
        if not mat or not mat.use_nodes: continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None: continue
        for key in ("Emission Color", "Emission"):
            if key in bsdf.inputs:
                for link in list(bsdf.inputs[key].links): mat.node_tree.links.remove(link)
                try: bsdf.inputs[key].default_value = (0, 0, 0, 1)
                except Exception: pass
        if "Emission Strength" in bsdf.inputs: bsdf.inputs["Emission Strength"].default_value = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "MineBoss306_rig.blend"))
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); rig.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT / "MineBoss306.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='FACE', path_mode='COPY', embed_textures=True, armature_nodetype='NULL')
    print("RIGEXPORT " + json.dumps(report))
