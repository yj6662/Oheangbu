"""SPEC-WORLD-HANYANG-308 phase 5b (§15): street life - market goods (E1), lane / plaza ground (E2), the empty quarters (E3), small touches (E4).
python Tools/Art/hanyang308_life.py  ->  Tools/Unity/Stage308_hanyang/life308.json (+ Out/life_report.json, Out/life_anchors.json)
ADDITIVE: only new units Hanyang308_Life_*; reads town308.json / walkers308.json / town_anchors.json, writes none of them.
Consumed by `Hanyang308 life:mats | life:meshes | life:blocks | life:apply[:<unit>] | life:clear | life:status | life:prune`.
Helpers are the phase-3 ones (head of hanyang308_town.py + helper part of hanyang308_town_b.py), exec'd without running their steps."""
import json, math, random, sys, zlib
import numpy as np
REV = 'r6'     # part of every block / ribbon id: a re-bake gets new mesh assets (bump when geometry changes after an apply)
_src = open('Tools/Art/hanyang308_town.py', encoding='utf-8').read().split('\ndef footing(')[0].replace("'T_%s_%s'", "'L_%s_%s'")
exec(compile(_src, 'town_head', 'exec'))
_srcb = open('Tools/Art/hanyang308_town_b.py', encoding='utf-8').read().split('# =====')[0]
exec(compile(_srcb, 'town_b_helpers', 'exec'))
R = random.Random(3085); blocks = []; units.clear(); solids.clear()
TOWN = json.load(open(ST / 'town308.json', encoding='utf-8')); ANC = json.load(open(ST / 'Out/town_anchors.json', encoding='utf-8'))
WALK = json.load(open(B.ROOT / 'Tools/Unity/Stage308_walkers/walkers308.json', encoding='utf-8'))
rep = dict(warn=[], counts={}, tris={}, walkers={}); kits = {}; ribbons = []; lblocks = []
TINTS = [dict(name='h308_silk', src='plaster', c=[0.56, 0.20, 0.17]), dict(name='h308_indigo', src='plaster', c=[0.20, 0.28, 0.44]), dict(name='h308_ochre', src='plaster', c=[0.74, 0.56, 0.22]),
         dict(name='h308_green', src='plaster', c=[0.34, 0.44, 0.27]), dict(name='h308_hemp', src='plaster', c=[0.74, 0.66, 0.48]), dict(name='h308_fish', src='plaster', c=[0.58, 0.60, 0.60]),
         dict(name='h308_persimmon', src='plaster', c=[0.82, 0.43, 0.15]), dict(name='h308_jujube', src='plaster', c=[0.50, 0.15, 0.11]), dict(name='h308_grain', src='plaster', c=[0.86, 0.78, 0.54]),
         dict(name='h308_paper', src='plaster', c=[0.93, 0.91, 0.84]), dict(name='h308_gat', src='plaster', c=[0.09, 0.09, 0.10]), dict(name='h308_wood', src='plaster', c=[0.47, 0.34, 0.22]),
         dict(name='h308_herb', src='plaster', c=[0.43, 0.39, 0.22]), dict(name='h308_feather', src='plaster', c=[0.82, 0.76, 0.64])]

# ---------------------------------------------------------------- what already stands (phase 3 / 5a) -> occupancy
HOUSE = np.zeros((NZ, NX), bool)      # building / wall / prop footprints only (no yard pad): lanes may pass next to them


def mark_h(cx, cz, w, d, yaw, pad=0.0):
    for x, z in pts(cx, cz, w, d, yaw, pad, 0.6):
        j, i = int(round(x - OX)), int(round(z - OZ))
        if 0 <= j < NX and 0 <= i < NZ: HOUSE[i, j] = True


SHOPS = []      # the shops of phase 3: centre, yaw, scale, front point (= the shop_fronts anchor)
PROPS = []      # existing street props (benches, carts, jars ...) of the shop-front and 난전 blocks
for b in TOWN['blocks']:
    street = b['id'].endswith('p') or b['id'].startswith('Nanjeon')
    for h in b['houses']:
        sz = SIZE[h['a']]; w, d = sz[0] * h['s'][0], sz[2] * h['s'][2]
        if h['a'].startswith('Gen_Pedestal'): continue
        if h['a'] in ('Gen_Giwa_Shop3', 'Gen_Sijeon_Row8'): SHOPS.append(dict(h, f=to_world(h['p'][0], h['p'][2], h['yaw'], 0, -(sz[2] / 2 + 1.4))))
        mark(h['p'][0], h['p'][2], w, d, h['yaw'], 1.2 if not h['a'].startswith(('Prop_', 'Gen_Wall')) else 0.3); mark_h(h['p'][0], h['p'][2], w, d, h['yaw'], 0.2)
        if street: PROPS.append(dict(a=h['a'], x=h['p'][0], y=h['p'][1], z=h['p'][2], yaw=h['yaw'], blk=b['id'], r=max(w, d) / 2))
for src_ in (TOWN, json.load(open(ST / 'city308.json', encoding='utf-8')), json.load(open(ST / 'palace308.json', encoding='utf-8'))):
    for u in src_['units']:
        for gr in u['groups']:
            for it in gr['items']:
                if it['a'] == 'col' and it['s'][1] > 0.6 and max(it['s'][0], it['s'][2]) < 120: mark(it['p'][0], it['p'][2], it['s'][0], it['s'][2], it['yaw'], 0.5); mark_h(it['p'][0], it['p'][2], it['s'][0], it['s'][2], it['yaw'], 0.3)


# ---------------------------------------------------------------- walkers (read only): posts and stops that must stay free
def route_xy(r, s, lat):
    P = r['points']; acc = 0.0
    for a, b_ in zip(P[:-1], P[1:]):
        L = math.hypot(b_['x'] - a['x'], b_['z'] - a['z'])
        if acc + L >= s or b_ is P[-1]:
            t = min(1.0, max(0.0, (s - acc) / (L or 1))); tx, tz = (b_['x'] - a['x']) / (L or 1), (b_['z'] - a['z']) / (L or 1)
            return a['x'] + (b_['x'] - a['x']) * t + tz * lat, a['z'] + (b_['z'] - a['z']) * t - tx * lat
        acc += L


KEEP = [(p['x'], p['z'], 0.75, 'post:' + p['role']) for p in WALK['posts']]
for i, s in enumerate(WALK['stops']):
    for sg in (1, -1):      # the sign convention is written in the plan (+ = right); both sides are kept free anyway
        x, z = route_xy(WALK['routes'][s['route']], s['s'], s['lat'] * sg); KEEP.append((x, z, 0.6, 'stop%d:%s' % (i, s['kind'])))
for w in WALK['walkers']: pass
kept_hits = []


def keep_free(x, z, w, d, yaw, tag):
    """True when the rotated footprint (w x d at x, z) stays clear of every walker post / stop circle"""
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for kx, kz, kr, kt in KEEP:
        dx, dz = kx - x, kz - z; lx = dx * c - dz * s_; lz = dx * s_ + dz * c
        if abs(lx) < w / 2 + kr and abs(lz) < d / 2 + kr: kept_hits.append((tag, kt, round(kx, 1), round(kz, 1))); return False
    return True


# ---------------------------------------------------------------- kits: generated low-poly goods in a local frame (front = -Z, origin on the ground)
def _h(t): return zlib.crc32(t.encode()) & 0xffff      # stable across runs (str hash is salted per process)


class Kit:
    def __init__(self, kid): self.id = kid; self.m = {}; self.tris = 0; kits[kid] = self
    def box(self, mat, x0, y0, z0, x1, y1, z1, bottom=False): self.m.setdefault(mat, []).extend([7.0 if bottom else 0.0] + [round(v, 3) for v in (min(x0, x1), y0, min(z0, z1), max(x0, x1), y1, max(z0, z1))]); self.tris += 12 if bottom else 10
    def cyl(self, mat, x, y0, z, r, y1, n=5): self.m.setdefault(mat, []).extend([6.0, round(x, 3), round(y0, 3), round(z, 3), round(r, 3), round(y1, 3), float(n)]); self.tris += 3 * n
    def pyr(self, mat, x0, y0, z0, x1, y1, z1): self.m.setdefault(mat, []).extend([4.0] + [round(v, 3) for v in (x0, y0, z0, x1, y1, z1)]); self.tris += 4
    def out(self): return dict(id=self.id, parts=[dict(mat=k, f=v) for k, v in self.m.items()])


BOLT = dict(silk=['h308_silk', 'h308_indigo', 'h308_ochre', 'h308_green', 'h308_silk', 'h308_cloth'], cotton=['h308_cloth', 'h308_paper', 'h308_cloth', 'h308_indigo'], myeongju=['h308_paper', 'h308_ochre', 'h308_cloth', 'h308_hemp'],
            hemp=['h308_hemp', 'h308_straw', 'h308_hemp', 'h308_cloth'])
