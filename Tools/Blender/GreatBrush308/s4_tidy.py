import io
s = io.open('clips_m2.py', encoding='utf-8').read(); k = '# ===== s4('
a = s.index(k); b = s.index(k, a + 5); s = s[:a] + s[b:]; io.open('clips_m2.py', 'w', encoding='utf-8', newline='').write(s); print(s.count(k), s.count('STANCE4 = '))
