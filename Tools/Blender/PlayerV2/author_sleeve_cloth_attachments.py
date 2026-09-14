"""Replace broad upper-height cloth pinning with inspected attachment distances.

Real shared cut/attachment boundaries and the torso-side seam remain fixed.
Only detached upper-arm cloth interior gains bounded, continuously faded motion.
No solver threshold or collision proxy is changed by this authoring operation.
"""
import bpy,bmesh,hashlib,heapq,importlib.util,json,math
import numpy as np
from pathlib import Path
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/UnderarmSeam/DosaV2_UnderarmSeamWeights.blend'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/SleeveAttachments';OUT.mkdir(parents=True,exist_ok=True)
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def module(name,file):
 spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
physics=module('cloth_physics','diagnose_cloth_blender.py');helper=module('cloth_render','build_brush.py')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
physics.direct_pose(rig,'rest_settle');source_hash=sha(SOURCE);(OUT/'rebuild-source.py').write_bytes(Path(__file__).read_bytes())
def boundary(o):
 bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table();result={v.index for e in bm.edges if e.is_boundary for v in e.verts};bm.free();return result
def signature(o,include_colors=True):
 data={'co':[list(v.co) for v in o.data.vertices],'polygons':[list(p.vertices) for p in o.data.polygons],
       'uvs':[[list(x.uv) for x in layer.data] for layer in o.data.uv_layers],
       'weights':[[(o.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in o.data.vertices]}
 if include_colors:data['colors']={a.name:[list(c.color) for c in a.data] for a in o.data.color_attributes}
 return hashlib.sha256(json.dumps(data,sort_keys=True).encode()).hexdigest()
outer=[bpy.data.objects['DosaV2_SleeveOuter_'+s] for s in ['L','R']]
protected={o.name:signature(o) for o in scene.objects if o.type=='MESH' and o not in outer}
outer_before={o.name:signature(o,False) for o in outer}
targets=[o for o in scene.objects if o.type=='MESH' and (o.name=='DosaV2_BodyCore' or o.name.startswith('DosaV2_SleeveInner_'))]
other_boundary=[(o.name,i,o.data.vertices[i].co.copy()) for o in targets for i in boundary(o)]
kd=KDTree(len(other_boundary))
for i,(_,_,p) in enumerate(other_boundary):kd.insert(p,i)
kd.balance()
def smooth(lo,hi,x):
 t=max(0,min(1,(x-lo)/(hi-lo)));return t*t*(3-2*t)
reports=[]
for o in outer:
 ids=boundary(o);seeds=[];neighbors=[[] for v in o.data.vertices]
 for e in o.data.edges:
  a,b=e.vertices;length=(o.data.vertices[a].co-o.data.vertices[b].co).length;neighbors[a].append((b,length));neighbors[b].append((a,length))
 for i in sorted(ids):
  p=o.data.vertices[i].co;loc,index,distance=kd.find(p)
  if distance<=.006:
   seeds.append({'sourceVertex':i,'sourceRestPoint':list(p),'otherMesh':other_boundary[index][0],'otherVertex':other_boundary[index][1],'distanceMeters':distance})
 assert seeds,'No actual attachment seeds'
 distances=[float('inf')]*len(o.data.vertices);queue=[]
 for seed in seeds:distances[seed['sourceVertex']]=0;heapq.heappush(queue,(0,seed['sourceVertex']))
 while queue:
  distance,index=heapq.heappop(queue)
  if distance!=distances[index]:continue
  for j,length in neighbors[index]:
   candidate=distance+length
   if candidate<distances[j]:distances[j]=candidate;heapq.heappush(queue,(candidate,j))
 attribute=o.data.color_attributes['ClothMobility'];before=[c.color[0] for c in attribute.data];changes=[]
 for v,d in zip(o.data.vertices,distances):
  p=v.co
  if not math.isfinite(d):continue
  # The preserved torso-side attachment occupies x<=.22m. A 6mm geodesic
  # attachment margin remains fixed. Full freedom is only the upper cloth
  # interior, away from both shoulder and inner-sleeve boundary lines.
  allowance=.08*smooth(.22,.27,abs(p.x))*smooth(1.28,1.315,p.z)*smooth(.006,.045,d)
  if allowance>before[v.index]+1e-7:
   attribute.data[v.index].color=(allowance,0,0,1)
   loc,index,distance=kd.find(p)
   changes.append({'sourceVertex':v.index,'sourceRestPoint':list(p),'previousMobilityMetres':before[v.index],'mobilityMetres':allowance,
                   'geodesicAttachmentDistanceMeters':d,'nearestOtherBoundaryDistanceMeters':distance,
                   'nearestOtherMesh':other_boundary[index][0],'reason':'Upper-arm cloth interior previously constrained by global height plane; not a shared attachment seam.'})
 assert all(attribute.data[s['sourceVertex']].color[0]==before[s['sourceVertex']] for s in seeds)
 assert all(attribute.data[v.index].color[0]==before[v.index] for v in o.data.vertices if abs(v.co.x)<=.22)
 fixed=[c.color[0]==0 for c in attribute.data];components=physics.component_inventory(o.data,np.asarray(fixed,dtype=bool))
 assert all(c['pinnedVertices']>0 for c in components),'Detached cloth component lost all attachment'
 reports.append({'mesh':o.name,'actualAttachmentSeeds':seeds,'previousExactPins':sum(v==0 for v in before),'exactPins':sum(fixed),
                 'changedVertices':changes,'allTrueAttachmentSeedsPreserved':True,'allTorsoSidePinsPreserved':True,'components':components})
assert protected=={o.name:signature(o) for o in scene.objects if o.type=='MESH' and o not in outer}
assert outer_before=={o.name:signature(o,False) for o in outer}
assert not bpy.data.actions
target=OUT/'DosaV2_SleeveAttachments.blend';bpy.ops.wm.save_as_mainfile(filepath=str(target))
report={'status':'SEMANTIC_CLOTH_ATTACHMENT_CANDIDATE_PENDING_NATIVE_SOLVERS','sourceSha256':source_hash,'sourceUnchanged':sha(SOURCE)==source_hash,
        'output':str(target),'outputSha256':sha(target),'rebuildScriptSha256':sha(OUT/'rebuild-source.py'),
        'basis':'Original rule pinned the entire upper half by z>=1.315, independently of attachment topology. This replaces that condition only outside the torso-side seam and away from actual BodyCore/inner-sleeve shared boundary vertices. Both pin geometry and external collision definitions are unchanged.',
        'maximumNewUpperInteriorTravelMetres':.08,'actualSeamMatchToleranceMetres':.006,'geodesicFixedMarginMetres':.006,'geodesicFullAllowanceMetres':.045,
        'allRestPositionsTopologyUVWeightsAndBonesUnchanged':True,'allOtherObjectsUnchanged':True,'actions':0,'surfaces':reports}
(OUT/'sleeve-attachment-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='surfaces'},indent=2),flush=True)
print([(r['mesh'],r['previousExactPins'],r['exactPins'],len(r['changedVertices']),len(r['actualAttachmentSeeds'])) for r in reports],flush=True)
# Colours are disposable visualization only, after the unchanged-material model
# has been saved. Red=preserved attachment, cyan=re-authored upper interior.
for o,row in zip(outer,reports):
 changed={v['sourceVertex'] for v in row['changedVertices']}
 for label,color in [('attachment',(1,.03,.08,1)),('uppercloth',(.01,.65,.8,1))]:
  mat=bpy.data.materials.new('Diagnostic_'+label);mat.diffuse_color=color;mat.use_nodes=True
  mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color;o.data.materials.append(mat)
 for face in o.data.polygons:
  if all(o.data.color_attributes['ClothMobility'].data[i].color[0]==0 for i in face.vertices):face.material_index=len(o.data.materials)-2
  elif any(i in changed for i in face.vertices):face.material_index=len(o.data.materials)-1
camera=scene.camera or helper.lighting(scene)
for angle,name in [(math.pi*.15,'front'),(math.pi*.85,'back')]:
 helper.render(scene,camera,OUT/('attachment-map-'+name+'.png'),(0,.02,1.30),1.3,angle,width=1100,height=700)
