"""Read-only free-endpoint Mum shortlist on the existing north tributary.

Writes a separate research receipt. Never changes terrain or accepted Mum proof.
"""
from pathlib import Path
import hashlib, json, math, time
import numpy as np
from build_watershed295 import triangle_sample, v3
from find_watershed295_mum_sites import normal_y

WORK = Path(__file__).resolve().parents[2] / 'Art/World/Compact/Rebuild/Watershed295'
FOLDER = WORK / 'Generated'
OUT = WORK / 'Analysis/mum-hyeongang-oblique.json'

def evaluate(h, a, b):
    delta = b[:, :2] - a[:, :2]
    length = np.linalg.norm(delta, axis=1)
    direction = delta / length[:, None]
    side = np.stack([-direction[:, 1], direction[:, 0]], axis=1)
    ok = (length >= 4) & (length <= 40) & (abs(b[:, 2]-a[:, 2]) <= 2) & (np.degrees(np.arctan2(abs(b[:,2]-a[:,2]),length))<=8)
    spread = np.zeros(len(a)); normal = np.ones(len(a))
    for point, sign in [(a, -1), (b, 1)]:
        for across in [-1.3, 0, 1.3]:
            for back in [0, .6]:
                q = point[:, :2]+side*across+direction*(sign*back)
                yy = triangle_sample(h, q[:, 0], q[:, 1])
                nn = normal_y(h, q)
                spread = np.maximum(spread, abs(yy-point[:, 2]))
                normal = np.minimum(normal, nn)
                ok &= (abs(yy-point[:, 2]) <= .18) & (nn >= math.cos(math.radians(20)))
    return ok, spread, normal

def span(h, a, b):
    direction = b[:2]-a[:2]; length = np.linalg.norm(direction); direction /= length
    side = np.array([-direction[1], direction[0]])
    ds = np.unique(np.r_[np.arange(0, length, .1), length, .6, length-.6, min(2.5,length/4), length-min(2.5,length/4)])
    deck = a[2]+.025+(b[2]-a[2])*ds/length
    maximum = np.full(len(ds), -1e6)
    for across in np.linspace(-1.37, 1.37, 29):
        q = a[:2]+direction*ds[:,None]+side*across
        maximum = np.maximum(maximum, triangle_sample(h, q[:,0], q[:,1])-deck)
    interior = (ds >= .6) & (ds <= length-.6)
    join = min(2.5,length/4)
    central = (ds >= join) & (ds <= length-join)
    wider_join = min(10., length/4)
    wider_central = (ds >= wider_join) & (ds <= length-wider_join)
    six_join = min(6., length/4)
    six_central = (ds >= six_join) & (ds <= length-six_join)
    top = float(maximum[interior].max()); ends = float(maximum[~interior].max())
    under = float((-maximum[central]-.45).min())
    wider_under = float((-maximum[wider_central]-.45).min())
    collisions = ds[maximum > -.46]
    start_embed = float(collisions[collisions<=length/2].max()) if np.any(collisions<=length/2) else 0.
    end_embed = float((length-collisions[collisions>=length/2]).max()) if np.any(collisions>=length/2) else 0.
    return top <= -.005 and ends <= .17 and under >= .01, dict(
        maximumInteriorAboveDeck=top, maximumLandingAboveDeck=ends,
        minimumCentralUndersideClearance=under, horizontalSpan=float(length),
        requiredStartEmbedWith1cmMargin=start_embed, requiredEndEmbedWith1cmMargin=end_embed,
        proposed10mEmbedCappedQuarter=wider_join, proposed10mCentralUndersideClearance=wider_under,
        proposed10mTerrainOnlyPass=bool(top<=-.005 and ends<=.17 and wider_under>=.01),
        sixMetreCentralUndersideClearance=float((-maximum[six_central]-.45).min()))

