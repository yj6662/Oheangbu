#!/usr/bin/env python3
"""#308 D308-125 적용 5 ember patch + scorch concepts before any VFX (user 2026-10-21 "vfx, 엔티티 외형 등은 codex의 image gen으로
시안 먼저 뽑고, 그 시안 두고 토론과정 거친 후 판정을 반복하고, 구현 방식까지 토론에서 정한 후 구현 진행해").

  python Tools/Art/SpellVFX308/concept_ember_patch308.py                 # writes the prompts
  python Tools/Art/SpellVFX308/concept_ember_patch308.py --run           # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_ember_patch308.py --run p_a s_c   # only these ids

Writes Art/SpellVFX308/Concepts/ember_patch/<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with game stills attached: ref_jeokro_ground.png (RealmLook308 r3 적로 open), ref_fire_language.png (the 노 fire
concept the user chose on 10-16: red pigment ink flame, no light) and, for the scorch set, ref_firstperson.png (the first-person
view with the great brush). The pictures are concepts for shape, motion, tone and readability only; they are never used as
textures (D308-28: commercial terms unconfirmed = TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/ember_patch'
GROUND, FIRE, FP = OUT / 'ref_jeokro_ground.png', OUT / 'ref_fire_language.png', OUT / 'ref_firstperson.png'

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

INK = """--- THE LOOK OF FIRE IN THIS GAME (absolute) ---
Fire here is PAINTED, NOT LIT: red pigment ink on the scene, matte like paint on paper, in East Asian ink-painting style. Inside, vermilion / cinnabar red #B8432F and deep red-brown #6E2A1F; toward the edges the red is swallowed by soot-black ink #2A2622, and edges break up like a dry calligraphy brush stroke (split bristle streaks, 갈필). Smoke is warm grey-brown soot #3E362C fading to #867C6B. The fire gives NO light: nothing around it is lit up, there is no bright core, no glow. Danger must read through shape, contrast and motion, never through light."""

STRIP = """--- LAYOUT ---
The image is ONE wide strip of three equal panels side by side, left to right, separated by thin plain paper-coloured gaps, all with the identical camera. %s"""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no light cast on the ground or the figures, no bright yellow or white flame core, no emissive anything, no sparks, no light rays, no lens flare. No flying debris, no rocks, no droplets. No text, numbers, labels, arrows, readable calligraphy characters, seal stamp, signature, watermark or frame.%s"""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

# --- set 1: the patch on the ground, seen in first person -------------------------------------------------------------
PATCH_REF = """--- THE TWO ATTACHED PICTURES ---
Picture 1 is a game screenshot of the region 적로 (Jeokro, "the red road"): rust-red earth slopes with dry pale grass, black burnt leafless trees, a pale grey-green sky, everything slightly desaturated, warm, like ink wash over a 3D scene. Use it for the setting, the ground colour and the tone.
Picture 2 is the fire concept the art director chose for the player's fire spell: use it ONLY for the way fire is painted (red pigment ink tongues with dry-brush soot edges). Do not copy its town, characters or layout."""

PATCH_SCENE = """--- THE SHOT ---
First-person game view at eye height (1.7 m), looking slightly down along a rust-red earth slope of 적로 with burnt black trees, like picture 1. About 9 m ahead stands an enemy: a gaunt deserter foot-soldier of the Joseon army in torn, scorched padded armour, hunched and wrong in its proportions, its skin streaked with ash. A moment ago it made the ground in front of the viewer erupt; where the eruption burst, about 4 m ahead of the viewer, it has left an EMBER PATCH: a round patch of burning ground about 2.7 m across lying flat on the slope. The patch is the subject. The player must see at a glance from where they stand: where the patch begins and ends (its edge), that it is dangerous to stand in, and how long it still has. The rust-red ground is a hard background for red fire - solve that contrast."""

PATCH_TIMES = 'Panel 1 = 0.2 s after the burst; panel 2 = 3 s later, the patch at full strength; panel 3 = 6 s, the patch dying out and what it leaves on the ground.'

