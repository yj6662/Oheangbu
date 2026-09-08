"""Explicit, local C02 lab operations. Loading definitions has no scene effects."""
import bpy, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'
OLD=ROOT/'Art/PlayerPhase1/AutoPlayerV1/Candidates/C02'

def write(path,value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False),encoding='utf8')

def assign(action,frame):
    rig=bpy.data.objects['Armature'];rig.animation_data_create()
    rig.animation_data.action=bpy.data.actions[action]
    if rig.animation_data.action.slots:rig.animation_data.action_slot=rig.animation_data.action.slots[0]
    rig.data.pose_position='POSE';bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()

def render(label,action=None,frame=1,view='front',target=(0,0,.90),width=3.6,gray=False,edge_ids=None):
    s=bpy.context.scene;o=bpy.data.objects['C02_Mesh_0'];rig=bpy.data.objects['Armature']
    if action:assign(action,frame)
    else:rig.data.pose_position='REST';bpy.context.view_layer.update()
    c=s.camera;ctr=Vector(target);dirs={'front':(0,-1,0),'back':(0,1,0),'left':(1,0,0),'right':(-1,0,0),'threequarter':(1,-2,.1)}
    c.location=ctr+Vector(dirs[view]).normalized()*5;c.rotation_euler=(ctr-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=width
    s.render.engine='CYCLES';s.cycles.samples=8;s.cycles.use_denoising=True
    s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100;s.render.fps=30
    s.render.image_settings.file_format='PNG';s.render.filepath=str(LAB/'Body/Previews'/(label+'.png'));Path(s.render.filepath).parent.mkdir(parents=True,exist_ok=True)
    original=list(o.data.materials);debug=[]
    try:
        if gray:
            mat=bpy.data.materials.get('LAB_NeutralGray') or bpy.data.materials.new('LAB_NeutralGray');mat.use_nodes=True
            node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.35,.35,.35,1);node.inputs['Roughness'].default_value=.7
            for i in range(len(o.data.materials)):o.data.materials[i]=mat
        if edge_ids:
            mat=bpy.data.materials.get('LAB_WarningRed') or bpy.data.materials.new('LAB_WarningRed');mat.diffuse_color=(1,.01,.005,1);mat.use_nodes=True
            node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(1,.01,.005,1);node.inputs['Emission Color'].default_value=(1,.01,.005,1);node.inputs['Emission Strength'].default_value=.5
            ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
            curve=bpy.data.curves.new('LAB_DebugEdges','CURVE');curve.dimensions='3D';curve.bevel_depth=.0006;curve.resolution_u=1;curve.bevel_resolution=1
            for eid in edge_ids:
                spl=curve.splines.new('POLY');spl.points.add(1)
                for pt,vid in zip(spl.points,o.data.edges[eid].vertices):pt.co=(*(ev.matrix_world@mesh.vertices[vid].co),1)
            ev.to_mesh_clear();d=bpy.data.objects.new('LAB_DebugEdges',curve);s.collection.objects.link(d);curve.materials.append(mat);debug.append(d)
        bpy.ops.render.render(write_still=True)
    finally:
        for i,mat in enumerate(original):o.data.materials[i]=mat
        for d in debug:
            data=d.data;bpy.data.objects.remove(d,do_unlink=True);bpy.data.curves.remove(data)
        rig.data.pose_position='POSE';bpy.context.view_layer.update()
    return s.render.filepath

