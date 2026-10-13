"""#308 GreatBrush v5: v4 를 조금 더 부드럽게 + 먹물에 젖은 붓으로(2026-10-13 사용자 요청).
    blender -b --factory-startup -P build_v5.py -- <out_dir> [--counts 14,24,36,50,56,24] [--seed 5083]
비례 · 소켓 이름 · 뼈 이름 · 셰이프 키 이름 · 꼭짓점 색 R/G/B 는 v4 그대로. fork_v5.py 가 build_v4.py 에서 갈라 만든다.
젖은 다발: 끝 55–60 % 구간에서 이웃 가닥 3–6개가 한 점으로 모여 붙는다(바깥 두 겹 + 셋째 겹). 안쪽 두 겹은 가운데 다발 = 붓끝.
TipSocket = 실제 털끝(축에서 LEAN_AMT 만큼 벗어난다).
꼭짓점 색 Col(선형 float): R = 가닥 난수, G = 뿌리→끝, B = 겹(0 = 속심, 1 = 잔털), A = 젖음 0–1(먹이 오른 높이는 가닥 · 다발마다 다르다). 붓털이 아닌 곳은 (0,0,0,0).
UV: u = 털 올(가닥마다 6칸), v = 먹 양(0 마른 털 → 0.5 묽은 먹 띠 → 1 먹). 뿌리→끝은 G 에 있다."""
import bpy, bmesh, json, math, os, sys, random
from mathutils import Vector, Matrix

ARGV = sys.argv[sys.argv.index('--') + 1:]
OUT = ARGV[0]; os.makedirs(OUT, exist_ok=True)


def arg(name, default): return ARGV[ARGV.index(name) + 1] if name in ARGV else default


COUNTS = [int(x) for x in arg('--counts', '14,24,36,50,56,24').split(',')]
SEED = int(arg('--seed', 5083)); TRI_BUDGET = 18000
rnd = random.Random(SEED)

# ---- 치수 (m) = 1차. z 는 꼬리 끝에서 잰 값, 마지막에 GripSocket 이 원점이 되게 옮긴다 ----
SHAFT = 1.10; NECK = 0.07; HEAD = .38; TOTAL = SHAFT + NECK + HEAD
R_SHAFT = 0.017; R_ROOT = .045
GRIP_FROM_FERRULE = .50; HAND_GAP = .42
Z_GRIP = SHAFT - GRIP_FROM_FERRULE; Z_GRIP_L = Z_GRIP - HAND_GAP
NODES = [0.055, 0.315, 0.455, 0.735, 0.885, 1.02]
KN = R_ROOT / .045
N_BONES = 8; SEG_SHAFT = 14; N_RING = 6; RING_BONES = 4; RING_NAMES = 'ABCDEF'; RING_RADIUS = .026
MATS = ['M_GreatBrush_Bamboo', 'M_GreatBrush_Brass', 'M_GreatBrush_Bristle', 'M_GreatBrush_Cord']
ROOT_COL = (.66, .58, .45); MID_COL = (.22, .17, .12); INK_COL = (.014, .014, .017)
COLORS = {'M_GreatBrush_Bamboo': ((.035, .022, .016), .28, 0), 'M_GreatBrush_Brass': ((.62, .43, .16), .32, 1),
          'M_GreatBrush_Bristle': ((.06, .055, .05), .30, 0), 'M_GreatBrush_Cord': ((.33, .05, .04), .8, 0)}
Z_HEAD = SHAFT + NECK
# 가닥 겹: 가운데 선의 반지름 비율(붓털 윤곽 대비) · 길이 비율 범위 · 뒤처짐 · 고리 사슬 몫 · 단면 각 수
FRACS = [.25, .45, .63, .80, .93, 1.0]; LEN = [(.96, 1.0), (.94, .995), (.93, .99), (.92, .985), (.89, .975), (.58, .88)]
LAG = [.0, .10, .25, .40, .55, .65]; RING_SHARE = [.10, .18, .30, .46, .60, .68]; SIDES = [3, 3, 3, 4, 4, 3]; RINGS = [8, 8, 8, 12, 12, 8]
THICK = [.68, .65, .68, .74, .72, 0]; JIT = [.03, .04, .05, .05, .035, .0]; STRAYS = [0, 0, 0, 1, 3, 0]; LEAN_AMT = .015; N_CLUMPS = 7
TEX = 256; CORE_COLS = 8; H_WET = .30; H_CORE = .25


def sstep(x): x = min(1.0, max(0.0, x)); return x * x * (3 - 2 * x)


