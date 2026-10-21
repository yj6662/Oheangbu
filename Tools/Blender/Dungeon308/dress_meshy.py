"""D308-117 C3b dungeon dress props: the four Meshy models to the dress standard (SPEC-FIELD-FIRST-HOUR-308 §8.7).
Import the GLB, join, scale to the standard size, origin = bottom centre, render a check view, and write the mesh as a Unity
JSON mesh (the SteamKit308 format: Unity (x, y, z) = Blender (x, z, y), winding flipped - no FBX axis or scale traps).
  blender -b --factory-startup --python Tools/Blender/Dungeon308/dress_meshy.py
  -> Art/World/Dungeon308/Dress308Meshy/Kit/<name>.json, <name>.png, kit.json
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / 'Art/World/Dungeon308/Dress308Meshy/Source'
OUT = ROOT / 'Art/World/Dungeon308/Dress308Meshy/Kit'
SPEC = {'dd_kiln_mouth': 1.6, 'dd_charcoal_heap': 1.4, 'dd_bone_pile': 1.3, 'dd_fungus_log': 1.7, 'dd_root_mass': 3.0}      # metres across the widest horizontal extent


def bbox(o):
    m = o.matrix_world; pts = [m @ v.co for v in o.data.vertices]
    return Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))), Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))


def load(name):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=str(SRC / name / 'source.glb'))
    new = [o for o in bpy.data.objects if o not in before]; meshes = [o for o in new if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]; bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    if len(meshes) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name
    for x in new:
        if x.type != 'MESH' and x.name in bpy.data.objects: bpy.data.objects.remove(x)
    # bake the world matrix into the vertices (transform_apply over imports is not trusted)
    o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4); o.data.update()
    lo, hi = bbox(o); k = SPEC[name] / max((hi - lo).x, (hi - lo).y)
    o.data.transform(Matrix.Scale(k, 4)); o.data.update(); lo, hi = bbox(o); c = (lo + hi) / 2
    o.data.transform(Matrix.Translation(Vector((-c.x, -c.y, -lo.z)))); o.data.update(); lo, hi = bbox(o)
    return o, dict(size=[round(v, 3) for v in (hi - lo)], triangles=sum(len(p.vertices) - 2 for p in o.data.polygons))


def unity_json(o, name):
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
    (OUT / (name + '.json')).write_text(json.dumps(dict(name=name, vertices=V, normals=N, uvs=U, submeshes=[dict(material=name, triangles=T)])), encoding='utf-8')
    return len(index)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True); OUT.mkdir(parents=True, exist_ok=True); scene = bpy.context.scene
    engines = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'BLENDER_EEVEE_NEXT'
    scene.view_settings.view_transform = 'Standard'; scene.render.resolution_x = 600; scene.render.resolution_y = 600
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.7, .7, .7, 1)
    for nm, e, rot in (('key', 3.0, (50, 0, 35)), ('fill', 1.2, (60, 0, -120))):
        l = bpy.data.objects.new(nm, bpy.data.lights.new(nm, 'SUN')); l.data.energy = e; l.rotation_euler = tuple(math.radians(v) for v in rot); scene.collection.objects.link(l)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); cam.data.type = 'ORTHO'; scene.collection.objects.link(cam); scene.camera = cam
    info = {}
    for name in SPEC:
        o, d = load(name); info[name] = d
        for x in bpy.data.objects:
            if x.type == 'MESH': x.hide_render = x is not o
        lo, hi = bbox(o); c = (lo + hi) / 2; ext = max(hi - lo)
        cam.data.ortho_scale = ext * 1.3; el = math.radians(16); dist = 10
        cam.location = (c.x, c.y - dist * math.cos(el), c.z + dist * math.sin(el)); cam.rotation_euler = (math.radians(90) - el, 0, 0)      # from Blender -Y = Unity -Z ("front")
        scene.render.filepath = str(OUT / (name + '.png')); bpy.ops.render.render(write_still=True)
        d['unity_vertices'] = unity_json(o, name); print('PART', name, d)
    (OUT / 'kit.json').write_text(json.dumps(info, indent=1), encoding='utf-8')


if __name__ == '__main__':
    main()
