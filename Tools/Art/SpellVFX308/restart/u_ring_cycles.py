"""D308-153 route A for 우: the whole breaking-wave ring built in Blender frame by frame and path-traced in Cycles from the capture camera's pose.
Two passes per frame: far (everything ahead of the caster, plus the ground shadow) and near (the arc between the camera and the caster) -
Unity lays the far card behind the bodies and the near card over them.

Coordinates: Blender x = right, y = forward (the caster faces +y), z = up; the caster stands at the origin; the ring is centred on him.
Capture camera (Vol2VfxCapture.Shoot, pitch 0): eye = origin + (0.7 right, 2.25 up, 2.8 back), level, vertical FOV 60.

  blender -b -P Tools/Art/SpellVFX308/restart/u_ring_cycles.py -- <out_dir> [--fps 30] [--seconds 2.6] [--w 960] [--h 540] [--samples 32] [--only 12]"""
import sys, os, math, random
import bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else 'u_ring')
def opt(n, d): return type(d)(argv[argv.index(n) + 1]) if n in argv else d
FPS, SECONDS, W, H, SAMPLES, ONLY = opt('--fps', 30), opt('--seconds', 2.6), opt('--w', 960), opt('--h', 540), opt('--samples', 32), opt('--only', -1)
os.makedirs(os.path.join(OUT, 'far'), exist_ok=True); os.makedirs(os.path.join(OUT, 'near'), exist_ok=True)
rng = random.Random(7)

# ------------------------------------------------------------------ the wave (same law as the design: rising sweep, chasing crests, curl, inward wash)
R = 2.2
SPIN = 1.0
NU, NV = 320, 34      # r4: finer sheet (r3 read as smooth plastic)
CURL0, CURL1, WASH0, WASH1, FADE0, FADE1 = 0.16, 0.55, 1.05, 1.7, 1.3, 2.0      # r2: fades earlier (r1 ended in a white disc)
LAYERS = [  # radial offset, height scale, lobe phase, flow speed, rise lag, lobes, shade
    (0.8, 0.75, 0.5, 0.8, 0.07, 6),
    (0.0, 1.0, 0.0, 1.0, 0.0, 5),
    (-0.65, 0.45, 0.27, 1.35, 0.04, 4)]

def sm(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)
def frac(x): return x - math.floor(x)
def ease_back(t):
    t = min(1.0, max(0.0, t)); c1 = 1.70158; c3 = c1 + 1; return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2
def phase(t): return SPIN * (2.4 * t + (8.0 - 2.4) * 0.9 * (1 - math.exp(-t / 0.9)))
def back(th): return sm(0.55, 1.0, abs(th) / math.pi)
def dirv(th): return Vector((math.sin(th), math.cos(th), 0.0))
def hmax(th, L): return (2.4 + (1.5 - 2.4) * sm(0.15, 0.6, abs(th) / math.pi)) * (1 - 0.95 * back(th)) * LAYERS[L][1]      # r3: the arc between the camera and the caster stays low (r2 buried him)

def height(th, t, L):
    dr, hk, off, sk, lag0, lobes = LAYERS[L]
    ph = th - phase(t) * sk
    s = frac(SPIN * ph * lobes / (2 * math.pi) + off)
    lobe = sm(0, 0.78, s) if s < 0.78 else 1 - sm(0.78, 1, s)
    bump = 0.5 + 0.42 * lobe + 0.1 * math.sin(7 * ph + 3 * L) + 0.06 * math.sin(13 * ph + 5 * t)
    lag = frac(SPIN * th / (2 * math.pi)) * 0.22 + lag0
    return max(0.0, hmax(th, L) * bump * ease_back((t - lag) / 0.32))

PROF = [(1.10, 0), (0.75, .18), (0.42, .48), (0.18, .80), (0.02, .97), (-0.10, 1), (-0.32, .93), (-0.48, .76), (-0.52, .55)]
def prof(v):
    f = min(1, max(0, v)) * (len(PROF) - 1); i = min(len(PROF) - 2, int(f)); u = f - i
    p = [Vector(PROF[max(0, i - 1)]), Vector(PROF[i]), Vector(PROF[i + 1]), Vector(PROF[min(len(PROF) - 1, i + 2)])]
    u2, u3 = u * u, u * u * u
    return 0.5 * (2 * p[1] + (-p[0] + p[2]) * u + (2 * p[0] - 5 * p[1] + 4 * p[2] - p[3]) * u2 + (-p[0] + 3 * p[1] - 3 * p[2] + p[3]) * u3)

