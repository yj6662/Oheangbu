"""Merge separately reviewed body repairs and pack, enforce the LOD0 budget.
Keeps every source branch and all production animation data untouched.
"""
import bpy,json,math,hashlib,bmesh
from pathlib import Path
from mathutils import Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');ART=ROOT/'Art/PlayerV2'
base=ART/'DosaV2_Controls.blend'
if not base.exists():base=ART/'DosaV2_PackIntegrated.blend'
bpy.ops.wm.open_mainfile(filepath=str(base));s=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
report={'status':'ASSEMBLED_PENDING_COMPLETE_RIG_REVIEW','base':str(base),'replaced':[],'budget_reductions':[]}
lower=ART/'DosaV2_LowerRepair.blend'
names=json.loads((ART/'Inspect/LowerRepair/lower-repair-report.json').read_text())['changed_objects']
def hand_hash():
    o=bpy.data.objects['DosaV2_Hands'];return hashlib.sha256(json.dumps([[list(v.co),[(g.group,g.weight) for g in v.groups]] for v in o.data.vertices]).encode()).hexdigest()
for name in names:
    obj=bpy.data.objects.get(name)
    if obj:bpy.data.objects.remove(obj,do_unlink=True)
with bpy.data.libraries.load(str(lower),link=False) as (src,dst):dst.objects=names
for obj in dst.objects:
    assert obj is not None
    s.collection.objects.link(obj);obj.hide_set(False);obj.hide_render=False
    obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)
    for mod in obj.modifiers:
        if mod.type=='ARMATURE':mod.object=rig
    for index,material in enumerate(obj.data.materials):
        if material.name.startswith('Material_0.'):obj.data.materials[index]=bpy.data.materials['Material_0']
        elif material.name.startswith('DosaV2_InnerLinen.'):obj.data.materials[index]=bpy.data.materials['DosaV2_InnerLinen']
    report['replaced'].append(obj.name)

# The separately reviewed hand branch owns both reconstructed skin and wrist cloth.
# Do not carry the old hand through a new sleeve, or reduce contact geometry below.
hand_source=ART/'DosaV2_HandsArmsFinal.blend'
assert hand_source.exists(), 'The reviewed hand branch must be present.'
assert hashlib.sha256(hand_source.read_bytes()).hexdigest()=='8858bbd515872c5707864b9940ccd253bd500b6033fd07415db7264166d5ed2b'
hand_names=['DosaV2_Hands','DosaV2_SleeveInner_L','DosaV2_SleeveInner_R',
            'DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
for name in hand_names:
    old=bpy.data.objects.get(name)
    if old:bpy.data.objects.remove(old,do_unlink=True)
with bpy.data.libraries.load(str(hand_source),link=False) as (src,dst):dst.objects=hand_names
for obj in dst.objects:
    assert obj is not None
    s.collection.objects.link(obj);obj.hide_set(False);obj.hide_render=False
    obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)
    for mod in obj.modifiers:
        if mod.type=='ARMATURE':mod.object=rig
    for index,material in enumerate(obj.data.materials):
        canonical=material.name.rsplit('.',1)[0] if material.name.rsplit('.',1)[-1].isdigit() else material.name
        if canonical in bpy.data.materials:obj.data.materials[index]=bpy.data.materials[canonical]
    bm=bmesh.new();bm.from_mesh(obj.data)
    wires=[e for e in bm.edges if not e.link_faces]
    assert not wires, (obj.name,'Final reviewed branch must contain no wire edges.')
    bm.free()
    report['replaced'].append(obj.name)
report['hand_source']=str(hand_source)
report['hand_source_sha256']=hashlib.sha256(hand_source.read_bytes()).hexdigest()
before=hand_hash()

