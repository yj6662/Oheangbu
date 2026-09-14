"""Add a short closed wrist-wrap collar, hiding the separate skin cap inside cloth."""
import bpy,json,math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT='C:/Users/yj666/Oheangbu';m=bpy.data.objects['Dosa_Body'];r=bpy.data.objects['Dosa_Rig'];s=bpy.context.scene
if not m.get('wrist_wrap_collar'):
    m.data.calc_loop_triangles();source=list(m.data.loop_triangles);tree=BVHTree.FromPolygons([v.co for v in m.data.vertices],[t.vertices for t in source],all_triangles=True);sourceuv=m.data.uv_layers['uv']
    spec=json.load(open(ROOT+'/Art/Player/refinement-report.json'))['hands'];verts=[];faces=[];uvs=[];weights=[]
    for side in ['Right','Left']:
        hs=spec[side];wrist=Vector(hs['wrist']);d=Vector(hs['length_axis']);w=Vector(hs['width_axis']);n=Vector(hs['dorsal_axis'])
        target=wrist-d*.07
        candidates=[p for p in m.data.polygons if p.material_index==0]
        p=min(candidates,key=lambda p:(p.center-target).length_squared)
        atlas=m.data.uv_layers['uv'];center=sum((atlas.data[li].uv for li in p.loop_indices),Vector((0,0)))/len(p.loop_indices)
        offset=len(verts);rings=[(-.078,.040),(-.063,.041),(-.050,.039),(-.030,.033),(-.013,.030),(.004,.030)]
        for t,rad in rings:
            for j in range(32):
                a=j*math.tau/32;pt=wrist+d*t+w*(math.cos(a)*rad)+n*(math.sin(a)*rad*.72)
                verts.append(tuple(pt));weights.append(side+'Hand');uvs.append((center.x+(j/31-.5)*.008,center.y+(t+.04)*.08))
        for k in range(len(rings)-1):
            for j in range(32):faces.append((offset+k*32+j,offset+k*32+(j+1)%32,offset+(k+1)*32+(j+1)%32,offset+(k+1)*32+j))
    mesh=bpy.data.meshes.new('Dosa_WristWrap');mesh.from_pydata(verts,[],faces);mesh.update();obj=bpy.data.objects.new('Dosa_WristWrap',mesh);s.collection.objects.link(obj);mesh.materials.append(m.data.materials[0]);uv=mesh.uv_layers.new(name='uv')
    for p in mesh.polygons:
        p.use_smooth=True
        for li in p.loop_indices:
            pos=mesh.vertices[mesh.loops[li].vertex_index].co;near,normal,index,dist=tree.find_nearest(pos);t=source[index];co=[m.data.vertices[v].co for v in t.vertices];tuv=[Vector((*sourceuv.data[l].uv,0)) for l in t.loops];uv.data[li].uv=barycentric_transform(near,*co,*tuv).xy
    for side in ['Right','Left']:
        group=obj.vertex_groups.new(name=side+'Hand');group.add([i for i,w in enumerate(weights) if w==side+'Hand'],1,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT');m.select_set(True);obj.select_set(True);bpy.context.view_layer.objects.active=m;bpy.ops.object.join();m['wrist_wrap_collar']=True
    print('wrist wrap added',len(verts),'vertices')
