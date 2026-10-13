"""#308 s4: clips_m2.py 의 한 줄 값을 바꾼다.  python s4_set.py <파일(바꿀 내용: 줄마다 '찾을 접두사 ||| 새 줄 전체')>  — 접두사로 시작하는 줄부터 다음 항목 전까지를 새 내용으로."""
import io, sys, re
p = 'clips_m2.py'; s = io.open(p, encoding='utf-8').read().split('\n'); spec = io.open(sys.argv[1], encoding='utf-8').read().split('\n@@ ')
for blk in spec:
    blk = blk.lstrip('@ ').rstrip('\n')
    if not blk.strip(): continue
    key, new = blk.split('\n', 1); key = key.strip(); i = next(k for k, l in enumerate(s) if l.strip().startswith(key)); j = i + 1
    while j < len(s) and s[j].startswith('                '): j += 1     # 이어지는 줄(깊은 들여쓰기)
    s[i:j] = new.split('\n'); print('set', key)
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(s))
