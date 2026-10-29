#!/usr/bin/env python3
"""#308 vol.2 120 spells (D308-135 · D308-149 D4): concepts the S5-b design (Docs/Research/vol2/s5b_design_v1.md 11절) still needs
on top of the verb-marks board (Art/SpellVFX308/Concepts/tier1/t1_v2_verb_marks3.png): the four frames, the five bodies' ends and
the 걱 self-recovery. User 2026-10-24: "술식 120개 만드는 게 제일 우선이야."

  python Tools/Art/SpellVFX308/concept_vol2_frames308.py --run            # all
  python Tools/Art/SpellVFX308/concept_vol2_frames308.py --run v2_frames  # only these

Writes Art/SpellVFX308/Concepts/vol2/<id>.prompt.txt (+ .png). Concepts only (D308-28)."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_reactions308 as base

OUT = base.REPO / 'Art/SpellVFX308/Concepts/vol2'
MARKS = base.REPO / 'Art/SpellVFX308/Concepts/tier1/t1_v2_verb_marks3.png'

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder) and the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is the region: 적로, a dry red-earth valley - use it for the ground.
Picture 4 is the game's accepted board of spell MARKS (what a spell leaves behind): rising branches, flat spreading strokes, a single vertical post, a sunken crack, a closed curve. Keep that mark language."""

GRAMMAR = """--- THE SPELL GRAMMAR ---
Every spell = BODY (one of five elements, the material of the ink) + FRAME (where and in what shape it acts) + VERB (what it does after touching). All magic is matte INK and soaked-in mineral pigment, never light. The caster's own ink is drawn as DRY-EDGED, HOLLOW bundles of brush strokes; enemy magic is wet solid patches with no rim - so 'mine' always reads at a glance."""

PICTURES = {
    'v2_frames': ([base.SHOULDER, base.BRUSH, base.JEOKRO, MARKS], REF + "\n\n" + GRAMMAR + """
--- THE PICTURE: the FOUR FRAMES, same body (wood: dark green-black ink, dry fibres) ---
LAYOUT: ONE wide strip of FOUR equal panels, identical shoulder camera, the same red-earth ground, thin paper-coloured gaps, two gaunt matte enemies about 6-8 m ahead in every panel.
Panel 1 - PATH: a narrow straight brush stroke (about 30 cm wide) shoots from the brush tip 10 m forward and touches the first enemy only - a single line, like one stroke of calligraphy across the ground and air.
Panel 2 - ATTACH: nothing flies out; the ink wraps the caster's own BRUSH SHAFT and SLEEVE as a tight dry-brush binding of dark green-black strands, and the caster swings the brush into the nearer enemy at close range.
Panel 3 - FAN: a wide fan of many short parallel strokes bursts forward from the caster, about 4 m wide and 5 m deep, covering both enemies' feet, like a bundle of strokes swept across the ground.
Panel 4 - RING: a HOLLOW ring band about 4 m across and only 0.6 m wide is drawn on the ground around the caster's own position - empty in the middle, the enemies at its outer edge. Not a filled disc."""),
    'v2_bodies': ([base.SHOULDER, base.BRUSH, base.JEOKRO, MARKS], REF + "\n\n" + GRAMMAR + """
--- THE PICTURE: the FIVE BODIES, same frame and verb (a path that LEAVES a mark at the hit point) ---
LAYOUT: ONE wide strip of FIVE equal panels, identical camera, the same ground, one enemy 7 m ahead, thin gaps. In each panel the same narrow path stroke hits the enemy and leaves a small mark at its feet, 1.5 m across; only the BODY differs, readable by colour AND by how its end moves:
1 WOOD: dark green-black ink, dry fibres; the mark is low branching roots, and its fine root tips unravel FORWARD.
2 FIRE: low-saturation rust-red ink with grey ash; the mark is charred strokes, and its ash rises UPWARD in a thin wisp (no flames, no glow).
3 EARTH: ochre-brown ink, rough slabs; the mark SINKS into the ground while the soil around it lifts slightly.
4 METAL: matte grey ink, thin blades; the mark breaks into small flat shards that snap off SIDEWAYS.
5 WATER: blue-grey ink, wet gradations; the mark runs and drips DOWNWARD into the cracks of the ground.
Same size, same place, so the five can be compared side by side."""),
    'v2_geok': ([base.SHOULDER, base.BRUSH, base.JEOKRO], '\n'.join(REF.splitlines()[:4]) + "\n\n" + GRAMMAR + """
--- THE PICTURE: 걱 - the wood spell that heals the caster (self-recovery), three moments ---
LAYOUT: ONE wide strip of THREE equal panels, closer shoulder camera on the caster, red-earth ground, thin gaps.
Panel 1 (start): the caster holds the brush upright; dark green-black dry ink fibres spiral up from the bristle head and wrap the caster's own forearm and chest like a fresh binding of roots - small, close to the body, nothing goes outward.
Panel 2 (3 s of healing): the binding has grown along the arm and around the torso, a few tiny new green-black leaves on it, fibres slowly drawing inward; the caster stands steady. Clearly NOT an attack: no strokes toward enemies, no ground mark.
Panel 3 (interrupted by a hit): an enemy's blow lands on the caster's shoulder; the binding snaps and the fibres fall away as dry brittle fragments, unravelling forward. No glow, no sparkles, no green light."""),
}


