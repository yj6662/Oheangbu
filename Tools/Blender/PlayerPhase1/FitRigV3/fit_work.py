import bpy, bmesh, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')
NAMES=['Body','InnerTop','Durumagi']
def write(name,v):
    (OUT/name).write_text(json.dumps(v,indent=2,ensure_ascii=False),encoding='utf-8')
def rig(): return bpy.data.objects['Dosa_Phase1_Rig']
def rest():
    a=rig(); a.animation_data_create(); a.animation_data.action=None
    a.data.pose_position='POSE'
    for p in a.pose.bones: p.matrix_basis=Matrix.Identity(4)
    bpy.context.scene.frame_set(1); bpy.context.view_layer.update()
def action(name,frame=1):
    a=rig(); a.animation_data_create(); a.animation_data.action=bpy.data.actions[name]
    if a.animation_data.action.slots: a.animation_data.action_slot=a.animation_data.action.slots[0]
    bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
def mode(stage):
    for name in NAMES:
        for m in bpy.data.objects[name].modifiers:
            state=(m.type=='ARMATURE') if stage!='physics' else ((m.type=='SURFACE_DEFORM') if name=='Durumagi' else m.type=='ARMATURE')
            m.show_viewport=m.show_render=state
    for m in bpy.data.objects['ClothProxy_Durumagi'].modifiers:
        if m.type=='CLOTH': m.show_viewport=m.show_render=(stage=='physics')
    bpy.context.view_layer.update()
def points(o,evaluated=True):
    if isinstance(o,str): o=bpy.data.objects[o]
    if not evaluated:return [o.matrix_world@v.co for v in o.data.vertices]
    e=o.evaluated_get(bpy.context.evaluated_depsgraph_get()); me=e.to_mesh()
    ps=[e.matrix_world@v.co for v in me.vertices];e.to_mesh_clear();return ps
def tree(o,evaluated=True):
    if isinstance(o,str):o=bpy.data.objects[o]
    e=o.evaluated_get(bpy.context.evaluated_depsgraph_get()) if evaluated else o
    me=e.to_mesh() if evaluated else o.data
    t=BVHTree.FromPolygons([e.matrix_world@v.co for v in me.vertices],[p.vertices[:] for p in me.polygons])
    if evaluated:e.to_mesh_clear()
    return t
def render(name,view='front',close=False,parts=None):
    s=bpy.context.scene; c=s.camera;target=Vector((0,0,1.43 if close else .89))
    offsets={'front':(0,-4,0),'back':(0,4,0),'left':(4,0,0),'right':(-4,0,0),'threequarter':(2,-4,.6)}
    c.location=target+Vector(offsets[view]);c.rotation_euler=(-Vector(offsets[view])).to_track_quat('-Z','Y').to_euler()
    c.data.type='ORTHO';c.data.ortho_scale=1.22 if close else 3.5
    s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100
    s.render.image_settings.file_format='PNG';s.cycles.samples=12
    folder=OUT/'Previews';folder.mkdir(exist_ok=True);s.render.filepath=str(folder/(name+'.png'))
    old={n:bpy.data.objects[n].hide_render for n in NAMES}
    if parts:
        for n in NAMES:bpy.data.objects[n].hide_render=n not in parts
    bpy.ops.render.render(write_still=True)
    for n,v in old.items():bpy.data.objects[n].hide_render=v
def region(p,label):
    x,y,z=p
    if label=='shoulder':return .12<abs(x)<.27 and z>1.40
    if label=='collar':return abs(x)<.12 and z>1.43
    return .13<abs(x)<.30 and 1.22<z<1.40
def clearances():
    rows={}
    for outer,inner in [('InnerTop','Body'),('Durumagi','InnerTop')]:
        t=tree(inner);base=points(outer,False);ps=points(outer)
        rows[outer]={}
        for label in ['shoulder','collar','armpit']:
            ds=[];signed=[]
            for p,b in zip(ps,base):
                if region(b,label):
                    co,no,idx,d=t.find_nearest(p)
                    if co is not None:ds.append(d*1000);signed.append((p-co).dot(no)*1000)
            rows[outer][label]={'n':len(ds),'distance_mm_p10_p50_p90':np.percentile(ds,[10,50,90]).tolist() if ds else [],'signed_mm_p10_p50_p90':np.percentile(signed,[10,50,90]).tolist() if ds else []}
    return rows
