"""Approved Sinmok generation. Persist a reservation before POST; never retry uncertain POSTs."""
import base64, importlib.util, json, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('meshy_base',ROOT/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/'Art/Characters/Sinmok272'; LEDGER=m.OUT/'meshy-ledger.json'
def run(op):
 d=json.loads(LEDGER.read_text(encoding='utf-8')) if LEDGER.exists() else dict(model='meshy-7.1',self_imposed_cap=35,automatic_post_retries=False,pricing_source='https://docs.meshy.ai/en/api/pricing',requests=[])
 if op=='submit':
  a=json.loads((m.OUT/'approval.json').read_text(encoding='utf-8')); image=m.OUT/a['image']
  if not a['approved'] or m.sha(image)!=a['sha256']:raise RuntimeError('Approved image mismatch')
  if d['requests']:raise RuntimeError('Reservation already exists; inspect status, no POST retry')
  cost=35
  if m.api('GET','/openapi/v1/balance').get('balance',0)<cost:raise RuntimeError('Insufficient existing credits; no purchase')
  cfg=dict(ai_model='meshy-7.1',geometry_resolution='4k',should_texture=True,enable_pbr=True,texture_resolution='4k',should_remesh=True,target_polycount=100000,topology='triangle',save_pre_remeshed_model=True,pose_mode='',image_enhancement=False,target_formats=['fbx','glb'])
  row=dict(name='sinmok_model',id='sinmok',mode='model',endpoint='/openapi/v1/image-to-3d',config=cfg,approved_image=a['image'],input_sha256=m.sha(image),reserved_credits=cost,status='SUBMISSION_RESERVED',created_at=m.now())
  d['requests'].append(row);m.save(LEDGER,d)
  payload=cfg.copy();payload['image_url']='data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode()
  try:row.update(task_id=m.api('POST',row['endpoint'],payload)['result'],status='SUBMITTED')
  except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';m.save(LEDGER,d);raise
  m.save(LEDGER,d)
 elif op=='poll':
  for row in d['requests']:
   if not row.get('task_id') or row.get('files') or row['status'] in ('FAILED','CANCELED'):continue
   r=m.api('GET',row['endpoint']+'/'+row['task_id']);m.save(m.OUT/'.private'/row['name'],r)
   for k in ('status','progress','consumed_credits'):row[k]=r.get(k)
   m.save(LEDGER,d)
   if row['status']=='SUCCEEDED':row['files']=m.download_files(r,row);m.save(LEDGER,d)
 print(json.dumps([{k:r.get(k) for k in ('name','status','progress','consumed_credits')} for r in d['requests']]))
if __name__=='__main__':
 try:run(sys.argv[1])
 except Exception as e:print(str(e) if isinstance(e,RuntimeError) else type(e).__name__);sys.exit(1)
