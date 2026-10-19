"""D308-120 field enemies, grotesque pass (SPEC-ENEMY-BODIES-308): the design of every part in body terms -> parts[] rows.

Bone axes are the idle axes printed by `Creatures308 parts:probe:<key>` (fox: Rig298 from rig_creatures like the boar, but its Pelvis
rolls like the boar's Spine - measured with probe parts at +x/+y/+z, 2026-10-20; fox
cannot be probed, its body is a scaled template). Each wish says, in body space (x right, y up, z forward): where the part sits
(off, metres from the bone), where its drawn top points (up) and its drawn front (fwd), and how long its longest side is (length).
Part axes as Creatures308 holds them (the FBX root's -90 X is zeroed): drawn top = part +Z, drawn front = part -Y (Blender Z / -Y).
The BearPart308 rows (crystal, shackle, rope) keep the parent's calibrated form - up = part +Y, fwd = part +Z (a ring's axis).
  python grotesque_design.py            -> grotesque_rows.json (then grotesque_rows.py grotesque_rows.json)
Light = one or two parts (pack animals, silhouettes stay readable in groups); strong = bandit, boar, turtle (Claude's pick, D308-120).
Nothing glows; the attacking limb / head stays clear (the telegraph).
"""
import json
import re
from pathlib import Path

from part_euler import dot, norm, cross, euler_from_matrix

HERE = Path(__file__).resolve().parent
P = 'Assets/_Project/Art/Characters/Creatures308/Parts/'
AXES = {   # parts:probe idle axes (body space)
    'jackal/Spine4': 'x(1.00,0.02,0.01) y(-0.02,0.25,0.97) z(0.01,-0.97,0.25)',
    'jackal/Spine5': 'x(1.00,0.01,0.03) y(-0.03,0.73,0.69) z(-0.01,-0.69,0.73)',
    'wolf/Neck': 'x(1.00,-0.07,-0.03) y(0.08,0.85,0.53) z(-0.01,-0.53,0.85)',
    'wolf/LegBack2.R': 'x(0.99,-0.11,0.10) y(-0.03,-0.80,-0.60) z(0.15,0.59,-0.80)',
    'bandit/Chest': 'x(-0.01,-1.00,-0.07) y(0.50,-0.06,0.86) z(-0.86,-0.03,0.50)',
    'bandit/LeftShoulder': 'x(0.93,0.16,-0.33) y(-0.36,0.25,-0.90) z(-0.06,0.96,0.29)',
    'bandit/Head': 'x(0.00,-1.00,-0.10) y(0.03,-0.10,0.99) z(-1.00,0.00,0.03)',
    'bandit/LeftLowerArm': 'x(-0.04,0.53,-0.85) y(-0.54,-0.72,-0.43) z(-0.84,0.44,0.31)',
    'rig298/Chest': 'x(1.00,0.00,0.00) y(0.00,-0.06,1.00) z(0.00,-1.00,-0.06)',
    'rig298/Spine': 'x(-1.00,0.00,0.00) y(0.00,0.00,1.00) z(0.00,1.00,0.00)',
    'rig298/Pelvis': 'x(1.00,0.00,0.00) y(0.00,0.10,0.99) z(0.00,-0.99,0.10)',
    'turtle/Shell': 'x(0.00,0.01,-1.00) y(-0.03,1.00,0.01) z(1.00,0.03,0.00)',
}


def place(axes, up, fwd, off, keep=''):
    a = dict(re.findall(r'([xyz])\(([-0-9., ]+)\)', axes))
    bx, by, bz = (norm([float(v) for v in a[k].split(',')]) for k in 'xyz')
    up, fwd = norm(up), list(fwd)
    if keep == 'fwd':
        fwd = norm(fwd)
        up = norm([u - dot(up, fwd) * f for u, f in zip(up, fwd)])
    else:
        fwd = norm([f - dot(fwd, up) * u for f, u in zip(fwd, up)])
    right = cross(up, fwd)
    tb = lambda d: [dot(d, bx), dot(d, by), dot(d, bz)]
    cx, cy, cz = tb(right), tb(up), tb(fwd)
    return [round(v, 3) for v in tb(off)], euler_from_matrix([[cx[i], cy[i], cz[i]] for i in range(3)])


