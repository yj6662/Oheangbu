"""New Meshy7 source -> isolated static rock dokkaebi, no rig or animation."""
import bpy,bmesh,json,hashlib,shutil,math
from pathlib import Path
from mathutils import Vector
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/SpellVFX120/StoneJangseung';A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/StoneJangseung';T=A/'Textures';T.mkdir(parents=True,exist_ok=True)
S=O/'Meshy/064_BAB8/refine';source=S/'model_urls_glb.glb';sha=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(source))
body=next(o for o in bpy.context.scene.objects if o.type=='MESH')
for obj in list(bpy.context.scene.objects):
 if obj!=body:bpy.data.objects.remove(obj,do_unlink=True)
body.name='StoneJangseung_Body';bpy.context.view_layer.objects.active=body;body.select_set(True);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
v=[v.co for v in body.data.vertices];lo=Vector(tuple(min(p[i] for p in v) for i in range(3)));hi=Vector(tuple(max(p[i] for p in v) for i in range(3)));scale=2.6/(hi.z-lo.z)
for p in body.data.vertices:p.co=(p.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*scale
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bm.to_mesh(body.data);bm.free()
original=body.data.copy();original_mat=body.data.materials[0]
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
edits={'crownVertices':0,'baseVertices':0,'eyeVertices':0}
for p in body.data.vertices:
 q=p.co.copy()
 if q.z>2.09:q.z=2.09+(q.z-2.09)*.22;edits['crownVertices']+=1
 if q.z<.38:
  w=1-smooth(.20,.38,q.z);q.x*=1-.27*w;q.y*=1-.22*w;edits['baseVertices']+=1
 # Broaden round stone eyes within their existing face UVs; no painted pupils.
 for side in [-1,1]:
  d=((q.x-side*.145)/.135)**2+((q.z-1.94)/.115)**2
  if d<1 and q.y<-.12:
   q.y-=.105*(1-d)**.65;edits['eyeVertices']+=1
 q.x*=1.12
 p.co=q
# Preserve Meshy face, replace the open architectural shaft with an organic monolith.
bm=bmesh.new();bm.from_mesh(body.data)
for z,normal in [(1.40,(0,0,1)),(2.075,(0,0,-1))]:
 bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=(0,0,z),plane_no=normal,clear_inner=True,dist=.0001)
boundary=[e for e in bm.edges if e.is_boundary]
if boundary:bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
bm.to_mesh(body.data);bm.free()
parts=[body]
verts=[];faces=[];segments=40;rings=32
for j in range(rings+1):
 z=2.075*j/rings;rx=.315+.04*(1-j/rings)+.008*math.sin(j*.8);ry=.30+.018*math.sin(j*.55)
 for i in range(segments):
  t=2*math.pi*i/segments;c=math.cos(t);d=math.sin(t);x=rx*math.copysign(abs(c)**.55,c);y=ry*math.copysign(abs(d)**.55,d)
  verts.append((x+.018*math.sin(j*.19),y+.05,z))
for j in range(rings):
 for i in range(segments):a=j*segments+i;b=j*segments+(i+1)%segments;faces.append((a,b,b+segments,a+segments))