def envn(t):
    """붓털 윤곽(1차와 같다)을 뿌리 반지름 1 로: 배(腹)에서 조금 부풀고 허리 뒤로 끝까지 뾰족하게."""
    # v5: 배가 조금 더 통통하고(+15 %), 끝으로 오목하게 빨려 들어간다(곧은 원뿔이 아니다)
    t = min(1.0, max(0.0, t)); return (1 + .22 * math.sin(math.pi * min(t / .62, 1.0))) * max(0.0, 1 - t ** 1.9) ** 1.1


def wetA(tg, h): return sstep((tg - (h - .06)) / .12)                  # 젖음 0–1 (h = 그 가닥에 먹이 오른 높이)
def inkv(tg, h): return min(.998, max(.002, .5 + (tg - h) * 1.5))      # 그림의 세로 = 먹 양


bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); col = bm.loops.layers.float_color.new('Col')
part = bm.verts.layers.int.new('part')   # 0 자루/쇠, 1 붓털
tval = bm.verts.layers.float.new('t')
mid = bm.verts.layers.int.new('mid')     # META 번호(0 = 붓털 아님)
META = [None]


def lathe(profile, segs, mat, cap0=False, cap1=False, wobble=None, center=(0, 0)):
    rings = []
    for k, p in enumerate(profile):
        z, r = p[0], p[1]; ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs; rr = r * (wobble(i, 0.0) if wobble else 1.0)
            v = bm.verts.new((center[0] + math.cos(a) * rr, center[1] + math.sin(a) * rr, z)); ring.append(v)
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            j = (i + 1) % segs
            f = bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i])); f.material_index = mat; f.smooth = True
            v0, v1 = profile[k][0], profile[k + 1][0]
            for l, u in zip(f.loops, [(i / segs, v0), ((i + 1) / segs, v0), ((i + 1) / segs, v1), (i / segs, v1)]): l[uv].uv = u
    for cap, ring, flip in ((cap0, rings[0], True), (cap1, rings[-1], False)):
        if cap:
            f = bm.faces.new(ring[::-1] if flip else ring); f.material_index = mat
            for l in f.loops: l[uv].uv = (.5 + l.vert.co.x * 4, .5 + l.vert.co.y * 4)
    return rings


# ---- 자루 · 꼬리 마개 · 놋쇠 목 (1차 그대로) ----
prof = [(0.0, R_SHAFT)]
for n in NODES:
    prof += [(n - .012, R_SHAFT), (n - .004, R_SHAFT * 1.10), (n, R_SHAFT * 1.02), (n + .004, R_SHAFT * 1.10), (n + .012, R_SHAFT)]
prof.append((SHAFT, R_SHAFT))
lathe(prof, SEG_SHAFT, 0)
lathe([(-.016, .0085), (-.012, .0195), (.0, .0205), (.022, .0205), (.026, .0178)], SEG_SHAFT, 1, cap0=True)
lathe([(SHAFT - .020, .0178), (SHAFT - .016, .0215), (SHAFT - .006, .0215), (SHAFT, .0235), (SHAFT + .006, .0215), (SHAFT + .018, .026 * KN),
       (SHAFT + .040, .0385 * KN), (SHAFT + .056, .0455 * KN), (SHAFT + .062, .0485 * KN), (SHAFT + NECK, .0485 * KN), (SHAFT + NECK, .0445 * KN)], 20, 1)


# ---- 붓털 ----
def hv(co, m):
    """붓털 꼭짓점. m = META(가닥 번호 · 가운데 선 점 · 방향 …)."""
    v = bm.verts.new(co); v[part] = 1; v[tval] = m['tg']; META.append(m); v[mid] = len(META) - 1; return v


def hface(vs, uvs, cols, mat=2):
    f = bm.faces.new(vs); f.material_index = mat; f.smooth = True
    for l, u, c in zip(f.loops, uvs, cols): l[uv].uv = u; l[col] = c
    return f


LEAN_A = rnd.random() * 2 * math.pi; LEAN = Vector((math.cos(LEAN_A), math.sin(LEAN_A), 0)) * LEAN_AMT      # 붓끝 전체가 한쪽으로 살짝 기운다(젖었다 마른 붓)
CLUMPS = [2 * math.pi * (j + rnd.uniform(-.25, .25)) / N_CLUMPS for j in range(N_CLUMPS)]                          # 퍼질 때 가닥이 뭉치는 무리
# 속심: 윤곽보다 조금 작은 어두운 덩어리(틈 사이로 비치는 속털). 뿌리 고리는 목(잔) 안을 막는다.
CSEG = 12; CR = 10; CORE_END = .70; CORE_SCALE = .76
crow = []
for k in range(CR):
    t = CORE_END * (k / (CR - 1)) ** .9; r = R_ROOT * (.985 if k == 0 else CORE_SCALE * envn(t) * (1 - .55 * sstep((t - .40) / .30))); z = Z_HEAD - .004 + (HEAD + .004) * t; ring = []
    for i in range(CSEG):
        a = 2 * math.pi * i / CSEG; d = Vector((math.cos(a), math.sin(a), 0))
        ring.append(hv(d * max(r, .0015) + Vector((0, 0, z)) + LEAN * t ** 2.4, dict(sid=-1, layer=-1, s=t / CORE_END, tg=t, c=Vector((0, 0, z)) + LEAN * t ** 2.4, d=d, td=Vector((-d.y, d.x, 0)), a=a, rand=0.0, core=True)))
    crow.append(ring)
