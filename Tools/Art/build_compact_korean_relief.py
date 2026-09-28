"""Derive game relief from a Korean DEM patch; preserve authored settlements and traversal.

Mapzen Terrain Tiles, global SRTM/GMTED2010 data courtesy of USGS.
This is an adapted game height field, not a survey or an unchanged geographic reproduction.
"""
from pathlib import Path
import gzip
import hashlib
import json
import sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Art/World/Compact/Rebuild/Terrain243'
dynamic = '--dynamic' in sys.argv
OUT = SOURCE.parent / ('Terrain244' if dynamic else 'Terrain243')
OUT.mkdir(exist_ok=True)
layout = json.loads((OUT.parent / 'layout.json').read_text(encoding='utf-8-sig'))
dem = np.frombuffer(gzip.decompress((SOURCE / 'N35E127.hgt.gz').read_bytes()), dtype='>i2').reshape(3601, 3601).astype(float)
meta = json.loads((SOURCE / 'before_heights.json').read_text())
old = np.fromfile(SOURCE / 'before_heights.f32', dtype='<f4').reshape(meta['height'], meta['width'])

def sample(a, y, x):
    x = np.clip(x, 0, a.shape[1]-1.00001); y = np.clip(y, 0, a.shape[0]-1.00001)
    i = np.floor(y).astype(int); j = np.floor(x).astype(int); u = x-j; v = y-i
    return a[i,j]*(1-u)*(1-v)+a[i,j+1]*u*(1-v)+a[i+1,j]*(1-u)*v+a[i+1,j+1]*u*v

def smooth(t):
    t = np.clip(t,0,1)
    return t*t*(3-2*t)

x, z = np.meshgrid(np.arange(801)*5., np.arange(1201)*5.)
latitude, longitude = 35.08, 127.34
source = sample(dem, (36-latitude-z/111200)*3600,
                (longitude+x/(111200*np.cos(np.radians(latitude)))-127)*3600)
if (source < -1000).any():
    raise ValueError('Void DEM sample')
# Modest vertical adaptation keeps the authored world's human scale. Plan dimensions remain 4x6 km.
vertical_scale = .86 if dynamic else .48
target = 35 + (source-source.min())*vertical_scale
# Smooth only the source sampling scale; no synthetic repeated peaks or screen noise.
k = np.exp(-np.arange(-6,7,dtype=float)**2/8); k /= k.sum()
p = np.pad(target,((0,0),(6,6)),mode='edge')
target = sum(p[:,i:i+target.shape[1]]*v for i,v in enumerate(k))
p = np.pad(target,((6,6),(0,0)),mode='edge')
target = sum(p[i:i+target.shape[0],:]*v for i,v in enumerate(k))
baseline = sample(old, z/meta['cellMetres']-.5, x/meta['cellMetres']-.5)
# Match the broad valley datum before protecting small authored areas. Without this correction,
# a low road cut into an unrelated high DEM patch would become a trench, and POIs circular pits.
anchors = [(p['XZ']['x'], p['XZ']['y']) for p in layout['Places']]
place_by_id = {p['Id']:p for p in layout['Places']}
lines = [layout['River']]
for route in layout['Routes']:
    if route['GradeForVehicle']:
        lines.append([place_by_id[route['From']]['XZ']]+route['Bends']+[place_by_id[route['To']]['XZ']])
for line in lines:
    for a,b in zip(line,line[1:]):
        for t in np.linspace(0,1,max(2,int(np.hypot(b['x']-a['x'],b['y']-a['y'])/220)+1)):
            anchors.append((a['x']+(b['x']-a['x'])*t,a['y']+(b['y']-a['y'])*t))
accum=np.zeros_like(target);total=np.zeros_like(target)
for ax,az in anchors:
    residual=float(sample(baseline,az/5,ax/5)-sample(target,az/5,ax/5))
    influence=np.exp(-((x-ax)**2+(z-az)**2)/(2*460**2))
    accum+=residual*influence;total+=influence
