"""Author the independent #295 watershed from a hash-locked #294 surface.

This command never writes Unity Assets. Generated water triangles are the source
for BOTH rendering and water queries. Units are metres, Y is altitude, and binary
rasters are little-endian float32, south row first, matching CompactWorldSurface.
"""
from __future__ import annotations
from pathlib import Path
from collections import deque
import argparse, hashlib, heapq, json, math, shutil
import numpy as np
from PIL import Image, ImageDraw
from build_reworld292 import blur

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Oheangbu/Assets/_Project/Art/World/Reworld292/Surface'
WORK = ROOT / 'Art/World/Compact/Rebuild/Watershed295'
OUT = WORK / 'Generated'
INPUT = WORK / 'InputSurface294'
ROUTE_CONTROLS = WORK / 'Controls/route-detours.json'
CELL = 4.; W = 1001; H = 1501
X, Z = np.meshgrid(np.arange(W, dtype=np.float32)*CELL,
                   np.arange(H, dtype=np.float32)*CELL)

def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)

def sample(a, x, z):
    x=np.clip(np.asarray(x)/CELL,0,W-1.00001);z=np.clip(np.asarray(z)/CELL,0,H-1.00001)
    ix=x.astype(int);iz=z.astype(int);u=x-ix;v=z-iz
    return (1-u)*(1-v)*a[iz,ix]+u*(1-v)*a[iz,ix+1]+(1-u)*v*a[iz+1,ix]+u*v*a[iz+1,ix+1]

def triangle_sample(a,x,z):
    """Exact lower-left to upper-right triangles used by final terrain mesh."""
    x=np.clip(np.asarray(x)/CELL,0,W-1.00001);z=np.clip(np.asarray(z)/CELL,0,H-1.00001)
    ix=x.astype(int);iz=z.astype(int);u=x-ix;v=z-iz
    return np.where(u+v<=1,a[iz,ix]+(a[iz,ix+1]-a[iz,ix])*u+(a[iz+1,ix]-a[iz,ix])*v,
        a[iz+1,ix+1]+(a[iz+1,ix]-a[iz+1,ix+1])*(1-u)+(a[iz,ix+1]-a[iz+1,ix+1])*(1-v))

def protection_transition(protection):
    """Broad outside-only feather; unchanged mask plus an 8m quiet margin.

    A chamfer distance is sufficient here: its small angular error is softened
    before the varying 140-200m transition. It cannot modify protected samples.
    """
    d=np.where(protection,0.,10000.).astype(np.float32)
    for _ in range(52):
        p=np.pad(d,1,constant_values=10000)
        d=np.minimum.reduce([d,p[1:-1,:-2]+4,p[1:-1,2:]+4,p[:-2,1:-1]+4,p[2:,1:-1]+4,
            p[:-2,:-2]+5.656854,p[:-2,2:]+5.656854,p[2:,:-2]+5.656854,p[2:,2:]+5.656854])
    width=170+20*np.sin(X/117+Z/171)+10*np.sin(Z/79-X/137)
    feather=smooth(8,width,blur(np.minimum(d,230),2))
    feather[protection]=0
    return feather.astype(np.float32)

def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write_json(p, value): p.write_text(json.dumps(value,ensure_ascii=False,separators=(',',':')),encoding='utf8')
def v3(x,y,z): return dict(x=round(float(x),4),y=round(float(y),4),z=round(float(z),4))
def v2(x,z): return dict(x=round(float(x),4),y=round(float(z),4))

def distance(points, widths=None, xx=X, zz=Z):
    result=np.full(xx.shape,np.inf,np.float32)
    for i,(a,b) in enumerate(zip(points,points[1:])):
        a=np.asarray(a,float);b=np.asarray(b,float);d=b-a
        t=np.clip(((xx-a[0])*d[0]+(zz-a[1])*d[1])/max(1,float(d@d)),0,1)
        q=np.hypot(xx-a[0]-t*d[0],zz-a[1]-t*d[1])
        if widths is not None:q-=widths[i]+(widths[i+1]-widths[i])*t
        result=np.minimum(result,q)
    return result

def lock_input():
    INPUT.mkdir(parents=True,exist_ok=True)
    files=['height.bytes','layout.json','routes.json','surface.png','cartography.png']
    receipt=INPUT/'hashes.json'
    if not receipt.exists():
        for f in files:shutil.copy2(SOURCE/f,INPUT/f)
        write_json(receipt,{f:digest(INPUT/f) for f in files})
    hashes=json.loads(receipt.read_text(encoding='utf8'))
    for name,sha in hashes.items():
        if digest(INPUT/name)!=sha:raise RuntimeError('Immutable #294 input changed: '+name)
    return hashes

