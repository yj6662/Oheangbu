"""Read-only structural/UV audit of the isolated lower-body repair; no animation production."""
import bpy,bmesh,json,hashlib,importlib.util,sys
from pathlib import Path
from collections import Counter
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/LowerRepair'
spec=importlib.util.spec_from_file_location('lower_repair_implementation',ROOT/'Tools/Blender/PlayerV2/repair_lower_body.py')
impl=importlib.util.module_from_spec(spec);spec.loader.exec_module(impl)

def face_key(obj,p):
    uv=obj.data.uv_layers.active
    corners=[tuple(round(v,6) for v in obj.data.vertices[vi].co)+tuple(round(v,6) for v in uv.data[li].uv) for vi,li in zip(p.vertices,p.loop_indices)]
    return tuple(sorted(corners))

def upper(obj):
    weights={}
    for v in obj.data.vertices:
        if v.co.z<1.1:continue
        key=tuple(round(c,7) for c in v.co)
        weights[key]={obj.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-7}
    return weights

def rig_key():
    return {b.name:([round(x,7) for row in b.matrix_local for x in row],round(b.length,7),b.parent.name if b.parent else None) for b in bpy.data.objects['DosaV2_Rig'].data.bones}

bpy.ops.wm.open_mainfile(filepath=str(ART/'DosaV2_Refined.blend'))
baseline={}
for pose in ['lowered','step','squat']:
    impl.set_pose(bpy.data.objects['DosaV2_Rig'],pose);baseline[pose]=impl.lower_metrics()
impl.set_pose(bpy.data.objects['DosaV2_Rig'],'rest')
source_faces=Counter()
for obj in bpy.context.scene.objects:
    if obj.name=='DosaV2_BodyCore' or obj.name.startswith('DosaV2_Robe_'):
        source_faces.update(face_key(obj,p) for p in obj.data.polygons)
source_upper=upper(bpy.data.objects['DosaV2_BodyCore']);source_rig=rig_key()
upper_faces=Counter(face_key(bpy.data.objects['DosaV2_BodyCore'],p) for p in bpy.data.objects['DosaV2_BodyCore'].data.polygons if p.center.z>=1.1)
bpy.ops.wm.open_mainfile(filepath=str(ART/'DosaV2_LowerRepair.blend'))
target_faces=Counter();parts=[];bad_weights=[]
for obj in bpy.context.scene.objects:
    if obj.type!='MESH' or obj.name=='DosaV2_SourceSurface':continue
    if obj.name=='DosaV2_BodyCore' or obj.name=='DosaV2_TrousersSource' or obj.name.startswith('DosaV2_Robe_'):
        target_faces.update(face_key(obj,p) for p in obj.data.polygons)
    obj.data.calc_loop_triangles()
    parts.append({'name':obj.name,'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles)})
    for v in obj.data.vertices:
        groups=[g for g in v.groups if g.weight>1e-7]
        if len(groups)>4 or abs(sum(g.weight for g in groups)-1)>1e-5:bad_weights.append([obj.name,v.index,len(groups),sum(g.weight for g in groups)])
target_upper=upper(bpy.data.objects['DosaV2_BodyCore'])
maximum_weight_delta=0
for key,ww in source_upper.items():
    current=target_upper.get(key,{})
    for bone in ww.keys()|current.keys():maximum_weight_delta=max(maximum_weight_delta,abs(ww.get(bone,0)-current.get(bone,0)))
target_upper_faces=Counter(face_key(bpy.data.objects['DosaV2_BodyCore'],p) for p in bpy.data.objects['DosaV2_BodyCore'].data.polygons if p.center.z>=1.1)
shell=bpy.data.objects['DosaV2_TrousersUnderShell'];bm=bmesh.new();bm.from_mesh(shell.data)
shell_boundary_edges=sum(e.is_boundary for e in bm.edges);shell_nonmanifold_edges=sum(not e.is_manifold for e in bm.edges);bm.free()
report={
 'status':'STRUCTURAL_AUDIT_ONLY_NOT_RIG_PASS','actions':len(bpy.data.actions),'rig_rest_unchanged':source_rig==rig_key(),
 'bodycore_upper_vertex_positions_unchanged':source_upper.keys()==target_upper.keys(),
 'bodycore_upper_maximum_weight_delta':maximum_weight_delta,'bodycore_upper_source_triangles_and_uv_unchanged':upper_faces==target_upper_faces,
 'retained_source_polygons_with_changed_uv_or_position':sum((target_faces-source_faces).values()),
 'source_polygons_replaced_by_authored_pants':sum((source_faces-target_faces).values()),
 'retained_source_polygons':sum(target_faces.values()),'invalid_skin_weights':bad_weights[:30],'invalid_skin_weight_count':len(bad_weights),
 'authored_shell_boundary_edges':shell_boundary_edges,'authored_shell_nonmanifold_edges':shell_nonmanifold_edges,
 'parts':parts,'total_triangles_without_backpack':sum(p['triangles'] for p in parts),
 'limitations':['Static poses only. Native Unity Cloth collision/stability has not been exercised.',
                'The authored under-shell has two closed leg components below retained waist clothing.',
                'Cloth particle counts changed and must be regenerated from current vertex attributes.']}
(OUT/'lower-repair-structural-audit.json').write_text(json.dumps(report,indent=2))
repair_report=json.loads((OUT/'lower-repair-report.json').read_text())
repair_report['before']=baseline
repair_report['baseline_note']='Re-evaluated read-only against the unchanged source by audit_lower_repair.py; same static pose definitions and frame.'
(OUT/'lower-repair-report.json').write_text(json.dumps(repair_report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k not in ['parts','limitations','invalid_skin_weights']}),flush=True)
if '--render-final' in sys.argv:
    camera=bpy.data.objects['LowerRepairCamera'];bpy.context.scene.camera=camera
    for pose in ['step','squat']:
        impl.set_pose(bpy.data.objects['DosaV2_Rig'],pose)
        for view in ['front','quarter']:impl.render(camera,'after-'+pose,view)
