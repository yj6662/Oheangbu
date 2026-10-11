"""#308 ore-craft fittings on ordinary buildings - `maseok308`. Offline, deterministic. SPEC-WORLD-MASEOK-308 (D308-64).

  python Tools/Art/maseok308.py Tools/Unity/Stage308_maseok/maseok308.json   # -> Art/World/Compact/Rebuild/Maseok308/{Meshes/*.json, layout.json}

"Ether punk" here = things that run on ore, made the Joseon way: glazed onggi pipe with brass bands, a powder jar and its funnel, a brass
brazier with an onggi flue, a row of small jars and a brass vane on the roof, an ore lamp under the eave. No copper pipe, gear, gauge or
boiler: those are the temples' (D308-43). Only the lamp glows. How much a building carries is its tier (rich / middle / poor / ruin).
Buildings come from the lists that exist: Hamlet308/layout.json and Town308/town308.json (round 1); a scanned list joins them later.
"""
import hashlib, json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from gallery308 import Mesh                                              # noqa: E402
from dungeon308 import stone                                             # noqa: E402

OUT = B.RB / 'Maseok308'; UP = np.array([0.0, 1.0, 0.0]); MD = 'Assets/_Project/Art/World/Maseok308/Materials/'; DG = 'Assets/_Project/Art/World/Dungeon308/Materials/'
ONGGI, YUGI, LACQ, DULL = MD + 'Onggi', MD + 'Yugi', MD + 'Lacquer', MD + 'PaperDull'; PAPER = 'Assets/_Project/Art/World/Mine308/Materials/Mine308_LanternPaper'


def seed_of(*parts):
    return int.from_bytes(hashlib.sha256(':'.join(map(str, parts)).encode('utf-8')).digest()[:4], 'little')


class Frame:
    """a building: local x across the front, local z from the back (-d/2) to the front (+d/2), y up; floor and eave heights in world."""
    def __init__(self, b, mesh, rng):
        yaw = math.radians(b['yaw']); self.ax = np.array([math.cos(yaw), 0.0, -math.sin(yaw)]); self.az = np.array([math.sin(yaw), 0.0, math.cos(yaw)]) * b['front']
        self.o = np.array([b['x'], 0.0, b['z']]); self.w_, self.d_ = b['w'], b['d']; self.floor, self.eave = b['floor'], b['eave']; self.m = mesh; self.rng = rng; self.b = b
        self.cols = []; self.lamps = []

    def w(self, x, y, z): return self.o + self.ax * x + UP * y + self.az * z
    def log(self, a, b, r0, r1, mat, sides=8): self.m.log(self.w(*a), self.w(*b), r0, r1, mat, sides=sides)
    def box(self, c, size, mat): self.m.box(self.w(*c), self.ax, UP, self.az, size, mat)
    def solid(self, c, size): self.cols.append(dict(center=[round(float(v), 3) for v in self.w(*c)], size=[round(float(v), 3) for v in size], yaw=self.b['yaw']))


def jar(F, x, z, y, s=1.0, broken=False, lid=True):
    prof = [(0.0, 0.16), (0.12, 0.27), (0.35, 0.33), (0.6, 0.3), (0.78, 0.2), (0.86, 0.17)]; n = 3 if broken else len(prof) - 1
    for (h0, r0), (h1, r1) in zip(prof[:n], prof[1:n + 1]): F.log((x, y + h0 * s, z), (x, y + h1 * s, z), r0 * s, r1 * s, ONGGI, sides=10)
    if broken:
        for _ in range(7): stone(F.m, F.w(x + F.rng.uniform(-0.5, 0.5), y + 0.04, z + F.rng.uniform(-0.5, 0.5)), F.rng.uniform(0.08, 0.2), ONGGI, F.rng)
    elif lid: F.log((x, y + 0.86 * s, z), (x, y + 0.9 * s, z), 0.21 * s, 0.19 * s, ONGGI, sides=10); F.log((x, y + 0.9 * s, z), (x, y + 0.96 * s, z), 0.04, 0.05, YUGI, sides=6)
    return y + 0.86 * s


