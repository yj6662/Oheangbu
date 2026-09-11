"""One paid preview/refine per summon; durable reservations, no POST retries.

Retry02 only; original ledger and rejected tasks are preserved.
Public output never includes credentials or signed download URLs. Raw responses
remain in .private. Only authored text is uploaded; no KTP or other asset images.
"""
import argparse
import datetime
import hashlib
import json
import struct
import sys
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/SpellVFX120/StoneJangseung/Meshy'
ORIGINAL_LEDGER = ROOT / 'Art/SpellVFX120/MeshySummons/ledger.json'
BASE = 'https://api.meshy.ai'
ENDPOINT = '/openapi/v2/text-to-3d'
CAP = 35
SPECS = {'064_BAB8': ('몸', 'StoneJangseung', 'One Korean stone jangseung guardian totem, sophisticated dark fantasy game prop. Single tall chunky tapered granite pillar, broad flat stable base, NO arms or legs. Face carved directly into upper third: huge protruding rounded stone eyes beneath sweeping joined brows, broad triangular blunt nose with curled nostrils, sly asymmetrical smile and few broad square teeth. Crown is a simple broken rock ridge, no hat or horns. Refined strong silhouette, restrained geometric carving and deliberate chisel planes. Lower column bears three shallow abstract flowing earth-seal grooves, NOT writing. Original stylized interpretation of weathered Korean village guardian stones, dignified and slightly mischievous. No humanoid torso, armor, weapons, pedestal, letters, jewelry or modern objects.', 'Muted warm grey weathered granite, broad mineral variation, subtle ochre dust in cracks and sparse dark green moss near the base. Carved eye sockets and mouth read clearly with dark recesses, raised eyes and brows remain stone. Matte rough stone and fine pores; no polished marble, metal, gold, lava, bright glowing eyes, lettering, painted colors or white plaster.')}