ctip = hv(Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))) + LEAN * (CORE_END + .02) ** 2.4, dict(sid=-1, layer=-1, s=1.0, tg=CORE_END + .02, c=Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))) + LEAN * (CORE_END + .02) ** 2.4, d=Vector((0, 0, 0)), td=Vector((0, 0, 0)), a=0.0, rand=0.0, core=True))


def ccol(v): return (0.0, META[v[mid]]['s'], 0.0, wetA(META[v[mid]]['tg'], H_CORE))
def cuv(i, v): return ((.5 + (i % CSEG) * 0 + (i / CSEG) * (CORE_COLS - 1)) / TEX, inkv(META[v[mid]]['tg'], H_CORE))


for k in range(CR - 1):
    for i in range(CSEG):
        vs = (crow[k][i], crow[k][(i + 1) % CSEG], crow[k + 1][(i + 1) % CSEG], crow[k + 1][i])
        hface(vs, [cuv(i, vs[0]), cuv(i + 1, vs[1]), cuv(i + 1, vs[2]), cuv(i, vs[3])], [ccol(v) for v in vs])
for i in range(CSEG):
    vs = (crow[-1][i], crow[-1][(i + 1) % CSEG], ctip); hface(vs, [cuv(i, vs[0]), cuv(i + 1, vs[1]), cuv(i + .5, vs[2])], [ccol(v) for v in vs])

# 가닥: 1) 값만 먼저 정하고 2) 젖은 다발을 묶고 3) 꼴을 만든다
def wrap(x): return (x + math.pi) % (2 * math.pi) - math.pi


STRANDS = []; n_layers = len(COUNTS); R_TIP = .00030
for L, n in enumerate(COUNTS):
    wisp = L == n_layers - 1; fc = FRACS[L]; spacing = 2 * math.pi * fc * R_ROOT / n; phase = rnd.random() * 2 * math.pi
    stray_ids = set(rnd.sample(range(n), STRAYS[L]))
    for q in range(n):
        st = dict(sid=len(STRANDS), layer=L, rand=rnd.random(), stray=q in stray_ids, wisp=wisp, lock=None)
        a0 = (phase + 2 * math.pi * (q + rnd.uniform(-.45, .45)) / n) % (2 * math.pi)
        lenf = rnd.uniform(*LEN[L]) * (rnd.uniform(.93, .98) if st['stray'] else 1.0)
        r0 = .0011 * rnd.uniform(.75, 1.3) if wisp else spacing * THICK[L] * rnd.uniform(.6, 1.35)
        fj = min(fc + rnd.uniform(-JIT[L], JIT[L]), 1.0 - .5 * r0 / R_ROOT)
        ar = rnd.uniform(-.002, .003); at = rnd.uniform(-.008, .008); tw = rnd.uniform(-.38, .38)
        aw = rnd.uniform(.0015, .0045); wph = rnd.random() * 6.283; wf = rnd.uniform(.55, 1.15); at2 = .003 + rnd.uniform(-.007, .007)
        if st['stray']: ar = rnd.uniform(.005, .010); at = rnd.uniform(-.010, .010)
        if wisp: ar = rnd.uniform(.003, .008); aw = rnd.uniform(.001, .003); at = rnd.uniform(-.006, .006); at2 = 0.0
        st.update(a0=a0, lenf=lenf, r0=r0, fj=fj, ar=ar, at=at, tw=tw, aw=aw, wph=wph, wf=wf, at2=at2, lag=max(0.0, LAG[L] + rnd.uniform(-.12, .25)),
                  share=RING_SHARE[L] + rnd.uniform(-.08, .08) + (.08 if st['stray'] else 0), frj=rnd.uniform(-.3, .3), fan=rnd.uniform(-1, 1),
                  h=H_WET + rnd.uniform(-.055, .055), u0=(CORE_COLS + rnd.randrange(TEX - CORE_COLS - 8) + .5) / TEX)
        STRANDS.append(st)


