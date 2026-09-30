"""Two explicitly requested Meshy bedrock assets. Durable POST reservations."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('meshy_base',ROOT/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
m.OUT=ROOT/'Art/World/Compact/Rebuild/Ascent282/Meshy'
LEDGER=m.OUT/'ledger.json'
PROMPTS={
 'GraniteButtress':'A single massive natural Korean granite cliff buttress, like exposed bedrock on Bukhansan. Tall asymmetric rock mass, height about twice its width, substantial depth. Three broad continuous angular faces interrupted by a few deep irregular steeply diagonal joints, narrow dark clefts, torn exfoliation ledges and chipped edges. Many sizes of erosion detail within one coherent solid mass. One uneven broken summit and a broad partly buried base. Realistic geology and natural gravity, not a stack of separate boulders. Complete standalone 3D environment mesh, all sides modeled. No plants, trees, ground plane, pedestal, buildings, stairs, roads, figures, text. No rounded clay lumps, cobblestones, tiled polygon pattern, regular brickwork, crystal spikes or canyon sedimentary stripes.',
 'GraniteShoulder':'One broad continuous natural granite mountain outcrop from a Korean mountain. Asymmetric weathered bedrock shoulder, wider than tall, thick solid volume, one sloping upper shelf with torn edges. Large connected planar rock faces, a few unequal deep vertical fissures and oblique sheared steps that stop inside the rock, rough chipped corners and shallow exfoliation. Strong angular silhouette with a small overhanging lip. Fine fractured geological detail, realistic rugged granite. Thick uneven base suitable for burying in terrain. Isolated complete 3D environment asset, all sides modeled. Not a pile of stones. No ground platform, plants, trees, buildings, path, text, human forms. No rounded clay bubbles, tiled polygons, stacked brick slabs or evenly spaced columns.'
}

def run(op):
 d=json.loads(LEDGER.read_text()) if LEDGER.exists() else dict(model='meshy-7.1',self_imposed_cap=50,automatic_post_retries=False,pricing_source='https://docs.meshy.ai/en/api/pricing',requests=[])
 if op=='submit':
  for name,prompt in PROMPTS.items():
   assert len(prompt)<=800
   if any(r['name']==name for r in d['requests']):continue
   if sum(r['reserved_credits'] for r in d['requests'])+25>50:raise RuntimeError('Run cap exceeded')
   if m.api('GET','/openapi/v1/balance').get('balance',0)<25:raise RuntimeError('Insufficient existing credits')
   cfg=dict(mode='preview',prompt=prompt,ai_model='meshy-7.1',model_type='standard',geometry_resolution='4k',should_remesh=True,target_polycount=60000,topology='triangle',target_formats=['glb','fbx'])
   row=dict(name=name,id=name,mode='preview',config=cfg,reserved_credits=25,status='SUBMISSION_RESERVED',created_at=m.now())
   d['requests'].append(row);m.save(LEDGER,d)
   try:row.update(task_id=m.api('POST',m.ENDPOINT,cfg)['result'],status='SUBMITTED')
   except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';m.save(LEDGER,d);raise
   m.save(LEDGER,d)
 elif op=='poll':
  for row in d['requests']:
   if not row.get('task_id') or row.get('files') or row['status'] in ('FAILED','CANCELED'):continue
   r=m.api('GET',m.ENDPOINT+'/'+row['task_id']);m.save(m.OUT/'.private'/(row['name']+'.json'),r)
   for k in ('status','progress','consumed_credits'):row[k]=r.get(k)
   m.save(LEDGER,d)
   if row['status']=='SUCCEEDED':row['files']=m.download_files(r,row);m.save(LEDGER,d)
 print(json.dumps([{k:r.get(k) for k in ('name','status','progress','consumed_credits')} for r in d['requests']]))

if __name__=='__main__':
 try:run(sys.argv[1])
 except Exception as e:print(str(e) if isinstance(e,RuntimeError) else type(e).__name__);sys.exit(1)
