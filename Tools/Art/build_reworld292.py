"""Whole-world Korean landform authoring. Immutable source DEM, deterministic output.

SRTM is ~30 m source data, not a claim of measured 4 m detail. Authored drainage,
connected ridge networks and game routes are explicitly recorded as adaptations.
"""
from pathlib import Path
import gzip, hashlib, heapq, json, math
import numpy as np
from PIL import Image, ImageFilter
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Reworld292'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292/Surface'
CELL=4.; W,H=1001,1501
X,Z=np.meshgrid(np.arange(W,dtype=np.float32)*CELL,np.arange(H,dtype=np.float32)*CELL)

def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)
def rescale(a,w,h):
    return np.asarray(Image.fromarray(a.astype('float32')).resize((w,h),Image.Resampling.BILINEAR)).copy()
def blur(a,r):
    # Separable box convolution, avoids introducing a heavyweight authoring dependency.
    def axis(a,r,axis):
        pad=[(0,0)]*2;pad[axis]=(r,r);b=np.pad(a,pad,mode='edge');c=np.cumsum(b,axis=axis,dtype=np.float64)
        shape=list(c.shape);shape[axis]=1;c=np.concatenate([np.zeros(shape),c],axis=axis)
        hi=[slice(None)]*2;lo=hi.copy();hi[axis]=slice(2*r+1,None);lo[axis]=slice(None,-2*r-1)
        return ((c[tuple(hi)]-c[tuple(lo)])/(2*r+1)).astype('float32')
    return axis(axis(a,r,0),r,1)
def distance(points):
    d=np.full((H,W),np.inf,dtype='float32');along=np.zeros_like(d);travel=0
    for a,b in zip(points,points[1:]):
        ax,az=a[:2];bx,bz=b[:2];dx,dz=bx-ax,bz-az;length=math.hypot(dx,dz)
        t=np.clip(((X-ax)*dx+(Z-az)*dz)/max(1,length*length),0,1)
        ds=np.hypot(X-ax-t*dx,Z-az-t*dz);take=ds<d
        d=np.minimum(d,ds);along=np.where(take,travel+t*length,along);travel+=length
    return d,along
def sample(a,x,z):
    xx=np.clip(np.asarray(x)/CELL,0,W-1.001);zz=np.clip(np.asarray(z)/CELL,0,H-1.001)
    ix=xx.astype(int);iz=zz.astype(int);u=xx-ix;v=zz-iz
    return (1-v)*((1-u)*a[iz,ix]+u*a[iz,ix+1])+v*((1-u)*a[iz+1,ix]+u*a[iz+1,ix+1])
