"""One-time source authoring setup. Existing approved summons are never rewritten."""
from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/MetalTiger';O.mkdir(exist_ok=True)
A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120'
files=list((A/'Profiles').glob('*.asset'))
for folder in ['WoodDeer','FireHaetae','MeshySummons']:files+=list((A/folder).rglob('*'))
if not (O/'scope_before.json').exists():(O/'scope_before.json').write_text(json.dumps({p.relative_to(R).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()},indent=2))
s=(R/'Tools/SpellVFX120/build_fire_haetae.py').read_text().replace('FireHaetae','MetalTiger').replace('FIRE_HAETAE','METAL_TIGER').replace('040_B188','088_C19C').replace('Haetae','Tiger')
start=s.index("sep=n.new('ShaderNodeSeparateColor')");end=s.index('scene=bpy.context.scene',start)
s=s[:start]+'''hue=n.new('ShaderNodeHueSaturation');hue.inputs['Saturation'].default_value=.3;hue.inputs['Value'].default_value=1.15;l.new(tex.outputs['Color'],hue.inputs['Color']);l.new(hue.outputs[0],bs.inputs['Base Color'])
''' + s[end:]
s=s.replace("for kind in ['BaseColor','EmberMask']:","for kind in ['BaseColor']:")
start=s.index(" else:\n  emission=n.new('ShaderNodeEmission')");end=s.index(' image.filepath_raw=',start);s=s[:start]+s[end:]
start=s.index(' else:\n  glow=');end=s.index("normal=n.new",start);s=s[:start]+s[end:]
s=s.replace("bs.inputs['Roughness'].default_value=.82", "bs.inputs['Roughness'].default_value=.43;bs.inputs['Metallic'].default_value=.65")
# Bake diffuse independently of the final metal BSDF to retain useful albedo.
s=s.replace("scene=bpy.context.scene;", "bs.inputs['Metallic'].default_value=0\nscene=bpy.context.scene;")
s=s.replace('cam.data.ortho_scale=4.1','cam.data.ortho_scale=4.8').replace('M_MetalTiger_CharcoalEmber','M_MetalTiger_BrushedSilver')
(R/'Tools/SpellVFX120/build_metal_tiger.py').write_text(s)
print('METAL_TIGER_AUTHORING_READY')
