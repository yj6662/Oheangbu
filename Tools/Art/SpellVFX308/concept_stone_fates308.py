#!/usr/bin/env python3
"""#308 D308-125 ④ the three fates of an enemy's thrown stone, concepts before any VFX (user 2026-10-21 "vfx, 엔티티 외형 등은
codex의 image gen으로 시안 먼저 뽑고, 그 시안 두고 토론과정 거친 후 판정을 반복하고, 구현 방식까지 토론에서 정한 후 구현 진행해").

  python Tools/Art/SpellVFX308/concept_stone_fates308.py                 # writes the prompts
  python Tools/Art/SpellVFX308/concept_stone_fates308.py --run           # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_stone_fates308.py --run b_a c_b   # only these ids

Today a main-world stone-thrower's stone (MineRangedNeutral308) ends in one of three ways that the screen does not show:
  막 (a rock the player raised) - the stone flies through the rock and "lands" on the player for 0 damage;
  눗 (the player's fire circle, 3 m) - the stone simply vanishes over the circle (cancelled attack);
  숭 (the player's metal circle, 3 m) - the stone vanishes and the thrower takes 6 damage with nothing flying back.
Writes Art/SpellVFX308/Concepts/stone_fates/<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with game stills attached: ref_firstperson.png (the first-person view and the game's ink look), ref_bandit.png (the
길목 도적 body that throws) and, for 눗, ref_fire_language.png (the fire language the user chose on 10-16). The pictures are
concepts for shape, motion, tone and readability only; they are never used as textures (D308-28: commercial terms unconfirmed
= TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/stone_fates'
FP, BANDIT, FIRE = OUT / 'ref_firstperson.png', OUT / 'ref_bandit.png', OUT / 'ref_fire_language.png'

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot of the player's first-person view: the player holds a long-handled great calligraphy brush in the right hand at the lower right; the world is a 3D scene washed over with East Asian ink painting - pale warm paper-coloured sky, grey-brown grass, dark trees, everything desaturated. Use it for the camera, the brush hand and the tone. Leave out every interface element (no jars, no compass bar, no minimap, no icons, no reticle, no text).
Picture 2 shows the enemy that throws: a Joseon-era road bandit in a dark worn jeogori, pale baggy trousers and a black headband, a crude club at his belt and a heap of grey stone shards bound to his back. In this game enemies are subtly wrong: draw him gaunt, a little too long in the arms, ash-grey skin. Use it for the thrower's body only (ignore the red capsule and the grey test floor).%s"""

FIRE_REF = """
Picture 3 is the fire concept the art director chose for the player's fire spell: use it ONLY for the way fire is painted (red pigment ink tongues with dry-brush soot edges). Do not copy its town, characters or layout."""

INK = """--- THE LOOK (absolute) ---
Everything the spells make is PAINTED INK, NOT LIGHT: matte brush strokes like ink and pigment on paper. Black ink #1E1B18 to soot grey #3E362C; fire = vermilion red pigment #B8432F turning soot-black at its dry-brush edges; metal = cold blue-grey steel ink #6F7A80 with crisp thin edges, like a single fast brush stroke. Nothing glows, nothing lights up anything around it, no bright core. A moment must read through shape, contrast and motion only."""

SCENE = """--- THE SHOT ---
First-person game view at eye height (1.7 m) on a grassy hillside road of grey-brown grass, framed like picture 1: the player's right hand on the great brush at the lower right, nothing else of the player's body. About 11 m ahead the bandit of picture 2 has just hurled a STONE at the player: a fist-sized grey stone wrapped in a short torn tail of black ink that streams behind it as it flies. The stone flies straight at the camera, chest high. The subject is what happens to that stone. The player must read at a glance, without any text: my spell stopped that stone, and how."""

STRIP = """--- LAYOUT ---
The image is ONE wide strip of three equal panels side by side, left to right, separated by thin plain paper-coloured gaps, all with the identical camera. %s"""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no light cast on anything, no bright white or yellow core, no emissive anything, no sparks, no light rays, no lens flare, no magic particles of light. No HUD or interface of any kind. No text, numbers, labels, arrows, readable calligraphy characters, seal stamp, signature, watermark or frame."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

# --- 막: the rock between the player and the thrower ------------------------------------------------------------------
ROCK = """--- THE PLAYER'S SPELL: 막 (a rock of ink) ---
Three metres ahead, between the player and the bandit, stands the rock the player raised a moment ago: a squat hump about 1.2 m high and 2 m wide, painted as overlapping upright strokes of dark earth-brown and black ink with a low skirt of earth dust at its foot, matte, like heaped ink on paper. The bandit stands beyond it, partly hidden behind its right shoulder. The stone is thrown so that the rock lies on its line."""

ROCK_TIMES = 'Panel 1 = the stone in flight, half-way, about to reach the rock; panel 2 = the instant it meets the rock; panel 3 = 0.6 s later.'

FATES = {
    'a_a_rock_shatter': ('rock', ROCK_TIMES, """--- THE FATE: the stone shatters on the rock face ---
Panel 1: the stone with its black ink tail clearly flying toward the rock's face. Panel 2: the stone smashes on the rock's near face: it bursts into a flat star-shaped splat of black ink pressed against the rock with a few short broken stone chips kicked sideways (not toward the camera), and its ink tail crumples into the splat. Panel 3: the chips have dropped to the rock's foot in a small puff of earth dust; the black splat is running down the rock face in a few thin drips and fading; the rock stands unharmed; the bandit beyond it is already reaching back for another stone."""),
    'a_b_rock_swallow': ('rock', ROCK_TIMES, """--- THE FATE: the rock swallows the stone ---
Panel 1: the stone with its black ink tail flying toward the rock. Panel 2: as the stone touches the rock, the rock's ink strokes ripple outward around the point of contact like a thick wet brush stroke being pressed - the stone sinks into the rock's face leaving a dark round dent, its ink tail sucked in after it. Panel 3: the dent is closing back, a dark ring of wet ink around it slowly drying to the rock's brown; a thin wisp of earth dust drifts off the rock; no stone anywhere."""),
}

