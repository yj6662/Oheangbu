"""Versioned, static LOD authoring for the assembled V2; no animation data."""
import bpy,json,sys,hashlib
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from cloth_export_channels import preserve_cloth_mobility
from lod_deformation_protection import structured_cuff,repair_hand_correctives,coarse_backpanel,decimate_hand_in_grip
ROOT=Path('C:/Users/yj666/Oheangbu');ART=ROOT/'Art/PlayerV2';STAGE=ART/'Staging/Character'
SOURCE=ART/'DosaV2_Assembled.blend'
OUTPUT=ART
REPORT=ART/'Inspect/character-lods.json'
LEVELS={1,2}
for argument in sys.argv:
    if argument.startswith('--levels='):LEVELS={int(x) for x in argument.split('=',1)[1].split(',')}
    if argument.startswith('--source='):SOURCE=Path(argument.split('=',1)[1])
    if argument.startswith('--out='):
        OUTPUT=Path(argument.split('=',1)[1]);OUTPUT.mkdir(parents=True,exist_ok=True)
        STAGE=OUTPUT;REPORT=OUTPUT/'character-lods.json'
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
assert LEVELS and LEVELS<={1,2},('Unsupported LOD selection',LEVELS)
# A one-level repair must not discard the other level's existing provenance.
reports=[]
if REPORT.exists() and LEVELS!={1,2}:
    reports=[r for r in json.loads(REPORT.read_text())['lods'] if r['lod'] not in LEVELS]
    assert all(r['sourceSha256']==source_hash for r in reports),'Retained LOD records belong to a different source; use a separate output directory.'
# Hands dominate LOD0 for the close drawing rig. At world LOD distances the
# face, boot silhouette and broad torso need more of the budget than finger folds.
PART_TARGETS={1:{'DosaV2_Hands':4000,'DosaV2_BodyCore':5500,'DosaV2_Head':2362},
              2:{'DosaV2_Hands':3100,'DosaV2_BodyCore':3900,'DosaV2_Head':1500,
                 'DosaV2_TrousersUnderShell':500,'DosaPackV2_Backpanel':250,
                 'DosaPackV2_BrushBundle':300,'DosaV2_Hair':550,'DosaV2_Hat':775}}
# The torso object also contains the visible boots. A final largest-mesh correction
# previously reduced its3300 target to1781, reproducing facial/cloth UV stretching
# and boot wedges in Blender before Unity. Protect these silhouette/identity parts.
PROTECTED={'DosaV2_Hands','DosaV2_BodyCore','DosaV2_Head','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R',
           'DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'}
CORRECTION_FLOORS={'DosaV2_TrousersUnderShell':400,'DosaPackV2_Backpanel':250,
                   'DosaPackV2_BrushBundle':250,'DosaV2_Hair':500,'DosaV2_Hat':775,
                   'DosaV2_Robe_Combined':1700,'DosaV2_SleeveInner_L':350,'DosaV2_SleeveInner_R':350}
RIGID_ENVELOPES={'DosaPackV2_Tube_Long_R','DosaPackV2_Tube_Long_L','DosaV2_WaistScroll_L',
                 'DosaV2_WaistUpperScroll_L','DosaV2_WaistTubeSide_L','DosaV2_WaistTubeBack_R',
                 'DosaV2_WaistTube_C','DosaV2_WaistPouch_R','DosaV2_WaistPouchBack_L'}
for name in RIGID_ENVELOPES:
    PART_TARGETS[2][name]=64
    CORRECTION_FLOORS[name]=48
