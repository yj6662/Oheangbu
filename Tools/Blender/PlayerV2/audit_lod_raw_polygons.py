"""Read exported FBX arrays before Blender's mesh import validation."""
import json,hashlib
import numpy as np
from pathlib import Path
from io_scene_fbx import parse_fbx
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/LOD458ActualFBX/RefinedCapture'
report={'method':'Raw FBX Geometry polygon indices before Blender import; n-2 triangle equivalent, repeated-index and near-zero-area polygons measured separately.','models':[]}
for level in (1,2):
    path=ART/'Staging/Character'/('SM_DosaV2_LOD'+str(level)+'.fbx')
    tree,version=parse_fbx.parse(str(path));objects=next(e for e in tree.elems if e.id==b'Objects')
    row={'level':level,'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'meshes':[]}
    for geom in objects.elems:
        if geom.id!=b'Geometry' or geom.props[-1]!=b'Mesh':continue
        xyz=np.array(next(e for e in geom.elems if e.id==b'Vertices').props[0]).reshape(-1,3)
        indices=next(e for e in geom.elems if e.id==b'PolygonVertexIndex').props[0]
        polygons=[];current=[]
        for index in indices:
            current.append(index if index>=0 else -index-1)
            if index<0:polygons.append(current);current=[]
        repeated=[];zero=[]
        for i,p in enumerate(polygons):
            if len(set(p))<len(p):repeated.append(i)
            area=sum(float(np.linalg.norm(np.cross(xyz[p[j]]-xyz[p[0]],xyz[p[j+1]]-xyz[p[0]])))*.5 for j in range(1,len(p)-1))
            if area<1e-16:zero.append(i)
        row['meshes'].append({'name':geom.props[1].decode(errors='replace').split('\x00')[0],
            'rawPolygons':len(polygons),'rawTriangleEquivalent':sum(max(0,len(p)-2) for p in polygons),
            'repeatedIndexPolygons':len(repeated),'nearZeroAreaPolygons':len(zero)})
    row['rawTriangleEquivalent']=sum(m['rawTriangleEquivalent'] for m in row['meshes']);row['nearZeroAreaPolygons']=sum(m['nearZeroAreaPolygons'] for m in row['meshes'])
    report['models'].append(row)
(OUT/'raw-fbx-polygons.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
