"""Offline authored SDF cave: irregular chambers, open mouth and separate floor.

Coordinates in geometry.json are Unity local x/y/z (metres); triangle geometric
normals point into the air. This script never opens Unity or edits a scene.
Run with Python 3 + NumPy. Blender is optional, for the editable source asset.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import time

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/WorldMacro/Playtest/NaturalCave"
FLOOR = .03
# Centres, radii and plan rotation are deliberately asymmetric. Radii describe
# air volume, not a stone shell. Overlapping volumes merge with a soft minimum.
CHAMBERS = [
    ("open_mouth", (0, 2.3, -28), (8.4, 5.9, 9.2), -8),
    ("entry_turn", (-8.5, 2.5, -15), (8.8, 5.8, 9.5), -22),
    ("entry_diagonal_link", (-16.5, 2.7, -7.5), (10.7, 6.3, 9.3), -24),
    ("low_bend", (-25, 3.3, -1), (13.4, 7.0, 10.1), 12),
    ("broken_gallery", (-44, 4.1, 5), (15.0, 8.6, 11.5), -16),
    ("main_chamber", (-73, 5.1, 5), (25.5, 11.2, 20.0), 9),
    ("narrow_gallery", (-104, 3.1, -3), (16.0, 7.2, 10.1), -13),
    ("starting_chamber", (-134, 5.0, 2), (24.0, 11.4, 18.7), -7),
    ("terminal_recess", (-153, 3.4, 3), (13.0, 8.1, 11.7), 21),
    ("north_shoulder", (-57, 3.4, 14), (11.5, 6.4, 10.5), 26),
    ("south_recess", (-84, 2.8, -10), (12.4, 6.7, 9.8), -19),
    ("start_side_ledge", (-127, 2.7, 14), (10.7, 6.0, 10.2), 17),
]
CENTERLINE = [(0,FLOOR,-26), (-9,FLOOR,-15), (-26,FLOOR,-1),
              (-43,FLOOR,5), (-74,FLOOR,5), (-103,FLOOR,-3),
              (-134,FLOOR,2), (-152,FLOOR,3)]


def smooth_min(a, b, k=2.15):
    h = np.maximum(k - np.abs(a-b), 0) / k
    return np.minimum(a, b) - h*h*k*.25


def field(x, y, z):
    """Negative in air. Ellipsoid distance approximation plus eroded strata."""
    result = None
    for _, c, r, angle in CHAMBERS:
        angle = math.radians(angle)
        dx, dz = x-c[0], z-c[2]
        qx = dx*math.cos(angle) + dz*math.sin(angle)
        qz = -dx*math.sin(angle) + dz*math.cos(angle)
        qy = y-c[1]
        k0 = np.sqrt((qx/r[0])**2+(qy/r[1])**2+(qz/r[2])**2)
        k1 = np.sqrt((qx/(r[0]**2))**2+(qy/(r[1]**2))**2+(qz/(r[2]**2))**2)
        d = k0*(k0-1)/np.maximum(k1, 1e-5)
        result = d if result is None else smooth_min(result, d)
    # Large skewed shelves plus smaller broken bedding. Frequencies are not
    # aligned to the path or polar angle; no repeated radial sections result.
    broad = .72*np.sin(x*.131+z*.191)*np.cos(y*.244-z*.089)
    broad += .41*np.sin(x*.283-z*.153+y*.417)
    broad += .25*np.cos(x*.49+z*.31-y*.17)
    bedding = .20*np.sin(y*1.76+x*.061+z*.083)
    grit = .105*np.sin(x*1.39+z*.77)*np.sin(y*1.17-z*.36)
    return result + broad + bedding + grit


def extract_tetra(xs, ys, zs):
    """Marching tetrahedra with edge-key welding and inside-facing winding."""
    x,y,z = np.meshgrid(xs,ys,zs,indexing="ij")
    values = field(x,y,z).astype(np.float32)
    shape = values.shape
    offsets = np.array([(0,0,0),(1,0,0),(1,1,0),(0,1,0),
                        (0,0,1),(1,0,1),(1,1,1),(0,1,1)], np.int32)
    corners = [values[o[0]:shape[0]-1+o[0],o[1]:shape[1]-1+o[1],o[2]:shape[2]-1+o[2]]
               for o in offsets]
    lo = np.minimum.reduce(corners); hi = np.maximum.reduce(corners)
    active = np.stack(np.where((lo<=0)&(hi>=0)),axis=1)
    del x,y,z,lo,hi,corners
    print(f"Volume grid {shape}: {len(active):,} active surface cells",flush=True)
    tets = ((0,5,1,6),(0,1,2,6),(0,2,3,6),(0,3,7,6),(0,7,4,6),(0,4,5,6))
    edges = ((0,1),(0,2),(0,3),(1,2),(1,3),(2,3))
    vertices = []
    triangles = []
    edge_cache = {}
    nz, ny = shape[2], shape[1]
    linear_offsets = offsets[:,0]*ny*nz+offsets[:,1]*nz+offsets[:,2]
    positions = np.stack(np.meshgrid(xs,ys,zs,indexing="ij"),axis=-1).reshape(-1,3)
    vf = values.ravel()
    # Cases have explicit polygon order, not arbitrary edge order.
    cases = {}
    for mask in range(1,15):
        inside = [j for j in range(4) if mask&(1<<j)]
        outside = [j for j in range(4) if not mask&(1<<j)]
        if len(inside)==1:
            polygon = [(inside[0],j) for j in outside]
        elif len(inside)==3:
            polygon = [(outside[0],j) for j in inside]
        else:
            a,b=inside; c,d=outside
            polygon=[(a,c),(a,d),(b,d),(b,c)]
        cases[mask]=polygon
    for origin in active:
        ids = (origin[0]*ny*nz+origin[1]*nz+origin[2])+linear_offsets
        for tet in tets:
            ti = ids[list(tet)]
            tv = vf[ti]
            mask = sum(1<<j for j in range(4) if tv[j]<0)
            if mask in (0,15): continue
            poly=[]
            for a,b in cases[mask]:
                ia,ib=int(ti[a]),int(ti[b])
                key=(min(ia,ib),max(ia,ib))
                idx=edge_cache.get(key)
                if idx is None:
                    t=float(tv[a]/(tv[a]-tv[b]))
                    point=positions[ia]+t*(positions[ib]-positions[ia])
                    idx=len(vertices); vertices.append(point)
                    edge_cache[key]=idx
                poly.append(idx)
            triangles.append(poly[:3])
            if len(poly)==4: triangles.append([poly[0],poly[2],poly[3]])
    vv=np.asarray(vertices,dtype=np.float32); ff=np.asarray(triangles,dtype=np.int32)
    p=vv[ff]
    normals=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0])
    lengths=np.linalg.norm(normals,axis=1)
    valid=lengths>1e-8
    ff=ff[valid]; normals=normals[valid]/lengths[valid,None]
    centers=vv[ff].mean(axis=1)
    plus=centers+normals*.06; minus=centers-normals*.06
    outward=field(plus[:,0],plus[:,1],plus[:,2])>field(minus[:,0],minus[:,1],minus[:,2])
    ff[outward]=ff[outward][:,[0,2,1]]
    print(f"Interior: {len(vv):,} vertices, {len(ff):,} triangles",flush=True)
    return vv,ff


def floor_mesh(xs,zs):
    """Clipped floor follows the exact air silhouette at the open lower rim."""
    xx,zz=np.meshgrid(xs,zs,indexing="ij")
    val=field(xx,np.full_like(xx,FLOOR),zz)
    vv=[]; ff=[]; cache={}
    def vertex(p):
        k=(round(float(p[0]),5),round(float(p[2]),5))
        if k not in cache: cache[k]=len(vv); vv.append(p)
        return cache[k]
    for i in range(len(xs)-1):
        for j in range(len(zs)-1):
            coords=[(i,j),(i+1,j),(i+1,j+1),(i,j+1)]
            points=[np.array((xs[a],FLOOR,zs[b]),np.float32) for a,b in coords]
            vals=[float(val[a,b]) for a,b in coords]
            for ids in ((0,1,2),(0,2,3)):
                clipped=[]
                for k in range(3):
                    a,b=ids[k],ids[(k+1)%3]
                    if vals[a]<=0: clipped.append(points[a])
                    if (vals[a]<=0)!=(vals[b]<=0):
                        clipped.append(points[a]+(points[b]-points[a])*(vals[a]/(vals[a]-vals[b])))
                if len(clipped)<3:continue
                ids2=[vertex(p) for p in clipped]
                for k in range(1,len(ids2)-1): ff.append((ids2[0],ids2[k+1],ids2[k]))
    vv=np.asarray(vv,np.float32); ff=np.asarray(ff,np.int32)
    print(f"Floor: {len(vv):,} vertices, {len(ff):,} triangles",flush=True)
    return vv,ff


def normals_for(v,f):
    n=np.zeros_like(v)
    p=v[f]; fn=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0])
    for i in range(3): np.add.at(n,f[:,i],fn)
    n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-9)
    return n


def contours(v,f):
    edges={}
    for t in f:
        for a,b in ((t[0],t[1]),(t[1],t[2]),(t[2],t[0])):
            key=tuple(sorted((int(a),int(b))))
            if key in edges:edges[key]=None
            else:edges[key]=(int(a),int(b))
    todo={a:b for e in edges.values() if e is not None for a,b in [e]}
    result=[]
    while todo:
        first=next(iter(todo)); at=first; line=[]
        while at in todo:
            line.append(at);at=todo.pop(at)
            if at==first:break
        if len(line)>2:
            result.append([{"x":round(float(v[k,0]),4),"z":round(float(v[k,2]),4)} for k in line])
    result.sort(key=len,reverse=True)
    return result


def mesh_record(name,v,f):
    n=normals_for(v,f)
    return {"name":name,"vertices":[dict(zip(('x','y','z'),map(lambda a:round(float(a),5),p))) for p in v],
            "normals":[dict(zip(('x','y','z'),map(lambda a:round(float(a),6),p))) for p in n],
            "triangles":f.ravel().tolist(),"vertexCount":len(v),"triangleCount":len(f)}


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--spacing',type=float,default=.78)
    ap.add_argument('--out',type=Path,default=OUT);args=ap.parse_args()
    args.out.mkdir(parents=True,exist_ok=True)
    started=time.time()
    xs=np.arange(-169,15+args.spacing,args.spacing,dtype=np.float32)
    ys=np.arange(FLOOR,22+args.spacing,args.spacing,dtype=np.float32)
    zs=np.arange(-26,33+args.spacing,args.spacing,dtype=np.float32)
    wall_v,wall_f=extract_tetra(xs,ys,zs)
    floor_v,floor_f=floor_mesh(xs,zs)
    # A floor need not retain the extraction grid. Keep source dense for exact
    # connectivity; optional Blender decimation provides the runtime derivative.
    allv=np.concatenate((wall_v,floor_v))
    bounds={"min":dict(zip(('x','y','z'),map(float,allv.min(axis=0)))),
            "max":dict(zip(('x','y','z'),map(float,allv.max(axis=0))))}
    samples=[]
    for x in np.arange(-165,11,2):
        mask=np.abs(wall_v[:,0]-x)<=1.4
        if mask.any():
            p=wall_v[mask]
            samples.append({"x":float(x),"minZ":float(p[:,2].min()),"maxZ":float(p[:,2].max()),"roofY":float(p[:,1].max())})
    doc={"schema":"natural-cave-unity-local-v1","floorY":FLOOR,"mouthPlaneZ":-26,
         "spacing":args.spacing,"bounds":bounds,"meshes":[mesh_record('Natural_Cave_Interior',wall_v,wall_f),
           mesh_record('Natural_Cave_Floor',floor_v,floor_f)],
         "footprintContours":contours(floor_v,floor_f),"envelopeSamples":samples,
         "centerline":[dict(zip(('x','y','z'),map(float,p))) for p in CENTERLINE],
         "chambers":[{"name":name,"center":dict(zip(('x','y','z'),c)),"airRadii":dict(zip(('x','y','z'),r)),"yaw":a} for name,c,r,a in CHAMBERS],
         "notes":["Interior surface normals face air; not an exterior cave shell.",
           "Mouth open at local z=-26. Bottom open at y=.03; separate connected walk floor.",
           "No textures included: assign rock triplanar material in Unity. No new generation services used.",
           "Width and height vary independently. Asymmetric bends and side recesses preserve a walkable centerline.",
           "Envelope is air cavity; mountain cover and its outer silhouette are an integration responsibility."],
         "elapsedSeconds":round(time.time()-started,3)}
    p=args.out/'geometry.json';p.write_text(json.dumps(doc,separators=(',',':')),encoding='utf-8')
    manifest={k:v for k,v in doc.items() if k not in ('meshes','footprintContours')}
    manifest['meshStatistics']=[{k:m[k] for k in ('name','vertexCount','triangleCount')} for m in doc['meshes']]
    manifest['footprintContourCount']=len(doc['footprintContours'])
    manifest['source']='Tools/Art/build_natural_cave.py'
    (args.out/'geometry_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(p,flush=True)


if __name__=='__main__':main()
