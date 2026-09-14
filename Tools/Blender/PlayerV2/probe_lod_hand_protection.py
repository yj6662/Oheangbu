import bpy,json,sys
import bmesh,collections
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(ART/'DosaV2_Assembled.blend'));h=bpy.data.objects['DosaV2_Hands'];k=h.data.shape_keys
basis=np.array([v.co for v in h.data.vertices]);delta=np.maximum(*[np.linalg.norm(np.array([v.co for v in b.data])-basis,axis=1) for b in list(k.key_blocks)[1:]])
print('SHAPE_COUNTS',[(t,int(np.sum(delta>t))) for t in [.0001,.0005,.001,.002,.004,.006]],flush=True)
mod=h.modifiers.new('Probe','DECIMATE')
for name in ['vertex_group','vertex_group_factor','invert_vertex_group']:
 p=mod.bl_rna.properties[name];print(name,p.description,getattr(p,'hard_min',None),getattr(p,'hard_max',None),flush=True)
for name in ['DosaPackV2_Backpanel','DosaPackV2_BrushBundle','DosaV2_Hair']:
 o=bpy.data.objects[name]
 for tolerance in [0,.000001,.0001,.0005]:
  bm=bmesh.new();bm.from_mesh(o.data)
  if tolerance:bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=tolerance)
  bm.verts.index_update();duplicates=collections.Counter(tuple(sorted(v.index for v in f.verts)) for f in bm.faces)
  print('LOW_PART',name,tolerance,{'vertices':len(bm.verts),'faces':len(bm.faces),'badEdges':sum(len(e.link_faces)>2 for e in bm.edges),'boundary':sum(len(e.link_faces)==1 for e in bm.edges),'dupeFaces':sum(x-1 for x in duplicates.values())},flush=True);bm.free()
