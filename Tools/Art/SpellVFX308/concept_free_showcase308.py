#!/usr/bin/env python3
"""D308-150 V10-V11 free showcase (2026-10-25, the user: "정말 아무 규칙없이 시안 뽑고 그 시안을 토대로 자유롭게 구현한 결과물도 한번만 보여주면 안돼?"):
FREE concepts (no look rules) of three vol.2 spells drawn on the real game view - 나 (fire projectile), 고 (wood fan sweep), 각 (wood path + branch cage).

  python Tools/Art/SpellVFX308/concept_free_showcase308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_free_showcase308.py --run      # runs codex exec for each (parallel), recovers from ~/.codex/generated_images
  python Tools/Art/SpellVFX308/concept_free_showcase308.py --run na_a go_b   # only these

Out: Art/SpellVFX308/Vol2Vfx/free_showcase/concepts/<id>.png (+ .prompt.txt, .log.txt). Concepts only (D308-28: commercial terms unconfirmed = TEST)."""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/concepts'
GEN = Path.home() / '.codex' / 'generated_images'
GAME = REPO / 'Art/SpellVFX308/Vol2Vfx/legacy_bridge/step5a/probe_pre/ground_red.jpg'      # the real game view: lab, main look, 적로 ground, real bear enemies
BRUSH = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_brush.png'                       # the caster and the great brush
BURST = REPO / 'Docs/Refs/SpellFX308/sheet_burst.jpg'                                       # the user's reference: a giant ink burst at the hit

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL screenshot of the game: third-person camera behind and above the caster's right shoulder, the caster at the bottom centre, a flat dry red-brown earth ground (the region 적로), a pale paper-beige misty sky, and two real enemies (dark matted bears with crusted crystal growths on their backs) about 5 and 7 metres ahead. Paint the concept ON THIS VIEW: same camera height and angle (you may lower the camera a little toward eye level), same ground, same sky, same bear enemies at the same distances. The scene keeps its muted Korean ink-wash painted look.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is a reference the director likes: at the hit, HUGE bold black ink strokes explode outward from the impact point, many times bigger than the characters. Use its boldness and scale, not its cartoon style."""

STYLE = """--- THE LOOK (no restrictions) ---
This is a free concept: there are NO style rules for the spell. The world stays a muted Korean ink-wash painting, but the SPELL itself must be spectacular, like a AAA action game's signature spell effect: fully three-dimensional and volumetric (depth, overlapping layers, foreground and background parts), real light from the spell falling on the ground, the caster and the enemies, particles, motion, energy. Glow, emission, bloom, light and heat are all allowed. The magic is made of the element AND of calligraphy ink together (ink is what the brush writes with). It must be BIG and bold - several metres, never a tiny shape - and a player who has never seen the game must understand AT A GLANCE what the spell is doing. It may cover the enemies.
NOT in the picture: no text, letters, numbers, labels, readable calligraphy characters, arrows, UI, HUD, frame, signature or watermark. Not a flat 2D sprite, not a flat decal: everything must have volume and depth."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. Print the saved file path and stop."""

SPELLS = {
    'na': "THE SPELL 나 (fire, one shot): the caster swings the great brush and a fire projectile flies from the brush tip along a narrow straight path to the FIRST bear and hits it once, burning it (it keeps burning for 2 seconds). Nothing is left behind.",
    'go': "THE SPELL 고 (wood, one sweep): with one swing of the great brush, a wave of wood sweeps INSTANTLY across a fan-shaped area in front of the caster (5 m deep, 4 m wide at its far edge) and entangles every enemy inside it, slowing them. Nothing is left behind afterwards.",
    'gak': "THE SPELL 각 (wood, path + peak 'leaves something'): the caster swings the great brush and a narrow wood strike runs along a straight path to the first bear; where it hits, a LOW CAGE of branches grows up around the bear's legs and pins it in place (about 1.5 seconds), and when it releases, a patch of living growth stays on the ground where the cage was.",
}

VARIANTS = {
    'na_a': ('na', "VARIANT A - comet: show the projectile in mid-flight, a little before it reaches the first bear. A dense blazing core of burning ink about the size of a head, wrapped in roaring orange-vermilion flame tongues, drags a long thick twisting tail of fire, black ink smoke and embers all the way back to the brush tip, so the path from the brush to the bear reads as one unbroken streak. The flame lights the red ground beneath it in a moving orange pool and rims the bear and the caster with warm light."),
    'na_b': ('na', "VARIANT B - fire-ink spear: show the projectile in mid-flight. It is a long spear of fire shaped like a single fat calligraphy brush stroke - a thick black ink stroke whose edges burn with bright orange flame and peel into sparks - spinning slightly as it flies, with a corkscrew of flame and ink droplets around it and a heat-shimmer wake. The caster has just finished the swing; the brush tip still trails fire."),
    'na_c': ('na', "VARIANT C - the hit: show the moment of impact on the first bear. The fireball has just struck: an explosion of flame and HUGE black ink strokes bursts outward from the bear's chest (like picture 3, but with fire inside the ink), burning ink splatters and embers fly in all directions, the bear is engulfed and reeling, its fur catching fire; a fading streak of fire and smoke still connects the impact back to the caster's brush tip so the path is readable."),
    'go_a': ('go', "VARIANT A - thorn wave: show the sweep at its peak. From the caster's feet a fan-shaped wave of thick dark-green thorned vines and branches erupts out of the red earth and lashes forward and outward across the whole fan (5 m deep, 4 m wide at the far edge), like a breaking wave of wood, ink and torn leaves; the bear inside the fan is caught, vines whipping around its body and legs. Splinters, leaves and ink spray fly forward. A faint green light runs along the leading edge of the wave."),
    'go_b': ('go', "VARIANT B - ink-forest sweep: show the sweep at its peak. The brush's swing tears through the air as a giant horizontal arc of black ink, and along the whole arc a forest of sharp young branches and bamboo shoots bursts up out of the ground in a fan in front of the caster, all leaning forward like a sweeping blade, knee to chest high; every enemy inside is snared by the shoots. Dust and leaves are thrown up; the fan shape is clearly readable on the ground."),
    'go_c': ('go', "VARIANT C - root whip: show the sweep at its peak. Dozens of glowing jade-green root tendrils mixed with black ink ribbons whip out from the brush in a wide fan, sweeping sideways across the area in front like a scythe of wood; where they pass the ground cracks and grass springs up for an instant; the bear inside is lashed and wrapped. Strong motion arcs show the sweep direction."),
    'gak_a': ('gak', "VARIANT A - the cage: show the moment just after the hit. A narrow line of wood strike (a dark ink-green streak with splinters) still runs from the caster's brush to the first bear; around the bear's legs a LOW CAGE of thick gnarled branches has burst up out of the ground and locked shut, the branches curving inward over its paws and up to its belly like ribs, leaves and splinters still flying, a faint green glow inside the cage; the bear strains but is pinned in place."),
    'gak_b': ('gak', "VARIANT B - the cage growing: show the cage in the middle of growing. The wood strike has hit the first bear: from the impact point on the ground, dark twisted branches spiral up around its legs like a closing hand of roots, each branch sprouting fresh green leaves at its tip, soil and red dust bursting up between them, ink dripping off the bark; the bear is caught mid-step. Behind, the path the strike ran along is marked by a line of torn earth and small sprouts back to the caster."),
    'gak_c': ('gak', "VARIANT C - pinned and left behind: show the cage holding the first bear: an ornate low cage of interlaced branches and vines (like a living wicker trap with thorns) wraps the bear's legs and lower body, the bear is pinned and roaring; a ring of young saplings, moss and small leaves is already growing on the ground around the cage - the growth that will stay after the cage opens. The wood strike's fading trail still connects back to the brush tip."),
}