def pipe_run(F, pts, r=0.065):
    """an onggi pipe through the points: short fired lengths, a brass band at every joint."""
    for a, b in zip(pts[:-1], pts[1:]):
        a = np.array(a, float); b = np.array(b, float); L = np.linalg.norm(b - a); n = max(1, int(round(L / 0.45)))
        for k in range(n):
            p0 = a + (b - a) * k / n; p1 = a + (b - a) * (k + 1) / n; F.log(tuple(p0), tuple(p1), r * 1.08, r * 0.94, ONGGI); d = (p1 - p0) / np.linalg.norm(p1 - p0) * 0.03; F.log(tuple(p1 - d), tuple(p1 + d), r * 1.3, r * 1.3, YUGI)


def lamp(F, x, z, lit):
    y = F.eave - 0.72; F.log((x, F.eave - 0.08, z), (x, y + 0.34, z), 0.012, 0.012, LACQ, sides=4)
    F.box((x, y + 0.32, z), (0.27, 0.04, 0.27), LACQ); F.box((x, y + 0.16, z), (0.2, 0.28, 0.2), PAPER if lit else DULL); F.box((x, y, z), (0.25, 0.04, 0.25), LACQ)
    for sx in (-1, 1):
        for sz in (-1, 1): F.box((x + sx * 0.105, y + 0.16, z + sz * 0.105), (0.02, 0.3, 0.02), LACQ)
    F.lamps.append(dict(pos=[round(float(v), 3) for v in F.w(x, y + 0.16, z)], lit=bool(lit), realm=F.b['realm']))


def brazier(F, x, z, side):
    y = F.floor
    for a in (0.3, 2.4, 4.5): F.log((x + math.cos(a) * 0.2, y, z + math.sin(a) * 0.2), (x + math.cos(a) * 0.16, y + 0.3, z + math.sin(a) * 0.16), 0.03, 0.03, YUGI, sides=5)
    F.log((x, y + 0.3, z), (x, y + 0.52, z), 0.2, 0.3, YUGI, sides=10); F.log((x, y + 0.5, z), (x, y + 0.53, z), 0.26, 0.24, DG + 'CaveSlag', sides=10)
    wx = side * (F.w_ / 2 + 0.1); pipe_run(F, [(x, y + 0.5, z), (wx, y + 1.1, z), (wx, F.eave - 0.25, z)], 0.055); F.solid((x, y + 0.27, z), (0.6, 0.54, 0.6))


def roof_kit(F):
    y = F.eave + F.b.get('roof_rise', 1.1); n = int(F.rng.integers(3, 6)); x0 = -0.5 * (n - 1) * 0.55
    for k in range(n): jar(F, x0 + k * 0.55 + F.rng.uniform(-0.04, 0.04), 0.0, y - 0.03, s=0.42, lid=False)
    vx = x0 - 0.7; F.log((vx, y - 0.05, 0.0), (vx, y + 0.9, 0.0), 0.02, 0.016, YUGI, sides=5)
    for a in range(4): ang = a * math.pi / 2 + 0.4; F.m.box(F.w(vx + math.cos(ang) * 0.17, y + 0.82, math.sin(ang) * 0.17), F.ax * math.cos(ang) + F.az * math.sin(ang), UP, F.az * math.cos(ang) - F.ax * math.sin(ang), (0.3, 0.1, 0.012), YUGI)


def heap(F, x, z, mat, n=16, r=0.5):
    for _ in range(n): a = F.rng.uniform(0, 2 * math.pi); d = r * math.sqrt(F.rng.random()); s = F.rng.uniform(0.08, 0.22); stone(F.m, F.w(x + math.cos(a) * d, F.floor + 0.2 * (1 - d / r) * F.rng.random() + s * 0.3, z + math.sin(a) * d), s, mat, F.rng)


