"""Read-only #295 terrain/bridge support audit; writes additive Analysis reports."""
from pathlib import Path
import json
import numpy as np
from build_watershed295 import sample, v3

ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'Art/World/Compact/Rebuild/Watershed295'
GEN=WORK/'Generated'
OUT=WORK/'Analysis'

def triangle_sample(h,p):
    q=np.asarray(p)/4;i=np.floor(q).astype(int);i[:,0]=np.clip(i[:,0],0,999);i[:,1]=np.clip(i[:,1],0,1499)
    u,v=(q-i).T;x,z=i.T
    return np.where(u+v<=1,h[z,x]+(h[z,x+1]-h[z,x])*u+(h[z+1,x]-h[z,x])*v,
                    h[z+1,x+1]+(h[z+1,x]-h[z+1,x+1])*(1-u)+(h[z,x+1]-h[z+1,x+1])*(1-v))

def resample(points,step=3):
    p=np.array([[v['x'],v['z']] for v in points],float)
    s=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p,axis=0),axis=1))]
    distance=np.linspace(0,s[-1],max(2,int(np.ceil(s[-1]/step))+1))
    return np.column_stack([np.interp(distance,s,p[:,i]) for i in [0,1]]),distance

def poly_distance(q,p):
    best=np.full(len(q),np.inf);height=np.zeros(len(q))
    for a,b in zip(p,p[1:]):
        vec=b[[0,2]]-a[[0,2]];t=np.clip(((q-a[[0,2]])*vec).sum(1)/max(1e-9,float(vec@vec)),0,1)
        at=a[[0,2]]+t[:,None]*vec;d=np.linalg.norm(q-at,axis=1);take=d<best
        best[take]=d[take];height[take]=a[1]+t[take]*(b[1]-a[1])
    return best,height

def audit(points,base,h,water,protect,crossings):
    q,s=resample(points);y=triangle_sample(h,q);old=triangle_sample(base,q)
    height=np.array([v['y'] for v in points],float)
    original=np.array([[v['x'],v['z']] for v in points]);old_s=np.r_[0,np.cumsum(np.linalg.norm(np.diff(original,axis=0),axis=1))]
    authored=np.interp(s,old_s,height)
    on_bridge=np.zeros(len(q),bool);bridge_clearance=np.full(len(q),np.nan)
    for c in crossings:
        p=np.array([[v['x'],v['y'],v['z']] for v in c['Points']],float)
        d,by=poly_distance(q,p);take=d<c['RouteWidth']*.5+.5
        on_bridge[take]=True;bridge_clearance[take]=by[take]-y[take]
    ix=np.clip(np.rint(q[:,0]/4).astype(int),0,1000);iz=np.clip(np.rint(q[:,1]/4).astype(int),0,1500)
    on_protected=protect[iz,ix]>0
    # Independent authored highland collision has its own accepted profile.
    exempt=on_bridge|on_protected
    ds=np.diff(s);grade=np.abs(np.diff(y))/np.maximum(.001,ds);oldgrade=np.abs(np.diff(old))/np.maximum(.001,ds)
    bad=(grade>.7)&~exempt[:-1]&~exempt[1:]
    changed=np.maximum(abs(y[:-1]-old[:-1]),abs(y[1:]-old[1:]))>.10
    introduced=bad&changed
    wy=sample(water,q[:,0],q[:,1]);unsupportedwet=(wy>y+.10)&(wy>-9000)&~exempt
    issues=[]
    for i in np.where(bad)[0]:
        p=(q[i]+q[i+1])*.5
        issues.append(dict(XZ=p.round(3).tolist(),Grade=round(float(grade[i]),4),BeforeGrade=round(float(oldgrade[i]),4),NewTerrain=bool(changed[i]),Arc=round(float(s[i]),2)))
    issues.sort(key=lambda x:(x['NewTerrain'],x['Grade']),reverse=True)
    return dict(Length=float(s[-1]),Samples=len(q),MaxTerrainGrade=float(grade.max()),UnsafeSegments=int(bad.sum()),NewTerrainUnsafeSegments=int(introduced.sum()),UnsafeMetres=float(ds[bad].sum()),UnsupportedWetSamples=int(unsupportedwet.sum()),WetCoordinates=[v3(q[i,0],wy[i],q[i,1]) for i in np.where(unsupportedwet)[0][::max(1,int(unsupportedwet.sum()/20))]],Worst=issues[:30],AllIssues=issues,ProtectedSamples=int(on_protected.sum()),BridgeSamples=int(on_bridge.sum()),AuthoredHeightErrorP95=float(np.percentile(abs(authored-y),95))),q,s

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    h=np.fromfile(GEN/'height.bytes',dtype='<f4').reshape(1501,1001);base=np.fromfile(WORK/'InputSurface294/height.bytes',dtype='<f4').reshape(1501,1001)
    water=np.fromfile(GEN/'waterlevel.bytes',dtype='<f4').reshape(1501,1001);protect=np.fromfile(GEN/'protected.bytes',dtype='u1').reshape(1501,1001)
    layout=json.loads((GEN/'layout.json').read_text(encoding='utf8'));routes=json.loads((GEN/'routes.json').read_text(encoding='utf8'))['routes'];crossings=json.loads((GEN/'crossings.json').read_text(encoding='utf8'))['Crossings'];specs={r['Id']:r for r in layout['Routes']}
    records=[]
    for route in routes:
        spec=specs.get(route['id'])
        if spec and spec['Role']!=0:continue
        result,q,s=audit(route['points'],base,h,water,protect,crossings);result['Id']=route['id'];result['Vehicle']=bool(route.get('vehicle',False));records.append(result)
    content=json.loads((WORK/'content-before.json').read_text(encoding='utf8'));content_results=[]
    for name in ['MainPath','BranchPath']:
        result,q,s=audit(content[name],base,h,water,protect,crossings);result['Id']=name;content_results.append(result)
    report=dict(Routes=records,ContentPaths=content_results,Method='Exact4m terrain triangles sampled every3m; exclude physical bridge Points corridor and independent protected highland footprints; NewTerrain means >0.10m local294-to295 terrain change.')
    (OUT/'route-support-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    for r in records+content_results:
        if r['NewTerrainUnsafeSegments'] or r['UnsupportedWetSamples']:
            print(json.dumps({k:r[k] for k in ['Id','Length','UnsafeSegments','NewTerrainUnsafeSegments','UnsupportedWetSamples','Worst','WetCoordinates']},ensure_ascii=False))

if __name__=='__main__':main()