def count(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def decimate(obj,ratio,label):
    if obj.name=='DosaV2_Hands':
        decimate_hand_in_grip(obj,ratio,label)
        return
    # Reduce Basis topology, then transfer each corrective delta barycentrically.
    # Applying a topology modifier directly to a keyed mesh is rejected by Blender.
    keys=obj.data.shape_keys;source=None;shapes=[]
    used_materials=sorted(set(p.material_index for p in obj.data.polygons))
    # QEM can erase a thin authored lining patch even when the material slot
    # remains in the datablock. Retain the preceding real surface in that case;
    # never manufacture a dummy triangle merely to satisfy an import slot count.
    material_backup=obj.data.copy() if len(used_materials)>1 and not keys else None
    if keys:
        obj.data.calc_loop_triangles()
        vertices=[p.co.copy() for p in keys.key_blocks[0].data]
        triangles=[tuple(t.vertices) for t in obj.data.loop_triangles]
        source=BVHTree.FromPolygons(vertices,triangles,all_triangles=True)
        shapes=[(key.name,[p.co.copy()-vertices[i] for i,p in enumerate(key.data)],key.value) for key in list(keys.key_blocks)[1:]]
        for key in keys.key_blocks:key.value=0.
        obj.shape_key_clear()
    mod=obj.modifiers.new(label,'DECIMATE');mod.ratio=ratio
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    if material_backup:
        reduced=obj.data
        if sorted(set(p.material_index for p in reduced.polygons))!=used_materials:
            attempts=json.loads(obj.get('LODMaterialReductionRejected','[]'))
            attempts.append({'operation':label,'ratio':ratio,'beforeTriangles':len(material_backup.loop_triangles),
                             'lostSlots':sorted(set(used_materials)-{p.material_index for p in reduced.polygons})})
            obj['LODMaterialReductionRejected']=json.dumps(attempts)
            obj.data=material_backup
            if reduced.users==0:bpy.data.meshes.remove(reduced)
        else:bpy.data.meshes.remove(material_backup)
    if source:
        obj.shape_key_add(name='Basis')
        nearest=[source.find_nearest(v.co) for v in obj.data.vertices]
        for name,deltas,value in shapes:
            key=obj.shape_key_add(name=name);key.value=value
            for i,v in enumerate(obj.data.vertices):
                point,normal,index,distance=nearest[i];assert index is not None
                a,b,c=triangles[index]
                key.data[i].co=v.co+barycentric_transform(point,vertices[a],vertices[b],vertices[c],deltas[a],deltas[b],deltas[c])
for level,budget in [(1,35000),(2,18000)]:
    if level not in LEVELS:continue
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE));s=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
    meshes=[o for o in s.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
    source_materials={o.name:{'slots':[m.name if m else None for m in o.data.materials],
                             'used':sorted(set(p.material_index for p in o.data.polygons))} for o in meshes}
    total=sum(count(o) for o in meshes);ratio=(budget-300)/total
    cuff_repairs=[];rigid_rebuilds=[]
    for obj in meshes:
        obj['LODLevel']=level
        if level==2 and obj.name in RIGID_ENVELOPES|{'DosaPackV2_BrushBundle'}:rigid_rebuilds.append(coarse_backpanel(obj))
        original=count(obj);target=PART_TARGETS[level].get(obj.name,max(16,int(original*ratio)))
        bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
        if obj.name in ('DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'):
            cuff_repairs.append(structured_cuff(obj,level,decimate))
        elif target<original:
            decimate(obj,target/original,'Authored_LOD'+str(level))
            if any(m.type=='ARMATURE' for m in obj.modifiers):
                for v in obj.data.vertices:
                    groups=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-8],key=lambda x:x[1],reverse=True)[:4]
                    weight=sum(w for g,w in groups);assert weight>0,(obj.name,v.index)
                    for group in obj.vertex_groups:group.remove([v.index])
                    for index,w in groups:obj.vertex_groups[index].add([v.index],w/weight,'REPLACE')
        for polygon in obj.data.polygons:polygon.use_smooth=True
        # The imported source contains custom loop normals. Their original
        # corner data is no longer a valid shading frame after strong collapse.
        if obj.data.has_custom_normals:
            obj.data.normals_split_custom_set([(0.,0.,0.)]*len(obj.data.loops))
    after=sum(count(o) for o in meshes)
    # Disconnected islands/capped seams can keep more triangles than the requested
    # Decimate ratio. Measure the actual result and spend the remaining reduction
    # on broad surfaces, with a bounded failure if the target is unreachable.
    adjustments=[]
    attempted=set()
    for attempt in range(len(meshes)):
        if after<=budget:break
        candidates=[o for o in meshes if o.name not in PROTECTED and o.name not in attempted
                    and count(o)>CORRECTION_FLOORS.get(o.name,max(16,int(count(o)*.7)))+4]
        if not candidates:break
        candidates.sort(key=lambda o:(o.name not in CORRECTION_FLOORS,
            -(count(o)-CORRECTION_FLOORS.get(o.name,max(16,int(count(o)*.7))))))
        obj=candidates[0];original=count(obj);attempted.add(obj.name)
        desired=max(CORRECTION_FLOORS.get(obj.name,max(16,int(original*.7))),original-(after-budget)-80)
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        decimate(obj,desired/original,'LOD_BudgetCorrection')
        for v in obj.data.vertices:
            values=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-8],key=lambda x:x[1],reverse=True)[:4]
            if not values:continue # bone-parent rigid geometry has no skin weights
            weight=sum(w for g,w in values)
            for group in obj.vertex_groups:group.remove([v.index])
            for index,w in values:obj.vertex_groups[index].add([v.index],w/weight,'REPLACE')
        adjustments.append({'mesh':obj.name,'before':original,'after':count(obj)})
        after=sum(count(o) for o in meshes)
    assert after<=budget,(level,after,budget,adjustments)
    hand_repair=repair_hand_correctives(bpy.data.objects['DosaV2_Hands'],rig)
    assert all(p['properCrossings']==0 for p in hand_repair['poses']+hand_repair['transition']),('LOD hand geometry gate failed',level,hand_repair)
    print(json.dumps({'lod':level,'triangles':after,'handInitial':hand_repair['initialGripCrossings'],'handFinal':hand_repair['finalGripCrossings'],'handRemaining':hand_repair['remainingDetails']}),flush=True)
    for obj in meshes:
        if obj.data.has_custom_normals:
            obj.data.normals_split_custom_set([(0.,0.,0.)]*len(obj.data.loops))
    assert len(bpy.data.actions)==0
    preserve_cloth_mobility(meshes)
    material_audit=[]
    for obj in meshes:
        original=source_materials[obj.name]
        slots=[m.name if m else None for m in obj.data.materials]
        used=sorted(set(p.material_index for p in obj.data.polygons))
        assert slots==original['slots'],(obj.name,'LOD material identity/order changed',original['slots'],slots)
        assert used==original['used'],(obj.name,'LOD lost an original used material slot',original['used'],used)
        obj.data.calc_loop_triangles()
        material_audit.append({'mesh':obj.name,'materialSlots':slots,'sourceUsedSlots':original['used'],
                               'lodUsedSlots':used,'trianglesPerSlot':[sum(t.material_index==i for t in obj.data.loop_triangles) for i in range(len(slots))],
                               'rejectedReductions':json.loads(obj.get('LODMaterialReductionRejected','[]'))})
    bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
    for o in meshes:o.select_set(True)
    target=STAGE/('SM_DosaV2_LOD'+str(level)+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(target),use_selection=True,object_types={'ARMATURE','MESH'},
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_mesh_modifiers=True,path_mode='STRIP',use_custom_props=True,colors_type='LINEAR')
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/('DosaV2_LOD'+str(level)+'.blend')))
    reports.append({'lod':level,'triangles':after,'budget':budget,'output':str(target),'budget_adjustments':adjustments,
        'partTargets':PART_TARGETS[level],'protectedParts':sorted(PROTECTED),
        'sourceSha256':source_hash,'cuffRepairs':cuff_repairs,'handCorrectiveRepair':hand_repair,
        'handReductionMethod':bpy.data.objects['DosaV2_Hands'].get('LODHandMethod'),
        'handOpenBasisRepair':json.loads(bpy.data.objects['DosaV2_Hands']['LODOpenBasisRepair']),
        'rigidRearRebuilds':rigid_rebuilds,
        'materialIdentityAudit':material_audit,
        'budgetPolicy':'Protected torso/boots, face and hands never receive a largest-mesh budget correction; named lower-priority targets/floors reduce internal/backpack surfaces first. Failure to meet the total budget remains a failure.',
        'shading':'smooth geometry-derived normals after all topology reductions',
        'mesh_counts':[{'name':o.name,'triangles':count(o)} for o in meshes],
        'native_cloth':'LOD0 only; lower LODs preserve skinned rest garment geometry and shared bone names.'})
    reports.sort(key=lambda r:r['lod'])
    REPORT.write_text(json.dumps({'status':'LOD_GEOMETRY_PENDING_IMPORT_VISUAL_REVIEW','lods':reports},indent=2))
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
REPORT.write_text(json.dumps({'status':'LOD_GEOMETRY_PENDING_IMPORT_VISUAL_REVIEW','lods':reports},indent=2))
print(json.dumps([{'lod':r['lod'],'triangles':r['triangles']} for r in reports]))
