import sys, json, base64, importlib.util, urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('client',ROOT/'Tools/MeshyRuns/PlayerV2/intake.py');client=importlib.util.module_from_spec(spec);spec.loader.exec_module(client)
OUT=ROOT/'Art/PlayerPhase1';MAN=OUT/'generation_manifest.json'
data=json.loads(MAN.read_text(encoding='utf-8')) if MAN.exists() else {'tasks':[],'balance_start':client.call('GET','/openapi/v1/balance')}
def save():client.save(MAN,data)
def summary():print(json.dumps([{k:t.get(k) for k in ['name','id','status','progress','consumed_credits']} for t in data['tasks']]))
action=sys.argv[1]
if action=='submit':
 name=sys.argv[2]
 if any(t['name']==name for t in data['tasks']):summary();sys.exit()
 counts={'Body':13000,'InnerTop':5000,'Durumagi':7000,'DurumagiRetry':14000};assert name in counts
 views=['front','side_facing_left','back','side_facing_right'] if name=='Body' else ['front','back']
 part='Durumagi' if name=='DurumagiRetry' else name
 paths=[OUT/'Source/Views'/part/(v+'.png') for v in views]
 config=dict(ai_model='meshy-7',should_remesh=True,topology='quad',target_polycount=counts[name],save_pre_remeshed_model=True,should_texture=True,enable_pbr=True,texture_resolution='2k',ultra_mode=False,image_enhancement=False,target_formats=['glb'])
 if name=='Body':config['pose_mode']='t-pose'
 if name=='DurumagiRetry':config['topology']='triangle'
 t=dict(name=name,endpoint='/openapi/v1/multi-image-to-3d',config=config,inputs=[str(p.relative_to(OUT)) for p in paths],status='SUBMISSION_RESERVED',estimated_credits=30)
 if name=='DurumagiRetry':t['retry_reason']='Initial quad remesh fragmented continuous cloth into 821 disconnected pieces; pre-remesh surface is intact. Switch to triangle remeshing, target 14000.'
 data['tasks'].append(t);save()
 urls=['data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode() for p in paths]
 r=client.call('POST',t['endpoint'],dict(config,image_urls=urls));t.update(id=r['result'],status='SUBMITTED');save()
elif action=='retexture':
 if any(t['name']=='DurumagiTextureRepair' for t in data['tasks']):summary();sys.exit()
 src=next(t for t in data['tasks'] if t['name']=='DurumagiRetry');assert src['status']=='SUCCEEDED'
 config=dict(input_task_id=src['id'],ai_model='meshy-7',enable_original_uv=False,enable_pbr=True,texture_resolution='2k',target_formats=['glb'])
 paths=[OUT/'Source/Views/Durumagi'/(v+'.png') for v in ['front','back']]
 t=dict(name='DurumagiTextureRepair',endpoint='/openapi/v1/retexture',config=config,status='SUBMISSION_RESERVED',estimated_credits=10,repair_reason='Triangle retry geometry intact but generated UV texture collapses into horizontal stripes; request fresh UV unwrap and same multiview texturing.')
 data['tasks'].append(t);save()
 urls=['data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode() for p in paths]
 r=client.call('POST',t['endpoint'],dict(config,multiview_image_urls=urls));t.update(id=r['result'],status='SUBMITTED');save()
elif action=='rig':
 if any(t['name']=='BodyRig' for t in data['tasks']):summary();sys.exit()
 body=next(t for t in data['tasks'] if t['name']=='Body');assert body['status']=='SUCCEEDED'
 config=dict(input_task_id=body['id'],height_meters=1.75)
 t=dict(name='BodyRig',endpoint='/openapi/v1/rigging',config=config,status='SUBMISSION_RESERVED',estimated_credits=5)
 data['tasks'].append(t);save()
 r=client.call('POST',t['endpoint'],config);t.update(id=r['result'],status='SUBMITTED');save()
elif action in ['poll','download']:
 for t in data['tasks']:
  if not t.get('id'):continue
  r=client.call('GET',t['endpoint']+'/'+t['id']);folder=OUT/'Source/Meshy'/t['name'];client.save(folder/'response.json',r)
  t.update({k:r[k] for k in ['status','progress','consumed_credits'] if k in r});save()
  if action=='download' and r['status']=='SUCCEEDED':
   urls={k+('.glb' if 'glb' in k else '.fbx'):v for k,v in r.get('model_urls',{}).items() if v and ('glb' in k or k=='fbx')}
   if t['name']=='BodyRig':
    result=r.get('result',{})
    urls.update({k.replace('_url','')+('.glb' if 'glb' in k else '.fbx'):v for k,v in result.items() if isinstance(v,str) and v and ('glb' in k or 'fbx' in k)})
    urls.update({k.replace('_url','')+('.glb' if 'glb' in k else '.fbx'):v for k,v in result.get('basic_animations',{}).items() if v})
   for i,maps in enumerate(r.get('texture_urls',[])):
    urls.update({f'T_{i}_{k}.png':v for k,v in maps.items() if v})
   for name,url in urls.items():
    dest=folder/name
    if not dest.exists():
     temp=dest.with_suffix(dest.suffix+'.part');urllib.request.urlretrieve(url,temp);temp.replace(dest)
 data['balance_latest']=client.call('GET','/openapi/v1/balance');save()
summary()
