import io
p = 'export_m2.py'; s = io.open(p, encoding='utf-8').read()
a = "right_hand_hold_behind_gripsocket_m=C2.SHIFT,"; assert s.count(a) == 1
s = s.replace(a, "right_hand_hold_behind_gripsocket_m=C2.SHIFT2, right_hand_hold_from_gripsocket_toward_tip_m=-C2.SHIFT2, carry_right_hand_hold_behind_gripsocket_m=C2.SHIFT, gripsocketL_from_gripsocket_toward_tip_m=C2.LZ, hold_convention='s4(2026-10-13): 오른손 = 앞손(붓머리 쪽 · 손바닥이 자루 밑 · 엄지 위 · 조종), 왼손 = 꼬리 축(GripSocketL · 손등이 위). 몸은 오른 어깨가 앞.',")
a = "left_hand_behind_right_hand_along_shaft_m=C2.GAP,"; assert s.count(a) == 1
s = s.replace(a, "left_hand_behind_right_hand_along_shaft_m=round(-C2.SHIFT2 - C2.LZ, 4),")
i = s.index("warning='오른손은 GripSocket 이 아니라"); j = s.index("),\n", i)
s = s[:i] + "warning='두 손 클립: 오른손은 GripSocket 보다 붓끝 쪽 0.08 m(꼬리 끝에서 0.68 m)를 쥐고, 왼손은 GripSocketL(GripSocket 뒤 0.42 m = 꼬리 끝에서 0.18 m)을 쥔다 — 두 손 사이 0.50 m. 어깨 걸치기(CarryIdle)는 옛 그대로 GripSocket 뒤 0.30 m 를 오른손으로 쥔다(전환 두 클립에서 오른손이 자루를 따라 0.38 m 미끄러진다).'" + s[j:]
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('ok')
