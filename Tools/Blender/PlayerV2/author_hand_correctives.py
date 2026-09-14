"""Author rig corrective shapes; no Actions or production animation are created."""
import bpy,bmesh,json,hashlib
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves'
SOURCE=ART/'DosaV2_ArmsWrappedBudget.blend';BASIS=ART/'DosaV2_HandsBasis.blend'
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [SOURCE,BASIS]}
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
target=[v.co.copy() for v in hand.data.vertices]
with bpy.data.libraries.load(str(BASIS),link=False) as(src,dst):dst.objects=['DosaV2_Hands']
reference=dst.objects[0]
assert len(reference.data.vertices)==len(hand.data.vertices)
assert [tuple(p.vertices) for p in reference.data.polygons]==[tuple(p.vertices) for p in hand.data.polygons]
for v,b in zip(hand.data.vertices,reference.data.vertices):v.co=b.co
basis=hand.shape_key_add(name='Basis',from_mix=False)
for side,sign in [('Right',-1),('Left',1)]:
    key=hand.shape_key_add(name='GripPalmRelax_'+side,from_mix=False)
    for i,v in enumerate(hand.data.vertices):
        if v.co.x*sign>.60:key.data[i].co=target[i]
    key.value=0;key.slider_min=0;key.slider_max=1
hand['corrective_shape_contract']='GripPalmRelax_Right/Left: 0 for open rest hand, 1 for the calibrated brush grip; supporting ring/pinky pen-up release keeps 1.'
bpy.data.objects.remove(reference,do_unlink=True)
wire_removed=0
for name in ['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R']:
    obj=bpy.data.objects[name];bm=bmesh.new();bm.from_mesh(obj.data);edges=[e for e in bm.edges if e.is_wire];wire_removed+=len(edges)
    bmesh.ops.delete(bm,geom=edges,context='EDGES');bm.to_mesh(obj.data);bm.free()
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();bpy.ops.file.pack_all();output=ART/'DosaV2_HandsArmsFinal.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output))
report={'source':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'status':'CORRECTIVE_RIG_PENDING_STATIC_REVIEW_NOT_ANIMATION',
        'shapes':['Basis','GripPalmRelax_Right','GripPalmRelax_Left'],'default_shape_values':[0,0],
        'target_grip_matches_budget_sculpt_exactly':all((hand.data.shape_keys.key_blocks['GripPalmRelax_'+('Right' if v.x<0 else 'Left')].data[i].co-v).length==0 for i,v in enumerate(target)),
        'wire_edges_removed':wire_removed,'actions':len(bpy.data.actions),'sources_unchanged':all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in hashes.items())}
(OUT/'hand-corrective-authoring.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
