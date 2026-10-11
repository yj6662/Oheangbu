"""#308 realm outbuildings - `hamlet308`. Offline, deterministic. SPEC-WORLD-REALM-IDENTITY-308 (D308-57).
Small buildings beside the roads, each realm in its own form (no mesh is shared between realms):
  Cheongrim   log cabins under plank roofs weighted with stones
  Hwanggyeong plastered houses with timber posts under tile roofs
  Cheolong    low rubble-walled huts (barrack form) under plank roofs
  Jeokro      what the fire left: charred posts, a fallen ridge, wall stumps
  Hyeongang   plank houses on stilts with a ladder

  python Tools/Art/hamlet308.py Tools/Unity/Stage308_dungeon/hamlet308.json   # -> Art/World/Compact/Rebuild/Hamlet308/{Meshes/*.json, layout.json}

Outside the EA the buildings are looks only (D308-55): one BoxCollider each, no door to walk through, no light.
"""
import json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from dungeon308 import Kit, seed_of, UP                                    # noqa: E402

OUT = B.RB / 'Hamlet308'


def gable(K, name, x0, x1, z0, z1, y_eave, rise, over, mat, sag=0.0):
    """a ridge roof (ridge along local x): two slopes with a little thickness, drawn from both sides."""
    m = K.mesh(name); tri = m.sub.setdefault(mat, []); zc = (z0 + z1) / 2; xa, xb = x0 - over, x1 + over
    for sgn, ze in ((-1, z0 - over), (1, z1 + over)):
        ye = y_eave - over * rise / max(0.01, (z1 - z0) / 2)
        q = [K.w(xa, ye, ze), K.w(xb, ye, ze), K.w(xb, y_eave + rise - sag, zc), K.w(xa, y_eave + rise, zc)]
        n_ = np.cross(q[1] - q[0], q[3] - q[0]); n_ /= np.linalg.norm(n_)
        if n_[1] < 0: n_ = -n_
        for flip, off in ((False, 0.0), (True, -0.09)):
            b0 = len(m.v)
            for i_, pt in enumerate(q): m.v.append(pt + n_ * off); m.n.append(-n_ if flip else n_); m.uv.append((float(i_ in (1, 2)) * (xb - xa), float(i_ >= 2) * 3.0))
            order = [0, 1, 2, 0, 2, 3] if (np.cross(q[1] - q[0], q[2] - q[0])[1] > 0) != flip else [0, 2, 1, 0, 3, 2]
            tri += [b0 + k for k in order]


