import bpy,json
bpy.ops.wm.open_mainfile(filepath='C:/Users/yj666/Oheangbu/Art/PlayerPhase1/PlaytestReRig/Work/Player_C02_ReRig.blend')
for o in bpy.data.objects:
 if o.type=='MESH':
  print(json.dumps({'name':o.name,'modifiers':[{'type':m.type,'preserve_volume':m.use_deform_preserve_volume if m.type=='ARMATURE' else None} for m in o.modifiers]}),flush=True)
