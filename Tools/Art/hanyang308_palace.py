"""SPEC-WORLD-HANYANG-308 phase 2 (§12): 경복궁 · 해태 · 육조거리 관아 as placements + merged masonry solids.
python Tools/Art/hanyang308_palace.py  ->  Tools/Unity/Stage308_hanyang/palace308.json (+ Out/palace_report.json)
Consumed by `Hanyang308 palace:import | palace:lite1 | palace:meshes | palace:apply[:<unit>]`.
World frame: x east, z north, yaw degrees clockwise from +z. Pack / gen buildings face -Z (south) at yaw 0.
Solids: per group and material one mesh, prims relative to the group origin:
  0 box  1 gable along X  2 gable along Z  3 top quad  4 pyramid  5 wedge (6 points)  6 cylinder  7 box with bottom"""
import json, math, sys
sys.path.insert(0, 'Tools/Art')
import cliff308_base as B

ST = B.ROOT / 'Tools/Unity/Stage308_hanyang'
J = json.loads((ST / 'hanyang308.json').read_text(encoding='utf-8'))
H = B.load_height(B.PL / 'Stage/height_pn8.bytes')
g = lambda x, z: float(B.sample(H, x, z))
SITE = {s['id']: s for s in J['sites']}
AX = 1959.0
ONLY_GEN = ['Gen_GateBase_3Arch', 'Gen_GateBase_Small', 'Gen_Munru_1F_3x2', 'Gen_Hall_2Tier_5x5', 'Gen_Gyeonghoeru']
LITE = ['Gate_Sinpungru', 'Gate_Naesanmun', 'Hall_Bongsudang', 'Hall_Yuyeotaek', 'Hall_Boknaedang', 'Corr_Jibsacheong', 'Hall_Yeonhuigak', 'Hall_Honhwagak', 'Hall_Wooryeondang', 'Hall_Yeonjuhyeopdang',
        'Gate_Soseul_Changwon', 'Pav_Octagonal']
SIZE = {e['name']: e['bounds_unity']['size'] for e in json.load(open(B.ROOT / 'Art/World/PackLite308/manifest.json', encoding='utf-8'))['items']}
HERO = True  # phase 5a: hero models (Art/World/PackLite308/hero) replace the stand-ins
composites = []
units = []; solids = []; rep = dict(warn=[], built={}, levels={})


def item(a, x, y, z, yaw=0.0, s=(1, 1, 1), col='', n='', pitch=0.0):
    return dict(n=n, a=a, col=col, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 3), s=[round(float(v), 4) for v in s], pitch=round(pitch, 3))


