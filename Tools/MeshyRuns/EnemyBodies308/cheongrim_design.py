"""D308-120 청림 first hour, the rest: the part design of the 청림-only species in body terms -> cheongrim_spec.json
(then cheongrim_rows.py cheongrim_spec.json). Same axes convention as grotesque_design.py (parts:probe idle axes, body space x right,
y up, z forward). RootPart308_* are Meshy image-to-3d parts baked by parts_bake.py like the GrotParts: drawn top = part +Z,
drawn front = part -Y.
  python cheongrim_design.py
root_beast (위임 판단 Claude, 2026-10-20): the free goat body - the only four-legged silhouette 청림 does not already use (wolf,
bear, boar are taken) - with root antlers swallowing its horns, roots erupting from the back and moss bark over the withers.
The head front stays clear (its attack is a head butt, contact bone Jaw). Nothing glows.
"""
import json
from pathlib import Path

import grotesque_design as g

HERE = Path(__file__).resolve().parent
g.AXES.update({
    'goat/Head': 'x(0.00,-1.00,0.02) y(0.00,0.02,1.00) z(-1.00,0.00,0.00)',
    'goat/Neck1': 'x(0.00,-0.78,-0.63) y(0.00,-0.63,0.78) z(-1.00,0.00,0.00)',
    'goat/Spine1': 'x(0.00,-0.02,-1.00) y(0.00,-1.00,0.02) z(-1.00,0.00,0.00)',
    'goat/Spine2': 'x(0.00,-0.38,-0.93) y(0.00,-0.93,0.38) z(-1.00,0.00,0.00)',
    'bear/Chest': 'x(0.01,0.02,-1.00) y(0.00,-1.00,-0.02) z(-1.00,0.00,-0.01)',
    'bear/Spine2': 'x(0.00,-0.01,-1.00) y(0.00,-1.00,0.01) z(-1.00,0.00,0.00)',
    'bear/Root': 'x(0.00,-0.01,-1.00) y(0.00,-1.00,0.01) z(-1.00,0.00,0.00)',
    'goat/Pelvis': 'x(0.00,0.07,-1.00) y(0.00,-1.00,-0.07) z(-1.00,0.00,0.00)',
})


def root(kind, axes_key, bone, length, off, up, fwd=(0, 0, 1), grip=.5, note='', **extra):
    pos, euler = g.place(g.AXES[axes_key], [-f for f in fwd], up, off, 'fwd')
    row = dict(prefab=g.P + 'RootPart308_' + kind + '.fbx', albedo=g.P + 'RootPart308_' + kind + '_albedo.png', bone=bone, length=length,
               grip01=grip, pos=pos, euler=euler, tint=[1, 1, 1], note=note)
    row.update(extra)
    return row


BARK = dict(saturation=.45, value=.8)     # dark wet root bark, darker than the bone spurs
MOSS = dict(saturation=.6, value=.9)      # grey-green moss, dull
SPEC = {
    'species': {
        'root_beast': {
            'from': 'goat', 'length_m': 1.5, 'hp': 24, 'damage': 12, 'telegraph': 1.35,
            'note': 'D308-120: 청림 뿌리 짐승 = 무료 산양 몸 + 뿌리 부품(아귀 인간형 몸 대신). 청림에 없는 네발 실루엣(뿔)',
            'parts': [
                root('antler', 'goat/Head', 'Head_M_joint', .72, (0, .13, .08), (0, 1, -.15), grip=.08, note='뿔을 삼킨 뿌리 뿔(머리 앞은 비움 - 박치기 예고)', **BARK),
                root('clump', 'goat/Spine1', 'Spine1_M_joint', .62, (0, .25, 0), (0, 1, -.15), note='등을 뚫고 터져 나온 뿌리 덩이', **BARK),
                root('moss', 'goat/Spine2', 'Spine2_M_joint', .55, (0, .17, 0), (0, 1, 0), note='어깨 위 이끼 낀 나무껍질', **MOSS),
                root('clump', 'goat/Pelvis', 'Pelvis_joint', .4, (-.08, .17, -.05), (-.5, 1, -.5), note='왼 엉덩이 작은 뿌리', **BARK),
                root('clump', 'goat/Pelvis', 'Pelvis_joint', .42, (0, -.28, .1), (0, -1, .2), note='배 밑에 늘어진 뿌리(젖통 가림)', **BARK),
            ],
        },
        # light (pack of 8): moss bark over the back + a small root clump breaking out of the right shoulder - unlike the moss jackal's
        # bone spurs and crystal; head and forepaws clear
        'moss_wolf': {
            'from': 'wolf', 'tint': [.92, 1.0, .86],
            'note': 'D308-120: 청림 이끼 늑대 = 늑대 몸 + 이끼 껍질 · 어깨 뿌리(가벼움, 무리)',
            'parts': [
                root('moss', 'jackal/Spine4', 'Spine4', .72, (0, .15, -.12), (0, 1, 0), note='등을 덮은 이끼 낀 나무껍질', **MOSS),
                root('clump', 'jackal/Spine5', 'Spine5', .42, (.08, .2, -.02), (.45, 1, -.2), note='오른 어깨를 뚫은 작은 뿌리', **BARK),
            ],
        },
        'forest_bear': {
            'from': 'bear',
            'note': 'D308-120: 청림 숲 곰 = 곰 몸 + 뿌리 · 이끼(보스 마석 곰보다 가볍게)',
            # light-to-medium: moss bark over the hump, a root clump out of the right hip, a smaller one at the left shoulder - wood and
            # moss, where the boss bear has crystals and shackles; head and forepaws (the swipe) clear
            'parts': [
                root('moss', 'bear/Spine2', 'Spine2_M', .9, (0, .28, 0), (0, 1, 0), note='등 혹을 덮은 이끼 낀 나무껍질', **MOSS),
                root('clump', 'bear/Root', 'Root_M', .75, (.12, .3, -.05), (.4, 1, -.3), note='오른 엉덩이를 뚫은 뿌리 덩이', **BARK),
                root('clump', 'bear/Chest', 'Chest_M', .42, (-.16, .26, 0), (-.5, 1, 0), note='왼 어깨 작은 뿌리', **BARK),
            ],
        },
    },
    'swaps': [
        {'match': 'field308/cheongrim_root_beast_', 'species': 'root_beast', 'note': 'D308-120 청림 뿌리 짐승(산양 몸 + 뿌리 부품)'},
        {'match': 'field308b/cheongrim_moss_wolf_', 'species': 'moss_wolf', 'own': True, 'note': 'D308-120 청림 이끼 늑대(늑대 몸 + 이끼 · 뿌리)'},
        {'match': 'field308b/cheongrim_forest_bear_', 'species': 'forest_bear', 'own': True, 'note': 'D308-120 청림 숲 곰(곰 몸 + 이끼 · 뿌리)'},
    ],
}

if __name__ == '__main__':
    out = HERE / 'cheongrim_spec.json'
    out.write_text(json.dumps(SPEC, ensure_ascii=False, indent=1), encoding='utf-8')
    for k, s in SPEC['species'].items():
        for r in s['parts']:
            print(k, r['bone'], Path(r['prefab']).stem, 'pos', r.get('pos'), 'euler', r.get('euler'))
