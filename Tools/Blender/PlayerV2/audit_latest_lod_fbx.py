"""Read-only audit of actual latest exported LOD skins, shapes, UVs and renders.

FBX import is isolated. Apply the source's exact skin deformation matrices to the
imported rest frames, so FBX bone-axis changes do not masquerade as retarget bugs.
"""
import bpy, ast, json, math, hashlib, shutil, sys
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from bpy_extras.object_utils import world_to_camera_view

ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';STAGE=ART/'Staging'
OUT=ART/'Inspect/LOD458ActualFBX/RefinedCapture';LOD_DIR=STAGE/'Character';LABELS=['lod0','lod1','lod2'];QUICK=False;BOOT_ONLY=False;ACCESSORIES_ONLY=False
SOURCE=ART/'DosaV2_Assembled.blend';DEFINITIONS=ART/'Validation/static-pose-definitions.json'
for argument in sys.argv:
    if argument.startswith('--source='):SOURCE=Path(argument.split('=',1)[1])
    if argument.startswith('--defs='):DEFINITIONS=Path(argument.split('=',1)[1])
    if argument.startswith('--out='):OUT=Path(argument.split('=',1)[1])
    if argument.startswith('--lod-dir='):LOD_DIR=Path(argument.split('=',1)[1])
    if argument.startswith('--labels='):LABELS=argument.split('=',1)[1].split(',')
    if argument=='--quick':QUICK=True
    if argument=='--boots-only':BOOT_ONLY=True
    if argument=='--accessories-only':ACCESSORIES_ONLY=True
OUT.mkdir(parents=True,exist_ok=True);(OUT/'Inputs').mkdir(exist_ok=True)
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def digest(v):return hashlib.sha256(json.dumps(v,sort_keys=True).encode()).hexdigest()
def hands_data(obj):
    me=obj.data;me.calc_loop_triangles();co=[obj.matrix_world@v.co for v in me.vertices]
    keys={k.name:[obj.matrix_world.to_3x3()@(p.co-me.vertices[i].co) for i,p in enumerate(k.data)] for k in me.shape_keys.key_blocks if k.name!='Basis'}
    return co,[tuple(t.vertices) for t in me.loop_triangles],keys

bpy.ops.wm.open_mainfile(filepath=str(ART/'DosaV2_HandsSelfFoldFinal.blend'))
f94=hands_data(bpy.data.objects['DosaV2_Hands'])
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
source_hash=sha(SOURCE)
definitions=json.loads(DEFINITIONS.read_text())
assert definitions['sourceSha256']==source_hash
rig=bpy.data.objects['DosaV2_Rig'];source_rig_world=rig.matrix_world.copy()
source_rest={b.name:b.matrix_local.copy() for b in rig.data.bones}
source_fingers={(side,finger):(source_rig_world@rig.data.bones[side+'Hand'+finger+'1'].head_local,
    source_rig_world@rig.data.bones[side+'Hand'+finger+'3'].tail_local) for side in ['Right','Left'] for finger in ['Thumb','Index','Middle','Ring','Pinky']}
source_hand=hands_data(bpy.data.objects['DosaV2_Hands'])
source_z=[(o.matrix_world@v.co).z for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface' for v in o.data.vertices]
baseline_height=max(source_z)-min(source_z);baseline_center=(0,0,(max(source_z)+min(source_z))*.5)
assert len(source_hand[0])==len(f94[0])
f94_delta=max((a-b).length for a,b in zip(source_hand[0],f94[0]))
f94_shape={n:max((a-b).length for a,b in zip(source_hand[2][n],f94[2][n])) for n in f94[2]}
hand_bvh=BVHTree.FromPolygons(source_hand[0],source_hand[1],all_triangles=True)
manifest=json.loads((STAGE/'texture-manifest.json').read_text())
mapping={x['renderer']:x['materials'] for x in manifest['rendererMaterials']}
pose_by_id={p['id']:p for p in definitions['poses']}
source_protected={o.name:sum(1 for _ in o.data.vertices) for o in bpy.context.scene.objects if o.type=='MESH' and o.name in ['DosaV2_Head','DosaV2_BodyCore']}
module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text())
function=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings')
exec(compile(ast.Module(body=[function],type_ignores=[]),'proper_triangle_function','exec'))
report={'scope':'Actual FBX round-trip diagnostic, not source blend renders. No original save, retargeted action, Unity call or rig gate approval.',
        'sourceSha256':source_hash,'f94SourceSha256':sha(ART/'DosaV2_HandsSelfFoldFinal.blend'),
        'assembledHandVsF94':{'basisMaxErrorM':f94_delta,'shapeDeltaMaxErrorsM':f94_shape},
        'poseDefinitionsSha256':sha(DEFINITIONS),'models':[],'renders':[],'rigPass':False}