def surface(th, v, t, L):
    h = height(th, t, L)
    curl, wash = sm(CURL0, CURL1, t), sm(WASH0, WASH1, t)
    ks = 0.45 + 0.35 * max(h, 0.4)
    p = prof(v)
    lip = sm(0.5, 0.75, v); up = Vector((-0.02, 0.97 + (v - 0.62) * 0.55))
    p = p.lerp(p.lerp(up, lip), 1 - curl)
    dr, y = p.x * ks, p.y * h
    y *= 1 - 0.9 * wash
    dr -= wash * 1.3 * sm(0.35, 1, v); dr += wash * 0.35 * (1 - sm(0, 0.3, v))
    lean = SPIN * ((0.18 + 0.3 * curl) * min(1, h / 1.6) * sm(0.4, 1, v) + 0.6 * wash * sm(0.3, 1, v))
    q = dirv(th + lean) * max(0.15, R + LAYERS[L][0] + dr); q.z = max(0.02, y)
    return q, h

# ------------------------------------------------------------------ scene
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
def mat(name, base, rough, trans=0.0, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Roughness'].default_value = rough
    for k in ('Transmission Weight', 'Transmission'):
        if k in b.inputs: b.inputs[k].default_value = trans; break
    b.inputs['IOR'].default_value = 1.33
    return m, b

# water: glassy teal, foam from the vertex attribute 'foam' x animated noise -> white rough; 'life' fades to transparent
water, wb = mat('Water', (0.07, 0.20, 0.22), 0.1, 0.35)      # t2: deep glassy teal (t1 read as white plastic)
for k, v in (('Coat Weight', 0.2), ('Coat Roughness', 0.05), ('Specular IOR Level', 0.6)):      # r4b: less lacquer
    if k in wb.inputs: wb.inputs[k].default_value = v
nt = water.node_tree
out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
attr_f = nt.nodes.new('ShaderNodeAttribute'); attr_f.attribute_name = 'foam'
attr_l = nt.nodes.new('ShaderNodeAttribute'); attr_l.attribute_name = 'life'
noise = nt.nodes.new('ShaderNodeTexNoise'); noise.noise_dimensions = '4D'; noise.inputs['Scale'].default_value = 26.0; noise.inputs['Detail'].default_value = 12.0
noise.inputs['Roughness'].default_value = 0.65
wdrv = noise.inputs['W']
ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position = 0.98; ramp.color_ramp.elements[1].position = 1.06
mul = nt.nodes.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'
foamb = nt.nodes.new('ShaderNodeBsdfPrincipled'); foamb.inputs['Base Color'].default_value = (0.95, 0.96, 0.95, 1); foamb.inputs['Roughness'].default_value = 0.45
mix = nt.nodes.new('ShaderNodeMixShader'); transp = nt.nodes.new('ShaderNodeBsdfTransparent'); mix2 = nt.nodes.new('ShaderNodeMixShader')
add = nt.nodes.new('ShaderNodeMath'); add.operation = 'ADD'
nt.links.new(noise.outputs['Fac'], add.inputs[0]); nt.links.new(attr_f.outputs['Fac'], add.inputs[1])
nt.links.new(add.outputs[0], ramp.inputs['Fac'])
nt.links.new(ramp.outputs['Color'], mul.inputs[0]); nt.links.new(attr_f.outputs['Fac'], mul.inputs[1])
nt.links.new(mul.outputs[0], mix.inputs['Fac']); nt.links.new(wb.outputs[0], mix.inputs[1]); nt.links.new(foamb.outputs[0], mix.inputs[2])
nt.links.new(attr_l.outputs['Fac'], mix2.inputs['Fac']); nt.links.new(transp.outputs[0], mix2.inputs[1]); nt.links.new(mix.outputs[0], mix2.inputs[2])
nt.links.new(mix2.outputs[0], out.inputs['Surface'])
# r4: ripples - two animated noises bump the sheet (r3's smooth normals read as plastic)
bn1 = nt.nodes.new('ShaderNodeTexNoise'); bn1.noise_dimensions = '4D'; bn1.inputs['Scale'].default_value = 14.0; bn1.inputs['Detail'].default_value = 6.0
bn2 = nt.nodes.new('ShaderNodeTexNoise'); bn2.noise_dimensions = '4D'; bn2.inputs['Scale'].default_value = 55.0; bn2.inputs['Detail'].default_value = 4.0
bmix = nt.nodes.new('ShaderNodeMath'); bmix.operation = 'MULTIPLY_ADD'; bmix.inputs[1].default_value = 0.35
nt.links.new(bn2.outputs['Fac'], bmix.inputs[0]); nt.links.new(bn1.outputs['Fac'], bmix.inputs[2])
bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.8; bump.inputs['Distance'].default_value = 0.05
nt.links.new(bmix.outputs[0], bump.inputs['Height']); nt.links.new(bump.outputs['Normal'], wb.inputs['Normal']); nt.links.new(bump.outputs['Normal'], foamb.inputs['Normal'])
WDRV2 = (bn1.inputs['W'], bn2.inputs['W'])

ink, inkb = mat('Ink', (0.008, 0.008, 0.01), 0.85)      # t3: matte (t2's 0.35 mirrored the sky into grey)
# r4: a real brush stroke - the sumi stroke sheet's broad dry-brush row as the ribbon's alpha (r3: flat black bands)
try:
    _img = bpy.data.images.load(os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', 'Oheangbu', 'Assets', '_Project', 'Art', 'SpellVFX308', 'Vol2Vfx', 'FreeShowcase', 'Resources', 'FreeShowcase', 'fs_sumi_stroke.png')))
    _int = ink.node_tree
    _tex = _int.nodes.new('ShaderNodeTexImage'); _tex.image = _img
    _uv = _int.nodes.new('ShaderNodeUVMap')
    _int.links.new(_uv.outputs['UV'], _tex.inputs['Vector'])
    _tr = _int.nodes.new('ShaderNodeBsdfTransparent'); _mx = _int.nodes.new('ShaderNodeMixShader')
    _o = next(n for n in _int.nodes if n.type == 'OUTPUT_MATERIAL')
    _int.links.new(_tex.outputs['Alpha'], _mx.inputs['Fac']); _int.links.new(_tr.outputs[0], _mx.inputs[1]); _int.links.new(inkb.outputs[0], _mx.inputs[2]); _int.links.new(_mx.outputs[0], _o.inputs['Surface'])
except Exception as e:
    print('[u_ring] ink texture skipped', e)
spray, sb = mat('Spray', (0.95, 0.97, 0.98), 0.25, 0.25)
bead, _ = mat('Bead', (0.9, 0.95, 0.96), 0.02, 0.95)

world = bpy.data.worlds.new('W'); sc.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Color'].default_value = (0.80, 0.76, 0.68, 1); bg.inputs['Strength'].default_value = 1.1
# t3: a sky for the reflections (t2's flat colour made the sheet read as clay); the film stays transparent
try:
    sky = world.node_tree.nodes.new('ShaderNodeTexSky')
    for st in ('MULTIPLE_SCATTERING', 'SINGLE_SCATTERING', 'NISHITA', 'HOSEK_WILKIE', 'PREETHAM'):
        try: sky.sky_type = st; break
        except Exception: pass
    if hasattr(sky, 'sun_elevation'): sky.sun_elevation = math.radians(35)
    world.node_tree.links.new(sky.outputs['Color'], bg.inputs['Color']); bg.inputs['Strength'].default_value = 0.35
except Exception as e:
    print('[u_ring] sky skipped', e)
bpy.ops.object.light_add(type='SUN'); sun = bpy.context.active_object; sun.data.energy = 3.0; sun.data.angle = math.radians(18)      # r2: soft shadows (r1's hard ones went black under the waves)
sun.rotation_euler = (math.radians(48), 0, math.radians(-35))
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); ground = bpy.context.active_object; ground.is_shadow_catcher = True

bpy.ops.object.camera_add(location=(0.7, -2.8, 2.25), rotation=(math.radians(90), 0, 0)); cam = bpy.context.active_object
cam.data.sensor_fit = 'VERTICAL'; cam.data.angle_y = math.radians(60); sc.camera = cam

sc.render.engine = 'CYCLES'; sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for dev in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = dev; prefs.get_devices()
            if any(d.type == dev for d in prefs.devices):
                for d in prefs.devices: d.use = d.type == dev
                sc.cycles.device = 'GPU'; break
        except Exception: pass
except Exception: pass
sc.render.film_transparent = True
sc.render.resolution_x, sc.render.resolution_y = W, H
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'

def mesh_obj(name, m):
    me = bpy.data.meshes.new(name); o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(m); return o

def build_wave(t, near):
    """All layers' sheets for one pass: faces whose centre is behind the caster (y < -0.35) go to the near pass."""
    life = 1 - sm(FADE0, FADE1, t)
    verts, faces, foam_v, life_v = [], [], [], []
    for L in range(len(LAYERS)):
        base = len(verts)
        for iu in range(NU):
            th = (iu / (NU - 1) * 2 - 1) * math.pi
            for iv in range(NV):
                v = iv / (NV - 1)
                p, h = surface(th, v, t, L)
                verts.append(p)
                # t2: foam only on the crest and the lip (+ the wash), lace elsewhere through the noise threshold (t1 whitened the whole sheet)
                crest = max(0.0, 1 - abs(v - 0.82) / 0.2)
                foam_v.append(min(1.0, 0.55 * crest + 0.15 * sm(0.7, 1.0, v) + 0.05 * sm(WASH0, WASH1, t)) * (1 if h > 0.05 else 0))      # t3: lace, not slabs (r2: wash foam .5 -> .2)
                # life: 1 = visible; the foot and the dead crest fade out, everything fades at the end
                vis = (1 if h > 0.03 else 0) * life * sm(0, 0.1, v) * (0.75 + 0.25 * (1 - back(th)))
                life_v.append(vis)
        for iu in range(NU - 1):
            for iv in range(NV - 1):
                a = base + iu * NV + iv; b, c, d = a + 1, a + NV, a + NV + 1
                cy = (verts[a].y + verts[d].y) * 0.5
                if near is not None and (cy < -0.35) != near: continue
                if life_v[a] + life_v[b] + life_v[c] + life_v[d] <= 0.001: continue
                faces.append((a, c, d, b))
    o = mesh_obj('Wave', water); me = o.data
    me.from_pydata([tuple(v) for v in verts], [], faces)
    fa = me.attributes.new('foam', 'FLOAT', 'POINT'); la = me.attributes.new('life', 'FLOAT', 'POINT')
    fa.data.foreach_set('value', foam_v); la.data.foreach_set('value', life_v)
    for poly in me.polygons: poly.use_smooth = True
    return o

RIBS = []
for k in range(32):
    RIBS.append(dict(th=(k + 0.5) / 32 * 2 * math.pi - math.pi + rng.uniform(-0.1, 0.1), span=rng.uniform(1.0, 1.9), va=rng.uniform(0.45, 0.65), vb=rng.uniform(0.8, 0.95),
                     w=rng.uniform(0.12, 0.26), at=CURL0 + 0.15 * rng.random(), L=2 if rng.random() < 0.3 else 1, rnd=rng.random()))

def build_ink(t, near):
    verts, faces, uvs = [], [], []
    dry = sm(WASH0 - 0.1, WASH0 + 0.5, t)
    for r in RIBS:
        g = min(1, max(0, (t - r['at']) / 0.35))
        if g < 0.02 or dry > 0.98: continue
        n = 22; row = []
        for j in range(n):
            u = j / (n - 1) * g
            th = r['th'] + 0.85 * phase(t) * LAYERS[r['L']][3] + SPIN * r['span'] * (u - g)
            if back(th) > 0.6: row.append(None); continue
            v = r['va'] + (r['vb'] - r['va']) * u ** 1.6 + 0.05 * math.sin(u * 6.9 + r['rnd'] * 6)
            p, h = surface(th, v, t, r['L'])
            if h < 0.2: row.append(None); continue
            pv, _ = surface(th, min(1, v + 0.04), t, r['L']); pt, _ = surface(th + 0.02, v, t, r['L'])
            nrm = (pt - p).cross(pv - p).normalized()
            if nrm.dot(cam.location - p) < 0: nrm = -nrm
            side = (pv - p).normalized()
            wd = r['w'] * (0.25 + 0.75 * math.sin(min(1, u / max(0.01, g)) * math.pi)) * (1 - 0.7 * dry)
            c = p + nrm * 0.03
            if near is not None and ((c.y < -0.35) != near): row.append(None); continue
            row.append((c - side * wd, c + side * wd, u / max(0.01, g)))
        for j in range(n - 1):
            if row[j] is None or row[j + 1] is None: continue
            b0 = len(verts); verts += [row[j][0], row[j][1], row[j + 1][1], row[j + 1][0]]; faces.append((b0, b0 + 1, b0 + 2, b0 + 3))
            # the stroke's wet head (u = 0 in the sheet) rides the ribbon's head; the sheet's broad bottom row (v .012 .. .238)
            uvs += [(1 - row[j][2], 0.012), (1 - row[j][2], 0.238), (1 - row[j + 1][2], 0.238), (1 - row[j + 1][2], 0.012)]
    o = mesh_obj('Ink', ink); o.data.from_pydata([tuple(v) for v in verts], [], faces)
    if faces:
        uvl = o.data.uv_layers.new(name='UVMap')
        for li, loop in enumerate(o.data.loops): uvl.data[li].uv = uvs[loop.vertex_index]
    return o

# spray: ballistic drops thrown off the crests along the flow; beads: fewer, bigger, glassy
P = []   # [pos, vel, radius, kind, age, life]
def step_spray(t, dt):
    if t > 0.06 and t < WASH1:
        for _ in range(int(6000 * dt * (1 - 0.6 * sm(WASH0, WASH1, t)))):      # r4: ~200 a frame (t2: ~100)
            L = rng.choice((0, 1, 1, 2)); th = rng.uniform(-math.pi, math.pi)
            c, h = surface(th, 0.75, t, L)
            if h < 0.35 * LAYERS[L][1] or back(th) > 0.6: continue
            outw = dirv(th); tang = Vector((math.cos(th), -math.sin(th), 0))
            # tangent toward increasing th: d/dth (sin, cos) = (cos, -sin)
            flow = (2.4 + 5.6 * math.exp(-t / 0.9)) * SPIN * (R + LAYERS[L][0])
            vel = Vector((0, 0, rng.uniform(1.5, 4.5) * (0.5 + min(1, h / 2)))) + outw * rng.uniform(-1.2, 1.8) + tang * (flow * rng.uniform(0.3, 0.6) + rng.uniform(-0.6, 0.6))
            kind = 1 if rng.random() < 0.1 else 0
            P.append([c + Vector((rng.uniform(-.1, .1), rng.uniform(-.1, .1), rng.uniform(-.05, .1))), vel, rng.uniform(0.008, 0.022) if kind == 0 else rng.uniform(0.025, 0.05), kind, 0.0, rng.uniform(0.4, 1.0)])
        # r4: crest plumes - the tallest crest points throw fans of white water straight up (the concept's spray crowns)
        if t < WASH0:
            for L in (0, 1):
                cand = [rng.uniform(-math.pi, math.pi) for _ in range(40)]
                best = sorted(((height(th, t, L) / max(0.05, hmax(th, L)), th) for th in cand if back(th) < 0.5), reverse=True)[:3]
                for q_, th in best:
                    if q_ < 0.8: continue
                    c, h = surface(th, 0.9, t, L)
                    for _ in range(18):
                        vel = Vector((rng.uniform(-0.8, 0.8), rng.uniform(-0.8, 0.8), rng.uniform(2.5, 5.5))) - dirv(th) * rng.uniform(0.2, 1.2)
                        P.append([c + Vector((rng.uniform(-.15, .15), rng.uniform(-.15, .15), 0)), vel, rng.uniform(0.006, 0.018), 0, 0.0, rng.uniform(0.5, 1.0)])
    for q in P:
        q[1].z -= 9.8 * 0.55 * dt; q[1] *= (1 - 0.25 * dt); q[0] += q[1] * dt; q[4] += dt
    P[:] = [q for q in P if q[4] < q[5] and q[0].z > 0.0][-7000:]

def build_drops(kind, near):
    bm = bmesh.new()
    for q in P:
        if q[3] != kind or (near is not None and (q[0].y < -0.35) != near): continue
        r = q[2] * (1 - 0.5 * (q[4] / q[5]))
        # r4b: a drop is stretched along its velocity (streaks, as a camera sees flying water; r4 drew polka dots)
        import mathutils
        v = q[1]; sp = v.length
        rot = v.to_track_quat('Z', 'Y').to_matrix().to_4x4() if sp > 1e-3 else mathutils.Matrix.Identity(4)
        st = mathutils.Matrix.Diagonal((0.7, 0.7, 1.0 + min(2.5, sp * 0.3), 1.0)) if kind == 0 else mathutils.Matrix.Identity(4)      # r4c: thinner, shorter (r4b: rice grains)
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r, matrix=mathutils.Matrix.Translation(q[0]) @ rot @ st)
    me = bpy.data.meshes.new('D'); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('Drops%d' % kind, me); sc.collection.objects.link(o); me.materials.append(spray if kind == 0 else bead)
    for p in me.polygons: p.use_smooth = True
    return o

