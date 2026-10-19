"""D308-117 C3b (SPEC-FIELD-FIRST-HOUR-308 §8.7, TEST): the look pass of the two dungeon front sections - 뿌리 굴 cr_root and
숯가마 골 땅굴 cr_kiln. Writes Tools/Unity/Stage308_dungeon/dress308/<id>.json for the editor tool DungeonDress308 (apply:<id>).

From the built layout.json (Tools/Art/dungeon308.py) it rebuilds the dungeon's local frame and cells, then:
  - walk boxes: each cell's centre square and a corridor from the centre to every doorway of the cell (half width WALK_HALF);
    only flat pieces (< 0.15 m) may lie in them
  - keep-outs: the door, every reward and its stand, the ledges (both levels), the C3 front enemies (feet and patrol point),
    the sack evidence point, the dungeon's own lanterns and rocks
  - wall spots: along every solid stretch of wall, a few metres apart, set off the wall face; doorway gaps are skipped
  - items: the hero props first (Meshy kiln mouths, charcoal heaps, bone piles, fungus logs), then the owned dressing, each on the
    first seeded spot that clears the walk boxes, the keep-outs and the items already set (footprint circles); rows that find no
    spot are reported, not forced
  - kiln only: timber sets (charred posts + cap) at both ends of every cell, the plaster / brick materials swapped for soot earth,
    and one lantern a room (a cell had none; D308-62 allows two)
Deterministic (seed per dungeon). python Tools/Art/dungeon308_dress.py [cr_root] [cr_kiln]
"""
import json, math, random, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LAYOUT = ROOT / 'Art/World/Compact/Rebuild/Dungeon308'
CFG = ROOT / 'Tools/Unity/Stage308_dungeon/dungeon308.json'
FRONT = ROOT / 'Tools/Unity/Stage308_populate/dungeon_front308.json'
OUT = ROOT / 'Tools/Unity/Stage308_dungeon/dress308'
VERSION = 'c3b.6'
WALK_HALF = 1.5
A = 'Assets/'
FLORA = A + '_Project/Art/World/Flora308/Prefabs/'
FEST = A + 'KoreanTraditionalFestival/Prefabs/'
TREAS = A + 'Korea_TreasureProps/Prefabs/'
DRESS = A + '_Project/Art/World/Dungeon308/Dress308/'
DMAT = A + '_Project/Art/World/Dungeon308/Materials/'
MESHY = ('kiln_mouth', 'charcoal_heap', 'bone_pile', 'fungus_log', 'root_mass')

