#!/usr/bin/env python3
"""#308 debate 11 concepts: the five weapon forms (것 · 넛 · 멋 · 섯 · 엇 stop being five swords) and the buff / debuff reading
grammar, before round 2 (user 2026-10-21 "전부 검으로 바꾸지 말고 ... 오행과 연결시켜서 ... 토론해줘"; process: concepts first,
debate on them, then decide the implementation).

  python Tools/Art/SpellVFX308/concept_weapon_forms308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_weapon_forms308.py --run      # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_weapon_forms308.py --run b_spear_form c_bow_form   # only these

Writes Art/SpellVFX308/Concepts/weapon_forms/wf_<id>.prompt.txt; with --run each prompt goes to `codex exec` (built-in image
generation) with game stills attached. Round-1 map (TEST): 목 장창 · 화 각궁 · 토 부월 · 금 쌍수도 · 수 편곤. Combat is the
shoulder camera (SPEC-SPELL-SWING-308 L18), so every shot is third person. The pictures are concepts only; they are never used as
textures (D308-28: commercial terms unconfirmed = TEST)."""
import subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/weapon_forms'
SHOULDER = OUT / 'ref_shoulder.png'   # na_smoke/ref_intended_soot.png - shoulder camera on the palace plaza
BRUSH = OUT / 'ref_brush.png'         # GreatBrush308/unity_sample/ToStance_2_end.png - the caster and the great brush
BANDIT = OUT / 'ref_bandit.png'       # stone_fates/ref_bandit.png - the bandit enemy body

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder), the setting and the tone. Do not copy the user interface.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat with a ragged hem, wrapped forearms, messy dark curly hair. He carries a great calligraphy brush: a black lacquered staff about 1.25 m long with a pale bone-coloured ferrule, a long black bristle head and a small red tassel at the butt. Keep that character and that brush."""

REF_BANDIT = """Picture 3 is the bandit enemy body (an untextured test render): a commoner in a worn dark jacket and cloth trousers with a head band, a shovel-like weapon, a pack of grey stones tied on his back. Paint him finished in the game's style."""

STYLE = """--- THE LOOK ---
A game concept paint-over in the style of picture 1: a Joseon-era Korean palace town, stone-paved plaza, dark tiled roofs, hazy hills, pale warm paper-coloured sky; everything warm grey-beige and slightly desaturated, like ink wash over a 3D scene. All the magic is INK: matte black ink and soaked-in mineral pigment, like paint on paper - never light. The weapons made from the brush are matte hardened ink, like dried lacquered ink with a dry-brush split texture on the surface, NOT shiny metal. Element pigments are only a thin stain soaked into edges: wood moss-green #426E58, fire vermilion #BE543B, earth ochre #977343, metal pale blue-grey #A6B0AD, water deep blue #2C465A."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no emissive anything, no light rays, no sparks, no lens flare, no bright metal reflections, no fire flames. No text, numbers, labels, diagram arrows, readable calligraphy characters, signature, watermark or frame. No HUD, no icons."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

PICTURES = {
    'a_lineup': ([SHOULDER, BRUSH], """--- THE PICTURE: five weapon forms side by side (does each read apart?) ---
The same caster shown five times standing in a row, seen from the front three-quarter at about 12 m, all in the same two-handed ready stance. Each weapon was made from the great brush: the black staff is still the haft (same length, the red tassel still at the butt) and the bristle head has hardened into the weapon head. Left to right:
1 WOOD long spear (jangchang): a long narrow leaf-shaped spear point; moss-green stain.
2 FIRE Korean horn bow (gakgung): the staff itself bent into a short, deeply recurved bow, the string one taut strand of black bristle, an ink arrow nocked and half drawn, its head stained vermilion like a dipped brush.
3 EARTH broad axe (buwol): a heavy broad crescent axe blade on the staff, raised high; ochre stain.
4 METAL two-handed sword (ssangsudo): a long straight single-edged blade growing from the ferrule, the staff become a long two-hand grip; pale blue-grey stain.
5 WATER chain flail (pyeongon): a short ink rod hanging from the staff tip on a few chain links of twisted ink strands, swinging; deep blue stain.
LAYOUT: TWO rows of the same five figures in the same poses and order, separated by a thin plain gap. TOP row: flat solid dark silhouettes only, no interior detail, on plain paper colour - a test that the five outlines read apart (long thin line / bow arc / heavy raised wedge / long straight blade / hanging chain and rod). BOTTOM row: the full painted version standing on the plaza."""),
    'b_spear_form': ([SHOULDER, BRUSH], """--- THE PICTURE: the brush becomes a spear (wood) ---
