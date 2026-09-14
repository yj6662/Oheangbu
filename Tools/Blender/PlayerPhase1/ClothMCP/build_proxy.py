"""Run through Blender MCP after opening the preserved ReRig source."""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ClothMCP')
OUT.mkdir(parents=True,exist_ok=True)
s=bpy.context.scene; rig=bpy.data.objects['Dosa_Phase1_Rig']
body=bpy.data.objects['Body']; top=bpy.data.objects['InnerTop']; robe=bpy.data.objects['Durumagi']
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
s.frame_set(1);bpy.context.view_layer.update()
assert not bpy.data.objects.get('ClothProxy_Durumagi'), 'Use the preserved source for a rebuild.'

def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)

# Refit the raised shoulder roof to the measured arm envelope, preserving collar and underarm.
fit=[]
for obj,source,gap,maxmove in [(top,body,.017,.035),(robe,top,.014,.045)]:
 coords=[v.co.copy() for v in source.data.vertices]
 changed=0; peak=0
 for v in obj.data.vertices:
  x,y,z=v.co
  if abs(x)<.17 or z<1.38:continue
  candidates=[p.z for p in coords if abs(p.x-x)<.025 and p.z>1.30]
  if not candidates:continue
  candidates.sort();ceiling=candidates[int(.95*(len(candidates)-1))]+gap
  dz=min(maxmove,max(0,z-ceiling))*smooth(.17,.23,abs(x))
  v.co.z-=dz
  if dz>0:changed+=1;peak=max(peak,dz)
 obj.data.update();fit.append({'mesh':obj.name,'vertices_adjusted':changed,'max_lowering_m':peak})

# A smooth, welded body collision surface; no gameplay colliders are changed.
collider=body.copy();collider.data=body.data.copy();collider.name='BodyCollisionProxy'
s.collection.objects.link(collider)
bm=bmesh.new();bm.from_mesh(collider.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0001)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(collider.data);bm.free()
bpy.context.view_layer.objects.active=collider
dec=collider.modifiers.new('Collision reduction','DECIMATE');dec.ratio=.28
bpy.ops.object.modifier_move_up(modifier=dec.name)
bpy.ops.object.modifier_apply(modifier=dec.name)
col=collider.modifiers.new('Cloth contact','COLLISION')
collider.collision.thickness_outer=.004;collider.collision.thickness_inner=.001
collider.collision.cloth_friction=3;collider.collision.damping=.2
collider.hide_render=True;collider.display_type='WIRE'

verts=[];faces=[];pins=[];kinds=[]
# Continuous torso/hem, open at the front. Torso follows the skeleton; hem falls freely.
levels=[1.50,1.46,1.41,1.35,1.28,1.20,1.12,1.065,1.025]+[1.025-(1.025-.18)*i/18 for i in range(1,19)]
N=40
for j,z in enumerate(levels):
 if z>=1.065:
  rx=.15+(.205-.15)*max(0,min(1,(1.5-z)/.435));ry=.10+(.16-.10)*max(0,min(1,(1.5-z)/.435))
 else:
  t=(1.065-z)/.885;rx=.205+.105*t;ry=.165+.13*t
 for i in range(N+1):
  th=.10+(2*math.pi-.20)*i/N
  verts.append((rx*math.sin(th),-ry*math.cos(th),z));pins.append(1 if z>=1.065 else (.55 if z>1.02 else 0));kinds.append('torso' if z>=1.025 else 'hem')
  if j and i:
   k=j*(N+1)+i;faces.append((k,k-1,k-(N+1)-1,k-(N+1)))
# Separate regular sleeve tubes, with a fixed upper seam and freely hanging undersides.
for side,sgn in [('Left',1),('Right',-1)]:
 start=len(verts);rows=14;circ=20
 for j in range(rows+1):
  x=.145+(.525-.145)*j/rows
  cloud=[v.co for v in robe.data.vertices if abs(v.co.x-sgn*x)<.027 and v.co.z>1.20]
  ys=sorted(v.y for v in cloud);zs=sorted(v.z for v in cloud)
  if len(cloud)>5:
   ylo,yhi=ys[int(.07*len(ys))],ys[int(.93*(len(ys)-1))]
   zlo,zhi=zs[int(.07*len(zs))],zs[int(.93*(len(zs)-1))]
   cy=(ylo+yhi)/2;cz=(zlo+zhi)/2;ry=max(.066,(yhi-ylo)/2);rz=max(.08,(zhi-zlo)/2)
  else:cy=.07;cz=1.42;ry=.075;rz=.09
  for i in range(circ):
   th=2*math.pi*i/circ
   verts.append((sgn*x,cy+ry*math.cos(th),cz+rz*math.sin(th)))
   seam=smooth(.45,.85,math.sin(th))
   pins.append(max(1-smooth(0,2,j),seam));kinds.append(side)
   if j:
    k=start+j*circ+i;prev=start+(j-1)*circ+i;nxt=start+j*circ+(i+1)%circ;pn=start+(j-1)*circ+(i+1)%circ
    faces.append((k,nxt,pn,prev))
