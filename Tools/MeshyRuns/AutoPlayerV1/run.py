"""Resumable paid requests. Never retries POST. Public ledgers contain no URLs/keys."""
import argparse, base64, datetime, hashlib, json, shutil, sys, urllib.request, urllib.error
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerPhase1/AutoPlayerV1'
BASE='https://api.meshy.ai'
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def save(path,value):
 path.parent.mkdir(parents=True,exist_ok=True)
 tmp=path.with_suffix(path.suffix+'.writing');tmp.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8');tmp.replace(path)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(name): return json.loads((OUT/name).read_text(encoding='utf-8'))
def api(method,endpoint,payload=None):
 key=next(s.split('=',1)[1].strip().strip('"').strip("'") for s in (ROOT/'.env').read_text(encoding='utf-8-sig').splitlines() if s.startswith('MESHY_API_KEY='))
 req=urllib.request.Request(BASE+endpoint,method=method,data=json.dumps(payload).encode() if payload is not None else None,headers={'Authorization':'Bearer '+key,'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=60) as r:return json.load(r)
 except urllib.error.HTTPError as e: raise RuntimeError('Meshy HTTP '+str(e.code)) from None
def used(ledger):return sum(t.get('consumed_credits') if t.get('consumed_credits') is not None else t['reserved_upper_credits'] for t in ledger['requests'])
def init():
 OUT.mkdir(parents=True,exist_ok=True)
 if (OUT/'run_state.json').exists():return status()
 source=Path('C:/Users/yj666/Desktop/PROMPT-Codex-Meshy7-Autonomous-Player.md')
 (OUT/'Source').mkdir(exist_ok=True);shutil.copy2(source,OUT/'Source/production-instructions.md')
 protected=[ROOT/'Art/PlayerPhase1/FitRigV3/Dosa_Phase1_FitRigV3.blend',ROOT/'Art/PlayerPhase1/FitRigV3/Before.blend',ROOT/'Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab',ROOT/'Oheangbu/Assets/_Project/Scenes/Dev/C2_CodexWorld.unity']
 balance=api('GET','/openapi/v1/balance')
 save(OUT/'preservation_before.json',{str(p.relative_to(ROOT)):{'sha256':sha(p),'bytes':p.stat().st_size} for p in protected if p.exists()})
 save(OUT/'run_state.json',{'version':1,'started_at':now(),'phase':'INPUT_PREPARATION','status':'RUNNING','project_root':str(ROOT),'unity_project':str(ROOT/'Oheangbu'),'max_candidates':3,'max_repairs_per_candidate':2,'budget_credits':200,'candidates':[],'selected_candidate':None,'canonical_player_replaced':False,'wardrobe_swap':'NOT_IMPLEMENTED','cloth_physics':'NOT_IMPLEMENTED','facial_rig':'NOT_IMPLEMENTED'})
 save(OUT/'cost_ledger.json',{'budget_credits':200,'budget_basis':'Current instructions default 300; apply lower previously explicit 200 as conservative cap for this new run. Historical V2=95 and Phase1=135 are separate runs, not new charges.','balance_start':balance,'requests':[],'price_source':'https://docs.meshy.ai/en/api/pricing','price_checked_at':now(),'prices':{'meshy7_2k_texture':30,'generation_reserved_upper':35,'rigging':5,'animation':3},'external_image_service':[]})
 save(OUT/'source_manifest.json',{'instructions_sha256':sha(source),'references':[],'inputs':[],'outputs':[]})
 (OUT/'.gitignore').write_text('.private/\n*.writing\n',encoding='utf-8')
 status()
def status():
 l=read('cost_ledger.json');print(json.dumps({'budget':l['budget_credits'],'spent_or_reserved':used(l),'tasks':[{k:t.get(k) for k in ['name','task_id','status','progress','consumed_credits','reserved_upper_credits']} for t in l['requests']]},ensure_ascii=False))
def submit(name,endpoint,config,upper,inputs=None,kind=None):
 l=read('cost_ledger.json');s=read('run_state.json')
 old=next((t for t in l['requests'] if t['name']==name),None)
 if old:
  print('Existing request retained; poll/reconcile it. No POST repeated.');return status()
 if s.get('paid_requests_stopped'):raise RuntimeError('This run is closed to further paid requests; existing results remain readable')
 if kind=='character' and s.get('selected_candidate'):raise RuntimeError('A selected candidate already exists; no unnecessary new character')
 if used(l)+upper>l['budget_credits']:raise RuntimeError('Budget including in-flight reservations exceeded')
 if kind=='character' and len(s['candidates'])>=s['max_candidates']:raise RuntimeError('Candidate limit reached')
 balance=api('GET','/openapi/v1/balance')
 if balance.get('balance',0)<upper:raise RuntimeError('Insufficient prepaid API balance; no purchase attempted')
 hashes={str(p.relative_to(OUT)):sha(p) for p in inputs or []}
 setting_hash=hashlib.sha256(json.dumps({'config':config,'inputs':hashes},sort_keys=True).encode()).hexdigest()
 t={'name':name,'kind':kind,'endpoint':endpoint,'config':config,'settings_sha256':setting_hash,'inputs':hashes,'created_at':now(),'status':'SUBMISSION_RESERVED','expected_credits':30 if kind=='character' else upper,'reserved_upper_credits':upper,'consumed_credits':None}
 l['requests'].append(t);save(OUT/'cost_ledger.json',l)
 if kind=='character':
  s['candidates'].append({'id':name,'corrections_used':0,'status':'GENERATING'});s['phase']='GENERATION';save(OUT/'run_state.json',s)
 payload=dict(config)
 if inputs:payload['image_url']='data:image/png;base64,'+base64.b64encode(inputs[0].read_bytes()).decode()
 try:r=api('POST',endpoint,payload)
 except Exception:
  t['status']='SUBMISSION_UNCERTAIN';save(OUT/'cost_ledger.json',l);raise
 t.update(task_id=r['result'],status='SUBMITTED');save(OUT/'cost_ledger.json',l);status()
def poll(name,download=False):
 l=read('cost_ledger.json');t=next(t for t in l['requests'] if t['name']==name)
 if not t.get('task_id'):raise RuntimeError('Uncertain request without ID: reconcile remote list before any new POST')
 r=api('GET',t['endpoint']+'/'+t['task_id']);save(OUT/'.private'/(name+'.json'),r)
 for k in ['status','progress','consumed_credits']:
  if k in r:t[k]=r[k]
 t['polled_at']=now();save(OUT/'cost_ledger.json',l)
 if download and r['status']=='SUCCEEDED':
  dest=OUT/'Candidates'/name/'Source';dest.mkdir(parents=True,exist_ok=True);downloads=[]
  def collect(x,prefix=''):
   if isinstance(x,dict):
    for k,v in x.items():collect(v,prefix+'_'+k if prefix else k)
   elif isinstance(x,list):
    for i,v in enumerate(x):collect(v,prefix+'_'+str(i))
   elif isinstance(x,str) and x.startswith('https://'):
    from urllib.parse import urlsplit
    ext=Path(urlsplit(x).path).suffix.lower()
    if ext not in ['.glb','.fbx','.png','.jpg','.jpeg','.mp4','.zip']:return
    p=dest/(prefix+ext)
    if not p.exists():
     tmp=p.with_suffix(p.suffix+'.part');urllib.request.urlretrieve(x,tmp);tmp.replace(p)
    downloads.append({'key':prefix,'path':str(p.relative_to(OUT)),'sha256':sha(p),'bytes':p.stat().st_size})
  collect(r)
  save(dest/'manifest.json',{'task_id':t['task_id'],'kind':t['kind'],'files':downloads,'config':t['config'],'consumed_credits':t.get('consumed_credits')})
  t['downloads']=downloads;save(OUT/'cost_ledger.json',l)
 status()
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('op',choices=['init','status','character','rig','animation','poll','download']);p.add_argument('name',nargs='?');p.add_argument('--source');p.add_argument('--action-id',type=int);a=p.parse_args()
 if a.op=='init':init()
 elif a.op=='status':status()
 elif a.op=='character':
  config=dict(model_type='standard',ai_model='meshy-7',should_texture=True,enable_pbr=True,texture_resolution='2k',ultra_mode=False,should_remesh=True,topology='triangle',target_polycount=50000,save_pre_remeshed_model=True,pose_mode='t-pose',image_enhancement=False,target_formats=['glb'],multi_view_thumbnails=True)
  submit(a.name,'/openapi/v1/image-to-3d',config,35,[OUT/'Source/production_front.png'],'character')
 elif a.op=='rig':
  t=next(t for t in read('cost_ledger.json')['requests'] if t['name']==a.source);assert t['status']=='SUCCEEDED';submit(a.name,'/openapi/v1/rigging',{'input_task_id':t['task_id'],'height_meters':1.75},5,kind='rigging')
 elif a.op=='animation':
  t=next(t for t in read('cost_ledger.json')['requests'] if t['name']==a.source);assert t['status']=='SUCCEEDED';assert a.action_id is not None;submit(a.name,'/openapi/v1/animations',{'rig_task_id':t['task_id'],'action_id':a.action_id},3,kind='animation')
 else:poll(a.name,a.op=='download')
