import bpy,json
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];dg=bpy.context.evaluated_depsgraph_get()
fixes=[]
for action in ['DIAG_Walk','DIAG_Run','DIAG_Squat','DIAG_BrushSwing','DIAG_ArmsUp']:
 a.animation_data.action=bpy.data.actions[action];end=int(a.animation_data.action.frame_range[1])
 count=0;maxdelta=0
 for f in sorted(set([1,end//4,end//2,3*end//4,end])):
  s.frame_set(f);dg.update();be=bpy.data.objects['Body'].evaluated_get(dg);bm=be.to_mesh();tree=BVHTree.FromPolygons([v.co for v in bm.vertices],[p.vertices[:] for p in bm.polygons])
  mats={p.name:(p.matrix@p.bone.matrix_local.inverted()).to_3x3() for p in a.pose.bones}
  for name in ['InnerTop','Durumagi']:
   o=bpy.data.objects[name];e=o.evaluated_get(dg);me=e.to_mesh();deltas=[]
   for v in me.vertices:
    if name=="Durumagi" and o.data.vertices[v.index].co.z<1.02:continue
    co,no,idx,d=tree.find_nearest(v.co);signed=(v.co-co).dot(no)
    if signed<.004 and d<.11:
     rest=o.data.vertices[v.index];m=Matrix(((0,0,0),(0,0,0),(0,0,0)))
     for g in rest.groups:
      n=o.vertex_groups[g.group].name
      if n in mats:m+=mats[n]*g.weight
     delta=m.inverted_safe()@(co+no*.006-v.co)
     if delta.length>.015:delta*=.015/delta.length
     deltas.append((v.index,delta));maxdelta=max(maxdelta,delta.length)
   e.to_mesh_clear()
   for idx,delta in deltas:o.data.vertices[idx].co+=delta
   o.data.update();count+=len(deltas)
  be.to_mesh_clear();dg.update()
 fixes.append({'action':action,'corrections':count,'max_rest_delta':maxdelta})
print(json.dumps(fixes))
(OUT/'clearance_refinement_latest.json').write_text(json.dumps(fixes,indent=2))
