"""#308 steam-temple part kit in Blender (D308-46, SPEC-ARCH-TEMPLE-308 §13): a prototype of the modular parts and of the props
assembled from them, built with bpy before any Meshy credit is spent. Parts are separate objects so the moving ones can turn,
slide or swing. Concepts: Art/World/Temple308/Concepts/*.png (Codex image generation).

  blender -b --factory-startup --python Tools/Blender/Temple308/steam_kit.py -- lantern|bell|sutra|all [frames]
  -> Art/World/Temple308/Blender/<prop>_{front,quarter}.png, <prop>_motion.png (frame strip), <prop>.blend

Mating standard (metres): octagon across flats S 0.6 / M 1.0 / L 1.6; flange 0.20 / 0.35 / 0.60; shaft 0.08 / 0.14.
"""
import bpy, bmesh, math, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/World/Temple308/Blender'
OCT = math.cos(math.pi / 8)          # across-flats = 2 r cos(22.5)


# ------------------------------------------------------------------------------------------------ materials (procedural)
def mat(name, base, rough, metal, patina=None, patina_amount=.0, bump=.15, scale=6.0):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    noise = nt.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = scale; noise.inputs['Detail'].default_value = 8
    coord = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(coord.outputs['Object'], noise.inputs['Vector'])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); nt.links.new(noise.outputs['Fac'], ramp.inputs['Fac'])
    lo = 1 - patina_amount
    ramp.color_ramp.elements[0].position = max(0.0, lo - .12); ramp.color_ramp.elements[0].color = (*base, 1)
    ramp.color_ramp.elements[1].position = min(1.0, lo + .12); ramp.color_ramp.elements[1].color = (*(patina or [c * .55 for c in base]), 1)
    nt.links.new(ramp.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    fine = nt.nodes.new('ShaderNodeTexNoise'); fine.inputs['Scale'].default_value = scale * 9; nt.links.new(coord.outputs['Object'], fine.inputs['Vector'])
    bn = nt.nodes.new('ShaderNodeBump'); bn.inputs['Strength'].default_value = bump; nt.links.new(fine.outputs['Fac'], bn.inputs['Height']); nt.links.new(bn.outputs['Normal'], b.inputs['Normal'])
    return m


def mats():
    return dict(
        granite=mat('granite', (.24, .24, .23), .92, 0, (.15, .16, .14), .5, .6, 9),
        iron=mat('iron', (.045, .045, .05), .55, .9, (.16, .09, .05), .30, .35, 7),
        copper=mat('copper', (.36, .17, .09), .55, 1, (.16, .33, .28), .45, .25, 4),
        bronze=mat('bronze', (.17, .12, .06), .55, 1, (.11, .21, .17), .42, .25, 5),
        brass=mat('brass', (.36, .26, .09), .5, 1, (.18, .13, .05), .4, .15, 8),
        glass=mat('glass', (.015, .017, .02), .08, 0, (.03, .03, .035), .5, .0, 3),
        soot=mat('soot', (.012, .012, .012), .95, 0, (.03, .03, .03), .5, .3, 12),
    )


# ------------------------------------------------------------------------------------------------ mesh helpers
def new_obj(name, bm, material, smooth=True, bevel=0.0):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); me.materials.append(material)
    for p in me.polygons: p.use_smooth = smooth
    if bevel > 0:
        md = o.modifiers.new('bevel', 'BEVEL'); md.width = bevel; md.segments = 2; md.limit_method = 'ANGLE'; md.angle_limit = math.radians(40)
    return o


def lathe(name, profile, material, segs=48, bevel=0.0, smooth=True):
    """profile = [(r, z), ...] bottom to top; segs = 8 gives an octagon (flat faces, rotated so a flat faces +Y)."""
    bm = bmesh.new(); rings = []; off = math.pi / 8 if segs == 8 else 0
    for r, z in profile:
        rings.append([bm.verts.new((r * math.cos(off + 2 * math.pi * k / segs), r * math.sin(off + 2 * math.pi * k / segs), z)) for k in range(segs)])
    for a, b in zip(rings[:-1], rings[1:]):
        for k in range(segs): bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    if profile[0][0] > 1e-5: bm.faces.new(list(reversed(rings[0])))
    if profile[-1][0] > 1e-5: bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm, material, smooth=smooth and segs != 8, bevel=bevel)