TRADES = ['silk', 'cotton', 'myeongju', 'paper', 'hemp', 'fish', 'grain', 'brass', 'onggi', 'herb', 'hats', 'wood', 'fruit', 'chicken', 'yeot']
TRADE_KO = dict(silk='선전(비단)', cotton='면포전(무명)', myeongju='면주전(명주)', paper='지전(종이)', hemp='저포전(모시 · 베)', fish='어물전', grain='미곡전(섬 · 말)', brass='유기전', onggi='옹기전', herb='약재', hats='갓 · 짚신',
                wood='나무전(장작)', fruit='과일 · 채소', chicken='닭전(둥우리)', yeot='엿판')


def goods(K, trade, x0, x1, z0, z1, y, rng, dens=1.0):
    """lay goods of a trade on the rectangle (x0..x1, z0..z1) at height y"""
    W = x1 - x0; D = z1 - z0; zc = (z0 + z1) / 2
    if trade in BOLT:
        n = max(1, int(W / 0.78)); cols = BOLT[trade]
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; hgt = rng.randint(2, 4) if dens >= 1 else 2; bw = min(0.7, W / n - 0.06)
            for q in range(hgt): K.box(cols[(k * 2 + q + rng.randint(0, 1)) % len(cols)], cx - bw / 2, y + q * 0.075, zc - min(D, 0.56) / 2 + 0.02 * (q % 2), cx + bw / 2, y + q * 0.075 + 0.07, zc + min(D, 0.56) / 2 - 0.02 * (q % 2))
        if D > 0.7: K.cyl(cols[0], x0 + 0.12, y, z1 - 0.1, 0.07, y + 0.62); K.cyl(cols[1], x0 + 0.3, y, z1 - 0.1, 0.07, y + 0.55)
    elif trade == 'paper':
        n = max(1, int(W / 0.62))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; K.box('h308_paper', cx - 0.24, y, zc - 0.17, cx + 0.24, y + 0.1 + 0.06 * rng.randint(0, 3), zc + 0.17)
        K.cyl('h308_paper', x1 - 0.14, y, z1 - 0.12 if D > 0.6 else zc, 0.1, y + 0.5); K.cyl('h308_hemp', x0 + 0.12, y, z1 - 0.12 if D > 0.6 else zc, 0.08, y + 0.42)
    elif trade == 'fish':
        n = max(1, int(W / 0.72))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; K.box('h308_straw', cx - 0.3, y, zc - min(D, 0.5) / 2, cx + 0.3, y + 0.03, zc + min(D, 0.5) / 2)
            for q in range(3): K.box('h308_fish', cx - 0.25, y + 0.03, zc - 0.17 + q * 0.13, cx + 0.22, y + 0.075, zc - 0.09 + q * 0.13)
    elif trade == 'grain':
        n = max(1, int(W / 0.5))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n
            if k % 3 == 2: K.cyl('h308_straw', cx, y, zc, 0.2, y + 0.42)
            else: K.box('h308_wood', cx - 0.17, y, zc - 0.17, cx + 0.17, y + 0.2, zc + 0.17); K.box('h308_grain' if k % 2 == 0 else 'h308_ochre', cx - 0.14, y + 0.2, zc - 0.14, cx + 0.14, y + 0.24, zc + 0.14)
    elif trade == 'fruit':
        n = max(1, int(W / 0.5)); cols = ['h308_persimmon', 'h308_green', 'h308_jujube', 'h308_paper', 'h308_persimmon']
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; K.cyl('h308_straw', cx, y, zc, 0.21, y + 0.14, 6); K.pyr(cols[(k + rng.randint(0, 1)) % len(cols)], cx - 0.17, y + 0.14, zc - 0.17, cx + 0.17, y + 0.27, zc + 0.17)
    elif trade == 'brass':
        n = max(2, int(W / 0.27))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; big = k % 4 == 1; K.cyl('h308_brass', cx, y, zc + (0.1 if k % 2 else -0.1) * min(1, D), 0.12 if big else 0.085, y + (0.13 if big else 0.07))
    elif trade == 'onggi':
        n = max(1, int(W / 0.5))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; r_ = 0.15 + 0.05 * rng.random(); hh = 0.26 + 0.2 * rng.random(); K.cyl('h308_onggi', cx, y, zc, r_, y + hh, 6); K.cyl('h308_onggi', cx, y + hh, zc, r_ * 0.6, y + hh + 0.05, 5)
    elif trade == 'herb':
        n = max(1, int(W / 0.42))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n
            if k % 2 == 0: K.box('wood_dark', cx - 0.17, y, zc - 0.15, cx + 0.17, y + 0.22, zc + 0.15); K.box('h308_herb', cx - 0.13, y + 0.22, zc - 0.11, cx + 0.13, y + 0.28, zc + 0.11)
            else: K.cyl('h308_straw', cx, y, zc, 0.15, y + 0.2); K.pyr('h308_herb', cx - 0.13, y + 0.2, zc - 0.13, cx + 0.13, y + 0.32, zc + 0.13)
    elif trade == 'hats':
        n = max(1, int(W / 0.5))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n
            if k % 3 != 2: K.cyl('h308_gat', cx, y, zc, 0.21, y + 0.015, 7); K.cyl('h308_gat', cx, y + 0.015, zc, 0.075, y + 0.13, 6)
            else:
                for q in range(3): K.box('h308_straw', cx - 0.13, y + q * 0.06, zc - 0.07, cx + 0.13, y + q * 0.06 + 0.055, zc + 0.07)
    elif trade == 'wood':
        n = max(1, int(W / 0.8))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n
            for q in range(3): K.box('h308_wood' if q % 2 == 0 else 'wood_dark', cx - 0.36, y + q * 0.16, zc - 0.2 + 0.03 * q, cx + 0.36, y + q * 0.16 + 0.15, zc + 0.2 - 0.03 * q)
    elif trade == 'chicken':
        n = max(1, int(W / 0.75))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; K.cyl('h308_straw', cx, y, zc, 0.27, y + 0.3, 6); K.box('h308_feather' if k % 2 == 0 else 'h308_wood', cx - 0.09, y + 0.3, zc - 0.13, cx + 0.07, y + 0.42, zc + 0.1); K.pyr('h308_jujube', cx - 0.04, y + 0.42, zc - 0.13, cx + 0.03, y + 0.47, zc - 0.07)
    elif trade == 'yeot':
        n = max(1, int(W / 0.75))
        for k in range(n):
            cx = x0 + (k + 0.5) * W / n; K.box('h308_wood', cx - 0.32, y, zc - 0.22, cx + 0.32, y + 0.05, zc + 0.22)
            for q in range(3): K.box('h308_paper' if q != 1 else 'h308_grain', cx - 0.28 + q * 0.19, y + 0.05, zc - 0.18, cx - 0.12 + q * 0.19, y + 0.085, zc + 0.18)


def ground_goods(K, trade, x, z, rng):
    """heavier goods of a trade standing on the ground beside a table (within 0.9 x 0.8 m around x, z)"""
    if trade == 'grain' or trade == 'herb':
        for q, (dx, dz) in enumerate(((-0.25, 0.1), (0.25, 0.1), (0.0, -0.25))): K.cyl('h308_straw', x + dx, 0, z + dz, 0.23, 0.55, 6)
        K.cyl('h308_straw', x, 0.55, z + 0.1, 0.22, 0.98, 6)
    elif trade == 'onggi' or trade == 'fish':
        for dx, dz, r_, hh in ((-0.25, 0.1, 0.24, 0.62), (0.28, 0.05, 0.2, 0.48), (0.02, -0.3, 0.16, 0.36)): K.cyl('h308_onggi', x + dx, 0, z + dz, r_, hh, 6); K.cyl('h308_onggi', x + dx, hh, z + dz, r_ * 0.62, hh + 0.07, 5)
    elif trade == 'wood':
        for q in range(4): K.box('h308_wood' if q % 2 == 0 else 'wood_dark', x - 0.42, q * 0.17, z - 0.3 + 0.04 * q, x + 0.42, q * 0.17 + 0.16, z + 0.3 - 0.04 * q)
    elif trade == 'fruit' or trade == 'chicken':
        K.cyl('h308_straw', x - 0.2, 0, z, 0.26, 0.3, 6); K.pyr('h308_green' if trade == 'fruit' else 'h308_feather', x - 0.42, 0.3, z - 0.22, x + 0.02, 0.46, z + 0.22); K.cyl('h308_straw', x + 0.28, 0, z - 0.1, 0.2, 0.22, 6); K.pyr('h308_persimmon', x + 0.12, 0.22, z - 0.26, x + 0.44, 0.36, z + 0.06)
    elif trade in BOLT or trade == 'paper':
        cols = BOLT.get(trade, ['h308_paper', 'h308_hemp'])
        for q in range(3): K.cyl(cols[q % len(cols)], x - 0.2 + q * 0.2, 0, z + (0.08 if q == 1 else 0), 0.085, 0.85 + 0.1 * q)
    elif trade == 'brass':
        K.cyl('h308_brass', x, 0, z, 0.25, 0.2, 7); K.cyl('h308_brass', x, 0.2, z, 0.2, 0.36, 7)
    elif trade == 'hats':
        K.box('wood_dark', x - 0.04, 0, z - 0.04, x + 0.04, 1.5, z + 0.04)
        for q in range(3): K.box('h308_straw', x - 0.2, 0.75 + q * 0.24, z - 0.07, x + 0.2, 0.92 + q * 0.24, z + 0.07)


