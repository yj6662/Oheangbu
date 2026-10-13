"""SPEC-WORLD-HANYANG-308 phases 3-4 (§13): 종묘 · 사직단 · 종각 · 운종가 시전 · 주거지 · 생활 소품 as placements, merged solids and merged blocks.
python Tools/Art/hanyang308_town.py  ->  Tools/Unity/Stage308_hanyang/town308.json (+ Out/town_report.json, Out/town_anchors.json)
Consumed by `Hanyang308 town:import | town:lite1 | town:mats | town:meshes | town:blocks | town:apply[:<unit>]`.
Helpers (G, wall, platform, stairs, corridor ...) are the phase-2 ones, taken from hanyang308_palace.py (solid ids get the prefix T_).
Blocks: buildings of one neighbourhood cell merged per material into one mesh per LOD step by `town:blocks` (prefab TB_<id>)."""
import json, math, random, sys
import numpy as np
sys.path.insert(0, 'Tools/Art')
exec(compile(open('Tools/Art/hanyang308_palace.py', encoding='utf-8').read().split('# =====')[0].replace("'P_%s_%s'", "'T_%s_%s'"), 'palace_helpers', 'exec'))
R = random.Random(308); blocks = []; anchors = dict(shop_fronts=[], stalls=[], wells=[], laundry=[], plazas=[], taverns=[], gates=[]); counts = {}
GEN = ['Gen_Jongmyo_Jeongjeon', 'Gen_Jongmyo_Bay', 'Gen_Sajikdan', 'Gen_Sijeon_Row8', 'Gen_Giwa_Shop3', 'Gen_Giwa_4kan', 'Gen_Giwa_L', 'Gen_Soseuldaemun']
LITE3 = ['Gate_Oisamun', 'Hall_Naeposa', 'Gate_Hongsalmun', 'Lu_MangGyeongru', 'Prop_Bell', 'Choga_Tavern', 'Prop_Bench', 'Prop_Cart', 'Prop_Jars', 'Prop_Jige', 'Prop_WaterJar', 'Prop_Well_Changwon',
         'Choga_L', 'Choga_Long', 'Choga_Smithy', 'Choga_Bench', 'Choga_LShaped', 'Giwa_Sarangchae', 'Giwa_Anchae', 'Gate_Small_Changwon']
TINTS = [dict(name='h308_thatch', src='plaster', c=[0.62, 0.52, 0.33]), dict(name='h308_earth', src='plaster', c=[0.66, 0.53, 0.38]), dict(name='h308_straw', src='plaster', c=[0.78, 0.69, 0.47]),
         dict(name='h308_onggi', src='plaster', c=[0.33, 0.21, 0.15]), dict(name='h308_brass', src='plaster', c=[0.62, 0.50, 0.24]), dict(name='h308_cloth', src='plaster', c=[0.86, 0.84, 0.78]),
         dict(name='h308_maseok', src='stone_plain', c=[0.42, 0.52, 0.55])]
MAN = json.load(open(B.ROOT / 'Art/World/PackLite308/manifest.json', encoding='utf-8'))['items']
TRI = {e['name']: [l['tris'] if isinstance(l, dict) else l for l in (e['lods'] if isinstance(e['lods'], list) else e['lods'].values())] for e in MAN}

# ---------------------------------------------------------------- occupancy (1 m cells)
OX, OZ, NX, NZ = 1400, 2540, 960, 1400
OCC = np.zeros((NZ, NX), bool)


def occ_rect(x0, z0, x1, z1, pad=0.0):
    j0 = max(0, int(math.floor(min(x0, x1) - pad - OX))); j1 = min(NX, int(math.ceil(max(x0, x1) + pad - OX)) + 1); i0 = max(0, int(math.floor(min(z0, z1) - pad - OZ))); i1 = min(NZ, int(math.ceil(max(z0, z1) + pad - OZ)) + 1)
    if j1 > j0 and i1 > i0: OCC[i0:i1, j0:j1] = True


def to_world(cx, cz, yaw, lx, lz):
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); return cx + lx * c + lz * s_, cz - lx * s_ + lz * c


def pts(cx, cz, w, d, yaw, pad=0.0, st=1.0):
    nx = max(2, int(math.ceil((w + 2 * pad) / st)) + 1); nz = max(2, int(math.ceil((d + 2 * pad) / st)) + 1)
    return [to_world(cx, cz, yaw, -w / 2 - pad + (w + 2 * pad) * i / (nx - 1), -d / 2 - pad + (d + 2 * pad) * j / (nz - 1)) for i in range(nx) for j in range(nz)]


def free(cx, cz, w, d, yaw, pad=0.6):
    for x, z in pts(cx, cz, w, d, yaw, pad, 0.9):
        j, i = int(round(x - OX)), int(round(z - OZ))
        if not (0 <= j < NX and 0 <= i < NZ) or OCC[i, j]: return False
    return True


