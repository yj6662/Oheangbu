#!/usr/bin/env python3
"""Ink-stroke spell concepts, 110 glyphs (2026-10-31, D308-161 - the user: "방어는 그런 육각면으로 하고, 공격은 다른 투사체를 사용해야지";
plan Docs/Handoff/PLAN-308-INK-STROKE-SPELLS-20261031.md §3). Codex built-in image generation (gpt-6-astra) on the lab plate + the approved riposte
stroke (the family) + the hexagon ward (what NOT to draw). Concept TEST (D308-28).
  python Tools/Art/SpellVFX308/restart/concept_ink308.py <key> [<key> ...]          # prompts only
  python Tools/Art/SpellVFX308/restart/concept_ink308.py <key> ... --run             # codex exec per key (parallel)
Keys = <글자>_<S1|S2|S3> (가_S1 ...). Out: Art/SpellVFX308/Restart/ink308/concepts/<key>.png"""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OUT = REPO / 'Art/SpellVFX308/Restart/ink308/concepts'
GEN = Path.home() / '.codex' / 'generated_images'
PLATE = REPO / 'Art/SpellVFX308/Restart/shots/가_plate/f_00000.jpg'
STROKE = REPO / 'Art/SpellVFX308/Concepts/riposte/rp_stroke5_board.png'
WARD = REPO / 'Art/SpellVFX308/Restart/shots/우_H/f_00081.jpg'

