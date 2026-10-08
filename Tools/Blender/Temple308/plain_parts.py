"""#308 steam-temple kit: the plain pieces made in Blender (D308-46, SPEC-ARCH-TEMPLE-308 §13c) - pipe run, pipe joint, pipe bracket,
flue tube, granite footing and base, connecting rod, striker ram, shaft, valve, rail. Geometry from steam_kit.py's helpers, procedural
materials (wear in the crevices from ambient occlusion), each piece unwrapped and BAKED to its own colour / metal+smoothness / normal
maps, exported in Unity axes like the Meshy parts (Art/World/Temple308/Kit/Meshes/plain_<name>.json, Textures/plain_<name>_*.png).

  blender -b --factory-startup --python Tools/Blender/Temple308/plain_parts.py [-- name ...]
"""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import steam_kit as K
import meshy_parts as MP

OUT = HERE.parents[2] / 'Art/World/Temple308/Kit'
MP.OUT = OUT
SOCK = {}          # material name -> dict(color, metal, rough, emit, bsdf, out)


def pm(name, base, patina, amount, metal, rough, scale=7.0, bump=.25, crevice=.75, speckle=None):
    """Procedural metal / stone: noise patina, more of it in the crevices (ambient occlusion); sockets kept for baking."""
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; N = nt.nodes; L = nt.links; b = N['Principled BSDF']; out = N['Material Output']
    coord = N.new('ShaderNodeTexCoord')
    noise = N.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = scale; noise.inputs['Detail'].default_value = 9; L.new(coord.outputs['Object'], noise.inputs['Vector'])
    ramp = N.new('ShaderNodeValToRGB'); L.new(noise.outputs['Fac'], ramp.inputs['Fac'])
    lo = 1 - amount; ramp.color_ramp.elements[0].position = max(0, lo - .1); ramp.color_ramp.elements[0].color = (0, 0, 0, 1); ramp.color_ramp.elements[1].position = min(1, lo + .1); ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    ao = N.new('ShaderNodeAmbientOcclusion'); ao.inputs['Distance'].default_value = .09; ao.samples = 8
    inv = N.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0; L.new(ao.outputs['AO'], inv.inputs[1])
    cre = N.new('ShaderNodeMath'); cre.operation = 'MULTIPLY'; cre.inputs[1].default_value = crevice * 1.6; L.new(inv.outputs[0], cre.inputs[0])
    mask = N.new('ShaderNodeMath'); mask.operation = 'MAXIMUM'; mask.use_clamp = True; L.new(ramp.outputs['Color'], mask.inputs[0]); L.new(cre.outputs[0], mask.inputs[1])
    fine = N.new('ShaderNodeTexNoise'); fine.inputs['Scale'].default_value = scale * 11; fine.inputs['Detail'].default_value = 4; L.new(coord.outputs['Object'], fine.inputs['Vector'])
    tone = N.new('ShaderNodeMixRGB'); tone.blend_type = 'MULTIPLY'; tone.inputs['Fac'].default_value = .55; tone.inputs['Color1'].default_value = (*base, 1); L.new(fine.outputs['Color'] if speckle else fine.outputs['Fac'], tone.inputs['Color2'])
    col = N.new('ShaderNodeMixRGB'); col.inputs['Color2'].default_value = (*patina, 1); L.new(mask.outputs[0], col.inputs['Fac']); L.new(tone.outputs['Color'], col.inputs['Color1'])
    met = N.new('ShaderNodeMath'); met.operation = 'MULTIPLY_ADD'; met.inputs[1].default_value = -metal * .85; met.inputs[2].default_value = metal; L.new(mask.outputs[0], met.inputs[0])
    rou = N.new('ShaderNodeMath'); rou.operation = 'MULTIPLY_ADD'; rou.inputs[1].default_value = .9 - rough; rou.inputs[2].default_value = rough; L.new(mask.outputs[0], rou.inputs[0])
    bn = N.new('ShaderNodeBump'); bn.inputs['Strength'].default_value = bump; bn.inputs['Distance'].default_value = .02; L.new(fine.outputs['Fac'], bn.inputs['Height'])
    L.new(col.outputs['Color'], b.inputs['Base Color']); L.new(met.outputs[0], b.inputs['Metallic']); L.new(rou.outputs[0], b.inputs['Roughness']); L.new(bn.outputs['Normal'], b.inputs['Normal'])
    emit = N.new('ShaderNodeEmission'); img = N.new('ShaderNodeTexImage'); img.name = 'BAKE'
    SOCK[name] = dict(color=col.outputs['Color'], metal=met.outputs[0], rough=rou.outputs[0], emit=emit, bsdf=b, out=out, img=img, tree=nt)
    return m


