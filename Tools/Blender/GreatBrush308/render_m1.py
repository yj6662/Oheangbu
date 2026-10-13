"""#308 GreatBrush motion1: 구운 클립의 프레임 스틸(Workbench).
    blender -b --factory-startup -P render_m1.py -- <clip> --frames 1,5,9 [--blend 경로] [--tag 수치 꼬리표] [--sub 폴더] [--res 480] [--cams q34,shoulder] [--camdir x,y,z]
출력: motion1/stills[/sub]/<clip>_<cam>_<frame>.png.  q34 = 앞 3/4(창 전체 범위에 고정), shoulder = 게임 어깨 카메라(뒤 2.8 m · 위 0.55 m · 아래 8도 · 세로 FOV 60, 엉덩이 XY 를 따라간다)."""
import bpy, json, math, os, sys
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C
ARGS = sys.argv[sys.argv.index('--') + 1:]; CLIP = ARGS[0]
def opt(k, d=None): return ARGS[ARGS.index(k) + 1] if k in ARGS else d
BLEND = opt('--blend', C.BLEND); TAG = opt('--tag', ''); SUB = opt('--sub', ''); RES = int(opt('--res', 480)); CAMS = opt('--cams', 'q34,shoulder').split(',')
FR = [int(x) for x in opt('--frames').split(',')]; CD = Vector([float(x) for x in opt('--camdir', '-.62,-.74,.25').split(',')]).normalized()
ST = C.OUT + '/stills' + ('/' + SUB if SUB else ''); os.makedirs(ST, exist_ok=True)
g = C.init(BLEND); assert C.assign_clip(CLIP), 'no actions for ' + CLIP
M = json.load(open('%s/metrics/%s%s.json' % (C.OUT, CLIP, ('_' + TAG) if TAG else ''), encoding='utf-8'))['frames']; byf = {m['frame']: m for m in M}
cam = C.setup_render(RES, RES); sc = bpy.context.scene
def root(f):
    i = M.index(byf[f]); w = M[max(0, i - 6):i + 7]; return Vector((sum(x['hips'][0] for x in w) / len(w), sum(x['hips'][1] for x in w) / len(w) + .048, 0))


pts = []
for f in FR:      # 범위는 따라가는 뿌리 기준(멀리 가는 클립에서도 몸이 크게 보이게)
    m = byf[f]; G = Vector(m['G']); d = Vector(m['d']); r = root(f)
    pts += [p - r for p in (Vector(m['head']) + Vector((0, 0, .22)), r + Vector((-.3, 0, 0)), r + Vector((.3, 0, 0)), G + d * C.TIPL, G - d * C.TAIL)]
lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)]); c = (lo + hi) / 2; c.z = max(c.z, .0); rad = max((p - c).length for p in pts) + .05
for f in FR:
    sc.frame_set(f); r = root(f)
    if 'q34' in CAMS:
        fov = 38; C.look(cam, r + c + CD * rad * .80 / math.sin(math.radians(fov / 2)), r + c, fov); C.shot('%s/%s_q34_%03d.png' % (ST, CLIP, f))
    if 'shoulder' in CAMS:
        C.shoulder_cam(cam, (r.x, r.y)); C.shot('%s/%s_shoulder_%03d.png' % (ST, CLIP, f))
    print('M1 shot', CLIP, f)
print('M1 DONE')
