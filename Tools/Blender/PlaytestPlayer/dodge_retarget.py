"""Derive five official Mixamo dodge/roll actions on the unchanged C02 bind."""
from pathlib import Path
base=Path(__file__).with_name('recovery_retarget.py').read_text(encoding='utf-8')
base=base.replace("O=R/'Art/PlaytestRecovery/Motion'","O=R/'Art/PlaytestRecovery/DodgeTurn/Motion'")
a=base.index('specs=');b=base.index('def frame_of',a)
base=base[:a]+"specs=[('DodgeForward','Standing Dodge Forward',None),('DodgeBack','Standing Dodge Backward',None),('DodgeLeft','Standing Dodge Left',None),('DodgeRight','Standing Dodge Right',None),('CrouchRoll','Stand To Roll',(.5,1.8))]\n"+base[b:]
base=base.replace("Art/PlaytestRecovery/MotionSources","Art/PlaytestRecovery/DodgeTurn/Sources")
# No source horizontal trajectory is applied twice. Motor owns all ground travel.
base=base.replace("hip=rest['Hips'].translation.copy()+Vector((delta.x,delta.y,0))","hip=rest['Hips'].translation.copy()")
# Feet rise over the head during a roll: their global minimum is not a stance metric.
base=base.replace("lift=-sorted(lows)[int((len(lows)-1)*.08)]","lift=-lows[0] if name=='CrouchRoll' else -sorted(lows)[int((len(lows)-1)*.08)]")
base=base.replace("assign(bpy.data.actions['PT_CrouchIdle'])","assign(bpy.data.actions['PT_CrouchRoll'])")
base=base.replace(" a=bpy.data.actions.new('PT_'+name)","""
 # A roll contacts shoulders/back as well as feet. Measure the actual unchanged
 # skinned C02 surface, including sleeves, instead of using the sole as its floor.
 clearance=[]
 skins=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
 for pose in poses:
  apply(pose); dg=bpy.context.evaluated_depsgraph_get(); low=1e9
  for skin in skins:
   ob=skin.evaluated_get(dg); mesh=ob.to_mesh()
   low=min(low,min((ob.matrix_world@v.co).z for v in mesh.vertices));ob.to_mesh_clear()
  clearance.append(max(0,.004-low))
 for i,pose in enumerate(poses):
  offset=max(clearance[max(0,i-2):min(len(poses),i+3)])
  for m in pose.values(): m.translation.z+=offset
 report.setdefault('surfaceClearance',{})[name]={'maximumLift':max(clearance),'method':'Evaluated full C02 skin minimum, 5-frame conservative envelope; no limb stretching'}
 a=bpy.data.actions.new('PT_'+name)""")
base=base.replace('Player_C02_AttachedMotions','Player_C02_DodgeRoll')
exec(compile(base,__file__,'exec'))
