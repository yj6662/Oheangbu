"""Blender static asset evidence. Measures real geometry; manual defect review stays pending."""
import bpy,bmesh,json,math,hashlib,sys
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Euler
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2'
SOURCE=ART/'DosaV2_Assembled.blend';POSES=ART/'Validation/static-pose-definitions.json'
OUT=ART/'Inspect/StaticRigBlender';OUT.mkdir(parents=True,exist_ok=True)
source_sha=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
assert not bpy.data.objects['CTRL_DosaV2_Root']['AuthoringMode'];assert len(bpy.data.actions)==0
definitions=json.loads(POSES.read_text());assert definitions['sourceSha256']==source_sha
meshes=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
near=[o for o in meshes if o.name=='DosaV2_Hands' or o.name.startswith(('DosaV2_Sleeve','DosaV2_ArmLining'))]
for o in scene.objects:
    if o.type=='MESH':o.hide_render=o not in meshes
for o in meshes:o.hide_set(False)
report={'status':'MEASURED_CANDIDATE_MANUAL_REVIEW_PENDING','source':str(SOURCE.relative_to(ROOT)),'sourceSha256':source_sha,
    'poseDefinitionsSha256':hashlib.sha256(POSES.read_bytes()).hexdigest(),'toolVersion':bpy.app.version_string,
    'productionActions':0,'inventory':[],'variants':{},'samples':[],'renders':[],
    'unperformedChecks':{'triangleSelfIntersectionResolution':None,'skinVsClothPenetrationResolution':None,'manualArtReview':None},
    'scope':'Actual rest vertices, effective bone-parent/skinned influences, direct-pose evaluated vertices and bone lengths. Edge strain is diagnostic; material wrinkles and seam contacts need manual classification. No Cloth solve or production animation in this fixture.'}
rest_edges={};rest_bones={p.name:(p.tail-p.head).length for p in rig.pose.bones};root_rest=rig.pose.bones['Root'].matrix.translation.copy()
for o in meshes:
    o.data.calc_loop_triangles();weights=[];invalid=[];sum_error=0.;max_influences=0
    for v in o.data.vertices:
        groups=[(o.vertex_groups[g.group].name,float(g.weight)) for g in v.groups if g.weight!=0]
        if o.parent==rig and o.parent_type=='BONE':groups=[(o.parent_bone,1.)]
        if not groups or any(n not in rig.data.bones or w<0 or not math.isfinite(w) for n,w in groups):invalid.append(v.index)
        sum_error=max(sum_error,abs(sum(w for n,w in groups)-1));max_influences=max(max_influences,len(groups))
    coords=np.array([list(o.matrix_world@v.co) for v in o.data.vertices]);edges=np.array([list(e.vertices) for e in o.data.edges],dtype=int)
    lengths=np.linalg.norm(coords[edges[:,0]]-coords[edges[:,1]],axis=1)
    rest_edges[o.name]=(edges,lengths)
    bm=bmesh.new();bm.from_mesh(o.data)
    item={'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'boundaryEdges':sum(e.is_boundary for e in bm.edges),
        'nonManifoldNonBoundaryEdges':sum(not e.is_manifold and not e.is_boundary for e in bm.edges),
        'invalidWeightVertices':invalid,'maxWeightSumError':sum_error,'maxInfluences':max_influences,
        'rigidParentBone':o.parent_bone if o.parent_type=='BONE' else None,'materials':[m.name for m in o.data.materials],
        'zeroAreaTriangles':sum(t.area<1e-12 for t in o.data.loop_triangles),'nonFiniteRestVertices':int(np.sum(~np.isfinite(coords).all(axis=1)))}
    report['inventory'].append(item);bm.free()
for name,items in [('world',meshes),('near',near)]:
    names={o.name for o in items};inventory=[v for v in report['inventory'] if v['name'] in names]
    report['variants'][name]={'vertexCount':sum(v['vertices'] for v in inventory),'triangleCount':sum(v['triangles'] for v in inventory),
        'deformBoneCount':len(rig.data.bones),'objects':[o.name for o in items]}
brush_source=ART/'DosaBrushV2.blend'
with bpy.data.libraries.load(str(brush_source),link=False) as (src,dst):
    dst.objects=['DosaBrushV2_Rig','DosaBrushV2_Handle','DosaBrushV2_Bristles','GripSocket','TipSocket']
for obj in dst.objects:scene.collection.objects.link(obj)
brush=next(o for o in dst.objects if o.name=='DosaBrushV2_Rig')
brush_grip=next(o for o in dst.objects if o.name=='GripSocket')
brush_meshes=[o for o in dst.objects if o.type=='MESH']
bpy.context.view_layer.update();brush_grip_local=brush.matrix_world.inverted()@brush_grip.matrix_world
report['brushSourceSha256']=hashlib.sha256(brush_source.read_bytes()).hexdigest()
# Evidence lighting/materials are unchanged. Only an isolated diagnostic camera and lights are added.
scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True
scene.render.resolution_x=960;scene.render.resolution_y=960;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.world.color=(.12,.12,.12)
for o in list(scene.objects):
    if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
