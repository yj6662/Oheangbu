"""#308 stone lantern (석등) concept for Meshy image-to-3D (SPEC-ARCH-TEMPLE-308 §12): a procedural model with the proportions
of a Korean octagonal stone lantern (base stone, lotus pedestal, slender octagonal pillar, lotus support, light chamber with
four windows, octagonal roof stone, bud finial) plus the temple's steam fittings (copper pipe, band, brass valve and gauge).
Step 1 (python):   python Tools/Art/lantern_concept308.py           -> Art/World/Temple308/Meshy/Lantern/concept/lantern.obj
Step 2 (blender):  blender -b --factory-startup --python Tools/Art/lantern_concept308.py -- render
                   -> concept/front.png, side.png, back.png (orthographic, plain background) = the three-view sheet (앞 · 옆 · 뒤)
"""
import math, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Temple308/Meshy/Lantern/concept'


def build():
    import numpy as np
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from steamkit308 import Mesh, tube, dome, band, torus, rivets, UP, X, Z
    m = Mesh('lantern'); S = 'stone'
    def octa(y0, y1, r0, r1, mat=S, cap0=False, cap1=True): tube(m, [0, y0, 0], [0, y1, 0], r0, r1, mat, 8, cap0, cap1)
    # base stones (지대석 · 하대 받침)
    m.box([0, .06, 0], X, UP, Z, (.92, .12, .92), S); m.box([0, .165, 0], X, UP, Z, (.74, .09, .74), S)
    # lower lotus pedestal (복련 하대석): eight petals turned down
    octa(.21, .27, .40, .40); octa(.27, .40, .40, .20)
    for k in range(8):
        th = math.pi / 4 * (k + .5); d = np.array([math.cos(th), 0, math.sin(th)])
        dome(m, d * .27 + UP * .285, d * .55 + UP * .85, .115, S, 10, 4, 1.25)
    # pillar (간주석) with a moulded foot and neck
    octa(.40, .44, .16, .13); octa(.44, 1.02, .115, .115); octa(1.02, 1.06, .13, .16)
    # upper lotus support (앙련 상대석): eight petals turned up
    octa(1.06, 1.20, .19, .37, cap1=False); octa(1.20, 1.25, .38, .38)
    for k in range(8):
        th = math.pi / 4 * (k + .5); d = np.array([math.cos(th), 0, math.sin(th)])
        dome(m, d * .26 + UP * 1.12, d * .6 + UP * .7, .105, S, 10, 4, 1.2)
    # light chamber (화사석): octagonal, four windows on the main faces
    octa(1.25, 1.60, .27, .27)
    for k in range(4):
        th = math.pi / 2 * k + math.pi / 8 * 0; d = np.array([math.cos(th + math.pi / 8), 0, math.sin(th + math.pi / 8)]); t = np.cross(UP, d)
        m.box(d * .255 + UP * 1.425, t, UP, d, (.15, .22, .03), 'dark')
        for sgn in (-1, 1): m.box(d * .262 + UP * 1.425 + t * sgn * .085, t, UP, d, (.02, .25, .03), S)
        m.box(d * .262 + UP * 1.545, t, UP, d, (.19, .02, .03), S); m.box(d * .262 + UP * 1.305, t, UP, d, (.19, .02, .03), S)
    # roof stone (옥개석): thick eave, sloped top, lifted corners
    octa(1.60, 1.66, .30, .56, cap1=False); octa(1.66, 1.71, .58, .58); octa(1.71, 1.90, .56, .13)
    for k in range(8):
        th = math.pi / 4 * k; d = np.array([math.cos(th), 0, math.sin(th)])
        dome(m, d * .555 + UP * 1.70, d * .5 + UP * .9, .05, S, 8, 3, 1.4)
        tube(m, d * .13 + UP * 1.895, d * .57 + UP * 1.715, .018, .028, S, 6)
    # finial (보주): ring, bud
    octa(1.90, 1.94, .15, .15); tube(m, [0, 1.94, 0], [0, 1.98, 0], .10, .07, S, 12); dome(m, [0, 2.02, 0], UP, .075, S, 12, 5, 1.5); dome(m, [0, 2.02, 0], -UP, .075, S, 12, 4, .6)
    # steam fittings: copper pipe up one side with iron brackets, copper band, brass valve and gauge on the pedestal
    px = np.array([.185, 0, 0])
    tube(m, px + UP * .33, px + UP * 1.03, .022, .022, 'copper', 10)
    tube(m, px + UP * 1.03, np.array([.30, 1.16, 0]), .022, .022, 'copper', 10); tube(m, px + UP * .33, np.array([.36, .25, 0]), .022, .022, 'copper', 10)
    tube(m, [.36, .25, 0], [.36, .125, 0], .022, .022, 'copper', 10)
    for y in (.56, .88): m.box([.15, y, 0], X, UP, Z, (.09, .03, .06), 'iron')
    band(m, [0, .72, 0], UP, .115, .05, .012, 'copper', 8); rivets(m, [0, .72, 0], UP, .127, 8, .012, 'brass')
    c = np.array([.30, .30, .12]); tube(m, c, c + np.array([0, 0, .05]), .03, .03, 'brass', 10, True, True); torus(m, c + np.array([0, 0, .07]), Z, .055, .008, 'iron', 16, 6)
    tube(m, c + np.array([0, 0, .05]), c + np.array([0, 0, .07]), .008, .008, 'iron', 6)
    g = np.array([.27, .33, -.14]); tube(m, g, g + np.array([0, .05, 0]), .008, .008, 'brass', 6)
    tube(m, g + np.array([0, .09, -.012]), g + np.array([0, .09, .012]), .045, .045, 'brass', 14, True, True)
    OUT.mkdir(parents=True, exist_ok=True)
    V = np.concatenate(m.V); N = np.concatenate(m.Nn)
    with open(OUT / 'lantern.obj', 'w') as f:
        for v in V: f.write('v %.5f %.5f %.5f\n' % (v[0], v[1], v[2]))
        for n in N: f.write('vn %.4f %.4f %.4f\n' % (n[0], n[1], n[2]))
        for mat, tl in m.T.items():
            f.write('usemtl %s\n' % mat)
            for t in np.concatenate(tl): f.write('f %d//%d %d//%d %d//%d\n' % (t[0] + 1, t[0] + 1, t[1] + 1, t[1] + 1, t[2] + 1, t[2] + 1))
    print('obj vertices', len(V), 'height', float(V[:, 1].max()))


