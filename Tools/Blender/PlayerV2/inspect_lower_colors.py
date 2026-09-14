"""Read-only atlas sample diagnostics for distinguishing cloth from trousers."""
from pathlib import Path
import json,numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[3]
data=json.loads((root/'Art/PlayerV2/Inspect/LowerRepair/lower-data.json').read_text())
im=np.asarray(Image.open(root/'Art/PlayerV2/Staging/Character/T_DosaV2_base_color.png').convert('RGB'))/255
out={}
for name,d in data.items():
    vs=np.asarray(d['vertices']);faces=np.asarray(d['faces']);uv=np.asarray(d['uv']);centers=vs[faces].mean(axis=1)
    samples=[]
    for weights in [(1/3,1/3,1/3),(.6,.2,.2),(.2,.6,.2),(.2,.2,.6),(.8,.1,.1),(.1,.8,.1),(.1,.1,.8)]:
        p=np.einsum('fvc,v->fc',uv,weights)%1
        colors=im[((1-p[:,1])*(im.shape[0]-1)).astype(int),(p[:,0]*(im.shape[1]-1)).astype(int)]
        samples.append(colors)
    col=np.median(samples,axis=0);lum=col@np.array([.2126,.7152,.0722])
    out[name]={'centers':centers.tolist(),'colors':col.tolist(),'luminance':lum.tolist()}
    low=(centers[:,2]>.32)&(centers[:,2]<.9)
    print(name,'lum percentiles',np.round(np.percentile(lum[low],[5,25,50,75,95]),3))
    for point in [[.0688,-.112,.422],[.199,.036,.316],[0,-.03,.633]]:
        i=np.argmin(np.linalg.norm(centers-point,axis=1));print('  ',np.round(centers[i],3),'color',np.round(col[i],3),'lum',round(lum[i],3))
(root/'Art/PlayerV2/Inspect/LowerRepair/lower-colors.json').write_text(json.dumps(out))
