import bpy,math,json,statistics
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig')
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];body=bpy.data.objects['Body']
if any(b.name.startswith('Cloth_') for b in a.data.bones):
 raise RuntimeError('Open ReRig/Before.blend before building; this file already contains the new cloth rig.')
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
# Start with the fitted cloth before repeated collision pushes deformed its silhouette.
with bpy.data.libraries.load(str(OUT.parent/'Source/BeforeClearance.blend'),link=False) as (src,dst):dst.objects=['InnerTop','Durumagi']
for src in dst.objects:
 name='InnerTop' if src.name.startswith('InnerTop') else 'Durumagi';dst=bpy.data.objects[name];dst.data=src.data.copy();bpy.data.objects.remove(src,do_unlink=True)
finger_data={}
for side,sign in [('Left',1),('Right',-1)]:
 pts=[Vector((sign*v.co.x,v.co.y,v.co.z)) for v in body.data.vertices if sign*v.co.x>.7]
 # Estimate individual digit centre lines from the two independently sampled hands.
 definitions=[('Thumb',-.030,.004,.707,.790),('Index',.005,.042,.748,.866),('Middle',.042,.069,.744,.876),('Ring',.069,.090,.737,.863),('Little',.090,.140,.727,.837)]
 finger_data[side]={}
 for name,ylo,yhi,base,limit in definitions:
  cloud=[p for p in pts if ylo<=p.y<yhi and p.x>base]
  tipx=max(p.x for p in cloud);tipcloud=[p for p in cloud if p.x>tipx-.012]
  tipy=statistics.median(p.y for p in tipcloud);basey=tipy
  if name=='Thumb':basey=.012
  points=[]
  for f in [0,.43,.76,1.0]:
   x=base+(tipx-.005-base)*f;y=basey+(tipy-basey)*f
   nearby=sorted(cloud,key=lambda p:(p.x-x)**2+(p.y-y)**2)[:18]
   z=(min(p.z for p in nearby)+max(p.z for p in nearby))/2
   points.append(Vector((sign*x,y,z)))
  finger_data[side][name]=points
bpy.context.view_layer.objects.active=a;bpy.ops.object.mode_set(mode='EDIT')
for b in list(a.data.edit_bones):
 if b.name.startswith('Hem_'):a.data.edit_bones.remove(b)
for side in ['Left','Right']:
 hand=a.data.edit_bones[side+'Hand'];hand.tail=hand.head+(hand.tail-hand.head).normalized()*.085
 for digit,points in finger_data[side].items():
  for j in range(3):
   b=a.data.edit_bones[side+'Hand'+digit+str(j+1)];b.head=points[j];b.tail=points[j+1];b.align_roll(Vector((0,0,1)))
   b.parent=hand if j==0 else a.data.edit_bones[side+'Hand'+digit+str(j)]
   b.use_connect=j>0
# Twelve radial panels with four length-preserving segments each.
N=12;levels=[1.055,.85,.65,.44,.20];radii=[.205,.235,.27,.295,.32]
hem_rest={}
for i in range(N):
 ang=2*math.pi*i/N;xy=Vector((math.sin(ang),-math.cos(ang),0));points=[xy*r+Vector((0,0,z)) for r,z in zip(radii,levels)];hem_rest[str(i)]=[list(p) for p in points]
 parent=a.data.edit_bones['Hips']
 for j in range(4):
  b=a.data.edit_bones.new(f'Cloth_{i:02d}_{j}');b.head=points[j];b.tail=points[j+1];b.parent=parent;b.use_connect=j>0;b.use_deform=True;b.align_roll(xy);parent=b
bpy.ops.object.mode_set(mode='OBJECT')
def clear(v,o):
 for g in list(v.groups):o.vertex_groups[g.group].remove([v.index])
def assign(o,v,weights):
 best=sorted([(n,w) for n,w in weights.items() if w>1e-7],key=lambda p:-p[1])[:4];total=sum(w for n,w in best)
 if total<=0:best=[('Hips',1)];total=1
 clear(v,o)
 for n,w in best:
  g=o.vertex_groups.get(n) or o.vertex_groups.new(name=n);g.add([v.index],w/total,'REPLACE')