def kit_table(trade, v):
    """좌판: board on two trestles (1.9 x 0.9 x 0.5), goods in two rows, ground goods on one side"""
    K = Kit('tb_%s_%d' % (trade, v)); rng = random.Random(_h('%s%d' % (trade, v))); sd = 1 if v % 2 == 0 else -1
    K.box('h308_wood', -0.95, 0.44, -0.45, 0.95, 0.5, 0.45, True); K.box('wood_dark', -0.8, 0, -0.38, -0.7, 0.44, 0.38); K.box('wood_dark', 0.7, 0, -0.38, 0.8, 0.44, 0.38)
    goods(K, trade, -0.9, 0.9, -0.42, 0.0, 0.5, rng)
    if v == 0: goods(K, trade, -0.9, 0.9, 0.02, 0.42, 0.5, rng, 0.5)
    ground_goods(K, trade, sd * 1.45, -0.05, rng); return K


def kit_shelf(trade):
    """back shelf against the shop front under the eave: 2.3 wide, 0.42 deep, three boards; reads as an open shop bay"""
    K = Kit('sh_' + trade); rng = random.Random(_h(trade))
    K.box('wood_dark', -1.15, 0, -0.21, -1.09, 1.55, 0.21); K.box('wood_dark', 1.09, 0, -0.21, 1.15, 1.55, 0.21)
    for y in (0.32, 0.86, 1.36): K.box('h308_wood', -1.09, y, -0.21, 1.09, y + 0.045, 0.21, y > 1.0)
    goods(K, trade, -1.02, 1.02, -0.19, 0.15, 0.365, rng, 0.5); goods(K, trade, -1.02, 1.02, -0.19, 0.15, 0.905, rng, 0.5)
    if trade == 'fish' or trade == 'herb' or trade == 'hats':      # a hanging row under the top board
        for q in range(7): K.box({'fish': 'h308_fish', 'herb': 'h308_herb', 'hats': 'h308_straw'}[trade], -0.95 + q * 0.3, 1.02, -0.2, -0.86 + q * 0.3, 1.34, -0.14)
    elif trade in BOLT:
        for q in range(5): K.box(BOLT[trade][q % len(BOLT[trade])], -1.0 + q * 0.42, 0.95, -0.24, -0.66 + q * 0.42, 1.36, -0.21)      # lengths of cloth hung over the top board
    return K


def kit_inner(trade):
    """shelf against the back wall inside an open 3칸 가게 (2.3 wide, stretched to 3.2 by the instance): two boards on end posts, goods on both"""
    K = Kit('in_' + trade); rng = random.Random(_h(trade + 'i'))
    K.box('wood_dark', -1.15, 0, -0.2, -1.09, 1.3, 0.2); K.box('wood_dark', 1.09, 0, -0.2, 1.15, 1.3, 0.2)
    for y in (0.4, 1.0): K.box('h308_wood', -1.09, y, -0.2, 1.09, y + 0.045, 0.2)
    goods(K, trade, -1.02, 1.02, -0.18, 0.16, 0.445, rng, 0.5); goods(K, trade, -1.02, 1.02, -0.18, 0.16, 1.045, rng, 0.5)
    if trade in BOLT:
        for q in range(4): K.box(BOLT[trade][q % len(BOLT[trade])], -0.95 + q * 0.5, 1.3, -0.23, -0.55 + q * 0.5, 1.9, -0.2, True)      # lengths of cloth hung above the shelf
    elif trade in ('fish', 'herb', 'hats'):
        K.box('wood_dark', -1.12, 1.82, -0.03, 1.12, 1.87, 0.03)
        for q in range(6): K.box({'fish': 'h308_fish', 'herb': 'h308_herb', 'hats': 'h308_straw'}[trade], -0.95 + q * 0.36, 1.45, -0.03, -0.83 + q * 0.36, 1.82, 0.03, True)
    return K


def kit_floor(trade):
    K = Kit('gg_' + trade); rng = random.Random(_h(trade + 'g')); ground_goods(K, trade, 0.0, 0.0, rng)
    if not K.m: K.cyl('h308_straw', -0.2, 0, 0, 0.24, 0.5, 6); K.cyl('h308_straw', 0.3, 0, 0.1, 0.22, 0.42, 6); K.box('h308_wood', -0.5, 0, -0.55, 0.5, 0.3, -0.25)
    return K


def kit_top(trade):
    """goods on an existing 평상 (Prop_Bench 2.26 x 0.47 x 2.39; a water jar of phase 3 sits near the middle)"""
    K = Kit('tp_' + trade); rng = random.Random(_h(trade + 't'))
    goods(K, trade, -1.0, 1.0, -1.05, -0.5, 0.47, rng); goods(K, trade, -1.0, 1.0, 0.5, 1.05, 0.47, rng, 0.5); ground_goods(K, trade, 1.65, -0.5, rng); return K


def kit_awning(kid, hw, z0, z1, h, col):
    """차일: four poles and a flat cloth with a front valance (the solids are axis-aligned boxes - the cloth does not sag)"""
    K = Kit(kid)
    for sx in (-hw, hw):
        K.box('wood_dark', sx - 0.035, 0, z0 - 0.035, sx + 0.035, h, z0 + 0.035); K.box('wood_dark', sx - 0.035, 0, z1 - 0.035, sx + 0.035, h + 0.12, z1 + 0.035)
    K.box(col, -hw - 0.12, h, z0 - 0.12, hw + 0.12, h + 0.03, (z0 + z1) / 2, True); K.box(col, -hw - 0.12, h + 0.06, (z0 + z1) / 2, hw + 0.12, h + 0.09, z1 + 0.12, True); K.box(col, -hw - 0.12, h - 0.22, z0 - 0.13, hw + 0.12, h, z0 - 0.11, True); return K


for t in TRADES: kit_table(t, 0); kit_table(t, 1); kit_shelf(t); kit_top(t); kit_inner(t); kit_floor(t)
kit_awning('aw_eave_w', 1.15, -0.55, 1.2, 2.2, 'h308_cloth'); kit_awning('aw_eave_h', 1.15, -0.55, 1.2, 2.25, 'h308_hemp'); kit_awning('aw_free_w', 1.4, -1.45, 1.45, 2.3, 'h308_cloth'); kit_awning('aw_free_h', 1.4, -1.45, 1.45, 2.35, 'h308_hemp')
for kid, col in (('bn_w', 'h308_cloth'), ('bn_i', 'h308_indigo'), ('bn_r', 'h308_silk'), ('bn_h', 'h308_hemp'), ('bn_o', 'h308_ochre')):      # 깃대 + 긴 천 (글자 없음)
    K = Kit(kid); K.box('wood_dark', -0.04, -0.3, -0.04, 0.04, 3.9, 0.04); K.box('wood_dark', -0.04, 3.7, -0.03, 0.62, 3.76, 0.03); K.box(col, 0.08, 1.75, -0.012, 0.58, 3.7, 0.012, True)
K = Kit('slab'); K.box('stone_rough', -0.55, -0.3, -0.42, 0.55, 0.07, 0.42)
K = Kit('jangseung'); K.box('h308_wood', -0.16, -0.4, -0.14, 0.16, 2.3, 0.14); K.box('wood_dark', -0.2, 2.3, -0.17, 0.2, 2.42, 0.17); K.box('h308_wood', -0.17, 2.42, -0.15, 0.17, 2.75, 0.15); K.pyr('wood_dark', -0.22, 2.75, -0.2, 0.22, 3.0, 0.2); K.box('wood_dark', -0.06, 1.75, -0.19, 0.06, 2.0, -0.14); K.box('wood_dark', -0.12, 1.5, -0.17, 0.12, 1.56, -0.14)
K = Kit('sotdae'); K.box('h308_wood', -0.05, -0.4, -0.05, 0.05, 5.2, 0.05); K.box('wood_dark', -0.26, 5.2, -0.05, 0.2, 5.3, 0.05); K.pyr('wood_dark', -0.4, 5.22, -0.03, -0.24, 5.36, 0.03); K.box('wood_dark', 0.14, 5.3, -0.03, 0.2, 5.42, 0.03)
K = Kit('hamabi'); K.box('stone_rough', -0.42, -0.3, -0.3, 0.42, 0.22, 0.3); K.box('stone_plain', -0.24, 0.22, -0.1, 0.24, 1.42, 0.1); K.pyr('stone_plain', -0.24, 1.42, -0.1, 0.24, 1.56, 0.1)


