"""Dosa motion intake. Reads only the configured project Meshy key; never prints it.
All tasks and charged operations are explicitly recorded in local provenance.
"""
import argparse, json, os, pathlib, urllib.request, urllib.error, datetime, base64

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art' / 'Player' / 'MeshySources'
OUT.mkdir(parents=True, exist_ok=True)
KEY = next(line.split('=',1)[1].strip() for line in (ROOT/'.env').read_text(encoding='utf-8-sig').splitlines() if line.startswith('MESHY_API_KEY='))
BASE = 'https://api.meshy.ai'

def call(method,path,payload=None):
    data=json.dumps(payload).encode() if payload is not None else None
    req=urllib.request.Request(BASE+path,data=data,method=method,headers={'Authorization':'Bearer '+KEY,'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=120) as r:return json.load(r)
    except urllib.error.HTTPError as e:
        raw=e.read().decode('utf-8','replace')
        raise RuntimeError('HTTP '+str(e.code)+' '+raw[:1200]) from None

def save(name,value):
    (OUT/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

p=argparse.ArgumentParser();p.add_argument('action',choices=['probe','balance-final','library','rig','rig-poll','create','motion','motion-poll','apply-motion','poll','download']);p.add_argument('--task');p.add_argument('--id',type=int);p.add_argument('--name');p.add_argument('--motion-task');args=p.parse_args()
if args.action=='balance-final':
    bal=call('GET','/openapi/v1/balance');save('balance-final.json',bal);print('balance',json.dumps(bal))
if args.action=='probe':
    bal=call('GET','/openapi/v1/balance');save('balance-start.json',bal);print('balance',json.dumps(bal))
    try:
        rig=call('GET','/openapi/v1/rigging/'+args.task);save('rig-status.json',rig);print('rig',rig.get('id'),rig.get('status'))
    except RuntimeError as e:print('rig unavailable',str(e))
elif args.action=='library':
    lib=call('GET','/openapi/v1/animations/library');save('animation-library.json',lib)
    arr=lib if isinstance(lib,list) else lib.get('result',lib.get('animations',[]))
    print(json.dumps([{k:x.get(k) for k in ('action_id','name','key','category')} for x in arr if any(w in str(x).lower() for w in ['strafe','sidestep','side_step','dodge','backward','left_run','right_run','left_walk','right_walk','back_run'])],indent=2))
elif args.action=='rig':
    manifestpath=OUT/'tasks.json';manifest=json.loads(manifestpath.read_text()) if manifestpath.exists() else []
    if any(t.get('type')=='rig' for t in manifest):raise RuntimeError('Rig task already submitted; poll instead')
    uri='data:model/gltf-binary;base64,'+base64.b64encode((OUT/'Dosa_Source_Upload.glb').read_bytes()).decode()
    result=call('POST','/openapi/v1/rigging',{'model_url':uri,'height_meters':1.75})
    entry={'name':'Dosa_RigRefresh','type':'rig','task_id':result['result'],'expected_credits':5,'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
    manifest.append(entry);save('tasks.json',manifest);print(json.dumps(entry))
elif args.action=='rig-poll':
    rig=call('GET','/openapi/v1/rigging/'+args.task);save('rig-refreshed-response.json',rig);print('rig',rig.get('status'),rig.get('progress'))
    if rig.get('status')=='SUCCEEDED':
        dest=OUT/'Dosa_RigRefreshed.fbx';urllib.request.urlretrieve(rig['result']['rigged_character_fbx_url'],dest);print('saved',dest.name)
elif args.action in ('create','motion','apply-motion'):
    manifestpath=OUT/'tasks.json';manifest=json.loads(manifestpath.read_text()) if manifestpath.exists() else []
    if sum(t['expected_credits'] for t in manifest)+3>45:raise RuntimeError('45-credit cap would be exceeded')
    if any(t['name']==args.name for t in manifest):raise RuntimeError('Named task already exists; poll it instead')
    if args.action=='motion':
        prompts={'RunBackMotion':'A man jogs backward in a straight line while his chest and face keep pointing forward. Continuous athletic backward running cycle with alternating legs, bent knees, short quick backward steps and natural balanced arm swing. No turn, no spin, no jump, no fall. Steady rhythmic game locomotion.',
                 'RunLeftMotion':'A man performs continuous athletic lateral shuffle jogging toward his own left. His chest and face keep pointing forward throughout. Feet remain uncrossed; left foot leads sideways then right foot follows. Repeating bent-knee lateral running cycle with natural arm balance. No turn, no spin, no forward movement.'}
        payload={'prompt':prompts[args.name],'mode':'swift','duration':4};result=call('POST','/openapi/v1/text-to-motion',payload)
    else:
        payload={'rig_task_id':args.task}
        payload.update({'motion_task_id':args.motion_task} if args.action=='apply-motion' else {'action_id':args.id})
        result=call('POST','/openapi/v1/animations',payload)
    entry={'name':args.name,'type':args.action,'request':payload,'task_id':result['result'],'expected_credits':3,'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
    manifest.append(entry);save('tasks.json',manifest);print(json.dumps(entry))
elif args.action=='motion-poll':
    data=call('GET','/openapi/v1/text-to-motion/'+args.task);save(args.name+'-response.json',data);print(args.name,data.get('status'),data.get('progress'))
    if data.get('status')=='SUCCEEDED':
        result=data['result'];dest=OUT/(args.name+'.'+result['motion_format']);urllib.request.urlretrieve(result['motion_url'],dest);print('saved',dest.name)
elif args.action in ('poll','download'):
    data=call('GET','/openapi/v1/animations/'+args.task);save(args.name+'-response.json',data);print(args.name,data.get('status'),data.get('progress'))
    if args.action=='download' and data.get('status')=='SUCCEEDED':
        result=data['result'];isfbx=bool(result.get('animation_fbx_url'));url=result['animation_fbx_url'] if isfbx else result['animation_glb_url'];dest=OUT/(args.name+('.fbx' if isfbx else '.glb'));urllib.request.urlretrieve(url,dest);print('saved',dest.name,dest.stat().st_size)
