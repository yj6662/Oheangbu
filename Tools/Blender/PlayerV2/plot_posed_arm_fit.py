"""Exact anatomy triangle sections versus baseline/posed capsule sections."""
import ast,json,math
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import LineCollection
ROOT=Path(__file__).resolve().parents[3]
DIR=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
helper=Path(__file__).with_name('fit_posed_arm_capsules.py')
node=next(n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='capsule_distances')
exec(compile(ast.Module(body=[node],type_ignores=[]),str(helper),'exec'))
topology=json.loads((DIR/'topology-rest.json').read_text())
report=json.loads((DIR/'posed-arm-candidate-report.json').read_text())
old=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/anatomy-capsules-final-9f4cd412.json').read_text(encoding='utf-8-sig'))['capsules']
fig,axes=plt.subplots(1,3,figsize=(17,6),layout='constrained')
cases=[('fixture_grip_settle','Left',53),('open_hand','Right',746),('combined_reach','Left',None)]
for ax,(poseid,side,index) in zip(axes,cases):
    pose=json.loads((DIR/(poseid+'.json')).read_text());lining='DosaV2_ArmLining_'+side;cloth='DosaV2_SleeveOuter_'+side[0]
    candidate=next(c for c in report['candidates'] if c['capsulesPerArm']==3)
    row=next(r for r in candidate['poses'] if r['poseId']==poseid and r['lining']==lining)
    if index is None:index=max(row['pinnedRecords'],key=lambda r:r['depthMeters'])['sourceVertex']
    point=np.array(pose['vertices'][cloth][index]);z=point[2]
    vertices=np.array(pose['vertices'][lining]);triangles=topology['topology'][lining]['triangles'];segments=[]
    for tri in triangles:
        points=vertices[tri];crossings=[]
        for i in range(3):
            a,b=points[i],points[(i+1)%3]
            if (a[2]-z)*(b[2]-z)<0:
                t=(z-a[2])/(b[2]-a[2]);crossings.append((a+t*(b-a))[:2])
        if len(crossings)==2:segments.append(crossings)
    prior=[]
    for cap in old:
        if 'ArmLining_'+side+'_' not in cap['name']:continue
        bone=cap['anchorBone'];matrix=np.array(pose['bonePoseMatrices'][bone])@np.linalg.inv(np.array(topology['boneRestMatrices'][bone]))
        cap=dict(cap)
        for field in ['startBlender','endBlender']:cap[field]=(matrix@np.r_[cap[field],1])[:3].tolist()
        prior.append(cap)
    x=np.linspace(point[0]-.085,point[0]+.085,320);y=np.linspace(point[1]-.075,point[1]+.075,320);xx,yy=np.meshgrid(x,y)
    grid=np.column_stack((xx.ravel(),yy.ravel(),np.full(xx.size,z)))
    for caps,color,style in [(prior,'#db7618','--'),(row['capsules'],'#2476bb','-')]:
        field=np.min(np.array([capsule_distances(grid,c) for c in caps]),axis=0).reshape(xx.shape)
        ax.contour(xx,yy,field,levels=[0],colors=[color],linestyles=[style],linewidths=1.6)
    ax.add_collection(LineCollection(segments,colors='#242424',linewidths=2.2))
    ax.scatter([point[0]],[point[1]],color='#cf2649',marker='*',s=150,zorder=6)
    ax.set(xlim=(x[0],x[-1]),ylim=(y[0],y[-1]),aspect='equal',xlabel='Blender X (m)',ylabel='Blender Y (m)',
           title=f'{poseid} / {side}\nExact pin {index}; section Z={z:.4f} m')
    ax.grid(alpha=.2)
fig.suptitle('Actual ArmLining section (black), rigid baseline (orange dashed), posed 3-caps fit (blue), pin (red)\nAnatomy-only fit: grip proxy conflict resolves; lowered-arm actual geometry conflict remains',fontsize=13)
fig.savefig(DIR/'posed-arm-fit-exact-sections.png',dpi=135)
print(DIR/'posed-arm-fit-exact-sections.png')
