"""#308 spell VFX overhaul (D308-52): one baked fluid simulation -> one flipbook sprite sheet. Blender 5.0, headless.

  blender --background --factory-startup --python Tools/Blender/SpellVFX308/flipbook308.py -- <preset> [--res N] [--frames N] [--tile N]

A gas simulation (Mantaflow) is baked in a small domain and rendered with Cycles from the side, orthographic, on a transparent film.
The sheet keeps two things the game shader reads: alpha = how much ink there is, red = how hot it is (fire only; zero elsewhere).
Colour is not baked: the element's ink colour is given in the game, so one sheet serves several letters.
Output: Art/SpellVFX308/Flipbooks/<preset>/f###.png and Art/SpellVFX308/Flipbooks/<preset>.png (tile x tile frames, row-major, top row first).
"""
import math
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[3]
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PRESET = argv[0] if argv else 'ink_burst'


def opt(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default


RES, FRAMES, TILE, CELL = opt('--res', 96), opt('--frames', 64), opt('--tile', 8), opt('--cell', 256)

# emitter: shape, size, initial velocity (m/s), how long it emits (frames); gas: buoyancy, vorticity, dissolve; view size (m)
PRESETS = {
    # a wet round burst that hangs and thins: the common hit
    'ink_burst': dict(shape='sphere', size=.16, velocity=(0, 0, 0), spread=9.0, jets=11, emit=3, density=3.0, heat=0.0, alpha=-.02, beta=0.0, vorticity=.9, dissolve=30, noise=2.0, view=3.2, gravity=-1.5, time=3.0),
    # fire: rises, licks, breaks - red channel carries the heat
    'fire_flame': dict(shape='sphere', size=.2, velocity=(0, 0, 1.2), spread=.6, emit=64, density=1.0, heat=1.6, alpha=.6, beta=1.2, vorticity=.9, dissolve=14, noise=1.6, view=2.8, gravity=0.0, fire=True, loop=True),
    'fire_burst': dict(shape='sphere', size=.24, velocity=(0, 0, .6), spread=4.0, emit=5, density=1.4, heat=2.0, alpha=.4, beta=1.0, vorticity=.8, dissolve=22, noise=1.5, view=3.4, gravity=0.0, fire=True),
    # water: a flat ring thrown outward that falls back
    'water_splash': dict(shape='torus', size=.3, velocity=(0, 0, 2.2), spread=3.6, emit=3, density=2.0, heat=0.0, alpha=-.35, beta=0.0, vorticity=.25, dissolve=26, noise=.6, view=3.4, gravity=-6.0),
    # earth: heavy dust that rolls low and settles
    'dust_burst': dict(shape='sphere', size=.3, velocity=(0, 0, .4), spread=2.4, emit=6, density=2.2, heat=0.0, alpha=-.18, beta=0.0, vorticity=.35, dissolve=46, noise=.9, view=3.6, gravity=-2.5),
    # metal: thin fast streaks that are gone at once
    'spark_burst': dict(shape='sphere', size=.1, velocity=(0, 0, 0), spread=7.5, emit=2, density=1.0, heat=0.0, alpha=0.0, beta=0.0, vorticity=.1, dissolve=9, noise=.3, view=3.4, gravity=-.5),
    # wood: splinters and sap thrown along one axis
    'splinter_burst': dict(shape='sphere', size=.16, velocity=(0, 0, 1.6), spread=2.6, emit=3, density=1.3, heat=0.0, alpha=-.05, beta=0.0, vorticity=.7, dissolve=20, noise=1.0, view=3.0, gravity=-1.8),
}
P = PRESETS[PRESET]
OUT = ROOT / 'Art/SpellVFX308/Flipbooks' / PRESET
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 1, FRAMES
scene.render.fps = 30

# ---- domain
view = P['view']
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, view * .5 - .25))
domain = bpy.context.object; domain.name = 'Domain'; domain.scale = (view, view, view)
bpy.ops.object.transform_apply(scale=True)
fluid = domain.modifiers.new('Fluid', 'FLUID'); fluid.fluid_type = 'DOMAIN'
ds = fluid.domain_settings
ds.domain_type = 'GAS'; ds.resolution_max = RES
ds.alpha = P['alpha']; ds.beta = P['beta']; ds.vorticity = P['vorticity']
ds.use_dissolve_smoke = True; ds.dissolve_speed = P['dissolve']; ds.use_dissolve_smoke_log = True
ds.use_noise = True; ds.noise_scale = 2; ds.noise_strength = P['noise']
ds.gravity = (0, 0, P['gravity'] * 9.81 / 9.81) if P['gravity'] else (0, 0, 0)
ds.use_adaptive_domain = False
ds.time_scale = P.get('time', 2.5)      # a burst is over in half a second: the default pace of smoke is far too slow for it
ds.cache_type = 'ALL'; ds.cache_directory = str(OUT / 'cache'); ds.cache_frame_start = 1; ds.cache_frame_end = FRAMES
for side in ('use_collision_border_front', 'use_collision_border_back', 'use_collision_border_left', 'use_collision_border_right', 'use_collision_border_top', 'use_collision_border_bottom'):
    setattr(ds, side, False)
