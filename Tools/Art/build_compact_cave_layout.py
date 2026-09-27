"""Author connected mine air volume from the v4 route ledger; no Unity asset writes."""
from pathlib import Path
import json
import numpy as np
import build_natural_cave as mesh
OUT=Path(__file__).resolve().parents[2]/"Art/World/Compact/Rebuild/CaveV4"
ORIGIN=np.array([3495.,135.76,1810.])
MAIN=[(3551,1778),(3540,1778),(3540,1800),(3518,1800),(3518,1830),(3485,1830),(3485,1810),(3464,1810),(3464,1833),(3452,1841),(3435,1857)]
BRANCH=[(3518,1800),(3518,1775),(3495,1775),(3480,1790),(3485,1810)]
RECESS=[(3518,1830),(3533,1840),(3541,1837)]
MOUTH=[MAIN[-1],(3410,1880)]
ROUTES=[("mouth_clearance",MOUTH,8),("haul_gallery",MAIN,4.8),("evidence_loop",BRANCH,3.8),("tool_bay",RECESS,4.2)]
def field(x,y,z):
 result=np.full(np.broadcast_shapes(np.shape(x),np.shape(y),np.shape(z)),1e5,dtype=np.float32)
 for _,path,radius in ROUTES:
  for a,b in zip(path,path[1:]):
   a=np.array(a)-ORIGIN[[0,2]];b=np.array(b)-ORIGIN[[0,2]];d=b-a
   t=np.clip(((x-a[0])*d[0]+(z-a[1])*d[1])/np.dot(d,d),0,1)
   dx=x-a[0]-t*d[0];dz=z-a[1]-t*d[1]
   v=np.sqrt(dx*dx+dz*dz+((y-2.0)*.91)**2)-radius
   result=mesh.smooth_min(result,v,.7)
 # One working chamber; most of the journey remains actual narrow, turning galleries.
 for wx,wz,r in [(3518,1800,6.3),(3549,1778,5.8),(3540,1837,5),(3495,1775,5.2)]:
  v=np.sqrt((x-wx+ORIGIN[0])**2+(z-wz+ORIGIN[2])**2+((y-2.4)*.85)**2)-r
  result=mesh.smooth_min(result,v,.8)
 rough=.36*np.sin(x*.27+z*.31)*np.cos(y*.81-z*.17)+.15*np.sin(y*2.1+x*.7)+.10*np.sin(x*1.3-z*.8)
 return result+rough

def vec(x,z):return {'x':float(x),'y':float(ORIGIN[1]+.12),'z':float(z)}
def main(polish_mouth=False):
 global OUT
 if polish_mouth:
  OUT=OUT.parent/'CavePolish237';MOUTH[-1]=(3390,1900)
 OUT.mkdir(parents=True,exist_ok=True);mesh.field=field;mesh.FLOOR=0.
 xs=np.arange((3395 if polish_mouth else 3423)-ORIGIN[0],3561-ORIGIN[0],.7,dtype=np.float32)
 zs=np.arange(1764-ORIGIN[2],(1900.1 if polish_mouth else 1854.1)-ORIGIN[2],.7,dtype=np.float32)
 ys=np.arange(-5 if polish_mouth else 0,12,.7,dtype=np.float32)
 wall,tri=mesh.extract_tetra(xs,ys,zs);floor,ft=mesh.floor_mesh(xs,np.arange(1764-ORIGIN[2],1854.1-ORIGIN[2],.7,dtype=np.float32))
 doc={'origin':dict(zip(('x','y','z'),ORIGIN)),'meshes':[mesh.mesh_record('Natural_Cave_Interior',wall,tri),mesh.mesh_record('Natural_Cave_Floor',floor,ft)],
 'mouth':[vec(x,z) for x,z in MOUTH],'main':[vec(x,z) for x,z in MAIN],'branch':[vec(x,z) for x,z in BRANCH],'recess':[vec(x,z) for x,z in RECESS],
 'start':vec(*MAIN[0]),'evidence':vec(3518,1802),'satchel':vec(3495,1775),
 'lights':[vec(x,z) for x,z in [(3547,1778),(3540,1796),(3520,1800),(3518,1778),(3495,1775),(3482,1790),(3521,1831),(3486,1828),(3466,1810),(3463,1832)]],
 'note':'Two independent paths join at work chamber and lower gallery; dead-end tool bay carries physical supplies. Open mouth continues into preserved exterior approach.'}
 (OUT/'geometry.json').write_text(json.dumps(doc,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
 # Reviewable plan from the exact same ledger used to create geometry and runtime placement.
 def pts(path):return ' '.join(f'{(3563-x)*6:.1f},{(z-1760)*6:.1f}' for x,z in path)
 svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 760"><rect width="960" height="760" fill="#ece8dc"/>'
 for name,path,r in ROUTES:svg+=f'<polyline points="{pts(path)}" fill="none" stroke="#45463f" stroke-width="{r*10}" stroke-linejoin="round" stroke-linecap="round"/>'
 for label,x,z in [('START',3551,1778),('EVIDENCE',3518,1802),('SATCHEL',3495,1775),('EXIT',3435,1857)]:
  px=(3563-x)*6;pz=(z-1760)*6;svg+=f'<circle cx="{px}" cy="{pz}" r="5" fill="#a44435"/><text x="{px+8}" y="{pz-8}" font-size="13" fill="#a44435">{label}</text>'
 (OUT/'layout.svg').write_text(svg+'</svg>',encoding='utf-8')
 print('V4 air volume and single route ledger written')
if __name__=='__main__':
 import sys
 main('--polish-mouth' in sys.argv)