# name: source (prefab | mesh + material), measured size (DungeonDress308 probe, x y z), fit target and axis (0 x, 1 y, 2 z, 3 widest
# horizontal - the only one that does not turn with the yaw; use it for anything set against a wall), seat, solid (box collider + carving obstacle), rot (x, z tilt before the yaw)
KIT = {
    'kiln_mouth': dict(mesh=DRESS + 'Meshes/dd_kiln_mouth.asset', material=DRESS + 'Materials/DD_kiln_mouth.mat', size=(1.6, 1.32, 0.42), fit=1.6, axis=3, solid=True),
    'charcoal_heap': dict(mesh=DRESS + 'Meshes/dd_charcoal_heap.asset', material=DRESS + 'Materials/DD_charcoal_heap.mat', size=(1.4, 0.62, 1.4), fit=1.4, axis=3, solid=True),
    'bone_pile': dict(mesh=DRESS + 'Meshes/dd_bone_pile.asset', material=DRESS + 'Materials/DD_bone_pile.mat', size=(1.3, 0.54, 1.3), fit=1.3, axis=3, solid=True),
    'fungus_log': dict(mesh=DRESS + 'Meshes/dd_fungus_log.asset', material=DRESS + 'Materials/DD_fungus_log.mat', size=(1.7, 0.67, 1.06), fit=1.7, axis=3, solid=True),
    'root_mass': dict(mesh=DRESS + 'Meshes/dd_root_mass.asset', material=DRESS + 'Materials/DD_root_mass.mat', size=(3.0, 2.96, 3.0), fit=3.0, axis=3, seat='ceiling'),      # hangs from the cave roof, its foot ~3.9 m over the floor
    'timber_stack': dict(mesh=A + '_Project/Art/World/Mine306/Meshes/TimberStack_LOD0.asset', material=A + '_Project/Art/World/Mine306/Materials/Mine306_Timber.mat', size=(1.7, 0.88, 3.51), fit=2.6, axis=3, solid=True, along=2),
    'sack': dict(prefab=FEST + 'SM_SackOfRice.prefab', size=(1.14, 0.93, 0.52), fit=0.62, axis=1),
    'firewood': dict(prefab=A + '_Project/Art/Characters/Npc306/Tools/PF_NpcTool306_Firewood.prefab', size=(0.32, 0.11, 0.61), fit=0.9, axis=3),
    'jar': dict(prefab=TREAS + 'SM_061_Jar.prefab', size=(0.33, 0.33, 0.33), fit=0.62, axis=1),
    'pot': dict(prefab=TREAS + 'SM_046_Pot.prefab', size=(0.41, 0.41, 0.40), fit=0.45, axis=1),
    'shovel': dict(prefab=FEST + 'SM_GalaeShovel.prefab', size=(1.78, 2.68, 0.40), fit=1.75, axis=1, tilt=(-14.0, 0.0), along=0, pad=0.5),
    'mat': dict(prefab=FEST + 'SM_MeongseogMat.prefab', size=(1.78, 0.34, 1.49), fit=1.5, axis=3),
    'charlog': dict(prefab=FLORA + 'Pack_CharLog_thin.prefab', size=(3.05, 0.29, 0.28), fit=1.9, axis=3, along=0),
    'branches': dict(prefab=FLORA + 'Pack_CharBranches.prefab', size=(0.32, 0.34, 1.30), fit=1.2, axis=3, along=2),
    'post': dict(prefab=FLORA + 'Pack_CharLog_thin.prefab', size=(0.29, 3.05, 0.28), fit=0.0, axis=1, tilt=(0.0, 90.0)),
    'cap': dict(prefab=FLORA + 'Pack_CharLog_thin.prefab', size=(3.05, 0.29, 0.28), fit=0.0, axis=2),
    'root_wall_1': dict(prefab=A + 'SeyeonjeongPavilion/Prefabs/SM_TreeRoot_1.prefab', size=(3.40, 3.18, 0.84), fit=3.8, axis=3, tilt=(-90.0, 0.0), seat='centre', along=0),
    'root_wall_2': dict(prefab=A + 'SeyeonjeongPavilion/Prefabs/SM_TreeRoot_2.prefab', size=(3.81, 2.81, 0.80), fit=4.2, axis=3, tilt=(-90.0, 0.0), seat='centre', along=0),
    'root_wall_3': dict(prefab=A + 'SeyeonjeongPavilion/Prefabs/SM_TreeRoot_3.prefab', size=(3.10, 2.98, 0.78), fit=3.6, axis=3, tilt=(-90.0, 0.0), seat='centre', along=0),
    'root_hang': dict(prefab=FLORA + 'root_cluster_01.prefab', size=(4.14, 1.51, 2.68), fit=4.8, axis=3, tilt=(180.0, 0.0), seat='ceiling'),
    'root_hang_b': dict(prefab=A + 'SeyeonjeongPavilion/Prefabs/SM_TreeRoot_2.prefab', size=(3.81, 0.80, 2.81), fit=4.2, axis=3, tilt=(180.0, 0.0), seat='ceiling'),
    'moss_rock_a': dict(prefab=FLORA + 'rock_moss_set_02_rock09.prefab', size=(1.63, 0.61, 1.01), fit=1.5, axis=3),
    'moss_rock_b': dict(prefab=FLORA + 'rock_moss_set_02_rock12.prefab', size=(1.58, 0.74, 1.44), fit=1.4, axis=3),
    'moss_rock_c': dict(prefab=FLORA + 'rock_moss_set_01_rock03.prefab', size=(2.12, 1.12, 2.09), fit=1.8, axis=3),
    'floor_roots_a': dict(prefab=FLORA + 'pine_roots_a.prefab', size=(1.75, 0.14, 0.83), fit=1.9, axis=3, flat=True),
    'floor_roots_b': dict(prefab=FLORA + 'pine_roots_b.prefab', size=(1.63, 0.15, 1.00), fit=1.8, axis=3, flat=True),
    'stump': dict(prefab=FLORA + 'Pack_Stump_B_rotten.prefab', size=(0.89, 1.25, 0.69), fit=0.9, axis=1),
    'fern': dict(prefab=FLORA + 'fern_02_a.prefab', size=(0.55, 0.29, 0.62), fit=0.6, axis=3),
}

