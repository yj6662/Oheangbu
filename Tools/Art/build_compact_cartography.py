"""Deterministic cartography from the Compact terrain and placement ledger (no reference image sampling)."""
import json,sys,argparse
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
def gaussian_filter(a,radius):
 if radius==2:
  k=np.exp(-np.arange(-6,7,dtype=float)**2/8);k/=k.sum()
  p=np.pad(a,((0,0),(6,6)),mode='edge');a=sum(p[:,i:i+a.shape[1]]*v for i,v in enumerate(k))
  p=np.pad(a,((6,6),(0,0)),mode='edge');return sum(p[i:i+a.shape[0],:]*v for i,v in enumerate(k))
 low=float(a.min());span=max(.001,float(a.max())-low)
 im=Image.fromarray(np.uint8(np.clip((a-low)/span*255,0,255)))
 r=max(radius) if isinstance(radius,tuple) else radius
 return np.array(im.filter(ImageFilter.GaussianBlur(r)),dtype=float)/255*span+low
def zoom(a,factor,order=1):
 return np.array(Image.fromarray(a).resize((round(a.shape[1]*factor),round(a.shape[0]*factor)),Image.Resampling.BICUBIC))
ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--output',type=Path,default=ROOT/'Art/World/Compact/Rebuild/Cartography')
parser.add_argument('--region',action='store_true')
parser.add_argument('--regional-bounds',type=float,nargs=4,default=(2500,1200,1500,1900))
args=parser.parse_args()
OUT=args.output.resolve()
layout=json.loads((OUT.parent/'layout.json').read_text(encoding='utf-8-sig'))
regional=args.region
xmin,zmin,xspan,zspan=tuple(args.regional_bounds) if regional else (0,0,4000,6000)
scale=5 if regional else 1
metadata=json.loads((OUT/'heights.json').read_text()) if (OUT/'heights.json').exists() else {'width':800,'height':1200,'cellMetres':5}
cell=metadata['cellMetres']
h=np.fromfile(OUT/'heights.f32',dtype='<f4').reshape(metadata['height'],metadata['width'])
if regional:h=h[round(zmin/cell):round((zmin+zspan)/cell),round(xmin/cell):round((xmin+xspan)/cell)]
h=h[::-1].copy()
# North is up; the same elevations drive 3D construction and this relief.
h=zoom(h,cell*(2 if regional else .4),order=1); H,W=h.shape
rng=np.random.default_rng(233)
dy,dx=np.gradient(gaussian_filter(h,2));dy*=scale;dx*=scale;slope=np.hypot(dx,dy)
shade=np.clip((dx*.7-dy*.6),-3,3)
relief=np.clip((h-40)/260,0,1)
fibers=gaussian_filter(rng.normal(0,1,(H,W)),(.6,2.8))
broad=gaussian_filter(rng.normal(0,1,(H,W)),16)*16
base=np.zeros((H,W,3))+np.array([217,208,175])
base-=relief[...,None]*np.array([58,57,48]);base+=shade[...,None]*17
base+=fibers[...,None]*2+broad[...,None]*2
# Hills retain open paper on the lit face, ink wash on their sheltered face.
base-=np.clip(slope-.5,0,3)[...,None]*np.array([4,4,3])
# Ten-metre minor and fifty-metre index contours from the final surface. Float
# interpolation preserves elevations; a sub-metre regional filter removes raster corners.
contour_count=0
if regional:
 ch=gaussian_filter(h,2)
 grad=np.hypot(*np.gradient(ch))
 def isoline(interval,width):
  phase=np.abs((ch+interval*.5)%interval-interval*.5)
  return np.clip(1-phase/np.maximum(grad*width,.08),0,1)*np.clip(grad/.075,0,1)
 minor=isoline(10,.85);major=isoline(50,1.25)
 # Crowded steep contours remain subordinate to terrain shading and icons.
 minor*=1-np.clip((grad-2.5)/6,0,.7)
 base-=minor[...,None]*np.array([17,16,13])+major[...,None]*np.array([12,11,9])
 contour_count=int((float(ch.max())-float(ch.min()))/10)