def record_original():
    report={'file':bpy.data.filepath,'units':{'system':bpy.context.scene.unit_settings.system,'scale':bpy.context.scene.unit_settings.scale_length},'objects':{}}
    for n in NAMES+['Dosa_Phase1_Rig','BodyCollisionProxy','ClothProxy_Durumagi']:
        o=bpy.data.objects[n];row={'matrix_world':[list(v) for v in o.matrix_world],'matrix_parent_inverse':[list(v) for v in o.matrix_parent_inverse],'parent':o.parent.name if o.parent else None,'modifiers':[]}
        for m in o.modifiers:
            d={'name':m.name,'type':m.type,'viewport':m.show_viewport,'render':m.show_render}
            if hasattr(m,'object'):d['target']=m.object.name if m.object else None
            if m.type=='SURFACE_DEFORM':d.update(target=m.target.name if m.target else None,bound=m.is_bound)
            if m.type=='CLOTH':
                d['settings']={k:getattr(m.settings,k) for k in ['quality','mass','air_damping','tension_stiffness','compression_stiffness','shear_stiffness','bending_stiffness','pin_stiffness','vertex_group_mass','use_dynamic_mesh']}
                d['cache']={'baked':m.point_cache.is_baked,'start':m.point_cache.frame_start,'end':m.point_cache.frame_end}
                d['collision']={k:getattr(m.collision_settings,k) for k in ['distance_min','collision_quality','use_self_collision']}
            row['modifiers'].append(d)
        if o.type=='MESH':
            o.data.calc_loop_triangles();row.update(vertices=len(o.data.vertices),tris=len(o.data.loop_triangles))
            deform={g.index for g in o.vertex_groups if g.name in rig().data.bones}
            ws=[[g.weight for g in v.groups if g.group in deform and g.weight>0] for v in o.data.vertices]
            row['weights']={'unassigned':sum(not w for w in ws),'max_influences':max(map(len,ws),default=0),'max_sum_error':max((abs(sum(w)-1) for w in ws),default=0)}
            row['geometry_sha256']=hashlib.sha256(np.array([list(v.co) for v in o.data.vertices],dtype=np.float32).tobytes()+np.array([list(t.vertices) for t in o.data.loop_triangles],dtype=np.int32).tobytes()).hexdigest()
        report['objects'][n]=row
    write('original_settings.json',report);return report
def diagnose(stage,label,pose=None,frame=1):
    rest();mode(stage)
    if pose:action(pose,frame)
    if stage=='physics':
        bpy.context.scene.frame_set(0)
        for f in range(1,frame+1):bpy.context.scene.frame_set(f);bpy.context.view_layer.update()
    result=clearances();write('diagnosis_'+label+'.json',result)
    render('Before_'+label,'threequarter',True)
    return result