if P.get('fire'):
    ds.burning_rate = .9; ds.flame_smoke = 1.0; ds.flame_vorticity = .6; ds.flame_max_temp = 3.0; ds.flame_ignition = 1.5

# ---- emitters. A burst is not one puff: it is thrown out in tongues. Besides the core there are `jets` small sources set round it
# in the picture plane, each flinging its ink along its own direction at its own speed; vorticity curls the tongues as they slow.
import random
random.seed(hash(PRESET) % 9973)
loc = (0, 0, P.get('origin', view * .5 - .25))


def flow(obj, velocity, normal, density):
    fm = obj.modifiers.new('Fluid', 'FLUID'); fm.fluid_type = 'FLOW'
    fs = fm.flow_settings
    fs.flow_type = 'BOTH' if P.get('fire') else 'SMOKE'; fs.flow_behavior = 'INFLOW'
    fs.density = density; fs.temperature = P['heat']; fs.fuel_amount = 1.2 if P.get('fire') else 0.0
    fs.use_initial_velocity = True; fs.velocity_normal = normal; fs.velocity_coord = velocity; fs.velocity_random = .5
    fs.surface_distance = 1.0; fs.volume_density = 1.0
    fs.use_inflow = True; fs.keyframe_insert('use_inflow', frame=1)
    if not P.get('loop'):
        fs.use_inflow = False; fs.keyframe_insert('use_inflow', frame=1 + P['emit'])
    obj.hide_render = True


if P['shape'] == 'torus':
    bpy.ops.mesh.primitive_torus_add(major_radius=P['size'], minor_radius=P['size'] * .3, location=loc)
else:
    bpy.ops.mesh.primitive_ico_sphere_add(radius=P['size'], subdivisions=3, location=loc)
emitter = bpy.context.object; emitter.name = 'Emitter'
flow(emitter, P['velocity'], P['spread'] * .35, P['density'])
jets = P.get('jets', 0); lo, hi = P.get('arc', (0, 360))
for j in range(jets):
    a = math.radians(lo + (hi - lo) * (j + random.uniform(-.3, .3)) / max(1, jets)); speed = P['spread'] * random.uniform(.55, 1.25)
    tilt = random.uniform(-.35, .35)                                       # a little toward or away from the camera, so tongues overlap in depth
    d = (math.cos(a) * math.cos(tilt), math.sin(tilt), math.sin(a) * math.cos(tilt))
    bpy.ops.mesh.primitive_ico_sphere_add(radius=P['size'] * random.uniform(.22, .4), subdivisions=2, location=(loc[0] + d[0] * P['size'], loc[1] + d[1] * P['size'], loc[2] + d[2] * P['size']))
    flow(bpy.context.object, (d[0] * speed + P['velocity'][0], d[1] * speed + P['velocity'][1], d[2] * speed + P['velocity'][2]), 0.0, P['density'] * random.uniform(.8, 1.4))

