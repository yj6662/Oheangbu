"""#308 spell VFX overhaul (D308-52): the solid parts of the five prototype spells. Blender 5.0, headless.

  blender --background --factory-startup --python Tools/Blender/SpellVFX308/meshes308.py

Thorn (wood), splinter, clod and chunk (earth), blade (metal). Each is about 1 m along its flight axis, which is Blender -Y
(Vfx308Build checks which way the tip ended up after the FBX import and turns the body if needed). Light: a few hundred triangles each, no UVs needed (flat ink shading).
Output: Oheangbu/Assets/_Project/Art/SpellVFX308/Meshes/V308_<name>.fbx and Art/SpellVFX308/Meshes/meshes308.blend.
"""
import math
import random
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

ROOT = Path(__file__).resolve().parents[3]
GAME = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Meshes'
SRC = ROOT / 'Art/SpellVFX308/Meshes'
GAME.mkdir(parents=True, exist_ok=True); SRC.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
FWD = Vector((0, -1, 0))


def spindle(bm, a, b, radius, segments=6, belly=.35, rings=5, wobble=0.0, seed=0):
    """a pointed stroke from a to b, thickest at `belly` of the way"""
    a, b = Vector(a), Vector(b); axis = (b - a); L = axis.length; axis.normalize()
    side = axis.orthogonal().normalized(); up = axis.cross(side)
    r = random.Random(seed)
    loops = []
    for i in range(rings + 1):
        v = i / rings
        rad = radius * ((v / belly) ** .7 if v < belly else ((1 - v) / (1 - belly)) ** .85)
        c = a + axis * (L * v) + (side * r.uniform(-1, 1) + up * r.uniform(-1, 1)) * wobble * math.sin(math.pi * v)
        if rad < 1e-5:
            loops.append([bm.verts.new(c)])
        else:
            loops.append([bm.verts.new(c + (side * math.cos(6.283 * k / segments) + up * math.sin(6.283 * k / segments)) * rad) for k in range(segments)])
    for i in range(rings):
        p, q = loops[i], loops[i + 1]
        for k in range(segments):
            k2 = (k + 1) % segments
            if len(p) == 1 and len(q) > 1:
                bm.faces.new((p[0], q[k], q[k2]))
            elif len(q) == 1 and len(p) > 1:
                bm.faces.new((p[k], q[0], p[k2]))
            elif len(p) > 1:
                bm.faces.new((p[k], q[k], q[k2], p[k2]))


def finish(name, bm, smooth):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # UVs (5th pass): u = the angle round the flight axis, v = the place along it. The ink shader builds its surface grain
    # from them, so the grain stays on the body while it rolls and flies (object space would swim on particle meshes).
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = (math.atan2(c.z, c.x) / 6.2831853 + .5, c.dot(FWD))
    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    for p in mesh.polygons:
        p.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(obj)
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    # FBX, not OBJ: Unity 6 imported the OBJ (v//vn faces) with vertices and no triangles
    bpy.ops.export_scene.fbx(filepath=str(GAME / f'V308_{name}.fbx'), use_selection=True, object_types={'MESH'}, bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
    print('MESH', name, len(mesh.polygons), 'faces')


# thorn: a living spike - a long spindle, tip forward, with two whorls of barbs swept back (wide across the view from behind)
bm = bmesh.new()
spindle(bm, FWD * -.42, FWD * .58, .05, segments=7, belly=.3, rings=7, wobble=.012, seed=1)
for whorl, (at, n, length, twist) in enumerate(((-.05, 3, .34, 0.0), (-.26, 3, .24, 60.0))):
    for k in range(n):
        ang = math.radians(twist + 360 * k / n)
        out = Vector((math.cos(ang), 0, math.sin(ang)))
        root = FWD * at + out * .03
        spindle(bm, root, root + (out * .8 - FWD * .6).normalized() * length, .022, segments=4, belly=.25, rings=3, seed=10 + whorl * 5 + k)
finish('thorn', bm, True)

# splinter: one thin straight sliver with a slight kink
bm = bmesh.new()
spindle(bm, FWD * -.5, FWD * .5, .035, segments=4, belly=.45, rings=4, wobble=.01, seed=3)
finish('splinter', bm, False)


def lump(name, subdiv, amount, seed, squash):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=.5)
    for v in bm.verts:
        n = noise.noise(v.co * 2.3 + Vector((seed, seed * 2, 0))) + .5 * noise.noise(v.co * 5.1 + Vector((0, seed, seed)))
        v.co = Vector((v.co.x * squash[0], v.co.y * squash[1], v.co.z * squash[2])) * (1 + amount * n)
    finish(name, bm, False)


