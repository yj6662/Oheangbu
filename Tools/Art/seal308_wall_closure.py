"""#308 D308-3b - the 4 m closure proof (Tools/Art/seal308_closure.py, unchanged) run on the 관문 성벽 footprint.

  python Tools/Art/seal308_wall_closure.py [--seed newgame|routes]
      -> Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/closure/
         seal308-{closed,open,baseline}.json + .png, closure308.txt, beyond308.json, _work/reach-*.npy

seal308_closure.py reads Out/Seal308/footprint308.json (wings / bastions / panel_closed / panel_open); seal308_wall.py now writes the
wall footprint there (the palisade footprint is kept in Out/Seal308/_palisade_v1/). This wrapper only redirects the proof's outputs
(REP -> Out/Seal308/closure, OUT -> Out/Seal308/closure) so the D308-3 palisade results in Enclosure305/Seal308/ and
Enclosure305/Out/beyond308.json stay untouched until the wall is accepted. The proof logic, lattice and thresholds are the module's
own: closed = wall + gate leaves + 4 capital doors closed from the new-game seed (AC-S2: late content reached 0, EA places n/n);
open = the south-gate fact (gate span + doors open; AC-S3 every place / rest / trail entry the lattice reaches without a seal);
sensitivity = each seal piece removed must leak; the wall's own pieces too (review fix): wing W0 / W1 removed must leak, the end 치
(bastions) are reported (at 4 m the wing band alone may already reach the seam shell end - the 치 is then a visual / physical joint, not a
lattice joint; the editor seal-check / check-scene probe it).
Idempotent (review fix): a rerun whose results equal the previous ones (ignoring the `generated` stamps and the ledger's clock) keeps the
previous files byte for byte, so beyond308.json's sha (WorldSealProfile308.SourceHash, the seal-scene input hash of all three scenes) only
changes when the proof itself changes.
"""
import copy, json, re, sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import seal308_closure as C                                                   # noqa: E402

OUTD = C.ENC / 'Out/Seal308'
C.FOOT = OUTD / 'footprint308.json'
C.REP = OUTD / 'closure'
C.OUT = OUTD / 'closure'


KEEP = ('seal308-closed.json', 'seal308-open.json', 'seal308-baseline.json', 'beyond308.json', 'closure308.txt', 'summary.json')
STAMP = re.compile(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}')


def _norm(name, raw):
    """Content without the clock: JSON minus every `generated` key, text with the ISO stamps blanked."""
    txt = raw.decode('utf-8', errors='replace')
    if name.endswith('.json'):
        def strip(o):
            if isinstance(o, dict): return {k: strip(v) for k, v in o.items() if k != 'generated'}
            if isinstance(o, list): return [strip(v) for v in o]
            return o
        try: return json.dumps(strip(json.loads(txt)), sort_keys=True, ensure_ascii=False)
        except ValueError: return txt
    return STAMP.sub('<t>', txt)


def _without(foot, piece):
    f = copy.deepcopy(foot)
    if piece.startswith('wing'): f['wings'] = [w for k, w in enumerate(f['wings']) if k != int(piece[4:])]
    else: f['bastions'] = [b for k, b in enumerate(f['bastions']) if k != 'AB'.index(piece[-1])]
    f['source'] = f'{C.FOOT} without {piece}'; return f


