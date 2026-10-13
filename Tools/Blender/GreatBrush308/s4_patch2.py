"""#308 s4: solve_m2.py 에 새 쥐기 · 열쇠 자세 길을 넣는다(한 번만 돈다. 되돌리기 = solve_m2.py.pre_s4)."""
import io, os, shutil
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'solve_m2.py'; s = io.open(p, encoding='utf-8').read(); assert 'build_keyed' not in s, 'already patched'
shutil.copy(p, p + '.pre_s4')
def rep(a, b, cnt=1):
    global s
    assert s.count(a) == cnt, (a[:70], s.count(a)); s = s.replace(a, b)
blk = io.open('s4_block.txt', encoding='utf-8').read()
rep("src = Source('Great_Sword_Idle'); STANCE = src.sample(1); src.close(); ref = Vector((.009, .037))     # 전투 태세의 원본 엉덩이 XY\n", blk)
rep("def pchip(xs, ys, m_end=None):", "def pchip(xs, ys, m_end=None, m_start=None):")
rep("m[0] = dl[0]; m[-1] = dl[-1] if m_end is None else m_end", "m[0] = dl[0] if m_start is None else m_start; m[-1] = dl[-1] if m_end is None else m_end")
rep("FR = []; LW_FORCE = None; info = dict(name=NAME, use=SPEC['use'])\nif 'special' not in SPEC:", "FR = []; LW_FORCE = None; info = dict(name=NAME, use=SPEC['use']); KEYED = SPEC.get('kind') == 'keyed'; SH = None; FW = None\nif KEYED:\n    FR = build_keyed(); info.update(src='손으로 만듦(열쇠 자세 · 새 쥐기)', speed_factors=[], root='none', stance={k_: (list(v_) if isinstance(v_, tuple) else v_) for k_, v_ in SC.items()})\nelif 'special' not in SPEC:")
# ---- 어깨 걸치기 길: 몸은 옛 태세(OLD)에서, 태세 쪽 끝은 새 태세(STANCE)
rep("CG = Vector(_cg); CD = Vector(_cd).normalized(); P0 = STANCE.P; d0, n0, G0 = hand_state(P0)", "CG = Vector(_cg); CD = Vector(_cd).normalized(); P0 = OLD.P; d0, n0, G0 = hand_state(STANCE.P)")
rep("P.fk(); legs(P, STANCE.ank, STANCE.fq, STANCE.knee); return P\n    def hang(P):", "P.fk(); legs(P, {s_: v.copy() for s_, v in OLD.ank.items()}, OLD.fq, OLD.knee); return P\n    def hang(P):")
rep("hint0 = P0.H['RightForeArm'] - P0.H['RightArm']", "hint0 = STANCE.P.H['RightForeArm'] - STANCE.P.H['RightArm']")
rep("        P = body(ease(s), breath); u = ease((s - .12) / .88)", "        Fc = Frame(body(1.0, breath), {s_: v.copy() for s_, v in OLD.ank.items()}, OLD.fq, OLD.knee); Fb = blend(STANCE, Fc, ease(s)); P = Fb.P; u = ease((s - .12) / .88)")
rep("""                for n_ in ('RightArm', 'RightForeArm', 'RightHand'): P.L[n_] = slerp(P0.L[n_], P.L[n_], u / .25)
        P.fk(); F = Frame(P, STANCE.ank, STANCE.fq, STANCE.knee); F.src_hand = 0; return F""", """                for n_ in ('RightArm', 'RightForeArm', 'RightHand'): P.L[n_] = slerp(STANCE.P.L[n_], P.L[n_], u / .25)
        P.fk(); F = Frame(P, Fb.ank, Fb.fq, Fb.knee); F.src_hand = 0; F.u = u; return F""")
