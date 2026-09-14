import bpy, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerPhase1/FitRigV3'
def defs():
    ns={};exec(compile((ROOT/'Tools/Blender/PlayerPhase1/FitRigV3/fit_work.py').read_text(encoding='utf-8'),'fit_work.py','exec'),ns);return ns
def inside(t,p):
    votes=0
    for d in [Vector((1,.137,.271)).normalized(),Vector((.173,1,.319)).normalized(),Vector((.113,.237,1)).normalized()]:
        q=p.copy();hits=0
        for k in range(30):
            co,no,i,dist=t.ray_cast(q,d,4)
            if co is None:break
            hits+=1;q=co+d*.00001
        votes+=hits%2
    return votes>=2
def audit_action(name,frames=None):
    f=defs();f['rest']();a=bpy.data.actions[name]
    if frames is None:frames=list(range(int(a.frame_range[0]),int(a.frame_range[1])+1))
    # Proxies do not participate in the exported skin test.
    for n in ['ClothProxy_Durumagi','BodyCollisionProxy']:
        for m in bpy.data.objects[n].modifiers:m.show_viewport=False
    rows=[]
    for frame in frames:
        f['action'](name,frame);bt=f['tree']('Body');row={'frame':frame,'parts':{}}
        for n in ['InnerTop','Durumagi']:
            o=bpy.data.objects[n];ps=f['points'](n);bad=[];candidates=0
            for i,p in enumerate(ps):
                co,no,j,d=bt.find_nearest(p)
                signed=(p-co).dot(no)
                if signed<-.002 and d<.1:
                    candidates+=1
                    if inside(bt,p):bad.append((i,-signed))
            edge_ratios=[];longest=0
            for e in o.data.edges:
                i,j=e.vertices;old=(o.data.vertices[i].co-o.data.vertices[j].co).length;length=(ps[i]-ps[j]).length
                longest=max(longest,length)
                if old>.005:edge_ratios.append(length/old)
            row['parts'][n]={'candidates':candidates,'inside_majority':len(bad),'max_inside_mm':max((d for i,d in bad),default=0)*1000,'inside_vertex_ids':[i for i,d in bad], 'finite':all(math.isfinite(c) for p in ps for c in p),'max_edge_ratio_over_5mm':max(edge_ratios,default=1),'longest_edge_m':longest}
        rows.append(row)
    f['rest']()
    result={'action':name,'method':'Every listed integer frame. Nearest body signed distance below -2mm within 100mm, confirmed by >=2 of 3 ray-parity votes. Open body boundaries and self folds can make parity ambiguous. Does not prove absence of cloth self-contact or layer intersections. Edge stretch ignores rest edges shorter than 5mm.','rows':rows}
    (OUT/('audit_'+name+'.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    summary={n:{'inside_max':max(r['parts'][n]['inside_majority'] for r in rows),'depth_max_mm':max(r['parts'][n]['max_inside_mm'] for r in rows),'max_edge_ratio':max(r['parts'][n]['max_edge_ratio_over_5mm'] for r in rows)} for n in ['InnerTop','Durumagi']}
    print(name,json.dumps(summary));return summary
def render_frames(name,frames):
    f=defs();f['rest']();s=bpy.context.scene;c=s.camera
    folder=OUT/'MotionFrames'/name;folder.mkdir(parents=True,exist_ok=True)
    c.location=(2.4,-4.5,1.6);target=Vector((0,0,1.05));c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=4.3
    s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100;s.cycles.samples=8;s.render.image_settings.file_format='PNG'
    for frame in frames:
        f['action'](name,frame);s.render.filepath=str(folder/(f'{frame:04d}.png'));bpy.ops.render.render(write_still=True)
    f['rest']();print('Rendered',name,frames)