# --- 눗: the fire circle on the ground ---------------------------------------------------------------------------------
FIRE_CIRCLE = """--- THE PLAYER'S SPELL: 눗 (a circle of fire) ---
Around the player lies a circle of fire on the ground, 3 m in radius, so its far edge crosses the road about 3 m ahead and it curves out of frame to both sides: a thin ring of low red-ink flame tongues along the edge, ankle high, each a single vermilion brush stroke turning soot-black at its split tip, with a faint dark scorched band inside the rim. It burns stones that fly in over it (at most four)."""

FIRE_TIMES = 'Panel 1 = the stone in flight, about to cross the circle\'s far edge; panel 2 = the instant it is over the edge; panel 3 = 0.5 s later.'

FATES.update({
    'b_a_ash_midair': ('fire', FIRE_TIMES, """--- THE FATE: burnt to ash in mid-air ---
Panel 1: the stone with its black ink tail flying in, chest high, about to pass over the far edge of the fire ring. Panel 2: right above the ring the stone is wrapped at once by a sheath of red-ink flame strokes licking backward along its flight; the stone itself goes soot-black; its flight bends downward. Panel 3: the stone has crumbled in the air into a short falling curtain of grey-brown ash flakes and a soft soot puff hanging where it died, 2.5 m ahead, drifting down onto the ring; the ring burns on, one tongue a little taller where the ash fell."""),
    'b_b_tongue_snatch': ('fire', FIRE_TIMES, """--- THE FATE: a flame tongue snatches it ---
Panel 1: the stone with its black ink tail flying in, chest high, toward the far edge of the fire ring. Panel 2: one flame tongue on the ring right under the stone's path leaps up into a tall hooked red-ink stroke - like a single brush flick reaching up two metres - and closes around the stone. Panel 3: the hooked tongue has fallen back into the ring; where it caught the stone a ragged soot cloud hangs in the air and a few charred black stone crumbs fall onto the ring; no stone continues toward the camera."""),
})

# --- 숭: the metal circle turns the stone back ------------------------------------------------------------------------
METAL_CIRCLE = """--- THE PLAYER'S SPELL: 숭 (a circle of metal) ---
Around the player lies a circle of metal on the ground, 3 m in radius, its far edge crossing the road about 3 m ahead and curving out of frame to both sides: a ring of short upright steel-ink blades, knee high, each one crisp thin blue-grey brush stroke, standing like a fence of drawn knives. It turns stones that fly in over it back on the one who threw them (at most three)."""

METAL_TIMES = 'Panel 1 = the instant the stone is over the circle\'s far edge; panel 2 = 0.15 s later; panel 3 = 0.4 s later, the stone arriving back at the bandit.'

FATES.update({
    'c_a_blade_strike': ('metal', METAL_TIMES, """--- THE FATE: a blade strikes it back ---
Panel 1: the stone with its black ink tail is right above the far edge of the blade ring; one blade under it whips upward in a single long diagonal steel-ink stroke that meets the stone - a crisp white-paper gap shows the cut line (paper, not light). Panel 2: the stone flies back the way it came, now trailing a thin straight blue-grey steel streak instead of black ink, the struck blade still raised. Panel 3: the stone hits the bandit in the chest; he reels backward, arms thrown out; a small flat splat of black ink and steel-grey strokes marks the hit on his chest; the steel streak hangs in the air along the whole return path, fading from the ring end."""),
    'c_b_caught_turned': ('metal', METAL_TIMES, """--- THE FATE: caught, turned and sent back ---
Panel 1: the stone with its black ink tail stops dead in the air right above the far edge of the blade ring, held by three or four thin taut steel-ink lines drawn up from the blades to it like wires; its ink tail snaps off and drops. Panel 2: the lines pull and the stone swings round, now pointing at the bandit, its surface streaked with blue-grey steel ink. Panel 3: the lines have let go and the stone is a blur shooting back along a straight thin steel streak, striking the bandit, who doubles over; a small flat ink-and-steel splat marks the hit."""),
})


def prompt(fid):
    kind, times, body = FATES[fid]
    spell = {'rock': ROCK, 'fire': FIRE_CIRCLE, 'metal': METAL_CIRCLE}[kind]
    parts = [HEAD, REF % (FIRE_REF if kind == 'fire' else ''), INK, SCENE, spell, STRIP % times, body, BAN]
    return '\n\n'.join(parts + [TAIL % (OUT / f'{fid}.png').as_posix()])


def refs(fid):
    return [FP, BANDIT, FIRE] if FATES[fid][0] == 'fire' else [FP, BANDIT]


def main():
    ids = [a for a in sys.argv[1:] if not a.startswith('--')] or list(FATES)
    OUT.mkdir(parents=True, exist_ok=True)
    for fid in ids:
        (OUT / f'{fid}.prompt.txt').write_text(prompt(fid), encoding='utf-8')
        print('PROMPT', fid)
    if '--run' not in sys.argv:
        return
    procs = []
    for fid in ids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in refs(fid):
            cmd += ['-i', str(r)]
        log = open(OUT / f'{fid}.log.txt', 'w', encoding='utf-8')
        procs.append((fid, subprocess.Popen(cmd, stdin=open(OUT / f'{fid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for fid, p in procs:
        print('DONE', fid, p.wait(), (OUT / f'{fid}.png').exists())


if __name__ == '__main__':
    main()
