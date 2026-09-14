"""Read-only PBR-channel/UV evidence for the actual authored brush."""
import bpy,json,hashlib,numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';SOURCE=ART/'DosaBrushV2.blend';OUT=ART/'Inspect/BrushMaterial';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));report={'source':str(SOURCE),'sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'objects':[]}
images={}
for name in ['BaseColor','Normal','Metallic','Roughness']:
 path=ART/'Staging/Brush/Textures'/('T_DosaBrushV2_'+name+'.png');im=bpy.data.images.load(str(path),check_existing=False);im.colorspace_settings.name='sRGB' if name=='BaseColor' else 'Non-Color';a=np.empty(len(im.pixels),dtype=np.float32);im.pixels.foreach_get(a);images[name]=(im,a.reshape(im.size[1],im.size[0],4));report.setdefault('textures',{})[name]={'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'size':list(im.size)}
for objname in ['DosaBrushV2_Handle','DosaBrushV2_Bristles']:
 o=bpy.data.objects[objname];o.data.calc_loop_triangles();uv=o.data.uv_layers.active.data;centers=np.array([sum((uv[i].uv for i in t.loops),start=uv[t.loops[0]].uv*0)/3 for t in o.data.loop_triangles]);areas=np.array([t.area for t in o.data.loop_triangles]);stats={}
 for name,(im,a) in images.items():
  ix=np.minimum(im.size[0]-1,np.maximum(0,(centers[:,0]*im.size[0]).astype(int)));iy=np.minimum(im.size[1]-1,np.maximum(0,(centers[:,1]*im.size[1]).astype(int)));samples=a[iy,ix,:3];stats[name]={'areaWeightedMeanRGB':np.average(samples,axis=0,weights=areas).tolist(),'redPercentiles':np.percentile(samples[:,0],[0,10,50,90,100]).tolist()}
 materials=[]
 for m in o.data.materials:
  bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');row={'name':m.name,'inputs':{},'images':[]}
  for key in ['Base Color','Metallic','Roughness','IOR','Normal']:
   p=bs.inputs[key];row['inputs'][key]={'default':list(p.default_value) if hasattr(p.default_value,'__len__') else p.default_value,'links':[{'node':l.from_node.name,'socket':l.from_socket.name} for l in p.links]}
  for n in m.node_tree.nodes:
   if n.type=='TEX_IMAGE' and n.image:row['images'].append({'node':n.name,'image':n.image.name,'size':list(n.image.size),'colorspace':n.image.colorspace_settings.name})
  materials.append(row)
 report['objects'].append({'name':objname,'uvStats':stats,'materials':materials})
(OUT/'source-material-audit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
