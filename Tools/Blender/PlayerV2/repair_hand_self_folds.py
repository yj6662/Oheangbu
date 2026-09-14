"""Contact-locked corrective refinement using actual proper triangle crossings."""
import bpy,json,math,hashlib,ast,heapq
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend';DEST=ART/'DosaV2_HandsSelfFolds.blend'
defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text());source_sha=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands'];keys=hand.data.shape_keys
hand.data.calc_loop_triangles();tri=np.asarray([t.vertices for t in hand.data.loop_triangles],dtype=int);n=len(hand.data.vertices);basis=np.asarray([v.co for v in keys.key_blocks['Basis'].data]);original=np.asarray([keys.key_blocks['GripPalmRelax_'+('Right' if v.co.x<0 else 'Left')].data[i].co for i,v in enumerate(hand.data.vertices)])
weights=[[(hand.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in hand.data.vertices]
neighbors=[set() for _ in range(n)]
for e in hand.data.edges:a,b=e.vertices;neighbors[a].add(b);neighbors[b].add(a)
def pose(pose_id):
 d=next(d for d in defs['poses'] if d['id']==pose_id)
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for side in ['Right','Left']:keys.key_blocks['GripPalmRelax_'+side].value=d.get('handGripCorrectives',{}).get(side.lower(),0.)
 for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def points():
 e=hand.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();p=np.asarray([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return p
def crossings(p):
 tree=BVHTree.FromPolygons(p.tolist(),tri.tolist(),all_triangles=True);pairs=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])]
 if not pairs:return np.empty((0,2),dtype=int)
 arr=np.asarray(pairs,dtype=int);return arr[proper_crossings(p[tri[arr[:,0]]],p[tri[arr[:,1]]])]
def set_corrective(rest):
 for i,p in enumerate(rest):keys.key_blocks['GripPalmRelax_'+('Right' if basis[i,0]<0 else 'Left')].data[i].co=p
 hand.data.update();bpy.context.view_layer.update()
def matrices():
 bone_m={b.name:np.asarray(rig.matrix_world@rig.pose.bones[b.name].matrix@b.matrix_local.inverted()@rig.matrix_world.inverted()@hand.matrix_world) for b in rig.data.bones}
 return np.asarray([sum((bone_m[name]*w for name,w in row),np.zeros((4,4))) for row in weights])
# Temporary actual brush import exists only to derive locked skin surface samples.
with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[name for name in src.objects if name.startswith('DosaBrushV2_') or name in ['GripSocket','TipSocket']]
brush_objects=[o for o in dst.objects if o]
for o in brush_objects:
 if not o.users_collection:bpy.context.scene.collection.objects.link(o)
grip=next(o for o in brush_objects if o.name=='GripSocket');handle=next(o for o in brush_objects if o.name=='DosaBrushV2_Handle');local=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles();shaft=BVHTree.FromPolygons([local@v.co for v in handle.data.vertices],[tuple(t.vertices) for t in handle.data.loop_triangles],all_triangles=True)
pose('grip_down');p=points();lock=np.zeros(n,dtype=bool)
for side,sign in [('Right',-1),('Left',1)]:
 inverse=(rig.matrix_world@rig.pose.bones[side+'BrushGrip'].matrix).inverted()
 for i in np.where(basis[:,0]*sign>.60)[0]:
  q=inverse@Vector(p[i]);distance=shaft.find_nearest(q)[3]
  if abs(q.y)<.095 and distance<.002:lock[i]=True
for _ in range(1):lock=np.asarray([lock[i] or any(lock[j] for j in neighbors[i]) for i in range(n)])
base_hits=crossings(p);seed={int(i) for a,b in base_hits for t in [a,b] for i in tri[t]};dist=np.full(n,np.inf);queue=[]
for i in seed:dist[i]=0;heapq.heappush(queue,(0.,i))
while queue:
 d,i=heapq.heappop(queue)
 if d>dist[i] or d>.025:continue
 for j in neighbors[i]:
  nd=d+float(np.linalg.norm(original[i]-original[j]))
  if nd<dist[j]:dist[j]=nd;heapq.heappush(queue,(nd,j))
amount=np.clip(1-dist/.025,0,1);amount=amount*amount*(3-2*amount);amount[lock]=0;amount[np.abs(basis[:,0])<.65]=0
movable=np.where(amount>0)[0];M=matrices();inverse=np.linalg.inv(M[:,:3,:3]);base=points();rest=original.copy();pose('grip_up');initial_up=len(crossings(points()));pose('grip_down');best=(len(base_hits)+initial_up,original.copy(),0);iterations=[]
def collision_delta(posed,hits):
 delta=np.zeros_like(posed);counts=np.zeros(n)
 for a,b in hits:
  ia,ib=tri[a],tri[b];ta,tb=posed[ia],posed[ib];ea=np.roll(ta,-1,axis=0)-ta;eb=np.roll(tb,-1,axis=0)-tb
  axes=[np.cross(ea[0],ea[1]),np.cross(eb[0],eb[1])]+[np.cross(x,y) for x in ea for y in eb]
  options=[]
  for axis in axes:
   length=np.linalg.norm(axis)
   if length<1e-12:continue
   axis=axis/length;pa=ta@axis;pb=tb@axis
   options.extend([(pb.max()-pa.min()+.00012,axis),(pa.max()-pb.min()+.00012,-axis)])
  distance,direction=min((o for o in options if o[0]>0),key=lambda o:o[0]);distance=min(distance,.0012)
  wa=float(amount[ia].mean());wb=float(amount[ib].mean());total=wa+wb
  if total<1e-8:continue
  for ids,weight,sign in [(ia,wa,1),(ib,wb,-1)]:
   for i in ids:
    if amount[i]<=0:continue
    delta[i]+=direction*distance*sign*weight/total*min(1,amount[i]/max(weight,1e-6));counts[i]+=1
 mask=counts>0;delta[mask]/=counts[mask,None];return delta
for iteration in range(1,161):
 posed=points();hits=crossings(posed);delta=collision_delta(posed,hits)
 for i in movable:delta[i]+=(np.mean(posed[list(neighbors[i])],axis=0)-posed[i])*.025*amount[i]
 change=np.einsum('vij,vj->vi',inverse,delta);proposed=rest+change
 displacement=proposed-original;length=np.linalg.norm(displacement,axis=1);displacement*=np.minimum(1,.006/np.maximum(length,1e-15))[:,None];rest=original+displacement;rest[lock]=original[lock]
 set_corrective(rest)
 if iteration%2:continue
 hits=crossings(points());up=0
 if len(hits)<=best[0]:
  pose('grip_up');up=len(crossings(points()));pose('grip_down')
  score=len(hits)+up
  if score<best[0] or score==best[0] and iteration<best[2]:best=(score,rest.copy(),iteration)
  if score==0:break
 iterations.append({'iteration':iteration,'gripDownCrossings':len(hits),'gripUpCrossingsWhenMeasured':up if len(hits)<=best[0] else None})
set_corrective(best[1]);results=[]
for pose_id in ['rest','open_hand','grip_down','grip_up','combined_reach']:
 pose(pose_id);hits=crossings(points());results.append({'poseId':pose_id,'properCrossingPairs':len(hits),'pairs':hits.tolist()})
changed=np.linalg.norm(best[1]-original,axis=1);assert np.max(changed[lock])==0
assert np.array_equal(np.asarray([v.co for v in keys.key_blocks['Basis'].data]),basis)
pose('rest')
for o in brush_objects:bpy.data.objects.remove(o,do_unlink=True)
assert len(bpy.data.actions)==0;bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'CONTACT_LOCKED_HAND_CORRECTIVE_CANDIDATE','sourceSha256':source_sha,'derivative':str(DEST),'derivativeSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'method':'Existing GripPalmRelax shapes only; minimum-separating-axis correction and gentle geodesic posed-space relaxation around actual proper crossing pairs, inverse-LBS displacement, original open Basis and all skin weights unchanged. Actual shaft vertices within2mm plus1neighbor ring locked; dense actual contact regression remains mandatory.',
 'protectedVertices':int(lock.sum()),'protectedIndices':np.where(lock)[0].tolist(),'movedVertices':int(np.sum(changed>1e-8)),'maximumCorrectiveDeltaMeters':float(changed.max()),'selectedIteration':best[2],
 'iterations':iterations,'results':results,'productionActions':0,'rigGate':'NOT_GRANTED','contactValidation':'PENDING_ACTUAL_DENSE_SHAFT','visibleReview':'PENDING'}
(OUT/'repair-report.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k not in ['iterations','protectedIndices']},indent=2))
