"""C1: additive skinned eyelids. Original C02 vertices/UV/weights are untouched.
Run only in a separate background process using Face_Baseline.blend.
"""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face'
assert (ROOT/'Docs/Specs/SPEC-C02-RIG-FACE-LAB.md').is_file()
ns={'__name__':'face_helpers'};exec(compile((ROOT/'Tools/Blender/C02_RigFaceLab/face_inspect.py').read_text(encoding='utf-8'),'face_inspect.py','exec'),ns)
body=bpy.data.objects['C02_Mesh_0'];rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
rig.animation_data_clear();rig.data.pose_position='REST'
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
body.data.calc_loop_triangles();tris=list(body.data.loop_triangles)
world=[body.matrix_world@v.co for v in body.data.vertices]
bvh=BVHTree.FromPolygons(world,[t.vertices[:] for t in tris],all_triangles=True)
uv=body.data.uv_layers.active.data
def sample(x,z):
    p,n,i,d=bvh.ray_cast(Vector((x,-1,z)),Vector((0,1,0)),2)
    if p is None:raise RuntimeError('Face sample missed')
    t=tris[i];a,b,c=[world[j] for j in t.vertices]
    ta,tb,tc=[Vector((*uv[j].uv,0)) for j in t.loops]
    tex=barycentric_transform(p,a,b,c,ta,tb,tc)
    return p,tex[:2]
def build_lid(side,cx,zc):
    name='FaceLid_'+side
    if name in bpy.data.objects:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
    basis=[];closed=[];tex=[];faces=[];nx,ny=32,8
    for j in range(ny+1):
      t=j/ny
      for i in range(nx+1):
        u=-1+2*i/nx;bell=math.sqrt(max(0,1-u*u));x=cx+u*.0130
        slope=(1 if cx>0 else -1)*u*.00055
        upper=zc+.00325*bell+slope;lower=zc-.0033*bell+slope
        zo=upper-.00012*t;zc1=upper+(lower-upper)*t
        po,_=sample(x,zo);pc,_=sample(x,zc1)
        po.y-=.00028;pc.y-=.0003+.00035*math.sin(math.pi*t)*bell
        basis.append(list(po));closed.append(list(pc))
        # Skin samples below the eye; never stretch the painted iris into a lid.
        _,tuv=sample(x,zc-.0050-.0055*(1-t)*bell);tex.append(tuv)
    for j in range(ny):
      for i in range(nx):
        a=j*(nx+1)+i;b=a+1;c=b+nx+1;d=a+nx+1;faces.append((a,d,c,b))
    m=bpy.data.meshes.new(name);m.from_pydata(basis,[],faces);m.update()
    o=bpy.data.objects.new(name,m);bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(body.data.materials[0]);layer=m.uv_layers.new(name='UVMap')
    for poly in m.polygons:
        poly.use_smooth=True
        for li in poly.loop_indices:layer.data[li].uv=tex[m.loops[li].vertex_index]
    # Coordinates are world metres. Match the existing body object basis exactly.
    inv=body.matrix_world.inverted()
    for v in m.vertices:v.co=inv@v.co
    o.matrix_world=body.matrix_world.copy()
    group=o.vertex_groups.new(name='Head');group.add(list(range(len(m.vertices))),1,'REPLACE')
    mod=o.modifiers.new('Armature','ARMATURE');mod.object=rig
    o.shape_key_add(name='Basis');key=o.shape_key_add(name='Blink'+side)
    for v,p in zip(key.data,closed):v.co=inv@Vector(p)
    key.slider_min=0;key.slider_max=1
    o['face_lab_role']='eyelid';o['face_lab_candidate']='C1';o['source_body_unchanged']=True
    return o
lids=[build_lid('Left',.02664062567,1.597148418),build_lid('Right',-.02749999985,1.597148418)]
folder=OUT/'C1';folder.mkdir(exist_ok=True)
manifest={'candidate':'C1','stage':'C','attempt':1,'method':'Additive skinned eyelid curtains fitted to raycast face surface. Painted eye geometry remains static beneath them.',
 'body_mesh_modified':False,'body_vertex_changes':[],'body_weights_modified':False,'new_bones':0,
 'added_triangles':sum(len(o.data.polygons)*2 for o in lids),'total_triangles':len(body.data.loop_triangles)+sum(len(o.data.polygons)*2 for o in lids),
 'new_meshes':[o.name for o in lids],'channels':['BlinkLeft','BlinkRight'],'range_blender':[0,1],'range_unity':[0,100],
 'face_blink':'UNVERIFIED_PENDING_RENDER','face_gaze':{'status':'NOT_ATTEMPTED','reason':'No independent eyeball geometry in source'},'face_jaw':{'status':'NOT_ATTEMPTED','reason':'Closed fused mouth; no mouth cavity established'},
 'neutral_source_face_geometry_exact':True,'paid_requests':0}
(folder/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
for value,label in [(0,'open'),(.5,'half'),(1,'closed')]:
    for o in lids:o.data.shape_keys.key_blocks[1].value=value
    ns['render']('C1_'+label+'_front',(0,-1,0))
for o in lids:o.data.shape_keys.key_blocks[1].value=0
rig.data.pose_position='POSE';bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'Face_C1.blend'))
print('FACE_C1_BUILT',json.dumps(manifest))