def protected(layout):
    """Preserve all accepted highland surfaces and their 30m context."""
    mask=np.zeros((H,W),bool);bounds=[]
    variants=WORK.parent/'Highlands293/Variants'
    for p in sorted(variants.glob('*/terrain.npz')):
        d=np.load(p);x0=float(d['x0'])-30;z0=float(d['z0'])-30
        x1=float(d['x0'])+d['T'].shape[1]-1+30;z1=float(d['z0'])+d['T'].shape[0]-1+30
        bounds.append(dict(Id=p.parent.name,Min=v2(x0,z0),Max=v2(x1,z1)))
        mask|=(X>=x0)&(X<=x1)&(Z>=z0)&(Z<=z1)
    # Connections between the three authored upper Hyeongang sections also stay.
    for m in layout['Mountains']:
        if m['Id']!='mountain_hyeongang':continue
        for name in ['MainPath','TemplePath','ReturnPath']:
            q=[[p['x'],p['z']] for p in m[name] if p['y']>=225]
            if len(q)>1:mask|=distance(q[::max(1,len(q)//100)]+q[-1:])<30
    return mask,bounds

def natural_arms(base, level):
    """Three broad irregular mountain arms close lowland escapes, not a berm."""
    result=base.copy()
    arms=[([[1020,4200],[1120,4580],[1210,4940],[1350,5350],[1500,5740]],
           [320,400,340,290,320], [65,52,78,50,75]),
          ([[3150,4150],[3110,4550],[3200,4880],[3190,5310],[2990,5650]],
           [330,370,430,330,290], [70,55,86,68,82]),
          ([[970,4220],[1430,4280],[1790,4330],[2290,4300],[2770,4310],[3200,4280]],
           [330,380,300,260,320,380], [66,84,48,28,72,64])]
    for idx,(points,widths,heights) in enumerate(arms):
        crest=np.full(base.shape,-1000,np.float32)
        for i,(a,b) in enumerate(zip(points,points[1:])):
            a=np.array(a);b=np.array(b);v=b-a
            t=np.clip(((X-a[0])*v[0]+(Z-a[1])*v[1])/(v@v),0,1)
            dist=np.hypot(X-a[0]-t*v[0],Z-a[1]-t*v[1]);width=widths[i]*(1-t)+widths[i+1]*t
            peak=level+(heights[i]*(1-t)+heights[i+1]*t)*.30
            variation=7*np.sin(X/151+Z/229+idx)+4*np.sin(Z/71-X/93)
            # A mountain slope, with >500m foothill support and variable crest.
            target=peak+variation-dist*.23-(dist/width)**2*18
            weight=1-smooth(width*.75,width*1.65,dist)
            crest=np.maximum(crest,base+(np.maximum(base,target)-base)*weight)
        result=np.maximum(result,crest)
    # Keep the source's branching relief on the newly raised broad slopes.
    residual=base-blur(base,35)
    delta=np.maximum(0,result-base)
    delta=blur(delta,3)
    erosion=8*np.sin(X/83+np.sin(Z/191))*np.sin(Z/137)+5*np.sin(Z/57+X/179)
    return base+np.maximum(0,delta+(residual*.85+erosion)*smooth(0,12,delta))

def lake_shape():
    central=(np.sqrt(((X-2320)/610)**2+((Z-4990)/390)**2)-1)*390
    arms=[([[2270,4920],[1840,4900],[1590,5000],[1430,4840]],[230,205,120,68]),
          ([[2500,5130],[2750,5310],[2830,5490]],[185,175,110]),
          ([[2310,5170],[2260,5420],[2220,5610]],[175,150,82]),
          ([[2270,4780],[2230,4570],[2230,4390]],[205,148,45])]
    d=central
    for points,widths in arms:d=np.minimum(d,distance(points,widths))
    # Broad shore variation: avoids a perfect ellipse/capsule silhouette.
    return d+14*np.sin(X/101+Z/151)+9*np.sin(X/63-Z/139)

def basin(base, protection, level, shape, feather=None):
    h=natural_arms(base,level)
    # Retain tall existing hills; their contours form peninsulas and islands.
    hill=smooth(level+27,level+70,base)
    strait=distance([[1710,4900],[1980,4900]],[74,94])
    hill*=smooth(-5,50,strait)
    inside=1-smooth(-5,40,shape)
    cut=inside*(1-hill)
    depth=6+9*smooth(0,170,-shape)+3*np.sin(X/190+Z/163)**2
    bed=np.minimum(base,level-depth)
    h=h*(1-cut)+bed*cut
    # Variable wide shore escarpments connect the large arms to the lake.
    # This fills only low escape saddles, not all shore pixels to one rim height.
    collar=(1-smooth(90,230,shape))*smooth(-48,5,shape)
    relief=base-blur(base,25)
    target=level+3+np.maximum(shape,0)*(.14+.09*np.sin(X/223-Z/193)**2)+relief*.8*smooth(5,70,shape)
    h=np.maximum(h,h+(target-h)*collar)
    # Two substantial, irregular mountain noses divide the large central water
    # into bays. Their roots join the broad existing mountain arms, not islands
    # dropped arbitrarily into the water. Shore clipping follows their terrain.
    for points,widths,heights in [
        ([[3070,4930],[2840,4930],[2680,4850],[2500,4860]],[210,165,112,38],[42,32,20,6]),
        ([[1870,4310],[1900,4520],[1960,4660],[2110,4760]],[215,180,125,45],[40,32,18,5])]:
        for i,(a,b) in enumerate(zip(points,points[1:])):
            a=np.array(a);b=np.array(b);v=b-a;t=np.clip(((X-a[0])*v[0]+(Z-a[1])*v[1])/(v@v),0,1)
            d=np.hypot(X-a[0]-t*v[0],Z-a[1]-t*v[1]);width=widths[i]*(1-t)+widths[i+1]*t
            rise=heights[i]*(1-t)+heights[i+1]*t
            erosion=6*np.sin(X/47+Z/63)*np.sin(Z/103)+relief*.75
            nose=level+rise-(d/width)**1.5*(rise+13)+erosion
            weight=1-smooth(width*.8,width*1.45,d)
            h=np.maximum(h,h+(np.maximum(h,nose)-h)*weight)
    # The partial hill blend can otherwise create a tiny steep annulus. It is
    # intentional dry terrain only where it meets the visible preserved hill.
    if feather is not None:h=base+(h-base)*feather
    h[protection]=base[protection]
    return h.astype(np.float32)

def lake_metrics(h,base,shape,protection,level):
    region=(shape<240)&~protection;wet=(shape<120)&(h<level)
    delta=h-base
    return dict(Level=level,WetAreaKm2=round(float(wet.sum()*16/1e6),4),
                CutMillionM3=round(float(np.maximum(-delta[region],0).sum()*16/1e6),3),
                FillMillionM3=round(float(np.maximum(delta[region],0).sum()*16/1e6),3),
                MaxCut=float(np.maximum(-delta,0).max()),MaxFill=float(np.maximum(delta,0).max()))

def route_path(h,start,end,water0,water1,blocked,attract=None):
    """16m A*: penalise earthwork and avoid accepted places/highlands."""
    scale=16.;a=h[::4,::4];bh=blocked[::4,::4];hh,ww=a.shape
    s=tuple(np.rint(np.array(start[::-1])/scale).astype(int));e=tuple(np.rint(np.array(end[::-1])/scale).astype(int))
    bh=bh.copy()
    for q in [s,e]:bh[max(0,q[0]-3):q[0]+4,max(0,q[1]-3):q[1]+4]=False
    st=np.array(start,float);en=np.array(end,float);v=en-st
    cost=np.full(a.shape,np.inf);cost[s]=0;parent={};queue=[(0,s)];closed=set()
    while queue:
        _,q=heapq.heappop(queue)
        if q in closed:continue
        if q==e:break
        closed.add(q)
        for dz,dx in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]:
            z,x=q[0]+dz,q[1]+dx
            if not(0<=z<hh and 0<=x<ww) or bh[z,x]:continue
            p=np.array([x*scale,z*scale]);t=np.clip(float((p-st)@v)/max(1,float(v@v)),0,1)
            wanted=water0+(water1-water0)*t
            cut=max(0,float(a[z,x])-wanted+3);fill=max(0,wanted-float(a[z,x])-3)
            length=scale*math.hypot(dx,dz)
            turn=0
            if q in parent:
                old=parent[q];turn=.06*((q[0]-old[0]-dz)**2+(q[1]-old[1]-dx)**2)
            deep_penalty=8*(max(0,cut-50)/25)**2 if water0<80 else 0
            nc=cost[q]+length*(1+.65*(cut/35)**2+2.8*(fill/30)**2+deep_penalty+turn)
            if nc<cost[z,x]:
                cost[z,x]=nc;parent[(z,x)]=q;heuristic=math.hypot(z-e[0],x-e[1])*scale
                heapq.heappush(queue,(nc+heuristic,(z,x)))
    if not np.isfinite(cost[e]):raise RuntimeError('No protected-safe route '+str((start,end)))
    q=e;path=[]
    while q!=s:path.append([q[1]*scale,q[0]*scale]);q=parent[q]
    path.append(list(start));path.reverse();path[-1]=list(end)
    # Continuous mild corner-cutting; no global pathfinding reroutes game paths.
    p=np.array(path,float)
    if len(p)>7:
        for _ in range(3):
            filtered=p.copy();filtered[2:-2]=(p[:-4]+4*p[1:-3]+6*p[2:-2]+4*p[3:-1]+p[4:])/16;p=filtered
    for _ in range(3):
        aa=.75*p[:-1]+.25*p[1:];bb=.25*p[:-1]+.75*p[1:]
        p=np.vstack([p[0],np.stack([aa,bb],1).reshape(-1,2),p[-1]])
    return p

def rows_for(path,h,start_y,end_y,width,identity):
    old_s=np.r_[0,np.cumsum(np.linalg.norm(np.diff(path,axis=0),axis=1))]
    s=np.linspace(0,old_s[-1],int(np.ceil(old_s[-1]/4))+1)
    p=np.column_stack([np.interp(s,old_s,path[:,i]) for i in [0,1]])
    terrain=sample(h,p[:,0],p[:,1]);raw=terrain+.35
    # Smoothed lower envelope follows the real valley, then approaches a shared
    # downstream elevation. This cannot reverse grade at a confluence.
    raw=np.convolve(np.pad(raw,(5,5),mode='edge'),np.ones(11)/11,mode='valid')
    raw[0]=start_y;water=np.minimum.accumulate(raw+s*.001)-s*.001
    if water[-1]>end_y:water-= (water[-1]-end_y)*(s/max(1,s[-1]))**1.3
    water=np.maximum(water,end_y+(s[-1]-s)*.0003)
    water[0]=start_y;water=np.minimum.accumulate(water);water[-1]=end_y
    for _ in range(2):water=np.convolve(np.pad(water,(8,8),mode='edge'),np.ones(17)/17,mode='valid')
    water[0]=start_y;water[-1]=end_y;water=np.minimum.accumulate(water)
    # Limit excessively sudden numeric profile kinks without raising the water.
    for i in range(len(water)-2,-1,-1):water[i]=min(water[i],water[i+1]+max(.035,(s[i+1]-s[i])*.22))
    # Upstream connecting reaches are physical cascades; keep their exact datum.
    if abs(water[0]-start_y)>.001:
        n=min(len(water)-1,max(2,int((start_y-water[0])/.45)))
        water[:n+1]=np.linspace(start_y,water[n],n+1)
    full=width*(1+.15*np.sin(s/179+identity)+.08*np.sin(s/67+identity*.7))
    full=np.clip(full,40 if width>35 else 16,70 if width>35 else 30)
    asym=.13*np.sin(s/121+identity)
    left=full*.5*(1+asym);right=full*.5*(1-asym)
    depth=(3.2 if width>35 else 1.8)+.7*np.sin(s/233+identity)**2
    return dict(p=p,s=s,y=water,left=left,right=right,bed=water-depth)

def stamp_river(h,water,owner,rows,index,protection):
    # 16m spans avoid millions of global raster projections. Each local block
    # is cut against the same interpolated water heights exported in Rows.
    n=len(rows['p']);ids=list(range(0,n,4))
    nearest=np.full(h.shape,np.inf,np.float32)
    heights=np.zeros(h.shape,np.float32);halves=np.ones(h.shape,np.float32);depths=np.zeros(h.shape,np.float32)
    if ids[-1]!=n-1:ids.append(n-1)
    for i,j in zip(ids,ids[1:]):
        a=rows['p'][i];b=rows['p'][j];v=b-a;length=np.linalg.norm(v)
        if length<.01:continue
        extent=max(rows['left'][i],rows['right'][i],rows['left'][j],rows['right'][j])+100
        lo=np.maximum(0,np.floor((np.minimum(a,b)-extent)/CELL).astype(int));hi=np.minimum([W-1,H-1],np.ceil((np.maximum(a,b)+extent)/CELL).astype(int))
        sl=(slice(lo[1],hi[1]+1),slice(lo[0],hi[0]+1));xx=X[sl];zz=Z[sl]
        t=np.clip(((xx-a[0])*v[0]+(zz-a[1])*v[1])/(length*length),0,1)
        lateral=((xx-a[0])*v[1]-(zz-a[1])*v[0])/length
        dist=np.hypot(xx-a[0]-t*v[0],zz-a[1]-t*v[1]);signed=np.where(lateral<0,-dist,dist)
        wl=rows['y'][i]*(1-t)+rows['y'][j]*t
        left=rows['left'][i]*(1-t)+rows['left'][j]*t;right=rows['right'][i]*(1-t)+rows['right'][j]*t
        half=np.where(signed<0,left,right);u=dist/half
        dep=(rows['y'][i]-rows['bed'][i])*(1-t)+(rows['y'][j]-rows['bed'][j])*t
        profile=wl-dep*np.maximum(0,1-u*u)**.65+np.maximum(dist-half,0)*(.8+.35*np.sin((xx+zz)/173)**2)
        take=dist<nearest[sl]
        nearest[sl][take]=dist[take];heights[sl][take]=wl[take];halves[sl][take]=half[take];depths[sl][take]=dep[take]
    active=np.isfinite(nearest)&~protection
    dist=nearest[active];half=halves[active];wl=heights[active];dep=depths[active];local=h[active]
    u=dist/half
    bank_noise=(7*np.sin(X[active]/53+np.sin(Z[active]/139))*np.sin(Z[active]/81)+3*np.sin(X[active]/31+Z[active]/47))
    profile=wl-dep*np.maximum(0,1-u*u)**.65+np.maximum(dist-half,0)*(.55+.65*np.sin((X[active]+Z[active])/173)**2)+bank_noise*smooth(half+7,half+30,dist)
    w=1-smooth(half+55,half+100,dist)
    proposed=local+(np.minimum(local,profile)-local)*w
    lip=(1-smooth(half+5,half+20,dist))*smooth(half-2,half+3,dist)
    # No end-cap may dam a reach that already intersects this one.
    lip*=water[active]<-9000
    proposed=np.maximum(proposed,proposed+(wl+.2-proposed)*lip)
    h[active]=proposed
    wet_support=active&(nearest<halves+5)
    use=wet_support&((water<-9000)|(heights<water)|(h>water+.02))
    water[use]=heights[use];owner[use]=index

def route_blockers(layout,protection):
    blocked=protection.copy()
    for p in layout['Places']:
        if p['XZ']['y']>4300 and p['Id'] in ['hyeongang','temple']:continue
        if p['Id'].startswith('mountain_hyeongang_') and p['XZ']['y']<5050:continue
        radius=max(22,p.get('GroundRadius',12))+28
        if p['Id']=='village':radius=175
        if p['Id']=='mine':radius=225
        blocked|=np.hypot(X-p['XZ']['x'],Z-p['XZ']['y'])<radius
    return blocked

def make_network(h,level,blocked):
    # The outlet follows south into the central lowlands and turns west. The
    # old southern route climbed >200m after a 48m tributary minimum.
    specs=[('main_upper',(2230,4400),(2350,3900),level,76.,56.),
           ('main_middle',(2350,3900),(1650,2760),76.,45.,56.),
           ('main_lower',(1650,2760),(0,2284),45.,39.,56.),
           ('west',(850,3670),(1650,2760),None,45.,23.),
           ('east',(3440,3670),(2110,3250),None,57.,24.),
           ('north',(2020,5810),(2280,5290),None,level,26.)]
    result=[]
    for idx,(name,start,end,sy,ey,width) in enumerate(specs):
        if name=='east':
            parent=result[1]['rows'];k=np.argmin(np.linalg.norm(parent['p']-np.array(end),axis=1))
            end=parent['p'][k].tolist();ey=float(parent['y'][k])
        if sy is None:sy=float(sample(h,*start))+.35
        path=route_path(h,start,end,sy,ey,blocked)
        rows=rows_for(path,h,sy,ey,width,idx)
        result.append(dict(id=name,rows=rows,parent={'main_upper':'main_middle','main_middle':'main_lower','main_lower':'','west':'main_lower','east':'main_middle','north':'lake'}[name]))
    # Tributaries stop at the FIRST actual main-channel encounter. Continuing a
    # tributary alongside a main channel to a nominal later join creates two
    # water levels in one valley and an invalid loop.
    for branch in result:
        if branch['id'] not in ['west','east']:continue
        hits=[];r=branch['rows']
        for parent in result[:3]:
            q=parent['rows'];sq=((r['p'][:,None,:]-q['p'][None,:,:])**2).sum(-1)
            near=sq.argmin(1);dist=np.sqrt(sq.min(1))
            threshold=(r['left']+r['right'])*.5+(q['left'][near]+q['right'][near])*.5+8
            candidates=np.where(dist<threshold)[0]
            if len(candidates):hits.append((int(candidates[0]),parent,int(near[candidates[0]])))
        if not hits:continue
        row,parent,at=min(hits,key=lambda x:x[0]);q=parent['rows']
        path=np.vstack([r['p'][:max(2,row-5)],q['p'][at]])
        branch['rows']=rows_for(path,h,float(r['y'][0]),float(q['y'][at]),23 if branch['id']=='west' else 24,3 if branch['id']=='west' else 4)
        branch['parent']=parent['id']
    return result

def raster_components(mask):
    """4-neighbour components, used on a coarse diagnostic copy only."""
    mask=mask.copy();out=[]
    for z,x in zip(*np.where(mask)):
        if not mask[z,x]:continue
        todo=deque([(z,x)]);mask[z,x]=False;pts=[]
        while todo:
            a,b=todo.popleft();pts.append((a,b))
            for aa,bb in [(a+1,b),(a-1,b),(a,b+1),(a,b-1)]:
                if 0<=aa<mask.shape[0] and 0<=bb<mask.shape[1] and mask[aa,bb]:mask[aa,bb]=False;todo.append((aa,bb))
        out.append(pts)
    return sorted(out,key=len,reverse=True)

def outer_polygon(mask):
    """Trace the largest raster boundary; holes remain represented by meshes."""
    edges={}
    for z,x in zip(*np.where(mask)):
        candidates=[]
        if z==0 or not mask[z-1,x]:candidates.append(((x,z),(x+1,z)))
        if x==W-1 or not mask[z,x+1]:candidates.append(((x+1,z),(x+1,z+1)))
        if z==H-1 or not mask[z+1,x]:candidates.append(((x+1,z+1),(x,z+1)))
        if x==0 or not mask[z,x-1]:candidates.append(((x,z+1),(x,z)))
        for a,b in candidates:edges.setdefault(a,[]).append(b)
    loops=[]
    while edges:
        start=next(iter(edges));p=start;loop=[]
        while p in edges:
            loop.append(p);q=edges[p].pop()
            if not edges[p]:del edges[p]
            p=q
            if p==start:break
        if len(loop)>3:loops.append(loop)
    if not loops:return []
    def area(p):return abs(sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(p,p[1:]+p[:1])))
    selected=max(loops,key=area)
    return [v2((x-.5)*4,(z-.5)*4) for x,z in selected[::3]]

def export_water(h,water,owner):
    """Clip the SAME terrain triangles used in Unity against the water plane."""
    folder=OUT/'WaterMeshes';folder.mkdir(parents=True,exist_ok=True)
    for p in folder.glob('*.json'):p.unlink()
    chunks={};wet=(water>-9000)&(h<water-.015)
    # Every triangle has a defined water plane: nearest supported corner water
    # extends only one grid step to meet the bank. At a dry edge h is above it.
    for z,x in zip(*np.where(wet[:-1,:-1]|wet[1:,:-1]|wet[:-1,1:]|wet[1:,1:])):
        points=[(x,z),(x,z+1),(x+1,z),(x+1,z+1)]
        for tri in [(0,1,2),(2,1,3)]:
            ids=[points[i] for i in tri];vals=[float(water[zz,xx]) for xx,zz in ids]
            supported=[v for v in vals if v>-9000]
            if not supported:continue
            fallback=min(supported);poly=[]
            for (xx,zz),value in zip(ids,vals):
                y=value if value>-9000 else fallback
                poly.append((np.array([xx*CELL,y,zz*CELL]),float(h[zz,xx])-y))
            clipped=[]
            for (a,da),(b,db) in zip(poly,poly[1:]+poly[:1]):
                if da<-.005:clipped.append(a)
                if (da<-.005)!=(db<-.005):clipped.append(a+(b-a)*((-0.005-da)/(db-da)))
            if len(clipped)<3:continue
            key=(int(x*CELL//160),int(z*CELL//160));entry=chunks.setdefault(key,[[],[]]);vs,ts=entry;n=len(vs)
            vs.extend(clipped)
            for i in range(1,len(clipped)-1):
                triangle=np.asarray([clipped[k] for k in [0,i,i+1]],np.float32)
                av=triangle[1,[0,2]]-triangle[0,[0,2]];bv=triangle[2,[0,2]]-triangle[0,[0,2]]
                if abs(float(av[0]*bv[1]-av[1]*bv[0]))>=.0001:ts.extend([n,n+i,n+i+1])
    files=[];triangles=0
    for (cx,cz),(verts,tris) in sorted(chunks.items()):
        name=f'Water_{cx:02}_{cz:02}';path=folder/(name+'.json')
        write_json(path,dict(Id=name,Vertices=[v3(*p) for p in verts],Triangles=tris))
        files.append('WaterMeshes/'+path.name);triangles+=len(tris)//3
    return files,triangles,wet

def crossings(layout,routes,h,water):
    records=[]
    for route in routes['routes']:
        pts=np.array([[p['x'],p['z']] for p in route['points']],float)
        if len(pts)<2:continue
        ss=np.r_[0,np.cumsum(np.linalg.norm(np.diff(pts,axis=0),axis=1))]
        s=np.linspace(0,ss[-1],int(ss[-1]/2)+2);q=np.column_stack([np.interp(s,ss,pts[:,i]) for i in [0,1]])
        ground=sample(h,q[:,0],q[:,1]);w=sample(water,q[:,0],q[:,1]);wet=(w>-900)&(ground<w-.10)
        indices=np.where(np.diff(np.r_[False,wet,False].astype(int))!=0)[0]
        for num,(a,b) in enumerate(zip(indices[::2],indices[1::2])):
            a=max(0,a-4);b=min(len(s)-1,b+4);start=q[a];end=q[b];span=float(np.linalg.norm(end-start))
            if span<3:continue
            wy=float(np.max(w[a:b+1]));deck=max(float(ground[a]),float(ground[b]),wy+3)+.4
            records.append(dict(Id=route['id']+'_water_'+str(num),RouteId=route['id'],Start=v3(start[0],deck,start[1]),End=v3(end[0],deck,end[1]),DeckHeight=deck,RouteWidth=float(route.get('width',3)),Span=span,WaterHeight=wy,Kind='FixedCrossing' if span<180 else 'RelocateRouteViaShore'))
    return dict(Crossings=records)

def land_path(h,water,start,end):
    """Route first-visit travel along dry shores; short water gaps cost extra."""
    a=h[::4,::4];wwater=water[::4,::4];wet=(wwater>-9000)&(a<wwater)
    walk=np.where(wet,wwater+3,a);hh,ww=a.shape;scale=16.
    s=tuple(np.rint(np.array(start[::-1])/scale).astype(int));e=tuple(np.rint(np.array(end[::-1])/scale).astype(int))
    queue=[(0,s)];cost={s:0.};parent={};closed=set()
    while queue:
        _,q=heapq.heappop(queue)
        if q in closed:continue
        if q==e:break
        closed.add(q)
        for dz,dx in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]:
            z,x=q[0]+dz,q[1]+dx
            if not(1<=z<hh-1 and 1<=x<ww-1):continue
            length=scale*math.hypot(dx,dz);grade=abs(float(walk[z,x]-walk[q]))/length
            penalty=400*max(0,grade-.30)**2+40*max(0,grade-.12)**2
            nc=cost[q]+length*(1+penalty+(28 if wet[z,x] else 0))
            if nc<cost.get((z,x),math.inf):
                cost[(z,x)]=nc;parent[(z,x)]=q;heapq.heappush(queue,(nc+math.hypot(z-e[0],x-e[1])*scale,(z,x)))
    if e not in cost:raise RuntimeError('No first-visit route '+str((start,end)))
    q=e;path=[]
    while q!=s:path.append([q[1]*scale,q[0]*scale]);q=parent[q]
    path.append(list(start));path.reverse();path[-1]=list(end)
    p=np.asarray(path,float)
    # Only one restrained corner pass; don't cut corners across a shoreline.
    if len(p)>3:
        alt=p.copy();alt[1:-1]=p[:-2]*.15+p[1:-1]*.7+p[2:]*.15
        dryold=sample(water,p[:,0],p[:,1])<sample(h,p[:,0],p[:,1]);drynew=sample(water,alt[:,0],alt[:,1])<sample(h,alt[:,0],alt[:,1])
        take=(~dryold)|drynew;p[take]=alt[take]
    return refine_land_path(h,water,p)

def refine_land_path(h,water,path):
    """Refine the coarse shoreline route on actual 4m terrain triangles.

    Along-route grade alone misses a path travelling across a cliff face.
    Both triangle gradient and every diagonal's midpoint are checked here.
    Water crossings are allowed only where the coarse route already chose one.
    """
    lo=np.maximum(0,np.floor((path.min(0)-112)/4).astype(int));hi=np.minimum([W-1,H-1],np.ceil((path.max(0)+112)/4).astype(int))
    x0,z0=lo;x1,z1=hi;terrain=h[z0:z1+1,x0:x1+1];plane=water[z0:z1+1,x0:x1+1]
    xx,zz=np.meshgrid(np.arange(x0,x1+1)*4,np.arange(z0,z1+1)*4)
    near=distance(path,xx=xx,zz=zz);allowed=near<100
    wet=(plane>-9000)&(terrain<plane+.15);walk=np.where(wet,plane+3,terrain)
    dz,dx=np.gradient(terrain,4);cross_slope=np.hypot(dx,dz)
    allowed&=wet|(cross_slope<1.10)
    start=np.asarray(path[0]);end=np.asarray(path[-1])
    s=tuple((np.rint(start[::-1]/4)-[z0,x0]).astype(int));e=tuple((np.rint(end[::-1]/4)-[z0,x0]).astype(int))
    allowed[s]=True;allowed[e]=True;queue=[(0.,s)];cost={s:0.};parent={};closed=set();hh,ww=terrain.shape
    while queue:
        _,q=heapq.heappop(queue)
        if q in closed:continue
        if q==e:break
        closed.add(q)
        for dz,dx in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]:
            z,x=q[0]+dz,q[1]+dx
            if not(0<=z<hh and 0<=x<ww) or not allowed[z,x]:continue
            length=4*math.hypot(dx,dz);grade=abs(float(walk[z,x]-walk[q]))/length
            crossing=wet[z,x] or wet[q]
            if not crossing and dx==dz and dx!=0:
                middle=(float(terrain[q[0],x])+float(terrain[z,q[1]]))*.5
                grade=max(grade,abs(middle-float(terrain[q]))/(length*.5),abs(float(terrain[z,x])-middle)/(length*.5))
            if not crossing and grade>.395:continue
            nc=cost[q]+length*(1+grade*grade*9+(32 if crossing else 0)+max(0,float(cross_slope[z,x])-.45)*1.5)
            if nc<cost.get((z,x),math.inf):
                cost[(z,x)]=nc;parent[(z,x)]=q;heapq.heappush(queue,(nc+math.hypot(z-e[0],x-e[1])*4,(z,x)))
    if e not in cost:raise RuntimeError('No fine terrain route '+str((start.tolist(),end.tolist())))
    q=e;points=[]
    while q!=s:points.append([(q[1]+x0)*4,(q[0]+z0)*4]);q=parent[q]
    points.append(start.tolist());points.reverse();points[-1]=end.tolist()
    return np.asarray(points,float)

def river_banks(h,water,network):
    """Dry shore samples with bend-side and confluence-aware ground types."""
    records=[]
    for reach in network:
        r=reach['rows'];p=r['p'];tangent=np.gradient(p,axis=0);tangent/=np.maximum(.001,np.linalg.norm(tangent,axis=1))[:,None]
        bend=np.gradient(tangent,axis=0);curvature=tangent[:,0]*bend[:,1]-tangent[:,1]*bend[:,0]
        for i in range(3,len(p)-3,5):
            for side in [-1,1]:
                normal=np.array([-tangent[i,1],tangent[i,0]])*side
                half=float(r['left'][i] if side>0 else r['right'][i]);offsets=np.arange(half*.75,half+110,2.)
                q=p[i]+normal[None,:]*offsets[:,None];ground=triangle_sample(h,q[:,0],q[:,1]);plane=sample(water,q[:,0],q[:,1])
                valid=(ground>=r['y'][i]+.6)&((plane<-9000)|(ground>=plane+.6))
                ids=np.where(valid)[0]
                if not len(ids):continue
                at=int(ids[0]);position=q[at];y=ground[at]
                slope=abs(float(triangle_sample(h,position[0]+normal[0]*4,position[1]+normal[1]*4)-y))/4
                near_join=r['s'][-1]-r['s'][i]<80
                inner=float(curvature[i])*side>0.0003
                kind='Mud' if near_join and slope<.4 else 'Gravel' if inner and slope<.7 else 'Cliff' if slope>.85 else 'RockBank' if not inner else 'Accessible'
                records.append(dict(Position=v3(position[0],y,position[1]),Type=kind,Flow=1.,Side=side,ReachId=reach['id'],Curvature=float(curvature[i]),Slope=slope))
    return records

def grade_hyeongang_approach(h,water,protection,layout,routes):
    """Give the lowland connector a real tread, not only a gentle centreline.

    The 4m terrain requires a wider vertex-level core to support a usable 5m
    walking width. Changes stay outside the accepted highland mask; its original
    upper path points and independent collision surfaces remain unchanged.
    """
    mountain=next(m for m in layout['Mountains'] if m['Id']=='mountain_hyeongang')
    original=json.loads((INPUT/'layout.json').read_text(encoding='utf8'))
    source=next(m for m in original['Mountains'] if m['Id']=='mountain_hyeongang')['MainPath']
    upper=next(i for i,p in enumerate(source) if p['y']>=225);join=source[upper]
    end=next(i for i,p in enumerate(mountain['MainPath']) if p==join)
    points=np.array([[p['x'],p['z']] for p in mountain['MainPath'][:end+1]],float)
    lengths=np.linalg.norm(np.diff(points,axis=0),axis=1);arc=np.r_[0,np.cumsum(lengths)]
    steps=np.linspace(0,arc[-1],max(2,int(np.ceil(arc[-1]))+1))
    path=np.column_stack([np.interp(steps,arc,points[:,i]) for i in [0,1]])
    planar_blend=smooth(0,25,steps)*(1-smooth(steps[-1]-70,steps[-1]-40,steps))
    kernel=np.exp(-.5*(np.arange(-12,13)/4.)**2);kernel/=kernel.sum()
    for _ in range(3):
        filtered=np.column_stack([np.convolve(np.pad(path[:,i],(12,12),mode='edge'),kernel,mode='valid') for i in [0,1]])
        path+=(filtered-path)*planar_blend[:,None]
    steps=np.r_[0,np.cumsum(np.linalg.norm(np.diff(path,axis=0),axis=1))]
    profile=triangle_sample(h,path[:,0],path[:,1]);smooth_profile=np.convolve(np.pad(profile,(4,4),mode='edge'),np.ones(9)/9,mode='valid')
    blend=smooth(0,12,steps)*(1-smooth(steps[-1]-12,steps[-1],steps))
    profile+= (smooth_profile-profile)*blend
    # The source route has already passed a .395 fine-grid grade search. This
    # small smoothing plus envelope leaves room for triangle interpolation.
    for _ in range(3):
        for i in range(1,len(profile)):profile[i]=np.clip(profile[i],profile[i-1]-.32*(steps[i]-steps[i-1]),profile[i-1]+.32*(steps[i]-steps[i-1]))
        for i in range(len(profile)-2,-1,-1):profile[i]=np.clip(profile[i],profile[i+1]-.32*(steps[i+1]-steps[i]),profile[i+1]+.32*(steps[i+1]-steps[i]))
    profile[0]=float(triangle_sample(h,*path[0]));profile[-1]=float(triangle_sample(h,*path[-1]))
    near=np.full(h.shape,np.inf,np.float32);target=h.copy()
    for i,(a,b) in enumerate(zip(path[:-1],path[1:])):
        lo=np.maximum(0,np.floor((np.minimum(a,b)-18)/4).astype(int));hi=np.minimum([W-1,H-1],np.ceil((np.maximum(a,b)+18)/4).astype(int))
        sl=(slice(lo[1],hi[1]+1),slice(lo[0],hi[0]+1));v=b-a;t=np.clip(((X[sl]-a[0])*v[0]+(Z[sl]-a[1])*v[1])/max(.0001,float(v@v)),0,1)
        d=np.hypot(X[sl]-a[0]-t*v[0],Z[sl]-a[1]-t*v[1]);take=d<near[sl]
        near[sl][take]=d[take];target[sl][take]=(profile[i]+(profile[i+1]-profile[i])*t)[take]
    # A nearest-segment projection alone makes a step between the two legs of
    # a tight switchback. Average nearby road heights in world space so both
    # legs share a gently graded turning area instead of intersecting treads.
    total=np.zeros(h.shape,np.float32);weights=np.zeros(h.shape,np.float32)
    for point,y in zip(path,profile):
        lo=np.maximum(0,np.floor((point-30)/4).astype(int));hi=np.minimum([W-1,H-1],np.ceil((point+30)/4).astype(int))
        sl=(slice(lo[1],hi[1]+1),slice(lo[0],hi[0]+1));d2=(X[sl]-point[0])**2+(Z[sl]-point[1])**2
        weight=np.exp(-d2/40.5);total[sl]+=weight*y;weights[sl]+=weight
    use=weights>.00001;target[use]=total[use]/weights[use]
    weight=1-smooth(4.5,16.5,near);weight[protection]=0
    # Only dry access terrain is graded; lake and flowing reach profiles stay.
    weight[(water>-9000)&(h<water+.7)]=0
    before=h.copy();delta=np.clip(target-h,-8,8)*weight;h[:]=h+delta
    updated=[v3(x,float(triangle_sample(h,x,z)),z) for x,z in path[:-1:2]]+source[upper:]
    mountain['MainPath']=updated;mountain['Foot']=updated[0]
    for r in routes['routes']:
        if r['id']=='mountain_hyeongang_main':r['points']=updated
    changed=np.abs(h-before)>.00001
    return dict(Id='mountain_hyeongang_main',CoreWidth=9.,Shoulder=12.,MaxDelta=float(np.abs(h-before).max()),MedianDelta=float(np.median(np.abs(h-before)[changed])),ChangedSamples=int(changed.sum()),ProtectedChanged=int((changed&protection).sum()),Length=float(steps[-1]))

def relocate_and_routes(base,h,water,layout,routes,protection,level):
    wet=(water>-9000)&(h<water+.5);dz,dx=np.gradient(h,4);slope=np.hypot(dx,dz)
    # Dry site samples have support around their full footprint, not just pivot.
    safe=~wet&~protection&(slope<.40)
    for oz,ox in [(0,7),(0,-7),(7,0),(-7,0),(5,5),(-5,-5),(5,-5),(-5,5)]:
        safe&=~np.roll(np.roll(wet,oz,0),ox,1)
    available=np.column_stack(np.where(safe[::2,::2]))*2
    place_by_id={p['Id']:p for p in layout['Places']};relocations=[];moved={}
    for p in layout['Places']:
        if p['Id'].startswith('mountain_'):continue
        x,z=p['XZ']['x'],p['XZ']['y'];wy=float(sample(water,x,z));oldh=float(sample(h,x,z))
        if wy<-9000 or oldh>wy+2:continue
        q=available;qx=q[:,1]*4;qz=q[:,0]*4;dist=np.hypot(qx-x,qz-z)
        eligible=(dist<850)&(qz>4300 if z>4300 else np.ones(len(q),bool))
        if p['Id'] in ['hyeongang','temple']:
            eligible&=(h[q[:,0],q[:,1]]>=level+7)&(h[q[:,0],q[:,1]]<=level+45)
        if not eligible.any():raise RuntimeError('No supported shore place '+p['Id'])
        q=q[eligible];dist=dist[eligible];heights=h[q[:,0],q[:,1]]
        target=np.array([1820,5040]) if p['Id']=='hyeongang' else np.array([2930,5240]) if p['Id']=='temple' else np.array([x,z])
        distance_preferred=np.linalg.norm(q[:,::-1]*4-target,axis=1)
        score=dist+distance_preferred*.8+np.abs(heights-(level+26))*.8+slope[q[:,0],q[:,1]]*180
        chosen=q[int(np.argmin(score))];nx,nz=int(chosen[1]*4),int(chosen[0]*4);y=float(h[tuple(chosen)])
        radius=max(14,min(22,p.get('GroundRadius',18)));d=np.hypot(X-nx,Z-nz);blend=1-smooth(radius,radius+22,d)
        blend[protection]=0;h[:]=h+(y-h)*blend
        before=v3(x,float(sample(base,x,z)),z);after=v3(nx,y,nz)
        p['XZ']=v2(nx,nz);moved[p['Id']]=after;relocations.append(dict(Id=p['Id'],Before=before,After=after))
    if 'hyeongang' in moved:
        after=moved['hyeongang'];foot=place_by_id['mountain_hyeongang_foot'];before=foot['XZ'];foot['XZ']=v2(after['x'],after['z'])
        relocations.append(dict(Id=foot['Id'],Before=v3(before['x'],float(sample(base,before['x'],before['y'])),before['y']),After=after));moved[foot['Id']]=after
        m=next(m for m in layout['Mountains'] if m['Id']=='mountain_hyeongang')
        old=m['MainPath'];index=next(i for i,p in enumerate(old) if p['y']>=225)
        end=old[index];path=land_path(h,water,[after['x'],after['z']],[end['x'],end['z']])
        new=[v3(x,float(sample(h,x,z)),z) for x,z in path[:-1]]+old[index:]
        m['MainPath']=new;m['Foot']=new[0];m['RouteRevision']=295
        for r in routes['routes']:
            if r['id']=='mountain_hyeongang_main':r['points']=new
    route_specs={r['Id']:r for r in layout['Routes']};route_changes=[]
    for r in routes['routes']:
        spec=route_specs.get(r['id']);original=r['points']
        if len(original)<2:continue
        if r['id']=='mountain_hyeongang_main':continue
        if spec:
            start=place_by_id[spec['From']]['XZ'];end=place_by_id[spec['To']]['XZ'];start=[start['x'],start['y']];end=[end['x'],end['y']]
            relocated=spec['From'] in moved or spec['To'] in moved
        else:start=[original[0]['x'],original[0]['z']];end=[original[-1]['x'],original[-1]['z']];relocated=False
        q=np.array([[p['x'],p['z']] for p in original]);y=sample(h,q[:,0],q[:,1]);w=sample(water,q[:,0],q[:,1]);inside=(w>-9000)&(y<w)
        # Leave accepted mountain sections verbatim. Their protected corridor is
        # dry; only their newly relocated lowland approach needs recomputation.
        if r['id'].startswith('mountain_') and not relocated:continue
        if not relocated and not inside.any():continue
        path=land_path(h,water,start,end);r['points']=[v3(x,float(sample(h,x,z)),z) for x,z in path]
        if spec:spec['Bends']=[v2(x,z) for x,z in path[1:-1]]
        route_changes.append(dict(Id=r['id'],Points=len(path),Reason='RelocatedEndpoint' if relocated else 'AvoidFloodedBasin'))
    write_json(OUT/'relocations.json',dict(Places=relocations,Routes=route_changes))
    return relocations,route_changes

def output_previews(base,h,water,protection,network,shape):
    dz,dx=np.gradient(h,CELL);normal=np.stack([-dx,np.ones_like(h),-dz],-1);normal/=np.linalg.norm(normal,axis=-1)[...,None]
    shade=np.clip((normal*np.array([-.55,.7,-.4])).sum(-1),0,1)
    land=np.zeros((H,W,3),np.float32);land[:]=[.54,.55,.40];land*=.52+.48*shade[...,None]
    land+=np.clip((h-70)/320,0,1)[...,None]*np.array([.20,.18,.18])
    wet=(water>-9000)&(h<water);depth=np.clip((water-h)/30,0,1)
    blue=np.array([.29,.54,.58])[None,None,:]*(1-depth[...,None]*.38)
    land=np.where(wet[...,None],blue,land);land[protection]=land[protection]*.65+np.array([.62,.31,.64])*.35
    image=Image.fromarray((np.flipud(np.clip(land,0,1))*255).astype('uint8'))
    image.save(OUT/'watershed-topdown.png')
    image.crop((240,20,840,440)).resize((1200,840)).save(OUT/'hyeongang-topdown.png')
    diff=h-base;scale=150
    rgb=np.ones((H,W,3),np.float32)*.90
    rgb[...,0]-=np.clip(-diff/scale,0,.8);rgb[...,1]-=np.clip(abs(diff)/scale,0,.8);rgb[...,2]-=np.clip(diff/scale,0,.8)
    Image.fromarray((np.flipud(np.clip(rgb,0,1))*255).astype('uint8')).save(OUT/'earthworks.png')
    return image

def apply_route_detours(h, routes, layout, crossing_data, control_path=None):
    """Apply complete authored XZ polylines after bridge support generation.

    Controls are mutable candidate inputs, separate from the immutable #294
    snapshot. Loading the same full polylines on every invocation is idempotent.
    Bridge routes are excluded because their exported collider profile owns Y.
    """
    path=Path(control_path) if control_path is not None else ROUTE_CONTROLS
    if not path.exists():return dict(ControlHashes={},AuthoredRouteDetours=[])
    raw=path.read_bytes();controls=json.loads(raw.decode('utf-8-sig'))
    if controls.get('Version')!=1 or not isinstance(controls.get('Routes'),list):
        raise ValueError('Route detours require Version=1 and a Routes array: '+str(path))
    rby={r['id']:r for r in routes['routes']};lby={r['Id']:r for r in layout['Routes']}
    places={p['Id']:p for p in layout['Places']}
    bridge_ids={c['RouteId'] for c in crossing_data['Crossings']};seen=set();pending=[]
    for record in controls['Routes']:
        route_id=record.get('Id')
        if route_id in seen or route_id not in rby:
            raise ValueError('Route detour ID is duplicated or missing from generated routes: '+str(route_id))
        seen.add(route_id)
        if route_id in bridge_ids:
            raise ValueError('Route detour cannot override an exported bridge support profile: '+route_id)
        points=record.get('Points')
        if not isinstance(points,list) or len(points)<2:
            raise ValueError('Route detour requires at least two complete XZ points: '+route_id)
        xy=np.array([[float(p['x']),float(p['z'])] for p in points],dtype=float)
        if not np.isfinite(xy).all() or (xy[:,0]<0).any() or (xy[:,0]>4000).any() or (xy[:,1]<0).any() or (xy[:,1]>6000).any():
            raise ValueError('Route detour has nonfinite or out-of-world points: '+route_id)
        distance=np.linalg.norm(np.diff(xy,axis=0),axis=1)
        if (distance<.001).any():raise ValueError('Route detour contains consecutive duplicate points: '+route_id)
        height=triangle_sample(h,xy[:,0],xy[:,1]);final=[v3(x,y,z) for (x,z),y in zip(xy,height)]
        # Inspect the actual intervening terrain, rather than only endpoint Y.
        dense=np.vstack([np.linspace(a,b,max(2,int(math.ceil(d))+1))[:-1] for a,b,d in zip(xy[:-1],xy[1:],distance)]+[xy[-1:]])
        dense_y=triangle_sample(h,dense[:,0],dense[:,1]);step=np.linalg.norm(np.diff(dense,axis=0),axis=1)
        max_grade=float(np.max(np.abs(np.diff(dense_y))/np.maximum(step,.00001)))
        spec=lby.get(route_id);endpoint_delta=0.
        if spec is not None:
            for i,key in [(0,'From'),(-1,'To')]:
                place=places.get(spec[key])
                if place is not None:
                    expected=np.array([place['XZ']['x'],place['XZ']['y']],float)
                    endpoint_delta=max(endpoint_delta,float(np.linalg.norm(xy[i]-expected)))
        pending.append((rby[route_id],spec,final,dict(Id=route_id,Reason=str(record.get('Reason','')),
            Points=len(final),Length=float(distance.sum()),MaxTerrainGrade=max_grade,
            LayoutSynchronized=spec is not None,LayoutEndpointMaxDelta=endpoint_delta)))
    # Validate the complete control file before changing either document.
    for route,spec,points,metric in pending:
        route['points']=points
        if spec is not None:spec['Bends']=[v2(p['x'],p['z']) for p in points[1:-1]]
    try:label=path.relative_to(WORK).as_posix()
    except ValueError:label=path.name
    return dict(ControlHashes={label:hashlib.sha256(raw).hexdigest()},AuthoredRouteDetours=[p[-1] for p in pending])


def crossing_supports():
    """Export the same deck/ramp polyline for gameplay routes and colliders."""
    h=np.fromfile(OUT/'height.bytes',dtype='<f4').reshape(H,W)
    # Each invocation starts from the same unmodified polylines. Repeated
    # supports-only calls must not resample an already resampled bridge route.
    source=OUT/'support-inputs.json'
    if not source.exists():
        write_json(source,{key:json.loads((OUT/(key+'.json')).read_text(encoding='utf8')) for key in ['crossings','routes','layout']})
    inputs=json.loads(source.read_text(encoding='utf8'))
    data=inputs['crossings'];routes=inputs['routes'];layout=inputs['layout']
    rby={r['id']:r for r in routes['routes']};lby={r['Id']:r for r in layout['Routes']}
    grades=[]
    for crossing in data['Crossings']:
        r=rby[crossing['RouteId']];p=np.array([[v['x'],v['y'],v['z']] for v in r['points']],float)
        q=p[:,[0,2]];length=np.linalg.norm(np.diff(q,axis=0),axis=1);s=np.r_[0,np.cumsum(length)]
        def project(v):
            target=np.array([v['x'],v['z']]);d=np.diff(q,axis=0)
            t=np.clip(((target-q[:-1])*d).sum(1)/np.maximum(length*length,.000001),0,1)
            nearest=q[:-1]+t[:,None]*d;i=int(np.argmin(((nearest-target)**2).sum(1)))
            return s[i]+t[i]*length[i]
        s0,s1=project(crossing['Start']),project(crossing['End'])
        if s1<s0:s0,s1=s1,s0
        # Wide ribbons and rails cannot follow a 120-degree one-metre hairpin.
        # Smooth XY before computing the elevation envelope, then make both
        # the route and support use that exact same curve.
        lo=max(0,s0-130);hi=min(s[-1],s1+130)
        dense=np.linspace(lo,hi,max(2,int(np.ceil(hi-lo))+1))
        curve=np.column_stack([np.interp(dense,s,q[:,i]) for i in [0,1]])
        original_curve=curve.copy();blend=smooth(lo,lo+25,dense)*(1-smooth(hi-25,hi,dense))
        kernel=np.exp(-.5*(np.arange(-24,25)/8.)**2);kernel/=kernel.sum()
        for _ in range(4):
            filtered=np.column_stack([np.convolve(np.pad(curve[:,i],(24,24),mode='edge'),kernel,mode='valid') for i in [0,1]])
            curve=curve+(filtered-curve)*blend[:,None]
        middle=np.column_stack([curve[:,0],triangle_sample(h,curve[:,0],curve[:,1]),curve[:,1]])
        p=np.vstack([p[s<lo-.001],middle,p[s>hi+.001]])
        q=p[:,[0,2]];length=np.linalg.norm(np.diff(q,axis=0),axis=1);s=np.r_[0,np.cumsum(length)]
        s0,s1=project(crossing['Start']),project(crossing['End'])
        if s1<s0:s0,s1=s1,s0
        deck=crossing['DeckHeight'];v0=np.array([np.interp(s0,s,q[:,i]) for i in [0,1]]);v1=np.array([np.interp(s1,s,q[:,i]) for i in [0,1]])
        maxgrade=.40
        ramp=max(30,abs(deck-float(sample(h,*v0)))*8,abs(deck-float(sample(h,*v1)))*8)
        for attempt in range(7):
            a=max(0,s0-ramp);b=min(s[-1],s1+ramp)
            steps=np.unique(np.r_[np.linspace(a,b,max(2,int(np.ceil(b-a))+1)),s0,s1])
            xy=np.column_stack([np.interp(steps,s,q[:,i]) for i in [0,1]])
            ground=triangle_sample(h,xy[:,0],xy[:,1])+.08
            physical=np.r_[0,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))]
            # A high dry bank close to the crossing requires a higher deck,
            # rather than a buried ramp or an instantaneous vertical step.
            d0=float(np.interp(s0,steps,physical));d1=float(np.interp(s1,steps,physical))
            fromdeck=np.maximum(np.maximum(d0-physical,physical-d1),0)
            deck=max(deck,float(np.max(ground-maxgrade*fromdeck)))
            y=np.full(len(steps),deck);left=steps<s0;right=steps>s1
            y[left]=ground[0]+(deck-ground[0])*smooth(a,s0,steps[left])
            y[right]=deck+(ground[-1]-deck)*smooth(s1,b,steps[right])
            y=np.maximum(y,ground)
            # Least slope-bounded support above the terrain/cubic profile.
            for k in range(1,len(y)):y[k]=max(y[k],y[k-1]-maxgrade*(physical[k]-physical[k-1]))
            for k in range(len(y)-2,-1,-1):y[k]=max(y[k],y[k+1]-maxgrade*(physical[k+1]-physical[k]))
            if y[0]-ground[0]<.025 and y[-1]-ground[-1]<.025:break
            ramp*=1.65
        crossing['DeckHeight']=float(deck);crossing['Start']=v3(*[v0[0],deck,v0[1]]);crossing['End']=v3(*[v1[0],deck,v1[1]])
        supports=[v3(x,yy,z) for (x,z),yy in zip(xy,y)]
        crossing['Points']=supports;crossing['DeckStartDistance']=float(s0-a);crossing['DeckEndDistance']=float(s1-a)
        crossing['ApproachStart']=supports[0];crossing['ApproachEnd']=supports[-1]
        dy=np.diff(y);ds=np.linalg.norm(np.diff(xy,axis=0),axis=1);grade=float(np.max(np.abs(dy)/np.maximum(ds,.001)))
        delta=np.diff(xy,axis=0);unit=delta/np.maximum(.0001,np.linalg.norm(delta,axis=1))[:,None]
        turn=np.arccos(np.clip((unit[:-1]*unit[1:]).sum(1),-1,1));radius=(ds[:-1]+ds[1:])*.5/np.maximum(.000001,turn)
        crossing['MaxGrade']=grade;crossing['MinPlanarRadius']=float(radius.min());grades.append(dict(Id=crossing['Id'],MaxGrade=grade,MinPlanarRadius=float(radius.min()),RampLength=float((s0-a)+(b-s1))))
        r['points']=[v3(*v) for v in p[s<a-.001]]+supports+[v3(*v) for v in p[s>b+.001]]
        if r['id'] in lby:lby[r['id']]['Bends']=[v2(v['x'],v['z']) for v in r['points'][1:-1]]
        # Independent highland route records must use the bridge above a carved
        # river too. Only the lower crossing changes; accepted upper points stay.
        for m in layout['Mountains']:
            if r['id']==m['Id']+'_main':m['MainPath']=r['points']
    route_detours=apply_route_detours(h,routes,layout,data)
    write_json(OUT/'crossings.json',data);write_json(OUT/'routes.json',routes);write_json(OUT/'layout.json',layout)
    hydro=json.loads((OUT/'hydro.json').read_text(encoding='utf8'))
    hydro['RasterMeaning']={'Waterlevel':'Candidate water plane, including dry shoreline support for exact clipping; -9999 means no plane.','Wet':'Actual occupied raster nodes: supported and terrain < waterlevel - 0.015m.','Meshes':'Terrain triangles clipped against the candidate plane; render and water query use identical exported vertices/indices.'}
    write_json(OUT/'hydro.json',hydro)
    triangles=0;removed=0
    for name in hydro['ChunkFiles']:
        p=OUT/name;j=json.loads(p.read_text(encoding='utf8'));v=np.array([[a['x'],a['y'],a['z']] for a in j['Vertices']],np.float32);t=np.array(j['Triangles']).reshape(-1,3)
        a=v[t[:,1]][:,[0,2]]-v[t[:,0]][:,[0,2]];b=v[t[:,2]][:,[0,2]]-v[t[:,0]][:,[0,2]]
        valid=np.abs(a[:,0]*b[:,1]-a[:,1]*b[:,0])>=.0001
        if not valid.all():removed+=int((~valid).sum());j['Triangles']=t[valid].reshape(-1).tolist();write_json(p,j)
        triangles+=int(valid.sum())
    diagnostics=json.loads((OUT/'diagnostics.json').read_text(encoding='utf8'));diagnostics['CrossingSupports']=grades;diagnostics['WaterTriangles']=triangles;diagnostics['RemovedDegenerateWaterTriangles']=diagnostics.get('RemovedDegenerateWaterTriangles',0)+removed
    diagnostics.update(route_detours)
    write_json(OUT/'diagnostics.json',diagnostics)
    print(json.dumps(dict(BridgeSupports=grades,RemovedDegenerateTriangles=removed,WaterTriangles=triangles),indent=2))

