"""Inspect real BodyCore triangles around the duplicated free gusset."""
import bpy,json,math,importlib.util
import numpy as np
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/VerifiedAttachments';SOURCE=OUT/'Inputs/Assembled-d979b9bc.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
body=bpy.data.objects['DosaV2_BodyCore'];outer=bpy.data.objects['DosaV2_SleeveOuter_R'];body.data.calc_loop_triangles();points=np.array([v.co[:] for v in outer.data.vertices])
faceids={p.index for p in body.data.polygons if 3237 in p.vertices};vertices={i for p in body.data.polygons if p.index in faceids for i in p.vertices};adjacent={p.index for p in body.data.polygons if set(p.vertices)&vertices}
triangles=[list(p.vertices) for p in body.data.polygons];edges={};neighbors=[set() for p in body.data.polygons]
for i,tri in enumerate(triangles):
    for a,b in zip(tri,tri[1:]+tri[:1]):edges.setdefault(tuple(sorted((a,b))),[]).append(i)
for ids in edges.values():
    for i in ids:neighbors[i].update(set(ids)-{i})
allowed=set()
for p in body.data.polygons:
    center=p.center;arm=sum(sum(g.weight for g in body.data.vertices[i].groups if body.vertex_groups[g.group].name in ['RightArm','RightForeArm']) for i in p.vertices)/len(p.vertices)
    if -.25<center.x<-.10 and center.y<-.025 and 1.14<center.z<1.39 and arm>.05:allowed.add(p.index)
panel=set(faceids);stack=list(faceids)
while stack:
    i=stack.pop()
    for j in neighbors[i]:
        if j in allowed and j not in panel:panel.add(j);stack.append(j)
assert len(panel)==31
(OUT/'body-panel-transfer-selection.json').write_text(json.dumps({'sourceSha256':'d979b9bc9b5002be66b3a9a5e05524a266703ab81dbe386643674e1a53c86bee','selectedBodyFaces':sorted(panel),'triangles':len(panel),'scope':'Connected front underarm garment panel surrounding the duplicated gusset; x(-.25,-.10),y<-.025,z(1.14,1.39), mean own upper-arm/forearm influence>.05. Visual classification is mandatory; this is not a numeric strain threshold.'},indent=2),encoding='utf-8')
rows=[]
for p in body.data.polygons:
    if p.index not in adjacent:continue
    rows.append({'face':p.index,'directSharedGusset':p.index in faceids,'material':body.data.materials[p.material_index].name,'vertices':[{'index':i,'point':list(body.data.vertices[i].co),
      'weights':{body.vertex_groups[g.group].name:g.weight for g in body.data.vertices[i].groups},'nearestOuterVertex':int(np.argmin(np.linalg.norm(points-np.array(body.data.vertices[i].co),axis=1))),
      'nearestOuterDistanceMeters':float(np.min(np.linalg.norm(points-np.array(body.data.vertices[i].co),axis=1)))} for i in p.vertices]})
(OUT/'body-panel-source-inspection.json').write_text(json.dumps(rows,indent=2),encoding='utf-8');print(json.dumps(rows,indent=2),flush=True)
spec=importlib.util.spec_from_file_location('panel_render_helpers',Path(__file__).with_name('build_brush.py'));helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
for o in scene.objects:
    if o.type=='MESH':o.hide_render=o.name not in ['DosaV2_BodyCore','DosaV2_SleeveOuter_R','DosaV2_SleeveInner_R'];o.hide_set(False)
for name,color,ids in [('Adjacent',(0,.4,1,1),adjacent-faceids),('Shared',(1,0,.4,1),faceids)]:
    m=bpy.data.materials.new('Diagnostic_'+name);m.use_nodes=True;m.diffuse_color=color;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color;body.data.materials.append(m)
    for p in body.data.polygons:
        if p.index in ids:p.material_index=len(body.data.materials)-1
camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16
helper.render(scene,camera,OUT/'body-panel-rest.png',(-.18,-.05,1.29),.32,-math.pi*.05,width=1100,height=900)
material=bpy.data.materials.new('Diagnostic_ProposedWholePanel');material.use_nodes=True;material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.01,.6,.7,1);body.data.materials.append(material)
for p in body.data.polygons:
    if p.index in panel:p.material_index=len(body.data.materials)-1
helper.render(scene,camera,OUT/'body-panel-proposed31.png',(-.18,-.05,1.27),.38,-math.pi*.05,width=1100,height=900)
