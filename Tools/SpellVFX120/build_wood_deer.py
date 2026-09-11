"""Isolated Blender authoring. Reuses source geometry and local licensed bark; no API generation."""
import bpy, bmesh, math, json, hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu'); OUT=ROOT/'Art/SpellVFX120/WoodDeer'; OUT.mkdir(exist_ok=True)
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer'; ASSET.mkdir(exist_ok=True)
TEX=ASSET/'Textures'; TEX.mkdir(exist_ok=True)
source=ROOT/'Art/SpellVFX120/MeshySummons/Blender/MeshySummons_Source.blend'
source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
body=bpy.data.objects['SM_Meshy_016_ACF0']; body.hide_set(False); body.hide_render=False
for o in list(bpy.data.objects):
 if o!=body:bpy.data.objects.remove(o,do_unlink=True)
body.name='WoodDeer_Body'; body.data=body.data.copy();body.data.name='WoodDeer_Body'
bpy.context.view_layer.objects.active=body;body.select_set(True)
# Preserve shape, use withers (upper torso behind the neck) for the requested shoulder height.
withers=max(v.co.z for v in body.data.vertices if .08<v.co.y<.30 and .8<v.co.z<1.5)
scale=1.5/withers
for v in body.data.vertices:v.co*=scale
body.data.update()
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bm.to_mesh(body.data);bm.free()
if body.data.has_custom_normals:body.data.normals_split_custom_set([(0,0,0)]*len(body.data.loops))
for polygon in body.data.polygons:polygon.use_smooth=True
coords=[v.co.copy() for v in body.data.vertices]
# Four actual hoof clusters, derived from source vertices rather than assumed symmetry.
low=[v for v in coords if v.z<.09*scale]
centres=[Vector(p)*scale for p in [(.33,.65,.03),(.14,.7,.03),(.10,-.09,.03),(-.07,-.04,.03)]]
for _ in range(12):
 groups=[[] for _ in centres]
 for v in low:groups[min(range(4),key=lambda k:(v-centres[k]).length_squared)].append(v)
 for k,g in enumerate(groups):
  if g:centres[k]=sum(g,Vector())/len(g)
feet=[Vector((v.x,v.y,0)) for v in centres]
original_mat=body.data.materials[0]
mat=original_mat.copy();mat.name='M_WoodDeer_Bark';body.data.materials[0]=mat;mat.use_nodes=True
nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
old_color=bsdf.inputs['Base Color'].links[0].from_socket if bsdf.inputs['Base Color'].is_linked else None
tc=nodes.new('ShaderNodeTexCoord'); sep=nodes.new('ShaderNodeSeparateXYZ');links.new(tc.outputs['Generated'],sep.inputs[0])
combine=nodes.new('ShaderNodeCombineXYZ');links.new(sep.outputs['Y'],combine.inputs['X']);links.new(sep.outputs['Z'],combine.inputs['Y'])
mul=nodes.new('ShaderNodeVectorMath');mul.operation='MULTIPLY';mul.inputs[1].default_value=(3,5,1);links.new(combine.outputs[0],mul.inputs[0])
fract=nodes.new('ShaderNodeVectorMath');fract.operation='FRACTION';links.new(mul.outputs[0],fract.inputs[0])
patch=nodes.new('ShaderNodeVectorMath');patch.operation='MULTIPLY_ADD';patch.inputs[1].default_value=(.28,.26,0);patch.inputs[2].default_value=(.035,.055,0);links.new(fract.outputs[0],patch.inputs[0])
bark=nodes.new('ShaderNodeTexImage');bark.image=bpy.data.images.load(str(ROOT/'Oheangbu/Assets/HwaseongHaenggung/Textures/Additions/T_Bark_BC.png'),check_existing=True);links.new(patch.outputs[0],bark.inputs['Vector'])
hue=nodes.new('ShaderNodeHueSaturation');hue.inputs['Saturation'].default_value=.32;hue.inputs['Value'].default_value=.95;links.new(bark.outputs['Color'],hue.inputs['Color'])
# Source color multiplicatively preserves eyes, nose and facial relief.
mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.55;links.new(hue.outputs[0],mix.inputs[1])
if old_color:links.new(old_color,mix.inputs[2])
else:mix.inputs[2].default_value=(.75,.75,.75,1)
links.new(mix.outputs[0],bsdf.inputs['Base Color']);bsdf.inputs['Roughness'].default_value=.85;bsdf.inputs['Metallic'].default_value=0
bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.55;bump.inputs['Distance'].default_value=.006
links.new(bark.outputs['Color'],bump.inputs['Height'])
links.new(bump.outputs[0],bsdf.inputs['Normal'])
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.device='CPU';scene.render.threads_mode='FIXED';scene.render.threads=2
scene.render.bake.margin=8
for kind,typ in [('BaseColor','DIFFUSE'),('Normal','NORMAL')]:
 saved=TEX/('T_WoodDeer_'+kind+'.png')
 if saved.exists() and kind=='BaseColor':
  image=bpy.data.images.load(str(saved),check_existing=False);image.name='T_WoodDeer_'+kind
  if kind=='Normal':image.colorspace_settings.name='Non-Color'
  continue
 image=bpy.data.images.new('T_WoodDeer_'+kind,width=2048,height=2048,alpha=False)
 if kind=='Normal':image.colorspace_settings.name='Non-Color'
 bake=nodes.new('ShaderNodeTexImage');bake.image=image;nodes.active=bake
 if typ=='DIFFUSE':scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
 bpy.ops.object.bake(type=typ)
 image.filepath_raw=str(TEX/('T_WoodDeer_'+kind+'.png'));image.file_format='PNG';image.save()
 print('BAKED',kind,flush=True)
