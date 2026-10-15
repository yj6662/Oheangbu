#!/usr/bin/env python3
"""#308 노 smoke concepts before the own VFX Graph (user 2026-10-15 "만들기 전에 codex image gen으로 시안 먼저 뽑고 vfx만들자").

  python Tools/Art/SpellVFX308/concept_na_smoke308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_na_smoke308.py --run      # also runs codex exec for each (parallel)

Writes Art/SpellVFX308/Concepts/na_smoke/na_smoke_<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with two game stills attached: ref_intended_soot.png (pq_drop frame 3 - the soot the user pointed at, "내가 의도한
건 이런 연기였는데") and ref_current_game.png (pq_vfxg3 - the VFX Graph spike, a round cloud). The pictures are concepts for
the graph's shape, motion and tone only; they are never used as textures (D308-28: commercial terms unconfirmed = TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/na_smoke'
REFS = [OUT / 'ref_intended_soot.png', OUT / 'ref_current_game.png']

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE TWO ATTACHED PICTURES (game screenshots) ---
Picture 1 is the effect the art director wants: look at the dark torn soot right of the character - ragged, uneven, broken into wisps and tatters with soft lost edges, dense dark cores and see-through frayed ends, lying low over the ground. That texture and tone is the target.
Picture 2 is the current attempt: the soot gathers into one round even cloud hanging in the air in front of the character. That shape is what we want to get away from.
Use them for the camera, the setting and the soot's tone. Do not copy the user interface."""

SCENE = """--- THE SHOT (same in every panel) ---
A game VFX concept paint-over in the style of the game's screenshots: a wide stone-paved plaza of a Joseon-era Korean palace town, warm grey-beige ground, dark tiled roofs and a distant palace gate, hazy hills, pale warm paper-coloured sky; everything slightly desaturated, warm, like ink wash over a 3D scene. Camera behind and slightly above the caster's right shoulder, as in picture 1. The caster - a scholar-mage in a dark worn hanbok-like coat with messy dark hair - has just swung a huge calligraphy brush on a long staff forward and holds it out level. One standing figure about 6 metres ahead is the target.
The spell is 노 (fire, by ink): it is NOT flame and NOT light. It is soot - scorched black-brown smoke thrown from the brush tip forward over the ground toward the target. Smoke tones only: darkest soot #2A2622, dark soot-brown #3E362C, warm grey #867C6B fading into the air; never pure black, never blue-black."""

STRIP = """--- LAYOUT ---
The image is ONE wide strip of three equal panels side by side, left to right, separated by thin plain paper-coloured gaps, all with the identical camera: panel 1 = 0.1 s after the swing, panel 2 = 0.4 s, panel 3 = 1.0 s. The same soot develops across the three panels."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no bright embers, no orange or yellow fire, no sparks, no light rays, no lens flare, nothing emissive. No flying debris, no rocks, no droplets. No text, numbers, labels, arrows, calligraphy, seal stamp, signature, watermark or frame. No HUD."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

PICTURES = {
    'a_ground_fan': """--- THE SOOT: a low ground fan ---
Panel 1: a dense dark soot burst leaves the brush tip, already tearing at its edges. Panel 2: it has been flung forward and spread into a FLAT FAN about 30 degrees wide that hugs the ground - knee-high at most - reaching most of the way to the target; its leading edge rolls and tears into ragged tongues and tatters, darker cores inside, see-through frayed ends. Panel 3: it has settled and crawls slowly along the ground around the target's feet, thinning into torn grey wisps that lift a little, gaps opening between them. The shape is a wedge on the ground widening away from the caster, never a ball.""",
    'b_torn_ribbons': """--- THE SOOT: torn ribbons ---
Panel 1: three or four long dark soot ribbons leave the brush tip along the swing, like smoke trailing from a snuffed wick but heavy and dark. Panel 2: the ribbons stretch forward toward the target, twisting and curling around each other in turbulent eddies, fraying into finer strands, each ribbon dense in the middle and torn at its edges. Panel 3: the ribbons have broken into many separate curling wisps drifting low near the target, slowly rising and fading into warm grey. Long, stretched, twisting shapes - never a round puff.""",
    'c_scorch_root': """--- THE SOOT: heavy scorch with a smothered root ---
Panel 1: at the brush tip a compact knot of the darkest soot with a dull, unlit dark-red-brown pigment (#5A2A20, like dried blood or scorched lacquer, NOT glowing) in its very heart. Panel 2: the knot has burst forward into heavy rolling soot that drags low along the ground toward the target, the dull red already smothered and gone, only black-brown soot with torn ragged edges. Panel 3: the soot has spread around the target and breaks into torn tatters and lifting wisps, leaving a faint dark scorch stain on the paving where it passed.""",
    'd_rolling_wall': """--- THE SOOT: a rolling scorched wave ---
Panel 1: soot pours from the brush tip and drops to the ground. Panel 2: it rolls forward along the paving as a low turbulent wave about waist-high, its crest curling over itself and tearing into ragged tongues, the underside dense and dark, the top frayed and see-through. Panel 3: the wave has broken over the target's position and collapsed into many torn drifting wisps close to the ground, thinning to warm grey. Wide and low like a breaking wave of smoke, never a sphere.""",
}


def prompt(pid):
    return '\n\n'.join([HEAD, REF, SCENE, STRIP, PICTURES[pid], BAN, TAIL % (OUT / f'na_smoke_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for pid in PICTURES:
        (OUT / f'na_smoke_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in PICTURES:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in REFS:
            cmd += ['-i', str(r)]
        log = open(OUT / f'na_smoke_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'na_smoke_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'na_smoke_{pid}.png').exists())


if __name__ == '__main__':
    main()
