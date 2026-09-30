"""Extract reusable CC0 timber pieces, retaining their original texture UVs."""
import bpy,json
from pathlib import Path
from mathutils import Vector
r=Path(__file__).resolve().parents[2];out=r/'Art/World/Compact/Rebuild/Mountain285'
src=out/'Sources/modular_wooden_pier'
bpy.ops.wm.open_mainfile(filepath=str(src/'modular_wooden_pier.blend'))
manifest=[]
for kind in ['planks','poles']:
 ob=bpy.data.objects['modular_wooden_pier_'+kind];me=ob.data;me.calc_loop_triangles()
 parts=json.loads((src/('modular_wooden_pier_'+kind+'-parts.json')).read_text())
 for index,part in enumerate(parts):
  ids=set(part['ids']);lo=Vector(part['lo']);size=Vector(part['size']);centre=lo+size*.5
  verts=[];normals=[];uv=[];triangles=[]
  for tri in me.loop_triangles:
   if not all(i in ids for i in tri.vertices):continue
   for li in tri.loops:
    co=me.vertices[me.loops[li].vertex_index].co-centre
    n=me.corner_normals[li].vector
    # Non-uniform normalization needs inverse-transpose for normals.
    nn=Vector((n.x*size.x,n.z*size.z,-n.y*size.y)).normalized()
    verts.append(dict(x=co.x/size.x,y=co.z/size.z,z=-co.y/size.y))
    normals.append(dict(x=nn.x,y=nn.y,z=nn.z));t=me.uv_layers.active.data[li].uv;uv.append(dict(x=t.x,y=t.y));triangles.append(len(verts)-1)
  name='Pier289_'+kind+'_'+str(index)
  (out/'Meshes'/(name+'.json')).write_text(json.dumps(dict(vertices=verts,normals=normals,uv=uv,triangles=triangles),separators=(',',':')))
  manifest.append(dict(name=name,kind=kind,source_object=ob.name,source_dimensions=list(size),triangles=len(triangles)//3))
(out/'pier289-manifest.json').write_text(json.dumps(manifest,indent=2))
print('Exported',len(manifest),'CC0 pieces with original UVs;',sum(x['triangles'] for x in manifest),'triangles')
