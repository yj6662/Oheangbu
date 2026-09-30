"""Owned new three-storey stone pagoda. Isolated --background process only."""
from pathlib import Path
import bpy, bmesh, math, json
from mathutils import Vector
R=Path(__file__).resolve().parents[3]
O=R/'Art/World/WorldMacro/Compact/KcisaReplacement/Source';O.mkdir(parents=True,exist_ok=True)
A=R/'Oheangbu/Assets/_Project/Art/World/WorldCompact/KcisaReplacement/Models';A.mkdir(parents=True,exist_ok=True)
for ob in list(bpy.data.objects):bpy.data.objects.remove(ob,do_unlink=True)
stone=bpy.data.materials.new('Pagoda_Granite');stone.diffuse_color=(.37,.355,.32,1);stone.use_nodes=True
bs=stone.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.88
noise=stone.node_tree.nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=65
bump=stone.node_tree.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.15;bump.inputs['Distance'].default_value=.018
stone.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height']);stone.node_tree.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
pieces=[]
def bevel(ob,width=.018):
 m=ob.modifiers.new('Worn stone arris','BEVEL');m.width=width;m.segments=2
 bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=m.name)
 ob.data.materials.append(stone);pieces.append(ob)
def block(name,p,size):
 bpy.ops.mesh.primitive_cube_add(size=1,location=p);ob=bpy.context.object;ob.name=name;ob.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);bevel(ob);return ob
def roof(name,z,half):
 # Separate lower ledge, drip edge, shallow concave slope, upper ridge seat.
 rings=[(half*.88,z),(half,z+.09),(half,z+.17),(half*.87,z+.25),(half*.64,z+.46),(half*.44,z+.57)]
 vs=[]
 for w,h in rings:
  for x,y in [(-1,-1),(1,-1),(1,1),(-1,1)]:vs.append((x*w,y*w,h+.055*(w/half)**4))
 faces=[(3,2,1,0)]
 for r in range(len(rings)-1):
  for i in range(4):faces.append((r*4+i,r*4+(i+1)%4,(r+1)*4+(i+1)%4,(r+1)*4+i))
 faces.append(tuple((len(rings)-1)*4+i for i in range(4)))
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],faces);mesh.update();ob=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(ob);bevel(ob,.015)
block('Ground_course',(0,0,.12),(3.25,3.25,.24));block('Lower_plinth',(0,0,.36),(2.98,2.98,.22));block('Base_moulding',(0,0,.55),(3.08,3.08,.14))
block('Base_dado',(0,0,.86),(2.68,2.68,.48))
for x in [-1.31,1.31]:
 for y in [-1.31,1.31]:block('Base_corner_pilaster',(x,y,.87),(.14,.14,.5))
block('Plinth_coping',(0,0,1.2),(3.1,3.1,.19))
z=1.3
for level,(w,h) in enumerate([(1.94,.85),(1.51,.64),(1.17,.5)]):
 block(f'Storey_{level+1}_body',(0,0,z+h/2),(w,w,h))
 for x in [-w/2,w/2]:
  for y in [-w/2,w/2]:block('Carved_corner_pilaster',(x,y,z+h/2),(.1,.1,h+.02))
 for j in range(3):block('Roof_stepped_bracket',(0,0,z+h+.04+j*.065),(w+.12+j*.12,w+.12+j*.12,.062))
 roof('Sloping_roof_'+str(level+1),z+h+.19,w*.73);z+=h+.79
block('Finial_seat',(0,0,z+.08),(.58,.58,.16))
for i,(rad,depth) in enumerate([(.25,.18),(.20,.22),(.15,.19),(.11,.25)]):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=1,location=(0,0,z+.2+i*.22));ob=bpy.context.object;ob.name='Lotus_finial';ob.scale=(rad,rad,depth);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);bevel(ob,.003)
bpy.ops.object.select_all(action='DESELECT')
for ob in pieces:ob.select_set(True)
bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();asset=bpy.context.object;asset.name='Korean_ThreeStorey_StonePagoda'
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
# FBX stays material-slot compatible with an owned KCISA granite texture in Unity.
bpy.ops.wm.save_as_mainfile(filepath=str(O/'StonePagoda.blend'))
bpy.ops.export_scene.fbx(filepath=str(A/'StonePagoda.fbx'),use_selection=True,object_types={'MESH'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False)
asset.data.calc_loop_triangles();(O/'StonePagoda.json').write_text(json.dumps(dict(triangles=len(asset.data.loop_triangles),materials=len(asset.data.materials),source='New authored mesh; supplier models unchanged; UV-unwrapped weathered masonry with sloping stone roof and finial'),indent=2))
print('PAGODA_DONE',len(asset.data.loop_triangles))
