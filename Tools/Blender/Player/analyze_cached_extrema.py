import json,pathlib,numpy as np
dirs={'Forward':(1,-1),'Back':(1,1),'Left':(0,1),'Right':(0,-1)}
for f in pathlib.Path('Art/Player/LocomotionPhaseSources').glob('*.json'):
    d=json.loads(f.read_text());ax,sign=next(v for k,v in dirs.items() if f.stem.endswith(k));out=[]
    for side in ['Left','Right']:
        a=np.array([q[side+'Foot'][ax][3]*sign for q in d['poses']]);a=sum(np.roll(a,k) for k in range(-2,3))/5
        peaks=[i for i in range(240) if a[i]>=a[(i-1)%240] and a[i]>a[(i+1)%240] and a[i]-min(a[(i+j)%240] for j in range(-24,25))>.1]
        out.append((side,[round(i/240,4) for i in peaks]))
    print(f.stem,out)
