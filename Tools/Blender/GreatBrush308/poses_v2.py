"""#308 GreatBrush 2차 시안: 자세 자료(pose.py --poses poses_v2). 형식은 poses.py 와 같다.
붓 = v2 비례(전체 1.25 m: 꼬리–오른손 0.48, 오른손–붓끝 0.77, 두 손 간격 0.34).
hands.<손>.free = 그 손은 쥐지 않는다(L 의 팔 회전 그대로). `--refine` 결과는 poses_v2_refined.json 에 적히고 평소 실행이 그 값을 쓴다."""
HAND_GAP = .34
GRIP_FROM_FERRULE = .30
TAIL_LEN = .48; TIP_LEN = .77; HEAD_LEN = .40; HEAD_R = .062
TWIST_SHARE = .55
SHOULDER_PIVOT_Z = 1.60
FIT = .80
SHOULDER_PITCH = 16        # 어깨 카메라를 아래로 16도(8도로는 바닥에서 0.42 m 까지가 화면 밖이라 발이 안 들어온다)
SLANTS = (0, 20, 40)
# 2차: 손바닥 살이 자루에 덜 파고들게 palm 을 5 mm 늘렸다(1차 .016)
GRIP = dict(palm=.021, back=.008, rf=.010, caps=(85, 100, 75), squeeze=0,
            thumb_psi=25, thumb_along=-.15, thumb_caps=(70, 80), rt=.013, thumb_roll=0)
STAND = {'Left': dict(t=(.03, 0, 0), yaw=8), 'Right': dict(t=(-.03, 0, 0), yaw=-8)}

