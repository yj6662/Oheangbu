"""One image-to-3D request per summon; second only after first delivery completes."""
import argparse,base64,json,sys
from pathlib import Path
import run_stone_dokkaebi as common
R=common.ROOT;ROOT=R/'Art/SpellVFX120'
SPECS={'DokkaebiClub':('064_BAB8','몸'),'WaterTurtle':('112_C634','옴')}
p=argparse.ArgumentParser();p.add_argument('name',choices=SPECS);p.add_argument('op',choices=['submit','poll']);a=p.parse_args()
O=ROOT/a.name;M=O/'Meshy';M.mkdir(parents=True,exist_ok=True);(M/'.gitignore').write_text('.private/\n');common.OUT=M;ledger=M/'ledger.json'
try:
 if a.op=='submit':
  if a.name=='WaterTurtle' and not (ROOT/'DokkaebiClub/DONE.json').exists():raise RuntimeError('Finish dokkaebi before turtle generation')
  if ledger.exists():raise RuntimeError('Existing request retained; poll it, do not resubmit')
  image=O/'Concept.png';assert image.exists()
  cfg=dict(ai_model='meshy-7',model_type='standard',ultra_mode=True,should_texture=True,enable_pbr=True,texture_resolution='2k',should_remesh=True,topology='triangle',target_polycount=16000,image_enhancement=False,target_formats=['glb','fbx'])
  balance=common.api('GET','/openapi/v1/balance').get('balance',0)
  if balance<35:raise RuntimeError('Insufficient existing credit balance')
  row=dict(name=a.name,id=SPECS[a.name][0],glyph=SPECS[a.name][1],mode='image',config=cfg,input_file='Concept.png',input_sha256=common.sha(image),expected_credits=35,reserved_credits=35,consumed_credits=None,status='SUBMISSION_RESERVED')
  value=dict(self_imposed_run_cap=35,combined_two_summon_cap=70,balance_before=balance,price_source='https://docs.meshy.ai/en/api/pricing',endpoint='/openapi/v1/image-to-3d',input_policy='Only generated production concept uploaded; source research images remain local.',requests=[row]);common.save(ledger,value)
  try:response=common.api('POST',value['endpoint'],dict(cfg,image_url='data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode()))
  except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';common.save(ledger,value);raise
  row.update(task_id=response['result'],status='SUBMITTED');common.save(ledger,value)
 else:
  value=json.loads(ledger.read_text());row=value['requests'][0]
  response=common.api('GET',value['endpoint']+'/'+row['task_id']);common.save(M/'.private'/'response.json',response)
  for k in ['status','progress','consumed_credits']:
   if k in response:row[k]=response[k]
  common.save(ledger,value)
  if row['status']=='SUCCEEDED':row['files']=common.download_files(response,row)
  value['balance_after']=common.api('GET','/openapi/v1/balance').get('balance');common.save(ledger,value)
 print(json.dumps({k:row.get(k) for k in ['name','task_id','status','progress','consumed_credits']},ensure_ascii=False))
except Exception as e:
 print('STOPPED: '+(str(e) if isinstance(e,RuntimeError) else type(e).__name__),file=sys.stderr);sys.exit(1)
