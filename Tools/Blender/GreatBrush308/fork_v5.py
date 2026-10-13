"""#308 GreatBrush v5: build_v4.py 에서 build_v5.py · verify_v5.py · probe_v5.py · close_v5.py · run_v5.sh 를 갈라 만든다(v3 · v4 스크립트는 읽기만 한다).
    python fork_v5.py      # 이 폴더에서. 조정 값은 여기에 있다 — build_v5.py 를 손으로 고치면 덮어쓰인다.
v5 = 조금 더 부드럽게 + 먹물에 젖은 느낌(2026-10-13 사용자 요청): 젖은 다발 · 먹방울 · 젖음 채널(Col.A) · 붓끝에 맞춘 TipSocket."""
import os, re
HERE = os.path.dirname(os.path.abspath(__file__)); os.chdir(HERE)
Q = '"' * 3


def patcher(text):
    box = [text]

    def rep(a, b, n=1):
        assert box[0].count(a) == n, (box[0].count(a), a); box[0] = box[0].replace(a, b)

    def cut(a, b, new):
        i = box[0].index(a); j = box[0].index(b, i); box[0] = box[0][:i] + new + box[0][j:]
    return box, rep, cut


# ================= build =================
box, rep, cut = patcher(open('build_v4.py', encoding='utf-8').read())
box[0] = box[0][box[0].index('import bpy, bmesh'):]
box[0] = Q + """#308 GreatBrush v5: v4 를 조금 더 부드럽게 + 먹물에 젖은 붓으로(2026-10-13 사용자 요청).
    blender -b --factory-startup -P build_v5.py -- <out_dir> [--counts 14,24,36,50,56,24] [--seed 5083]
비례 · 소켓 이름 · 뼈 이름 · 셰이프 키 이름 · 꼭짓점 색 R/G/B 는 v4 그대로. fork_v5.py 가 build_v4.py 에서 갈라 만든다.
젖은 다발: 끝 55–60 % 구간에서 이웃 가닥 3–6개가 한 점으로 모여 붙는다(바깥 두 겹 + 셋째 겹). 안쪽 두 겹은 가운데 다발 = 붓끝.
TipSocket = 실제 털끝(축에서 LEAN_AMT 만큼 벗어난다).
꼭짓점 색 Col(선형 float): R = 가닥 난수, G = 뿌리→끝, B = 겹(0 = 속심, 1 = 잔털), A = 젖음 0–1(먹이 오른 높이는 가닥 · 다발마다 다르다). 붓털이 아닌 곳은 (0,0,0,0).
UV: u = 털 올(가닥마다 6칸), v = 먹 양(0 마른 털 → 0.5 묽은 먹 띠 → 1 먹). 뿌리→끝은 G 에 있다.""" + Q + "\n" + box[0]
rep("SEED = int(arg('--seed', 4083))", "SEED = int(arg('--seed', 5083))")
rep("RINGS = [6, 6, 8, 12, 12, 8]", "RINGS = [8, 8, 8, 12, 12, 8]")
rep("LEAN_AMT = .011", "LEAN_AMT = .015")
rep("CORE_END = .87; CORE_SCALE = .76", "CORE_END = .70; CORE_SCALE = .76")
rep("CORE_SCALE * envn(t)); z", "CORE_SCALE * envn(t) * (1 - .55 * sstep((t - .40) / .30))); z")
rep("(.91, .985), (.88, .97), (.84, .95), (.58, .88)]", "(.93, .99), (.92, .985), (.89, .975), (.58, .88)]")
rep("STRAYS = [0, 0, 0, 3, 5, 0]", "STRAYS = [0, 0, 0, 1, 3, 0]")
rep("TEX = 256; CORE_COLS = 8", "TEX = 256; CORE_COLS = 8; H_WET = .30; H_CORE = .25")
rep("    t = min(1.0, max(0.0, t)); return (1 + .10 * math.sin(math.pi * min(t / .55, 1.0))) * max(0.0, 1 - t ** 2.1) ** .82",
    "    # v5: 배가 조금 더 통통하고(+15 %), 끝으로 오목하게 빨려 들어간다(곧은 원뿔이 아니다)\n"
    "    t = min(1.0, max(0.0, t)); return (1 + .22 * math.sin(math.pi * min(t / .62, 1.0))) * max(0.0, 1 - t ** 1.9) ** 1.1\n\n\n"
    "def wetA(tg, h): return sstep((tg - (h - .06)) / .12)                  # 젖음 0–1 (h = 그 가닥에 먹이 오른 높이)\n"
    "def inkv(tg, h): return min(.998, max(.002, .5 + (tg - h) * 1.5))      # 그림의 세로 = 먹 양")
rep("def hface(vs, uvs, cols):\n    f = bm.faces.new(vs); f.material_index = 2; f.smooth = True",
    "def hface(vs, uvs, cols, mat=2):\n    f = bm.faces.new(vs); f.material_index = mat; f.smooth = True")
