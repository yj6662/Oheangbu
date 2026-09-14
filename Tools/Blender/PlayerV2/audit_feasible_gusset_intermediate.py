"""Read-only direct quaternion sampling; no saved animation or source mutation."""
import bpy,ast,json,math,hashlib,importlib.util,argparse,sys
from pathlib import Path
import numpy as np
from mathutils import Matrix,Vector,Euler,Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
parser=argparse.ArgumentParser();parser.add_argument('--source');parser.add_argument('--output');parser.add_argument('--steps',type=int,default=11)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
OUT=Path(args.output) if args.output else ROOT/'Art/PlayerV2/Inspect/ClothBlender/FeasibleGusset';OUT.mkdir(parents=True,exist_ok=True)
POSEDATA=OUT/'IntermediatePoseData';POSEDATA.mkdir(parents=True,exist_ok=True)
SOURCE=Path(args.source) if args.source else OUT/'DosaV2_FeasibleGusset.blend'
def load(name,file):
    s=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
physics=load('physics_helper','diagnose_cloth_blender.py')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses']
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
nodes=[n for n in ast.parse(Path(__file__).with_name('author_feasible_gusset.py').read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['pose','inside']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),'author_feasible_gusset.py','exec'))
names=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
objects={o.name:o for o in bpy.context.scene.objects if o.type=='MESH' and (o.name in names or 'Lining' in o.name)}
static={}
for n in names:
    o=objects[n];o.data.calc_loop_triangles();edges=np.array(sorted({tuple(sorted((a,b))) for tr in o.data.loop_triangles for t in [list(tr.vertices)] for a,b in zip(t,t[1:]+t[:1])}))
    rest=np.array([list(o.matrix_world@v.co) for v in o.data.vertices]);mob=np.array([v.color[0] for v in o.data.color_attributes['ClothMobility'].data])
    static[n]={'edges':edges,'restLength':np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1),'mobility':mob}
rows=[]
for target in ['fixture_raised_arms_settle','fixture_grip_settle','open_hand','combined_reach']:
    pose(target);endpoint={p.name:p.matrix_basis.copy().decompose() for p in rig.pose.bones}
    for step in range(args.steps):
        t=step/(args.steps-1)
        for name,(loc,q,scale) in endpoint.items():
            rotation=Quaternion((1,0,0,0)).slerp(q,t);rig.pose.bones[name].matrix_basis=Matrix.LocRotScale(loc*t,rotation,Vector((1,1,1)).lerp(scale,t))
        bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();points={};triangles={}
        for n,o in objects.items():
            ev=o.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
            points[n]=np.array([list(ev.matrix_world@v.co) for v in me.vertices]);triangles[n]=[list(tr.vertices) for tr in me.loop_triangles];ev.to_mesh_clear()
        trees=[(n,BVHTree.FromPolygons(p.tolist(),triangles[n],all_triangles=True)) for n,p in points.items() if 'Lining' in n]
        (POSEDATA/(target+'_'+str(step).zfill(2)+'.json')).write_text(json.dumps({
          'poseId':'intermediate_'+target+'_'+str(step).zfill(2),'bonePoseMatrices':{p.name:[list(r) for r in p.matrix] for p in rig.pose.bones},
          'vertices':{n:p.tolist() for n,p in points.items() if 'Lining' in n}}),encoding='utf-8')
        for n in names:
            a=static[n];e=a['edges'];m=a['mobility'];p=points[n]
            distance=np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1);ratio=np.maximum(0,distance-m[e[:,0]]-m[e[:,1]])/np.maximum(a['restLength'],1e-30)
            hits=[]
            for i in np.flatnonzero(m==0):
                point=Vector(p[i])
                for lining,tree in trees:
                    if inside(tree,point):
                        near,norm,face,depth=tree.find_nearest(point);hits.append({'vertex':int(i),'lining':lining,'depthMeters':depth,'point':list(point)})
            worst=[]
            for i in np.argsort(ratio)[-4:][::-1]:worst.append({'vertices':e[i].tolist(),'ratio':float(ratio[i]),'restLengthMeters':float(a['restLength'][i]),'skinTargetDistanceMeters':float(distance[i]),'mobilityMeters':m[e[i]].tolist()})
            rows.append({'targetPose':target,'normalizedFraction':t,'surface':n,'maximumMinimumRequiredStretchRatio':float(np.max(ratio)),
               'infeasibleEdges':int(np.sum(ratio>1.35)),'actualInsidePins':hits,'worstEdges':worst})
        print(target+' '+str(t)+' '+json.dumps({'maxRatio':max(r['maximumMinimumRequiredStretchRatio'] for r in rows[-3:]),'inside':sum(len(r['actualInsidePins']) for r in rows[-3:])}),flush=True)
report={'status':'ACTUAL_BLENDER_QUATERNION_INTERMEDIATE_MEASURED_NOT_PHYSICS_PASS',
  'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
  'samples':4*args.steps,'targets':4,'samplesPerTarget':args.steps,'productionActions':len(bpy.data.actions),
  'interpolation':'Each bone basis local quaternion identity.slerp(target,t), linearly interpolated local translation and scale. Rest to each target; not a produced animation.',
  'maximumMinimumRequiredStretchRatio':max(r['maximumMinimumRequiredStretchRatio'] for r in rows),
  'infeasibleEdgeCases':sum(r['infeasibleEdges'] for r in rows),'actualInsidePinCases':sum(len(r['actualInsidePins']) for r in rows),'rows':rows}
(OUT/('actual-intermediate-'+str(4*args.steps)+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2),flush=True)
