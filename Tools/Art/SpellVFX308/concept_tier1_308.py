#!/usr/bin/env python3
"""#308 D308-133 (debate 23) tier-1 concepts: the looks still to judge before anything is built. Each picture is two panels
(before / after) or a small board, made the same way as concept_reactions308.py (codex exec, built-in image generation,
game stills attached). Process (user 2026-10-22: "겉모습은 시안을 통해 판정하자"): concepts -> debate -> verdict -> build.

  python Tools/Art/SpellVFX308/concept_tier1_308.py            # writes the prompts
  python Tools/Art/SpellVFX308/concept_tier1_308.py --run      # also runs codex exec for each (parallel)
  python Tools/Art/SpellVFX308/concept_tier1_308.py --run t_a_wash_path   # only these

Writes Art/SpellVFX308/Concepts/tier1/t1_<id>.prompt.txt (+ .png with --run). Concepts only, never textures (D308-28)."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_reactions308 as base   # the shared head, references, look and ban of the reaction concepts

OUT = base.REPO / 'Art/SpellVFX308/Concepts/tier1'
SHOULDER, BRUSH, JEOKRO, FIRE, PATCH = base.SHOULDER, base.BRUSH, base.JEOKRO, base.FIRE, base.PATCH

# what the reaction debates taught (debate 21, D308-133): colours against the ground of the same frame, mine = strokes, theirs = pools
LESSONS = """--- RULES LEARNED FROM EARLIER ROUNDS ---
Colours are judged against the ground of the same picture: every effect is darker than the ground at its base and only its upper edge thins out lighter; nothing is white, nothing is brighter than the ground. What the PLAYER made is drawn as brush STROKES and lines; what an ENEMY made is a flat POOL - keep that split readable. Effects come DOWN or RISE a little and lean with their cause; no perfect circles or rings on the ground unless asked. Enemies stay at least 70% visible."""

PICTURES = {
    't_a_wash_path': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], """--- THE PICTURE: 온 - a low advancing fire wave that cleans the ground (purifying, 'washing by burning') ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): the ground ahead is cluttered: on the left an enemy's charred ember pool (as in picture 5, a flat dark pool with low ink tongues), on the right the caster's own leftovers - grey ash vine strokes and a dark wet mud smear. The caster swings the brush; a LOW front of fire ink starts rolling forward from the brush along the ground: a line of short torn black ink tongues with thin vermilion edges, about knee high, moving away from the caster.
Panel 2 (after, 1 s): the front has rolled 8 m forward and keeps going; behind it a straight clean PATH about 3 m wide: dry, slightly paler bare earth (still darker than a sunlit stone, matte), its two edges lined with a thin crust of grey ash and a few curling soot wisps. The pool and the leftovers are gone only inside the path; outside it they remain. The clean path must read as 'swept clean' at a glance."""),
    't_b_metal_cuts_wood': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 금극목 (metal cuts wood) - a vine field is sliced into a rain of thorns ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead the caster's wood spell lies on the red earth: a circle of low tangled green-black ink vines (about 3.5 m across), two gaunt soot-dark wolf enemies slowed in it. The caster flicks the brush and a fan of thin cold grey steel-ink needles (metal: dark gunmetal grey #6F7A80 strokes with sharp tips) flies at the vines.
Panel 2 (after, 0.5 s): the needles have cut the vines: short severed vine pieces have flown up and are FALLING back down as a rain of hard black thorns, the first ones already stuck upright in the ground like a field of short spikes, slanted the way they fell; cut vine ends show pale green-grey cores; a few severed strands still twisting in the air. The enemies stand among the falling thorns. It must read as 'cut, then rained down', not as a new growth."""),
    't_c_water_feeds_wood': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 수생목 (water feeds wood) - vines cast on wet ground grow twice as big ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead a patch of wet ground left by a water ward: dark soaked earth with an indigo-grey (#3F5866) sheen at its edges and a few small puddles (about 3 m across, irregular, not round). The caster's brush has just thrown a vine spell into it: green-black ink sprouts are starting where the wet ground is.
Panel 2 (after, 0.6 s): the vines have drunk the water: thick green-black ink vines and broad leaves burst up and out, clearly larger and denser than a normal vine patch, spreading beyond the old wet edge, their roots drinking the puddles which are now gone (the ground under them drier, darker). Two gaunt enemies caught at the edge, vines curling around their ankles. Thickening and spreading outward is the sign: water fed wood."""),
    't_d_earth_feeds_metal': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 토생금 (earth feeds metal) - a rock cover bursts into iron shards ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 6 m ahead stands the caster's earth spell: a waist-high rough ochre-and-black ink boulder rooted in the ground as cover, two gaunt enemies just behind it. The caster sends a single grey steel-ink needle (dark gunmetal #6F7A80) into the boulder.
Panel 2 (after, 0.4 s): the boulder splits open from where the needle hit: its earth crumbles into ochre dust that falls, and from inside a burst of dark iron flakes and short metal shards flies OUTWARD in a fan away from the caster toward the enemies, some shards already stuck in the ground behind them; the boulder's base remains as a low broken stump. Earth giving birth to metal: the shards come out of the rock."""),
    't_e_ward_remains': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: what the five wide wards leave behind when they end (구 누 무 수 우) ---
