"""Four independent Meshy 7 requests, durable reservations, never retry a POST."""
import argparse, base64, json, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'Tools/MeshyRuns/SpellVFX120'))
import run_stone_dokkaebi as common
OUT=ROOT/'Art/World/WorldMacro/MagicStoneCar/Meshy'
common.OUT=OUT
PARTS={'Cabin':16000,'Roof':8000,'Engine':8000,'Wheel':3000}
ENDPOINT='/openapi/v1/image-to-3d'
CAP=120

def main():
    p=argparse.ArgumentParser();p.add_argument('op',choices=['submit','poll']);p.add_argument('part',choices=PARTS);a=p.parse_args()
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'.gitignore').write_text('.private/\n')
    ledger=OUT/'ledger.json'
    value=json.loads(ledger.read_text()) if ledger.exists() else dict(user_authorized_cap=CAP,budget_basis='User approved maximum 120 credits. Four Meshy 7 Standard textured PBR 4k image requests, 30 credits each. No paid retries. Public API docs checked 2026-09-12: 7.1 not listed.',price_source='https://docs.meshy.ai/en/api/pricing',endpoint=ENDPOINT,automatic_post_retries=False,requests=[])
    row=next((r for r in value['requests'] if r['name']==a.part),None)
    if a.op=='submit':
        if row:raise RuntimeError('Existing reservation retained. Poll; never resubmit.')
        if any(r['status'] not in ('SUCCEEDED','FAILED','CANCELED') for r in value['requests']):raise RuntimeError('Finish the previous part first')
        if sum(r['reserved_credits'] for r in value['requests'])+30>CAP:raise RuntimeError('Run cap reached')
        image=OUT.parent/'Concepts'/f'{a.part}.png'
        if not image.exists():raise RuntimeError('Missing reviewed part concept')
        cfg=dict(ai_model='meshy-7',model_type='standard',ultra_mode=False,should_texture=True,enable_pbr=True,texture_resolution='4k',should_remesh=True,topology='triangle',target_polycount=PARTS[a.part],image_enhancement=False,target_formats=['glb','fbx'])
        balance=common.api('GET','/openapi/v1/balance').get('balance',0)
        if balance<30:raise RuntimeError('Insufficient existing credits; no purchase')
        row=dict(name=a.part,id=a.part,mode='image',config=cfg,input_file=str(image.relative_to(ROOT)),input_sha256=common.sha(image),expected_credits=30,reserved_credits=30,consumed_credits=None,balance_before=balance,status='SUBMISSION_RESERVED',created_at=common.now())
        value['requests'].append(row);common.save(ledger,value)
        try:response=common.api('POST',ENDPOINT,dict(cfg,image_url='data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode()))
        except Exception:row['status']='SUBMISSION_UNCERTAIN_NO_RETRY';common.save(ledger,value);raise
        row.update(task_id=response['result'],status='SUBMITTED');common.save(ledger,value)
    else:
        if not row or not row.get('task_id'):raise RuntimeError('No known task to poll')
        response=common.api('GET',ENDPOINT+'/'+row['task_id']);common.save(OUT/'.private'/f'{a.part}.json',response)
        for k in ['status','progress','consumed_credits']:
            if k in response:row[k]=response[k]
        common.save(ledger,value)
        if row['status']=='SUCCEEDED':row['files']=common.download_files(response,row)
        row['balance_after']=common.api('GET','/openapi/v1/balance').get('balance');common.save(ledger,value)
    print(json.dumps({k:row.get(k) for k in ['name','task_id','status','progress','expected_credits','consumed_credits']},ensure_ascii=False))

if __name__=='__main__':
    try:main()
    except Exception as e:
        print('STOPPED: '+(str(e) if isinstance(e,RuntimeError) else type(e).__name__),file=sys.stderr);sys.exit(1)
