"""Independent exact-triangle countercheck of northern Hyeongang bridge end support."""
from pathlib import Path
from datetime import datetime, timezone
import sys, json, hashlib, math
import numpy as np

WORK=Path(__file__).resolve().parents[2]/'Art/World/Compact/Rebuild/Watershed295'


def main():
 cap=float(sys.argv[1]) if len(sys.argv)>1 else 6.
 h=np.fromfile(WORK/'Generated/height.bytes','<f4').reshape(1501,1001).astype(float)
 source=WORK/'Analysis/mum-hyeongang-oblique.json';data=json.loads(source.read_text(encoding='utf8'))
 def terrain(q):
  x=q[...,0]/4;z=q[...,1]/4;ix=x.astype(int);iz=z.astype(int);u=x-ix;v=z-iz
  a=h[iz,ix];b=h[iz,ix+1];c=h[iz+1,ix];e=h[iz+1,ix+1]
  return np.where(u+v<=1,a+(b-a)*u+(c-a)*v,e+(c-e)*(1-u)+(b-e)*(1-v))
 def normal(q):
  x=q[...,0]/4;z=q[...,1]/4;ix=x.astype(int);iz=z.astype(int);u=x-ix;v=z-iz
  gx=np.where(u+v<=1,h[iz,ix+1]-h[iz,ix],h[iz+1,ix+1]-h[iz+1,ix])/4
  gz=np.where(u+v<=1,h[iz+1,ix]-h[iz,ix],h[iz+1,ix+1]-h[iz,ix+1])/4
  return 1/np.sqrt(1+gx*gx+gz*gz)
 results=[]
 for site in data['proposedEmbedCandidates']:
  a=np.array([site['start']['x'],site['start']['z']]);b=np.array([site['end']['x'],site['end']['z']]);length=np.linalg.norm(b-a)
  forward=(b-a)/length;right=np.array([-forward[1],forward[0]]);ay=float(terrain(a));by=float(terrain(b));embed=min(cap,length/4)
  distances=np.unique(np.r_[np.arange(0,length,.025),length,.6,length-.6,2.5,length-2.5,embed,length-embed])
  widths=np.linspace(-1.37,1.37,111);positions=a+distances[:,None,None]*forward+widths[None,:,None]*right
  deck=ay+.025+(by-ay)*distances/length;relative=terrain(positions)-deck[:,None];top=relative.max(1)
  interior=(distances>=.6)&(distances<=length-.6);landing=~interior;old=(distances>=2.5)&(distances<=length-2.5);central=(distances>=embed)&(distances<=length-embed)
  normal_min=1.;spread=0.
  for bank,sign,bank_y in [(a,-1,ay),(b,1,by)]:
   points=np.array([bank+right*across+forward*sign*back for across in [-1.3,0,1.3] for back in [0,.6]])
   normal_min=min(normal_min,float(normal(points).min()));spread=max(spread,float(abs(terrain(points)-bank_y).max()))
  rise=abs(by-ay);angle=math.degrees(math.atan2(rise,length))
  passed=bool(4<=length<=40 and rise<=2 and angle<=8 and top[interior].max()<=0 and top[landing].max()<=.18 and (-top[central]-.45).min()>=0 and normal_min>=math.cos(math.radians(20)) and spread<=.18)
  results.append(dict(id=site['id'],start=site['start'],end=site['end'],span=float(length),endRise=rise,deckSlopeDegrees=angle,sampleCount=int(relative.size),interiorMaxAboveDeck=float(top[interior].max()),landingMaxAboveDeck=float(top[landing].max()),old2_5mUndersideMargin=float((-top[old]-.45).min()),quarterCappedEmbed=float(embed),centralUndersideMargin=float((-top[central]-.45).min()),minimumBankNormal=normal_min,landingHeightSpread=spread,passed=passed))
 result=dict(checkedUtc=datetime.now(timezone.utc).isoformat(),sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),heightSha256=hashlib.sha256((WORK/'Generated/height.bytes').read_bytes()).hexdigest(),maximumBankEmbed=cap,method='Independent direct exact-triangle implementation; 0.025m along span,111 positions across2.74m, exact .6m/2.5m/embed boundaries. Recheck six bank samples. Only terrain, not actual scene props/Physics; no rule or terrain edits.',results=results,passed=sum(r['passed'] for r in results),failed=sum(not r['passed'] for r in results))
 path=WORK/f'Analysis/mum-hyeongang-embed{cap:g}-independent.json';path.write_text(json.dumps(result,indent=2),encoding='utf8')
 print(json.dumps({k:v for k,v in result.items() if k!='results'},indent=2));print(json.dumps(results[:3],indent=2))


if __name__=='__main__':main()
