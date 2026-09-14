"""Separate anatomical shoulder connection trial; frozen AF meshes unchanged."""
import bpy,json,math,ast,hashlib,importlib.util,argparse,sys
from pathlib import Path
import numpy as np
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
parser=argparse.ArgumentParser();parser.add_argument('--middle-weight',type=float,default=.5);parser.add_argument('--label',default='')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection'
if args.label:OUT=OUT/args.label
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=BASE/'DosaV2_FoldedGusset.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for name,wanted in [('fit_gusset_anchor_field.py',['skin','inside']),('audit_triangle_crossings.py',['proper_crossings'])]:
    file=Path(__file__).with_name(name);nodes=[n for n in ast.parse(file.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in wanted];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(file),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
rest_inverse={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones}
parts={};sides=[('Left',1),('Right',-1)]
for side,sign in sides:
    arm=bpy.data.objects['DosaV2_ArmLining_'+side];head=np.array(rig.data.bones[side+'Arm'].head_local);axis=np.array(rig.data.bones[side+'ForeArm'].head_local)-head;axis/=np.linalg.norm(axis)
    points=np.array([v.co[:] for v in arm.data.vertices]);original_radius=max(np.linalg.norm(p-head-np.dot(p-head,axis)*axis) for p in points[24:48]);radius=original_radius
    y=np.array([0.,1.,0.]);z=np.cross(axis,y);z/=np.linalg.norm(z)
    # The existing shoulder joint radius is retained. Only a connecting deltoid
    # volume is added between the actual torso interior and the shoulder pivot.
    proximal=np.array([sign*.111,0.,1.351]);middle=(proximal+head)*.5
    rings=[(proximal,radius,0.),(middle,radius,args.middle_weight),(head,radius,1.),(head+axis*radius/math.sqrt(2),radius/math.sqrt(2),1.)]
    verts=[];weights=[];faces=[];segments=12
    for center,r,shoulder in rings:
        for i in range(segments):
            a=2*math.pi*i/segments;verts.append((center+r*(math.cos(a)*y+math.sin(a)*z)).tolist());weights.append({'Spine02':1-shoulder,side+'Shoulder':shoulder})
    for j in range(len(rings)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments;c=(j+1)*segments+(i+1)%segments;d=(j+1)*segments+i;faces.extend([(a,b,c),(a,c,d)])
    first=len(verts);verts.append(proximal.tolist());weights.append({'Spine02':1.})
    last=len(verts);verts.append((head+axis*radius).tolist());weights.append({side+'Shoulder':1.})
    for i in range(segments):faces.extend([(first,(i+1)%segments,i),(last,3*segments+i,3*segments+(i+1)%segments)])
    name='DosaV2_ShoulderLining_'+side;me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();obj=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(obj)
    import bmesh
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    for row in weights:
        for n,w in row.items():
            if w>0 and obj.vertex_groups.get(n) is None:obj.vertex_groups.new(name=n)
    for i,row in enumerate(weights):
        for n,w in row.items():
            if w>0:obj.vertex_groups[n].add([i],w,'REPLACE')
    for material in arm.data.materials:me.materials.append(material)
    for face in me.polygons:face.use_smooth=True
    uv=me.uv_layers.new(name='UVMap')
    for loop in me.loops:
        p=me.vertices[loop.vertex_index].co;uv.data[loop.index].uv=(p.y*6+.5,p.z*6)
    modifier=obj.modifiers.new('DosaV2_Deform','ARMATURE');modifier.object=rig;obj.parent=rig;obj['SemanticPart']='shoulder_anatomy_connection';obj['ExportPart']='WholeBody'
    me.calc_loop_triangles();parts[name]={'rest':np.array(verts),'weights':weights,'triangles':np.array([list(t.vertices) for t in me.loop_triangles]),'originalArmRadiusMeters':radius}
    assert len(me.loop_triangles)==96
bpy.context.view_layer.update()
DEST=OUT/'DosaV2_ShoulderConnection_Candidate.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
cloth={}
for name in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    o=bpy.data.objects[name];mob=o.data.color_attributes['ClothMobility'];ids=[i for i,v in enumerate(mob.data) if v.color[0]==0]
    cloth[name]={'ids':ids,'rest':np.array([o.data.vertices[i].co[:] for i in ids]),'weights':[{o.vertex_groups[g.group].name:g.weight for g in o.data.vertices[i].groups} for i in ids]}
rows=[]
paths=sorted((BASE/'IntermediatePoseData').glob('*.json'))
for path in paths:
    pose=json.loads(path.read_text());mat={n:np.array(m)@rest_inverse[n] for n,m in pose['bonePoseMatrices'].items()};row={'poseId':pose['poseId'],'parts':{}}
    for name,m in parts.items():
        p=skin(m['rest'],m['weights'],mat);t=m['triangles'];tree=BVHTree.FromPolygons(p.tolist(),t.tolist(),all_triangles=True);lo=np.min(p,axis=0);hi=np.max(p,axis=0)
        pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
        crosses=int(np.sum(proper_crossings(p[t[pairs[:,0]]],p[t[pairs[:,1]]]))) if len(pairs) else 0
        hits=[]
        for n,c in cloth.items():
            pts=skin(c['rest'],c['weights'],mat);ids=np.flatnonzero(np.all(pts>=lo,axis=1)&np.all(pts<=hi,axis=1))
            for i in ids:
                v=Vector(pts[i]);loc,norm,face,d=tree.find_nearest(v)
                if inside(tree,v):hits.append({'cloth':n,'vertex':c['ids'][i],'depthMeters':d,'point':list(v)})
        row['parts'][name]={'selfCrossings':crosses,'pinsInside':hits}
    rows.append(row)
    if len(rows)%100==0:print('CONNECTION_POSES '+str(len(rows)),flush=True)
report={'status':'ADDITIVE_SHOULDER_CANDIDATE_NOT_RIG_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'newParts':{n:{'vertices':len(m['rest']),'triangles':len(m['triangles']),'originalArmRadiusMeters':m['originalArmRadiusMeters']} for n,m in parts.items()},
 'sourceMeshesUnchanged':True,'middleShoulderWeight':args.middle_weight,'poseCount':len(rows),'pinCasesInside':sum(len(p['pinsInside']) for r in rows for p in r['parts'].values()),
 'maxInsideDepthMeters':max([0]+[h['depthMeters'] for r in rows for p in r['parts'].values() for h in p['pinsInside']]),
 'maximumSelfCrossings':max(p['selfCrossings'] for r in rows for p in r['parts'].values()),'rows':rows}
(OUT/'candidate-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2),flush=True)
