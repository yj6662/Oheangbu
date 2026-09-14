"""Neutral A FBX round-trip marker and contact test, not a Unity runtime acceptance."""
from pathlib import Path
import json
source=Path(__file__).with_name('check_export_grip.py').read_text()
source=source.replace("O=R/'Art/PlayerPhase1/PlaytestReRig'","O=R/'Art/PlaytestPolish/Hands'").replace('Player_C02_ReRig','Player_C02_GripA')
exec(compile(source,'grip_a_roundtrip_kernel','exec'))
report={'status':'PASS_FBX_MARKER_VERTEX_CONTACT'if worst<=.0005 and len(rotations)==15 else'FAIL_FBX_MARKER_VERTEX_CONTACT',
 'maximum_vertex_penetration_m':worst,'samples':count,'right_finger_markers':len(rotations),'bones':len(r.data.bones),
 'scope':'Blender blank-scene FBX reimport with actual brush and imported local pose markers. Unity skin/contact remains a separate check.'}
(O/'Validation/fbx_roundtrip.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
