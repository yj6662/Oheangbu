import bpy,bmesh,math,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1')
a=bpy.data.objects['Dosa_Phase1_Rig'];body=bpy.data.objects['Body'];top=bpy.data.objects['InnerTop'];robe=bpy.data.objects['Durumagi']
for v in robe.data.vertices:
 f=min(1,max(0,(abs(v.co.x)-.16)/.13))
 v.co.x=math.copysign(.16+(abs(v.co.x)-.16)*.68,v.co.x) if abs(v.co.x)>.16 else v.co.x
 if v.co.z>1.1:v.co.z+=.065*f;v.co.y+=.065*f
# Continuous surface cleanup. Keep openings, UVs and all body geometry.
bm=bmesh.new();bm.from_mesh(robe.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(robe.data);bm.free()
for p in robe.data.polygons:p.use_smooth=True
if robe.data.has_custom_normals:robe.data.normals_split_custom_set([(0,0,0)]*len(robe.data.loops))
for src,margin in [(body,.012),(top,.008)]:
 tree=BVHTree.FromPolygons([v.co for v in src.data.vertices],[p.vertices[:] for p in src.data.polygons])
 for v in robe.data.vertices:
  co,no,idx,d=tree.find_nearest(v.co)
  if co is not None and d<.06 and (v.co-co).dot(no)<margin:v.co=co+no*margin
# Four independently weighted hem chains, never bind hem to legs.
bpy.context.view_layer.objects.active=a;bpy.ops.object.mode_set(mode='EDIT')
for label,xy in [('Front',(0,-.17)),('Back',(0,.19)),('Left',(.23,0)),('Right',(-.23,0))]:
 parent=a.data.edit_bones['Hips']
 for j in range(3):
  b=a.data.edit_bones.new('Hem_'+label+'_'+str(j));b.head=(*xy,1.02-j*.26);b.tail=(*xy,.76-j*.26);b.parent=parent;parent=b;b.use_deform=True
bpy.ops.object.mode_set(mode='OBJECT')
for b in a.data.bones:robe.vertex_groups.new(name=b.name)
kd=KDTree(len(body.data.vertices))
for v in body.data.vertices:kd.insert(v.co,v.index)
kd.balance()
for v in robe.data.vertices:
 w={}
 if v.co.z<1.02:
  x,y,z=v.co;directions={'Front':max(0,-y),'Back':max(0,y),'Left':max(0,x),'Right':max(0,-x)}
  labels=sorted(directions,key=directions.get,reverse=True)[:2];t=max(0,min(2,(.99-z)/.26));j=min(1,int(t));f=t-j
  for label in labels:
   for k,weight in [(j,1-f),(j+1,f)]:w['Hem_'+label+'_'+str(k)]=directions[label]*weight
 else:
  for co,idx,d in kd.find_n(v.co,3):
   for g in body.data.vertices[idx].groups:
    n=body.vertex_groups[g.group].name
    if 'Leg' in n or 'Hand' in n:continue
    w[n]=w.get(n,0)+g.weight/max(.003,d)**2
 best=sorted(w.items(),key=lambda i:-i[1])[:4];total=sum(q for n,q in best)
 if total<1e-12:best=[('Hips',1)];total=1
 for n,q in best:
  if q>1e-8:robe.vertex_groups[n].add([v.index],q/total,'REPLACE')
mod=robe.modifiers.new('SharedSkeleton','ARMATURE');mod.object=a;robe.parent=a
bpy.context.scene.render.filepath=str(OUT/'Previews/Assembly_fitted.png');bpy.ops.render.render(write_still=True)
print('Shared skeleton and 12 hem bones bound')