def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.writing')
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    tmp.replace(path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def api(method, endpoint, payload=None):
    key = next(line.split('=', 1)[1].strip().strip('"').strip("'")
               for line in (ROOT / '.env').read_text(encoding='utf-8-sig').splitlines()
               if line.startswith('MESHY_API_KEY='))
    req = urllib.request.Request(BASE + endpoint, method=method,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError('Meshy HTTP ' + str(error.code)) from None
    except (urllib.error.URLError, TimeoutError):
        raise RuntimeError('Meshy transport unavailable; no POST retry') from None


def ledger():
    return json.loads((OUT / 'ledger.json').read_text(encoding='utf-8'))


def used(value):
    return sum(row['consumed_credits'] if row.get('consumed_credits') is not None
               and row.get('status') in ('SUCCEEDED', 'FAILED', 'CANCELED')
               else row['reserved_credits'] for row in value['requests'])


def init():
    if (OUT / 'ledger.json').exists():
        return
    for _, _, prompt, texture in SPECS.values():
        if len(prompt) > 800 or len(texture) > 800:
            raise RuntimeError('Authored prompt exceeds official 800-character maximum')
    balance = api('GET', '/openapi/v1/balance')
    save(OUT / 'ledger.json', {'schema': 2, 'started_at': now(), 'original_ledger_sha256': sha(ORIGINAL_LEDGER), 'prior_summon_batches_actual_credits': 240, 'self_imposed_run_cap': CAP,
        'budget_basis': 'Self-imposed cap 35: one Meshy 7 Ultra preview at 25 and one refine at 10. No automatic paid retries. Separate from earlier batches; not a user-set limit.',
        'balance_start': balance.get('balance'), 'price_checked_at': now(),
        'price_source': 'https://docs.meshy.ai/en/api/pricing',
        'api_source': 'https://docs.meshy.ai/en/api/text-to-3d',
        'prices': {'meshy7_ultra_preview': 25, 'meshy7_refine_2k_pbr': 10},
        'input_policy': 'Authored text only. No KTP or other paid asset images uploaded.',
        'automatic_post_retries': False, 'requests': []})


def status():
    value = ledger()
    print(json.dumps({'self_imposed_cap': CAP, 'spent_or_reserved': used(value),
        'balance_latest': value.get('balance_latest', value.get('balance_start')),
        'tasks': [{k: row.get(k) for k in ('name', 'task_id', 'status', 'progress',
            'consumed_credits', 'reserved_credits', 'geometry_review')}
            for row in value['requests']]}, ensure_ascii=False))


def submit(ident, mode):
    value = ledger()
    name = ident + '_' + mode
    if any(row['name'] == name for row in value['requests']):
        print('Retaining existing reservation/task: ' + name)
        return
    if value.get('further_paid_requests_stopped'):
        raise RuntimeError('This run is closed to new paid requests; retained tasks stay readable')
    glyph, family, prompt, texture = SPECS[ident]
    price = 25 if mode == 'preview' else 10
    if used(value) + price > CAP:
        raise RuntimeError('Self-imposed cap including reservations exceeded')
    if mode == 'preview':
        config = {'mode': 'preview', 'prompt': prompt, 'ai_model': 'meshy-7',
            'model_type': 'standard', 'ultra_mode': True, 'should_remesh': True,
            'topology': 'triangle', 'target_polycount': 12000, 'target_formats': ['glb', 'fbx']}
    else:
        source = next(row for row in value['requests'] if row['name'] == ident + '_preview')
        if source['status'] != 'SUCCEEDED' or source.get('geometry_review', {}).get('decision') != 'ACCEPTED_FOR_REFINE':
            raise RuntimeError('Refine requires completed preview and recorded visual geometry acceptance')
        config = {'mode': 'refine', 'preview_task_id': source['task_id'], 'ai_model': 'meshy-7',
            'texture_resolution': '2k', 'enable_pbr': True, 'texture_prompt': texture,
            'target_formats': ['glb', 'fbx']}
    balance_before = api('GET', '/openapi/v1/balance').get('balance', 0)
    if balance_before < price:
        raise RuntimeError('Insufficient existing API balance; no purchase attempted')
    row = {'name': name, 'id': ident, 'glyph': glyph, 'family': family, 'mode': mode,
        'created_at': now(), 'config': config, 'expected_credits': price,
        'reserved_credits': price, 'consumed_credits': None, 'status': 'SUBMISSION_RESERVED', 'balance_before': balance_before,
        'config_sha256': hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()}
    value['requests'].append(row)
    save(OUT / 'ledger.json', value)  # Durable reservation BEFORE the paid POST.
    try:
        response = api('POST', ENDPOINT, config)
        row.update(task_id=response['result'], status='SUBMITTED')
    except Exception:
        row['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'
        save(OUT / 'ledger.json', value)
        raise
    save(OUT / 'ledger.json', value)
    print(json.dumps({k: row[k] for k in ('name', 'task_id', 'status', 'reserved_credits')}, ensure_ascii=False))


def glb_stats(path):
    blob = path.read_bytes()
    if blob[:4] != b'glTF':
        return {'status': 'UNVERIFIED_NOT_GLB'}
    size, kind = struct.unpack_from('<II', blob, 12)
    doc = json.loads(blob[20:20+size].decode('utf-8').rstrip(' \x00'))
    triangles = vertices = 0
    modes = []
    for mesh in doc.get('meshes', []):
        for primitive in mesh['primitives']:
            mode = primitive.get('mode', 4); modes.append(mode)
            count = doc['accessors'][primitive['indices']]['count'] if 'indices' in primitive else doc['accessors'][primitive['attributes']['POSITION']]['count']
            triangles += count // 3 if mode == 4 else max(0, count - 2) if mode in (5, 6) else 0
            vertices += doc['accessors'][primitive['attributes']['POSITION']]['count']
    return {'status': 'GLB_ACCESSOR_COUNTS', 'unique_mesh_triangles': triangles,
        'primitive_vertices': vertices, 'meshes': len(doc.get('meshes', [])),
        'materials': len(doc.get('materials', [])), 'primitive_modes': modes,
        'note': 'Actual GLB primitive counts, not requested target. Scene instancing/runtime deformation not included.'}


def download_files(response, row):
    dest = OUT / row['id'] / row['mode']
    files = []
    def collect(item, prefix=''):
        if isinstance(item, dict):
            for key, child in item.items():
                collect(child, prefix + '_' + key if prefix else key)
        elif isinstance(item, list):
            for index, child in enumerate(item):
                collect(child, prefix + '_' + str(index))
        elif isinstance(item, str) and item.startswith('https://'):
            extension = Path(urlsplit(item).path).suffix.lower()
            if extension not in ('.glb', '.fbx', '.png', '.jpg', '.jpeg'):
                return
            path = dest / (prefix + extension)
            if not path.exists():
                dest.mkdir(parents=True, exist_ok=True)
                tmp = path.with_suffix(path.suffix + '.part')
                try:
                    with urllib.request.urlopen(item, timeout=90) as response_file, tmp.open('wb') as output:
                        while block := response_file.read(1024 * 1024):
                            output.write(block)
                    tmp.replace(path)
                except Exception:
                    raise RuntimeError('Download incomplete for local file ' + path.name) from None
            entry = {'path': str(path.relative_to(OUT)), 'bytes': path.stat().st_size, 'sha256': sha(path)}
            if extension == '.glb':
                entry['geometry'] = glb_stats(path)
            files.append(entry)
    collect(response)
    save(dest / 'manifest.json', {'task_id': row['task_id'], 'config': row['config'],
        'consumed_credits': row.get('consumed_credits'), 'files': files})
    return files


def poll(download):
    value = ledger()
    for row in value['requests']:
        if not row.get('task_id'):
            continue
        response = api('GET', ENDPOINT + '/' + row['task_id'])
        save(OUT / '.private' / (row['name'] + '.json'), response)
        for key in ('status', 'progress', 'consumed_credits'):
            if key in response:
                row[key] = response[key]
        row['polled_at'] = now()
        save(OUT / 'ledger.json', value)
        if download and row['status'] == 'SUCCEEDED':
            row['files'] = download_files(response, row)
            save(OUT / 'ledger.json', value)
    value['balance_latest'] = api('GET', '/openapi/v1/balance').get('balance')
    value['balance_checked_at'] = now()
    value['original_ledger_preserved'] = sha(ORIGINAL_LEDGER) == value['original_ledger_sha256']
    save(OUT / 'ledger.json', value)
    status()


def review(ident, decision, note):
    value = ledger()
    row = next(row for row in value['requests'] if row['name'] == ident + '_preview')
    if row['status'] != 'SUCCEEDED' or not row.get('files'):
        raise RuntimeError('Review requires downloaded successful preview')
    row['geometry_review'] = {'decision': decision, 'notes': note, 'reviewed_at': now(),
        'scope': 'Actual preview thumbnail visual check. Full turntable, rigging and Unity runtime remain unverified.'}
    save(OUT / 'ledger.json', value)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('op', choices=['init', 'status', 'preview', 'refine', 'poll', 'review'])
    parser.add_argument('--ids', default='all')
    parser.add_argument('--download', action='store_true')
    parser.add_argument('--decision', choices=['ACCEPTED_FOR_REFINE', 'REJECTED_GEOMETRY'])
    parser.add_argument('--note')
    args = parser.parse_args()
    identifiers = list(SPECS) if args.ids == 'all' else args.ids.split(',')
    if any(ident not in SPECS for ident in identifiers):
        parser.error('Unknown summon ID')
    if args.op == 'init':
        init(); status()
    elif args.op == 'status':
        status()
    elif args.op == 'poll':
        poll(args.download)
    elif args.op == 'review':
        if len(identifiers) != 1 or not args.decision or not args.note:
            parser.error('Review requires one ID, --decision and --note')
        review(identifiers[0], args.decision, args.note)
    else:
        for ident in identifiers:
            submit(ident, args.op)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Exception text is deliberately sanitized. Signed URLs must never reach logs.
        message = str(error) if isinstance(error, RuntimeError) else type(error).__name__
        print('STOPPED: ' + message, file=sys.stderr)
        sys.exit(1)