def mats():
    return dict(
        copper=pm('p_copper', (.30, .15, .09), (.13, .26, .22), .22, 1, .45, 5, .2, .45),
        iron=pm('p_iron', (.05, .05, .055), (.20, .10, .05), .14, .85, .55, 8, .5, .35),
        bronze=pm('p_bronze', (.17, .12, .06), (.11, .21, .17), .22, 1, .48, 6, .25, .45),
        brass=pm('p_brass', (.30, .22, .09), (.13, .10, .05), .25, 1, .42, 8, .15, .4),
        granite=pm('p_granite', (.29, .29, .28), (.17, .18, .17), .3, 0, .88, 14, .7, .35, speckle=True),
        wood=pm('p_wood', (.17, .10, .06), (.06, .04, .03), .35, 0, .75, 3, .5, .5),
    )


# ------------------------------------------------------------------------------------------------ pieces (metres, Z up)
def pipe(M):
    """2 m of steam pipe along +Z from the origin, a bolted flange coupling in the middle."""
    p = [K.cyl('pipe', .045, 2.0, M['copper'], (0, 0, 1.0), 'Z', 10, .0)]
    for z in (.985, 1.015): p.append(K.cyl('flange', .078, .022, M['brass'], (0, 0, z), 'Z', 10, .0))
    p.append(K.rivets('bolts', M['iron'], K.ring_points(.062, 1.03, 5) + K.ring_points(.062, .97, 5), .011))
    for z in (.02, 1.98): p.append(K.cyl('lip', .052, .04, M['brass'], (0, 0, z), 'Z', 10, .0))
    return K.join('plain_pipe', p)


def joint(M):
    """Cast junction for a pipe bend: a bronze ball with a bolted boss on each of the six sides (no orientation needed)."""
    p = [K.lathe('ball', [(0, -.085), (.05, -.07), (.082, -.03), (.088, 0), (.082, .03), (.05, .07), (0, .085)], M['bronze'], 10)]
    for axis, sign in (('X', 1), ('X', -1), ('Y', 1), ('Y', -1), ('Z', 1), ('Z', -1)):
        loc = [0, 0, 0]; loc['XYZ'.index(axis)] = .08 * sign; p.append(K.cyl('boss', .062, .03, M['brass'], tuple(loc), axis, 8, .0))
    return K.join('plain_joint', p)


def bracket(M):
    """Wall bracket for the double pipe run (pipes along X, .15 m apart, the origin midway between them; the wall is at +Y)."""
    p = [K.box('plate', (.07, .018, .40), M['iron'], (0, .085, 0), .004)]
    for z in (.075, -.075):
        p.append(K.torus('strap', .052, .011, M['iron'], (0, 0, z), 'X', 12, 5)); p.append(K.box('arm', (.03, .05, .024), M['iron'], (0, .062, z), .003))
    p.append(K.rivets('bolts', M['brass'], [(0, .072, .17), (0, .072, -.17), (0, .072, 0)], .012))
    return K.join('plain_bracket', p)