def main():
    begin = time.time()
    h = np.fromfile(FOLDER/'height.bytes', '<f4').reshape(1501,1001)
    hydro = json.loads((FOLDER/'hydro.json').read_text(encoding='utf8'))
    rows = next(r['Rows'] for r in hydro['Reaches'] if r['Id']=='north')
    points = np.array([[r['Position']['x'],r['Position']['z'],r['Position']['y']] for r in rows])
    lo = np.floor(points[:,:2].min(axis=0)-32); hi = np.ceil(points[:,:2].max(axis=0)+32)
    x,z = np.meshgrid(np.arange(lo[0],hi[0]+.5, .5),np.arange(lo[1],hi[1]+.5,.5))
    q = np.column_stack([x.ravel(),z.ravel()]); dist = np.full(len(q),1e9); water = np.zeros(len(q))
    for a,b in zip(points,points[1:]):
        vec=b[:2]-a[:2]; t=np.clip((q-a[:2])@vec/max(.001,vec@vec),0,1)
        d=np.sum((q-a[:2]-t[:,None]*vec)**2,axis=1); near=d<dist
        dist[near]=d[near];water[near]=a[2]+(b[2]-a[2])*t[near]
    y=triangle_sample(h,q[:,0],q[:,1]);n=normal_y(h,q)
    mask=(dist<=32**2)&(y>=water+.05)&(n>=math.cos(math.radians(20)))
    # A genuine gap near a bank cannot begin on a broad level interior; retain
    # every dry support point within32m of this narrow tributary for pairing.
    q=np.column_stack([q[mask],y[mask]]); print('dry near-channel samples',len(q),flush=True)
    bins={}
    for i,key in enumerate(np.floor(q[:,:2]/20).astype(int)):
        bins.setdefault(tuple(key),[]).append(i)
    counts=dict(drySamples=len(q),geometricPairs=0,landingPairs=0,fullSpanCandidates=0)
    candidates=[]; rejected=[]
    keys=sorted(bins)
    for ki,key in enumerate(keys):
        ai=np.array(bins[key]); av=q[ai]
        for dx in range(-2,3):
            for dz in range(-2,3):
                other=(key[0]+dx,key[1]+dz)
                if other not in bins or other<key:continue
                bi=np.array(bins[other]); bv=q[bi]
                for offset in range(0,len(av),100):
                    a0=av[offset:offset+100]; ida=ai[offset:offset+100]
                    distance=np.linalg.norm(a0[:,None,:2]-bv[None,:,:2],axis=2)
                    valid=(distance>=4)&(distance<=40)&(abs(a0[:,None,2]-bv[None,:,2])<=2)&(ida[:,None]<bi[None,:])
                    ia,ib=np.where(valid)
                    if not len(ia):continue
                    a=a0[ia];b=bv[ib]; mid=(a+b)/2
                    gap=triangle_sample(h,mid[:,0],mid[:,1])<mid[:,2]-.65
                    a=a[gap];b=b[gap];counts['geometricPairs']+=len(a)
                    if not len(a):continue
                    good,spread,norm=evaluate(h,a,b)
                    for j in np.where(good)[0]:
                        counts['landingPairs']+=1
                        ok,details=span(h,a[j],b[j])
                        record=dict(id='hyeongang_oblique_'+str(counts['landingPairs']),realm='Hyeongang',reach='north',
                            start=v3(a[j,0],a[j,2],a[j,1]),end=v3(b[j,0],b[j,2],b[j,1]),
                            endDelta=float(abs(a[j,2]-b[j,2])),minBankNormalY=float(norm[j]),
                            maxLandingHeightSpread=float(spread[j]),**details)
                        if ok:
                            counts['fullSpanCandidates']+=1;candidates.append(record)
                        else:rejected.append(record)
        if ki%20==0:print('bins',ki,'/',len(keys),counts,'seconds',round(time.time()-begin,1),flush=True)
    candidates.sort(key=lambda a:(-a['minimumCentralUndersideClearance'],a['maxLandingHeightSpread']))
    proposed=sorted([r for r in rejected if r['proposed10mTerrainOnlyPass']],key=lambda r:(max(r['requiredStartEmbedWith1cmMargin'],r['requiredEndEmbedWith1cmMargin']),-r['proposed10mCentralUndersideClearance']))
    counts['proposed10mQuarterCapTerrainOnlyPass']=len(proposed)
    result=dict(terrainRevision='Generated-004',sourceHeightSha256=hashlib.sha256((FOLDER/'height.bytes').read_bytes()).hexdigest(),
        method='Independent free endpoints on a0.5m grid within32m of north tributary; exact4m terrain triangles, six landing samples, full span0.1m by29 width samples; no terrain/Unity modifications; actual props/water/collider validation remains required.',
        seconds=time.time()-begin,counts=counts,candidates=candidates[:40],proposedEmbedCandidates=proposed[:40],rejectedCandidates=sorted(rejected,key=lambda r:max(r['maximumInteriorAboveDeck']+.005,r['maximumLandingAboveDeck']-.17,.01-r['minimumCentralUndersideClearance']))[:100])
    OUT.write_text(json.dumps(result,indent=2),encoding='utf8');print(json.dumps(dict(path=str(OUT),counts=counts,seconds=result['seconds']),indent=2))

if __name__=='__main__':main()
