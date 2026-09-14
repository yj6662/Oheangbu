"""Locate angular LOD surface deviation without writing model sources."""
import bpy,json,ast,numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete/NearOverrides'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_NearArmLining18.blend'))
g=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/geometry-rest.json').read_text())
rows=[]
for side in ['Left','Right']:
    n='DosaV2_ArmLining_'+side;o=bpy.data.objects[n];o.data.calc_loop_triangles();near=np.array([v.co[:] for v in o.data.vertices]);nt=np.array([list(t.vertices) for t in o.data.loop_triangles]);old=np.array(g['models'][n]['rest']);t=np.array(g['models'][n]['triangles'])
    tree=BVHTree.FromPolygons(near.tolist(),nt.tolist(),all_triangles=True)
    probes=[{'kind':'vertex','sourceIds':[i],'point':p.tolist()} for i,p in enumerate(old)]
    for tri in t:
        probes.append({'kind':'centroid','sourceIds':tri.tolist(),'point':old[tri].mean(axis=0).tolist()})
        for a,b in zip(tri,np.roll(tri,-1)):probes.append({'kind':'edge','sourceIds':[int(a),int(b)],'point':((old[a]+old[b])*.5).tolist()})
    for probe in probes:probe['distanceMeters']=tree.find_nearest(Vector(probe['point']))[3]
    rows.append({'mesh':n,'worst':sorted(probes,key=lambda r:r['distanceMeters'],reverse=True)[:25]})
(OUT/'surface-error-location.json').write_text(json.dumps(rows,indent=2),encoding='utf-8');print(json.dumps(rows,indent=2),flush=True)
