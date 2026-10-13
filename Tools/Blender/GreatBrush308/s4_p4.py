import io
p = 'solve_m2.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b):
    global s
    assert s.count(a) == 1, a[:60]; s = s.replace(a, b)
rep("def lhint(i): return ((POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * clips_m2.LHINT_FWD) if (KEYED or i in LENDS) else",
    "LHF = {}\ndef lhint(i): return ((POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * LHF.get(i, clips_m2.LHINT_FWD)) if (KEYED or i in LENDS) else")
rep("if SPEC.get('loop') and not KEYED: path = [max(set(path), key=path.count)] * NF",
"""if SPEC.get('loop') and not KEYED: path = [max(set(path), key=path.count)] * NF
if KEYED:       # s4: 왼 팔꿈치 방향(자루 쪽 치우침 f)도 프레임 사이 DP — 꼬리 쪽 자루가 왼 아래팔을 지나가지 않게, 손목이 꺾이지 않게
    FS = [0.0, .2, .35, .5, .7, .9, 1.1]; c3 = np.zeros((NF, len(FS))); L1L = (RH['LeftForeArm'] - RH['LeftArm']).length; L2L = (RH['LeftHand'] - RH['LeftForeArm']).length
    for i in range(NF):
        P = POSES[i]; b = BR[i]; d = b['d']; e1, e2 = E1[i]; a = 2 * math.pi * path[i] / K; nk_ = e1 * math.cos(a) + e2 * math.sin(a); sock = b['G'] + d * LZ; qh = GL.hand_q(d, nk_); W_ = sock - qh @ (GL.C - RH['LeftHand'])
        for j, f_ in enumerate(FS):
            h_ = (P.Q['Spine'] @ HINT_L).normalized() + d * f_; E_, W2_, rc_ = two_bone(P.H['LeftArm'], W_, L1L, L2L, h_); pen = 0
            for sd in (.07, .12, .17): pen += (max(0, .065 - seg_pt(sock - d * sd, E_, E_.lerp(W2_, .8))) * 90) ** 2 + (max(0, .09 - seg_pt(sock - d * sd, P.H['LeftArm'], E_)) * 60) ** 2
            c3[i, j] = attempt(P, 'Left', sock, d, nk_, torso_caps(P), h_)[0] + pen + ((f_ - clips_m2.LHINT_FWD) / .8) ** 2 + (1e5 if i in LENDS and f_ != clips_m2.LHINT_FWD else 0)
    T3 = ((np.array(FS)[:, None] - np.array(FS)[None, :]) / .25) ** 2; acc = c3[0].copy(); bk3 = np.zeros((NF, len(FS)), dtype=int)
    for i in range(1, NF): tot = acc[:, None] + T3; bk3[i] = tot.argmin(axis=0); acc = tot.min(axis=0) + c3[i]
    p3 = [int(acc.argmin())]
    for i in range(NF - 1, 0, -1): p3.append(int(bk3[i][p3[-1]]))
    p3.reverse(); LHF = {i: FS[p3[i]] for i in range(NF)}; info['l_hint_f'] = [FS[j] for j in p3]""")
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('ok')
