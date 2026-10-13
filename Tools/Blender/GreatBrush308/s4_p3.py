import io
p = 'solve_m2.py'; s = io.open(p, encoding='utf-8').read()
a = "def lhint(i): return (POSES[i].Q['Spine'] @ HINT_L) if (KEYED or i in LENDS) else"
assert s.count(a) == 1
s = s.replace(a, "def lhint(i): return ((POSES[i].Q['Spine'] @ HINT_L).normalized() + BR[i]['d'] * clips_m2.LHINT_FWD) if (KEYED or i in LENDS) else")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
p = 'clips_m2.py'; s = io.open(p, encoding='utf-8').read()
s = s.replace("LEFT_LOOSE = .999 ", "LHINT_FWD = .7                                                            # 왼 팔꿈치를 자루 꼬리 반대쪽(붓끝 쪽)으로 치우치게 하는 정도 — 꼬리가 왼 아래팔을 지나가지 않게\nLEFT_LOOSE = .999 ")
s = s.replace("(.40, 0, .21, -.25, .98, 42, -18), (.48, 0, .21, -.255, .975, 44, -21)", "(.40, 0, .20, -.19, 1.04, 42, -18), (.48, 0, .20, -.195, 1.035, 44, -21)")
s = s.replace("(.70, 8, 20, 26, 0, 0, -.06, -.24, 4, 10), (.80, 8, 21, 27, 0, 0, -.06, -.25, 4, 11)", "(.70, 8, 20, 30, 0, 0, -.06, -.22, 4, 8), (.80, 8, 21, 31, 0, 0, -.06, -.23, 4, 9)")
s = s.replace("(.70, 0, .22, -.26, .72, 34, -24), (.80, 0, .22, -.26, .71, 34, -25)", "(.70, 0, .21, -.24, .92, 30, -30), (.80, 0, .21, -.24, .91, 30, -31)")
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('ok', 'LHINT_FWD' in s, '-.19, 1.04' in s, '.92, 30, -30' in s)
