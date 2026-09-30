"""Keep original near buildings; reduce roof tiles and dissolve only coplanar structural faces."""
from pathlib import Path
import bpy,bmesh,json,hashlib,time,math
ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild/Architecture296/Complex';OUT=WORK/'Reduced';OUT.mkdir(exist_ok=True,parents=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
receipts=[]
# This source material is the exposed ceramic roof. Paper, metal window lattices,
# wood frames, stone plinths and all other disconnected structural pieces never use collapse.
ROOF_GUID='abc00000000004934170793764722915'
for file in sorted((WORK/'Source').glob('*.json')):
 start=time.time();data=json.loads(file.read_text(encoding='utf-8-sig'));total=sum(len(part['Mesh']['Triangles'])//3 for part in data['Parts']);output=[];counts=[total,0,0];part_receipts=[]
 for pi,part in enumerate(data['Parts']):
  m=part['Mesh'];v=[tuple(p[k]for k in 'xyz')for p in m['Vertices']];uv=[tuple(p[k]for k in 'xy')for p in m['UV']];faces=[m['Triangles'][i:i+3]for i in range(0,len(m['Triangles']),3)]
  roof=Path(part['Material']).stem==ROOF_GUID
  mesh=bpy.data.meshes.new('NativeSource');mesh.from_pydata(v,[],faces);mesh.update();layer=mesh.uv_layers.new(name='OriginalUV')
  for poly in mesh.polygons:
   for loop in poly.loop_indices:layer.data[loop].uv=uv[mesh.loops[loop].vertex_index]
  bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.0000001);bm.to_mesh(mesh);bm.free();mesh.update();source=len(faces);levels=[];part_counts=[]
  for level in range(2):
   copy=mesh.copy();obj=bpy.data.objects.new('NativePart',copy);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
   if roof:
    dec=obj.modifiers.new('CeramicRoofOnlyQEM','DECIMATE');dec.ratio=[.18,.045][level];dec.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=dec.name)
   else:
    dec=obj.modifiers.new('CoplanarStructureOnly','DECIMATE');dec.decimate_type='DISSOLVE';dec.angle_limit=math.radians([.05,.2][level]);dec.use_dissolve_boundaries=False;dec.delimit={'UV','NORMAL','MATERIAL'};bpy.ops.object.modifier_apply(modifier=dec.name)
   tri=obj.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name);obj.data.update();obj.data.calc_loop_triangles();vs=[];ns=[];us=[];ts=[];layer=obj.data.uv_layers.active;seen={}
   # Shared vertices retain independent UV and normal seams without triplicating every triangle.
   for face in obj.data.loop_triangles:
    if face.area<1e-13:continue
    for loop in face.loops:
     vertex_index=obj.data.loops[loop].vertex_index;vert=obj.data.vertices[vertex_index];p=vert.co;n=vert.normal;u=layer.data[loop].uv;key=(vertex_index,round(u.x,8),round(u.y,8))
     index=seen.get(key)
     if index is None:
      index=len(vs);seen[key]=index;vs.append(dict(x=p.x,y=p.y,z=p.z));ns.append(dict(x=n.x,y=n.y,z=n.z));us.append(dict(x=u.x,y=u.y))
     ts.append(index)
   levels.append(dict(Vertices=vs,Normals=ns,UV=us,Triangles=ts));counts[level+1]+=len(ts)//3;part_counts.append(len(ts)//3);bpy.data.objects.remove(obj,do_unlink=True);bpy.data.meshes.remove(copy)
  output.append(dict(Material=part['Material'],Levels=levels));part_receipts.append(dict(index=pi,sourceTriangles=source,levels=part_counts,method='ceramic roof QEM'if roof else'coplanar dissolve only; open boundaries and UV seams retained'));bpy.data.meshes.remove(mesh)
 result={k:data[k]for k in ['Id','Source','SourceSha256','Min','Max']};result['NearFromSource']=True;result['ExportSha256']=hashlib.sha256(file.read_bytes()).hexdigest();result['Method']='L0 original source meshes directly, compact native vertices/UV/normals. L1/L2 preserve every structural component using only coplanar dissolve with open boundaries/UV seams retained; collapse QEM only exposed ceramic roof material.';result['Parts']=output
 (OUT/file.name).write_text(json.dumps(result,separators=(',',':')),encoding='utf-8');receipts.append(dict(source=data['Source'],originalTriangles=total,levels=counts,parts=part_receipts,seconds=round(time.time()-start,2),sourceExportSha256=hashlib.sha256(file.read_bytes()).hexdigest(),reducedSha256=hashlib.sha256((OUT/file.name).read_bytes()).hexdigest()));print('COMPLEX296',json.dumps({k:v for k,v in receipts[-1].items()if k!='parts'}),flush=True)
(OUT/'receipts.json').write_text(json.dumps(receipts,indent=2),encoding='utf-8')