with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[n for n in src.objects if n in ['DosaBrushV2_Handle','DosaBrushV2_Rig','GripSocket']]
for o in dst.objects:
    if o and not o.users_collection:bpy.context.scene.collection.objects.link(o)
bpy.context.view_layer.update();brush_grip=bpy.data.objects['GripSocket'];brush_handle=bpy.data.objects['DosaBrushV2_Handle']
matrix=brush_grip.matrix_world.inverted()@brush_handle.matrix_world;brush_handle.data.calc_loop_triangles()
shaft_points=[matrix@v.co for v in brush_handle.data.vertices];shaft_tris=[tuple(t.vertices) for t in brush_handle.data.loop_triangles]
shaft=BVHTree.FromPolygons(shaft_points,shaft_tris,all_triangles=True)
files=[('lod0','SM_DosaV2_Rigged.fbx'),('lod1','SM_DosaV2_LOD1.fbx'),('lod2','SM_DosaV2_LOD2.fbx')]
files=[p for p in files if p[0] in LABELS]
for label,name in files:
    source=LOD_DIR/name;target=OUT/'Inputs'/(sha(source)+'_'+name)
    if not target.exists():shutil.copy2(source,target)
    assert sha(target)==sha(source)

def materials():
    result={}
    for entry in manifest['materials']:
        m=bpy.data.materials.new('QA_'+entry['name']);m.use_nodes=True;bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');links=m.node_tree.links
        bs.inputs['Metallic'].default_value=entry.get('metallicFallback',0);bs.inputs['Roughness'].default_value=entry.get('roughnessFallback',.75)
        for channel,input_name in [('baseColor','Base Color'),('roughness','Roughness'),('metallic','Metallic'),('normal','Normal')]:
            if not entry.get(channel):continue
            path=STAGE/entry[channel];assert path.exists(),path
            image=bpy.data.images.load(str(path),check_existing=True);image.colorspace_settings.name='sRGB' if channel=='baseColor' else 'Non-Color'
            node=m.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
            if channel=='normal':
                normal=m.node_tree.nodes.new('ShaderNodeNormalMap');links.new(node.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bs.inputs[input_name])
            elif channel in ('metallic','roughness'):
                separate=m.node_tree.nodes.new('ShaderNodeSeparateColor');links.new(node.outputs['Color'],separate.inputs[0]);links.new(separate.outputs[entry.get(channel+'Channel',0)],bs.inputs[input_name])
            else:links.new(node.outputs['Color'],bs.inputs[input_name])
        result[entry['name']]=m
    return result

def signed_distance(point):
    distance=shaft.find_nearest(point)[3];radial=Vector((point.x,0,point.z));radius=radial.length
    if radius<1e-10:return -distance
    hit=shaft.ray_cast(Vector((0,point.y,0)),radial/radius,.15)
    inside=hit[0] is not None and radius<math.hypot(hit[0].x,hit[0].z)-1e-7
    return -distance if inside else distance

def actual_contact(hand,world,rest,triangles,desired):
    result={}
    for side,sign in [('Right',-1),('Left',1)]:
        inverse=(source_rig_world@desired[side+'BrushGrip']).inverted();ids=[i for i,p in enumerate(rest) if p.x*sign>.60]
        points={i:inverse@Vector(world[i]) for i in ids};distances={i:signed_distance(p) for i,p in points.items()};fingers={}
        for finger in ['Thumb','Index','Middle','Ring','Pinky']:
            start,end=source_fingers[(side,finger)];axis=end-start
            pool=[i for i in ids if sum(g.weight for g in hand.data.vertices[i].groups if hand.vertex_groups[g.group].name.startswith(side+'Hand'+finger))>.45 and (rest[i]-start).dot(axis)/axis.length_squared>.32]
            assert pool,(side,finger)
            index=min(pool,key=lambda i:distances[i]);gap=distances[index]
            fingers[finger]={'signedGapM':gap,'contactVertex':index,'positionInGrip':list(points[index])}
        maximum=max(0.,-min(distances.values()));samples=0
        for t in triangles:
            if not all(int(i) in points for i in t):continue
            p=[points[int(i)] for i in t];edge=max((p[i]-p[(i+1)%3]).length for i in range(3))
            if min(abs(distances[int(i)]) for i in t)>edge+.001:continue
            divisions=max(2,min(48,math.ceil(edge/.0005)))
            for a in range(divisions+1):
                for b in range(divisions+1-a):
                    point=p[0]*(a/divisions)+p[1]*(b/divisions)+p[2]*(1-(a+b)/divisions)
                    maximum=max(maximum,-signed_distance(point));samples+=1
        result[side]={'fingers':fingers,'maximumWholeSkinPenetrationM':maximum,'triangleSamples':samples,'sampleEdgeStepM':.0005,
                      'pass':maximum<=.0005 and all(-.0005<=fingers[f]['signedGapM']<=.0015 for f in ['Thumb','Index','Middle'])}
    return result

