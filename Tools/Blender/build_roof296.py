"""Build three bounded tile LODs from the exposed surface of an owned KCISA roof.
Run with Blender 5 --background --factory-startup --python Tools/Blender/build_roof296.py.
This reads a Unity export; no vendor assets or Unity state are changed.
"""
from pathlib import Path
import bpy,bmesh,json,hashlib
import numpy as np
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296/Roof'
src=OUT/'source-patch.json';raw=json.loads(src.read_text(encoding='utf-8'));m=raw['Mesh']
p=np.array([[v[k] for k in 'xyz'] for v in m['Vertices']],dtype=float)
uv=np.array([[v[k] for k in 'xy'] for v in m['UV']],dtype=float)
t=np.array(m['Triangles']).reshape(-1,3);q=p[t];normal=np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]);good=normal[:,1]>1e-9
q=q[good];t=t[good];e1=q[:,1]-q[:,0];e2=q[:,2]-q[:,0];det=e1[:,0]*e2[:,2]-e1[:,2]*e2[:,0]
# The central z=0 is the source's ridge, not flat tiles. This off-ridge strip is all external grey ceramic.
x0,x1,z0,z1=-1.249,1.249,.451,1.249
nx,nz=50,16;verts=[];textures=[];miss=0
for ix,x in enumerate(np.linspace(x0,x1,nx+1)):
 for iz,z in enumerate(np.linspace(z0,z1,nz+1)):
  d=np.array([x,0,z])-q[:,0];u=(d[:,0]*e2[:,2]-d[:,2]*e2[:,0])/det;v=(e1[:,0]*d[:,2]-e1[:,2]*d[:,0])/det
  inside=(u>=-1e-6)&(v>=-1e-6)&(u+v<=1.000001)
  y=q[:,0,1]+u*e1[:,1]+v*e2[:,1];y[~inside]=-1e9
  best=int(np.argmax(y))
  if y[best]<-1e8:raise RuntimeError(f'Uncovered roof source at {x},{z}')
  tex=uv[t[best,0]]*(1-u[best]-v[best])+uv[t[best,1]]*u[best]+uv[t[best,2]]*v[best]
  verts.append((x,y[best],z));textures.append(tex)
verts=np.array(verts);fit=np.linalg.lstsq(np.c_[verts[:,0],verts[:,2],np.ones(len(verts))],verts[:,1],rcond=None)[0]
verts[:,1]-=np.c_[verts[:,0],verts[:,2],np.ones(len(verts))]@fit
verts[:,0]-=(x0+x1)/2;verts[:,2]-=(z0+z1)/2
# Flat two-centimetre perimeter joins prevent patch cracks after independent QEM collapses.
original_range=verts[:,1].copy()
for ix in range(nx+1):
 for iz in range(nz+1):
  edge=min(ix,nx-ix,iz,nz-iz)
  if edge==0:verts[ix*(nz+1)+iz,1]=0
faces=[]
for ix in range(nx):
 for iz in range(nz):
  a=ix*(nz+1)+iz;b=a+nz+1;faces.extend([(a,a+1,b),(b,a+1,b+1)])
# Bake source-visible colour and tile normals before decimation, so atlas seams cannot smear
# across the simplified triangles. Each texel samples the highest original triangle, not the grid.
W,H=640,208;top=np.full((H,W),-1e9);colour=np.zeros((H,W,4));normalmap=np.zeros((H,W,4));normalmap[:,:,:3]=[.5,.5,1];normalmap[:,:,3]=1
source_image=bpy.data.images.load(str(ROOT/'Oheangbu/Assets/HwaseongForteressGate/Textures/T_B_GateHouse_001_BC.png'))
texels=np.array(source_image.pixels[:]).reshape(source_image.size[1],source_image.size[0],4)
source_normals=np.array([[v[k] for k in 'xyz'] for v in m['Normals']],dtype=float)
for j,triangle in enumerate(q):
 lo=triangle.min(0);hi=triangle.max(0)
 ax=max(0,int(np.floor((lo[0]-x0)/(x1-x0)*W)));bx=min(W-1,int(np.ceil((hi[0]-x0)/(x1-x0)*W)))
 az=max(0,int(np.floor((lo[2]-z0)/(z1-z0)*H)));bz=min(H-1,int(np.ceil((hi[2]-z0)/(z1-z0)*H)))
 if ax>bx or az>bz:continue
 zz,xx=np.mgrid[az:bz+1,ax:bx+1];xx=x0+(xx+.5)/W*(x1-x0);zz=z0+(zz+.5)/H*(z1-z0)
 ddx=xx-triangle[0,0];ddz=zz-triangle[0,2]
 uu=(ddx*e2[j,2]-ddz*e2[j,0])/det[j];vv=(e1[j,0]*ddz-e1[j,2]*ddx)/det[j]
 yy=triangle[0,1]+uu*e1[j,1]+vv*e2[j,1]
 mask=(uu>=-1e-6)&(vv>=-1e-6)&(uu+vv<=1.000001)&(yy>top[az:bz+1,ax:bx+1])
 if not mask.any():continue
 weights=np.stack([1-uu-vv,uu,vv],-1);tt=weights@uv[t[j]]
 px=np.clip((tt[:,:,0]*texels.shape[1]).astype(int),0,texels.shape[1]-1);py=np.clip((tt[:,:,1]*texels.shape[0]).astype(int),0,texels.shape[0]-1)
 col=texels[py,px];nn=weights@source_normals[t[j]];nn[:,:,0]+=fit[0]*nn[:,:,1];nn[:,:,2]+=fit[1]*nn[:,:,1];nn/=np.maximum(1e-12,np.linalg.norm(nn,axis=-1,keepdims=True))
 rgb=np.stack([nn[:,:,0],nn[:,:,2],nn[:,:,1]],-1)*.5+.5
 top[az:bz+1,ax:bx+1][mask]=yy[mask];colour[az:bz+1,ax:bx+1][mask]=col[mask];normalmap[az:bz+1,ax:bx+1,:3][mask]=rgb[mask]
