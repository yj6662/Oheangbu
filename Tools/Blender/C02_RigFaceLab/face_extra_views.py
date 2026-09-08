"""Right-side C3 checks, where the initial C1 hair-contamination defect occurred."""
import bpy
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu');ns={'__name__':'face_helpers'}
exec(compile((ROOT/'Tools/Blender/C02_RigFaceLab/face_inspect.py').read_text(encoding='utf-8'),'face_inspect.py','exec'),ns)
for value,name in [(0,'C3_open_right'),(1,'C3_closed_right')]:
 for side in ['Left','Right']:bpy.data.objects['FaceLid_'+side].data.shape_keys.key_blocks['Blink'+side].value=value
 bpy.context.view_layer.update();ns['render'](name,(-1,0,0))