for label,name in files:
    original=LOD_DIR/name;fbx=OUT/'Inputs'/(sha(original)+'_'+name)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx),automatic_bone_orientation=False,use_anim=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    imported_rest={b.name:b.matrix_local.copy() for b in rig.data.bones}
    bone_order=sorted(rig.pose.bones,key=lambda p:len(p.parent_recursive))
    h=next(o for o in meshes if o.name=='DosaV2_Hands')
    model={'label':label,'file':str(original),'inputCopy':str(fbx),'sha256':sha(fbx),'boneCount':len(rig.data.bones),'productionActions':len(bpy.data.actions),
           'meshes':[],'shapeTransfer':{},'poses':[]}
    for ob in meshes:
        me=ob.data;me.calc_loop_triangles();uv=me.uv_layers[0].data if me.uv_layers else None
        weights=[sum(g.weight for g in v.groups) for v in me.vertices]
        item={'name':ob.name,'vertices':len(me.vertices),'triangles':len(me.loop_triangles),'uvLayers':[u.name for u in me.uv_layers],
              'nonfiniteUv':sum(not math.isfinite(x) for d in uv for x in d.uv) if uv else None,
              'maxWeightSumError':max([abs(w-1) for w in weights if w>0] or [0]),'maxInfluences':max([len(v.groups) for v in me.vertices] or [0]),
              'shapeNames':[k.name for k in me.shape_keys.key_blocks] if me.shape_keys else []}
        if ob.name.startswith('DosaV2_SleeveInner'):
            slot=next((i for i,m in enumerate(me.materials) if m and 'WristWrapping' in m.name),None)
            verts=set(i for p in me.polygons if p.material_index==slot for i in p.vertices) if slot is not None else set()
            values=[sum(g.weight for g in me.vertices[i].groups if ob.vertex_groups[g.group].name in ('LeftArm','RightArm')) for i in verts]
            item['wrappingBinding']={'materialSlot':slot,'vertices':len(verts),'maxUpperArmWeight':max(values or [0]),'upperArmWeightedVertices':sum(v>1e-6 for v in values)}
        model['meshes'].append(item)
    model['triangles']=sum(x['triangles'] for x in model['meshes'])
    actual_co,_,actual_keys=hands_data(h)
    nearest=[hand_bvh.find_nearest(v) for v in actual_co]
    model['handBasisNearestSourceMaxDistanceM']=max(x[3] for x in nearest)
    for key,source_deltas in source_hand[2].items():
        if key not in actual_keys:model['shapeTransfer'][key]={'missing':True};continue
        errors=[]
        for i,(p,n,tri,dist) in enumerate(nearest):
            a,b,c=source_hand[1][tri]
            expected=barycentric_transform(p,source_hand[0][a],source_hand[0][b],source_hand[0][c],source_deltas[a],source_deltas[b],source_deltas[c])
            errors.append((actual_keys[key][i]-expected).length)
        model['shapeTransfer'][key]={'missing':False,'maxDeltaErrorM':max(errors),'p99DeltaErrorM':float(np.percentile(errors,99)),
                                    'maxActualDeltaM':max(v.length for v in actual_keys[key]),'changedVertices':sum(v.length>1e-7 for v in actual_keys[key])}
    report['models'].append(model)
    (OUT/'report.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({'model':label,'triangles':model['triangles'],'shapeTransfer':model['shapeTransfer'],'handBasisDistanceM':model['handBasisNearestSourceMaxDistanceM']}),flush=True)
    mats=materials()
    for ob in meshes:
        original_slots=len(ob.data.materials);names=mapping.get(ob.name)
        assert names and len(names)==original_slots,(ob.name,original_slots,names)
        for i,n in enumerate(names):ob.data.materials[i]=mats[n]
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
    scene.world=bpy.data.worlds.new('FBXQAWorld');scene.world.use_nodes=True;bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.12,.12,.12,1);bg.inputs['Strength'].default_value=.5
    scene.view_settings.view_transform='AgX';scene.view_settings.exposure=0;scene.render.image_settings.file_format='PNG';scene.render.resolution_percentage=100
    for ln,loc,power,size in [('Key',(-3,-4,4),400,4),('Fill',(3,-1,2),220,3),('Rim',(1,3,3),330,3)]:
        d=bpy.data.lights.new('FBXQA'+ln,'AREA');d.energy=power;d.size=size;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1.1))-o.location).to_track_quat('-Z','Y').to_euler()
    cam=bpy.data.objects.new('FBXQACamera',bpy.data.cameras.new('FBXQACamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
    def pose(pid):
        definition=pose_by_id[pid];desired={n:Matrix(m) for n,m in definition['boneMatricesRigLocal'].items()}
        for p in bone_order:
            if p.name not in source_rest:continue
            deform=source_rig_world@desired[p.name]@source_rest[p.name].inverted()@source_rig_world.inverted()
            p.matrix=rig.matrix_world.inverted()@deform@rig.matrix_world@imported_rest[p.name]
            bpy.context.view_layer.update()
        for side in ('Right','Left'):h.data.shape_keys.key_blocks['GripPalmRelax_'+side].value=definition['handGripCorrectives'][side.lower()]
        bpy.context.view_layer.update()
        return desired
    def render(pid,view,center,offset,scale,size=(850,850)):
        # In arms-down pose the palm camera would otherwise sit inside the torso.
        # Preserve the same hand plus its actual cuff/sleeve; use the full model in
        # external views to assess all body/sleeve overlap separately.
        hand_side='Right' if view=='RightHand' else 'Left' if view=='LeftHand' else None
        for ob in meshes:
            ob.hide_render=bool(hand_side) and ob.name not in ['DosaV2_Hands','DosaV2_SleeveInner_'+hand_side[0],
                'DosaV2_SleeveOuter_'+hand_side[0],'DosaV2_ArmLining_'+hand_side]
            if view=='boot':ob.hide_render=ob.name not in ['DosaV2_BodyCore','DosaV2_TrousersSource','DosaV2_TrousersUnderShell']
            if view.startswith('boot_isolated_'):ob.hide_render=ob.name!='DosaV2_BodyCore'
        cam.location=Vector(center)+Vector(offset);cam.rotation_euler=(Vector(center)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=size
        path=OUT/(label+'_'+pid+'_'+view+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
        actual_pixels=None
        if view.startswith('actual_height_'):
            top=world_to_camera_view(scene,cam,Vector(baseline_center)+Vector((0,0,baseline_height*.5)))
            bottom=world_to_camera_view(scene,cam,Vector(baseline_center)-Vector((0,0,baseline_height*.5)))
            actual_pixels=abs(top.y-bottom.y)*size[1]
            assert abs(actual_pixels-int(view.rsplit('_',1)[1]))<.01
        report['renders'].append({'label':label,'pose':pid,'view':view,'path':str(path),'sha256':sha(path),'sourceFbxSha256':sha(fbx),'resolution':list(size),'orthoScale':scale,'isolatedHandAndSleeve':bool(hand_side),'actualBaselineHeightPixels':actual_pixels})
        (OUT/'report.json').write_text(json.dumps(report,indent=2))
    for pid in ['open_hand','grip_down','grip_up','combined_reach','shoulder_120','forearm_plus90','forearm_minus90','hip_flex','hip_flex_right','knee_flex','knee_flex_right','ankle_flex','ankle_flex_right']:
        if ACCESSORIES_ONLY and pid!='open_hand':continue
        if BOOT_ONLY and pid not in ['open_hand','knee_flex','knee_flex_right','ankle_flex','ankle_flex_right']:continue
        desired=pose(pid);entry={'id':pid,'handCorrectives':pose_by_id[pid]['handGripCorrectives']}
        if ACCESSORIES_ONLY:
            render(pid,'rear_pack',(0,.04,1.29),(.45,4,.05),.85)
            render(pid,'actual_height_120',baseline_center,(0,-5,0),baseline_height*1920/120,(1920,1080))
            render(pid,'actual_height_rear_120',baseline_center,(0,5,0),baseline_height*1920/120,(1920,1080))
            model['poses'].append(entry);continue
        if BOOT_ONLY:
            for side in (['Right','Left'] if pid=='open_hand' else ['Right' if pid.endswith('right') else 'Left']):
                foot=source_rig_world@desired[side+'Foot'];center=foot.translation
                render(pid,'boot_isolated_'+side+'_rear',center,(.55,3,.5),.34)
            model['poses'].append(entry);continue
        ev=h.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();me.calc_loop_triangles();co=np.array([list(ev.matrix_world@v.co) for v in me.vertices]);tris=np.array([tuple(t.vertices) for t in me.loop_triangles],dtype=np.int32)
        entry['handBoundsSizeM']=(co.max(axis=0)-co.min(axis=0)).tolist()
        if pid in ('grip_down','grip_up','combined_reach'):
            tree=BVHTree.FromPolygons(co.tolist(),tris.tolist(),all_triangles=True)
            pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(tris[a]).intersection(tris[b])],dtype=np.int32)
            hits=pairs[proper_crossings(co[tris[pairs[:,0]]],co[tris[pairs[:,1]]])].tolist() if len(pairs) else []
            entry['handProperSelfCrossings']={'count':len(hits),'trianglePairs':hits}
        if pid in ('grip_down','grip_up'):entry['actualFbxContact']=actual_contact(h,co,actual_co,tris,desired)
        entry['wrappingProperCrossings']={}
        for sleeve in [o for o in meshes if o.name in ['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R']]:
            evaluated=sleeve.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
            ps=np.array([list(evaluated.matrix_world@v.co) for v in mesh.vertices]);ts=np.array([tuple(t.vertices) for t in mesh.loop_triangles if 'WristWrapping' in sleeve.data.materials[mesh.polygons[t.polygon_index].material_index].name],dtype=int)
            tree=BVHTree.FromPolygons(ps.tolist(),ts.tolist(),all_triangles=True);pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(ts[a]).intersection(ts[b])],dtype=int)
            count=int(np.sum(proper_crossings(ps[ts[pairs[:,0]]],ps[ts[pairs[:,1]]]))) if len(pairs) else 0
            entry['wrappingProperCrossings'][sleeve.name]=count;evaluated.to_mesh_clear()
        ev.to_mesh_clear();model['poses'].append(entry)
        if pid in ('open_hand','grip_down','grip_up') and (not QUICK or pid=='grip_down'):
            for side in ('Right','Left'):
                hand_frame=source_rig_world@desired[side+'Hand'];q=hand_frame.to_quaternion();center=hand_frame@Vector((0,.048,-.008))
                render(pid,side+'Hand',center,-(q@Vector((0,0,1)))*.4+(q@Vector((1,0,0)))*.07,.245)
        elif pid=='combined_reach':render(pid,'upper',(0,-.05,1.16),(0,-4,.18),1.05)
        elif pid=='shoulder_120' and not QUICK:render(pid,'face',(0,-.02,1.55),(0,-4,.05),.60)
        elif pid in ('knee_flex','knee_flex_right') and not QUICK:
            side='Right' if pid.endswith('right') else 'Left';foot_frame=source_rig_world@desired[side+'Foot'];render(pid,'boot',foot_frame.translation,(-.65,-3,.4),.55)
    pose('open_hand')
    if not QUICK and not BOOT_ONLY and not ACCESSORIES_ONLY:
        render('open_hand','rear_pack',(0,.04,1.29),(.45,4,.05),.85)
    for px in ([] if QUICK or BOOT_ONLY or ACCESSORIES_ONLY else [240 if label=='lod1' else 120] if label!='lod0' else [240,120]):
        render('open_hand','actual_height_'+str(px),baseline_center,(0,-5,0),baseline_height*1920/px,(1920,1080))
    (OUT/'report.json').write_text(json.dumps(report,indent=2))
    assert sha(original)==sha(fbx),'Live FBX changed during its bounded audit; results remain bound to the input copy.'
report['status']='ACTUAL_FBX_MEASURED_AND_RENDERED_PENDING_MANUAL_CLASSIFICATION'
(OUT/'report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'models':[(m['label'],m['triangles']) for m in report['models']],'renders':len(report['renders'])}))