def clear(objs):
    for o in objs:
        me = o.data; bpy.data.objects.remove(o, do_unlink=True); bpy.data.meshes.remove(me)

# r2: one colour pass of everything + one depth pass (camera Z / DEPTH_RANGE as 16-bit grey, every material overridden by an emission of the view depth).
# Unity's card tests each pixel against the scene depth itself, so the bodies, the caster and the ground occlude the water exactly where they should
# (r1's far/near split put the far card in front of the bear and hid it).
DEPTH_RANGE = 12.0      # metres per full grey (8-bit after Unity's import: ~4.7 cm a step)
dmat = bpy.data.materials.new('DepthOut'); dmat.use_nodes = True
dn = dmat.node_tree
for n in list(dn.nodes):
    if n.type != 'OUTPUT_MATERIAL': dn.nodes.remove(n)
dout = next(n for n in dn.nodes if n.type == 'OUTPUT_MATERIAL')
cd = dn.nodes.new('ShaderNodeCameraData'); dv = dn.nodes.new('ShaderNodeMath'); dv.operation = 'DIVIDE'; dv.inputs[1].default_value = DEPTH_RANGE
em = dn.nodes.new('ShaderNodeEmission'); em.inputs['Strength'].default_value = 1.0
dn.links.new(cd.outputs['View Z Depth'], dv.inputs[0]); dn.links.new(dv.outputs[0], em.inputs['Color']); dn.links.new(em.outputs[0], dout.inputs['Surface'])
os.makedirs(os.path.join(OUT, 'col'), exist_ok=True); os.makedirs(os.path.join(OUT, 'dep'), exist_ok=True)

