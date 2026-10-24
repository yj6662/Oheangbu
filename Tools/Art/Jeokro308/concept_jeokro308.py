#!/usr/bin/env python3
"""#308 D308-149 D1: 적로 2막 (가마골 · 옛 절 가마터 · 주작) concepts. Design = Docs/Research/jeokro308/design_v1.md 5절.
User 2026-10-24: "Bible 신경 쓰지 말고 구현하되, 큰축은 유지해서 진행" + questionnaire answer "기존 적로 개조".
Made the same way as Tools/Art/SpellVFX308/concept_riposte308.py (codex exec, built-in image generation).

  python Tools/Art/Jeokro308/concept_jeokro308.py --run            # all
  python Tools/Art/Jeokro308/concept_jeokro308.py --run jj_three   # only these

Writes Art/Jeokro308/Concepts/<id>.prompt.txt (+ .png). Concepts only (D308-28)."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'SpellVFX308'))
import concept_reactions308 as base

OUT = base.REPO / 'Art/Jeokro308/Concepts'

REF_PLACE = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot of the 적로 (red-earth) region: use it for the terrain, the red-earth colour, the light and the ink-wash finish. Do not copy the user interface.
Picture 2 is a game screenshot from the shoulder camera: use it for the camera height and the scale of a person."""
REF_BIRD = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot of the 적로 (red-earth) region: use it only for the ink-wash finish and the muted colour. Do not copy the user interface."""

WORLD = """--- THE WORLD ---
Korea, early Joseon, a land of ink-wash painting made real. 적로 is a red-earth highland of burned army camps and pottery kilns. Everything is matte and painterly: ink outlines, muted washes, paper grain. Fire is drawn as black ink tongues with a thin dull-vermilion edge and grey-white ash - never as light."""

BIRD = """--- THE CREATURE: 주작 (the Vermilion Bird), a boss ---
A man-made guardian beast built in the shape of the Vermilion Bird, now gone wrong: it lives in a pottery workyard and keeps pushing its heat into the kilns until the heat burns the workers. Size: standing height about 5 m, wingspan about 11 m. It walks and makes short low glides; it never soars. Long neck, heavy beak, strong four-toed bird feet, broad wings with grouped primary feathers, a long trailing tail of 5-7 ribbon feathers. Body = dull vermilion and rust-brown washes under black ink feathers, ash caught in the feathers, soot on the beak. It looks crafted as well as alive: faint seams of old glazed ceramic plates and iron rivets along the neck and wing bones, like a kiln-fired sculpture that came alive. NOT a phoenix of light: no flames on the body, no glow, no aura."""

PICTURES = {
    'jj_three': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png'], REF_BIRD + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: model sheet for 3D modelling ---
ONE wide sheet on plain light paper: the SAME creature three times side by side at the same scale - FRONT view, SIDE view (facing left), BACK view. Standing pose on the ground, wings half folded against the body so the shoulder, elbow and wrist joints read, tail feathers hanging down behind, head neutral. Orthographic-looking, no perspective, no ground shadow, no background scenery, even light. Clear readable silhouettes for rigging."""),
    'jj_wings': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png'], REF_BIRD + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: wing and tail construction study ---
ONE sheet on plain light paper with four drawings of the same creature's parts: (1) left wing fully folded, (2) left wing fully spread from above, showing shoulder-elbow-wrist and 4 grouped fans of primary feathers, (3) tail feathers hanging, (4) tail feathers spread in a fan. Clean production reference drawings, no smoke or fire hiding the anatomy."""),
    'jj_arena': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER], REF_PLACE + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: the boss arena ---