# ---- look: density -> alpha, heat -> red. White scattering under one flat light; the game gives the colour.
mat = bpy.data.materials.new('Ink'); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear()
out = nt.nodes.new('ShaderNodeOutputMaterial'); vol = nt.nodes.new('ShaderNodeVolumePrincipled'); info = nt.nodes.new('ShaderNodeVolumeInfo')
vol.inputs['Color'].default_value = (.92, .92, .92, 1); vol.inputs['Density'].default_value = 14.0
if P.get('fire'):
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position = .05; ramp.color_ramp.elements[1].position = .6
    ramp.color_ramp.elements[1].color = (1, 0, 0, 1)
    nt.links.new(info.outputs['Flame'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], vol.inputs['Emission Color'])
    vol.inputs['Emission Strength'].default_value = 4.0
    vol.inputs['Color'].default_value = (.0, .9, .9, 1)          # smoke carries no red: red is heat alone
nt.links.new(vol.outputs['Volume'], out.inputs['Volume'])
domain.data.materials.append(mat)

# ---- camera, light, film
cam_data = bpy.data.cameras.new('Cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = view
cam = bpy.data.objects.new('Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (0, -view * 3, view * .5 - .25); cam.rotation_euler = (math.radians(90), 0, 0)
sun = bpy.data.lights.new('Sun', 'SUN'); sun.energy = 3.0; so = bpy.data.objects.new('Sun', sun); scene.collection.objects.link(so)
so.rotation_euler = (math.radians(50), 0, math.radians(30))
world = bpy.data.worlds.new('W'); scene.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (1, 1, 1, 1); world.node_tree.nodes['Background'].inputs['Strength'].default_value = .6
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24; scene.cycles.use_denoising = False; scene.cycles.volume_bounces = 0; scene.cycles.max_bounces = 2
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'OPTIX'; prefs.get_devices()
    for d in prefs.devices: d.use = d.type != 'CPU'
    scene.cycles.device = 'GPU'
except Exception as e:      # the CPU renders the same picture, only slower
    print('GPU not set:', e)
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = CELL; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'; scene.render.image_settings.color_depth = '8'
scene.view_settings.view_transform = 'Standard'

# ---- bake, render
bpy.context.view_layer.objects.active = domain
bpy.ops.fluid.bake_all()
for f in range(1, FRAMES + 1):
    scene.frame_set(f); scene.render.filepath = str(OUT / f'f{f:03d}.png'); bpy.ops.render.render(write_still=True)

# ---- sheet (numpy only: Blender's Python has no PIL)
import numpy as np
sheet = np.zeros((TILE * CELL, TILE * CELL, 4), np.float32)
for f in range(1, min(FRAMES, TILE * TILE) + 1):
    img = bpy.data.images.load(str(OUT / f'f{f:03d}.png')); px = np.array(img.pixels[:], np.float32).reshape(CELL, CELL, 4); bpy.data.images.remove(img)
    r, c = (f - 1) // TILE, (f - 1) % TILE
    sheet[(TILE - 1 - r) * CELL:(TILE - r) * CELL, c * CELL:(c + 1) * CELL] = px        # image rows run bottom-up; frame 1 is the top-left tile
out_img = bpy.data.images.new(PRESET, TILE * CELL, TILE * CELL, alpha=True); out_img.pixels = sheet.ravel().tolist()
out_img.filepath_raw = str(OUT.parent / f'{PRESET}.png'); out_img.file_format = 'PNG'; out_img.save()
print('SHEET', OUT.parent / f'{PRESET}.png', 'alpha max %.3f mean %.4f' % (float(sheet[..., 3].max()), float(sheet[..., 3].mean())))
