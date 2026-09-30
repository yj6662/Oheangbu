"""Approved concept driven Meshy workflow. Durable reservations; no paid retries."""
import base64, importlib.util, json, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('meshy_base',ROOT/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/'Art/Characters/Principal257'; LEDGER=m.OUT/'ledger.json'

def run(op, ident=None, note=None):
 d=json.loads(LEDGER.read_text(encoding='utf-8')) if LEDGER.exists() else dict(model='meshy-7.1',self_imposed_cap=40,automatic_post_retries=False,pricing_source='https://docs.meshy.ai/en/api/pricing',requests=[])
 if op in ('geometry','texture','rig','rig_local'):
  a=json.loads((m.OUT/'approval.json').read_text(encoding='utf-8'))
  if ident != 'wangso' or not a['generation_authorization_by_character'].get(ident): raise RuntimeError('User art approval required')
  c=a['concept_images'][ident]
  if m.sha(m.OUT/c['path'])!=c['sha256']:raise RuntimeError('Approved image changed')
  if any(r['name']==ident+'_'+op for r in d['requests']):raise RuntimeError('Existing reservation retained; no POST retry')
  image=m.OUT/(ident+'_input.png');cfg={};endpoint='/openapi/v1/image-to-3d'
  if op=='geometry':
   cfg=dict(ai_model='meshy-7.1',geometry_resolution='4k',should_texture=False,should_remesh=False,pose_mode='a-pose',image_enhancement=False,target_formats=['fbx','glb'])
   field='image_url';cost=25
  else:
   prev=next(r for r in d['requests'] if r['name']==ident+('_geometry' if op=='texture' else '_texture'))
   if prev['status']!='SUCCEEDED' or not prev.get('visual_review'):raise RuntimeError('Downloaded preceding stage must be visually reviewed')
   if op=='texture':
    endpoint='/openapi/v1/retexture';cfg=dict(input_task_id=prev['task_id'],ai_model='meshy-7',enable_original_uv=True,enable_pbr=True,texture_resolution='4k',target_formats=['fbx','glb']);field='image_style_url';cost=10
   else:
    endpoint='/openapi/v1/rigging';cfg=dict(height_meters=1.68);field=None;cost=5
    if op=='rig_local':
     image=m.OUT/'wangso/rig_input/Wangso.glb';field='model_url'
     if m.glb_stats(image)['unique_mesh_triangles']>300000:raise RuntimeError('Local mesh exceeds rig face cap')
    else:cfg['input_task_id']=prev['task_id']
  if sum(r['reserved_credits'] for r in d['requests'])+cost>d['self_imposed_cap']:raise RuntimeError('Self-imposed run cap reached')
  if m.api('GET','/openapi/v1/balance').get('balance',0)<cost:raise RuntimeError('Insufficient existing credits; no purchase')
  row=dict(name=ident+'_'+op,id=ident,mode='rig' if op=='rig_local' else op,endpoint=endpoint,config=cfg.copy(),approved_concept=c,input_image=str(image.relative_to(m.OUT)),input_sha256=m.sha(image),reserved_credits=cost,status='SUBMISSION_RESERVED',created_at=m.now())
  d['requests'].append(row);m.save(LEDGER,d)
  payload=cfg.copy()
  if field:payload[field]=('data:model/gltf-binary;base64,' if op=='rig_local' else 'data:image/png;base64,')+base64.b64encode(image.read_bytes()).decode()
  try:row.update(task_id=m.api('POST',endpoint,payload)['result'],status='SUBMITTED')
  except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';m.save(LEDGER,d);raise
  m.save(LEDGER,d)
 elif op=='review':
  row=next(r for r in d['requests'] if r['name']==ident)
  if row['status']!='SUCCEEDED' or not row.get('files') or not note:raise RuntimeError('Completed downloads and explicit review note required')
  row['visual_review']=dict(note=note,at=m.now(),reviewer='assistant technical review, not user final 3D art approval');m.save(LEDGER,d)
 elif op=='poll':
  for row in d['requests']:
   if not row.get('task_id') or row.get('files') or row['status'] in ('FAILED','CANCELED'):continue
   result=m.api('GET',row['endpoint']+'/'+row['task_id']);m.save(m.OUT/'.private'/(row['name']+'.json'),result)
   for k in ('status','progress','consumed_credits'):row[k]=result.get(k)
   m.save(LEDGER,d)
   if row['status']=='SUCCEEDED':row['files']=m.download_files(result,row);m.save(LEDGER,d)
 print(json.dumps([{k:r.get(k) for k in ('name','status','progress','consumed_credits')} for r in d['requests']]))
if __name__=='__main__':
 try:run(*sys.argv[1:])
 except Exception as e:print(str(e) if isinstance(e,RuntimeError) else type(e).__name__);sys.exit(1)
