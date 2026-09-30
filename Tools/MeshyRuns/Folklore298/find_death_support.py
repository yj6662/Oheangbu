import json, math
from pathlib import Path
import numpy as np
p=Path(__file__).resolve().parents[3]/'Art/Characters/Folklore298'
v=np.array(json.loads((p/'Analysis/RigInspection/cheongryong/vertices-world.json').read_text())['Mesh_0']['vertices'])
# Read-only orientation search. The source vertices are never moved or saved.
bins=[(v[:,1]>=a)&(v[:,1]<a+.14)&(v[:,2]>-.145)&(abs(v[:,0])<.07) for a in [-.3,-.16,-.02,.12]]
rows=[]
for roll in range(100,181,2):
 for pitch in range(-40,41,2):
  a,b=map(math.radians,(roll,pitch))
  ry=np.array([[math.cos(a),0,-math.sin(a)],[0,1,0],[math.sin(a),0,math.cos(a)]])
  rx=np.array([[1,0,0],[0,math.cos(b),-math.sin(b)],[0,math.sin(b),math.cos(b)]])
  q=rx@ry
  z=v@q[2]
  gaps=[float(np.min(z[m])-z.min()) for m in bins]
  score=float(np.mean(gaps)+max(gaps)*.5)
  rows.append(dict(roll=roll,pitch=pitch,score=score,torsoBinGroundGaps=gaps,lowestVertex=int(np.argmin(z))))
rows.sort(key=lambda r:r['score'])
(p/'Analysis/death-rigid-support-search.json').write_text(json.dumps(rows[:20],indent=2))
print(json.dumps(rows[:3]))
