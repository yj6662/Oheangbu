#!/usr/bin/env python3
"""#308 D308-126 ground VFX that rise (user 2026-10-21 "장판 계열 vfx가 바닥에만 붙어있는 게 아니라, 위로 일렁거리는 효과, particle
등을 이용하는 게 좋을 거 같은데"). Concepts before any VFX, as for every look (concept -> debate -> judgment -> how to build -> build -> mp4).

  python Tools/Art/SpellVFX308/concept_ground_rise308.py                 # writes the prompts (and copies the refs)
  python Tools/Art/SpellVFX308/concept_ground_rise308.py --run           # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_ground_rise308.py --run d_fire_veil pr_a_haze   # only these ids

Round 1 asks one question: what grammar does the rising part follow? Three grammars (veil on the rim / particles from the ring
band / a fence of strands) on two domain elements (눗 fire - the hard case, it shares 적로 with the enemy's ember patch; 운 water
- the one the player casts against a scorch), plus two ways for the enemy ember patch (D308-125 5 (c), look E) to rise. The 20
domain glyphs (D308-121) have no circle of their own yet (the deploy layer's Blank stands in), so the chosen grammar becomes
their first look. Writes Art/SpellVFX308/Concepts/ground_rise/<id>.prompt.txt; the pictures are concepts for shape, motion,
tone and readability only and are never used as textures (D308-28: commercial terms unconfirmed = TEST)."""
import shutil, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/ground_rise'
STILLS = REPO / 'Art/Presentation297/Stills'
EMBER = REPO / 'Art/SpellVFX308/Concepts/ember_patch'
REFS = {
    'ref_jeokro.png': STILLS / 'realmlook308_r3_jeokro_open.png',
    'ref_hyeongang.png': STILLS / 'realmlook308_after_hyeongang_open.png',
    'ref_firstperson.png': EMBER / 'ref_firstperson.png',
    'ref_fire_language.png': EMBER / 'ref_fire_language.png',
    'ref_patch_e.png': EMBER / 'p_e_merged.png',
}

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

PAINTED = """--- THE LOOK (absolute) ---
Everything the spell makes is PAINTED, NOT LIT: matte ink and pigment on the scene, like paint on paper, in East Asian ink-painting style, with edges that break up like a dry calligraphy brush stroke (split bristle streaks, 갈필) or bleed softly like wet ink on paper. It gives NO light: nothing around it is lit up, there is no bright core, no glow, no rim light, no sparkle. It must read through shape, value contrast and motion, never through light."""

