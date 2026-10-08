"""#308 steam-temple props assembled from the Meshy part kit (D308-46, SPEC-ARCH-TEMPLE-308 §13).
Meshy parts (Art/World/Temple308/Kit/kit.blend, made by meshy_parts.py) carry the sculpted pieces; plain pieces (granite plinth,
copper column, shafts, governor, valve wheel, sutra case) come from steam_kit.py. Moving pieces stay separate objects.

  blender -b --factory-startup --python Tools/Blender/Temple308/assemble_kit.py -- lantern|sutra|all
  -> Art/World/Temple308/Blender/kit_<prop>_{front,quarter}.png, kit_<prop>_motion_<k>.png, kit_<prop>.blend
"""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import steam_kit as K

ROOT = HERE.parents[2]
KIT = ROOT / 'Art/World/Temple308/Kit/kit.blend'
OUT = ROOT / 'Art/World/Temple308/Blender'


def extent(o):
    xs = [v.co for v in o.data.vertices]
    return Vector((max(c.x for c in xs) - min(c.x for c in xs), max(c.y for c in xs) - min(c.y for c in xs), max(c.z for c in xs) - min(c.z for c in xs)))


def bake(o, rotation=None, scale=None):
    o.rotation_mode = 'XYZ'                                          # glTF imports leave QUATERNION, which ignores rotation_euler
    if rotation is not None: o.rotation_euler = rotation
    if scale is not None: o.scale = scale
    bpy.context.view_layer.update(); o.data.transform(o.matrix_basis); o.matrix_basis.identity(); o.data.update()


def part(name, scale=1.0, loc=(0, 0, 0), axis=None, new_name=None):
    """A copy of a kit part with its transform baked into the mesh. scale = number or (x, y, z). axis = 'Z' lays a wheel part
    flat, 'Y' stands it up (the part's thin direction is its turning axis, wherever the kit file left it)."""
    with bpy.data.libraries.load(str(KIT), link=False) as (src, dst): dst.objects = [name]
    o = dst.objects[0]; bpy.context.scene.collection.objects.link(o); o.name = new_name or name.replace('part_', '').title()
    bake(o)
    if axis:
        e = extent(o); thin = min(range(3), key=lambda i: e[i]); want = 'XYZ'.index(axis)
        if thin != want:
            other = 3 - thin - want                                  # turn about the third axis
            rot = [0, 0, 0]; rot[other] = math.radians(90); bake(o, rotation=tuple(rot))
        lo = Vector((min(v.co.x for v in o.data.vertices), min(v.co.y for v in o.data.vertices), min(v.co.z for v in o.data.vertices))); e = extent(o)
        o.data.transform(__import__('mathutils').Matrix.Translation(-(lo + e / 2)))
    sc = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    bake(o, scale=sc); o.location = loc; return o


def height(o): return max((o.matrix_world @ v.co).z for v in o.data.vertices) - min((o.matrix_world @ v.co).z for v in o.data.vertices)


def lantern(M):
    """Steam lantern: plinth, fire-box, lotus collar, copper column, lotus collar, pressure vessel, canopy with its flue and bud.
    The kit parts are stretched to the concept's proportions (squat fire-box, wide vessel, wide low canopy); G = overall size."""
    z = 0; objs = []
    def fit(name, across, tall, at, **kw):
        """A kit part stretched to an outer size in metres (across = widest horizontal, tall = height)."""
        o = part(name, 1.0, (0, 0, 0), **kw); e = extent(o); k = across / max(e.x, e.y); bake(o, scale=(k, k, tall / e.z)); o.location = (0, 0, at); return o
    plinth = K.part_plinth(M, 1.0, .24); objs.append(plinth); z += .24
    fb = fit('part_firebox', .62, .44, z); objs.append(fb); z += .44
    c1 = fit('part_collar', .40, .12, z - .01, new_name='CollarLow'); objs.append(c1); z += .10
    col = K.part_column(M, .20, .44); col.location.z = z; objs.append(col); z += .44
    c2 = fit('part_collar', .46, .12, z - .01, new_name='CollarHigh'); objs.append(c2); z += .10
    ves = fit('part_vessel', .70, .42, 0); bake(ves, rotation=(0, 0, math.radians(90))); ves.location = (0, 0, z); objs.append(ves); z += .42   # a porthole to the front
    can = fit('part_canopy', 1.08, .58, z - .03); objs.append(can); z += .55
    gov = K.part_governor(M); gov.scale = (.55, .55, .55); gov.location = (0, 0, z - .02); objs.append(gov)
    return objs, {gov: ('spin', 'Z', 2.0)}, z + .3


