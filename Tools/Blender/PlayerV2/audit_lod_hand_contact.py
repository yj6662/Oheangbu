"""Actual triangle contact evidence for LOD hands; never save imported source."""
import bpy,json,sys,hashlib
import numpy as np
from pathlib import Path
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import calibrate_hands as cal
ART=cal.ART
DIRECTORY=ART/'Inspect/LODDeformationRepair/PosedTransfer'
for a in sys.argv:
    if a.startswith('--dir='):DIRECTORY=Path(a.split('=',1)[1])
report={'rigPass':False,'models':[]}
for level in [1,2]:
    source=DIRECTORY/f'DosaV2_LOD{level}.blend';bpy.ops.wm.open_mainfile(filepath=str(source))
    rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[name for name in src.objects if name in ['DosaBrushV2_Handle','GripSocket','DosaBrushV2_Rig']]
    for o in dst.objects:
        if o and not o.users_collection:bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update();grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle'];local=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles()
    tree=BVHTree.FromPolygons([local@v.co for v in handle.data.vertices],[tuple(t.vertices) for t in handle.data.loop_triangles],all_triangles=True)
    row={'lod':level,'blendSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sides':{}}
    for side in ['Right','Left']:
        for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
        for name in ['GripPalmRelax_Right','GripPalmRelax_Left']:hand.data.shape_keys.key_blocks[name].value=1 if name.endswith(side) else 0
        fit=cal.Fit(side,rig,hand,tree);key=hand.data.shape_keys.key_blocks['GripPalmRelax_'+side]
        fit.vertices[:,:3]=np.array([key.data[int(i)].co for i in fit.ids])
        values=np.array(json.loads((ART/'Calibration'/f'{side}-parameters.json').read_text())['parameters'])
        up=values.copy();up[11:17]*=.9
        row['sides'][side]={'down':fit.actual(values),'up':fit.actual(up)}
        print(json.dumps({'lod':level,'side':side,'result':row['sides'][side]}),flush=True)
    row['pass']=all(p['primary_contacts_pass'] for side in row['sides'].values() for p in side.values());report['models'].append(row)
    (DIRECTORY/'dense-hand-contact.json').write_text(json.dumps(report,indent=2))
report['allPass']=all(r['pass'] for r in report['models']);(DIRECTORY/'dense-hand-contact.json').write_text(json.dumps(report,indent=2))