faces.append(tuple(reversed(range(segments))));faces.append(tuple(rings*segments+i for i in range(segments)))
mesh=bpy.data.meshes.new('OrganicMonolith');mesh.from_pydata(verts,[],faces);mesh.update();pillar=bpy.data.objects.new('OrganicMonolith',mesh);bpy.context.collection.objects.link(pillar);parts.append(pillar)
for side in [-1,1]:
 x=side*.16;z=1.945
 samples=[v.co.y for v in body.data.vertices if abs(v.co.x-x)<.07 and abs(v.co.z-z)<.06]
 y=min(samples) if samples else -.28
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(x,y+.065,z));eye=bpy.context.object;eye.scale=(.115,.075,.112);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);parts.append(eye)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:obj.select_set(True)
bpy.context.view_layer.objects.active=body;bpy.ops.object.join()
remesh=body.modifiers.new('FuseStone','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.012;remesh.use_smooth_shade=True;bpy.ops.object.modifier_apply(modifier=remesh.name)
smoothmod=body.modifiers.new('SoftenToolPlanes','SMOOTH');smoothmod.factor=.38;smoothmod.iterations=3;bpy.ops.object.modifier_apply(modifier=smoothmod.name)
body.data.calc_loop_triangles();pre=len(body.data.loop_triangles)
if pre>16500:
 dec=body.modifiers.new('GameBudget','DECIMATE');dec.ratio=16500/pre;bpy.ops.object.modifier_apply(modifier=dec.name)
for poly in body.data.polygons:poly.use_smooth=True
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.01);bpy.ops.object.mode_set(mode='OBJECT')
edits['replacementMonolith']=True;edits['roundStoneEyesAdded']=2;edits['voxelUnionM']=.012;edits['preDecimationTriangles']=pre
body.data.update();bpy.context.view_layer.update();after=body.data
mat=bpy.data.materials.new('M_StoneJangseung_Granite');mat.use_nodes=True;body.data.materials.clear();body.data.materials.append(mat)
n=mat.node_tree.nodes;l=mat.node_tree.links;n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');bs.inputs['Metallic'].default_value=0;bs.inputs['Roughness'].default_value=.88;l.new(bs.outputs[0],out.inputs[0])
tex=n.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(S/'texture_urls_0_base_color.png'))
coord=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=24;noise.inputs['Detail'].default_value=3;l.new(coord.outputs['Object'],noise.inputs['Vector'])
ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.18;ramp.color_ramp.elements[0].color=(.13,.105,.075,1);ramp.color_ramp.elements[1].position=.83;ramp.color_ramp.elements[1].color=(.46,.40,.30,1);l.new(noise.outputs['Fac'],ramp.inputs[0])
mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.72;l.new(tex.outputs[0],mix.inputs[1]);l.new(ramp.outputs[0],mix.inputs[2]);l.new(ramp.outputs[0],bs.inputs['Base Color'])
normal=n.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(S/'texture_urls_0_normal.png'));normal.image.colorspace_settings.name='Non-Color';nm=n.new('ShaderNodeNormalMap');l.new(normal.outputs[0],nm.inputs['Color'])
fine=n.new('ShaderNodeTexNoise');fine.inputs['Scale'].default_value=210;fine.inputs['Detail'].default_value=2;l.new(coord.outputs['Object'],fine.inputs['Vector']);bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.3;bump.inputs['Distance'].default_value=.012;l.new(fine.outputs['Fac'],bump.inputs['Height']);l.new(bump.outputs[0],bs.inputs['Normal'])
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('ReviewWorld');scene.world.color=(.15,.15,.15);scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.threads_mode='FIXED';scene.render.threads=2;scene.render.bake.margin=8
images={}
for kind in ['BaseColor','Normal']:
 image=bpy.data.images.new('T_StoneJangseung_'+kind,width=2048,height=2048,alpha=False)
 if kind=='Normal':image.colorspace_settings.name='Non-Color'
 target=n.new('ShaderNodeTexImage');target.image=image;n.active=target
 scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
 bpy.ops.object.bake(type='DIFFUSE' if kind=='BaseColor' else 'NORMAL')
 image.filepath_raw=str(T/(image.name+'.png'));image.file_format='PNG';image.save();images[kind]=image
n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');l.new(bs.outputs[0],out.inputs[0]);bs.inputs['Roughness'].default_value=.88;bs.inputs['Metallic'].default_value=0
for kind,image in images.items():
 node=n.new('ShaderNodeTexImage');node.image=image;image.pack()
 if kind=='BaseColor':l.new(node.outputs[0],bs.inputs['Base Color'])
 else:
  nm=n.new('ShaderNodeNormalMap');l.new(node.outputs[0],nm.inputs['Color']);l.new(nm.outputs[0],bs.inputs['Normal'])
body.data.calc_loop_triangles();tris=len(body.data.loop_triangles);assert tris<=18000
fbx=A/'SM_StoneJangseung.fbx';bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,path_mode='COPY')
scene.cycles.samples=16;scene.cycles.use_denoising=True
for loc,power,size in [((3,-4,5),700,4),((-3,-2,3),500,3),((1,3,4),800,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(Vector((0,0,1.1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-5,2.5));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=4.4;scene.camera=cam;scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
for image in bpy.data.images:
 if image.source=='FILE' and image.has_data:image.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(O/'StoneJangseung_Working.blend'))
report=dict(triangles=tris,vertices=len(body.data.vertices),materials=1,dimensions=list(body.dimensions),sourceHashBefore=sha,sourceHashAfter=hashlib.sha256(source.read_bytes()).hexdigest(),meshyCredits=35,requestedPolycount=12000,sourceActualTriangles=len(original.polygons),rigged=False,edits=edits)
(O/'model_report.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(O/'model_after.png');bpy.ops.render.render(write_still=True)
body.data=original;scene.render.filepath=str(O/'model_before.png');bpy.ops.render.render(write_still=True)
body.data=after;cam.location=(0,-5,2.05);cam.rotation_euler=(Vector((0,-.05,1.8))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.5;scene.render.filepath=str(O/'face_detail.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx));rows=[]
for obj in bpy.context.scene.objects:
 if obj.type=='MESH':obj.data.calc_loop_triangles();rows.append(dict(name=obj.name,triangles=len(obj.data.loop_triangles),dimensions=list(obj.dimensions)))
assert sum(r['triangles'] for r in rows)==tris
(O/'fbx_roundtrip.json').write_text(json.dumps(rows,indent=2));print('STONE_JANGSEUNG_EXPORT_PASS',report,flush=True)