def own(st, tg):
    """가닥 제 길(xy). 끝 40 % 에서 한쪽으로 완만하게 더 휜다. 잔털은 마른 뿌리 구간에서만 살짝 뜨고 젖은 구간에서는 몸통에 붙는다."""
    s = min(1.0, tg / st['lenf']); a = st['a0'] + st['tw'] * s; d = Vector((math.cos(a), math.sin(a), 0)); td = Vector((-d.y, d.x, 0)); tip = max(0.0, (s - .6) / .4)
    rad = st['ar'] * math.sin(math.pi * min(1.0, tg / .45)) ** 2 if st['wisp'] else st['ar'] * s ** 1.6 * ((1 - .5 * s) if st['stray'] else 1)
    return (d * (st['fj'] * R_ROOT * .97 * envn(tg) * ((1 - .06 * sstep((tg - .3) / .2)) if st['wisp'] else 1) + rad)
            + td * (st['at'] * s ** 2 + st['aw'] * s ** 1.5 * math.sin(st['wph'] + 6.283 * st['wf'] * s) + st['at2'] * tip ** 2) + LEAN * tg ** 2.4)


# 젖은 다발: 각도 순으로 이웃한 3–6가닥을 묶는다(사이에 낱가닥을 몇 개 남긴다). 뿌리는 낱가닥, 끝으로 갈수록 가운데 가닥(leader)의 길에 붙는다.
LOCKS = []