target+=accum/np.maximum(total,.00001)
target=32+8*np.logaddexp(0,(target-32)/8)
weight = np.ones_like(target)

def preserve(distance, core, transition):
    global weight
    weight = np.minimum(weight, smooth((distance-core)/transition))

def path_distance(points):
    result = np.full_like(x, 1e10)
    for a,b in zip(points,points[1:]):
        ax,az=a; bx,bz=b; dx=bx-ax; dz=bz-az
        t = np.clip(((x-ax)*dx+(z-az)*dz)/max(.001,dx*dx+dz*dz),0,1)
        result = np.minimum(result, np.hypot(x-ax-dx*t,z-az-dz*t))
    return result

places={p['Id']:p for p in layout['Places']}
for place in places.values():
    if place['Id'] not in ('mine','geumpyo_inn'): continue
    px,pz=place['XZ']['x'],place['XZ']['y']
    core=65 if place['Id']=='geumpyo_inn' else place['GroundRadius']+28
    preserve(np.hypot(x-px,z-pz),core,220)
for route in layout['Routes']:
    if not route['GradeForVehicle']: continue
    points=[places[route['From']]['XZ']]+route['Bends']+[places[route['To']]['XZ']]
    preserve(path_distance([(p['x'],p['y']) for p in points]),route['Width']*.5+18,280)
river=path_distance([(p['x'],p['y']) for p in layout['River']])
preserve(river,60,300)
walk=json.loads((OUT.parent/'art_route.json').read_text())['trail']
walk=walk[::3]+walk[-1:]
preserve(path_distance([(p['x'],p['z']) for p in walk]),45,210)
# Keep the entire authored mine envelope and mouth support intact, not just its centreline.
preserve(np.hypot(np.maximum(np.maximum(3350-x,x-3650),0),
                  np.maximum(np.maximum(1670-z,z-1960),0)),35,180)
delta=(target-baseline)*weight
delta.astype('<f4').tofile(OUT/'relief_delta.bytes')
final=baseline+delta
stats={'sourceOriginLatLon':[latitude,longitude],'sourceExtentMetres':[4000,6000],
       'width':801,'height':1201,'cellMetres':5,'verticalScale':vertical_scale,
       'sourceElevationRange':[float(source.min()),float(source.max())],
       'adaptedRange':[float(final.min()),float(final.max())],
       'maxRaise':float(delta.max()),'maxLower':float(delta.min()),
       'changedSamples':int((abs(delta)>.025).sum()),'protectedSamples':int((weight==0).sum()),
       'sha256':hashlib.sha256((OUT/'relief_delta.bytes').read_bytes()).hexdigest(),
       'note':'Source DEM approximately 30m. 5m interpolated game grid does not add surveyed detail. Protected geometry remains authored.'}
(OUT/'relief.json').write_text(json.dumps(stats,indent=2)+'\n')
canvas=Image.new('RGB',(1602,1241),'white');draw=ImageDraw.Draw(canvas)
previous = baseline + np.fromfile(SOURCE/'relief_delta.bytes',dtype='<f4').reshape(1201,801) if dynamic else baseline
for i,h in enumerate([previous,final]):
    gz,gx=np.gradient(h,5);light=np.clip((.7-.6*gx+.5*gz)/np.sqrt(1+gx*gx+gz*gz),.15,1)
    rgb=np.stack([light*180+35,light*172+40,light*140+38],axis=-1)
    rgb[np.mod(h,25)<.8]*=.78
    canvas.paste(Image.fromarray(rgb[::-1].astype('uint8')),(i*801,40))
    draw.text((i*801+10,10),(['Before: revision 243','After: stronger Korean mountain relief'] if dynamic else ['Before: authored rounded ridges','After: Korean DEM relief + protected corridors'])[i],fill='black')
canvas.save(OUT/'relief_plan.png')
print(json.dumps(stats))
