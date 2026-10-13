"""#308 GreatBrush motion2: 확인 스틸. render_m1.py 를 motion2 설정(common_m2)으로 돌린다.
    blender -b --factory-startup -P render_m2.py -- <이름> [--frames a,b,c] [--sub 폴더] [--res 480] [--cams q34,shoulder]   (프레임 생략 = clips_m2.SHOTS)"""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1, common_m2, clips_m2
if '--frames' not in sys.argv: sys.argv += ['--frames', ','.join(str(f) for f in clips_m2.SHOTS[sys.argv[sys.argv.index('--') + 1]])]
exec(compile(open(os.path.join(HERE, 'render_m1.py'), encoding='utf-8').read(), 'render_m1.py', 'exec'))