HEAD = """You are being called non-interactively to produce ONE concept image file. Use ONLY your built-in image generation tool (landscape 16:9); if unavailable print IMAGE_TOOL_UNAVAILABLE and stop. Do not draw with code, download, or read, change, move or delete any other file.
Picture 1 is a frame of our game: Korean ink-wash look, third-person camera over the caster's RIGHT shoulder (2.8 m behind, eye height, the ground seen almost edge-on), a robed Taoist holding a big brush, red-brown ground, cream paper sky, a bear (the enemy) standing about 5 m ahead. Keep that camera, caster, bear and ground EXACTLY. Picture 2 is our approved "one great brush stroke": matte black sumi ink, a wet rounded head, long dry-brush bristle streaks where the paper shows through, a thin dry tail, cracking as it dries - THIS is the family our spell must belong to. Picture 3 is our DEFENCE spell (translucent hexagon cells, cream seams, engraved patterns): the attack spell must look NOTHING like it.
THE LOOK: a fast, sharp BLACK INK BRUSH STROKE with a visible direction (wet head toward the enemy, dry bristle tail toward the brush), monochrome ink on the scene, plus EXACTLY ONE accent colour (%s) used only as light: a hair-thin bright core line, sharp sparks at the point of impact, a faint glow on the stroke's last afterimage. Black ink droplets fly forward from the impact (three sizes). Blade-sharp ends, no round blobs. Scale: the stroke's head is about 0.6 m (a third of the bear's height); the whole flying stroke 1.2-1.8 m; elements smaller than 0.25 m are not drawn.
FORBIDDEN: hexagons, glass, translucency, cream seams, engraved or Korean traditional patterns, realistic plants/fire/water/rock textures, smoke clouds, glowing fog, rainbow or multiple colours, text, UI, a second brush, anything floating above the bear's head, strokes lying flat on the ground as a filled area.
"""
ACCENT = {'ㄱ': 'jade green', 'ㄴ': 'vermilion', 'ㅁ': 'ochre', 'ㅅ': 'gold-white', 'ㅇ': 'blue-white'}
BODY = {
    'ㄱ': "THE BODY (WOOD): the stroke's head splits into a THORN-AWL of 3-5 bristle strands - sharp, barbed, biting; matte ink only, jade light only at the tips.",
    'ㄴ': "THE BODY (FIRE): an ink ARROW stroke, its tail split into 3-4 upward-licking dry-brush flame tongues trailing black ash specks and a few vermilion embers.",
    'ㅁ': "THE BODY (EARTH): a short, blunt, heavy ink stroke with angular matte INK-STONE chips (3-5) clinging to it, breaking off at impact; ochre dust.",
    'ㅅ': "THE BODY (METAL): the thinnest, straightest stroke of all, both edges ruler-sharp, a gold-white hair-thin core line, ending in one blade flick.",
    'ㅇ': "THE BODY (WATER): a curved WET stroke whipping in an S, wide bleeding edges, the lash end snapping at the bear; ink runs downward.",
}
VARIANT = {
    'S1': "THE VARIANT - dry-brush dominant: long bristle streaks run the whole stroke, the paper shows through in many thin gaps, the head is only slightly wet, edges are ragged everywhere, the tail splits into many fibres.",
    'S2': "THE VARIANT - wet-ink dominant: the head is a heavy wet pool with a dark gathered rim and soft 2-3 px bleeding edges, droplets are large and round-edged, the tail is short, only the very end is dry.",
    # 2026-10-31 the user's pick (questionnaire): S1's dry-brush body with S3's blade ends - every concept after P0 uses this
    'S13': "THE VARIANT - dry-brush body, blade ends: long bristle streaks run the whole body of the stroke with the paper showing through thin gaps and a slightly wet head, BUT both ends finish in a ruler-straight razor flick, the hair-thin accent core line is crisp, afterimages are sharp slash-like copies, and the impact throws needle-like ink darts forward plus a few larger droplets.",
    'S3': "THE VARIANT - blade dominant: the stroke is narrow with ruler-straight edges and a razor flick at both ends, the core line is the brightest element, afterimages are crisp copies offset like a slash, droplets are few and needle-like.",
}
SPELLS = {
    '가': ('ㄱ', "\"가\" = one ink stroke shot from the brush tip straight to the bear; the moment of impact: the stroke's split head has become a thorn-awl of 3-5 bristle strands biting into the bear's chest and starting to coil round it, jade sparks and black ink droplets flying forward, two translucent-free dark afterimages of the stroke lagging behind it, a thin ink stain on the ground at the bear's feet toward the caster. Nothing hexagonal anywhere."),
    '나': ('ㄴ', "\"나\" = one ink arrow stroke shot from the brush tip to the bear; the moment of impact: embers rising, ash specks, black ink droplets flying forward."),
    '마': ('ㅁ', "\"마\" = one heavy ink stroke shot to the bear; at impact its ink-stone chips break off and the ground under the bear lifts as a flat earthen slab 10 cm; ochre dust; the bear is shoved back."),
    '사': ('ㅅ', "\"사\" = one blade-thin ink stroke shot to the bear; impact = a shower of 15-20 thin gold sparks sideways, a hit-stop feeling."),
    '아': ('ㅇ', "\"아\" = one whipping wet ink stroke lashing the bear; impact = ink running down the bear and blue-white water-drop glints below."),
    # P2 frames (plan §3.3)
    '고': ('ㄱ', "\"고\" (front area) = a STANDING crescent ink stroke about 1 m tall - a sweep of the brush frozen in the air - racing forward through the wide wedge in front of the caster, the bear caught mid-stroke, thin ink lines on the ground behind it only."),
    '굿': ('ㄱ', "\"굿\" (ring round the caster) = a ring of ink drawn on the ground round the caster (2 m radius), six to eight short BLADE strokes standing on it knee-high, leaning outward, gaps between them, a dark crack line under the ring - no wall, no dome. CAMERA NOTE: only the FAR and SIDE arcs are in frame; the near arc is cut off by the bottom edge and partly hidden by the caster - do not draw a complete visible circle."),
    '것': ('ㄱ', "\"것\" (on the caster) = the whole shaft of the caster's brush turned into an ink blade, a thin bright core line along its edge, dry-brush fibres along the shaft, a short afterimage ribbon where it was just swung; nothing flies to the bear."),
    # P3 verbs (wood)
    '각': ('ㄱ', "\"각\" (strongest form) = the coiled ink has pooled at the bear's feet and 8-10 branch strokes have risen from the pool into a cage round the bear, thin ink threads wavering upward; the stroke is 1.3x wider than normal with a constant jade core line."),
    '간': ('ㄱ', "\"간\" = from the first bear's feet a second ink bleed runs flat along the ground to a second bear 3 m aside and a smaller thorn stroke rises there (draw two bears)."),
    '감': ('ㄱ', "\"감\" = only one ink DOT (15 cm) sits on the bear's chest with a hair-thin vertical ink thread wavering up from it, the rest of the stroke already dispersed into ink specks - quiet, waiting."),
    '갓': ('ㄱ', "\"갓\" = the stroke stands edgewise like a blade and has passed THROUGH the bear, 2-3 dark armour chips flying, a straight dark crack carved in the ground from caster to bear and 1 m beyond."),
    '강': ('ㄱ', "\"강\" = the stroke has U-turned at the bear and is returning to the brush, its ink tail drawing a closed loop between the caster and the bear."),
}
# ---- round 2 ("S13b", 2026-10-31): applies judge/concept_r1.md common handles (§3) + per-glyph handles (§1). Keys <글자>_S13b. R1 prompts (S13) stay reproducible. ----
HEAD_B = """You are being called non-interactively to produce ONE concept image file. Use ONLY your built-in image generation tool (landscape 16:9); if unavailable print IMAGE_TOOL_UNAVAILABLE and stop. Do not draw with code, download, or read, change, move or delete any other file.
Picture 1 is a frame of our game: Korean ink-wash look, third-person camera over the caster's RIGHT shoulder (2.8 m behind, eye height, the ground seen almost edge-on), a robed Taoist holding a big brush, red-brown ground, cream paper sky, a bear (the enemy) standing about 5 m ahead. Keep that camera, caster, bear, brush pose and ground EXACTLY. Picture 2 is our approved "one great brush stroke": matte black sumi ink, a wet rounded head, long dry-brush bristle streaks where the paper shows through, a thin dry tail, cracking as it dries - THIS is the family our spell must belong to. Picture 3 is our DEFENCE spell (translucent hexagon cells, cream seams, engraved patterns): the attack spell must look NOTHING like it.
THE LOOK: a fast, sharp BLACK INK BRUSH STROKE with a visible direction (wet head toward the enemy, dry bristle tail toward the brush), monochrome ink on the scene, plus EXACTLY ONE accent colour (%s) used only as light: a hair-thin bright core line, sharp sparks at the point of impact, a faint glow on the stroke's last afterimage. Everything is MATTE ink: the pale streaks inside a stroke are GAPS where the ground shows through (dry-brush), never white gloss highlights. Black ink droplets are FLAT wet splats and thin needle streaks, matte, flying forward and up only (never toward the camera), in three sizes. Blade-sharp ends, no round blobs. Scale: the stroke's head is about 0.6 m (a third of the bear's height); the whole flying stroke 1.2-1.8 m; everything the spell draws fits inside a box 1.8 m long and 1.2 m tall unless THE SPELL states another size, and nothing crosses the horizon line; elements smaller than 0.25 m are not drawn. The hit is on the bear's CHEST or shoulder (46-70 %% of its height); the bear's face stays fully visible.
FORBIDDEN: hexagons, glass, translucency, cream seams, engraved or Korean traditional patterns, realistic plants/fire/water/rock textures, smoke clouds, glowing fog, rainbow or multiple colours, text, UI, a second brush, anything floating above the bear's head, strokes lying flat on the ground as a filled area, faceted 3D crystal shards, gems, polyhedra, obsidian chips, glossy crystals, wooden boards or planks, glossy highlights of any kind.
Draw ONLY what THE SPELL describes: one main stroke and at most three secondary marks. No extra projectile, loop, or scattered shards elsewhere in the frame.
"""
BODY_B = dict(BODY)
BODY_B.update({
    'ㄱ': "THE BODY (WOOD): the stroke's edges bristle with SHORT BARBED THORN FLICKS (each no longer than half the stroke width); the head splits into 3-5 strands that bite and COIL round the target on contact; matte ink only, jade light only at the tips.",
    'ㄴ': "THE BODY (FIRE): an ink ARROW stroke whose body shows paper gaps; the TAIL (brush side) splits into 3-4 dry-brush flame tongues that lick UPWARD and backward like fire caught in wind, each tongue a ragged bristle streak; black ash specks drift up from them and a few vermilion embers rise.",
    'ㅁ': "THE BODY (EARTH): a short, blunt, heavy ink stroke with an angular rim; flat matte INK-STONE chips (3-5, dry-brush faceted, no gloss, no highlights) cling to it and break off at impact; ochre dust is a low puff.",
    'ㅇ': "THE BODY (WATER): a curved WET stroke whipping in an S, wide bleeding edges, the lash end snapping at the bear; ink runs downward; the stroke is matte and its pale streaks are dry-brush gaps, never white gloss lines.",
})
VARIANT['S13b'] = "THE VARIANT - dry-brush body, blade ends: long bristle streaks run the whole body of the stroke with the paper showing through thin gaps and a slightly wet head, BUT both ends finish in a ruler-straight razor flick, the hair-thin accent core line is crisp, afterimages are sharp slash-like copies, and the impact throws thin needle streaks and flat wet splats forward (never faceted shards) plus a few larger round wet droplets."
SPELLS_B = dict(SPELLS)
SPELLS_B.update({
    '나': ('ㄴ', "\"나\" = one ink arrow stroke shot from the brush tip to the bear; the moment of impact on the bear's CHEST (face visible): vermilion embers rising, ash specks drifting up, black ink streaks flying FORWARD and up only, none toward the camera; no faceted shards."),
    '마': ('ㅁ', "\"마\" = ONE short blunt ink stroke (1.2 m, head 0.6 m, angular rim) has just hit the bear's chest; the bear is pushed back half a step, leaning away; ONLY 3-5 matte ink-stone chips (flat, dry-brush faceted, NO gloss, NO highlights) break off the head and fall forward; under the bear a thin dark slab edge lifts 10 cm as a cracked ink line, not a plank; ochre dust is a low puff, not sparks. The whole effect is no wider than the bear."),
    '사': ('ㅅ', "\"사\" = one blade-thin ink stroke shot to the bear; the stroke hits the bear's SHOULDER, face fully visible; impact = a shower of 15-20 thin gold sparks sideways, a hit-stop feeling; the afterimage shards behind the bear are thin needle streaks no longer than 0.3 m, not feathers."),
    '아': ('ㅇ', "\"아\" = one whipping wet ink stroke lashing the bear's chest; the ink runs down its fur in 2-3 wet trails with blue-white water-drop glints below; the ink drops beyond the bear are round wet splats, no faceted shards."),
    '고': ('ㄱ', "\"고\" (front area) = a STANDING crescent ink stroke - a sweep of the brush frozen in the air: the crescent is ABOUT 1 m tall and 2 m wide (bear-chest height, never above the horizon line), tilted 20 degrees forward, its edge sweeping THROUGH the bear's body, the jade sparks are ON the bear; short thorn flicks bristle along the crescent's outer edge; behind it only 3-5 thin ink lines on the ground, nothing flying off to the right."),
    '굿': ('ㄱ', "\"굿\" (ring round the caster) = a ring of ink drawn on the ground round the caster (2 m radius), six to eight short BLADE strokes standing on it, each a single straight razor ink flick 0.3 m tall leaning 25 degrees outward (NOT bristle tufts), gaps between them, a dark crack line under the ring - no wall, no dome. NOTHING flies to the bear - the bear is untouched, standing 5 m ahead outside the ring. CAMERA NOTE: only the FAR and SIDE arcs are in frame; the ring's near arc is OUT of frame, cut off by the bottom edge and partly hidden by the caster - do not draw a complete visible circle. FORBIDDEN here: a projectile, a hit on the bear."),
    '것': ('ㄱ', "\"것\" (on the caster) = the WHOLE shaft of the caster's brush from grip to tip is wrapped in one matte ink stroke with long dry-brush fibres and short thorn flicks bristling along it; the tip ends in a single thin ink blade flick (NOT a metal trident, NOT a weapon head - still a brush dipped in ink); a thin dark crack line runs along the shaft; the afterimage is one short ink ribbon 0.4 m where it was just swung; nothing flies to the bear."),
    '각': ('ㄱ', "\"각\" (strongest form) = the flying stroke is already GONE - do not draw it or any loop in the air; only the pool of coiled ink at the bear's feet and the cage remain: 8-10 branch strokes risen from the pool into a cage round the bear, cage 1.4 m tall, radius = bear + 0.3 m, each branch bristling with short thorn flicks, thin ink threads wavering upward; the stroke is 1.3x wider than normal with a constant jade core line; nothing flies off to the right."),
    '간': ('ㄱ', "\"간\" = the caster's pose and brush are EXACTLY as in picture 1; the first hit is on the bear's chest, face visible; from the first bear's feet a second bleed runs along the ground to a second bear 3 m aside (draw two bears): the second bleed is a FLAT wet ink stain creeping along the ground (0.3 m wide, soft edge), and the thorn stroke that rises at the second bear is half the size of the first."),
    '감': ('ㄱ', "\"감\" = ONE image drawn as TWO side-by-side panels of the same scene (same camera, caster, bear and ground, each half cropped to the area around the caster and the bear), separated by a thin dark vertical line; no text. LEFT panel = WAITING: ONE ink dot 0.25 m on the bear's CHEST (centre of the torso, not the head), wet and dark with a small gathered rim; from it one hair-thin ink thread rises 0.5 m and wavers (it must not pass above the bear's head); the dispersed stroke is 4-6 ink specks in a straight line between brush tip and bear, nothing else. RIGHT panel = TRIGGERED by a second hit: the dot has burst and 3-5 barbed thorn strands (jade light only at the tips) have shot out and COIL round the bear's torso, holding it in place; a few ink droplets fly forward and up."),
    '갓': ('ㄱ', "\"갓\" = the stroke stands edgewise like a blade and has passed THROUGH the bear, 2-3 dark armour chips (dark ink flakes, flat) flying, a straight DARK INK LINE 4 cm wide drawn on the ground from the brush tip to 1 m past the bear - flat ink with a few dry-brush gaps along its edges, no raised earth, no trench, no soil clods."),
    '강': ('ㄱ', "\"강\" = the stroke has U-turned at the bear and is returning to the brush, its ink tail drawing a closed loop between the caster and the bear: the loop is 2 m long and lies tilted 35 degrees toward the camera, its whole length dry-brush with paper gaps; the outgoing half of the loop bristles with short thorn flicks; 2 strands stay coiled on the bear's chest where it struck."),
})
TAIL = """
Save the image unchanged (PNG) as: %s
Print the saved file path and stop."""
PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(key):
    dst = OUT / f'{key}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{key}.log.txt'
    cand = [GEN / m.group(1) / m.group(2) for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace'))] if log.exists() else []
    cand = [p for p in cand if p.exists()]
    if cand:
        dst.write_bytes(cand[-1].read_bytes()); return True
    return False


def prompt(key):
    letter, var = key.split('_')
    if var.endswith('b'):   # round 2
        body, spell = SPELLS_B[letter]
        return (HEAD_B % ACCENT[body]) + 'THE SPELL: ' + spell + '\n' + BODY_B[body] + '\n' + VARIANT[var] + TAIL % (OUT / f'{key}.png')
    body, spell = SPELLS[letter]
    return (HEAD % ACCENT[body]) + 'THE SPELL: ' + spell + '\n' + BODY[body] + '\n' + VARIANT[var] + TAIL % (OUT / f'{key}.png')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    keys = [a for a in sys.argv[1:] if not a.startswith('--')]
    for k in keys:
        (OUT / f'{k}.prompt.txt').write_text(prompt(k), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', keys); return
    procs = []
    for k in keys:
        if recover(k):
            continue
        cmd = ['codex', 'exec', '-m', 'gpt-6-astra', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT), '-i', str(PLATE), '-i', str(STROKE), '-i', str(WARD)]
        log = open(OUT / f'{k}.log.txt', 'w', encoding='utf-8')
        procs.append((k, subprocess.Popen(cmd, stdin=open(OUT / f'{k}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
        time.sleep(2)
    for k, p in procs:
        try: p.wait(timeout=2700)
        except subprocess.TimeoutExpired: p.kill()
        print('DONE', k, p.returncode, recover(k), flush=True)


if __name__ == '__main__':
    main()