def oct_r(across): return across / 2 / OCT


def box(name, size, material, loc=(0, 0, 0), bevel=.01):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    o = new_obj(name, bm, material, smooth=False, bevel=bevel); o.location = loc; return o


def cyl(name, r, h, material, loc=(0, 0, 0), axis='Z', segs=24, bevel=.004):
    o = lathe(name, [(r, -h / 2), (r, h / 2)], material, segs, bevel); o.location = loc
    if axis == 'X': o.rotation_euler = (0, math.radians(90), 0)
    if axis == 'Y': o.rotation_euler = (math.radians(90), 0, 0)
    return o


def torus(name, R, r, material, loc=(0, 0, 0), axis='Z', segs=40, sides=10):
    bm = bmesh.new(); grid = []
    for i in range(segs):
        a = 2 * math.pi * i / segs; ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides; rr = R + r * math.cos(b); ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), r * math.sin(b))))
        grid.append(ring)
    for i in range(segs):
        for j in range(sides): bm.faces.new((grid[i][j], grid[(i + 1) % segs][j], grid[(i + 1) % segs][(j + 1) % sides], grid[i][(j + 1) % sides]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = new_obj(name, bm, material); o.location = loc
    if axis == 'X': o.rotation_euler = (0, math.radians(90), 0)
    if axis == 'Y': o.rotation_euler = (math.radians(90), 0, 0)
    return o


def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    for o in objs:
        bpy.context.view_layer.objects.active = o
        for md in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)      # the joined part keeps its origin at the kit origin, axes unrotated
    bpy.ops.object.join(); objs[0].name = name; return objs[0]


def rivets(name, material, points, size=.012):
    bm = bmesh.new()
    for p in points:
        m = Matrix.Translation(p) @ Matrix.Diagonal((1, 1, 1, 1)); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size, matrix=m)
    return new_obj(name, bm, material)


def ring_points(r, z, n, off=0.0): return [(r * math.cos(off + 2 * math.pi * k / n), r * math.sin(off + 2 * math.pi * k / n), z) for k in range(n)]


def petals(name, material, r, z, n, length, width, up=True, lean=.55, off=0.0):
    """A row of lotus petals around radius r at height z: flattened pointed leaves leaning outward."""
    bm = bmesh.new()
    for k in range(n):
        a = off + 2 * math.pi * k / n
        ret = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=.5)
        vs = ret['verts']
        for v in vs:
            t = (v.co.z + .5)                                  # 0 base .. 1 tip
            taper = math.sin(math.pi * min(1, t * .9 + .1)) ** .6
            v.co = Vector((v.co.x * width * taper, v.co.y * width * .32, (v.co.z + .5) * length))
        tilt = Matrix.Rotation(-(lean if up else math.pi - lean), 4, 'X')    # tips lean outward (+Y before the turn)
        m = Matrix.Rotation(a - math.pi / 2, 4, 'Z') @ Matrix.Translation((0, r, z)) @ tilt
        bmesh.ops.transform(bm, matrix=m, verts=vs)
    return new_obj(name, bm, material)


# ------------------------------------------------------------------------------------------------ kit parts (origin on the bottom mating face)
def part_plinth(M, across=1.5, h=.42):
    r = oct_r(across)
    return lathe('Plinth', [(r, 0), (r, h * .45), (r * .9, h * .5), (r * .86, h * .5), (r * .86, h), (0, h)], M['granite'], 8, bevel=.02)


