"""#308 GreatBrush motion2: 게임용 클립 시간표. knots = (클립 시각 s, 원본 프레임) — 속도 배율이 여기서 구워진다(게임은 1.0배 재생).
blend_in/out = 전투 태세(Great_Sword_Idle 1프레임)와 섞는 길이. twist = 윗몸 요(도, 오른쪽. 40 · 55 는 뒤 카메라에서 붓머리가 몸에 가렸고 68 에서 보였다 → 65). root: none | lowpass(엉덩이 수평 이동의 느린 성분을 뺀다) | (dx, dy)(원본 구간 전체에 걸쳐 고르게 뺀다, 원본 m)."""
# swing3: 대기 자세에 얹는 움직임(solve_m2.life). 각 = 도, 길이 = cm. 모든 항은 루프 첫 · 끝 프레임에서 0 이라 첫 프레임 자세(= 전환 · 휘두르기가 시작 · 끝나는 자세)는 그대로다.
LIFE_STANCE = dict(breath_k=2, breath_deg=1.1, shoulder_deg=2.0, bob_cm=.25, sway_cm=1.2, sway_y_cm=.5, sway_roll=1.2, chest_yaw=1.6, head_yaw=3.0, head_pitch=1.0, arm_deg=(1.3, 1.1), hand_deg=.9)
LIFE_CARRY = dict(breath_k=2, breath_deg=1.3, shoulder_deg=2.2, bob_cm=.3, sway_cm=1.5, sway_y_cm=.6, sway_roll=1.5, chest_yaw=2.0, head_yaw=5.0, head_pitch=1.5, shaft_pitch=1.6, shaft_yaw=1.3, shaft_follow=.9, hang_deg=3.0)
# ===== s4(2026-10-13 저녁): 새 쥐기 규약. 오른 어깨가 앞(몸을 왼쪽으로 돌림 · 오른발 앞). 오른손 = 앞손(붓머리 쪽): 손바닥 · 네 손가락이 자루를 밑에서 감싸고 엄지는 위 — 붓을 조종한다.
# 왼손 = 꼬리 끝의 축(왼 허리 옆): 손등이 위(덮어 쥐기). 열 클립 모두 손으로 만든 열쇠 자세(kind='keyed', solve_m2.build_keyed). 옛 모캡 시간표는 _backup_swing3/scripts/clips_m2.py.
# STANCE4: hy · cy = 골반 · 가슴 요(도, + = 오른쪽), lean = 앞으로 숙임, hd · hp = 머리 요 · 숙임(월드), hips = 쉬는 자세에서 옮긴 양(m), lf · rf = 발목 (x, y, 발끝 요), 좌표: 앞 = -Y, 오른쪽 = -X.
# brush = 왼손(꼬리 축) 점 p(m) + 자루 방향(yaw: 0 = 정면, + = 오른쪽 / pitch: + = 위). 오른손 = p + d × 0.50.  nR · nL = 손바닥 방향 선호(약하게).
STANCE4 = dict(hy=-30, cy=-42, lean=9, hd=0, hp=2, hips=(.02, .02, -.075), rf=(-.11, -.24, -8), lf=(.15, .22, -60), p=(.24, -.03, 1.02), yaw=10, pitch=13, nR=(0, 0, 1), nR_soft=60, nL=(.25, 0, -.95), nL_soft=60)
THUMB_UP = dict()                                                         # 오른손 손가락 설정을 따로 줄 때 쓴다(지금은 기본 쥐기 = 엄지가 자루 위를 덮는다)
HINT_L = (.8, .3, -.5)                                                    # 왼 팔꿈치 방향(가슴 기준: 왼쪽 · 뒤 · 아래)
LHINT_FWD = .2                                                            # 왼 팔꿈치를 자루 꼬리 반대쪽(붓끝 쪽)으로 치우치게 하는 정도 — 꼬리가 왼 아래팔을 지나가지 않게
LEFT_LOOSE = .999                                                         # 왼손 손가락을 쥐는 정도
LIFE4_STANCE = dict(breath_k=2, breath_deg=1.7, shoulder_deg=2.8, bob_cm=.7, sway_cm=2.4, sway_y_cm=1.1, sway_roll=1.9, chest_yaw=3.0, head_yaw=4.0, head_pitch=1.5, brush_follow=.75, tip_yaw=2.2, tip_pitch=1.9)
LIFE4_GUARD = dict(breath_k=1, breath_deg=1.0, shoulder_deg=1.6, bob_cm=.3, sway_cm=.7, sway_y_cm=.4, sway_roll=.7, chest_yaw=1.0, head_yaw=1.5, head_pitch=.6, brush_follow=.8, tip_yaw=.8, tip_pitch=.7)
_Z = (0, 0, 0, 0, 0, 0, 0, 0, 0)
_G = (4, 8, -3, 0, 0, .03, -.03, 0, -2)                 # 막기 몸
_GB = (0, .21, -.08, 1.02, 50, 44)                      # 막기 붓
# body 열쇠 = (t, 골반 요, 가슴 요, 숙임, 옆 기울임(+ = 왼쪽), dx, dy, dz, 머리 요, 머리 숙임) — 전투 태세 대비 차이
# brush 열쇠 = (t, 틀 요 fy, px, py, pz, yaw, pitch) — p · yaw 는 세로축 둘레로 fy 만큼 오른쪽으로 돌린 틀에서 적는다(월드 yaw = fy + yaw). 'S' = 전투 태세.
CLIPS = {
    'StanceIdle': dict(kind='keyed', seconds=4.0, loop=True, body=[(0,) + _Z], brush=[(0, 'S')], life=LIFE4_STANCE, use='전투 태세(루프)'),
    'SwingSweep': dict(kind='keyed', seconds=1.0, apex=.40, spec=.40, use='공격 단일(돌지 않고 크게 긋기)',
                       body=[(0,) + _Z, (.17, -6, -12, -5, 2, 0, .02, .01, -3, -5), (.40, 10, 26, 12, -2, 0, -.05, -.05, 4, 6), (.48, 11, 28, 13, -2, 0, -.055, -.055, 4, 7), (.72, 4, 10, 4, 0, 0, -.02, -.02, 1, 2), (1.0,) + _Z],
                       brush=[(0, 'S'), (.17, 0, .27, -.18, 1.04, -25, 72), (.40, 0, .20, -.20, 1.12, 50, -21), (.48, 0, .20, -.205, 1.115, 52, -24), (.72, 0, .23, -.10, 1.01, 22, 0), (1.0, 'S')]),
    'SwingArc': dict(kind='keyed', seconds=1.4, apex=.65, spec=.65, use='공격 광역(넓게 쓸기)',
                     body=[(0,) + _Z, (.32, -10, -20, -2, 3, .02, .02, 0, -8, -2), (.65, 22, 50, 4, -3, 0, -.02, -.03, 10, 3), (.75, 24, 54, 4, -3, 0, -.02, -.03, 11, 3), (1.05, 10, 20, 2, -1, 0, -.01, -.01, 4, 1), (1.4,) + _Z],
                     brush=[(0, 'S'), (.32, 0, .29, -.04, 1.03, -14, 32), (.65, 0, .12, -.33, 1.06, 108, 3), (.75, 0, .12, -.33, 1.06, 114, 1), (1.05, 0, .21, -.14, 1.04, 48, 8), (1.4, 'S')]),
    'SwingSlam': dict(kind='keyed', seconds=1.3, apex=.70, spec=.70, use='내리찍기(소환 · 설치)',
                      body=[(0,) + _Z, (.28, -2, -4, -6, 0, 0, .02, .03, 0, -8), (.46, -3, -6, -8, 0, 0, .02, .04, 0, -10), (.70, 8, 20, 30, 0, 0, -.06, -.22, 4, 8), (.80, 8, 21, 31, 0, 0, -.06, -.23, 4, 9), (1.05, 3, 6, 9, 0, 0, -.03, -.07, 1, 5), (1.3,) + _Z],
                      brush=[(0, 'S'), (.28, 0, .18, -.24, 1.00, 5, 84), (.46, 0, .18, -.24, 1.04, 0, 88), (.70, 0, .21, -.24, .92, 48, -30), (.80, 0, .21, -.24, .91, 48, -31), (1.05, 0, .23, -.10, .97, 18, -5), (1.3, 'S')]),
    'GuardIn': dict(kind='keyed', seconds=.30, apex=.15, spec=.15, end='guard', use='막기 전환',
                    body=[(0,) + _Z, (.18,) + tuple(.85 * v for v in _G), (.30,) + _G], brush=[(0, 'S'), (.18, 0, .22, -.12, 1.02, 46, 41), (.30,) + _GB]),
    'GuardHold': dict(kind='keyed', seconds=2.0, loop=True, start='guard', end='guard', life=LIFE4_GUARD, use='막기 유지(루프)', body=[(0,) + _G], brush=[(0,) + _GB]),
    'BuffDraw': dict(kind='keyed', seconds=.8667, apex=.30, spec=.30, use='버프',
                     body=[(0,) + _Z, (.30, 6, 12, -5, 0, 0, .02, .03, 0, -8), (.40, 6, 13, -6, 0, 0, .02, .035, 0, -9), (.62, 3, 6, -2, 0, 0, .01, .015, 0, -3), (.8667,) + _Z],
                     brush=[(0, 'S'), (.30, 0, .19, -.27, 1.03, 10, 80), (.40, 0, .19, -.27, 1.06, 12, 83), (.62, 0, .23, -.14, 1.02, 10, 45), (.8667, 'S')]),
    'CarryIdle': dict(special='carry_idle', seconds=4.0, loop=True, life=LIFE_CARRY, use='평상시 어깨 걸치기(루프 · 손으로 만듦)'),
    'ToCarry': dict(special='to_carry', seconds=.70, use='전환: 태세 → 어깨(손으로 만듦)'),
    'ToStance': dict(special='to_stance', seconds=.35, use='전환: 어깨 → 태세(손으로 만듦)'),
}

