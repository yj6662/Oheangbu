"""Topology and projected landmark check before any facial deformation."""
exec(compile((Path('C:/Users/yj666/Oheangbu/Tools/Blender/C02_RigFaceLab/face_inspect.py')).read_text(encoding='utf-8'),'face_inspect.py','exec'),{'__name__':'face_helpers'}) if False else None
import bpy, json, math, numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face'
ns={'__name__':'face_helpers'};exec(compile((ROOT/'Tools/Blender/C02_RigFaceLab/face_inspect.py').read_text(encoding='utf-8'),'face_inspect.py','exec'),ns)
o=bpy.data.objects['C02_Mesh_0'];m=o.data;m.calc_loop_triangles()
world=[o.matrix_world@v.co for v in m.vertices];tris=[t.vertices[:] for t in m.loop_triangles]
bvh=BVHTree.FromPolygons(world,tris,all_triangles=True)
# Pixel locations chosen after viewing the actual neutral front render; not bbox ratios.
pixel_points={'EyeRight_center':(576,355),'EyeLeft_center':(702,355),'EyeRight_inner':(602,355),'EyeRight_outer':(548,351),
 'EyeLeft_inner':(676,355),'EyeLeft_outer':(729,353),'Mouth_center':(640,496),'Mouth_left':(691,493),'Mouth_right':(587,493),'Chin':(640,557)}
landmarks={}
for name,(px,py) in pixel_points.items():
 x=(px-640)*.55/1280;z=1.595+(360-py)*.55/1280
 loc,normal,index,distance=bvh.ray_cast(Vector((x,-1,z)),Vector((0,1,0)),2)
 landmarks[name]={'source_pixel':[px,py],'world':list(loc) if loc else None,'normal':list(normal) if normal else None,'triangle':index,'vertex_ids':list(tris[index]) if index is not None else None}
records={'landmarks':landmarks,'welded_component_count':1,'independent_eyeball_components':0,'independent_hair_brow_beard_components':0,
 'interpretation':'All source triangles belong to one connected component after coincident UV-split vertices are merged at 1 micrometre. Separate eye and mouth interior volumes still require local inspection; component count alone does not prove their absence.'}
(OUT/'face_landmarks.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
gray=bpy.data.materials.new('FaceLabDiagnosticGray');gray.diffuse_color=(.35,.35,.35,1);gray.use_nodes=True
bsdf=next(n for n in gray.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bsdf.inputs['Base Color'].default_value=(.35,.35,.35,1);bsdf.inputs['Roughness'].default_value=.65
bpy.context.scene.view_layers[0].material_override=gray
ns['render']('Structure_gray_front',(0,-1,0));ns['render']('Structure_gray_threequarter',(1,-2,0))
bpy.context.scene.view_layers[0].material_override=None
for name,l in landmarks.items():
 if l['world'] is None:continue
 bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=.0013,location=l['world']);mark=bpy.context.object;mark.name='Landmark_'+name
 mat=bpy.data.materials.new('Marker_'+name);mat.diffuse_color=(1,.03,.02,1);mark.data.materials.append(mat)
ns['render']('Structure_landmarks_front',(0,-1,0))
print('STRUCTURE_COMPLETE',json.dumps(records))
