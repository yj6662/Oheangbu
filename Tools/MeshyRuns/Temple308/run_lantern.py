"""#308 temple stone lantern (석등) with steam fittings - one Meshy text-to-3D preview + one refine (D308-43, SPEC-ARCH-TEMPLE-308 §12).
User 2026-10-08: "Meshy로 만들자. 한번에 잘 만들어줘." Authored text only; durable reservation before each paid POST; no POST retry.
  python Tools/MeshyRuns/Temple308/run_lantern.py init | preview | poll | accept | refine | status
"""
import hashlib, json, sys, urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'SpellVFX120'))
import run_stone_dokkaebi as H

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/World/Temple308/Meshy/Lantern'
H.OUT = OUT
CAP = 35
PROMPT = ('A traditional Korean Buddhist stone lantern (seokdeung) carved from weathered grey granite, 2 meters tall. '
          'From bottom to top: a square stepped base stone, a round lotus-petal pedestal, a slender octagonal pillar, a lotus-petal upper support, '
          'an octagonal light chamber with four open rectangular windows, a wide octagonal roof stone with slightly upturned corners, a lotus bud finial. '
          'Steampunk retrofit: one thin copper pipe runs up one side of the pillar held by small iron brackets, a small brass valve wheel and one round '
          'brass pressure gauge sit on the pedestal, a riveted copper band wraps the pillar. Single freestanding object, upright, symmetrical, clean '
          'silhouette, no ground plane, no flame, no glow.')
TEXTURE = ('Weathered pale grey granite with lichen stains and dark soot under the roof stone; aged copper pipe and band with green verdigris; '
           'tarnished brass valve and gauge; dark iron brackets. Matte surfaces, no emissive, no glowing light.')
ID = 'lantern'


def ledger(): return json.loads((OUT / 'ledger.json').read_text(encoding='utf-8'))


def init():
    if (OUT / 'ledger.json').exists(): print('ledger exists'); return
    assert len(PROMPT) <= 800 and len(TEXTURE) <= 800, (len(PROMPT), len(TEXTURE))
    balance = H.api('GET', '/openapi/v1/balance')
    H.save(OUT / 'ledger.json', {'schema': 2, 'started_at': H.now(), 'self_imposed_run_cap': CAP,
        'budget_basis': 'One Meshy 7 ultra preview (25) + one 2k PBR refine (10). No automatic paid retry.',
        'balance_start': balance.get('balance'), 'prices': {'meshy7_ultra_preview': 25, 'meshy7_refine_2k_pbr': 10},
        'input_policy': 'Authored text only.', 'automatic_post_retries': False, 'requests': []})
    print('balance', balance.get('balance'))


