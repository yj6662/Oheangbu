import bpy, json, hashlib, importlib.util
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/PlayerPhase1/PlaytestReRig'
for d in ['Work','Validation','Images','Textures','Exports']:(OUT/d).mkdir(parents=True,exist_ok=True)
src=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Final/Integrated_B2_C3.blend'
report={'source':str(src),'source_sha256':hashlib.sha256(src.read_bytes()).hexdigest()}
bpy.ops.wm.open_mainfile(filepath=str(src))
report['objects']=[{'name':o.name,'type':o.type,'matrix':[list(r) for r in o.matrix_world],'dimensions':list(o.dimensions),'vertices':len(o.data.vertices) if o.type=='MESH' else 0,'modifiers':[(m.name,m.type) for m in o.modifiers]} for o in bpy.data.objects]
for o in bpy.data.objects:
 if o.type=='ARMATURE':
  report['rig']=o.name
  report['bones']=[{'name':b.name,'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None,'matrix':[list(r) for r in b.matrix_local]} for b in o.data.bones]
  o.animation_data_clear()
  for p in o.pose.bones:p.matrix_basis.identity()
report['actions']=[{'name':a.name,'range':list(a.frame_range)} for a in bpy.data.actions]
(OUT/'Validation/source_inspection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Work/Player_ReRig_Work.blend'))
print('PLAYTEST_RERIG_READY',flush=True)