rep("P = body(1.0); life(P, x, LIFE); legs(P, {s: v.copy() for s, v in STANCE.ank.items()}, STANCE.fq, STANCE.knee); th = 2 * math.pi * x", "P = body(1.0); life(P, x, LIFE); legs(P, {s: v.copy() for s, v in OLD.ank.items()}, OLD.fq, OLD.knee); th = 2 * math.pi * x")
rep("c, r = attempt(P, 'Right', G, d, nn, torso_caps(P), hint1); set_arm(P, 'Right', r); P.fk(); F = Frame(P, STANCE.ank, STANCE.fq, STANCE.knee); F.src_hand = 0; return F", "c, r = attempt(P, 'Right', G, d, nn, torso_caps(P), hint1); set_arm(P, 'Right', r); P.fk(); F = Frame(P, OLD.ank, OLD.fq, OLD.knee); F.src_hand = 0; F.u = 1.0; return F")
rep("NF = int(round(SPEC['seconds'] * FPS)) + 1; LW_FORCE = []\n", "NF = int(round(SPEC['seconds'] * FPS)) + 1; LW_FORCE = []; SH = []; FW = []\n")
rep("F.t = i / FPS; F.srcf = 0; F.hang = lw < 1; FR.append(F); LW_FORCE.append(lw)", "F.t = i / FPS; F.srcf = 0; F.hang = lw < 1; FR.append(F); LW_FORCE.append(lw); SH.append(SHIFT2 + (SHIFT - SHIFT2) * F.u); FW.append(1 - F.u)")
# ---- 2) 오른손 당김 + 붓
rep("NF = len(FR); POSES = [F.P for F in FR]; MET =", "NF = len(FR); SH = SH or [SHIFT2] * NF; FW = FW or [1.0] * NF; POSES = [F.P for F in FR]; MET =")
rep("P = POSES[i]; d, n, Gp = hand_state(P); v = P.H['LeftArm'] - (Gp - d * GAP); over = v.length - .03 - .93 * tgt_arm", "P = POSES[i]; d, n, Gp = hand_state(P); v = P.H['LeftArm'] - (Gp + d * (SH[i] + LZ)); over = (v.length - .03 - .93 * tgt_arm) if not KEYED else 0")
rep("    for n_, l in GR.L.items(): P.L[n_] = l\n    P.fk(); d, n, Gp = hand_state(P); BR.append(dict(G=Gp + d * SHIFT, Gh=Gp, d=d, n=n))", "    for n_, l in GR.L.items(): P.L[n_] = slerp(l, GRT.L[n_], FW[i]) if 0 < FW[i] < 1 else (GRT.L[n_] if FW[i] >= 1 else l)\n    P.fk(); d, n, Gp = hand_state(P); BR.append(dict(G=Gp + d * SH[i], Gh=Gp, d=d, n=n))")
# ---- 3) 왼손
rep("        MET[i]['l_fk_elbow'] = STANCE.P.H['LeftForeArm'] - STANCE.P.H['LeftArm']\n", "        MET[i]['l_fk_elbow'] = STANCE.P.H['LeftForeArm'] - STANCE.P.H['LeftArm']\nNL = SC['nL']; NLS = math.radians(SC['nL_soft']); LREF = {}\nLENDS = [k_ for k_, on_ in ((0, KEYED or SPEC.get('special') == 'to_carry'), (NF - 1, KEYED or SPEC.get('special') == 'to_stance')) if on_]\n")
rep("def lhint(i): return MET[i]['l_fk_elbow'].normalized() + (POSES[i].Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35", "def lhint(i): return (POSES[i].Q['Spine'] @ HINT_L) if (KEYED or i in LENDS) else MET[i]['l_fk_elbow'].normalized() + (POSES[i].Q['Spine'] @ Vector((.55, .45, -.7))).normalized() * .35")
rep("""    e2 = d.cross(e1); E1.append((e1.copy(), e2.copy())); caps = torso_caps(P); hint = lhint(i); sock = b['Gh'] - d * GAP; mr = 9
    for k in range(K):
        a = 2 * math.pi * k / K; costs[i, k], r_ = attempt(P, 'Left', sock, d, e1 * math.cos(a) + e2 * math.sin(a), caps, hint); mr = min(mr, r_['reach'])""",
"""    caps = torso_caps(P); hint = lhint(i); sock = b['G'] + d * LZ; mr = 9; kr = None; basel = n_pref(d, NL)
    if i in LENDS: e1, e2, kr = ring_fix(e1, d, ref_n(P, 'Left', sock, d, hint, NL, SC['nL_soft']), K); LREF[i] = kr
    else: e2 = d.cross(e1)
    E1.append((e1.copy(), e2.copy()))
    for k in range(K):
        a = 2 * math.pi * k / K; nk_ = e1 * math.cos(a) + e2 * math.sin(a); costs[i, k], r_ = attempt(P, 'Left', sock, d, nk_, caps, hint); mr = min(mr, r_['reach']); costs[i, k] += (nk_.angle(basel) / NLS) ** 2
    if kr is not None: costs[i, :] += 1e5; costs[i, kr] -= 1e5""")
rep("if SPEC.get('loop'): path = [max(set(path), key=path.count)] * NF", "if SPEC.get('loop') and not KEYED: path = [max(set(path), key=path.count)] * NF")
rep("cst, r = attempt(P, 'Left', b['Gh'] - d * GAP, d, e1 * math.cos(a) + e2 * math.sin(a), torso_caps(P), lhint(i)); m.pop('l_fk_elbow')", "cst, r = attempt(P, 'Left', b['G'] + d * LZ, d, e1 * math.cos(a) + e2 * math.sin(a), torso_caps(P), lhint(i)); m.pop('l_fk_elbow')")
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('patched solve_m2')