def flue(M):
    """2 m of flue tube (radius .2) along +Z: a riveted lap seam down one side, a rolled joint band at the top."""
    p = [K.lathe('tube', [(.2, 0), (.2, 2.0)], M['copper'], 20)]
    p.append(K.box('seam', (.05, .012, 2.0), M['copper'], (0, -.2, 1.0), .002))
    p.append(K.rivets('seamrivets', M['bronze'], [(dx, -.208, .08 + .13 * i) for i in range(15) for dx in (0,)], .011))
    p.append(K.lathe('band', [(.2, 1.86), (.214, 1.87), (.218, 1.93), (.214, 1.99), (.2, 2.0)], M['bronze'], 20))
    p.append(K.rivets('bandrivets', M['brass'], K.ring_points(.218, 1.93, 10), .012))
    p.append(K.lathe('foot', [(.2, 0), (.21, .01), (.21, .05), (.2, .06)], M['bronze'], 20))
    return K.join('plain_flue', p)


def footing(M):
    """Granite footing 1 x 1 x .25 in two courses (origin bottom centre)."""
    return K.join('plain_footing', [K.box('low', (1, 1, .14), M['granite'], (0, 0, .07), .012), K.box('high', (.84, .84, .11), M['granite'], (0, 0, .195), .012)])


def base(M):
    """Granite base block 1 x 1 x 1 with a projecting cap and plinth course (origin bottom centre)."""
    return K.join('plain_base', [K.box('body', (.94, .94, .84), M['granite'], (0, 0, .5), .01), K.box('plinth', (1, 1, .1), M['granite'], (0, 0, .05), .012), K.box('cap', (1, 1, .1), M['granite'], (0, 0, .95), .012)])


def rod(M):
    """Connecting rod 1 m along +Z between two brass big ends (their pins along Y)."""
    p = [K.box('bar', (.05, .028, .88), M['iron'], (0, 0, .5), .006)]
    for z in (.05, .95): p.append(K.cyl('end', .062, .05, M['brass'], (0, 0, z), 'Y', 18, .004)); p.append(K.cyl('pin', .024, .08, M['iron'], (0, 0, z), 'Y', 12, .002))
    return K.join('plain_rod', p)


def ram(M):
    """Striker ram 1 m along +Z: a wooden log bound with three iron bands, an iron socket at the tail."""
    p = [K.cyl('log', .11, 1.0, M['wood'], (0, 0, .5), 'Z', 20, .012)]
    for z in (.1, .5, .86): p.append(K.lathe('band', [(.11, z - .03), (.118, z - .025), (.118, z + .025), (.11, z + .03)], M['iron'], 20))
    p.append(K.cyl('socket', .075, .06, M['iron'], (0, 0, .0), 'Z', 16, .004))
    return K.join('plain_ram', p)


def shaft(M):
    """1 m of iron shaft (radius .06) along +Z with two brass set collars."""
    p = [K.cyl('shaft', .06, 1.0, M['iron'], (0, 0, .5), 'Z', 18, .0)]
    for z in (.2, .8): p.append(K.cyl('collar', .085, .05, M['brass'], (0, 0, z), 'Z', 18, .004))
    return K.join('plain_shaft', p)


def valve(M):
    """Stop valve on a pipe stub along X (origin on the pipe axis): bronze body, stem, a brass hand wheel cast as an eight-petal lotus."""
    p = [K.cyl('stub', .047, .32, M['copper'], (0, 0, 0), 'X', 16, .0), K.lathe('body', [(0, -.07), (.06, -.055), (.08, 0), (.06, .055), (.035, .075), (.035, .13), (0, .13)], M['bronze'], 16)]
    for x in (-.15, .15): p.append(K.cyl('flange', .075, .02, M['brass'], (x, 0, 0), 'X', 16, .003))
    p.append(K.cyl('stem', .014, .1, M['iron'], (0, 0, .17), 'Z', 10, .0)); p.append(K.torus('wheel', .11, .012, M['brass'], (0, 0, .2), 'Z', 16, 6))
    for k in range(8):
        a = math.pi / 4 * k; s = K.box('spoke', (.1, .022, .01), M['brass'], (.055 * math.cos(a), .055 * math.sin(a), .2), .003); s.rotation_euler = (0, 0, a); p.append(s)
    p.append(K.cyl('hub', .024, .03, M['brass'], (0, 0, .2), 'Z', 12, .003))
    return K.join('plain_valve', p)