def part_firebox(M, across=1.0, h=.9):
    r = oct_r(across); parts = [lathe('fb', [(r * 1.03, 0), (r * 1.03, .05), (r, .06), (r, h - .06), (r * 1.03, h - .05), (r * 1.03, h), (0, h)], M['iron'], 8, bevel=.008)]
    pts = []
    for z in (.11, h - .11):
        for k in range(8):
            a0 = math.pi / 8 + 2 * math.pi * k / 8; a1 = a0 + 2 * math.pi / 8
            for f in (.14, .32, .5, .68, .86): pts.append(((r * 1.005) * (math.cos(a0) * (1 - f) + math.cos(a1) * f), (r * 1.005) * (math.sin(a0) * (1 - f) + math.sin(a1) * f), z))
    for k in range(8):                                             # vertical seams at the corners
        a = math.pi / 8 + 2 * math.pi * k / 8
        for z in (.26, .41, .56, .71): pts.append((r * 1.0 * math.cos(a), r * 1.0 * math.sin(a), z))
    parts.append(rivets('fbr', M['brass'], pts, .014))
    front = across / 2                                              # door on the -Y flat (front)
    door = box('door', (.42, .035, .42), M['iron'], (0, -front - .012, .42), .012); parts.append(door)
    parts.append(box('doorframe', (.5, .02, .5), M['iron'], (0, -front - .004, .42), .008))
    for sx in (-.2,):
        for z in (.3, .54): parts.append(box('hinge', (.07, .03, .05), M['iron'], (sx - .03, -front - .03, z), .006))
    parts.append(rivets('dr', M['brass'], [(x, -front - .032, z) for x in (-.17, 0, .17) for z in (.25, .59)] + [(x, -front - .032, .42) for x in (-.17, .17)], .011))
    return join('Firebox', parts)


def part_valve(M, d=.2):
    """Eight-petal lotus hand wheel on a short stem; turns about its own -Y axis (origin at the stem root)."""
    parts = [cyl('stem', .018, .07, M['brass'], (0, -.035, 0), 'Y', 12), torus('rim', d / 2, .014, M['brass'], (0, -.07, 0), 'Y', 28, 8), cyl('hub', .03, .03, M['brass'], (0, -.07, 0), 'Y', 12)]
    for k in range(8):
        a = math.pi / 4 * k; sp = cyl('spoke', .009, d / 2, M['brass'], (math.cos(a) * d / 4, -.07, math.sin(a) * d / 4), 'X', 8)
        sp.rotation_euler = (0, math.radians(90) - a, 0); parts.append(sp)
    return join('Valve', parts)


def part_collar(M, d=.6, col=.35):
    parts = [lathe('fl', [(col / 2 + .02, 0), (d / 2 * .86, 0), (d / 2 * .86, .05), (col / 2 + .02, .05)], M['bronze'], 40, .004)]
    parts.append(petals('pu', M['bronze'], d / 2 * .62, .04, 12, .19, .2, True, .62))
    parts.append(petals('pd', M['bronze'], d / 2 * .62, .01, 12, .16, .2, False, .62, math.pi / 12))
    parts.append(rivets('cr', M['brass'], ring_points(d / 2 * .78, .055, 12, math.pi / 12), .013))
    return join('Collar', parts)


def part_column(M, d=.35, h=.75):
    r = d / 2
    parts = [lathe('col', [(r, 0), (r, h), (0, h)], M['copper'], 40)]
    for z in (.06, h - .06): parts.append(lathe('band', [(r + .012, z - .03), (r + .018, z - .02), (r + .018, z + .02), (r + .012, z + .03)], M['bronze'], 40))
    parts.append(rivets('colr', M['brass'], ring_points(r + .018, .06, 14) + ring_points(r + .018, h - .06, 14, .2), .011))
    return join('Column', parts)


def part_vessel(M, across=1.0, h=.8):
    r = oct_r(across); parts = [lathe('ves', [(r * .9, 0), (r, .06), (r, h - .06), (r * 1.04, h - .04), (r * 1.04, h), (0, h)], M['bronze'], 8, bevel=.01)]
    pts = []
    for k in range(4):                                             # portholes on the four main flats (+-X, +-Y)
        a = math.pi / 2 * k; n = Vector((math.cos(a), math.sin(a), 0)); c = n * (across / 2) + Vector((0, 0, h * .5))
        ring = torus('pr', .21, .035, M['bronze'], c + n * .01, 'Z', 36, 10); ring.rotation_euler = (math.radians(90), 0, a + math.radians(90)); parts.append(ring)
        g = lathe('pg', [(0, .012), (.12, .008), (.2, 0)], M['glass'], 32); g.rotation_euler = (math.radians(90), 0, a + math.radians(90)); g.location = c + n * .012; parts.append(g)
        t = Vector((-math.sin(a), math.cos(a), 0))
        pts += [tuple(c + n * .045 + (t * math.cos(b) + Vector((0, 0, 1)) * math.sin(b)) * .21) for b in [2 * math.pi * i / 12 for i in range(12)]]
    for z in (.1, h - .1):
        for k in range(8):
            a0 = math.pi / 8 + 2 * math.pi * k / 8; a1 = a0 + math.pi / 4
            for f in (.12, .31, .5, .69, .88): pts.append((r * 1.005 * (math.cos(a0) * (1 - f) + math.cos(a1) * f), r * 1.005 * (math.sin(a0) * (1 - f) + math.sin(a1) * f), z))
    parts.append(rivets('vr', M['brass'], pts, .013))
    return join('Vessel', parts)


