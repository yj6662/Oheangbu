"""Read-only actual terrain shortlist for the runtime Mum landing contract."""
from pathlib import Path
import argparse,hashlib,json,math
import numpy as np
from build_watershed295 import triangle_sample,sample,v3

WORK=Path(__file__).resolve().parents[2]/'Art/World/Compact/Rebuild/Watershed295'

def normal_y(h,q):
    q=np.asarray(q)/4;i=np.floor(q).astype(int);i[...,0]=np.clip(i[...,0],0,999);i[...,1]=np.clip(i[...,1],0,1499)
    u,v=(q-i).T;x,z=i.T
    gx=np.where(u+v<=1,h[z,x+1]-h[z,x],h[z+1,x+1]-h[z+1,x])/4
    gz=np.where(u+v<=1,h[z+1,x]-h[z,x],h[z+1,x+1]-h[z,x+1])/4
    return 1/np.sqrt(1+gx*gx+gz*gz)

def find(folder):
    h=np.fromfile(folder/'height.bytes','<f4').reshape(1501,1001);water=np.fromfile(folder/'waterlevel.bytes','<f4').reshape(h.shape)
    hydro=json.loads((folder/'hydro.json').read_text(encoding='utf8'));candidates=[];rejected=[];counts={}
    for reach in hydro['Reaches']:
        if reach['Id'].startswith('main'):continue
        rows=reach['Rows'];p=np.array([[r['Position']['x'],r['Position']['z']]for r in rows]);flow=np.gradient(p,axis=0);flow/=np.linalg.norm(flow,axis=1)[:,None]
        good_banks=0;paired=0
        for i in range(2,len(p)-2):
            centre=p[i];base=np.array([-flow[i,1],flow[i,0]])
            for angle in [-20,-10,0,10,20]:
                c,s=math.cos(math.radians(angle)),math.sin(math.radians(angle));normal=np.array([c*base[0]-s*base[1],s*base[0]+c*base[1]]);side=np.array([-normal[1],normal[0]])
                bank=[]
                for sign in [-1,1]:
                    distances=np.arange(5.,24.,.25);q=centre+normal[None,:]*distances[:,None]*sign
                    y=triangle_sample(h,q[:,0],q[:,1]);valid=y>=rows[i]['Position']['y']+.025;minnormal=np.ones(len(q));heightspread=np.zeros(len(q))
                    for across in [-1.3,0,1.3]:
                        for back in [0,.6]:
                            at=q+side*across+normal*sign*back;yy=triangle_sample(h,at[:,0],at[:,1]);n=normal_y(h,at);minnormal=np.minimum(minnormal,n);heightspread=np.maximum(heightspread,abs(yy-y));wy=sample(water,at[:,0],at[:,1])
                            valid&=(n>=math.cos(math.radians(20)))&(abs(yy-y)<=.18)&((wy<-9000)|(yy>=wy+.025))
                    ids=np.where(valid)[0];good_banks+=len(ids);bank.append([(q[k],float(y[k]),float(distances[k]),float(minnormal[k]),float(heightspread[k]))for k in ids])
                if not bank[0] or not bank[1]:continue
                for a in bank[0]:
                    for b in bank[1]:
                        length=a[2]+b[2];rise=abs(a[1]-b[1])
                        if length>40 or rise>2 or math.degrees(math.atan2(rise,length))>8:continue
                        paired+=1;d=np.arange(.6,length-.599,.25);q=a[0]+normal*d[:,None];deck=a[1]+.025+(b[1]-a[1])*d/length
                        clearance=np.full(len(d),1e6)
                        for across in [-1.33,0,1.33]:
                            at=q+side*across;yy=triangle_sample(h,at[:,0],at[:,1]);clearance=np.minimum(clearance,deck-.45-yy)
                        record=dict(id='natural_'+reach['Id']+'_'+str(i)+'_'+str(angle)+'_'+str(round(a[2],2))+'_'+str(round(b[2],2)),realm='Hyeongang' if reach['Id']=='north' else reach['Id'],reach=reach['Id'],row=i,start=v3(a[0][0],a[1],a[0][1]),end=v3(b[0][0],b[1],b[0][1]),length=round(length,4),waterY=rows[i]['Position']['y'],endDelta=round(rise,4),minBankNormalY=min(a[3],b[3]),maxLandingHeightSpread=max(a[4],b[4]),minimumTerrainClearance=float(clearance.min()),minimumClearanceDistance=float(d[int(clearance.argmin())]))
                        below=np.where(clearance<-.03)[0]
                        record['undersideIntersectionStart']=float(d[below[0]]) if len(below) else None
                        record['undersideIntersectionEnd']=float(d[below[-1]]) if len(below) else None
                        record['maximumGroundAboveDeck']=float(-clearance.min()-.45)
                        middle=(d>=2)&(d<=length-2)
                        record['minimumClearanceExcluding2mEnds']=float(clearance[middle].min()) if middle.any() else None
                        record['spanSamples']=[dict(distance=round(float(dd),3),groundRelativeToDeck=round(float(-cc-.45),4))for dd,cc in zip(d,clearance)]
                        if clearance.min()<-.03:rejected.append(record);continue
                        candidates.append(record)
        counts[reach['Id']]=dict(LandingCandidates=good_banks,PairedSpans=paired,ClearSpans=sum(c['reach']==reach['Id']for c in candidates))
    candidates.sort(key=lambda c:(c['reach']!='north',-c['minimumTerrainClearance'],c['maxLandingHeightSpread']))
    selected=[]
    for c in candidates:
        q=np.array([c['start']['x'],c['start']['z']])
        if any(np.linalg.norm(q-[s['start']['x'],s['start']['z']])<16 for s in selected):continue
        c['id']='natural_'+c['reach']+'_'+str(c['row']);c['realm']='Hyeongang' if c['reach']=='north' else c['reach'];selected.append(c)
        if len(selected)>=5:break
    result=dict(terrainRevision=folder.name,sourceHeightSha256=hashlib.sha256((folder/'height.bytes').read_bytes()).hexdigest(),sites=selected,rejectedCandidates=sorted(rejected,key=lambda c:-c['minimumTerrainClearance']),counts=counts,method='Exact4m terrain triangles; width2.6m six bank samples, back.6m, height tolerance.18m, banknormal cos20;4–40mspan,≤2m end rise,≤8degrees;terrain-only full-span underside clearance. Actual runtime props/colliders remain unverified.')
    (WORK/'Analysis').mkdir(exist_ok=True);(WORK/'Analysis/mum-sites.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(result,ensure_ascii=True,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--folder',default=str(WORK/'Generated'));args=parser.parse_args();find(Path(args.folder))