rep("def ccol(v): return (0.0, META[v[mid]]['s'], 0.0, 1.0)", "def ccol(v): return (0.0, META[v[mid]]['s'], 0.0, wetA(META[v[mid]]['tg'], H_CORE))")
rep("(i / CSEG) * (CORE_COLS - 1)) / TEX, min(.999, META[v[mid]]['s']))", "(i / CSEG) * (CORE_COLS - 1)) / TEX, inkv(META[v[mid]]['tg'], H_CORE))")
cut("# 가닥 다발", "# ---- 꼬리 고리", """# 가닥: 1) 값만 먼저 정하고 2) 젖은 다발을 묶고 3) 꼴을 만든다
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
    \"\"\"가닥 제 길(xy). 끝 40 % 에서 한쪽으로 완만하게 더 휜다. 잔털은 마른 뿌리 구간에서만 살짝 뜨고 젖은 구간에서는 몸통에 붙는다.\"\"\"
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


""")
rep("    if f.material_index != 2:\n        for l in f.loops: l[col] = (0, 0, 0, 1)", "    if f.material_index != 2:\n        for l in f.loops: l[col] = (0, 0, 0, 0)")
cut("trnd = random.Random(308)", "tex = bpy.data.images.new", """trnd = random.Random(508); px = [0.0] * (TEX * TEX * 4)       # v5: 세로 = 먹 양(0 마른 털 → 묽은 먹 띠 → 먹). 경계 높이는 가닥마다 UV 로 정한다
shade = [.80 + trnd.random() * .34 for _ in range(TEX)]; wet = [trnd.random() for _ in range(TEX)]; BAND = Vector((.115, .098, .085))
for y in range(TEX):
    v = y / (TEX - 1)
    for x in range(TEX):
        sh_ = .62 if x < CORE_COLS else shade[x]
        dry = Vector(ROOT_COL) * sh_ * (1 - .30 * sstep((v - .10) / .30))
        ink = Vector(INK_COL) * (.55 + (0 if x < CORE_COLS else 1.5 * wet[x] ** 3))
        c = dry.lerp(BAND * (.8 + .5 * (sh_ - .8)), sstep((v - .34) / .15)).lerp(ink, sstep((v - .50) / .16))
        i = (y * TEX + x) * 4; px[i:i + 4] = (c.x, c.y, c.z, 1)
""")
rep("ang = (st['a0'] % (2 * math.pi))", "ang = (st.get('wa0', st['a0']) % (2 * math.pi))")
cut("    if st:\n        fl = .74 if st['wisp']", "    else:\n        k = sstep((tg - .35) / .6)", """    if st:      # v5: 다발째로 바깥으로 휘며 갈라진다(다발 안의 가닥은 같은 방향 · 같은 양)
        k = max(0.0, (tg - .22) / .78); kc = k ** 2.0
        cs = c + Vector((math.cos(st['sp_ang']), math.sin(st['sp_ang']), 0)) * (st['sp_amp'] * kc); cs.z -= st['sp_amp'] * .5 * kc
        splay.data[v.index].co = cs + o
""")
rep("socket('TipSocket', Vector((0, 0, TOTAL - Z_GRIP)), 'Bristle_%02d' % N_BONES)", "socket('TipSocket', TIP_XY + Vector((0, 0, TOTAL - Z_GRIP)), 'Bristle_%02d' % N_BONES)      # 실제 털끝(가운데 다발 끝)")
rep("r = [cols.data[i].color[0] for i in hl]", "r = [cols.data[i].color[0] for i in hl]; a_ = [cols.data[i].color[3] for i in hl]")
rep("'version': 'v4'", "'version': 'v5'")
rep("'strand_rings': RINGS,", "'wet_locks': len(LOCKS), 'wet_lock_sizes': [len(lk['members']) for lk in LOCKS], 'wet_lock_strands': sum(len(lk['members']) for lk in LOCKS), 'centre_lock_strands': len(CENTRE['members']),\n"
    "          'tip_socket_offset_from_axis_m': TIP_XY.length,\n"
    "          'strand_rings': RINGS,")
rep("'non_bristle': '(0,0,0)',", "'A': 'wetness 0-1 (ink line height differs per strand / lock)', 'non_bristle': '(0,0,0,0)',\n"
    "                           'bristle_a_mean': sum(a_) / len(a_), 'bristle_a_min': min(a_), 'bristle_a_max': max(a_),")
assert "m['lean']" not in box[0]
open('build_v5.py', 'w', encoding='utf-8').write(box[0])

# ================= verify =================
box, rep, cut = patcher(open('verify_v4.py', encoding='utf-8').read().replace('v4', 'v5'))
rep("bi = mats.index('M_GreatBrush_Bristle'); R = []; G = []; other = 0.0", "bi = mats.index('M_GreatBrush_Bristle'); R = []; G = []; A = []; other = 0.0")
rep("            if p.material_index == bi: R.append(c[0]); G.append(c[1])\n            else: other = max(other, c[0], c[1], c[2])",
    "            if p.material_index == bi: R.append(c[0]); G.append(c[1]); A.append(c[3])\n            else: other = max(other, c[0], c[1], c[2], c[3])")