POSES = {
    'a_carry': dict(
        label='a 들기(오른손 · 어깨에 걸침)',
        L={'LeftArm': (0, 76, 0), 'LeftForeArm': (0, 0, -14), 'LeftShoulder': (0, 4, 0)}, head=(0, 0, 0), feet=STAND,
        hands={'Left': dict(free=True), 'Right': dict(hint=(-.5, .2, -.85))},
        brush=dict(G=(-.20, -.27, 1.25), d=(.0, .75, .66)), refine=dict(wg=3, wd=6, g=.03, d=.04), hands_closeup=(-.75, -.6, .25)),
    'b_write': dict(
        label='b 쓰기 준비',
        L={'Hips': (2, 0, 12), 'Spine02': (3, 0, 8), 'Spine01': (3, 0, 8), 'Spine': (2, 0, 6)}, head=(0, 0, 0),
        feet={'Left': dict(t=(.06, .14, 0), yaw=40), 'Right': dict(t=(0, -.14, 0), yaw=0)},
        brush=dict(G=(.10, -.44, 1.27), d=(-.12, -.82, .56)), refine=dict(tip=(0, -1.06, 1.70)), bristle=dict(press=.45, bend=(22, (1, 0, 0))), hands_closeup=(-.8, -.45, .4)),
    'c1_windup': dict(
        label='c 준비', cam_dir=(-.15, -.95, .28),
        hips_t=(-.03, .03, -.08),
        L={'Hips': (2, 0, -18), 'Spine02': (2, 0, -14), 'Spine01': (2, 0, -14), 'Spine': (0, 0, -10)}, head=(2, 0, -8),
        feet={'Left': dict(t=(.12, -.14, 0), yaw=10), 'Right': dict(t=(-.12, .16, 0), yaw=-40)},
        brush=dict(G=(-.42, -.02, 1.27), d=(-.70, .62, .35)), refine=dict()),
    'c_sweep': dict(
        label='c 쓸어 던지기 정점', cam_dir=(-.15, -.95, .28),
        hips_t=(.05, -.04, -.12),
        L={'Hips': (5, 0, 22), 'Spine02': (5, 0, 16), 'Spine01': (5, 0, 16), 'Spine': (3, 0, 12)}, head=(3, 0, 28),
        feet={'Left': dict(t=(.16, .14, 0), yaw=50), 'Right': dict(t=(-.14, -.16, 0), yaw=10)},
        brush=dict(G=(.24, -.44, 1.22), d=(.88, -.44, .16)), refine=dict(wd=5), bristle=dict(splay=.6, bend=(26, (0, 0, -1)))),
    'c3_follow': dict(
        label='c 여운', cam_dir=(-.15, -.95, .28),
        hips_t=(.07, -.03, -.10),
        L={'Hips': (4, 0, 30), 'Spine02': (4, 0, 20), 'Spine01': (4, 0, 20), 'Spine': (2, 0, 14)}, head=(3, 0, 40),
        feet={'Left': dict(t=(.16, .14, 0), yaw=60), 'Right': dict(t=(-.14, -.16, 0), yaw=20)},
        brush=dict(G=(.36, -.14, 1.30), d=(.72, .60, .34)), refine=dict(), bristle=dict(splay=.3, bend=(12, (0, 0, -1)))),
    'd1_windup': dict(
        label='d 준비', cam_dir=(.45, -.85, .25),
        hips_t=(-.02, .02, -.10),
        L={'Hips': (4, 0, -16), 'Spine02': (4, 0, -12), 'Spine01': (3, 0, -12), 'Spine': (2, 0, -8)}, head=(4, 0, -6),
        feet={'Left': dict(t=(.10, -.12, 0), yaw=5), 'Right': dict(t=(-.12, .14, 0), yaw=-35)},
        brush=dict(G=(-.40, -.10, 1.05), d=(-.55, .55, -.63)), refine=dict()),
    'd_arc': dict(
        label='d 크게 돌려 쓸기 정점', cam_dir=(.45, -.85, .25),
        L={'Hips': (0, 0, 4), 'Spine02': (-4, 0, 4), 'Spine01': (-4, 0, 4), 'Spine': (-3, 0, 3), 'LeftShoulder': (0, -12, 0), 'RightShoulder': (0, 12, 0)}, head=(-12, 0, 0),
        feet={'Left': dict(t=(.10, -.06, 0), yaw=10), 'Right': dict(t=(-.10, .08, 0), yaw=-20)},
        brush=dict(G=(-.12, -.26, 1.86), d=(-.42, .10, .90)), refine=dict(), bristle=dict(splay=.4, bend=(22, (0, -1, 0)))),
    'd3_follow': dict(
        label='d 여운', cam_dir=(.45, -.85, .25),
        hips_t=(.05, -.03, -.14),
        L={'Hips': (8, 0, 22), 'Spine02': (7, 0, 14), 'Spine01': (6, 0, 14), 'Spine': (4, 0, 10)}, head=(8, 0, 22),
        feet={'Left': dict(t=(.16, .12, 0), yaw=45), 'Right': dict(t=(-.14, -.14, 0), yaw=5)},
        brush=dict(G=(.26, -.44, 1.02), d=(.78, -.38, -.50)), refine=dict(), bristle=dict(splay=.5, bend=(20, (0, 0, -1)))),
    'e1_windup': dict(
        label='e 준비', cam_dir=(.62, -.74, .25),
        L={'Hips': (-3, 0, -6), 'Spine02': (-5, 0, -5), 'Spine01': (-5, 0, -5), 'Spine': (-4, 0, -4), 'LeftShoulder': (0, -12, 0), 'RightShoulder': (0, 12, 0)}, head=(-6, 0, 0),
        feet={'Left': dict(t=(.08, -.10, 0), yaw=5), 'Right': dict(t=(-.10, .12, 0), yaw=-25)},
        brush=dict(G=(-.16, -.20, 1.84), d=(-.15, .55, .82)), refine=dict()),
    'e_slam': dict(
        label='e 내리찍기 정점(왼쪽 옆)', cam_dir=(.62, -.74, .25),
        hips_t=(.03, .06, -.24),
        L={'Hips': (16, 0, 14), 'Spine02': (11, 0, 10), 'Spine01': (9, 0, 10), 'Spine': (6, 0, 6)}, head=(22, 0, 10),
        feet={'Left': dict(t=(.14, .16, 0), yaw=40), 'Right': dict(t=(-.10, -.20, 0), yaw=0)},
        brush=dict(G=(.28, -.39, .67), d=(.28, -.60, -.75)), refine=dict(tip=(.50, -.85, .09)), bristle=dict(press=1, splay=.5, bend=(40, (.9, .42, 0)))),
    'e3_follow': dict(
        label='e 여운', cam_dir=(.62, -.74, .25),
        hips_t=(.03, .08, -.30),
        L={'Hips': (20, 0, 14), 'Spine02': (12, 0, 10), 'Spine01': (10, 0, 10), 'Spine': (6, 0, 6)}, head=(26, 0, 10),
        feet={'Left': dict(t=(.14, .16, 0), yaw=40), 'Right': dict(t=(-.10, -.20, 0), yaw=0)},
        brush=dict(G=(.28, -.36, .56), d=(.30, -.70, -.64)), refine=dict(tip=(.51, -.90, .10)), bristle=dict(press=1, splay=.8, bend=(55, (.9, .42, 0)))),
    'f_guard': dict(
        label='f 세워 들어 막기',
        hips_t=(0, .03, -.13),
        L={'Hips': (5, 0, 0), 'Spine02': (4, 0, 0), 'Spine01': (3, 0, 0)}, head=(6, 0, 0),
        feet={'Left': dict(t=(.13, -.10, 0), yaw=12), 'Right': dict(t=(-.13, .10, 0), yaw=-28)},
        brush=dict(G=(.0, -.47, 1.30), d=(.0, -.06, 1.0)), refine=dict(wg=6, wd=6)),
    'g_drawin': dict(
        label='g 끌어당기기',
        hips_t=(0, .03, -.08),
        L={'Hips': (3, 0, 10), 'Spine02': (3, 0, 8), 'Spine01': (3, 0, 8), 'Spine': (2, 0, 5)}, head=(8, 0, -6),
        feet={'Left': dict(t=(.08, .10, 0), yaw=25), 'Right': dict(t=(-.08, -.10, 0), yaw=-5)},
        brush=dict(G=(.05, -.40, .93), d=(-.45, -.80, -.40)), refine=dict()),
}