# cells are named as in layout.json (A1 = the entry cell). per dungeon: (kit name, how many, where: wall | corner | floor | ceiling | cells=[...] limits the cells)
PLAN = {
    'cr_kiln': dict(seed=11702, lampsPerCell=1, lamp=dict(intensity=10.0, range=13.0), hero=[
            ('kiln_mouth', ['A2:in', 'B2:out', 'B4:out']),
            ('charcoal_heap', ['A4', 'B3', 'B1', 'A3']),
        ], rows=[
            ('timber_stack', 3, 'wall', None), ('sack', 7, 'wall', None), ('firewood', 6, 'wall', None), ('charlog', 5, 'wall', None),
            ('jar', 4, 'corner', None), ('pot', 3, 'corner', None), ('shovel', 2, 'wall', None), ('mat', 2, 'wall', None), ('branches', 4, 'wall', None),
        ]),
    'cr_root': dict(seed=11701, lampsPerCell=1, lamp=dict(intensity=8.0, range=11.0), hero=[
            ('bone_pile', ['B2', 'A3', 'A1', 'B1']),
            ('fungus_log', ['A2', 'B3', 'B1']),
        ], rows=[
            ('root_mass', 12, 'ceiling', None), ('root_hang', 24, 'ceiling', None), ('root_hang_b', 12, 'ceiling', None), ('root_wall_1', 6, 'wall', None), ('root_wall_2', 6, 'wall', None), ('root_wall_3', 6, 'wall', None),
            ('moss_rock_a', 4, 'wall', None), ('moss_rock_b', 4, 'corner', None), ('moss_rock_c', 2, 'corner', None),
            ('floor_roots_a', 4, 'floor', None), ('floor_roots_b', 4, 'floor', None), ('stump', 3, 'corner', None), ('fern', 0, 'wall', None),
        ]),
}

KILN_MATS = dict(
    newMats=[
        dict(path=DRESS + 'Materials/DD_KilnWall.mat', source=DMAT + 'CaveRock_Cheongrim.mat', color=[0.33, 0.28, 0.22]),
        dict(path=DRESS + 'Materials/DD_KilnRoof.mat', source=DMAT + 'CaveRock_Cheongrim.mat', color=[0.2, 0.18, 0.16]),
        dict(path=DRESS + 'Materials/DD_KilnFloor.mat', source=DMAT + 'CaveRock_Cheongrim.mat', color=[0.27, 0.24, 0.2]),
    ],
    mats=[
        dict(renderer='Drawn_Wall', **{'from': 'brick_dark'}, to=DRESS + 'Materials/DD_KilnWall.mat'),
        dict(renderer='Drawn_Wall', **{'from': 'stone_rough'}, to=DRESS + 'Materials/DD_KilnWall.mat'),
        dict(renderer='Drawn_Roof', **{'from': 'stone_rough'}, to=DRESS + 'Materials/DD_KilnRoof.mat'),
        dict(renderer='Drawn_Floor', **{'from': 'paving'}, to=DRESS + 'Materials/DD_KilnFloor.mat'),
        dict(renderer='Drawn_Prop', **{'from': 'wood_dark'}, to=DMAT + 'CaveTimber_Char.mat'),
        dict(renderer='Drawn_Prop', **{'from': 'brick_dark'}, to=DRESS + 'Materials/DD_KilnWall.mat'),
        dict(renderer='Drawn_Prop', **{'from': 'stone_rough'}, to=DRESS + 'Materials/DD_KilnWall.mat'),
    ])
