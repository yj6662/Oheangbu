"""D308-153 NPR test (2026-10-29): route A's real 3D ring wave (u_ring_cycles.py - three layers, curl, rise, wash) drawn as an illustration instead of
realistic water: EEVEE, stepped (toon) indigo shading x the seigaiha tile on the surface, cream Freestyle outlines on the silhouettes, cream foam on the crests,
black ink veins and brush ribbons. No spray. The geometry and the timeline are u_ring_cycles.py's own (its source is patched and run, not copied).
The user (after two flat pattern-band builds): "문양만 사용해서 만들려고 해서 그런 거 같기도 해. 실제 시안은 문양가지고 만든 게 아니거든."

  blender -b -P Tools/Art/SpellVFX308/restart/u_ring_npr.py -- <out_dir> [--w 960] [--h 540] [--only 18] [--fps 30] [--seconds 2.6]
Needs Art/SpellVFX308/Restart/npr_test/seigaiha.png (seigaiha_tile.py)."""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SEIGAIHA = os.path.normpath(os.path.join(HERE, '..', '..', '..', '..', 'Art', 'SpellVFX308', 'Restart', 'npr_test', 'seigaiha.png'))
src = open(os.path.join(HERE, 'u_ring_cycles.py'), encoding='utf-8').read()


def R(a, b):
    global src
    if a not in src:
        raise SystemExit('[u_ring_npr] patch anchor missing: ' + a[:70])
    src = src.replace(a, b, 1)


NPR = r'''
# ---- NPR (u_ring_npr.py): the water becomes an illustration ----
TILE_U, TILE_V = 1.15, 0.58          # metres per seigaiha tile (2 scales across, 4 rows down): scale radius ~0.29 m
sc.view_settings.view_transform = 'Standard'      # exact colours (AgX / Filmic would wash the indigo)
def _npr_water():
    m = bpy.data.materials.new('WaterNPR'); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    dif = nt.nodes.new('ShaderNodeBsdfDiffuse')
    s2r = nt.nodes.new('ShaderNodeShaderToRGB'); nt.links.new(dif.outputs[0], s2r.inputs[0])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.interpolation = 'CONSTANT'
    e = ramp.color_ramp.elements; e[0].position = 0.0; e[0].color = (0.48, 0.5, 0.56, 1); e[1].position = 0.32; e[1].color = (0.8, 0.82, 0.86, 1)
    e3 = e.new(0.62); e3.color = (1.0, 1.0, 1.0, 1)
    nt.links.new(s2r.outputs['Color'], ramp.inputs['Fac'])
    uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'UVMap'
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(SEIGAIHA); tex.interpolation = 'Cubic'
    nt.links.new(uv.outputs['UV'], tex.inputs['Vector'])
    mul = nt.nodes.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1.0
    nt.links.new(tex.outputs['Color'], mul.inputs[6]); nt.links.new(ramp.outputs['Color'], mul.inputs[7])
    # crest foam: cream lace (the existing 'foam' attribute x a cloud-ish noise threshold)
    af = nt.nodes.new('ShaderNodeAttribute'); af.attribute_name = 'foam'
    nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 7.0; nz.inputs['Detail'].default_value = 2.0
    add = nt.nodes.new('ShaderNodeMath'); add.operation = 'ADD'; nt.links.new(af.outputs['Fac'], add.inputs[0]); nt.links.new(nz.outputs['Fac'], add.inputs[1])
    fr = nt.nodes.new('ShaderNodeValToRGB'); fr.color_ramp.interpolation = 'CONSTANT'; fr.color_ramp.elements[1].position = 0.98
    nt.links.new(add.outputs[0], fr.inputs['Fac'])
    foam = nt.nodes.new('ShaderNodeMix'); foam.data_type = 'RGBA'
    nt.links.new(fr.outputs['Color'], foam.inputs['Factor']); nt.links.new(mul.outputs[2], foam.inputs[6]); foam.inputs[7].default_value = (0.93, 0.9, 0.8, 1)
    # ink veins
    ai = nt.nodes.new('ShaderNodeAttribute'); ai.attribute_name = 'ink'
    ink = nt.nodes.new('ShaderNodeMix'); ink.data_type = 'RGBA'
    nt.links.new(ai.outputs['Fac'], ink.inputs['Factor']); nt.links.new(foam.outputs[2], ink.inputs[6]); ink.inputs[7].default_value = (0.02, 0.02, 0.025, 1)
    em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(ink.outputs[2], em.inputs['Color'])
    al = nt.nodes.new('ShaderNodeAttribute'); al.attribute_name = 'life'
    tr = nt.nodes.new('ShaderNodeBsdfTransparent'); mx = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(al.outputs['Fac'], mx.inputs['Fac']); nt.links.new(tr.outputs[0], mx.inputs[1]); nt.links.new(em.outputs[0], mx.inputs[2])
    nt.links.new(mx.outputs[0], out.inputs['Surface'])
    for k, v in (('surface_render_method', 'DITHERED'), ('blend_method', 'HASHED')):
        try: setattr(m, k, v)
        except Exception: pass
    return m
water = _npr_water()
def _npr_ink():
    m = bpy.data.materials.new('InkNPR'); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Color'].default_value = (0.012, 0.012, 0.016, 1)
    tr = nt.nodes.new('ShaderNodeBsdfTransparent'); mx = nt.nodes.new('ShaderNodeMixShader')
    try:
        tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', 'Oheangbu', 'Assets', '_Project', 'Art', 'SpellVFX308', 'Vol2Vfx', 'FreeShowcase', 'Resources', 'FreeShowcase', 'fs_sumi_stroke.png')))
        uv = nt.nodes.new('ShaderNodeUVMap'); nt.links.new(uv.outputs['UV'], tex.inputs['Vector'])
        ab = nt.nodes.new('ShaderNodeMath'); ab.operation = 'MULTIPLY'; ab.use_clamp = True; ab.inputs[1].default_value = 1.7
        nt.links.new(tex.outputs['Alpha'], ab.inputs[0]); nt.links.new(ab.outputs[0], mx.inputs['Fac'])
    except Exception as ex:
        print('[u_ring_npr] ink texture skipped', ex); mx.inputs['Fac'].default_value = 1.0
    nt.links.new(tr.outputs[0], mx.inputs[1]); nt.links.new(em.outputs[0], mx.inputs[2]); nt.links.new(mx.outputs[0], out.inputs['Surface'])
    for k, v in (('surface_render_method', 'DITHERED'), ('blend_method', 'HASHED')):
        try: setattr(m, k, v)
        except Exception: pass
    return m
ink = _npr_ink()
'''