LAYOUT: ONE wide strip of FIVE equal narrow panels side by side, the same low first-person-ish view in each (a short stretch of red-earth ground 4 m ahead), a thin paper-coloured gap between panels. In each panel the same faint ink outline of a just-vanished ward (a low arc of brush strokes about 3 m wide where the ward stood) and what it left:
1 wood: a low tangle of dark green-black brush (knee high), the ground slowing anything that walks in.
2 fire: a thin ring-shaped trail of short black ink tongues with vermilion edges dying down into ash along the arc (harmless, already fading).
3 earth: a low mound of ochre-and-black earth along the arc, a knee-high bank one could crouch behind.
4 metal: a scatter of dark gunmetal shards that flew outward from the arc and stuck in the ground, slanted away from it.
5 water: a dark soaked strip of ground along the arc with small indigo-grey puddles, the earth darker than around it.
Each must be readable on its own; all are matte ink on the ground, nothing glows."""),
    't_f_ink_rail': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 선 (swift buff) - the caster's shots leave ink rails that the next shot rides ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1: the buff is on: a few thin dark grey ink lines (taut like a carpenter's ink line, 먹줄) hang in the air at chest height, left by the caster's last two shots, stretching from near the caster toward two enemies 10 m away; the lines are slightly sagging, fading at their far ends.
Panel 2 (0.2 s later): a new shot (a small black ink missile) is racing ALONG one of the lines, the line snapping taut and vibrating behind it, leaving a short smear of speed along the line; the shot is clearly faster than normal (stretched streak). Ink, not light: the lines are matte dark strokes."""),
    't_g_hover_cast': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 석 (focus buff) - drawing in mid-air slows the fall ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical camera from the side and a little below, a thin paper-coloured gap.
Panel 1: the caster has jumped off a low rock ledge (about 2 m) and, at the top of the jump, is drawing a glyph with the great brush in front of them: a ring of half-finished black ink strokes (the spell circle) in the air.
Panel 2 (0.6 s later): the caster is still almost at the same height, sinking slowly: long thin black ink strands hang down from the brush and the coat hem like slow falling drips, pulling downward, and the finished ink circle in front of them is about to fire. Gravity visibly held back by the ink, not by wings or light."""),
}

# round 2 (debate 24: Codex + Fable; where they split, the stricter call) - every picture again with the fixes
FIRST = """CAMERA: FIRST PERSON this time, as the player sees the game: no body in view except two hands and the lower part of the great brush at the bottom edge of the frame. Eye height 1.6 m."""
PICTURES.update({
    't_a2_wash_path': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + """
--- THE PICTURE: 온 - the caster's purifying wave washes the ground clean, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), same camera, a thin paper-coloured gap.
Panel 1 (before): on the ground ahead, an enemy's charred ember pool (flat, as in picture 5) on the left and the caster's own leftovers on the right (grey ash vine strokes, a dark mud smear). The caster's wave has just left the brush: ONE long low horizontal brush stroke lying across the ground about knee high, rolling forward, black ink with only two or three small vermilion-edged tongues riding ON the stroke, and a spray of small dark water-ink drops falling off its back edge.
Panel 2 (after, 1 s): the stroke has rolled about 8 m on. Behind it a 3 m wide strip where the pool and the leftovers have been folded away in the stroke's direction: their remains are small wet dark streaks and drops bent forward, already fading. The strip itself is the SAME brightness as the ground around it (not paler), bare and matte; it is told apart only by what is missing and by two thin edge lines of grey ash DARKER than the ground. Outside the strip the pool and leftovers remain. No white foam, no steam, no bright path."""),
    't_b2_metal_cuts_wood': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 금극목 (metal cuts wood) - the vine field is sliced into a short rain of thorns, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead a circle of low tangled green-black ink vines about 3.5 m across with two gaunt soot-dark wolf enemies slowed in it. A fan of thin MATTE dark blue-grey (#6F7A80, darker than the ground, no white, no shine) steel-ink needles flies from the brush at the vines.
Panel 2 (after, 0.5 s): the vines are cut low: about 20 short hard thorns (each about a hand long, 15-30 cm, dark green-black ink strokes with a sharp point) have hopped up no higher than knee-to-hip height and are falling back slantwise like a short rain; most are already stuck in the ground at an angle among the cut vine stubs, at most six still in the air. No green cylinders, no bamboo pieces, no leaves, nothing up in the sky. Cut ends only a slightly lighter grey-green tip. Both wolves at least 70% visible."""),
    't_c2_water_feeds_wood': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 수생목 (water feeds wood) - the wet ground is drunk by the vines, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 8 m ahead an irregular strip of wet ground left by a water ward: dark soaked earth drawn as brushed wet bands with indigo-grey (#3F5866) edges, a few small dark puddles with NO bright reflection. A vine spell has just landed in it: thin green-black ink vine strokes starting at the wet spot.
Panel 2 (after, 0.5 s): the wet bands and puddles have shrunk and darkened into the ground and are mostly gone; in their place the vine strokes are visibly THICKER and DENSER than a normal vine patch, flat woodgrain-like brush strokes lying low on the ground (below knee height), spreading outward AWAY from the caster toward and around two gaunt enemies, curling at their ankles. NO broad leaves, nothing tall, nothing within 2 m of the caster. The water feeding the wood must read: the wet is gone, the vines are fat."""),
    't_d2_earth_feeds_metal': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: 토생금 (earth feeds metal) - the earth of a rock cover wears away into iron, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), identical shoulder camera, a thin paper-coloured gap.