def mark(cx, cz, w, d, yaw, pad=0.6):
    for x, z in pts(cx, cz, w, d, yaw, pad, 0.7):
        j, i = int(round(x - OX)), int(round(z - OZ))
        if 0 <= j < NX and 0 <= i < NZ: OCC[i, j] = True


def ground(cx, cz, w, d, yaw):
    hs = [g(x, z) for x, z in pts(cx, cz, w, d, yaw, 0.0, 1.5)]; return min(hs), max(hs)


def line_block(P, r, st=1.0):
    rr = int(math.ceil(r)); yy, xx = np.mgrid[-rr:rr + 1, -rr:rr + 1]; disc = (xx * xx + yy * yy) <= r * r
    for (x0, z0), (x1, z1) in zip(P[:-1], P[1:]):
        L = math.hypot(x1 - x0, z1 - z0); n = max(1, int(L / st))
        for k in range(n + 1):
            j, i = int(round(x0 + (x1 - x0) * k / n - OX)), int(round(z0 + (z1 - z0) * k / n - OZ))
            if rr <= j < NX - rr and rr <= i < NZ - rr: OCC[i - rr:i + rr + 1, j - rr:j + rr + 1] |= disc


ROAD = {r['id']: r for r in J['roads']}
for r in J['roads']: line_block([p[:2] for p in r['line']], r['width'] / 2 + 1.2)
for s in J['sites']: occ_rect(s['x'] - s['w'] / 2, s['z'] - s['d'] / 2, s['x'] + s['w'] / 2, s['z'] + s['d'] / 2, 2.0)
occ_rect(X0 := J['palace_wall']['x0'], J['palace_wall']['z0'], J['palace_wall']['x1'], J['palace_wall']['z1'], 6.0)
for sg in J['wall']['segments']: line_block([p[:2] for p in sg['line']], 14.0, 4.0)
for gt in J['gates']: occ_rect(gt['x'] - 22, gt['z'] - 22, gt['x'] + 22, gt['z'] + 22)
for b in json.load(open(ST / 'Out/keep_boxes.json', encoding='utf-8'))['boxes']: occ_rect(b[0], b[2], b[1], b[3], 2.5)
for p in json.load(open(ST / 'content_points.json', encoding='utf-8'))['points']: occ_rect(p['x'] - 3, p['z'] - 3, p['x'] + 3, p['z'] + 3)
for br in J['p1c']['bridges'].values(): line_block([br['a'], br['b']], 9.0)
_W = B.wet(); _P = B.protected()
for i, j in zip(*np.nonzero(_W[OZ // 4:(OZ + NZ) // 4 + 1, OX // 4:(OX + NX) // 4 + 1])): occ_rect((j + OX // 4) * 4 - 10, (i + OZ // 4) * 4 - 10, (j + OX // 4) * 4 + 10, (i + OZ // 4) * 4 + 10)
for i, j in zip(*np.nonzero(_P[OZ // 4:(OZ + NZ) // 4 + 1, OX // 4:(OX + NX) // 4 + 1])): occ_rect((j + OX // 4) * 4 - 6, (i + OZ // 4) * 4 - 6, (j + OX // 4) * 4 + 6, (i + OZ // 4) * 4 + 6)
# inside the city wall only (polygon of the wall vertices)
_poly = [(v['x'], v['z']) for v in J['wall']['vertices']]
_X, _Z = np.meshgrid(np.arange(NX) + OX, np.arange(NZ) + OZ); INSIDE = B.poly_mask(_poly, _X, _Z).astype(bool) if hasattr(B, 'poly_mask') else np.ones((NZ, NX), bool)
OCC |= ~INSIDE
OCC0 = OCC.copy()


def face_yaw(fx, fz):
    """yaw that turns a -Z facing module toward (fx, fz)"""
    return math.degrees(math.atan2(-fx, -fz)) % 360


class Blk:
    """one merged block: buildings (prefab name, world position, yaw, scale, own top LOD)"""
    def __init__(self, bid, top=1, cut=0.5, cull=0.02): self.id = bid; self.top = top; self.cut = cut; self.cull = cull; self.h = []; blocks.append(self)
    def add(self, a, x, y, z, yaw=0.0, s=(1, 1, 1), l=-1): self.h.append(dict(a=a, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 2), s=[round(float(v), 4) for v in s], l=l))
    def tris(self): return sum(TRI[h['a']][h['l'] if h['l'] >= 0 else self.top] for h in self.h)
    def place(self, G_):
        if not self.h: blocks.remove(self); return
        ox = sum(h['p'][0] for h in self.h) / len(self.h); oz = sum(h['p'][2] for h in self.h) / len(self.h); oy = min(h['p'][1] for h in self.h); self.o = [round(ox, 2), round(oy, 2), round(oz, 2)]
        G_.prefab('TB_' + self.id, *self.o, n='TB_' + self.id)
    def out(self): return dict(id=self.id, o=self.o, top=self.top, cut=self.cut, cull=self.cull, houses=self.h)


def footing(G_, cx, cz, w, d, yaw, y, mat='stone_rough', lo=None, up=0.0):
    """막돌 기단 under a rotated footprint (axis-aligned box around it when the yaw is off-axis by little)"""
    a = round(yaw) % 180; ww, dd = (d, w) if 45 < a < 135 else (w, d); glo, ghi = ground(cx, cz, w, d, yaw)
    G_.box(mat, cx - ww / 2, (glo if lo is None else lo) - 0.5, cz - dd / 2, cx + ww / 2, y + up, cz + dd / 2)


def lowwall(G_, a, b, h=1.5, th=0.5, body='stone_rough', cap='h308_thatch', step=4.0, col=True):
    """돌담 · 토담: axis-aligned, steps with the ground"""
    ax, az = a; bx, bz = b; L = math.hypot(bx - ax, bz - az)
    if L < 0.8: return
    dx, dz = (bx - ax) / L, (bz - az) / L; n = max(1, round(L / step)); hx, hz = abs(dz) * th / 2, abs(dx) * th / 2
    for k in range(n):
        x0, z0 = ax + dx * L * k / n, az + dz * L * k / n; x1, z1 = ax + dx * L * (k + 1) / n, az + dz * L * (k + 1) / n; ys = [g(x0, z0), g(x1, z1), g((x0 + x1) / 2, (z0 + z1) / 2)]; y = max(ys); lo = min(ys) - 0.5
        G_.box(body, min(x0, x1) - hx, lo, min(z0, z1) - hz, max(x0, x1) + hx, y + h, max(z0, z1) + hz)
        G_.box(cap, min(x0, x1) - hx - 0.12, y + h, min(z0, z1) - hz - 0.12, max(x0, x1) + hx + 0.12, y + h + 0.16, max(z0, z1) + hz + 0.12, True)
        if col: G_.col(min(x0, x1) - hx, lo, min(z0, z1) - hz, max(x0, x1) + hx, y + h, max(z0, z1) + hz, n='WallCol')


def samun(G_, x, z, along, y, bays=3, bay=3.2, h=3.6, depth=3.4, shut=(), n='Samun'):
    """삼문(맞배, 판문): posts, lintels, plank leaves, tiled gable roof on a low stone base. along = 'x' | 'z' (the direction the gate is wide in). shut = bay indexes with a collider"""
    ax = 1 if along == 'x' else 0; az = 1 - ax; W_ = bays * bay
    def bx(mat, t0, t1, o0, o1, y0, y1, bottom=False): G_.box(mat, x + ax * t0 + az * o0, y0, z + az * t0 + ax * o0, x + ax * t1 + az * o1, y1, z + az * t1 + ax * o1, bottom)
    bx('stone_dressed', -W_ / 2 - 0.5, W_ / 2 + 0.5, -depth / 2 - 0.4, depth / 2 + 0.4, y - 1.2, y + 0.3)
    for k in range(bays + 1):
        t = -W_ / 2 + k * bay
        for o in (-depth / 2 + 0.2, 0, depth / 2 - 0.2): bx('wood_red', t - 0.17, t + 0.17, o - 0.17, o + 0.17, y + 0.3, y + 0.3 + h)
        bx('plaster', t - 0.06, t + 0.06, -depth / 2 + 0.2, depth / 2 - 0.2, y + 0.3 + h * 0.62, y + 0.3 + h)
    for o in (-depth / 2 + 0.2, 0, depth / 2 - 0.2): bx('wood_red', -W_ / 2 - 0.3, W_ / 2 + 0.3, o - 0.13, o + 0.13, y + 0.3 + h - 0.32, y + 0.3 + h, True)
    for k in range(bays):
        t0 = -W_ / 2 + k * bay + 0.2; t1 = t0 + bay - 0.4
        bx('wood_red', t0, t0 + (t1 - t0) * 0.5 - 0.03, -0.06, 0.06, y + 0.42, y + 0.3 + h * 0.8, True); bx('wood_red', t0 + (t1 - t0) * 0.5 + 0.03, t1, -0.06, 0.06, y + 0.42, y + 0.3 + h * 0.8, True)
        bx('wood_dark', t0, t1, -0.05, 0.05, y + 0.3 + h * 0.8, y + 0.3 + h - 0.32, True)
        if k in shut or shut == 'all': G_.col(x + ax * t0 - az * 0.3, y, z + az * t0 - ax * 0.3, x + ax * t1 + az * 0.3, y + h, z + az * t1 + ax * 0.3, n='Shut')
    ye = y + 0.3 + h - 0.05; ov = 1.1
    G_.gable('tile_dark', x - ax * (W_ / 2 + 0.7) - az * (depth / 2 + ov), ye, z - az * (W_ / 2 + 0.7) - ax * (depth / 2 + ov), x + ax * (W_ / 2 + 0.7) + az * (depth / 2 + ov), ye + 1.9, z + az * (W_ / 2 + 0.7) + ax * (depth / 2 + ov), along=along)
    bx('tile_dark', -W_ / 2 - 0.7, W_ / 2 + 0.7, -0.17, 0.17, ye + 1.8, ye + 2.1)
    for sgn in (-1, 1): G_.col(x + ax * sgn * (W_ / 2 + 0.25) - 0.3, y - 1, z + az * sgn * (W_ / 2 + 0.25) - 0.3, x + ax * sgn * (W_ / 2 + 0.25) + 0.3, y + h, z + az * sgn * (W_ / 2 + 0.25) + 0.3, n='Post')
    anchors['gates'].append(dict(id=n, at=[round(x, 1), round(y + 0.3, 2), round(z, 1)]))


def sinro(G_, a, b, y, w=3.9):
    """신로: three lanes of rough 박석, the centre lane a little higher (axis aligned a -> b)"""
    ax, az = a; bx, bz = b; hx, hz = (0, 1) if abs(bx - ax) > abs(bz - az) else (1, 0)
    lo = min(g(ax, az), g(bx, bz), g((ax + bx) / 2, (az + bz) / 2)) - 0.6
    G_.box('fieldstone', min(ax, bx) - hx * w / 2, lo, min(az, bz) - hz * w / 2, max(ax, bx) + hx * w / 2, y + 0.07, max(az, bz) + hz * w / 2)
    G_.box('paving', min(ax, bx) - hx * w / 6, lo, min(az, bz) - hz * w / 6, max(ax, bx) + hx * w / 6, y + 0.13, max(az, bz) + hz * w / 6)


# =====================================================================================================
# C1  종묘
# =====================================================================================================
def c1():
    u = unit('Jongmyo'); FX = 0.71
    Y = 71.5; x0, x1, z0, z1 = 2194.5, 2287.5, 3222.5, 3283.5; cx = (x0 + x1) / 2
    # --- 정전 마당: 담 + 남신문(3칸) · 동문 · 서문, 박석 마당
    Gc = G(u, 'JeongjeonCourt', cx, (z0 + z1) / 2, cull=0.006)
    for a, b_, gaps in (((x0, z0), (x1, z0), [(cx - x0 - 6.2, cx - x0 + 6.2)]), ((x0, z1), (x1, z1), ()), ((x0, z0), (x0, z1), [(11.0, 18.0)]), ((x1, z0), (x1, z1), [(11.0, 18.0)])): wall(Gc, a, b_, h=2.8, th=0.8, gaps=gaps, lvl_fix=Y)
    samun(Gc, cx, z0, 'x', Y, bays=3, bay=3.6, h=3.9, n='Jongmyo_Namsinmun'); samun(Gc, x0, z0 + 14.5, 'z', Y, bays=1, bay=3.4, h=3.2, depth=2.8, n='Jongmyo_Seomun'); samun(Gc, x1, z0 + 14.5, 'z', Y, bays=3, bay=2.2, h=3.2, depth=2.8, n='Jongmyo_Dongmun')
    Gc.top('fieldstone', x0 + 0.4, Y + 0.04, z0 + 0.4, x1 - 0.4, z1 - 0.4)
    # --- 월대 (109 x 69 m x 0.767 = 83.6 x 52.9 m, 높이 1.0 m) + 신로 + 계단
    Gw = G(u, 'JeongjeonWoldae', cx, 3254, cull=0.004); wx0, wx1, wz0, wz1 = cx - 44.0, cx + 44.0, 3227.5, 3280.4; WT = Y + 1.0
    platform(Gw, wx0, wz0, wx1, wz1, Y - 1.0, WT, mat='stone_dressed'); Gw.top('fieldstone', wx0 + 0.3, WT + 0.03, wz0 + 0.3, wx1 - 0.3, wz1 - 0.3)
    for sx, w_ in ((0, 4.2), (-24, 3.0), (24, 3.0)): stairs(Gw, cx + sx, wz0, 180, w_, Y, WT)
    stairs(Gw, wx0, 3250, 270, 3.0, Y, WT); stairs(Gw, wx1, 3250, 90, 3.0, Y, WT)
    Gw.box('paving', cx - 0.75, WT, wz0 + 0.2, cx + 0.75, WT + 0.09, 3262.0); sinro(Gw, (cx, z0 + 2.2), (cx, wz0 - 1.6), Y, w=3.9)
    # --- 정전: 19칸 + 협실 + 동 · 서 월랑. 칸 폭만 터 축척 (4.0 -> 3.07 m), 높이 · 깊이는 실제
    Gh = G(u, 'Jeongjeon', cx, 3264, cull=0.003); hz = wz1 - 0.6 - SIZE['Gen_Jongmyo_Jeongjeon'][2] / 2
    Gh.prefab('Gen_Jongmyo_Jeongjeon', cx, WT, hz, 0, (FX, 1, 1), 'Jongmyo_Jeongjeon'); L_ = 101.0 * FX
    Gh.col(cx - L_ / 2, WT, wz1 - 13.4, cx + L_ / 2, WT + 6.5, wz1 - 1.0, n='Body')
    for sgn in (-1, 1): Gh.col(cx + sgn * (L_ / 2 + 2.0) - 3.2, WT, hz - 15.5, cx + sgn * (L_ / 2 + 2.0) + 3.2, WT + 4.5, wz1 - 12.0, n='Wolrang')
    rep['built']['jeongjeon'] = dict(length=round(L_, 1), bay=round(4.0 * FX, 2), woldae=[round(wx1 - wx0, 1), round(wz1 - wz0, 1), 1.0], court_level=Y)
    # --- 신로 on the terrace south of the court + 계단 from the gate court
    Gs = G(u, 'Sinro', 2215, 3218, cull=0.005); zs = 3218.2; ys = round(max(g(x, zs) for x in range(2200, 2250, 4)), 2); rep['levels']['jongmyo_strip'] = ys
    sinro(Gs, (2193.0, zs), (cx + 2.0, zs), ys); run = stairs(Gs, 2193.0, zs, 270, 4.2, 64.9, ys)   # down to the gate court (west)
    r2 = stairs(Gs, cx, zs + 2.0, 0, 6.0, Y, ys, cheek=True)                                           # down (north) to 남신문
    sinro(Gs, (2193.0 - run - 14.0, zs), (2193.0 - run, zs), 64.9)
    # --- 외대문 (3칸 맞배; 화성행궁 외삼문 경량본) at the west edge of the gate court, facing west
    Gg = G(u, 'Oedaemun', 2176, 3232, cull=0.006); gx = 2193.0 - run - 10.0; gy = lvl(gx - 5, zs - 10, gx + 5, zs + 10, 0.05)
    Gg.prefab('Gate_Oisamun', gx, gy, zs, 90, n='Jongmyo_Oedaemun'); Gg.col(gx - 3.4, gy, zs - 9.4, gx + 3.4, gy + 5, zs - 2.4, n='Side'); Gg.col(gx - 3.4, gy, zs + 2.4, gx + 3.4, gy + 5, zs + 9.4, n='Side')
    footing(Gg, gx, zs, 8.6, 19.2, 0, gy, 'stone_dressed'); anchors['gates'].append(dict(id='Jongmyo_Oedaemun', at=[round(gx, 1), gy, zs]))
    # 재궁 · 향대청 · 전사청: 작은 맞배 채 셋 (화성행궁 내포사 경량본) + 낮은 담
    yy = 64.9; hx = 2182.0
    for k, (hz_, yaw, nm) in enumerate(((3232.0, 270, 'Hyangdaecheong'), (3242.5, 270, 'Jaegung'), (3252.5, 180, 'Jeonsacheong'))):
        y_ = lvl(hx - 4.5, hz_ - 4.5, hx + 4.5, hz_ + 4.5, 0.05); hall(Gg, 'Hall_Naeposa', hx, y_, hz_, yaw, n='Jongmyo_' + nm, ov=0.9)
    wall(Gg, (2174.5, 3226.0), (2174.5, 3258.5), h=2.2, th=0.6); wall(Gg, (2174.5, 3258.5), (2189.5, 3258.5), h=2.2, th=0.6); wall(Gg, (2174.5, 3226.0), (2178.0, 3226.0), h=2.2, th=0.6)
    # --- 영녕전: 16칸 (가운데 태실 4칸이 높다) in its own walled court on the upper terrace
    Ye = 74.8; ex0, ex1, ez0, ez1 = 2199.0, 2257.0, 3185.5, 3213.5; ecx = (ex0 + ex1) / 2
    Ge = G(u, 'YeongnyeongCourt', ecx, 3200, cull=0.006)
    wall(Ge, (ex0, ez1), (ex1, ez1), h=2.6, th=0.7, gaps=[(ecx - ex0 - 4.4, ecx - ex0 + 4.4)], lvl_fix=Ye); wall(Ge, (ex0, ez0), (ex1, ez0), h=2.6, th=0.7); wall(Ge, (ex0, ez0), (ex0, ez1), h=2.6, th=0.7, lvl_fix=Ye); wall(Ge, (ex1, ez0), (ex1, ez1), h=2.6, th=0.7)
    samun(Ge, ecx, ez1, 'x', Ye, bays=3, bay=2.6, h=3.3, depth=3.0, n='Yeongnyeong_Sinmun'); stairs(Ge, ecx, ez1 + 1.9, 0, 5.0, ys, Ye)
    Ge.top('fieldstone', ex0 + 0.4, Ye + 0.04, ez0 + 0.4, ex1 - 0.4, ez1 - 0.4)
    ET = Ye + 0.7; platform(Ge, ecx - 26.5, ez0 + 3.0, ecx + 26.5, ez1 - 6.0, Ye - 0.6, ET); stairs(Ge, ecx, ez1 - 6.0, 0, 3.6, Ye, ET)
    # the hall faces north here (toward the 신로 terrace): the court is entered from the north. One long 맞배 hall, the four middle bays (태실) higher.
    Gy = G(u, 'Yeongnyeongjeon', ecx, 3196, cull=0.004); bw = 3.2; zb = ez0 + 4.4; zf = zb + 8.6; hc = 3.5
    for a_, b__, up in ((0, 6, 0.0), (6, 10, 0.85), (10, 16, 0.0)):
        xa, xb = ecx + (a_ - 8) * bw, ecx + (b__ - 8) * bw; yb_ = ET + 0.45; yt_ = yb_ + hc + up
        Gy.box('stone_dressed', xa - (0.7 if a_ == 0 else 0), ET - 0.1, zb - 0.7, xb + (0.7 if b__ == 16 else 0), yb_, zf + 0.7)
        Gy.box('wood_red', xa, yb_, zb, xb, yt_, zb + 0.28); Gy.box('wood_red', xa, yb_, zf - 2.5, xb, yt_, zf - 2.26)
        for k in range(a_, b__ + 1):
            x = ecx + (k - 8) * bw; Gy.box('wood_red', x - 0.17, yb_, zf - 0.34, x + 0.17, yt_, zf); Gy.box('wood_dark', x - 0.05, yb_ + 0.1, zf - 2.26, x + 0.05, yt_ - 0.4, zf - 2.2, True)
        for xe in ((xa,) if a_ == 0 or up else ()) + ((xb,) if b__ == 16 or up else ()): Gy.box('wood_red', xe - 0.14, yb_, zb, xe + 0.14, yt_ + (1.2 if not up else 0.0), zf - 2.26)
        Gy.box('wood_red', xa - 0.2, yt_ - 0.34, zf - 0.3, xb + 0.2, yt_, zf - 0.04, True)
        ov = 1.25; Gy.gable('tile_dark', xa - (ov if a_ == 0 or up else 0.0), yt_, zb - ov, xb + (ov if b__ == 16 or up else 0.0), yt_ + 2.5, zf + ov, along='x'); Gy.box('tile_dark', xa - 0.3, yt_ + 2.42, (zb + zf) / 2 - 0.17, xb + 0.3, yt_ + 2.74, (zb + zf) / 2 + 0.17)
        Gy.box('plaster', xa, yt_ - 0.05, zb - ov + 0.25, xb, yt_, zf + ov - 0.25, True)
    Gy.col(ecx - 8 * bw, ET, zb, ecx + 8 * bw, ET + 5.5, zf - 2.2, n='Body')
    rep['built']['yeongnyeongjeon'] = dict(bays=16, bay=bw, length=16 * bw)
    for q in ((x0, z0, x1, z1), (wx0, wz0, wx1, wz1), (ex0, ez0, ex1, ez1), (2174, 3206, 2192, 3260), (2190, 3214, 2246, 3222)): occ_rect(*q, 2.0)
    anchors['plazas'].append(dict(id='jongmyo_woldae', at=[cx, WT, 3240.0], note='empty on purpose - no walkers on the 월대'))


# =====================================================================================================
# C2  사직단
# =====================================================================================================
def c2():
    u = unit('Sajik'); Y = 61.2; cx, cz = 1694.0, 3252.0
    Go = G(u, 'SajikOuter', cx, cz, cull=0.005); R_ = 29.0; gm, gs = 5.4, 2.2
    wall(Go, (cx - R_, cz + R_), (cx + R_, cz + R_), h=2.0, th=0.6, gaps=[(R_ - gm, R_ + gm)], lvl_fix=Y); wall(Go, (cx - R_, cz - R_), (cx + R_, cz - R_), h=2.0, th=0.6, gaps=[(R_ - gs, R_ + gs)], lvl_fix=Y)
    wall(Go, (cx - R_, cz - R_), (cx - R_, cz + R_), h=2.0, th=0.6, gaps=[(R_ - gs, R_ + gs)], lvl_fix=Y); wall(Go, (cx + R_, cz - R_), (cx + R_, cz + R_), h=2.0, th=0.6, gaps=[(R_ - gs, R_ + gs)], lvl_fix=Y)
    samun(Go, cx, cz + R_, 'x', Y, bays=3, bay=3.1, h=3.4, n='Sajik_Buksinmun'); samun(Go, cx, cz - R_, 'x', Y, bays=1, bay=3.4, h=3.0, depth=2.8, n='Sajik_Namsinmun')
    samun(Go, cx - R_, cz, 'z', Y, bays=1, bay=3.4, h=3.0, depth=2.8, n='Sajik_Seosinmun'); samun(Go, cx + R_, cz, 'z', Y, bays=1, bay=3.4, h=3.0, depth=2.8, n='Sajik_Dongsinmun')
    hall(Go, 'Hall_Naeposa', cx - 21.0, Y, cz - 19.0, 90, n='Sajik_Sinsil', ov=0.9)
    # 유(壝): 낮은 담 + 홍살문 넷, 그 안에 사단(동) · 직단(서)
    Gi = G(u, 'SajikAltars', cx, cz, cull=0.004); ix, iz = 19.0, 11.0     # the gen module brings the low 유 wall; 홍살문 stand in its four openings
    for (px, pz, yaw) in ((cx, cz + iz, 0), (cx, cz - iz, 0), (cx - ix, cz, 90), (cx + ix, cz, 90)): Gi.prefab('Gate_Hongsalmun', px, Y, pz, yaw, (0.6, 0.6, 0.6), n='Sajik_Hongsalmun')
    Gi.prefab('Gen_Sajikdan', cx, Y, cz, 0, n='Sajikdan')
    for sgn in (-1, 1):   # the gen altars are plain: add 3-step solid altars (7.65 m square, 1.0 m high) + 4 stairs + 석주
        ax_ = cx + sgn * 8.2
        for k in range(3): Gi.box('stone_dressed', ax_ - 3.83 + k * 0.0, Y - 0.4, cz - 3.83, ax_ + 3.83, Y + 1.0, cz + 3.83) if k == 0 else None
        Gi.top('h308_earth', ax_ - 3.5, Y + 1.02, cz - 3.5, ax_ + 3.5, cz + 3.5)
        for yaw, (ex, ez) in ((0, (0, 3.83)), (180, (0, -3.83)), (90, (3.83, 0)), (270, (-3.83, 0))): stairs(Gi, ax_ + ex, cz + ez, yaw, 2.2, Y, Y + 1.0, tread=0.34, cheek=False)
        Gi.col(ax_ - 3.83, Y, cz - 3.83, ax_ + 3.83, Y + 1.0, cz + 3.83, n='Altar')
    Gi.box('stone_plain', cx + 8.2 - 0.25, Y + 1.0, cz - 3.0 - 0.25, cx + 8.2 + 0.25, Y + 1.76, cz - 3.0 + 0.25)   # 석주 (사단의 남쪽 계단 위)
    occ_rect(cx - R_, cz - R_, cx + R_, cz + R_, 3.0); rep['built']['sajik'] = dict(altar=7.65, h=1.0, yu=[2 * ix, 2 * iz], outer=2 * R_)
    anchors['gates'].append(dict(id='sajik_south_approach', at=[cx, Y, cz - R_ - 4]))


# =====================================================================================================
# C3  종각 (종루) + 마당
# =====================================================================================================
def c3():
    u = unit('Jonggak'); cx, cz = 1942.0, 2966.0; Y = lvl(cx - 9, cz - 7, cx + 9, cz + 7, 0.05)
    Gj = G(u, 'Jongru', cx, cz, cull=0.004); sc = 0.62
    platform(Gj, cx - 9.0, cz - 7.0, cx + 9.0, cz + 7.0, Y - 2.5, Y + 0.3); Gj.prefab('Lu_MangGyeongru', cx, Y + 0.3, cz, 90, (sc, sc, sc), n='Jongru')   # faces east, toward the junction
    Gj.col(cx - 3.6, Y, cz - 5.6, cx + 3.6, Y + 3.0, cz - 1.0, n='Body')
    # bell under its own four-post shelter in front (east) of the 누각 - the bell stays visible from the street
    bx_, bz_ = cx + 13.0, cz; yb = lvl(bx_ - 2.5, bz_ - 2.5, bx_ + 2.5, bz_ + 2.5, 0.05); platform(Gj, bx_ - 2.6, bz_ - 2.6, bx_ + 2.6, bz_ + 2.6, yb - 2.5, yb + 0.35)
    for sx in (-1.9, 1.9):
        for sz in (-1.9, 1.9): Gj.box('wood_red', bx_ + sx - 0.16, yb + 0.35, bz_ + sz - 0.16, bx_ + sx + 0.16, yb + 3.75, bz_ + sz + 0.16)
    Gj.box('wood_red', bx_ - 2.1, yb + 3.5, bz_ - 0.14, bx_ + 2.1, yb + 3.78, bz_ + 0.14, True); Gj.box('wood_red', bx_ - 0.14, yb + 3.5, bz_ - 2.1, bx_ + 0.14, yb + 3.78, bz_ + 2.1, True)
    for sz in (-1.9, 1.9): Gj.box('wood_red', bx_ - 2.1, yb + 3.5, bz_ + sz - 0.12, bx_ + 2.1, yb + 3.75, bz_ + sz + 0.12, True)
    for sx in (-1.9, 1.9): Gj.box('wood_red', bx_ + sx - 0.12, yb + 3.5, bz_ - 2.1, bx_ + sx + 0.12, yb + 3.75, bz_ + 2.1, True)
    Gj.pyr('tile_dark', bx_ - 3.3, yb + 3.75, bz_ - 3.3, bx_ + 3.3, yb + 5.6, bz_ + 3.3); Gj.prefab('Prop_Bell', bx_, yb + 1.05, bz_, 0, n='Jong'); Gj.col(bx_ - 0.8, yb, bz_ - 0.8, bx_ + 0.8, yb + 3.4, bz_ + 0.8, n='Bell')
    # steps from the knoll down to the junction side (east)
    x_e = bx_ + 2.6; y_lo = g(x_e + 4.0, bz_)
    for _ in range(3): run = max(1, round((yb + 0.35 - y_lo) / 0.19)) * 0.36; y_lo = g(x_e + run + 0.5, bz_)
    if yb + 0.35 - y_lo > 0.25: stairs(Gj, x_e, bz_, 90, 4.4, y_lo, yb + 0.35)
    if yb + 0.35 - (Y + 0.3) < -0.05 or True: Gj.box('stone_dressed', cx + 9.0, min(Y, yb) - 2.5, cz - 2.2, bx_ - 2.6, min(Y + 0.3, yb + 0.35), cz + 2.2); Gj.col(cx + 9.0, min(Y, yb) - 2.5, cz - 2.2, bx_ - 2.6, min(Y + 0.3, yb + 0.35), cz + 2.2, n='Link')
    occ_rect(cx - 10, cz - 8, bx_ + 4 + 6, cz + 8, 1.5); rep['levels']['jonggak'] = [Y, yb]
    anchors['plazas'].append(dict(id='jonggak', at=[round(bx_ + 8, 1), round(g(bx_ + 8, bz_), 2), bz_], r=8))


exec(open('Tools/Art/hanyang308_town_b.py', encoding='utf-8').read()) if (B.ROOT / 'Tools/Art/hanyang308_town_b.py').exists() else None
STEPS = [f for f in (c1, c2, c3, globals().get('c4'), globals().get('c5'), globals().get('c6')) if f]
for f in STEPS: f()
out_units = [dict(name=u['name'], groups=[gr.done() for gr in u['groups']]) for u in units if u['groups']]
prefabs = sorted({it['a'][7:] for u in out_units for gr in u['groups'] for it in gr['items'] if it['a'].startswith('prefab:')})
plan = dict(version='308.town.p3', prefabs=GEN, lite=LITE3, lite1=sorted(p[:-3] for p in prefabs if p.endswith('_L1')), tints=TINTS, solids=solids, blocks=[b.out() for b in blocks], units=out_units, ribbons=[], bridges=[], rays=[], joins=[])
(ST / 'town308.json').write_text(json.dumps(plan, ensure_ascii=False), encoding='utf-8')
tri = {0: 10, 1: 6, 2: 6, 3: 2, 4: 4, 5: 6, 7: 12}; nf = {0: 6, 1: 6, 2: 6, 3: 6, 4: 6, 5: 18, 6: 6, 7: 6}; total = 0
for s in solids:
    f = s['f']; i = 0
    while i < len(f):
        k = int(f[i]); total += tri.get(k, int(f[i + 6]) * 4 if k == 6 else 0); i += 1 + nf[k]
ptri = 0
for u in out_units:
    for gr in u['groups']:
        for it in gr['items']:
            a = it['a'][7:] if it['a'].startswith('prefab:') else ''
            if a and not a.startswith('TB_'): ptri += TRI[a.replace('_L1', '')][1 if a.endswith('_L1') else 0]
btri = sum(b.tris() for b in blocks)
rep['built'].update(units={u['name']: dict(groups=len(u['groups']), items=sum(len(gr['items']) for gr in u['groups']), cols=sum(1 for gr in u['groups'] for it in gr['items'] if it['a'] == 'col'),
                                             renderers_est=sum(sum(1 for it in gr['items'] if it['a'].startswith('mesh:')) + 2 * sum(1 for it in gr['items'] if it['a'].startswith('prefab:TB_')) + 3 * sum(1 for it in gr['items'] if it['a'].startswith('prefab:') and not it['a'].startswith('prefab:TB_')) for gr in u['groups'])) for u in out_units},
                    solids=len(solids), solid_tris=total, prefab_top_tris=ptri, block_top_tris=btri, top_tris=total + ptri + btri, blocks=len(blocks), block_houses=sum(len(b.h) for b in blocks), counts=counts, prefabs=prefabs)
(ST / 'Out/town_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8'); (ST / 'Out/town_anchors.json').write_text(json.dumps(anchors, ensure_ascii=False, indent=1), encoding='utf-8')
print(json.dumps({k: v for k, v in rep['built'].items() if k != 'prefabs'}, ensure_ascii=False)[:2600]); print('levels', rep['levels'], 'warn', rep['warn'][:12])

if '--diag' in sys.argv:
    for z in range(3880, 2560, -20):
        row = ''
        for x in range(1440, 2310, 10):
            j, i = x - OX, z - OZ
            if OCC0[i, j]: row += '#'; continue
            sl = math.hypot(g(x + 4, z) - g(x - 4, z), g(x, z + 4) - g(x, z - 4)) / 8.0
            row += 'h' if OCC[i, j] else ('^' if sl > 0.22 else ':' if sl > 0.12 else '.')
        print('%4d %s' % (z, row))
