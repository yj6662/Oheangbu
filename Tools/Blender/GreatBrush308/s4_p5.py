import io
p = 'solve_m2.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b):
    global s
    assert s.count(a) == 1, a[:60]; s = s.replace(a, b)
rep("LHF = {}\ndef lhint(i): return ((POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * LHF.get(i, clips_m2.LHINT_FWD)) if (KEYED or i in LENDS) else",
    "LHF = {}\ndef lhint0(i): return (POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * clips_m2.LHINT_FWD\ndef lhint(i): return (LHF[i] if i in LHF else lhint0(i)) if (KEYED or i in LENDS) else")
rep("FS = [0.0, .2, .35, .5, .7, .9, 1.1]; c3", "FS = [-90, -60, -30, 0, 30, 60, 90, 120]; HR = []; c3")
rep("        for j, f_ in enumerate(FS):\n            h_ = (P.Q['Spine'] @ HINT_L).normalized() + d * f_; E_", "        ax_ = (W_ - P.H['LeftArm']).normalized(); h0_ = lhint0(i); h0_ = (h0_ - ax_ * h0_.dot(ax_)).normalized(); HR.append([axis_rot(ax_, f_) @ h0_ for f_ in FS])\n        for j, f_ in enumerate(FS):\n            h_ = HR[i][j]; E_")
rep("+ pen + ((f_ - clips_m2.LHINT_FWD) / .8) ** 2 + (1e5 if i in LENDS and f_ != clips_m2.LHINT_FWD else 0)", "+ pen + (f_ / 80.0) ** 2 + (1e5 if i in LENDS and f_ != 0 else 0)")
rep("T3 = ((np.array(FS)[:, None] - np.array(FS)[None, :]) / .25) ** 2", "T3 = ((np.array(FS)[:, None] - np.array(FS)[None, :]) / 35.0) ** 2")
rep("LHF = {i: FS[p3[i]] for i in range(NF)}; info['l_hint_f'] = [FS[j] for j in p3]", "LHF = {i: HR[i][p3[i]] for i in range(NF)}; info['l_swivel'] = [FS[j] for j in p3]")
rep("HINT_L = Vector((.45, .55, -.7))", "HINT_L = Vector(clips_m2.HINT_L)")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
p = 'clips_m2.py'; s = io.open(p, encoding='utf-8').read()
s = s.replace("LHINT_FWD = .35", "HINT_L = (.8, .3, -.5)                                                    # 왼 팔꿈치 방향(가슴 기준: 왼쪽 · 뒤 · 아래)\nLHINT_FWD = .2 ")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
p = 's4_sum.py'; s = io.open(p, encoding='utf-8').read()
s = s.replace("'wrist', S['l_wrist_swing_deg_max'])", "'wrist', S['l_wrist_swing_deg_max'], 'Lswivel', sorted(set(S.get('l_swivel', []))))")
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('ok')
