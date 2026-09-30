"""User-approved (2026-09-27) Meshy rock masses for the highland sections. Durable POST reservations.

python Tools/MeshyRuns/highlands293.py submit   # once; never retried automatically
python Tools/MeshyRuns/highlands293.py poll     # until both SUCCEEDED; downloads GLB/FBX + thumbnail
Budget: Step 1 = 2 models (≈50 credits), Step 2 = 4 models (≈100 credits, user-approved 2026-09-27; plan limit 8).
Ledger: Art/World/Compact/Rebuild/Highlands293/Meshy/ledger.json
"""
import importlib.util, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('meshy_base', ROOT / 'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
m.OUT = ROOT / 'Art/World/Compact/Rebuild/Highlands293/Meshy'
LEDGER = m.OUT / 'ledger.json'
CAP = 150  # Step 1 50 + Step 2 100
PROMPTS = {
    'CheongrimSheetingOutcrop': 'One broad natural Korean granite outcrop from Seoraksan, exposed bedrock rising from a forested mountain slope. '
    'Thick curved exfoliation sheets peel parallel to the rounded surface in two or three layers with torn broken edges, '
    'a few deep unequal vertical joints cut down through the mass and stop inside it, one side a steep jointed face, the other a rounded dome shoulder. '
    'Coarse weathered granite detail at many scales in one coherent solid mass, thick uneven base for burying in soil. '
    'Complete standalone 3D environment mesh, all sides modeled. No plants, trees, ground plane, pedestal, buildings, stairs, text. '
    'No stack of separate boulders, no clay lumps, no tiled polygons, no brick slabs, no sedimentary stripes.',
    'PineViewpointRock': 'A natural granite ledge rock at a Korean mountain viewpoint, wider than tall, with a gently sloping flat upper shelf '
    'about the size of a small room, one side dropping as a short vertical jointed cliff, the other side buried into the slope. '
    'Deep narrow cracks and a pocket in the top surface where a pine tree could root, chipped rounded edges, fine weathered grain. '
    'One coherent solid rock, realistic geology and gravity, thick base for burying in terrain. Complete standalone 3D environment mesh, all sides modeled. '
    'No trees, plants, ground plane, railings, buildings, stairs, figures or text. No pile of stones, no clay bubbles, no tiled polygons.',
    # Step 2 (one per realm)
    'HwanggyeongGraniteTor': 'A tall natural granite tor from Inwangsan in Seoul: two or three huge rounded granite boulders stacked on a bedrock base, '
    'smooth weathered pale granite with rounded shoulders, deep curved horizontal cracks between the blocks, shallow exfoliation sheets, '
    'a few vertical fractures and pitted hollows on one side. Taller than wide, a standing stone cluster on a mountain ridge. '
    'One coherent solid mass with realistic gravity and contact between the blocks, thick base for burying in soil. '
    'Complete standalone 3D environment mesh, all sides modeled. No plants, trees, ground plane, pedestal, buildings, stairs, carvings, figures or text. '
    'No clay lumps, no tiled polygons, no brick-like blocks.',
    'HyeongangWetOverhang': 'A dark wet overhanging gorge rock from a Korean river valley: a thick slab of weathered dark grey rock whose upper part leans '
    'far forward over a hollow, like a cliff roof above a narrow path. Water-worn smooth undercut beneath, rough fractured top with shallow ledges, '
    'vertical water streaks and damp stains running down the face, a few deep cracks. One coherent solid mass, realistic geology and gravity, '
    'thick base for burying in the slope. Complete standalone 3D environment mesh, all sides modeled. '
    'No water, plants, ground plane, buildings, stairs, bridges, figures or text. No stack of separate stones, no clay bubbles, no tiled polygons.',
    'JeokroScorchedOutcrop': 'A low weathered rust-red granite outcrop on an old burnt battlefield hillside in Korea: blocky cubic joint blocks with '
    'strongly rounded eroded edges, cracked into large angular pieces that still form one mass, crumbling corners, a short scree apron of broken '
    'fragments at its base, surfaces blackened by old fire soot in patches. Wider than tall. One coherent solid mass, realistic geology and gravity, '
    'thick base for burying in soil. Complete standalone 3D environment mesh, all sides modeled. '
    'No plants, trees, ground plane, weapons, bones, buildings, stairs, fire, flames, figures or text. No clay lumps, no tiled polygons, no brick walls.',
    'CheolongBeddedSlab': 'A grey-blue horizontally bedded rock cliff block from a Korean frontier mountain: thick flat stone layers 0.4 to 1.2 m thick '
    'stacked like pages, each layer stepping back a little to form narrow ledges, broken slab edges, a few vertical joints cutting through the layers, '
    'one end collapsed into tilted fallen slabs. Wider than tall. One coherent solid mass, realistic geology and gravity, thick base for burying in the slope. '
    'Complete standalone 3D environment mesh, all sides modeled. No plants, trees, ground plane, walls, buildings, stairs, figures or text. '
    'No clay lumps, no tiled polygons, no bricks, no perfectly regular stripes.',
}


def run(op):
    d = json.loads(LEDGER.read_text()) if LEDGER.exists() else dict(model='meshy-7.1', self_imposed_cap=CAP, automatic_post_retries=False,
                                                                   approved='user 2026-09-27: 2 generations (~50 credits)',
                                                                   pricing_source='https://docs.meshy.ai/en/api/pricing', requests=[])
    d['self_imposed_cap'] = CAP
    if 'step2' not in d.get('approved', ''):
        d['approved'] = d.get('approved', '') + '; user 2026-09-27 step2: 4 generations (~100 credits)'
    if op == 'submit':
        for name, prompt in PROMPTS.items():
            assert len(prompt) <= 800, (name, len(prompt))
            if any(r['name'] == name for r in d['requests']):
                continue
            if sum(r['reserved_credits'] for r in d['requests']) + 25 > CAP:
                raise RuntimeError('Run cap exceeded')
            if m.api('GET', '/openapi/v1/balance').get('balance', 0) < 25:
                raise RuntimeError('Insufficient existing credits')
            cfg = dict(mode='preview', prompt=prompt, ai_model='meshy-7.1', model_type='standard', geometry_resolution='4k', should_remesh=True,
                       target_polycount=60000, topology='triangle', target_formats=['glb', 'fbx'])
            row = dict(name=name, id=name, mode='preview', config=cfg, reserved_credits=25, status='SUBMISSION_RESERVED', created_at=m.now())
            d['requests'].append(row); m.save(LEDGER, d)
            try:
                row.update(task_id=m.api('POST', m.ENDPOINT, cfg)['result'], status='SUBMITTED')
            except Exception:
                row['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'; m.save(LEDGER, d); raise
            m.save(LEDGER, d)
    elif op == 'poll':
        for row in d['requests']:
            if not row.get('task_id') or row.get('files') or row['status'] in ('FAILED', 'CANCELED'):
                continue
            r = m.api('GET', m.ENDPOINT + '/' + row['task_id']); m.save(m.OUT / '.private' / (row['name'] + '.json'), r)
            for k in ('status', 'progress', 'consumed_credits'):
                row[k] = r.get(k)
            m.save(LEDGER, d)
            if row['status'] == 'SUCCEEDED':
                row['files'] = m.download_files(r, row); m.save(LEDGER, d)
    print(json.dumps([{k: r.get(k) for k in ('name', 'status', 'progress', 'consumed_credits')} for r in d['requests']]))


if __name__ == '__main__':
    try:
        run(sys.argv[1])
    except Exception as e:
        print(str(e) if isinstance(e, RuntimeError) else type(e).__name__); sys.exit(1)
