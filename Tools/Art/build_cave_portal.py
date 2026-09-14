"""Boolean stone mountain surface minus the authored irregular cavern air volume.
Uses measured original scene ground; no scene editing or image generation.
"""
import json, math, time
from pathlib import Path
import numpy as np
import build_natural_cave as cave
OUT=cave.OUT
d=json.loads((OUT/'portal_original_height.json').read_text(encoding='utf-8-sig'))
xs=np.array(d['xs'],dtype=np.float32);zs=np.array(d['zs'],dtype=np.float32)
h=np.array(d['heights'],dtype=np.float32).reshape(len(xs),len(zs))
air=cave.field
def original(x,z):
    xi=np.clip((x-xs[0])/(xs[1]-xs[0]),0,len(xs)-1.00001)
    zi=np.clip((z-zs[0])/(zs[1]-zs[0]),0,len(zs)-1.00001)
    ix=xi.astype(np.int32);iz=zi.astype(np.int32);a=xi-ix;b=zi-iz
    return (h[ix,iz]*(1-a)+h[ix+1,iz]*a)*(1-b)+(h[ix,iz+1]*(1-a)+h[ix+1,iz+1]*a)*b
def solid(x,y,z):
    # One broad foothill shoulder; exactly zero displacement at the terrain patch perimeter.
    edge=np.minimum.reduce([np.clip((x-xs[0])/8,0,1),np.clip((xs[-1]-x)/8,0,1),np.clip((z-zs[0])/8,0,1),np.clip((zs[-1]-z)/8,0,1)])
    edge=edge*edge*(3-2*edge)
    height=original(x,z)+28*np.exp(-((x+52)/22)**2-((z-4)/20)**2)*edge
    # Allow for the 1m extraction interpolation error; the preserved inner lining stays in front.
    return np.maximum(y-height,-air(x,y,z)-.30)
cave.field=solid
ys=np.arange(-4,math.ceil(float(h.max()))+19,1,dtype=np.float32)
start=time.time();v,f=cave.extract_tetra(xs,ys,zs);f=f[:,[0,2,1]]
centres=v[f].mean(axis=1);face=np.cross(v[f[:,1]]-v[f[:,0]],v[f[:,2]]-v[f[:,0]]);face/=np.maximum(np.linalg.norm(face,axis=1)[:,None],1e-9)
rock=(np.abs(air(centres[:,0],centres[:,1],centres[:,2]))<3.3)|(face[:,1]<.5)
normals=cave.normals_for(v,f)
records=[]
for name,indices in [('Solid_Mountain_Portal_Rock',f[rock]),('Solid_Mountain_Portal_Terrain',f[~rock])]:
    used,remap=np.unique(indices.ravel(),return_inverse=True);rec=cave.mesh_record(name,v[used],remap.reshape(-1,3))
    rec['normals']=[dict(zip(('x','y','z'),map(float,p))) for p in normals[used]];records.append(rec)
(OUT/'portal_solid_geometry.json').write_text(json.dumps({'meshes':records},separators=(',',':')),encoding='utf-8')
(OUT/'portal_solid_manifest.json').write_text(json.dumps({'method':'original terrain height field minus authored cave air; marching tetrahedra','step_metres':1,'vertices':len(v),'triangles':len(f),'source_height_min':float(h.min()),'source_height_max':float(h.max()),'seconds':time.time()-start},indent=2),encoding='utf-8')
print(json.dumps({'vertices':len(v),'triangles':len(f),'seconds':time.time()-start}))
