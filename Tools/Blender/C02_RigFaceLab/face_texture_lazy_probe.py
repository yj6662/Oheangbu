"""Reproduce lazy-image destination contamination in isolated Face/TextureProbe."""
import bpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path('C:/Users/yj666/Oheangbu');L=R/'Art/PlayerPhase1/C02_RigFaceLab';O=L/'Face/TextureProbe';O.mkdir(exist_ok=True)
records=[]
for force in [False,True]:
 bpy.ops.wm.open_mainfile(filepath=str(L/'Face/C3/Face_C3.blend'))
 im=bpy.data.images['texture_0'];target=O/('forced.png' if force else 'lazy.png')
 target.write_bytes((L/'Final/Textures/C02_1_texture_0.png').read_bytes())
 before=im.has_data
 if force:p=np.asarray(im.pixels[:],dtype=np.float32)
 im.filepath_raw=str(target);im.file_format='PNG';im.save()
 p=np.asarray(im.pixels[:],dtype=np.float32)
 records.append({'forced_load_before_filepath_change':force,'initial_has_data':before,'pixels_sha256':hashlib.sha256(p.tobytes()).hexdigest(),'png_sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
(O/'result.json').write_text(json.dumps(records,indent=2),encoding='utf-8');print('LAZY_PROBE',json.dumps(records))
