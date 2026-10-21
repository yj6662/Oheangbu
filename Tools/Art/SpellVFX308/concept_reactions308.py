#!/usr/bin/env python3
"""#308 D308-128 (debate 18) concepts: the five-element reactions between the player's own zones and the word layer. Each picture
is TWO panels (before / after) because a reaction is a moment (Fable): 목생화 wildfire, 수극화 steam, and the word join 감옥.
Process: concepts first, debate on them, then decide the implementation.

  python Tools/Art/SpellVFX308/concept_reactions308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_reactions308.py --run      # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_reactions308.py --run r_a_wildfire   # only these

Writes Art/SpellVFX308/Concepts/reactions/rx_<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with game stills attached. Concepts only, never textures (D308-28: commercial terms unconfirmed = TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/reactions'
SHOULDER = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_shoulder.png'   # shoulder camera on the palace plaza
BRUSH = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_brush.png'         # the caster and the great brush
JEOKRO = REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png'        # the 적로 (red-earth) region look
FIRE = REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_fire_language.png'   # the fire language chosen earlier (ink tongues)
PATCH = REPO / 'Art/SpellVFX308/Concepts/ember_patch/p_e_merged.png'         # the ember patch that passed (debate 8)

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder) and the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is the region: 적로, a dry red-earth valley of cracked ochre ground, scorched pines and dark rock - use it for the ground.
Picture 4 is the game's fire language: fire drawn as torn black-ink tongues with only a thin vermilion (#D65A43) edge, never bright flame.
Picture 5 is a fire patch on the ground the game already accepted: a charred ink pool with a few low ink tongues and grey smoke wisps rising - use it as the scale and finish for any ground patch."""

STYLE = """--- THE LOOK ---
A game concept paint-over in the style of picture 1: warm grey-beige and slightly desaturated, like ink wash over a 3D scene. All magic is INK: matte black ink and soaked-in mineral pigment (wood green-black #3E5B3A, fire vermilion only on edges, earth ochre, water indigo-grey #3F5866), like paint on paper - never light. Grammar of the reactions: when one element FEEDS the next (상생) the ink spreads outward and thickens; when one element BREAKS another (상극) the ink cracks apart and is washed away. Effects rise a little off the ground (low wisps, strands), they are not flat decals."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no emissive anything, no light rays, no sparks, no lens flare, no bright flames, no white-hot cores. No red blood. No text, numbers, labels, diagram arrows, readable calligraphy characters, signature, watermark or frame. No HUD, no icons."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

PICTURES = {
    'r_a_wildfire': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 목생화 (wood feeds fire) - a vine field catches and becomes a wildfire ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead, a circle of ground about 3 m across covered by the caster's wood spell - low tangled dark green-black ink vines and thorns lying on the red earth, two gaunt matte enemies (soot-dark wolves with rust crystal slag on their spines) slowed inside it. The caster is swinging the brush, a single fire stroke (torn black ink with a vermilion edge) flying toward the vines.
Panel 2 (after, 0.5 s): the vines have caught: the same patch is now about 1.5 times wider, the vines blackened into charred ink and torn ink fire-tongues (thin vermilion edges only) running outward along every vine, thick grey-black smoke wisps rising; the patch's old green edge is visibly consumed into a ring of ash. The enemies recoil inside it. The growth (bigger, thicker) must read at a glance."""),
    'r_b_steam': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 수극화 (water breaks fire) - a fire patch is put out and leaves a veil of steam ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead a burning patch about 3 m across (as in picture 5: charred ink pool, low ink fire-tongues with vermilion edges, smoke), an enemy standing in it; the caster has sent a low wave of water ink (indigo-grey washes, white paper showing through as foam) rolling toward it.
Panel 2 (after, 0.4 s): where the wave met the patch the charred ink is CRACKED APART into dark wet islands and washed toward the edges, the fire tongues are gone, and a low dense veil of grey-white steam drawn as soft dry-brush ink rises about 1.5 m high over the whole patch, so the enemy behind it is half hidden. A clear dark wet line marks where the wave passed. Breaking, not growing: the patch is gone, the steam stays."""),
    # round 2 (debate 21: Codex + Fable) - a fixed, b fixed, c redrawn
    'r_a2_wildfire': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 목생화 (wood feeds fire) - a vine field catches and becomes a wildfire, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead, a circle of ground about 3 m across covered by the caster's wood spell - low tangled dark green-black ink vines and thorns lying on the red earth, two gaunt matte enemies (soot-dark wolves with rust crystal slag on their spines) slowed inside it. The caster is swinging the brush, a single fire stroke (torn black ink with a vermilion edge) flying toward the vines.
Panel 2 (after, 0.5 s): the VINES ARE STILL THERE as the fuel: every branching vine stroke is now charred black, and small torn black ink tongues run ALONG the vine branches and out past their tips, so the branching pattern is clearly visible - this must NOT look like a round pool or a ring of fire. The tongues are black ink bodies with only a thin vermilion edge, no red filling, the red area is small. The old green outline has burnt to pale grey ash (#867C6B) and a new, 1.5 times wider ragged outline of vine tips spreads beyond it. A few sparse grey smoke strands, not a thick cloud. The enemies stand in it unchanged (no recoil)."""),
    'r_b2_steam': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 수극화 (water breaks fire) - a fire patch is put out and leaves a veil of steam, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead a burning patch about 3 m across (as in picture 5: charred ink pool, low ink fire-tongues with vermilion edges, smoke), an enemy standing in it; the caster has sent a low wave of water ink (indigo-grey washes, very little white foam) rolling toward it.
