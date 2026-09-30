"""Joint-bounded outcrop sheets. No smoothed ellipsoid/clay boulders."""
import math, random
from mathutils import Vector, noise

def clip(poly,n,d):
    result=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        da=a[0]*n[0]+a[1]*n[1]-d;db=b[0]*n[0]+b[1]*n[1]-d
        if da<=0:result.append(a)
        if (da<0)!=(db<0):
            t=da/(da-db);result.append((a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])))
    return result

def build_facades(route):
    rng=random.Random(280)
    # Stretched Voronoi domains give shared, irregular fracture boundaries.
    # Position axes are metres along the road and height above the road.
    seeds=[(z+rng.uniform(-4,4),y+rng.uniform(-5,5))
           for z in range(-28,365,9) for y in range(-6,85,13) if rng.random()>.18]
    sectors=[([],[]) for _ in range(6)]
    for index,(cz,cy) in enumerate(seeds):
        poly=[(-25,-9),(360,-9),(360,82),(-25,82)]
        for oz,oy in seeds:
            if (oz,oy)==(cz,cy):continue
            # Anisotropic joint orientation; not a rectangular brick grid.
            nz=oz-cz;ny=(oy-cy)*.55
            d=((oz*oz-cz*cz)+(oy*oy-cy*cy)*.55)*.5
            poly=clip(poly,(nz,ny),d)
            if not poly:break
        if len(poly)<3:continue
        # End regions taper into the base mountain instead of a level parapet.
        summit=55+14*math.sin(cz*.024)+9*math.sin(cz*.051+.7)
        poly=clip(poly,(0,1),summit)
        if len(poly)<3:continue
        mz=sum(p[0] for p in poly)/len(poly);my=sum(p[1] for p in poly)/len(poly)
        shrink=rng.uniform(.990,.997)
        poly=[(mz+(z-mz)*shrink,my+(y-my)*shrink) for z,y in poly]
        # Chipped fracture edges, shared world deformation. Smooth polygon
        # outlines would read as fitted paving slabs rather than torn bedrock.
        jagged=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            for j in range(4):
                t=j/4;z=a[0]*(1-t)+b[0]*t;y=a[1]*(1-t)+b[1]*t
                warp=noise.noise_vector(Vector((z*.73,y*.73,18)))
                jagged.append((z+warp.x*.25,y+warp.y*.25))
        poly=jagged
        sector=max(0,min(5,int((mz+24)/64)));vs,fs=sectors[sector]
        # Shared bedrock slope, offset blocks, and tilted shear faces. Front
        # surfaces remain broad plates; the edges split normals from side faces.
        setback=rng.uniform(-1.5,1.0);tiltz=rng.uniform(-.16,.16);tilty=rng.uniform(-.055,.07)
        depth=rng.uniform(4,9)
        def front(z,y):
            x=4.8+max(0,y)*.10+max(0,y-12)*.45+setback+tiltz*(z-mz)+tilty*(y-my)
            x=max(3.8,x)
            # Small chipped relief only; never globally round the joint faces.
            relief=noise.fractal(Vector((z*.8,y*.8,index*.07)),1,2,3)*.40
            return (x+relief,y,z)
        def world(v):
            x,y,z=v;rx,ry,_=route(max(0,min(330,z)))
            return (rx+x,ry+y,z)
        local=[];tri=[];lookup={}
        def vertex(z,y):
            key=(round(z,5),round(y,5))
            if key not in lookup:lookup[key]=len(local);local.append(front(z,y))
            return lookup[key]
        def subdiv(a,b,c,level):
            if level==0:tri.append((vertex(*a),vertex(*b),vertex(*c)));return
            ab=((a[0]+b[0])*.5,(a[1]+b[1])*.5);bc=((b[0]+c[0])*.5,(b[1]+c[1])*.5);ca=((c[0]+a[0])*.5,(c[1]+a[1])*.5)
            for t in ((a,ab,ca),(ab,b,bc),(ca,bc,c),(ab,bc,ca)):subdiv(*t,level-1)
        for a,b in zip(poly,poly[1:]+poly[:1]):subdiv((mz,my),a,b,2)
        off=len(vs);vs.extend(world(v) for v in local);fs.extend(tuple(off+i for i in f) for f in tri)
        # Side fracture faces have their own vertices so no soft normal bridges.
        for a,b in zip(poly,poly[1:]+poly[:1]):
            va=front(*a);vb=front(*b);off=len(vs)
            vs.extend(world(v) for v in (va,(va[0]+depth,va[1],va[2]),(vb[0]+depth,vb[1],vb[2]),vb))
            fs.extend([(off,off+1,off+2),(off,off+2,off+3)])
    return sectors
