"""Rejoin original coat seams and return disconnected prop shell fragments.

Derivative only. Source triangles and UV corners are preserved; duplicate coincident
coat vertices are welded. No body, hands, rest bones or animation is authored here.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/Seams'
PROP_ASSIGNMENTS = {
 ('Back_R',4):'WaistTubeBack_R', ('Front_R',3):'WaistPouch_R', ('Front_R',4):'WaistPouch_R',
 ('Front_L',5):'WaistTube_C', ('Front_L',6):'WaistVial_L', ('Front_L',7):'WaistScroll_L',
 ('Front_L',8):'WaistScroll_L', ('Front_L',9):'WaistScroll_L', ('Front_L',11):'WaistScroll_L',
 ('Front_L',14):'WaistScroll_L', ('Side_L',6):'WaistTubeSide_L',
 ('Back_L',5):'WaistTubeSide_L',
 ('Back_L',1):'TrousersSource', ('Back_L',3):'TrousersSource', ('Back_L',4):'TrousersSource'}

def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def select(obj):
 bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
def components(mesh):
 adjacency=[[] for _ in mesh.vertices]
 for e in mesh.edges:
  a,b=e.vertices;adjacency[a].append(b);adjacency[b].append(a)
 pending=set(range(len(mesh.vertices)));result=[]
 while pending:
  seed=min(pending);pending.remove(seed);members=[seed];stack=[seed]
  while stack:
   for other in adjacency[stack.pop()]:
    if other in pending:pending.remove(other);members.append(other);stack.append(other)
  result.append(members)
 return result
def keep_vertices(mesh, ids):
 bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in ids],context='VERTS')
 bm.to_mesh(mesh);bm.free();mesh.update()
def tri_count(obj):obj.data.calc_loop_triangles();return len(obj.data.loop_triangles)
def uv_triangles(obj):
 obj.data.calc_loop_triangles();uv=obj.data.uv_layers.active
 return Counter(tuple(sorted(tuple(round(float(x),7) for x in uv.data[li].uv) for li in tri.loops)) for tri in obj.data.loop_triangles)
def mesh_signature(obj):
 h=hashlib.sha256()
 for v in obj.data.vertices:
  h.update(np.asarray(v.co,dtype=np.float32).tobytes())
  for g in v.groups:h.update((obj.vertex_groups[g.group].name+format(g.weight,'.8f')).encode())
 for layer in obj.data.uv_layers:
  for uv in layer.data:h.update(np.asarray(uv.uv,dtype=np.float32).tobytes())
 return h.hexdigest()

def run(args):
 global OUT
 OUT=Path(args.out).resolve()
 OUT.mkdir(parents=True,exist_ok=True);source=Path(args.source).resolve();sh=digest(source)
 script_snapshot=OUT/'rebuild-source.py';script_snapshot.write_bytes(Path(__file__).read_bytes())
 bpy.ops.wm.open_mainfile(filepath=str(source));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
 for obj in scene.objects:
  if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:obj['AuthoringMode']=False;obj.update_tag()
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 bpy.context.view_layer.update()
 protected={o.name:mesh_signature(o) for o in scene.objects if o.type=='MESH' and o.name!='DosaV2_TrousersSource' and not o.name.startswith(('DosaV2_Robe_','DosaV2_Waist'))}
 robes=sorted([o for o in scene.objects if o.type=='MESH' and o.name.startswith('DosaV2_Robe_')],key=lambda o:o.name)
 if len(robes)!=6 or any(o.name=='DosaV2_Robe_Combined' for o in robes):raise RuntimeError('Rebuild requires the original six coat renderers.')
 bone_snapshot=[(b.name,b.parent.name if b.parent else None,tuple(b.head_local),tuple(b.tail_local),tuple(tuple(r) for r in b.matrix_local)) for b in rig.data.bones]
 original_triangles=sum(tri_count(o) for o in robes);original_vertices=sum(len(o.data.vertices) for o in robes)
 moves=[];semantic=[];original_uv=Counter();moved_uv=Counter()
 for obj in robes:original_uv.update(uv_triangles(obj))
 for label,obj in enumerate(robes):
  semantic.append({'id':label,'source':obj.name})
  attr=obj.data.attributes.new('CoatRegion','INT','FACE')
  for item in attr.data:item.value=label
  groups=components(obj.data); removed=set()
  for cid,members in enumerate(groups):
   part=PROP_ASSIGNMENTS.get((obj.name.removeprefix('DosaV2_Robe_'),cid))
   if not part:continue
   target=bpy.data.objects['DosaV2_'+part];fragment=obj.copy();fragment.data=obj.data.copy();scene.collection.objects.link(fragment)
   keep_vertices(fragment.data,set(members));removed.update(members)
   # These are disconnected source shell fragments sharing the prop's exact seam.
   bone=None if part=='TrousersSource' else 'J_'+part
   if bone:
    if bone not in rig.data.bones:raise RuntimeError('Missing prop joint '+bone)
    fragment.vertex_groups.clear();vg=fragment.vertex_groups.new(name=bone);vg.add(list(range(len(fragment.data.vertices))),1,'REPLACE')
   moved_triangles=tri_count(fragment);moved_uv.update(uv_triangles(fragment));select(target);fragment.select_set(True);bpy.ops.object.join()
   moves.append({'source':obj.name,'componentId':cid,'target':target.name,'bone':bone,'vertices':len(members),'triangles':moved_triangles})
  if removed:keep_vertices(obj.data,set(range(len(obj.data.vertices)))-removed)
 select(robes[0])
 for obj in robes:obj.select_set(True)
 bpy.ops.object.join();coat=bpy.context.object;coat.name='DosaV2_Robe_Combined'
 pre_weld=len(coat.data.vertices);bm=bmesh.new();bm.from_mesh(coat.data)
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
 bm.to_mesh(coat.data);bm.free();coat.data.update()
 mobility=coat.data.color_attributes['ClothMobility'];inventory=[]
 for cid,members in enumerate(components(coat.data)):
  points=np.asarray([tuple(coat.data.vertices[i].co) for i in members]);pins=[i for i in members if mobility.data[i].color[0]==0]
  inventory.append({'componentId':cid,'vertices':len(members),'pinnedVertices':len(pins),'members':sorted(members),'bounds':[points.min(0).tolist(),points.max(0).tolist()]})
 coat['semantic_regions_json']=json.dumps(semantic);coat['cloth_solver']='One connected garment with six face-domain semantic regions; sleeves remain separate.'
 output=OUT/'DosaV2_ClothSeamRepair.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output))
 report={'status':'SEAM_DERIVATIVE_PENDING_PHYSICS','recordedAtUtc':datetime.now(timezone.utc).isoformat(),'source':str(source),'source_sha256':sh,'source_unchanged':sh==digest(source),
  'output':str(output),'output_sha256':digest(output),'renderer':coat.name,'mobilityAttribute':'ClothMobility','semanticAttribute':'CoatRegion','semanticRegions':semantic,
  'originalCoatTriangles':original_triangles,'originalCoatVertices':original_vertices,'combinedCoatTriangles':tri_count(coat),'combinedCoatVertices':len(coat.data.vertices),
  'weldedDuplicateVertices':pre_weld-len(coat.data.vertices),'weldToleranceMeters':.00001,'propShellMoves':moves,'components':inventory,
  'originalUvTriangleMultisetPreserved':original_uv==uv_triangles(coat)+moved_uv,
  'originalTriangleCountPreserved':original_triangles==tri_count(coat)+sum(m['triangles'] for m in moves),
  'rebuildScriptSnapshot':str(script_snapshot),'rebuildScriptSha256':digest(script_snapshot),
  'boneRestUnchanged':bone_snapshot==[(b.name,b.parent.name if b.parent else None,tuple(b.head_local),tuple(b.tail_local),tuple(tuple(r) for r in b.matrix_local)) for b in rig.data.bones],
  'protectedMeshesUnchanged':{n:mesh_signature(bpy.data.objects[n])==h for n,h in protected.items()},'bones':len(rig.data.bones),'actions':len(bpy.data.actions)}
 (OUT/'seam-repair-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in report.items() if k not in ['protectedMeshesUnchanged','components']},indent=2))
 print('UNPINNED',json.dumps([r for r in inventory if not r['pinnedVertices']]))

if __name__=='__main__':
 import sys
 parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--out',default=str(OUT))
 run(parser.parse_args(sys.argv[sys.argv.index('--')+1:]))
