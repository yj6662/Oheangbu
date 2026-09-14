import bpy,bmesh,math,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path

def rebuild_robe_upper():
    o=bpy.data.objects['Durumagi'];shirt=bpy.data.objects['InnerTop'];body=bpy.data.objects['Body']
    source=o.data.copy();source.name='Preserved_Meshy_Robe_LocalFit'
    source.use_fake_user=True;source.calc_loop_triangles()
    tris=list(source.loop_triangles);uv=source.uv_layers.active.data
    tree=BVHTree.FromPolygons([v.co for v in source.vertices],[t.vertices[:] for t in tris],all_triangles=True)
    bt=BVHTree.FromPolygons([v.co for v in body.data.vertices],[p.vertices[:] for p in body.data.polygons])
    bm=bmesh.new();bm.from_mesh(shirt.data)
    # Use the fitted shirt's shoulder/collar construction; shorten the outer sleeves.
    for co,no in [((0,0,1.115),(0,0,1)),((.535,0,0),(-1,0,0)),((-.535,0,0),(1,0,0))]:
        geom=list(bm.verts)+list(bm.edges)+list(bm.faces)
        bmesh.ops.bisect_plane(bm,geom=geom,dist=.000001,plane_co=co,plane_no=no,clear_inner=True,clear_outer=False)
    for v in bm.verts:
        p=v.co.copy();q,n,i,d=bt.find_nearest(p)
        if q is None:continue
        delta=n*.013
        ax=abs(p.x);t=max(0,min(1,(ax-.17)/.16));t=t*t*(3-2*t)
        # A broad sleeve hangs below the upper arm; upper shoulder support stays close.
        underside=max(0,min(1,(1.425-p.z)/.08))
        delta.z-=.07*t*underside
        delta.y+=(.016 if p.y>.075 else -.016)*t*underside
        v.co+=delta
    bm.normal_update()
    layer=bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    # Reuse original robe UV regions by a local affine transfer per face.
    for f in bm.faces:
        center=f.calc_center_median();co,no,idx,dist=tree.find_nearest(center)
        tri=tris[idx];a,b,c=[source.vertices[i].co for i in tri.vertices]
        ua,ub,uc=[uv[i].uv.copy() for i in tri.loops]
        u=b-a;w=c-a;den=u.dot(u)*w.dot(w)-u.dot(w)**2
        for l in f.loops:
            r=l.vert.co-a
            x=(w.dot(w)*r.dot(u)-u.dot(w)*r.dot(w))/den if abs(den)>1e-12 else 0
            y=(u.dot(u)*r.dot(w)-u.dot(w)*r.dot(u))/den if abs(den)>1e-12 else 0
            # Keep interpolation within the source triangle to avoid empty atlas space.
            x=max(0,min(1,x));y=max(0,min(1-x,y));l[layer].uv=ua*(1-x-y)+ub*x+uc*y
        f.smooth=True
    upper=bpy.data.meshes.new('Fitted_Shoulder_Robe_Upper');bm.to_mesh(upper);bm.free()
    # Retain original long panels and openings below the upper-torso seam.
    lower=bmesh.new();lower.from_mesh(source)
    bmesh.ops.bisect_plane(lower,geom=list(lower.verts)+list(lower.edges)+list(lower.faces),dist=.000001,plane_co=(0,0,1.13),plane_no=(0,0,1),clear_outer=True,clear_inner=False)
    # Join the two pieces in one garment object with a 15 mm sewn overlap.
    newmesh=bpy.data.meshes.new('Durumagi_Fitted_SeparatedLayers')
    lower.to_mesh(newmesh);lower.free()
    merged=bmesh.new();merged.from_mesh(newmesh);merged.from_mesh(upper)
    merged.to_mesh(newmesh);merged.free()
    newmesh.materials.clear()
    for mat in source.materials:newmesh.materials.append(mat)
    o.data=newmesh
    for p in newmesh.polygons:p.use_smooth=True
    newmesh.calc_loop_triangles()
    print('Rebuilt robe upper:',len(newmesh.vertices),'verts',len(newmesh.loop_triangles),'tris')
