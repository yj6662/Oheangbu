"""Five actual static-pose before/after renders and exact local closed-volume tests."""
import bpy,bmesh,json,math,hashlib,importlib.util,argparse,sys
from pathlib import Path
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/ArmLiningClearance';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend';DEST=ART/'DosaV2_ArmLiningClearance.blend'
parser=argparse.ArgumentParser();parser.add_argument('--derivative');parser.add_argument('--out');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
if args.derivative:DEST=Path(args.derivative).resolve()
if args.out:OUT=Path(args.out).resolve();OUT.mkdir(parents=True,exist_ok=True)
names=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right'];cases=['rest_settle','grip_settle','elbow_120','forearm_minus90','forearm_plus90']
definitions=json.loads((ART/'Validation/static-pose-definitions.json').read_text())
spec=importlib.util.spec_from_file_location('cloth_fixture',Path(__file__).with_name('diagnose_cloth_blender.py'));fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def eval_mesh(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();points=[e.matrix_world@v.co for v in m.vertices];tri=[tuple(t.vertices) for t in m.loop_triangles];e.to_mesh_clear();return points,tri
directions=[Vector(v).normalized() for v in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def inside(tree,point):
 votes=[]
 for direction in directions:
  count=0;origin=point+direction*.000001
  for _ in range(64):
   hit=tree.ray_cast(origin,direction,10)
   if hit[0] is None:break
   count+=1;origin=hit[0]+direction*.000005
  votes.append(count%2)
 return sum(votes)>=2
report={'status':'MEASURED_FIVE_POSE_LOCAL_CANDIDATE','sourceSha256':digest(SOURCE),'derivativeSha256':digest(DEST),'poseIds':cases,'samples':[],'renders':[],
 'method':'Actual evaluated LBS lining and sleeve vertices, every UNorm8-zero sleeve pin against its same-side closed lining, three-ray majority parity. Five real static snapshots, no production actions or Cloth solve.',
 'globalSkinSelfIntersectionCount':None,'globalClothIntersectionCount':None,'manualReview':None}
for variant,path in [('before',SOURCE),('after',DEST)]:
 bpy.ops.wm.open_mainfile(filepath=str(path));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
 for obj in scene.objects:
  if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:obj['AuthoringMode']=False;obj.update_tag()
  if obj.type=='MESH':obj.hide_render=not obj.name.startswith(('DosaV2_','DosaPackV2_')) or obj.name=='DosaV2_SourceSurface'
 for o in list(scene.objects):
  if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
 scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=True;scene.world.color=(.12,.12,.12)
 scene.render.resolution_x=1050;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 for name,location,energy,size in [('Key',(-3,-4,4),400,4),('Fill',(3,-1,2),220,3),('Rim',(1,3,3),330,3)]:
  d=bpy.data.lights.new('ArmLiningQA'+name,'AREA');d.energy=energy;d.shape='DISK';d.size=size;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=location;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
 camera=bpy.data.objects.new('ArmLiningQACamera',bpy.data.cameras.new('ArmLiningQACamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO'
 for case in cases:
  for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
  if case.endswith('_settle'):fixture.direct_pose(rig,case)
  else:
   d=next(d for d in definitions['poses'] if d['id']==case)
   for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
  bpy.context.view_layer.update()
  sample={'variant':variant,'poseId':case,'linings':[]}
  for side in ['Left','Right']:
   obj=bpy.data.objects['DosaV2_ArmLining_'+side];points,tri=eval_mesh(obj);tree=BVHTree.FromPolygons(points,tri,all_triangles=True)
   bm=bmesh.new();bm.from_mesh(obj.data);boundary=sum(e.is_boundary for e in bm.edges);nonmanifold=sum(not e.is_manifold for e in bm.edges);bm.free()
   minarea=min((points[b]-points[a]).cross(points[c]-points[a]).length*.5 for a,b,c in tri)
   volume=sum(points[a].dot(points[b].cross(points[c])) for a,b,c in tri)/6.
   sleeve=bpy.data.objects['DosaV2_SleeveOuter_'+side[0]];sleevepoints,_=eval_mesh(sleeve);pins=[]
   for i,color in enumerate(sleeve.data.color_attributes['ClothMobility'].data):
    if round(color.color[0]*255)!=0:continue
    loc,n,t,d=tree.find_nearest(sleevepoints[i]);ins=inside(tree,sleevepoints[i]);pins.append({'index':i,'distanceMeters':d,'inside':ins,'signedMeters':-d if ins else d})
   sample['linings'].append({'name':obj.name,'boundaryEdges':boundary,'nonManifoldEdges':nonmanifold,'minimumTriangleArea':minarea,'evaluatedSignedVolume':volume,'checkedPins':len(pins),'insidePins':sum(p['inside'] for p in pins),'minimumPinClearanceMeters':min(p['signedMeters'] for p in pins),'pins':pins})
   # Exact posed cross section at 63% of the upper arm, where modified points lie.
   a=rig.matrix_world@rig.pose.bones[side+'Arm'].head;b=rig.matrix_world@rig.pose.bones[side+'ForeArm'].head;center=a+(b-a)*.63;axis=(b-a).normalized();basis=axis.cross(Vector((0,0,1))).normalized();up=axis.cross(basis).normalized();layers=[]
   for mesh_obj in [obj,sleeve]:
    pnts,tris=eval_mesh(mesh_obj);segments=[]
    for ids in tris:
     pp=[pnts[i] for i in ids];cross=[]
     for j in range(3):
      p,q=pp[j],pp[(j+1)%3];dp=(p-center).dot(axis);dq=(q-center).dot(axis)
      if dp*dq<0:
       hit=p+(q-p)*(dp/(dp-dq));cross.append([(hit-center).dot(basis),(hit-center).dot(up)])
     if len(cross)==2:segments.append(cross)
    layers.append({'mesh':mesh_obj.name,'segments':segments})
   sample['linings'][-1]['section']={'center':list(center),'axis':list(axis),'layers':layers}
  report['samples'].append(sample)
  # Visible bilateral cuffs/upper arms. Camera and original materials are identical for each pair.
  centers=[rig.matrix_world@rig.pose.bones[s+'ForeArm'].head for s in ['Left','Right']];target=(centers[0]+centers[1])*.5
  camera.location=target+Vector((.25,-3,.30));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=1.12
  file=OUT/(variant+'_'+case+'.png');scene.render.filepath=str(file);bpy.ops.render.render(write_still=True);report['renders'].append({'variant':variant,'poseId':case,'path':str(file),'sha256':digest(file)})
  (OUT/'five-pose-validation.json').write_text(json.dumps(report,indent=2))
assert digest(SOURCE)==report['sourceSha256'] and digest(DEST)==report['derivativeSha256']
print(json.dumps({'samples':len(report['samples']),'renders':len(report['renders']),'after':[{'pose':s['poseId'],'lining':l['name'],'inside':l['insidePins'],'gap':l['minimumPinClearanceMeters']} for s in report['samples'] if s['variant']=='after' for l in s['linings']]},indent=2))
