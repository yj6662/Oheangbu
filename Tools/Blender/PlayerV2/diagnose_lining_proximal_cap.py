"""Read-only source audit; virtual coherent cap-weight variants are not saved as assets."""
import bpy,ast,json,hashlib,math,collections
import numpy as np
from pathlib import Path
from mathutils import Matrix,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/ArmLiningTransition/ProximalCap';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend';defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text())
module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
def pose(rig,pid):
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for e in next(p for p in defs['poses'] if p['id']==pid)['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def measure(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();pts=np.array([e.matrix_world@v.co for v in m.vertices]);tri=np.array([t.vertices for t in m.loop_triangles]);tree=BVHTree.FromPolygons(pts.tolist(),tri.tolist(),all_triangles=True);pairs=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])];arr=np.array(pairs,dtype=int);hits=arr[proper_crossings(pts[tri[arr[:,0]]],pts[tri[arr[:,1]]])] if len(arr) else arr
 details=[{'triangles':[int(a),int(b)],'vertices':[tri[a].tolist(),tri[b].tolist()],'restCenter':np.mean([o.data.vertices[int(i)].co for i in np.r_[tri[a],tri[b]]],axis=0).tolist()} for a,b in hits];e.to_mesh_clear();return {'properCrossingPairs':len(hits),'pairs':details}
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'variants':[],'assetMutation':False}
for variant in ['original','common_mean_cap','common_mean_first_two_rings','common_cross_sections','shoulder_cap','arm_cap','upper_arm_proximal']:
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];record={'variant':variant,'rings':{},'results':[]}
 for side in ['Left','Right']:
  o=bpy.data.objects['DosaV2_ArmLining_'+side];ring=[v for v in o.data.vertices if abs(v.co.x)<.15];weights=collections.defaultdict(float)
  for v in ring:
   for g in v.groups:weights[o.vertex_groups[g.group].name]+=g.weight/len(ring)
  record['rings'][side]={'firstRingAbsXRange':[min(abs(v.co.x) for v in ring),max(abs(v.co.x) for v in ring)],'vertices':len(ring),'meanWeights':dict(weights),'perVertexWeights':[{'index':v.index,'position':list(v.co),'weights':{o.vertex_groups[g.group].name:g.weight for g in v.groups}} for v in ring]}
  if variant=='original':continue
  if variant=='shoulder_cap':weights={side+'Shoulder':1.}
  elif variant in ['arm_cap','upper_arm_proximal']:weights={side+'Arm':1.}
  else:
   weights=dict(sorted(weights.items(),key=lambda p:-p[1])[:4]);total=sum(weights.values());weights={n:w/total for n,w in weights.items()}
  if variant=='common_cross_sections':
   for low,high in [(0,.15),(.15,.24),(.29,.33)]:
    section=[v for v in o.data.vertices if low<=abs(v.co.x)<high];section_weights=collections.defaultdict(float)
    for v in section:
     for g in v.groups:section_weights[o.vertex_groups[g.group].name]+=g.weight/len(section)
    section_weights=dict(sorted(section_weights.items(),key=lambda p:-p[1])[:4]);total=sum(section_weights.values());section_weights={n:w/total for n,w in section_weights.items()}
    for v in section:
     for g in o.vertex_groups:g.remove([v.index])
     for n,w in section_weights.items():o.vertex_groups[n].add([v.index],w,'REPLACE')
   continue
  selected=[v for v in o.data.vertices if abs(v.co.x)<(.335 if variant=='upper_arm_proximal' else .24 if variant=='common_mean_first_two_rings' else .15)]
  for v in selected:
   for g in o.vertex_groups:g.remove([v.index])
   for n,w in weights.items():o.vertex_groups[n].add([v.index],w,'REPLACE')
 for pid in ['rest','open_hand','grip_down','combined_reach','shoulder_120','elbow_120','forearm_minus90','forearm_plus90']:
  pose(rig,pid);record['results'].append({'poseId':pid,'objects':{side:measure(bpy.data.objects['DosaV2_ArmLining_'+side]) for side in ['Left','Right']}})
 report['variants'].append(record)
(OUT/'read-only-cap-weights.json').write_text(json.dumps(report,indent=2));print(json.dumps([{'variant':r['variant'],'results':[{'poseId':p['poseId'],'counts':{s:v['properCrossingPairs'] for s,v in p['objects'].items()}} for p in r['results']]} for r in report['variants']],indent=2))