# 1. materials: rebind water / ink after the originals are made
R("bead, _ = mat('Bead', (0.9, 0.95, 0.96), 0.02, 0.95)\n", "bead, _ = mat('Bead', (0.9, 0.95, 0.96), 0.02, 0.95)\n" + NPR)
# 2. EEVEE instead of Cycles, cream Freestyle outlines
R("sc.render.engine = 'CYCLES'; sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True",
  """for _eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = _eng; break
    except TypeError: pass
sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True
try: sc.eevee.taa_render_samples = 16
except Exception: pass
sc.render.use_freestyle = True; sc.render.line_thickness_mode = 'ABSOLUTE'; sc.render.line_thickness = 2.2
_fs = bpy.context.view_layer.freestyle_settings
_ls = _fs.linesets[0] if len(_fs.linesets) else _fs.linesets.new('Cream')
if _ls.linestyle is None: _ls.linestyle = bpy.data.linestyles.new('Cream')      # headless: a factory scene's lineset has no line style
_ls.select_silhouette = True; _ls.select_border = True; _ls.select_crease = False
_ls.linestyle.color = (0.93, 0.9, 0.8); _ls.linestyle.thickness = 2.6""")
# 3. UVs on the wave: arc length round the ring x height up the face (the tile keeps its scale)
R("    verts, faces, foam_v, life_v, ink_v = [], [], [], [], []", "    verts, faces, foam_v, life_v, ink_v, uv_v = [], [], [], [], [], []")
R("                verts.append(p)\n", "                verts.append(p); uv_v.append((th * (R + LAYERS[L][0]) / TILE_U, v * max(h, 0.3) * 1.5 / TILE_V + L * 0.37))\n")
R("    ia = me.attributes.new('ink', 'FLOAT', 'POINT'); ia.data.foreach_set('value', ink_v)",
  "    ia = me.attributes.new('ink', 'FLOAT', 'POINT'); ia.data.foreach_set('value', ink_v)\n"
  "    if faces:\n        uvl = me.uv_layers.new(name='UVMap')\n        for li, loop in enumerate(me.loops): uvl.data[li].uv = uv_v[loop.vertex_index]")
# 4. no spray; the brush ribbons come back (black, painted)
R("    objs = [build_wave(t, None), build_drops(0, None), build_drops(1, None)]", "    objs = [build_wave(t, None), build_ink(t, None)]")
# 5. a single colour pass (this is a look test; the depth pass is route A's install concern)
R("    render_pass('dep', f)\n", "")

sys.argv = [sys.argv[0]] + sys.argv[1:]
exec(compile(src, os.path.join(HERE, 'u_ring_cycles.py') + ' [npr]', 'exec'), {'__name__': '__main__', '__file__': os.path.join(HERE, 'u_ring_cycles.py'), 'SEIGAIHA': SEIGAIHA})