PATCH = {
    'p_a_char_pool': """--- THE PATCH: a pool of char with red veins ---
Panel 1: the burst's red-ink flame tongues are collapsing straight down into the earth, and a pool of black-brown char ink spreads out from the centre like ink soaking into paper, its outer edge a crisp dry-brush ring. Panel 2: the round pool is fully spread, soot-black and matte; through it run cracks and veins of cinnabar pigment, like the cracks in burning charcoal, slowly crawling and changing shape; thin torn soot wisps rise from the veins. The black pool with its sharp brushed rim stands out clearly on the rust-red earth. Panel 3: the red veins have dried to dark brown one by one from the edge inward, the wisps are gone, and only a dark soot stain remains on the ground, already fading.""",
    'p_b_tongue_ring': """--- THE PATCH: a ring of low flame tongues ---
Panel 1: the burst leaves a round scorched spot, and along its circumference small red-ink flame tongues spring up. Panel 2: a ring of low flame tongues, ankle to knee high, stands along the edge of the 2.7 m circle, leaning and flickering, each tongue a single bold red brush stroke turning soot-black and split at its tip; the inside of the ring is smouldering black char with soot smoke drifting up. The ring marks the edge unmistakably, like a fence of fire. Panel 3: the tongues have collapsed one after another into soot, leaving a ring-shaped scorch stain on the earth.""",
    'p_c_ash_rim': """--- THE PATCH: an ash rim around live coals ---
Panel 1: where the burst struck, the earth is blasted black and a ring of pale grey-white ash is thrown out to the circle's edge. Panel 2: the patch reads like the burnt edge of a sheet of paper laid on the ground: a crumbling pale ash rim, bone-grey and paper-white, outlines the 2.7 m circle - the brightest value in the picture, standing out against the rust-red earth - and inside it lies black char strewn with dull cinnabar coals painted as dabs of red pigment, with short flame tongues flickering up here and there and thin soot rising. Panel 3: the coals have gone dark, the ash rim crumbles and scatters, a grey-black smudge remains.""",
    'p_d_smoke_column': """--- THE PATCH: blackened ground under a leaning smoke column ---
Panel 1: the burst's flames sink into the ground and a thick soot cloud billows up. Panel 2: the circle of ground is blackened and dotted with low red-ink flame tongues, and from all of it rises one tall leaning column of grey-brown soot smoke, torn at its edges, drifting with the wind - readable from far away, like a signal - while low ground soot creeps along the edge of the circle. Panel 3: the column has thinned to a few wisps, the flame tongues are gone, the blackened circle is fading into the red earth.""",
}

# --- set 2: the scorch on the player, first person ---------------------------------------------------------------------
SCORCH_REF = """--- THE TWO ATTACHED PICTURES ---
Picture 1 is a game screenshot of the player's first-person view: the player holds a long-handled great calligraphy brush in the right hand, seen at the lower right; the arms wear a dark worn hanbok-like coat with long sleeves. The interface elements at the screen edges are the game's HUD: copy ONLY the two liquid jars at the lower left (health = red liquid, ink = black liquid) where the description below asks for them, and leave out every other interface element (no compass bar, no minimap, no icons, no reticle, no text).
Picture 2 is the fire concept the art director chose for the player's fire spell: use it ONLY for the way fire and soot are painted. Do not copy its town, characters or layout."""

SCORCH_SCENE = """--- THE SHOT ---
First-person game view on a rust-red earth slope of 적로 with burnt black trees and a pale grey-green sky. The player has just stepped out of a patch of burning ground and is SCORCHED: for 4 seconds they keep burning wherever they go, a little damage every half second, and dodging does not put it out. The player must understand three things without any text: I am burning, it is still going, and it is about to end. The camera sees the player's own right hand on the great brush at the lower right and the cuff of the left sleeve entering at the lower left, as in a first-person game; the rest of the body is below the frame."""

SCORCH_TIMES = 'Panel 1 = 0.3 s after catching; panel 2 = 2 s, at full strength; panel 3 = 3.8 s, just before it ends.'