def pathfind(height,start,end,vehicle=False):
    step=4;coarse=height[::step,::step];scale=CELL*step;hh,ww=coarse.shape
    s=tuple(np.clip(np.rint(np.array(start[::-1])/scale).astype(int),[0,0],[hh-1,ww-1]));e=tuple(np.clip(np.rint(np.array(end[::-1])/scale).astype(int),[0,0],[hh-1,ww-1]))
    frontier=[(0.,s)];cost={s:0.};parent={};closed=set()
    dirs=[(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]
    while frontier:
        _,q=heapq.heappop(frontier)
        if q in closed:continue
        if q==e:break
        closed.add(q)
        for dz,dx in dirs:
            v=(q[0]+dz,q[1]+dx)
            if not(0<=v[0]<hh and 0<=v[1]<ww):continue
            length=scale*math.hypot(dx,dz);grade=abs(float(coarse[v]-coarse[q]))/length
            penalty=(90 if vehicle else 26)*max(0,grade-(.09 if vehicle else .16))**2
            c=cost[q]+length*(1+penalty)
            if c<cost.get(v,math.inf):
                cost[v]=c;parent[v]=q;heapq.heappush(frontier,(c+math.hypot(v[0]-e[0],v[1]-e[1])*scale,v))
    if e not in cost:raise RuntimeError('Disconnected authored route')
    q=e;points=[]
    while q!=s:points.append([q[1]*scale,q[0]*scale]);q=parent[q]
    points.append(list(start));points.reverse();points[-1]=list(end)
    # Two corner-cutting passes; re-sample height from the same final field below.
    p=np.asarray(points,dtype=float)
    for _ in range(2):
        a=.75*p[:-1]+.25*p[1:];b=.25*p[:-1]+.75*p[1:];p=np.vstack([p[0],np.stack([a,b],axis=1).reshape(-1,2),p[-1]])
    return p[::2].tolist()+[list(end)]

def build():
    OUT.mkdir(parents=True,exist_ok=True);ASSET.mkdir(parents=True,exist_ok=True)
    layout=json.loads((OUT.parent/'Mountain290/layout.json').read_text(encoding='utf-8-sig'))
    demfile=OUT.parent/'Terrain243/N35E127.hgt.gz'
    raw=np.frombuffer(gzip.decompress(demfile.read_bytes()),dtype='>i2');side=math.isqrt(raw.size)
    dem=raw.reshape(side,side).astype('float32');dem[dem<-100]=0
    # Southern Korean DEM crop supplies correlated, branching relief. This crop
    # is not a reproduction of Seoraksan/Bukhansan/Jirisan's named summits.
    crop=dem[int((1-.20)*(side-1)):int((1-.08)*(side-1)),int(.28*(side-1)):int(.38*(side-1))]
    relief=rescale(crop[::-1],W,H);low=blur(relief,35)
    residual=np.clip((relief-low)*.48,-45,45)
    # Measured Korean relief carries the landform, rather than being a fine noise
    # layer laid over smooth artificial mounds.
    height=36+(relief-relief.min())*.43
    authored=np.zeros_like(height)
    ridge_specs=[
      ('east_main',[(3930,1000),(3640,1950),(3880,2710),(3590,3580),(3240,4250),(2810,4750)],340,330),
      ('east_inner',[(3860,2750),(3420,3060),(2870,3480),(2570,3960),(2230,4360)],260,250),
      ('east_spur',[(3650,1910),(3250,1800),(2820,1510)],155,340),
      ('northeast_spur',[(3260,4250),(3460,4700),(3260,5390),(3500,5970)],225,350),
      ('north_main',[(350,5840),(1090,5670),(1730,5460),(2290,5820),(2870,5710)],280,410),
      ('north_spur',[(1710,5460),(1510,4990),(1210,4550),(1090,4160)],180,300),
      ('west_main',[(90,1730),(450,2450),(510,3340),(790,3940),(1140,4300),(1350,4860)],300,320),
      ('west_outer',[(70,3650),(140,4430),(510,4900),(710,5580)],180,400),
      ('inwang',[(510,3340),(1030,3530),(1330,3960),(1730,4200),(2100,4450)],245,270),
      ('south_main',[(500,180),(1140,450),(1650,410),(2260,930),(2670,1330),(3130,1420)],275,340),
      ('south_spur',[(2260,930),(2510,420),(3020,140)],155,320),
      ('west_foothill',[(450,2450),(1080,2270),(1420,2010)],105,380)]
    ridge_weight=np.zeros_like(height)
    for idx,(name,points,amplitude,width) in enumerate(ridge_specs):
        d,t=distance(points)
        # Vary cross-section and crest height continuously along each connected ridge.
        # World-continuous modulation: nearest-segment arc length jumps at bends.
        # Using that arc length across a whole slope created diagonal height seams.
        crest=amplitude*(.80+.14*np.sin((X+Z*.73)/270+idx)+.08*np.sin((Z-X*.3)/107+idx*.4))
        widthfield=width*(1+.15*np.sin((X*.4+Z)/310+idx))
        shoulder=np.exp(-(d/widthfield)**1.55)*crest
        spine=np.exp(-(d/(widthfield*.24))**2)*crest*.17
        authored=np.maximum(authored,(shoulder+spine)*.28)
        ridge_weight=np.maximum(ridge_weight,np.exp(-(d/widthfield)**2))
    height+=authored
    rivers=[('main',[(2020,5810),(2200,5270),(2090,4740),(2320,4180),(2420,3610),(2290,3060),(2190,2590),(2410,2030),(2010,1590),(1720,1070),(1430,490),(1520,0)],15),
      ('east',[(3440,3670),(3310,3260),(3070,2890),(3120,2470),(2780,2110),(2410,2030)],8),
      ('west',[(850,3670),(1140,3120),(1400,2780),(1810,2710),(2190,2590)],7)]
    wet=np.zeros_like(height);allriver=np.full_like(height,10000)
    for name,points,width in rivers:
        d,t=distance(points);allriver=np.minimum(d,allriver)
        # Shallow channel following actual valley elevation. Never force every
        # upstream stream to the capital's elevation through a mountain massif.
        influence=1-smooth(width,width+45,d)
        height-=influence*(2.5+2*np.exp(-d/max(width,1)))
        wet=np.maximum(wet,np.exp(-d/(width+30)))
    # Settlements occupy a basin/valley. Their exact footprint is conformed later in Unity.
    basin=np.exp(-(((X-1960)/490)**4+((Z-2940)/620)**4))
    height=height*(1-basin*.82)+(48+residual*.16)*basin*.82
    places={p['Id']:p for p in layout['Places']}
    # Nearby village service records share one rigid horizontal relocation.
    for p in layout['Places']:
        x,z=p['XZ']['x'],p['XZ']['y']
        if (x-2700)**2+(z-2180)**2<150**2:p['XZ']={'x':x+60,'y':z-40}
        elif p['Id']=='geumpyo_inn':p['XZ']={'x':3070.,'y':2330.}
    # Preserve each dungeon/settlement's own dimensions, not its old world elevation.
    for p in layout['Places']:
        if p['Id'].startswith('mountain_'):continue
        x,z=p['XZ']['x'],p['XZ']['y'];radius=min(22,max(6,p.get('GroundRadius',10)))
        target=float(sample(height,x,z));d=np.hypot(X-x,Z-z)
        w=1-smooth(radius,radius+35,d);height=height*(1-w)+target*w
    route_records=[]
    for r in layout['Routes']:
        if r['Id'].startswith('mountain_'):continue
        if r['From'] not in places or r['To'] not in places:continue
        a=places[r['From']]['XZ'];b=places[r['To']]['XZ']
        p=pathfind(height,[a['x'],a['y']],[b['x'],b['y']],r.get('GradeForVehicle',False))
        r['Bends']=[{'x':x,'y':z} for x,z in p[1:-1:3]]
        route_records.append((r,p))
    new_peaks=[(2870,3480),(2260,930),(510,3340),(1710,5460),(1330,3960)]
    for m,peak in zip(layout['Mountains'],new_peaks):
        foot=places[m['EntryPlaceId']]['XZ'];p=pathfind(height,[foot['x'],foot['y']],peak)
        junction=p[int(len(p)*.53)]
        # Select a nearby hollow on the inward side with a natural approach.
        candidates=[]
        jh=float(sample(height,*junction))
        for angle in np.linspace(0,2*math.pi,24,endpoint=False):
            q=[junction[0]+65*math.cos(angle),junction[1]+65*math.sin(angle)]
            if not(35<q[0]<3965 and 35<q[1]<5965):continue
            delta=abs(float(sample(height,*q))-jh);candidates.append((delta,q))
        temple=min(candidates,key=lambda x:x[0])[1]
        branch=pathfind(height,junction,temple);reunion=p[int(len(p)*.40)]
        ret=pathfind(height,temple,reunion)
        def points3(path):return [{'x':float(x),'y':float(sample(height,x,z)),'z':float(z)} for x,z in path]
        m['MainPath']=points3(p);m['TemplePath']=points3(branch);m['ReturnPath']=points3(ret)
        m['Foot']=m['MainPath'][0];m['Summit']=m['MainPath'][-1];m['Temple']=points3([temple])[0]
        m['RouteRevision']=292
        for suffix,point in [('foot',m['Foot']),('summit',m['Summit']),('temple',m['Temple']),('junction',points3([junction])[0]),('reunion',points3([reunion])[0])]:
            places[m['Id']+'_'+suffix]['XZ']={'x':point['x'],'y':point['z']}
        for suffix,path in [('main',p),('hidden',branch),('return',ret)]:
            route_records.append((dict(Id=m['Id']+'_'+suffix,Width=2.2,GradeForVehicle=False),path))
    road=np.zeros_like(height)
    # Narrow, feathered path influence only. No thirty-metre shelves along routes.
    for route,path in route_records:
        width=min(7,max(1.6,route.get('Width',3)))
        points=np.array(path)[::max(1,len(path)//100)].tolist()
        if points[-1]!=path[-1]:points.append(path[-1])
        d,_=distance(points);road=np.maximum(road,1-smooth(width*.42,width*.5+2,d))
    dz,dx=np.gradient(height,CELL);slope=np.hypot(dx,dz)
    curvature=height-blur(height,5)
    exposure=smooth(.18,.72,slope)*(.55+.45*ridge_weight)+smooth(1,5,curvature)*.35
    rock=np.clip(exposure*(1-wet*.35),0,1)
    debris=np.clip(smooth(.08,.25,slope)*(1-smooth(.45,.75,slope))*(1-smooth(-3,2,curvature)),0,1)*(1-rock)
    forest=np.clip((1-rock)*(1-wet*.35)*(1-road),0,1)
    masks=np.stack([rock,debris,wet,road],axis=-1)
    height.astype('<f4').tofile(ASSET/'height.bytes')
    # Texture row zero is south. Unity LoadImage uses bottom-left texture coordinates.
    Image.fromarray((np.flipud(masks)*255).astype('uint8')).save(ASSET/'surface.png')
    normal=np.stack([-dx,np.ones_like(dx),-dz],axis=-1);normal/=np.linalg.norm(normal,axis=-1)[...,None]
    shade=np.clip((normal*np.array([-.45,.8,-.35])).sum(axis=-1),0,1)
    colour=np.zeros((H,W,3),dtype='float32');colour[:]=[.70,.66,.55]
    colour=colour*(1-rock[...,None])+np.array([.67,.66,.60])*rock[...,None]
    colour=colour*(1-forest[...,None]*.22)+np.array([.32,.37,.28])*forest[...,None]*.22
    colour*=.58+.42*shade[...,None]
    contour=1-smooth(.4,1.0,np.abs((height+4)%8-4));colour*=1-contour[...,None]*.10
    colour=colour*(1-road[...,None]*.3)+np.array([.76,.72,.63])*road[...,None]*.3
    Image.fromarray((np.flipud(np.clip(colour,0,1))*255).astype('uint8')).save(ASSET/'cartography.png')
    Image.fromarray((np.flipud(np.clip((height-25)/350,0,1))*255).astype('uint8')).save(OUT/'height-overview.png')
    layout['Revision']='reworld-292';layout['Ridges']=[dict(Id=n,Spine=[dict(x=x,y=z) for x,z in p],Height=a,Width=w,Kind=1 if a>200 else 0) for n,p,a,w in ridge_specs]
    layout['River']=[dict(x=x,y=z) for x,z in rivers[0][1]]
    layout['Drainages']=[dict(Id=n,Centreline=[dict(x=x,y=z) for x,z in p],HalfWidth=w) for n,p,w in rivers]
    layout['SurfaceWidth']=W;layout['SurfaceHeight']=H;layout['SurfaceCell']=CELL
    (ASSET/'layout.json').write_text(json.dumps(layout,ensure_ascii=False),encoding='utf8')
    (ASSET/'routes.json').write_text(json.dumps({'routes':[dict(id=r['Id'],width=r.get('Width',3),vehicle=r.get('GradeForVehicle',False),points=[dict(x=x,y=float(sample(height,x,z)),z=z) for x,z in p]) for r,p in route_records]}),encoding='utf8')
    metadata=dict(revision='reworld-292',width=W,height=H,cell=CELL,min=float(height.min()),max=float(height.max()),source=str(demfile.relative_to(ROOT)),sourceSHA256=hashlib.sha256(demfile.read_bytes()).hexdigest(),sourceBounds=dict(south=35.08,north=35.20,west=127.28,east=127.38),sourceResolution='approximately 30m SRTM; 4m output is authored interpolation, not measured detail',ridgeCount=len(ridge_specs),rivers=[dict(id=n,points=p,width=w) for n,p,w in rivers],routeCount=len(route_records))
    (OUT/'surface.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(metadata,ensure_ascii=False))
if __name__=='__main__':build()
