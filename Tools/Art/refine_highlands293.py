"""Reuse the accepted 285/289 trail only in designated highland windows."""
from pathlib import Path
import json,hashlib,zipfile
import numpy as np
from PIL import Image,ImageDraw
from build_reworld292 import sample,smooth,blur
ROOT=Path(__file__).resolve().parents[2]
A=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292'
O=ROOT/'Art/World/Compact/Rebuild/Highlands293'

def clip(poly,normal,limit):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        da=np.dot(a,normal)-limit;db=np.dot(b,normal)-limit
        if da<=0:out.append(a)
        if (da<=0)!=(db<=0):out.append((np.array(a)+(np.array(b)-a)*da/(da-db)).tolist())
    return out

def run():
    O.mkdir(parents=True,exist_ok=True)
    if not (O/'baseline-height.bytes').exists():
        for name in ['height.bytes','layout.json','routes.json','surface.png','cartography.png']:(O/('baseline-'+name)).write_bytes((A/'Surface'/name).read_bytes())
        with zipfile.ZipFile(O/'Recovery292.zip','w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
            for directory in ['Data','Materials','Meshes','TerrainData']:
                for p in (A/directory).rglob('*'):
                    if p.is_file():z.write(p,p.relative_to(A))
            z.write(A/'W_Demo_Compact_Reworld.unity','W_Demo_Compact_Reworld.unity')
    h=np.fromfile(O/'baseline-height.bytes',dtype='<f4').reshape(1501,1001).copy();base=h.copy()
    layout=json.loads((O/'baseline-layout.json').read_text(encoding='utf8'));routes=json.loads((O/'baseline-routes.json').read_text(encoding='utf8'))
    source=json.loads((ROOT/'Art/World/Compact/Rebuild/Mountain285/route.json').read_text(encoding='utf8'))
    sp=np.array([[p['x'],p['y'],p['z']] for p in source['points']]);sv=sp[-1,[0,2]]-sp[0,[0,2]];sl=np.linalg.norm(sv);sf=sv/sl;sr=np.array([sf[1],-sf[0]])
    # The 285 native terrain was authored and verified against these exact rock meshes; reuse it as
    # the land behind the escarpment instead of reconstructing a heightfield from a near-vertical face.
    native=np.asarray(json.loads((ROOT/'Art/World/Compact/Rebuild/Mountain285/terrain.json').read_text())['heights'],np.float32).reshape(1025,1025)*400-140
    NX0,NZ0,NCELL=-650.,-350.,1200/1024
    def window(a,r,op):
        # Separable square min/max filter (Chebyshev radius r samples).
        for axis in (0,1):
            pad=[(0,0)]*2;pad[axis]=(r,r);b=np.pad(a,pad,mode='edge');out=b.take(range(0,a.shape[axis]),axis=axis)
            for s in range(1,2*r+1):out=op(out,b.take(range(s,s+a.shape[axis]),axis=axis))
            a=out
        return a
    footprint=np.zeros(native.shape,bool)
    for mesh in (ROOT/'Art/World/Compact/Rebuild/Mountain285/Meshes').glob('*.json'):
        if not mesh.name.startswith(('Granite_','Carved_trail_','Trail_shoulder_')) or '_LOD0' in mesh.name or '_LOD2' in mesh.name:continue
        v=np.asarray([[q['x'],q['z']] for q in json.loads(mesh.read_text())['vertices']])
        ix=np.clip(np.round((v[:,0]-NX0)/NCELL).astype(int),0,1024);iz=np.clip(np.round((v[:,1]-NZ0)/NCELL).astype(int),0,1024)
        footprint[iz,ix]=True
    footprint=window(footprint.astype(np.float32),2,np.maximum)>0
    # A 4m game terrain cell linearly interpolates its corners; lowering each corner to the minimum
    # of the fine surface within one cell keeps the coarse surface behind the rock skin everywhere.
    covered=window(footprint.astype(np.float32),5,np.maximum)>0
    native=np.where(covered,window(native,5,np.minimum)-.35,native)
    # Soft weight: the 285 land owns the rock surroundings, the 292 mountain owns everything beyond.
    coarse=footprint[::4,::4].astype(np.float32);near=np.zeros_like(coarse)
    for radius,weight in [(0,1.),(4,.8),(8,.45),(12,.15)]:near=np.maximum(near,window(coarse,radius,np.maximum)*weight)
    near=blur(near,3)
    def grid(a,cell,sx,sz):
        xx=np.clip((sx-NX0)/cell,0,a.shape[1]-1.001);zz=np.clip((sz-NZ0)/cell,0,a.shape[0]-1.001);ix=xx.astype(int);iz=zz.astype(int);u=xx-ix;v=zz-iz
        return (1-v)*((1-u)*a[iz,ix]+u*a[iz,ix+1])+v*((1-u)*a[iz+1,ix]+u*a[iz+1,ix+1])
    def pts(a):return [dict(x=float(v[0]),y=float(v[1]),z=float(v[2])) for v in a]
    modules=[];changed=np.zeros(h.shape,dtype=bool);walkmask=np.zeros(h.shape,dtype=float);rockmask=np.zeros(h.shape,dtype=float)
    # #293 variants: mountains with site-specific sections skip the warped 285 transplant (Step 2: all five).
    record=json.loads((O/'Variants/sections.json').read_text(encoding='utf8')) if (O/'Variants/sections.json').exists() else {}
    sectioned={r['mountain']:r['ids']+r.get('repair_ids',[]) for r in record.get('mountains',[record] if 'mountain' in record else [])}
    for k,m in enumerate(layout['Mountains']):
        if m['Id'] in sectioned:continue
        p=np.array([[v['x'],v['y'],v['z']] for v in m['MainPath']]);d=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p[:,[0,2]],axis=0),axis=1))]
        best=[];junction=np.array([m['TemplePath'][0]['x'],m['TemplePath'][0]['z']])
        for i in range(1,len(p)-1):
            if not .60<d[i]/d[-1]<.82:continue
            j=min(len(p)-2,np.searchsorted(d,d[i]+110+k*3))
            chord=np.linalg.norm(p[j,[0,2]]-p[i,[0,2]])
            if chord<80 or min(np.linalg.norm(p[i,[0,2]]-junction),np.linalg.norm(p[j,[0,2]]-junction))<30:continue
            best.append((abs(chord-106)+(d[j]-d[i]-chord)*2,i,j))
        if not best:raise RuntimeError('No non-destructive module location: '+m['Id'])
        _,i,j=min(best);a=p[i];b=p[j];tf=(b[[0,2]]-a[[0,2]])/np.linalg.norm(b[[0,2]]-a[[0,2]]);tr=np.array([tf[1],-tf[0]])
        mid=(a+b)*.5
        sign=1 if sample(base,mid[0]+tr[0]*30,mid[2]+tr[1]*30)>=sample(base,mid[0]-tr[0]*30,mid[2]-tr[1]*30) else -1
        scale=np.linalg.norm(b[[0,2]]-a[[0,2]])/sl;width=[1,.92,1.08,.97,1.02][k];vertical=[1,.90,1.05,.92,1][k]
        correction=b[1]-a[1]-21*vertical
        def world(q):
            q=np.asarray(q);delta=q[..., [0,2]]-sp[0,[0,2]];u=delta@sf;v=delta@sr
            xz=a[[0,2]]+u[...,None]*scale*tf+v[...,None]*sign*width*tr
            y=a[1]+(q[...,1]-18)*vertical+(u/sl)*correction
            return np.stack([xz[...,0],y,xz[...,1]],axis=-1)
        replacement=world(sp);m['MainPath']=pts(np.vstack([p[:i],replacement,p[j+1:]]));m['RouteRevision']=293
        centre=world(np.array([0,18,45]));radius=185
        lo=np.maximum(0,np.floor((centre[[0,2]]-radius)/4).astype(int));hi=np.minimum([1000,1500],np.ceil((centre[[0,2]]+radius)/4).astype(int))
        x,z=np.meshgrid(np.arange(lo[0],hi[0]+1)*4,np.arange(lo[1],hi[1]+1)*4)
        delta=np.stack([x-a[0],z-a[2]],axis=-1);u=(delta@tf)/scale;v=(delta@tr)/(sign*width)
        sourceXZ=sp[0,[0,2]]+u[...,None]*sf+v[...,None]*sr;sx,sz=sourceXZ[...,0],sourceXZ[...,1]
        pathx=np.interp(sz,sp[:,2],sp[:,0]);pathy=np.interp(sz,sp[:,2],sp[:,1]);side=sx-pathx
        localHeight=grid(native,NCELL,sx,sz)
        # Source geometry owns the visible walking surface, including the timber gap.
        localHeight=np.where(abs(side)<3,np.minimum(localHeight,pathy-1.5),localHeight)
        bridge=(sz>source['points'][int(len(sp)*.485)]['z'])&(sz<source['points'][int(len(sp)*.55)]['z'])&(abs(side)<3)
        localHeight=np.where(bridge,pathy-3,localHeight)
        target=a[1]+(localHeight-18)*vertical+(u/sl)*correction
        view=h[lo[1]:hi[1]+1,lo[0]:hi[0]+1]
        target=view+(target-view)*grid(near,NCELL*4,sx,sz)
        influence=(1-smooth(62,110,abs(side)))*(smooth(-45,-15,sz))*(1-smooth(108,145,sz))
        # Protected existing settlements/encounters remain in the unmodified landform.
        for place in layout['Places']:
            if place['Id'].startswith('mountain_'):continue
            dist=np.hypot(x-place['XZ']['x'],z-place['XZ']['y'])
            influence*=smooth(max(20,place.get('GroundRadius',18)),max(20,place.get('GroundRadius',18))+30,dist)
        view=h[lo[1]:hi[1]+1,lo[0]:hi[0]+1];view[:]=view*(1-influence)+target*influence
        changed[lo[1]:hi[1]+1,lo[0]:hi[0]+1]|=influence>0
        walkmask[lo[1]:hi[1]+1,lo[0]:hi[0]+1]=np.maximum(walkmask[lo[1]:hi[1]+1,lo[0]:hi[0]+1],(1-smooth(3,6,abs(side)))*smooth(-8,-4,sz)*(1-smooth(90,94,sz)))
        modules.append(dict(Id=m['Id'],Realm=m['Realm'],A=pts([a])[0],B=pts([b])[0],Sign=sign,Width=width,Vertical=vertical,SourceLength=float(sl),Centre=pts([centre])[0],Path=pts(replacement)))
        for r in routes['routes']:
            if r['id']==m['Id']+'_main':r['points']=m['MainPath'];r['independentSurface']='Highlands293 source trail'
        m['VehicleExclusions']=[dict(Centre=dict(x=v['x'],y=v['y']+5,z=v['z']),Size=dict(x=18,y=22,z=18)) for path in ['MainPath','TemplePath','ReturnPath'] for v in m[path][::2]]
    for mid,ids in sectioned.items():
        m=next(v for v in layout['Mountains'] if v['Id']==mid)
        p=np.array([[v['x'],v['y'],v['z']] for v in m['MainPath']]);d=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p[:,[0,2]],axis=0),axis=1))]
        spliced=[];cursor=0.
        infos={sid:json.loads((O/'Variants'/sid/'section.json').read_text(encoding='utf8')) for sid in ids}
        for sid in sorted(ids,key=lambda i:infos[i]['s0']):  # the main path is spliced in path order (repairs sit lower)
            info=infos[sid]
            spliced+=[v for v,dist in zip(p,d) if cursor<=dist<info['s0']-.05]
            spliced+=[[q['x'],q['y'],q['z']] for q in info['mainPath']];cursor=info['s1']+.05
        for sid in ids:  # terrain patches in build order: each recorded base already contains the earlier patches
            c=np.load(O/'Variants'/sid/'coarse.npz');iz0,ix0=int(c['iz0']),int(c['ix0']);win=(slice(iz0,iz0+c['coarse'].shape[0]),slice(ix0,ix0+c['coarse'].shape[1]))
            if not np.allclose(h[win],c['base'],atol=1e-4):raise RuntimeError('Section base drifted from the installed height: '+sid)
            changed[win]|=np.abs(c['coarse']-c['base'])>1e-6;h[win]=c['coarse']
            walkmask[win]=np.maximum(walkmask[win],c['walk']);rockmask[win]=np.maximum(rockmask[win],c['rock'])
        spliced+=[v for v,dist in zip(p,d) if dist>=cursor]
        m['MainPath']=pts(np.array(spliced,float));m['RouteRevision']=293
        for r in routes['routes']:
            if r['id']==m['Id']+'_main':r['points']=m['MainPath'];r['independentSurface']='Highlands293 site sections'
        m['VehicleExclusions']=[dict(Centre=dict(x=v['x'],y=v['y']+5,z=v['z']),Size=dict(x=18,y=22,z=18)) for path in ['MainPath','TemplePath','ReturnPath'] for v in m[path][::2]]
    anchors=[('Cheongrim','청림',[3250,2900],[.50,.60,.48],1.),('Jeokro','적로',[2000,750],[.64,.46,.38],.42),('Cheolong','철옹',[450,3000],[.61,.62,.60],.50),('Hyeongang','현강',[2000,5250],[.39,.49,.51],.78),('Hwanggyeong','황경',[1900,3000],[.64,.59,.43],.55)]
    realms=[]
    for name,label,c,tint,density in anchors:
        poly=[[0,0],[4000,0],[4000,6000],[0,6000]]
        for other,_,b,_,_ in anchors:
            if other!=name:poly=clip(poly,2*(np.array(b)-c),np.dot(b,b)-np.dot(c,c))
        realms.append(dict(Id=name,Label=label,Centre=dict(x=c[0],y=c[1]),Polygon=[dict(x=x,y=z) for x,z in poly],Tint=dict(r=tint[0],g=tint[1],b=tint[2],a=1),TreeDensity=density))
    layout['Realms']=realms;layout['Revision']='reworld-293'
    for m in layout['Mountains']:
        p=m['MainPath'];j=np.argmin([(v['x']-m['TemplePath'][0]['x'])**2+(v['z']-m['TemplePath'][0]['z'])**2 for v in p]);r=np.argmin([(v['x']-m['ReturnPath'][-1]['x'])**2+(v['z']-m['ReturnPath'][-1]['z'])**2 for v in p])
        for suffix,points in [('lower',p[:r+1]),('middle',p[r:j+1]),('upper',p[j:])]:
            route=next(v for v in layout['Routes'] if v['Id']==m['Id']+'_'+suffix);route['Bends']=[dict(x=v['x'],y=v['z']) for v in points[1:-1]]
    mask=np.flipud(np.asarray(Image.open(O/'baseline-surface.png')).astype(float)/255).copy();dz,dx=np.gradient(h,4);slope=np.hypot(dx,dz)
    mask[:,:,0]=np.where(changed,np.maximum(mask[:,:,0],smooth(.25,.8,slope)),mask[:,:,0])
    mask[:,:,3]=np.maximum(mask[:,:,3],walkmask);mask[:,:,0]=np.maximum(mask[:,:,0],rockmask)
    palette=np.zeros((1501,1001,3));xx,zz=np.meshgrid(np.arange(1001)*4,np.arange(1501)*4);dist=np.stack([(xx-c[0])**2+(zz-c[1])**2 for _,_,c,_,_ in anchors]);owner=dist.argmin(0)
    for k,(_,_,_,tint,_) in enumerate(anchors):palette[owner==k]=tint
    Image.fromarray((np.flipud(palette)*255).astype('uint8')).save(A/'Surface/realm293.png')
    # near-field realm floor weights, soft 60m borders: r Cheongrim forest floor, g Jeokro burnt ground,
    # b Cheolong layered scree, a Hyeongang riverbank mud; Hwanggyeong dry stony ground takes the remainder
    floor=np.zeros(h.shape+(4,))
    for k in range(4):
        w=np.zeros(h.shape);w[owner==k]=1.;floor[...,k]=np.clip(blur(w,15),0,1)
    Image.fromarray((np.flipud(floor)*255).round().astype('uint8'),'RGBA').save(A/'Surface/floor293.png')
    Image.fromarray((np.flipud(mask)*255).astype('uint8')).save(A/'Surface/surface.png')
    h.astype('<f4').tofile(A/'Surface/height.bytes')
    (A/'Surface/layout.json').write_text(json.dumps(layout,ensure_ascii=False),encoding='utf8');(A/'Surface/routes.json').write_text(json.dumps(routes,ensure_ascii=False),encoding='utf8')
    (O/'modules.json').write_text(json.dumps(dict(Modules=modules,Sections=record.get('ids',[i for ids in sectioned.values() for i in ids])),ensure_ascii=False),encoding='utf8')
    Image.fromarray((np.flipud(changed)*255).astype('uint8')).save(O/'modified-area.png')
    shade=np.clip((-.45*dx-.35*dz+.8)/np.sqrt(dx*dx+dz*dz+1),0,1)
    cart=(np.array([.69,.66,.56])[None,None,:]*(.65+.35*shade[...,None]))*.84+palette*.16
    cart*=1-(1-smooth(.4,1.,abs((h+4)%8-4)))[...,None]*.12
    Image.fromarray((np.flipud(cart)*255).astype('uint8')).save(A/'Surface/cartography.png')
    report=dict(changedSamples=int(changed.sum()),unchangedSamples=int((~changed).sum()),outsideUnchanged=bool(np.array_equal(h[~changed],base[~changed])),changedPercent=float(changed.mean()*100),realms=[r['Id'] for r in realms],modules=len(modules),source='MountainTrail285 revision 289',baseHash=hashlib.sha256((O/'baseline-height.bytes').read_bytes()).hexdigest(),manualPlay='NOT RUN')
    (O/'static-checks.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(report)
if __name__=='__main__':run()