def rail(M):
    """2 m of guard rail along +X from the origin: iron posts, brass top rail, iron mid rail."""
    p = [K.cyl('top', .026, 2.0, M['brass'], (1.0, 0, .95), 'X', 14, .0), K.cyl('mid', .016, 2.0, M['iron'], (1.0, 0, .5), 'X', 10, .0)]
    for x in (.03, 1.0, 1.97):
        p.append(K.cyl('post', .022, .95, M['iron'], (x, 0, .475), 'Z', 12, .0)); p.append(K.cyl('foot', .06, .02, M['iron'], (x, 0, .01), 'Z', 14, .003)); p.append(K.lathe('knob', [(0, .93), (.04, .95), (.03, .99), (0, 1.0)], M['brass'], 12))
    return K.join('plain_rail', p)


PIECES = dict(pipe=(pipe, 512), joint=(joint, 256), bracket=(bracket, 256), flue=(flue, 1024), footing=(footing, 512), base=(base, 1024), rod=(rod, 256), ram=(ram, 512), shaft=(shaft, 256), valve=(valve, 512), rail=(rail, 512))


# ------------------------------------------------------------------------------------------------ bake and export
def bake(o, size):
    scene = bpy.context.scene; name = o.name
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    for p in o.data.polygons: p.use_smooth = True
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')
    used = [s.material.name for s in o.material_slots]
    (OUT / 'Textures').mkdir(parents=True, exist_ok=True); paths = {}
    for kind in ('BC', 'M', 'R', 'N'):
        img = bpy.data.images.new('%s_%s' % (name, kind), size, size, alpha=False); img.colorspace_settings.name = 'sRGB' if kind == 'BC' else 'Non-Color'
        for mn in used:
            s = SOCK[mn]; nt = s['tree']; s['img'].image = img; nt.nodes.active = s['img']
            for l in list(s['out'].inputs['Surface'].links): nt.links.remove(l)
            for l in list(s['emit'].inputs['Color'].links): nt.links.remove(l)
            if kind == 'N': nt.links.new(s['bsdf'].outputs['BSDF'], s['out'].inputs['Surface'])
            else:
                nt.links.new(s[{'BC': 'color', 'M': 'metal', 'R': 'rough'}[kind]], s['emit'].inputs['Color']); nt.links.new(s['emit'].outputs['Emission'], s['out'].inputs['Surface'])
        bpy.ops.object.bake(type='NORMAL' if kind == 'N' else 'EMIT', margin=6, use_clear=True)
        paths[kind] = str(OUT / 'Textures' / ('%s_%s.png' % (name, kind if kind in ('BC', 'N') else '_' + kind)))
        img.filepath_raw = paths[kind]; img.file_format = 'PNG'; img.save()
    return paths


def main(names):
    bpy.ops.wm.read_factory_settings(use_empty=True); scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 12; scene.render.bake.use_selected_to_active = False
    M = mats(); done = []
    for n in names:
        fn, size = PIECES[n]; o = fn(M); paths = bake(o, size)
        n_unity = MP.unity_json(o, o.name); done.append((o.name, paths, n_unity, len(o.data.polygons)))
        print('PLAIN', o.name, 'faces', len(o.data.polygons), 'unity vertices', n_unity)
        o.hide_render = True; o.location.x += 50                      # out of the next piece's ambient occlusion
    # metal + smoothness are packed afterwards by Tools/Blender/Temple308/plain_pack.py (Blender's Python has no PIL)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'plain.blend'))


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    main(args or list(PIECES))