def main():
    seed = 'routes' if '--seed' in sys.argv and sys.argv[sys.argv.index('--seed') + 1] == 'routes' else 'newgame'
    foot = json.loads(C.FOOT.read_text(encoding='utf-8'))
    if foot.get('kind') != 'wall': raise SystemExit(f'{C.FOOT} is not the wall footprint (run Tools/Art/seal308_wall.py first)')
    C.REP.mkdir(parents=True, exist_ok=True)
    before = {n: (C.REP / n).read_bytes() for n in KEEP if (C.REP / n).exists()}
    G = C.lattice(); C.log('lattice ready (wall footprint', C.FOOT, ')')
    dc, rc, _ = C.run('closed', seed, False, G)
    do, ro, _ = C.run('open', seed, False, G)
    db, rb, _ = C.run('baseline', seed, False, G)
    # AC-S3 against the baseline (the same finalisation as seal308_closure.py `all`)
    reqk = ('place', 'trail_entry', 'arena', 'rest', 'rest_point', 'rest_marker')
    base = {(i['kind'], i['id']): i['reached'] for i in db['items']}
    req = [i for i in do['items'] if i['kind'] in reqk and not i['underground']]
    caused = [i for i in req if base.get((i['kind'], i['id'])) and not i['reached']]
    lattice_out = [i for i in req if not base.get((i['kind'], i['id']))]
    do['verdict'].update({'AC-S3': 'PASS' if not caused else 'FAIL', 'missed_caused_by_seal': len(caused),
                          'lattice_unreachable_without_seal': [f"{i['kind']} {i['id']}" for i in lattice_out],
                          'n_of_n': f"{sum(i['reached'] for i in req if base.get((i['kind'], i['id'])))}/{sum(1 for i in req if base.get((i['kind'], i['id'])))} (items the lattice reaches at all)"})
    do['open_vs_baseline'] = dict(open_reach_ha=do['reach_ha'], baseline_reach_ha=db['reach_ha'],
                                  open_minus_baseline_ha=round(float((ro & ~rb).sum()) * 16 / 1e4, 1), baseline_minus_open_ha=round(float((rb & ~ro).sum()) * 16 / 1e4, 1))
    (C.REP / 'seal308-open.json').write_text(json.dumps(do, ensure_ascii=False, indent=1), encoding='utf-8')
    C.log('open', json.dumps(do['verdict'], ensure_ascii=False))
    bey = C.beyond(rc, ro, G, dc['items'], dc, do) if seed == 'newgame' else None
    sens = []
    for x in ['panel', 'doors'] + list(C.E305.SEAL308):
        d, _, _ = C.run('closed', seed, False, G, omit=x, write=False)
        sens.append(dict(omit=x, late_reached=d['verdict']['late_content_reached'], ac_s2=d['verdict']['AC-S2']))
    # the wall's own pieces (footprint wings / end 치), one at a time (review fix)
    pal0 = C.palisade
    try:
        for piece in [f'wing{k}' for k in range(len(foot['wings']))] + ['bastion' + 'AB'[k] for k in range(len(foot.get('bastions', [])))]:
            C.palisade = lambda draft, f=_without(dict(foot, source=str(C.FOOT)), piece): f
            d, _, _ = C.run('closed', seed, False, G, write=False)
            sens.append(dict(omit=f'wall:{piece}', late_reached=d['verdict']['late_content_reached'], ac_s2=d['verdict']['AC-S2']))
    finally:
        C.palisade = pal0
    C.ledger(dc, do, bey, db, sens)
    led = C.REP / 'closure308.txt'
    led.write_text(led.read_text(encoding='utf-8').replace(' | palisade ', ' | footprint ', 1), encoding='utf-8')
    summary = dict(closed=dc['verdict'], open=do['verdict'], sensitivity=sens, beyond=None if not bey else dict(ha=bey[0]['beyond_ha'], rings=len(bey[0]['beyond_rings']),
                   disagreements=f'{bey[1]}/{bey[2]}', item_check=bey[0]['item_check'], ea_rest_ids=bey[0]['ea_rest_ids']))
    (C.REP / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding='utf-8')
    kept = []
    for n, raw in before.items():
        f = C.REP / n
        if f.exists() and f.read_bytes() != raw and _norm(n, f.read_bytes()) == _norm(n, raw): f.write_bytes(raw); kept.append(n)
    print(json.dumps(summary, ensure_ascii=False))
    print('unchanged results, previous files kept byte for byte:', kept or 'none')
    # wings and every seam / panel / door piece must leak; the end 치 are informational at 4 m (see the module note)
    ok = (dc['verdict'].get('AC-S2') == 'PASS' and do['verdict'].get('AC-S3') == 'PASS' and dc['verdict'].get('AC-S4') == 'PASS'
          and all(s['late_reached'] > 0 for s in sens if not s['omit'].startswith('wall:bastion')))
    return ok


if __name__ == '__main__':
    sys.exit(0 if main() else 1)