image=Image.fromarray(np.uint8(np.clip(base,0,255))).convert('RGBA')
ink=Image.new('RGBA',(W,H));draw=ImageDraw.Draw(ink,'RGBA')
def pt(v):return ((v['x']-xmin)/xspan*W,(1-(v['y']-zmin)/zspan)*H)
# Broken brush hatching follows the measured fall line. Authored ridge polylines
# are placement guides, not visible geography: drawing them made angular ladders.
for y in range(8,H-12,10*scale):
 for x in range(8,W-12,9*scale):
  xx=int(x+rng.integers(-3,4));yy=int(y+rng.integers(-3,4))
  if slope[yy,xx]<.65 or h[yy,xx]<85 or rng.random()>.5:continue
  length=min(17,5+slope[yy,xx]*2.5)*scale
  p=np.array([float(xx),float(yy)]);path=[tuple(p)]
  for _ in range(10):
   ix=int(np.clip(p[0],0,W-2));iy=int(np.clip(p[1],0,H-2))
   fx=p[0]-ix;fy=p[1]-iy
   def sample(field):
    return (field[iy,ix]*(1-fx)+field[iy,ix+1]*fx)*(1-fy)+(field[iy+1,ix]*(1-fx)+field[iy+1,ix+1]*fx)*fy
   v=np.array([sample(dx),sample(dy)]);norm=np.linalg.norm(v)
   if norm<.2:break
   p-=v/norm*length/10
   if not(1<=p[0]<W-2 and 1<=p[1]<H-2):break
   path.append(tuple(p))
  if len(path)>1:
   alpha=int(min(46,16+slope[yy,xx]*8))
   for i in range(len(path)-1):
    draw.line(path[i:i+2],fill=(68,68,52,int(alpha*(1-i/len(path)*.65))),width=1)
image=Image.alpha_composite(image,ink)
ink=Image.new('RGBA',(W,H));draw=ImageDraw.Draw(ink,'RGBA')
# River banks and quieter blue-grey water follow the recorded watercourse.
river=[pt(v) for v in layout['River']]
for width,col in [(13,(81,86,71,105)),(10,(128,153,149,230)),(6,(151,172,162,240))]:draw.line(river,fill=col,width=width,joint='curve')
image=Image.alpha_composite(image,ink)
# Only authored tree placements receive forest symbols; no invented settlements or lakes.
art=json.loads((OUT/'placements.json').read_text(encoding='utf-8-sig'))
proto={p['Id']:p for p in art['Prototypes']}
trees=[p for p in art['FixedPlacements'] if proto[p['PrototypeId']]['Category']==0]
canopy=Image.new('RGBA',(W,H));cd=ImageDraw.Draw(canopy,'RGBA')
occupied=set()
for p in sorted(trees,key=lambda p:-p['Position']['z']):
 pos=p['Position'];x=(pos['x']-xmin)/xspan*W;y=(1-(pos['z']-zmin)/zspan)*H
 if not(0<=x<W and 0<=y<H):continue
 cluster=(int(x//(5*scale)),int(y//(5*scale)))
 if cluster in occupied:continue
 occupied.add(cluster);s=float(rng.uniform(2.2,4.6))*scale
 # A bent trunk and three uneven, spreading pine crowns, drawn with ink wash.
 cd.line([(x-s*.15,y+s*.7),(x+s*.08,y-s*.6),(x,y-s*2.25)],fill=(64,68,47,170),width=max(1,int(scale*.35)))
 for k in range(3):
  yy=y-s*1.9+k*s*.72;ww=s*(.65+k*.29);lean=float(rng.uniform(-.15,.15))*s
  crown=[(x-ww,yy+s*.28),(x-ww*.72,yy-s*.08),(x-ww*.4,yy-s*.02),(x+lean,yy-s*.39),(x+ww*.22,yy-s*.18),(x+ww*.5,yy-s*.22),(x+ww,yy+s*.18),(x+ww*.68,yy+s*.35),(x+ww*.17,yy+s*.28),(x-ww*.35,yy+s*.39)]
  cd.polygon(crown,fill=(72,89,56,125))
  cd.line(crown[:7],fill=(53,69,45,175),width=max(1,int(scale*.32)))
  cd.line([(x-ww*.65,yy+s*.28),(x,yy+s*.14),(x+ww*.73,yy+s*.28)],fill=(57,67,46,135),width=max(1,int(scale*.22)))
image=Image.alpha_composite(image,canopy)
stem='region' if regional else 'terrain'
image.convert('RGB').save(OUT/(stem+'.png'))
image.resize((800,1200),Image.Resampling.LANCZOS).convert('RGB').save(OUT/(stem+'_preview.png'))
(OUT/(stem+'_generation.json')).write_text(json.dumps({'seed':233,'size':[W,H],'heightSamples':metadata['width']*metadata['height'],'heightCellMetres':cell,'contours':{'minorMetres':10,'indexMetres':50,'interpolation':'float bicubic, 1m regional smoothing, gradient-antialiased widths','levels':contour_count},'authoredTrees':len(trees),'drawnTreeClusters':len(occupied),'roads':'Runtime discovery-gated pale corridors from Map.Lines; no red navigation overlay','source':'final scene heights.f32 / layout.json / placements.json'},indent=2))
print('Cartography',W,H,'clusters',len(occupied))
