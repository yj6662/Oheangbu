"""Localize actual crossing candidates, compare lining derivative, render marked evidence."""
import bpy,json,hashlib,math,importlib.util,ast
import numpy as np
from pathlib import Path
from collections import Counter
from mathutils import Matrix,Euler,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/TriangleCrossings/RegionReview';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend';DERIVATIVE=ART/'DosaV2_ArmLiningTransition_2.blend'
source_report=json.loads((ART/'Inspect/TriangleCrossings/report.json').read_text());defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text())
module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
report={'status':'CROSSING_REGIONS_MEASURED_REVIEW_PENDING','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'sourceCrossingReportSha256':hashlib.sha256((ART/'Inspect/TriangleCrossings/report.json').read_bytes()).hexdigest(),'classifications':[],'renders':[],'derivativeLiningComparison':[],'productionActions':0}
def apply_pose(rig,pose_id):
 d=next(d for d in defs['poses'] if d['id']==pose_id)
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 keys=bpy.data.objects['DosaV2_Hands'].data.shape_keys
 for side in ['Right','Left']:keys.key_blocks['GripPalmRelax_'+side].value=d.get('handGripCorrectives',{}).get(side.lower(),0.)
 for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def data(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();pts=np.array([list(e.matrix_world@v.co) for v in m.vertices]);tris=np.array([list(t.vertices) for t in m.loop_triangles],dtype=int);mats=[obj.data.materials[m.polygons[t.polygon_index].material_index].name for t in m.loop_triangles];e.to_mesh_clear();return pts,tris,mats
for file in [SOURCE,DERIVATIVE]:
 bpy.ops.wm.open_mainfile(filepath=str(file));rig=bpy.data.objects['DosaV2_Rig'];apply_pose(rig,'combined_reach')
 for name in ['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']:
  pts,tri,mats=data(bpy.data.objects[name]);tree=BVHTree.FromPolygons(pts.tolist(),tri.tolist(),all_triangles=True);pairs=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])]
  arr=np.array(pairs,dtype=int);hits=arr[proper_crossings(pts[tri[arr[:,0]]],pts[tri[arr[:,1]]])] if len(arr) else arr
  report['derivativeLiningComparison'].append({'source':str(file),'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'poseId':'combined_reach','mesh':name,'properCrossingPairs':len(hits),'pairs':[{'triangleA':int(a),'triangleB':int(b),'center':pts[np.r_[tri[a],tri[b]]].mean(axis=0).tolist()} for a,b in hits]})
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
 if o.type=='MESH':o.hide_render=not o.name.startswith(('DosaV2_','DosaPackV2_')) or o.name=='DosaV2_SourceSurface'
for o in list(scene.objects):
 if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True;scene.world.color=(.12,.12,.12)
scene.render.resolution_x=1000;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
for name,location,energy,size in [('Key',(-3,-4,4),400,4),('Fill',(3,-1,2),220,3),('Rim',(1,3,3),330,3)]:
 d=bpy.data.lights.new('CrossQA'+name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=location;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.objects.new('CrossQACamera',bpy.data.cameras.new('CrossQACamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO'
mark=bpy.data.materials.new('DiagnosticCrossings');mark.diffuse_color=(1,.02,.25,1);mark.use_nodes=True;bs=next(n for n in mark.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(1,.015,.18,1);bs.inputs['Emission Color'].default_value=(.35,0,.035,1);bs.inputs['Emission Strength'].default_value=.8
def render(name,center,offset,scale,visible=None):
 if visible is not None:
  for o in scene.objects:
   if o.type=='MESH':o.hide_render=o.name not in visible
 camera.location=Vector(center)+Vector(offset);camera.rotation_euler=(Vector(center)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
 path=OUT/(name+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);report['renders'].append({'name':name,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
for pose_id in ['rest','combined_reach']:
 apply_pose(rig,pose_id);pose_source=next(p for p in source_report['poses'] if p['id']==pose_id)
 for name in ['DosaV2_Hands','DosaV2_SleeveInner_L','DosaV2_SleeveInner_R']:
  src=next(p for p in pose_source['objects'] if p['name']==name);obj=bpy.data.objects[name];pts,tri,mats=data(obj);pairs=src['pairs'];categories=Counter();entries=[]
  for pair in pairs:
   a,b=pair['triangleA'],pair['triangleB'];ids=np.r_[tri[a],tri[b]];dominant=Counter()
   for i in ids:
    for g in obj.data.vertices[int(i)].groups:dominant[obj.vertex_groups[g.group].name]+=g.weight
   material_pair=' x '.join(sorted([mats[a],mats[b]]));categories[material_pair]+=1
   entries.append(dict(pair,materials=[mats[a],mats[b]],dominantBones=dominant.most_common(4),restCenter=np.mean([list(obj.data.vertices[int(i)].co) for i in ids],axis=0).tolist()))
  classification={'poseId':pose_id,'mesh':name,'properCrossingPairs':len(pairs),'materialPairs':dict(categories),'pairs':entries};report['classifications'].append(classification)
  if not pairs:continue
  if name=='DosaV2_Hands':
   # Anatomical side split, then one close view for each occupied side.
   for side,sign in [('Left',1),('Right',-1)]:
    subset=[p for p in entries if p['restCenter'][0]*sign>0]
    if not subset:continue
    center=np.mean([p['center'] for p in subset],axis=0);hand=rig.pose.bones[side+'Hand'];rot=(rig.matrix_world@hand.matrix).to_quaternion();dorsal=rot@Vector((0,0,1));width=rot@Vector((1,0,0));finger=rot@Vector((0,1,0));visible={o.name for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface'}
    for view,offset in [('palm',-dorsal*.35+width*.08),('back',dorsal*.35+width*.08),('side',width*.35-finger*.04)]:render(pose_id+'_'+side+'_'+view,center,offset,.12,visible)
    # Diagnostic marked triangles are exact evaluated positions, no displaced highlights.
    marked_ids=sorted({t for p in subset for t in [p['triangleA'],p['triangleB']]});m=bpy.data.meshes.new('CrossingTriangles');m.from_pydata(pts.tolist(),[],[tri[t].tolist() for t in marked_ids]);m.materials.append(mark);overlay=bpy.data.objects.new('CrossingTriangles',m);scene.collection.objects.link(overlay)
    render(pose_id+'_'+side+'_marked',center,-dorsal*.35+width*.08,.12,{name,'CrossingTriangles'});bpy.data.objects.remove(overlay,do_unlink=True)
  else:
   center=np.mean([p['center'] for p in entries],axis=0);side='Left' if name.endswith('_L') else 'Right';hand=rig.pose.bones[side+'ForeArm'];rot=(rig.matrix_world@hand.matrix).to_quaternion();offset=rot@Vector((0,0,.6));visible={o.name for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface'}
   render(pose_id+'_'+name+'_full',center,offset,.32,visible);render(pose_id+'_'+name+'_isolated',center,offset,.32,{name})
  (OUT/'report.json').write_text(json.dumps(report,indent=2))
(OUT/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps({'classifications':[dict((k,v) for k,v in c.items() if k!='pairs') for c in report['classifications']],'linings':[dict((k,v) for k,v in c.items() if k!='pairs') for c in report['derivativeLiningComparison']],'renders':len(report['renders'])},indent=2))