def smoothstep(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def fit_local(name,inner):
    rest();mode('skin');o=bpy.data.objects[name]
    # Only the editable clothing copy is welded. UVs stay on face corners.
    bm=bmesh.new();bm.from_mesh(o.data);before=len(bm.verts)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.0000005)
    bm.verts.ensure_lookup_table();original={v:v.co.copy() for v in bm.verts}
    # Fair the irregular generated upper garment while retaining collar/cuff edges.
    for iteration in range(10):
        updates={}
        for v in bm.verts:
            x,y,z=v.co
            if z<1.16 or not v.link_edges:continue
            w=smoothstep(1.16,1.32,z)*(.12 if v.is_boundary else .35)
            ns=[e.other_vert(v).co for e in v.link_edges]
            avg=sum(ns,Vector())/len(ns)
            updates[v]=v.co.lerp(avg,w)
        for v,p in updates.items():v.co=p
    target=tree(inner,False);bodytree=tree('Body',False);moved=[]
    for v in bm.verts:
        p=v.co.copy();x,y,z=p;ax=abs(x)
        # Collar and shoulder cap only; sleeve underside and skirt keep their ease.
        upper=smoothstep(1.29,1.43,z)
        shoulder=(1-smoothstep(.26,.40,ax))*upper
        collar=(1-smoothstep(.085,.14,ax))*smoothstep(1.42,1.48,z)
        armroof=smoothstep(.21,.30,ax)*smoothstep(1.39,1.46,z)
        w=max(shoulder,collar,armroof*.75)
        co,no,idx,d=target.find_nearest(p)
        if co is not None and z>1.18 and d<.16:
            if inner!='Body':
                bc,bn,bi,bd=bodytree.find_nearest(co)
                if no.dot(co-bc)<0:no=-no
            signed=(p-co).dot(no)
            # A local offset accounts for shirt thickness and the outer layer.
            gap=.010 if name=='InnerTop' else .009
            goal=co+no*gap
            if signed>gap: v.co=p.lerp(goal,w*.88)
            elif signed<gap*.65 and d<.055:
                v.co=p.lerp(goal,smoothstep(1.18,1.34,z)*.8)
        moved.append((v.co-original[v]).length)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(o.data);after=len(bm.verts);bm.free()
    for p in o.data.polygons:p.use_smooth=True
    # Split normals from the generated flat-shaded source no longer fit this mesh.
    if o.data.has_custom_normals:
        bpy.context.view_layer.objects.active=o
        try:bpy.ops.mesh.customdata_custom_splitnormals_clear()
        except Exception:pass
    o.data.update()
    write('fit_'+name+'.json',{'before_vertices':before,'after_vertices':after,'max_local_move_mm':max(moved)*1000,'p50_move_mm':float(np.median(moved))*1000,'method':'Weld coincident clothing vertices preserving corner UVs; upper-garment local fairing and collar/shoulder/upper-sleeve projection to the inner layer. Hem and loose sleeve underside excluded.'})
    return clearances()

def transfer_upper(name):
    rest();o=bpy.data.objects[name];body=bpy.data.objects['Body'];body.data.calc_loop_triangles()
    tris=[tuple(t.vertices) for t in body.data.loop_triangles]
    bt=BVHTree.FromPolygons([v.co for v in body.data.vertices],tris,all_triangles=True)
    count=0
    for v in o.data.vertices:
        if name=='Durumagi' and v.co.z<1.08:continue
        co,no,idx,d=bt.find_nearest(v.co);ids=tris[idx]
        p0,p1,p2=[body.data.vertices[i].co for i in ids];u=p1-p0;w=p2-p0;r=co-p0
        den=u.dot(u)*w.dot(w)-u.dot(w)**2
        b=(w.dot(w)*r.dot(u)-u.dot(w)*r.dot(w))/den if abs(den)>1e-12 else 0
        c=(u.dot(u)*r.dot(w)-u.dot(w)*r.dot(u))/den if abs(den)>1e-12 else 0
        ws={}
        for i,q in zip(ids,[max(0,1-b-c),max(0,b),max(0,c)]):
            for g in body.data.vertices[i].groups:
                n=body.vertex_groups[g.group].name
                if 'Hand' in n:n=('Left' if n.startswith('Left') else 'Right')+'ForeArm'
                if name=='Durumagi' and any(k in n for k in ['Leg','Foot','Toe']):continue
                ws[n]=ws.get(n,0)+g.weight*q
        best=sorted(ws.items(),key=lambda a:-a[1])[:4];total=sum(w for n,w in best)
        if total<1e-8:best=[('Hips',1)];total=1
        for gi in [g.group for g in v.groups]:
            if o.vertex_groups[gi].name in rig().data.bones:o.vertex_groups[gi].remove([v.index])
        for n,w in best:
            vg=o.vertex_groups.get(n) or o.vertex_groups.new(name=n);vg.add([v.index],w/total,'REPLACE')
        count+=1
    print('Transferred',name,count)
