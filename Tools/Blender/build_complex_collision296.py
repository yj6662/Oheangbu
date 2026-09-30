"""Separate collision meshes: keep structural boundaries; simplify ceramic tiles and omit tiny ornaments."""
from pathlib import Path
import bpy,bmesh,json,hashlib,time,math
ROOT=Path(__file__).resolve().parents[2];WORK=ROOT/'Art/World/Compact/Rebuild/Architecture296/Complex';OUT=WORK/'Collision';OUT.mkdir(exist_ok=True,parents=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
ROOF='abc00000000004934170793764722915';receipts=[]
for file in sorted((WORK/'Source').glob('*.json')):
 start=time.time();data=json.loads(file.read_text(encoding='utf-8-sig'));parts=[];before=0;smallfaces=0;convexReduced=0;convexPieces=0;afterroof=0
 for index,part in enumerate(data['Parts']):
  m=part['Mesh'];verts=[tuple(p[k]for k in 'xyz')for p in m['Vertices']];faces=[m['Triangles'][i:i+3]for i in range(0,len(m['Triangles']),3)];before+=len(faces)
  mesh=bpy.data.meshes.new('CollisionSource');mesh.from_pydata(verts,[],faces);mesh.update();bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
  # A 0.18m detached ornament cannot be a floor, doorway, wall panel or useful column.
  # Traverse welded components, not triangle islands split by original texture seams.
  bm.normal_update();seen=set();remove=[];hull_additions=[]
  for seed in list(bm.verts):
   if seed in seen:continue
   stack=[seed];seen.add(seed);component=[]
   while stack:
    v=stack.pop();component.append(v)
    for edge in v.link_edges:
     other=edge.other_vert(v)
     if other not in seen:seen.add(other);stack.append(other)
   extent=[max(v.co[i]for v in component)-min(v.co[i]for v in component)for i in range(3)]
   component_faces={f for v in component for f in v.link_faces}
   if max(extent)<.18:smallfaces+=len(component_faces);remove.extend(component)
   elif len(component_faces)>32 and len(component)<4000 and all(edge.is_manifold for v in component for edge in v.link_edges):
    original_volume=abs(sum(f.calc_center_median().dot(f.normal)*f.calc_area()/3 for f in component_faces))
    if original_volume>1e-7:
     hull=bmesh.new();hv=[hull.verts.new(v.co)for v in component];bmesh.ops.convex_hull(hull,input=hv,use_existing_faces=False);unused=[v for v in hull.verts if not v.link_faces]
     if unused:bmesh.ops.delete(hull,geom=unused,context='VERTS')
     hull.normal_update();volume=hull.calc_volume(signed=False)
     if volume<=original_volume*1.02:
      bmesh.ops.dissolve_limit(hull,angle_limit=math.radians(.2),use_dissolve_boundaries=False,verts=list(hull.verts),edges=list(hull.edges),delimit=set());hull.verts.ensure_lookup_table();index={v:i for i,v in enumerate(hull.verts)};hfaces=[[index[v]for v in f.verts]for f in hull.faces];triangles=sum(len(f)-2 for f in hfaces)
      if triangles<len(component_faces)*.6:
       hull_additions.append(([v.co.copy()for v in hull.verts],hfaces));remove.extend(component);convexReduced+=len(component_faces)-triangles;convexPieces+=1
     hull.free()
  for coords,faces in hull_additions:
   nv=[bm.verts.new(co)for co in coords]
   for face in faces:bm.faces.new([nv[i]for i in face])
  if remove:bmesh.ops.delete(bm,geom=remove,context='VERTS')
  bm.to_mesh(mesh);bm.free();mesh.update();obj=bpy.data.objects.new('PhysicalPart',mesh);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
  if Path(part['Material']).stem==ROOF:
   dec=obj.modifiers.new('CeramicEnvelope','DECIMATE');dec.ratio=.03;dec.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=dec.name)
  else:
   dec=obj.modifiers.new('StructuralPlanarOnly','DECIMATE');dec.decimate_type='DISSOLVE';dec.angle_limit=math.radians(1);dec.use_dissolve_boundaries=False;dec.delimit=set();bpy.ops.object.modifier_apply(modifier=dec.name)
  parts.append(obj)
 bpy.ops.object.select_all(action='DESELECT')
 for obj in parts:obj.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();obj=parts[0]
 # Once source-material/UV boundaries are gone, adjacent coplanar surfaces can share triangles.
 bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(.05),use_dissolve_boundaries=False,verts=list(bm.verts),edges=list(bm.edges),delimit=set());bm.to_mesh(obj.data);bm.free()
 tri=obj.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name);obj.data.calc_loop_triangles();vs=[];ts=[];seen={}
 for face in obj.data.loop_triangles:
  if face.area<1e-13:continue
  for index in face.vertices:
   if index not in seen:
    seen[index]=len(vs);p=obj.data.vertices[index].co;vs.append(dict(x=p.x,y=p.y,z=p.z))
   ts.append(seen[index])
 result=dict(Id=data['Id'],Source=data['Source'],SourceSha256=data['SourceSha256'],Min=data['Min'],Max=data['Max'],ExportSha256=hashlib.sha256(file.read_bytes()).hexdigest(),Method='Separate static collision. Weld UV/material seams; coplanar dissolve keeps open boundaries; drop detached ornaments smaller than0.18m; exposed ceramic roof only collapse. Closed structural components may use an independent convex hull only when volume grows no more than2%; open walls and doorway frames remain exact. Structural faces never use collapse QEM.',Mesh=dict(Vertices=vs,Triangles=ts))
 (OUT/file.name).write_text(json.dumps(result,separators=(',',':')),encoding='utf-8');receipt=dict(source=data['Source'],originalTriangles=before,collisionTriangles=len(ts)//3,omittedTinyOrnamentFaces=smallfaces,convexPieces=convexPieces,convexFaceReduction=convexReduced,vertices=len(vs),seconds=round(time.time()-start,2));receipts.append(receipt);print('COLLISION296',json.dumps(receipt),flush=True);bpy.data.objects.remove(obj,do_unlink=True)
(OUT/'receipts.json').write_text(json.dumps(receipts,indent=2),encoding='utf-8')
