import bpy,bmesh,json
from pathlib import Path
from mathutils import Matrix,Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ClothMCP')
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];r=bpy.data.objects['Durumagi'];p=bpy.data.objects['ClothProxy_Durumagi']
a.animation_data.action=None
for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
s.frame_set(0);s.frame_set(1);bpy.context.view_layer.update()
bpy.context.view_layer.objects.active=r;sd=next(m for m in r.modifiers if m.type=='SURFACE_DEFORM')
if sd.is_bound:bpy.ops.object.surfacedeform_bind(modifier=sd.name)
bm=bmesh.new();bm.from_mesh(r.data)
before=(len(bm.verts),len(bm.faces))
selected=[f for f in bm.faces if f.calc_center_median().y<-.08 and f.calc_center_median().z<1.025]
geom=set(selected)
for f in selected:geom.update(f.edges);geom.update(f.verts)
cut=bmesh.ops.bisect_plane(bm,geom=list(geom),dist=.000001,plane_co=Vector((0,0,0)),plane_no=Vector((1,0,0)),clear_inner=False,clear_outer=False)
edges=[e for e in cut['geom_cut'] if isinstance(e,bmesh.types.BMEdge)]
if edges:bmesh.ops.split_edges(bm,edges=edges)
shifted=0
for v in bm.verts:
 x,y,z=v.co
 if y>=-.08 or z>=1.025 or abs(x)>=.075:continue
 sign=1 if x>1e-6 else -1 if x< -1e-6 else (1 if sum(f.calc_center_median().x for f in v.link_faces)>=0 else -1)
 blend=max(0,min(1,(1.025-z)/.12))*max(0,1-abs(x)/.075)
 v.co.x+=sign*.028*blend;shifted+=1
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));after=(len(bm.verts),len(bm.faces));bm.to_mesh(r.data);bm.free();r.data.update()
bpy.context.view_layer.update();bpy.ops.object.surfacedeform_bind(modifier=sd.name)
assert sd.is_bound
report={'cause':'Front-facing source faces crossed x=0 and bound to opposite edges of the open cloth driver, stretching up to 0.423m in Run frame 11.','repair':'Bisected and split the lower front seam, preserved UV interpolation, opened the seam by up to 28mm per side with a tapered transition.','before_vertices_faces':before,'after_vertices_faces':after,'shifted_vertices':shifted}
(OUT/'front_seam_repair.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_ClothMCP.blend'),compress=True)
print(json.dumps(report))
