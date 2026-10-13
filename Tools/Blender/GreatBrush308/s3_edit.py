"""#308 swing3: clips_m2.py 의 한 클립 항목(여러 줄)을 파일 내용으로 바꾼다.  python s3_edit.py <이름> <새 항목 파일>"""
import io, os, re, sys
H = os.path.dirname(os.path.abspath(__file__)); p = H + '/clips_m2.py'; s = io.open(p, encoding='utf-8').read(); n = sys.argv[1]; new = io.open(sys.argv[2], encoding='utf-8').read().rstrip('\n')
i = s.index("    '%s': dict(" % n); j = s.index("\n    '", i + 5); s = s[:i] + new + s[j:]; io.open(p, 'w', encoding='utf-8', newline='').write(s); print('edited', n)
