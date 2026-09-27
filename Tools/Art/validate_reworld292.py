"""Read-only checks of newly generated terrain and cross-file route identities."""
from pathlib import Path
import json, hashlib, math
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
S=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292/Surface'
O=ROOT/'Art/World/Compact/Rebuild/Reworld292'
def run():
    h=np.fromfile(S/'height.bytes',dtype='<f4').reshape(1501,1001)
    layout=json.loads((S/'layout.json').read_text(encoding='utf8'))
    routes=json.loads((S/'routes.json').read_text(encoding='utf8'))['routes']
    old=json.loads((O.parent/'Mountain290/layout.json').read_text(encoding='utf-8-sig'))
    rows=[]
    def check(ok,label):rows.append(dict(pass_=bool(ok),check=label))
    check(np.isfinite(h).all(),'all terrain samples finite')
    check({p['Id'] for p in old['Places']}=={p['Id'] for p in layout['Places']},'stable place IDs')
    check(len({r['id'] for r in routes})==len(routes),'unique route IDs')
    # A 4m authored sample must not jump like a nearest-polyline Voronoi seam.
    steps=max(np.max(np.abs(np.diff(h,axis=0))),np.max(np.abs(np.diff(h,axis=1))))
    check(steps<25,'no >25m adjacent height discontinuity')
    def sample(x,z):
        x=np.clip(x/4,0,999.999);z=np.clip(z/4,0,1499.999);a=int(x);b=int(z);u=x-a;v=z-b
        return (1-v)*((1-u)*h[b,a]+u*h[b,a+1])+v*((1-u)*h[b+1,a]+u*h[b+1,a+1])
    metrics=[]
    for r in routes:
        p=np.array([[v['x'],v['y'],v['z']] for v in r['points']]);d=np.diff(p,axis=0)
        dist=np.linalg.norm(d[:,[0,2]],axis=1);grade=np.abs(d[:,1])/np.maximum(.01,dist)
        check(max(abs(sample(x,z)-y) for x,y,z in p)<.02,'surface route sample '+r['id'])
        limit=.20 if r['vehicle'] else .70
        check(float(grade.max(initial=0))<=limit,'route grade '+r['id'])
        metrics.append(dict(id=r['id'],length=float(np.linalg.norm(d,axis=1).sum()),maxGrade=float(grade.max(initial=0)),vehicle=r['vehicle']))
    report=dict(revision=292,heightSHA256=hashlib.sha256((S/'height.bytes').read_bytes()).hexdigest(),maximumSampleStep=float(steps),checks=rows,routes=metrics,manualPlay='NOT RUN',performance='NOT MEASURED',art='NOT APPROVED')
    (O/'static-checks.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    print(f"{sum(r['pass_'] for r in rows)}/{len(rows)} passed; max sample step {steps:.2f}m")
    print('\n'.join(r['check'] for r in rows if not r['pass_']))
if __name__=='__main__':run()