cloth_handoff=json.loads((ART/'Inspect/ClothBlender/Topology5mm/final-cloth-handoff.json').read_text())
cloth_source=Path(cloth_handoff['source'])
assert hashlib.sha256(cloth_source.read_bytes()).hexdigest()==cloth_handoff['source_sha256']
cloth_names=[item['name'] for item in cloth_handoff['mergeObjects']]
for obj in list(s.objects):
    if obj.name in cloth_names or obj.name.startswith('DosaV2_Robe_'):bpy.data.objects.remove(obj,do_unlink=True)
with bpy.data.libraries.load(str(cloth_source),link=False) as (src,dst):dst.objects=cloth_names
for obj in dst.objects:
    assert obj is not None
    s.collection.objects.link(obj);obj.hide_set(False);obj.hide_render=False
    obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)
    for mod in obj.modifiers:
        if mod.type=='ARMATURE':mod.object=rig
    for index,material in enumerate(obj.data.materials):
        canonical=material.name.rsplit('.',1)[0] if material.name.rsplit('.',1)[-1].isdigit() else material.name
        if canonical in bpy.data.materials:obj.data.materials[index]=bpy.data.materials[canonical]
    report['replaced'].append(obj.name)
report['cloth_source_sha256']=cloth_handoff['source_sha256']
report['cloth_renderers']=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']

def merge_reviewed_meshes(source, sha256, mesh_names, role, additions=()):
    source=Path(source)
    recorded_names=list(mesh_names)
    assert hashlib.sha256(source.read_bytes()).hexdigest()==sha256, role+' source changed'
    for name in mesh_names:
        old=bpy.data.objects.get(name)
        assert (old is None) == (name in additions), (name, 'Unexpected add/replace state')
        if old is not None:bpy.data.objects.remove(old,do_unlink=True)
    with bpy.data.libraries.load(str(source),link=False) as (src,dst):dst.objects=mesh_names
    for obj in dst.objects:
        assert obj is not None
        s.collection.objects.link(obj);obj.hide_set(False);obj.hide_render=False
        obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)
        for mod in obj.modifiers:
            if mod.type=='ARMATURE':mod.object=rig
        for index,material in enumerate(obj.data.materials):
            canonical=material.name.rsplit('.',1)[0] if material.name.rsplit('.',1)[-1].isdigit() else material.name
            if canonical in bpy.data.materials:obj.data.materials[index]=bpy.data.materials[canonical]
        report['replaced'].append(obj.name)
    report[role]={'source':str(source),'sha256':sha256,'meshes':recorded_names}

lining=json.loads((ART/'Inspect/ArmLiningTransition/handoff-integrity.json').read_text())
assert lining['protectedMeshesUnchanged'] and lining['restBonesAndParentsUnchanged']
merge_reviewed_meshes(lining['derivative'],lining['derivativeSha256'],lining['changedMeshes'],'arm_lining_transition')
hand_fold_handoff=json.loads((ART/'Inspect/HandsSelfFolds/final-handoff.json').read_text())
assert hand_fold_handoff['validation']['staticPoseCount']==22
for index, derivative in enumerate(hand_fold_handoff['frozen']):
    assert derivative['actions']==0
    merge_reviewed_meshes(derivative['path'],derivative['sha256'],list(derivative['mergeOnly']),
                          'hands_self_fold' if index==0 else 'wrapping_forearm_binding')