def house(K, style, rng, w, d, yg_min, th):
    """one building in local coords: footprint [0, w] x [0, d], floor at y 0 (the podium top); ground as low as yg_min."""
    h = rng.uniform(2.1, 2.5)
    if style == 'log':
        K.box('Base', -0.15, w + 0.15, yg_min - 0.5, 0.25, -0.15, d + 0.15, th['base'])
        yy = 0.25 + 0.13
        while yy < 0.25 + h:
            for (a, b_) in (((-0.25, yy, 0.0), (w + 0.25, yy, 0.0)), ((-0.25, yy, d), (w + 0.25, yy, d))): K.mesh('Wall').log(K.w(*a), K.w(*b_), 0.13, 0.12, th['wall'], rng=rng, wob=0.08)
            for (a, b_) in (((0.0, yy + 0.13, -0.25), (0.0, yy + 0.13, d + 0.25)), ((w, yy + 0.13, -0.25), (w, yy + 0.13, d + 0.25))): K.mesh('Wall').log(K.w(*a), K.w(*b_), 0.13, 0.12, th['wall'], rng=rng, wob=0.08)
            yy += 0.26
        K.box('Wall', w * 0.4, w * 0.4 + 0.9, 0.25, 0.25 + 1.8, -0.16, -0.1, th['dark'])
        gable(K, 'Roof', 0, w, 0, d, 0.25 + h, rng.uniform(1.0, 1.5), 0.55, th['roof'], sag=rng.uniform(0.0, 0.12))
        for _ in range(int(rng.integers(4, 9))):      # stones holding the planks down
            u, v = rng.uniform(0.3, w - 0.3), rng.uniform(0.4, d - 0.4); yr = 0.25 + h + (1 - abs(v - d / 2) / (d / 2)) * 1.2 + 0.1
            K.mesh('Roof').slab(K.w(u, yr, v), K.ax, K.az, rng.uniform(0.15, 0.3), rng.uniform(0.14, 0.26), 0.16, th['base'], rng, tilt=0.35)
        top = 0.25 + h
    elif style == 'plaster':
        K.box('Base', -0.3, w + 0.3, yg_min - 0.5, 0.4, -0.3, d + 0.3, th['base'])
        K.box('Wall', 0.0, w, 0.4, 0.4 + h, 0.0, d, th['wall'])
        for u in np.linspace(0.0, w, max(2, int(round(w / 2.2)) + 1)):
            for v in (0.0, d): K.box('Wall', u - 0.11, u + 0.11, 0.4, 0.4 + h, v - 0.11, v + 0.11, th['dark'])
        K.box('Wall', w * 0.45, w * 0.45 + 0.95, 0.4, 0.4 + 1.85, -0.06, 0.0, th['dark'])
        gable(K, 'Roof', 0, w, 0, d, 0.4 + h, rng.uniform(1.1, 1.6), 0.7, th['roof'])
        K.box('Roof', -0.7, w + 0.7, 0.4 + h + 1.05, 0.4 + h + 1.75, d / 2 - 0.13, d / 2 + 0.13, th['roof'])
        top = 0.4 + h
    elif style == 'rubble':
        h = rng.uniform(1.7, 2.0); K.box('Base', -0.2, w + 0.2, yg_min - 0.5, 0.15, -0.2, d + 0.2, th['base'])
        K.th = dict(wall='rubble', wall_mat=th['wall'], cope_mat=th['base'])
        for (a, b_) in (((0, 0), (w, 0)), ((0, d), (w, d)), ((0, 0), (0, d)), ((w, 0), (w, d))): K.wall(a[0], a[1], b_[0], b_[1], 0.15, 0.15 + h, 0.5)
        K.cols.clear() if False else None
        K.box('Wall', w * 0.4, w * 0.4 + 0.85, 0.15, 0.15 + 1.6, -0.27, -0.2, th['dark'])
        gable(K, 'Roof', 0, w, 0, d, 0.15 + h, rng.uniform(0.6, 0.9), 0.4, th['roof'], sag=rng.uniform(0.0, 0.1))
        top = 0.15 + h
    elif style == 'burnt':
        K.box('Base', -0.1, w + 0.1, yg_min - 0.5, 0.12, -0.1, d + 0.1, th['base'])
        K.th = dict(wall='rubble', wall_mat=th['wall'], cope_mat=th['base'])
        for (a, b_) in (((0, 0), (w, 0)), ((0, d), (w, d)), ((0, 0), (0, d)), ((w, 0), (w, d))):
            if rng.uniform() < 0.8: K.wall(a[0], a[1], b_[0], b_[1], 0.12, 0.12 + rng.uniform(0.4, 1.3), 0.45)
        for u in np.linspace(0.3, w - 0.3, 3):
            for v in (0.3, d - 0.3):
                if rng.uniform() < 0.75: K.mesh('Wall').log(K.w(u, 0.0, v), K.w(u + rng.uniform(-0.3, 0.3), rng.uniform(1.2, 2.9), v + rng.uniform(-0.3, 0.3)), rng.uniform(0.09, 0.13), 0.06, th['dark'], rng=rng, wob=0.15)
        K.mesh('Wall').log(K.w(-0.2, rng.uniform(0.3, 1.6), d / 2), K.w(w + 0.2, rng.uniform(0.2, 0.5), d / 2 + rng.uniform(-0.6, 0.6)), 0.12, 0.09, th['dark'], rng=rng, wob=0.15)      # the fallen ridge
        for _ in range(int(rng.integers(3, 7))): K.mesh('Roof').slab(K.w(rng.uniform(0.3, w - 0.3), 0.2 + rng.uniform(0.0, 0.25), rng.uniform(0.3, d - 0.3)), K.ax, K.az, rng.uniform(0.4, 0.9), rng.uniform(0.3, 0.6), 0.07, th['roof'], rng, tilt=0.5)
        top = 1.0
    else:      # stilt
        lift = rng.uniform(1.2, 1.8)
        for u in np.linspace(0.2, w - 0.2, 3):
            for v in (0.2, d - 0.2): K.mesh('Base').log(K.w(u, yg_min - 0.6, v), K.w(u + rng.uniform(-0.06, 0.06), lift, v), rng.uniform(0.1, 0.14), 0.09, th['dark'], rng=rng, wob=0.1)
        K.box('Base', -0.4, w + 0.4, lift, lift + 0.1, -0.7, d + 0.2, th['wall'])
        K.th = dict(wall='plank', wall_mat=th['wall'], cope_mat=th['dark'])
        for (a, b_) in (((0, 0), (w, 0)), ((0, d), (w, d)), ((0, 0), (0, d)), ((w, 0), (w, d))): K.wall(a[0], a[1], b_[0], b_[1], lift + 0.1, lift + 0.1 + h, 0.16)
        K.box('Wall', w * 0.4, w * 0.4 + 0.85, lift + 0.1, lift + 0.1 + 1.75, -0.12, -0.07, th['dark'])
        for sx in (w * 0.4 + 0.1, w * 0.4 + 0.75): K.mesh('Base').log(K.w(sx, yg_min - 0.2, -1.6), K.w(sx, lift + 0.1, -0.7), 0.045, 0.04, th['dark'], rng=rng, wob=0.1)
        for k in range(5): K.box('Base', w * 0.4 + 0.05, w * 0.4 + 0.8, yg_min + (lift - yg_min) * (k + 0.5) / 5.5, yg_min + (lift - yg_min) * (k + 0.5) / 5.5 + 0.04, -1.6 + 0.9 * (k + 0.5) / 5.5 - 0.05, -1.6 + 0.9 * (k + 0.5) / 5.5 + 0.05, th['dark'])
        gable(K, 'Roof', 0, w, 0, d, lift + 0.1 + h, rng.uniform(0.8, 1.2), 0.5, th['roof'], sag=rng.uniform(0.0, 0.1))
        top = lift + 0.1 + h
    K.cols.clear(); K.collide(0.0, w, yg_min - 0.5, max(top, 0.6), 0.0, d)      # one box per building, inside its walls