def part_canopy(M, across=1.6, h=.55):
    """Octagonal copper roof: concave slope, upturned corners, ridge ribs, eave band; the flue seat is at its top (z = h)."""
    R = oct_r(across); bm = bmesh.new(); rings = []; steps = 8
    for i in range(steps + 1):
        t = i / steps; r = R * (1 - t) + .16 * t; z = h * (t ** 1.7)                # concave: flat at the eave, steep near the top
        ring = []
        for k in range(16):                                        # 8 corners + 8 mid-flats
            a = math.pi / 8 + 2 * math.pi * k / 16; corner = k % 2 == 0
            rr = r if corner else r * OCT
            lift = (1 - t) ** 3 * (.13 if corner else 0.0)          # corners curl up toward the eave
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z + lift)))
        rings.append(ring)
    for a, b in zip(rings[:-1], rings[1:]):
        for k in range(16): bm.faces.new((a[k], a[(k + 1) % 16], b[(k + 1) % 16], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    roof = new_obj('roof', bm, M['copper'], smooth=False, bevel=.004); parts = [roof]
    md = roof.modifiers.new('solid', 'SOLIDIFY'); md.thickness = .03; md.offset = -1
    for k in range(8):                                             # ridge ribs along the corners, ending in a small curl
        a = math.pi / 8 + 2 * math.pi * k / 8; d = Vector((math.cos(a), math.sin(a), 0))
        pts = [d * (R * (1 - t) + .16 * t) + Vector((0, 0, h * t ** 1.7 + (1 - t) ** 3 * .13 + .012)) for t in [i / 6 for i in range(7)]]
        for p0, p1 in zip(pts[:-1], pts[1:]):
            seg = cyl('rib', .022, (p1 - p0).length, M['bronze'], (p0 + p1) / 2, 'Z', 8, 0); seg.rotation_euler = (p1 - p0).to_track_quat('Z', 'Y').to_euler(); parts.append(seg)
        curl = torus('curl', .05, .018, M['bronze'], pts[0] + d * .03 + Vector((0, 0, .05)), 'Z', 14, 6); curl.rotation_euler = (math.radians(90), 0, a); parts.append(curl)                      # the curl lies in the corner's own vertical plane
    parts.append(lathe('eave', [(R * .96, -.05), (R * 1.0, -.05), (R * 1.0, 0), (R * .96, 0)], M['bronze'], 8))
    parts.append(rivets('er', M['brass'], [(R * OCT * 1.0 * math.cos(a), R * OCT * 1.0 * math.sin(a), -.025) for a in [2 * math.pi * i / 40 for i in range(40)]], .011))
    return join('Canopy', parts)


def part_fluecap(M):
    parts = [lathe('neck', [(.16, 0), (.16, .16), (.19, .17), (.19, .21), (.13, .23)], M['copper'], 32)]
    parts.append(lathe('bud', [(.0, .21), (.11, .23), (.155, .31), (.13, .41), (.06, .50), (0, .55)], M['bronze'], 32))
    parts.append(petals('bp', M['bronze'], .10, .24, 8, .22, .15, True, .16))
    parts.append(rivets('nr', M['brass'], ring_points(.19, .19, 12), .011))
    return join('FlueCap', parts)


def part_boss(M):
    """Side boss with a lotus rosette (the round fittings on the fire-box sides in the concept)."""
    parts = [cyl('boss', .085, .05, M['bronze'], (0, -.025, 0), 'Y', 20), petals('br', M['brass'], .0, 0, 8, .075, .06, True, math.pi / 2)]
    parts[1].rotation_euler = (math.radians(90), 0, 0); parts[1].location = (0, -.052, 0)
    return join('Boss', parts)


def part_flywheel(M, d=1.4, thick=.15):
    """Upright wheel in the XZ plane, turning about Y (origin at the hub centre)."""
    R = d / 2; parts = [torus('rim', R - .07, .07, M['iron'], (0, 0, 0), 'Y', 56, 10)]
    parts[0].scale = (1, 1, thick / .14)
    for k in range(8):
        a = math.pi / 4 * k; sp = box('sp', (R - .16, .045, .07), M['iron'], (math.cos(a) * (R - .08) / 2, 0, math.sin(a) * (R - .08) / 2), .01); sp.rotation_euler = (0, -a, 0); parts.append(sp)
    parts.append(cyl('hub', .13, thick + .04, M['bronze'], (0, 0, 0), 'Y', 28))
    hubp = petals('hp', M['bronze'], .0, 0, 8, .2, .13, True, math.pi / 2); hubp.rotation_euler = (math.radians(90), 0, 0); hubp.location = (0, -(thick / 2 + .03), 0); parts.append(hubp)
    parts.append(cyl('pin', .03, .12, M['brass'], (R * .55, -(thick / 2 + .05), 0), 'Y', 12))
    return join('Flywheel', parts)


def part_gear(M, d=2.0, thick=.14, teeth=36, name='Gear'):
    """Gear lying flat (XY plane), turning about Z (origin at its centre)."""
    R = d / 2; bm = bmesh.new(); prof = []
    for k in range(teeth):
        a0 = 2 * math.pi * k / teeth; w = 2 * math.pi / teeth
        for da, rr in ((0, R * .93), (.22 * w, R), (.5 * w, R), (.72 * w, R * .93)): prof.append((rr * math.cos(a0 + da), rr * math.sin(a0 + da)))
    top = [bm.verts.new((x, y, thick / 2)) for x, y in prof]; bot = [bm.verts.new((x, y, -thick / 2)) for x, y in prof]; n = len(prof)
    inner_t = [bm.verts.new((R * .78 * math.cos(2 * math.pi * k / n), R * .78 * math.sin(2 * math.pi * k / n), thick / 2)) for k in range(n)]
    inner_b = [bm.verts.new((v.co.x, v.co.y, -thick / 2)) for v in inner_t]
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new((top[k], top[j], bot[j], bot[k])); bm.faces.new((top[k], inner_t[k], inner_t[j], top[j])); bm.faces.new((bot[k], bot[j], inner_b[j], inner_b[k])); bm.faces.new((inner_t[k], inner_b[k], inner_b[j], inner_t[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); parts = [new_obj('gr', bm, M['bronze'], smooth=False, bevel=.006)]
    for k in range(8):
        a = math.pi / 4 * k; sp = box('gs', (R * .62, .09, thick * .7), M['bronze'], (math.cos(a) * R * .47, math.sin(a) * R * .47, 0), .012); sp.rotation_euler = (0, 0, a); parts.append(sp)
    parts.append(cyl('gh', R * .2, thick + .06, M['bronze'], (0, 0, 0), 'Z', 32)); parts.append(petals('gp', M['brass'], R * .2, thick / 2 + .02, 12, R * .13, R * .1, True, 1.25))
    return join(name, parts)


def part_governor(M):
    """Two-ball centrifugal governor on a spindle, spinning about Z (origin at the spindle foot)."""
    parts = [cyl('gsp', .014, .34, M['brass'], (0, 0, .17), 'Z', 10), lathe('gtop', [(0, .34), (.03, .35), (.02, .39), (0, .41)], M['brass'], 16)]
    for sx in (-1, 1):
        arm = cyl('ga', .008, .2, M['iron'], (sx * .07, 0, .25), 'Z', 8); arm.rotation_euler = (0, sx * math.radians(42), 0); parts.append(arm)
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=.045); ball = new_obj('gb', bm, M['brass']); ball.location = (sx * .14, 0, .17); parts.append(ball)
    return join('Governor', parts)


# ------------------------------------------------------------------------------------------------ props
def stack(parts_with_z):
    for o, z in parts_with_z: o.location.z += z


def prop_lantern(M):
    """The steam lantern of concept lantern.png: plinth, fire-box, collar, column, collar, vessel, canopy, flue cap. ~2.6 m."""
    plinth = part_plinth(M, 1.5, .42); fb = part_firebox(M, 1.0, .82); c1 = part_collar(M, .62, .36); col = part_column(M, .36, .62)
    c2 = part_collar(M, .7, .36); ves = part_vessel(M, .95, .62); can = part_canopy(M, 1.42, .46); cap = part_fluecap(M)
    z = 0; plinth.location.z = z; z += .42; fb.location.z = z; z += .82; c1.location.z = z; z += .05; col.location.z = z; z += .62; c2.location.z = z - .02; z += .06
    ves.location.z = z; z += .62; can.location.z = z + .05; z += .55; cap.location.z = z - .02
    valve = part_valve(M, .2); valve.location = (0, -.5 - .03, .42 + .41)
    b1 = part_boss(M); b1.rotation_euler = (0, 0, math.radians(-90)); b1.location = (.5, 0, .42 + .41)
    b2 = part_boss(M); b2.rotation_euler = (0, 0, math.radians(90)); b2.location = (-.5, 0, .42 + .41)
    gov = part_governor(M); gov.scale = (.9, .9, .9); gov.location = (.0, .0, z + .5)     # a small governor on the bud: the lantern's moving piece
    top = z + .9
    moving = {gov: ('spin', 'Z', 2.0), valve: ('spin', 'Y', .25)}
    return [plinth, fb, c1, col, c2, ves, can, cap, valve, b1, b2, gov], moving, top


def prop_sutra(M):
    """Geared sutra wheel (윤장대), simplified from concept sutra_wheel.png: the case turns on a big gear driven by a pinion and a flywheel."""
    plinth = part_plinth(M, 2.6, .5)
    bearing = lathe('Bearing', [(.5, 0), (.5, .12), (.32, .16), (.32, .5), (.2, .5), (0, .5)], M['iron'], 32, .01); bearing.location.z = .5
    big = part_gear(M, 1.9, .14, 40, 'BigGear'); big.location.z = 1.1
    pin = part_gear(M, .5, .14, 10, 'Pinion'); pin.location = (1.9 / 2 + .5 / 2 - .035, 0, 1.1)
    shaft = cyl('PinionShaft', .05, .55, M['iron'], (1.9 / 2 + .5 / 2 - .035, 0, .82), 'Z', 14)
    axle = cyl('Axle', .07, 2.4, M['iron'], (0, 0, 1.0 + 1.2), 'Z', 20)
    case_parts = [lathe('case', [(oct_r(1.5) * .92, 0), (oct_r(1.5), .08), (oct_r(1.5), 1.5), (oct_r(1.5) * 1.03, 1.56), (0, 1.56)], M['copper'], 8, .012)]
    r = oct_r(1.5)
    for k in range(8):                                             # dark lattice panels and bronze frames on the eight faces
        a = 2 * math.pi * k / 8; n = Vector((math.cos(a), math.sin(a), 0)); c = n * (1.5 / 2 + .004) + Vector((0, 0, .8))
        p = box('panel', (.02, .44, 1.08), M['soot'], c, .0); p.rotation_euler = (0, 0, a); case_parts.append(p)
        for zz in (.24, .8, 1.36): bar = box('bar', (.035, .5, .05), M['bronze'], n * (1.5 / 2 + .016) + Vector((0, 0, zz)), .006); bar.rotation_euler = (0, 0, a); case_parts.append(bar)
        for dy in (-.1, .1): v = box('v', (.03, .025, 1.08), M['bronze'], c + Vector((-math.sin(a), math.cos(a), 0)) * dy + n * .012, .004); v.rotation_euler = (0, 0, a); case_parts.append(v)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 8; h = cyl('handle', .028, .5, M['brass'], (math.cos(a) * (r + .22), math.sin(a) * (r + .22), .75), 'X', 12); h.rotation_euler = (0, math.radians(90), a); case_parts.append(h)
    case = join('SutraCase', case_parts); case.location.z = 1.2
    can = part_canopy(M, 2.1, .55); can.location.z = 1.2 + 1.6; cap = part_fluecap(M); cap.location.z = 1.2 + 1.6 + .53
    fly = part_flywheel(M, 1.0, .12); fly.location = (1.9 / 2 + .5 / 2 - .035, -.55, .82)
    cross = cyl('CrossShaft', .04, .7, M['iron'], (1.9 / 2 + .5 / 2 - .035, -.25, .82), 'Y', 12)
    ratio = 40 / 10
    moving = {big: ('spin', 'Z', .12), case: ('spin', 'Z', .12), can: ('spin', 'Z', .12), cap: ('spin', 'Z', .12), pin: ('spin', 'Z', -.12 * ratio), shaft: ('spin', 'Z', -.12 * ratio), fly: ('spin', 'Y', .12 * ratio), cross: ('spin', 'Y', .12 * ratio)}
    return [plinth, bearing, big, pin, shaft, axle, case, can, cap, fly, cross], moving, 4.2


# ------------------------------------------------------------------------------------------------ scene, motion, render
def animate(moving, frames):
    scene = bpy.context.scene; scene.frame_start = 1; scene.frame_end = frames; fps = 24
    for o, (kind, axis, hz) in moving.items():
        i = 'XYZ'.index(axis)
        if kind == 'spin':
            for f in (1, frames + 1):
                e = list(o.rotation_euler); e[i] += 2 * math.pi * hz * (f - 1) / fps
                o.rotation_euler = e; o.keyframe_insert('rotation_euler', frame=f)
                e[i] -= 2 * math.pi * hz * (f - 1) / fps; o.rotation_euler = e
            if o.animation_data and o.animation_data.action:
                try:
                    for fc in o.animation_data.action.fcurves:
                        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
                except Exception: pass


def setup(height):
    scene = bpy.context.scene
    engines = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'BLENDER_EEVEE_NEXT'
    scene.view_settings.view_transform = 'Standard'; scene.render.resolution_x = 900; scene.render.resolution_y = 1200
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.72, .72, .72, 1); world.node_tree.nodes['Background'].inputs[1].default_value = .55
    for name, energy, rot in (('key', 2.6, (50, 0, 35)), ('fill', .9, (60, 0, -120)), ('rim', 1.3, (70, 0, 160))):
        l = bpy.data.objects.new(name, bpy.data.lights.new(name, 'SUN')); l.data.energy = energy; l.rotation_euler = tuple(math.radians(v) for v in rot); scene.collection.objects.link(l)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); cam.data.type = 'ORTHO'; cam.data.ortho_scale = height * 1.12; scene.collection.objects.link(cam); scene.camera = cam
    return cam


def shoot(cam, name, height, yaw, elev=0.0):
    a = math.radians(yaw); el = math.radians(elev); d = 12
    cam.location = (d * math.sin(a) * math.cos(el), -d * math.cos(a) * math.cos(el), height / 2 + d * math.sin(el)); cam.rotation_euler = (math.radians(90) - el, 0, a)
    bpy.context.scene.render.filepath = str(OUT / name); bpy.ops.render.render(write_still=True)


def run(which, frames):
    bpy.ops.wm.read_factory_settings(use_empty=True); OUT.mkdir(parents=True, exist_ok=True); M = mats()
    objs, moving, height = dict(lantern=prop_lantern, sutra=prop_sutra)[which](M)
    cam = setup(height); animate(moving, frames)
    bpy.context.scene.frame_set(1); shoot(cam, which + '_front.png', height, 0); shoot(cam, which + '_quarter.png', height, 32, 14)
    for k, f in enumerate((1, 5, 9, 13)):                         # four frames of the motion, same view
        bpy.context.scene.frame_set(f); shoot(cam, '%s_motion_%d.png' % (which, k), height, 32, 14)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / (which + '.blend')))
    tris = sum(len(o.data.polygons) for o in objs); print('BUILT', which, 'objects', len(objs), 'faces', tris, 'moving', len(moving))


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['lantern']
    frames = int(args[1]) if len(args) > 1 else 48
    for w in (['lantern', 'sutra'] if args[0] == 'all' else [args[0]]): run(w, frames)