SCORCH = {
    's_a_sleeve_smoke': """--- THE SCORCH: smoke from the sleeves ---
Panel 1: the sleeve cuffs and the hand on the brush start to smoke: thin curls of soot rise from the cloth. Panel 2: thick torn soot smoke pours up from both sleeve cuffs and the hand, curling up into the lower third of the view, with dull cinnabar specks smouldering in the cloth's weave and fine ash flakes drifting up past the camera. Panel 3: only thin wisps remain and the cloth is left darkened with soot.""",
    's_b_creeping_char': """--- THE SCORCH: burn creeping along the hem ---
Panel 1: a thin line of red pigment appears at the edge of the left sleeve's cuff, like the edge of burning paper. Panel 2: the burn line has crept along the sleeve hem and onto the brush hand's cuff - a cinnabar line with black char behind it and tiny red-ink flame tongues licking up from it - and soot smoke trails off it. Panel 3: the line has stopped and gone dark; the hem is charred black and still smoking faintly.""",
    's_c_rising_wisps': """--- THE SCORCH: soot rising through the view ---
Panel 1: dark torn soot wisps start rising from below the bottom edge of the frame (from the burning body). Panel 2: a veil of torn soot rises across the lower third of the view, curling and thinning by mid-screen, the hand and sleeve also smoking, ash flakes floating up - enough to read as 'I am on fire' without hiding the enemy ahead. Panel 3: the veil has thinned to a few frayed wisps at the bottom edge.""",
    's_d_health_jar': """--- THE SCORCH: shown in the health jar ---
Copy the two liquid jars from picture 1 at the lower left (red health liquid, black ink liquid). Panel 1: a thin curl of soot rises from the mouth of the red health jar and the red liquid starts to churn darker at its surface. Panel 2: soot smoke churns inside the health jar above the red liquid, curling out of its mouth, and the liquid level visibly drops in small steps; a little smoke also rises from the sleeve cuff at the lower left. Panel 3: the smoke in the jar has almost cleared; one last wisp leaves its mouth.""",
}


# --- round 2 (debate 8, Codex + Fable): the merged direction at the true camera geometry ------------------------------
GRID = """--- LAYOUT ---
The image is a 2 x 2 grid of four equal 16:9 panels (reading order: top-left, top-right, bottom-left, bottom-right), separated by thin plain paper-coloured gaps, all with the identical camera."""

GEOMETRY = """--- TRUE SIZE ---
The camera is at 1.7 m eye height. The patch centre lies on the ground 4 m ahead; the patch is 2.7 m across, so its near edge is about 2.65 m and its far edge about 5.35 m from the viewer. Draw it at that true size and foreshortening (seen from about 23 degrees above the ground)."""

MERGED = """The patch is a flat, matte, soot-black char pool exactly filling the 2.7 m circle, darker than the rust-red earth, with a crisp dry-brush outer rim that runs unbroken around the whole circle (small bristle gaps are fine, no missing stretches). Along that rim, just inside it, stands a ring of 10 to 14 separate low flame tongues with gaps between them, each a single bold vermilion red-ink brush stroke turning soot-black and split at its tip, at most 35 cm high, leaning slightly up and inward, never reaching outside the rim. Inside the circle only 3 or 4 short sparse tongues (under 15 cm) and at most three thin soot wisps. NO red cracks or veins, NO continuous red surface anywhere (it must never read as lava), NO tall smoke column, NO white ash."""

SCORCH_SCENE2 = """--- THE SHOT ---
First-person game view on a rust-red earth slope of 적로 with burnt black trees and a pale grey-green sky, framed exactly like picture 1: ONLY the player's right hand gripping the long brush shaft at the lower right, with the dark sleeve cuff at the bottom-right corner. There is NO left arm, NO left hand and NO left sleeve anywhere in the frame. The brush tip is clean ink-black and nothing comes off it. At the lower left are the game's two liquid jars from picture 1 (red health liquid, black ink liquid): copy them and no other interface element. The centre of the screen, where an enemy stands about 8 m ahead, stays completely clear. The player has just stepped out of a patch of burning ground and is SCORCHED: for 4 seconds they keep burning wherever they go. The player must understand without any text: I am burning, it is still going, it is about to end."""