ROOT_MATS = dict(
    newMats=[dict(path=DRESS + 'Materials/DD_RootRock.mat', source=DMAT + 'CaveRock_Cheongrim.mat', color=[0.2, 0.21, 0.15])],
    mats=[dict(renderer='Drawn_Prop', **{'from': m}, to=DMAT + 'CaveTimber_Char.mat') for m in ('Mine306_TimberBoard', 'wood_board', 'wood_dark')]      # the cave rock stays the realm's own (D308-62, Dungeon308 check)
         + [dict(renderer='Drawn_Prop', **{'from': 'stone_rough'}, to=DRESS + 'Materials/DD_RootRock.mat')])
MESHY_MATS = [dict(path=DRESS + f'Materials/DD_{n}.mat', shader='Universal Render Pipeline/Lit', baseMap=DRESS + f'Textures/DD_{n}_Base.png',
                   normalMap=DRESS + f'Textures/DD_{n}_Normal.png', color=[0.82, 0.8, 0.78], smoothness=0.05) for n in MESHY]


class Frame:
    def __init__(self, L, G):
        s = L['site']; self.n = L['cols']; self.cw, self.cd = L['cell']; self.y0 = L['podium_y']; self.levels = L['levels']
        c, sn = math.cos(math.radians(s['yaw'])), math.sin(math.radians(s['yaw'])); self.ax = (c, -sn); self.az = (sn, c)
        W, D = self.n * self.cw, 2 * self.cd
        self.o = (s['x'] - self.ax[0] * W / 2 - self.az[0] * D / 2, s['z'] - self.ax[1] * W / 2 - self.az[1] * D / 2)
        self.roof = G['roof_h']; self.t = G['wall_t']; self.dw = G['door_w']; self.cave = L['cave']
        self.chain = [('A', k) for k in range(self.n)] + [('B', k) for k in range(self.n - 1, -1, -1)]

    def w(self, x, z):
        return (self.o[0] + self.ax[0] * x + self.az[0] * z, self.o[1] + self.ax[1] * x + self.az[1] * z)

    def dirw(self, dx, dz):
        return (self.ax[0] * dx + self.az[0] * dz, self.ax[1] * dx + self.az[1] * dz)

    def level(self, cell):
        return self.levels[cell[0] + str(cell[1] + 1)]

    def rect(self, cell):
        k = cell[1]; return (k * self.cw, (k + 1) * self.cw, 0.0 if cell[0] == 'A' else self.cd, self.cd if cell[0] == 'A' else 2 * self.cd)

    def doorways(self, cell):
        """local doorway mids of a cell: the entrance, the door, the chain links"""
        out = []
        if cell == ('A', 0): out += [(self.cw / 2, 0.0), (self.cw / 2, self.cd)]
        if cell == ('B', 0): out += [(self.cw / 2, self.cd)]
        i = self.chain.index(cell)
        for j in (i - 1, i + 1):
            if 0 <= j < len(self.chain):
                o = self.chain[j]
                if o[0] == cell[0]: x = max(o[1], cell[1]) * self.cw; z = self.cd / 2 if cell[0] == 'A' else 1.5 * self.cd; out.append((x, z))
                else: out.append(((cell[1] + 0.5) * self.cw, self.cd))
        return out


HERO_KINDS = ('kiln_mouth', 'charcoal_heap', 'bone_pile', 'fungus_log')


def yaw_facing(d):
    """yaw (deg) that turns the item's local -Z to the world direction d"""
    return round(math.degrees(math.atan2(-d[0], -d[1])) % 360.0, 1)