class LBlk:
    """one merged life block: pack props (PackLite names) + kit instances; top < 0 = one LOD step"""
    def __init__(self, bid, top=-1, cut=0.5, cull=0.2): self.id = REV + '_' + bid; self.top = top; self.cut = cut; self.cull = cull; self.h = []; self.q = []; lblocks.append(self)
    def add(self, a, x, y, z, yaw=0.0, s=(1, 1, 1), l=-1): self.h.append(dict(a=a, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 2), s=[round(float(v), 4) for v in s], l=l))
    def kit(self, k, x, y, z, yaw=0.0, s=(1, 1, 1)): self.q.append(dict(k=k, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 2), s=[round(float(v), 4) for v in s]))
    def tris(self): return sum(TRI[h['a']][h['l'] if h['l'] >= 0 else max(self.top, 0)] for h in self.h) + sum(kits[q['k']].tris for q in self.q)
    def place(self, G_):
        if not self.h and not self.q: lblocks.remove(self); return False
        al = [h['p'] for h in self.h] + [q['p'] for q in self.q]; self.o = [round(sum(p[0] for p in al) / len(al), 2), round(min(p[1] for p in al), 2), round(sum(p[2] for p in al) / len(al), 2)]
        G_.prefab('LB_' + self.id, *self.o, n='LB_' + self.id); return True
    def out(self): return dict(id=self.id, o=self.o, top=max(self.top, 0), cut=self.cut, cull=self.cull, single=self.top < 0, houses=self.h, inst=self.q)


# =====================================================================================================
# E1  저잣거리: 가게 앞 선반 + 좌판 + 차일, 평상 위 물건.  육의전 순서(종루 쪽부터)는 EST - 자리 기록은 못 찾았다
# =====================================================================================================
JL = [p[:2] for p in ROAD['rd_jongno']['line']][::-1]; JP = path(JL)
J_BANDS = ['silk', 'cotton', 'myeongju', 'paper', 'hemp', 'fish', 'grain', 'brass', 'hats', 'herb', 'onggi', 'wood']      # 46 m each from the 종루 junction eastward
N_BANDS = ['fish', 'grain', 'fruit', 'onggi', 'wood', 'chicken', 'hemp', 'herb']                                         # 남대문로 from the junction southward (칠패 쪽 = 어물)
NAN = ['fruit', 'yeot', 'grain', 'chicken', 'fish', 'fruit', 'herb', 'hats']


def trade_at(x, z, tag):
    if tag.startswith('Jongno'):
        s = min(JP, key=lambda q: (q[1] - x) ** 2 + (q[2] - z) ** 2)[0]; return J_BANDS[min(len(J_BANDS) - 1, int(s / 46.0))]
    return N_BANDS[min(len(N_BANDS) - 1, int(max(0.0, 2960.0 - z) / 48.0))]


