"""Numeric terrain morphology, not a painted colour map. XZ origin is (0,0)."""
from pathlib import Path
import hashlib, json
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
source=ROOT/'Art/World/Compact/Rebuild/Cartography/heights.f32'
out=ROOT/'Oheangbu/Assets/_Project/Art/World/Surface276'
out.mkdir(parents=True,exist_ok=True)
h=np.fromfile(source,dtype='<f4').reshape(2400,1600)
def neighbourhood(radius):
    p=np.pad(h,radius,mode='edge')
    return sum(p[radius+z:radius+z+2400,radius+x:radius+x+1600]
               for z,x in [(radius,0),(-radius,0),(0,radius),(0,-radius),
                           (radius,radius),(-radius,radius),(radius,-radius),(-radius,-radius)])/8
curvature=h-neighbourhood(8) # 20m support: positive crests, negative deposition hollows
relief=h-neighbourhood(48) # 120m local exposure, not absolute elevation
channels=np.stack([np.clip(curvature/8+.5,0,1),np.clip(relief/70+.5,0,1),
                   np.clip(h/500,0,1),np.ones_like(h)],axis=-1)
# PNG top is north. Unity UV.y grows north from texture bottom.
im=Image.fromarray(np.uint8(np.round(channels[::-1]*255)),'RGBA')
im.resize((512,768),Image.Resampling.BILINEAR).save(out/'Morphology.png')
report=ROOT/'Art/World/Compact/Rebuild/Surface276'
report.mkdir(parents=True,exist_ok=True)
(report/'field.json').write_text(json.dumps(dict(source=str(source),sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    size=[512,768],world=[0,0,4000,6000],curvature_support_m=20,exposure_support_m=120,
    channels=['curvature / 8 + .5','local relief / 70 + .5','height / 500','1']),indent=2))
print('Morphology: 512 x 768 linear field; deterministic source SHA recorded')
