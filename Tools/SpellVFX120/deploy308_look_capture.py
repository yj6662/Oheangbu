# SPEC-SPELL-DEPLOY-308 look gate: editor stills of the deploy layer (in-memory preview, nothing saved).
#   python Tools/resource_guard.py --gpu-run deploylook -- python Tools/SpellVFX120/deploy308_look_capture.py [yaw] [x,y,z] [shot,shot,...]
# Default spot: the Geumpyo inn forecourt (open ground). Stills go to Art/SpellVFX120/Deploy308/Look/. The editor must be in Edit mode.
import json, math, shutil, sys, time, contextlib, io
from pathlib import Path
ROOT = Path(r'C:/Users/yj666/Oheangbu'); sys.path.insert(0, str(ROOT / 'Tools/Unity'))
from playtest_polish import call
def q(t, a, timeout=300):
    with contextlib.redirect_stdout(io.StringIO()):
        r = call('Oheangbu.EditorTools.WorldMacro.' + t, 'Run', a, timeout)
    return r
out = ROOT / 'Art/SpellVFX120/Deploy308/Look'; out.mkdir(parents=True, exist_ok=True)
YAW = float(sys.argv[1]) if len(sys.argv) > 1 else 225.0
AT = tuple(float(v) for v in sys.argv[2].split(',')) if len(sys.argv) > 2 else (3062.0, 128.1, 2322.0)
ONLY = sys.argv[3].split(',') if len(sys.argv) > 3 else None
fx, fz = math.sin(math.radians(YAW)), math.cos(math.radians(YAW))
eye = (AT[0], AT[1] + 1.6, AT[2])
# name, preview spec, look distance (m ahead of the eye), look drop (m below the eye)
shots = [
    # D308-10c: the three cases of one cast, then the other forms
    ('c_ga_birth', '가:birth:view=4', 4, .35), ('c_ga_burst', '가:burst:view=4', 4, .35), ('c_ga_hit', '가:burst:hit:view=4', 4, .35), ('c_ga_hit_late', '가:burst:hit:hitage=0.3:view=4', 4, .35),
    ('c_ga_groggy1', '가:burst:hit:groggy:view=4', 4, .35), ('c_ga_groggy2', '가:burst:hit:groggy=2:view=4', 4, .35), ('c_ga_groggy3', '가:burst:hit:groggy=3:view=4', 4, .35),
    ('c_ga_residue', '가:residue:hit:view=4', 4, 1.3),
    ('c_na_birth', '나:birth:view=4', 4, .35), ('c_na_hit', '나:burst:hit:view=4', 4, .35),
    ('c_ma_hit', '마:burst:hit:view=4', 4, .35), ('c_sa_hit', '사:burst:hit:view=4', 4, .35), ('c_a_hit', '아:burst:hit:view=4', 4, .35),
    ('c_no_birth', '노:birth:view=10', 10, .9), ('c_no_hit', '노:burst:hit:view=10', 10, .9), ('c_no_groggy1', '노:burst:hit:groggy:view=10', 10, .9),
    ('c_seo', '서:ignite:view=4', 4, .35), ('c_mu_hold', '무:hold:view=4', 4, 1.2), ('c_om_burst', '옴:burst:view=4', 4, .5),
]
log = []
def shoot(name, e, tgt):
    s = q('Presentation297', 'shot:deploy308_%s:%.2f,%.2f,%.2f:%.2f,%.2f,%.2f:fov=60:w=1600:h=900:hideplayer' % (name, *e, *tgt))
    src = Path(str(s.get('result', '')).strip())
    if s.get('status') == 'COMPLETE' and src.exists(): shutil.copy(src, out / (name + '.png'))
    return s
try:
    for name, spec, dist, drop in shots:
        if ONLY and name not in ONLY: continue
        r = q('DeployLook308', 'preview:on:%s:at=%.2f,%.2f,%.2f:yaw=%.1f' % (spec, AT[0], AT[1], AT[2], YAW))
        head = str(r.get('result') or r.get('error')).replace('\r', '').replace('\n', ' | ')
        s = shoot(name, eye, (eye[0] + fx * dist, eye[1] - drop, eye[2] + fz * dist))
        log.append((name, head[:420], s.get('status'), str(s.get('error', ''))[:120])); print(log[-1], flush=True)
    if not ONLY or 'foot' in ONLY:
        steps, stride = 10, .68
        r = q('DeployLook308', 'preview:on:foot:%d:at=%.2f,%.2f,%.2f:yaw=%.1f' % (steps, AT[0], AT[1], AT[2], YAW))
        # the way the player sees them: standing at the end of the trail, looking back and down
        end = stride * (steps + 1.4)
        e2 = (AT[0] + fx * end, AT[1] + 1.6, AT[2] + fz * end)
        back = stride * (steps - 2.5)
        s = shoot('foot', e2, (AT[0] + fx * back, AT[1], AT[2] + fz * back))
        print('foot', str(r.get('result')).replace('\r', '').replace('\n', ' | ')[:300], s.get('status'), flush=True)
        r = q('DeployLook308', 'preview:on:foot:%d:age=1.8:at=%.2f,%.2f,%.2f:yaw=%.1f' % (steps, AT[0], AT[1], AT[2], YAW))
        s = shoot('foot_old', e2, (AT[0] + fx * back, AT[1], AT[2] + fz * back))
        print('foot_old', s.get('status'), flush=True)
finally:
    print('off:', str(q('DeployLook308', 'preview:off').get('result'))[:200])
    print('status:', str(q('DeployLook308', 'status').get('result'))[:300])