def render():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(OUT / 'lantern.obj'), up_axis='Y', forward_axis='NEGATIVE_Z')
    colors = dict(stone=(.40, .40, .38, 1), dark=(.03, .03, .03, 1), copper=(.62, .33, .18, 1), brass=(.75, .58, .22, 1), iron=(.10, .10, .10, 1))
    for mat in bpy.data.materials:
        key = mat.name.split('.')[0]; mat.use_nodes = True; b = mat.node_tree.nodes.get('Principled BSDF')
        if b and key in colors:
            b.inputs['Base Color'].default_value = colors[key]; b.inputs['Roughness'].default_value = .85 if key in ('stone', 'dark') else .45
            b.inputs['Metallic'].default_value = 0 if key in ('stone', 'dark') else .8
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            for p in o.data.polygons: p.use_smooth = False
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
    scene.view_settings.view_transform = 'Standard'; scene.render.resolution_x = 1024; scene.render.resolution_y = 1024; scene.render.film_transparent = False
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True; world.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1); world.node_tree.nodes['Background'].inputs[1].default_value = .55
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 4.5; scene.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.45; scene.collection.objects.link(cam); scene.camera = cam
    for name, ang in (('front', 0), ('side', 90), ('back', 180)):
        a = math.radians(ang)
        cam.location = (6 * math.sin(a), -6 * math.cos(a), 1.02); cam.rotation_euler = (math.radians(90), 0, a)
        sun.rotation_euler = (math.radians(55), 0, a + math.radians(35))
        scene.render.filepath = str(OUT / (name + '.png')); bpy.ops.render.render(write_still=True)
    a = math.radians(35); el = math.radians(14)
    cam.location = (6 * math.sin(a) * math.cos(el), -6 * math.cos(a) * math.cos(el), 1.02 + 6 * math.sin(el)); cam.rotation_euler = (math.radians(90) - el, 0, a)
    sun.rotation_euler = (math.radians(50), 0, a + math.radians(40)); scene.render.filepath = str(OUT / 'quarter.png'); bpy.ops.render.render(write_still=True)
    print('rendered')


if __name__ == '__main__':
    if 'render' in sys.argv: render()
    else: build()
