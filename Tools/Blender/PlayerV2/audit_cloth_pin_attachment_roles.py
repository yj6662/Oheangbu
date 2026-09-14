"""Read-only source-role audit of the worst GlobalEnvelope sewn path."""
import bpy,bmesh,json,math,hashlib,heapq,importlib.util
from pathlib import Path
from mathutils import Vector,Matrix,Euler
from mathutils.kdtree import KDTree
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2/Inspect/ClothBlender';SOURCE=ART/'GlobalEnvelope/DosaV2_GlobalClothEnvelope.blend';OUT=ART/'PinAttachmentAudit';OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig'];obj=bpy.data.objects['DosaV2_SleeveOuter_L'];obj.data.calc_loop_triangles()
for o in scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
spec=importlib.util.spec_from_file_location('pin_render',Path(__file__).with_name('build_brush.py'));helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
defs=json.loads((ART/'FoldedGusset/Inputs/static-pose-definitions.json').read_text())['poses']
authored=next(r for r in json.loads((ART/'SleeveAttachments/sleeve-attachment-report.json').read_text())['surfaces'] if r['mesh']==obj.name)
worst=json.loads((ART/'GlobalEnvelope/DenseData/DosaV2_SleeveOuter_L-429-all.json').read_text())['maxWorstPath'];path=worst['shortestRestPath']
def weights(o,i):return {o.vertex_groups[g.group].name:g.weight for g in o.data.vertices[i].groups}
def boundaries(o):
    bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table();ids={v.index for e in bm.edges if e.is_boundary for v in e.verts};bm.free();return ids
targets={}
for name in ['DosaV2_BodyCore','DosaV2_SleeveInner_L']:
    o=bpy.data.objects[name];o.data.calc_loop_triangles();ids=sorted(boundaries(o));kd=KDTree(len(ids))
    for i,index in enumerate(ids):kd.insert(o.data.vertices[index].co,i)
    kd.balance();tree=BVHTree.FromPolygons([v.co[:] for v in o.data.vertices],[list(t.vertices) for t in o.data.loop_triangles],all_triangles=True)
    targets[name]=(o,ids,kd,tree)
edges={tuple(sorted((a,b))) for t in obj.data.loop_triangles for a,b in zip(list(t.vertices),list(t.vertices[1:])+[t.vertices[0]])};adj=[[] for v in obj.data.vertices]
for a,b in edges:
    length=(obj.data.vertices[a].co-obj.data.vertices[b].co).length;adj[a].append((b,length));adj[b].append((a,length))
distance=[math.inf]*len(adj);queue=[]
for s in authored['actualAttachmentSeeds']:distance[s['sourceVertex']]=0;heapq.heappush(queue,(0,s['sourceVertex']))
while queue:
    d,i=heapq.heappop(queue)
    if d>distance[i]:continue
    for j,length in adj[i]:
        new=d+length
        if new<distance[j]:distance[j]=new;heapq.heappush(queue,(new,j))
boundary=boundaries(obj);rows=[]
for i in [26,325]:
    p=obj.data.vertices[i].co;neighbors={}
    for name,(target,ids,kd,tree) in targets.items():
        q,k,d=kd.find(p);hit=tree.find_nearest(p);vertex=ids[k]
        neighbors[name]={'nearestBoundaryVertex':vertex,'nearestBoundaryPoint':list(q),'boundaryDistanceMeters':d,'boundaryVertexWeights':weights(target,vertex),'surfaceDistanceMeters':hit[3],'nearestSurfacePoint':list(hit[0])}
    rows.append({'vertex':i,'restPoint':list(p),'sourceWeights':weights(obj,i),'maxDistanceMeters':obj.data.color_attributes['ClothMobility'].data[i].color[0],
      'isOpenSurfaceBoundary':i in boundary,'actualOriginalAttachmentSeed':next((s for s in authored['actualAttachmentSeeds'] if s['sourceVertex']==i),None),
      'triangleRestGeodesicToAnyActualAttachmentSeedMeters':distance[i],'insideOriginalAutomaticTorsoProtectionXLE22cm':abs(p.x)<=.22,'nearestOtherParts':neighbors,
      'incidentTriangles':[list(t.vertices) for t in obj.data.loop_triangles if i in t.vertices]})
