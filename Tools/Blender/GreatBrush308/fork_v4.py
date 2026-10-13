"""#308 GreatBrush v4: build_v3.py 에서 build_v4.py · verify_v4.py · probe_v4.py · run_v4.sh 를 갈라 만든다(한 번 쓰는 도구 — v3 스크립트는 읽기만 한다).
    python fork_v4.py      # 이 폴더에서
build_v4.py 를 손으로 고친 뒤 다시 돌리면 덮어쓴다 — 조정은 여기 값에서 한다."""
import os, re
HERE = os.path.dirname(os.path.abspath(__file__)); os.chdir(HERE)
s = open('build_v3.py', encoding='utf-8').read()


def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (s.count(a), a); s = s.replace(a, b)


def cut(a, b, new):
    global s
    i = s.index(a); j = s.index(b, i); s = s[:i] + new + s[j:]


Q = '"' * 3
s = s[s.index('import bpy, bmesh'):]
s = Q + """#308 GreatBrush v4: v3(1차 비례 + 가닥 붓털)를 더 세밀하고 약간만 더 부드럽게(2026-10-13 사용자 요청).
    blender -b --factory-startup -P build_v4.py -- <out_dir> [--counts 14,24,36,50,56,24] [--seed 4083]
비례 · 소켓 · 뼈 이름 · 셰이프 키 이름 · 꼭짓점 색 채널은 v3 그대로. fork_v4.py 가 build_v3.py 에서 갈라 만든다(v3 파일은 건드리지 않는다).
가닥 = 다섯 겹 + 맨 바깥 잔털 한 겹(마지막 겹). 안쪽 겹은 3각 단면 · 적은 마디, 바깥 두 겹에 마디를 쓴다. 가닥 뿌리 굵기는 v3 의 약 60 %.
끝으로 갈수록 완만한 S자/한쪽 휨, 붓끝 전체가 한쪽으로 살짝 기운다(LEAN). 퍼짐 = 바깥 겹이 몇 무리로 뭉쳐 바깥으로 휘며 풀린다.
꼭짓점 색 Col(선형 float): R = 가닥 난수 0–1, G = 뿌리→끝 0–1, B = 겹(0 = 속심, 1 = 맨 바깥 잔털). 붓털이 아닌 곳은 (0,0,0).""" + Q + "\n" + s
rep("arg('--counts', '12,18,26,32')", "arg('--counts', '14,24,36,50,56,24')")
rep("NR = int(arg('--rings', 9)); SEED = int(arg('--seed', 3083)); TRI_BUDGET = 12000", "SEED = int(arg('--seed', 4083)); TRI_BUDGET = 18000")
cut("FRACS = [", "TEX = 256", """FRACS = [.25, .45, .63, .80, .93, 1.0]; LEN = [(.96, 1.0), (.94, .995), (.91, .985), (.88, .97), (.84, .95), (.58, .88)]
LAG = [.0, .10, .25, .40, .55, .65]; RING_SHARE = [.10, .18, .30, .46, .60, .68]; SIDES = [3, 3, 3, 4, 4, 3]; RINGS = [6, 6, 8, 12, 12, 8]
THICK = [.68, .65, .68, .74, .72, 0]; JIT = [.03, .04, .05, .05, .035, .0]; STRAYS = [0, 0, 0, 3, 5, 0]; LEAN_AMT = .011; N_CLUMPS = 7
""")
rep("ring.append(hv(d * max(r, .0015) + Vector((0, 0, z)), dict(", "ring.append(hv(d * max(r, .0015) + Vector((0, 0, z)) + LEAN * t ** 2.4, dict(")
rep("c=Vector((0, 0, z)), d=d, td=Vector((-d.y, d.x, 0)), a=a, rand=0.0, core=True)))", "c=Vector((0, 0, z)) + LEAN * t ** 2.4, d=d, td=Vector((-d.y, d.x, 0)), a=a, rand=0.0, core=True)))")
rep("ctip = hv(Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))), dict(", "ctip = hv(Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))) + LEAN * (CORE_END + .02) ** 2.4, dict(")
rep("c=Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))), d=Vector((0, 0, 0))", "c=Vector((0, 0, Z_HEAD + HEAD * (CORE_END + .02))) + LEAN * (CORE_END + .02) ** 2.4, d=Vector((0, 0, 0))")
rep("# 속심: 윤곽보다","LEAN_A = rnd.random() * 2 * math.pi; LEAN = Vector((math.cos(LEAN_A), math.sin(LEAN_A), 0)) * LEAN_AMT      # 붓끝 전체가 한쪽으로 살짝 기운다(젖었다 마른 붓)\n"
               "CLUMPS = [2 * math.pi * (j + rnd.uniform(-.25, .25)) / N_CLUMPS for j in range(N_CLUMPS)]                          # 퍼질 때 가닥이 뭉치는 무리\n# 속심: 윤곽보다")
