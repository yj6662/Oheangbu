"""#308 GreatBrush motion2: common_m1 을 v5 붓 · motion2 출력으로 돌려 쓴다(common_m1 자체는 고치지 않는다)."""
import bpy, os
import common_m1 as C
C.OUT = C.GB + '/motion2'; C.BLEND = C.OUT + '/GreatBrush308_motion2.blend'; C.V3 = C.GB + '/v5/SM_GreatBrush308.blend'
PRE = 'A_Brush308_'
NAMES = ['SwingSweep', 'SwingArc', 'SwingSlam', 'GuardIn', 'GuardHold', 'BuffDraw', 'StanceIdle', 'CarryIdle', 'ToCarry', 'ToStance', 'SwingThrow', 'SwingStrikeFar']   # D308-84a: the last two
SHIFT = .30          # 오른손이 GripSocket 에서 꼬리 쪽으로 물러난 거리(모든 클립 공통)
SHIFT2 = -.08        # s4: 두 손 클립에서 오른손(앞손)이 쥐는 점 = GripSocket 에서 붓끝 쪽 +0.08 m (꼬리 끝에서 0.68 m). SHIFT(.30)는 어깨 걸치기에만 남는다
LZ = -.42            # s4: 왼손(꼬리 축) = GripSocketL = GripSocket 뒤 0.42 m (꼬리 끝에서 0.18 m, 오른손 뒤 0.50 m) — 소켓 자리는 옛 그대로
GAP = .12            # 오른손 쥠 점 → 왼손 = GripSocketL(GripSocket 뒤 0.42 m) 그대로


def act(name, suf=''): return PRE + name + suf


def assign_clip(name):
    g = C.G; ok = True
    for idb, suf in ((g['arm'], ''), (g['brush_rig'], '__Brush'), (g['brush'].data.shape_keys, '__Keys')):
        a = bpy.data.actions.get(act(name, suf))
        if a is None: ok = False; continue
        C.assign(idb, a)
    return ok


C.assign_clip = assign_clip
os.makedirs(C.OUT + '/metrics', exist_ok=True)
