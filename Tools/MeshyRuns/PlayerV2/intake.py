"""Budgeted, resumable Meshy 7 intake. No POST is retried automatically.

Requests persist a reservation before network IO. An uncertain POST must be
reconciled against remote tasks before any further submission with that name.
API credentials and image data are never printed or written to provenance.
"""
import argparse
import base64
import datetime as dt
import hashlib
import json
from pathlib import Path
import urllib.request
import urllib.error

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/PlayerV2/MeshySources'
REF = ROOT / 'Art/PlayerV2/Reference'
LEDGER = OUT / 'ledger.json'
BASE = 'https://api.meshy.ai'

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.writing')
    temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)

def call(method, endpoint, payload=None):
    key = next(x.split('=', 1)[1].strip().strip('"').strip("'") for x in
               (ROOT / '.env').read_text(encoding='utf-8-sig').splitlines()
               if x.startswith('MESHY_API_KEY='))
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + endpoint, data=data, method=method,
        headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        # API errors may echo inputs; keep only the safe status on stdout.
        raise RuntimeError('Meshy HTTP ' + str(exc.code)) from None

def ledger():
    if LEDGER.exists():
        return json.loads(LEDGER.read_text(encoding='utf-8'))
    data = {'version': 1, 'created_at': now(), 'budget': 200,
            'previous_character_credits_excluded': 35,
            'balance_start': call('GET', '/openapi/v1/balance'), 'tasks': []}
    save(LEDGER, data)
    return data

def public_summary(data):
    print(json.dumps({'budget': data['budget'],
        'reserved_or_consumed': sum(x.get('consumed_credits') if x.get('consumed_credits') is not None
                                   else x['estimated_credits'] for x in data['tasks']),
        'balance_start': data['balance_start'],
        'tasks': [{k: x.get(k) for k in ('name', 'id', 'status', 'progress', 'estimated_credits', 'consumed_credits')}
                  for x in data['tasks']]}, ensure_ascii=False))

def submit(kind, name):
    data = ledger()
    if any(x['name'] == name for x in data['tasks']):
        public_summary(data)
        return
    character = kind == 'character'
    views = ['front', 'left', 'back', 'right'] if character else [kind]
    cost = 35 if character else 30
    reserved = sum(x.get('consumed_credits') if x.get('consumed_credits') is not None else x['estimated_credits']
                   for x in data['tasks'])
    if reserved + cost > data['budget']:
        raise RuntimeError('Approved 200 credit cap would be exceeded')
    files = [REF / (view + '.png') for view in views]
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    urls = ['data:image/png;base64,' + base64.b64encode(p.read_bytes()).decode() for p in files]
    config = dict(ai_model='meshy-7', ultra_mode=character, should_texture=True,
                  enable_pbr=True, texture_resolution='4k', image_enhancement=False,
                  remove_lighting=True, should_remesh=True, topology='triangle',
                  target_polycount=70000 if character else (12000 if kind == 'backpack' else 4500),
                  save_pre_remeshed_model=True, target_formats=['glb', 'fbx'],
                  multi_view_thumbnails=True)
    if character:
        config['pose_mode'] = 't-pose'
    entry = {'name': name, 'kind': kind, 'created_at': now(),
             'endpoint': '/openapi/v1/multi-image-to-3d', 'config': config,
             'reference_sha256': hashes, 'reference_order': [p.name for p in files],
             'estimated_credits': cost, 'consumed_credits': None,
             'status': 'SUBMISSION_RESERVED'}
    data['tasks'].append(entry)
    save(LEDGER, data)
    result = call('POST', entry['endpoint'], dict(config, image_urls=urls, texture_image_urls=urls))
    entry.update(id=result['result'], status='SUBMITTED', submitted_at=now())
    save(LEDGER, data)
    public_summary(data)

def poll(download=False):
    data = ledger()
    for entry in data['tasks']:
        if not entry.get('id'):
            continue
        result = call('GET', entry['endpoint'] + '/' + entry['id'])
        task_dir = OUT / entry['name']
        save(task_dir / 'response.json', result)
        for field in ('status', 'progress', 'consumed_credits', 'finished_at'):
            if field in result:
                entry[field] = result[field]
        save(LEDGER, data)
        if download and result.get('status') == 'SUCCEEDED':
            assets = []
            for fmt, url in result.get('model_urls', {}).items():
                if url and fmt in ('glb', 'fbx', 'pre_remeshed_glb'):
                    assets.append(('source_' + fmt + ('.fbx' if fmt == 'fbx' else '.glb'), url))
            if result.get('thumbnail_url'):
                assets.append(('preview.png', result['thumbnail_url']))
            for view, url in result.get('thumbnail_urls', {}).items():
                if url:
                    assets.append(('preview_' + view + '.png', url))
            for index, maps in enumerate(result.get('texture_urls', [])):
                for kind, url in maps.items():
                    if url:
                        assets.append((f'T_{index}_{kind}.png', url))
            records = []
            for name, url in assets:
                path = task_dir / name
                if not path.exists():
                    with urllib.request.urlopen(url, timeout=120) as response:
                        tmp = path.with_suffix(path.suffix + '.downloading')
                        with tmp.open('wb') as output:
                            while chunk := response.read(1024 * 1024):
                                output.write(chunk)
                        tmp.replace(path)
                records.append({'path': name, 'bytes': path.stat().st_size,
                                'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
            save(task_dir / 'downloads.json', records)
            entry['downloaded_at'] = now()
            save(LEDGER, data)
    data['balance_latest'] = call('GET', '/openapi/v1/balance')
    data['checked_at'] = now()
    save(LEDGER, data)
    public_summary(data)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['init', 'character', 'brush', 'backpack', 'poll', 'download'])
    parser.add_argument('--name')
    args = parser.parse_args()
    if args.action in ('character', 'brush', 'backpack'):
        submit(args.action, args.name or args.action + '-01')
    elif args.action in ('poll', 'download'):
        poll(args.action == 'download')
    else:
        public_summary(ledger())
