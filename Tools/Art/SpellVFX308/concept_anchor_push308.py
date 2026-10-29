#!/usr/bin/env python3
"""#308 debate 17 concepts: 숨 (iron-house domain: inside it the player is not pushed or pulled, damage still lands) has no
consumer - no enemy pushes the player yet. Before the debate (which enemy pushes, how far, what the anchored player looks like)
the main session draws the candidates (process: concepts first, debate on them, then decide the implementation).

  python Tools/Art/SpellVFX308/concept_anchor_push308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_anchor_push308.py --run      # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_anchor_push308.py --run p_a_wolf_push   # only these

Writes Art/SpellVFX308/Concepts/anchor_push/ap_<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with game stills attached. Concepts only, never textures (D308-28: commercial terms unconfirmed = TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/anchor_push'
SHOULDER = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_shoulder.png'   # shoulder camera on the palace plaza
BRUSH = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_brush.png'         # the caster and the great brush
JEOKRO = REPO / 'Art/SpellVFX308/Concepts/ground_rise/ref_jeokro.png'        # the 적로 (red-earth) region look
DOMAIN = REPO / 'Art/SpellVFX308/Concepts/ground_rise/d_water_strands.png'   # the domain grammar chosen in debate 10 (strands)

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder) and the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat with a ragged hem, wrapped forearms, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is the region: 적로, a dry red-earth valley of cracked ochre ground, scorched pines and dark rock - use it for the ground and the setting."""

REF_DOMAIN = """Picture 4 is the domain look the game already chose: a ring of ground about 3 m across marked by thin rising ink strands like incense smoke, a fence of smoke, not a wall of light. For this domain (METAL, 숨 'iron house') the strands are pale blue-grey #A6B0AD soaked into black ink, rising straight and still, slightly heavier and more vertical than in picture 4, like iron bars drawn in smoke."""

STYLE = """--- THE LOOK ---
A game concept paint-over in the style of picture 1: everything warm grey-beige and slightly desaturated, like ink wash over a 3D scene. All the magic is INK: matte black ink and soaked-in mineral pigment, like paint on paper - never light.
THE ENEMY: a fire-tainted wolf of the red-earth valley, the size of a large dog, gaunt and wrong in its proportions (legs a little too long, ribs showing), its fur matted dark with soot, rust-red cracked crystal growths along the spine like dried ember slag, a broken iron shackle ring on one foreleg, thin dull gold seams across the muzzle like old kintsugi repairs. Grotesque but matte: nothing on it glows, no fire, no light from the eyes."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no emissive anything, no light rays, no sparks, no lens flare, no flames. No red blood (a hit reads as a black ink splash). No text, numbers, labels, diagram arrows, readable calligraphy characters, signature, watermark or frame. No HUD, no icons."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

PICTURES = {
    'p_a_wolf_push': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: the wolf's charge knocks the caster back (no domain) ---
LAYOUT: ONE wide strip of THREE equal panels left to right, identical shoulder camera, thin paper-coloured gaps.
Panel 1 (0.0 s): the wolf low and charging straight at the caster from 6 m ahead, dust kicked up behind it, the caster bracing with the brush across the body.
Panel 2 (0.1 s): the impact - the wolf's shoulder slams the caster's chest, a small black ink splash at the contact point, the caster's feet leaving the ground slightly, coat thrown back.
Panel 3 (0.35 s): the caster has been shoved about 3 m backwards and is skidding to a stop, knees bent, one hand down, two long dragged furrows of disturbed red earth with black ink scuffs in them from where the feet were to where they are now; the wolf runs on past beside the caster. The camera has been pushed back with the caster - the distance travelled must read clearly."""),
    'p_b_anchored': ([SHOULDER, BRUSH, JEOKRO, DOMAIN], """--- THE PICTURE: the same charge inside the iron-house domain (숨) - the caster does not move ---
LAYOUT: ONE wide strip of THREE equal panels, identical shoulder camera, thin paper-coloured gaps. The caster stands in the middle of the metal domain ring (picture 4 grammar, pale blue-grey iron strands).
Panel 1 (0.0 s): the wolf charging from 6 m ahead; the caster inside the ring of still vertical blue-grey ink strands, feet planted.
Panel 2 (0.1 s): the impact - the wolf's shoulder hits; a small black ink splash on the caster's coat (the damage still lands); the caster does not budge, but the strands of the ring on the caster's side all lean hard in the direction of the shove and the push visibly breaks against them like water on a weir: a short fan of grey ink flecks thrown sideways along the ground at the caster's heels.
Panel 3 (0.35 s): the caster exactly where they stood, heels rooted, a short dark scuff only under the heels (no slide), the strands snapping back upright; the wolf staggers past, its charge spent."""),
    'p_c_shove_tell': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: how the player learns which hit pushes (the tell) ---
ONE single panel, shoulder camera, the wolf at about 7 m in its wind-up before the charge: it is crouched low with its head down and its hind legs gathered, and a short dark smear of soot is dragged along the ground in front of it in the exact line it will run - a matte black ink streak like a brush pulled across the earth, 4 to 8 m long, pointing at the caster. This streak is the warning that the coming hit shoves. Nothing glows; the warning is ink on the ground and the pose."""),
    'p_d_pull_maw': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: a pull (for comparison - the domain also stops pulls) ---
ONE single panel, shoulder camera. A larger grotesque beast of the same valley (bear-sized, crystal slag growths, shackles, gold seams, matte) at 9 m, rearing back with its jaws wide, drawing a long breath: the loose red dust, ash and the caster's coat hem stream toward its mouth in dark ink-wash streaks, and the caster leans back against it, being dragged forward a step, the brush planted in the ground like a staff. Nothing glows."""),
}


def prompt(pid):
    refs, body = PICTURES[pid]
    ref = REF + ('\n' + REF_DOMAIN if DOMAIN in refs else '')
    return '\n\n'.join([HEAD, ref, STYLE, body, BAN, TAIL % (OUT / f'ap_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'ap_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'ap_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'ap_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'ap_{pid}.png').exists())


if __name__ == '__main__':
    main()