# round 2 (Art/SpellVFX308/Concepts/vol2/judge_codex_answer2.md: frames 69, bodies 66, geok 62)
OWN = """
OWNERSHIP (the most important rule): everything the caster makes is drawn as DRY, HOLLOW bundles of thin brush strokes with a dry rim and paper-coloured gaps between the strokes - never solid filled shapes, never thick vines. Enemies stay at least 70 % visible through every effect."""
PICTURES.update({
    'v2_frames2': ([base.SHOULDER, base.BRUSH, base.JEOKRO, MARKS, OUT / 'v2_frames.png'], REF + """
Picture 5 is the FIRST version of this exact picture: keep its four-panel layout, camera and ground, fix only what is listed.""" + "\n\n" + GRAMMAR + OWN + """
--- THE PICTURE: the FOUR FRAMES, same body (wood), second version ---
Panel 1 - PATH: ONE narrow straight lane, 30 cm wide and 12 m long, made of a few thin parallel dry strokes; small root tips fray only FORWARD at the far end, never widening the lane.
Panel 2 - ATTACH: thin dry dark-green strokes bound ONLY around the caster's own brush shaft and right sleeve, like a tight wrapping; NOTHING reaches the enemy, no big swing arc; the enemy stands 2 m away, untouched.
Panel 3 - FAN: one single spread of short, sparse dry strokes covering a 4 m wide, 5 m deep fan in front of the caster, with plenty of ground showing between strokes so both enemies' legs and bodies are clearly visible.
Panel 4 - RING: exactly one clean band, 2 m radius and only 0.6 m wide, made of thin dry strokes along the band; the centre and the area outside are completely empty ground."""),
    'v2_bodies2': ([base.SHOULDER, base.BRUSH, base.JEOKRO, MARKS, OUT / 'v2_bodies.png'], REF + """
Picture 5 is the FIRST version of this exact picture: keep its five-panel layout, camera, ground and enemy, fix only what is listed.""" + "\n\n" + GRAMMAR + OWN + """
--- THE PICTURE: the FIVE BODIES, second version ---
In all five panels the path is the SAME straight narrow lane (no S-curve), same length, and the mark at the enemy's feet is the same size; the bodies must differ by HOW THE STROKE ENDS MOVE, readable even without colour:
1 WOOD: the stroke ends fray into fine root tips that stretch FORWARD past the enemy.
2 FIRE: the stroke ends break off into small grey ash flakes that float clearly UPWARD in a thin column (no flame, no glow; colour only a dull rust edge).
3 EARTH: the mark SINKS into a shallow depression while a raised rim of cracked soil LIFTS around it - both visible together.
4 METAL: the stroke ends snap into short flat blade-shards that fly out to the LEFT and RIGHT, perpendicular to the lane.
5 WATER: the stroke ends drip and run DOWNWARD into the ground cracks, thin wet trickles."""),
    'v2_geok2': ([base.SHOULDER, base.BRUSH, base.JEOKRO, OUT / 'v2_geok.png'], '\n'.join(REF.splitlines()[:4]) + """
Picture 4 is the FIRST version of this exact picture: keep its three-panel layout and camera, fix only what is listed.""" + "\n\n" + GRAMMAR + OWN + """
--- THE PICTURE: 걱 self-recovery, second version ---
Panel 1 (start): a few thin dry dark-green strokes curl from the brush head and wind loosely around the brush shaft and the caster's right forearm only - small, light, not armour.
Panel 2 (healing, same place 1.5 s later): the SAME few strokes keep being re-drawn: one fades while a new one is drawn beside it, like a calligrapher re-inking the same line; a few old torn marks on the caster's coat close up as fresh ink fills them. Nothing grows bigger, nothing points outward.
Panel 3 (hit): an enemy's blow lands; the strokes STOP being re-drawn at once and the last two simply dry and crumble straight down - no burst, no flying leaves, no shield clash, nothing thrown at the enemy."""),
})


def prompt(pid):
    refs, body = PICTURES[pid]
    return '\n\n'.join([base.HEAD, body, base.STYLE, base.BAN, base.TAIL % (OUT / f'{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'{pid}.png').exists())


if __name__ == '__main__':
    main()