ROUND2 = {
    'p_e_merged': ('patch', """--- THE PATCH (round 2): a ring of low tongues on a black char pool ---
""" + MERGED + """
Panel 1 (0.3 s after the burst): the black pool has just spread to the full rim and the rim tongues spring up together.
Panel 2 (3 s): full strength: the ring of tongues on the unbroken black rim, clearly a burning circle.
Panel 3 (5.5 s, about to end): the rim tongues are half as many and half as tall, their red tips mostly dried to soot, but the black pool and its rim still hold exactly the same circle.
Panel 4 (just after 6 s, over): no tongues and no red at all; a ring of warm grey ash stubs (#867C6B, NOT white) where the tongues stood and a faint motionless soot stain, already fading into the earth."""),
    'p_f_wiped': ('patch', """--- THE PATCH WIPED BY 온 (the player's water wave) ---
The patch: """ + MERGED + """
온 is a low sheet of dark wet ink-water (blue-black ink, matte, never shiny, no foam highlights) sweeping along the ground away from the viewer.
Panel 1: the patch burns at full strength; the leading edge of the ink-water wave reaches its near rim.
Panel 2: the wave passes over the patch: the tongues are pressed flat and collapse inward toward the centre, their red turning at once to wet black ink; the char pool blurs and bleeds like wet ink on paper.
Panel 3: half a second later the wave has passed: no tongues, no red; only a wet, darker, slightly spread grey-black stain with soft bled edges where the patch was.
Panel 4: two seconds later: the stain has nearly sunk into the red earth; nothing burns."""),
    's_e_merged': ('scorch', """--- THE SCORCH (round 2): the right hand smoulders, the health jar clouds ---
On the back of the right hand and the right cuff: 3 or 4 flat strokes of vermilion red pigment ink (#B8432F, matte) like a burn painted on the knuckles and cloth. From the knuckles and the cuff rise at most three thin matte soot wisps (#3E362C fading to #867C6B) that drift up and off to the right edge of the frame, never higher than a quarter of the screen and never crossing toward the centre. Inside the glass of the red health jar only, grey soot smoke swirls above the liquid; nothing comes out of the jar's mouth; the red liquid level is the same in every panel.
Panel 1 (0.3 s after catching): the red strokes flash onto the knuckles and cuff, the first wisp curls up, smoke begins to rise inside the jar from the bottom.
Panel 2 (2 s, full): red strokes, three wisps, the jar's glass clouded with smoke above the liquid.
Panel 3 (3.8 s, just before it ends): the strokes have dried mostly to soot-black, one single short wisp, the jar's smoke clearing from the top down.
Panel 4 (washed off by 운, the player's water ring, at 2 s): the wisps are cut off at once, the red strokes are washed to a wet grey smear running down the back of the hand, the jar's smoke is gone; 2 m ahead on the ground the ember patch the player stepped out of is still burning with its low red-ink tongues."""),
}


def prompt(pid):
    if pid in ROUND2:
        kind, body = ROUND2[pid]
        if kind == 'patch':
            parts = [HEAD, PATCH_REF, INK, PATCH_SCENE, GEOMETRY, GRID, body, BAN % '']
        else:
            parts = [HEAD, SCORCH_REF, INK, SCORCH_SCENE2, GRID, body, BAN % '']
    elif pid in PATCH:
        parts = [HEAD, PATCH_REF, INK, PATCH_SCENE, STRIP % PATCH_TIMES, PATCH[pid], BAN % '']
    else:
        hud = '' if pid == 's_d_health_jar' else ' No HUD of any kind.'
        parts = [HEAD, SCORCH_REF, INK, SCORCH_SCENE, STRIP % SCORCH_TIMES, SCORCH[pid], BAN % hud]
    return '\n\n'.join(parts + [TAIL % (OUT / f'{pid}.png').as_posix()])


def refs(pid):
    kind = ROUND2[pid][0] if pid in ROUND2 else ('patch' if pid in PATCH else 'scorch')
    return [GROUND, FIRE] if kind == 'patch' else [FP, FIRE]


def main():
    ids = [a for a in sys.argv[1:] if not a.startswith('--')] or list(PATCH) + list(SCORCH) + list(ROUND2)
    OUT.mkdir(parents=True, exist_ok=True)
    for pid in ids:
        (OUT / f'{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in ids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in refs(pid):
            cmd += ['-i', str(r)]
        log = open(OUT / f'{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'{pid}.png').exists())


if __name__ == '__main__':
    main()
