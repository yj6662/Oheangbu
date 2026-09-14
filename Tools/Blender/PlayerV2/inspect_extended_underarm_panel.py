"""Inspect a torso-supported garment panel instead of pinning an arm transition."""
import bpy,json,math,hashlib,importlib.util
import numpy as np
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'TorsoSupportedPanel';OUT.mkdir(exist_ok=True)
SOURCE=BASE/'VerifiedAttachments/Inputs/Assembled-d979b9bc.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
body=bpy.data.objects['DosaV2_BodyCore'];triangles=[list(p.vertices) for p in body.data.polygons];edges={};neighbors=[set() for p in triangles]
for i,t in enumerate(triangles):
    for a,b in zip(t,t[1:]+t[:1]):edges.setdefault(tuple(sorted((a,b))),[]).append(i)
for ids in edges.values():
    for i in ids:neighbors[i].update(set(ids)-{i})
original=json.loads((BASE/'VerifiedAttachments/body-panel-transfer-selection.json').read_text());seed=set(original['selectedBodyFaces'])
spec=importlib.util.spec_from_file_location('torso_panel_render',Path(__file__).with_name('build_brush.py'));helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
for o in scene.objects:
    if o.type=='MESH':o.hide_render=not(o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface');o.hide_set(False)
camera=scene.camera or helper.lighting(scene);scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
mat=bpy.data.materials.new('DiagnosticTorsoSupportedPanel');mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.012,.48,.55,1);body.data.materials.append(mat);original_mats=[p.material_index for p in body.data.polygons]
results=[]
for limit in [-.12,-.10,-.08]:
    allowed={p.index for p in body.data.polygons if -.27<p.center.x<limit and p.center.y<.005 and 1.14<p.center.z<1.46}
    panel=set(seed);stack=list(seed)
    while stack:
        i=stack.pop()
        for j in neighbors[i]:
            if j in allowed and j not in panel:panel.add(j);stack.append(j)
    boundary_edges=[e for e,ids in edges.items() if any(i in panel for i in ids) and any(i not in panel for i in ids)];boundary=sorted({i for e in boundary_edges for i in e})
    rows=[]
    for i in boundary:
        v=body.data.vertices[i];weights={body.vertex_groups[g.group].name:g.weight for g in v.groups};rows.append({'vertex':i,'point':list(v.co),'weights':weights})
    label=str(abs(round(limit*100)));selection={'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'selectedBodyFaces':sorted(panel),'triangles':len(panel),'nearTriangleEstimate':25966+len(panel),'remainingBodyInterfaceVertices':rows,
      'scope':'Connected front/axilla/shoulder cloth extends from the actual garment side toward a stable torso-supported line. This is a proposed garment cut, not evidence that original scan triangulation contains stitches. Visually inspect and validate before assigning attachment weights.'}
    (OUT/('panel-'+label+'.json')).write_text(json.dumps(selection,indent=2),encoding='utf-8')
    for p in body.data.polygons:p.material_index=len(body.data.materials)-1 if p.index in panel else original_mats[p.index]
    path=OUT/('panel-'+label+'-rest.png');helper.render(scene,camera,path,(0,-.01,1.25),.76,math.pi*.12,width=1100,height=950)
    results.append({'limitX':limit,'faces':len(panel),'boundaryVertices':len(boundary),'minZ':min(v['point'][2] for v in rows),'maxZ':max(v['point'][2] for v in rows),'boundaryMaximumArmWeight':max(v['weights'].get('RightArm',0)+v['weights'].get('RightForeArm',0) for v in rows),'path':str(path.relative_to(ROOT))});print(json.dumps(results[-1]),flush=True)
(OUT/'inspection-report.json').write_text(json.dumps({'status':'READ_ONLY_PANEL_CLASSIFICATION_NOT_RIG_PASS','candidates':results},indent=2),encoding='utf-8')