def run():
    OUT.mkdir(parents=True,exist_ok=True);hashes=lock_input()
    base=np.fromfile(INPUT/'height.bytes',dtype='<f4').reshape(H,W)
    layout=json.loads((INPUT/'layout.json').read_text(encoding='utf8'));routes=json.loads((INPUT/'routes.json').read_text(encoding='utf8'))
    protection,bounds=protected(layout);feather=protection_transition(protection);shape=lake_shape();trials=[]
    for level in [125.,140.,150.,160.,170.,180.,190.,200.,210.]:
        candidate=basin(base,protection,level,shape,feather);m=lake_metrics(candidate,base,shape,protection,level)
        # Penalise fill more heavily; never improve score by shrinking the lake.
        m['Score']=(m['CutMillionM3']+m['FillMillionM3']*1.35)/max(.1,m['WetAreaKm2'])
        components=raster_components(((shape<120)&(candidate<level))[::4,::4])
        m['LargestWaterKm2']=len(components[0])*256/1e6 if components else 0
        m['ConnectedFraction']=len(components[0])/max(1,sum(map(len,components))) if components else 0
        m['Feasible']=bool(m['LargestWaterKm2']>.50 and m['ConnectedFraction']>.90)
        trials.append(m)
    eligible=[m for m in trials if m['Feasible']]
    best=min(eligible or trials,key=lambda m:m['Score']);level=best['Level'];h=basin(base,protection,level,shape,feather)
    water=np.full(h.shape,-9999,np.float32);owner=np.zeros(h.shape,np.uint8)
    lake_support=shape<150;water[lake_support]=level;owner[lake_support]=1
    blocked=route_blockers(layout,protection);network=make_network(h,level,blocked);before_rivers=h.copy()
    for idx,reach in enumerate(network):stamp_river(h,water,owner,reach['rows'],idx+2,protection)
    h=before_rivers+(h-before_rivers)*feather
    relocations,route_changes=relocate_and_routes(base,h,water,layout,routes,protection,level)
    # Immutable upper surfaces win even at a nearby confluence/corridor.
    h[protection]=base[protection]
    tread=grade_hyeongang_approach(h,water,protection,layout,routes)
    # The accepted upper walks may have low terrain beneath them. Flooding that
    # unchanged ground is valid; clipping water at a protection rectangle is not.
    lake_components=raster_components((owner==1)&(h<water)&(water>-9000))
    removed_pools=0
    for component in lake_components[1:]:
        zz,xx=np.array(component).T;water[zz,xx]=-9999;owner[zz,xx]=0;removed_pools+=len(component)
    h[np.abs(h-base)<.0001]=base[np.abs(h-base)<.0001]
    changed=np.abs(h-base)>.00001
    dz,dx=np.gradient(h,CELL);slope=np.hypot(dx,dz)
    surface=np.flipud(np.asarray(Image.open(INPUT/'surface.png')).astype(np.float32)/255).copy()
    surface[...,0]=np.where(changed,np.maximum(surface[...,0],smooth(.35,.9,slope)),surface[...,0])
    surface[...,2]=np.where(changed,np.maximum(surface[...,2],((water>-9000)&(h<water+3)).astype(float)*.95),surface[...,2])
    Image.fromarray((np.flipud(np.clip(surface,0,1))*255).astype('uint8')).save(OUT/'surface.png')
    files,triangles,wet=export_water(h,water,owner)
    h.astype('<f4').tofile(OUT/'height.bytes');water.astype('<f4').tofile(OUT/'waterlevel.bytes');wet.astype('uint8').tofile(OUT/'wet.bytes');protection.astype('uint8').tofile(OUT/'protected.bytes')
    reach_json=[];metrics=[]
    for item in network:
        r=item['rows'];rows=[]
        for i,p in enumerate(r['p']):rows.append(dict(Position=v3(p[0],r['y'][i],p[1]),LeftWidth=float(r['left'][i]),RightWidth=float(r['right'][i]),BedY=float(r['bed'][i]),Distance=float(r['s'][i]),Flow=1.))
        parent=next((p for p in network if p['id']==item['parent']),None)
        join=0.
        if parent is not None:
            pi=int(np.argmin(np.linalg.norm(parent['rows']['p']-r['p'][-1],axis=1)));join=float(parent['rows']['s'][pi])
        reach_json.append(dict(Id=item['id'],ParentId=item['parent'],JoinDistance=join,Rows=rows))
        old=sample(base,r['p'][:,0],r['p'][:,1]);metrics.append(dict(Id=item['id'],Length=float(r['s'][-1]),StartY=float(r['y'][0]),EndY=float(r['y'][-1]),UphillSegments=int((np.diff(r['y'])>.001).sum()),MaxCentreCut=float(np.maximum(0,old-r['bed']).max()),MaxWaterAboveOldGround=float(np.maximum(0,r['y']-old).max()),MinWidth=float((r['left']+r['right']).min()),MaxWidth=float((r['left']+r['right']).max())))
    shore=wet&(~np.roll(wet,1,0)|~np.roll(wet,-1,0)|~np.roll(wet,1,1)|~np.roll(wet,-1,1))
    shore_ids=np.column_stack(np.where(shore));banks=[]
    for z,x in shore_ids[::8]:
        if owner[z,x]!=1:continue
        steep=float(slope[z,x]);banks.append(dict(Position=v3(x*CELL,float(water[z,x]),z*CELL),Type='Cliff' if steep>.8 else 'Accessible' if steep<.28 else 'RockBank',Flow=0.,Side=0))
    coords=np.column_stack(np.where(changed));changedbounds=dict(Min=v2(coords[:,1].min()*4,coords[:,0].min()*4),Max=v2(coords[:,1].max()*4,coords[:,0].max()*4))
    lake_shores=[b['Position'] for b in banks];banks.extend(river_banks(h,water,network))
    hydro=dict(Version=295,Width=W,Height=H,Cell=CELL,SourceHash=hashes['height.bytes'],Lake=dict(Level=level,Polygon=outer_polygon(wet&(owner==1)),ShorePoints=lake_shores),Reaches=reach_json,ProtectedBounds=bounds,ChangedBounds=changedbounds,TerrainBounds=dict(Min=v2(0,0),Max=v2(4000,6000)),ChunkFiles=files,Banks=banks)
    write_json(OUT/'hydro.json',hydro)
    layout['Revision']='watershed-295';layout['Drainages']=[dict(Id=r['Id'],Centreline=[v2(p['Position']['x'],p['Position']['z']) for p in r['Rows'][::4]],HalfWidth=28 if r['Id'].startswith('main') else 12) for r in reach_json]
    layout['River']=layout['Drainages'][0]['Centreline'];layout['RiverWidth']=56
    write_json(OUT/'layout.json',layout);write_json(OUT/'routes.json',routes)
    crossing_data=crossings(layout,routes,h,water);write_json(OUT/'crossings.json',crossing_data)
    write_json(OUT/'support-inputs.json',dict(crossings=crossing_data,routes=routes,layout=layout))
    image=output_previews(base,h,water,protection,network,shape);image.save(OUT/'cartography.png')
    coarse=raster_components(((owner==1)&wet)[::2,::2]);lake_areas=[len(c)*64/1e6 for c in coarse]
    report=dict(Version=295,SourceHashes=hashes,LakeTrials=trials,SelectedLake=best,Reaches=metrics,WaterTriangles=triangles,WaterChunks=len(files),ChangedSamples=int(changed.sum()),ChangedPercent=float(changed.mean()*100),ProtectedSamples=int(protection.sum()),ProtectedMaxDelta=float(abs(h[protection]-base[protection]).max()),UnchangedOutsideMask=bool(np.array_equal(h[~changed],base[~changed])),LakeComponentsKm2=lake_areas[:20],Crossings=len(crossing_data['Crossings']),Relocations=relocations,ChangedRoutes=route_changes,MaxCut=float(np.maximum(0,base-h).max()),MaxFill=float(np.maximum(0,h-base).max()),GeneratedHeightSHA256=digest(OUT/'height.bytes'),GeneratedWaterSHA256=digest(OUT/'waterlevel.bytes'))
    report['HyeongangTread']=tread
    write_json(OUT/'diagnostics.json',report)
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--supports-only',action='store_true');args=parser.parse_args()
    if args.supports_only:crossing_supports()
    else:run();crossing_supports()
