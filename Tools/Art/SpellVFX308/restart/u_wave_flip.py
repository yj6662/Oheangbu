"""D308-153 route A (Blender bake) for 우: one segment of the breaking ring wall, simulated as FLIP liquid and rendered to a flipbook.
A jet of water shoots up out of a slot in the ground, leans toward the viewer (the ring's centre), curls over and collapses, throwing spray.
Unity places several of these segments round the ring as camera-facing cards (route A); the sim is camera-independent, the render is seen from the
ring's inside at the shoulder camera's elevation.

  blender -b -P Tools/Art/SpellVFX308/restart/u_wave_flip.py -- <out_dir> [--res 128] [--frames 48] [--samples 32] [--skip-bake]

Writes <out_dir>/cache (fluid cache), <out_dir>/frames/f_####.png (RGBA, transparent film), <out_dir>/scene.blend."""
import sys, os, math
import bpy

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else 'u_wave_flip')
def opt(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default
RES, FRAMES, SAMPLES = opt('--res', 128), opt('--frames', 48), opt('--samples', 32)
SKIP_BAKE = '--skip-bake' in argv
os.makedirs(OUT, exist_ok=True)

# ---- clean scene ----
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.frame_start, sc.frame_end = 1, FRAMES
sc.render.fps = 24

def box(name, loc, size):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.active_object; o.name = name
    o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    bpy.ops.object.transform_apply(scale=True)
    return o

# ---- fluid domain: 4 m along the wall (x), 3.6 m across (y, viewer at -y), 3.2 m tall ----
dom = box('Domain', (0, 0, 1.6), (4.0, 3.6, 3.2))
m = dom.modifiers.new('Fluid', 'FLUID'); m.fluid_type = 'DOMAIN'
ds = m.domain_settings
ds.domain_type = 'LIQUID'
ds.resolution_max = RES
ds.use_mesh = True
ds.mesh_scale = 2
ds.use_spray_particles = True
ds.use_foam_particles = False      # r2: r1's spray + foam was a grey curtain over the sheet
ds.use_bubble_particles = False
for k, v in (('sndparticle_sampling_wavecrest', 60), ('sndparticle_potential_min_wavecrest', 0.25), ('sndparticle_potential_max_wavecrest', 2.0),
             ('sndparticle_potential_min_energy', 3.0), ('sndparticle_life_max', 80)):
    if hasattr(ds, k): setattr(ds, k, v)
ds.cache_directory = os.path.join(OUT, 'cache')
ds.cache_type = 'ALL'
ds.cache_frame_start, ds.cache_frame_end = 1, FRAMES
ds.time_scale = 1.0
for side in ('use_collision_border_front', 'use_collision_border_back', 'use_collision_border_right', 'use_collision_border_left', 'use_collision_border_top'):
    if hasattr(ds, side): setattr(ds, side, False)
if hasattr(ds, 'use_collision_border_bottom'): ds.use_collision_border_bottom = True

# ---- r2: a standing slab of water thrown up and toward the viewer at frame 1 (r1's inflow slot never rose: the water spread on the floor) ----
# r3: three thin slabs stacked, the higher ones thrown harder toward the viewer (shear -> the top overtakes the base and curls; r2's single slab
# moved as one block and slumped)
for i, (z, vy, vz) in enumerate(((0.25, -0.6, 3.2), (0.7, -2.0, 4.0), (1.15, -3.6, 4.4))):
    inf = box('Slab%d' % i, (0, 0.9, z), (3.6, 0.22, 0.45))
    fm = inf.modifiers.new('Fluid', 'FLUID'); fm.fluid_type = 'FLOW'
    fs = fm.flow_settings
    fs.flow_type = 'LIQUID'
    fs.flow_behavior = 'GEOMETRY'
    fs.use_initial_velocity = True
    fs.velocity_coord = (0.0, vy, vz)
    inf.hide_render = True

# ---- ground (collision) ----
bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0.0))
gnd = bpy.context.active_object; gnd.name = 'Ground'
em = gnd.modifiers.new('Fluid', 'FLUID'); em.fluid_type = 'EFFECTOR'
gnd.hide_render = True

# ---- bake ----
if not SKIP_BAKE:
    bpy.context.view_layer.objects.active = dom
    dom.select_set(True)
    print('[u_wave] baking res', RES, 'frames', FRAMES, flush=True)
    r = bpy.ops.fluid.bake_all()
    print('[u_wave] bake result', r, flush=True)

# ---- look: teal glassy water, white foam / spray points, transparent film ----
water = bpy.data.materials.new('Water'); water.use_nodes = True
bsdf = next(n for n in water.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
bsdf.inputs['Base Color'].default_value = (0.32, 0.46, 0.47, 1)
bsdf.inputs['Roughness'].default_value = 0.06
bsdf.inputs['IOR'].default_value = 1.33
for key in ('Transmission Weight', 'Transmission'):
    if key in bsdf.inputs: bsdf.inputs[key].default_value = 0.85; break
dom.data.materials.clear(); dom.data.materials.append(water)

foam = bpy.data.materials.new('Foam'); foam.use_nodes = True
fb = next(n for n in foam.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
fb.inputs['Base Color'].default_value = (0.95, 0.97, 0.97, 1)
fb.inputs['Roughness'].default_value = 0.5
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0, location=(0, 0, -20))
drop = bpy.context.active_object; drop.name = 'Drop'; drop.data.materials.append(foam)
for ps in dom.particle_systems:
    st = ps.settings
    st.render_type = 'OBJECT'; st.instance_object = drop
    st.particle_size = 0.006
    st.size_random = 0.6

world = bpy.data.worlds.new('World'); sc.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (0.78, 0.75, 0.68, 1); bg.inputs['Strength'].default_value = 1.2
bpy.ops.object.light_add(type='SUN', location=(2, -3, 6)); sun = bpy.context.active_object
sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30))

# camera inside the ring at the shoulder camera's elevation, looking at the wall
bpy.ops.object.camera_add(location=(0, -4.6, 2.0)); cam = bpy.context.active_object
cam.rotation_euler = (math.radians(80), 0, 0); cam.data.lens = 30
sc.camera = cam

sc.render.engine = 'CYCLES'
sc.cycles.samples = SAMPLES
sc.cycles.use_denoising = True
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for dev in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = dev; prefs.get_devices()
            if any(d.type == dev for d in prefs.devices):
                for d in prefs.devices: d.use = d.type == dev
                sc.cycles.device = 'GPU'; break
        except Exception: pass
except Exception as e:
    print('[u_wave] gpu setup skipped', e)
sc.render.film_transparent = True
sc.render.resolution_x, sc.render.resolution_y = 512, 512
sc.render.image_settings.file_format = 'PNG'
sc.render.image_settings.color_mode = 'RGBA'
sc.render.filepath = os.path.join(OUT, 'frames', 'f_')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'scene.blend'))
print('[u_wave] rendering', flush=True)
bpy.ops.render.render(animation=True)
print('[u_wave] done', flush=True)