def distance(p,u,v):
 t=max(0,min(1,(p-u).dot(v-u)/(v-u).length_squared));return (p-u.lerp(v,t)).length,t
for v in body.data.vertices:
 if abs(v.co.x)<.705:continue
 side='Left' if v.co.x>0 else 'Right';candidates=[]
 for digit,points in finger_data[side].items():
  for j in range(3):
   d,t=distance(v.co,points[j],points[j+1]);candidates.append((d,digit,j,t))
 _,digit,j,t=min(candidates);points=finger_data[side][digit]
 # One digit per vertex; no cross-finger nearest-distance blending.
 full=(v.co-points[0]).dot(points[-1]-points[0])/(points[-1]-points[0]).length_squared
 if full<-.12:assign(body,v,{side+'Hand':1});continue
 pos=max(0,min(2.999,j+t));k=min(2,int(pos));u=pos-k
 weights={side+'Hand'+digit+str(k+1):1-u}
 if k<2:weights[side+'Hand'+digit+str(k+2)]=u
 else:weights[side+'Hand'+digit+'3']=1
 palm=max(0,min(1,1-full/.22))
 weights={n:w*(1-palm) for n,w in weights.items()};weights[side+'Hand']=palm
 assign(body,v,weights)
# Barycentric weight transfer on the body's triangles, preserving surface locality.
body.data.calc_loop_triangles();tri=[tuple(t.vertices) for t in body.data.loop_triangles];tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],tri,all_triangles=True)
def body_weights(p):
 co,no,idx,d=tree.find_nearest(p);ids=tri[idx];v0,v1,v2=[body.data.vertices[i].co for i in ids];u=v1-v0;v=v2-v0;w=co-v0;den=u.dot(u)*v.dot(v)-u.dot(v)**2
 b=(v.dot(v)*w.dot(u)-u.dot(v)*w.dot(v))/den if abs(den)>1e-12 else 0;c=(u.dot(u)*w.dot(v)-u.dot(v)*w.dot(u))/den if abs(den)>1e-12 else 0
 ws={}
 for i,q in zip(ids,[max(0,1-b-c),max(0,b),max(0,c)]):
  for g in body.data.vertices[i].groups:
   n=body.vertex_groups[g.group].name;ws[n]=ws.get(n,0)+q*g.weight
 return ws
for name in ['InnerTop','Durumagi']:
 o=bpy.data.objects[name];o.vertex_groups.clear()
 for b in a.data.bones:o.vertex_groups.new(name=b.name)
 for v in o.data.vertices:
  if name=='Durumagi' and v.co.z<1.075:
   ang=math.atan2(v.co.x,-v.co.y)%(2*math.pi);u=ang/(2*math.pi)*N;i=int(u)%N;frac=u-int(u)
   z=v.co.z;j=next((j for j in range(4) if z>=levels[j+1]),3);f=max(0,min(1,(levels[j]-z)/(levels[j]-levels[j+1])))
   # Weights interpolate adjacent panels and segment centres, maximum four.
   weights={}
   for k,w in [(i,1-frac),((i+1)%N,frac)]:
    weights[f'Cloth_{k:02d}_{j}']=w*(1-f) if j<3 else w
    if j<3:weights[f'Cloth_{k:02d}_{j+1}']=w*f
   if z>1.035:
    weights={'Hips':max(0,min(1,(z-1.035)/.04)),f'Cloth_{i:02d}_0':max(0,min(1,(1.075-z)/.04))}
  else:
   weights=body_weights(v.co)
   if name=='Durumagi':weights={n:w for n,w in weights.items() if 'Hand' not in n and 'Leg' not in n}
  assign(o,v,weights)
 for mod in o.modifiers:
  if mod.type=='ARMATURE':mod.object=a;mod.use_deform_preserve_volume=False
 o.parent=a
(OUT/'rig_definition.json').write_text(json.dumps({'fingers':{s:{d:[list(p) for p in ps] for d,ps in digits.items()} for s,digits in finger_data.items()},'cloth_panels':N,'cloth_rest':hem_rest,'levels':levels},indent=2))
for o in bpy.context.scene.objects:
 if o.type=='ARMATURE' and o!=a:o.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_Rerig.blend'),compress=True)
print('Rerig built:',len(a.data.bones),'bones')