if (top<-1e8).any():raise RuntimeError('Source bake has uncovered texels')
# The roof atlas has stretched padding around each tile UV island. A continuous low-poly
# surface must use the broad ceramic-only colour island instead of interpolating those seams.
vv,uu=np.mgrid[0:H,0:W];tx=((.04+.92*(uu+.5)/W)*texels.shape[1]).astype(int);ty=((.02+.15*(vv+.5)/H)*texels.shape[0]).astype(int)
colour=texels[ty,tx].copy();colour[:,:,3]=1
for name,data,space in [('tile-base296.png',colour,'sRGB'),('tile-normal296.png',normalmap,'Non-Color')]:
 img=bpy.data.images.new(name,width=W,height=H,alpha=True);img.colorspace_settings.name=space;img.pixels[:]=data.astype(np.float32).ravel();img.filepath_raw=str(OUT/name);img.file_format='PNG';img.save()
textures=[((v[0]+(x1-x0)/2)/(x1-x0),(v[2]+(z1-z0)/2)/(z1-z0)) for v in verts]
mesh=bpy.data.meshes.new('KCISA_exposed_tile_strip_296');mesh.from_pydata(verts.tolist(),[],faces);mesh.update()
layer=mesh.uv_layers.new(name='SourceUV')
for poly in mesh.polygons:
 for loop in poly.loop_indices:layer.data[loop].uv=textures[mesh.loops[loop].vertex_index]
