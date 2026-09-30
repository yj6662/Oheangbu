"""Blender 5.0 batch preview of #297 kit JSON meshes with the candidate textures (look review before Unity).

blender -b --factory-startup -P Tools/Blender/preview_hanok297.py -- <specimen dir> <out dir> [name ...]
Unity (x right, y up, z fwd, left-handed) -> Blender (x, y=z_u, z=y_u); the handedness flip reverses the winding.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
A = ROOT / 'Oheangbu/Assets'
STAGE = ROOT / 'Art/World/Compact/Rebuild/Finish297/Stage/Textures'
HW = A / 'HwaseongHaenggung/Textures'; FG = A / 'HwaseongForteressGate/Textures'
TEX = {  # slot: (base colour, normal, uv scale, tint)
    'tile': (STAGE / 'T_RoofTile297_BC.png', STAGE / 'T_RoofTile297_N.png', 1.0, None),
    'tile_dark': (STAGE / 'T_RoofTile297_BC.png', None, 1.0, (.55, .55, .58)),
    'ridge': (HW / 'T_KoreanWall_1_BC.png', HW / 'T_KoreanWall_1_N.png', 1.0, (.92, .92, .9)),
    'dancheong_beam': (FG / 'T_Crossbeam_BC.png', FG / 'T_Crossbeam_N.png', 1.0, None),
    'dancheong_bracket': (FG / 'T_ComplexBracket_BC.png', FG / 'T_ComplexBracket_N.png', 1.0, None),
    'wood_red': (HW / 'T_KoreanWood_2_BC.png', HW / 'T_KoreanWood_2_N.png', 1.0, None),
    'wood_dark': (HW / 'T_Koreanwood_1_BC.png', HW / 'T_Koreanwood_1_N.png', 1.0, None),
    'wood_board': (HW / 'T_Koreanwood_1_BC.png', HW / 'T_Koreanwood_1_N.png', 1.0, (.85, .8, .75)),
    'fascia': (HW / 'T_KoreanWood_2_BC.png', None, 1.0, None),
    'gable': (FG / 'T_B_RoofBoard_001_BC.png', None, 1.0, None),
    'plaster': (HW / 'T_KoreanWall_1_BC.png', HW / 'T_KoreanWall_1_N.png', 1.0, None),
    'stone_dressed': (FG / 'T_Fortification_001_BC.png', FG / 'T_Fortification_001_N.png', 1.0, None),
    'stone_rough': (FG / 'T_CW_001_BC.png', FG / 'T_CW_001_N.png', 1.0, None),
    'stone_plain': (HW / 'T_KoreanStone_1_BC.png', HW / 'T_KoreanStone_1_N.png', 1.0, None),
    'lattice': (STAGE / 'T_DoorTtisal297_BC.png', STAGE / 'T_DoorTtisal297_N.png', 1.0, None),
    'lattice_red': (STAGE / 'T_DoorTtisalRed297_BC.png', STAGE / 'T_DoorTtisal297_N.png', 1.0, None),
    'window_grid': (STAGE / 'T_WindowGrid297_BC.png', STAGE / 'T_WindowGrid297_N.png', 1.0, None),
    'soffit': (STAGE / 'T_Soffit297_BC.png', STAGE / 'T_Soffit297_N.png', 1.0, None),
    'floor_wood': (HW / 'T_Koreanwood_1_BC.png', None, 1.0, (.9, .85, .8)),
    'floor_brick': (HW / 'T_KoreanTile_1_BC.png', HW / 'T_KoreanTile_1_N.png', 1.0, None),
    'ceiling': (HW / 'T_Ceiling_BC.png', None, 1.0, None),
    'brick_dark': (FG / 'T_B_CastleWall_001_BC.png', FG / 'T_B_CastleWall_001_N.png', 1.0, None),
    'paving': (HW / 'T_KoreanTile_3_BC.png', HW / 'T_KoreanTile_3_N.png', 1.0, None),
}


def material(slot):
    if slot in bpy.data.materials: return bpy.data.materials[slot]
    m = bpy.data.materials.new(slot); m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bc, nm, _, tint = TEX.get(slot, (None, None, 1, (.5, .5, .5)))
    if bc and Path(bc).exists():
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(bc), check_existing=True)
        if tint:
            mix = nt.nodes.new('ShaderNodeMixRGB'); mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1
            mix.inputs[2].default_value = (*tint, 1); nt.links.new(t.outputs[0], mix.inputs[1]); nt.links.new(mix.outputs[0], bsdf.inputs['Base Color'])
        else: nt.links.new(t.outputs[0], bsdf.inputs['Base Color'])
    elif tint: bsdf.inputs['Base Color'].default_value = (*tint, 1)
    if nm and Path(nm).exists():
        tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(str(nm), check_existing=True); tn.image.colorspace_settings.name = 'Non-Color'
        nmap = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs[0], nmap.inputs['Color']); nt.links.new(nmap.outputs[0], bsdf.inputs['Normal'])
    bsdf.inputs['Roughness'].default_value = .7 if slot not in ('tile', 'tile_dark') else .5
    return m


def load(path, name):
    d = json.loads(Path(path).read_text(encoding='utf-8'))
    v = d['v']; uv = d['uv']; nv = len(v) // 3
    verts = [(v[3 * i], v[3 * i + 2], v[3 * i + 1]) for i in range(nv)]
    faces = []; mats = []; slots = []
    for s in d['sub']:
        if s['m'] not in slots: slots.append(s['m'])
        t = s['t']
        for k in range(0, len(t), 3): faces.append((t[k], t[k + 2], t[k + 1])); mats.append(slots.index(s['m']))
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    uvl = me.uv_layers.new(name='UV')
    for poly in me.polygons:
        poly.material_index = mats[poly.index]
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index; uvl.data[li].uv = (uv[2 * vi], uv[2 * vi + 1])
    for s in slots: me.materials.append(material(s))
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob); return ob


def setup(out_dir):
    sc = bpy.context.scene
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
        try: sc.render.engine = eng; break
        except TypeError: continue
    if sc.render.engine == 'CYCLES': sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs[0].default_value = (.62, .66, .70, 1); bg.inputs[1].default_value = .9
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); bpy.context.collection.objects.link(sun)
    sun.data.energy = 3.2; sun.data.color = (1, .95, .88); sun.rotation_euler = (math.radians(55), 0, math.radians(-35))
    ground = bpy.data.meshes.new('g'); ground.from_pydata([(-80, -80, 0), (80, -80, 0), (80, 80, 0), (-80, 80, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('ground', ground); bpy.context.collection.objects.link(g)
    gm = bpy.data.materials.new('ground'); gm.use_nodes = True; next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (.36, .33, .28, 1)
    ground.materials.append(gm)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); bpy.context.collection.objects.link(cam); sc.camera = cam
    return cam


def shoot(cam, ob, out, views):
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb))); hi = Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb)))
    c = (lo + hi) / 2; r = (hi - lo).length
    for k, (az, el, dist, look_z) in enumerate(views):
        a, e = math.radians(az), math.radians(el)
        cam.location = c + Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e))) * r * dist   # front = +z Unity = +y Blender
        tgt = Vector((c.x, c.y, lo.z + (hi.z - lo.z) * look_z))
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = 35
        bpy.context.scene.render.filepath = str(out.with_name(out.stem + f'_{k}.png')); bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:]; src, dst = (p if p.is_absolute() else ROOT / p for p in (Path(argv[0]), Path(argv[1]))); names = argv[2:]
    dst.mkdir(parents=True, exist_ok=True)
    for p in sorted(src.glob('*_LOD0.json')):
        name = p.stem[:-5]
        if names and name not in names: continue
        bpy.ops.wm.read_factory_settings(use_empty=True); cam = setup(dst)
        ob = load(p, name)
        shoot(cam, ob, dst / f'{name}.png', [(-28, 14, .95, .45), (35, 34, 1.05, .35), (-8, 4, .62, .62)])
    print('previews ->', dst)
