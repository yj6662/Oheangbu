"""#308 stage n2 - `rocks308`: boulders at the foot of the terrain cliffs. Offline, deterministic.
SPEC-WORLD-CLIFF-BOUNDARY-308 "n2 단계". Two kinds: fallen blocks on the ground just in front of a face (outside the cliff body,
never on a route corridor, wet or protected cell) and toe blocks half buried in the first metres of the face itself.

  python Tools/Art/rocks308.py Tools/Unity/Stage308_rugged/rugged308.json   # -> Tools/Unity/Stage308_rugged/rocks308.json
"""
import hashlib, json, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402


def main():
    B.utf8(); cfg_p = Path(sys.argv[1]); cfg = json.loads(cfg_p.read_text(encoding='utf-8')); rk = cfg['rocks']; stage = cfg['stage']
    h = B.load_height(); t1 = B.load_height(B.ROOT / cfg['inputs']['prev_height']); f = B.load_height(B.PL / f'Stage/height_p{stage}.bytes'); X, Z = B.grid_xz()
    raised = (t1 - h) > cfg['footprint']['min_fill_m']; lab, n = ndi.label(raised); size = ndi.sum(raised, lab, np.arange(1, n + 1))
    F = np.isin(lab, 1 + np.flatnonzero(size >= cfg['footprint']['min_cells']))
    d_in = ndi.distance_transform_edt(F, sampling=4.0); d_out = ndi.distance_transform_edt(~F, sampling=4.0)
    hard = B.wet() | B.protected()
    lat = json.loads((B.ROOT / rk['routes']).read_text(encoding='utf-8'))
    for r_ in lat['routes']: hard |= B.stamp(np.asarray(r_['pts'], float), r_['width'] / 2 + rk['route_margin_m'], X, Z)
    for kp in cfg.get('keep', []): hard |= np.hypot(X - kp['at'][0], Z - kp['at'][1]) <= kp['radius_m']
    sl = B.slope_deg(f); seed = int.from_bytes(hashlib.sha256(f"{cfg['seed']}:rocks".encode()).digest()[:4], 'little'); rng = np.random.default_rng(seed)
    out = []; taken = set()

    def pick(mask, count, spacing, kind):
        ii, jj = np.nonzero(mask); order = rng.permutation(len(ii)); got = 0
        for k in order:
            if got >= count: break
            i, j = int(ii[k]), int(jj[k]); key = (int(i * 4 // spacing), int(j * 4 // spacing))
            if any((key[0] + a, key[1] + b) in taken for a in (-1, 0, 1) for b in (-1, 0, 1)): continue
            taken.add(key); x = j * 4 + rng.uniform(-1.6, 1.6); z = i * 4 + rng.uniform(-1.6, 1.6); S = rk[kind]
            realm = next((nm for nm, poly in B.REALMS.items() if B.poly_mask(poly, np.array([[x]]), np.array([[z]]))[0, 0]), None)
            out.append(dict(prefab=str(rng.choice(rk.get('by_realm', {}).get(realm) or rk['prefabs'])), realm=realm or '', x=round(float(x), 2), y=round(float(B.sample_tri(f, x, z)), 2), z=round(float(z), 2), yaw=round(float(rng.uniform(0, 360)), 1),
                            size=round(float(rng.uniform(S['size'][0], S['size'][1]) ** 1.0), 2), sink=round(float(rng.uniform(S['sink'][0], S['sink'][1])), 2),
                            tiltX=round(float(rng.uniform(-S['tilt'], S['tilt'])), 1), tiltZ=round(float(rng.uniform(-S['tilt'], S['tilt'])), 1), kind=kind)); got += 1
        return got

    n_fallen = pick(~F & ~hard & (d_out <= rk['fallen']['within_m']) & (sl < 30), rk['fallen']['count'], rk['fallen']['spacing_m'], 'fallen')
    n_toe = pick(F & ~hard & (d_in <= rk['toe']['within_m']), rk['toe']['count'], rk['toe']['spacing_m'], 'toe')
    dst = cfg_p.parent / 'rocks308.json'
    dst.write_text(json.dumps(dict(version=cfg['version'], stage=stage, height_sha256=B.sha256(B.PL / f'Stage/height_p{stage}.bytes'), prefabDir=rk['prefabDir'], counts=dict(fallen=n_fallen, toe=n_toe, by_realm={r_: sum(1 for o in out if o['realm'] == r_) for r_ in sorted(set(o['realm'] for o in out))}), rocks=out), ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    print(f'rocks308: fallen {n_fallen}, toe {n_toe} -> {dst}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
