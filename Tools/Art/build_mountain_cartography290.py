"""Build only the independent #290 atlas from its freshly exported physical surface."""
import hashlib
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
from tint_realm_maps263 import COLORS

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Mountain290/Cartography'
BOUNDS=(2500,1200,1500,2600)

def tint(image,rect,realms):
    a=np.asarray(image.convert('RGB'),dtype=np.float32)/255
    h,w=a.shape[:2];x0,z0,dx,dz=rect
    x=x0+(np.arange(w,dtype=np.float32)+.5)/w*dx
    z=z0+(1-(np.arange(h,dtype=np.float32)+.5)/h)*dz
    total=np.zeros((h,w),np.float32);wash=np.zeros_like(a)
    def smooth(v):
        v=np.clip(v,0,1);return v*v*(3-2*v)
    for realm in realms:
        p=realm['Polygon']
        if len(p)<3:continue
        left,right=min(q['x'] for q in p),max(q['x'] for q in p)
        bottom,top=min(q['y'] for q in p),max(q['y'] for q in p)
        weight=(smooth((z-bottom+35)/70)*smooth((top-z+35)/70))[:,None]*(smooth((x-left+35)/70)*smooth((right-x+35)/70))[None,:]
        total+=weight;wash+=weight[:,:,None]*np.asarray(COLORS[realm['Id']],dtype=np.float32)
    wash/=np.maximum(total[:,:,None],1e-6)
    alpha=.085*np.clip(total,0,1)[:,:,None]
    return Image.fromarray(np.uint8(np.clip((a*(1-alpha)+wash*alpha)*255,0,255)))

def main():
    generator=ROOT/'Tools/Art/build_compact_cartography.py'
    subprocess.run([sys.executable,str(generator),'--output',str(OUT)],check=True)
    subprocess.run([sys.executable,str(generator),'--output',str(OUT),'--region','--regional-bounds',*map(str,BOUNDS)],check=True)
    realms=[e for e in json.loads((OUT/'catalog.json').read_text(encoding='utf-8-sig'))['Entries'] if e['Priority']==0]
    macro=tint(Image.open(OUT/'terrain.png'),(0,0,4000,6000),realms)
    region=tint(Image.open(OUT/'region.png'),BOUNDS,realms)
    atlas=macro.resize((3200,4800),Image.Resampling.LANCZOS)
    atlas.paste(region.resize((1200,2080),Image.Resampling.LANCZOS),(2000,1760))
    macro.save(OUT/'terrain.png');region.save(OUT/'region.png');atlas.save(OUT/'explored.png')
    atlas.resize((800,1200),Image.Resampling.LANCZOS).save(OUT/'preview.png')
    inputs={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [OUT/'heights.f32',OUT/'placements.json',OUT.parent/'layout.json',OUT/'catalog.json']}
    (OUT/'receipt.json').write_text(json.dumps({'inputs':inputs,'regionWorldBounds':BOUNDS,'contours':[10,50],'atlasPixels':[3200,4800],'scope':'candidate only; runtime discovery still gates detailed roads and markers'},indent=2),encoding='utf-8')
    print('Candidate #290 map built; canonical images unchanged')

if __name__=='__main__':main()