# Replace authoring graph with portable baked maps in the delivery blend.
nodes.clear();bsdf=nodes.new('ShaderNodeBsdfPrincipled');bsdf.inputs['Roughness'].default_value=.85
output=nodes.new('ShaderNodeOutputMaterial');links.new(bsdf.outputs[0],output.inputs[0])
for kind in ['BaseColor','Normal']:
 tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images['T_WoodDeer_'+kind]
 if kind=='BaseColor':
  tint=nodes.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1;tint.inputs[2].default_value=(.72,.62,.48,1);links.new(tex.outputs['Color'],tint.inputs[1]);links.new(tint.outputs[0],bsdf.inputs['Base Color'])
 else:
  normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.7;links.new(tex.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs[0],bsdf.inputs['Normal'])
def simple_mat(name,color):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=.85;return m
rootmat=simple_mat('M_WoodDeer_Root',(.12,.095,.068));leafmat=simple_mat('M_WoodDeer_Leaf',(.20,.29,.075))
bvh=BVHTree.FromPolygons(coords,[p.vertices[:] for p in body.data.polygons])
def attach(p,offset=.009):
 q,n,_,_=bvh.find_nearest(Vector(p));return q+n*offset
def tubes(paths,radius):
 vertices=[];faces=[]
 for points in paths:
  base=len(vertices)
  for j,p in enumerate(points):
   direction=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized();side=direction.cross(Vector((0,1,0))).normalized()
   if side.length<.1:side=Vector((1,0,0))
   up=direction.cross(side).normalized();r=radius*(.35+.65*math.sin(math.pi*j/(len(points)-1))**.5)
   for k in range(6):vertices.append(p+r*(math.cos(k*math.tau/6)*side+math.sin(k*math.tau/6)*up))
   if j:
    for k in range(6):a=base+(j-1)*6+k;b=base+(j-1)*6+(k+1)%6;faces.append((a,b,b+6,a+6))
  faces.append(tuple(base+k for k in reversed(range(6))));faces.append(tuple(base+(len(points)-1)*6+k for k in range(6)))
 mesh=bpy.data.meshes.new('AttachedRoots');mesh.from_pydata(vertices,[],faces);mesh.update();o=bpy.data.objects.new('WoodDeer_Roots',mesh);scene.collection.objects.link(o);mesh.materials.append(rootmat)
 for p in mesh.polygons:p.use_smooth=True
 return o
paths=[]
for foot in feet:
 for a in [0,2.4]:
  points=[]
  for j in range(17):
   t=j/16;z=(.08+.37*t)*scale;angle=a+t*3.0;p=foot+Vector((math.cos(angle)*.08,math.sin(angle)*.07,z));points.append(attach(p))
  paths.append(points)
for side in [-1,1]:
 for n in range(3):
  points=[attach(Vector((side*(.15-.025*n)*scale,(-.31+.21*j/20)*scale,(.94+.35*j/20)*scale))) for j in range(21)];paths.append(points)
# A slender continuous vine supports the leaf necklace, fitted to the actual neck.
def collar_point(angle):
 return Vector((-.015+.19*math.cos(angle),-.225+.19*math.sin(angle),1.40+.035*math.sin(angle)))*scale
paths.append([attach(collar_point(j*math.tau/64),.016) for j in range(65)])
roots=tubes(paths,.012*scale)
# Leaves anchored to dispersed actual antler tips; no free-floating invented attachment points.
high=sorted([v for v in coords if v.z>1.91*scale],key=lambda v:-v.z);tips=[]
for v in high:
 if all((v-p).length>.14*scale for p in tips):tips.append(v)
 if len(tips)==10:break
