"""Remove tiny interior cloth edges in a derivative; preserve skin/pins/outlines.

This is a topology repair, not a simulation shortcut: no free vertex is pinned and
no garment is hidden. Original UV face corners survive edge collapses (uvs=False).
"""
import argparse, hashlib, heapq, importlib.util, json, math
from datetime import datetime,timezone
from pathlib import Path
import bpy,bmesh
import numpy as np
from mathutils import Vector,kdtree

ROOT=Path(__file__).resolve().parents[3]
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def triangles(obj):obj.data.calc_loop_triangles();return len(obj.data.loop_triangles)
def run(args):
 source=Path(args.source).resolve();out=Path(args.out).resolve();out.mkdir(parents=True,exist_ok=True);sh=digest(source)
 snapshot=out/'rebuild-source.py';snapshot.write_bytes(Path(__file__).read_bytes())
 bpy.ops.wm.open_mainfile(filepath=str(source));scene=bpy.context.scene;rows=[]
 surfaces=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_Robe_','DosaV2_SleeveOuter_'))]
 for obj in surfaces:
  original=[v.co.copy() for v in obj.data.vertices];oldtris=triangles(obj)
  oldpins=[tuple(v.co) for v,c in zip(obj.data.vertices,obj.data.color_attributes['ClothMobility'].data) if c.color[0]==0]
  bm=bmesh.new();bm.from_mesh(obj.data);mob=bm.verts.layers.float_color.get('ClothMobility')
  if mob is None:raise RuntimeError('ClothMobility layer missing')
  queue=[];counter=0
  def enqueue(edge):
   nonlocal counter
   if edge.is_valid and not edge.is_boundary and len(edge.link_faces)==2 and all(v[mob][0]>0 for v in edge.verts):
    length=edge.calc_length()
    if length<args.minimum_edge:heapq.heappush(queue,(length,counter,edge));counter+=1
  for edge in bm.edges:enqueue(edge)
  collapsed=0
  while queue:
   previous,_,edge=heapq.heappop(queue)
   if not edge.is_valid or edge.is_boundary or len(edge.link_faces)!=2 or any(v[mob][0]<=0 for v in edge.verts):continue
   length=edge.calc_length()
   if length>=args.minimum_edge or abs(previous-length)>.0000001:continue
   # Do not allow a chain collapse to pull a protected silhouette vertex inward.
   if any(v.is_boundary for v in edge.verts):continue
   neighbors={e for v in edge.verts for e in v.link_edges}
   bmesh.ops.collapse(bm,edges=[edge],uvs=False);collapsed+=1
   for neighbor in neighbors:
    if neighbor.is_valid:
     enqueue(neighbor)
     for v in neighbor.verts:
      for other in v.link_edges:enqueue(other)
  bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free();obj.data.update()
  # BMesh interpolates deform groups on collapse. Normalize roundoff without
  # changing any surviving pinned vertex's individual weights.
  for vertex in obj.data.vertices:
   total=sum(g.weight for g in vertex.groups)
   if total<=0:raise RuntimeError('Unweighted cloth vertex')
   if abs(total-1)>1e-6:
    for group in list(vertex.groups):obj.vertex_groups[group.group].add([vertex.index],group.weight/total,'REPLACE')
  tree=kdtree.KDTree(len(original))
  for i,p in enumerate(original):tree.insert(p,i)
  tree.balance();nearest=max(tree.find(v.co)[2] for v in obj.data.vertices)
  newpins=[tuple(v.co) for v,c in zip(obj.data.vertices,obj.data.color_attributes['ClothMobility'].data) if c.color[0]==0]
  rows.append({'name':obj.name,'oldVertices':len(original),'vertices':len(obj.data.vertices),'oldTriangles':oldtris,'triangles':triangles(obj),
   'collapsedInteriorEdges':collapsed,'maxNewVertexNearestSourceVertexMeters':nearest,'pinnedPositionsExact':sorted(oldpins)==sorted(newpins),
   'unweightedVertices':sum(not v.groups for v in obj.data.vertices),'maxWeightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in obj.data.vertices)})
 output=out/'DosaV2_ClothTopologyRefined.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output))
 report={'status':'TOPOLOGY_DERIVATIVE_PENDING_SOLVER_AND_VISUAL_REVIEW','recordedAtUtc':datetime.now(timezone.utc).isoformat(),'source':str(source),'source_sha256':sh,
  'sourceUnchanged':sh==digest(source),'output':str(output),'output_sha256':digest(output),'rebuildScriptSha256':digest(snapshot),'minimumInteriorEdgeMeters':args.minimum_edge,
  'policy':'Only free interior manifold edges collapse; all pinned positions, all boundary vertices and their silhouettes remain. No geometry hidden; no new pins; no hand/body/prop/rig changes.',
  'surfaces':rows,'actions':len(bpy.data.actions),'bones':len(bpy.data.objects['DosaV2_Rig'].data.bones)}
 (out/'cloth-topology-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))

if __name__=='__main__':
 import sys
 parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--out',required=True);parser.add_argument('--minimum-edge',type=float,default=.005)
 run(parser.parse_args(sys.argv[sys.argv.index('--')+1:]))