ONE single full frame from the player's shoulder camera at the edge of the arena: a 44 by 36 m kiln workyard set inside a burned army camp - charred palisade and old camp posts at the edges, two long brick kilns along one side with clay heat channels running into them, two low stone walls as cover, a broad clear packed-earth floor in the middle. The 주작 stands low in the middle of the yard, head turned toward the player, one wing lifting; grey ash drifts DOWN from above its raised wing (its attack warning). Two round patches of ink-tongue embers on the ground. No lava, no glow."""),
    'jk_village': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER], REF_PLACE + "\n\n" + WORLD + """
--- THE PICTURE: 가마골, the pottery hamlet ---
ONE single full frame from the shoulder camera: a small working pottery hamlet that reuses the structures of a burned army camp - the old camp barracks now workshops, one craft kiln (a long climbing brick kiln on the slope) with heat shimmer drawn as ink, stacked firewood, drying racks of unfinished grey bowls, a small canvas tent where two burned workers lie on mats with a woman healer kneeling and changing water, a handcart with a big earthenware medicine pot. A faint sealed pattern brushed on the kiln door. Charred palisade and a rusty camp banner pole in the background. Ordinary labour, tired people, no glow."""),
    'jk_oldkiln': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER], REF_PLACE + "\n\n" + WORLD + """
--- THE PICTURE: the old temple kiln, a wolf den ---
ONE single full frame from the shoulder camera: a cold abandoned temple kiln on a hillside - a collapsed stone plinth of a small Korean Buddhist temple, broken roof tiles, the low dark mouth of an old kiln used as a den, bones and drag marks of hunting, scattered small metal movable-type pieces glinting dully in the dirt (matte, no shine). Two lean fire-wolves with ash-grey fur and ink-black manes stand guard near the den mouth. Older, lower and more ruined than a working pottery kiln. No glow, no symbols."""),
}


# round 2 (Docs/Research/jeokro308/c1_codex_answer.md: set 76 - three 74, wings 75, arena 83, village 79, oldkiln 71)
V1 = lambda pid: OUT / f'{pid}.png'
KEEP = """
Picture {n} is the FIRST version of this exact picture: keep its character, colours and composition, and fix only what the list below says."""
CERAMIC = """
- The pale armour plates are unmistakably KILN-FIRED CERAMIC (like old celadon/onggi stoneware): matte glaze, exposed reddish clay at chipped edges, soot-filled crackle lines. Few rivets, no metal plate look, no bone look."""
PICTURES.update({
    'jj_three2': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_three')], REF_BIRD + KEEP.format(n=2) + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: model sheet for 3D modelling (second version) ---
ONE wide sheet on plain light paper: a TRUE orthographic turnaround of the same creature - FRONT, LEFT SIDE, and REAR view, identical proportions and height lines. Standing pose, wings folded against the body, tail feathers hanging.
- The REAR view shows the BACK of the head, the back of the neck and the back - no face, no beak visible from behind.
- Exactly 5 main tail feathers in every view; the same wing roots and the same armour divisions in every view.
- A thin vertical 5-metre scale bar with a small human silhouette (1.7 m) beside the side view.""" + CERAMIC),
    'jj_wings2': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_wings')], REF_BIRD + KEEP.format(n=2) + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: wing and tail construction study (second version) ---
ONE sheet on plain light paper. Top row: the SAME left wing in three poses - folded, half-open, fully extended - with every ceramic plate and feather group kept identical. Mark the shoulder, elbow and wrist pivots with small circles; the rigid ceramic plates sit between joints and never cross a joint. Bottom row: the same 5 main tail feathers closed, and spread, plus a small side view showing their roots and overlap order.""" + CERAMIC),
    'jj_arena2': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER, V1('jj_arena')], REF_PLACE + KEEP.format(n=3) + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: the boss arena (second version) ---
Same scene and camera as picture 3, with these fixes:
- The player character holds the great calligraphy BRUSH from picture 2, not a sword.
- Ground hazards are black ink residue with pale grey-white ash falling onto them - no red or orange cores, no light spill.
- Both of the bird's feet and its raised attacking wing stand out from the ground by local light/dark contrast; a soft contact shadow under the feet.
- Scale: the bird is 5 m tall with an 11 m wingspan; a villager-sized wooden cart (1.5 m) stands at the same depth as the bird.""" + CERAMIC),
    'jk_village2': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER, V1('jk_village')], REF_PLACE + KEEP.format(n=3) + "\n\n" + WORLD + """
--- THE PICTURE: 가마골 (second version) ---
Same scene and camera as picture 3, with these fixes:
- No visible flames, glowing embers or orange light anywhere: kiln activity is shown by soot, grey smoke and ash only.
- The reused burned army camp is legible: workbenches standing on old barrack stone foundations, a half-burned watchtower turned into a storage shed, charred gate posts at the hamlet entrance.
- Fewer repeated kiln mouths; one long climbing kiln is the clear centre. A continuous open walking path through the middle stays free of clutter.
- The player character holds the great calligraphy BRUSH from picture 2, not a sword."""),
    'jk_oldkiln2': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', base.SHOULDER, V1('jk_oldkiln')], REF_PLACE + KEEP.format(n=3) + "\n\n" + WORLD + """