def make_locks(pool, lo, hi, g0, g1, kind):
    pool = sorted(pool, key=lambda x: x['a0']); i = rnd.randint(0, 2)
    while i + lo <= len(pool):
        mem = pool[i:i + rnd.randint(lo, hi)]; lead = mem[len(mem) // 2]
        lk = dict(id=len(LOCKS), kind=kind, leader=lead, members=mem, s0=rnd.uniform(.38, .46), h=H_WET + rnd.uniform(-.055, .055), fan=rnd.uniform(-1, 1))
        lead['at2'] = rnd.choice((-1, 1)) * rnd.uniform(.005, .011)
        for x in mem:
            x['lock'] = lk
            if x is not lead: x['lenf'] = lead['lenf'] * rnd.uniform(.975, 1.0)
            x.update(wa0=lead['a0'], lag=lead['lag'], share=lead['share'], frj=lead['frj'], fan=lk['fan'] + rnd.uniform(-.15, .15), h=lk['h'] + rnd.uniform(-.03, .03))
        LOCKS.append(lk); i += len(mem) + rnd.randint(g0, g1)


make_locks([x for x in STRANDS if x['layer'] == 2 and not x['stray']], 3, 5, 2, 4, 'mid')
make_locks([x for x in STRANDS if x['layer'] in (3, 4) and not x['stray']], 3, 6, 3, 5, 'outer')
CENTRE = dict(id=-1, kind='centre', leader=None, members=[x for x in STRANDS if x['layer'] < 2], s0=.42, h=H_WET - .03, fan=0.0)      # 안쪽 두 겹 = 붓끝
for i, x in enumerate(CENTRE['members']):
    x['lock'] = CENTRE; x['lenf'] = 1.0 if i == 0 else rnd.uniform(.975, 1.0); x.update(lag=.03, share=.08, frj=0.0, fan=rnd.uniform(-.2, .2), h=CENTRE['h'] + rnd.uniform(-.03, .03))


def lock_path(lk, tg): return LEAN * tg ** 2.4 if lk['leader'] is None else own(lk['leader'], min(tg, lk['leader']['lenf']))


def center(st, s):
    tg = s * st['lenf']; p = own(st, tg); lk = st['lock']
    if lk: p = p.lerp(lock_path(lk, tg), sstep((s - lk['s0']) / (.97 - lk['s0'])))
    if not st['wisp']: p = p.lerp(LEAN * tg ** 2.4, .85 * sstep((tg - .68) / .28))      # 젖은 붓끝: 바깥 다발도 끝에서는 가운데로 빨려 든다(따로 선 가시가 아니다)
    return p


# 퍼짐 방향: 다발 단위(낱가닥은 가까운 다발을 따른다). 다발은 일곱 구역으로 조금 당겨져 몇 덩이로 갈라진다.
SECT_AMP = [rnd.uniform(.65, 1.35) for _ in CLUMPS]
for lk in LOCKS:
    a = lk['leader']['a0']; j = min(range(N_CLUMPS), key=lambda j: abs(wrap(CLUMPS[j] - a)))
    lk['sp_ang'] = a + .45 * wrap(CLUMPS[j] - a); lk['sp_amp'] = (.046 if lk['kind'] == 'outer' else .018) * SECT_AMP[j] * rnd.uniform(.85, 1.15)
for st in STRANDS:
    lk = st['lock']; mult = 1.0
    if lk is CENTRE: st['sp_ang'] = st['a0']; st['sp_amp'] = .003; continue
    if lk is None:
        kind = 'mid' if st['layer'] == 2 else 'outer'
        lk = min((x for x in LOCKS if x['kind'] == kind), key=lambda x: abs(wrap(x['leader']['a0'] - st['a0']))); mult = .9 * rnd.uniform(.85, 1.1)
    st['sp_ang'] = lk['sp_ang']; st['sp_amp'] = lk['sp_amp'] * mult

for st in STRANDS:
    L = st['layer']; wisp = st['wisp']; NRL = RINGS[L]; sides = SIDES[L]; sec_phase = rnd.random() * 2 * math.pi; rows = []
    for k in range(NRL + 1):
        s = k / NRL; tg = s * st['lenf']; z = Z_HEAD - .006 + HEAD * tg + .006 * s
        a = st['a0'] + st['tw'] * s; d = Vector((math.cos(a), math.sin(a), 0)); td = Vector((-d.y, d.x, 0)); A = wetA(tg, st['h'])
        c = center(st, s) + Vector((0, 0, z))
        m = dict(sid=st['sid'], layer=L, s=s, tg=tg, c=c, d=d, td=td, a=st['a0'], rand=st['rand'], core=False, st=st, wet=A)
        if k == NRL: rows.append([hv(c, m)]); st['tip'] = m; break
        rt = R_TIP + (st['r0'] * envn(tg) ** .55 * (1 + .30 * A) - R_TIP) * (1 - s ** 2.4)       # 젖은 구간은 먹을 머금어 조금 굵고, 끝에서만 가늘어진다
        fr_, ft_ = ((.85, 1.15) if (L < 2 or wisp) else (.62, 1.55)); sp = math.pi / 4 if sides == 4 else sec_phase
        rows.append([hv(c + d * (rt * fr_ * math.cos(sp + 2 * math.pi * j / sides)) + td * (rt * ft_ * math.sin(sp + 2 * math.pi * j / sides)), m) for j in range(sides)])
    cc = lambda v: (st['rand'], META[v[mid]]['s'], (L + 1) / n_layers, META[v[mid]]['wet'])
    uu = lambda j, v: (st['u0'] + (j / sides) * (3 if wisp else 6) / TEX, inkv(META[v[mid]]['tg'], st['h']))
    for k in range(NRL - 1):
        for j in range(sides):
            vs = (rows[k][j], rows[k][(j + 1) % sides], rows[k + 1][(j + 1) % sides], rows[k + 1][j])
            hface(vs, [uu(j, vs[0]), uu(j + 1, vs[1]), uu(j + 1, vs[2]), uu(j, vs[3])], [cc(v) for v in vs])
    for j in range(sides):
        vs = (rows[NRL - 1][j], rows[NRL - 1][(j + 1) % sides], rows[NRL][0]); hface(vs, [uu(j, vs[0]), uu(j + 1, vs[1]), uu(j + .5, vs[2])], [cc(v) for v in vs])


TIP_META = CENTRE['members'][0]['tip']; TIP_XY = Vector((TIP_META['c'].x, TIP_META['c'].y, 0))


# ---- 꼬리 고리 + 술 (1차 그대로) ----
def torus(center, R, r, seg, sides, mat):
    grid = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; row = []
        for j in range(sides):
            b = 2 * math.pi * j / sides; d = R + r * math.cos(b)
            row.append(bm.verts.new(Vector(center) + Vector((r * math.sin(b), d * math.cos(a), d * math.sin(a)))))
        grid.append(row)
    for i in range(seg):
        for j in range(sides):
            f = bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % sides], grid[i][(j + 1) % sides])); f.material_index = mat; f.smooth = True


torus((0, 0, -.034), .020, .0032, 14, 6, 1)
zc = -.054
lathe([(zc - .060, .0030), (zc, .0030)], 6, 3)
lathe([(zc - .084, .0105), (zc - .078, .0125), (zc - .066, .0115), (zc - .060, .0050)], 8, 3, cap1=True)
lathe([(zc - .215, .0175), (zc - .180, .0150), (zc - .120, .0118), (zc - .084, .0102)], 10, 3, cap0=True, wobble=lambda i, t: 1 + (.10 if i % 2 else -.10))
lathe([(zc - .096, .0126), (zc - .090, .0126)], 8, 1)

