import bpy,math,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1')
a=bpy.data.objects['Dosa_Phase1_Rig'];body=bpy.data.objects['Body'];top=bpy.data.objects['InnerTop']
for n in ['Durumagi','Durumagi_PreRemesh']:bpy.data.objects[n].hide_render=True
bpy.context.view_layer.objects.active=a
bpy.ops.object.mode_set(mode='EDIT')
finger_defs=[('Thumb',.714,-.005,.779,-.026),('Index',.752,.025,.858,.026),('Middle',.746,.050,.872,.052),('Ring',.738,.074,.858,.078),('Little',.729,.096,.826,.103)]
segments=[]
for side,sign in [('Left',1),('Right',-1)]:
 for label,x0,y0,x1,y1 in finger_defs:
  parent=a.data.edit_bones[side+'Hand']
  for j in range(3):
   n=side+'Hand'+label+str(j+1);b=a.data.edit_bones.new(n)
   p0=Vector((sign*x0,y0,1.39));p1=Vector((sign*x1,y1,1.39))
   b.head=p0.lerp(p1,j/3);b.tail=p0.lerp(p1,(j+1)/3);b.parent=parent;b.use_deform=True;parent=b
   segments.append((n,b.head.copy(),b.tail.copy(),side,label))
bpy.ops.object.mode_set(mode='OBJECT')
for n,_,_,_,_ in segments:body.vertex_groups.new(name=n)
def segdist(p,u,v):
 t=max(0,min(1,(p-u).dot(v-u)/(v-u).length_squared));return (p-u.lerp(v,t)).length,t
for v in body.data.vertices:
 x=abs(v.co.x)
 if x<.717:continue
 side='Left' if v.co.x>0 else 'Right'
 choices=sorted([(segdist(v.co,u,w)[0],n) for n,u,w,si,lab in segments if si==side])[:2]
 influence=min(1,max(0,(x-.717)/.043));weights={side+'Hand':1-influence}
 den=sum(1/max(d,.003)**2 for d,n in choices)
 for d,n in choices:weights[n]=influence/max(d,.003)**2/den
 for g in list(v.groups):body.vertex_groups[g.group].remove([v.index])
 for n,w in weights.items():
  if w>1e-6:body.vertex_groups[n].add([v.index],w,'REPLACE')
# Raise and move sleeves onto the actual arm centerlines; torso remains fitted.
for v in top.data.vertices:
 f=min(1,max(0,(abs(v.co.x)-.08)/.18));v.co.z+=.075*f;v.co.y+=.060*f
tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],[p.vertices[:] for p in body.data.polygons],all_triangles=True)
fixed=0
for v in top.data.vertices:
 co,no,idx,dist=tree.find_nearest(v.co)
 if co is not None:
  signed=(v.co-co).dot(no)
  if signed<.006 and dist<.1:v.co=co+no*.008;fixed+=1
# Transfer weights from nearest body surface vertices, limiting to four.
kd=KDTree(len(body.data.vertices))
for v in body.data.vertices:kd.insert(v.co,v.index)
kd.balance()
for g in body.vertex_groups:top.vertex_groups.new(name=g.name)
for v in top.data.vertices:
 weights={}
 for co,idx,d in kd.find_n(v.co,3):
  for g in body.data.vertices[idx].groups:
   n=body.vertex_groups[g.group].name;weights[n]=weights.get(n,0)+g.weight/max(.003,d)**2
 best=sorted(weights.items(),key=lambda x:-x[1])[:4];total=sum(w for n,w in best)
 for n,w in best:top.vertex_groups[n].add([v.index],w/total,'REPLACE')
mod=top.modifiers.new('SharedSkeleton','ARMATURE');mod.object=a;top.parent=a
for ob in [body,top]:
 for m in ob.data.materials:
  bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  for l in list(bs.inputs['Metallic'].links):m.node_tree.links.remove(l)
  bs.inputs['Metallic'].default_value=0
bpy.context.scene.render.filepath=str(OUT/'Previews/InnerTop_fitted.png');bpy.ops.render.render(write_still=True)
print('Finger bones added:30; inner vertices clearance corrected:',fixed)
