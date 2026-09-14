import bpy,os,json,math
from mathutils import Vector
ROOT='C:/Users/yj666/Oheangbu';DIR=ROOT+'/Art/Player/MeshySources';LEGACY='C:/Users/yj666/MandateOfInk/MandateOfInk/Assets/_Project/Art/Models/Player'
s=bpy.data.scenes.get('Dosa_Motion_Source') or bpy.data.scenes.new('Dosa_Motion_Source');bpy.context.window.scene=s
sources={name:LEGACY+'/A_DosaCourier_'+source+'.fbx' for name,source in [('Idle','Idle'),('CombatReady','CombatReady'),('WalkForward','Walk'),('RunForward','Run')]}
sources.update({n:DIR+'/'+n+'.fbx' for n in ['WalkBackSource','WalkLeftSource','RunBackSource','RunLeftSource','DodgeSource156','DodgeSource157','DodgeSource162','DodgeSource164']})
report={}
for name,path in sources.items():
    arm=next((o for o in s.objects if o.type=='ARMATURE' and o.get('dosa_motion_name')==name),None)
    if not arm:
        before=set(s.objects);bpy.ops.import_scene.fbx(filepath=path);added=set(s.objects)-before;arm=next(o for o in added if o.type=='ARMATURE');arm['dosa_motion_name']=name
        arm.name='MotionRig_'+name
        for o in added:o.hide_render=True
    action=arm.animation_data.action;start,end=[int(x) for x in action.frame_range];fps=s.render.fps/s.render.fps_base
    hips=[];feet={'Left':[],'Right':[]};yaw=[]
    for f in range(start,end+1):
        s.frame_set(f);hips.append(list(arm.matrix_world@arm.pose.bones['Hips'].head))
        for side in feet:feet[side].append(list(arm.matrix_world@arm.pose.bones[side+'Foot'].head))
        d=(arm.matrix_world@arm.pose.bones['RightShoulder'].head)-(arm.matrix_world@arm.pose.bones['LeftShoulder'].head)
        yaw.append(math.atan2(d.y,d.x))
    report[name]={'path':path,'armature':arm.name,'action':action.name,'frames':[start,end],'fps':fps,'duration':(end-start)/fps,
        'hip_start':hips[0],'hip_end':hips[-1],'hip_displacement':[hips[-1][i]-hips[0][i] for i in range(3)],
        'hip_range':[[min(p[i] for p in hips),max(p[i] for p in hips)] for i in range(3)],'hips':hips,'feet':feet,'yaw':yaw}
    print(name,'frames',start,end,'fps',fps,'delta',[round(v,3) for v in report[name]['hip_displacement']],'range',[[round(v,3) for v in a] for a in report[name]['hip_range']])
open(ROOT+'/Art/Player/motion-source-report.json','w',encoding='utf-8').write(json.dumps(report,indent=2))
bpy.context.window.scene=bpy.data.scenes['Dosa_Player_Workshop']