Panel 2 (after, 0.4 s): where the wave met the patch the charred ink is CRACKED APART into dark wet islands and washed toward the edges, the fire tongues are gone, a dark wet line marks where the wave passed. Steam rises as a few loose columns of DARK grey-brown dry-brush ink (darker than the ground at the base, only the tips fade lighter, never white, never brighter than the ground), drifting in the wave's direction, at most chest high (1.3 m), thin and broken, with the middle open: the enemy's head, arms and weapon stay clearly visible through it (at least 70% of the enemy visible). No ring shape, no wall."""),
    'r_c_word_join': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: the word join - two glyphs written one after the other fuse into one word and seal an enemy (감옥 'prison') ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): in the air in front of the caster hang two just-finished brush-written ink shapes side by side (draw them as abstract wet black brush marks with thick and thin strokes, NOT readable letters), a thin wet ink thread connecting the end of the first to the start of the second; 7 m ahead a gaunt enemy carries a small dark-green ink mark on its chest (the first spell's mark).
Panel 2 (after, 0.5 s): the two marks have run together into one heavy ink knot which has dropped along the thread onto the marked enemy: around the enemy stands a cage of vertical indigo-grey water-ink bars rising from the ground like falling rain frozen in place, about 2.5 m tall, the enemy pressing against them. The join (two become one, then fall onto the target) must read."""),
    # round 3 (debate 21b: Codex re-draw / Fable fix - straight columns read as after-fire smoke, not a steam veil)
    'r_b3_steam': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 수극화 (water breaks fire) - a fire patch is put out and leaves a veil of steam, third version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead a burning patch about 3 m across (as in picture 5: charred ink pool, low ink fire-tongues with vermilion edges, a little smoke), an enemy standing in it; the caster has sent a low wave of water ink (indigo-grey washes, very little white foam) rolling toward it.
Panel 2 (after, 0.5 s): the charred ink is CRACKED APART into dark wet islands, the fire tongues are gone, a dark wet line marks where the wave passed. From MANY of the wet cracks at once, steam billows up and is pushed SIDEWAYS in the wave's direction as one low, wide, broken bank of soft grey-brown dry-brush ink (darker than the ground at its base, thinning and lighter only at its upper edge, never white, never brighter than the ground): about 1.5 to 2 m wide, knee to chest high (0.8 to 1.2 m), torn into a few loosely linked drifting sheets with gaps - NOT thin vertical columns, NOT a solid wall, NOT a ring. The middle is open so the enemy's face, arms and weapon stay clearly visible (at least 70% of the enemy visible). It must read as steam pushed by the water, clearly different from the thin vertical smoke of a fire."""),
    'r_c2_word_join': ([SHOULDER, BRUSH, JEOKRO],"""--- THE PICTURE: the word join - two glyphs written one after the other fuse into one word and seal an enemy (감옥 'prison'), second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): above and to either side of a gaunt enemy 7 m ahead hang two SMALL just-finished wet black brush marks (abstract, NOT readable letters), one with a dark green-black edge (wood), one with an indigo-grey edge (water); a thin wet ink thread runs between them. The enemy's face and body are clear.
Panel 2 (after, 0.5 s): the thread has pulled tight into a heavy black ink knot right above the enemy, and from the knot about eight long strokes have FALLEN like rain along threads and stuck into the ground around the enemy, about 2.5 m tall: they look like wet black charred wooden stems (wood), with indigo-grey (#3F5866) only at their edges and where they hit the ground with small ink splashes (water). They come DOWN from the knot - nothing rises from the ground, no round ring on the ground. Matte, no highlights, no ice, no metal, no blue glassy bars. The enemy is visible between the stems (at least 70% visible), pressing against them."""),
}


def prompt(pid):
    refs, body = PICTURES[pid]
    ref = REF if FIRE in refs else '\n'.join(REF.splitlines()[:4])
    return '\n\n'.join([HEAD, ref, STYLE, body, BAN, TAIL % (OUT / f'rx_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'rx_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'rx_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'rx_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'rx_{pid}.png').exists())


if __name__ == '__main__':
    main()
