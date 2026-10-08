"""#308 steam-temple kit: the Meshy parts brought to the kit's mating standard and looked at (D308-46, SPEC-ARCH-TEMPLE-308 §13).
For each part: import the GLB, join, scale to the standard size, put the origin on the mating point (bottom centre; wheel
parts: the hub centre, turning axis = Y), save <part>.blend / .fbx, and render it. Then one sheet of all parts.

  blender -b --factory-startup --python Tools/Blender/Temple308/meshy_parts.py
  -> Art/World/Temple308/Kit/<part>.fbx, <part>.png, kit.json (sizes), kit.blend
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / 'Art/World/Temple308/Meshy/Parts'
OUT = ROOT / 'Art/World/Temple308/Kit'
# target: ('width', metres across the widest horizontal extent) or ('height', metres); kind: 'stack' (origin bottom centre) | 'wheel' (origin centre, axis Y)
SPEC = {
    'part_firebox': ('stack', 'width', 1.08),      # 1.0 m across flats = 1.08 across corners
    'part_vessel': ('stack', 'width', 1.08),
    'part_canopy': ('stack', 'width', 1.70),
    'part_collar': ('stack', 'width', 0.62),
    'part_flywheel': ('wheel', 'width', 1.40),
    'part_gear': ('wheel', 'width', 2.00),
    'part_cylinder': ('stack', 'width', 1.30),
    'part_bell': ('stack', 'height', 1.90),
    'part_case': ('stack', 'width', 1.50),
    'part_stand': ('stack', 'height', 1.40),
}


def bbox(o):
    """World bounds from the vertices themselves (Object.bound_box is stale right after a transform is applied)."""
    m = o.matrix_world; pts = [m @ v.co for v in o.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def load(part):
    glb = SRC / part / 'image' / 'model_urls_glb.glb'
    if not glb.exists(): return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(glb))
    new = [o for o in bpy.data.objects if o not in before]; meshes = [o for o in new if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    if len(meshes) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = part
    for x in new:
        if x.type != 'MESH' and x.name in bpy.data.objects: bpy.data.objects.remove(x)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    kind, basis, target = SPEC[part]
    lo, hi = bbox(o); size = hi - lo
    if kind == 'wheel':
        # the thin axis of a wheel is its turning axis: bring it to Y
        thin = min(range(3), key=lambda i: size[i])
        if thin == 0: o.rotation_euler = (0, 0, math.radians(90))
        elif thin == 2: o.rotation_euler = (math.radians(90), 0, 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False); lo, hi = bbox(o); size = hi - lo
        k = target / max(size.x, size.z)
    else:
        k = target / (max(size.x, size.y) if basis == 'width' else size.z)
    o.scale = (k, k, k); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); lo, hi = bbox(o)
    c = (lo + hi) / 2
    shift = Vector((-c.x, -c.y, -c.z if kind == 'wheel' else -lo.z))
    o.data.transform(Matrix.Translation(shift)); o.data.update()
    lo, hi = bbox(o)
    return o, dict(kind=kind, size=[round(v, 3) for v in (hi - lo)], min=[round(v, 3) for v in lo], max=[round(v, 3) for v in hi], vertices=len(o.data.vertices), triangles=sum(len(p.vertices) - 2 for p in o.data.polygons))


def unity_json(o, part):
    """The part as a Unity mesh (JSON, the SteamKit308 format): Unity (x, y, z) = Blender (x, z, y), winding flipped. Front (-Y) becomes -Z."""
    me = o.data; me.calc_loop_triangles(); uv = me.uv_layers.active.data if me.uv_layers.active else None
    index = {}; V = []; N = []; U = []; T = []
    for t in me.loop_triangles:
        tri = []
        for li in t.loops:
            l = me.loops[li]; n = l.normal; u = uv[li].uv if uv else (0, 0)
            key = (l.vertex_index, round(u[0], 5), round(u[1], 5), round(n.x, 2), round(n.y, 2), round(n.z, 2))
            k = index.get(key)
            if k is None:
                k = index[key] = len(index); c = me.vertices[l.vertex_index].co
                V += [round(c.x, 5), round(c.z, 5), round(c.y, 5)]; N += [round(n.x, 4), round(n.z, 4), round(n.y, 4)]; U += [round(u[0], 5), round(u[1], 5)]
            tri.append(k)
        T += [tri[0], tri[2], tri[1]]
    (OUT / 'Meshes').mkdir(exist_ok=True)
    (OUT / 'Meshes' / (part + '.json')).write_text(json.dumps(dict(name=part, vertices=V, normals=N, uvs=U, submeshes=[dict(material=part, triangles=T)])), encoding='utf-8')
    return len(index)


def setup():
    scene = bpy.context.scene
    engines = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'BLENDER_EEVEE_NEXT'
    scene.view_settings.view_transform = 'Standard'; scene.render.resolution_x = 800; scene.render.resolution_y = 800
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.7, .7, .7, 1); world.node_tree.nodes['Background'].inputs[1].default_value = .9
    for name, energy, rot in (('key', 3.0, (50, 0, 35)), ('fill', 1.2, (60, 0, -120)), ('rim', 1.5, (70, 0, 160))):
        l = bpy.data.objects.new(name, bpy.data.lights.new(name, 'SUN')); l.data.energy = energy; l.rotation_euler = tuple(math.radians(v) for v in rot); scene.collection.objects.link(l)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); cam.data.type = 'ORTHO'; scene.collection.objects.link(cam); scene.camera = cam
    return cam


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True); OUT.mkdir(parents=True, exist_ok=True); cam = setup(); info = {}; x = 0.0
    for part in SPEC:
        r = load(part)
        if r is None: continue
        o, d = r; info[part] = d
        lo, hi = bbox(o); c = (lo + hi) / 2; ext = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
        for others in bpy.data.objects:
            if others.type == 'MESH': others.hide_render = others is not o
        cam.data.ortho_scale = ext * 1.25; a = math.radians(30); el = math.radians(16); dist = 10
        cam.location = (c.x + dist * math.sin(a) * math.cos(el), c.y - dist * math.cos(a) * math.cos(el), c.z + dist * math.sin(el)); cam.rotation_euler = (math.radians(90) - el, 0, a)
        bpy.context.scene.render.filepath = str(OUT / (part + '.png')); bpy.ops.render.render(write_still=True)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=str(OUT / (part + '.fbx')), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
        d['unity_vertices'] = unity_json(o, part)
        print('PART', part, d)
    for o in bpy.data.objects:
        if o.type == 'MESH': o.hide_render = False
    (OUT / 'kit.json').write_text(json.dumps(info, indent=1), encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'kit.blend'))


if __name__ == '__main__':
    main()
