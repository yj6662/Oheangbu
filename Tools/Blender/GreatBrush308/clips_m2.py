"""#308 GreatBrush motion2: 게임용 클립 시간표. knots = (클립 시각 s, 원본 프레임) — 속도 배율이 여기서 구워진다(게임은 1.0배 재생).
blend_in/out = 전투 태세(Great_Sword_Idle 1프레임)와 섞는 길이. twist = 윗몸 요(도, 오른쪽. 40 · 55 는 뒤 카메라에서 붓머리가 몸에 가렸고 68 에서 보였다 → 65). root: none | lowpass(엉덩이 수평 이동의 느린 성분을 뺀다) | (dx, dy)(원본 구간 전체에 걸쳐 고르게 뺀다, 원본 m)."""
CLIPS = {
    'SwingSweep': dict(src='Great_Sword_Power_Slash', knots=[(0, 1), (.25, 22), (.45, 29)], blend_in=.06, blend_out=.27, root='lowpass', apex=.25, spec=.25, use='공격 단일'),
    'SwingArc': dict(src='Standing_Melee_Attack_360_High', knots=[(0, 11), (.50, 33), (.80, 47)], blend_in=.10, blend_out=.35, root='lowpass', apex=.50, spec=.50, use='공격 광역'),
    'SwingSlam': dict(src='Great_Sword_Downward_Slash', knots=[(0, 1), (.55, 24.5), (.80, 27.5)], blend_in=.06, blend_out=.30, root='none', apex=.55, spec=.55, twist=65, use='내리찍기'),
    'GuardIn': dict(src='Great_Sword_Standing_To_Block', knots=[(0, 1), (.15, 16), (.30, 16)], blend_in=.0, blend_out=0, root=(-.106, .027), apex=.15, spec=.15, use='막기 전환'),
    'GuardHold': dict(src='Great_Sword_Blocking_Idle', knots=[(0, 1), (29 / 30, 30)], blend_in=0, blend_out=0, root='none', loop=True, use='막기 유지(루프)'),
    'BuffDraw': dict(src='Great_Sword_Power_Up', knots=[(0, 1), (.30, 13), (.60, 25)], blend_in=0, blend_out=.25, root='none', apex=.30, spec=.30, use='버프'),
    'StanceIdle': dict(src='Great_Sword_Idle', knots=[(0, 1), (2.0, 61)], blend_in=0, blend_out=0, root='none', loop=True, use='전투 태세(루프)'),
    'CarryIdle': dict(special='carry_idle', seconds=3.0, loop=True, use='평상시 어깨 걸치기(루프 · 손으로 만듦)'),
    'ToCarry': dict(special='to_carry', seconds=.70, use='전환: 태세 → 어깨(손으로 만듦)'),
    'ToStance': dict(special='to_stance', seconds=.35, use='전환: 어깨 → 태세(손으로 만듦)'),
}
CARRY_G = (-.20, -.27, 1.295); CARRY_D = (0, .75, .66)

# 확인 스틸에 쓰는 프레임(클립 프레임 번호, 0 부터) — 6장
SHOTS = {'SwingSweep': [0, 4, 7, 10, 14, 22], 'SwingArc': [0, 8, 12, 15, 20, 34], 'SwingSlam': [0, 8, 13, 17, 21, 33], 'GuardIn': [0, 2, 3, 4, 5, 9], 'GuardHold': [0, 6, 12, 18, 24, 29],
         'BuffDraw': [0, 5, 9, 13, 18, 25], 'StanceIdle': [0, 12, 24, 36, 48, 60], 'CarryIdle': [0, 18, 36, 54, 72, 90], 'ToCarry': [0, 4, 9, 13, 17, 21], 'ToStance': [0, 2, 4, 6, 8, 10]}