cut("# 가닥 다발", "# ---- 꼬리 고리", """# 가닥 다발
def wrap(x): return (x + math.pi) % (2 * math.pi) - math.pi


STRANDS = []; n_layers = len(COUNTS); R_TIP = .00032
for L, n in enumerate(COUNTS):
    wisp = L == n_layers - 1; fc = FRACS[L]; spacing = 2 * math.pi * fc * R_ROOT / n; phase = rnd.random() * 2 * math.pi
    NRL = RINGS[L]; sides = SIDES[L]; stray_ids = set(rnd.sample(range(n), STRAYS[L]))
    for q in range(n):
        st = dict(sid=len(STRANDS), layer=L, rand=rnd.random(), stray=q in stray_ids, wisp=wisp)
        a0 = phase + 2 * math.pi * (q + rnd.uniform(-.45, .45)) / n                       # 시작 각을 크게 흩뜨린다(뿌리에서 나란한 관 줄무늬가 안 생기게)
        lenf = rnd.uniform(*LEN[L]) * (rnd.uniform(.93, .98) if st['stray'] else 1.0)
        r0 = .0011 * rnd.uniform(.75, 1.3) if wisp else spacing * THICK[L] * rnd.uniform(.6, 1.35)
        fj = min(fc + rnd.uniform(-JIT[L], JIT[L]), 1.0 - .5 * r0 / R_ROOT)
        ar = rnd.uniform(-.002, .004); at = rnd.uniform(-.008, .008); tw = rnd.uniform(-.38, .38)
        aw = rnd.uniform(.0012, .0038); wph = rnd.random() * 6.283; wf = rnd.uniform(.55, 1.15)
        if st['stray']: ar = rnd.uniform(.006, .013); at = rnd.uniform(-.010, .010)
        if wisp: ar = rnd.uniform(.002, .008); aw = rnd.uniform(.0025, .0055); at = rnd.uniform(-.012, .012)
        cd = min((wrap(c - a0) for c in CLUMPS), key=abs)
        st.update(a0=a0, lenf=lenf, r0=r0, lag=max(0.0, LAG[L] + rnd.uniform(-.12, .25)), share=RING_SHARE[L] + rnd.uniform(-.08, .08) + (.08 if st['stray'] else 0), frj=rnd.uniform(-.3, .3),
                  splay=rnd.uniform(.6, 1.4), splay_t=rnd.uniform(-1, 1), fan=rnd.uniform(-1, 1), clump=cd, u0=(CORE_COLS + rnd.randrange(TEX - CORE_COLS - 8) + .5) / TEX)
        sec_phase = rnd.random() * 2 * math.pi; rows = []
        for k in range(NRL + 1):
            s = k / NRL; tg = s * lenf; z = Z_HEAD - .006 + (HEAD * lenf + .006) * s
            a = a0 + tw * s; d = Vector((math.cos(a), math.sin(a), 0)); td = Vector((-d.y, d.x, 0)); lean = LEAN * tg ** 2.4
            c = (d * (fj * R_ROOT * .97 * envn(tg)) + d * (ar * s ** 1.6 * ((1 - .5 * s) if st['stray'] else 1))
                 + td * (at * s ** 2 + aw * s ** 1.5 * math.sin(wph + 6.283 * wf * s)) + lean + Vector((0, 0, z)))
            m = dict(sid=st['sid'], layer=L, s=s, tg=tg, c=c, d=d, td=td, a=a0, rand=st['rand'], core=False, st=st, lean=lean)
            if k == NRL: rows.append([hv(c, m)]); break
            rt = R_TIP + (r0 * envn(tg) ** .6 - R_TIP) * (1 - s ** 1.7)
            fr_, ft_ = ((.85, 1.15) if (L < 2 or wisp) else (.62, 1.55)); sp = math.pi / 4 if sides == 4 else sec_phase
            rows.append([hv(c + d * (rt * fr_ * math.cos(sp + 2 * math.pi * j / sides)) + td * (rt * ft_ * math.sin(sp + 2 * math.pi * j / sides)), m) for j in range(sides)])
        cc = lambda v: (st['rand'], META[v[mid]]['s'], (L + 1) / n_layers, 1.0)
        uu = lambda j, v: (st['u0'] + (j / sides) * (3 if wisp else 6) / TEX, min(.999, META[v[mid]]['s']))
        for k in range(NRL - 1):
            for j in range(sides):
                vs = (rows[k][j], rows[k][(j + 1) % sides], rows[k + 1][(j + 1) % sides], rows[k + 1][j])
                hface(vs, [uu(j, vs[0]), uu(j + 1, vs[1]), uu(j + 1, vs[2]), uu(j, vs[3])], [cc(v) for v in vs])
        for j in range(sides):
            vs = (rows[NRL - 1][j], rows[NRL - 1][(j + 1) % sides], rows[NRL][0]); hface(vs, [uu(j, vs[0]), uu(j + 1, vs[1]), uu(j + .5, vs[2])], [cc(v) for v in vs])
        STRANDS.append(st)


""")
rep("share = min(.6, max(0.0, st['share'])) * sstep(m['tg'] / .18)", "share = min(.78, max(0.0, st['share'])) * sstep(m['tg'] / .18)")
rep("fr = ang - math.floor(ang)\n", "fr = min(1.0, max(0.0, ang - math.floor(ang) + st['frj']))      # 이웃 가닥끼리도 두 고리 사슬 몫이 다르다\n")
i = s.index("    if st:\n        k = max(0.0, (s - .10) / .90) ** 1.5"); j = s.index("    else:\n        k = sstep((tg - .35) / .6)", i)
s = s[:i] + """    if st:
        fl = .74 if st['wisp'] else FRACS[st['layer']]; base = c - m['lean']; rr = math.hypot(base.x, base.y); aa = math.atan2(base.y, base.x)
        k = max(0.0, (s - .12) / .88); kc = k ** 2.4                       # 끝으로 갈수록 바깥으로 휘며 풀린다(곧은 부채가 아니다)
        rr2 = rr + (.003 + .052 * fl ** 3) * st['splay'] * kc + .008 * fl * k; aa2 = aa + st['clump'] * .50 * fl * k ** 1.6 + .035 * st['splay_t'] * kc
        cs = Vector((math.cos(aa2) * rr2, math.sin(aa2) * rr2, base.z - (.004 + .030 * fl) * kc)) + m['lean']
        splay.data[v.index].co = cs + o
""" + s[j:]
rep("'version': 'v3'", "'version': 'v4'")
rep("'strand_rings': NR, 'strand_sides': SIDES,", "'strand_rings': RINGS, 'strand_sides': SIDES, 'wisp_strands': COUNTS[-1], 'tip_lean_m': LEAN_AMT, 'splay_clumps': N_CLUMPS,\n"
    "          'strand_root_radius_mean_m': sum(x['r0'] for x in STRANDS if not x['wisp']) / sum(1 for x in STRANDS if not x['wisp']),")
rep("'B': 'layer (0 core .. 1 outer)'", "'B': 'layer (0 core .. 1 outermost wisps)'")
assert not re.findall(r'\bNR\b', s), 'NR left'
open('build_v4.py', 'w', encoding='utf-8').write(s)
for f in ('verify', 'probe'):
    t = open(f + '_v3.py', encoding='utf-8').read().replace('v3', 'v4').replace('12000', '18000'); open(f + '_v4.py', 'w', encoding='utf-8').write(t)
t = open('run_v3.sh', encoding='utf-8').read().replace('v3', 'v4'); open('run_v4.sh', 'w', encoding='utf-8', newline='\n').write(t)
print('ok')
