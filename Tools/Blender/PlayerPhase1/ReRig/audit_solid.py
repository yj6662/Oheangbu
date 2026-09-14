import bpy,json,sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig')
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];dg=bpy.context.evaluated_depsgraph_get()
directions=[Vector(v).normalized() for v in [(1,.137,.271),(.173,1,.319),(.113,.237,1)]]
def votes(tree,point):
 result=0
 for direction in directions:
  p=point.copy();n=0
  for step in range(40):
   hit,normal,idx,dist=tree.ray_cast(p,direction,5)
   if hit is None:break
   n+=1;p=hit+direction*.00001
  result+=n%2
 return result
rows=[]
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 a.animation_data.action=bpy.data.actions['DIAG_'+label];end=int(a.animation_data.action.frame_range[1])
 for f in range(1,end+1):
  s.frame_set(f);dg.update();be=bpy.data.objects['Body'].evaluated_get(dg);bm=be.to_mesh()
  tree=BVHTree.FromPolygons([v.co for v in bm.vertices],[p.vertices[:] for p in bm.polygons]);row={'action':label,'frame':f,'parts':{}}
  for name in ['InnerTop','Durumagi']:
   e=bpy.data.objects[name].evaluated_get(dg);me=e.to_mesh();candidates=[];confirmed=[];uncertain=0
   for v in me.vertices:
    co,no,idx,d=tree.find_nearest(v.co);signed=(v.co-co).dot(no)
    if signed<-.002 and d<.1:
     candidates.append(-signed);n=votes(tree,v.co)
     if n>=2:confirmed.append(-signed)
     if n in (1,2):uncertain+=1
   row['parts'][name]={'candidates':len(candidates),'inside_majority':len(confirmed),'max_inside_depth_m':max(confirmed,default=0),'ray_disagreement':uncertain}
   e.to_mesh_clear()
  be.to_mesh_clear();rows.append(row)
 print('AUDITED',label,flush=True)
name='before_solid_audit.json' if '--before' in sys.argv else 'solid_audit.json'
(OUT/name).write_text(json.dumps({'method':'Nearest-surface candidates (<-2mm, distance<100mm), then majority of three ray parity tests. Not a complete triangle/self-collision proof; open body boundaries and folded triangles can make parity ambiguous. Every integer frame at 24fps.','results':rows},indent=2))