verts=[];faces=[]
for j,p in enumerate(tips):
 for sign in [-1,1]:
  direction=Vector((sign*.75,math.sin(j*1.7)*.45,.4)).normalized();length=(.08+.01*(j%3))*scale;side=direction.cross(Vector((0,0,1))).normalized();base=len(verts)
  verts.extend([p,p+direction*length*.48+side*.026*scale,p+direction*length*.53+Vector((0,0,.008)),p+direction*length,p+direction*length*.48-side*.026*scale]);faces.extend([(base,base+1,base+2),(base+1,base+3,base+2),(base+3,base+4,base+2),(base+4,base,base+2)])
# Overlapping leaves follow the neck surface with their own outward clearance.
necklace_count=26
for j in range(necklace_count):
 angle=j*math.tau/necklace_count;p=collar_point(angle)
 tangent=Vector((-math.sin(angle),math.cos(angle),0))
 direction=(Vector((0,0,-1))+tangent*.28).normalized()
 length=(.135+.012*math.sin(j*2.1))*scale;width=.045*scale
 outline=[p,p+direction*length*.46+tangent*width,p+direction*length*.50,p+direction*length,p+direction*length*.46-tangent*width]
 base=len(verts)
 verts.extend([attach(q,.022 if k!=2 else .035) for k,q in enumerate(outline)])
 faces.extend([(base,base+1,base+2),(base+1,base+3,base+2),(base+3,base+4,base+2),(base+4,base,base+2)])
lm=bpy.data.meshes.new('NewLeaves');lm.from_pydata(verts,[],faces);lm.update();leaves=bpy.data.objects.new('WoodDeer_Leaves',lm);scene.collection.objects.link(leaves);lm.materials.append(leafmat)
luv=lm.uv_layers.new(name='UVMap')
for loop in lm.loops:luv.data[loop.index].uv=(0, [0,.5,.5,1,.5][loop.vertex_index%5])
for o in [body,roots,leaves]:o.hide_render=False;o.hide_set(False)
body['shoulder_height_m']=1.5;body['source']='Meshy7 016_ACF0; original topology and silhouette preserved'
objects=[body,roots,leaves]
rows=[]
for o in objects:o.data.calc_loop_triangles();rows.append(dict(name=o.name,tris=len(o.data.loop_triangles),vertices=len(o.data.vertices),materials=len(o.data.materials)))
assert sum(r['tris'] for r in rows)<=18000
for o in bpy.context.selected_objects:o.select_set(False)
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=body
fbx=ASSET/'SM_WoodDeer.fbx';bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,use_mesh_modifiers=True,path_mode='COPY',embed_textures=False)
# Pack only images used by the finished material to keep the editable file modest.
for i in list(bpy.data.images):
 if i.name.startswith('T_WoodDeer_'):i.pack()
scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
scene.world.color=(.15,.15,.15)
for loc,power,size in [((3,-4,5),700,4),((-3,-2,3),500,3),((1,3,4),800,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(Vector((0,0,1.1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-4,2.4));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.3))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=5.4;scene.camera=cam
scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'WoodDeer_Working.blend'))
report=dict(sourceHashBefore=source_hash,sourceHashAfter=hashlib.sha256(source.read_bytes()).hexdigest(),shoulderHeight=1.5,scale=scale,sourceShoulder=withers,feet=[list(v) for v in feet],parts=rows,totalTris=sum(r['tris'] for r in rows),materials=3,fbx=str(fbx),meshGenerationCredits=0,rigged=False)
report['antlerLeaves']=len(tips)*2;report['necklaceLeaves']=necklace_count
(OUT/'model_report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
# Render the same camera both before and after; do not save source state into the work file.
scene.render.filepath=str(OUT/'model_after.png');bpy.ops.render.render(write_still=True)
roots.hide_render=True;leaves.hide_render=True;body.data.materials[0]=original_mat
scene.render.filepath=str(OUT/'model_before.png');bpy.ops.render.render(write_still=True)
# Empty-scene FBX readback measures actual export geometry, materials and shape bounds.
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx))
roundtrip=[]
for o in bpy.context.scene.objects:
 if o.type=='MESH':o.data.calc_loop_triangles();roundtrip.append(dict(name=o.name,tris=len(o.data.loop_triangles),materials=len(o.data.materials),dimensions=list(o.dimensions)))
(OUT/'fbx_roundtrip.json').write_text(json.dumps(roundtrip,indent=2));assert sum(r['tris'] for r in roundtrip)==report['totalTris']
print('WOOD_DEER_EXPORT_PASS',flush=True)