--- THE PICTURE: the old temple kiln, a wolf den (second version) ---
Same mood and camera as picture 3, with these fixes:
- The den is unmistakably an abandoned POTTERY KILN: a long sloped brick firing chamber climbing the hill, its roof partly collapsed so the heat-scarred inner walls and the flue are visible; the temple plinth ruins sit beside it, not on top of it.
- Broken ceramic vessels, pale clay shards, kiln stilts and fused failed pots lie around the mouth; fewer roof tiles.
- The two wolves in different poses: one guards the mouth low and still, one circles out to the side. Their manes are black ink that drips and sheds grey ash - not antlers or branches. No glow.
- The player character holds the great calligraphy BRUSH from picture 2, not a sword."""),
})


# round 3: the turnaround keeps drawing a face on the rear view -> the rear view and the wing poses as their own pictures
PICTURES.update({
    'jj_rear3': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_three2')], REF_BIRD + """
Picture 2 shows the same creature from the front (left) and from the side (middle). Ignore its right-hand drawing.""" + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: REAR VIEW ONLY, for 3D modelling ---
ONE drawing on plain light paper: the same creature seen from DIRECTLY BEHIND, standing, facing AWAY from the viewer. We see the back of its head (the crest feathers from behind), the back of the long neck, the back and both folded wings from behind, and the 5 tail feathers hanging down the middle. The beak and the eyes are NOT visible at all - the head points away from us. Same proportions as picture 2. No background, no shadow.""" + CERAMIC),
    'jj_wingpose3': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_wings2')], REF_BIRD + """
Picture 2 shows this creature's wing and tail parts: keep their feathers and colours.""" + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: ONE wing in THREE poses, for rigging ---
ONE sheet on plain light paper, three drawings in a single row, all of the SAME left wing seen from above at the same scale: (1) FOLDED tight against the body, (2) HALF-OPEN at about 45 degrees, (3) FULLY EXTENDED. In all three the same three joints are marked with small ink circles - shoulder, elbow, wrist - and the same 4 fans of primary feathers attach at the wrist. The pale ceramic plates sit only on the bone segments between joints, never across a joint, and keep the same count and shape in all three poses. Nothing else on the sheet.""" + CERAMIC),
})


# round 4 (Docs/Research/jeokro308/c2_codex_answer.md: set 84 - scale reads 7-8 m, armour still metal): single views for Meshy
SINGLE = """
--- FOR 3D MODELLING ---
ONE drawing on plain light paper, the creature alone: no human figure, no scale bar, no text, no other poses, no shadow, no background. Same body proportions, same folded wings, same 5 tail feathers and same crest as pictures 2 and 3. Neutral standing pose with clear space between the legs, between the wings and the body, and between the tail and the legs.
- Heavy, compact proportions of a 5 m tall creature: the neck is thick and strong, the legs are sturdy (not stilt-thin), the body is a large barrel.
- The pale plates are fired CERAMIC tiles laced onto the body with dark cords - matte crackled glaze, chipped edges that show the coarse reddish clay body inside, soot in the cracks. NO rivets, NO metal edges, NO bolts."""
REF_TRIO = REF_BIRD + """
Picture 2 shows this creature from the front and side (ignore its right-hand drawing); picture 3 shows it from behind. Keep this design."""
PICTURES.update({
    'jj_front4': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_three2'), V1('jj_rear3')], REF_TRIO + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: FRONT VIEW ONLY ---
The creature seen from directly in front, facing the viewer, symmetric.""" + SINGLE),
    'jj_side4': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_three2'), V1('jj_rear3')], REF_TRIO + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: SIDE VIEW ONLY ---
The creature seen exactly from its left side, facing left, orthographic.""" + SINGLE),
    'jj_rear4': ([base.REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png', V1('jj_three2'), V1('jj_rear3')], REF_TRIO + "\n\n" + WORLD + "\n\n" + BIRD + """
--- THE PICTURE: REAR VIEW ONLY ---
The creature seen from directly behind, facing AWAY: back of the head, back of the neck, back, folded wings from behind, the 5 tail feathers down the middle. No beak, no eyes visible.""" + SINGLE),
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