Panel 1 (before): 6 m ahead a waist-high rough ochre-and-black ink earth boulder rooted as cover, two gaunt enemies just behind it. One matte dark blue-grey steel-ink needle (#6F7A80, no shine) flies into it.
Panel 2 (after, 0.5 s): the boulder has worn down and slumped into a low heap of crumbling ochre earth and falling ochre dust (the mother used up); out of it, short thick rough flakes of MATTE dark iron (drawn as flat dry-brush strokes, blunt edges, no facets, no highlights, no glassy or crystal look, darker than the ground) - about ten - fly outward in a fan AWAY from the caster toward the enemies, a few already stuck slantwise in the ground beyond them. No big boulder left standing."""),
    't_e2_ward_remains': ([SHOULDER, BRUSH, JEOKRO], """--- THE PICTURE: what the five wide wards leave behind when they end (구 누 무 수 우), second version ---
LAYOUT: ONE wide strip of FIVE equal narrow panels side by side, the same low view of a stretch of red-earth ground 4 m ahead in each, a thin paper-coloured gap. In each panel a faint broken arc of brush strokes about 3 m wide where a ward stood, and what it left along that arc:
1 wood: a flat low tangle of dark green-black brush strokes, below knee height, lying along the arc.
2 fire: along a BROKEN arc (not a closed ring), only three or four small black ink tongues with thin vermilion edges dying down into ash darker than the ground.
3 earth: the ground itself heaved up into an irregular, broken bank of cracked ochre earth and dark clods, knee high (0.45 m), with NO stones, NO bricks, NO mortar lines, nothing built - just lifted earth.
4 metal: about eight flat dry-brush strokes of matte dark blue-grey iron (#6F7A80) stuck slantwise in the ground, leaning outward away from the arc, 20-30 cm each - NO crystals, NO glass, NO shine.
5 water: a dark soaked band of ground brushed along the arc, edges indigo-grey, NO bright puddle reflections.
All darker than the ground, matte ink, nothing glows."""),
    't_f2_ink_rail': ([SHOULDER, BRUSH, JEOKRO], FIRST + """
--- THE PICTURE: 선 (swift buff) - the shots leave a carpenter's ink line that the next shot rides, second version ---
LAYOUT: ONE wide strip of TWO equal panels left (before) and right (after), same first-person camera, a thin paper-coloured gap.
Panel 1: the buff is on and the last shot has just flown at an enemy 10 m ahead, a little to the right: along the exact path it flew there hangs a thin dark broken ink stroke at about waist height (0.9 m), seen slanting away below eye level; on the ground directly beneath it, a faint straight snapped-chalk-line of ink (like the mark a carpenter's ink line leaves when plucked) runs from near the caster's feet to the enemy. There is NO line between enemies.
Panel 2 (0.15 s later): a new small black ink missile races along that same path: the stretch of the hanging stroke it is passing has gone thick and is vibrating, and the ground line beneath has darkened from the caster up to the missile's position, lighter beyond it. The missile is visibly faster (stretched streak). Matte ink only, no light."""),
    't_g2_hover_cast': ([SHOULDER, BRUSH, JEOKRO], FIRST + """
--- THE PICTURE: 석 (focus buff) - drawing in mid-air slows the fall, seen from the eyes, second version ---
LAYOUT: ONE wide strip of TWO equal panels left and right, the SAME first-person jump off the same 2 m rock ledge into the same red-earth valley, a thin paper-coloured gap.
Panel 1 (normal jump, 0.4 s after leaving the ledge): the ledge's edge is already far up at the top of the frame and the ground is rushing close, a slight vertical motion smear on the near rocks, the brush held down.
Panel 2 (same jump while drawing, 0.4 s after leaving the ledge): the ledge's edge is still near the middle of the frame, the ground still far below and sharp; in front, the half-drawn glyph (a few black ink strokes, small, not a big portal ring) at the brush tip; from the brush bristles hang two or three thin black ink strands swaying downward, and a few ink drops let go and fall FAST past the bottom of the frame - faster than the viewer. The viewer is held; the ink falls."""),
})

# vol.2 (debate 25, evidence 5): a melee seen from the eyes - can the player read whose ink is whose in half a second?
PICTURES.update({
    'v2_melee_read': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test ---
ONE single full frame (no panels). Red-earth valley, three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a ragged soldier with a raised shield in the middle, and an enemy ink-caster at the back right with one hand raised.
On the ground, exactly these, each clearly separate:
1. The PLAYER's ink, always drawn as STROKES and lines (never pools): (a) a low cluster of dark green-black branching vine strokes rising a little off the ground, 7 m ahead slightly left, lying across the wolf's path; (b) a thin straight crack-line of dark metal-grey ink cut into the ground running toward the shield soldier; (c) a ring of dark water-ink strokes about 2.5 m from the viewer, its front arc visible at the bottom of the frame (the player's own ward), nothing hiding the hands.
2. The ENEMY's ink, always a flat POOL: one charred black ember pool with a few low ink tongues with thin vermilion edges near the caster, and from it a faint smudged ink bleed already creeping along the ground toward the player's vine strokes (a warning: in 0.3 s it will touch them) - the bleed is a soft spreading stain, not a line.
3. Above the enemy caster's raised hand, a small floating glyph that has been SMEARED OVER with ink so it cannot be read (an enemy using a letter the player does not own).
All matte ink, darker than the ground, nothing glows, no HUD. The enemies are at least 70% visible; the middle third of the frame is not covered. It must be possible to tell at a glance which marks are the player's (strokes), which are the enemy's (pools), and where the next reaction will happen."""),
    'v2_melee_read2': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test, second version ---
ONE single full frame (no panels). Red-earth valley, three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a ragged soldier with a raised shield in the middle, an enemy ink-caster at the back right with one hand raised.
On the ground, exactly these, each clearly separate:
1. The PLAYER's ink, always drawn as STROKES and lines (never pools): (a) a low cluster of dark green-black branching vine strokes rising a little off the ground, 7 m ahead slightly left, across the wolf's path; (b) a thin straight crack-line of dark metal-grey ink cut into the ground running toward the shield soldier; (c) the front arc of a ring of dark water-ink strokes about 2.5 m ahead at the bottom of the frame, not hiding the hands.
2. The ENEMY's ink, always a flat POOL: one charred black pool near the caster with only two or three small, DARK ink tongues (the vermilion is a thin dull edge, not bright flame).
3. THE WARNING (the most important thing): from the enemy pool, a dark WET INK STAIN creeps along the ground like several thin spreading fingers of soaked ink, flat on the ground, darker than the earth, reaching to within half a metre of the player's vine strokes - like ink bleeding into paper. NOT smoke, NOT a cloud, nothing in the air.
4. Above the enemy caster's raised hand, a small floating glyph SMEARED OVER with ink so it cannot be read.
All matte ink, darker than the ground, nothing glows, no HUD. Enemies at least 70% visible; the middle third of the frame not covered."""),
    'v2_verb_marks': ([SHOULDER, JEOKRO], """--- THE PICTURE: the five verb marks - a reading chart of the spell grammar ---
CAMERA: looking down at about 45 degrees at flat red-earth ground, first person, no body visible.
LAYOUT: ONE wide strip of SIX equal panels side by side, a thin paper-coloured gap, the same ground and light in each. Each panel shows ONE ink mark left on the ground by a spell, about 1.5 m across, all in the same dark matte black ink with only a thin edge tint, darker than the ground:
1 (leave): ink BRANCHES rising up out of the ground - forked stroke tips standing a little up, like a small low thicket.
2 (spread): a FLAT soft-edged blot spreading outward in several directions, lying flat, no height.
3 (plant): a few small upright PEGS or short posts of ink stuck into the ground in a loose cluster, each with a dot at its foot.
4 (cut): a straight CRACK-LINE cut DOWN into the ground, sharp-edged, a groove with split earth on both sides.
5 (return): a CLOSED CURVED loop of ink, a ring-like stroke that comes back to where it started.
6 (none): bare ground with only a faint scuff - nothing left.
The six must be told apart instantly by their OUTLINE alone (rising branches / flat blot / standing pegs / sunken line / closed loop / nothing). Nothing glows, no text or labels in the image."""),
    'v2_melee_read3': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test, third version ---
ONE single full frame (no panels). Red-earth valley, three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a ragged soldier with a raised shield in the middle, an enemy ink-caster at the back right with one hand raised.
OWNERSHIP RULE that must be visible: the PLAYER's ink is HOLLOW - built of separate dry-brush strokes with a visible dry, broken brush EDGE, never a solid fill. The ENEMY's ink is a SOLID wet pool with no edge strokes at all.
On the ground, exactly these:
1. The player's THREE marks, all hollow dry-brush strokes: (a) a low cluster of dark green-black branching vine strokes 7 m ahead slightly left, across the wolf's path; (b) a thin straight crack-line of dark metal-grey ink cut into the ground running toward the shield soldier; (c) a flat BAND about 0.6 m wide of indigo-grey water-ink strokes lying on the ground in a wide arc about 2.5 m ahead (the player's ward), its front arc in the lower part of the frame but NOT crossing the hands.
2. The enemy's ONE mark: a solid charred black wet pool near the caster, with two small ink tongues whose edges are only a dull dark soot-brown (no red, no orange, no bright colour at all).
3. THE WARNING: from the enemy pool, two or three thin creeping fingers of wet dark ink run flat along the ground and their tips TOUCH the outline of the player's vine strokes. Thin, on the ground, nothing in the air.
4. Above the enemy caster's raised hand, a small floating glyph SMEARED OVER with ink so it cannot be read.
The brush and hands stay at the bottom edge, out of the middle third. All matte ink, darker than the ground, nothing glows, no HUD. Enemies at least 70% visible."""),
    'v2_melee_read4': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test, fourth version ---
ONE single full frame (no panels). Red-earth valley, three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a ragged soldier with a raised shield in the middle (a Joseon-era soldier: lamellar coat, a long rectangular wooden shield, not a round shield), an enemy ink-caster at the back right with one hand raised.
OWNERSHIP RULE, shown by EDGES first: the PLAYER's ink is HOLLOW - separate dry-brush strokes with a dry, broken, feathered brush edge, the ground visible between strokes. The ENEMY's ink is a SOLID wet pool with a smooth soaked edge and no strokes.
On the ground, exactly these:
1. The player's THREE marks, all hollow dry-brush strokes: (a) a low tangle of green-black branching vine strokes 7 m ahead slightly left, across the wolf's path, its outline clearly made of separate strokes; (b) a thin straight crack-line of dark metal-grey ink cut into the ground running toward the shield soldier; (c) ONE single flat band about 0.6 m wide of indigo-grey dry-brush water strokes lying on the ground in a wide arc about 3 m ahead - it stays in the lower third of the frame ABOVE the hands, does not cross the hands, does not enter the middle third.
2. The enemy's ONE mark: a solid wet black pool around the caster's feet. Its fire nature is shown by SHAPE, not colour: a few tongue-shaped licks of the same black ink rising a little from the pool's rim, soot-brown at most - no red, no orange.
3. THE WARNING: from the enemy pool, two or three thin creeping fingers of wet black ink (smooth soaked edges, same as the pool) run flat along the ground to the LEFT, and their tips clearly TOUCH the outer strokes of the player's vine tangle (a). Nothing in the air.
4. Above the caster's raised hand, a small floating glyph made of a few clear brush strokes that has been crossed out and smeared over with a blot of ink, so you can tell it was a letter but cannot read it.
Hands and brush at the very bottom edge. All matte ink, darker than the ground, nothing glows, no HUD. Enemies at least 70% visible."""),
    'v2_verb_marks2': ([SHOULDER, JEOKRO], FIRST.replace('no body in view except two hands and the lower part of the great brush at the bottom edge of the frame', 'no body in view') + """
--- THE PICTURE: the spell marks as the player sees them on the ground - a reading chart, second version ---
LAYOUT: ONE wide strip of EIGHT equal narrow panels side by side, the SAME first-person camera in each looking at flat red-earth ground, the mark about 5 m ahead, a thin paper-coloured gap. Every PLAYER mark is drawn as HOLLOW dry-brush strokes with a visible dry broken brush edge. Panels:
1 leave: dark ink BRANCH strokes rising up out of the ground, a low forked thicket (knee high).
2 leave, EXTREME version: the same branch strokes grown into a fuller, taller (waist high) thicket of the same shape - clearly the same mark, bigger and more complete.
3 spread (player's): a FLAT spreading mark made of many overlapping dry brush strokes fanning outward - hollow, you can see the separate strokes and the ground between them.
4 spread (ENEMY's, for comparison): a SOLID wet black pool of the same size with no edge strokes - a filled stain.
5 plant: ONE single small upright peg of ink stuck in the ground with a dot at its foot (only one).
6 cut: a straight crack-line cut DOWN into the ground, sharp-edged groove.
7 return: ONE thin closed loop stroke about 1 m across lying on the ground.
8 none: bare ground, only a faint scuff.
Nothing glows, no text or labels."""),
})

# vol.2 round 6 (debate 25): the melee and the chart in ONE painted ink look, and how an enemy's attack shows its element
INKLOOK = """--- PAINTING, NOT PHOTO ---
Render the whole frame as a painted ink-wash game frame: ground, rocks and figures built from brush washes and dry-brush texture over a faint paper grain, like the ink-wash tone of picture 1 pushed further. No photographic textures, no realistic 3D-render sheen, no specular highlights."""
PICTURES.update({
    'v2_melee_read5': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + '\n' + INKLOOK + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test, fifth version ---
ONE single full frame (no panels). Red-earth valley, three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a Joseon-era soldier in a lamellar coat with a long rectangular wooden shield in the middle, an enemy ink-caster at the back right with one hand raised.
OWNERSHIP RULE, shown by EDGES: the PLAYER's ink is HOLLOW - separate dry-brush strokes with a dry, broken, feathered edge, ground visible between strokes. The ENEMY's ink is a SOLID wet pool with a smooth soaked edge.
On the ground, exactly these:
1. The player's THREE marks: (a) a low tangle of hollow dry-brush vine strokes 7 m ahead on the LEFT, in open ground, clearly in front of and apart from every enemy body; (b) a thin straight crack-line cut into the ground running toward the shield soldier; (c) ONE flat band about 0.6 m wide of hollow indigo-grey dry-brush strokes lying on the ground in a wide arc about 3 m ahead, in the lower third of the frame above the hands, never crossing them.
2. The enemy's ONE mark: a solid wet black pool around the caster's feet, fire shown by SHAPE only: a few black tongue-shaped licks rising from its rim, soot-brown at most.
3. THE WARNING (most important): a WIDE, SOFT, PALE-GREY ink-wash BAND (about 1 m wide, lighter than the black pool but still darker than the ground, like diluted ink soaking into paper) runs flat along the open ground from the edge of the enemy pool to the player's vine tangle and visibly soaks into the vine's outer strokes. The meeting point is in EMPTY GROUND, not behind any enemy. It is clearly a different thing from the black pool (paler, blurred, no edge) and from the player's strokes (no strokes at all).
4. Above the caster's raised hand, a small floating glyph of a few clear brush strokes, crossed out and smeared with a blot of ink.
Hands and brush at the very bottom edge. Matte ink, nothing glows, no HUD. Enemies at least 70% visible."""),
    'v2_verb_marks3': ([SHOULDER, JEOKRO], FIRST.replace('no body in view except two hands and the lower part of the great brush at the bottom edge of the frame', 'no body in view') + '\n' + INKLOOK + """
--- THE PICTURE: the spell marks as the player sees them on the ground - a reading chart, third version ---
LAYOUT: TWO rows of FIVE equal panels (ten panels), the SAME first-person camera in each looking at flat red-earth ground, the mark about 5 m ahead, thin paper-coloured gaps. Every PLAYER mark is HOLLOW dry-brush strokes with a dry broken brush edge. Panels, top row left to right then bottom row:
1 leave: dark ink BRANCH strokes rising out of the ground, a low forked thicket, knee high.
2 leave, EXTREME: the same branch strokes, fuller and denser, at most waist high (1 m) - clearly the same mark, more complete.
3 spread (player's): a FLAT spreading mark of many overlapping dry brush strokes fanning outward, the ground visible between them.
4 spread (ENEMY's): a SOLID wet black pool of the same size, smooth soaked edge, no strokes.
5 plant: ONE small upright ink peg stuck in the ground with a dot at its foot.
6 cut: a straight crack-line cut DOWN into the ground, a sharp-edged groove about 3 m long.
7 cut, EXTREME: the same crack-line, longer and deeper with split earth thrown up on both sides - clearly the same mark, more complete.
8 ward band: a flat BAND about 0.6 m wide of hollow indigo-grey dry-brush strokes lying on the ground as a wide ring about 4 m across (only the near part of the ring in view).
9 return: ONE thin closed loop stroke, at most 1 m across, lying on the ground - a single thin line, nothing like the wide band of panel 8.
10 none: bare ground, only a faint scuff.
Nothing glows, no text or labels."""),
    'v2_attack_tells': ([SHOULDER, JEOKRO], FIRST.replace('no body in view except two hands and the lower part of the great brush at the bottom edge of the frame', 'only the two hands and the lower brush at the bottom edge') + '\n' + INKLOOK + """
--- THE PICTURE: how an enemy's attack shows its element 0.3 s before it lands - five tells ---
LAYOUT: ONE wide strip of FIVE equal panels side by side, thin paper-coloured gaps. In every panel the SAME enemy (a gaunt Joseon-era soldier in a worn lamellar coat with a long spear) 3 m ahead, in the SAME wind-up pose (spear drawn back, about to thrust), the same red-earth ground. Only the tell on the spear and body changes:
1 wood: thin dark root hairs and tiny twig strokes sprouting along the spear shaft and the soldier's forearm, curling forward.
2 fire: black ash flakes and soot curls lifting UP off the spear head and shoulders, tiny dull soot-brown edges, like paper about to catch - no flame, no light.
3 earth: the soldier visibly heavy and sinking: feet pressed into the ground with ochre dust and clods sliding down around them, the spear head dragging dust downward.
4 metal: the spear blade and the coat's lamellar plates split by sharp thin dry cracks and chipped edges, a few small dark flakes snapping off sideways - hard, angular, dry, no shine.
5 water: wet dark ink running DOWN the spear and dripping from its tip and the coat hem, small drops falling to the ground.
Each tell must be told apart by MOTION DIRECTION and shape alone (wood = curls forward, fire = lifts up, earth = sinks down, metal = snaps sideways, water = drips down). All matte ink, nothing glows, no text."""),
})
PICTURES['v2_attack_tells2'] = (PICTURES['v2_attack_tells'][0], PICTURES['v2_attack_tells'][1]
    .replace('the five tells', 'the five tells, second version')
    .replace('only the two hands and the lower brush at the bottom edge', 'only a small part of one hand and the brush tip in the bottom-right corner (at most 15% of the panel)')
    .replace("""3 earth: the soldier visibly heavy and sinking: feet pressed into the ground with ochre dust and clods sliding down around them, the spear head dragging dust downward.""",
             """3 earth: NOTHING falls or drips. The soldier's feet are driven ankle-deep INTO the ground and the cracked earth around them has heaved UP into a low raised ring of broken clods and slabs, like ground pushed up by a heavy weight; the spear shaft bowed down by weight.""")
    .replace('a few small dark flakes snapping off sideways - hard, angular, dry, no shine.', 'a few small dark flakes snapping off sideways - hard, angular, dry, MATTE dark grey like the rest of the ink, no white edges, no glint, no shine.')
    .replace('wet dark ink running DOWN the spear and dripping from its tip and the coat hem, small drops falling to the ground.', 'thin wet BLACK ink running down the spear and dripping from its tip and the coat hem, the only thing in the five panels that drips; matte, no blue sheen, no reflection.')
    .replace('earth = sinks down', 'earth = sinks in and the ground heaves up'))

# vol.2 round 7 (debate 25): the melee once more, and how a used-up child residue disappears
PICTURES.update({
    'v2_melee_read6': ([SHOULDER, BRUSH, JEOKRO, FIRE, PATCH], FIRST + '\n' + INKLOOK + """
--- THE PICTURE: a busy fight, the moment before a reaction - readability test, sixth version ---
ONE single full frame (no panels). Red-earth valley, muted and slightly desaturated like ink on tinted paper. Three gaunt enemies 6-12 m ahead: a soot-dark wolf charging from the left, a Joseon-era soldier in a lamellar coat with a long rectangular wooden shield on the RIGHT side of the frame, an enemy ink-caster at the back slightly right of centre with one hand raised.
OWNERSHIP RULE, shown by EDGES: the PLAYER's ink is HOLLOW dry-brush strokes with a dry, broken, feathered edge, ground visible between strokes. The ENEMY's ink is a SOLID wet pool with a smooth soaked edge.
On the ground, exactly these:
1. The player's TWO marks: (a) a low tangle of hollow dry-brush vine strokes 7 m ahead on the LEFT in open ground, apart from every enemy - dark ink with only a faint dull olive tint, not bright green; (b) ONE flat band about 0.6 m wide of hollow dark indigo-grey dry-brush strokes on the ground as a short arc about 3 m ahead, spanning at most HALF the frame width, low in the frame above the hands.
2. The enemy's ONE mark: a solid wet black pool around the caster's feet whose FIRE nature shows in the same vocabulary as fire everywhere in this game: fine black ash flakes and thin soot curls LIFTING UP from its rim (upward motion), a few black tongue-shaped licks, soot-brown at most.
3. THE WARNING: a WIDE, SOFT, PALE-GREY ink-wash band (about 1 m wide, lighter than the black pool, darker than the ground, like diluted ink soaking into paper) runs flat from the LEFT edge of the enemy pool across EMPTY ground - passing under NO enemy - to the player's vine tangle and soaks into its outer strokes.
4. Above the caster's raised hand, a small floating glyph of a few brush strokes, crossed out and smeared with a blot of ink.
No crack lines on the ground. Hands and brush at the very bottom edge. Matte ink, nothing glows, no HUD. Enemies at least 70% visible."""),
    'v2_child_consumed': ([SHOULDER, JEOKRO], FIRST.replace('no body in view except two hands and the lower part of the great brush at the bottom edge of the frame', 'no body in view') + '\n' + INKLOOK + """
--- THE PICTURE: a real reaction versus a used-up leftover - two panels ---
LAYOUT: ONE wide strip of TWO equal panels left and right, the SAME first-person view of flat red-earth ground 5 m ahead, a thin paper-coloured gap. In both panels a small black fire-ink stroke (torn black ink with a thin dull vermilion edge) has just landed on a patch of the player's hollow dry-brush vine strokes about 1.5 m across.
Panel 1 (a real reaction, 0.4 s later): the vines have CAUGHT: on that spot a NEW thing now stands - a small cluster of black fire-ink tongues rising from the vine strokes with fine ash lifting up, clearly a new mark born there, as dark as before.
Panel 2 (a used-up leftover, 0.4 s later): the vine strokes are a used-up leftover: they simply DRY OUT and FADE like ink drying on paper - the strokes go from black to pale grey, thin, broken, their edges crumbling into faint dust, almost gone; NOTHING new appears, no flame, no tongues, no smoke. The fire stroke too has gone pale.
The two must read at a glance as 'something new was born' versus 'it just dried away'. Matte ink, nothing glows, no text."""),
})



def prompt(pid):
    refs, body = PICTURES[pid]
    ref = base.REF if FIRE in refs else '\n'.join(base.REF.splitlines()[:4])
    return '\n\n'.join([base.HEAD, ref, base.STYLE, LESSONS, body, base.BAN, base.TAIL % (OUT / f't1_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f't1_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f't1_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f't1_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f't1_{pid}.png').exists())


if __name__ == '__main__':
    main()
