"""#308 summon ink bodies (SPEC-SPELL-VFX-OVERHAUL-308 11.14, D308-83b): melt a summon model into a body of LIQUID ink.

  blender --background --factory-startup --python Tools/Blender/SpellVFX308/inkbody308.py -- deer

User, 2026-10-14: "고체형태가 아니라 액체덩어리처럼" - the shader alone left a statue with a sheen. Here the model itself is
melted: voxel remesh (the modelled detail - fur planes, hard creases - fuses into one skin), smoothing, then everything that
faces the ground is pulled down in clumps (hanging lobes), and a few drops hang on threads from the lowest points.
The silhouette of the animal is kept; the surface is no longer carved.
In : the game's own model FBX (read only).   Out: Assets/_Project/Art/SpellVFX308/Meshes/V308_ink<key>.fbx (metres, Y up
after export, one mesh, smooth normals), source .blend kept in Art/SpellVFX308/Meshes/.
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[3]
GAME = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Meshes'
KEEP = ROOT / 'Art/SpellVFX308/Meshes'
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
KEY = argv[0] if argv else 'deer'

# per summon: source FBX, which objects make the body, voxel size (m), how far the undersides hang (m), triangle budget
BODIES = {
    'deer': dict(src='Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer/SM_WoodDeer.fbx', pick=('Body',), voxel=.011, hang=.07, tris=22000, drips=0),     # 3rd pass (D308-83d): no hanging drops - the user found them awkward; the ink leaves the body as streaming strands (Unity side)
}
B = BODIES[KEY]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT / B['src']))
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH' and any(p in o.name for p in B['pick'])]
if not parts:
    raise SystemExit('no body mesh among ' + ', '.join(o.name for o in bpy.context.scene.objects))
for o in bpy.context.scene.objects:
    o.select_set(o in parts)
bpy.context.view_layer.objects.active = parts[0]
if len(parts) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != body:
        bpy.data.objects.remove(o, do_unlink=True)
zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
height = max(zs) - min(zs)
print('INKBODY source', body.name, len(body.data.polygons), 'faces, height %.2f' % height)

# 1. one skin: voxel remesh, then smooth the carving away (twice: before and after the hang)
rm = body.modifiers.new('melt', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = B['voxel']; rm.adaptivity = 0.0
bpy.ops.object.modifier_apply(modifier=rm.name)
sm = body.modifiers.new('soft', 'SMOOTH'); sm.factor = .5; sm.iterations = 4      # more than this eats the thin parts (antlers, shins)
bpy.ops.object.modifier_apply(modifier=sm.name)

# 2. the hang: what faces the ground is pulled down, in clumps, most where the body is widest overhead (belly, chin, brisket)
rnd = random.Random(308 + sum(map(ord, KEY)))
bm = bmesh.new(); bm.from_mesh(body.data); bm.normal_update()
ground = min(v.co.z for v in bm.verts)
lows = []
for v in bm.verts:
    down = max(0.0, -v.normal.z)
    if down < .25:
        continue
    clump = noise.noise(Vector((v.co.x * 5.5, v.co.y * 5.5, 3.1))) * .5 + .5          # 0..1, patches a hand wide
    rel = (v.co.z - ground) / height
    lift = max(0.0, min(1.0, (rel - .36) / .08)) * max(0.0, min(1.0, (.74 - rel) / .06))   # only the trunk and the neck hang: legs, head and antlers keep their line
    pull = B['hang'] * down * down * (.25 + 1.3 * clump ** 2) * lift
    v.co.z -= pull
    if clump > .58 and down > .6 and lift >= 1.0 and rel < .62:
        lows.append((v.co.copy(), clump))
# a slow swell over the whole skin so no part is a clean primitive
for v in bm.verts:
    v.co += v.normal * (noise.noise(v.co * 3.2) * .018)

# 3. drops on threads from the lowest lobes (kept apart from each other)
lows.sort(key=lambda t: -t[1]); taken = []
for co, _ in lows:
    if len(taken) >= B['drips']:
        break
    if any((co - t).length < .16 for t in taken):
        continue
    taken.append(co)
    length = rnd.uniform(.12, .42); r = rnd.uniform(.022, .04)
    top = co + Vector((0, 0, .03)); end = co + Vector((0, 0, -length))
    g = bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=r * 1.5, radius2=r * .35, depth=length)
    for v in g['verts']:
        v.co.z = -v.co.z                                                              # thick at the body, thin toward the drop
        v.co += (top + end) * .5
    g = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r * rnd.uniform(1.3, 1.9))
    for v in g['verts']:
        v.co.z *= 1.25
        v.co += end
bm.to_mesh(body.data); bm.free()

# 4. fuse the threads into the skin, smooth, bring the triangle count down, smooth normals
rm = body.modifiers.new('fuse', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = B['voxel']; rm.adaptivity = 0.0
bpy.ops.object.modifier_apply(modifier=rm.name)
sm = body.modifiers.new('soft2', 'SMOOTH'); sm.factor = .5; sm.iterations = 3
bpy.ops.object.modifier_apply(modifier=sm.name)
faces = len(body.data.polygons) * 2
if faces > B['tris']:
    dc = body.modifiers.new('budget', 'DECIMATE'); dc.ratio = B['tris'] / faces
    bpy.ops.object.modifier_apply(modifier=dc.name)
bpy.ops.object.shade_smooth()
body.name = 'V308_ink' + KEY; body.data.name = body.name
body.data.calc_loop_triangles()
print('INKBODY', KEY, len(body.data.loop_triangles), 'triangles,', len(taken), 'drips')

GAME.mkdir(parents=True, exist_ok=True); KEEP.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(KEEP / f'inkbody308_{KEY}.blend'))
for o in bpy.context.scene.objects:
    o.select_set(o == body)
bpy.ops.export_scene.fbx(filepath=str(GAME / f'V308_ink{KEY}.fbx'), use_selection=True, object_types={'MESH'}, bake_space_transform=True,
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF', add_leaf_bones=False, bake_anim=False)
print('INKBODY wrote', GAME / f'V308_ink{KEY}.fbx')
