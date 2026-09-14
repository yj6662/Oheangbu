"""Half-budget render derivatives. Original source and native world Cloth topology stay unchanged."""
import bpy, json, sys, hashlib
from pathlib import Path
from mathutils import Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
from lod_deformation_protection import decimate_hand_in_grip
from cloth_export_channels import preserve_cloth_mobility

ROOT=Path('C:/Users/yj666/Oheangbu')
ART=ROOT/'Art/PlayerV2'
OUT=ART/'HalfPoly'
SOURCE=ART/'DosaV2_Assembled.blend'
OUT.mkdir(parents=True,exist_ok=True)
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
CLOTH={'DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'}
HANDS='DosaV2_Hands'
report={'status':'WORKING','source':str(SOURCE),'sourceSha256':source_hash,'method':'Per-part QEM, grip-aware hand reduction and preserved world Cloth. Not full manual quad retopology.','levels':[]}

def count(o): o.data.calc_loop_triangles(); return len(o.data.loop_triangles)
def select(o):
    bpy.ops.object.select_all(action='DESELECT');o.hide_set(False);o.select_set(True);bpy.context.view_layer.objects.active=o
def normalize(o):
    if not any(m.type=='ARMATURE' for m in o.modifiers):return
    for v in o.data.vertices:
        groups=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-8],key=lambda x:-x[1])[:4]
        total=sum(w for _,w in groups)
        assert total>0,(o.name,v.index,'unweighted')
        for group in o.vertex_groups:group.remove([v.index])
        for index,w in groups:o.vertex_groups[index].add([v.index],w/total,'REPLACE')
def reduce(o,target):
    before=count(o)
    if target>=before:return
    select(o)
    if o.name==HANDS:
        o['LODLevel']=1
        decimate_hand_in_grip(o,target/before,'HalfPoly_ProtectedGrip')
        repair=json.loads(o['LODOpenBasisRepair'])
        assert repair['finalCrossings']==0,repair
    else:
        assert o.data.shape_keys is None,(o.name,'unexpected corrective mesh')
        old=o.data.copy();slots={p.material_index for p in old.polygons}
        m=o.modifiers.new('HalfPoly_Reduction','DECIMATE');m.ratio=target/before;m.use_collapse_triangulate=True
        while o.modifiers.find(m.name)>0:bpy.ops.object.modifier_move_up(modifier=m.name)
        bpy.ops.object.modifier_apply(modifier=m.name)
        if {p.material_index for p in o.data.polygons}!=slots:
            reduced=o.data;o.data=old
            if reduced.users==0:bpy.data.meshes.remove(reduced)
            print('Preserved material patches: '+o.name,flush=True)
        else:bpy.data.meshes.remove(old)
    normalize(o)
    for p in o.data.polygons:p.use_smooth=True
    if o.data.has_custom_normals:o.data.normals_split_custom_set([(0,0,0)]*len(o.data.loops))
    print(json.dumps({'mesh':o.name,'before':before,'after':count(o)}),flush=True)

def export(label,rig,meshes):
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    rig.animation_data_clear()
    for o in meshes:
        if o.data.shape_keys:
            for k in o.data.shape_keys.key_blocks:k.value=0
    bpy.context.view_layer.update();preserve_cloth_mobility(meshes)
    bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
    for o in meshes:o.hide_set(False);o.select_set(True)
    path=OUT/('SM_DosaV2_Half_'+label+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
        use_armature_deform_only=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',use_mesh_modifiers=True,path_mode='STRIP',use_custom_props=True,colors_type='LINEAR')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('DosaV2_Half_'+label+'.blend')),compress=True)
    return str(path)

bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
rig=bpy.data.objects['DosaV2_Rig']
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
initial={o.name:count(o) for o in meshes}
targets={'DosaV2_Head':1800,'DosaV2_BodyCore':4200,'DosaV2_Hat':1600,'DosaV2_Hair':700,
         'DosaV2_TrousersUnderShell':1500,'DosaPackV2_Backpanel':900,HANDS:8750}
for o in sorted(meshes,key=lambda o:o.name):
    if o.name not in CLOTH:reduce(o,targets.get(o.name,max(12,int(initial[o.name]*.37))))
after=sum(count(o) for o in meshes)
for o in sorted(meshes,key=count,reverse=True):
    if after<=37800:break
    if o.name in CLOTH|{HANDS,'DosaV2_Head','DosaV2_BodyCore'}:continue
    old=count(o);reduce(o,max(24,int(old*.7)));after-=old-count(o)
assert after<=37800,('World budget not reached',after)
assert all(count(bpy.data.objects[n])==initial[n] for n in CLOTH)
path=export('World',rig,meshes)
report['levels'].append({'name':'World','beforeTriangles':sum(initial.values()),'triangles':after,'fbx':path,
    'parts':[{'name':o.name,'before':initial[o.name],'triangles':count(o)} for o in meshes],
    'handMethod':bpy.data.objects[HANDS]['LODHandMethod'],'handRepair':json.loads(bpy.data.objects[HANDS]['LODOpenBasisRepair']),
    'preservedWorldCloth':sorted(CLOTH)})
(OUT/'half-poly-report.json').write_text(json.dumps(report,indent=2))

# Reload the same source so first-person sleeves keep their own, milder reduction budget.
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
rig=bpy.data.objects['DosaV2_Rig']
near=[o for o in bpy.context.scene.objects if o.type=='MESH' and (o.name==HANDS or o.name.startswith(('DosaV2_Sleeve','DosaV2_ArmLining','DosaV2_ShoulderLining')))]
hand=bpy.data.objects[HANDS]
with bpy.data.libraries.load(str(OUT/'DosaV2_Half_World.blend'),link=False) as (src,dst):dst.objects=[HANDS]
reduced_hand=dst.objects[0]
assert [(g.index,g.name) for g in hand.vertex_groups]==[(g.index,g.name) for g in reduced_hand.vertex_groups]
hand.data=reduced_hand.data.copy()
near_initial={o.name:count(o) for o in near}
for o in near:
    if o.name!=HANDS:reduce(o,max(16,int(count(o)*.50)))
after=sum(count(o) for o in near)
assert after<=13500,('Near budget not reached',after)
path=export('Arms',rig,near)
report['levels'].append({'name':'Arms','referenceBeforeTriangles':25221,'triangles':after,'fbx':path,
    'parts':[{'name':o.name,'triangles':count(o)} for o in near]})
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
report['status']='BLENDER_REDUCED_PENDING_UNITY_AND_VISUAL_REVIEW'
(OUT/'half-poly-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'status':report['status'],'levels':[(r['name'],r['triangles']) for r in report['levels']]}),flush=True)