def part(kind, axes_key, bone, length, off, up, fwd=(0, 0, 1), grip=.5, keep='', note='', **extra):
    name = {'crystal': 'BearPart308_crystal', 'shackle': 'BearPart308_shackle', 'rope': 'BearPart308_rope'}.get(kind, 'GrotPart308_' + kind)
    if name.startswith('GrotPart308_'):   # drawn top exact on part +Z, drawn front on part -Y
        pos, euler = place(AXES[axes_key], [-f for f in fwd], up, off, 'fwd')
    else:
        pos, euler = place(AXES[axes_key], up, fwd, off, keep)
    # tint [1,1,1]: the part keeps its own colour under the species' fur tint (bone stays bone, crystal stays grey-green)
    row = dict(prefab=P + name + '.fbx', albedo=P + name + '_albedo.png', bone=bone, length=length, grip01=grip, pos=pos, euler=euler, tint=[1, 1, 1], note=note)
    row.update(extra)
    return row


BONE = dict(saturation=.5, value=.98)       # pale raw bone (spurs, jaw)
STONE = dict(saturation=.45, value=1.0)     # dull grey-green maseok crystal, never lit
IRON = dict(saturation=0, value=.95)        # rust iron (shackle, stake) - light enough to read on a dark coat
PAPER = dict(smoothness=.05, saturation=.7, value=1.0)   # aged paper, red cinnabar writing
ROWS = {
    # light: bone spurs breaking out along the spine + one small crystal at the withers (pack animal, head and forepaws clear)
    'jackal': [
        part('spurs', 'jackal/Spine4', 'Spine4', .45, (0, .2, -.05), (0, 1, -.35), note='등뼈를 뚫고 나온 뼈 가시(물든 몸)', **BONE),
        part('crystal', 'jackal/Spine5', 'Spine5', .26, (.06, .16, 0), (.6, 1, 0), note='어깨 위 오른쪽 작은 마석 결정', **STONE),
    ],
    # light, different from the jackal: a second jaw on the right of the neck (남는 입; pale bone tint so it reads on the dark coat at
    # fighting distance) + a broken shackle on the right hind leg (the chain trails toward the tail, so it lies along the ground on death)
    'ember_dog': [
        part('jaw', 'wolf/Neck', 'Neck', .36, (.1, -.06, .03), (1, .3, 0), (0, 0, 1), note='목 오른쪽에 돋은 남는 턱(이빨)', saturation=.3, value=.98, tint=[1.5, 1.45, 1.3]),
        part('shackle', 'wolf/LegBack2.R', 'LegBack2.R', .4, (0, 0, 0), (0, 0, -1), (-.03, -.80, -.60), grip=.76, keep='fwd', note='오른 뒷다리 끊긴 족쇄(사슬은 꼬리 쪽으로 끌림)', **IRON),
    ],
    # strong: the only human - a talisman nailed over the face (turned a little to the right so it reads from the side too),
    # crystals through the back and the left shoulder, a shackle on the left wrist; the right arm and the axe (the telegraph) stay clear
    'bandit': [
        part('talisman', 'bandit/Head', 'Head', .32, (.02, .11, .19), (0, 1, 0), (.45, 0, .9), note='얼굴을 덮은 못 박힌 부적', **PAPER),
        part('crystal', 'bandit/Chest', 'Chest', .5, (0, .14, -.13), (.25, .9, -1), (0, 1, 0), note='등을 뚫고 나온 마석 결정', **STONE),
        part('crystal', 'bandit/LeftShoulder', 'LeftShoulder', .32, (-.05, .08, -.03), (-.4, 1, -.3), note='왼 어깨 결정', **STONE),
        part('shackle', 'bandit/LeftLowerArm', 'LeftLowerArm', .4, (.008, -.106, .17), (0, 1, 0), (-.04, .53, -.85), grip=.76, keep='fwd', note='왼 손목 족쇄 + 끊긴 사슬', **IRON),
    ],
    # strong: two clusters of spine spurs along the ridge, a hunter's iron stake still in the haunch, a tumor with eyes on the right
    # flank (남는 눈), a crystal on the left hip; the head and tusks (the charge) stay clear
    'boar': [
        part('spurs', 'rig298/Chest', 'Chest', .45, (0, .22, 0), (0, 1, -.3), note='어깨 위 등뼈 가시', **BONE),
        part('spurs', 'rig298/Spine', 'Spine', .36, (0, .22, 0), (0, 1, .25), (0, 0, -1), note='허리 등뼈 가시', **BONE),
        part('stake', 'rig298/Pelvis', 'Pelvis', .6, (-.08, .17, -.1), (-.5, 1, -.6), grip=.35, note='엉덩이에 박힌 채 남은 사냥꾼 쇠말뚝', **IRON),
        part('tumor', 'rig298/Spine', 'Spine', .34, (.19, .02, 0), (1, .3, 0), (0, .3, 1), note='오른 옆구리 눈 달린 혹(남는 눈)'),
        part('crystal', 'rig298/Pelvis', 'Pelvis', .32, (-.12, .15, .05), (-.7, 1, -.2), note='왼 엉덩이 결정', **STONE),
    ],
    # light: a talisman nailed to the right haunch + a straw 금줄 round the neck (a fox someone tried to bind); both darker and warmer
    # than the paper elsewhere so they separate from the white fur
    'fox': [
        part('talisman', 'rig298/Spine', 'Pelvis', .36, (.17, -.02, 0), (0, 1, 0), (1, 0, 0), note='오른 엉덩이에 못 박힌 부적', smoothness=.05, saturation=.7, value=.55, tint=[.8, .65, .45]),
        part('rope', 'rig298/Chest', 'Neck', .34, (0, 0, 0), (0, -.06, 1), (0, 1, 0), saturation=.6, value=.5, tint=[.85, .7, .5], note='목 금줄(종이 수술)'),
    ],
    # strong: the shell bristles - crystals, two stakes driven in, a 금줄 round the rim with paper strips, a talisman on the right
    # side; the head (the bite) stays clear
    'turtle': [
        part('crystal', 'turtle/Shell', 'Shell', .6, (0, .2, -.12), (.2, 1, -.4), note='등껍질 한가운데를 뚫은 마석 결정', **STONE),
        part('crystal', 'turtle/Shell', 'Shell', .38, (.25, .15, .15), (.6, 1, .2), note='오른 앞 결정', **STONE),
        part('stake', 'turtle/Shell', 'Shell', .6, (-.25, .14, -.05), (-.6, 1, -.3), grip=.3, note='등껍질에 박힌 쇠말뚝', **IRON),
        part('stake', 'turtle/Shell', 'Shell', .5, (.15, .2, -.3), (.4, 1, -.8), grip=.3, note='뒤쪽 쇠말뚝', **IRON),
        part('rope', 'turtle/Shell', 'Shell', 1.02, (0, .02, 0), (0, 1, 0), saturation=0, value=.95, note='등껍질 둘레 금줄'),
        part('talisman', 'turtle/Shell', 'Shell', .32, (.45, .08, 0), (0, 1, 0), (1, 0, 0), note='오른 옆 부적', **PAPER),
    ],
}

if __name__ == '__main__':
    out = HERE / 'grotesque_rows.json'
    out.write_text(json.dumps(ROWS, ensure_ascii=False, indent=1), encoding='utf-8')
    for k, rows in ROWS.items():
        for r in rows:
            print(k, r['bone'], Path(r['prefab']).stem, 'pos', r['pos'], 'euler', r['euler'])