def count(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
meshes=[o for o in s.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
report['triangles_before_budget']=sum(count(o) for o in meshes)
# Concentrate the reduction in the broad torso/belt surface and concealed trouser shell.
# Never reduce the reconstructed hands, face, joints or brush to conceal grip errors.
for name,target in [('DosaV2_BodyCore',6000),('DosaV2_TrousersUnderShell',7474)]:
    total=sum(count(o) for o in meshes)
    if total<=74500:break
    obj=bpy.data.objects[name];n=count(obj)
    desired=target if target is not None else max(2500,n-(total-74500)-20)
    if desired>=n:continue
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('FinalBodyBudget','DECIMATE');mod.ratio=desired/n
    # Operate in rest space, before skinning.
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for v in obj.data.vertices:
        values=sorted([(obj.vertex_groups[g.group].name,g.weight) for g in v.groups if g.weight>1e-8],key=lambda x:x[1],reverse=True)[:4]
        totalw=sum(w for name,w in values)
        assert totalw>0
        for group in obj.vertex_groups:group.remove([v.index])
        for name,w in values:obj.vertex_groups[name].add([v.index],w/totalw,'REPLACE')
    report['budget_reductions'].append({'mesh':obj.name,'before':n,'after':count(obj)})

# This repair was authored on the previously budgeted torso. Apply it after
# decimation so identical garment attachment boundaries keep coherent weights.
attachments=json.loads((ART/'Inspect/ClothBlender/SleeveAttachments/sleeve-attachment-report.json').read_text())
assert attachments['allRestPositionsTopologyUVWeightsAndBonesUnchanged'] and attachments['allOtherObjectsUnchanged']
merge_reviewed_meshes(attachments['output'],attachments['outputSha256'],
    ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],'semantic_sleeve_attachments')
merge_reviewed_meshes(ART/'Inspect/ClothBlender/UnderarmTailoring/DosaV2_GussetTailored.blend',
    '5fe134e4a4e4bbd93d19c31a12e8de4807c1b157ec3f5a55178136d74f03ce99',
    ['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],
    'shared_underarm_gusset')
merge_reviewed_meshes(ART/'Inspect/ClothBlender/FoldedGusset/DosaV2_FoldedGusset.blend',
    'af4fa998c6ccb5c0b058a430447bd86bce49b85a08ef8c33f1d7a41814bb8847',
    ['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R',
     'DosaV2_ArmLining_Left','DosaV2_ArmLining_Right'],
    'folded_underarm_gusset_and_joint_centered_lining')
merge_reviewed_meshes(ART/'Inspect/ClothBlender/GlobalGusset/DosaV2_GlobalClothSlack.blend',
    '30b849f9865bb7751b2206f052d52ae45e2a60214190f54b8429cf9791b48b18',
    ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],
    'global_fixed_seam_path_cloth_slack')
merge_reviewed_meshes(ART/'Inspect/ClothBlender/GlobalEnvelope/DosaV2_GlobalClothEnvelope.blend',
    '5eaa8b82a9d3e53b1d31ed323fc5f55f1b3b421400082fbda09a5cef687c19c2',
    ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],
    'complete_cloth_path_geometry_and_free_motion_envelope')
merge_reviewed_meshes(ART/'Inspect/ClothBlender/ShoulderComplete/DosaV2_ShoulderComplete_Candidate.blend',
    '8be2afa8ae8d0afd0710c7b812aadd6c0830d299203d8c4f5f4b7654c38b2c3e',
    ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R',
     'DosaV2_ShoulderLining_Left','DosaV2_ShoulderLining_Right'],
    'closed_shoulder_lining_and_local_seam_darts',
    additions=('DosaV2_ShoulderLining_Left','DosaV2_ShoulderLining_Right'))
merge_reviewed_meshes(ART/'Inspect/ClothBlender/TorsoSupportedPanel/SeamLineCandidate/DosaV2_FreeHemPanel_Candidate.blend',
    '93f74bc12a70b4d9b0960dcfd05894c7cec9c35bf623b301ef0e52cbf1d0587a',
    ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],
    'torso_supported_cloth_panel_actual_attachment_lines_and_free_outer_hems')
# Native topology-isolation candidate, NOT a visual/cloth/RIG_PASS approval.
# Rest nonadjacent folded-layer crossings remain in its explicit source report.
merge_reviewed_meshes(ART/'Inspect/ClothBlender/SingleLayerSleeve/DosaV2_SingleLayerSleeve.blend',
    'e9c8a2b9656968417d3aded71ce4cf78d39e104a19a700b231b34d2e3f2f9e8f',
    ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'],
    'intermediate_manifold_and_sliver_isolation_pending_native_and_visual_repair')