def area(name,location,power,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=location
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
area('EvidenceKey',(-3,-4,4),400,4);area('EvidenceFill',(3,-1,2),220,3);area('EvidenceRim',(1,3,3),330,3)
camera=bpy.data.objects.new('StaticEvidenceCamera',bpy.data.cameras.new('StaticEvidenceCamera'));scene.collection.objects.link(camera);scene.camera=camera
camera.data.type='ORTHO';camera.data.lens=50
def render(variant,pose,view,position,target,scale,region=None):
    if '--no-renders' in sys.argv:return
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
    path=OUT/(variant+'_'+pose+'_'+view+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
    report['renders'].append({'variant':variant,'poseId':pose,'view':view,'region':region,'path':str(path.relative_to(ROOT)).replace('\\','/'),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
for definition in definitions['poses']:
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    for side in ('Right','Left'):
        keys=bpy.data.objects['DosaV2_Hands'].data.shape_keys
        if keys and 'GripPalmRelax_'+side in keys.key_blocks:
            keys.key_blocks['GripPalmRelax_'+side].value=definition.get('handGripCorrectives',{}).get(side.lower(),0.)
    for entry in definition['boneRotations']:
        rig.pose.bones[entry['bone']].matrix_basis=Euler([math.radians(entry[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();geometry=[]
    show_brush=definition['id'] in ('grip_down','grip_up','wrist_flex','wrist_extend','combined_reach')
    brush.matrix_world=rig.matrix_world@rig.pose.bones['RightBrushGrip'].matrix@brush_grip_local.inverted()
    for obj in brush_meshes:obj.hide_render=not show_brush
    bpy.context.view_layer.update()
    max_bone=max(abs((p.tail-p.head).length-rest_bones[p.name]) for p in rig.pose.bones)
    root_delta=(rig.pose.bones['Root'].matrix.translation-root_rest).length
    for obj in meshes:
        evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh();coords=np.array([list(evaluated.matrix_world@v.co) for v in mesh.vertices]);evaluated.to_mesh_clear()
        edges,original=rest_edges[obj.name];current=np.linalg.norm(coords[edges[:,0]]-coords[edges[:,1]],axis=1)
        mask=original>1e-6;ratio=current[mask]/original[mask]
        geometry.append({'name':obj.name,'checkedVertices':len(coords),'nonFiniteVertices':int(np.sum(~np.isfinite(coords).all(axis=1))),
            'maximumEdgeStretchRatio':float(ratio.max()) if len(ratio) else 1,'edgesBelowOneMicrometer':int(np.sum(~mask))})
    for variant,items in [('world',meshes),('near',near)]:
        if variant=='near' and definition['id'].startswith(('hip_flex','knee_flex','ankle_flex')):continue
        names={o.name for o in items};sample={'variant':variant,'poseId':definition['id'],'checkedBones':len(rig.pose.bones),
            'maxBoneLengthDeltaMeters':max_bone,'rootDeltaMeters':root_delta,'geometry':[v for v in geometry if v['name'] in names],
            'invertedJointFaces':None,'unresolvedSelfIntersections':None,'reviewStatus':'PENDING_VISUAL_CLASSIFICATION'}
        sample['checkedVertices']=sum(v['checkedVertices'] for v in sample['geometry']);report['samples'].append(sample)
        for obj in meshes:obj.hide_render=obj not in items
        render(variant,definition['id'],'quarter',(2.6,-4,1.7),(0,0,.88 if variant=='world' else 1.30),1.95 if variant=='world' else 1.75)
        if variant=='world' and definition['id'].startswith(('hip_flex','knee_flex','ankle_flex')):
            render(variant,definition['id'],'leg_side',(-3,0,.55),(0,0,.52),1.18,'knees')
            render(variant,definition['id'],'leg_back_quarter',(-2,3,.8),(0,0,.52),1.18,'knees')
        if variant=='world' and definition['id']=='rest':
            for view,pos in [('front',(0,-4,1)),('back',(0,4,1)),('left',(4,0,1)),('right',(-4,0,1)),('top',(0,0,5)),('bottom',(0,0,-3))]:render(variant,'rest',view,pos,(0,0,.875),1.95)
            for region,center,size in [('armpits',(-.21,0,1.27),.46),('elbows',(-.44,0,1.32),.38),
                    ('inner_sleeves',(-.31,0,1.23),.5),('waist',(0,0,.90),.65),('knees',(-.1,0,.48),.48)]:
                render(variant,'rest',region,Vector(center)+Vector((-.25,-.7,.08)),center,size,region)
        if definition['id'] in ('grip_down','grip_up'):
            hand=rig.pose.bones['RightHand'].head;center=tuple(hand+Vector((-.045,0,0)))
            render(variant,definition['id'],'right_hand',Vector(center)+Vector((-.25,-.55,.17)),center,.35,'hands')
    (OUT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_sha;assert len(bpy.data.actions)==0
print(json.dumps({'status':report['status'],'samples':len(report['samples']),'renders':len(report['renders'])}))