def submit(mode):
    value = ledger(); name = ID + '_' + mode
    if any(r['name'] == name for r in value['requests']): print('already submitted', name); return
    price = 25 if mode == 'preview' else 10
    if H.used(value) + price > CAP: raise RuntimeError('cap exceeded')
    if mode == 'preview':
        config = {'mode': 'preview', 'prompt': PROMPT, 'ai_model': 'meshy-7', 'model_type': 'standard', 'ultra_mode': True, 'should_remesh': True,
                  'topology': 'triangle', 'target_polycount': 9000, 'symmetry_mode': 'auto', 'target_formats': ['glb', 'fbx']}
    else:
        source = next(r for r in value['requests'] if r['name'] == ID + '_preview')
        if source['status'] != 'SUCCEEDED' or source.get('geometry_review', {}).get('decision') != 'ACCEPTED_FOR_REFINE': raise RuntimeError('preview not accepted')
        config = {'mode': 'refine', 'preview_task_id': source['task_id'], 'ai_model': 'meshy-7', 'texture_resolution': '2k', 'enable_pbr': True,
                  'texture_prompt': TEXTURE, 'target_formats': ['glb', 'fbx']}
    before = H.api('GET', '/openapi/v1/balance').get('balance', 0)
    if before < price: raise RuntimeError('insufficient balance %s' % before)
    row = {'name': name, 'id': ID, 'mode': mode, 'created_at': H.now(), 'config': config, 'expected_credits': price, 'reserved_credits': price,
           'consumed_credits': None, 'status': 'SUBMISSION_RESERVED', 'balance_before': before,
           'config_sha256': hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()}
    value['requests'].append(row); H.save(OUT / 'ledger.json', value)
    try:
        response = H.api('POST', H.ENDPOINT, config); row.update(task_id=response['result'], status='SUBMITTED')
    except Exception:
        row['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'; H.save(OUT / 'ledger.json', value); raise
    H.save(OUT / 'ledger.json', value); print(name, row['status'], row['task_id'])


def poll():
    value = ledger()
    for row in value['requests']:
        if not row.get('task_id'): continue
        r = H.api('GET', H.ENDPOINT + '/' + row['task_id']); H.save(OUT / '.private' / (row['name'] + '.json'), r)
        for k in ('status', 'progress', 'consumed_credits'):
            if k in r: row[k] = r[k]
        if r.get('status') == 'SUCCEEDED':
            row['files'] = H.download_files(r, row)
            thumb = r.get('thumbnail_url')
            if thumb:
                p = OUT / row['id'] / row['mode'] / 'thumbnail.png'
                if not p.exists(): p.write_bytes(urllib.request.urlopen(thumb, timeout=60).read())
        print(row['name'], row.get('status'), row.get('progress'), row.get('consumed_credits'))
    H.save(OUT / 'ledger.json', value)


def accept(note):
    value = ledger(); row = next(r for r in value['requests'] if r['name'] == ID + '_preview')
    row['geometry_review'] = {'decision': 'ACCEPTED_FOR_REFINE', 'note': note, 'at': H.now()}; H.save(OUT / 'ledger.json', value); print('accepted')


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'status'
    if cmd == 'init': init()
    elif cmd == 'preview': submit('preview')
    elif cmd == 'refine': submit('refine')
    elif cmd == 'poll': poll()
    elif cmd == 'accept': accept(' '.join(sys.argv[2:]))
    else: print(json.dumps([{k: r.get(k) for k in ('name', 'status', 'progress', 'consumed_credits', 'reserved_credits')} for r in ledger()['requests']], ensure_ascii=False))


# ---- second attempt (user 2026-10-08 "삼면도로 다시 (+30 크레딧)"): the three-view concept of Tools/Art/lantern_concept308.py as images
def image(multi=True):
    import base64
    global CAP
    CAP = 60
    value = ledger(); name = ID + '_image'
    if any(r['name'] == name and r['status'] not in ('HTTP_REFUSED_NO_CHARGE',) for r in value['requests']): print('already submitted', name); return
    views = ['front', 'side', 'back', 'quarter'] if multi else ['quarter']
    uris = ['data:image/png;base64,' + base64.b64encode((OUT / 'concept' / (v + '.png')).read_bytes()).decode() for v in views]
    config = {'ai_model': 'meshy-7', 'model_type': 'standard', 'should_texture': True, 'enable_pbr': True, 'texture_resolution': '2k', 'should_remesh': True,
              'topology': 'triangle', 'target_polycount': 9000, 'texture_prompt': TEXTURE, 'target_formats': ['glb', 'fbx']}
    endpoint = '/openapi/v1/multi-image-to-3d' if multi else '/openapi/v1/image-to-3d'
    before = H.api('GET', '/openapi/v1/balance').get('balance', 0)
    if before < 30: raise RuntimeError('insufficient balance %s' % before)
    row = {'name': name, 'id': ID, 'mode': 'image', 'endpoint': endpoint, 'views': views, 'created_at': H.now(), 'config': config, 'expected_credits': 30,
           'reserved_credits': 30, 'consumed_credits': None, 'status': 'SUBMISSION_RESERVED', 'balance_before': before}
    value['requests'].append(row); H.save(OUT / 'ledger.json', value)
    try:
        response = H.api('POST', endpoint, dict(config, **({'image_urls': uris} if multi else {'image_url': uris[0]})))
        row.update(task_id=response['result'], status='SUBMITTED')
    except RuntimeError as e:
        after = H.api('GET', '/openapi/v1/balance').get('balance', 0)
        row['status'] = 'HTTP_REFUSED_NO_CHARGE' if after == before and 'HTTP 4' in str(e) else 'SUBMISSION_UNCERTAIN_NO_RETRY'
        row['reserved_credits'] = 0 if row['status'] == 'HTTP_REFUSED_NO_CHARGE' else 30; row['error'] = str(e); row['name'] = name + ('_refused_multi' if multi else '_refused_single')
        H.save(OUT / 'ledger.json', value); print(row['status'], e, 'balance', before, '->', after); return
    H.save(OUT / 'ledger.json', value); print(name, row['status'], row['task_id'], endpoint)


def poll_image():
    value = ledger()
    for row in value['requests']:
        if row.get('mode') != 'image' or not row.get('task_id'): continue
        r = H.api('GET', row['endpoint'] + '/' + row['task_id']); H.save(OUT / '.private' / (row['name'] + '.json'), r)
        for k in ('status', 'progress', 'consumed_credits'):
            if k in r: row[k] = r[k]
        if r.get('status') == 'SUCCEEDED':
            row['files'] = H.download_files(r, row); thumb = r.get('thumbnail_url')
            if thumb:
                p = OUT / row['id'] / row['mode'] / 'thumbnail.png'
                if not p.exists(): p.write_bytes(urllib.request.urlopen(thumb, timeout=60).read())
        print(row['name'], row.get('status'), row.get('progress'), row.get('consumed_credits'))
    H.save(OUT / 'ledger.json', value)


if __name__ == '__main__' and len(sys.argv) > 1 and sys.argv[1] in ('image', 'image-single', 'poll-image'):
    {'image': lambda: image(True), 'image-single': lambda: image(False), 'poll-image': poll_image}[sys.argv[1]]()