rep("'build_g_mean': REP['vertex_color']['bristle_g_mean']}", "'build_g_mean': REP['vertex_color']['bristle_g_mean'],\n"
    "               'a_mean': sum(A) / len(A), 'a_min': min(A), 'a_max': max(A), 'build_a_mean': REP['vertex_color']['bristle_a_mean']}\n"
    "    need(abs(colinfo['a_mean'] - REP['vertex_color']['bristle_a_mean']) < .01 and colinfo['a_min'] < .01 and colinfo['a_max'] > .99, 'wetness alpha %s' % colinfo)\n"
    "")
rep("r = {'fbx': fbx,", "ax_ = (g - end).normalized(); tip_off = ((tip - end) - ax_ * (tip - end).dot(ax_)).length\n"
    "need(abs(tip_off - REP['tip_socket_offset_from_axis_m']) < 5e-4, 'tip offset %.4f' % tip_off)\n"
    "r = {'fbx': fbx, 'tip_socket_offset_from_axis_m': tip_off,")
open('verify_v5.py', 'w', encoding='utf-8').write(box[0])
open('probe_v5.py', 'w', encoding='utf-8').write(open('probe_v4.py', encoding='utf-8').read().replace('v4', 'v5'))
open('run_v5.sh', 'w', encoding='utf-8', newline='\n').write(open('run_v4.sh', encoding='utf-8').read().replace('v4', 'v5'))

# ================= close =================
box, rep, cut = patcher(open('close_v4.py', encoding='utf-8').read())
rep("close_v4.py", "close_v5.py"); rep("GreatBrush v4", "GreatBrush v5"); rep("<v4 out_dir>", "<v5 out_dir> [--light studio.sl]")
rep("vs_rest · vs_bend(v3 / v4)", "vs_rest · vs_tip(v4 / v5)")
rep("V3 = os.path.join(os.path.dirname(V1), 'v3', 'SM_GreatBrush308.blend')", "V4 = os.path.join(os.path.dirname(V1), 'v4', 'SM_GreatBrush308.blend')")
rep("s = bpy.context.scene; s.render.engine", """# 렌더 전용: 젖은 구간(Col.A > 0.03)의 면을 윤나는 재질 사본으로 나눈다. 내보낸 FBX 는 붓털 재질 하나 그대로다 — 게임에서는 셰이더가 A 로 윤기를 낸다.
def wet_split(ob_):
    me_ = ob_.data; ca = me_.color_attributes.get('Col'); bi = next(i for i, m in enumerate(me_.materials) if m.name.startswith('M_GreatBrush_Bristle'))
    base = me_.materials[bi]; base.roughness = .92; base.metallic = 0
    wm = base.copy(); wm.name = 'R_BristleWet'; wm.roughness = WET_ROUGH; wm.metallic = WET_METAL; me_.materials.append(wm); wi = len(me_.materials) - 1
    for p in me_.polygons:
        if p.material_index == bi and sum(ca.data[li].color[3] for li in p.loop_indices) / p.loop_total > .03: p.material_index = wi


WET_ROUGH = float(ARGS[ARGS.index('--rough') + 1]) if '--rough' in ARGS else .26; WET_METAL = float(ARGS[ARGS.index('--metal') + 1]) if '--metal' in ARGS else .0
wet_split(brush)
s = bpy.context.scene; s.render.engine""")
rep("sh.light = 'STUDIO';", "sh.light = 'STUDIO'; sh.studio_light = ARGS[ARGS.index('--light') + 1] if '--light' in ARGS else sh.studio_light;")
cut("UP = Vector((0, 0, .27))", "print('CLOSE DONE')", """UP = Vector((0, 0, .27)); DIR = Vector((1, 0, .12)).normalized(); TIPW = Vector((0, 0, .45)) + DIR * .95
if not ONLY or 'compare' in ONLY:
    new, rig1, k1 = load(V1)
    place(REST); rig1.matrix_world = frame(Vector((0, 0, .72)), (1, 0, .12)); bpy.context.view_layer.update()
    look(HEADC + Vector((.10, -1.02, .30)), HEADC + Vector((.0, 0, .13)), 30); shot('compare'); hide(new)
if not ONLY or 'vs_rest' in ONLY or 'vs_tip' in ONLY:
    new, rig4, k4 = load(V4)
    place(REST); place(frame(Vector((0, 0, .45)) + UP, (1, 0, .12)), r=rig4, k=k4)
    look(HEADC + Vector((.06, -.86, .26)), HEADC + Vector((.0, 0, .135)), 30); shot('vs_rest')
    place(frame(Vector((0, 0, .45)) + Vector((0, 0, .105)), (1, 0, .12)), r=rig4, k=k4)
    look(TIPW + Vector((.0, -.44, .12)), TIPW + Vector((-.07, 0, .05)), 30); shot('vs_tip'); hide(new)
""")
open('close_v5.py', 'w', encoding='utf-8').write(box[0])
print('ok')
