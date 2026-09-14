"""Check the exact two-mesh derivative against its frozen parent before merge."""
import bpy,json,hashlib,bmesh
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/ArmLiningTransition'
SOURCE=ART/'DosaV2_ArmLiningClearance.blend';DEST=ART/'DosaV2_ArmLiningTransition_2.blend';TARGETS=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def mesh_hash(obj):
 m=obj.data;data={'positions':[list(v.co) for v in m.vertices],'faces':[list(p.vertices) for p in m.polygons],
 'weights':[[(obj.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in m.vertices],
 'uv':[[list(v.uv) for v in l.data] for l in m.uv_layers],'colors':{a.name:[list(v.color) for v in a.data] for a in m.color_attributes},
 'materials':[m.name for m in m.materials],'materialIndices':[p.material_index for p in m.polygons]}
 if m.shape_keys:data['shapes']={k.name:[list(v.co) for v in k.data] for k in m.shape_keys.key_blocks}
 return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()
states=[]
for path in [SOURCE,DEST]:
 bpy.ops.wm.open_mainfile(filepath=str(path));rig=bpy.data.objects['DosaV2_Rig'];meshes={o.name:mesh_hash(o) for o in bpy.context.scene.objects if o.type=='MESH'}
 bones={b.name:{'matrix':[list(r) for r in b.matrix_local],'parent':b.parent.name if b.parent else None} for b in rig.data.bones}
 stats={}
 for name in TARGETS:
  o=bpy.data.objects[name];m=o.data;m.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(m)
  stats[name]={'vertices':len(m.vertices),'triangles':len(m.loop_triangles),'boundaryEdges':sum(e.is_boundary for e in bm.edges),'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),
   'zeroAreaFaces':sum(t.area<1e-12 for t in m.loop_triangles),'minWeightSum':min(sum(g.weight for g in v.groups) for v in m.vertices),'maxWeightSum':max(sum(g.weight for g in v.groups) for v in m.vertices),'maxInfluences':max(len(v.groups) for v in m.vertices),'materials':[a.name for a in m.materials]}
  bm.free()
 states.append({'path':str(path),'sha256':sha(path),'meshes':meshes,'bonesHash':hashlib.sha256(json.dumps(bones,sort_keys=True).encode()).hexdigest(),'targetStats':stats,'actions':len(bpy.data.actions)})
changes=[n for n,h in states[0]['meshes'].items() if states[1]['meshes'].get(n)!=h]
assert set(changes)==set(TARGETS);assert states[0]['bonesHash']==states[1]['bonesHash'];assert states[1]['actions']==0
report={'status':'TWO_MESH_DERIVATIVE_VALIDATED_FOR_PARENT_MERGE','source':states[0]['path'],'sourceSha256':states[0]['sha256'],'derivative':states[1]['path'],'derivativeSha256':states[1]['sha256'],
 'changedMeshes':changes,'protectedMeshesUnchanged':True,'protectedMeshCount':len(states[0]['meshes'])-2,'restBonesAndParentsUnchanged':True,'productionActions':0,
 'before':states[0]['targetStats'],'after':states[1]['targetStats'],'triangleDelta':sum(s['triangles'] for s in states[1]['targetStats'].values())-sum(s['triangles'] for s in states[0]['targetStats'].values()),
 'method':'Previously corrected rest shape is retained; only two added skin sampling rings between upper arm x=.32 and elbow x=.442 plus local sleeve-compatible weight reassignment. No additional radial reduction.',
 'rigGate':'NOT_GRANTED','coverage':'Five measured static poses only; not a whole-animation or native Cloth pass.'}
(OUT/'handoff-integrity.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
