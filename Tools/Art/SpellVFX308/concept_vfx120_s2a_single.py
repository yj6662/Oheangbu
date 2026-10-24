#!/usr/bin/env python3
"""vfx120 stage 2, discriminating test after group A went 0/9 (best 86): is the bottleneck the 4-panel layout (836x470
per panel) or the image tool itself?  One panel of sheet 1a per FULL 1672x941 image, judge fixes reconciled by hand.

  python Tools/Art/SpellVFX308/concept_vfx120_s2a_single.py --run --judge        # P1, P2, A2 single images
  python Tools/Art/SpellVFX308/concept_vfx120_s2a_single.py --judge --round 2 ... # same flags as concept_vfx120_s2a.py

Outputs: Art/SpellVFX308/Concepts/vfx120_s2/s2a_1a_P*_r<N>.png (+ judge)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_vfx120_s2a as a

a.LAYOUT_TXT['1x1'] = ("LAYOUT: ONE single full landscape picture (no panels, no gutters). The spell effect is the subject: "
                       "frame the camera so the effect fills a large part of the picture and every stroke, gap and stroke end is drawn large and clear.")
_judge_prompt = a.judge_prompt


def judge_prompt(pid, rnd=1):
    if a.S[pid]['layout'] != '1x1':
        return _judge_prompt(pid, rnd)
    s = a.S[pid]
    (t, d), = s['panels']
    return '\n\n'.join([a.JUDGE_HEAD, a.RUBRIC, a.ITEM4_NOTE,
                        f"[이 그림] 장 {s['sheet']} ({pid}) 중 한 칸을 1672x941 한 장 전체로 그린 시험 그림. 칸 수는 1.\n[의도한 칸]\n  칸 1: {t} — {d}",
                        f"[이 칸의 고유 정지 판정] {s['proof']}\n[계획서 발췌] {s['plan']}",
                        a.JUDGE_TAIL])


a.judge_prompt = judge_prompt

WOOD_LOOK = ("Wood strokes are matte dry ink: a dark near-black reading edge plus a subdued teal-green (#4B9F88) dry pigment layer, "
             "broken dry edges, LONG TORN FIBRE TIPS at the stroke ends. No pale glossy edge highlights, no shine, no glow.")
base1a = a.S['s2a_1a_1']
SINGLE = {
    's2a_1a_P1': ("ㅏ + 남긴다 (P1)",
                  "Low shoulder camera, closer than usual so the landing point is large in the lower-middle of the picture. The caster's narrow lane "
                  "(0.3 m wide, three or four thin parallel dry strokes with ground showing between them) runs from the brush tip to the nearer enemy 5 m ahead and stops at the enemy's feet. "
                  "At that landing point a small cluster of four or five LOW branches STANDS UP from the ground (about 0.4 m high): each branch is a single thin hollow stroke that "
                  "FORKS into two or three tips at its TOP end, the tips ending in torn fibres. Nothing lies flat, nothing spreads sideways, nothing hangs. The enemy's body stays plain and fully visible. "
                  + WOOD_LOOK,
                  "남긴다 = 착점에서 위로 갈라지는 서 있는 가지(윗끝 분기, 찢긴 섬유 끝)가 한눈에 읽힌다. 경로는 가는 평행 획 몇 개."),
    's2a_1a_P2': ("ㅏ + 번진다 (P2)",
                  "Same low shoulder camera, same lane, same enemy, landing point large in the lower-middle of the picture. At the landing point FIVE or SIX short SEPARATE hollow strokes "
                  "lie FLAT ALONG THE GROUND, each about 0.5 m long, fanning slightly outward like loose straws dropped on the earth, with clearly visible bare earth between every stroke. "
                  "There is NO dense centre, NO hub, NO starburst: the inner ends start apart from each other. The OUTER ends are OPEN and FRAYED into torn fibres. Nothing stands above ankle height. "
                  + WOOD_LOOK,
                  "번진다 = 바닥에 눕고 열린 끝을 가진 짧은 획 다발(중심 뭉침 없음)이 한눈에 읽힌다. 가지와 혼동되지 않는다."),
    's2a_1a_A2': ("ㅓ + 번진다 (A2)",
                  "Close shoulder camera on the caster's right arm and brush, enemy crouched low at the right side 1.5 m away. Four or five thin SEPARATE dry strokes lie PARALLEL ALONG "
                  "the brush shaft and the right sleeve, hugging them (not wrapped around in rings, not standing outward), with the cloth and lacquer visible between strokes. "
                  "The brush tip visibly TOUCHES the enemy's wrist just above the ground, and ONLY THERE a small FLAT bundle of four short strokes (about 0.3 m) lies on the ground at that contact "
                  "point with open frayed ends - no hanging roots, no upright branches, no strokes elsewhere on the ground. "
                  + WOOD_LOOK,
                  "ㅓ+번진다 = 붓대·소매를 따라 누운 부착 다발 + 실제 접촉점에만 평평한 외부 다발."),
}
for pid, (title, desc, proof) in SINGLE.items():
    a.S[pid] = dict(sheet='1a', layout='1x1', refs=['shoulder', 'brush', 'jeokro', 'marks', 'frames2'], bodies=['wood'],
                    panels=[(title, desc)], proof=proof, plan=base1a['plan'])

if __name__ == '__main__':
    args = sys.argv[1:]
    if not [x for x in args if not x.startswith('--') and not x.isdigit()]:
        args += list(SINGLE)
    sys.argv = [sys.argv[0]] + args
    a.main()
