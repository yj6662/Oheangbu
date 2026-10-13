import io
p = 'common_m2.py'; s = io.open(p, encoding='utf-8').read()
if 'SHIFT2' not in s:
    s = s.replace("GAP = .12 ", "SHIFT2 = .38         # s4(쥐기 뒤집힘): 두 손 클립에서 오른손 = 꼬리 끝에서 0.22 m (GripSocket 뒤 0.38 m). SHIFT(.30)는 어깨 걸치기에만 남는다\nLZ = .12             # s4: 왼손 받침 = GripSocketL = GripSocket 에서 붓끝 쪽 +0.12 m (오른손 앞 0.50 m)\nGAP = .12 ")
    io.open(p, 'w', encoding='utf-8', newline='').write(s)
p = 'clips_m2.py'; s = io.open(p, encoding='utf-8').read()
i = s.index("CLIPS = {"); j = s.index("# carry2(2026-10-13)")
new = io.open('s4_clips.txt', encoding='utf-8').read()
s = s[:i] + new + "\n" + s[j:]
k = s.index("SHOTS = {"); s = s[:k] + io.open('s4_shots.txt', encoding='utf-8').read()
io.open(p, 'w', encoding='utf-8', newline='').write(s); print('patched')