def render_pass(kind, f):
    vl = sc.view_layers[0]
    if kind == 'dep':
        vl.material_override = dmat; ground.hide_render = True
        sc.cycles.samples, sc.cycles.use_denoising = 1, False
        sc.render.filter_size = 0.01
        # linear values in the file: 'Raw' (no display curve); 'Standard' would sRGB-encode the depth
        try: sc.view_settings.view_transform = 'Raw'
        except TypeError: sc.view_settings.view_transform = 'Standard'
        sc.view_settings.exposure = 0; sc.view_settings.gamma = 1
        sc.render.image_settings.color_mode = 'RGBA'; sc.render.image_settings.color_depth = '16'
    else:
        vl.material_override = None; ground.hide_render = True      # r3: no shadow catcher (r2's ground shadow read as a black hole)
        sc.cycles.samples, sc.cycles.use_denoising = SAMPLES, True
        sc.render.filter_size = 1.5
        sc.view_settings.view_transform = VIEW_SAVED
        sc.render.image_settings.color_mode = 'RGBA'; sc.render.image_settings.color_depth = '8'
    sc.render.filepath = os.path.join(OUT, kind, 'f_%04d.png' % f)
    bpy.ops.render.render(write_still=True)

VIEW_SAVED = sc.view_settings.view_transform
frames = int(round(SECONDS * FPS))
dt = 1.0 / FPS
for f in range(frames):
    t = f * dt
    step_spray(t, dt)
    if ONLY >= 0 and f != ONLY: continue
    wdrv.default_value = t * 0.8
    for _w in WDRV2: _w.default_value = t * 1.6
    objs = [build_wave(t, None), build_ink(t, None), build_drops(0, None), build_drops(1, None)]
    render_pass('col', f)
    render_pass('dep', f)
    clear(objs)
    print('[u_ring] frame', f, '/', frames, 'drops', len(P), flush=True)
print('[u_ring] done', flush=True)