def e1():
    u = unit('Life_Market'); G_of = {}; life_anchor = []; n = dict(shelf=0, floor=0, banner=0, table=0, awning=0, top=0, col=0, skipped_prop=0, skipped_walker=0); by_trade = {}
    def blk(key, x, z):
        if key not in G_of: G_of[key] = (G(u, 'Mk_' + key, x, z, cull=0.01), LBlk('Mk_' + key, top=-1, cull=0.2))
        return G_of[key]
    shops = []
    for a in ANC['shop_fronts']:
        tag, k = a['id'].rsplit('_', 1); key = '%s_%d' % (tag, (int(k) - 1) // 4); ax, ay, az = a['at']; yaw = a['yaw']; tr = trade_at(ax, az, tag); shops.append((ax, az, key, tr))
        Gm, bl = blk(key, ax, az); open3 = a['bays'] == 3; slots = (-2.15, 2.15) if open3 else (-8.1, -2.7, 2.7, 8.1); KD = SIZE['Gen_Giwa_Shop3' if open3 else 'Gen_Sijeon_Row8'][2]
        hs = min(SHOPS, key=lambda h: (h['f'][0] - ax) ** 2 + (h['f'][1] - az) ** 2); assert math.hypot(hs['f'][0] - ax, hs['f'][1] - az) < 1.0, a['id']
        cx, cz = hs['p'][0], hs['p'][2]; fl = hs['p'][1] + 0.3 * hs['s'][1]      # shop centre and floor (module platform 0.3 m)
        if open3:      # 앞이 트인 3칸 가게: shelves on the back wall + goods on the floor, inside
            for sx in (-1.75, 1.75): px, pz = to_world(cx, cz, yaw, sx, 1.85); bl.kit('in_' + tr, px, fl, pz, yaw, (1.4, 1, 1)); n['shelf'] += 1
            for sx, sz_ in ((R.uniform(-1.6, 1.6), 0.5), (R.choice((-2.9, 2.9)), -1.4))[:1 if R.random() < 0.75 else 2]: px, pz = to_world(cx, cz, yaw, sx, sz_); bl.kit('gg_' + tr, px, fl, pz, yaw + R.uniform(-30, 30)); n['floor'] += 1
        done = []; e_tab = 0.5 if open3 else 0.95
        bx, bz = to_world(ax, az, yaw, (SIZE['Gen_Giwa_Shop3' if open3 else 'Gen_Sijeon_Row8'][0] / 2 - 0.2) * (1 if int(k) % 2 else -1), 1.4 - 1.25)      # a cloth banner at one end of the front
        if keep_free(bx, bz, 1.4, 0.6, yaw, 'banner') and not any(math.hypot(p['x'] - bx, p['z'] - bz) < p['r'] + 0.3 for p in PROPS):
            bl.kit({'silk': 'bn_r', 'cotton': 'bn_w', 'myeongju': 'bn_o', 'hemp': 'bn_h', 'paper': 'bn_w', 'fish': 'bn_i', 'grain': 'bn_h'}.get(tr, R.choice(['bn_w', 'bn_i', 'bn_h'])), bx, g(bx, bz), bz, yaw + 90, (1, 1.0 + R.uniform(-0.08, 0.1), 1)); n['banner'] += 1
        for si, lx in enumerate(slots):
            if not open3:      # 8칸 행랑 (front with counter boards): shelf against the front under the eave
                sx, sz = to_world(ax, az, yaw, lx, 1.4 + 0.72); bl.kit('sh_' + tr, sx, g(*to_world(ax, az, yaw, lx, 1.4)), sz, yaw); n['shelf'] += 1
            hwid = (SIZE['Gen_Giwa_Shop3'][0] if open3 else SIZE['Gen_Sijeon_Row8'][0]) / 2 - 1.1; found = None
            for dl in (0.0, -0.9, 0.9, -1.7, 1.7):      # slide along the front to a spot free of the phase-3 props and of the walkers' posts / stops
                lt = lx + dl
                if abs(lt) > hwid or any(abs(lt - o) < 2.3 for o in done): continue
                tx, tz = to_world(ax, az, yaw, lt, 1.4 - e_tab)
                if any(math.hypot(p['x'] - tx, p['z'] - tz) < p['r'] * 0.85 + 1.0 for p in PROPS): continue
                if not keep_free(tx, tz, 3.2, 1.3, yaw, 'table'): n['skipped_walker'] += 1; continue
                found = lt; break
            if found is None: n['skipped_prop'] += 1; continue
            if R.random() < 0.06: continue
            done.append(found); lx = found
            ty = g(tx, tz); v = (si + int(k)) % 2; bl.kit('tb_%s_%d' % (tr, v), tx, ty, tz, yaw + R.uniform(-4, 4)); n['table'] += 1; by_trade[tr] = by_trade.get(tr, 0) + 1
            Gm.items.append(item('col', tx, ty + 0.4, tz, yaw, (1.9, 0.9, 0.9), n='Jwapan')); n['col'] += 1
            vx, vz = to_world(ax, az, yaw, lx, 1.4 - e_tab + 0.85); life_anchor.append(dict(kind='shop_table', trade=tr, at=[round(tx, 2), round(ty, 2), round(tz, 2)], yaw=round(yaw, 1), vendor=[round(vx, 2), round(g(vx, vz), 2), round(vz, 2)]))
            if R.random() < 0.8 and keep_free(tx, tz, 2.6, 2.0, yaw, 'awning'): bl.kit('aw_eave_w' if R.random() < 0.6 else 'aw_eave_h', tx, ty, tz, yaw, (1.2, 1.0 + R.uniform(-0.04, 0.1), 1.1)); n['awning'] += 1
    # goods on the existing 평상 (phase-3 benches) + a free-standing 차일 over some
    ki = 0
    for p in PROPS:
        if p['a'] != 'Prop_Bench': continue
        if p['blk'].startswith('Nanjeon'): key = 'Nan_%d' % int((p['z'] - 2650.0) // 52); tr = NAN[ki % len(NAN)]; ki += 1
        else:
            sx, sz, key, tr = min(shops, key=lambda q: (q[0] - p['x']) ** 2 + (q[1] - p['z']) ** 2)
            if R.random() < 0.3: tr = R.choice(['fruit', 'yeot', 'chicken', 'grain', 'hats'])      # 난전 between the licensed shops
        Gm, bl = blk(key, p['x'], p['z']); bl.kit('tp_' + tr, p['x'], p['y'], p['z'], p['yaw']); n['top'] += 1; by_trade[tr] = by_trade.get(tr, 0) + 1
        life_anchor.append(dict(kind='bench_goods', trade=tr, at=[round(p['x'], 2), round(p['y'], 2), round(p['z'], 2)], yaw=round(p['yaw'], 1)))
        if R.random() < 0.62 and keep_free(p['x'], p['z'], 3.1, 3.2, p['yaw'], 'awning'): bl.kit('aw_free_w' if R.random() < 0.55 else 'aw_free_h', p['x'], p['y'], p['z'], p['yaw']); n['awning'] += 1
    for key, (Gm, bl) in G_of.items(): bl.place(Gm)
    rep['counts']['market'] = dict(n, by_trade=by_trade, blocks=len(G_of)); rep['tris']['market'] = sum(bl.tris() for _, bl in G_of.values()); return life_anchor


# =====================================================================================================
# E2  골목 · 피맛골 · 종각 마당 겉면 (draped strips, merged per zone by life:meshes: names <set>__<k>), 우물 · 빨래터 돌
# =====================================================================================================
LIFT = 0.09; LANE_CAP = 140.0


def strip(setname, origin, line, w, kind, cross=3, st=3.0, wob=0.0, lift=LIFT):
    pts_ = []
    for (xa, za), (xb, zb) in zip(line[:-1], line[1:]):
        Ls = math.hypot(xb - xa, zb - za); k = max(1, round(Ls / st))
        for q in range(k): pts_.append((xa + (xb - xa) * q / k, za + (zb - za) * q / k))
    pts_.append(line[-1])
    if len(pts_) < 2: return 0.0
    v = []; x0, y0, z0 = origin; L = 0.0
    for i, (x, z) in enumerate(pts_):
        a = pts_[max(i - 1, 0)]; b_ = pts_[min(i + 1, len(pts_) - 1)]; tx, tz = b_[0] - a[0], b_[1] - a[1]; tl = math.hypot(tx, tz) or 1; nx, nz = -tz / tl, tx / tl
        sh = wob * math.sin(i * 0.9 + x0 * 0.01); ww = w * (1.0 + 0.12 * math.sin(i * 0.53))
        for c in range(cross):
            o = sh + ww * (c / (cross - 1) - 0.5); px, pz = x + nx * o, z + nz * o; v += [round(px - x0, 3), round(g(px, pz) + lift - y0, 3), round(pz - z0, 3)]
        if i: L += math.hypot(x - pts_[i - 1][0], z - pts_[i - 1][1])
    ribbons.append(dict(name='L_%s_%s__%d' % (REV, setname, sum(1 for r in ribbons if r['name'].startswith('L_%s_%s__' % (REV, setname)))), mat=kind, n=len(pts_), m=cross, v=v, a=[1.0] * cross)); return L


def rib_tris(): return sum((r['n'] - 1) * (r['m'] - 1) * 2 for r in ribbons)


def corridors(rect, axis, min_run, gap, near):
    """free straight corridors between the houses of a rectangle: lines along x (axis 0) or z (axis 1) -> [((xa, za), (xb, zb))]"""
    x0, z0, x1, z1 = [int(v) for v in rect]; sub = (OCC | HOUSE)[z0 - OZ:z1 - OZ, x0 - OX:x1 - OX]; nearh = near[z0 - OZ:z1 - OZ, x0 - OX:x1 - OX]
    blocked = sub.copy()
    for d in (-1, 1): blocked |= np.roll(sub, d, 0) | np.roll(sub, d, 1)
    if axis == 1: blocked = blocked.T; nearh = nearh.T
    cand = []
    for i in range(2, blocked.shape[0] - 2):
        runs = []; j = 0; row = blocked[i]
        while j < row.size:
            if row[j]: j += 1; continue
            k = j
            while k < row.size and not row[k]: k += 1
            if k - j >= min_run and nearh[i, j:k].mean() > 0.55: runs.append((j, k))
            j = k
        if runs: cand.append((sum(b_ - a for a, b_ in runs), i, runs))
    cand.sort(reverse=True); taken = []; out = []
    for sc, i, runs in cand:
        if any(abs(i - t) < gap for t in taken): continue
        taken.append(i)
        for a, b_ in runs: out.append(((x0 + a + 1, z0 + i), (x0 + b_ - 1, z0 + i)) if axis == 0 else ((x0 + i, z0 + a + 1), (x0 + i, z0 + b_ - 1)))
    return out


def e2(extra_zones):
    R.seed(3087); u = unit('Life_Ground'); lens = {}
    near = HOUSE.copy()
    for _ in range(9): near = near | np.roll(near, 1, 0) | np.roll(near, -1, 0) | np.roll(near, 1, 1) | np.roll(near, -1, 1)
    zones = [(z['id'], z['rect']) for z in ZONES_P3] + extra_zones
    for zid, rect in zones:
        cx, cz = (rect[0] + rect[2]) / 2, (rect[1] + rect[3]) / 2; org = (cx, g(cx, cz), cz); total = 0.0; name = 'lane_' + zid
        for a, b_ in corridors(rect, 0, 18, 15, near):
            if total < LANE_CAP: total += strip(name, org, [a, b_], 2.3, 'road', wob=0.35)
        for a, b_ in corridors(rect, 1, 14, 40, near):
            if total < LANE_CAP * 1.25: total += strip(name, org, [a, b_], 2.0, 'road', wob=0.3)
        if total > 0:
            Gz = G(u, 'Lane_' + zid, cx, cz, cull=0.004); Gz.items.append(item('mesh:Rib_L_%s_%s' % (REV, name), org[0], org[1], org[2], 0, col='road', n='Lane_' + zid)); lens[zid] = round(total, 1)
    # 피맛골: the lane behind the north row of 종로 (3.2 m gap between the row and the back houses)
    org = (2090.0, g(2090.0, 3160.0), 3160.0); off = 4.0 + 2.3 + SIZE['Gen_Sijeon_Row8'][2] + 1.6; line = []
    for s in np.arange(16.0, 520.0, 3.0):
        _, x, z, _, _ = at(JP, s); tx, tz = smooth_t(JP, s, 10.0); line.append((x - tz * off, z + tx * off))
    lens['pimatgol'] = round(strip('pimat', org, line, 2.6, 'road', wob=0.0), 1); Gp = G(u, 'Lane_Pimatgol', org[0], org[2], cull=0.004); Gp.items.append(item('mesh:Rib_L_%s_pimat' % REV, org[0], org[1], org[2], 0, col='road', n='Lane_Pimatgol'))
    # 종루 네거리 마당 (packed earth, 46 x 26 m) and the paved yard on the 종각 mound (14 x 14 m)
    org = (2000.0, g(2000.0, 2962.0), 2962.0); lens['junction'] = round(strip('plaza', org, [(1977.0, 2962.0), (2023.0, 2962.0)], 26.0, 'road', cross=9, st=2.5, lift=0.045), 1)
    Gj = G(u, 'Plaza_Jongnu', 2000.0, 2962.0, cull=0.004); Gj.items.append(item('mesh:Rib_L_%s_plaza' % REV, org[0], org[1], org[2], 0, col='road', n='Plaza_Jongnu'))
    s = SITE['jonggak']; org = (s['x'], g(s['x'], s['z']), s['z']); strip('jonggak', org, [(s['x'] - 8.0, s['z']), (s['x'] + 8.0, s['z'])], 16.0, 'pave', cross=7, st=2.0)
    Gj.items.append(item('mesh:Rib_L_%s_jonggak' % REV, org[0], org[1], org[2], 0, col='pave', n='Yard_Jonggak'))
    # flat stones round the wells and at the laundry spots (one merged block per side of the city)
    bw = {}
    def sb(x, z):
        key = 'W' if x < 1960 else 'E'
        if key not in bw: bw[key] = (G(u, 'Stones_' + key, x, z, cull=0.004), LBlk('Stones_' + key, top=-1, cull=0.03))
        return bw[key][1]
    ns = 0
    for w_ in ANC['wells']:
        x, y, z = w_['at']; bl = sb(x, z)
        for q in range(5):
            a_ = q * math.tau / 5 + 0.3; rr = 1.75 + 0.25 * (q % 2); px, pz = x + math.cos(a_) * rr, z + math.sin(a_) * rr
            bl.kit('slab', px, g(px, pz), pz, math.degrees(-a_) + 90 + R.uniform(-15, 15), (0.8 + 0.3 * R.random(), 1, 0.9 + 0.3 * R.random())); ns += 1
    for l_ in ANC['laundry']:
        x, y, z = l_['at']; bl = sb(x, z)
        for q in range(6): px, pz = x + q * 1.25 - 3.1, z - 1.3 + (q % 2) * 0.5; bl.kit('slab', px, g(px, pz), pz, R.uniform(-20, 20), (1.1, 1, 1.2)); ns += 1
    for key, (Gs, bl) in bw.items(): bl.place(Gs)
    rep['counts']['ground'] = dict(lanes_m=lens, total_m=round(sum(lens.values()), 1), ribbon_parts=len(ribbons), slabs=ns); rep['tris']['ground'] = rib_tris() + sum(bl.tris() for _, bl in bw.values())


# =====================================================================================================
# E3  빈 동네: 동쪽 초가 마을 + 북촌 기와집(ㄷ / ㅁ 배치, 솟을대문).  창덕궁은 짓지 않는다(예산) - 그 터를 북촌으로 쓴다
# =====================================================================================================
ZONES_P3 = [dict(id='Jongno', rect=(2010, 2940, 2130, 3180)), dict(id='Namchon', rect=(2020, 2595, 2250, 2935)), dict(id='Saemunan', rect=(1545, 3184, 1792, 3330)), dict(id='SaemunanS', rect=(1470, 3136, 1930, 3164)),
            dict(id='Myeongdong', rect=(1800, 2822, 1992, 2962)), dict(id='Seosomun', rect=(1665, 2580, 1992, 2820)), dict(id='Seochon', rect=(1655, 3332, 1788, 3530)), dict(id='Jeongdong', rect=(1440, 2880, 1800, 3134))]
EAST = dict(id='East', rect=(2130, 2940, 2300, 3180), mix=[('Choga_Long', 4), ('Choga_L', 4), ('Choga_Tavern', 0.25), ('Choga_Smithy', 0.12)], wall='Gen_Wall_Makdol4', wallp=0.6, keep=0.9, pz=15.5, max_houses=30, max_tris=38500)


def pick(mix):
    t = sum(w for _, w in mix); r = R.random() * t
    for n_, w in mix:
        r -= w
        if r <= 0: return n_
    return mix[-1][0]


def wall_run2(bl, G_, cx, cz, yaw, lx0, lx1, lz, kind, gap=None, hs=1.0):
    """담 modules along local X (as phase 3) + one box collider per unbroken stretch (as phase 5a `YardWall`)"""
    L = lx1 - lx0; n_ = max(1, int(round(L / 4.0))); m = L / n_; runs = []; th = SIZE[kind][2]; hh = SIZE[kind][1] * hs
    for k in range(n_):
        a, b_ = lx0 + k * m, lx0 + (k + 1) * m
        if gap and not (b_ <= gap[0] or a >= gap[1]): continue
        x, z = to_world(cx, cz, yaw, (a + b_) / 2, lz); xa, za = to_world(cx, cz, yaw, a, lz); xb, zb = to_world(cx, cz, yaw, b_, lz); ys = [g(x, z), g(xa, za), g(xb, zb)]
        if max(ys) - min(ys) > 1.3 or not all(INSIDE[int(round(q_[1] - OZ)), int(round(q_[0] - OX))] for q_ in ((xa, za), (xb, zb))): continue
        bl.add(kind, x, min(ys) - 0.15, z, yaw, (m / 4.0, hs + (max(ys) - min(ys)) / 1.8, 1.0), l=0); mark_h(x, z, m, th, yaw, 0.1)
        if runs and abs(runs[-1][1] - a) < 1e-6: runs[-1][1] = b_; runs[-1][2] = min(runs[-1][2], min(ys)); runs[-1][3] = max(runs[-1][3], max(ys))
        else: runs.append([a, b_, min(ys), max(ys)])
    for a, b_, lo, hi in runs:
        x, z = to_world(cx, cz, yaw, (a + b_) / 2, lz); G_.items.append(item('col', x, (lo - 0.5 + hi + hh) / 2, z, yaw, (b_ - a, hi + hh - lo + 0.5, th), n='YardWall'))
    return len(runs)


def e3_east():
    R.seed(int(__import__('os').environ.get('EAST_SEED', '6'))); Z_ = EAST; u = unit('Life_East'); x0, z0, x1, z1 = Z_['rect']; cells = {}; nh = 0; spent = 0; row = 0; z = z0 + 7.0; lane = 4.2; wells = 0; walls = 0; jang = 0; types = {}; new_wells = []
    while z < z1 - 6.0:
        face_s = row % 2 == 0; x = x0 + R.uniform(2.0, 9.0)
        while x < x1 - 6.0:
            kind = pick(Z_['mix'])
            if R.random() > Z_['keep'] or nh >= Z_['max_houses'] or spent > Z_['max_tris']: x += R.uniform(9.0, 16.0); continue
            sw, sd = SIZE[kind][0], SIZE[kind][2]; turn = R.random() < 0.16
            if sd > sw * 1.25: turn = not turn
            base = 0 if face_s else 180; px_, pz_ = x + sw / 2, z; gx_ = (g(px_ + 4, pz_) - g(px_ - 4, pz_)) / 8.0; gz_ = (g(px_, pz_ + 4) - g(px_, pz_ - 4)) / 8.0
            if math.hypot(gx_, gz_) > 0.05: base = round(face_yaw(-gx_, -gz_) / 15.0) * 15.0
            yaw = base + (90 if turn else 0) + R.uniform(-7, 7); w_, d_ = (sd, sw) if turn else (sw, sd); cx = x + sw / 2; cz = z + R.uniform(-1.6, 1.6); fy = yaw - (90 if turn else 0)
            yard = 3.4; lcx, lcz = to_world(cx, cz, fy, 0, -yard / 2); ok = lot(lcx, lcz, w_ + 1.6, d_ + yard, fy, 9.0, 0.35); ok2 = lot(cx, cz, w_, d_, fy, 1.6, 0.0) if ok else None
            if ok2 is None: x += R.uniform(4.0, 8.0); continue
            yt, yb = ok2; key = (int(cx // 64), int(cz // 64))
            if key not in cells: cells[key] = (G(u, 'East_%d_%d' % key, key[0] * 64 + 32, key[1] * 64 + 32, cull=0.006), LBlk('East_%d_%d' % key, top=1, cut=0.5, cull=0.02))
            Gb, bl = cells[key]; bl.add(kind, cx, yt, cz, yaw, l=1); plinth(bl, cx, cz, w_ - 1.7, d_ - 1.7, fy, yt + 0.03, yb); colbox(Gb, cx, cz, fy, w_ - 2.0, d_ - 2.0, yb, yt - yb + 3.2)
            mark(lcx, lcz, w_ + 1.6, d_ + yard, fy, 0.35); mark_h(cx, cz, w_, d_, fy, 0.2); nh += 1; spent += TRI[kind][1]; types[kind] = types.get(kind, 0) + 1
            if R.random() < Z_['wallp']:
                fz_ = -(d_ / 2 + yard - 0.5); walls += wall_run2(bl, Gb, cx, cz, fy, -(w_ / 2 + 0.6), w_ / 2 + 0.6, fz_, Z_['wall'], gap=(-1.3, 1.3))
                for sgn in (-1, 1):
                    if R.random() < 0.6: walls += wall_run2(bl, Gb, cx, cz, fy + 90, -(d_ / 2), d_ / 2 + yard - 0.5, sgn * (w_ / 2 + 0.6), Z_['wall'])
            if R.random() < 0.4:
                jx, jz = to_world(cx, cz, fy, w_ / 2 - 1.4, -(d_ / 2 + 1.3)); bl.add('Prop_Jars', jx, g(jx, jz), jz, fy + R.uniform(-20, 20), l=2); jang += 1
            x += w_ + R.uniform(2.4, 6.5)
        z += Z_['pz'] + (lane if not face_s else R.uniform(0.5, 2.0)); row += 1
    for key, (Gb, bl) in cells.items():
        hs = [h for h in bl.h if h['a'].startswith('Choga')]
        if len(hs) < 3: continue
        mx = sum(h['p'][0] for h in hs) / len(hs); mz = sum(h['p'][2] for h in hs) / len(hs)
        for _ in range(60):
            wx_, wz_ = mx + R.uniform(-16, 16), mz + R.uniform(-16, 16); lo, hi = ground(wx_, wz_, 3.0, 3.0, 0)
            if hi - lo < 0.35 and free(wx_, wz_, 3.0, 3.0, 0, 0.4):
                bl.add('Prop_Well_Changwon', wx_, lo, wz_, R.uniform(0, 90), l=1); colbox(Gb, wx_, wz_, 0, 1.8, 1.8, lo - 0.3, 1.5, 'Well'); mark(wx_, wz_, 2.4, 2.4, 0, 0.2); mark_h(wx_, wz_, 2.0, 2.0, 0, 0.1); wells += 1; new_wells.append(dict(zone='East', at=[round(wx_, 1), round(lo, 2), round(wz_, 1)]))
                for q in range(7): a_ = q * math.tau / 7; px, pz = wx_ + math.cos(a_) * 1.8, wz_ + math.sin(a_) * 1.8; bl.kit('slab', px, g(px, pz), pz, math.degrees(-a_) + 90, (0.9, 1, 1.0))
                break
    for Gb, bl in cells.values(): bl.place(Gb)
    rep['counts']['east'] = dict(houses=nh, types=types, wall_stretches=walls, jangdok=jang, wells=wells, blocks=len(cells)); rep['tris']['east'] = sum(bl.tris() for _, bl in cells.values()); return new_wells


COMPOUND_W, COMPOUND_D = 27.0, 31.0


def e3_bukchon():
    """ㄷ / ㅁ 배치 기와집: 안채(ㄱ자, 뒤) + 사랑채(일자, 앞 한쪽) + 곁채(일자를 돌려 옆) 를 토담이 두르고 앞에 솟을대문"""
    R.seed(3086); u = unit('Life_Bukchon'); cells = {}; nc = 0; nb = 0; gates = 0; walls = 0; hero = 0; spent = 0; anchors_g = []
    s = SITE['changdeok']; x0c, z0c, x1c, z1c = s['x'] - s['w'] / 2 - 2, s['z'] - s['d'] / 2 - 2, s['x'] + s['w'] / 2 + 2, s['z'] + s['d'] / 2 + 2
    OCC[int(z0c - OZ):int(z1c - OZ) + 1, int(x0c - OX):int(x1c - OX) + 1] = False        # the reserved 창덕궁 site is released ...
    for r in J['roads']: line_block([p[:2] for p in r['line']], r['width'] / 2 + 1.2)   # ... roads, the wall strip and what stands stay blocked
    OCC[:] |= ~INSIDE | HOUSE
    for sg in J['wall']['segments']: line_block([p[:2] for p in sg['line']], 14.0, 4.0)
    cands = []; fails = dict(occ=0, relief=0)
    for cz in np.arange(3322.0, 3528.0, 2.0):
        for cx in np.arange(2126.0, 2306.0, 2.0):
            gx_ = (g(cx + 6, cz) - g(cx - 6, cz)) / 12.0; gz_ = (g(cx, cz + 6) - g(cx, cz - 6)) / 12.0; sl = math.hypot(gx_, gz_)
            if sl > 0.17: continue
            fy = round(face_yaw(-gx_, -gz_) / 15.0) * 15.0 if sl > 0.04 else 0.0      # flat terrace: 남향 (front toward -Z)
            if not free(cx, cz, COMPOUND_W + 1.0, COMPOUND_D + 1.0, fy, 0.3): fails['occ'] += 1; continue
            lo, hi = ground(cx, cz, COMPOUND_W, COMPOUND_D, fy)
            if hi - lo > 4.6: fails['relief'] += 1; continue
            cands.append((hi - lo, cx, cz, fy))
    cands.sort(key=lambda c: (round(c[0] * 2), c[2], c[1]))
    for rel, cx, cz, fy in cands:
        if nc >= 9 or spent > 30000: break
        if not free(cx, cz, COMPOUND_W + 1.0, COMPOUND_D + 1.0, fy, 0.3): continue
        key = (int(cx // 64), int(cz // 64))
        if key not in cells: cells[key] = (G(u, 'Bukchon_%d_%d' % key, key[0] * 64 + 32, key[1] * 64 + 32, cull=0.006), LBlk('Bukchon_%d_%d' % key, top=0, cut=0.4, cull=0.015))
        Gb, bl = cells[key]; placed = 0; big = nc == 0       # two compounds get the 창원의 집 안채 (LOD1) as the main house
        parts = [('Giwa_Anchae' if big else 'Gen_Giwa_L', 1.0 if not big else -4.0, 6.5 if not big else 2.0, 0 if not big else 90, 1 if big else 0), ('Gen_Giwa_4kan', -5.0, -8.5, 0, 0), ('Gen_Giwa_4kan', 8.6, -1.5, 90, 0)]
        if nc % 3 == 2: parts = parts[:2]          # every third compound is the smaller ㄱ + 사랑채 (ㄷ without the side wing)
        for kind, lx, lz, turn, lod in parts:
            px, pz = to_world(cx, cz, fy, lx, lz); sw, sd = SIZE[kind][0], SIZE[kind][2]; w_, d_ = (sd, sw) if turn else (sw, sd); glo, ghi = ground(px, pz, w_, d_, fy)
            if ghi - glo > 1.9 and kind == 'Giwa_Anchae':      # the long 안채 does not sit here: the ㄱ자 module instead
                kind, lx, lz, turn, lod = 'Gen_Giwa_L', 1.0, 6.5, 0, 0; px, pz = to_world(cx, cz, fy, lx, lz); sw, sd = SIZE[kind][0], SIZE[kind][2]; w_, d_ = sw, sd; glo, ghi = ground(px, pz, w_, d_, fy)
            if ghi - glo > 1.9: continue
            yt = math.ceil(ghi / 0.05) * 0.05 + 0.25; yb = glo - 0.45; bl.add(kind, px, yt, pz, fy + turn, l=lod); plinth(bl, px, pz, w_ - 1.2, d_ - 1.2, fy, yt + 0.03, yb); colbox(Gb, px, pz, fy, w_ - 1.6, d_ - 1.6, yb, yt - yb + 3.4)
            mark_h(px, pz, w_, d_, fy, 0.2); placed += 1; spent += TRI[kind][lod]; hero += 1 if big and kind == 'Giwa_Anchae' else 0
        if placed == 0: continue
        nb += placed; nc += 1; mark(cx, cz, COMPOUND_W, COMPOUND_D, fy, 2.4)
        # 솟을대문 in the front wall (toward the downhill lane), 토담 round the compound
        hw, hd = COMPOUND_W / 2, COMPOUND_D / 2; gxl = -3.0; gx_, gz_ = to_world(cx, cz, fy, gxl, -hd); gl, gh = ground(gx_, gz_, 14.4, 4.4, fy)
        if gh - gl < 1.7:
            bl.add('Gen_Soseuldaemun', gx_, gh + 0.02, gz_, fy, l=0); plinth(bl, gx_, gz_, 14.0, 4.0, fy, gh + 0.04, gl - 0.4); spent += TRI['Gen_Soseuldaemun'][0]; gates += 1
            for sx in (-1, 1): Gb.items.append(item('col', *to_world(cx, cz, fy, gxl + sx * 4.6, -hd)[:1], gl + 1.6, to_world(cx, cz, fy, gxl + sx * 4.6, -hd)[1], fy, (5.0, gh - gl + 3.6, 3.0), n='GateWing'))
            ax_, az_ = to_world(cx, cz, fy, gxl, -hd - 3.0); anchors_g.append(dict(id='Bukchon_%d' % nc, at=[round(ax_, 1), round(g(ax_, az_), 2), round(az_, 1)], yaw=round(fy, 1)))
            walls += wall_run2(bl, Gb, cx, cz, fy, -hw, gxl - 7.2, -hd, 'Gen_Wall_Todam4') if gxl - 7.2 - (-hw) > 2.0 else 0; walls += wall_run2(bl, Gb, cx, cz, fy, gxl + 7.2, hw, -hd, 'Gen_Wall_Todam4')
        else: walls += wall_run2(bl, Gb, cx, cz, fy, -hw, hw, -hd, 'Gen_Wall_Todam4', gap=(-4.4, -1.6))
        walls += wall_run2(bl, Gb, cx, cz, fy, -hw, hw, hd, 'Gen_Wall_Todam4')
        for sgn in (-1, 1): walls += wall_run2(bl, Gb, cx, cz, fy + 90, -hd, hd, sgn * hw, 'Gen_Wall_Todam4')
        jx, jz = to_world(cx, cz, fy, -9.0, 9.5); bl.add('Prop_Jars', jx, g(jx, jz), jz, fy + 90, l=1)
    # 작은 기와집 (ㄱ자 / 일자 + 앞 토담) where a walled compound does not fit: the strip south of the terraces and the slope north of 종묘
    small = 0
    for rect in ((2137, 3439, 2205, 3458), (2139, 3470, 2205, 3494), (2214, 3298, 2306, 3362)):
        z = rect[1] + 8.0
        while z < rect[3] - 5.0:
            x = rect[0] + 2.0
            while x < rect[2] - 8.0 and small < 12 and spent < 36000:
                kind = 'Gen_Giwa_L' if (small + int(x)) % 2 == 0 else 'Gen_Giwa_4kan'; sw, sd = SIZE[kind][0], SIZE[kind][2]; cx = x + sw / 2; cz = z + (3.0 if kind == 'Gen_Giwa_L' else 0.0)
                gx_ = (g(cx + 5, cz) - g(cx - 5, cz)) / 10.0; gz_ = (g(cx, cz + 5) - g(cx, cz - 5)) / 10.0; fy = round(face_yaw(-gx_, -gz_) / 15.0) * 15.0 if math.hypot(gx_, gz_) > 0.05 else 0.0
                ccx, ccz = to_world(cx, cz, fy, 0, -3.3 if kind == 'Gen_Giwa_L' else 0.0); ok = lot(ccx, ccz, sw + 1.0, sd + 3.0, fy, 2.0, 0.3)
                if ok is None: x += 3.0; continue
                yt, yb = ok; key = (int(cx // 64), int(cz // 64))
                if key not in cells: cells[key] = (G(u, 'Bukchon_%d_%d' % key, key[0] * 64 + 32, key[1] * 64 + 32, cull=0.006), LBlk('Bukchon_%d_%d' % key, top=0, cut=0.4, cull=0.015))
                Gb, bl = cells[key]; bl.add(kind, cx, yt + 0.2, cz, fy, l=0); plinth(bl, ccx, ccz, sw - 1.2, sd - 1.2, fy, yt + 0.23, yb); colbox(Gb, ccx, ccz, fy, sw - 1.6, sd - 1.6, yb, yt - yb + 3.6)
                mark(ccx, ccz, sw + 1.0, sd + 3.0, fy, 0.5); mark_h(ccx, ccz, sw, sd, fy, 0.2); walls += wall_run2(bl, Gb, ccx, ccz, fy, -(sw / 2 + 0.5), sw / 2 + 0.5, -(sd / 2 + 2.6), 'Gen_Wall_Todam4', gap=(-1.4, 1.4)); small += 1; nb += 1; spent += TRI[kind][0]; x += sw + 3.5
            z += 19.0
    for Gb, bl in cells.values(): bl.place(Gb)
    rep['counts']['bukchon'] = dict(cands=len(cands), fails=fails, small_houses=small, compounds=nc, buildings=nb, soseuldaemun=gates, anchae_changwon=hero, wall_stretches=walls, blocks=len(cells)); rep['tris']['bukchon'] = sum(bl.tris() for _, bl in cells.values()); return anchors_g


# =====================================================================================================
# E4  작은 것: 돈의문 밖 장승 · 솟대, 육조거리 머리 하마비 (글자 없음)
# =====================================================================================================
def e4():
    u = unit('Life_Touches'); n = {}
    gd = next(q for q in J['gates'] if q['id'] == 'gate_donui'); Gd = G(u, 'Donui_Outside', gd['x'] - 26, gd['z'], cull=0.004); bl = LBlk('Donui', top=-1, cull=0.03); k = 0
    for dx, dz, kit, yaw in ((-24.0, -7.5, 'jangseung', 90), (-24.0, 7.5, 'jangseung', 90), (-27.5, -9.5, 'sotdae', 60), (-28.5, 9.8, 'sotdae', 120), (-29.5, -8.0, 'sotdae', 30)):
        x, z = gd['x'] + dx, gd['z'] + dz; sl = abs(g(x + 1.5, z) - g(x - 1.5, z)) + abs(g(x, z + 1.5) - g(x, z - 1.5))
        if sl > 1.2: rep['warn'].append('donui %s at %.0f, %.0f: ground too steep (%.2f) - skipped' % (kit, x, z, sl)); continue
        bl.kit(kit, x, g(x, z), z, yaw, (1, 1.0 if kit != 'sotdae' else 0.85 + 0.1 * k, 1)); k += 1
        if kit == 'jangseung': Gd.items.append(item('col', x, g(x, z) + 1.3, z, yaw, (0.5, 2.8, 0.5), n='Jangseung'))
    if bl.place(Gd): n['donui'] = k
    s = SITE['yukjo_plaza']; Gh = G(u, 'Hamabi', s['x'], s['z'], cull=0.004); bl = LBlk('Hamabi', top=-1, cull=0.02); k = 0
    for sx in (-1, 1):
        x, z = 1959.0 + sx * 19.5, s['z'] - 4.0; bl.kit('hamabi', x, g(x, z), z, 0); Gh.items.append(item('col', x, g(x, z) + 0.7, z, 0, (0.7, 1.6, 0.5), n='Hamabi')); k += 1
    bl.place(Gh); n['hamabi'] = k; rep['counts']['touches'] = n; rep['tris']['touches'] = sum(b.tris() for b in lblocks if b.id.endswith(('Donui', 'Hamabi')))


life_anchor = e1(); wells_e = e3_east(); gates_b = e3_bukchon(); e2([('East', EAST['rect']), ('Bukchon', (2126, 3322, 2306, 3528))]); e4()
out_units = [dict(name=u['name'], groups=[gr.done() for gr in u['groups']]) for u in units if u['groups']]
assert all(u['name'].startswith('Hanyang308_Life_') for u in out_units), [u['name'] for u in out_units]
used = {q['k'] for b in lblocks for q in b.q}
plan = dict(version='308.life.p5b.' + REV, tints=TINTS, solids=solids, kits=[k.out() for k in kits.values() if k.id in used], blocks=[b.out() for b in lblocks], units=out_units, ribbons=ribbons, bridges=[], rays=[], joins=[], prefabs=[])
(ST / 'life308.json').write_text(json.dumps(plan, ensure_ascii=False), encoding='utf-8')
cols = sum(1 for u in out_units for gr in u['groups'] for it in gr['items'] if it['a'] == 'col'); rend = sum(1 for u in out_units for gr in u['groups'] for it in gr['items'] if it['a'] != 'col')
rep['walkers'] = dict(posts=len(WALK['posts']), stops=len(WALK['stops']), dressed_around=sorted(set(kept_hits)))
rep['total'] = dict(top_tris=sum(rep['tris'].values()), renderers_lod0=rend, colliders=cols, blocks=len(lblocks), kits_used=len(used), units=[u['name'] for u in out_units])
(ST / 'Out/life_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8')
(ST / 'Out/life_anchors.json').write_text(json.dumps(dict(market=life_anchor, wells=wells_e, gates=gates_b, clear=dict(jongno='dressing stays within 1.5 m of the eave line (좌판 0.5 - 1.4 m, 차일 기둥 1.5 m) -> >= 9.6 m of the 12.6 m between the fronts stays clear; the 8 m road itself is untouched', namdaemunro='only goods on the existing 평상 and 차일 over them (poles 1.45 m from the bench centre); both 6 m lines stay clear')), ensure_ascii=False, indent=1), encoding='utf-8')
print(json.dumps(rep['counts'], ensure_ascii=False)); print('tris', rep['tris'], rep['total']); print('kit tris', {k.id: k.tris for k in kits.values() if k.id[:2] in ('sh', 'aw') or k.id.endswith('_0')}); print('walker spots dressed around', len(set(kept_hits))); [print('WARN', w) for w in rep['warn']]

if '--diag' in sys.argv:      # occupancy after the run: # blocked before, h house, . free (slope: ^ > 0.17, : > 0.1)
    x0, z0, x1, z1 = [int(v) for v in sys.argv[sys.argv.index('--diag') + 1].split(',')]
    for z in range(z1, z0, -6):
        row = ''
        for x in range(x0, x1, 3):
            i, j = z - OZ, x - OX; sl = math.hypot(g(x + 4, z) - g(x - 4, z), g(x, z + 4) - g(x, z - 4)) / 8.0
            row += 'h' if HOUSE[i, j] else '#' if OCC[i, j] else ('^' if sl > 0.17 else ':' if sl > 0.1 else '.')
        print('%4d %s' % (z, row))
