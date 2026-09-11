from pathlib import Path
R=Path(__file__).resolve().parents[2];p=R/'Tools/SpellVFX120/build_stone_jangseung.py';s=p.read_text()
start=s.index('for poly in body.data.polygons:');end=s.index("mat=bpy.data.materials.new",start)
s=s[:start]+'''# Preserve Meshy face, replace the open architectural shaft with an organic monolith.
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
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(x,y+.015,z));eye=bpy.context.object;eye.scale=(.122,.105,.119);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);parts.append(eye)
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
''' +s[end:]
s=s.replace("l.new(mix.outputs[0],bs.inputs['Base Color'])", "l.new(ramp.outputs[0],bs.inputs['Base Color'])")
s=s.replace("l.new(nm.outputs[0],bump.inputs['Normal']);",'')
p.write_text(s);print('REFINED_BUILDER')
