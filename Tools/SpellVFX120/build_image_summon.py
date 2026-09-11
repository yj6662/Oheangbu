"""Blender static summon preparation; keeps image-derived shape and source textures."""
import bpy,bmesh,json,hashlib,sys,shutil
from pathlib import Path
from mathutils import Vector
name=sys.argv[sys.argv.index('--')+1];water=name=='WaterTurtle';ident='112_C634' if water else '064_BAB8'
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/SpellVFX120'/name;A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120'/name;T=A/'Textures';T.mkdir(parents=True,exist_ok=True);S=O/'Meshy'/ident/'image';source=S/'model_urls_glb.glb';sha=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(source));meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();body=bpy.context.object
for o in list(bpy.context.scene.objects):
 if o!=body:bpy.data.objects.remove(o,do_unlink=True)
body.name=name+'_Body';bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
v=[v.co for v in body.data.vertices];lo=Vector(tuple(min(p[i] for p in v) for i in range(3)));hi=Vector(tuple(max(p[i] for p in v) for i in range(3)));factor=(3.6/(hi.y-lo.y)) if water else (2.15/(hi.z-lo.z))
for p in body.data.vertices:p.co=(p.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*factor
support_rotation=0;club_shortening=0
if not water:
 # Recover the rest plane from the two soles and club tip. Rigid rotation preserves the grip.
 # Weld first so UV seams cannot split a sole into false islands.
 bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bm.to_mesh(body.data);bm.free()
 eligible={v.index for v in body.data.vertices if v.co.z<.50};adj={i:set() for i in eligible}
 for e in body.data.edges:
  i,j=e.vertices
  if i in eligible and j in eligible:adj[i].add(j);adj[j].add(i)
 groups=[]
 while eligible:
  todo=[eligible.pop()];group=[]
  while todo:
   i=todo.pop();group.append(i)
   for j in adj[i]:
    if j in eligible:eligible.remove(j);todo.append(j)
  groups.append(group)
 groups=sorted(groups,key=len,reverse=True)[:3];assert len(groups)==3
 anchors=[min((body.data.vertices[i].co.copy() for i in g),key=lambda p:p.z) for g in groups]
 club_index=min(range(3),key=lambda i:anchors[i].z);sole_z=min(v.z for i,v in enumerate(anchors) if i!=club_index);club_shortening=sole_z-anchors[club_index].z
 eligible={v.index for v in body.data.vertices if v.co.z<1.06};adj={i:set() for i in eligible}
 for e in body.data.edges:
  i,j=e.vertices
  if i in eligible and j in eligible:adj[i].add(j);adj[j].add(i)
 todo=[groups[club_index][0]];club=set(todo)
 while todo:
  i=todo.pop()
  for j in adj[i]:
   if j not in club:club.add(j);todo.append(j)
 for i in club:
  v=body.data.vertices[i];t=max(0,min(1,(v.co.z-.2)/.86));v.co.z+=club_shortening*(1-t*t*(3-2*t))
 for v in body.data.vertices:v.co.z-=sole_z
 aligned=[min((body.data.vertices[i].co.copy() for i in g),key=lambda p:p.z) for g in groups]
 (O/'support_components.json').write_text(json.dumps(dict(componentSizes=[len(g) for g in groups],sourceAnchors=[list(v) for v in anchors],alignedAnchors=[list(v) for v in aligned],clubShorteningM=club_shortening),indent=2))
body.data.calc_loop_triangles();source_tris=len(body.data.loop_triangles);original=body.data.copy();original_materials=list(body.data.materials)
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
if body.data.has_custom_normals:body.data.normals_split_custom_set([(0,0,0)]*len(body.data.loops))
for p in body.data.polygons:p.use_smooth=True
body.data.update();bpy.context.view_layer.update();after=body.data
mat=bpy.data.materials.new('M_'+name);mat.use_nodes=True;body.data.materials.clear();body.data.materials.append(mat)
n=mat.node_tree.nodes;l=mat.node_tree.links;n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');l.new(bs.outputs[0],out.inputs[0]);bs.inputs['Metallic'].default_value=0;bs.inputs['Roughness'].default_value=.72
tex=n.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(S/'texture_urls_0_base_color.png'));hue=n.new('ShaderNodeHueSaturation');hue.inputs['Saturation'].default_value=.78 if water else .7;hue.inputs['Value'].default_value=1;l.new(tex.outputs[0],hue.inputs['Color']);l.new(hue.outputs[0],bs.inputs['Base Color'])
if not water:
 tint=n.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=.45;tint.inputs[2].default_value=(.48,.30,.15,1);l.new(hue.outputs[0],tint.inputs[1]);l.new(tint.outputs[0],bs.inputs['Base Color'])
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('ReviewWorld');scene.world.color=(.15,.15,.15);scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.threads_mode='FIXED';scene.render.threads=2;scene.render.bake.margin=8
image=bpy.data.images.new('T_'+name+'_BaseColor',width=2048,height=2048,alpha=False);target=n.new('ShaderNodeTexImage');target.image=image;n.active=target;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True;bpy.ops.object.bake(type='DIFFUSE');image.filepath_raw=str(T/(image.name+'.png'));image.file_format='PNG';image.save()
shutil.copy2(S/'texture_urls_0_normal.png',T/('T_'+name+'_Normal.png'))
n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');l.new(bs.outputs[0],out.inputs[0]);bs.inputs['Roughness'].default_value=.66 if water else .7;bs.inputs['Metallic'].default_value=.04 if water else .3
node=n.new('ShaderNodeTexImage');node.image=image;l.new(node.outputs[0],bs.inputs['Base Color']);image.pack()
normal=n.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(T/('T_'+name+'_Normal.png')));normal.image.colorspace_settings.name='Non-Color';nm=n.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.75;l.new(normal.outputs[0],nm.inputs['Color']);l.new(nm.outputs[0],bs.inputs['Normal']);normal.image.pack()
body.data.calc_loop_triangles();tris=len(body.data.loop_triangles);assert tris<=22000;fbx=A/('SM_'+name+'.fbx');bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,path_mode='COPY')
scene.cycles.samples=12;scene.cycles.use_denoising=True
targetpos=Vector((0,0,.8 if water else 1.0))
for loc,power,size in [((3,-4,5),700,4),((-3,-2,3),500,3),((1,3,4),800,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.size=size;light.rotation_euler=(targetpos-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-5,3 if water else 2.6));cam=bpy.context.object;cam.rotation_euler=(targetpos-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=6.2 if water else 4.8;scene.camera=cam;scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
for im in bpy.data.images:
 if im.source=='FILE' and im.has_data:im.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(O/(name+'_Working.blend')))
report=dict(triangles=tris,sourceActualTriangles=source_tris,requestedPolycount=16000,vertices=len(body.data.vertices),materials=1,dimensions=list(body.dimensions),sourceHashBefore=sha,sourceHashAfter=hashlib.sha256(source.read_bytes()).hexdigest(),meshyCredits=35,rigged=False,edits='Normalize to metres, centre/ground origin, weld coincident vertices, recalculate normals, smooth shading, preserve silhouette and UV, desaturate source BaseColor via Blender bake. No rig or sculpt redesign.')
report['supportPlaneRigidRotationRadians']=support_rotation
report['clubShorteningM']=club_shortening
(O/'model_report.json').write_text(json.dumps(report,indent=2));scene.render.filepath=str(O/'model_after.png');bpy.ops.render.render(write_still=True)
body.data=original;scene.render.filepath=str(O/'model_before.png');bpy.ops.render.render(write_still=True);body.data=after
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx));rows=[]
for o in bpy.context.scene.objects:
 if o.type=='MESH':o.data.calc_loop_triangles();rows.append(dict(name=o.name,triangles=len(o.data.loop_triangles),dimensions=list(o.dimensions)))
assert sum(r['triangles'] for r in rows)==tris;(O/'fbx_roundtrip.json').write_text(json.dumps(rows,indent=2));print(name,'EXPORT_PASS',report,flush=True)