def dress(F, tier):
    rng = F.rng; w, d = F.w_, F.d_; side = int(rng.choice([-1, 1])); ore = DG + 'CaveOre_' + F.b['realm']; slag = DG + 'CaveSlag'
    if tier == 'rich':
        for sx in (-1, 1): lamp(F, sx * (w / 2 - 0.5), d / 2 + 0.3, True)
        jx, jz = side * (w / 2 + 0.45), rng.uniform(-d / 4, d / 4); top = jar(F, jx, jz, F.floor); F.solid((jx, F.floor + 0.43, jz), (0.66, 0.86, 0.66)); wx = side * (w / 2 + 0.1)
        F.log((jx, top + 0.08, jz), (jx, top + 0.3, jz), 0.06, 0.16, YUGI)      # the funnel
        pipe_run(F, [(jx, top + 0.3, jz), (wx, top + 0.75, jz), (wx, F.eave - 0.22, jz), (wx, F.eave - 0.22, jz + rng.uniform(1.2, min(2.6, d / 2)) * rng.choice([-1, 1]))])
        brazier(F, -side * (w / 2 + 0.55), rng.uniform(-d / 4, d / 4), -side); roof_kit(F); heap(F, -side * (w / 2 + 1.0), -d / 2 + 0.4, slag, 14, 0.45)
    elif tier == 'middle':
        lamp(F, side * (w / 2 - 0.5), d / 2 + 0.3, True)
        if rng.random() < 0.55:
            jx, jz = -side * (w / 2 + 0.45), rng.uniform(-d / 4, d / 4); top = jar(F, jx, jz, F.floor); F.solid((jx, F.floor + 0.43, jz), (0.66, 0.86, 0.66)); wx = -side * (w / 2 + 0.1)
            pipe_run(F, [(jx, top + 0.1, jz), (wx, top + 0.6, jz), (wx, F.eave - 0.22, jz)])
        else: brazier(F, -side * (w / 2 + 0.55), rng.uniform(-d / 4, d / 4), -side)
        if rng.random() < 0.5: heap(F, side * (w / 2 + 0.8), -d / 2 + 0.3, slag, 10, 0.4)
    elif tier == 'poor':
        r = rng.random()
        if r < 0.45: lamp(F, side * (w / 2 - 0.5), d / 2 + 0.3, False)
        elif r < 0.8: jar(F, side * (w / 2 + 0.45), rng.uniform(-d / 4, d / 4), F.floor, broken=True)
        if rng.random() < 0.3: heap(F, -side * (w / 2 + 0.6), 0.0, ore, 6, 0.3)
    elif tier == 'ruin':
        if rng.random() < 0.7: jar(F, rng.uniform(-w / 3, w / 3), rng.uniform(-d / 3, d / 3), F.floor, broken=True)
        heap(F, rng.uniform(-w / 3, w / 3), rng.uniform(-d / 3, d / 3), slag, 12, 0.5)


