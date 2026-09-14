"""Create the selected-A model, preserving the accepted C02 mesh outside the right hand."""
from pathlib import Path
import json,hashlib
R=Path('C:/Users/yj666/Oheangbu');OUT=R/'Art/PlaytestPolish/Hands'
kernel=Path(__file__).with_name('finalize_contact.py').read_text()
kernel=kernel.replace("O=R/'Art/PlayerPhase1/PlaytestReRig'","O=R/'Art/PlaytestPolish/Hands'")
kernel=kernel.replace('Work/Player_C02_FingerRig.blend','Work/Player_C02_GripA_Work.blend')
kernel=kernel.replace('Work/Player_C02_ReRig.blend','Work/Player_C02_GripA.blend').replace('Exports/Player_C02_ReRig.fbx','Exports/Player_C02_GripA.fbx')
kernel=kernel.replace("body=bpy.data.objects['C02_Mesh_0']","body=bpy.data.objects['C02_Mesh_0']; before_vertices=[v.co.copy()for v in body.data.vertices]; before_bones={b.name:[list(row)for row in b.matrix_local]for b in r.data.bones}")
# Reuse real shaft radial intersections and inverse-skin corrections; no source edits.
exec(compile(kernel,'grip_a_final_contact_kernel','exec'))
unchanged=max((v.co-before_vertices[v.index]).length for v in body.data.vertices if v.index not in original)
bind_error=max(abs(b.matrix_local[i][j]-before_bones[b.name][i][j])for b in r.data.bones for i in range(4)for j in range(4))
report={'status':'PASS_PRESERVATION'if unchanged==0 and bind_error==0 else'FAIL_PRESERVATION',
 'source':str(R/'Art/PlayerPhase1/PlaytestReRig/Work/Player_C02_ReRig.blend'),'selected_reference':'Grip_A.png',
 'outside_right_hand_max_change_source_units':unchanged,'bone_bind_matrix_max_change':bind_error,'bone_count':len(r.data.bones),
 'changed_finger_pose':True,'source_fbx_sha256':hashlib.sha256((OUT/'Exports/Player_C02_GripA.fbx').read_bytes()).hexdigest(),
 'scope':'Selected A right-hand pose and bounded skin contact refinement only; no new animation action, root skeleton, body/face/outfit edit.'}
(OUT/'Validation/preservation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