def add_twist_diagnostic():
    """Existing diagnostics preserved; one missing torso-twist diagnostic, not production."""
    rig=bpy.data.objects['Armature'];name='LAB_DIAG_TorsoTwist'
    if bpy.data.actions.get(name):return name
    action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data_create();rig.animation_data.action=action
    for f,angle in [(1,0),(16,35),(30,35),(45,-35),(59,-35),(74,0)]:
        for p in rig.pose.bones:p.matrix_basis.identity()
        # Bone-local Y follows the anatomical spine chain; rotation twists around its length.
        for bn,fraction in [('Spine02',.25),('Spine01',.4),('Spine',.35)]:
            p=rig.pose.bones[bn];p.rotation_mode='QUATERNION'
            from mathutils import Quaternion
            p.rotation_quaternion=Quaternion((0,1,0),math.radians(angle*fraction));p.keyframe_insert('rotation_quaternion',frame=f,group=bn)
        for p in rig.pose.bones:
            if p.name not in ['Spine02','Spine01','Spine']:p.keyframe_insert('rotation_quaternion',frame=f,group=p.name)
            p.keyframe_insert('location',frame=f,group=p.name);p.keyframe_insert('scale',frame=f,group=p.name)
    write(LAB/'Body/torso_twist_protocol.json',{'action':name,'frames':[1,74],'fps':30,'amplitude_degrees':35,'axis':'anatomical spine bone local Y','production_motion':False})
    return name

def audit_stage(stage,max_influences=4):
    ns={};f=ROOT/'Tools/Blender/AutoPlayerV1/audit_candidate.py';exec(compile(f.read_text(encoding='utf8'),str(f),'exec'),ns)
    if max_influences != 4:
        old_check=ns['ac_weights']
        def lab_weights(obj,rig):
            rows,stats=old_check(obj,rig)
            invalid=[i for i,row in enumerate(rows) if not row or len(row)>max_influences or any(not math.isfinite(g['weight']) or g['weight']<0 for g in row) or abs(sum(g['weight'] for g in row)-1)>.0001]
            stats['invalid_vertex_ids']=invalid;stats['status']='FAIL' if invalid or stats['negative_weights'] or stats['nonfinite_weights'] else 'PASS'
            stats['lab_allowed_influences']=max_influences
            stats['exception_reason']='Bounded axilla five-influence test to avoid discontinuous top-four palette truncation. Requires Unity Custom import and Unlimited skinning; not a universal default change.'
            return rows,stats
        ns['ac_weights']=lab_weights
    result=[]
    for f in (OLD/'Repair2Audit').glob('*_audit.json'):
        d=json.loads(f.read_text(encoding='utf8'));r=ns['audit_candidate']('Armature',['C02_Mesh_0'],d['action'],LAB/'Body'/stage/(d['action']+'_audit.json'),provenance=d['provenance'],source_fps=30,source_frame_range=d['source_frame_range']);result.append(r)
    if bpy.data.actions.get('LAB_DIAG_TorsoTwist'):
        result.append(ns['audit_candidate']('Armature',['C02_Mesh_0'],'LAB_DIAG_TorsoTwist',LAB/'Body'/stage/'LAB_DIAG_TorsoTwist_audit.json',provenance={'kind':'local_diagnostic','protocol':'torso_twist_protocol.json'},source_fps=30,source_frame_range=[1,74]))
    summary=[]
    for r in result:
        m=r['meshes']['C02_Mesh_0'];w=m['worst_qualified_edge'];summary.append({'action':r['action'],'data':r.get('data_status'),'frames':len(r['sampled_integer_frames']),'over2':len(m['warn_edges_over_2_unique']),'over4':len(m['warn_edges_over_4_unique']),'worst':w})
    write(LAB/'Body'/stage/'summary.json',summary)
    return [{k:v for k,v in r.items() if k!='worst'}|{'max_ratio':r['worst']['ratio']} for r in summary]

