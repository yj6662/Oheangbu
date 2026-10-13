"""#308 GreatBrush: 자세 자료(pose.py 가 읽는다). 좌표: 캐릭터는 -Y 를 본다, 오른쪽 = -X, 위 = +Z (m).
L = 뼈별 국소 회전 (앞으로 숙임, 옆으로 기울임, 왼쪽으로 돌림) 도. head = 머리의 월드 회전(같은 순서). feet.t = 발목 이동.
brush.G = GripSocket(오른손) 자리, brush.d = 붓끝 방향. 왼손은 G - d * HAND_GAP.
hands.<손>.phi = 손이 자루 둘레 어느 쪽에서 쥐는지(도) — 없으면 손목이 가장 덜 꺾이는 값을 찾는다. hint = 팔꿈치가 가는 쪽.
refine = `--refine` 때 G · d 를 처음 값 둘레에서 다듬는 범위(찾은 값은 다시 여기에 적는다 — 평소 실행은 적힌 값 그대로)."""
HAND_GAP = .42
GRIP_FROM_FERRULE = .50
TWIST_SHARE = .55          # 손목 비틀림 가운데 아래팔이 가져가는 몫(비틀림 뼈가 없는 리그)
SHOULDER_PIVOT_Z = 1.60    # 게임 카메라 축(머리 높이)
FIT = .80                  # 3/4 화면 채움(작을수록 가까이)
SHOULDER_PITCH = 0         # 어깨 카메라 피치(0 = 수평으로 앞을 본다 — 게임 기본, 무릎 아래는 화면 밖)
SLANTS = (0, 20, 40)       # 자루가 손바닥을 가로지르는 각: 0 = 망치 쥐기(자루 ⟂ 아래팔), 40 = 악수 쥐기(창 · 칼처럼 자루가 아래팔 쪽으로 눕는다)

# 쥐기(두 손 공통): palm = 뼈 평면에서 손바닥 살까지, back = 자루가 손가락 뿌리에서 손목 쪽으로 물러난 거리, rf/rt = 손가락 · 엄지 반지름
GRIP = dict(palm=.016, back=.008, rf=.009, caps=(85, 100, 75), squeeze=0,
            thumb_psi=25, thumb_along=-.15, thumb_caps=(70, 80), rt=.012, thumb_roll=0)

POSES = {
    'a_carry': dict(
        label='a 들기(몸 앞 비스듬히)',
        L={'Spine01': (0, 0, -3), 'Spine': (0, 0, -3)}, head=(0, 0, 0),
        feet={'Left': dict(t=(.03, 0, 0), yaw=8), 'Right': dict(t=(-.03, 0, 0), yaw=-8)},
        brush=dict(G=(-0.227, -0.359, 1.363), d=(-0.652, -0.222, 0.725)), refine=dict(),
        hands_closeup=(-.2, -.95, .3)),
    # 어깨에 걸치는 들기(a2)는 시도 후 뺐다: G=(-.17,-.20,1.34) d=(-.40,.40,.82) 에서 왼손 닿는 거리 1.07(안 닿음) · 손목 71도. 손 간격 0.42 m + 팔 0.51 m 로는 두 손을 쥔 채 어깨에 못 얹는다.
    'b_write': dict(
        label='b 쓰기 준비',
        L={'Hips': (2, 0, 12), 'Spine02': (3, 0, 8), 'Spine01': (3, 0, 8), 'Spine': (2, 0, 6)}, head=(0, 0, 0),
        feet={'Left': dict(t=(.06, .14, 0), yaw=40), 'Right': dict(t=(0, -.14, 0), yaw=0)},
        brush=dict(G=(.14, -.46, 1.26), d=(-.143, -.857, .50)), refine=dict(tip=(0, -1.28, 1.74)), bristle=dict(press=.45, bend=(22, (1, 0, 0)))),
    'c_sweep': dict(
        label='c 쓸어 던지기 정점', cam_dir=(-.15, -.95, .28),
        hips_t=(.04, -.05, -.10),
        L={'Hips': (6, 0, 24), 'Spine02': (6, 0, 16), 'Spine01': (6, 0, 16), 'Spine': (4, 0, 12)}, head=(4, 0, 30),
        feet={'Left': dict(t=(.14, .20, 0), yaw=55), 'Right': dict(t=(-.10, -.22, 0), yaw=12)},
        brush=dict(G=(0.402, -0.486, 1.040), d=(0.381, -0.921, 0.081)), refine=dict(wd=4), bristle=dict(splay=.5, bend=(24, (0, 0, -1)))),
    'd_arc': dict(
        label='d 크게 돌려 쓸기 정점', cam_dir=(.45, -.85, .25),
        L={'Hips': (0, 0, -8), 'Spine02': (-4, -3, -8), 'Spine01': (-4, -3, -8), 'Spine': (-3, -2, -6)}, head=(-10, 0, -10),
        feet={'Left': dict(t=(.06, -.08, 0), yaw=0), 'Right': dict(t=(-.08, .10, 0), yaw=-25)},
        brush=dict(G=(0.071, -0.151, 1.752), d=(-0.794, -0.358, 0.491)), refine=dict(), bristle=dict(splay=.4, bend=(20, (0, 1, 0)))),
    'e_slam': dict(
        label='e 내리찍기 정점',
        hips_t=(0, .03, -.22),
        L={'Hips': (15, 0, 8), 'Spine02': (12, 0, 7), 'Spine01': (10, 0, 6), 'Spine': (8, 0, 4)}, head=(28, 0, 0),
        feet={'Left': dict(t=(.06, .16, 0), yaw=25), 'Right': dict(t=(-.04, -.22, 0), yaw=0)},
        brush=dict(G=(-.070, -.710, .835), d=(.056, -.539, -.840)), refine=dict(tip=(0, -1.15, .08)), bristle=dict(press=1, splay=.5, bend=(40, (1, 0, 0)))),
    'f_plant': dict(
        label='f 세워 들기(꽂기 아님 — 꼬리 0.8 m 위)',
        hips_t=(0, .02, -.16),
        L={'Hips': (6, 0, 0), 'Spine02': (4, 0, 0), 'Spine01': (4, 0, 0)}, head=(8, 0, 0),
        feet={'Left': dict(t=(.14, .06, 0), yaw=25), 'Right': dict(t=(-.14, -.04, 0), yaw=-20)},
        brush=dict(G=(-0.015, -0.483, 1.421), d=(-0.093, -0.121, 0.988)), refine=dict()),
    'g_drawin': dict(
        label='g 끌어당기기',
        hips_t=(0, .02, -.03),
        L={'Hips': (-2, 0, 0), 'Spine02': (-2, 0, -3), 'Spine01': (-2, 0, -3), 'Spine': (0, 0, -3)}, head=(-4, 0, 0),
        feet={'Left': dict(t=(.03, -.05, 0), yaw=6), 'Right': dict(t=(-.04, .06, 0), yaw=-15)},
        brush=dict(G=(-0.140, -0.200, 1.500), d=(-0.260, 0.300, 0.920)), refine=dict()),
}