class G:
    """one group = one distance-cull set; masonry merged per material"""
    def __init__(self, unit, name, ox, oz, cull=0.012):
        self.name = name; self.o = (ox, round(g(ox, oz), 2), oz); self.m = {}; self.items = []; self.cull = cull; self.mute = False; unit['groups'].append(self)
    def _p(self, mat, kind, vals):
        if self.mute: return
        o = self.o; out = [float(kind)]
        for i, v in enumerate(vals): out.append(round(v - o[i % 3], 3))
        self.m.setdefault(mat, []).extend(out)
    def box(self, mat, x0, y0, z0, x1, y1, z1, bottom=False): self._p(mat, 7 if bottom else 0, (min(x0, x1), y0, min(z0, z1), max(x0, x1), y1, max(z0, z1)))
    def gable(self, mat, x0, y0, z0, x1, y1, z1, along='x'): self._p(mat, 1 if along == 'x' else 2, (min(x0, x1), y0, min(z0, z1), max(x0, x1), y1, max(z0, z1)))
    def top(self, mat, x0, y, z0, x1, z1): self._p(mat, 3, (min(x0, x1), y, min(z0, z1), max(x0, x1), y, max(z0, z1)))
    def pyr(self, mat, x0, y0, z0, x1, y1, z1): self._p(mat, 4, (x0, y0, z0, x1, y1, z1))
    def wedge(self, mat, pts): self._p(mat, 5, [c for p in pts for c in p])
    def cyl(self, mat, x, y0, z, r, y1, n=12): self.m.setdefault(mat, []).extend([6.0, round(x - self.o[0], 3), round(y0 - self.o[1], 3), round(z - self.o[2], 3), r, round(y1 - self.o[1], 3), float(n)])
    def col(self, x0, y0, z0, x1, y1, z1, n='Col'): self.items.append(item('col', (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, 0, (abs(x1 - x0), y1 - y0, abs(z1 - z0)), n=n))
    def ramp(self, cx, cz, yaw, w, run, y_lo, y_hi, n='Ramp'):
        """walkable slope; yaw = direction of descent"""
        L = math.hypot(run, y_hi - y_lo); pitch = math.degrees(math.atan2(y_hi - y_lo, run)); self.items.append(item('col', cx, (y_lo + y_hi) / 2 - 0.1, cz, yaw, (w, 0.2, L), n=n, pitch=pitch))
    def prefab(self, name, x, y, z, yaw=0.0, s=(1, 1, 1), n=''): self.items.append(item('prefab:' + name, x, y, z, yaw, s, n=n or name))
    def done(self):
        its = []
        for mat, f in self.m.items():
            sid = 'P_%s_%s' % (self.name, mat); solids.append(dict(id=sid, mat=mat, o=list(self.o), f=f)); its.append(item('mesh:' + sid, *self.o, col=mat, n=sid))
        return dict(name=self.name, cull=self.cull, items=its + self.items)


def unit(name):
    u = dict(name='Hanyang308_' + name, groups=[]); units.append(u); return u


def lvl(x0, z0, x1, z1, q=0.05):
    """highest ground under a footprint"""
    n = 5; return math.ceil(max(g(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * j / n) for i in range(n + 1) for j in range(n + 1)) / q) * q


def body_col(G_, name, x, y, z, yaw=0.0, s=(1, 1, 1), ov=1.7, h=4.6):
    sx, sy, sz = SIZE[name.replace('_L1', '')]; G_.items.append(item('col', x, y + h / 2, z, yaw, (max(2, sx * s[0] - 2 * ov), h, max(2, sz * s[2] - 2 * ov)), n='Body'))


def hall(G_, name, x, y, z, yaw=0.0, s=(1, 1, 1), n='', col=True, ov=1.7):
    G_.prefab(name, x, y, z, yaw, s, n)
    if col: body_col(G_, name, x, y, z, yaw, s, ov)


# ---------- masonry pieces ----------
def wall(G_, a, b, h=2.6, th=0.7, gaps=(), step=8.0, body='stone_plain', lvl_fix=None, col=True, skirt=1.6):
    """담장 along an axis-aligned line a->b: stone footing, body, tile cap; steps with the ground. gaps = [(t0, t1)] in metres from a"""
    ax, az = a; bx, bz = b; L = math.hypot(bx - ax, bz - az); dx, dz = (bx - ax) / L, (bz - az) / L; n = max(1, round(L / step)); st = L / n; runs = []
    for k in range(n):
        t0, t1 = k * st, (k + 1) * st; pieces = [(t0, t1)]
        for g0, g1 in gaps:
            nxt = []
            for p0, p1 in pieces:
                if g1 <= p0 or g0 >= p1: nxt.append((p0, p1)); continue
                if g0 > p0: nxt.append((p0, g0))
                if g1 < p1: nxt.append((g1, p1))
            pieces = nxt
        for p0, p1 in pieces:
            if p1 - p0 < 0.2: continue
            xs = [(ax + dx * t, az + dz * t) for t in (p0, (p0 + p1) / 2, p1)]; y = lvl_fix if lvl_fix is not None else math.ceil(max(g(*q) for q in xs) / 0.25) * 0.25
            lo = min(g(*q) for q in xs) - skirt
            x0, z0 = ax + dx * p0, az + dz * p0; x1, z1 = ax + dx * p1, az + dz * p1; hx, hz = abs(dz) * th / 2, abs(dx) * th / 2
            G_.box('stone_dressed', x0 - hx * 1.25, lo, z0 - hz * 1.25, x1 + hx * 1.25, y + 0.55, z1 + hz * 1.25)
            G_.box(body, x0 - hx, y + 0.55, z0 - hz, x1 + hx, y + h, z1 + hz)
            cw = th / 2 + 0.28
            G_.gable('tile_dark', x0 - abs(dz) * cw, y + h, z0 - abs(dx) * cw, x1 + abs(dz) * cw, y + h + 0.42, z1 + abs(dx) * cw, along='x' if abs(dx) > 0.5 else 'z')
            if runs and abs(runs[-1][4] - y) < 0.01 and abs(runs[-1][1] - p0) < 0.01: runs[-1][1] = p1; runs[-1][3] = min(runs[-1][3], lo)
            else: runs.append([p0, p1, 0, lo, y])
    if col:
        for p0, p1, _, lo, y in runs:
            x0, z0 = ax + dx * p0, az + dz * p0; x1, z1 = ax + dx * p1, az + dz * p1; hx, hz = abs(dz) * th / 2, abs(dx) * th / 2
            G_.col(min(x0, x1) - hx, lo, min(z0, z1) - hz, max(x0, x1) + hx, y + h + 0.4, max(z0, z1) + hz, n='WallCol')


def rail(G_, a, b, y, h=0.95, sp=2.3):
    ax, az = a; bx, bz = b; L = math.hypot(bx - ax, bz - az)
    if L < 0.5: return
    dx, dz = (bx - ax) / L, (bz - az) / L; n = max(1, round(L / sp))
    for k in range(n + 1):
        x, z = ax + dx * L * k / n, az + dz * L * k / n; G_.box('stone_dressed', x - 0.13, y, z - 0.13, x + 0.13, y + h + 0.18, z + 0.13)
    hx, hz = abs(dz) * 0.08 + abs(dx) * 0, abs(dx) * 0.08
    G_.box('stone_dressed', min(ax, bx) - hx, y + h - 0.2, min(az, bz) - hz, max(ax, bx) + hx, y + h, max(az, bz) + hz, bottom=True)
    G_.box('stone_dressed', min(ax, bx) - hx * 0.7, y + 0.12, min(az, bz) - hz * 0.7, max(ax, bx) + hx * 0.7, y + 0.5, max(az, bz) + hz * 0.7, bottom=True)


def stairs(G_, cx, cz, yaw, w, y_lo, y_hi, tread=0.36, mat='stone_dressed', cheek=True):
    """steps descending toward yaw from the edge point (cx, cz) of the upper level"""
    n = max(1, round((y_hi - y_lo) / 0.19)); r = (y_hi - y_lo) / n; fx, fz = math.sin(math.radians(yaw)), math.cos(math.radians(yaw)); sx, sz = abs(fz), abs(fx)
    for k in range(n):
        d0, d1 = k * tread, (k + 1) * tread; top = y_hi - (k + 1) * r
        xa, za = cx + fx * d0, cz + fz * d0; xb, zb = cx + fx * d1, cz + fz * d1
        G_.box(mat, min(xa, xb) - sx * w / 2, y_lo - 0.6, min(za, zb) - sz * w / 2, max(xa, xb) + sx * w / 2, top, max(za, zb) + sz * w / 2)
    run = n * tread
    if cheek:
        for sgn in (-1, 1):
            ox, oz = sx * sgn * (w / 2 + 0.2), sz * sgn * (w / 2 + 0.2); xa, za = cx + ox, cz + oz; xb, zb = cx + ox + fx * (run + 0.2), cz + oz + fz * (run + 0.2)
            G_.box(mat, min(xa, xb) - sx * 0.2, y_lo - 0.6, min(za, zb) - sz * 0.2, max(xa, xb) + sx * 0.2, y_lo + 0.3, max(za, zb) + sz * 0.2)
    G_.ramp(cx + fx * run / 2, cz + fz * run / 2, yaw, w, run, y_lo, y_hi)
    return run


def platform(G_, x0, z0, x1, z1, y0, y1, mat='stone_dressed', coping=True, col=True):
    G_.box(mat, x0, y0, z0, x1, y1 - (0.16 if coping else 0), z1)
    if coping: G_.box(mat, x0 - 0.1, y1 - 0.16, z0 - 0.1, x1 + 0.1, y1, z1 + 0.1, bottom=True)
    if col: G_.col(x0, y0, z0, x1, y1, z1, n='PlatCol')


D_COR = 5.4; OV = 0.9; COL_H = 3.0; PL = 0.4


def corridor(G_, a, b, inner, y, mitre=(False, False), col=True):
    """행각 from a to b (centre line, axis aligned): 기단, outer wall shut, inner side open on posts, 맞배 roof (mitred ends join the next run). inner = (nx, nz) unit vector to the court"""
    ax, az = a; bx, bz = b; L = math.hypot(bx - ax, bz - az); dx, dz = (bx - ax) / L, (bz - az) / L; nx, nz = inner; hw = D_COR / 2
    ea, eb = (hw if mitre[0] else 0), (hw if mitre[1] else 0)
    def P(t, o, yy): return (ax + dx * t + nx * o, yy, az + dz * t + nz * o)
    def bx_(mat, t0, t1, o0, o1, y0, y1, bottom=False):
        p = P(t0, o0, y0); q = P(t1, o1, y1); G_.box(mat, p[0], y0, p[2], q[0], y1, q[2], bottom)
    lo = min(g(ax, az), g(bx, bz), g((ax + bx) / 2, (az + bz) / 2)) - 1.5
    bx_('stone_dressed', -ea, L + eb, -hw - 0.25, hw + 0.25, lo, y + PL)
    bx_('plaster', -ea, L + eb, -hw + 0.02, -hw + 0.3, y + PL + 0.7, y + PL + COL_H)
    bx_('stone_plain', -ea, L + eb, -hw - 0.02, -hw + 0.34, y + PL, y + PL + 0.7)
    n = max(1, round(L / 3.0))
    for k in range(n + 1):
        t = L * k / n
        for o in (hw - 0.3, -hw + 0.15):
            bx_('wood_red', t - 0.14, t + 0.14, o - 0.14, o + 0.14, y + PL, y + PL + COL_H)
    bx_('wood_red', -ea, L + eb, hw - 0.42, hw - 0.18, y + PL + COL_H - 0.3, y + PL + COL_H, True)
    bx_('wood_red', -ea, L + eb, -hw + 0.03, -hw + 0.27, y + PL + COL_H - 0.3, y + PL + COL_H + 0.02, True)
    ye = y + PL + COL_H - 0.12; yr = ye + 1.75; ew = hw + OV
    ta_o, ta_r, ta_i = (-ew if mitre[0] else 0), 0, (ew if mitre[0] else 0)
    tb_o, tb_r, tb_i = (L + ew if mitre[1] else L), L, (L - ew if mitre[1] else L)
    G_.wedge('tile_dark', [P(ta_o, -ew, ye), P(ta_r, 0, yr), P(ta_i, ew, ye), P(tb_o, -ew, ye), P(tb_r, 0, yr), P(tb_i, ew, ye)])
    bx_('tile_dark', ta_r, tb_r, -0.16, 0.16, yr - 0.1, yr + 0.22)
    bx_('plaster', ta_i if mitre[0] else 0, tb_i if mitre[1] else L, -ew + 0.25, ew - 0.25, ye - 0.06, ye, True)
    if col:
        p = P(-ea, -hw - 0.05, 0); q = P(L + eb, -hw + 0.35, 0); G_.col(min(p[0], q[0]), lo, min(p[2], q[2]), max(p[0], q[0]), y + PL + COL_H + 1.2, max(p[2], q[2]), n='CorrWall')
        p = P(-ea, -hw - 0.25, 0); q = P(L + eb, hw + 0.25, 0); G_.col(min(p[0], q[0]), lo, min(p[2], q[2]), max(p[0], q[0]), y + PL, max(p[2], q[2]), n='CorrFloor')


def ilgak(G_, x, z, along, y, w=2.6):
    """일각문: two posts, lintel, small 맞배 roof; open"""
    ax = 1 if along == 'x' else 0; az = 1 - ax; h = 2.9
    for sgn in (-1, 1):
        cx, cz = x + ax * sgn * w / 2, z + az * sgn * w / 2; G_.box('wood_red', cx - 0.16, y - 0.4, cz - 0.16, cx + 0.16, y + h, cz + 0.16)
    G_.box('wood_red', x - ax * (w / 2 + 0.3) - az * 0.14, y + h - 0.3, z - az * (w / 2 + 0.3) - ax * 0.14, x + ax * (w / 2 + 0.3) + az * 0.14, y + h, z + az * (w / 2 + 0.3) + ax * 0.14, True)
    G_.gable('tile_dark', x - ax * (w / 2 + 0.8) - az * 1.0, y + h, z - az * (w / 2 + 0.8) - ax * 1.0, x + ax * (w / 2 + 0.8) + az * 1.0, y + h + 0.8, z + az * (w / 2 + 0.8) + ax * 1.0, along=along)
    G_.box('stone_dressed', x - ax * (w / 2) - az * 0.5, y - 0.6, z - az * (w / 2) - ax * 0.5, x + ax * (w / 2) + az * 0.5, y + 0.06, z + az * (w / 2) + ax * 0.5)


def eodo(G_, z0, z1, y, w=5.0):
    """어도 (삼도): raised stone strip, the centre lane higher"""
    G_.box('stone_dressed', AX - w / 2, y - 0.5, z0, AX + w / 2, y + 0.09, z1); G_.box('paving', AX - w / 6, y - 0.5, z0, AX + w / 6, y + 0.14, z1)


def gate_small(G_, x, z, yaw, y, shut=True, n=''):
    """single-arch wall gate: gen base 16 x 7.5 x 4.8 (arch 3.6 x 4.2) + 3 x 2 단층 우진각 문루; -Z of the module = outside"""
    G_.prefab('Gen_GateBase_Small', x, y, z, yaw, n=n + '_Base'); G_.prefab('Gen_Munru_1F_3x2', x, y + 4.8, z, yaw, n=n + '_Munru')
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    def W(lx, lz): return (x + lx * c + lz * s_, z - lx * s_ + lz * c)
    for sgn in (-1, 1):
        px, pz = W(sgn * 4.9, 0); G_.items.append(item('col', px, y + 3.0, pz, yaw, (6.2, 6.0, 7.5), n='GateCol'))
    px, pz = W(0, 0); G_.items.append(item('col', px, y + 5.4, pz, yaw, (3.6, 1.2, 7.5), n='GateCol'))
    if shut:
        for sgn in (-1, 1):
            px, pz = W(-1.8 if sgn < 0 else 0, -1.2); G_.items.append(item('mesh:Leaf', px, y, pz, yaw, (1.8, 4.2, 1.0), n='Leaf'))
        px, pz = W(0, -1.2); G_.items.append(item('col', px, y + 2.1, pz, yaw, (3.6, 4.2, 0.5), n='Shut'))


# =====================================================================================================
# B1  광화문 · 궁장 · 십자각 · 월대 · 해태
# =====================================================================================================
PW = J['palace_wall']; X0, X1, Z0, Z1 = PW['x0'], PW['x1'], PW['z0'], PW['z1']
L_OUT, L_MID, L_JO, L_SA, L_GA, L_GY = 60.0, 60.5, 61.0, 61.6, 62.2, 62.8
GH_Y = 59.8; GH_Z = Z0; BASE_H = 7.0


def b1():
    u = unit('PalaceFront')
    G1 = G(u, 'Gwanghwamun', AX, GH_Z, cull=0.006)
    G1.prefab('Hero_Gwanghwamun', AX, GH_Y, GH_Z, 0, n='Gwanghwamun')  # 석축 + 홍예 셋 + 여장 + 3칸 중층 우진각 문루, 한 덩이
    # piers between / beside the three 홍예 (centres -9.5 / 0 / +9.5, widths 4.5 / 5.5 / 4.5) + the deck over them
    for x0, x1 in ((-20, -11.75), (-7.25, -2.75), (2.75, 7.25), (11.75, 20)): G1.col(AX + x0, GH_Y - 1, GH_Z - 6.5, AX + x1, GH_Y + BASE_H, GH_Z + 6.5, n='Pier')
    G1.col(AX - 20, GH_Y + 5.5, GH_Z - 6.5, AX + 20, GH_Y + BASE_H, GH_Z + 6.5, n='Deck')
    G1.col(AX - 11.85, GH_Y + BASE_H, GH_Z - 3.1, AX + 11.85, GH_Y + BASE_H + 5, GH_Z + 3.1, n='Body')
    # gen 석축 LOD0 은 홍예 사이 벽면이 뚫려 보인다 -> 홍예 사이 두 기둥 몸을 돌 덩이로 채운다
    for x0, x1 in ((-20.06, -11.75), (-7.25, -2.75), (2.75, 7.25), (11.75, 20.06)): G1.box('stone_dressed', AX + x0, GH_Y - 1.2, GH_Z - 6.8, AX + x1, GH_Y + 0.02, GH_Z + 6.8)  # footing under the hero base
    # paving through the arches + apron
    G1.top('paving', AX - 20, GH_Y + 0.05, GH_Z - 6.6, AX + 20, GH_Z + 6.6)
    # 월대 (실제 48.7 x 29.7 m -> 터 37 x 23 m), 어도, 난간, 남쪽 계단
    Gw = G(u, 'Woldae', AX, 3297.0)
    wz0, wz1 = 3278.6, GH_Z - 6.5; ww = 11.5; top = GH_Y + 0.7
    platform(Gw, AX - ww, wz0, AX + ww, wz1, GH_Y - 1.0, top)
    Gw.box('stone_dressed', AX - 2.7, top, wz0, AX + 2.7, top + 0.07, wz1); Gw.box('paving', AX - 0.95, top, wz0, AX + 0.95, top + 0.11, wz1)
    for sgn in (-1, 1): rail(Gw, (AX + sgn * (ww - 0.2), wz0 + 0.2), (AX + sgn * (ww - 0.2), wz1), top)
    stairs(Gw, AX, wz0, 180, 2 * ww - 1.0, GH_Y, top)
    for sgn in (-1, 1): Gw.col(AX + sgn * (ww - 0.2) - 0.15, top, wz0, AX + sgn * (ww - 0.2) + 0.15, top + 1.1, wz1, n='RailCol')
    # 해태 한 쌍: 월대 남쪽 끝 좌우 (2023 복원 뒤 자리 꼴), 남쪽을 봄
    for sid in ('haetae_w', 'haetae_e'):
        s = SITE[sid]; x, z = s['x'], s['z']; y = g(x, z)
        yt = math.ceil(max(g(x + a_, z + b_) for a_ in (-1, 0, 1) for b_ in (-1.6, 0, 1.6)) / 0.05) * 0.05
        Gw.box('stone_dressed', x - 1.1, y - 0.9, z - 1.7, x + 1.1, yt + 0.02, z + 1.7)
        Gw.col(x - 0.95, y - 0.9, z - 1.55, x + 0.95, yt + 3.7, z + 1.55, n='HaetaeCol')
        Gw.prefab('Hero_Haetae_' + sid[-1].upper(), x, yt, z, 0, n='Haetae_' + sid[-1].upper())  # 앉은 해치 + 받침, 남향
    # 궁장: 사괴석 담 + 기와, 높이 4.6 m (EST) - 문 자리는 비움
    gaps = {'S': [(AX - 20 - X0, AX + 20 - X0)], 'E': [(SITE['gbg_geonchunmun']['z'] - 8 - Z0, SITE['gbg_geonchunmun']['z'] + 8 - Z0)],
            'W': [(SITE['gbg_yeongchumun']['z'] - 8 - Z0, SITE['gbg_yeongchumun']['z'] + 8 - Z0)], 'N': [(SITE['gbg_sinmumun']['x'] - 8 - X0, SITE['gbg_sinmumun']['x'] + 8 - X0)]}
    sides = dict(S=((X0, Z0), (X1, Z0)), E=((X1, Z0), (X1, Z1)), N=((X0, Z1), (X1, Z1)), W=((X0, Z0), (X0, Z1)))
    for k, (a, b) in sides.items():
        L = math.hypot(b[0] - a[0], b[1] - a[1]); nch = max(1, round(L / 110)); dx, dz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
        for c in range(nch):
            t0, t1 = L * c / nch, L * (c + 1) / nch; pa = (a[0] + dx * t0, a[1] + dz * t0); pb = (a[0] + dx * t1, a[1] + dz * t1)
            Gc = G(u, 'Gungjang_%s%d' % (k, c), (pa[0] + pb[0]) / 2, (pa[1] + pb[1]) / 2, cull=0.008)
            wall(Gc, pa, pb, h=4.6, th=0.9, gaps=[(g0 - t0, g1 - t0) for g0, g1 in gaps[k]], step=7.0, skirt=2.2)
    # 건춘문(동) · 영추문(서) · 신무문(북): 닫힘
    Gg = G(u, 'WallGates', AX, 3520.0, cull=0.004)
    for sid, yaw in (('gbg_geonchunmun', 270), ('gbg_yeongchumun', 90), ('gbg_sinmumun', 180)):
        s = SITE[sid]; x = X1 if yaw == 270 else X0 if yaw == 90 else s['x']; z = Z1 if yaw == 180 else s['z']
        y = lvl(x - 6, z - 6, x + 6, z + 6, 0.1); gate_small(Gg, x, z, yaw, y, True, sid[4:]); Gg.box('stone_dressed', x - 8.4, y - 4.5, z - 8.4, x + 8.4, y + 0.02, z + 8.4); rep['levels'][sid] = y
    # 동 · 서십자각: 돌 받침 + 여장 + 3 x 3칸 사모지붕 누 (EST)
    for sid in ('gbg_sipjagak_e', 'gbg_sipjagak_w'):
        s = SITE[sid]; x, z = s['x'], s['z']; Gs = G(u, 'Sipjagak_' + sid[-1].upper(), x, z, cull=0.006); y = lvl(x - 4, z - 4, x + 4, z + 4, 0.1); hb = 5.2
        Gs.box('stone_dressed', x - 4.3, y - 4.0, z - 4.3, x + 4.3, y + 0.62, z + 4.3)
        Gs.prefab('Hero_Sipjagak', x, y + 0.6, z, 0, n='Sipjagak')
        Gs.col(x - 4.2, y - 4, z - 4.2, x + 4.2, y + hb + 3, z + 4.2, n='TowerCol')


# =====================================================================================================
# B2  흥례문 - 영제교 - 근정문 - 근정전 (조정)
# =====================================================================================================
RX0, RX1 = 1914.0, 2004.0; ZS, ZM, ZN = 3425.6, 3489.3, 3586.4
C_W, C_E = RX0 + D_COR / 2, RX1 - D_COR / 2
GJ = (AX, 3552.2)


def b2():
    u = unit('PalaceAxis')
    # --- outer court: side walls (용성문 · 협생문 자리는 닫힌 담), 어도
    Go = G(u, 'OuterCourt', AX, 3373.0)
    ox0, ox1 = 1886.1, 2031.9
    wall(Go, (ox0, Z0 + 0.6), (ox0, ZS), h=3.4, th=0.8); wall(Go, (ox1, Z0 + 0.6), (ox1, ZS), h=3.4, th=0.8)
    wall(Go, (ox0, ZS), (RX0, ZS), h=3.4, th=0.8); wall(Go, (RX1, ZS), (ox1, ZS), h=3.4, th=0.8)
    eodo(Go, GH_Z + 6.6, ZS - 6.0, L_OUT)
    # --- 흥례문 (중층 누문: 화성행궁 신풍루 경량본, 실제 크기) + 기단
    Gh = G(u, 'Heungnyemun', AX, ZS, cull=0.006)
    gate2(Gh, AX, ZS, L_MID, L_OUT, 'Heungnyemun')
    # --- middle court: 행각 ring, 금천 + 영제교
    Gm = G(u, 'MiddleCourt', AX, 3457.0)
    gw = 9.4
    corridor(Gm, (C_W, ZS), (AX - gw, ZS), (0, 1), L_MID, (True, False)); corridor(Gm, (AX + gw, ZS), (C_E, ZS), (0, 1), L_MID, (False, True))
    corridor(Gm, (C_W, ZS), (C_W, ZM), (1, 0), L_MID, (True, False)); corridor(Gm, (C_E, ZS), (C_E, ZM), (-1, 0), L_MID, (True, False))
    cz = SITE['gbg_yeongjegyo']['z']; ch0, ch1 = cz - 2.3, cz + 2.3; bed = 59.3
    for z_ in (ch0, ch1):
        for xa, xb in ((RX0 + D_COR, AX - 5.2), (AX + 5.2, RX1 - D_COR)): Gm.box('stone_dressed', xa, bed - 0.6, z_ - 0.25, xb, L_MID + 0.12, z_ + 0.25)
    Gm.top('fieldstone', RX0 + D_COR, bed + 0.05, ch0, RX1 - D_COR, ch1)
    # 영제교: 13.85 x 9.8 m (실제) -> 길이는 도랑(4.6 m)에 맞춰 9 m, 세 갈래, 돌난간
    bz0, bz1 = cz - 4.5, cz + 4.5; dk = L_MID + 0.22
    Gm.box('stone_dressed', AX - 4.9, bed - 0.6, bz0, AX + 4.9, dk, bz1); Gm.box('paving', AX - 1.7, dk, bz0, AX + 1.7, dk + 0.06, bz1)
    for sgn in (-1, 1):
        rail(Gm, (AX + sgn * 4.7, bz0), (AX + sgn * 4.7, bz1), dk); Gm.col(AX + sgn * 4.7 - 0.15, dk, bz0, AX + sgn * 4.7 + 0.15, dk + 1.1, bz1, n='RailCol')
    Gm.col(AX - 4.9, bed - 0.6, bz0, AX + 4.9, dk, bz1, n='BridgeCol')
    for z_, yaw in ((bz0, 180), (bz1, 0)): Gm.ramp(AX, z_ + (-0.6 if yaw == 180 else 0.6), yaw, 9.8, 1.2, L_MID, dk)
    eodo(Gm, ZS + 6.0, bz0, L_MID); eodo(Gm, bz1, ZM - 6.0, L_MID)
    # --- 근정문 (중층) + 조정 행각 ring + 박석 + 품계석
    Gn = G(u, 'Geunjeongmun', AX, ZM, cull=0.006)
    gate2(Gn, AX, ZM, 60.79, L_MID, 'Geunjeongmun', content_door=True)  # floor = the content door's sill (60.81)
    Gj = G(u, 'Jojeong', AX, 3540.0, cull=0.006)
    corridor(Gj, (C_W, ZM), (AX - gw, ZM), (0, 1), L_JO, (False, False)); corridor(Gj, (AX + gw, ZM), (C_E, ZM), (0, 1), L_JO, (False, False))
    yn = L_SA
    for xc, inner in ((C_W, (1, 0)), (C_E, (-1, 0))):
        zc = ZM; segs = [(ZM, 3522.0), (3522.0, 3554.0), (3554.0, ZN)]
        for i, (za, zb) in enumerate(segs):
            y = max(L_JO, lvl(xc - 3, za, xc + 3, zb)); corridor(Gj, (xc, za), (xc, zb), inner, y, (False, i == len(segs) - 1)); rep['levels']['jo_%s_%d' % ('W' if inner[0] > 0 else 'E', i)] = y
            yn = max(yn, y if i == len(segs) - 1 else 0)
    corridor(Gj, (C_W, ZN), (AX - 6.4, ZN), (0, -1), yn, (True, False)); corridor(Gj, (AX + 6.4, ZN), (C_E, ZN), (0, -1), yn, (False, True))
    # 박석 마당 (남쪽이 낮게 기운 실제 조정처럼 땅을 따라 얹는다: 4 m 칸)
    px0, px1, pz0, pz1 = RX0 + D_COR + 0.3, RX1 - D_COR - 0.3, ZM + D_COR / 2 + 0.3, ZN - D_COR / 2 - 0.3; nx_, nz_ = 22, 24
    for i in range(nx_):
        for j in range(nz_):
            xa, xb = px0 + (px1 - px0) * i / nx_, px0 + (px1 - px0) * (i + 1) / nx_; za, zb = pz0 + (pz1 - pz0) * j / nz_, pz0 + (pz1 - pz0) * (j + 1) / nz_
            y = max(g(xa, za), g(xb, za), g(xa, zb), g(xb, zb), g((xa + xb) / 2, (za + zb) / 2)) + 0.05
            Gj.top('paving', xa, y, za, xb, zb)
    eodo(Gj, ZM + 6.0, GJ[1] - 22.5, L_JO)
    PUM = []
    for k in range(12):
        z_ = 3498.5 + k * 2.75
        for sgn in (-1, 1):
            x_ = AX + sgn * 5.4; y = g(x_, z_); PUM.extend([round(sgn * 5.4, 3), round(y - 0.04 - L_JO, 3), round(z_ - 3498.5, 3), 270 if sgn < 0 else 90])
    composites.append(dict(name='Hero_Pumgyeseok_Rows', src='Hero_Pumgyeseok', lod=0, cull=0.008, pts=PUM)); Gj.prefab('Hero_Pumgyeseok_Rows', AX, L_JO, 3498.5, 0, n='Pumgyeseok')
    # --- 근정전: 2단 월대(합 2.72 m) + 5 x 5칸 중층 팔작 (gen, 실제 크기 30.2 x 20.6 m 몸)
    Gk = G(u, 'Geunjeongjeon', GJ[0], GJ[1], cull=0.004)
    cx, cz = GJ; t1, t2 = L_JO + 1.36, L_JO + 2.72
    lx, lz0, lz1 = 21.5, cz - 21.5, cz + 17.5; ux, uz0, uz1 = 18.6, cz - 16.5, cz + 14.4
    TR = 2.86 / 7; Gk.mute = True  # colliders only: the hero 월대 carries the stone
    platform(Gk, cx - lx, lz0, cx + lx, lz1, L_JO - 1.6, t1); platform(Gk, cx - ux, uz0, cx + ux, uz1, t1 - 0.1, t2)
    sw = 7.0
    stairs(Gk, cx, lz0, 180, sw, L_JO, t1, tread=TR); stairs(Gk, cx, uz0, 180, sw, t1, t2, tread=TR)
    stairs(Gk, cx, lz1, 0, 4.0, max(L_JO, g(cx, lz1 + 3)), t1, tread=TR); stairs(Gk, cx, uz1, 0, 4.0, t1, t2, tread=TR)
    for sgn, yaw in ((-1, 270), (1, 90)):
        stairs(Gk, cx + sgn * lx, cz - 4, yaw, 3.6, max(L_JO, g(cx + sgn * (lx + 3), cz - 4)), t1, tread=TR); stairs(Gk, cx + sgn * ux, cz - 4, yaw, 3.6, t1, t2, tread=TR)
    Gk.mute = False
    Gk.box('stone_dressed', cx - lx, L_JO - 1.6, lz0, cx + lx, L_JO + 0.03, lz1); Gk.box('stone_dressed', cx - 3.9, L_JO - 1.6, lz0 - 3.0, cx + 3.9, L_JO + 0.02, lz0)
    for sgn in (-1, 1): Gk.box('stone_dressed', cx + sgn * lx, L_JO - 1.6, cz - 6.0, cx + sgn * (lx + 3.0), L_JO + 0.02, cz - 2.0)
    Gk.prefab('Hero_Woldae_Geunjeong', cx, L_JO, cz, 0, n='Woldae')
    Gk.prefab('Hero_Geunjeongjeon', cx, t2, cz, 0, n='Geunjeongjeon'); Gk.col(cx - 15.1, t2, cz - 10.3, cx + 15.1, t2 + 9, cz + 10.3, n='Body')
    # 드므 한 쌍: 하월대 남쪽 좌우 모서리 (출처가 엇갈림 - spec 14.1)
    dz = lz0 + 2.0 - cz; composites.append(dict(name='Hero_Deumeu_Pair', src='Hero_Deumeu', lod=0, cull=0.01, pts=[-19.9, 0, dz, 0, 19.9, 0, dz, 0]))
    Gk.prefab('Hero_Deumeu_Pair', cx, t1, cz, 0, n='Deumeu')
    for sgn in (-1, 1): Gk.col(cx + sgn * 19.9 - 0.6, t1, cz + dz - 0.6, cx + sgn * 19.9 + 0.6, t1 + 1.0, cz + dz + 0.6, n='DeumeuCol')


def gate2(G_, x, z, y_hi, y_lo, name, content_door=False):
    """중층 누문 (신풍루 경량본 19.7 x 13.5 m) on a 기단; centre bay open, side bays shut by colliders"""
    hw, hd = 9.2, 5.0
    platform(G_, x - hw, z - hd, x + hw, z + hd, y_lo - 1.5, y_hi + 0.02, col=False)
    stairs(G_, x, z - hd, 180, 2 * hw - 1.2, y_lo, y_hi + 0.02, cheek=False)
    G_.col(x - hw, y_lo - 1.5, z - hd, x + hw, y_hi + 0.02, z + hd, n='PlatCol')
    G_.prefab('Hero_Gate_Heungnye', x, y_hi + 0.02, z, 0, n=name)
    if content_door:  # the 5.55 m centre bay around the 4.4 m content door: jambs
        for sgn in (-1, 1): G_.box('wood_red', x + sgn * 2.2 - (0.22 if sgn < 0 else 0), y_hi, z - 0.55, x + sgn * 2.2 + (0.22 if sgn > 0 else 0), y_hi + 4.3, z + 0.15)
    op = 2.2 if content_door else 2.5  # the content door (Door_Jeonmun, 4.4 m of leaves) fills the open bay
    for sgn in (-1, 1): G_.col(min(x + sgn * op, x + sgn * 8.6), y_hi, z - 3.2, max(x + sgn * op, x + sgn * 8.6), y_hi + 6.5, z + 3.2, n='SideBay')
    G_.col(x - 2.5, y_hi + 4.6, z - 3.2, x + 2.5, y_hi + 6.5, z + 3.2, n='Lintel')


# =====================================================================================================
# B3  사정전 - 강녕전 - 교태전 - 아미산
# =====================================================================================================
def court_walls(G_, x0, z0, x1, z1, south_gap, north_gap, side_gaps_z=(), h=2.8, south=True, north=True):
    cx = (x0 + x1) / 2
    if south: wall(G_, (x0, z0), (x1, z0), h=h, gaps=[(cx - x0 - south_gap / 2, cx - x0 + south_gap / 2)] if south_gap else ())
    if north: wall(G_, (x0, z1), (x1, z1), h=h, gaps=[(cx - x0 - north_gap / 2, cx - x0 + north_gap / 2)] if north_gap else ())
    gz = [(zc - z0 - 1.5, zc - z0 + 1.5) for zc in side_gaps_z]
    wall(G_, (x0, z0), (x0, z1), h=h, gaps=gz); wall(G_, (x1, z0), (x1, z1), h=h, gaps=gz)
    for zc in side_gaps_z:
        for xx in (x0, x1): ilgak(G_, xx, zc, 'z', max(g(xx, zc), g(xx - 1, zc), g(xx + 1, zc)))


def mury(G_, x, y, z, s, n):
    """무량각 침전 (Hero_Hall_Muryanggak9: 몸 28.5 x 11.8, 기단 0.9, 앞 월대 18 x 6): body + 월대 colliders, centre front stair walkable"""
    sx, sy, sz = s; G_.prefab('Hero_Hall_Muryanggak9', x, y, z, 0, s, n=n)
    G_.col(x - 15.75 * sx, y, z - 7.4 * sz, x + 15.75 * sx, y + 5.5 * sy, z + 7.4 * sz, n='Body')
    G_.col(x - 9.0 * sx, y - 0.5, z - 13.4 * sz, x + 9.0 * sx, y + 0.78 * sy, z - 7.4 * sz, n='PlatCol')
    G_.ramp(x, z - (13.4 + 0.75) * sz, 180, 2.6 * sx, 1.5 * sz, y, y + 0.78 * sy)


def gate1(G_, x, z, y, name):
    """삼문 (내삼문 경량본 12.6 x 7.9 m), centre bay open"""
    G_.prefab('Gate_Naesanmun', x, y, z, 0, n=name)
    for sgn in (-1, 1): G_.col(min(x + sgn * 1.7, x + sgn * 5.6), y, z - 2.2, max(x + sgn * 1.7, x + sgn * 5.6), y + 4.5, z + 2.2, n='SideBay')


def b3():
    u = unit('PalaceInner')
    # 사정전 일곽: 사정문은 조정 북행각 가운데 (ZN)
    Gs = G(u, 'Sajeongjeon', AX, 3607.5, cull=0.005)
    gate1(Gs, AX, ZN, L_SA, 'Sajeongmun')
    sx0, sx1, sz1 = 1926.0, 1992.0, 3626.0
    wall(Gs, (sx0, ZN + 3.2), (sx0, sz1), h=2.8, gaps=[(12, 15)]); wall(Gs, (sx1, ZN + 3.2), (sx1, sz1), h=2.8, gaps=[(12, 15)])
    for xx in (sx0, sx1): ilgak(Gs, xx, ZN + 3.2 + 13.5, 'z', g(xx, ZN + 16.7))
    hall(Gs, 'Hall_Yeonhuigak', AX, L_SA, 3610.0, 0, n='Sajeongjeon'); hall(Gs, 'Hall_Honhwagak', AX + 23.6, L_SA, 3610.5, 0, (0.8, 0.88, 0.8), 'Manchunjeon'); hall(Gs, 'Hall_Wooryeondang', AX - 23.6, L_SA, 3610.5, 0, (0.8, 0.88, 0.8), 'Cheonchujeon')
    eodo(Gs, ZN + 4.5, 3602.5, L_SA, 3.2)
    # 강녕전 일곽: 향오문 + 강녕전(봉수당 경량본 41.7 x 30.5 m) + 연생전 · 경성전 (남북으로 긴 소침)
    Gg = G(u, 'Gangnyeongjeon', AX, 3648.9, cull=0.005)
    gx0, gx1, gz0, gz1 = 1920.7, 1997.3, sz1, 3670.5
    court_walls(Gg, gx0, gz0, gx1, gz1, 11.6, 0, side_gaps_z=(3634.0,), north=False)
    gate1(Gg, AX, gz0, L_GA, 'Hyangomun')
    mury(Gg, AX, L_GA, 3651.5, (1.15, 1.0, 1.0), 'Gangnyeongjeon')
    hall(Gg, 'Corr_Jibsacheong', AX - 31.0, L_GA, 3650.0, 0, n='Gyeongseongjeon'); hall(Gg, 'Corr_Jibsacheong', AX + 31.0, L_GA, 3650.0, 180, n='Yeonsaengjeon')
    # 교태전 일곽: 양의문 + 교태전(복내당 경량본) + 아미산
    Gt = G(u, 'Gyotaejeon', AX, 3690.0, cull=0.005)
    tx0, tx1, tz0, tz1 = 1926.8, 1991.2, gz1, 3722.5
    court_walls(Gt, tx0, tz0, tx1, tz1, 11.6, 0, side_gaps_z=(3677.0,))
    wall(Gt, (gx0, gz1), (tx0, gz1), h=2.8); wall(Gt, (tx1, gz1), (gx1, gz1), h=2.8)
    gate1(Gt, AX, tz0, L_GY, 'Yanguimun')
    mury(Gt, AX, L_GY, 3690.5, (0.85, 0.85, 0.85), 'Gyotaejeon')
    # 아미산: 4단 화계 (장대석 단) + 육각 굴뚝 4 (벽돌빛, 기와 지붕)
    az0 = 3704.2
    for k in range(4):
        za = az0 + k * 3.6; yk = L_GY + 0.7 * (k + 1)
        Gt.box('stone_dressed', 1934.5, L_GY - 1.0, za, 1983.5, yk, tz1 - 0.4); Gt.col(1934.5, L_GY - 1.0, za, 1983.5, yk, tz1 - 0.4, n='Terrace')
        Gt.top('fieldstone', 1934.7, yk + 0.01, za + 0.3, 1983.3, tz1 - 0.5)
    for i, xo in enumerate((-13.5, -4.5, 4.5, 13.5)):
        x_, z_ = AX + xo, az0 + 3.6 * 1 + 1.8 + (0.0 if i in (0, 3) else 3.6); yb = L_GY + 0.7 * (2 if i in (0, 3) else 3)
        Gt.cyl('brick_dark', x_, yb, z_, 0.62, yb + 2.3, 6); Gt.cyl('stone_dressed', x_, yb, z_, 0.72, yb + 0.3, 6); Gt.pyr('tile_dark', x_ - 0.95, yb + 2.3, z_ - 0.95, x_ + 0.95, yb + 2.85, z_ + 0.95)
        Gt.col(x_ - 0.6, yb, z_ - 0.6, x_ + 0.6, yb + 2.3, z_ + 0.6, n='Chimney')


# =====================================================================================================
# B4  경회루 · B5 향원정 · 수정전 · 자경전 · 부속 행각
# =====================================================================================================
def pond(G_, x0, z0, x1, z1, bed, bank, water):
    t = 0.7
    for a, b in (((x0, z0), (x1, z0 + t)), ((x0, z1 - t), (x1, z1)), ((x0, z0), (x0 + t, z1)), ((x1 - t, z0), (x1, z1))):
        G_.box('stone_dressed', a[0], bed - 0.8, a[1], b[0], bank + 0.25, b[1]); G_.col(a[0], bed - 0.8, a[1], b[0], bank + 0.25, b[1], n='Revet')
    G_.top('water', x0 + t, water, z0 + t, x1 - t, z1 - t)


def b4():
    u = unit('PalacePonds')
    s = SITE['gbg_gyeonghoe_pond']; x0, z0, x1, z1 = s['corners'][0][0], s['corners'][0][1], s['corners'][2][0], s['corners'][2][1]
    bank = lvl(x0, z0, x1, z1, 0.1); bed = 59.8; water = min(bank - 0.55, 61.6); rep['levels']['gyeonghoe'] = dict(bank=bank, water=water, corners=[round(g(x, z), 2) for x in (x0 - 1.5, x1 + 1.5) for z in (z0 - 1.5, z1 + 1.5)])
    Gp = G(u, 'GyeonghoePond', (x0 + x1) / 2, (z0 + z1) / 2, cull=0.004)
    pond(Gp, x0, z0, x1, z1, bed, bank, water)
    # 섬 + 경회루 (gen 34.4 x 28.5 m 실제 크기, 돌기둥 48)
    ix0, ix1, iz0, iz1 = 1854.0, 1898.0, 3616.5 - 1.2, 3653.7 + 1.2; it = water + 0.55
    platform(Gp, ix0, iz0, ix1, iz1, bed - 0.8, it); cx, cz = (ix0 + ix1) / 2, (iz0 + iz1) / 2
    for a, b in (((ix0 + .2, iz0 + .2), (ix1 - .2, iz0 + .2)), ((ix0 + .2, iz1 - .2), (ix1 - .2, iz1 - .2)), ((ix0 + .2, iz0 + .2), (ix0 + .2, iz1 - .2))): rail(Gp, a, b, it)
    Gp.prefab('Hero_Gyeonghoeru', cx, it, cz, 90, n='Gyeonghoeru')
    Gp.col(cx - 15.8, it, cz - 18.7, cx + 15.8, it + 0.9, cz + 18.7, n='Stylobate')
    stairs(Gp, cx + 15.8, cz, 90, 5.0, it, it + 0.9, cheek=False)
    # 돌다리 셋 (동쪽): 가운데가 넓다
    prev = iz0 + .2
    for zc, w in ((cz - 11.5, 3.4), (cz, 5.0), (cz + 11.5, 3.4)):
        Gp.box('stone_dressed', ix1, it - 0.5, zc - w / 2, x1, it + 0.04, zc + w / 2, True); Gp.col(ix1, it - 0.5, zc - w / 2, x1, it + 0.04, zc + w / 2, n='BridgeCol')
        for k in range(1, 3): Gp.box('stone_dressed', ix1 + (x1 - ix1) * k / 3 - 0.3, bed - 0.8, zc - w / 2 + 0.2, ix1 + (x1 - ix1) * k / 3 + 0.3, it - 0.5, zc + w / 2 - 0.2)
        for sgn in (-1, 1): rail(Gp, (ix1, zc + sgn * (w / 2 - 0.12)), (x1, zc + sgn * (w / 2 - 0.12)), it + 0.04, sp=2.0)
        rail(Gp, (ix1 - .2, prev), (ix1 - .2, zc - w / 2), it); prev = zc + w / 2
        if abs(bank + 0.25 - it) > 0.05: Gp.ramp(x1 + 0.6, zc, 90 if bank + 0.25 < it else 270, w, 1.6, min(bank + 0.25, it), max(bank + 0.25, it))
    rail(Gp, (ix1 - .2, prev), (ix1 - .2, iz1 - .2), it)
    # --- 향원지 + 향원정 (팔각정 경량본: 실제는 육각 2층) + 취향교(북쪽 나무다리)
    s = SITE['gbg_hyangwon_pond']; x0, z0, x1, z1 = s['corners'][0][0], s['corners'][0][1], s['corners'][2][0], s['corners'][2][1]
    bank = lvl(x0, z0, x1, z1, 0.1); bed = 54.22; water = bank - 0.5; rep['levels']['hyangwon'] = dict(bank=bank, water=water)
    Gh = G(u, 'Hyangwon', (x0 + x1) / 2, (z0 + z1) / 2, cull=0.004)
    pond(Gh, x0, z0, x1, z1, bed, bank, water)
    cx, cz = SITE['gbg_hyangwonjeong']['x'], SITE['gbg_hyangwonjeong']['z']; it = water + 0.5
    Gh.cyl('stone_dressed', cx, bed - 0.8, cz, 9.0, it, 16); Gh.top('fieldstone', cx - 6.2, it + 0.01, cz - 6.2, cx + 6.2, cz + 6.2); Gh.col(cx - 7.2, bed - 0.8, cz - 7.2, cx + 7.2, it, cz + 7.2, n='Island')
    Gh.prefab('Hero_Hyangwonjeong', cx, it, cz, 180, n='Hyangwonjeong'); Gh.col(cx - 2.2, it, cz - 2.2, cx + 2.2, it + 3, cz + 2.2, n='Body')
    bz0, bz1 = cz + 8.2, z1; w = 2.0
    Gh.box('wood_dark', cx - w / 2, it - 0.22, bz0, cx + w / 2, it + 0.02, bz1, True); Gh.col(cx - w / 2, it - 0.22, bz0 - 1.5, cx + w / 2, it + 0.02, bz1, n='BridgeCol')
    n = 7
    for k in range(n + 1):
        z_ = bz0 + (bz1 - bz0) * k / n
        for sgn in (-1, 1):
            Gh.box('wood_dark', cx + sgn * (w / 2 - 0.08) - 0.07, bed - 0.6 if k % 2 == 0 else it, z_ - 0.07, cx + sgn * (w / 2 - 0.08) + 0.07, it + 0.95, z_ + 0.07)
    for sgn in (-1, 1): Gh.box('wood_red', cx + sgn * (w / 2 - 0.08) - 0.05, it + 0.82, bz0, cx + sgn * (w / 2 - 0.08) + 0.05, it + 0.92, bz1, True)
    if abs(bank + 0.25 - it) > 0.05: Gh.ramp(cx, z1 + 0.6, 0 if bank + 0.25 < it else 180, w, 1.6, min(bank + 0.25, it), max(bank + 0.25, it))


def yard(u, name, sid, halls, gate_side='S', h=2.6, gap=4.0, cull=0.006):
    s = SITE[sid]; x0, z0, x1, z1 = s['corners'][0][0] + 0.8, s['corners'][0][1] + 0.8, s['corners'][2][0] - 0.8, s['corners'][2][1] - 0.8
    Gy = G(u, name, (x0 + x1) / 2, (z0 + z1) / 2, cull=cull); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    def gp(side, L): return [(L / 2 - gap / 2, L / 2 + gap / 2)] if gate_side == side else ()
    wall(Gy, (x0, z0), (x1, z0), h=h, gaps=gp('S', x1 - x0)); wall(Gy, (x0, z1), (x1, z1), h=h, gaps=gp('N', x1 - x0)); wall(Gy, (x0, z0), (x0, z1), h=h, gaps=gp('W', z1 - z0)); wall(Gy, (x1, z0), (x1, z1), h=h, gaps=gp('E', z1 - z0))
    gx, gz = dict(S=(cx, z0), N=(cx, z1), W=(x0, cz), E=(x1, cz))[gate_side]
    return Gy, (x0, z0, x1, z1), (gx, gz)


def b5():
    u = unit('PalaceService')
    # 수정전 (집현전 자리): 넓은 월대 위 긴 전각 - 봉수당 LOD1
    Gy, (x0, z0, x1, z1), (gx, gz) = yard(u, 'Sujeongjeon', 'gbg_sujeongjeon', None, 'S'); y = L_JO; cx = (x0 + x1) / 2
    ilgak(Gy, gx, gz, 'x', g(gx, gz)); platform(Gy, cx - 24, 3540.0, cx + 24, 3569.5, y - 1.2, y + 0.75); stairs(Gy, cx, 3540.0, 180, 9.0, y, y + 0.75)
    hall(Gy, 'Hall_Bongsudang_L1', cx, y + 0.75, 3555.5, 0, (0.95, 1, 0.82), 'Sujeongjeon', ov=2.4)
    hall(Gy, 'Corr_Jibsacheong_L1', x0 + 7.0, y, 3530.0, 0, n='Gaksa_W'); hall(Gy, 'Corr_Jibsacheong_L1', x1 - 7.0, y, 3530.0, 180, n='Gaksa_E')
    # 동궁 (자선당 · 비현각)
    Gy, (x0, z0, x1, z1), (gx, gz) = yard(u, 'Donggung', 'gbg_donggung', None, 'W'); ilgak(Gy, gx, gz, 'z', g(gx, gz)); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; y = lvl(cx - 12, cz - 10, cx + 12, cz + 14, 0.1)
    hall(Gy, 'Hall_Yuyeotaek_L1', cx - 16, lvl(cx - 27, cz, cx - 5, cz + 16, 0.1), cz + 8, 0, n='Jaseondang'); hall(Gy, 'Hall_Yeonjuhyeopdang_L1', cx + 16, lvl(cx + 6, cz, cx + 26, cz + 16, 0.1), cz + 8, 0, n='Bihyeongak')
    hall(Gy, 'Corr_Jibsacheong_L1', x1 - 7.5, lvl(x1 - 12, z0 + 4, x1 - 3, z0 + 26, 0.1), z0 + 15, 180, n='Chunbang')
    # 소주방
    Gy, (x0, z0, x1, z1), (gx, gz) = yard(u, 'Sojubang', 'gbg_sojubang', None, 'W'); ilgak(Gy, gx, gz, 'z', g(gx, gz)); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    for i, (xo, yaw) in enumerate(((-14, 0), (2, 180), (18, 0))): hall(Gy, 'Corr_Jibsacheong_L1', cx + xo, lvl(cx + xo - 5, cz - 10, cx + xo + 5, cz + 10, 0.1), cz, yaw, n='Sojubang_%d' % i)
    # 자경전
    Gy, (x0, z0, x1, z1), (gx, gz) = yard(u, 'Jagyeongjeon', 'gbg_jagyeongjeon', None, 'S'); ilgak(Gy, gx, gz, 'x', g(gx, gz)); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    hall(Gy, 'Hall_Boknaedang_L1', cx, lvl(cx - 11, cz - 6, cx + 11, cz + 14, 0.1), cz + 4, 0, (1.15, 1, 1), 'Jagyeongjeon'); hall(Gy, 'Corr_Jibsacheong_L1', x0 + 7.5, lvl(x0 + 3, cz - 12, x0 + 12, cz + 10, 0.1), cz - 1, 0, n='Hyeopgyeongdang')
    # 건청궁
    Gy, (x0, z0, x1, z1), (gx, gz) = yard(u, 'Geoncheonggung', 'gbg_geoncheong', None, 'S'); ilgak(Gy, gx, gz, 'x', g(gx, gz)); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; y = lvl(x0, z0, x1, z1, 0.1)
    hall(Gy, 'Hall_Yuyeotaek_L1', cx - 14, y, cz + 2, 0, n='Jangandang'); hall(Gy, 'Hall_Yeonhuigak_L1', cx + 14, y, cz + 2, 0, n='Gonnyeonghap')


# =====================================================================================================
# B6  육조거리 관아
# =====================================================================================================
OFFICE = dict(yukjo_e0=('Uijeongbu', 3), yukjo_e1=('Ijo', 1), yukjo_e2=('Hanseongbu', 1), yukjo_w0=('Yejo', 1), yukjo_w1=('Jungchubu', 1), yukjo_w2=('Saheonbu', 1),
              yukjo_s0=('Byeongjo', 1), yukjo_s1=('Hojo', 1), yukjo_s2=('Hyeongjo', 1), yukjo_s3=('Gongjo', 1))
HALLS = ['Hall_Yeonhuigak_L1', 'Hall_Honhwagak_L1', 'Hall_Wooryeondang_L1', 'Hall_Yeonjuhyeopdang_L1']


def b6():
    u = unit('Yukjo'); k = 0
    for sid, (name, nh) in OFFICE.items():
        s = SITE[sid]; door = s['door']; x0, z0, x1, z1 = s['corners'][0][0] + 0.7, s['corners'][0][1] + 0.7, s['corners'][2][0] - 0.7, s['corners'][2][1] - 0.7
        Go = G(u, name, (x0 + x1) / 2, (z0 + z1) / 2, cull=0.008); cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; y = lvl(x0, z0, x1, z1, 0.1); rep['levels'][sid] = y
        gl = 11.2  # 솟을대문채 (창원의 집 대문채 경량본 7.3 x 10.8 m: 문이 +-X 를 봄)
        def gp(side, L): return [(L / 2 - gl / 2, L / 2 + gl / 2)] if door == side else ()
        wall(Go, (x0, z0), (x1, z0), h=2.6, gaps=gp('S', x1 - x0), lvl_fix=None); wall(Go, (x0, z1), (x1, z1), h=2.6); wall(Go, (x0, z0), (x0, z1), h=2.6, gaps=gp('W', z1 - z0)); wall(Go, (x1, z0), (x1, z1), h=2.6, gaps=gp('E', z1 - z0))
        if door == 'W': gx, gz, gyaw, hyaw, fx, fz = x0 + 2.4, cz, 0, 90, 1, 0
        elif door == 'E': gx, gz, gyaw, hyaw, fx, fz = x1 - 2.4, cz, 180, 270, -1, 0
        else: gx, gz, gyaw, hyaw, fx, fz = cx, z0 + 2.4, 90, 0, 0, 1
        gy = max(y, lvl(gx - 5, gz - 5, gx + 5, gz + 5, 0.1)); Go.prefab('Gate_Soseul_Changwon', gx, gy, gz, gyaw, n=name + '_Daemun')
        Go.items.append(item('col', gx, gy + 2.5, gz, gyaw, (5.0, 5.0, gl), n='GateShut'))
        Go.box('stone_dressed', gx - (3.4 if fx else 5.8), gy - 1.5, gz - (5.8 if fx else 3.4), gx + (3.4 if fx else 5.8), gy + 0.03, gz + (5.8 if fx else 3.4))
        depth = (x1 - x0) if fx else (z1 - z0); span = (z1 - z0) if fx else (x1 - x0)
        hx, hz = cx + fx * (depth / 2 - 10.5), cz + fz * (depth / 2 - 10.5)
        if nh == 3:  # 의정부: 정본당 가운데 + 협선당 · 석획당 좌우 (3당 병립), 뒤에 연지
            for j, (o, hn, sc) in enumerate(((0, 'Hall_Yeonhuigak', 1.0), (-15.2, 'Hall_Yeonjuhyeopdang_L1', 0.74), (15.2, 'Hall_Honhwagak_L1', 0.74))):
                px, pz = hx + (0 if fx else o), hz + (o if fx else 0); hall(Go, hn, px, y, pz, hyaw, (sc, max(sc, 0.85), sc), name + '_' + ('Jeongbondang', 'Hyeopseondang', 'Seokhoekdang')[j])
        else:
            hall(Go, HALLS[k % 4], hx, y, hz, hyaw, n=name + '_Daecheong'); k += 1
            if span > 30:  # 행랑 (집사청 LOD1): 마당 한쪽 담 곁
                lx, lz = (cx - fx * 3.0, z0 + 6.0) if fx else (x0 + 6.0, cz - fz * 3.0)
                hall(Go, 'Corr_Jibsacheong_L1', lx, y, lz, 90 if fx else 0, (0.85, 0.9, 0.85), name + '_Haengnang')
    rep['built']['offices'] = len(OFFICE)


for f in (b1, b2, b3, b4, b5, b6): f()
out_units = [dict(name=u['name'], groups=[gr.done() for gr in u['groups']]) for u in units]
prefabs = sorted({it['a'][7:] for u in out_units for gr in u['groups'] for it in gr['items'] if it['a'].startswith('prefab:')})
(ST / 'hero308.json').write_text(json.dumps(dict(version='p5', composites=composites), ensure_ascii=False), encoding='utf-8')
plan = dict(version='308.palace.p5a', prefabs=ONLY_GEN, lite=LITE, lite1=sorted(p[:-3] for p in prefabs if p.endswith('_L1')), solids=solids, units=out_units, ribbons=[], bridges=[], rays=[], joins=[])
(ST / 'palace308.json').write_text(json.dumps(plan, ensure_ascii=False), encoding='utf-8')
tri = {0: 10, 1: 6, 2: 6, 3: 2, 4: 4, 5: 6, 7: 12}; nf = {0: 6, 1: 6, 2: 6, 3: 6, 4: 6, 5: 18, 6: 6, 7: 6}; total = 0
for s in solids:
    f = s['f']; i = 0; t = 0
    while i < len(f):
        k = int(f[i]); t += tri.get(k, int(f[i + 6]) * 4 if k == 6 else 0); i += 1 + nf[k]
    total += t
rep['built'].update(units={u['name']: dict(groups=len(u['groups']), items=sum(len(gr['items']) for gr in u['groups']), cols=sum(1 for gr in u['groups'] for it in gr['items'] if it['a'] == 'col')) for u in out_units}, solids=len(solids), solid_tris=total, prefabs=prefabs)
(ST / 'Out/palace_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8')
print(json.dumps(rep, ensure_ascii=False)[:3000])