base=bpy.data.objects.new('Exposed_original_tiles_off_ridge',mesh);bpy.context.collection.objects.link(base)
# Original grey ceramic atlas is retained for visual inspection and production materials.
mat=bpy.data.materials.new('KCISA_GateHouse001_ceramic');mat.use_nodes=True
node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(OUT/'tile-base296.png'))
mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color']);mesh.materials.append(mat)
source_tris=len(faces);levels=[];counts=[]
for level,target in enumerate([160,32,4]):
 obj=base.copy();obj.data=base.data.copy();bpy.context.collection.objects.link(obj)
 bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 dec=obj.modifiers.new('Measured_tile_LOD','DECIMATE');dec.ratio=target/source_tris;dec.use_collapse_triangulate=True
 bpy.ops.object.modifier_apply(modifier=dec.name)
 tri=obj.modifiers.new('Explicit_triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
 if level==1:
  old=obj.data;mid=bpy.data.meshes.new('Middle_regular_tile_grid');points=[];middle_faces=[];hx=(x1-x0)/2;hz=(z1-z0)/2
  for ix in range(9):
   for iz in range(3):
    point=verts[int(round(ix/8*nx))*(nz+1)+int(round(iz/2*nz))].copy();point[0]=-hx+2*hx*ix/8;point[2]=-hz+2*hz*iz/2;points.append(point.tolist())
  for ix in range(8):
   for iz in range(2):
    a=ix*3+iz;b=a+3;middle_faces.extend([(a,a+1,b),(b,a+1,b+1)])
  mid.from_pydata(points,[],middle_faces);mid.uv_layers.new(name='PlanarBake');mid.materials.append(mat);obj.data=mid;bpy.data.meshes.remove(old)
 if level==2:
  old=obj.data;flat=bpy.data.meshes.new('Far_exact_tile_quad');hx=(x1-x0)/2;hz=(z1-z0)/2
  flat.from_pydata([(-hx,0,-hz),(-hx,0,hz),(hx,0,-hz),(hx,0,hz)],[],[(0,1,2),(2,1,3)]);flat.uv_layers.new(name='PlanarBake');flat.materials.append(mat);obj.data=flat;bpy.data.meshes.remove(old)
 elif level==0:
  bm=bmesh.new();bm.from_mesh(obj.data);boundary=[v for v in bm.verts if v.is_boundary];hx=(x1-x0)/2;hz=(z1-z0)/2
  # QEM moves free boundary vertices. Restore the four corners and every edge to the exact
  # original rectangle; the repeated strip then has no centimetre-scale holes at any LOD.
  corner_vertices=set()
  for cx,cz in [(-hx,-hz),(-hx,hz),(hx,-hz),(hx,hz)]:
   chosen=min((v for v in boundary if v not in corner_vertices),key=lambda v:(v.co.x-cx)**2+(v.co.z-cz)**2);chosen.co=(cx,0,cz);corner_vertices.add(chosen)
  for vert in boundary:
   if vert in corner_vertices:continue
   if abs(abs(vert.co.x)-hx)<abs(abs(vert.co.z)-hz):vert.co.x=hx if vert.co.x>0 else -hx
   else:vert.co.z=hz if vert.co.z>0 else -hz
   vert.co.y=0
  bm.to_mesh(obj.data);bm.free()
 obj.data.update();obj.data.calc_loop_triangles()
 for poly in obj.data.polygons:poly.use_smooth=True
 # Planar patch coordinates preserve baked detail at every LOD; QEM's old atlas loop averaging is discarded.
 for polygon in obj.data.polygons:
  for loop_index in polygon.loop_indices:
   co=obj.data.vertices[obj.data.loops[loop_index].vertex_index].co
   obj.data.uv_layers.active.data[loop_index].uv=((co.x+(x1-x0)/2)/(x1-x0),(co.z+(z1-z0)/2)/(z1-z0))
 obj.data.update();vs=[];ns=[];us=[];ts=[];layer=obj.data.uv_layers.active
 for face in obj.data.loop_triangles:
  for loop_id in face.loops:
   vert=obj.data.vertices[obj.data.loops[loop_id].vertex_index];co=vert.co;nn=vert.normal;tex=layer.data[loop_id].uv
   vs.append(dict(x=co.x,y=co.y,z=co.z));ns.append(dict(x=nn.x,y=nn.y,z=nn.z));us.append(dict(x=tex.x,y=tex.y));ts.append(len(ts))
 levels.append(dict(Vertices=vs,Normals=ns,UV=us,Triangles=ts));counts.append(len(ts)//3)
 obj.name=f'TileLOD{level}_{len(ts)//3}tri';obj.location.x=3.5*(level+1);obj.select_set(False)
result=dict(SourceSha256=raw['SourceSha256'],ExportSha256=hashlib.sha256(src.read_bytes()).hexdigest(),TileSize=dict(x=x1-x0,y=z1-z0),Material=raw['Material'],Method='Topmost upward-facing source triangles sampled at 5cm in an off-ridge 2.498x0.798m strip. Source grey ceramic colour island (.04..96,.02..17); normals baked by highest original triangle per texel; source macro-plane removed; flat perimeter seam; Blender5 QEM160 with exact rectangular boundaries; middle32 sampled regular grid; far2 exact quad. No underside/ridge faces.',InputSourceHashes={str(path.relative_to(ROOT)):hashlib.sha256(path.read_bytes()).hexdigest() for path in [ROOT/'Oheangbu'/raw['Source'],ROOT/'Oheangbu/Assets/HwaseongForteressGate/Models/Barbican/SM_B_GateHouse_Roof_001.fbx',ROOT/'Oheangbu/Assets/HwaseongForteressGate/Textures/T_B_GateHouse_001_BC.png']},SourceTriangles=len(t),HeightfieldTriangles=source_tris,ActualTriangles=counts,OriginalResidualRange=[float(original_range.min()),float(original_range.max())],Levels=levels)
(OUT/'tile-lods.json').write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
from mathutils import Vector
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.world.color=(.5,.5,.5);scene.view_settings.view_transform='Standard'
camdata=bpy.data.cameras.new('QA_camera');camera=bpy.data.objects.new('QA_camera',camdata);bpy.context.collection.objects.link(camera)
camera.location=(5.2,7,5);camera.rotation_euler=(Vector((5.2,0,0))-camera.location).to_track_quat('-Z','Y').to_euler();camdata.type='ORTHO';camdata.ortho_scale=13;scene.camera=camera
lightdata=bpy.data.lights.new('QA_area','AREA');lightdata.energy=1800;lightdata.shape='DISK';lightdata.size=10
light=bpy.data.objects.new('QA_area',lightdata);bpy.context.collection.objects.link(light);light.location=(3,8,3);light.rotation_euler=(Vector((5,0,0))-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=1600;scene.render.resolution_y=650;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/'tile-lods-preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'tile-patch296.blend'));bpy.ops.render.render(write_still=True)
print('ROOF296',json.dumps({k:result[k] for k in ['ActualTriangles','TileSize','OriginalResidualRange']}))