def sutra(M):
    """Geared sutra wheel: the case rides on the big gear; a pinion and a flywheel on a cross shaft drive it."""
    objs = []; plinth = K.part_plinth(M, 2.7, .5); objs.append(plinth)
    bearing = K.lathe('Bearing', [(.55, 0), (.55, .12), (.34, .16), (.34, .46), (.2, .46), (0, .46)], M['iron'], 32, .01); bearing.location.z = .5; objs.append(bearing)
    gz = 1.12; big = part('part_gear', 1.0, (0, 0, gz), axis='Z', new_name='BigGear'); objs.append(big)
    pr = .27; px = 1.0 + pr - .05
    pin = part('part_gear', pr, (px, 0, gz), axis='Z', new_name='Pinion'); objs.append(pin)
    shaft = K.cyl('PinionShaft', .05, .62, M['iron'], (px, 0, gz - .31), 'Z', 14); objs.append(shaft)
    fz = .5 + .56; fly = part('part_flywheel', .78, (px, -.62, fz), axis='Y'); objs.append(fly)
    cross = K.cyl('CrossShaft', .045, .8, M['iron'], (px, -.3, fz), 'Y', 12); objs.append(cross)
    for sy in (-.95,):
        post = K.box('Bracket', (.16, .12, fz - .5 + .1), M['iron'], (px, sy, .5 + (fz - .5 + .1) / 2), .015); objs.append(post)
    axle = K.cyl('Axle', .07, 2.2, M['iron'], (0, 0, gz + 1.1), 'Z', 20); objs.append(axle)
    r = K.oct_r(1.5); case_parts = [K.lathe('case', [(r * .92, 0), (r, .08), (r, 1.5), (r * 1.03, 1.56), (0, 1.56)], M['copper'], 8, .012)]
    for k in range(8):
        a = 2 * math.pi * k / 8; n = Vector((math.cos(a), math.sin(a), 0)); c = n * (1.5 / 2 + .004) + Vector((0, 0, .8)); t = Vector((-math.sin(a), math.cos(a), 0))
        p = K.box('panel', (.02, .44, 1.08), M['soot'], c, .0); p.rotation_euler = (0, 0, a); case_parts.append(p)
        for zz in (.24, .8, 1.36): bar = K.box('bar', (.035, .5, .05), M['bronze'], n * (1.5 / 2 + .016) + Vector((0, 0, zz)), .006); bar.rotation_euler = (0, 0, a); case_parts.append(bar)
        for dy in (-.15, -.05, .05, .15): v = K.box('v', (.03, .02, 1.08), M['bronze'], c + t * dy + n * .012, .004); v.rotation_euler = (0, 0, a); case_parts.append(v)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 8; h = K.cyl('handle', .028, .5, M['brass'], (math.cos(a) * (r + .22), math.sin(a) * (r + .22), .75), 'X', 12); h.rotation_euler = (0, math.radians(90), a); case_parts.append(h)
    case = K.join('SutraCase', case_parts); case.location.z = gz + .2; objs.append(case)
    can = part('part_canopy', (1.32, 1.32, .9), (0, 0, gz + .2 + 1.5)); objs.append(can)
    ratio = 1.0 / pr; hz = .12
    moving = {big: ('spin', 'Z', hz), case: ('spin', 'Z', hz), can: ('spin', 'Z', hz), pin: ('spin', 'Z', -hz * ratio), shaft: ('spin', 'Z', -hz * ratio), fly: ('spin', 'Y', hz * ratio), cross: ('spin', 'Y', hz * ratio)}
    return objs, moving, gz + .2 + 1.5 + height(can) + .1


def run(which):
    bpy.ops.wm.read_factory_settings(use_empty=True); OUT.mkdir(parents=True, exist_ok=True); M = K.mats()
    objs, moving, top = dict(lantern=lantern, sutra=sutra)[which](M)
    cam = K.setup(top); K.OUT = OUT; K.animate(moving, 48)
    bpy.context.scene.frame_set(1); K.shoot(cam, 'kit_%s_front.png' % which, top, 0); K.shoot(cam, 'kit_%s_quarter.png' % which, top, 32, 14)
    for k, f in enumerate((1, 5, 9, 13)): bpy.context.scene.frame_set(f); K.shoot(cam, 'kit_%s_motion_%d.png' % (which, k), top, 32, 14)
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(OUT / ('kit_%s.blend' % which)))
    print('ASSEMBLED', which, 'objects', len(objs), 'height %.2f' % top, 'moving', len(moving), 'triangles', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs))


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['all']
    for w in (['lantern', 'sutra'] if args[0] == 'all' else [args[0]]): run(w)
