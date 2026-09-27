"""Compose detailed surveyed atlas and a rock-cut cave plan from authored galleries."""
from pathlib import Path
import json
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/World/Compact/Rebuild/Cartography'
# Full map and zoomed map share the same regional illustration.
im=Image.open(OUT/'terrain.png').resize((3200,4800),Image.Resampling.LANCZOS)
reg=Image.open(OUT/'region.png').resize((1200,1520),Image.Resampling.LANCZOS)
im.paste(reg,(2000,2480));im.save(OUT/'explored.png')
d=json.loads((OUT.parent/'CaveV4/geometry.json').read_text());W,H=1536,1152
xmin,zmin,xspan,zspan=3420,1750,160,120
pt=lambda p:((p['x']-xmin)/xspan*W,(1-(p['z']-zmin)/zspan)*H)
mask=Image.new('L',(W,H));dr=ImageDraw.Draw(mask)
floor_mesh=next(m for m in d['meshes'] if m['name']=='Natural_Cave_Floor')
verts=[pt({'x':v['x']+d['origin']['x'],'z':v['z']+d['origin']['z']}) for v in floor_mesh['vertices']]
tri=floor_mesh['triangles']
for i in range(0,len(tri),3):dr.polygon([verts[tri[i+j]] for j in range(3)],fill=255)
# Candidate mouth cross-section comes from the same boundary used to build its mesh.
# Keep the old gallery floor mask; contract only the rebuilt entrance segment.
mouth_path=OUT.parent/'Mouth241/aperture.json'
if mouth_path.exists():
 mouth=json.loads(mouth_path.read_text());boundary=mouth.get('boundary')
 if boundary:
  yy,xx=np.mgrid[:H,:W];wx=xmin+xx/W*xspan;wz=zmin+(1-yy/H)*zspan
  dx=wx-mouth['origin']['x'];dz=wz-mouth['origin']['z']
  u=dx*mouth['side']['x']+dz*mouth['side']['z'];depth=dx*mouth['forward']['x']+dz*mouth['forward']['z']
  def limits(polygon):
   crossings=[]
   for a,b in zip(polygon,polygon[1:]+polygon[:1]):
    if (a['y']>=1.4)!=(b['y']>=1.4):
     crossings.append(a['x']+(b['x']-a['x'])*(1.4-a['y'])/(b['y']-a['y']))
   return min(crossings),max(crossings)
  lo,hi=limits(mouth['aperture']);endlo,endhi=limits(boundary)
  t=np.clip((depth-mouth['front'])/(mouth['cut']-mouth['front']),0,1)
  blend=np.clip((t-.18)/.82,0,1);blend=blend*blend*(3-2*blend)
  bend=.55*np.sin(t*np.pi*2)*np.sin(t*np.pi)
  permitted=(u-bend>=lo+(endlo-lo)*blend)&(u-bend<=hi+(endhi-hi)*blend)
  values=np.array(mask);values[(depth<mouth['cut'])&(~permitted)]=0
  mask=Image.fromarray(values)
rng=np.random.default_rng(234);rock=np.array(mask.filter(ImageFilter.MaxFilter(41)),float)/255
# Rock edges retain small broken shelves, never shifting the actual walkable plan.
floor=np.array(mask,float)/255;edge=np.array(mask.filter(ImageFilter.GaussianBlur(9)),float)/255
y,x=np.mgrid[:H,:W];grain=rng.normal(0,1,(H,W))
strata=np.sin(x*.053+y*.027+np.sin(y*.012)*2)*2.4+np.sin(x*.012-y*.023)*3
base=np.zeros((H,W,3))+[89,87,76];base+=(strata+grain*.8)[...,None]
base+=rock[...,None]*np.array([20,16,10]);base+=floor[...,None]*np.array([77,70,50])
base-=(floor-edge).clip(0,1)[...,None]*36
image=Image.fromarray(np.uint8(np.clip(base,0,255))).convert('RGBA');ink=Image.new('RGBA',(W,H));dr=ImageDraw.Draw(ink)
# Rock facets sit outside the walkable footprint.
for _ in range(1250):
 xx=int(rng.integers(5,W-25));yy=int(rng.integers(5,H-25))
 if floor[yy,xx]>.1:continue
 length=int(rng.integers(6,28));dr.line([(xx,yy),(xx+length*.5,yy-4),(xx+length,yy+6)],fill=(38,40,35,int(rng.integers(20,65))),width=1)
# Timber portal crossbars are keyed to real lamps, not objective markers.
for p in d['lights']:
 xx,yy=pt(p);dr.line([(xx-14,yy-14),(xx+14,yy+14)],fill=(90,69,43,145),width=3)
image=Image.alpha_composite(image,ink);image.convert('RGB').save(OUT/'cave.png');image.resize((768,576)).save(OUT/'cave_preview.png')
(OUT/'explored_generation.json').write_text(json.dumps({'atlas':[3200,4800],'regionalSource':'region.png','contoursMetres':{'minor':10,'index':50},'caveWorldRect':[xmin,zmin,xspan,zspan],'discovery':'runtime saved 32m cells; no automatic region-wide unlock'},indent=2))
print('Saved surveyed atlas and authored cave illustration')
