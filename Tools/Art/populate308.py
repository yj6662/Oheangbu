"""#308 field enemies and name-only sub NPC spots - `populate308`. Offline, deterministic. D308-63.

  python Tools/Art/populate308.py Tools/Unity/Stage308_populate/populate308.json   # -> Art/World/Compact/Rebuild/Populate308/plan.json

Enemies stand in the open field of every realm (never in a dungeon, a building, a keep-out, on the road or in water): groups a little
off the roads, each member a stand-in body (a template actor that already exists in the play scene) under the realm's own name.
Sub NPCs are spots only: a name and a trade in front of a new outbuilding (Hamlet308). No body, no content row, nothing at run time.
What is placed, how many and what it is called is the data file; this script only finds the ground for it.
"""
import hashlib, json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402

OUT = B.RB / 'Populate308'


def seed_of(*parts):
    return int.from_bytes(hashlib.sha256(':'.join(map(str, parts)).encode('utf-8')).digest()[:4], 'little')


def main():
    B.utf8(); cfg = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8')); P = cfg['place']; f = B.load_height(B.ROOT / cfg['height']); X, Z = B.grid_xz()
    keep = B.wet() | B.protected()
    sc = json.loads((B.ROOT / P['scan']).read_text(encoding='utf-8')); occ = np.zeros(sc['cols'] * sc['rows'], bool); occ[sc['occupied']] = True; occ = occ.reshape(sc['rows'], sc['cols'])
    keep |= occ[np.clip((Z / sc['cell']).astype(int), 0, sc['rows'] - 1), np.clip((X / sc['cell']).astype(int), 0, sc['cols'] - 1)]
    keep |= np.abs(f - B.load_height()) > 0.05                                # cliff bodies, ledges, the cove mountain
    for it in json.loads((B.ROOT / P['keepout']).read_text(encoding='utf-8'))['items']: keep |= np.hypot(X - it['x'], Z - it['z']) <= max(float(it.get('keepout_m', 10)), 10) + P['keepout_margin_m']
    houses = json.loads((B.RB / 'Hamlet308/layout.json').read_text(encoding='utf-8'))['houses']
    for h in houses: keep |= np.hypot(X - h['x'], Z - h['z']) <= P['house_clear_m']
    for st in json.loads((B.RB / 'Dungeon308/sites.json').read_text(encoding='utf-8')).values():
        if 'x' in st: keep |= np.hypot(X - st['x'], Z - st['z']) <= P['dungeon_clear_m']
    for x, z in P.get('existing_encounters', []): keep |= np.hypot(X - x, Z - z) <= P['encounter_clear_m']
    lat = json.loads((B.ROOT / P['routes']).read_text(encoding='utf-8')); road = np.zeros(X.shape, bool)
    for r in lat['routes']: road |= B.stamp(np.asarray(r['pts'], float), r['width'] / 2 + 2.0, X, Z)
    droad = ndi.distance_transform_edt(~road, sampling=4.0); dk = ndi.distance_transform_edt(~keep, sampling=4.0)
    gz, gx = np.gradient(f, 4.0); slope = np.degrees(np.arctan(np.hypot(gx, gz))); slope = ndi.maximum_filter(slope, size=5)
    gy = lambda x, z: float(B.sample_tri(f, x, z))
    enemies = []; taken = []
    for realm, groups in cfg['enemies'].items():
        rng = np.random.default_rng(seed_of(cfg['seed'], 'enemy', realm))
        base = B.poly_mask(B.REALMS[realm], X, Z) & (dk >= P['clear_m']) & (slope <= P['slope_max_deg']) & (droad >= P['road_min_m']) & (droad <= P['road_max_m']) & (X > 120) & (X < 3880) & (Z > 120) & (Z < 5880)
        for gi, g in enumerate(groups):
            m = base.copy()
            for tx, tz in taken: m &= np.hypot(X - tx, Z - tz) >= P['apart_m']
            ii, jj = np.nonzero(m)
            if len(ii) == 0: print(realm, g['key'], 'NO GROUND'); continue
            k = int(rng.integers(0, len(ii))); cx, cz = jj[k] * 4.0 + rng.uniform(0.5, 3.5), ii[k] * 4.0 + rng.uniform(0.5, 3.5); taken.append((cx, cz)); a0 = rng.uniform(0, 2 * math.pi)
            for n in range(int(g['count'])):
                a = a0 + n * 2 * math.pi / max(1, g['count']); r = 0.0 if g['count'] == 1 else rng.uniform(3.0, 5.5); x, z = cx + math.cos(a) * r, cz + math.sin(a) * r; pa = rng.uniform(0, 2 * math.pi); pr = rng.uniform(9, 15)
                enemies.append(dict(id=f"field308/{realm.lower()}_{g['key']}_{gi}_{n}", name=g['name'], contentId=f"field308/{g['key']}", template=g['template'], realm=realm, group=f"{realm.lower()}_{g['key']}_{gi}",
                                    feet=[round(x, 2), round(gy(x, z), 2), round(z, 2)], patrol2=[round(x + math.cos(pa) * pr, 2), round(gy(x + math.cos(pa) * pr, z + math.sin(pa) * pr), 2), round(z + math.sin(pa) * pr, 2)],
                                    road_m=round(float(droad[int(z // 4), int(x // 4)]), 1)))
    npcs = []
    for realm, people in cfg['npcs'].items():
        hs = [h for h in houses if h['realm'] == realm]; rng = np.random.default_rng(seed_of(cfg['seed'], 'npc', realm)); order = list(rng.permutation(len(hs))); used = []
        for person in people:      # spread over the hamlets: the next house is the one farthest from the houses already taken
            if not order: break
            pick = order[0] if not used else max(order, key=lambda q: min(math.hypot(hs[q]['x'] - hs[u]['x'], hs[q]['z'] - hs[u]['z']) for u in used))
            order.remove(pick); used.append(pick); h = hs[pick]; yaw = math.radians(h['yaw']); fx, fz = -math.sin(yaw), -math.cos(yaw); d = h['d'] / 2 + 1.6 + h.get('step_m', 0.0); x, z = h['x'] + fx * d, h['z'] + fz * d
            npcs.append(dict(id=f"subnpc308/{realm.lower()}_{person['key']}", name=person['name'], job=person['job'], realm=realm, pos=[round(x, 2), round(gy(x, z), 2), round(z, 2)], house=[h['x'], h['z']], style=h['style']))
    OUT.mkdir(parents=True, exist_ok=True); B.write_json(OUT / 'plan.json', dict(version=cfg['version'], height=cfg['height'], nav_radius_m=P['nav_radius_m'], enemies=enemies, npcs=npcs))
    from collections import Counter
    print('enemies', len(enemies), dict(Counter(e['realm'] for e in enemies)), '| npc spots', len(npcs), dict(Counter(n['realm'] for n in npcs)))
    print('probe:' + ';'.join(f"{e['feet'][0]},{e['feet'][1]},{e['feet'][2]}" for e in enemies[::3]))


if __name__ == '__main__':
    sys.exit(main())