# carry2(2026-10-13): 어깨 걸치기 = 자루를 오른 어깨에 얹는다. 옛 값 CARRY_G = (-.20, -.27, 1.295); CARRY_D = (0, .75, .66) 은 자루가 어깨 위에 떠서 41도로 서 있었다(_backup_carry1).
# CARRY_S = 자루 축이 어깨를 지나는 점, PITCH = 수평 위 각(도), YAW = 붓머리가 바깥(오른쪽)으로 벌어진 각(도), L = 그 점에서 오른손 쥠 점까지 자루를 따라 앞으로 간 거리.
CARRY_S = (-.18, -.07, 1.457); CARRY_PITCH = 30.0; CARRY_YAW = 7.0; CARRY_L = .38
CARRY_BUMP = (-.05, -.05, .10)        # 전환 도중 손이 지나는 길의 볼록함(m)
def carry_gd(S=None, pitch=None, yaw=None, L=None):
    import math
    S = CARRY_S if S is None else S; pitch = math.radians(CARRY_PITCH if pitch is None else pitch); yaw = math.radians(CARRY_YAW if yaw is None else yaw); L = CARRY_L if L is None else L
    d = (-math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch))
    return tuple(S[i] - L * d[i] for i in range(3)), d
CARRY_G, CARRY_D = carry_gd()

# 확인 스틸에 쓰는 프레임(클립 프레임 번호, 0 부터) — 6장
SHOTS = {'SwingSweep': [0, 4, 6, 9, 12, 14, 21, 30], 'SwingArc': [0, 6, 10, 15, 19, 22, 31, 42], 'SwingSlam': [0, 6, 10, 15, 18, 21, 24, 39], 'GuardIn': [0, 2, 4, 5, 7, 9], 'GuardHold': [0, 12, 24, 36, 48, 60],
         'BuffDraw': [0, 4, 7, 9, 12, 18, 22, 26], 'StanceIdle': [0, 24, 48, 72, 96, 120], 'CarryIdle': [0, 24, 48, 72, 96, 120], 'ToCarry': [0, 4, 9, 13, 17, 21], 'ToStance': [0, 2, 4, 6, 8, 10]}