GRID = """--- LAYOUT ---
The image is a 2 x 2 grid of four equal 16:9 panels (reading order: top-left, top-right, bottom-left, bottom-right), separated by thin plain paper-coloured gaps."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no light cast on the ground or the figures, no bright or white core, no emissive anything, no sparks, no light rays, no lens flare, no shiny or reflective surfaces. No falling droplets, no flying rocks. No interface of any kind (no health jars, no compass, no minimap, no icons, no reticle). No text, numbers, labels, arrows, readable calligraphy characters, magic-circle symbols or runes, seal stamp, signature, watermark or frame."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""

# --- the player's domain (결계, D308-121): one circle at the caster's feet ---------------------------------------------
DOMAIN_SCENE = """--- WHAT IS SHOWN ---
The player has just cast a DOMAIN (결계): a circle of ink on the ground, 6 m across (radius 3 m), centred on the player's own feet, which stays for 8 seconds. It does no damage. It is the player's own ground and must read as 'mine, held, safe to stand in' - never as an enemy's dangerous burning patch. Enemies stay outside it.
The problem this picture must solve: in first person the rim lies 3 m away all round, so the flat ring on the ground sits at the very bottom of the view and is easy to miss. That is why the domain has a part that RISES UP from the ground and wavers, so the circle can be read at a glance while walking and fighting."""

DOMAIN_GEOMETRY = """--- TRUE SIZE ---
First-person panels: camera at 1.7 m eye height, looking about 12 degrees down. The rim is exactly 3 m from the viewer in every direction, so in first person the front of the ring crosses the lower third of the view as a wide, gentle arc whose two ends run out of the frame at the left and right edges. The player's right hand holding a long-handled great calligraphy brush shows at the lower right, as in picture 2 (copy only that hand, sleeve cuff and brush framing; nothing else from picture 2). An enemy stands 8 m ahead, outside the circle, in the middle of the screen; the middle 20 % of the screen around that enemy stays completely clear - nothing that rises may cross the enemy's body.
Heights of the rising part: dense only below knee height (0.5 m); above that only thin and see-through, up to at most 1.3 m; a few sparse small particles may drift higher, up to 2.2 m, and fade. Nothing rises from the inner part of the circle closer than 2 m to the centre, so nothing passes in front of the camera."""

DOMAIN_PANELS = """Panel 1 (top-left, first person, 0.3 s after the cast): ink pours out from under the viewer across the ground to the rim and the rim is brushed round in one sweep; the rising part is only beginning.
Panel 2 (top-right, first person, 3 s, full strength): the circle and its rising part all round; the enemy 8 m ahead, outside the circle, fully visible in the clear middle of the screen.
Panel 3 (bottom-left, side view from 9 m outside the circle at 1.7 m height, full strength): the player - a figure in a dark worn hanbok-like long coat holding the long great brush - stands at the centre; the whole circle on the ground and the full height of the rising part are visible, so its height profile can be judged against the figure. No enemy in this panel.
Panel 4 (bottom-right, first person, the last second, 7.8 s): nothing rises any more; the last particles settle and fade; the rim dries to a faint pale stain on the ground."""

ELEMENTS = {
    'fire': {
        'glyph': '눗 (fire, burns enemy missiles crossing its rim)',
        'ref': ('ref_jeokro.png', """Picture 1 is a game screenshot of the region 적로 (Jeokro, "the red road"): rust-red earth slopes with dry pale grass, black burnt leafless trees, a pale grey-green sky, everything slightly desaturated and warm, like ink wash over a 3D scene. Use it for the setting, the ground colour and the tone; the domain is cast on the open red earth."""),
        'extra': ['ref_fire_language.png'],
        'extra_text': """Picture 3 is the fire concept the art director chose for the player's fire spells: use it ONLY for the way fire is painted (red pigment ink with dry-brush soot edges). Do not copy its town, characters or layout.""",
        'ring': """The ring on the ground: a crisp dry-brush ring of vermilion / cinnabar red ink (#B8432F) about 15 cm wide, turning soot-black (#2A2622) on its inner edge, with a faint warm red-brown wash (#6E2A1F, very thin) over the whole disc inside. NO flame tongues, NO black char pool, NO red cracks: that is how the ENEMY's ember patch looks, and this circle must never be mistaken for it. The rust-red earth is a hard background for red ink - solve that contrast (value and edge, not light).""",
        'veil': 'red-brown soot wash (#6E2A1F fading to warm grey #867C6B), like heat shimmer painted with a wet brush',
        'mote': 'flakes of warm grey ash and soot (#3E362C to #867C6B), a few with a dull cinnabar edge (painted, not glowing)',
        'strand': 'red-brown heat-haze soot smoke (#6E2A1F fading to #867C6B)',
    },
    'water': {
        'glyph': '운 (water, washes the enemy\'s burning and other lingering harm off the player)',
        'ref': ('ref_hyeongang.png', """Picture 1 is a game screenshot of the region 현강 (Hyeongang, "the dark river"): dense blue-grey and slate-green bushes and trees, blue-grey hills, a pale grey-green sky, everything slightly desaturated and cold, like ink wash over a 3D scene. Use it for the setting, colour and tone; the domain is cast on an open stretch of blue-grey ground at the edge of the bushes."""),
        'extra': [],
        'extra_text': '',
        'ring': """The ring on the ground: a crisp ring of blue-black ink (#0A1421) about 15 cm wide whose outer edge bleeds softly outward like wet ink on paper, with a cold indigo wash (#2B4F69, thin) over the whole disc inside, the ground looking damp and darker, matte - never shiny, never reflective, no water surface, no puddle highlights.""",
        'veil': 'pale cold indigo ink mist (#2B4F69 fading to grey #8A9499), like vapour painted with a wet brush',
        'mote': 'tiny matte flecks of indigo ink and pale mist (#2B4F69 to #8A9499), drifting like spray rising from a river',
        'strand': 'pale indigo ink mist (#2B4F69 fading to #8A9499)',
    },
}

GRAMMARS = {
    'veil': """--- THE RISING PART: a wavering veil standing on the rim ---
Along the whole rim stands a low, thin, see-through veil of {veil}: densest at its foot just above the ring, thinning upward and fraying into torn wisps by about 1.2 m. It sways and ripples sideways in slow waves, so all round the player the rim reads as a soft standing wall; through it the ground and anything beyond stay visible. A few small {mote} drift up out of its frayed top edge and fade.""",
    'motes': """--- THE RISING PART: particles rising from the ring band ---
No veil. From the outer band of the circle only (between 2 m and 3 m from the centre) a steady, sparse stream of small {mote} rises slowly, swaying from side to side, each a single small brush dab, thinning out and fading between 1 m and 2 m; along the rim itself short curling wisps lift off and break away. There are enough particles that the round shape of the circle can be read from them alone, but never so many that they form a wall.""",
    'strands': """--- THE RISING PART: a fence of rising strands ---
Spaced evenly around the rim, 16 to 20 separate thin strands of {strand} rise from the ring like incense smoke from sticks, each waving gently in its own slow rhythm, thin and calligraphic, fading out between 1 m and 1.5 m. They stand like the posts of a fence, so the circle reads as a ring of standing lines; between the strands the view is open. A few small {mote} drift loose from the strands' tops.""",
}

