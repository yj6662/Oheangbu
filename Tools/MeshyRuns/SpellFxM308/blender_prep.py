"""Variant 우/M: headless Blender (Workbench only, no Cycles/EEVEE) - reads each Meshy GLB, reports bounds / triangles, normalises (height 1 m, pivot bottom-centre,
front facing -Y = Blender's front), writes a lean FBX (mesh + UV only) and a 2048 px colour JPG to u_M/models/, and Workbench previews to u_M/preview_<name>.png.
blender.exe -b --python Tools/MeshyRuns/SpellFxM308/blender_prep.py -- <repo root> name1 name2 ...
"""
import sys, os, math, bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
ROOT, names = argv[0], argv[1:]
SRC = os.path.join(ROOT, 'Art/SpellVFX308/Restart/u_M/meshy/Source')
OUT = os.path.join(ROOT, 'Art/SpellVFX308/Restart/u_M')
MODELS = os.path.join(OUT, 'models'); os.makedirs(MODELS, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def render(name, tag, loc, rot, ortho=None):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.display.shading.light = 'FLAT'
    scn.display.shading.color_type = 'TEXTURE'
    scn.display.shading.show_backface_culling = False
    scn.render.resolution_x = 640; scn.render.resolution_y = 640
    scn.render.film_transparent = False
    scn.world = bpy.data.worlds.new('w') if scn.world is None else scn.world
    scn.world.color = (0.55, 0.55, 0.55)
    cam = bpy.data.cameras.new('c'); co = bpy.data.objects.new('c', cam); scn.collection.objects.link(co)
    co.location = loc; co.rotation_euler = [math.radians(a) for a in rot]
    if ortho: cam.type = 'ORTHO'; cam.ortho_scale = ortho
    scn.camera = co
    scn.render.filepath = os.path.join(OUT, 'preview_%s_%s.png' % (name, tag))
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)


for name in names:
    reset()
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name, 'source.glb'))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes: print('NO MESH', name); continue
    for o in bpy.context.scene.objects: o.select_set(False)
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    lo, hi = bounds([ob]); d = hi - lo
    print('REPORT %s tris=%d verts=%d dims(x,y,z)=%.3f %.3f %.3f  min=%s max=%s uv=%d mats=%d' % (name, tris, len(ob.data.vertices), d.x, d.y, d.z, tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi), len(ob.data.uv_layers), len(ob.data.materials)))
    # normalise: height (Blender Z) = 1 m, pivot bottom-centre
    s = 1.0 / d.z
    ob.scale = (s, s, s); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lo, hi = bounds([ob])
    ob.location = (-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z); bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    lo, hi = bounds([ob]); d = hi - lo
    print('NORMALISED %s dims=%.3f %.3f %.3f (x wide, y thin, z tall)' % (name, d.x, d.y, d.z))
    # previews (before stripping the material)
    render(name, 'front', (0, -3.2, 0.5), (90, 0, 0), ortho=1.6)
    render(name, 'q3', (2.2, -2.4, 1.2), (72, 0, 42))
    # colour map: downscale the base colour to 2048 px JPG next to the FBX
    img = None
    for n in ob.data.materials[0].node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image: img = n.image; break
    if img is not None:
        img.scale(2048, 2048)
        img.file_format = 'JPEG'
        img.filepath_raw = os.path.join(MODELS, 'wave_%s_paint.jpg' % name)
        scn = bpy.context.scene; scn.view_settings.view_transform = 'Standard'; scn.view_settings.look = 'None'; scn.render.image_settings.file_format = 'JPEG'; scn.render.image_settings.quality = 92
        img.save_render(img.filepath_raw, scene=scn) if hasattr(img, 'save_render') else img.save()
        print('TEXTURE', name, img.size[0], img.size[1])
    # lean FBX: no materials in the file (Unity maps the paint itself), Y up / -Z forward (Blender front -Y = Unity +Z)
    ob.data.materials.clear()
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    ob.name = 'Wave_' + name
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODELS, 'wave_%s.fbx' % name), use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                            axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'}, use_mesh_modifiers=False, mesh_smooth_type='OFF',
                            path_mode='STRIP', embed_textures=False, add_leaf_bones=False, bake_anim=False)
    print('EXPORTED', name)


