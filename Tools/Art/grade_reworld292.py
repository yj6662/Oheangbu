"""Derive local path earthworks from the immutable 292 landscape; never stack edits."""
import json, math
from pathlib import Path
import numpy as np
from PIL import Image
from build_reworld292 import ROOT, OUT, ASSET, W, H, CELL, sample, smooth, blur

def run():
    backup=OUT/'ungraded292.npz'
    if not backup.exists():np.savez_compressed(backup,height=np.fromfile(ASSET/'height.bytes',dtype='<f4').reshape(H,W))
    h=np.load(backup)['height'].copy();layout=json.loads((ASSET/'layout.json').read_text(encoding='utf8'))
    routes=json.loads((ASSET/'routes.json').read_text(encoding='utf8'))['routes']
    # Shared intersections use their existing heights; no broad settlement terrace.
    for sweep in range(3):
      for r in routes:
        p=np.array([[v['x'],v['z']] for v in r['points']]);d=np.linalg.norm(np.diff(p,axis=0),axis=1)
        y=sample(h,p[:,0],p[:,1]);limit=.13 if r['vehicle'] else .40
        for _ in range(12):
            for i in range(1,len(y)):y[i]=np.clip(y[i],y[i-1]-d[i-1]*limit,y[i-1]+d[i-1]*limit)
            for i in range(len(y)-2,-1,-1):y[i]=np.clip(y[i],y[i+1]-d[i]*limit,y[i+1]+d[i]*limit)
        half=max(3.5,min(7,r['width']*.5+2));outer=half+12
        # Project only within the segment bounding box, not the entire 24km² grid.
        for i,(a,b) in enumerate(zip(p,p[1:])):
            if d[i]<.01:continue
            lo=np.maximum(0,np.floor((np.minimum(a,b)-outer)/4).astype(int));hi=np.minimum([W-1,H-1],np.ceil((np.maximum(a,b)+outer)/4).astype(int))
            xx,zz=np.meshgrid(np.arange(lo[0],hi[0]+1)*4,np.arange(lo[1],hi[1]+1)*4)
            t=np.clip(((xx-a[0])*(b[0]-a[0])+(zz-a[1])*(b[1]-a[1]))/(d[i]*d[i]),0,1)
            dist=np.hypot(xx-a[0]-t*(b[0]-a[0]),zz-a[1]-t*(b[1]-a[1]));weight=1-smooth(half,outer,dist)
            target=y[i]+t*(y[i+1]-y[i]);view=h[lo[1]:hi[1]+1,lo[0]:hi[0]+1];view[:]=view*(1-weight)+target*weight
    # Limit earthworks: a route is rejected for redesign rather than excavating a
    # forty-metre wall merely to force its current coordinates to pass a slope test.
    original=np.load(backup)['height'];h=original+blur(np.clip(h-original,-8,8),2)
    # Local foundations and small forecourts, rather than a mountain-wide platform.
    for m in layout['Mountains']:
        tx,tz=m['Temple']['x'],m['Temple']['z'];floor=float(sample(h,tx,tz))
        lo=np.maximum(0,np.floor([(tx-23)/4,(tz-20)/4]).astype(int));hi=np.minimum([W-1,H-1],np.ceil([(tx+23)/4,(tz+31)/4]).astype(int))
        xx,zz=np.meshgrid(np.arange(lo[0],hi[0]+1)*4,np.arange(lo[1],hi[1]+1)*4)
        edge=np.maximum(np.abs(xx-tx)-11,np.abs(zz-(tz+5.5))-15.5)
        weight=1-smooth(0,10,edge);view=h[lo[1]:hi[1]+1,lo[0]:hi[0]+1];view[:]=view*(1-weight)+floor*weight
    for r in routes:
        for v in r['points']:v['y']=float(sample(h,v['x'],v['z']))
    route_lookup={r['id']:r for r in routes}
    for m in layout['Mountains']:
        for field,suffix in [('MainPath','main'),('TemplePath','hidden'),('ReturnPath','return')]:m[field]=route_lookup[m['Id']+'_'+suffix]['points']
        m['Foot']=m['MainPath'][0];m['Summit']=m['MainPath'][-1];m['Temple']=m['TemplePath'][-1]
        exclusions=[]
        for field in ['MainPath','TemplePath','ReturnPath']:
            for point in m[field][::2]:exclusions.append(dict(Centre=dict(x=point['x'],y=point['y']+5,z=point['z']),Size=dict(x=18,y=22,z=18)))
        for point in [m['Summit'],m['Temple']]:exclusions.append(dict(Centre=dict(x=point['x'],y=point['y']+5,z=point['z']),Size=dict(x=46,y=24,z=46)))
        m['VehicleExclusions']=exclusions
    for r in layout['Routes']:
        if r['Id'] in route_lookup:r['Bends']=[dict(x=v['x'],y=v['z']) for v in route_lookup[r['Id']]['points'][1:-1]]
    for m in layout['Mountains']:
        path=m['MainPath'];a=int(len(path)*.40);b=int(len(path)*.53)
        segments={'approach':[path[0],path[0]],'lower':path[:a+1],'middle':path[a:b+1],'upper':path[b:],'branch':m['TemplePath'],'loop':m['ReturnPath']}
        for suffix,points in segments.items():
            r=next((r for r in layout['Routes'] if r['Id']==m['Id']+'_'+suffix),None)
            if r is not None:r['Bends']=[dict(x=v['x'],y=v['z']) for v in points[1:-1]]
    h.astype('<f4').tofile(ASSET/'height.bytes')
    (ASSET/'layout.json').write_text(json.dumps(layout,ensure_ascii=False),encoding='utf8');(ASSET/'routes.json').write_text(json.dumps(dict(routes=routes)),encoding='utf8')
    mask=np.flipud(np.asarray(Image.open(ASSET/'surface.png')).astype(float)/255).copy()
    dz,dx=np.gradient(h,4);slope=np.hypot(dx,dz);curvature=h-blur(h,5)
    mask[:,:,0]=np.clip(smooth(.18,.72,slope)+smooth(1,5,curvature)*.25,0,1)*(1-mask[:,:,2]*.35)
    mask[:,:,1]=np.clip(smooth(.08,.25,slope)*(1-smooth(.45,.75,slope))*(1-smooth(-3,2,curvature)),0,1)*(1-mask[:,:,0])
    Image.fromarray((np.flipud(mask)*255).astype('uint8')).save(ASSET/'surface.png')
    n=np.stack([-dx,np.ones_like(dx),-dz],axis=-1);n/=np.linalg.norm(n,axis=-1)[...,None];shade=np.clip((n*np.array([-.45,.8,-.35])).sum(axis=-1),0,1)
    c=np.ones((H,W,3))*[.67,.65,.56];c*=.68+.32*shade[...,None];c*=1-(1-smooth(.4,1.,abs((h+4)%8-4)))[...,None]*.12
    Image.fromarray((np.flipud(np.clip(c,0,1))*255).astype('uint8')).save(ASSET/'cartography.png')
    Image.fromarray((np.flipud(np.clip((h-25)/350,0,1))*255).astype('uint8')).save(OUT/'height-overview.png')
    print('Local road grading, mountain policies and route data synchronized; run validation separately.')
if __name__=='__main__':run()