mesh=bpy.data.meshes.new('Regular cloth simulation surface');mesh.from_pydata(verts,[],faces);mesh.update()
proxy=bpy.data.objects.new('ClothProxy_Durumagi',mesh);s.collection.objects.link(proxy)
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
pin=proxy.vertex_groups.new(name='SimPin')
kd=KDTree(len(body.data.vertices))
for v in body.data.vertices:kd.insert(v.co,v.index)
kd.balance()
for v,pw,kind in zip(proxy.data.vertices,pins,kinds):
 if pw>0:pin.add([v.index],pw,'REPLACE')
 weights={}
 if kind=='hem':weights={'Hips':1}
 else:
  for co,idx,d in kd.find_n(v.co,3):
   for g in body.data.vertices[idx].groups:
    name=body.vertex_groups[g.group].name
    if name.startswith('Cloth_') or 'Hand' in name:continue
    weights[name]=weights.get(name,0)+g.weight/max(.015,d)**2
  if kind=='torso':
   weights={n:w for n,w in weights.items() if not any(x in n for x in ['Arm','Leg','Foot','Toe'])}
 best=sorted(weights.items(),key=lambda x:-x[1])[:4] or [('Hips',1)];total=sum(w for n,w in best)
 for name,w in best:(proxy.vertex_groups.get(name) or proxy.vertex_groups.new(name=name)).add([v.index],w/total,'REPLACE')
arm=proxy.modifiers.new('SharedSkeleton','ARMATURE');arm.object=rig
cloth=proxy.modifiers.new('Gravity and inertia','CLOTH');cs=cloth.settings
cs.quality=10;cs.mass=.22;cs.air_damping=3;cs.tension_stiffness=35;cs.compression_stiffness=35;cs.shear_stiffness=18;cs.bending_stiffness=.35
cs.tension_damping=8;cs.compression_damping=8;cs.shear_damping=8;cs.bending_damping=1.5
cs.vertex_group_mass='SimPin';cs.pin_stiffness=20;cs.use_dynamic_mesh=True
cloth.collision_settings.use_collision=True;cloth.collision_settings.distance_min=.006;cloth.collision_settings.collision_quality=6;cloth.collision_settings.use_self_collision=False
cloth.point_cache.frame_start=1;cloth.point_cache.frame_end=250
proxy.hide_render=True;proxy.display_type='WIRE';proxy['purpose']='Blender-only regular cloth driver, not a visible extra garment.'
# Full garment follows the proxy, never Armature plus Surface Deform at once.
for m in robe.modifiers:
 if m.type=='ARMATURE':m.show_viewport=False;m.show_render=False
sd=robe.modifiers.new('Cloth surface transfer','SURFACE_DEFORM');sd.target=proxy;sd.falloff=4
bpy.context.view_layer.objects.active=robe
bpy.context.view_layer.update();bpy.ops.object.surfacedeform_bind(modifier=sd.name)
assert sd.is_bound, 'Surface Deform binding failed'
for obj in (proxy,collider):obj.hide_set(True)
rig.hide_set(True)
for obj in (body,top,robe):obj.hide_set(False);obj.hide_render=False
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.shading.type='MATERIAL';area.spaces.active.overlay.show_overlays=False
  area.spaces.active.region_3d.view_distance=2.6;area.spaces.active.region_3d.view_location=(0,0,.95)
(OUT/'build_report.json').write_text(json.dumps({'fit':fit,'physics_vertices':len(mesh.vertices),'physics_triangles':len(mesh.polygons),'source':'Dosa_Phase1_Rerig.blend','method':'Regular cloth proxy Armature -> Cloth, render robe Surface Deform only','RIG_PASS':False},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_ClothMCP.blend'),compress=True)
print(json.dumps({'fit':fit,'proxy_vertices':len(mesh.vertices),'bound':sd.is_bound}))
