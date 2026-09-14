"""Inspect the pinned lower axilla before any local weight repair."""
import bpy,importlib.util,json,math
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/OuterSeam';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
def module(name,file):
 spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
physics=module('cloth_physics','diagnose_cloth_blender.py');helper=module('cloth_render','build_brush.py')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
 if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
physics.direct_pose(rig,'rest_settle')
core=bpy.data.objects['DosaV2_BodyCore'];core.data.calc_loop_triangles();triangles=[tuple(t.vertices) for t in core.data.loop_triangles]
bvh=BVHTree.FromPolygons([v.co for v in core.data.vertices],triangles,all_triangles=True)
def weights(o,v):return {o.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-8}
rows=[]
for side in ['L','R']:
 o=bpy.data.objects['DosaV2_SleeveOuter_'+side]
 indices=[v.index for v in o.data.vertices if abs(v.co.x)<.235 and v.co.z<1.23]
 neighbours={i:set() for i in range(len(o.data.vertices))}
 for e in o.data.edges:
  a,b=e.vertices;neighbours[a].add(b);neighbours[b].add(a)
 for i in indices:
  v=o.data.vertices[i];hit=bvh.find_nearest(v.co);tri=triangles[hit[2]]
  rows.append({'mesh':o.name,'index':i,'co':list(v.co),'mobility':o.data.color_attributes['ClothMobility'].data[i].color[0],
   'weights':weights(o,v),'neighbours':sorted(neighbours[i]),'coreDistance':hit[3],'coreNearest':list(hit[0]),
   'coreTriangle':[{'v':j,'co':list(core.data.vertices[j].co),'weights':weights(core,core.data.vertices[j])} for j in tri]})
 # Diagnostic material assignment exists only in this in-memory inspection.
 mat=bpy.data.materials.new('DiagnosticLowerAxilla');mat.diffuse_color=(1,.03,.12,1);mat.use_nodes=True
 p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(1,.03,.12,1)
 o.data.materials.append(mat);slot=len(o.data.materials)-1
 for face in o.data.polygons:
  if any(i in indices for i in face.vertices):face.material_index=slot
(OUT/'lower-axilla-source-inspection.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
camera=scene.camera or helper.lighting(scene)
for pose in ['rest_settle','grip_settle','raised_arms_settle']:
 physics.direct_pose(rig,pose);helper.render(scene,camera,OUT/('before-highlight-'+pose+'.png'),(0,-.03,1.20),.79,math.pi*.15,width=1000,height=800)
print(OUT)
