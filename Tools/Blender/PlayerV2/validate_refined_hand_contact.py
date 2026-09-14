"""Dense actual-triangle contact regression for the isolated palm-weight refinement."""
import bpy,json,hashlib,importlib.util,sys
import numpy as np
from pathlib import Path
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves'
SOURCE=ART/'DosaV2_ArmsWrapped.blend';PREFIX='wrapped'
for arg in sys.argv:
    if arg.startswith('--source='):SOURCE=Path(arg.split('=',1)[1])
    if arg.startswith('--prefix='):PREFIX=arg.split('=',1)[1]
before=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
spec=importlib.util.spec_from_file_location('calibration',ROOT/'Tools/Blender/PlayerV2/calibrate_hands.py')
cal=importlib.util.module_from_spec(spec);spec.loader.exec_module(cal)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle']
local=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles()
tree=BVHTree.FromPolygons([local@v.co for v in handle.data.vertices],[t.vertices for t in handle.data.loop_triangles],all_triangles=True)
report={'status':'STATIC_CONTACT_REGRESSION_NOT_RIG_PASS','source_sha256':before,'sides':{},'actions':len(bpy.data.actions)}
for side in ['Right','Left']:
    key=None
    if hand.data.shape_keys:
        for name in ['GripPalmRelax_Right','GripPalmRelax_Left']:
            shape=hand.data.shape_keys.key_blocks.get(name)
            if shape:shape.value=1 if name.endswith(side) else 0
        key=hand.data.shape_keys.key_blocks.get('GripPalmRelax_'+side)
    fit=cal.Fit(side,rig,hand,tree)
    if key:
        fit.vertices[:,:3]=np.asarray([key.data[int(i)].co for i in fit.ids]);report['corrective_shapes_evaluated']=True
    values=np.asarray(json.loads((ART/'Calibration'/(side+'-parameters.json')).read_text())['parameters'])
    report['sides'][side]={'penDown':fit.actual(values)}
    released=values.copy();released[11:17]*=.9
    report['sides'][side]['penUp']=fit.actual(released)
report['all_primary_contacts_pass']=all(p['primary_contacts_pass'] for r in report['sides'].values() for p in r.values())
report['source_unchanged']=before==hashlib.sha256(SOURCE.read_bytes()).hexdigest()
(OUT/(PREFIX+'-dense-contact-validation.json')).write_text(json.dumps(report,indent=2))
print(json.dumps(report),flush=True)
