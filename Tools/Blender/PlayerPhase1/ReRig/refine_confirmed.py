import bpy, json
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig')
s=bpy.context.scene; a=bpy.data.objects['Dosa_Phase1_Rig']; dg=bpy.context.evaluated_depsgraph_get()
directions=[Vector(v).normalized() for v in [(1,.137,.271),(.173,1,.319),(.113,.237,1)]]
def inside(tree,point):
 votes=0
 for direction in directions:
  p=point.copy(); n=0
  for step in range(40):
   hit,normal,idx,dist=tree.ray_cast(p,direction,5)
   if hit is None:break
   n+=1; p=hit+direction*.00001
  votes+=n%2
 return votes>=2
o=bpy.data.objects['Durumagi']; initial=[v.co.copy() for v in o.data.vertices]; rows=[]
for label in ['Walk','Run','Squat']:
 a.animation_data.action=bpy.data.actions['DIAG_'+label]; count=0; maximum=0
 for f in range(1,int(a.animation_data.action.frame_range[1])+1):
  s.frame_set(f); dg.update(); be=bpy.data.objects['Body'].evaluated_get(dg); bm=be.to_mesh()
  tree=BVHTree.FromPolygons([v.co for v in bm.vertices],[p.vertices[:] for p in bm.polygons])
  e=o.evaluated_get(dg); me=e.to_mesh(); updates=[]
  mats={p.name:(p.matrix@p.bone.matrix_local.inverted()).to_3x3() for p in a.pose.bones}
  for v in me.vertices:
   rv=o.data.vertices[v.index]
   if rv.co.z>=1.02:continue
   co,no,idx,d=tree.find_nearest(v.co); signed=(v.co-co).dot(no)
   if signed>=-.002 or d>=.1 or not inside(tree,v.co):continue
   target=co+no*.007
   if inside(tree,target):target=co-no*.007
   m=Matrix(((0,0,0),(0,0,0),(0,0,0)))
   for g in rv.groups:m+=mats[o.vertex_groups[g.group].name]*g.weight
   delta=m.inverted_safe()@(target-v.co)
   if delta.length>.012:delta*=.012/delta.length
   new=rv.co+delta; total=new-initial[v.index]
   if total.length>.03:new=initial[v.index]+total.normalized()*.03
   updates.append((v.index,new)); maximum=max(maximum,(new-rv.co).length)
  e.to_mesh_clear(); be.to_mesh_clear()
  for i,co in updates:o.data.vertices[i].co=co
  o.data.update(); count+=len(updates)
 rows.append({'action':label,'vertex_corrections':count,'max_step_m':maximum})
(OUT/'confirmed_clearance.json').write_text(json.dumps(rows,indent=2)); print(json.dumps(rows))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_Rerig.blend'),compress=True)