bm.normal_update()
SHIFT = Vector((0, 0, Z_GRIP))
for v in bm.verts: v.co.z -= Z_GRIP          # 원점 = GripSocket
for f in bm.faces:
    if f.material_index != 2:
        for l in f.loops: l[col] = (0, 0, 0, 0)
me = bpy.data.meshes.new('SM_GreatBrush308')
vmeta = [META[v[mid]] if v[part] == 1 else None for v in bm.verts]
bm.to_mesh(me); bm.free()

# ---- 붓털 그림: 가로 = 털 한 올(가닥마다 7칸을 쓴다), 세로 = 뿌리(밝은 털) → 먹 먹은 끝. 칸마다 먹이 오른 높이 · 밝기가 다르다 ----
trnd = random.Random(508); px = [0.0] * (TEX * TEX * 4)       # v5: 세로 = 먹 양(0 마른 털 → 묽은 먹 띠 → 먹). 경계 높이는 가닥마다 UV 로 정한다
shade = [.80 + trnd.random() * .34 for _ in range(TEX)]; wet = [trnd.random() for _ in range(TEX)]; BAND = Vector((.115, .098, .085))
for y in range(TEX):
    v = y / (TEX - 1)
    for x in range(TEX):
        sh_ = .62 if x < CORE_COLS else shade[x]
        dry = Vector(ROOT_COL) * sh_ * (1 - .30 * sstep((v - .10) / .30))
        ink = Vector(INK_COL) * (.55 + (0 if x < CORE_COLS else 1.5 * wet[x] ** 3))
        c = dry.lerp(BAND * (.8 + .5 * (sh_ - .8)), sstep((v - .34) / .15)).lerp(ink, sstep((v - .50) / .16))
        i = (y * TEX + x) * 4; px[i:i + 4] = (c.x, c.y, c.z, 1)
tex = bpy.data.images.new('T_GreatBrush308_Bristle', TEX, TEX); tex.pixels = px; tex.filepath_raw = os.path.join(OUT, 'T_GreatBrush308_Bristle.png'); tex.file_format = 'PNG'; tex.save()
for name in MATS:
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); c, rough, metal = COLORS[name]
    b.inputs['Base Color'].default_value = (*c, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*c, 1); m.roughness = rough; m.metallic = metal
    if name == 'M_GreatBrush_Bristle':
        tn = m.node_tree.nodes.new('ShaderNodeTexImage'); tn.image = tex; tn.interpolation = 'Closest'; m.node_tree.links.new(tn.outputs['Color'], b.inputs['Base Color']); m.node_tree.nodes.active = tn
    me.materials.append(m)
ob = bpy.data.objects.new('SM_GreatBrush308', me); bpy.context.scene.collection.objects.link(ob)

# ---- 뼈대 ----
z0 = -Z_GRIP; z_head = Z_HEAD - Z_GRIP; seg = HEAD / N_BONES; rseg = HEAD / RING_BONES
arm = bpy.data.armatures.new('GreatBrush308_Rig'); rig = bpy.data.objects.new('GreatBrush308_Rig', arm); bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
hb = arm.edit_bones.new('BrushHandle'); hb.head = (0, 0, z0); hb.tail = (0, 0, z_head); hb.roll = 0
prev = hb; first = None
for i in range(N_BONES):
    b = arm.edit_bones.new('Bristle_%02d' % (i + 1)); b.head = (0, 0, z_head + seg * i); b.tail = (0, 0, z_head + seg * (i + 1)); b.roll = 0
    b.parent = prev; b.use_connect = True; prev = b; first = first or b
for r in range(N_RING):      # 고리 보조 사슬: 60도 간격, _01 은 가운데 사슬 뿌리(Bristle_01)를 따라간다(연결하지 않는다)
    a = 2 * math.pi * r / N_RING; x, y = math.cos(a) * RING_RADIUS, math.sin(a) * RING_RADIUS; prev = first
    for j in range(RING_BONES):
        b = arm.edit_bones.new('BristleRing_%s_%02d' % (RING_NAMES[r], j + 1)); b.head = (x, y, z_head + rseg * j); b.tail = (x, y, z_head + rseg * (j + 1)); b.roll = 0
        b.parent = prev; b.use_connect = j > 0; prev = b
bpy.ops.object.mode_set(mode='OBJECT')
BONES = [b.name for b in arm.bones]
groups = {n: ob.vertex_groups.new(name=n) for n in BONES}


