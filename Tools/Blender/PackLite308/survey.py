"""#308 PackLite: list prefabs of a folder with resolved part counts and total source FBX size (pure Python).
  python Tools/Blender/PackLite308/survey.py <folder> [min parts]
"""
import sys, glob, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_prefab as UP

root = sys.argv[1]; minp = int(sys.argv[2]) if len(sys.argv) > 2 else 1
rows = []
for f in glob.glob(root + '/**/*.prefab', recursive=True):
    f = f.replace(os.sep, '/')
    try: ps = UP.resolve(f)
    except Exception as e:
        print('ERR', f, e); continue
    kb = sum(os.path.getsize(x) for x in {p['fbx'] for p in ps if os.path.exists(p['fbx'])}) // 1024
    rows.append((len(ps), kb, f[len(root):].lstrip('/')))
for n, kb, f in sorted(rows, reverse=True):
    if n >= minp: print('parts %4d  fbxKB %7d  %s' % (n, kb, f))
print(len(rows), 'prefabs')
