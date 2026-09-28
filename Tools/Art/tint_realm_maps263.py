"""Tint candidate map copies from immutable exported inputs, in world coordinates."""
from pathlib import Path
import json
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Sinmok263'
DEST=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/Sinmok263'
COLORS={'realm_cheongrim':(.24,.46,.58),'realm_jeokro':(.60,.28,.24),'realm_hwanggyeong':(.72,.60,.30),'realm_cheolong':(.98,.97,.91),'realm_hyeongang':(.20,.23,.26)}

def main():
    catalog=json.loads((OUT/'catalog.json').read_text(encoding='utf-8-sig'))
    realms=[e for e in catalog['Entries'] if e['Priority']==0]
    sources=json.loads((OUT/'map-sources.json').read_text(encoding='utf-8-sig'))['images']
    DEST.mkdir(parents=True,exist_ok=True)
    for source in sources:
        im=Image.open(OUT/'MapSources'/(source['id']+'.png')).convert('RGB')
        a=np.asarray(im).astype(np.float32)/255;h,w=a.shape[:2];r=source['rect']
        # JsonUtility serializes Rect as x/y/width/height on current Unity.
        x=(r['x']+(np.arange(w)+.5)/w*r['width'])*4000
        z=(r['y']+(1-(np.arange(h)+.5)/h)*r['height'])*6000
        total=np.zeros((h,w),np.float32);wash=np.zeros_like(a)
        for realm in realms:
            p=realm['Polygon'];xmin=min(q['x'] for q in p);xmax=max(q['x'] for q in p);zmin=min(q['y'] for q in p);zmax=max(q['y'] for q in p)
            def smooth(v):
                v=np.clip(v,0,1);return v*v*(3-2*v)
            wx=smooth((x-xmin+35)/70)*smooth((xmax-x+35)/70)
            wz=smooth((z-zmin+35)/70)*smooth((zmax-z+35)/70)
            weight=wz[:,None]*wx[None,:];total+=weight;wash+=weight[:,:,None]*np.array(COLORS[realm['Id']])
        wash/=np.maximum(total[:,:,None],1e-6)
        # Modest glazed wash; dark ink and relief remain legible. White gets a pale warm lift.
        alpha=.085*np.clip(total,0,1)[:,:,None]
        result=a*(1-alpha)+wash*alpha
        Image.fromarray(np.uint8(np.clip(result*255,0,255))).save(DEST/('Map_'+source['id']+'.png'))
        if source['id']=='macro':Image.fromarray(np.uint8(result*255)).resize((800,1200)).save(OUT/'map-preview.png')
    print('Tinted',len(sources),'private map images; no source overwrite')

if __name__=='__main__':main()