def weights(m):
    """가닥 가운데 선 점으로 계산해 단면 꼭짓점이 같이 쓴다(관이 찌그러지지 않게). 영향 뼈 ≤ 4."""
    zrel = m['c'].z - Z_HEAD; st = m.get('st')
    if zrel <= 1e-5: return {'BrushHandle': 1.0}
    zl = HEAD * min(1.0, zrel / HEAD) ** (1 + (st['lag'] if st else 0.0))      # 바깥 가닥일수록 끝 뼈를 늦게 따른다
    x = zl / seg - .5; W = {}
    if x <= 0: w1 = sstep(zl / (seg * .5)); W['Bristle_01'] = w1; W['BrushHandle'] = 1 - w1
    else:
        i = min(N_BONES - 1, int(math.floor(x)))
        if i >= N_BONES - 1: W['Bristle_%02d' % N_BONES] = 1.0
        else: f = sstep(x - i); W['Bristle_%02d' % (i + 1)] = 1 - f; W['Bristle_%02d' % (i + 2)] = f
    if st:
        share = min(.78, max(0.0, st['share'])) * sstep(m['tg'] / .18)
        ang = (st.get('wa0', st['a0']) % (2 * math.pi)) / (2 * math.pi / N_RING); k0 = int(math.floor(ang)) % N_RING; fr = min(1.0, max(0.0, ang - math.floor(ang) + st['frj']))      # 이웃 가닥끼리도 두 고리 사슬 몫이 다르다
        j = min(RING_BONES - 1, int(zrel / rseg))
        W = {n: w * (1 - share) for n, w in W.items()}
        W['BristleRing_%s_%02d' % (RING_NAMES[k0], j + 1)] = share * (1 - fr); W['BristleRing_%s_%02d' % (RING_NAMES[(k0 + 1) % N_RING], j + 1)] = share * fr
    W = {n: w for n, w in W.items() if w > .004}; t = sum(W.values()); return {n: w / t for n, w in W.items()}


max_inf = 0
for v, m in zip(me.vertices, vmeta):
    W = {'BrushHandle': 1.0} if m is None else weights(m); max_inf = max(max_inf, len(W))
    for n, w in W.items(): groups[n].add([v.index], w, 'REPLACE')
mod = ob.modifiers.new('Armature', 'ARMATURE'); mod.object = rig; ob.parent = rig

# ---- 블렌드셰이프(가닥 단위: 가운데 선을 옮기고 단면은 그대로 따라간다) ----
ob.shape_key_add(name='Basis')
splay = ob.shape_key_add(name='BristleSplay'); press = ob.shape_key_add(name='BristlePress')
for v, m in zip(me.vertices, vmeta):
    if m is None: continue
    c = m['c'] - SHIFT; o = v.co - c; st = m.get('st'); tg = m['tg']; s = m['s']
    # 퍼짐: 뿌리는 그대로, 가닥마다 다른 양으로 부채처럼 벌어진다(바깥 겹일수록 많이) — 속심은 남아 틈 사이로 어둡게 보인다
    if st:      # v5: 다발째로 바깥으로 휘며 갈라진다(다발 안의 가닥은 같은 방향 · 같은 양)
        k = max(0.0, (tg - .22) / .78); kc = k ** 2.0
        cs = c + Vector((math.cos(st['sp_ang']), math.sin(st['sp_ang']), 0)) * (st['sp_amp'] * kc); cs.z -= st['sp_amp'] * .5 * kc
        splay.data[v.index].co = cs + o
    else:
        k = sstep((tg - .35) / .6); q = v.co.copy(); q.z -= .11 * k * tg; q.x *= 1 - .55 * k; q.y *= 1 - .55 * k; splay.data[v.index].co = q
    # 눌림: 로컬 Y 로 납작해지고(면에 눕는다) 로컬 X 로 옆으로 퍼진다. 사슬은 로컬 X 축으로 꺾어 쓴다.
    k = sstep((tg - .12) / .60); fan = (st['fan'] + .6 * math.copysign(1, math.cos(st['a0']))) if st else 0.0
    cp = c.copy(); cp.y *= 1 - (.74 if st else .90) * k; cp.x = cp.x * (1 + (1.0 if st else .5) * k) + fan * (.018 * k + .030 * k * tg); cp.z -= (.030 if st else .12) * k * tg
    if st: cp.y += (st['rand'] - .5) * .006 * k
    op = o.copy(); op.y *= 1 - .45 * k
    press.data[v.index].co = cp + op


splay.value = 0; press.value = 0      # Blender 5 는 새 키를 1.0 으로 만든다 — 0 으로 내보낸다(1차 · 2차 FBX 는 1.0 으로 들어 있다)


