"""Isolated mountain ascent: continuous trail, jointed cliff mesh and valley ridges."""
import bpy, bmesh, math, json, random
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree
from pathlib import Path
GEOMETRY={}
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Ascent278';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def route(z):
    return (42*math.sin(z*.031)+.19*z,22+.265*z+2*math.sin(z*.04),z)
def write_mesh(name,vertices,faces,rough=False):
    GEOMETRY[name]=(vertices,faces)
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(x,-z,y) for x,y,z in vertices],[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    for f in mesh.polygons:f.use_smooth=not rough
    mesh.calc_loop_triangles()
    data=dict(vertices=[],normals=[],uv=[],triangles=[])
    for tri in mesh.loop_triangles:
        off=len(data['vertices'])
        for li in tri.loops:
            v=mesh.vertices[mesh.loops[li].vertex_index].co;n=mesh.corner_normals[li].vector
            data['vertices'].append(dict(x=v.x,y=v.z,z=-v.y))
            data['normals'].append(dict(x=n.x,y=n.z,z=-n.y))
            data['uv'].append(dict(x=v.x*.25,y=-v.y*.25))
        data['triangles'].extend([off,off+1,off+2])
    (OUT/(name+'.json')).write_text(json.dumps(data,separators=(',',':')))
    return len(data['triangles'])//3

# The full shelf is continuous. It is not a set of disconnected rock platforms.
offsets=[-185,-140,-100,-70,-45,-28,-18,-11,-7,-4.8,-3.25,0,3.25,5,7,10,14,20,28,40,58,82,115,150,190]
heights=[-130,-115,-100,-85,-62,-44,-25,-12,-3.6,-.7,-.22,-.22,-.22,1.0,8,20,28,32,44,62,72,62,35,5,-38]
counts={}
for sector in range(6):
    zs=[-24+sector*64+i for i in range(65)]
    vertices=[];faces=[]
    for z in zs:
        cx,cy,_=route(max(0,min(330,z)))
        for j,(offset,base) in enumerate(zip(offsets,heights)):
            p=Vector((offset*.065,z*.065,1.7))
            f=noise.noise_vector(p)[0]
            irregular=math.sin(z*.14+j*1.71)*.9+f*1.8
            h=base
            if abs(offset)>5:
                h+=irregular
                if offset>7:
                    summits=sum(a*math.exp(-((z-c)/w)**2) for c,w,a in [(35,26,18),(115,32,38),(220,32,26),(307,27,17)])
                    h=h*(.76+.18*math.sin(z*.027))+summits*min(1,(offset-7)/25)+4*math.sin(z*.075+offset*.17)
            # Lower inn terrace and upper saddle are natural wider shoulders.
            if z<25 and -45<=offset<=5:
                t=max(0,min(1,(25-z)/14));h=h*(1-t)+(-.25)*t
            if z>305 and -10<=offset<=12:
                t=max(0,min(1,(z-305)/18));h=h*(1-t)+(-.25)*t
            x=cx+offset+(f*1.8 if abs(offset)>7 else 0)
            if 7<offset<65:
                x+=3.8*math.sin(z*.11)+2.0*math.sin(h*.17+z*.075)+2*f
                x=max(cx+5.4,x)
            vertices.append((x,cy+h,z))
    cols=len(offsets)
    for r in range(len(zs)-1):
        for c in range(cols-1):
            a=r*cols+c;faces.extend([(a,a+cols,a+1),(a+1,a+cols,a+cols+1)])
    counts['Cliff_'+str(sector)]=write_mesh('Cliff_'+str(sector),vertices,faces)
    collision=[]
    for r in range(len(zs)-1):
        for c in range(cols-1):
            # The trail owns the central walking collision. Avoid two floors 22cm apart.
            if c in (10,11) and zs[r]>=0 and zs[r+1]<=330:continue
            a=r*cols+c;collision.extend([(a,a+cols,a+1),(a+1,a+cols,a+cols+1)])
    write_mesh('Cliff_'+str(sector)+'_Collision',vertices,collision,True)
    bpy.data.objects['Cliff_'+str(sector)+'_Collision'].hide_render=True
    bpy.data.objects['Cliff_'+str(sector)+'_Collision'].hide_set(True)

# Fine, deliberately traversable ribbon. Stones and scree are separate dressing.
vertices=[];faces=[]
for i in range(661):
    z=i*.5;cx,cy,_=route(z)
    for j in range(9):
        offset=(j/8-.5)*6.2
        irregular=(math.sin(z*1.13+offset*.9)*.017+math.sin(offset*2.1+z*.7)*.014)
        edge=abs(offset)/3.1;offset+=math.copysign((.20*math.sin(z*.47)+.12*math.sin(z*1.23))*edge**5,offset)
        vertices.append((cx+offset,cy+irregular,z))
for r in range(660):
    for c in range(8):
        a=r*9+c;faces.extend([(a,a+9,a+1),(a+1,a+9,a+10)])
counts['Trail']=write_mesh('Trail',vertices,faces)
# Collision has no cosmetic bumps or narrow micro-triangles.
vertices=[];faces=[]
for i in range(166):
    z=i*2;cx,cy,_=route(z)
    vertices.extend([(cx-3.1,cy,z),(cx+3.1,cy,z)])
for r in range(165):
    a=r*2;faces.extend([(a,a+2,a+1),(a+1,a+2,a+3)])
write_mesh('Trail_Collision',vertices,faces)
bpy.data.objects['Trail_Collision'].hide_render=True
bpy.data.objects['Trail_Collision'].hide_set(True)


# Valley silhouettes are actual mesh ridges with progressively broader erosion.
for k in range(3):
    vertices=[];faces=[];nx=65;nz=57
    for iz in range(nz):
        z=-450+iz*27
        for ix in range(nx):
            x=-370-k*450+(ix/(nx-1)-.5)*480
            spine=-370-k*450+70*math.sin(z*.007+k)
            ridge=max(0,1-abs(x-spine)/260)**1.4
            h=-150+ridge*(160+65*math.sin(z*.009+k)+26*math.sin(z*.027))
            h+=noise.fractal(Vector((x*.009,z*.009,k)),1,2,3)*18*ridge
            vertices.append((x,h,z))
    for iz in range(nz-1):
        for ix in range(nx-1):
            a=iz*nx+ix;faces.extend([(a,a+nx,a+1),(a+1,a+nx,a+nx+1)])
    counts['Ridge_'+str(k)]=write_mesh('Ridge_'+str(k),vertices,faces)
# 279: convex fracture volumes with staggered joints. World-space vertices are
# warped to the road alignment so long blocks cannot cut across a bend.
def block_shape(seed):
    rng=random.Random(seed);bm=bmesh.new()
    for x in (-1,1):
        for y in (-1,1):
            for z in (-1,1):
                for axis in range(3):
                    p=[x,y,z];p[axis]*=rng.uniform(.55,.82)
                    bm.verts.new(tuple(v*rng.uniform(.90,1.04) for v in p))
    bm.verts.ensure_lookup_table()
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.verts.ensure_lookup_table();bm.verts.index_update()
    vs=[tuple(v.co) for v in bm.verts];fs=[tuple(v.index for v in f.verts) for f in bm.faces];bm.free();return vs,fs
shapes=[block_shape(2790+i) for i in range(8)]
for i,(vs,fs) in enumerate(shapes):write_mesh('FractureKit_'+str(i),vs,fs,True)
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
from cliff_mass281 import build_facades
for sector,(vs,fs) in enumerate(build_facades(route)):
    counts['FractureFacade_'+str(sector)]=write_mesh('FractureFacade_'+str(sector),vs,fs)
# Bake low-frequency geometric contact shade into vertex colour. This is
# actual occlusion against the assembled cliff geometry, not painted black lines.
aov=[];aof=[]
for name,(vs,fs) in GEOMETRY.items():
    if (name.startswith('Cliff_') and not name.endswith('Collision')) or name.startswith('FractureFacade_'):
        start=len(aov);aov.extend(Vector(v) for v in vs);aof.extend(tuple(start+i for i in f) for f in fs)
bvh=BVHTree.FromPolygons(aov,aof,all_triangles=False)
for name in list(GEOMETRY):
    if not ((name.startswith('Cliff_') and not name.endswith('Collision')) or name.startswith('FractureFacade_')):continue
    file=OUT/(name+'.json');data=json.loads(file.read_text());colors=[]
    for v,n in zip(data['vertices'],data['normals']):
        p=Vector((v['x'],v['y'],v['z']));normal=Vector((n['x'],n['y'],n['z'])).normalized()
        axis=Vector((0,1,0)) if abs(normal.y)<.85 else Vector((1,0,0))
        tangent=normal.cross(axis).normalized();bitangent=normal.cross(tangent)
        occ=0
        for offset in [tangent*.8,-tangent*.8,bitangent*.8,-bitangent*.8]:
            direction=(normal+offset).normalized();hit=bvh.ray_cast(p+normal*.08,direction,8)
            if hit[0] is not None:occ+=1-min(1,hit[3]/8)*.65
        shade=max(.32,1-occ/4)
        colors.append(dict(r=shade,g=shade,b=shade,a=1))
    data['colors']=colors;file.write_text(json.dumps(data,separators=(',',':')))
points=[dict(x=route(i)[0],y=route(i)[1],z=i) for i in range(331)]
length=sum((Vector(tuple(points[i].values()))-Vector(tuple(points[i-1].values()))).length for i in range(1,len(points)))
(OUT/'route.json').write_text(json.dumps(dict(points=points,length=length,rise=points[-1]['y']-points[0]['y']),indent=2))
(OUT/'blender-report.json').write_text(json.dumps(counts,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CliffAscent278.blend'))
print('ASCENT278',length,counts)
