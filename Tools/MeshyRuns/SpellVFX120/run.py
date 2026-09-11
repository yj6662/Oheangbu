"""One paid preview/refine per summon; durable reservations, no POST retries.

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
OUT = ROOT / 'Art/SpellVFX120/MeshySummons'
BASE = 'https://api.meshy.ai'
ENDPOINT = '/openapi/v2/text-to-3d'
CAP = 175
SPECS = {
    '016_ACF0': ('곰', 'RootDeer',
        'A fully three-dimensional ancient root-and-vine guardian deer inspired by Korean minhwa deer and longevity paintings. Dignified heavy neck, long deer torso, four distinct weight-bearing legs with substantial cloven hooves, clear open space between legs, organic branching antlers that read as old living wood. Broad interwoven roots form anatomy, thick continuous wooden tendons, restrained carved surface grain, a few broad leaf forms only. Alert neutral standing pose, head raised, legs slightly apart, antlers and ears clearly separated. Believable flowing anatomy with a readable silhouette from front, sides and back. Game creature, complete freestanding body. No pedestal, ground slab, text, clothing, jewelry, thin tangled twigs, tiny foliage clusters or toy proportions.',
        'Weathered grey-brown hardwood and old roots, deep charcoal grain, subdued moss green in crevices. Natural matte bark, low saturation Korean ink-painting palette, subtle weathering, dark hooves. Broad tonal planes, no bright magic glow, no gold, no jewelry, no painted text.'),
    '040_B188': ('놈', 'FireHaetae',
        'A powerful four-legged fire guardian beast inspired by traditional Korean haetae stone sculptures. Broad short muzzle, large watchful eyes under curled eyebrows, strong chest and haunches, thick rounded paws. Sculptural mane shaped as broad curling flame masses around neck and back, with substantial three-dimensional curved volume rather than thin cards. A short sturdy horn, curled tail. Functional neutral standing stance with all four legs individually separated and open gaps under belly and between paws, head lifted, mouth slightly open. Complete freestanding creature with convincing anatomy and dignified Korean guardian character, not a western dragon. No pedestal, ground slab, writing, armor, jewelry, attached scene, tiny floating particles or rigid rectangular blocks.',
        'Dark charcoal and weathered reddish clay body, muted burnt sienna flame mane with sparse warm ember orange recessed accents. Matte, rough sculptural surfaces with natural carved grooves. Low saturation, restrained Korean temple guardian palette. No luminous neon, jewels, metallic armor or painted symbols.'),
    '064_BAB8': ('몸', 'StoneJangseung',
        'A heavy anthropomorphic stone guardian inspired by Korean seok-jangseung village guardian sculptures. Tall rectangular expressive face with exaggerated watchful eyes, broad carved nose and prominently exposed uneven large teeth. Broad shoulders, thick torso, two strong separated arms ending in recognizable closed hands, two sturdy separated legs and broad feet. Substantial rounded weathered granite volumes, sculpted organic joints and curved muscular stone contours, not stacked cubes. Neutral ready stance, elbows slightly bent, hands held away from torso, clear gaps at armpits and between thighs. Entire freestanding three-dimensional creature, resolved front and back. No base, pedestal, ground slab, inscriptions, text, hat signage, armor, Minecraft blocks, voxel shapes or toy style.',
        'Old grey-brown granite, matte stone with weathered pores, dark ink-like recesses around brows, nose and teeth. Teeth are carved from the same pale stone, not gold. Restrained warm-grey low saturation tones, subtle earthy seams. No colorful painted signs, lettering, glowing runes, metal armor or jewels.'),
    '088_C19C': ('솜', 'IronMinhwaTiger',
        'A proud forged-iron guardian tiger inspired by the proportions and expression of Korean minhwa tiger paintings. Wide confident face, rounded muzzle and paws, alert slightly exaggerated eyes, curved powerful back, substantial haunches, continuous S-shaped tail held clear of body. Four distinct legs with clearly separated rounded paws, open gaps beneath belly. Strong organic tiger anatomy rendered as a continuous forged metal animal, shallow sculptural stripe grooves flowing across the body, not plates of armor. Functional neutral standing pose with head slightly raised and legs comfortably apart, readable front, side and back silhouettes. Complete freestanding three-dimensional beast. No pedestal, base, writing, jewels, armor, weapons, mechanical hinges, toy style or tangled thin whiskers.',
        'Blackened forged iron with restrained dark steel highlights, matte hammered surface and gentle graphite-grey ridges. Recessed tiger stripes in darker charcoal, subtle oxidized brown weathering. Dignified low saturation metal, no polished gold, no gems, no attached armor, no colorful paint, no neon glow.'),
    '112_C634': ('옴', 'InkWaterImugi',
        'A water guardian imugi inspired by Korean ink-painting serpentine dragons. Long thick muscular horizontal S-shaped body, substantial overlapping scales, broad expressive dragon head, branching antlers and flowing water-like mane built from thick rounded sculptural curls. Four small but distinct clawed legs spaced along the body, all separated from the torso, thick tapering tail continuing the horizontal S silhouette. Calm functional hovering-ready neutral pose with space under the belly and clear gaps between limbs and body, head looking forward. Freestanding 3D creature with resolved underside and back. Heavy flowing anatomy, not a western winged dragon. No wings, pedestal, sea base, thin fin sheets, hair cards, text, jewels, armor, tiny floating particles or narrow tangled strands.',
        'Deep ink charcoal and subdued blue-grey scales, broad soft grey water-mane curls, darker recessed scales, muted pale horn tips. Rough natural sculptural surface, low saturation Korean ink wash character with restrained cool edge tones. No neon blue glow, no rainbow colors, gold, armor, jewels or painted lettering.'),
}


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
    save(OUT / 'ledger.json', {'schema': 1, 'started_at': now(), 'self_imposed_run_cap': CAP,
        'budget_basis': 'Conservative implementation cap: 5 previews at 25 plus 5 refinements at 10. This is not described as a user-set credit limit.',
        'balance_start': balance.get('balance'), 'price_checked_at': now(),
        'price_source': 'https://docs.meshy.ai/en/api/pricing',
        'api_source': 'https://docs.meshy.ai/en/api/text-to-3d',
        'prices': {'meshy7_ultra_preview': 25, 'meshy7_refine_2k_pbr': 10},
        'input_policy': 'Authored text only. No KTP or other paid asset images uploaded.',
        'automatic_post_retries': False, 'requests': []})


def status():
    value = ledger()
    print(json.dumps({'self_imposed_cap': CAP, 'spent_or_reserved': used(value),
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
    if api('GET', '/openapi/v1/balance').get('balance', 0) < price:
        raise RuntimeError('Insufficient existing API balance; no purchase attempted')
    row = {'name': name, 'id': ident, 'glyph': glyph, 'family': family, 'mode': mode,
        'created_at': now(), 'config': config, 'expected_credits': price,
        'reserved_credits': price, 'consumed_credits': None, 'status': 'SUBMISSION_RESERVED',
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
    dest = OUT / 'Source' / row['id'] / row['mode']
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
