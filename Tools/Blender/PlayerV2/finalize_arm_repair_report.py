"""Read-only structural handoff for the isolated hand/forearm repair."""
import bpy,bmesh,json,hashlib,math
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves'
SOURCE=ART/'DosaV2_HandsArmsFinal.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
rig=bpy.data.objects['DosaV2_Rig']
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
names=['DosaV2_Hands','DosaV2_SleeveInner_L','DosaV2_SleeveInner_R','DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
def audit(obj):
    mesh=obj.data;mesh.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(mesh)
    result={'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),'materials':[m.name for m in mesh.materials],
        'boundary_edges':sum(e.is_boundary for e in bm.edges),'wire_edges':sum(e.is_wire for e in bm.edges),
        'zero_area_faces':sum(f.calc_area()<1e-12 for f in bm.faces),'all_faces_smooth':all(p.use_smooth for p in mesh.polygons),
        'max_skin_weights':max(len(v.groups) for v in mesh.vertices),'max_weight_sum_error':max(abs(1-sum(g.weight for g in v.groups)) for v in mesh.vertices),
        'finite_geometry':all(math.isfinite(x) for v in mesh.vertices for x in v.co),'uv_corners':len(mesh.uv_layers.active.data) if mesh.uv_layers.active else 0}
    bm.free();return result
report={'source':str(SOURCE),'sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'status':'STATIC_REPAIR_HANDOFF_NOT_RIG_PASS',
        'actions':len(bpy.data.actions),'merge_objects':{n:audit(bpy.data.objects[n]) for n in names}}
with bpy.data.libraries.load(str(ART/'DosaV2_Refined.blend'),link=False) as(src,dst):dst.objects=list(names)
baseline={name:audit(obj) for name,obj in zip(names,dst.objects)}
report['triangles_added_vs_refined']=sum(v['triangles'] for v in report['merge_objects'].values())-sum(v['triangles'] for v in baseline.values())
report['reference_triangles']={n:v['triangles'] for n,v in baseline.items()}
report['reference_near_21697_plus_this_delta']=21697+report['triangles_added_vs_refined']
report['new_material']={'name':'DosaV2_WristWrapping','metallic':0,'roughness':.94,'BaseColor':str(ART/'Staging/Character/WrappingTextures/T_DosaV2_Wrapping_BaseColor.png'),
                      'Normal':str(ART/'Staging/Character/WrappingTextures/T_DosaV2_Wrapping_Normal.png')}
report['constraints']=['Append only the five listed meshes into the assembled rig; reconnect their Armature modifiers to the existing DosaV2_Rig.',
 'Preserve Basis + GripPalmRelax_Right/Left blendshapes: value 0 for open hand, 1 for calibrated grip; pen-up retains 1 while ring/pinky joints relax 10%.',
 'Do not replace the assembled skeleton, backpack, robe components or lower repair with this older-body derivative.',
 'Primary thumb/index/middle contact tolerance remains <=1.5mm gap and <=0.5mm penetration; pen-up releases only supporting ring/pinky 10%.',
 'No actions or production animation clips were created. Unity rig acceptance is still a separate gate.']
(OUT/'arm-repair-handoff.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
