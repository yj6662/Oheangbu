"""Rig the existing #234 human models; durable reservations and no paid retries."""
import sys,json,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('base',ROOT/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/'Art/World/Compact/Rebuild/Characters/Rig236'
ledger=m.OUT/'ledger.json'
endpoint='/openapi/v1/rigging'

def run(op,ident='logger'):
 data=json.loads(ledger.read_text(encoding='utf-8')) if ledger.exists() else {'self_imposed_cap':15,'scope':'Three existing humanoids only. One rig first; no new geometry or quadruped auto-rig requests. No automatic POST retries.','api_source':'https://docs.meshy.ai/en/api/rigging','pricing_source':'https://docs.meshy.ai/en/api/pricing','requests':[]}
 if op=='submit':
  if ident not in ('logger','herbalist','innkeeper'):raise RuntimeError('Humanoid pilot required')
  if any(r['id']==ident for r in data['requests']):raise RuntimeError('Existing reservation retained; no duplicate POST')
  if sum(r['reserved_credits'] for r in data['requests'])+5>data['self_imposed_cap']:raise RuntimeError('Run cap')
  old=json.loads((m.OUT.parent/'ledger.json').read_text(encoding='utf-8'))
  source=next(r for r in old['requests'] if r['name']==ident+'_refine' and r['status']=='SUCCEEDED')
  if m.api('GET','/openapi/v1/balance').get('balance',0)<5:raise RuntimeError('Insufficient existing balance; no purchase')
  row={'id':ident,'name':ident+'_rig','mode':'rig','config':{'input_task_id':source['task_id'],'height_meters':1.72},'reserved_credits':5,'created_at':m.now(),'status':'SUBMISSION_RESERVED'}
  data['requests'].append(row);m.save(ledger,data)
  try:row.update(task_id=m.api('POST',endpoint,row['config'])['result'],status='SUBMITTED')
  except Exception:
   row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';m.save(ledger,data);raise
  m.save(ledger,data)
 elif op=='poll':
  for row in data['requests']:
   if not row.get('task_id') or (row.get('status')=='SUCCEEDED' and row.get('files')):continue
   response=m.api('GET',endpoint+'/'+row['task_id']);m.save(m.OUT/'.private'/(row['name']+'.json'),response)
   for k in ('status','progress','consumed_credits'):row[k]=response.get(k)
   m.save(ledger,data)
   if row['status']=='SUCCEEDED':row['files']=m.download_files(response,row);m.save(ledger,data)
 print(json.dumps([{k:r.get(k) for k in ('name','task_id','status','progress','consumed_credits')} for r in data['requests']]))

if __name__=='__main__':
 try:run(sys.argv[1],sys.argv[2] if len(sys.argv)>2 else 'logger')
 except Exception as e:
  print(str(e) if isinstance(e,RuntimeError) else type(e).__name__);sys.exit(1)