def candidate_a1():
    """Boundary-conditioned weight smoothing at known left axilla; no mesh/rest/motion edits."""
    if (LAB/'Body/A1.json').exists():raise RuntimeError('A1 already evaluated or started; do not silently repeat')
    o=bpy.data.objects['C02_Mesh_0'];rig=bpy.data.objects['Armature'];names=list(rig.data.bones.keys());n=len(names)
    xyz=np.array([tuple(o.matrix_world@v.co) for v in o.data.vertices]);keys={};indices=[]
    for i,p in enumerate(xyz):key=tuple(np.round(p,6));keys.setdefault(key,[]).append(i);indices.append(key)
    unique=list(keys);lookup={k:i for i,k in enumerate(unique)};v2u=np.array([lookup[k] for k in indices]);pts=np.array(unique);weights=np.zeros((len(unique),n))
    for k,ids in keys.items():
        for g in o.data.vertices[ids[0]].groups:
            name=o.vertex_groups[g.group].name
            if name in names:weights[lookup[k],names.index(name)]=g.weight
    original=weights.copy();center=np.array([.152,.068,1.201]);radii=np.array([.11,.105,.13]);dist=np.linalg.norm((pts-center)/radii,axis=1);mask=dist<1;fade=np.clip((1-dist)/.55,0,1);fade=fade*fade*(3-2*fade)
    links=[set() for _ in unique]
    for e in o.data.edges:
        a,b=v2u[list(e.vertices)]
        if a!=b:links[a].add(b);links[b].add(a)
    ids=np.flatnonzero(mask); hip=names.index('Hips');sp=names.index('Spine02')
    seed=weights.copy();seed[ids,sp]+=seed[ids,hip];seed[ids,hip]=0
    work=seed.copy()
    for _ in range(12):
        nxt=work.copy()
        for i in ids:
            neighbors=list(links[i])
            if neighbors:nxt[i]=.55*work[i]+.45*np.mean(work[neighbors],axis=0)
        work=nxt
    changes=[]
    for i in ids:
        w=(1-fade[i])*original[i]+fade[i]*work[i]
        keep=np.argsort(w)[-4:];w[np.setdiff1d(np.arange(n),keep)]=0;w/=w.sum()
        if np.max(np.abs(w-original[i]))<1e-7:continue
        vv=keys[unique[i]]
        for g in o.vertex_groups:g.remove(vv)
        for name,value in zip(names,w):
            if value>0:o.vertex_groups[name].add(vv,float(value),'REPLACE')
        changes.append({'vertex_ids':vv,'position_m':list(unique[i]),'before':original[i].tolist(),'after':w.tolist()})
    write(LAB/'Body/A1.json',{'attempt':1,'hypothesis':'Previous isolated palette patches moved the discontinuity; a bounded continuous axilla field should reduce it without changing motion.','region_center_m':center.tolist(),'region_radii_m':radii.tolist(),'method':'12 boundary-conditioned graph diffusion iterations; Hips influence reallocated to adjacent Spine02 within falloff; top4 normalized; duplicate-position data treated consistently, geometry not welded','changed_unique_positions':len(changes),'bone_order':names,'changes':changes,'geometry_uv_rest_motion_changed':False,'status':'AWAITING_COMPARISON'})
    bpy.ops.wm.save_as_mainfile(filepath=str(LAB/'Body/A1.blend'))
    return {'changed_unique_positions':len(changes),'vertex_ids':sum(len(c['vertex_ids']) for c in changes)}

def candidate_a2():
    """Same bounded field with five influences; branch from Baseline, never stack on A1."""
    if (LAB/'Body/A2.json').exists():raise RuntimeError('A2 already evaluated or started')
    o=bpy.data.objects['C02_Mesh_0'];a1=json.loads((LAB/'Body/A1.json').read_text(encoding='utf8'))
    for c in a1['changes']:
        vv=c['vertex_ids']
        for g in o.vertex_groups:g.remove(vv)
        for name,value in zip(a1['bone_order'],c['before']):
            if value>0:o.vertex_groups[name].add(vv,value,'REPLACE')
    import inspect
    code=inspect.getsource(candidate_a1).replace('def candidate_a1():','def build_a2():').replace('A1','A2').replace("'attempt':1","'attempt':2").replace('[-4:]','[-5:]').replace('top4 normalized','top5 normalized; bounded five-weight exception').replace('Previous isolated palette patches moved the discontinuity; a bounded continuous axilla field should reduce it without changing motion.','A1 top-four projection moved the discontinuity. Retain fifth influence in the identical bounded field; restore baseline first.')
    scope=dict(globals());exec(compile(code,'a2_method','exec'),scope)
    return scope['build_a2']()
