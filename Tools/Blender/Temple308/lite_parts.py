"""#308 steam-temple kit: light copies of the parts that are used many times on one building (SPEC-ARCH-TEMPLE-308 §13c).
part_collar -> part_collars (decimated; same UVs and textures) for pillars, flues and lanterns.
  blender -b --factory-startup --python Tools/Blender/Temple308/lite_parts.py
"""
import bpy, bmesh, sys, shutil
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import meshy_parts as MP
KIT = HERE.parents[2] / 'Art/World/Temple308/Kit'
MP.OUT = KIT
LITE = {'part_collar': ('part_collars', .2), 'part_canopy': ('part_canopys', .3), 'part_firebox': ('part_fireboxs', .4), 'part_vessel': ('part_vessels', .4), 'part_flywheel': ('part_flywheels', .4),
        'part_gear': ('part_gears', .4), 'part_cylinder': ('part_cylinders', .4), 'part_stand': ('part_stands', .4), 'part_case': ('part_cases', .45), 'part_bell': ('part_bells', .45)}
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(KIT / 'kit.blend'), link=False) as (src, dst): dst.objects = list(LITE)
for o in dst.objects:
    name, ratio = LITE[o.name]; bpy.context.scene.collection.objects.link(o); bpy.context.view_layer.objects.active = o; o.select_set(True)
    # the GLB comes split along its UV seams: weld first, or the collapse tears the surface open along every seam
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(o.data); bm.free()
    md = o.modifiers.new('d', 'DECIMATE'); md.ratio = ratio; md.delimit = {'UV'}; bpy.ops.object.modifier_apply(modifier='d')
    for poly in o.data.polygons: poly.use_smooth = True
    n = MP.unity_json(o, name); print('LITE', name, 'unity vertices', n)
    for k in ('BC', 'N', 'MS'): shutil.copyfile(KIT / 'Textures' / ('%s_%s.png' % (o.name, k)), KIT / 'Textures' / ('%s_%s.png' % (name, k)))