def build(did):
    L = json.loads((LAYOUT / did / 'layout.json').read_text(encoding='utf-8')); cfg = json.loads(CFG.read_text(encoding='utf-8'))
    G = {k: cfg['grammar'][k] for k in ('roof_h', 'wall_t', 'door_w')}
    roofs = [c['center'][1] - c['size'][1] / 2 - L['podium_y'] for c in L['colliders'] if abs(c['size'][1] - 0.18) < 1e-3]
    G['roof_h'] = round(min(roofs), 3)      # the lowest roof slab over a cell on the entry level = the roof height (4.7 room, 6.5 cave)
    F = Frame(L, G); P = PLAN[did]; rng = random.Random(P['seed'])
    walk, keep, placed, items, report = [], [], [], [], []

    def wbox(what, cell, x0, x1, z0, z1):
        (ax_, az_), (bx, bz) = F.w(x0, z0), F.w(x1, z1); y = F.y0 + F.level(cell)
        walk.append(dict(what=what, min=[round(min(ax_, bx), 3), round(y, 3), round(min(az_, bz), 3)], max=[round(max(ax_, bx), 3), round(y + 2.2, 3), round(max(az_, bz), 3)], clutter=0.15))

    cells = F.chain
    for cell in cells:
        x0, x1, z0, z1 = F.rect(cell); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; nm = cell[0] + str(cell[1])
        wbox(nm + ' centre', cell, cx - WALK_HALF, cx + WALK_HALF, cz - WALK_HALF, cz + WALK_HALF)
        for (dx, dz) in F.doorways(cell):
            if abs(dx - cx) > abs(dz - cz): wbox(nm + ' to door', cell, min(cx, dx), max(cx, dx), cz - WALK_HALF, cz + WALK_HALF)
            else: wbox(nm + ' to door', cell, cx - WALK_HALF, cx + WALK_HALF, min(cz, dz), max(cz, dz))
    def k_(what, p, r, h=2.4): keep.append(dict(what=what, pos=[round(v, 3) for v in p], r=r, h=h))
    d = L['door']; k_('door', d['pos'], 1.7, 2.8)
    for rw in L['rewards']: k_(rw['id'], rw['pos'], 1.2); k_(rw['id'] + ' stand', rw['stand'], 1.0)
    for lg in L['ledges']: k_('ledge ' + lg['feature'], lg['pos'], 1.9, 4.2)      # up to the head of a player on the upper floor (2.3 + 1.9); the cap beams run over it
    for lm in L['lamps']: k_('lantern', lm['pos'], 1.0)
    for rk in L['rocks']: k_('rock ' + rk['prefab'], rk['pos'], rk['size'] * 0.6 + 0.3)
    for e in json.loads(FRONT.read_text(encoding='utf-8'))['enemies']:
        if e['dungeon'] == did: k_(e['id'] + ' feet', e['feet'], 1.7); k_(e['id'] + ' patrol', e['patrol2'], 1.4)
    if did == 'cr_kiln': k_('dg308_cr_kiln_sack', [2754.2, F.y0, 2705.0], 1.1)

    ext = lambda r: r if isinstance(r, tuple) else (r, r)      # world half extents (x, z) of a footprint

    def in_walk(p, r, y):
        hx, hz = ext(r)
        for wb in walk:
            if wb['min'][1] - 0.01 <= y <= wb['min'][1] + 0.01 and wb['min'][0] - hx < p[0] < wb['max'][0] + hx and wb['min'][2] - hz < p[1] < wb['max'][2] + hz: return wb['what']
        return None

    def in_keep(p, r, y0, y1):
        hx, hz = ext(r)
        for kp in keep:      # the distance from the keep-out centre to the footprint rectangle (as DungeonDress308 check measures it)
            qx = min(max(kp['pos'][0], p[0] - hx), p[0] + hx); qz = min(max(kp['pos'][2], p[1] - hz), p[1] + hz)
            if math.hypot(qx - kp['pos'][0], qz - kp['pos'][2]) < kp['r'] and y0 < kp['pos'][1] + kp['h'] and y1 > kp['pos'][1] + 0.05: return kp['what']
        return None

    def clash(p, r, y0, y1):
        return any(math.hypot(p[0] - q[0], p[1] - q[1]) < r + q[2] and y0 < q[4] and y1 > q[3] for q in placed)

    def dims(name):
        K = KIT[name]; sx, sy, sz = K['size']
        k = 1.0 if not K['fit'] else K['fit'] / (sx if K['axis'] == 0 else sy if K['axis'] == 1 else sz if K['axis'] == 2 else max(sx, sz))
        return sx * k, sy * k, sz * k

    def spots(cell, where, depth):
        """candidate (local x, z, inward direction) on the solid walls of a cell, `depth` = the item's depth off the wall"""
        x0, x1, z0, z1 = F.rect(cell); half_t = F.t / 2 + (0.35 if F.cave else 0.05); out = []; gaps = F.doorways(cell); g = F.dw / 2 + 0.5
        sides = [('z0', (x0, z0), (x1, z0), (0, 1)), ('z1', (x0, z1), (x1, z1), (0, -1)), ('x0', (x0, z0), (x0, z1), (1, 0)), ('x1', (x1, z0), (x1, z1), (-1, 0))]
        for nm_, a, b, inward in sides:
            L_ = math.hypot(b[0] - a[0], b[1] - a[1]); step = 0.6
            for u in [i * step for i in range(1, int(L_ / step))]:
                px = a[0] + (b[0] - a[0]) * u / L_; pz = a[1] + (b[1] - a[1]) * u / L_
                if any(math.hypot(px - gx, pz - gz) < g for gx, gz in gaps): continue
                corner = min(u, L_ - u) < 1.4
                if where == 'corner' and not corner: continue
                off = half_t + depth / 2 + 0.05
                out.append((px + inward[0] * off, pz + inward[1] * off, inward, nm_, corner))
        rng.shuffle(out); return out

    def put(name, cell, where, side=None):
        K = KIT[name]; sx, sy, sz = dims(name); along = K.get('along', 0); lvl = F.level(cell); y = F.y0 + lvl
        depth = sz if along == 0 else sx
        if where == 'ceiling':
            x0, x1, z0, z1 = F.rect(cell); cands = [(rng.uniform(x0 + 1.6, x1 - 1.6), rng.uniform(z0 + 1.6, z1 - 1.6), (0, 1), 'ceiling', False) for _ in range(60)]
        elif where == 'floor':
            x0, x1, z0, z1 = F.rect(cell); cands = [(rng.uniform(x0 + 1.6, x1 - 1.6), rng.uniform(z0 + 1.6, z1 - 1.6), (0, 1), 'floor', False) for _ in range(30)]
        else:
            cands = spots(cell, where, depth)
            if side: cands = [c for c in cands if c[3] == side] + [c for c in cands if c[3] != side]
        r = 0.5 * math.hypot(sx, sz)
        for (lx, lz, inward, nm_, corner) in cands:
            p = F.w(lx, lz); dwld = F.dirw(*inward)
            if where == 'ceiling': y0_, y1_ = y + F.roof - sy, y + F.roof
            else: y0_, y1_ = y, y + sy
            if where in ('wall', 'corner'):      # wall-set: the footprint is a rectangle, depth along the inward direction (+10 % for the yaw jitter)
                hd, ha = (sz if along == 0 else sx) / 2 * 1.1, (sx if along == 0 else sz) / 2 * 1.1
                hd += K.get('pad', 0.0); fp = (hd, ha) if abs(dwld[0]) > abs(dwld[1]) else (ha, hd)
            else: fp = r
            if where not in ('ceiling',) and not K.get('flat') and in_walk(p, fp, y): continue
            if in_keep(p, fp, y0_, y1_): continue      # a hanging item too (a reward on a ledge sits high)
            if clash(p, r * (0.4 if where == 'ceiling' else 0.85), y0_, y1_): continue      # hanging roots may tangle into each other
            if where in ('wall', 'corner'):
                yaw = yaw_facing(dwld) if along == 0 else (yaw_facing(dwld) + 90.0) % 360
                if not K.get('solid'): yaw = (yaw + rng.uniform(-12, 12)) % 360
            else: yaw = round(rng.uniform(0, 360), 1)
            tilt = K.get('tilt', (0.0, 0.0)); row = dict(id=f'{name}_{len([i for i in items if i["kind"] == name])}', kind=name, cell=cell[0] + str(cell[1] + 1))
            if 'prefab' in K: row['prefab'] = K['prefab']
            else: row['mesh'] = K['mesh']; row['material'] = K['material']
            seat = K.get('seat', 'ceiling' if where == 'ceiling' else 'floor')
            if seat == 'ceiling': py = y + F.roof      # the slab underside; the item's top goes `sink` into it
            elif seat == 'centre': py = y + sy / 2 - 0.1
            else: py = y
            row.update(pos=[round(p[0], 3), round(py, 3), round(p[1], 3)], rot=[tilt[0], yaw, tilt[1]], seat=seat, fit=K['fit'], fitAxis=float(K['axis']), sink=0.04 if seat == 'floor' else 0.0, solid=bool(K.get('solid')))
            if seat == 'ceiling': row['sink'] = 0.35 if F.cave else 0.05
            items.append(row); placed.append((p[0], p[1], r, y0_, y1_)); return row
        report.append(f'{name} in {cell}: no free spot'); return None

    def cell_of(s):
        return (s[0], int(s[1]) - 1)

    # kiln: timber sets first (they fix the rhythm of the tunnel)
    if did == 'cr_kiln':
        for cell in cells:
            x0, x1, z0, z1 = F.rect(cell); y = F.y0 + F.level(cell); h = F.roof - 0.05
            for xe in (x0 + 0.45, x1 - 0.45):
                for ze, inward in ((z0 + F.t / 2 + 0.16, 1), (z1 - F.t / 2 - 0.16, -1)):
                    p = F.w(xe, ze)
                    if in_keep(p, 0.35, y, y + h): continue
                    items.append(dict(id=f'post_{len([i for i in items if i["kind"] == "post"])}', kind='post', cell=cell[0] + str(cell[1] + 1), prefab=KIT['post']['prefab'],
                                      pos=[round(p[0], 3), round(y, 3), round(p[1], 3)], rot=[0.0, round(rng.uniform(0, 360), 1), 90.0], scale=[1.0, 0.6, 0.6], seat='floor', fit=h, fitAxis=1.0, sink=0.05, solid=False))
                    placed.append((p[0], p[1], 0.2, y, y + h))
                a, b = F.w(xe, z0 + F.t / 2), F.w(xe, z1 - F.t / 2); m = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2); span = math.hypot(b[0] - a[0], b[1] - a[1])
                cy = y + F.roof - 0.2
                if any(in_keep((a[0] + (b[0] - a[0]) * u / 8, a[1] + (b[1] - a[1]) * u / 8), 0.25, cy - 0.2, cy + 0.2) for u in range(9)): continue
                along_world = (b[0] - a[0], b[1] - a[1]); yaw = round(math.degrees(math.atan2(along_world[1], along_world[0])) * -1 % 360.0, 1)      # the log's long axis (local x) along the span
                items.append(dict(id=f'cap_{len([i for i in items if i["kind"] == "cap"])}', kind='cap', cell=cell[0] + str(cell[1] + 1), prefab=KIT['cap']['prefab'],
                                  pos=[round(m[0], 3), round(cy, 3), round(m[1], 3)], rot=[0.0, yaw, 0.0], scale=[1.0, 0.55, 0.55], seat='centre', fit=span + 0.1, fitAxis=3.0, sink=0.0, solid=False))
    for name, where_list in P['hero']:
        for s in where_list:
            cs, _, side = s.partition(':'); cell = cell_of(cs)
            put(name, cell, 'wall' if name in ('kiln_mouth', 'charcoal_heap') else 'corner', {'in': 'z1' if cell[0] == 'A' else 'z0', 'out': 'z0' if cell[0] == 'A' else 'z1'}.get(side))
    lamps = []      # lanterns right after the heroes, before the rows fill the walls
    for cell in cells[:] if P['lampsPerCell'] else []:
        heroes = [it['pos'] for it in items if it['cell'] == cell[0] + str(cell[1] + 1) and it['kind'] in HERO_KINDS]
        def lamp_key(c):      # beside the cell's hero prop (1.2 - 2.2 m off it, nearest 1.6 m) first, else a corner, else any wall spot
            if not heroes: return (1, not c[4], 0.0)
            q = F.w(c[0], c[1]); d = min(math.hypot(q[0] - h[0], q[1] - h[2]) for h in heroes)
            return (0 if 1.2 <= d <= 2.2 else 1, not c[4] if not 1.2 <= d <= 2.2 else 0, abs(d - 1.6))
        for (lx, lz, inward, nm_, corner) in sorted(spots(cell, 'wall', 0.5), key=lamp_key):
            p = F.w(lx, lz); y = F.y0 + F.level(cell)
            if in_walk(p, 0.3, y) or in_keep(p, 0.4, y, y + 0.7) or clash(p, 0.35, y, y + 0.7): continue
            lamps.append(dict(cell=cell[0] + str(cell[1] + 1), pos=[round(p[0], 3), round(y + 0.04, 3), round(p[1], 3)], yaw=round(rng.uniform(0, 360), 1), **P.get('lamp', {})))
            placed.append((p[0], p[1], 0.35, y, y + 0.7)); break
        else: report.append(f'lantern in {cell}: no free spot')
    for name, count, where, only in P['rows']:
        pool = [c for c in cells if only is None or c[0] + str(c[1] + 1) in only]
        for i in range(count): put(name, pool[(i * 3 + rng.randrange(len(pool))) % len(pool)], where)
    margin = 1.6 if F.cave else 0.6      # cave walls bulge out (bulge_m 1.0) and the wall roots sit in them
    xs = [F.w(x, z) for x in (0, F.n * F.cw) for z in (0, 2 * F.cd)]; lo_y = F.y0 - 0.8; hi_y = F.y0 + max(F.levels.values()) + F.roof + 2.5
    data = dict(version=VERSION, id=did, doc='D308-117 C3b dress pass (SPEC-FIELD-FIRST-HOUR-308 §8.7). Written by Tools/Art/dungeon308_dress.py - do not edit by hand',
                boundsMin=[round(min(p[0] for p in xs) - margin, 2), round(lo_y, 2), round(min(p[1] for p in xs) - margin, 2)],
                boundsMax=[round(max(p[0] for p in xs) + margin, 2), round(hi_y, 2), round(max(p[1] for p in xs) + margin, 2)],
                newMats=MESHY_MATS + (KILN_MATS if did == 'cr_kiln' else ROOT_MATS)['newMats'], mats=(KILN_MATS if did == 'cr_kiln' else ROOT_MATS)['mats'],
                items=items, lamps=lamps, keep=keep, walk=walk, lampsPerCell=max(1, P['lampsPerCell']))
    OUT.mkdir(parents=True, exist_ok=True); (OUT / (did + '.json')).write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    kinds = {}
    for it in items: kinds[it['kind']] = kinds.get(it['kind'], 0) + 1
    print(did, 'items', len(items), kinds, 'lamps', len(lamps), 'walk', len(walk), 'keep', len(keep))
    for r_ in report: print('  NOTE', r_)


if __name__ == '__main__':
    for did in (sys.argv[1:] or list(PLAN)): build(did)
