"""Read-only source texture audit; writes only facial diagnostic JSON."""
import bpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path('C:/Users/yj666/Oheangbu');L=R/'Art/PlayerPhase1/C02_RigFaceLab'
records=[]
for rel in ['Baseline.blend','Motion/B1/Motion_B1.blend','Face/C3/Face_C3.blend','Final/Integrated_B1_C3.blend']:
 bpy.ops.wm.open_mainfile(filepath=str(L/rel))
 for name in ['C02_Mesh_0','FaceLid_Left','FaceLid_Right']:
  o=bpy.data.objects.get(name)
  if not o:continue
  for mat in o.data.materials:
   if not mat or not mat.use_nodes:continue
   for n in mat.node_tree.nodes:
    if n.type!='TEX_IMAGE' or not n.image:continue
    im=n.image;pixels=np.asarray(im.pixels[:],dtype=np.float32)
    packed=bytes(im.packed_file.data) if im.packed_file else None
    path=Path(bpy.path.abspath(im.filepath))
    records.append({'blend':rel,'object':name,'material':mat.name,'image':im.name,'colorspace':im.colorspace_settings.name,
     'alpha_mode':im.alpha_mode,'source':im.source,'size':list(im.size),'filepath':str(path),'is_dirty':im.is_dirty,
     'packed_sha256':hashlib.sha256(packed).hexdigest() if packed else None,'packed_size':len(packed) if packed else 0,
     'file_sha256':hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None,
     'linear_pixels_sha256':hashlib.sha256(pixels.tobytes()).hexdigest(),'min':float(pixels.min()) if pixels.size else None,'max':float(pixels.max()) if pixels.size else None,
     'mean':float(pixels.mean()) if pixels.size else None,'sample':pixels[400000:400016].tolist()})
(L/'Face/texture_source_audit.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
print('FACE_TEXTURE_AUDIT',json.dumps(records))