# --- the enemy's ember patch (D308-125 5 (c), look E, already built flat): what rises above it ---------------------------
PATCH_REF = """--- THE THREE ATTACHED PICTURES ---
Picture 1 is a game screenshot of the region 적로 (Jeokro): rust-red earth slopes with dry pale grass, black burnt trees, a pale grey-green sky. Use it for the setting and tone.
Picture 2 is the fire concept the art director chose for fire: use it ONLY for the way fire is painted. Do not copy its town, characters or layout.
Picture 3 is the APPROVED look of the ember patch on the ground (top-right panel = full strength): a flat soot-black char pool exactly filling a 2.7 m circle with a crisp dry-brush rim, and a ring of 10 to 14 separate low vermilion red-ink flame tongues (at most 35 cm) standing just inside the rim. Keep that ground part EXACTLY as it is; this picture only adds what rises ABOVE it."""

PATCH_SCENE = """--- WHAT IS SHOWN ---
An ENEMY's ember patch: a gaunt deserter foot-soldier standing 9 m ahead, in torn scorched padded armour, made the ground erupt; where it burst, about 4 m ahead of the viewer, it left this patch of burning ground for 6 seconds. It is DANGEROUS to stand in. First-person game view at 1.7 m eye height looking slightly down along a rust-red slope; the patch centre lies 4 m ahead, its near edge 2.65 m and far edge 5.35 m away, drawn at that true size and foreshortening. The rising part must make the patch readable from farther away and from the side, without hiding the enemy behind it: the enemy's body (middle of the screen) stays clearly visible through or above it."""

PATCH_PANELS = """Panel 1 (top-left, 0.3 s after the burst): the pool has spread and the rim tongues spring up; the rising part is only beginning.
Panel 2 (top-right, 3 s, full strength).
Panel 3 (bottom-left, the same moment seen from the side, 10 m away at 1.7 m height, the enemy standing beyond the patch): the full height profile of the rising part can be judged against the enemy's body.
Panel 4 (bottom-right, 5.5 s, about to end): the tongues are half as many and half as tall, mostly dried to soot; the rising part has thinned to a last few traces."""

PATCH_RISE = {
    'pr_a_haze': """--- WHAT RISES: a heat-haze veil and ash ---
Above the ring of tongues stands a wavering heat-haze veil of thin red-brown soot wash (#6E2A1F fading to warm grey #867C6B), see-through, rippling sideways in slow waves, densest just above the tongues and fraying out by about 0.9 m. Out of the black pool, flakes of warm grey ash and soot (#3E362C to #867C6B), a few with a dull cinnabar edge (painted, not glowing), rise in a slow swirl and fade by about 2 m. No smoke column.""",
    'pr_b_strands': """--- WHAT RISES: torn soot strands and cinders ---
No veil. From among the tongues 6 to 8 thin torn strands of soot smoke (#3E362C fading to #867C6B) rise and lean with the wind, each frayed like a dry brush stroke, fading out by about 1.5 m. Grey ash flakes and a few dull red cinders - small painted dabs of #B8432F with soot edges, NOT glowing - rise and swirl among them, up to 2 m. No smoke column, no continuous wall.""",
}

IDS = [f'd_{e}_{g}' for e in ELEMENTS for g in GRAMMARS] + list(PATCH_RISE)


def domain_ref_text(el):
    name, text = el['ref']
    lines = ['--- THE ATTACHED PICTURES ---', text,
             'Picture 2 is the game\'s first-person view: the player holds a long-handled great calligraphy brush in the right hand at the lower right. '
             'Use it ONLY for that framing (right hand, sleeve cuff and brush at the lower right); leave out everything else in it - the interface, '
             'the enemy, the dark shape at the left.']
    if el['extra_text']:
        lines.append(el['extra_text'])
    return '\n'.join(lines)


def prompt(pid):
    if pid in PATCH_RISE:
        parts = [HEAD, PATCH_REF, PAINTED, PATCH_SCENE, GRID, PATCH_RISE[pid], PATCH_PANELS, BAN]
    else:
        _, e, g = pid.split('_')
        el = ELEMENTS[e]
        body = GRAMMARS[g].format(**el)
        parts = [HEAD, domain_ref_text(el), PAINTED, DOMAIN_SCENE + '\nThe glyph: ' + el['glyph'] + '.', DOMAIN_GEOMETRY, GRID,
                 '--- THE CIRCLE ---\n' + el['ring'], body, DOMAIN_PANELS, BAN]
    return '\n\n'.join(parts + [TAIL % (OUT / f'{pid}.png').as_posix()])


def refs(pid):
    if pid in PATCH_RISE:
        names = ['ref_jeokro.png', 'ref_fire_language.png', 'ref_patch_e.png']
    else:
        el = ELEMENTS[pid.split('_')[1]]
        names = [el['ref'][0], 'ref_firstperson.png'] + el['extra']
    return [OUT / n for n in names]


def main():
    ids = [a for a in sys.argv[1:] if not a.startswith('--')] or IDS
    bad = [i for i in ids if i not in IDS]
    if bad:
        sys.exit('unknown ids: ' + ' '.join(bad))
    OUT.mkdir(parents=True, exist_ok=True)
    for name, src in REFS.items():
        if not (OUT / name).exists():
            shutil.copyfile(src, OUT / name)
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