Shoulder camera as in picture 1: the caster about 3 m in front of the camera, facing away; a target figure 8 m ahead.
LAYOUT: ONE wide strip of FOUR equal panels left to right, identical camera, thin paper-coloured gaps.
Panel 1 (0.0 s): the caster holds the great brush across the body in both hands, bristle head forward.
Panel 2 (0.15 s): wet black ink pours out of the bristle and stretches forward past the tip like a long drip in the air, the bristle head thinning; a faint moss-green stain soaks into the running ink.
Panel 3 (0.3 s): the ink hardens from the ferrule outward into a long narrow leaf-shaped spear point; the last ink at the very tip still wet and dripping; the bristle is gone into it.
Panel 4 (0.5 s): the caster has dropped into a low spear stance, both hands on the black staff, the matte ink spear point aimed at the target, a few ink drops falling from it. The haft is the same black staff with the red tassel at the butt."""),
    'c_bow_form': ([SHOULDER, BRUSH], """--- THE PICTURE: the brush becomes a horn bow (fire) ---
Shoulder camera as in picture 1. LAYOUT: ONE wide strip of FOUR equal panels, identical camera, thin paper-coloured gaps.
Panel 1: the caster holds the brush upright in the left hand.
Panel 2: the black staff bends like a bamboo pole into the deeply recurved shape of a short Korean horn bow; the black lacquer cracks into ink scales at the bends; one long strand of black bristle stretches from tip to tip and becomes the string.
Panel 3: the caster draws: the right hand pulls the bristle string back to the cheek; a drop of ink on the string stretches into an ink arrow whose head is stained vermilion red like a brush dipped in pigment (red PIGMENT, not fire, not glowing).
Panel 4: release: the ink arrow is a thin black streak flying straight at a target 15 m away, leaving a short torn soot trail; the string still shivering; the caster's drawing hand open."""),
    'd_strikes': ([SHOULDER, BRUSH], """--- THE PICTURE: three weapons, three rhythms, at the moment of the hit ---
Shoulder camera as in picture 1, THREE equal panels side by side, thin paper-coloured gaps, same plaza. The enemies are bandits in worn Joseon commoner clothes.
Panel 1 WOOD SPEAR, a straight thrust: the matte ink spear point has run THROUGH the first bandit and reaches a second bandit right behind him on the same line; a narrow straight streak of ink splash bursts out behind them along that line; moss-green stain at the splash edges.
Panel 2 EARTH AXE, an overhead chop: the caster's whole body committed low, the broad ink axe driven down onto a bandit; his wooden shield and the front of his padded armour crack open, ink spilling down from the cracks; a heavy ink splash on the ground; ochre stain at the edges.
Panel 3 WATER FLAIL, a wide sweep of about 130 degrees: the ink chain and rod whip through three bandits standing in a row, a long curved smear of ink following the arc and splashing off each of them; deep blue stain at the edges."""),
    'e_exhaust': ([SHOULDER, BRUSH], """--- THE PICTURE: the weapon runs dry and turns back into the brush (metal sword) ---
Shoulder camera as in picture 1. LAYOUT: ONE wide strip of THREE equal panels, identical camera, thin paper-coloured gaps.
Panel 1: the caster holds a long straight two-handed ink sword (ssangsudo), matte black, pale blue-grey stain on the edge, the surface fresh and wet.
Panel 2 (the last seconds of the ink): the blade is drying - its surface turns grey and dull, hairline cracks run through it from the tip, flakes of dry ink peel off and drift down like burnt paper, the outline getting ragged.
Panel 3 (release): the blade has unravelled from the tip into loose black ink strands that flow back along the staff and gather into the bristle head again; the caster holds the great brush; a few dry ink flakes still falling."""),
    'f_buff_brush': ([SHOULDER, BRUSH], """--- THE PICTURE: my own buffs live on the brush; what enemies put on me lives on my clothes ---
Shoulder camera, a little closer than picture 1: the caster in a ready stance holding the great brush across the body, seen from behind and to the right. LAYOUT: TWO equal panels side by side, identical camera, thin paper-coloured gap.
Panel 1 (three buffs at once): three separate ink bands wrapped around the black staff at three places, one band per buff: near the bristle a moss-green stained band wrapping like a vine; in the middle of the staff a vermilion stained band of tight slanted wraps; just above the front hand an ochre band of two or three hardened rings. The bands are still, not flowing - tidy, matte ink with pigment soaked in. A thin ink ring lies on the ground around the caster's feet. The clothes are clean.
Panel 2 (an enemy has cursed the caster): the same stance and the same three bands on the staff, AND now dark grey ink-smoke seeps out of the right sleeve cuff, the wrist and the coat hem, drifting down and outward, dirty and uneven, with no colour. The difference between the tidy pigment bands on the brush and the dirty grey seep on the clothes must be obvious at a glance."""),
    'g_debuff_body': ([SHOULDER, BRUSH, BANDIT], """--- THE PICTURE: how weakened enemies look ---
FOUR equal panels side by side, thin paper-coloured gaps. In each, the same bandit enemy (picture 3) stands on the plaza about 5 m away, front three-quarter view, the caster's shoulder and brush just visible at the panel edge. In every panel black ink runs DOWN his body from ONE place, thinning to grey as it runs, with only a thin pigment edge:
Panel 1 SLOWED: ink drips down his shins and drags from his ankles onto the ground in heavy wet strands; his stride sluggish; moss-green edge.
Panel 2 WEAKENED ATTACK: his weapon hand and the weapon head are smeared with drooping ink hanging off the tip, making the weapon look blunt and heavy; ochre edge.
Panel 3 ARMOUR BROKEN: the front of his jacket has cracked into a network of ink cracks with pale paper colour showing through them, ink running down from the cracks; pale blue-grey edge.
Panel 4 EXPOSED: one round dark-ink seal mark (no readable characters) pressed into the middle of his chest, small ink runs dripping from its lower edge; deep blue edge.
No red anywhere on the enemy."""),
    # Round 2 (Codex + Sonnet both: redraw a and g). a: the spear point read as bristle, the sword was longer than the spear and
    # metal-shiny with no tassel, the axe read as a sickle. g: the chest seal and the torso cracks merged at range, the seal sat
    # on a light shirt that faked its contrast.
    'a2_lineup': ([SHOULDER, BRUSH], """--- THE PICTURE: five weapon forms, round 2 (shoulder camera and the 12 m outline test) ---
The game camera is behind and slightly above the caster's right shoulder (about 2.8 m behind, 0.55 m up, field of view 60 degrees): the caster's body fills about half of the frame height.
Every weapon is made from the great brush: the same black staff is the haft, the SAME small dark crimson tassel (deep wine red, darker and duller than the fire pigment) hangs at the butt of ALL FIVE, and the bristle has hardened into the head. All heads are matte black hardened ink with a dry-brush grain: no polished surface, no bright edge, no blue-grey sheen, no reflections. The caster holds four of them with the same two-hand grip; only the bow is held differently.
1 WOOD long spear (jangchang), the LONGEST weapon: a narrow pointed leaf blade with hard straight edges and a sharp tip and a small ink collar where it meets the ferrule - clearly a spear point, NOT a soft brush tip; moss-green stain on its edges only.
2 FIRE horn bow (gakgung): the staff bent into a short, deeply recurved bow (the only curved outline), a black bristle string, an ink arrow nocked; vermilion stain only on the arrow head edge.
3 EARTH broad axe (buwol): ONE broad single-edged blade fixed close to the haft near the top, its curved cutting edge facing forward, about as wide as the caster's shoulders at most - a battle axe, NOT a sickle, NOT a scythe, NOT a big crescent moon; ochre stain on the cutting edge only.
4 METAL two-handed sword (ssangsudo): a straight single-edged blade, the whole weapon clearly SHORTER than the spear (about three quarters of its length), blade about five times as wide as the haft; matte black ink, pale blue-grey stain on the edge only, NO metal look.
5 WATER chain flail (pyeongon): a short ink rod (about half a metre) hanging from the staff tip on a few links of twisted ink strands; deep blue stain on the rod edges only.
LAYOUT: TWO rows. TOP row: FIVE equal panels, each the same shoulder-camera view of the caster from behind holding one weapon in ready stance, left to right wood, fire, earth, metal, water. BOTTOM row: the same five, side by side, seen from the front at about 12 m, as flat solid dark silhouettes only (no interior detail) on plain paper colour - a test that the five outlines read apart: long thin line with a small leaf / the only curve / a short haft with one broad blade at the top / a shorter thick straight blade / a hanging rod on a chain."""),
    'g2_debuff_body': ([SHOULDER, BRUSH, BANDIT], """--- THE PICTURE: how weakened enemies look, round 2 (two distances) ---
The same bandit enemy (picture 3) in a DARK jacket (no light shirt showing), front three-quarter view on the plaza. LAYOUT: TWO rows of FOUR equal panels, thin paper-coloured gaps. TOP row: the bandit about 3 m from the camera (he fills most of the panel height). BOTTOM row: the SAME four states with the bandit about 15 m away (small in the panel, about a tenth of the panel height), same camera height - a test that the four still read apart when small.
Panel 1 SLOWED: thick black ink coats both whole shins from knee to foot and drags from the ankles onto the ground in heavy wet strands; moss-green stain only on the outer edge of the ink.
Panel 2 WEAKENED ATTACK: the weapon head is swollen with a big drooping lump of black ink about twice its size, hanging off the tip, ink running down his weapon hand and wrist; ochre stain only on the outer edge.
Panel 3 ARMOUR BROKEN: the front of his dark jacket is split by a wide branching network of PALE paper-coloured cracks (light lines on dark cloth) covering about half of his chest and belly, small ink runs under the cracks; NO dark blob or disc in the middle; pale blue-grey stain only on the crack edges.
Panel 4 EXPOSED: ONE round PALE paper-coloured disc about as wide as his chest's half (about 40 cm) pressed on the middle of his chest, with ONE small dark angular ink seal mark in its centre (no readable characters), thin ink runs dripping from the disc's lower edge; deep blue stain only on the disc rim. The disc is the only light round shape on him.
Rule: armour broken = light lines on dark; exposed = one light round disc with a dark mark. No red anywhere on the enemy."""),
    # Round 2 for d (all three named the axe and flail panels): the axe head was person-sized with an ochre puddle, the flail
    # chain was iron rings, the effect covered the target.
    'd2_strikes': ([SHOULDER, BRUSH], """--- THE PICTURE: three weapons at the moment of the hit, round 2 (small, local ink) ---
Shoulder camera as in picture 1 (the caster about 3 m in front of the camera, facing away), THREE equal panels side by side, thin paper-coloured gaps, same plaza. The enemies are bandits in worn dark Joseon commoner clothes, about 4 m beyond the caster. Every weapon keeps the black staff as its haft and a small dark crimson tassel at the butt; every weapon head is matte black hardened ink with a dry-brush grain - no metal, no shine. In every panel the hit effect stays SMALL and LOCAL: it covers less than a quarter of the panel and never hides the enemy's head or body outline. Pigment only as a thin stain on the outer edge of the ink - no coloured puddles, no coloured clouds.
Panel 1 WOOD SPEAR, a straight thrust: the caster lunges one step; the narrow leaf-shaped ink spear point has run through the first bandit and touches a second bandit right behind him on the same line; a thin straight streak of black ink spatter behind them along that line; moss-green only on the spatter edges.
Panel 2 EARTH AXE, an overhead chop: the caster's whole body committed low; the axe head is ONE broad single-edged ink blade no wider than the caster's torso (much smaller than a person), fixed near the top of the haft; it lands on a bandit's wooden shield and padded armour, which split open with pale cracks and short ink runs; a few heavy black ink drops on the ground (no puddle); ochre only on the blade's cutting edge and the crack edges.
Panel 3 WATER FLAIL, a wide sweep: a short ink rod (about half a metre) on a chain of TWISTED BLACK INK STRANDS (no iron links, no metal rings) whips across three bandits standing side by side; ONE thin curved ink smear follows the arc and small ink flecks fly off each bandit; deep blue only on the smear edges."""),
}


def prompt(pid):
    refs, body = PICTURES[pid]
    ref = REF + ('\n' + REF_BANDIT if BANDIT in refs else '')
    return '\n\n'.join([HEAD, ref, STYLE, body, BAN, TAIL % (OUT / f'wf_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'wf_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'wf_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'wf_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'wf_{pid}.png').exists())


if __name__ == '__main__':
    main()
