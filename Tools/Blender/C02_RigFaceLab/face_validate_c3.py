"""C3 validation and true 30fps PNG/MP4 blink sequence, isolated background only."""
import bpy,json,math,hashlib,subprocess
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face';FOLDER=OUT/'C3'
ns={'__name__':'face_helpers'};exec(compile((ROOT/'Tools/Blender/C02_RigFaceLab/face_inspect.py').read_text(encoding='utf-8'),'face_inspect.py','exec'),ns)
body=bpy.data.objects['C02_Mesh_0'];lids=[bpy.data.objects['FaceLid_Left'],bpy.data.objects['FaceLid_Right']]
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');rig.animation_data_clear();rig.data.pose_position='POSE'
for p in rig.pose.bones:p.matrix_basis.identity()
body.data.calc_loop_triangles();coords=np.array([v.co[:] for v in body.data.vertices],dtype=np.float64);tris=np.array([t.vertices[:] for t in body.data.loop_triangles],dtype=np.int32)
groups=[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
baseline=json.loads((OUT/'baseline_inspection.json').read_text(encoding='utf-8'))['meshes'][0]
preservation={'geometry_sha256':hashlib.sha256(coords.tobytes()+tris.tobytes()).hexdigest(),'weights_sha256':hashlib.sha256(json.dumps(groups).encode()).hexdigest()}
preservation['geometry_unchanged']=preservation['geometry_sha256']==baseline['geometry_sha256'];preservation['weights_unchanged']=preservation['weights_sha256']==baseline['weights_sha256']
body_bvh=BVHTree.FromPolygons([body.matrix_world@v.co for v in body.data.vertices],[t.vertices[:] for t in body.data.loop_triangles],all_triangles=True)
def values(left,right):
 lids[0].data.shape_keys.key_blocks['BlinkLeft'].value=left;lids[1].data.shape_keys.key_blocks['BlinkRight'].value=right;bpy.context.view_layer.update()
def evaluated(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();p=[e.matrix_world@v.co for v in m.vertices];t=[x.vertices[:] for x in m.loop_triangles];e.to_mesh_clear();return p,t
checks=[]
for value in [0,.25,.5,.75,1,0]:
 values(value,value)
 for lid,cx in zip(lids,[.02664062567,-.02749999985]):
  pts,tri=evaluated(lid);bvh=BVHTree.FromPolygons(pts,tri,all_triangles=True);gaps=[]
  for p in pts:
   hit,_,_,_=body_bvh.ray_cast(Vector((p.x,-1,p.z)),Vector((0,1,0)),2)
   if hit:gaps.append(hit.y-p.y)
  covered=0
  for dx in [-.0015,0,.0015]:
   for dz in [-.0015,0,.0015]:
    origin=Vector((cx+dx,-1,1.597148418+dz));hit,_,_,_=bvh.ray_cast(origin,Vector((0,1,0)),2);surface,_,_,_=body_bvh.ray_cast(origin,Vector((0,1,0)),2)
    covered+=int(hit is not None and surface is not None and hit.y<surface.y)
  checks.append({'mesh':lid.name,'value':value,'finite':all(math.isfinite(x) for p in pts for x in p),'minimum_vertex_offset_in_front_of_source_m':min(gaps),'vertices_behind_source_over_0_1mm':sum(g<-.0001 for g in gaps),'pupil_covered_samples_of_9':covered})
for value,label in [(.25,'quarter'),(.75,'threequarter')]:
 values(value,value);ns['render']('C3_'+label+'_front',(0,-1,0))
values(1,1);ns['render']('C3_closed_threequarter',(1,-2,0));ns['render']('C3_closed_left',(1,0,0))
values(0,0)
report={'candidate':'C3','preservation':preservation,'checks':checks,'source_eyeballs':'NONE_SEPARATE','limits':'Occlusion of selected painted pupil landmarks only; no anatomical globe/lid contact claim. Surface-depth test uses the original fixed face shell.',
 'face_blink':'UNVERIFIED_PENDING_CONTINUOUS_REVIEW','fbx_roundtrip':'PASS_SERIALIZATION_ONLY'}
(FOLDER/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
# Real contiguous 60-frame sequence at 30fps; no retiming or missing simulation frames.
s=bpy.context.scene;s.render.engine='BLENDER_EEVEE';
if hasattr(s.eevee,'taa_render_samples'):s.eevee.taa_render_samples=8
s.render.fps=30;s.render.fps=30;s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100
target=Vector((0,-.025,1.595));c=s.camera;c.location=target+Vector((0,-2,0));c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
frames=FOLDER/'BlinkFrames';frames.mkdir(exist_ok=True);timeline=[]
for f in range(1,61):
 s.frame_set(f)
 if f<=10:a=0
 elif f<=18:a=(f-10)/8
 elif f<=23:a=1
 elif f<=33:a=1-(f-23)/10
 elif f<=39:a=0
 elif f<=46:a=(f-39)/7
 elif f<=49:a=1
 else:a=max(0,1-(f-49)/10)
 left=a;right=a if f<=33 else 0;values(left,right)
 s.render.filepath=str(frames/f'frame_{f:06d}.png');bpy.ops.render.render(write_still=True)
 timeline.append({'frame':f,'left':left,'right':right})
values(0,0)
ffmpeg='C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
video=FOLDER/'C3_Blink_30fps.mp4';command=[ffmpeg,'-y','-hide_banner','-loglevel','error','-framerate','30','-i',str(frames/'frame_%06d.png'),'-frames:v','60','-an','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
result=subprocess.run(command,capture_output=True,text=True)
(FOLDER/'video_manifest.json').write_text(json.dumps({'status':'ENCODED' if result.returncode==0 else 'FAIL','error':result.stderr,'frames':60,'fps':30,'duration_seconds':2,'video':str(video),'timeline':timeline,'engine':s.render.engine,'resolution':[1280,720]},indent=2),encoding='utf-8')
print('C3_VALIDATION_COMPLETE',json.dumps(report))
