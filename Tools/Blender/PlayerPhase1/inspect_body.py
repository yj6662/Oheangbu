import bpy,json
from pathlib import Path
from mathutils import Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1')
before=set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(OUT/'Source/Meshy/Body/glb.glb'))
objects=list(set(bpy.data.objects)-before)
meshes=[o for o in objects if o.type=='MESH']
points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
factor=1.75/(hi.z-lo.z)
for o in meshes:
 m=o.matrix_world.copy()
 for v in o.data.vertices:v.co=(m@v.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*factor
 o.parent=None;o.matrix_world.identity()
 o.name='Body' if len(meshes)==1 else 'Body_'+o.name
rows=[]
for o in meshes:
 o.data.calc_loop_triangles()
 rows.append(dict(name=o.name,vertices=len(o.data.vertices),faces=len(o.data.polygons),triangles=len(o.data.loop_triangles),bounds=list(o.dimensions),materials=[m.name for m in o.data.materials]))
print(json.dumps(rows));(OUT/'Source/body_initial_stats.json').write_text(json.dumps(rows,indent=2))
bpy.context.view_layer.update()
for o in bpy.context.selected_objects:o.select_set(False)
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
for a in bpy.context.screen.areas:
 if a.type=='VIEW_3D':
  region=next(r for r in a.regions if r.type=='WINDOW')
  with bpy.context.temp_override(area=a,region=region):bpy.ops.view3d.view_selected()