# Visible core draft: replace layered scan folds with one fabric sheet per sleeve.
# The preceding BodyCore already includes the sewn trim, so do not append it twice.
merge_reviewed_meshes(ART/'Inspect/ClothBlender/SheetCrossingRepair/DosaV2_SemanticOuterSheet_Handoff.blend',
    '65fedc6c0d4d1fcf192a334de944357b9f8fc4d89032e1e110841835250d4b91',
    ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'], 'single_sheet_sleeve_core_draft')
merge_reviewed_meshes(ART/'Inspect/WrappingBendRepair/DosaV2_WrappingBendCandidate.blend',
    'a9083c36947b7b9ac675c140efbedf6c161167c55dd30a9213aca2c05246a4f3',
    ['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'], 'wrist_wrapping_bend_preview')
waist_manifest=ART/'Inspect/WaistSideSplitd979/repair-merge-manifest.json'
assert hashlib.sha256(waist_manifest.read_bytes()).hexdigest()=='b88fc53417fd17cedbb11c39ad8cbe074b8f3eeab23a8b6e02de1b61d9a14ce5'
waist=json.loads(waist_manifest.read_text())
original_bones={b.name:(b.matrix_local.copy(),b.parent.name if b.parent else None,b.length) for b in rig.data.bones}
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for item in waist['mergeOnlyBones']:
    assert item['name'] not in rig.data.edit_bones
    bone=rig.data.edit_bones.new(item['name']);bone.matrix=Matrix(item['matrixRestRowMajor'])
    bone.length=item['lengthMeters'];bone.use_deform=True
    bone.parent=rig.data.edit_bones[item['parent']]
bpy.ops.object.mode_set(mode='OBJECT')
for name,(matrix,parent,length) in original_bones.items():
    bone=rig.data.bones[name]
    assert max(abs(matrix[r][c]-bone.matrix_local[r][c]) for r in range(4) for c in range(4))<1e-6
    assert (bone.parent.name if bone.parent else None)==parent and abs(bone.length-length)<1e-6
for name in waist['replaceOnlyObjects']:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
merge_reviewed_meshes(waist['candidate'],waist['candidateSha256'],waist['mergeOnlyObjects'],
    'split_waist_three_surface_anchors_pending_native_preview',additions=tuple(waist['mergeOnlyObjects']))
report['added_waist_bones']=[b['name'] for b in waist['mergeOnlyBones']]
# A small budget adjustment on the concealed trouser under-shell, never on hands or cloth.
obj=bpy.data.objects['DosaV2_TrousersUnderShell'];n=count(obj)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
mod=obj.modifiers.new('SplitPartsBudget','DECIMATE');mod.ratio=7420/n
while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
bpy.ops.object.modifier_apply(modifier=mod.name)
for v in obj.data.vertices:
    values=sorted([(obj.vertex_groups[g.group].name,g.weight) for g in v.groups if g.weight>1e-8],key=lambda x:x[1],reverse=True)[:4]
    totalw=sum(w for _,w in values);assert totalw>0
    for group in obj.vertex_groups:group.remove([v.index])
    for name,w in values:obj.vertex_groups[name].add([v.index],w/totalw,'REPLACE')
report['budget_reductions'].append({'mesh':obj.name,'before':n,'after':count(obj),'reason':'Independent waist parts budget; concealed under-shell only'})
meshes=[o for o in s.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
assert hand_hash()==before
report['hand_geometry_weights_unchanged']=True
report['triangles']=sum(count(o) for o in meshes);assert report['triangles']<=75000
report['production_actions']=len(bpy.data.actions);assert report['production_actions']==0
report['meshes']=[{'name':o.name,'triangles':count(o),'materials':[m.name for m in o.data.materials]} for o in meshes]
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'DosaV2_Assembled.blend'))
(ART/'Inspect/assembled-character.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k!='meshes'}))