def main():
    B.utf8(); cfg = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8')); P = cfg['plan']; f = B.load_height(B.ROOT / cfg['height']); X, Z = B.grid_xz()
    keep = B.wet() | B.protected(); sc = json.loads((B.ROOT / P['scan']).read_text(encoding='utf-8')); occ = np.zeros(sc['cols'] * sc['rows'], bool); occ[sc['occupied']] = True; occ = occ.reshape(sc['rows'], sc['cols'])
    keep |= occ[np.clip((Z / sc['cell']).astype(int), 0, sc['rows'] - 1), np.clip((X / sc['cell']).astype(int), 0, sc['cols'] - 1)]
    keep |= np.abs(f - B.load_height()) > 0.05
    lat = json.loads((B.ROOT / P['routes']).read_text(encoding='utf-8')); road = np.zeros(X.shape, bool)
    for r in lat['routes']: road |= B.stamp(np.asarray(r['pts'], float), r['width'] / 2 + P['route_margin_m'], X, Z)
    for it in json.loads((B.ROOT / P['keepout']).read_text(encoding='utf-8'))['items']: keep |= np.hypot(X - it['x'], Z - it['z']) <= max(float(it.get('keepout_m', 10)), 10) + 15
    for st in json.loads((B.RB / 'Dungeon308/sites.json').read_text(encoding='utf-8')).values():
        if 'x' in st: keep |= np.hypot(X - st['x'], Z - st['z']) <= 45
    keep |= road; dk = ndi.distance_transform_edt(~keep, sampling=4.0); droad = ndi.distance_transform_edt(~road, sampling=4.0)
    rel = ndi.maximum_filter(f, size=5) - ndi.minimum_filter(f, size=5); gz, gx = np.gradient(f, 4.0)
    ok = (dk >= P['clear_m']) & (droad >= P['road_near_m']) & (droad <= P['road_far_m']) & (rel <= P['relief_max_m']) & (X > 40) & (X < 3960) & (Z > 40) & (Z < 5960)
    meshes = {}; cols = []; houses = []; report = {}
    for realm, R in cfg['realms'].items():
        rng = np.random.default_rng(seed_of(cfg['seed'], 'hamlet', realm))
        okr = (dk >= P['clear_m']) & (droad >= P['road_near_m']) & (droad <= R.get('road_far_m', P['road_far_m'])) & (rel <= R.get('relief_max_m', P['relief_max_m'])) & (X > 40) & (X < 3960) & (Z > 40) & (Z < 5960)
        m = okr & B.poly_mask(B.REALMS[realm], X, Z)
        if 'box' in R: x0, z0, x1, z1 = R['box']; m &= (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
        ii, jj = np.nonzero(m); order = rng.permutation(len(ii)); centres = []; placed = []; shared = {}
        for k in order:      # cluster centres far apart, then a few houses around each
            if len(centres) * R['per_cluster'] >= R['count']: break
            x, z = jj[k] * 4.0, ii[k] * 4.0
            if all(math.hypot(x - a, z - b) >= R['cluster_apart_m'] for a, b in centres): centres.append((x, z))
        for cx, cz in centres:
            near = np.flatnonzero((np.hypot(jj * 4.0 - cx, ii * 4.0 - cz) <= R['cluster_r_m'])); rng.shuffle(near); got = 0
            for k in near:
                if got >= R['per_cluster'] or len(placed) >= R['count']: break
                x, z = jj[k] * 4.0 + rng.uniform(-1.5, 1.5), ii[k] * 4.0 + rng.uniform(-1.5, 1.5)
                if any(math.hypot(x - a, z - b) < R['house_apart_m'] for a, b, _ in placed): continue
                i, j = ii[k], jj[k]; pack = None
                if R.get('pack') and rng.random() < R.get('pack_share', 1.0): pack = R['pack'][int(rng.integers(0, len(R['pack'])))]
                w, d = (pack['w'], pack['d']) if pack else (rng.uniform(*R['w']), rng.uniform(*R['d']))
                if pack and (dk[i, j] < 0.5 * math.hypot(w, d) + P['pack_clear_m'] or any(math.hypot(x - a, z - b) < 0.5 * math.hypot(w, d) + P['pack_apart_m'] for a, b, _ in placed)): continue
                yaw = math.degrees(math.atan2(gz[i, j], gx[i, j])) + rng.uniform(-12, 12) if math.hypot(gx[i, j], gz[i, j]) > 0.02 else rng.uniform(0, 360)      # long side along the contour
                c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); ax = np.array([c, -s]); az = np.array([s, c]); o2 = np.array([x, z]) - ax * w / 2 - az * d / 2
                g = [float(B.sample_tri(f, *(o2 + ax * u + az * v))) for u in (0, w / 2, w) for v in (0, d / 2, d)]
                if pack:      # D308-65: a whole building of the downloaded packs stands here (HouseKit308 prefab), the realm's variant of its materials
                    g = [float(B.sample_tri(f, *(o2 + ax * u + az * v))) for u in np.linspace(0, w, 5) for v in np.linspace(0, d, 5)]
                    if max(g) - min(g) > P['pack_relief_m']: continue
                    y = max(g) - P['pack_sink_m']; K = Kit(realm, [o2[0], y, o2[1]], yaw, rng, {}); K.ax = np.array([ax[0], 0.0, ax[1]]); K.az = np.array([az[0], 0.0, az[1]]); K.m = shared
                    if pack.get('footing', True) and max(g) - min(g) > 0.6: K.box('Base', 0.15, w - 0.15, min(g) - y - 0.4, -0.02, 0.15, d - 0.15, R['materials'].get('base', 'fieldstone'))      # a footing of field stone under the low side
                    h_ = pack.get('wall_top_m', 3.2); cols.append(dict(center=[round(float(x), 3), round(y + h_ / 2, 3), round(float(z), 3)], size=[round(w * pack.get('solid', 0.8), 3), round(h_, 3), round(d * pack.get('solid', 0.8), 3)], yaw=float(yaw)))
                    placed.append((x, z, yaw)); got += 1
                    houses.append(dict(realm=realm, style='choga', prefab=pack['prefab'], variant=R.get('variant', ''), x=round(float(x), 2), y=round(float(y), 3), z=round(float(z), 2), yaw=round(yaw, 1), w=round(w, 2), d=round(d, 2), step_m=round(max(g) - min(g), 2)))
                    continue
                K = Kit(realm, [o2[0], max(g) + 0.05, o2[1]], yaw, rng, {}); K.ax = np.array([ax[0], 0.0, ax[1]]); K.az = np.array([az[0], 0.0, az[1]]); K.m = shared
                house(K, R['style'], rng, w, d, min(g) - max(g) - 0.05, R['materials']); cols += K.cols; placed.append((x, z, yaw)); got += 1
                houses.append(dict(realm=realm, style=R['style'], x=round(float(x), 1), z=round(float(z), 1), yaw=round(yaw, 1), w=round(w, 2), d=round(d, 2), step_m=round(max(g) - min(g), 2)))
        meshes.update({m_.name: m_ for m_ in shared.values() if m_.v}); report[realm] = dict(style=R['style'], houses=len(placed), pack=sum(1 for h_ in houses if h_['realm'] == realm and h_.get('prefab')), clusters=len(centres), candidates=int(m.sum()), vertices=int(sum(len(m_.v) for m_ in shared.values())))
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    for old in (OUT / 'Meshes').glob('*.json'): old.unlink()
    for m_ in meshes.values(): B.write_json(OUT / 'Meshes' / f'{m_.name}.json', m_.dump(), indent=None)
    B.write_json(OUT / 'layout.json', dict(version=cfg['version'], drawn=sorted(meshes), colliders=cols, houses=houses, variants=cfg.get('variants', []), prefabDir=cfg.get('prefabDir', ''), report=report))
    for k, v in report.items(): print(k, v)
    return 0


if __name__ == '__main__':
    sys.exit(main())
