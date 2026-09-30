"""Two principal NPCs. Durable Meshy 7.1 reservations; no automatic paid retries."""
import sys,json,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('meshy_base',ROOT/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/'Art/Characters/Principal255';ledger=m.OUT/'ledger.json'
common='Full body realistic Korean historical fantasy game character, natural anatomy. Symmetrical A-pose, straight elbows, open hands separated from hips, fingers distinct, feet apart. Empty hands. No floor, pedestal, text, weapon, glow or floating parts. '
PROMPTS={
 'wangso':common+'Adult Korean woman disguised as a practical travelling merchant. Composed observant face, sharp thoughtful eyes, hair in a low braided bun without ornaments. Plain fitted cross-collar jeogori, modest knee-length sleeveless travel coat split at the sides, loose divided travel trousers bound at the ankles, soft leather shoes. Narrow cloth sash, small flat cloth purse at waist. Calm upright posture, slender capable silhouette. No royal crown, armor, revealing clothes, oversized sleeves or large accessories.',
 'jeongdam':common+'Adult Korean male military official and travelling ritual practitioner. Weathered angular face, restrained short moustache, tied topknot with a simple dark headband. Structured cross-collar coat ending above knees, compact cloth shoulder layers, narrow sleeves with leather wrist bindings, straight roomy baji trousers, practical calf boots and plain belt. Disciplined broad-shouldered silhouette. No helmet, ornate armor, royal robe, enormous hat or shoulder weapons.'
}
TEXTURES={
 'wangso':'Worn matte Korean hemp and cotton. Muted walnut-brown sleeveless travel coat, faded warm ivory jacket, charcoal blue-grey trousers, dark brown shoes. Natural Korean skin and dark hair, quiet fabric seams and creases. Practical merchant, discreet wealth only in well-kept stitching. No lettering, glow, glossy plastic, painted outlines or saturated colors.',
 'jeongdam':'Worn matte dark indigo Korean military travelling coat, warm grey hemp underlayers, charcoal trousers, weathered dark brown leather boots and wrist wraps. Natural Korean skin and black hair with slight grey. Subtle stitched hems and restrained material grain. No lettering, gold embroidery, glow, plastic shine or painted outlines.'
}
def run(op,ident=None):
 data=json.loads(ledger.read_text()) if ledger.exists() else dict(model='meshy-7.1',self_imposed_cap=80,scope='Wangso and Jeongdam only; visual interpretation does not change narrative canon.',pricing_source='https://docs.meshy.ai/en/api/pricing',api_source='https://docs.meshy.ai/en/api/text-to-3d',requests=[])
 if op in ('preview','refine','rig','accept') and data.get('user_art_review',{}).get('status')=='REJECTED':raise RuntimeError('User rejected this art direction. Review approved concept images before replacement generation; this legacy generator is closed.')
 if op in ('preview','refine','rig'):
  if ident not in PROMPTS:raise RuntimeError('Choose principal NPC')
  name=ident+'_'+op
  if any(r['name']==name for r in data['requests']):raise RuntimeError('Prior reservation retained; no repeated paid POST')
  cfg=dict(mode=op,ai_model='meshy-7.1',target_formats=['fbx','glb']);endpoint=m.ENDPOINT
  if op=='preview':cfg.update(prompt=PROMPTS[ident],geometry_resolution='2k',pose_mode='a-pose',should_remesh=True,target_polycount=25000,topology='triangle')
  else:
   previous=next(r for r in data['requests'] if r['name']==ident+('_preview' if op=='refine' else '_refine'))
   if previous['status']!='SUCCEEDED':raise RuntimeError('Prior task incomplete')
   if op=='refine':
    if previous.get('geometry_review')!='ACCEPTED_FOR_REFINE':raise RuntimeError('Visual geometry review required')
    cfg.update(preview_task_id=previous['task_id'],texture_prompt=TEXTURES[ident],enable_pbr=True,texture_resolution='2k')
   else:cfg=dict(input_task_id=previous['task_id'],height_meters=1.68 if ident=='wangso' else 1.78);endpoint='/openapi/v1/rigging'
  cost={'preview':25,'refine':10,'rig':5}[op]
  if sum(r['reserved_credits'] for r in data['requests'])+cost>data['self_imposed_cap']:raise RuntimeError('Run cap reached')
  if m.api('GET','/openapi/v1/balance').get('balance',0)<cost:raise RuntimeError('Insufficient existing credits; no purchase')
  if len(PROMPTS[ident])>800:raise RuntimeError('Prompt too long')
  row=dict(id=ident,name=name,mode=op,endpoint=endpoint,config=cfg,reserved_credits=cost,status='SUBMISSION_RESERVED',created_at=m.now())
  data['requests'].append(row);m.save(ledger,data)
  try:row.update(task_id=m.api('POST',endpoint,cfg)['result'],status='SUBMITTED')
  except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';m.save(ledger,data);raise
  m.save(ledger,data)
 elif op=='accept':
  row=next(r for r in data['requests'] if r['name']==ident+'_preview')
  if row['status']!='SUCCEEDED':raise RuntimeError('No completed preview')
  row['geometry_review']='ACCEPTED_FOR_REFINE';row['review_at']=m.now();m.save(ledger,data)
 elif op=='poll':
  for row in data['requests']:
   if not row.get('task_id') or row.get('files') or row['status'] in ('FAILED','CANCELED'):continue
   response=m.api('GET',row['endpoint']+'/'+row['task_id']);m.save(m.OUT/'.private'/(row['name']+'.json'),response)
   for k in ('status','progress','consumed_credits'):row[k]=response.get(k)
   m.save(ledger,data)
   if row['status']=='SUCCEEDED':row['files']=m.download_files(response,row);m.save(ledger,data)
 print(json.dumps([{k:r.get(k) for k in ('name','status','progress','consumed_credits')} for r in data['requests']],ensure_ascii=False))
if __name__=='__main__':
 try:run(sys.argv[1],sys.argv[2] if len(sys.argv)>2 else None)
 except Exception as e:print(str(e) if isinstance(e,RuntimeError) else type(e).__name__);sys.exit(1)