# ---------------------------------------------------------------------------------------------------------------------------------------------------------
# batch 1 (2026-10-26, D308-150 V12, the user: "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자."): P0 가 마 사 아 + F0 노 모 소 오.
#   python Tools/Art/SpellVFX308/concept_free_showcase308.py --batch b1 [--run] [ids]
# Picture 1 = the REAL pitch-0 gameplay camera frame of the template's own capture (r6 frame 0: bears where the engine puts them), so concept and
# engine share the camera. Each glyph's verb + end clause (vol2_table120_v11.csv) is baked into its variants so the five bodies never read as recolours.
B1_OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch1/concepts'
B1_GAME = {'P0': REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/r6/frames/P0_bridge_Fire_red/f_00000.jpg',
           'F0': REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/r6/frames/F0_bridge_Wood_red/f_00000.jpg'}
B1_REF = {
    'P0': """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, the great brush raised), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and two real enemies (dark matted bears with crusted crystal growths on their backs) standing in a line straight ahead, the first about 4.6 metres away (just right of the caster's head), the second about 8 metres away behind it. Paint the concept ON THIS VIEW: keep this exact camera height, angle and framing, the same ground, sky and bears at the same places. Ignore the small orange flame on the caster's back in picture 1 (a capture artefact, not part of the scene). The scene keeps its muted Korean ink-wash painted look.""",
    'F0': """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, the great brush raised over the right shoulder), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and two real enemies (dark matted bears with crusted crystal growths on their backs) close ahead: one about 3.4 metres ahead, partly hidden behind the caster's left side, the other about 4.3 metres ahead and a little to the right. Paint the concept ON THIS VIEW: keep this exact camera height, angle and framing, the same ground, sky and bears at the same places. Ignore the small green dot on the right bear in picture 1 (a capture artefact). The scene keeps its muted Korean ink-wash painted look.""",
}
B1_REF_TAIL = """Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is a reference the director likes: HUGE bold black ink strokes exploding outward, many times bigger than the characters. Use its boldness and scale where it fits, not its cartoon style."""
B1_SPELLS = {
    'ga': ('P0', "THE SPELL 가 (WOOD, one shot along a narrow path): the caster swings the great brush and shoots a living VINE along a narrow straight path to the FIRST bear; it hits once and BINDS that bear (holds it, slowing it to half speed for 2 seconds). When the bind ends it unravels into fine rootlets reaching forward. Nothing stays behind. It must NOT look like fire, stone, metal or water, and NOT like a cage of branches or a forest of bamboo shoots (other spells own those)."),
    'ma': ('P0', "THE SPELL 마 (EARTH, one shot along a narrow path): the caster swings the great brush and shoots a mass of EARTH and stone along a narrow straight path at the FIRST bear; it hits once, very hard, and SHOVES the bear backwards (heavy damage + knockback). Where it hits, the ground around lifts up as broken plates of earth. Nothing stays behind. Heavy, weighty, slow-feeling power; NOT fire, NOT wood, NOT a glowing magic beam."),
    'sa': ('P0', "THE SPELL 사 (METAL, one shot along a narrow path): the caster swings the great brush and shoots a STEEL BLADE along a narrow straight path at the FIRST bear; it hits once and CUTS it (a fast, sharp slash). At the end it snaps with a bright ringing flash and metal shards fly out SIDEWAYS. Nothing stays behind. Cold, fast, razor-sharp: polished steel, white-silver and pale gold light, thin sharp lines; NOT fire, NOT a rock, NOT a vine."),
    'a': ('P0', "THE SPELL 아 (WATER, one shot along a narrow path): the caster swings the great brush and shoots WATER along a narrow straight path at the FIRST bear; it hits once and SOAKS the bear (it stays drenched for 4 seconds, water running down its fur). Nothing stays behind. Clear, heavy, liquid water with ink swirling inside it; deep blue-green and white foam; NOT fire, NOT ice crystals, NOT a glowing laser."),
    'no': ('F0', "THE SPELL 노 (FIRE, one sweep over an area): with one swing of the great brush, FIRE sweeps INSTANTLY across a fan-shaped area in front of the caster (5 m deep, about 4 m wide at its far edge) and sets every enemy inside it burning (they keep burning for 2 seconds). It burns out from the edges into ash rising upward. Nothing stays behind. NOT a single projectile and NOT bamboo or vines."),
    'mo': ('F0', "THE SPELL 모 (EARTH, one sweep over an area): with one swing of the great brush, the EARTH of a fan-shaped area in front of the caster (5 m deep, about 4 m wide at its far edge) heaves up INSTANTLY and SHOVES every enemy inside it backwards (heavy damage + knockback). Afterwards the earth sinks and the ground around lifts as plates. Nothing stays behind. Heavy, weighty rock and soil; NOT fire, NOT wood, NOT glowing."),
    'so': ('F0', "THE SPELL 소 (METAL, one sweep over an area): with one swing of the great brush, STEEL BLADES sweep INSTANTLY across a fan-shaped area in front of the caster (5 m deep, about 4 m wide at its far edge) and CUT every enemy inside once (fast, sharp). At the end the blades snap with a bright ringing flash and shards fly out sideways. Nothing stays behind. Polished steel, white-silver and pale gold light, razor-thin lines; NOT fire, NOT rock, NOT wood."),
    'o': ('F0', "THE SPELL 오 (WATER, one sweep over an area): with one swing of the great brush, WATER sweeps INSTANTLY across a fan-shaped area in front of the caster (5 m deep, about 4 m wide at its far edge) and SOAKS every enemy inside (drenched for 4 seconds, water running down). Nothing stays behind. Clear heavy water with ink swirling in it, deep blue-green and white foam; NOT fire, NOT ice, NOT glowing beams."),
}
B1_VARIANTS = {
    'ga_a': ('ga', "VARIANT A - vine harpoon: show the moment the vine has just struck. A thick living vine, dark green-black bark with sharp thorns and a few fresh leaves, has shot out of the brush tip like a harpoon and its barbed tip has speared into the first bear; the vine is still taut in the air from the brush to the bear like a tether, and its front part has whipped into tight coils around the bear's body and legs, binding it; leaves, splinters and ink droplets burst from the impact, a faint jade light pulses along the vine."),
    'ga_b': ('ga', "VARIANT B - seed bolt that blooms into a snare: show the hit. A glowing jade seed wrapped in black ink has flown along the path (its wake still visible as a streak of leaves and ink from the brush) and burst on the first bear into a NET of whipping vines and tendrils that wrap and knot around the bear, pinning its limbs; leaves and green sparks spray outward; at the net's loose ends fine rootlets already reach forward along the ground."),
    'ma_a': ('ma', "VARIANT A - boulder ram: show the impact. A huge rough boulder (about 1.2 m), dark stone cracked with black ink seams glowing faintly orange from friction, has been hurled from the brush along a low straight line, ploughing a shallow furrow and kicking up dust behind it, and has just SLAMMED into the first bear: the bear is being thrown backwards, a shock of dust and soil chunks explodes outward, and the ground around the hit lifts up in broken tilted slabs of earth."),
    'ma_b': ('ma', "VARIANT B - earth lance from the ground: show the hit. Along the narrow path from the caster's feet to the first bear, the ground ruptures in a running line of jagged stone spikes and flying clods, ending in one massive angled stone pillar that has burst out of the ground and rammed the bear, flinging it backwards; slabs of earth around the pillar tilt up, dust and black ink spray off the rock."),
    'sa_a': ('sa', "VARIANT A - flying crescent blade: show the cut. A long thin crescent blade of polished steel with a black ink edge, spinning flat, has flown from the brush tip leaving a razor-thin bright silver line hanging in the air along the whole path; it has just sliced clean through the first bear: a brilliant white slash line crosses the bear, sparks fan out, and the blade is snapping into bright metal shards that fly out sideways with a ringing flash."),
    'sa_b': ('sa', "VARIANT B - thrown sword: show the moment of the hit. A narrow straight double-edged steel sword (like a Korean jian, about 1.5 m) made of steel and ink has been thrown from the brush like a javelin, leaving a long thin white-gold streak from the brush to the bear; it has pierced the first bear, a sharp X-shaped slash flare of white light opens on the bear, and the sword shatters into glittering shards that spray sideways, with fine ink lines like cut threads."),
    'a_a': ('a', "VARIANT A - water lance: show the hit. A spiralling pressurised jet of clear blue-green water threaded with black ink swirls has shot from the brush tip in a straight lance to the first bear, the whole column still visible; on the bear it explodes into a huge splash crown and sheets of spray, the bear is drenched, water pouring down its fur and pooling on the red ground; droplets catch the light."),
    'a_b': ('a', "VARIANT B - water orb: show the moment of the burst. A wobbling sphere of heavy water (about 1 m) with black ink curling inside it has flown along the path (a trail of droplets and mist behind it) and has just burst over the first bear into a heavy dome of water that collapses and pours down over it; the bear is soaked and streaming; a wide wet dark splash stain spreads on the ground with white foam."),
    'no_a': ('no', "VARIANT A - fire breath fan: show the sweep at its peak. From the brush a wide fan of roaring orange-vermilion flame and black ink smoke blasts forward like a dragon's breath, filling the whole fan-shaped area in front of the caster, engulfing both bears; the flames curl at the fan's outer edge into rising ash and embers; the ground under the fan glows with heat."),
    'no_b': ('no', "VARIANT B - burning ink scythe: show the sweep at its peak. A giant horizontal crescent of black ink whose whole edge is on fire sweeps across the fan in front of the caster from left to right; behind its path a carpet of low flames fills the fan-shaped ground; both bears inside catch fire, flames climbing their fur; ash and embers rise from the fan's edges."),
    'mo_a': ('mo', "VARIANT A - earth wave: show the sweep at its peak. The ground of the whole fan in front of the caster heaves up into a breaking WAVE of soil, rocks and earth slabs rolling outward from the caster's feet, like a tsunami of earth; both bears are being lifted and shoved backwards by the wave; dust and black ink spray off its crest; heavy, weighty."),
    'mo_b': ('mo', "VARIANT B - slab eruption: show the sweep at its peak. Across the fan in front of the caster, dozens of big flat slabs of rock and packed earth burst up out of the ground in a sweeping sequence, tilted outward like opening fan ribs, ramming both bears backwards; soil clods and dust explode between them; deep cracks radiate on the ground."),
    'so_a': ('so', "VARIANT A - fan of blades: show the sweep at its peak. A spread of dozens of long thin steel blades (like a thrown fan of knives) sprays out from the brush across the whole fan in front of the caster, each blade leaving a razor-thin bright silver streak, slicing through both bears with sparks and bright cut lines; the blades' tips snap into shards at the fan's edge."),
    'so_b': ('so', "VARIANT B - giant steel crescent slash: show the instant of the cut. One huge horizontal crescent slash of polished steel and white light sweeps across the fan in front of the caster, leaving a thin brilliant cut line hanging in the air and both bears with bright slash marks; a ring of steel shards and sparks bursts sideways along the arc; fine black ink threads trail behind the edge."),
    'o_a': ('o', "VARIANT A - tidal fan: show the sweep at its peak. A curling WAVE of clear blue-green water with ink swirling inside rises from the brush and sweeps out across the fan in front of the caster, its white foamy crest crashing over both bears, drenching them; spray and mist hang in the air; the red ground under the fan is dark and glistening wet."),
    'o_b': ('o', "VARIANT B - water fan spray: show the sweep at its peak. A wide flat fan of high-pressure water jets bursts from the brush like a fan-shaped sheet of water, thin streams and heavy droplets, sweeping across both bears; water splashes off them in crowns, they are drenched and streaming, puddles form on the ground."),
}


# ---------------------------------------------------------------------------------------------------------------------------------------------------------
# batch 2 (2026-10-26, D308-150 V12): A0 거 너 머 서 어 (ㅓ attach: ink on the brush shaft / sleeve, parry window 0.5 s, reacts when an enemy blow is parried)
# + R0 구 누 무 수 우 (ㅜ ring: one instant hit on a hollow band r ~2 m round the caster).
#   python Tools/Art/SpellVFX308/concept_free_showcase308.py --batch b2 [--run] [ids]
# Picture 1 = the real pitch-0 gameplay frame of the template's own capture (batch2/r0 f_00000: A0 bear 1.6 m ahead, R0 bear 1.85 m ahead on the band).
B2_OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/concepts'
B2_GAME = {'A0': REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/r0/frames/A0_bridge_Wood_red/f_00000.jpg',
           'R0': REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/r0/frames/R0_bridge_Wood_red/f_00000.jpg'}
B2_REF = {
    'A0': """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, holding the great brush), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and one real enemy (a dark matted bear with crusted crystal growths on its back) VERY close, about 1.6 metres ahead, right in front of the caster, rearing to strike. Paint the concept ON THIS VIEW: keep this camera height, angle and framing, the same ground, sky and bear. Ignore any small coloured blob on the caster or the bear in picture 1 (capture artefacts). The scene keeps its muted Korean ink-wash painted look.""",
    'R0': """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, holding the great brush), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and one real enemy (a dark matted bear with crusted crystal growths on its back) close, about 2 metres ahead and a little to the right - exactly on the ring that the spell strikes. Paint the concept ON THIS VIEW: keep this camera height, angle and framing (you may raise the camera a little so the ring around the caster reads), the same ground, sky and bear. Ignore any small coloured blob in picture 1 (capture artefacts). The scene keeps its muted Korean ink-wash painted look.""",
}
B2_A0 = "It is a PARRY / GUARD spell: the element wraps the great brush's shaft and the caster's sleeve for half a second as a guard; the moment the bear's heavy paw strike lands on that guard it is parried (the caster takes no damage) and the guard answers with its own reaction burst at the point of contact, throwing the paw back. Show THE MOMENT OF THE PARRY: the bear's paw meeting the guard, the guard on the brush clearly visible, the reaction exploding at the contact point."
B2_R0 = "It is a RING spell: ONE instant strike on a hollow circular band about 2 metres around the caster (a ring, not a filled disc - the caster stands in the empty middle, untouched); the bear standing on the band is struck. It must read at a glance as a CIRCLE around the caster (the near half behind the camera may be cut off, the front half and the sides must show). Show THE INSTANT OF THE STRIKE."
B2_SPELLS = {
    'geo': ('A0', "THE SPELL 거 (WOOD guard): " + B2_A0 + " WOOD: living vines, thorns, bark and fresh leaves; on the parry the vines catch and bind the striking paw. NOT fire, stone, steel or water."),
    'neo': ('A0', "THE SPELL 너 (FIRE guard): " + B2_A0 + " FIRE: roaring orange-vermilion flame and burning black ink; on the parry the fire blasts back and scorches the paw, embers and ash fly up. NOT wood, stone, steel or water."),
    'meo': ('A0', "THE SPELL 머 (EARTH guard): " + B2_A0 + " EARTH: rough stone, packed soil and dust, heavy and weighty; on the parry the stone takes the blow with a heavy dust shock and the ground around lifts as plates. NOT fire, vines, steel or glowing water."),
    'seo': ('A0', "THE SPELL 서 (METAL guard): " + B2_A0 + " METAL: polished steel edges, white-silver and pale gold light, razor-thin lines; on the parry the steel rings out with a bright flash and sparks, shards fly SIDEWAYS. NOT fire, rock, vines or water."),
    'eo': ('A0', "THE SPELL 어 (WATER guard): " + B2_A0 + " WATER: clear heavy blue-green water with black ink swirling inside, white foam; on the parry the water splashes the blow aside and runs down. NOT fire, ice, rock or steel."),
    'gu': ('R0', "THE SPELL 구 (WOOD ring): " + B2_R0 + " WOOD: living vines, thorns, roots and leaves; the bear on the band is snared and held (slowed). It ends unravelling into rootlets that reach forward. NOT fire, stone, steel or water, and NOT a fan of bamboo shoots in front."),
    'nu': ('R0', "THE SPELL 누 (FIRE ring): " + B2_R0 + " FIRE: roaring orange-vermilion flame with burning black ink; the bear on the band is scorched and keeps burning. It burns out from its tips into ash rising upward. NOT wood, stone, steel or water."),
    'mu': ('R0', "THE SPELL 무 (EARTH ring): " + B2_R0 + " EARTH: heavy rock, soil and dust; the bear on the band is SHOVED outward hard (knockback). Afterwards it sinks and the ground around lifts as plates. NOT fire, vines, steel or glowing; NOT a fan of slabs in front (another spell owns that)."),
    'su': ('R0', "THE SPELL 수 (METAL ring): " + B2_R0 + " METAL: polished steel blades, white-silver and pale gold light, razor-thin lines; the bear on the band is CUT once, fast. It ends snapping with a bright ringing flash, shards flying out sideways. NOT fire, rock, vines or water."),
    'u': ('R0', "THE SPELL 우 (WATER ring): " + B2_R0 + " WATER: clear heavy blue-green water with black ink swirling inside, white foam; the bear on the band is DRENCHED (soaked for 4 seconds, water running down). It ends flowing down onto the ground. NOT fire, ice, rock or steel."),
}
B2_VARIANTS = {
    'geo_a': ('geo', "VARIANT A - wicker vine shield: thick thorned vines have burst out of the brush shaft and woven, in an instant, into a round living shield of vines and bark (about 1.3 m) held in front of the caster's sleeve; the bear's paw has slammed into it and the vines have whipped forward and coiled around the paw and forearm, binding it, leaves and splinters bursting from the contact, a faint jade light pulsing in the weave."),
    'geo_b': ('geo', "VARIANT B - bamboo cross guard: the brush shaft and the caster's forearm are wrapped in spiralling green bamboo and vine, and two bamboo stalks have shot out crosswise from the shaft like a staff block; the bear's paw strikes the crossing point and is caught: bamboo splinters, torn leaves and black ink spray outward in a big burst, root tendrils snap around the paw."),
    'neo_a': ('neo', "VARIANT A - fire wheel: a blazing wheel of fire spins around the brush shaft like a pinwheel of flame and burning ink in front of the caster's sleeve; the bear's paw has struck it and the wheel answers with a huge fire PEONY bursting open at the contact - layered petals of flame and black ink blooming outward, scorching the paw, embers and ash rising."),
    'neo_b': ('neo', "VARIANT B - flame sleeve wing: the caster's sleeve and the brush are wreathed in a long ribbon of flame like a burning silk sleeve; the bear's paw hits it and the flame flares into a great wing of fire sweeping up and back across the bear's arm, a ring of fire shock at the contact, sparks and black smoke."),
    'meo_a': ('meo', "VARIANT A - stone slab wall: as the paw comes down a heavy upright slab of rough stone has torn up out of the red ground in front of the caster's guard (the stone crust also runs up the brush shaft and the sleeve like a bracer); the paw slams into the slab: a heavy shock ring of dust, cracks across the stone, the ground around lifting into tilted earth plates, soil clods flying."),
    'meo_b': ('meo', "VARIANT B - stone gauntlet: the caster's forearm and the brush shaft are encased in a rugged gauntlet of stone and packed earth; the bear's paw hits it and bounces off with a dense explosion of dust and rock fragments, a dust shock ring on the ground, the earth around the caster's feet cracking into lifted plates."),
    'seo_a': ('seo', "VARIANT A - steel crescent guard: a long polished steel crescent edge has formed along the brush shaft like a halberd guard; the paw has struck the steel: a brilliant white-gold flash and a great FOUNTAIN of sparks bursts sideways from the contact, razor-thin light lines and steel shards flying out to the sides, the paw thrown back."),
    'seo_b': ('seo', "VARIANT B - fan of blades: a half-circle of floating steel blades has fanned open around the caster's forearm like a folding fan shield; the paw clangs against it: a ring-shaped flash of white light and sparks, two blades snapping into glittering shards that spray sideways, fine black ink lines like cut threads."),
    'eo_a': ('eo', "VARIANT A - water vortex shield: a spinning disc of clear water swirls around the brush shaft in front of the caster like a whirlpool shield, ink spiralling inside it; the paw strikes the whirlpool and is swept aside, a huge crown splash erupting at the contact, sheets of spray and foam, water running down the caster's sleeve."),
    'eo_b': ('eo', "VARIANT B - water mirror: a curved membrane of clear water stretches from the brush like a rippling mirror; the paw hits it and the water ripples out in rings and bursts into a heavy splash that wraps the paw and pours down onto the ground, droplets catching the light."),
    'gu_a': ('gu', "VARIANT A - crown of thorns: a full circle of thick thorned vines has whipped up out of the ground on the 2 m ring around the caster, leaning outward like a crown of thorns, waist to head high; on the right of the ring the bear is caught, the vines coiling round its legs and body; leaves, splinters and ink spray fly up along the whole circle."),
    'gu_b': ('gu', "VARIANT B - root ring snare: the ground has split in a perfect circle around the caster and a ring of dark roots and young shoots has lashed up out of the crack, curling inward and outward like a closing snare; the bear on the ring is wrapped by roots; soil and leaves burst along the circle, a faint jade light along the crack."),
    'nu_a': ('nu', "VARIANT A - ring of fire wall: a roaring circular WALL of flame has erupted on the 2 m ring around the caster, flames leaning outward, black ink smoke rolling off its crest; the caster stands untouched in the empty middle; the bear on the ring is engulfed and burning; embers and ash rise from the whole circle, the ground glows along the ring."),
    'nu_b': ('nu', "VARIANT B - fire lotus: a ring of giant flame petals has burst open around the caster like a lotus of fire opening outward, each petal a tongue of flame edged with burning black ink; the bear on the ring is struck by a petal and catches fire; the petals' tips burn into rising ash."),
    'mu_a': ('mu', "VARIANT A - earth shock ring: the ground has heaved up in a thick rolling circular ridge of soil and rock all around the caster, like a stone ripple bursting outward from a dropped weight; the bear on the ring is thrown outward by it; a huge donut of dust rolls out, rocks fly, and behind the ridge the ground cracks into lifted plates."),
    'mu_b': ('mu', "VARIANT B - standing stones burst: a circle of massive angular stone pillars has burst up out of the ground on the 2 m ring around the caster in an instant, all leaning outward like a broken henge; one pillar has rammed the bear and is shoving it backwards; dust, clods and black ink spray off the stones."),
    'su_a': ('su', "VARIANT A - blade whirl: a whirling ring of dozens of long steel blades spins around the caster at waist height on the 2 m ring, like a circular saw of knives, each blade leaving a razor-thin bright silver streak, the whole ring a blur of light; the bear on the ring is slashed, a bright cut line across it and sparks; at the edge the blades snap into shards flying sideways."),
    'su_b': ('su', "VARIANT B - halo of swords: a circle of upright steel swords has pierced up out of the ground on the 2 m ring around the caster, points up, with a bright ring of light running along the ground connecting them; the bear on the ring is pierced and cut, sparks and an X-shaped flash; the swords' tips snap with a ringing flash."),
    'u_a': ('u', "VARIANT A - ring wave: a circular WAVE of clear blue-green water has risen out of the ground all around the caster on the 2 m ring and is rolling outward, its white foamy crest curling over; the caster stands dry in the middle; the bear on the ring is hit by the wave and drenched; spray and mist hang in the air, the ground under the ring is dark and wet."),
    'u_b': ('u', "VARIANT B - water ring geyser: a ring of geyser jets of water shoots up out of the ground on the 2 m ring around the caster like a fountain crown, ink swirling in the columns; the bear on the ring is soaked as the water crashes down on it; puddles and foam spread on the ground."),
}


# ---------------------------------------------------------------------------------------------------------------------------------------------------------
# batch 3 (2026-10-26, D308-150 V12, the user: "남긴다 19자(묶음 3)를 쇼케이스 방식으로 시작해줘."): the ㄱ book 남긴다 - the spell lands and LEAVES a standing
# residue for ~10 s (6 s on the caster's own body for ㅓ). P1 낙 막 삭 악 (각 done in batch 0) · A1 걱 넉 먹 석 억 · F1 곡 녹 목 속 옥 · R1 국 눅 묵 숙 욱.
#   python Tools/Art/SpellVFX308/concept_free_showcase308.py --batch b3 [--run] [--wave 6] [ids]
# Picture 1 = the real pitch-0 gameplay frame of the template's own capture (batch3/r0 f_00000, cast camera A on): P1 bear 4.6 m ahead, A1 bear 1.2 m
# ahead / 0.3 m right, F1 bears 3.4 m (left, half hidden) and 4.3 m (right), R1 bear 1.9 m ahead / 0.5 m right on the 2 m band.
B3_OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch3/concepts'
B3_GAME = {t: REPO / f'Art/SpellVFX308/Vol2Vfx/free_showcase/batch3/r0/frames/{t}_bridge4_Fire_red/f_00000.jpg' for t in ('P1', 'A1', 'F1', 'R1')}
_B3_SCENE = """Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, holding the great brush), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and %s. Paint the concept ON THIS VIEW: keep this camera height, angle and framing (you may raise the camera a little so the thing left behind on the ground reads), the same ground, sky and bears at the same places. Ignore any dark smoke wisp or small coloured blob in picture 1 (capture artefacts). The scene keeps its muted Korean ink-wash painted look."""
B3_REF = {
    'P1': '--- THE ATTACHED PICTURES ---\n' + _B3_SCENE % "one real enemy (a dark matted bear with crusted crystal growths on its back) about 4.6 metres straight ahead, just right of the caster's head",
    'A1': '--- THE ATTACHED PICTURES ---\n' + _B3_SCENE % "one real enemy (a dark matted bear with crusted crystal growths on its back) VERY close, about 1.2 metres ahead and a little to the right",
    'F1': '--- THE ATTACHED PICTURES ---\n' + _B3_SCENE % "two real enemies (dark matted bears with crusted crystal growths on their backs) close ahead: one about 3.4 metres ahead, partly hidden behind the caster's left side, the other about 4.3 metres ahead and a little to the right",
    'R1': '--- THE ATTACHED PICTURES ---\n' + _B3_SCENE % "one real enemy (a dark matted bear with crusted crystal growths on its back) about 2 metres ahead and a little to the right - exactly on the ring around the caster",
}
_LEAVE = ("It is a 'LEAVES SOMETHING BEHIND' spell: it strikes now, and where it lands a STANDING RESIDUE of the element stays in the world for about 10 seconds - a thing that grows UP out of the ground like branches or a structure, "
          "alive and slowly moving (flickering, swaying, dripping, humming) - and any enemy that touches it is hurt again. Show the moment JUST AFTER the cast: the strike has landed on the enemy AND the residue already stands, "
          "so a player sees at a glance BOTH what the spell did AND that something stays behind.")
B3_P1 = "It flies along a narrow straight PATH from the brush tip to the first bear, hits it once, and leaves its residue as one standing mass (about 1.2 m across) right where it hit, at the bear's feet. " + _LEAVE
B3_A1 = ("The residue is on the CASTER'S OWN BODY: for 6 seconds the element clads the great brush, the sleeve and the arm (nothing is left on the ground), and every swing of the brush strikes with it. Show the caster just after "
         "a swing: the brush and arm clearly clad in the element, BIG and bold (it must read from behind the shoulder), and the close bear just struck by the swing. It must read as 'my weapon and arm are now made of this element'.")
B3_F1 = "It covers a fan-shaped field in front of the caster (5 m deep, about 4 m wide at its far edge) and the whole fan becomes a standing field of the element that stays. " + _LEAVE
B3_R1 = "It strikes a hollow circular band about 2 metres around the caster (a ring, not a filled disc - the caster stands in the empty middle) and the RING itself stays standing round the caster like a fence or a moat. " + _LEAVE
B3_SPELLS = {
    'nak': ('P1', "THE SPELL 낙 (FIRE, path, leaves fire): " + B3_P1 + " FIRE: roaring orange-vermilion flame and burning black ink; the bear is scorched. NOT a flying fire spear, NOT a wall or fan of fire, NOT a fire wheel or flower of petals (other spells own those)."),
    'mak': ('P1', "THE SPELL 막 (EARTH, path, leaves earth): " + B3_P1 + " EARTH: rough stone, packed soil and dust, heavy; the bear is shoved back. The residue blocks shots and can be climbed. NOT a rolling boulder, NOT a wave or wall of flat slabs, NOT a ring ridge."),
    'sak': ('P1', "THE SPELL 삭 (METAL, path, leaves steel): " + B3_P1 + " METAL: polished steel edges, white-silver and pale gold light, razor-thin lines; the bear is cut. NOT a thrown sword, NOT a fan of flying knives, NOT a spinning saw ring."),
    'ak': ('P1', "THE SPELL 악 (WATER, path, leaves water): " + B3_P1 + " WATER: clear heavy blue-green water with black ink swirling inside, white foam; the bear is drenched. NOT a bursting water orb, NOT a rolling wave, NOT a ring of geysers, NOT ice."),
    'geok': ('A1', "THE SPELL 걱 (WOOD, the peak of this book, SELF-HEALING): the caster heals themself: 30 health over 3 seconds; a hit taken ends the healing early. It harms NO enemy and leaves nothing in the world - the living wood grows AROUND and INTO the caster for those 3 seconds, its life flowing into the body. The bear stands near, untouched. WOOD: living vines, young leaves, blossoms, bark and soft jade-green life-light. NOT a weapon, NOT an attack, NOT a cage of branches."),
    'neok': ('A1', "THE SPELL 넉 (FIRE on my body): " + B3_A1 + " FIRE: roaring orange-vermilion flame and burning black ink; the struck bear is scorched. NOT a spinning fire wheel or flame flower (another spell owns that)."),
    'meok': ('A1', "THE SPELL 먹 (EARTH on my body): " + B3_A1 + " EARTH: rough stone, packed soil, heavy and weighty; the struck bear is shoved back hard. NOT a stone wall standing in front of the caster (another spell owns that)."),
    'seok': ('A1', "THE SPELL 석 (METAL on my body): " + B3_A1 + " METAL: polished steel edges, white-silver and pale gold light, razor-thin lines; the struck bear is cut. NOT a single crescent blade on the shaft (another spell owns that)."),
    'eok': ('A1', "THE SPELL 억 (WATER on my body): " + B3_A1 + " WATER: clear heavy blue-green water with black ink swirling inside, white foam; the struck bear is drenched. NOT a spinning water disc shield (another spell owns that)."),
    'gok': ('F1', "THE SPELL 곡 (WOOD, fan, the peak of this book): " + B3_F1 + " WOOD: the left-behind field is a young FOREST that keeps GROWING for 10 seconds, its edge spreading wider (to 1.5 times the area), and it grabs and holds every enemy that walks into it. Living trees, saplings, vines, leaves. NOT a fan of bamboo shoots that sweeps once, NOT a rib cage of branches."),
    'nok': ('F1', "THE SPELL 녹 (FIRE, fan, leaves fire): " + B3_F1 + " FIRE: the left-behind field burns: orange-vermilion flame and burning black ink; enemies inside are scorched. NOT a one-time blast of fire breath; the fire STAYS and burns in place."),
    'mok': ('F1', "THE SPELL 목 (EARTH, fan, leaves earth): " + B3_F1 + " EARTH: rough stone, packed soil, heavy; enemies inside are shoved; the field blocks shots and can be climbed. NOT a one-time eruption of flat slabs, NOT a rolling wave of earth."),
    'sok': ('F1', "THE SPELL 속 (METAL, fan, leaves steel): " + B3_F1 + " METAL: polished steel, white-silver and pale gold light, razor-thin lines; enemies passing are cut; nearby steel pieces are linked to each other by taut bright blade-lines. NOT a fan of flying knives."),
    'ok': ('F1', "THE SPELL 옥 (WATER, fan, leaves water): " + B3_F1 + " WATER: clear blue-green water with black ink swirling, white foam; enemies inside are drenched. NOT a rolling wave, NOT ice."),
    'guk': ('R1', "THE SPELL 국 (WOOD, ring, the peak of this book): " + B3_R1 + " WOOD: the roots left on the ring GROW INTO PILLARS (living tree trunks) round the caster, tall and sturdy enough to climb and stand on, branches and leaves on top. NOT a crown of thorny vines, NOT a cage over an enemy."),
    'nuk': ('R1', "THE SPELL 눅 (FIRE, ring, leaves fire): " + B3_R1 + " FIRE: orange-vermilion flame and burning black ink; an enemy crossing the ring is scorched. NOT a one-time wall of fire that dies at once; the fire STAYS standing round the caster."),
    'muk': ('R1', "THE SPELL 묵 (EARTH, ring, leaves earth): " + B3_R1 + " EARTH: rough stone, packed soil, heavy; an enemy crossing is shoved; it blocks and can be climbed. NOT a rolling ridge of earth that bursts outward once."),
    'suk': ('R1', "THE SPELL 숙 (METAL, ring, leaves steel): " + B3_R1 + " METAL: polished steel, white-silver and pale gold light, razor-thin lines; an enemy crossing is cut; the steel pieces of the ring are linked by taut bright blade-lines. NOT a spinning saw of knives."),
    'uk': ('R1', "THE SPELL 욱 (WATER, ring, leaves water): " + B3_R1 + " WATER: clear blue-green water with black ink swirling, white foam; an enemy crossing is drenched. NOT a ring of geyser jets that crash down once, NOT a rolling wave, NOT ice."),
}
B3_VARIANTS = {
    'nak_a': ('nak', "VARIANT A - fire tree: at the bear's feet a burning TREE of fire stands: a twisted trunk of glowing black ink-coal splitting into branches of flame reaching up 2 to 3 metres, embers drifting off, heat shimmer, a pool of orange light on the red ground; the bear beside it is engulfed and reeling; a fading streak of fire still links back to the brush."),
    'nak_b': ('nak', "VARIANT B - ember brazier: the fire bolt has smashed into the bear and left a heap of burning ink-coal like a giant brazier at its feet, from which a tall column of flame and sparks keeps rising and swirling, black smoke curling off its top; the ground around glows with scattered embers."),
    'mak_a': ('mak', "VARIANT A - stone crag: at the bear's feet a jagged CRAG of dark rock has torn up out of the ground (about 2 m tall), stone spurs branching upward like a petrified bush, cracks with dust pouring off; the bear has been shoved back off it; a line of torn earth runs back to the brush."),
    'mak_b': ('mak', "VARIANT B - earth pillar with roots: a thick pillar of packed earth and stone has erupted where the strike hit, its crown splitting into rocky spikes, soil roots and clods hanging from it, dust still falling; the bear is staggering back from the impact."),
    'sak_a': ('sak', "VARIANT A - steel thorn bloom: at the bear's feet a cluster of long polished steel blades has burst up out of the ground like a steel thornbush or a bloom of swords, points up and out, catching white-gold light, a faint humming glow at its heart; the bear has a bright cut line and recoils; a thin bright line still hangs from the brush to the bloom."),
    'sak_b': ('sak', "VARIANT B - steel branch tree: a small tree of steel stands where the strike hit - a metal trunk branching into thin razor branches with blade-leaves, ringing and glittering; sparks fall from the cut bear; razor-thin light lines vibrate between the branches."),
    'ak_a': ('ak', "VARIANT A - standing spout: where the water hit the bear, a STANDING FOUNTAIN of water stays: a column of clear water rising 2 m and branching at the top like coral, falling back in sheets into a swirling pool on the ground, ink curling inside; the bear is drenched and dripping."),
    'ak_b': ('ak', "VARIANT B - whirlpool well: the water strike has drenched the bear and left a living whirlpool sunk into the ground at its feet, a spinning pool of blue-green water and black ink with a small waterspout twisting up out of its centre, spray hanging in the air."),
    'geok_a': ('geok', "VARIANT A - sprouting sapling: out of the ground at the caster's feet a young tree and vines have sprouted and spiral up AROUND the caster's body like a gentle embrace (up to the shoulders), new leaves unfurling, small white blossoms opening; soft jade-green life-light and drifting pollen motes flow INTO the caster's chest; the caster's wounds glow as they close."),
    'geok_b': ('geok', "VARIANT B - lotus of sap: a great lotus of jade-green leaves and bark petals has opened on the ground around the caster's feet, and streams of glowing green sap and leaves rise from it and wind up into the caster's body and the brush, which sprouts small leaves; a halo of floating leaves turns around the caster."),
    'neok_a': ('neok', "VARIANT A - fire mantle: the caster's right arm, sleeve and the whole great brush are wreathed in roaring flame like a burning mantle, flames trailing off the brush head like a torch, branches of flame rising from the shoulder; the brush has just swung through the bear leaving an arc of fire and scorch."),
    'neok_b': ('neok', "VARIANT B - ember armour: glowing cracked plates of burning ink-coal have formed over the caster's forearm and along the brush shaft, the bristle head a blazing torch; the swing has just struck the bear, a burst of embers and black smoke at the contact."),
    'meok_a': ('meok', "VARIANT A - stone gauntlet and mace: the caster's whole arm is encased in a huge rugged gauntlet of rock and packed earth and the brush head has become a heavy stone mace head; the swing has just smashed into the bear, dust and rock fragments exploding, the bear knocked back."),
    'meok_b': ('meok', "VARIANT B - orbiting stones: a ring of heavy rough stones and earth clods orbits the caster's arm and the brush, the brush shaft crusted with rock; the swing has just struck the bear and the stones slam into it with a dust shock."),
    'seok_a': ('seok', "VARIANT A - blade glaive: steel blades have grown along the whole brush shaft like the branches of a steel tree, turning the brush into a long glaive, more thin blades along the forearm; the swing has just cut the bear, a brilliant white slash and sparks."),
    'seok_b': ('seok', "VARIANT B - wing of blades: a wing of floating long steel blades fans out behind the caster's arm and follows the brush; the swing has just slashed the bear with all of them, several bright parallel cut lines and sparks."),
    'eok_a': ('eok', "VARIANT A - water whip: a long ribbon of clear water flows around the caster's arm and spirals off the brush head like a living whip, ink swirling inside; the swing has just lashed the bear, a heavy splash crown at the contact, the bear drenched."),
    'eok_b': ('eok', "VARIANT B - waterfall sleeve: the caster's sleeve and the brush pour with flowing water like a waterfall that never runs out, the brush head a heavy blade of water; the swing has just struck the bear, sheets of spray and foam."),
    'gok_a': ('gok', "VARIANT A - growing grove: across the whole fan a young grove has sprung up - saplings, small twisted trees and leafy vines, waist to head high - its outer edge visibly spreading further out with new shoots; both bears inside are wrapped by vines and held; leaves drifting, a soft jade glow in the undergrowth."),
    'gok_b': ('gok', "VARIANT B - bramble forest: the fan has become a dense living bramble wood of thorny branches and ferns rising out of the red ground, thickest near the far edge where it keeps growing outward; both bears are snared in it; ink drips from the bark, small blossoms open."),
    'nok_a': ('nok', "VARIANT A - burning reeds: the whole fan is a field of standing fire: hundreds of flame stalks like burning reeds or grass flicker and sway on smouldering black ground, flames taller at the far edge; both bears inside are burning; ash and embers rise into the sky."),
    'nok_b': ('nok', "VARIANT B - ember bed with fire trees: the fan is a bed of glowing ink-coal embers with a few twisted trees of flame standing in it; heat shimmer over the whole fan, both bears scorched, black smoke rolling."),
    'mok_a': ('mok', "VARIANT A - stone forest: across the fan a forest of jagged stone spires and spikes (knee to head high) has torn up out of the ground, standing like stalagmites; both bears are shoved back between them, dust hanging in the air."),
    'mok_b': ('mok', "VARIANT B - basalt field: the fan has risen into a broken field of hexagonal basalt columns of different heights, like a raised stone plateau with steps, the bears thrown back off its edge; cracks and dust at its border."),
    'sok_a': ('sok', "VARIANT A - stake field: across the fan dozens of upright steel stakes and blades stand stuck in the ground at angles, linked to each other by taut thin glowing blade-lines like a web of wires; both bears are cut, sparks flying where they touched the lines."),
    'sok_b': ('sok', "VARIANT B - sword grass: the fan has become a meadow of thin steel blades growing like tall grass, glittering silver and pale gold, swaying as one; both bears inside have bright cut lines; fine sparks drift up."),
    'ok_a': ('ok', "VARIANT A - standing pool with fountains: the fan is a shallow pool of standing blue-green water on the red ground with small fountains and reeds of water rising and falling in it, ink swirling under the surface; both bears are drenched, water running down their fur."),
    'ok_b': ('ok', "VARIANT B - water marsh: the fan is a living marsh of swirling water, foam and floating ink, with slender waterspouts twisting up like stalks across it; both bears inside are soaked."),
    'guk_a': ('guk', "VARIANT A - ring of tree pillars: on the 2 m ring around the caster, roots have grown into a circle of thick living tree trunks (2 to 3 m tall, about 7 of them) with flat leafy crowns, like pillars of a round pavilion one could climb on; the bear on the ring has been struck and pushed between two trunks; leaves and green light drift down."),
    'guk_b': ('guk', "VARIANT B - root platform ring: giant interlaced roots have risen out of the ground in a circle round the caster and grown into a raised round wooden rim like a living platform, knotted trunks holding it up, saplings sprouting on top; the bear on the ring is knocked aside."),
    'nuk_a': ('nuk', "VARIANT A - ring of fire torches: a circle of standing pillars of fire (like tall burning torches or fire trees, about 8 of them) stands on the 2 m ring around the caster, linked low by a glowing line of embers; the bear crossing the ring is scorched; smoke and embers rise from each pillar."),
    'nuk_b': ('nuk', "VARIANT B - smouldering coal ring: the ring around the caster is a circular bed of glowing ink-coal with flames branching up out of it like burning shrubs, black smoke curling; the bear on the ring is burning."),
    'muk_a': ('muk', "VARIANT A - stone henge: a circle of rough standing stones (menhirs, 1.5 to 2.5 m) stands on the 2 m ring around the caster, solid enough to climb, dust settling around them; the bear on the ring has been shoved outward by the stone that rose under it."),
    'muk_b': ('muk', "VARIANT B - earth rampart: a low circular rampart of packed earth and stone with branching rocky spurs has risen on the ring around the caster like a fort wall, the caster in the hollow middle; the bear crossing it is thrown back, clods falling."),
    'suk_a': ('suk', "VARIANT A - ring of stakes and wires: a fence of upright steel stakes and spears stands on the 2 m ring around the caster, linked to each other by taut glowing blade-wires; the bear crossing the ring has bright cut lines and sparks spraying."),
    'suk_b': ('suk', "VARIANT B - circle of planted swords: a circle of swords planted point-down in the ground around the caster, their blades humming with white-gold light, a bright ring of light on the ground connecting them; the bear on the ring is cut."),
    'uk_a': ('uk', "VARIANT A - moat: a circular moat of clear swirling water runs around the caster on the 2 m ring, slow currents and ink swirling in it, low fountains and reeds of water rising along it; the bear standing in it is drenched."),
    'uk_b': ('uk', "VARIANT B - flowing water fence: a ribbon of water flows round and round the caster on the ring like a river in a circle, rising waist-high in arching branches of water that fall back into it; the bear crossing it is soaked."),
}


def prompt_b3(vid):
    spell, var = B3_VARIANTS[vid]
    tmpl, text = B3_SPELLS[spell]
    return '\n\n'.join([HEAD, B3_REF[tmpl] + '\n' + B1_REF_TAIL, STYLE, '--- THE PICTURE ---\n' + text + '\n' + var, TAIL % (B3_OUT / f'{vid}.png')])


def prompt_b2(vid):
    spell, var = B2_VARIANTS[vid]
    tmpl, text = B2_SPELLS[spell]
    return '\n\n'.join([HEAD, B2_REF[tmpl] + '\n' + B1_REF_TAIL, STYLE, '--- THE PICTURE ---\n' + text + '\n' + var, TAIL % (B2_OUT / f'{vid}.png')])


def prompt_b1(vid):
    spell, var = B1_VARIANTS[vid]
    tmpl, text = B1_SPELLS[spell]
    return '\n\n'.join([HEAD, B1_REF[tmpl] + '\n' + B1_REF_TAIL, STYLE, '--- THE PICTURE ---\n' + text + '\n' + var, TAIL % (B1_OUT / f'{vid}.png')])


def prompt(vid):
    if vid in B3_VARIANTS:
        return prompt_b3(vid)
    if vid in B2_VARIANTS:
        return prompt_b2(vid)
    if vid in B1_VARIANTS:
        return prompt_b1(vid)
    spell, var = VARIANTS[vid]
    return '\n\n'.join([HEAD, REF, STYLE, '--- THE PICTURE ---\n' + SPELLS[spell] + '\n' + var, TAIL % (OUT / f'{vid}.png')])


PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def out_of(vid):
    return B3_OUT if vid in B3_VARIANTS else B2_OUT if vid in B2_VARIANTS else B1_OUT if vid in B1_VARIANTS else OUT


def game_of(vid):
    if vid in B3_VARIANTS:
        return B3_GAME[B3_SPELLS[B3_VARIANTS[vid][0]][0]]
    if vid in B2_VARIANTS:
        return B2_GAME[B2_SPELLS[B2_VARIANTS[vid][0]][0]]
    return B1_GAME[B1_SPELLS[B1_VARIANTS[vid][0]][0]] if vid in B1_VARIANTS else GAME


def recover(vid, started):
    od = out_of(vid)
    dst = od / f'{vid}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = od / f'{vid}.log.txt'
    cand = []
    if log.exists():
        for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace')):
            p = GEN / m.group(1) / m.group(2)
            if p.exists():
                cand.append(p)
    if not cand and GEN.exists():
        cand = sorted([p for p in GEN.rglob('*.png') if p.stat().st_mtime >= started], key=lambda p: p.stat().st_mtime)[-1:]
        if cand:
            print('WARN newest-file recovery for', vid, cand[-1])
    if cand:
        dst.write_bytes(cand[-1].read_bytes())
        print('RECOVERED', vid, cand[-1])
        return True
    return False


def main():
    batch = sys.argv[sys.argv.index('--batch') + 1] if '--batch' in sys.argv else None
    args = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith('--') and not (i > 1 and sys.argv[i - 1] in ('--batch', '--wave'))]
    pool = B3_VARIANTS if batch == 'b3' else B2_VARIANTS if batch == 'b2' else B1_VARIANTS if batch == 'b1' else VARIANTS
    vids = [v for v in pool if not args or v in args]
    for v in vids:
        out_of(v).mkdir(parents=True, exist_ok=True)
        (out_of(v) / f'{v}.prompt.txt').write_text(prompt(v), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', vids)
        return
    wave = int(sys.argv[sys.argv.index('--wave') + 1]) if '--wave' in sys.argv else 8      # at most this many codex processes at once (memory)
    pending = [v for v in vids if not recover(v, time.time() + 1e9)]
    for attempt in range(1, 3):
        failed = []
        for w in range(0, len(pending), wave):
            procs = []
            for v in pending[w:w + wave]:
                od = out_of(v)
                cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(od), '-i', str(game_of(v)), '-i', str(BRUSH), '-i', str(BURST)]
                log = open(od / f'{v}.log.txt', 'w', encoding='utf-8')
                procs.append((v, time.time(), subprocess.Popen(cmd, stdin=open(od / f'{v}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
                time.sleep(2)
            for v, t0, p in procs:
                try:
                    p.wait(timeout=1500)
                except subprocess.TimeoutExpired:
                    p.kill()
                ok = recover(v, t0)
                print('DONE', v, 'try', attempt, p.returncode, ok, flush=True)
                if not ok:
                    failed.append(v)
        pending = failed
        if not pending:
            break
    for v in pending:
        print('FAILED', v)


if __name__ == '__main__':
    main()
