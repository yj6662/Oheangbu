import bpy,json,math
from mathutils.bvhtree import BVHTree
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];dg=bpy.context.evaluated_depsgraph_get()
results=[]
for act in ['DIAG_Walk','DIAG_Run','DIAG_ArmsUp','DIAG_Squat','DIAG_BrushSwing']:
 a.animation_data.action=bpy.data.actions[act];end=int(a.animation_data.action.frame_range[1])
 for f in range(1,end+1):
  s.frame_set(f);dg.update();be=bpy.data.objects['Body'].evaluated_get(dg);bm=be.to_mesh()
  tree=BVHTree.FromPolygons([v.co for v in bm.vertices],[p.vertices[:] for p in bm.polygons]);row={'action':act,'frame':f,'parts':{},'body_min_z':min(v.co.z for v in bm.vertices)}
  for name in ['InnerTop','Durumagi']:
   e=bpy.data.objects[name].evaluated_get(dg);me=e.to_mesh();hits=[]
   for v in me.vertices:
    co,no,idx,d=tree.find_nearest(v.co);signed=(v.co-co).dot(no)
    if signed<-.002 and d<.1:hits.append(-signed)
   row['parts'][name]={'penetration_candidate_vertices':len(hits),'max_candidate_depth_m':max(hits,default=0)};e.to_mesh_clear()
  be.to_mesh_clear();results.append(row)
(OUT/'pose_audit.json').write_text(json.dumps({'method':'Nearest body surface signed distance; candidates require visual confirmation, not full triangle collision proof. Every integer frame of all five diagnostic actions.','results':results},indent=2))
print("Full audit saved")