report={'status':'PIN_ROLE_DIAGNOSIS_NO_PIN_OR_MODEL_MUTATION','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'sourceMesh':obj.name,'worstNecessaryPair':worst,'pins':rows,
 'originalAttachmentRule':'True shared boundary seed within6mm of BodyCore or SleeveInner; additionally an automatic |restX|<=.22m region preserved all old pins. Counts230/307 were never a user-defined seam count.',
 'interpretation':'325 is an exact recorded shared-cut attachment to SleeveInner_L vertex6. 26 is not a shared-boundary seed; geometry proximity and visible cut context must determine whether its automatic torso-side pin is legitimate. No removal or replacement has been applied.'}
for o in scene.objects:
    if o.type=='MESH':o.hide_render=o.name not in ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveInner_L','DosaV2_ArmLining_Left'];o.hide_set(False)
camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True;markers=[]
def material(name,color):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=.4;return m
mats=[material('Pin26_Magenta',(1,.025,.35)),material('Pin325_Green',(.025,1,.25)),material('Path_Yellow',(1,.7,.025))]
for name in ['rest','forearm_minus90']:
    for marker in markers:bpy.data.objects.remove(marker,do_unlink=True)
    markers=[]
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    d=next(d for d in defs if d['id']==name)
    for entry in d['boneRotations']:rig.pose.bones[entry['bone']].matrix_basis=Euler([math.radians(entry[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update();ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();points={i:ev.matrix_world@me.vertices[i].co for i in path};ev.to_mesh_clear()
    center=(points[26]+points[325])*.5;camera.location=center+Vector((.25,-2.,1.4));helper.aim(camera,center);camera.data.ortho_scale=.46
    direction=(camera.location-center).normalized();curve=bpy.data.curves.new('MeasuredRestPath','CURVE');curve.dimensions='3D';curve.bevel_depth=.0013;curve.bevel_resolution=1;spline=curve.splines.new('POLY');spline.points.add(len(path)-1)
    # Orthographic depth overlay: moving toward the camera keeps the measured
    # projected image coordinates while ensuring labels are not hidden by cloth.
    for item,index in zip(spline.points,path):item.co=(*(points[index]+direction*.30),1)
    line=bpy.data.objects.new('DiagnosticPath',curve);scene.collection.objects.link(line);line.data.materials.append(mats[2]);markers.append(line)
    for index,mat in zip([26,325],mats):
        p=points[index]+direction*.30;bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.0045,location=p);ball=bpy.context.object;ball.data.materials.append(mat);markers.append(ball)
        font=bpy.data.curves.new('PinLabel','FONT');font.body='PIN '+str(index);font.size=.017;font.align_x='CENTER';label=bpy.data.objects.new('DiagnosticLabel',font);scene.collection.objects.link(label);label.location=p+camera.rotation_euler.to_matrix()@Vector((0,.021,.007));label.rotation_euler=camera.rotation_euler;font.materials.append(mat);markers.append(label)
    scene.render.resolution_x=1100;scene.render.resolution_y=900;scene.render.resolution_percentage=100;file=OUT/(name+'-pin26-325.png');scene.render.filepath=str(file);bpy.ops.render.render(write_still=True)
    report.setdefault('renders',[]).append({'poseId':name,'path':str(file.relative_to(ROOT)),'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'actualPinPoints':{str(i):list(points[i]) for i in [26,325]},'annotationOffsetMeters':.30,'annotationMeaning':'Depth overlay along orthographic viewing direction: projected source points/path are exact, but visual markers are always shown in front of cloth.'})
(OUT/'pin-role-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2),flush=True)