lump('clod', 2, .34, 3.1, (1.0, 1.15, .8))        # the lobbed body: heavy, faceted
lump('chunk', 1, .4, 7.7, (1.0, .8, .65))         # what breaks off it

# blade: a flat four-point star that flies face-on and spins about the flight axis (so the player, behind it, sees the whole
# star turning and from the side it is a thin line), on a short needle that points the way
bm = bmesh.new()
spindle(bm, FWD * -.3, FWD * .75, .05, segments=6, belly=.3, rings=4, seed=5)       # 5th pass: a body with thickness, not a flat star
for k, length in enumerate((.5, .3, .5, .3)):
    ang = math.radians(90 * k + 8)
    out = Vector((math.cos(ang), 0, math.sin(ang))); side = Vector((-out.z, 0, out.x))
    tipv = bm.verts.new(out * length)
    l, r = bm.verts.new(side * .1 + out * .02), bm.verts.new(side * -.1 + out * .02)
    f, bk = bm.verts.new(FWD * .085 + out * .06), bm.verts.new(FWD * -.085 + out * .06)      # a ridged blade: two faces a side
    bm.faces.new((tipv, l, f)); bm.faces.new((tipv, f, r)); bm.faces.new((tipv, r, bk)); bm.faces.new((tipv, bk, l))
finish('blade', bm, False)

# stalk (4th pass): what stands in a wood circle - a fat jointed BRUSH STROKE of bamboo (three segments with a breath between
# the joints, an oblique top), not a blade leaf. Stands along the flight axis like the others; the builder sets it upright.
bm = bmesh.new()
z0 = -.5
for g, (length, rad) in enumerate(((.3, .1), (.33, .094), (.34, .088))):      # 5th pass: a round, thick cane with a CUT top - no point
    a, b = FWD * z0, FWD * (z0 + length - .025)
    side = Vector((1, 0, 0)); up = Vector((0, 0, 1)); seg = 8
    ra = [bm.verts.new(a + (side * math.cos(6.283 * k / seg) + up * math.sin(6.283 * k / seg)) * rad * 1.12) for k in range(seg)]
    rb = [bm.verts.new(b + (side * math.cos(6.283 * k / seg) + up * math.sin(6.283 * k / seg)) * rad * (.92 if g < 2 else .9) + (FWD * .06 * math.cos(6.283 * k / seg) if g == 2 else Vector())) for k in range(seg)]
    ca, cb = bm.verts.new(a - FWD * .012), bm.verts.new(b + FWD * (.012 if g < 2 else .05))
    for k in range(seg):
        k2 = (k + 1) % seg
        bm.faces.new((ra[k], rb[k], rb[k2], ra[k2])); bm.faces.new((ca, ra[k], ra[k2])); bm.faces.new((cb, rb[k2], rb[k]))
    z0 += length
finish('stalk', bm, True)

# drop (5th pass): the heavy heart of a water shot - a smooth, slightly sagging bead
bm = bmesh.new()
bmesh.ops.create_icosphere(bm, subdivisions=3, radius=.5)
for v in bm.verts:
    sag = max(0.0, -v.co.z) * .22
    v.co = Vector((v.co.x * (1 + sag), v.co.y * (1 + sag), v.co.z * 1.08)) * (1 + .05 * noise.noise(v.co * 2.0 + Vector((4.2, 0, 0))))
finish('drop', bm, True)

# tongue (5th pass): one strand of flame for the cone - a curling tapered strand, root at the origin, a unit long
bm = bmesh.new()
spindle(bm, FWD * 0.0, FWD * 1.0, .085, segments=6, belly=.28, rings=9, wobble=.075, seed=11)
finish('tongue', bm, True)

bpy.ops.wm.save_as_mainfile(filepath=str(SRC / 'meshes308.blend'))
