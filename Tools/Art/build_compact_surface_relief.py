"""Periodic physical-scale height/occlusion data, not a colour painting."""
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Surface239/Textures';OUT.mkdir(parents=True,exist_ok=True)
N=2048
Y,X=np.mgrid[:N,:N].astype(np.float32)
def random(x,y,s):
 return np.mod(np.sin(x*127.1+y*311.7+s*41.3)*43758.5453,1).astype(np.float32)
def field(cells,rock):
 x=X/N*cells;y=Y/N*cells;x=x+.28*np.sin(y/cells*2*np.pi*7)+.16*np.sin(y/cells*2*np.pi*13+x/cells*2*np.pi*3);y=y+.3*np.sin(x/cells*2*np.pi*9);gx=np.floor(x);gy=np.floor(y)
 best=np.full((N,N),1e6,np.float32);second=best.copy();seed=np.zeros_like(best);vx=seed.copy();vy=seed.copy()
 for j in [-1,0,1]:
  for i in [-1,0,1]:
   cx=gx+i;cy=gy+j;a=np.mod(cx,cells);b=np.mod(cy,cells)
   dx=x-(cx+.06+random(a,b,1)*.88);dy=y-(cy+.06+random(a,b,2)*.88)
   dist=dx*dx+dy*dy;closer=dist<best;second=np.minimum(second,np.where(closer,best,dist))
   seed=np.where(closer,random(a,b,3),seed);vx=np.where(closer,dx,vx);vy=np.where(closer,dy,vy);best=np.minimum(best,dist)
 if rock:
  gap=np.sqrt(second)-np.sqrt(best);joint=np.clip(gap/.045,0,1)
  broken=np.clip((np.sin(X/N*2*np.pi*7+np.sin(Y/N*2*np.pi*3)*2)+np.sin(Y/N*2*np.pi*9)+.3)*.7,0,1)
  joint=1-(1-joint)*broken*.65
  height=.48+.08*np.sin(X/N*2*np.pi*5+np.sin(Y/N*2*np.pi*4)*2)-.26*(1-joint)
  cavity=.65+.35*joint
  mask=joint
 else:
  angle=seed*19
  rx=vx*np.cos(angle)+vy*np.sin(angle);ry=-vx*np.sin(angle)+vy*np.cos(angle)
  radius=.19+seed*.26
  edge=(np.abs(rx*(.8+seed*.3))**3+np.abs(ry*(1.4-seed*.35))**3)**(1/3)/radius
  density=np.clip(.6+.4*np.sin(X/N*2*np.pi*3+np.sin(Y/N*2*np.pi*4)),.25,1)
  mask=np.clip((1-edge)*12,0,1)*(seed>1-density)
  height=mask*(.18+seed*.6)*np.clip(1-edge*edge,0,1)**.45
  cavity=1-.48*np.exp(-((edge-1.02)/.12)**2)*(seed>1-density)
 grain=(np.sin(X/N*2*np.pi*101+np.sin(Y/N*2*np.pi*28)*3)*np.sin(Y/N*2*np.pi*75-X/N*2*np.pi*19))*.012
 height=np.clip(height+grain*.3,0,1)
 return height,cavity,mask
for name,cells,depth in [('Gravel',38,.024),('Fracture',7,.045)]:
 h,ao,mask=field(cells,name=='Fracture')
 # Tile represents four metres; normals are derivatives of the same height.
 dx=(np.roll(h,-1,1)-np.roll(h,1,1))*depth/(8/N)
 dy=(np.roll(h,-1,0)-np.roll(h,1,0))*depth/(8/N)
 normals=np.stack([-dx,dy,np.ones_like(h)],axis=-1);normals/=np.linalg.norm(normals,axis=-1)[...,None]
 Image.fromarray(np.uint8(np.clip(np.stack([h,ao,mask],axis=-1)*255,0,255))).save(OUT/(name+'_Relief.png'))
 Image.fromarray(np.uint8((normals*.5+.5)*255)).save(OUT/(name+'_Normal.png'))
 print(name,N,'height metres',depth)
