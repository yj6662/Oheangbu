"""#308 steam-temple part kit - Meshy multi-image-to-3D from the Codex three-view concepts (D308-46, SPEC-ARCH-TEMPLE-308 §13).
User 2026-10-08: batch 1 (six parts, 180 credits) approved after seeing the concept sheets ("진행해줘").
One paid POST per part, durable reservation before the POST, no POST retry. The concept sheet is cut into its three views.
  python Tools/MeshyRuns/Temple308/run_parts.py submit <part> [<part> ...] | poll | status
"""
import base64, io, json, sys, urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'SpellVFX120'))
import run_stone_dokkaebi as H
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/World/Temple308/Meshy/Parts'
H.OUT = OUT
CONCEPTS = ROOT / 'Art/World/Temple308/Concepts'
ENDPOINT = '/openapi/v1/multi-image-to-3d'
CAP = 300                                     # batch 1 = 180 (approved); batch 2 = 90 (cylinder, bell, sutra case) approved 2026-10-08 ("1, 2 뽑고 조립해줘")
BATCH1 = ['part_firebox', 'part_vessel', 'part_canopy', 'part_collar', 'part_flywheel', 'part_gear']
TEXTURE = {
    'part_firebox': 'Black riveted iron plate with rust in the seams, dark bronze lotus bands, soot around the door. Matte, no emissive.',
    'part_vessel': 'Dark oxidised bronze with green verdigris in the recesses, bolted bronze porthole rings, black glass. No emissive.',
    'part_canopy': 'Hammered copper roof panels with green verdigris streaks, dark bronze ribs and brackets, bronze lotus bud. No emissive.',
    'part_collar': 'Cast bronze lotus petals, dark patina with green verdigris in the hollows, worn brighter edges. No emissive.',
    'part_flywheel': 'Cast iron with rust and oil stains, bronze lotus hub with verdigris, brass crank pin. No emissive.',
    'part_gear': 'Cast bronze gear, dark patina, worn brighter tooth faces, verdigris near the hub. No emissive.',
    'part_cylinder': 'Cast iron cylinder with rust, hammered copper lagging band with verdigris, bronze fittings. No emissive.',
    'part_bell': 'Dark oxidised bronze temple bell with green verdigris, worn relief. No emissive.',
    'part_stand': 'Black cast iron with rust in the hollows and oil stains, bronze bearing cap with green verdigris, worn brighter edges. No emissive.',
    'part_case': 'Cast bronze frame with dark patina and green verdigris, pierced flower-lattice panels over a black interior, worn brighter edges. No emissive.',
}
POLY = {'part_flywheel': 7000, 'part_gear': 8000, 'part_collar': 7000, 'part_case': 12000}
CUT = {'part_cylinder': (0, .25, .74, 1), 'part_case': (0, .37, .635, 1), 'part_stand': (0, .42, .58, 1)}     # where the three views sit when they are not equal thirds


def ledger():
    p = OUT / 'ledger.json'
    if p.exists(): return json.loads(p.read_text(encoding='utf-8'))
    balance = H.api('GET', '/openapi/v1/balance')
    value = {'schema': 2, 'started_at': H.now(), 'self_imposed_run_cap': CAP, 'budget_basis': 'Textured Meshy 7 multi-image requests at 30 each; no automatic paid retry.',
             'balance_start': balance.get('balance'), 'input_policy': 'Concept sheets made with the Codex CLI image generation (authored prompts); no third-party asset images.',
             'automatic_post_retries': False, 'requests': []}
    H.save(p, value); return value


def views(part):
    sheet = Image.open(CONCEPTS / (part + '.png')).convert('RGB'); w, h = sheet.size; uris = []
    (OUT / part / 'input').mkdir(parents=True, exist_ok=True)
    for i, name in enumerate(('front', 'side', 'back')):
        c = CUT.get(part, (0, 1 / 3, 2 / 3, 1)); v = sheet.crop((int(c[i] * w), 0, int(c[i + 1] * w), h)); v.save(OUT / part / 'input' / (name + '.png'))
        b = io.BytesIO(); v.save(b, 'PNG'); uris.append('data:image/png;base64,' + base64.b64encode(b.getvalue()).decode())
    return uris


def submit(part):
    value = ledger()
    if any(r['name'] == part for r in value['requests']): print('already submitted', part); return
    if H.used(value) + 30 > CAP: raise RuntimeError('cap exceeded')
    config = {'ai_model': 'meshy-7', 'model_type': 'standard', 'should_texture': True, 'enable_pbr': True, 'texture_resolution': '2k', 'should_remesh': True,
              'topology': 'triangle', 'target_polycount': POLY.get(part, 9000), 'texture_prompt': TEXTURE[part], 'target_formats': ['glb', 'fbx']}
    uris = views(part)
    before = H.api('GET', '/openapi/v1/balance').get('balance', 0)
    if before < 30: raise RuntimeError('insufficient balance %s' % before)
    row = {'name': part, 'id': part, 'mode': 'image', 'created_at': H.now(), 'config': config, 'expected_credits': 30, 'reserved_credits': 30, 'consumed_credits': None,
           'status': 'SUBMISSION_RESERVED', 'balance_before': before}
    value['requests'].append(row); H.save(OUT / 'ledger.json', value)
    try:
        response = H.api('POST', ENDPOINT, dict(config, image_urls=uris)); row.update(task_id=response['result'], status='SUBMITTED')
    except Exception as e:
        row['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'; row['error'] = str(e); H.save(OUT / 'ledger.json', value); raise
    H.save(OUT / 'ledger.json', value); print(part, row['status'], row['task_id'])


def poll():
    value = ledger(); done = 0
    for row in value['requests']:
        if not row.get('task_id'): continue
        if row.get('status') == 'SUCCEEDED' and row.get('files'): done += 1; continue
        r = H.api('GET', ENDPOINT + '/' + row['task_id']); H.save(OUT / '.private' / (row['name'] + '.json'), r)
        for k in ('status', 'progress', 'consumed_credits'):
            if k in r: row[k] = r[k]
        if r.get('status') == 'SUCCEEDED':
            row['files'] = H.download_files(r, row); done += 1
            thumb = r.get('thumbnail_url')
            if thumb: (OUT / row['id'] / 'image' / 'thumbnail.png').write_bytes(urllib.request.urlopen(thumb, timeout=60).read())
        print(row['name'], row.get('status'), row.get('progress'), row.get('consumed_credits'))
    H.save(OUT / 'ledger.json', value)
    pending = [r['name'] for r in value['requests'] if r.get('status') not in ('SUCCEEDED', 'FAILED', 'CANCELED', 'EXPIRED')]
    print('PENDING', len(pending), 'DONE', done)


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'status'
    if cmd == 'submit':
        for part in (sys.argv[2:] or BATCH1): submit(part)
    elif cmd == 'poll': poll()
    else: print(json.dumps([{k: r.get(k) for k in ('name', 'status', 'progress', 'consumed_credits')} for r in ledger()['requests']], ensure_ascii=False))
