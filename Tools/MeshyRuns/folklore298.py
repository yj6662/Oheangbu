"""#298 Meshy Smart Topology intake. Paid POSTs are explicit, serialized, never retried.

The 280-credit ceiling is a conservative local operating limit, not a purchase
authorization. Original models, provider receipts, and hashes remain separate
from rigging derivatives. Secrets and reference-image data are never logged.
"""
import argparse
import base64
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import re
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Characters/Folklore298'
LEDGER = OUT / 'meshy-ledger.json'
CAP = 280
BASE = 'https://api.meshy.ai'


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.writing')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def call(method, endpoint, payload=None):
    key = next((line.split('=', 1)[1].strip().strip('\"').strip("'")
                for line in (ROOT / '.env').read_text(encoding='utf-8-sig').splitlines()
                if line.startswith('MESHY_API_KEY=')), None)
    if not key:
        raise RuntimeError('MESHY_API_KEY is not configured')
    request = urllib.request.Request(BASE + endpoint, method=method,
        data=None if payload is None else json.dumps(payload).encode(),
        headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError('Meshy HTTP ' + str(error.code)) from None
    except (TimeoutError, urllib.error.URLError):
        raise RuntimeError('Meshy transport failure; reconcile any reserved POST before retrying') from None


def ledger():
    if LEDGER.exists():
        return json.loads(LEDGER.read_text(encoding='utf-8'))
    result = dict(schema=1, created_at=now(), operating_cap=CAP,
        cap_basis='Local ceiling within existing balance; no credits purchased',
        balance_start=call('GET', '/openapi/v1/balance'), tasks=[])
    save(LEDGER, result)
    return result


def cost(entry):
    actual = entry.get('consumed_credits')
    return credits(actual if actual is not None else entry['reserved_credits'])


def credits(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise RuntimeError('Invalid credit accounting value; no paid request can proceed')
    return value


def summary(data):
    return dict(operating_cap=data['operating_cap'], reserved_or_consumed=sum(map(cost, data['tasks'])),
        balance_latest=data.get('balance_latest', data.get('balance_start')),
        tasks=[{k: x.get(k) for k in ('name', 'kind', 'id', 'status', 'progress',
                                    'reserved_credits', 'consumed_credits', 'downloaded_at')}
               for x in data['tasks']])


def submit(args):
    if not re.fullmatch(r'[a-z0-9_-]+', args.name):
        raise RuntimeError('Use a simple unique lowercase task name')
    data = ledger()
    if any(t['name'] == args.name for t in data['tasks']):
        return summary(data)
    if any(t['status'] in ('POST_RESERVED', 'POST_UNCERTAIN') for t in data['tasks']):
        raise RuntimeError('An earlier POST needs reconciliation; no additional paid call submitted')
    source = None
    if args.kind == 'model':
        source = Path(args.reference).resolve()
        if not source.is_file() or source.suffix.lower() not in ('.png', '.jpg', '.jpeg'):
            raise RuntimeError('A reviewed PNG/JPEG reference is required')
        config = dict(model_type='smart-topology', ai_model='meshy-t2',
            target_polycount=args.polygons, should_texture=True, enable_pbr=True,
            texture_resolution='4k', target_formats=['glb', 'fbx'],
            pose_mode='a-pose' if args.humanoid else '', multi_view_thumbnails=True)
        endpoint, estimate = '/openapi/v1/image-to-3d', 15
        mime = 'image/png' if source.suffix.lower() == '.png' else 'image/jpeg'
        payload = dict(config, image_url='data:' + mime + ';base64,' + base64.b64encode(source.read_bytes()).decode())
    else:
        parent = next((t for t in data['tasks'] if t['name'] == args.parent), None)
        if not parent or parent['status'] != 'SUCCEEDED':
            raise RuntimeError('The parent task must have succeeded')
        if args.kind == 'rig':
            config = dict(input_task_id=parent['id'], height_meters=args.height)
            endpoint, estimate = '/openapi/v1/rigging', 5
        else:
            actions = [int(x) for x in args.actions.split(',')]
            if not 1 <= len(actions) <= 10 or len(set(actions)) != len(actions):
                raise RuntimeError('Require 1 to 10 distinct animation action IDs')
            config = dict(rig_task_id=parent['id'], action_ids=actions)
            endpoint, estimate = '/openapi/v1/animations', 3 * len(actions)
        payload = config
    reserved = sum(map(cost, data['tasks']))
    if reserved + estimate > min(CAP, credits(data['operating_cap'])):
        raise RuntimeError('Local operating ceiling would be exceeded')
    balance = call('GET', '/openapi/v1/balance')
    available = balance.get('balance', 0)
    if isinstance(available, dict):
        available = available.get('credits', 0)
    # Current API reports credit_balance; do not infer zero from an unknown schema.
    if 'credit_balance' in balance:
        available = balance['credit_balance']
    if credits(available) < estimate:
        raise RuntimeError('Insufficient or unrecognized account balance; no POST submitted')
    entry = dict(name=args.name, kind=args.kind, endpoint=endpoint, config=config,
        reserved_credits=estimate, consumed_credits=None, status='POST_RESERVED', created_at=now())
    if source:
        entry['reference'] = str(source.relative_to(ROOT)) if source.is_relative_to(ROOT) else source.name
        entry['reference_sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
    else:
        entry['parent'] = args.parent
    if args.dry_run:
        return dict(dry_run=True, entry=entry, balance=balance, projected=reserved + estimate)
    data['tasks'].append(entry)
    save(LEDGER, data)
    try:
        response = call('POST', endpoint, payload)
        task_id = response['result']
        if not isinstance(task_id, str) or not task_id:
            raise RuntimeError('Unexpected submission response; inspect provider task list')
        entry.update(id=task_id, status='SUBMITTED', submitted_at=now())
    except Exception:
        entry.update(status='POST_UNCERTAIN', checked_at=now())
        save(LEDGER, data)
        raise
    save(LEDGER, data)
    return summary(data)


def asset_urls(result):
    for fmt, url in result.get('model_urls', {}).items():
        if url and fmt in ('glb', 'fbx'):
            yield 'source.' + fmt, url
    if result.get('thumbnail_url'):
        yield 'preview.png', result['thumbnail_url']
    for view, url in result.get('thumbnail_urls', {}).items():
        if url:
            yield 'preview_' + re.sub(r'[^a-zA-Z0-9_-]', '_', view) + '.png', url
    for index, maps in enumerate(result.get('texture_urls', [])):
        for kind, url in maps.items():
            if url:
                yield f'T_{index}_{kind}.png', url
    def recurse(obj, prefix):
        if isinstance(obj, dict):
            for key, value in obj.items():
                yield from recurse(value, prefix + '_' + str(key))
        elif isinstance(obj, list):
            for index, value in enumerate(obj):
                yield from recurse(value, prefix + '_' + str(index))
        elif isinstance(obj, str) and obj.startswith('https://'):
            bare = obj.split('?')[0].lower()
            ext = next((x for x in ('.glb', '.fbx', '.mp4') if bare.endswith(x)), None)
            if ext:
                yield re.sub(r'[^a-zA-Z0-9_-]', '_', prefix) + ext, obj
    yield from recurse(result.get('result', {}), 'result')


def poll(download=False):
    data = ledger()
    for entry in data['tasks']:
        if not entry.get('id'):
            continue
        task_dir = OUT / 'Source' / entry['name']
        result = call('GET', entry['endpoint'] + '/' + entry['id'])
        save(task_dir / 'provider-response.private.json', result)
        for field in ('status', 'progress', 'consumed_credits', 'finished_at'):
            if field in result:
                entry[field] = result[field]
        save(LEDGER, data)
        if download and result.get('status') == 'SUCCEEDED':
            records = []
            for filename, url in asset_urls(result):
                path = task_dir / filename
                if not path.exists():
                    try:
                        with urllib.request.urlopen(url, timeout=120) as response:
                            temp = path.with_suffix(path.suffix + '.downloading')
                            with temp.open('wb') as out:
                                while chunk := response.read(1024 * 1024):
                                    out.write(chunk)
                            temp.replace(path)
                    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError):
                        raise RuntimeError('Asset download failed; resumable without a new paid task') from None
                records.append(dict(path=filename, bytes=path.stat().st_size,
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
            save(task_dir / 'downloads.json', records)
            entry['downloaded_at'] = now()
            save(LEDGER, data)
    data.update(balance_latest=call('GET', '/openapi/v1/balance'), checked_at=now())
    save(LEDGER, data)
    return summary(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    for name in ('status', 'poll', 'download', 'library'):
        commands.add_parser(name)
    task = commands.add_parser('submit')
    task.add_argument('kind', choices=['model', 'rig', 'animation'])
    task.add_argument('name')
    task.add_argument('--reference')
    task.add_argument('--humanoid', action='store_true')
    task.add_argument('--polygons', type=int, default=12000, choices=range(100, 15001), metavar='100..15000')
    task.add_argument('--height', type=float, default=2)
    task.add_argument('--parent')
    task.add_argument('--actions')
    task.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    lock = OUT / '.intake-lock'
    try:
        descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        raise RuntimeError('Another intake command owns the ledger; inspect before removing a stale lock') from None
    try:
        os.close(descriptor)
        if args.command == 'submit':
            result = submit(args)
        elif args.command == 'library':
            result = call('GET', '/openapi/v1/animations/library')
            save(OUT / 'Analysis/animation-library.json', result)
            result = dict(saved='Analysis/animation-library.json')
        elif args.command in ('poll', 'download'):
            result = poll(args.command == 'download')
        else:
            result = summary(ledger())
        print(json.dumps(result, ensure_ascii=False, indent=2))
    finally:
        lock.unlink(missing_ok=True)


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, ValueError, KeyError, StopIteration) as error:
        # Only application-authored errors escape; provider response bodies never do.
        if isinstance(error, RuntimeError):
            print(str(error))
        else:
            print('Invalid request or provider schema; inspect local task receipt safely')
        raise SystemExit(1)