# ---- 소켓 (1차 그대로) ----
def socket(name, world, bone=None):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = .06
    bpy.context.scene.collection.objects.link(e); e.parent = rig
    if bone: e.parent_type = 'BONE'; e.parent_bone = bone
    bpy.context.view_layer.update()
    e.matrix_world = Matrix.Translation(world) @ Matrix.Rotation(math.radians(90), 4, 'X')
    return e


socket('GripSocket', Vector((0, 0, 0)))
socket('GripSocketL', Vector((0, 0, Z_GRIP_L - Z_GRIP)))
socket('ShaftEndSocket', Vector((0, 0, z0)))
socket('TipSocket', TIP_XY + Vector((0, 0, TOTAL - Z_GRIP)), 'Bristle_%02d' % N_BONES)      # 실제 털끝(가운데 다발 끝)
bpy.context.view_layer.update()

me.calc_loop_triangles(); tris = len(me.loop_triangles)
head_tris = sum(1 for t in me.loop_triangles if t.material_index == 2)
if tris > TRI_BUDGET: raise RuntimeError('triangle budget %d' % tris)
zs = [v.co.z for v in me.vertices]
cols = me.color_attributes['Col']; hl = [l.index for p in me.polygons if p.material_index == 2 for l in [me.loops[i] for i in p.loop_indices]]
g = [cols.data[i].color[1] for i in hl]; r = [cols.data[i].color[0] for i in hl]; a_ = [cols.data[i].color[3] for i in hl]
blend = os.path.join(OUT, 'SM_GreatBrush308.blend'); fbx = os.path.join(OUT, 'SM_GreatBrush308.fbx')
bpy.ops.wm.save_as_mainfile(filepath=blend)
for o in bpy.context.scene.objects: o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
                         bake_anim=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False, path_mode='COPY', embed_textures=True,
                         colors_type='LINEAR')
report = {'version': 'v5', 'triangles': tris, 'bristle_triangles': head_tris, 'vertices': len(me.vertices), 'strands': len(STRANDS), 'strands_per_layer': COUNTS, 'stray_strands': sum(1 for s in STRANDS if s['stray']),
          'wet_locks': len(LOCKS), 'wet_lock_sizes': [len(lk['members']) for lk in LOCKS], 'wet_lock_strands': sum(len(lk['members']) for lk in LOCKS), 'centre_lock_strands': len(CENTRE['members']),
          'tip_socket_offset_from_axis_m': TIP_XY.length,
          'strand_rings': RINGS, 'strand_sides': SIDES, 'wisp_strands': COUNTS[-1], 'tip_lean_m': LEAN_AMT, 'splay_clumps': N_CLUMPS,
          'strand_root_radius_mean_m': sum(x['r0'] for x in STRANDS if not x['wisp']) / sum(1 for x in STRANDS if not x['wisp']), 'seed': SEED, 'max_bone_influences': max_inf,
          'shaft_to_tip_m': TOTAL, 'mesh_z_min': min(zs), 'mesh_z_max': max(zs), 'tassel_below_tail_m': -(min(zs) - z0), 'shaft_m': SHAFT, 'neck_m': NECK, 'head_m': HEAD,
          'shaft_diameter_m': R_SHAFT * 2, 'bristle_root_diameter_m': R_ROOT * 2, 'grip_from_ferrule_m': GRIP_FROM_FERRULE, 'hand_gap_m': HAND_GAP,
          'bones': BONES, 'sockets': ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket'], 'shape_keys': [k.name for k in me.shape_keys.key_blocks], 'materials': MATS,
          'vertex_color': {'attribute': 'Col', 'type': 'float linear, face corner', 'R': 'strand random 0-1 (core 0)', 'G': 'root->tip 0-1', 'B': 'layer (0 core .. 1 outermost wisps)', 'A': 'wetness 0-1 (ink line height differs per strand / lock)', 'non_bristle': '(0,0,0,0)',
                           'bristle_a_mean': sum(a_) / len(a_), 'bristle_a_min': min(a_), 'bristle_a_max': max(a_),
                           'bristle_g_mean': sum(g) / len(g), 'bristle_g_max': max(g), 'bristle_r_distinct': len(set(round(x, 4) for x in r))},
          'ring_chains': 'BristleRing_A..F_01..04 at 60 deg, radius %.3f m; _01 parent = Bristle_01 (not connected)' % RING_RADIUS,
          'press_axis': 'BristlePress flattens along local Y (Blender) and spreads along local X; bend the chains about local X',
          'axis_contract': 'Brush points Blender +Z / Unity +Y. Origin = GripSocket. Socket local +Y points toward tip.'}
json.dump(report, open(os.path.join(OUT, 'build-report.json'), 'w', encoding='utf-8'), indent=1)
print('BUILD', json.dumps(report)); print('BUILD DONE')