def main():
    B.utf8(); cfg = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8')); T = cfg['tiers']; builds = []
    hm = json.loads((B.RB / 'Hamlet308/layout.json').read_text(encoding='utf-8'))
    for h, c in zip(hm['houses'], hm['colliders']):      # the hamlet's front is its -z face; the collider box runs from the ground to the eave
        if h['style'] == 'stilt': continue      # on posts over the bank: nothing stands beside it (round 1)
        builds.append(dict(src='Hamlet308', realm=h['realm'], style=h['style'], x=h['x'], z=h['z'], yaw=h['yaw'], w=h['w'], d=h['d'], front=-1.0, floor=c['center'][1] - c['size'][1] / 2 + T['hamlet_floor'].get(h['style'], 0.2), eave=c['center'][1] + c['size'][1] / 2, tier=T['hamlet'][h['style']], roof_rise=1.1))
    tw = json.loads((B.RB / 'Town308/town308.json').read_text(encoding='utf-8'))
    for sl in tw['slices']:
        if not sl.get('enabled', True): continue
        for r in sl['rows']:
            k = tw['kinds'][r['kind']]; tier = T['town'].get(r['kind'])
            if tier is None or 'plinth' not in k or 'y_offline' not in r: continue
            builds.append(dict(src='Town308', realm='Hwanggyeong', style=r['kind'], x=r['x'], z=r['z'], yaw=r['yaw'], w=k['plinth'][0], d=k['plinth'][1], front=1.0, floor=r['y_offline'], eave=r['y_offline'] + k['top_m'], tier=tier, roof_rise=T['town_roof_rise']))
    meshes = {}; cols = []; lamps = []; count = {}
    for i, b in enumerate(builds):
        m = meshes.setdefault(b['realm'], Mesh(f"Maseok308_{b['realm']}")); F = Frame(b, m, np.random.default_rng(seed_of(cfg['seed'], b['src'], i))); dress(F, b['tier']); cols += F.cols; lamps += F.lamps; count[b['tier']] = count.get(b['tier'], 0) + 1
    S = cfg.get('strings')      # the ruined temple: lotus lanterns on two sagging lines between poles (the temple's steam idiom: brass caps, a gear ring)
    if S:
        f = B.load_height(B.ROOT / json.loads((B.ROOT / 'Tools/Unity/Stage308_dungeon/dungeon308.json').read_text(encoding='utf-8'))['height']); gy = lambda x, z: float(B.sample_tri(f, x, z))
        m = Mesh('Maseok308_TempleStrings'); meshes['TempleStrings'] = m; rng = np.random.default_rng(seed_of(cfg['seed'], 'strings')); nth = 0
        for line in S['lines']:
            tops = []
            for x, z in line:
                g = gy(x, z); top = np.array([x + rng.uniform(-0.08, 0.08), g + S['poleM'] + rng.uniform(-0.2, 0.2), z + rng.uniform(-0.08, 0.08)]); tops.append(top)
                m.log([x, g - 0.3, z], top, 0.11, 0.08, 'wood_dark', rng=rng, wob=0.08); m.log([x, g + 1.2, z], [x, g + 1.32, z], 0.15, 0.15, S['brass'], sides=8); m.log(top - [0, 0.25, 0], top + [0, 0.05, 0], 0.12, 0.1, S['brass'], sides=8)
                cols.append(dict(center=[round(x, 3), round(g + 1.2, 3), round(z, 3)], size=[0.26, 2.4, 0.26], yaw=0.0))
            for a_, b_ in zip(tops[:-1], tops[1:]):
                L = float(np.linalg.norm(b_ - a_)); n = max(2, int(L / 0.6)); pts = [a_ + (b_ - a_) * t - np.array([0, S['sagM'] * 4 * t * (1 - t), 0]) for t in np.linspace(0, 1, n + 1)]
                for p0, p1 in zip(pts[:-1], pts[1:]): m.log(p0, p1, 0.012, 0.012, S['iron'], sides=4)
                for t in np.arange(S['everyM'] / L, 1.0 - 0.02, S['everyM'] / L):
                    c = a_ + (b_ - a_) * t - np.array([0, S['sagM'] * 4 * t * (1 - t), 0]); y = c[1]; x, z = c[0], c[2]; s_ = rng.uniform(0.9, 1.15)
                    m.log([x, y, z], [x, y - 0.14, z], 0.008, 0.008, S['iron'], sides=4); m.log([x, y - 0.14, z], [x, y - 0.2, z], 0.05 * s_, 0.13 * s_, S['brass'], sides=8)      # the brass cap
                    m.log([x, y - 0.2, z], [x, y - 0.34, z], 0.13 * s_, 0.17 * s_, PAPER, sides=8); m.log([x, y - 0.34, z], [x, y - 0.48, z], 0.17 * s_, 0.09 * s_, PAPER, sides=8)      # the lotus body, glowing paper
                    for k_ in range(8): ang = k_ * math.pi / 4; m.box([x + math.cos(ang) * 0.185 * s_, y - 0.34, z + math.sin(ang) * 0.185 * s_], [math.cos(ang), 0, math.sin(ang)], UP, [-math.sin(ang), 0, math.cos(ang)], (0.035, 0.03, 0.03), S['brass'])      # a gear ring round its waist
                    m.log([x, y - 0.48, z], [x, y - 0.62, z], 0.012, 0.004, S['brass'], sides=4)
                    lamps.append(dict(pos=[round(float(x), 3), round(float(y - 0.34), 3), round(float(z), 3)], lit=True, realm='TempleStrings', light=nth % S['lightEvery'] == 0)); nth += 1
    for realm in meshes:      # at most maxLights lit lamps of a realm carry a light; the others only show their paper
        lit = [l for l in lamps if l['realm'] == realm and l['lit']]; step = max(1, math.ceil(len(lit) / cfg['maxLights']))
        if realm == 'TempleStrings': continue
        for k, l in enumerate(lit): l['light'] = k % step == 0
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    for old in (OUT / 'Meshes').glob('*.json'): old.unlink()
    drawn = []
    for realm, m in meshes.items():
        if m.v: B.write_json(OUT / 'Meshes' / f'{m.name}.json', m.dump(), indent=None); drawn.append(m.name)
    B.write_json(OUT / 'layout.json', dict(version=cfg['version'], drawn=drawn, colliders=cols, lamps=[dict(pos=l['pos'], light=bool(l.get('light'))) for l in lamps if l['lit']], cullM=cfg['cullM'], light=cfg['light']))
    print('buildings', len(builds), count, '| lit lamps', sum(1 for l in lamps if l['lit']), 'with a light', sum(1 for l in lamps if l.get('light')), '| vertices', {r: len(m.v) for r, m in meshes.items()})


if __name__ == '__main__':
    sys.exit(main())
