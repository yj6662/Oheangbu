"""Three missing creature appearances; durable reservations, no paid retries or purchases."""
import argparse,json,sys
from pathlib import Path
R=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(R/'Tools/MeshyRuns/SpellVFX120'))
import run_stone_dokkaebi as c
O=R/'Art/World/WorldMacro/Compact/KcisaReplacement/Meshy';c.OUT=O
S={
'AzureDragon':('A realistic Korean Cheongryong guardian dragon, complete single creature. Long sinuous muscular serpentine body in a low open S curve, four short powerful clawed legs on the ground, lifted dignified head, long muzzle, whiskers, swept back branched antler horns, small mane and individually readable overlapping scales. East Asian dragon anatomy, no wings. Head and all four feet clearly separated from the body. Tail extends behind, no self intersections, no pedestal, no environment, no text, no jewelry. Natural animal proportions and believable skin, game production creature sculpture.', 'Natural muted deep blue green scales, pale warm grey belly scutes, weathered dark antlers and ivory claws, charcoal whiskers, restrained amber eyes. Physically based realistic rough reptile skin. No neon glow, armor or metallic scales.'),
'VermilionBird':('A realistic Korean sacred vermilion bird, one complete creature standing on two powerful bird feet, long crane-like neck, small dignified pheasant head with feather crest, two half-open feathered wings, long flowing layered tail feathers arching down behind. Believable muscular bird anatomy and overlapping primary flight feathers, elegant Korean phoenix silhouette. Wings clearly separated from torso, all feet visible, natural asymmetrical alert pose. No fire, no particles, no pedestal, no environment, no letters, no armor, no dragon head. Game production creature sculpture.', 'Deep muted vermilion and russet plumage, charcoal feather roots and warm ochre tips, dark bronze brown beak and scaly bird feet. Real feather roughness and layered grain. No glow, flames or metallic body.'),
'Imugi':('A realistic Korean imugi river serpent, complete large limbless creature. Broad old crocodilian snake head, strong jaw, no horns, no wings, no legs. Thick muscular serpentine body in a wide low S-shaped curve, lifted forward head, gradually tapering long tail, worn overlapping scales and broad belly scutes. All loops clearly separated, continuous anatomically coherent snake, open mouth only slightly with small teeth. Grounded natural animal pose, believable reptile skin. No pedestal, no environment, no lettering, no armor, no jewelry.', 'Old charcoal and muddy olive reptile scales, pale grey belly, earthy stains and subtle healed scars. Wetness only on some scale edges, realistic roughness. Dull amber eyes, off white teeth. No glow or decorative paint.')}
def main():
 p=argparse.ArgumentParser();p.add_argument('op',choices=['submit','poll','refine']);p.add_argument('name',choices=S);a=p.parse_args()
 O.mkdir(parents=True,exist_ok=True);(O/'.gitignore').write_text('.private/\n');lp=O/'ledger.json'
 v=json.loads(lp.read_text()) if lp.exists() else dict(self_imposed_cap=90,price_checked='2026-09-16',price_source='https://docs.meshy.ai/en/api/pricing',paid_retries=False,requests=[])
 mode='refine' if a.op=='refine' else 'preview';name=a.name+'_'+mode
 row=next((r for r in v['requests'] if r['name']==name),None)
 if a.op in ['submit','refine']:
  if row:raise RuntimeError('Reservation exists; never resubmit a paid request')
  price=20 if mode=='preview' else 10
  if sum(r['reserved_credits'] for r in v['requests'])+price>90:raise RuntimeError('Self-imposed cap reached')
  bal=c.api('GET','/openapi/v1/balance').get('balance',0)
  if bal<price:raise RuntimeError('Insufficient existing credits; no purchase')
  if mode=='preview':cfg=dict(mode='preview',prompt=S[a.name][0],ai_model='meshy-7',model_type='standard',ultra_mode=False,should_remesh=True,topology='triangle',target_polycount=18000,target_formats=['glb','fbx'])
  else:
   prev=next(r for r in v['requests'] if r['name']==a.name+'_preview')
   if prev['status']!='SUCCEEDED':raise RuntimeError('Preview not ready')
   cfg=dict(mode='refine',preview_task_id=prev['task_id'],ai_model='meshy-7',enable_pbr=True,texture_resolution='2k',texture_prompt=S[a.name][1],target_formats=['glb','fbx'])
  row=dict(name=name,id=a.name,mode=mode,config=cfg,reserved_credits=price,status='RESERVED',balance_before=bal);v['requests'].append(row);c.save(lp,v)
  try:r=c.api('POST','/openapi/v2/text-to-3d',cfg)
  except Exception:row['status']='UNCERTAIN_NO_RETRY';c.save(lp,v);raise
  row.update(task_id=r['result'],status='SUBMITTED');c.save(lp,v)
 else:
  for row in [r for r in v['requests'] if r['id']==a.name and r.get('task_id')]:
   r=c.api('GET','/openapi/v2/text-to-3d/'+row['task_id']);c.save(O/'.private'/(row['name']+'.json'),r)
   for k in ['status','progress','consumed_credits']:
    if k in r:row[k]=r[k]
   c.save(lp,v)
   if row['status']=='SUCCEEDED':row['files']=c.download_files(r,row);c.save(lp,v)
 print(json.dumps([{k:r.get(k) for k in ['name','status','progress','consumed_credits']} for r in v['requests']],ensure_ascii=False))
if __name__=='__main__':
 try:main()
 except Exception as e:print('STOPPED: '+(str(e) if isinstance(e,RuntimeError) else type(e).__name__));sys.exit(1)
