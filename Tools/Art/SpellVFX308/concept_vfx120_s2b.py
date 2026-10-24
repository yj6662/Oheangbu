#!/usr/bin/env python3
"""#308 vol.2 vfx120 stage 2 (concept stills), sheet group B (2026-10-24, plan Docs/Research/vfx120/plan_v10_final.md,
sheet list Docs/Research/vfx120/stage2_sheet_list.md, ledger Docs/Research/vfx120/stage2_B_ledger.md).

Sheets: 6a 6b 6c 6d 7a 7b 7c 7e 7f 7g 7h 7n 10a 10b 10c 10d  (7d 7i 7j 7k 7l 7m = engine renders, not here).

  python Tools/Art/SpellVFX308/concept_vfx120_s2b.py --list
  python Tools/Art/SpellVFX308/concept_vfx120_s2b.py --prompts                 # write all gen prompts only
  python Tools/Art/SpellVFX308/concept_vfx120_s2b.py --gen 6d,7a --ver 1       # generate (<=3 codex image runs at once)
  python Tools/Art/SpellVFX308/concept_vfx120_s2b.py --gen 6d --ver 2          # redraw: attaches v1 image + <sheet>_fix1.txt
  python Tools/Art/SpellVFX308/concept_vfx120_s2b.py --judge 6d --ver 1        # Codex judge with the still-only rubric

Outputs: Art/SpellVFX308/Concepts/vfx120_s2/<sheet>_<part>_v<ver>.png (+ .prompt.txt, .log.txt), <sheet>_v<ver>.judge.md.
Concepts only (D308-28). Image runs: 3 retries on network errors; the file is recovered from ~/.codex/generated_images if codex
did not copy it."""
import re, subprocess, sys, time, shutil, os, threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_reactions308 as base

REPO = base.REPO
OUT = REPO / 'Art/SpellVFX308/Concepts/vfx120_s2'
PLAN = REPO / 'Docs/Research/vfx120/plan_v10_final.md'
CON = REPO / 'Art/SpellVFX308/Concepts'
MARKS = CON / 'tier1/t1_v2_verb_marks3.png'
BODIES2 = CON / 'vol2/v2_bodies2.png'
FRAMES2 = CON / 'vol2/v2_frames2.png'
RX_FIRE = CON / 'reactions/rx_r_a2_wildfire.png'
RX_STEAM = CON / 'reactions/rx_r_b3_steam.png'
RX_MUD = CON / 'reactions/rx_r_d2_mud.png'
TELLS = CON / 'tier1/t1_v2_attack_tells2.png'
BANDIT = CON / 'weapon_forms/ref_bandit.png'
FIRSTP = CON / 'ground_rise/ref_firstperson.png'
CHEONG = REPO / 'Art/Presentation297/Stills/cliffs308_final_cheongrim_a.png'
GEN_HOME = Path.home() / '.codex/generated_images'
GATE = threading.Semaphore(3)
SIMPLE = {}   # sid -> simplified parts used from version 4 on (structure simplification after two failed redraws)
SIMPLE_NOTE = {}

# ----------------------------------------------------------------------------------------------------------------- shared text
REF_NAMES = {
    'SHOULDER': "a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder) and the ink-wash tone. Do not copy any user interface.",
    'BRUSH': "the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.",
    'JEOKRO': "the region 적로: a dry red-earth valley of cracked ochre-brick ground, scorched trees and dark rock - use it for the red ground (ground colour about Lab 35,30,25 = #863D2C in shade).",
    'MARKS': "the game's accepted board of what spells LEAVE on the ground (branches, flat spread strokes, single post, crack, band, thin ring): keep that mark language - all of it is thin dry-edged ink.",
    'BODIES2': "the game's accepted board of the five bodies as one path stroke with a mark at the target's feet (wood fibres, fire ash, earth slab, metal shards, water drips): keep its ink language and ground.",
    'FRAMES2': "the game's accepted board of the four frames (path lane, attach on brush shaft and sleeve, fan, hollow ring band): keep its ink language.",
    'RX_FIRE': "an accepted reaction concept (wood feeds fire = wildfire): charred vine branches with black torn ink tongues running ALONG the branches, only a thin brick-red edge, grey ash.",
    'RX_STEAM': "an accepted reaction concept (water breaks fire = steam): dark grey-brown broken sheets, centre open, enemy visible.",
    'RX_MUD': "an accepted reaction concept (earth breaks water = mud): sticky dark-brown mud at the same footprint, low folds, enemy feet sunk.",
    'TELLS': "the game's accepted enemy attack-tell idea: an enemy fighter whose weapon carries the body's mark. In THIS job a tell is always a FILLED WET stroke attached to the enemy's weapon or body, never a drawing on the ground and never hollow or dry-rimmed - override anything in the picture that disagrees.",
    'BANDIT': "an enemy body for scale and pose: a gaunt soldier-bandit with a short weapon. Repaint it in the ink-wash style (matte, torn cloth, soot-dark skin) - no cartoon colours.",
    'FIRSTP': "a first-person game screenshot: the caster's right hand holds the great brush at the lower right; the eye is at head height looking along the ground. Use it ONLY for the eye height, the brush at the lower right and the field of view. Do not copy any user interface, compass, jars, map or caster ink.",
    'CHEONG': "the region 청림 (green forest): only for the grey-green sky and the olive-green hue of the world - the ground there is DARK DAMP FOREST FLOOR (moss, leaf litter, shaded green-black earth, Lab 25,-22,15 = #1C4324 in shade).",
}
REF_KEYS = {'SHOULDER': base.SHOULDER, 'BRUSH': base.BRUSH, 'JEOKRO': base.JEOKRO, 'MARKS': MARKS, 'BODIES2': BODIES2,
            'FRAMES2': FRAMES2, 'RX_FIRE': RX_FIRE, 'RX_STEAM': RX_STEAM, 'RX_MUD': RX_MUD, 'TELLS': TELLS, 'BANDIT': BANDIT,
            'FIRSTP': FIRSTP, 'CHEONG': CHEONG}

PALETTE = """--- THE PALETTE (plan C) ---
Body pigments - colour only reinforces the shape: WOOD green #368B75 (dry layer #4B9F88); FIRE brick red #BE543B (dry layer #DF7155), heat colour #C75436 only locally on the edge of a black tongue, red never more than half of any fire shape; EARTH ochre #B68A3D; METAL pale grey #A6B0AD (dry layer #899390), never brighter than the paper, no glint; WATER slate blue rim #4F6A84 with a deep core #2C465A (dry layer #7893AF); a blue-grey #3F5866 only at the base of steam and as wet shading.
TWO LAYERS COEXIST on every caster stroke along most of its outline: a dark reading-ink layer (#2C2C2C, like the black of the brush) and a lighter dry pigment layer of the body colour (about L*60), laid as the dry edge and pigment deposit of the SAME stroke. It must never read as a glowing rim, a pasted sticker or a separate double outline. Stroke layers are at least 3 px thick, hollow gaps inside the caster's marks at least 6 px."""

OWNERSHIP = """--- OWNERSHIP GRAMMAR (the most important rule; colour alone never says who owns something) ---
MINE (the caster's): thin dry brush strokes in HOLLOW bundles with a dry broken rim and the ground or paper showing between the strokes - never a filled shape, never a solid pool, never a ring disc, never a wall.
ENEMY: rimless, WET, solid-filled matte surfaces whose darker/lighter wash is inside the shape (lightness about L* 25-45), no dry rim at all, no hollow bundle. An enemy ATTACK TELL is a filled wet directional stroke fixed on the enemy's weapon or body. An enemy MATERIAL WARNING is a thin filled wet link that runs from the enemy to the chosen piece of ground material and touches it. An enemy ZONE is a filled wet surface lying on the ground and not touching the weapon.
Wet enemy surfaces of the same body use the same colour as each other: the shape alone (where it is attached, which way it points, how far it lies on the ground) tells them apart. Enemy wet surface targets: fire on red earth Lab (45,60,40) matte wet red-brown (about #B0382A, never glowing, chroma not above the body colour + 8); wood on green forest Lab (45,-38,25) matte wet green (about #29793F)."""

BODY_CUES = """--- THE FIVE BODIES: first cue (readable without colour) ---
WOOD: long torn fibre ends and fraying branches. FIRE: burnt voids and saw-tooth ends, black torn tongues. EARTH: low overlapping stair-stepped terraces drawn like CONTOUR LINES - nested stepped outline strokes in dry ochre-and-ink brush, flat on the ground, with the red ground colour visible INSIDE every step (only the outlines and a few thin hatching strokes carry ochre); NO planks, NO 3D box faces, NO visible side walls, NO ochre-filled tops, nothing that looks like cut wood or stone blocks. METAL: straight cut ends and short transverse breaks. WATER: open hooks and hollow parallel currents that cling to a surface.
HOLLOWNESS APPLIES TO EVERY BODY: slabs, blades, fibre bundles and currents are bundles of thin dry strokes with the ground visible between and inside them; METAL blades are bundles of thin needle-like straight strokes (pairs of parallel hairlines with ground between them, matte, broken dry pigment inside the dark stroke, never solid plates, bars, rods or polished strips); WATER currents are bundles of thin parallel broken lines with open hooks, never smooth ribbons; no uniform flat bands, no colour decals inside a stroke.\nNatural endings (only when a stroke is ending): wood - fine rootlets unravel in the direction it was drawn; fire - ash goes up; earth - sinks while the ground slabs around it lift; metal - snaps short and parts sideways; water - runs down along the surface."""

STYLE = """--- THE LOOK ---
A game concept paint-over in the style of the attached game pictures: warm grey-beige, slightly desaturated, like ink wash over a 3D scene, matte, soft paper grain. All magic is INK and soaked-in mineral pigment, never light: matte, no highlights, no sheen, nothing glows, no light is cast onto the ground or the characters. Effects rise at most 1.3 m (usually far less), enemies stay at least 70 % visible, nothing hides the caster's hand or brush."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no emissive anything, no light rays, no sparks, no lens flare, no bright flames, no white-hot cores, no white steam. No red blood. No text, numbers, labels, captions, panel titles, diagram arrows, readable calligraphy characters, letters, signature, watermark or outer frame. No HUD, no icons, no compass, no map. No filled hero shapes for the caster's magic, no walls of blades, no discs or round barrier on the ground, no ring of fire."""

LAYOUT = {
    '2x2': "ONE wide 16:9 picture divided into a 2 x 2 grid of four equal panels (top row: panels 1, 2; bottom row: panels 3, 4), thin paper-coloured gaps, identical camera and ground in all panels.",
    '4strip': "ONE wide 16:9 picture of FOUR equal tall panels side by side (panels 1 to 4 from left to right), thin paper-coloured gaps, identical camera and ground in all panels.",
    '5strip': "ONE wide 16:9 picture of FIVE equal tall panels side by side (panels 1 to 5 from left to right), thin paper-coloured gaps, identical camera and ground in all panels.",
    '3x2': "ONE wide 16:9 picture divided into a 3 x 2 grid of six equal panels (top row: panels 1, 2, 3; bottom row: panels 4, 5, 6), thin paper-coloured gaps, identical camera in all panels.",
    '2x2pairs': "ONE wide 16:9 picture divided into a 2 x 2 grid of four equal panels (top row: panels 1, 2; bottom row: panels 3, 4). EVERY panel is split by a thin vertical paper-coloured seam into a LEFT half (A) and a RIGHT half (B) that show the two things being compared on the SAME ground, same camera, same distance, same size.",
}

# ----------------------------------------------------------------------------------------------------------------- reaction panels
REACT_INTRO = """--- THE PICTURE ---
Each panel is ONE reaction of the five-element system as the still frame 0.08 s after the commit: a low ground-level three-quarter view of a patch of red earth about 2.5 m wide, about 4 m in front of the caster (the caster is not in the picture). EVERYTHING in these panels belongs to the caster: thin dry strokes in hollow bundles, dry broken rim, ground showing between strokes. A pale grey dry line (consumed material) marks where the old material was used up. Every panel shows (a) what the contact material WAS (left side, ghosted/remaining parts), (b) the RESULT (right side), and the result must be readable by its BODY (colour plus edge cue), by its CONTACT SILHOUETTE (the named shape) and by OWNERSHIP (the caster's dry hollow grammar). Nothing taller than 1.2 m. No ring, no disc, no wall, no explosion, no flying particles, no second owner. The enemy is not in the picture."""

R = {  # reaction number -> panel text (acting -> contact, result)
 1: "WOOD acts on WOOD (비화, spread of the same): an existing dry wood branch bundle on the ground; ONE extra long torn-fibre layer lies beside it, so the bundle is thicker by exactly one layer. Result: wood, same owner, nothing new born, no burst.",
 2: "WOOD acts on FIRE (먹임, feed): wood fibres FOLD INTO the split ends of an existing fire form (black torn tongues with burnt voids); the fibres end inside the tongues' split tips. Result: a new FIRE body in the fire-tongue form (saw-tooth black tongues, only a thin brick-red edge), owner = the caster (dry hollow bundles).",
 3: "WOOD acts on EARTH (뿌리 붕괴, root collapse): the support is cut - wood roots fold low and flat under stair-stepped ochre slabs that sag; result: a low, slowing contact outline (slabs and folded roots, dry hollow strokes), no pit, no hole. Owner = the caster.",
 4: "WOOD acts on METAL (잘림, cut): fibres run into straight pale-grey blades and are cut off at a straight line, the severed fibre ends lie short; the blade bundle remaining is only HALF of what it was (a visible half-size remainder), same owner.",
 5: "WOOD acts on WATER (옮겨붙음, carry over): the gaps between the hollow parallel water currents dry out and fibres sprout from them. Result: a WOOD body that follows the water-current form (long fibres running in parallel hollow current lines), owner = the caster.",
 6: "FIRE acts on WOOD (들불, wildfire): a vine/branch bundle (wood) is still there as fuel, every branch charred black; small black torn ink tongues run ALONG the branches and past their tips so the branching pattern stays visible; only a thin brick-red edge, red area at most half; old green outline burnt to grey ash that is DARKER than the ground; no round pool, no ring, no tongue standing alone.",
 7: "FIRE acts on FIRE (비화): an existing black torn fire tongue; one more layer of saw-tooth tips is added along its edge, thicker by one layer; no explosion, nothing new born.",
 8: "FIRE acts on EARTH (먹임, feed): burnt black tongue ends WRAP around the corners of low stair-stepped ochre slabs. Result: a new EARTH body in a clod/soil form (low ochre slabs with dry hollow gaps), owner = the caster.",
 9: "FIRE acts on METAL (달군 쇠, heated iron): the blade tips are blunted, and ONLY the blunt tip carries heat colour #C75436 (heat area under 15 % of the shape, matte, no glow); a damage contact outline of short blunt blades; owner = the caster.",
 10: "FIRE acts on WATER (꺼짐, put out): saw-tooth tongues fold into FLAT cut-off ends; the hollow water currents that remain are only HALF of what they were; no steam, no product; same owner.",
 11: "EARTH acts on WOOD (걸림, snag): the stair-stepped slab ends are jammed in the fork of a branching wood bundle and STOP there; the wood remaining is half; same owner.",
 12: "EARTH acts on FIRE (옮겨붙음, carry over): the fire form dies down to a few low tongues and low slabs pile up over it. Result: an EARTH body in the fire form (stair-stepped ochre slabs shaped like a tongue outline), owner = the caster.",
 13: "EARTH acts on EARTH (비화): the section of an existing slab shows ONE extra stair step added; thicker by one step; no new product.",
 14: "EARTH acts on METAL (먹임, feed): earth layer ends stick onto a cut face of pale-grey blades. Result: a METAL body in a blade form (straight cut ends, short transverse breaks), owner = the caster.",
 15: "EARTH acts on WATER (진창, mud): an earth slab and hollow slate-blue current strokes INTERLOCK; the contact outline is a slowing sticky one made of OPEN earth strokes and OPEN water strokes with dry rims - hollow, NOT a filled mud pool. Owner = the caster.",
 16: "METAL acts on WOOD (가시, thorn): fibres are cut into LOW cut thorns (pale grey cut tips, short, no taller than 0.4 m); damage once, no repeated pulsing; owner = the caster.",
 17: "METAL acts on FIRE (무뎌짐, dulled): a straight blade tip is worn to a short obtuse end; the fire form that remains is only HALF; same owner.",
 18: "METAL acts on EARTH (옮겨붙음, carry over): a blade sits in the cracks of a stair-stepped slab. Result: a METAL body in a soil form (cut-ended blades lying in slab-shaped bundle), owner = the caster.",
 19: "METAL acts on METAL (비화): one more parallel cut face layer beside an existing blade bundle, thicker by one layer; no new product.",
 20: "METAL acts on WATER (먹임, feed): the hollow water current is EXTENDED along the cut end of a blade. Result: a WATER body in a blade form (slate-blue current strokes with cut ends), owner = the caster.",
 21: "WATER acts on WOOD (먹임, feed): the water current is absorbed ALONG the wood fibres. Result: a WOOD body in a vine form (long fibres with a damp slate-blue base), owner = the caster.",
 22: "WATER acts on FIRE (김, steam): the burnt tongue form splits into wet dark islets; steam as a few low torn sheets of dark grey-brown dry-brush ink drifting in the water's direction, centre open, at most knee-to-chest high (1.2 m), base blue-grey #3F5866 and DARKER than the ground, tips only slightly lighter than the ground, never white, no ring, no wall; a 'wet broken island' look.",
 23: "WATER acts on EARTH (막힘, blocked): the water hooks FOLD SIDEWAYS in front of a low earth bank; the water action is used up; the earth bank that remains is half; no product; same owner.",
 24: "WATER acts on METAL (옮겨붙음, carry over): the current FILLS the seams between metal blades. Result: a WATER body in a blade form (slate-blue current strokes with cut ends), owner = the caster.",
}

def react_body(nums, layout='2x2'):
    lines = [REACT_INTRO, '', LAYOUT[layout]]
    for i, n in enumerate(nums, 1):
        lines.append(f'Panel {i}: ' + R[n])
    return '\n'.join(lines)

# ----------------------------------------------------------------------------------------------------------------- sheet definitions
SHEETS = {}

def sheet(sid, title, parts, na, special, plan_lines, extra=''):
    SHEETS[sid] = dict(title=title, parts=parts, na=na, special=special, plan_lines=plan_lines, extra=extra)

COMMON_REFS = ['MARKS', 'BODIES2']

# ---- 6a / 6b / 6c / 6d : reactions
sheet('6a', 'reactions 1-8', [
    ('p1', ['MARKS', 'BODIES2', 'RX_FIRE', 'JEOKRO'], react_body([1, 2, 3, 4])),
    ('p2', ['MARKS', 'BODIES2', 'RX_FIRE', 'JEOKRO'], react_body([5, 6, 7, 8])),
], na='The picture has no frame-and-verb test of the usual kind: score 틀·동사 형태 on the CONTACT SILHOUETTE named in each panel; 극치·반응 구조 is the reaction structure (consume, result body, form, owner, product count).',
   special='결과 몸 · 접촉 꼴 · 소유 (result body, contact silhouette, ownership) for reactions 1-8 in the order of the plan table.',
   plan_lines=[(524, 530), (531, 538), (554, 567)], extra='REACT')
sheet('6b', 'reactions 9-16', [
    ('p1', ['MARKS', 'BODIES2', 'RX_STEAM', 'JEOKRO'], react_body([9, 10, 11, 12])),
    ('p2', ['MARKS', 'BODIES2', 'RX_MUD', 'JEOKRO'], react_body([13, 14, 15, 16])),
], na='Same as sheet 6a.', special='결과 몸 · 접촉 꼴 · 소유 for reactions 9-16.', plan_lines=[(524, 530), (531, 548), (554, 567)], extra='REACT')
sheet('6c', 'reactions 17-24', [
    ('p1', ['MARKS', 'BODIES2', 'RX_FIRE', 'JEOKRO'], react_body([17, 18, 19, 20])),
    ('p2', ['MARKS', 'BODIES2', 'RX_STEAM', 'JEOKRO'], react_body([21, 22, 23, 24])),
], na='Same as sheet 6a.', special='결과 몸 · 접촉 꼴 · 소유 for reactions 17-24.', plan_lines=[(524, 530), (544, 553), (554, 567), (593, 609)], extra='REACT')
sheet('6d', 'reaction 25, child consumed, takeover, adjacency comparison', [
    ('p1', ['MARKS', 'BODIES2', 'RX_FIRE', 'JEOKRO'], REACT_INTRO + """

""" + LAYOUT['2x2'] + """
Panel 1: WATER acts on WATER (reaction 25, 비화): an existing hollow slate-blue current of dry thin parallel strokes; ONE extra parallel stroke has been added INSIDE the existing current, so it is one layer thicker; nothing new born, nothing bursts, same owner, same footprint.
Panel 2: CHILD CONSUMED (자식 소비): a small FIRE-form child (born earlier by a feeding reaction, drawn a little paler and smaller than the others) is touched again by a feeding contact. It is used up and DRIES IN PLACE: the same footprint turned to pale dry grey-brown ash lines, no direction of motion, no flying particles, no new product, no growth, nothing left standing; a bare dry outline of where it was.
Panel 3: TAKEOVER (탈취): one patch of ground shown as TWO MOMENTS split by a clean vertical seam (left half 0.1 s before the commit, right half the commit frame). Left half: the old owner is an ENEMY wet rimless filled matte patch (wet red-brown), and a thin broken dry hint line marks the place where it is about to be lost. Right half: the same patch now belongs to the CASTER: hollow dry-rimmed bundles of thin strokes in the same place. No place shows both owners at once; no double outline.
Panel 4: ADJACENT CONTACT COMPARISON (연접 비교): three contacts of the SAME acting body (wood fibres) side by side in three vertical thirds, each on its own neighbour material: left = same-body spread (wood onto wood: the wood bundle one layer thicker); middle = feed (wood onto fire: a new FIRE tongue form born, saw-tooth black tongues); right = cut (wood onto metal: fibres cut off straight, metal remainder half). The three result silhouettes must differ at a glance."""),
], na='Same as sheet 6a.', special='산물 · 소유 전환 (product, ownership switch, no grandchildren): panel 1 reaction 25, panel 2 child consumed, panel 3 takeover, panel 4 adjacency comparison.',
   plan_lines=[(471, 472), (512, 523), (524, 530), (548, 567)], extra='REACT')

# ---- 7a / 7b / 7g / 7h : five bodies on a ground
def five_bodies(ground, tone, bg_note):
    return f"""--- THE PICTURE: the FIVE BODIES on {ground} ---
{LAYOUT['5strip']} Shoulder camera (third person, behind and slightly above the caster's right shoulder, the caster at the lower left in every panel, same pose), one gaunt matte enemy 7 m ahead, the same narrow straight path stroke (30 cm wide) hits the enemy and leaves a 1.5 m mark at its feet. ONLY the body differs. {bg_note}
{tone}
Panel 1 WOOD: long torn fibre ends fraying forward; panel 2 FIRE: burnt voids and saw-tooth black tongue ends; panel 3 EARTH: low overlapping stair-stepped slabs; panel 4 METAL: straight cut ends and short transverse breaks; panel 5 WATER: open hooks and hollow parallel currents. Same size, same place, same hollow dry-rimmed grammar, no glow."""

TONE_DARK = "Reading: the dark reading-ink layer and the lighter dry pigment layer both stay visible along most of each outline; the pigment layer carries the body colour clearly against this ground."
TONE_MID = "The ground here is MID-LIGHTNESS (L* about 55), so the pigment colours sit close to the ground. The body must therefore be told apart by the dark reading-ink layer's FORM alone (fibre ends, saw-tooth voids, stair slabs, straight cuts, open hooks), with the pigment layer only a weak tint of the same stroke. The ink layer must be clearly darker than the ground along the whole outline."

sheet('7a', 'five bodies on 적로', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'BODIES2'], five_bodies('the 적로 red-earth valley', TONE_DARK,
        'Ground: dark brick-red cracked earth (about #863D2C, Lab 35,30,25) - FIRE (#BE543B) is the near-colour of the ground, so FIRE must rely on both layers: the black burnt voids and saw-tooth ends plus a clearly lighter dry coral-red edge (about #DF7155).')),
], na='No verb-frame or reaction structure is tested here: score 틀·동사 형태 and 극치·반응 구조 by absence of contradiction (full marks unless the picture breaks the plan on that axis).',
   special='근접 색상 · 두 명도층 (close-colour separation on 적로: 화 against the red earth, two lightness layers coexisting, eye-level colour separation only; exact dE00 is an engine-stage measurement).',
   plan_lines=[(257, 316), (338, 350)])
sheet('7b', 'five bodies on 청림', [
    ('p1', ['SHOULDER', 'BRUSH', 'CHEONG', 'BODIES2'], five_bodies('the 청림 green forest', TONE_DARK,
        'Ground: dark damp forest floor with moss and leaf litter, shaded green-black (about #1C4324, Lab 25,-22,15), a few dark tree trunks far behind. WOOD (#368B75) is the near-colour of the ground, so WOOD must rely on both layers: the dark reading-ink fibre ends plus a clearly lighter dry pale-green edge (about #4B9F88).')),
], na='Same as sheet 7a.', special='근접 색상 · 두 명도층 on 청림 (목 against the green forest).', plan_lines=[(257, 316), (338, 350)])
sheet('7g', 'five bodies, mid-lightness 적로', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'BODIES2'], five_bodies('a MID-LIGHTNESS 적로 ground', TONE_MID,
        'Ground: lighter dusty pinkish red earth (about #BF6E5A, Lab 55,30,25) under an overcast sky, no deep shadow.')),
], na='Same as sheet 7a.', special='먹층만으로 몸 구별 (bodies told apart by the reading-ink layer alone on mid-lightness 적로).', plan_lines=[(257, 316), (338, 350)])
sheet('7h', 'five bodies, mid-lightness 청림', [
    ('p1', ['SHOULDER', 'BRUSH', 'CHEONG', 'BODIES2'], five_bodies('a MID-LIGHTNESS 청림 ground', TONE_MID,
        'Ground: lighter sage-green moss and grass (about #648D69, Lab 55,-22,15) under a soft grey-green sky, no deep shadow.')),
], na='Same as sheet 7a.', special='먹층만으로 몸 구별 on mid-lightness 청림.', plan_lines=[(257, 316), (338, 350)])

# ---- 7c : no-support / ownership / tells / enemy material warning / zone
sheet('7c', 'no-support four frames, ownership, tells, material warning, zone', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'FRAMES2'], f"""--- THE PICTURE: the four frames of a ONE-SHOT spell (무받침, 한 번) at the first 0.08 s, nothing left behind ---
{LAYOUT['4strip']} Shoulder camera, the caster at the lower left, red-earth ground, one gaunt enemy 7 m ahead in each panel; same body in all four: FIRE (black torn tongues, brick-red edge only, hollow dry bundles).
Panel 1 PATH: a thin straight lane (30 cm wide) from the brush tip to the enemy, touching only the enemy. Behind the touch point the ground is CLEAN: no mark remains.
Panel 2 ATTACH: a short hollow stroke with a broken dry rim wrapped only on the caster's own brush shaft and right sleeve (under 0.45 m long); nothing reaches the enemy, the stroke is empty inside; the ground is clean.
Panel 3 FAN: a spread of short sparse dry strokes in a 4 m wide, 5 m deep fan from the caster; the ground between strokes shows; the ground after the fan edge is clean.
Panel 4 BAND: a hollow ring band 0.6 m wide and 4 m across around the caster of thin dry strokes, empty centre, empty outside; no residue anywhere."""),
    ('p2', ['SHOULDER', 'BRUSH', 'JEOKRO', 'TELLS', 'BANDIT'], f"""--- THE PICTURE: ownership and what enemies show, at the first 0.08 s (same FIRE body colour everywhere) ---
{LAYOUT['2x2pairs']} Camera: third person from behind the caster's right shoulder, red-earth ground, enemies about 6 m ahead.
Panel 1 (OWN vs ENEMY ownership): A = the caster's fire stroke on the ground: thin dry hollow bundle with a broken dry rim, ground visible between the strokes. B = an enemy's fire patch on the ground: rimless, wet, solid-filled black-red matte surface with darker/lighter wash inside, NO dry rim, no hollow.
Panel 2 (MY ATTACH vs ENEMY WEAPON TELL): A = the caster's attach mark on the brush shaft and sleeve at the lower frame: hollow stroke with a broken dry rim, empty inside, under 0.45 m. B = an enemy soldier holding a spear: a FILLED WET directional stroke fixed to the spear shaft and tip pointing along the thrust direction, rimless, no dry rim, no hollow, attached to the weapon.
Panel 3 (ENEMY ATTACK TELL vs ENEMY ZONE): A = the enemy soldier's weapon with its FILLED WET tell stroke attached to the weapon and pointing toward the caster; the ground is clean. B = an enemy FILLED WET surface lying on the ground about 3 m across at the enemy's feet, not touching the weapon, with a wet dark edge-wash that thins at the edge but no dry rim; weapon plain.
Panel 4 (ENEMY MATERIAL WARNING vs ENEMY ZONE - pair 24): A = a thin FILLED WET link that runs from the enemy's hand/foot along the air and ground to a chosen piece of ground material (a low pile of dark vine/branch material on the ground) and TOUCHES it at the link's end; the ground around is not wet. B = the zone already active: the SAME wet colour as A, now a filled wet surface lying on the ground (about 3 m across) around that material, with no link to the enemy. The colour of A and B is identical, only the shape differs (link ending on the material vs surface on the ground).""")
], na='Verb forms and reaction structure are tested only through the one-shot frames and the pair differences: score 극치·반응 구조 by absence of contradiction.',
   special='잔존 없음 (panels 1-4 of the first image) · 빈 획 / 젖은 면 (own hollow dry vs enemy wet filled) · 재료 연결 끝 / 장판 지속 범위 (the second image panel 4 = pair 24); the second image panel 2 = pair 17, panel 3 = pair 21; panels 3 and 4 together also stand for pair 25 (attack tell attached to body/weapon vs material warning link ending on the ground).',
   plan_lines=[(461, 500), (484, 497), (1394, 1396), (1503, 1520)],
   extra='Layout: the sheet is split into TWO images (image 1 = panels 1-4 one-shot frames, image 2 = panels 5-8 as a 2x2 of A/B pairs: ownership, my attach vs enemy tell, enemy tell vs zone, material warning vs zone). Judge both images together; the plan sheet lists them as panels 1-8.')

# ---- 7e / 7f : horizontal view R0-R5
def horiz(ground, body, body_desc, ref_ground):
    return f"""--- THE PICTURE: R0 to R5, the six band verbs seen from FIRST PERSON LEVEL EYE HEIGHT (horizontal view) ---
{LAYOUT['3x2']} Eye height 1.6 m, looking straight ahead at a level horizon (not tilted down), the great brush visible only as its tip at the lower right (keep the hand and brush small, as in picture 1; no interface). Ground: {ground}. The caster stands at the centre of a 2 m radius, 0.6 m wide hollow ground band (the band itself is below the frame edge and not visible); only its TWO UPRIGHT SIDE STROKES are visible: two thin vertical strokes rising from the band, one at about 20 degrees left and one at about 20 degrees right of the view centre, about 2.3 m away, each 1.0 to 1.2 m tall (about a third of the frame height), only about 6 px thin lines, with the CENTRE between them completely empty (no fill, no bridge, no wall connecting their tops). Body = {body}: {body_desc} Both strokes are hollow dry-rimmed ink, ground visible through and between them. The verb is read at the TOP of the strokes:
Panel 1 R0 (한 번, one shot): two thin plain strokes with no branching, no knots, nothing else.
Panel 2 R1 (남긴다, leave): the top of each stroke truly FORKS into a few real branches (a short upward branching).
Panel 3 R2 (번진다, spread): the top of each stroke SPLAYS OPEN outward like a bundle of opened strands, an open pair whose tops lean away from each other; open ends, and flat strokes lying along the ground at their feet connect to a flat spreading bundle.
Panel 4 R3 (심는다, plant): each stroke is a single plain stem ending in an isolated rounded DOT head, no branching.
Panel 5 R4 (가른다, split): the two strokes are a FACING PAIR OF LIPS (like the dry lips of a cut), leaning toward each other without touching, and below them a narrow engraved groove runs between their feet; no blade wall.
Panel 6 R5 (돌아온다, return): each stroke is the vertical side of a THIN CLOSED LOOP (a slim closed oval of thin line about 1 m tall, hollow inside, the inside clearly closed), standing at the band edge.
Same size, same place, same camera and ground in all six panels; the two strokes in every panel are one pair, not two separate seeds."""

sheet('7e', 'horizontal R0-R5 on 적로 (fire)', [
    ('p1', ['FIRSTP', 'JEOKRO', 'BODIES2', 'FRAMES2'], horiz('dark brick-red cracked red earth of 적로 with a dim hazy ridge far away', 'FIRE (the near-colour of this ground)',
         'black torn tongue ends with burnt voids and saw-tooth tips, dry brick-red edge (#DF7155 layer) over the black reading-ink layer.', 'JEOKRO')),
], na='No body-colour or reaction test: score 몸·소유 구별 on the dry hollow grammar and the fire cue; 극치·반응 구조 by absence of contradiction. Hand occlusion is an engine measurement.',
   special='세운 획 · 동사 구별 (six verbs readable from the top of the two upright strokes at horizontal eye level, centre empty, no wall).',
   plan_lines=[(825, 855), (875, 884)])
sheet('7f', 'horizontal R0-R5 on 청림 (wood)', [
    ('p1', ['FIRSTP', 'CHEONG', 'BODIES2', 'FRAMES2'], horiz('dark damp forest floor of 청림 (moss, leaf litter, shaded green-black) with dark tree trunks far behind', 'WOOD (the near-colour of this ground)',
         'long torn fibre ends and fraying, dark reading-ink fibres with a lighter dry pale-green edge (#4B9F88 layer).', 'CHEONG')),
], na='Same as sheet 7e.', special='세운 획 · 동사 구별 on 청림 (same criteria as 7e).', plan_lines=[(825, 855), (875, 884)])

# ---- 7n : three simultaneous enemy tells (composition only)
def tells3(ground, body, desc):
    return f"""--- THE PICTURE: THREE enemies start an attack at the same moment (composition only) ---
ONE wide 16:9 picture divided into a 3 x 2 grid of six equal panels (columns = camera pitch: left 0 degrees level, middle 15 degrees looking down, right 30 degrees looking down; the top row panels 1-3 on the red-earth 적로 ground, the bottom row panels 4-6 on the 청림 dark forest floor). Third-person shoulder camera behind and above the caster's right shoulder (the caster's shoulder and the brush at the lower left corner, small, never hidden), ground: top row dark brick-red earth, bottom row dark damp green-black forest floor. In EVERY panel the same three gaunt armoured soldier enemies stand 5 to 8 m ahead in a loose triangle, each at least 70 % visible, each with a FILLED WET directional tell stroke fixed on its OWN weapon or body, rimless, no dry rim, no hollow, no glow, body colour: top row FIRE (matte wet red-brown, #B0382A, never bright), bottom row WOOD (matte wet green #29793F):
Enemy A (left): a spear thrust - a filled wet stroke along the spear shaft and tip aimed straight at the caster.
Enemy B (centre, a little farther): an overhead axe/blade chop - a filled wet arc stroke fixed on the raised blade curving down over the enemy's head toward the caster.
Enemy C (right): a low sweep - a filled wet horizontal arc stroke fixed on the weapon at knee height swinging across the ground level toward the caster.
The three tells must read as three different attackers, three different directions (forward line, vertical arc, low sweep), not as one shape; nothing connects the three tells; no tell lies on the ground; ground clear of the caster's own magic."""

sheet('7n', 'three enemy tells x 2 grounds x 3 pitches', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'CHEONG', 'TELLS', 'BANDIT'], tells3('', '', '')),
], na='Composition only: tell shape, body colour, ink look and enemy visibility. Score 틀·동사 형태, 극치·반응 구조 by absence of contradiction (full marks unless broken). Per-pitch projection and visibility percentages are engine measurements.',
   special='세 공격의 독립 판독 (three attackers, three attached wet tells with different directions readable independently in every panel).',
   plan_lines=[(461, 500), (484, 497), (1398, 1398), (1475, 1475)])

# ---- 10a / 10b / 10c / 10d : confusion pairs
PAIR_INTRO = """--- THE PICTURE: confusion pairs, each panel compares A (left half) and B (right half) at the same size, same ground (red earth, third-person view from behind the caster's right shoulder, about 4 m ahead), same body unless stated. The two halves must be told apart at a glance by SHAPE alone (colour does not decide); the caster's own work is thin hollow dry-rimmed strokes, the enemy's is a rimless wet filled surface. Nothing taller than 1.2 m; no glow."""

PAIRS = {
 1: "ㅗ vs ㅜ. A = FAN (ㅗ): many short strokes radiating from the caster's position outward and ending in a far arc, wedge shaped; the caster's feet at the wedge's point. B = HOLLOW BAND (ㅜ): a 0.6 m wide band of thin arcs running tangentially around the caster at 2 m radius, the middle EMPTY, no radiating strokes.",
 2: "LEAVE vs PLANT. A = LEAVE (남긴다): a mark whose top FORKS into branches (wood body, low branching). B = PLANT (심는다): one isolated short vertical post with a dot head, NO branching, a small dry ground hole at its foot.",
 3: "SPREAD vs SPLIT. A = SPREAD (번진다): a flat bundle of many strokes spreading outward with open ends and several branches (fire body, flat on the ground). B = SPLIT (가른다): a single engraved crack in the ground with two dry lips (a pair of cut faces) on either side of the line, no bundle.",
 4: "SPLIT vs RETURN. A = SPLIT (가른다): an OPEN cut line with its two dry lips, ends open. B = RETURN (돌아온다): a small thin CLOSED curve about 0.8 m across, closed inside (a tiny ring made of a thin stroke), nothing open.",
 5: "SPREAD-BACK (논) vs RETURN. A = 논 re-spread: a fan-edge bundle whose strokes LINK BACK along themselves to the first bundle (a reversed connection: forward spread and a return link joined along the same strokes), open at the far ends. B = RETURN (돌아온다): a small thin CLOSED curve head, a ring closed in itself.",
 6: "걱 vs A1. A = 걱 (self-recovery): a few thin dry strokes on the caster's own forearm/sleeve FOLDED INWARD, curling toward the body, nothing stands outward, nothing goes away from the body. B = A1 attach: the same few thin strokes standing OUTWARD from the brush shaft/sleeve as an upright branch pointing away from the body. Close shot of the caster's right forearm and the brush, wood body.",
 7: "MY WATER vs ENEMY ZONE. A = the caster's water on the ground: hollow parallel slate-blue current strokes with a dry broken rim, ground showing between. B = an enemy water zone: a rimless WET FILLED slate-blue surface lying on the ground, wash inside, no dry rim, no hollow.",
 8: "MY MUD vs ENEMY ZONE. A = the caster's mud (earth breaks water): OPEN ochre earth strokes and OPEN slate-blue water strokes with dry rims, interlocked, not filled, ground showing through. B = an enemy zone: a rimless wet FILLED dark surface of the same size, no dry rim.",
 9: "MY STEAM vs ENEMY STEAM/DANGER SURFACE. A = the caster's steam (water breaks fire): a few low torn sheets of dark grey-brown dry-brush ink with a dry base, centre open, at most 1.2 m, the base blue-grey and darker than the ground, never white. B = an enemy steam/danger surface: a WET FILLED rimless dark grey-brown surface lying on the ground, no dry base, no torn sheets, no rim.",
 10: "GIANT vs ENEMY. A = the caster's earth giant (몸, 1.3 m high at most): a figure drawn as hollow stair-stepped ochre slabs with an EMPTY torso and a dry broken rim, with a thin dry strand of ink still connecting it to the caster's brush. B = an enemy soldier: a SOLID matte wet-dark filled silhouette, no hollow body, no connection to the caster.",
 11: "HEATED IRON vs METAL BODY END. A = heated iron (달군 쇠): pale-grey blades whose tips are BLUNT and only the blunt tip has a dull heat colour #C75436 (under 15 % of the shape, matte). B = a metal body's end: pale-grey blades with STRAIGHT SHARP CUT ENDS and short transverse breaks, no heat colour.",
 12: "MY ENDING vs ENEMY TELL. A = the caster's own long-lived mark in its last 0.4 s: the dry strokes on its OWN OUTLINE are worn and dry-eroded, the mark thinning, rim crumbling, still lying where it was (the valid mark about to be lost). B = an enemy soldier with a FILLED WET directional tell fixed on the enemy's weapon, the ground clean.",
 13: "METAL BODY END vs SPLIT LIPS. A = a metal body's end: a pale-grey stroke that has a LOCAL short transverse break at one place (a straight cut across), the break is only on the stroke. B = the lips of a split (가른다): a line with a matched PAIR of lips, one on each side of the line, mirror image, dry rims.",
 14: "WATER HOOK vs RETURN LOOP. A = water body end: a slate-blue current ending in an OPEN HOOK (open end, inside open to the ground). B = the return loop (돌아온다): a thin slate-blue CLOSED loop about 0.8 m across, closed inside.",
 15: "METAL CLIMAX vs FIRE SPREAD CLIMAX. A = a metal climax (삿): engraved cut faces - parallel straight cut surfaces running to up to three points, flat cut faces. B = a fire spread climax (난): a flat bundle of three equally dense branches from one landing point, a branching bundle.",
 16: "EARTH STANDBY DOT vs ENEMY MARK. A = the caster's earth standby dot: a dry hollow stair-stepped ochre mark with a dry rim on a target's foot. B = an enemy mark of the same size and place: a WET FILLED rimless dark mark.",
 17: "MY ATTACH vs ENEMY WEAPON TELL at the first 0.08 s (pair 17). A = the caster's attach on the brush shaft and sleeve: an EMPTY hollow stroke with a broken dry rim, under 0.45 m. B = an enemy spearman's tell: a FILLED WET stroke on the spear, no dry rim.",
 18: "EARTH SUPPORT PLATE vs NATURAL TERRAIN (pair 18). A = the caster's earth support plate (뭄) as a low ledge of stair-stepped ochre slabs, owner rim (dry broken rim all along its edge), visible plate thickness (side face seen), a valid-mark ink line on top. B = a natural ledge of the red-earth terrain of the same size and place, no ink rim, no plate thickness layers, no ink line.",
 19: "STRIPPING vs LOAM GROUND CRACK (pair 19). A = stripping (벗김): at the contact of an enemy's armour plate a SHORT PAIR of cut faces (two matched short lips with dry pigment) fixed at the armour seam. B = a ground crack in yellow-brown loam: a long irregular crack in the soil that has no matched lip pair and is not at an armour.",
 21: "ENEMY ATTACK TELL vs ENEMY ZONE (pair 21). A = an enemy soldier whose weapon carries a FILLED WET directional stroke attached to the weapon, pointing the attack direction; ground clean. B = a filled wet surface on the ground about 3 m across at the enemy's feet, not touching the weapon, weapon plain. SAME wet colour in A and B.",
 22: "MY REACTION PREVIEW vs ENEMY MATERIAL WARNING (pair 22). A = the caster's reaction preview (0.3 s before contact): an OPEN dry stroke with a dry broken rim whose end just reaches a piece of ground material (a dry vine bundle), hollow, thin. B = an enemy material warning: a thin FILLED WET link running from the enemy to the same kind of material and touching it, rimless.",
}

def pair_panels(nums, extra=None):
    out = [PAIR_INTRO, '', LAYOUT['2x2pairs']]
    for i, n in enumerate(nums, 1):
        out.append(f'Panel {i}: ' + PAIRS[n])
    return '\n'.join(out)

sheet('10a', 'confusion pairs 1-8', [
    ('p1', ['MARKS', 'BODIES2', 'RX_MUD', 'SHOULDER'], pair_panels([1, 2, 3, 4])),
    ('p2', ['MARKS', 'BODIES2', 'RX_MUD', 'SHOULDER', 'BRUSH'], pair_panels([5, 6, 7, 8])),
], na='No ownership-wide or reaction structure test beyond the pairs: 극치·반응 구조 by absence of contradiction.',
   special='쌍별 정지 구별 (each of pairs 1-8 told apart by shape at a glance, A vs B halves).',
   plan_lines=[(1483, 1496), (461, 497)],
   extra='Layout: the sheet is split into TWO images (image 1 = pairs 1-4, image 2 = pairs 5-8); panel n of an image is pair number as written in the panel list.')
sheet('10b', 'confusion pairs 9-16', [
    ('p1', ['MARKS', 'BODIES2', 'RX_STEAM', 'SHOULDER', 'BANDIT'], pair_panels([9, 10, 11, 12])),
    ('p2', ['MARKS', 'BODIES2', 'RX_FIRE', 'SHOULDER', 'BANDIT'], pair_panels([13, 14, 15, 16])),
], na='Same as sheet 10a.', special='쌍별 정지 구별 for pairs 9-16.', plan_lines=[(1497, 1503), (461, 497), (1524, 1535)],
   extra='Layout: split into TWO images (image 1 = pairs 9-12, image 2 = pairs 13-16).')

sheet('10c', 'pairs 17, 18, 19, 21, 22 + marker pair 23 + two coexistence panels', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'TELLS', 'BANDIT'], pair_panels([17, 18, 19, 21])),
    ('p2', ['SHOULDER', 'BRUSH', 'JEOKRO', 'TELLS', 'BANDIT', 'MARKS'], PAIR_INTRO + "\n\n" + LAYOUT['2x2'] + """
Panel 1 (pair 22): """ + PAIRS[22] + """ Shown as left half A and right half B inside the panel (thin vertical seam).
Panel 2 (marker pair 23, three states of the SAME outline in three vertical thirds): the same hollow dry-rimmed wood-brown standing mark (a small stand of dry branches, about 1 m wide) in three states. Left third = MINIMIZED form (최소형): same outline and same position, but its strokes are thinner and fewer; the valid mark is clearly still there, still hollow. Middle third = CONSUMED (소): the mark has been used up and FOLDS into its contact point (the strokes collapsing toward the one point where it touched, nothing else left standing). Right third = CANCELLED (피, hit cancelled): the stroke being drawn STOPPED mid-way - the half-finished strokes stop abruptly with a short faint afterimage, not folding, not thinning.
Panel 3 (coexistence a: giant + 뭄 standing, 멈 shows low): the caster's earth giant (1.3 m high, hollow stair slabs with an empty torso) stands on the left and the caster's 뭄 support plate (a low raised ledge of stair slabs with dry rim) on the right; between them the caster's 멈 appears ONLY as a small dry sleeve-point at the caster's cuff plus a low dry ground-contact stroke on the earth - NO third tall slab; the two tall earth structures are the only high objects.
Panel 4 (coexistence b: same-frame ownership swap + enemy child, no double ownership): a patch of ground at the instant of takeover: the left part is the enemy's old wet filled surface, the right part has just become the caster's hollow dry-rimmed bundle, and a newly born small enemy child (a small wet filled rimless surface of the same body) lies at the seam on the enemy side only; every area has exactly ONE owner style, never both."""),
], na='Moving checks (motion of the three states, pair 23 shrink/return) are not judged here; score the still silhouettes.',
   special='전조/장판 (pair 21) · 예보/예고 (pair 22) · 최소형 / 소비 / 피격 취소 (pair 23, three states of one outline) · 구조 공존 2 (giant+뭄 with 멈 low; same-frame ownership swap + enemy child).',
   plan_lines=[(1503, 1523), (432, 437), (512, 523), (1396, 1396), (1516, 1516)],
   extra="""Layout: split into TWO images. Image 1 = panels 1-4 = pairs 17, 18, 19, 21. Image 2 = panels 5-8 = pair 22, marker pair 23 (three states), coexistence a (giant+뭄 standing, 멈 as sleeve-point + low contact stroke, D-1), coexistence b (same-frame ownership swap + enemy child). Interpretation: the plan's phrase 'structure coexistence 2' is not defined in the plan text; these two cells come from the 'key 16 cells' (giant+뭄 멈 / ownership swap + enemy child in the same frame). Judge both images together.""")

sheet('10d', 'spread climaxes 난 넌 논 눈', [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'FRAMES2', 'MARKS'], f"""--- THE PICTURE: the four SPREAD climaxes (번진다 극치), the same FIRE body (black torn tongues with a dry brick-red edge, hollow dry bundles, flat on the ground), same camera and ground ---
{LAYOUT['2x2']} Shoulder camera, the caster at the left of each panel, red-earth ground, one or two enemies 6-8 m ahead.
Panel 1 난 (path frame): a narrow straight path lane to the landing point; at the landing point THREE branches of the SAME density and width fan out from the one point (three equal flat strokes), nothing else.
Panel 2 넌 (attach frame): the spread rides on the caster's own brush shaft and sleeve as a BOUND BUNDLE of dry strokes (under 0.45 m, hollow), and a contact exit stroke leaves it only at the point where the brush touches the enemy; no ground fan.
Panel 3 논 (fan frame): a flat fan bundle in front of the caster whose BOUNDARY has an open re-spread connection: strokes at the fan's far edge link back toward the first bundle (spread, then spread back), open ends; no small closed loop, no second fan.
Panel 4 눈 (band frame): a hollow dry band around the caster (0.6 m wide, 2 m radius, empty centre) and a pre-existing dark vine/branch material lying ALONG the band; the burnt black strokes are attached ONLY to that wood material and run along it outward (burnt connection following the fuel), no fire rim, no tongue lying alone on the ground.
The four panels must show four clearly different function cues: three-way split / carried by the sleeve / spread then re-spread / burning along the fuel."""),
], na='Spread-climax cues only: score 틀·동사 형태 as the frame-and-verb of each panel; 극치 as the unique cue of each letter.',
   special='고유 단서 · 기능 차이 (난 same-density three branches from one point / 넌 sleeve-bound bundle with contact exit / 논 re-spread connection at the fan boundary / 눈 burnt connection along wood fuel). The six pair comparisons (난-넌, 난-논, 난-눈, 넌-논, 넌-눈, 논-눈) are judged by comparing the four panels (same camera, body, ground).',
   plan_lines=[(1524, 1535), (396, 401), (451, 459)],
   extra='Layout note: the plan lists 6 panels (one per pair); here the 4 letters are drawn once each (4 panels) and the 6 pair contrasts are read between them, because six pair panels would repeat the same four drawings.')


# ----------------------------------------------------------------------------------------------------------------- simplified versions (v4)
LAYOUT['2side'] = "ONE wide 16:9 picture of TWO large equal panels side by side (panel 1 left, panel 2 right), a thin paper-coloured gap, identical camera and ground in both panels."
LAYOUT['2sidepairs'] = "ONE wide 16:9 picture of TWO large equal panels side by side (panel 1 left, panel 2 right). EVERY panel is split by a thin vertical paper-coloured seam into a LEFT half (A) and a RIGHT half (B) showing the two things being compared on the SAME ground, same camera, same distance, same size."
DRAW_LESS = """
SIMPLER, BIGGER DRAWING (this is the simplified version of a crowded picture that kept failing): each panel is large, so draw FEW and BIG strokes and leave a lot of bare ground: EARTH = three nested stepped OUTLINE lines only (dry ochre line, ground visible inside, no side faces, no thickness); METAL = four parallel hairline pairs (thin straight needles with ground between them, no bars); WATER = five thin parallel broken lines with one or two open hooks; WOOD = a handful of long thin torn fibres; FIRE = a few black torn tongues with a thin brick-red edge. Nothing in any panel is filled."""
SIMPLE_NOTE.update({s: 'SIMPLIFIED version 4: the same panels as the plan sheet, redrawn as several images of two large panels each (panel numbers continue in order across the images: image 1 = panels of the first two cells, image 2 = the next two, ...). Judge all images together as one sheet.' for s in ['6a', '6b', '6c', '6d', '10a', '10b', '10c']})
SIMPLE_NOTE.update({s: 'SIMPLIFIED version 4: still the five bodies, but each panel is a close crop of ONLY the 1.5 m mark at the enemy\'s feet (no long path lane, no caster body; the enemy\'s legs and the lower part of the enemy visible above the mark).' for s in ['7a', '7b', '7g', '7h']})

def _react_parts(sid, groups, refs):
    parts = []
    for i, g in enumerate(groups, 1):
        parts.append((f'p{i}', refs, react_body(g, '2side') + DRAW_LESS))
    return parts

SIMPLE['6a'] = _react_parts('6a', [[1, 2], [3, 4], [5, 6], [7, 8]], ['MARKS', 'BODIES2', 'JEOKRO'])
SIMPLE['6b'] = _react_parts('6b', [[9, 10], [11, 12], [13, 14], [15, 16]], ['MARKS', 'BODIES2', 'JEOKRO'])
SIMPLE['6c'] = _react_parts('6c', [[17, 18], [19, 20], [21, 22], [23, 24]], ['MARKS', 'BODIES2', 'JEOKRO'])

_d = SHEETS['6d']['parts'][0][2]
_d1 = _d.split('Panel 3: TAKEOVER')[0]
_d2 = _d.split('Panel 3: TAKEOVER')[1]
SIMPLE['6d'] = [
    ('p1', ['MARKS', 'BODIES2', 'JEOKRO'], _d1.replace(LAYOUT['2x2'], LAYOUT['2side']) + DRAW_LESS),
    ('p2', ['MARKS', 'BODIES2', 'JEOKRO'], REACT_INTRO + '\n\n' + LAYOUT['2side'] + '\nPanel 1: TAKEOVER' + _d2.replace('Panel 4:', 'Panel 2:') + DRAW_LESS),
]

def five_simple(ground, tone, bg_note):
    return f"""--- THE PICTURE: the FIVE BODIES as close crops of the mark on {ground} ---
{LAYOUT['5strip']} Low, close camera at the enemy's feet: each panel shows ONLY the 1.5 m mark that a path stroke left at the enemy's feet (the lower legs and boots of the gaunt enemy visible at the top edge of the panel), drawn LARGE and clear, nothing else. {bg_note}
{tone}
Panel 1 WOOD: long torn fibre ends fraying forward, a handful of thin fibres; panel 2 FIRE: a few black burnt tongues with burnt voids and saw-tooth ends; panel 3 EARTH: three nested stepped contour outlines, ground visible inside, no thickness; panel 4 METAL: four parallel pairs of needle hairlines with straight cut ends and short transverse breaks; panel 5 WATER: five thin parallel broken lines with open hooks. Same size and place in all panels; all hollow, dry-rimmed, nothing filled, no glow."""

def _five_simple_part(sid):
    s = SHEETS[sid]
    body = s['parts'][0][2]
    return None

SIMPLE['7a'] = [('p1', ['JEOKRO', 'BODIES2'], five_simple('the 적로 red-earth valley', TONE_DARK, 'Ground: dark brick-red cracked earth (about #863D2C); FIRE is the near-colour of the ground, so FIRE relies on both layers: black burnt voids plus a clearly lighter dry coral-red edge (#DF7155).'))]
SIMPLE['7b'] = [('p1', ['CHEONG', 'BODIES2'], five_simple('the 청림 green forest', TONE_DARK, 'Ground: dark damp forest floor, moss and leaf litter, shaded green-black (about #1C4324); WOOD is the near-colour of the ground, so WOOD relies on both layers: dark ink fibre ends plus a clearly lighter dry pale-green edge (#4B9F88).'))]
SIMPLE['7g'] = [('p1', ['JEOKRO', 'BODIES2'], five_simple('a MID-LIGHTNESS 적로 ground', TONE_MID, 'Ground: lighter dusty pinkish red earth (about #BF6E5A) under an overcast sky.'))]
SIMPLE['7h'] = [('p1', ['CHEONG', 'BODIES2'], five_simple('a MID-LIGHTNESS 청림 ground', TONE_MID, 'Ground: lighter sage-green moss and grass (about #648D69) under a soft grey-green sky.'))]

def pair_panels2(nums):
    out = [PAIR_INTRO, '', LAYOUT['2sidepairs']]
    for i, n in enumerate(nums, 1):
        out.append(f'Panel {i}: ' + PAIRS[n])
    return '\n'.join(out) + DRAW_LESS

_PR = ['MARKS', 'BODIES2', 'SHOULDER']
SIMPLE['10a'] = [(f'p{i}', _PR, pair_panels2(g)) for i, g in enumerate([[1, 2], [3, 4], [5, 6], [7, 8]], 1)]
SIMPLE['10b'] = [(f'p{i}', _PR + ['BANDIT'], pair_panels2(g)) for i, g in enumerate([[9, 10], [11, 12], [13, 14], [15, 16]], 1)]
_c2 = SHEETS['10c']['parts'][1][2]
_c_mark = 'Panel 2 (marker pair 23' + _c2.split('Panel 2 (marker pair 23')[1].split('Panel 3 (coexistence a')[0]
_c_a = 'Panel 3 (coexistence a' + _c2.split('Panel 3 (coexistence a')[1].split('Panel 4 (coexistence b')[0]
_c_b = 'Panel 4 (coexistence b' + _c2.split('Panel 4 (coexistence b')[1]
SIMPLE['10c'] = [
    ('p1', ['SHOULDER', 'JEOKRO', 'TELLS', 'BANDIT'], pair_panels2([17, 18])),
    ('p2', ['SHOULDER', 'JEOKRO', 'TELLS', 'BANDIT'], pair_panels2([19, 21])),
    ('p3', ['SHOULDER', 'JEOKRO', 'TELLS', 'BANDIT', 'MARKS'], PAIR_INTRO + '\n\n' + LAYOUT['2sidepairs'] + '\nPanel 1 (pair 22): ' + PAIRS[22] + '\n' + _c_mark.replace('Panel 2 (marker pair 23', 'Panel 2 (marker pair 23 - here this one panel shows its three states in three vertical thirds instead of A/B halves') + DRAW_LESS),
    ('p4', ['SHOULDER', 'JEOKRO', 'MARKS'], PAIR_INTRO + '\n\n' + LAYOUT['2side'] + '\n' + _c_a.replace('Panel 3', 'Panel 1', 1) + '\n' + _c_b.replace('Panel 4', 'Panel 2', 1) + DRAW_LESS),
]

LAYOUT['3strip'] = "ONE wide 16:9 picture of THREE equal tall panels side by side (panels 1 to 3 from left to right), thin paper-coloured gaps, IDENTICAL background, horizon, ground and camera in all three panels (only the strokes differ)."

def subset_panels(text, keep, old_layout, new_layout):
    out, n = [], 0
    for ln in text.split('\n'):
        m = re.match(r'^Panel (\d+)', ln)
        if m:
            if int(m.group(1)) not in keep:
                continue
            n += 1
            ln = re.sub(r'^Panel \d+', f'Panel {n}', ln, count=1)
        out.append(ln)
    t = '\n'.join(out).replace(old_layout, new_layout).replace('all six panels', 'all three panels')
    return t

for _sid, _g, _b in [('7e', 'JEOKRO', 'FIRE'), ('7f', 'CHEONG', 'WOOD')]:
    _t = SHEETS[_sid]['parts'][0][2]
    _refs = SHEETS[_sid]['parts'][0][1]
    SIMPLE[_sid] = [('p1', _refs, subset_panels(_t, [1, 2, 3], LAYOUT['3x2'], LAYOUT['3strip'])),
                    ('p2', _refs, subset_panels(_t, [4, 5, 6], LAYOUT['3x2'], LAYOUT['3strip']))]
SIMPLE_NOTE.update({s: 'SIMPLIFIED version 4: the six panels redrawn as TWO images of three tall panels each (image 1 = panels 1-3 = R0, R1, R2; image 2 = panels 4-6 = R3, R4, R5), identical background in every panel. Judge both images together as one sheet.' for s in ['7e', '7f']})

_n = SHEETS['7n']['parts'][0][2]
_nhead, _nrest = _n.split('Enemy A (left)')[0], 'Enemy A (left)' + _n.split('Enemy A (left)')[1]
def _tells_part(row, ground_txt, colour_txt):
    head = _nhead.replace('ONE wide 16:9 picture divided into a 3 x 2 grid of six equal panels', 'ONE wide 16:9 picture of THREE equal tall panels side by side')
    head = head.split('(columns = camera pitch')[0] + f'(panels 1-3 = camera pitch 0 degrees level, 15 degrees looking down, 30 degrees looking down; ALL THREE on {ground_txt}; identical background and enemy positions in the three panels). Third-person shoulder camera behind and above the caster\'s right shoulder (shoulder and brush small at the lower left, never hidden). In EVERY panel the same three gaunt armoured soldier enemies stand 5 to 8 m ahead in a loose triangle, each at least 70 % visible, each with ONE continuous FILLED WET directional tell stroke fixed on its OWN weapon or body (rimless, no dry rim, no hollow, darker and lighter matte wash only INSIDE the stroke, no coloured outer band, no long parallel streaks), body colour {colour_txt}:\n'
    rest = _nrest.replace('Enemy C (right): a low sweep - a filled wet horizontal arc stroke fixed on the weapon at knee height swinging across the ground level toward the caster.', 'Enemy C (right): a low sweep - a filled wet horizontal arc stroke fixed on the weapon head at knee height, lifted clearly ABOVE the ground with bare ground visible beneath it, its whole tip INSIDE the panel with a margin.')
    return head + rest
SIMPLE['7n'] = [
    ('p1', ['SHOULDER', 'BRUSH', 'JEOKRO', 'TELLS', 'BANDIT'], _tells_part(0, 'the dark brick-red earth of 적로', 'FIRE (matte wet red-brown, about #B0382A, black torn tongue ends with saw-tooth tips)')),
    ('p2', ['SHOULDER', 'BRUSH', 'CHEONG', 'TELLS', 'BANDIT'], _tells_part(1, 'the dark damp green-black forest floor of 청림, kept quiet and free of twigs behind the tells', 'WOOD (matte wet green, about #29793F, long directional fibre tips and a few fraying branch ends on a solid wet core, NOT flame-shaped crescents)')),
]
SIMPLE_NOTE['7n'] = 'SIMPLIFIED version 4: the six panels redrawn as TWO images of three tall panels (image 1 = 적로 at pitches 0, 15, 30 degrees = panels 1-3; image 2 = 청림 at pitches 0, 15, 30 degrees = panels 4-6). Judge both images together as one sheet.'

_d10 = SHEETS['10d']['parts'][0]
SIMPLE['10d'] = [('p1', _d10[1], subset_panels(_d10[2], [1, 2], LAYOUT['2x2'], LAYOUT['2side']) .replace('four panels', 'two panels') + DRAW_LESS),
                 ('p2', _d10[1], subset_panels(_d10[2], [3, 4], LAYOUT['2x2'], LAYOUT['2side']).replace('four panels', 'two panels') + DRAW_LESS)]
SIMPLE_NOTE['10d'] = 'SIMPLIFIED version 4: the four letters redrawn as TWO images of two large panels (image 1 = 난 and 넌, image 2 = 논 and 눈; panels 1-4 in order). The six pair contrasts are read between the four panels. Judge both images together.'

_c7 = SHEETS['7c']['parts'][1][2]
_c7_head = _c7.split('Panel 1 (OWN vs ENEMY')[0]
_c7_p = ['Panel 1 (OWN vs ENEMY' + _c7.split('Panel 1 (OWN vs ENEMY')[1].split('Panel 2 (MY ATTACH')[0],
         'Panel 2 (MY ATTACH' + _c7.split('Panel 2 (MY ATTACH')[1].split('Panel 3 (ENEMY ATTACK TELL')[0],
         'Panel 3 (ENEMY ATTACK TELL' + _c7.split('Panel 3 (ENEMY ATTACK TELL')[1].split('Panel 4 (ENEMY MATERIAL')[0],
         'Panel 4 (ENEMY MATERIAL' + _c7.split('Panel 4 (ENEMY MATERIAL')[1]]
_c7_head2 = _c7_head.replace(LAYOUT['2x2pairs'], LAYOUT['2sidepairs'])
SIMPLE['7c'] = [
    SHEETS['7c']['parts'][0],
    ('p2', SHEETS['7c']['parts'][1][1], _c7_head2 + _c7_p[0] + _c7_p[1].replace('Panel 2', 'Panel 2', 1)),
    ('p3', SHEETS['7c']['parts'][1][1], _c7_head2 + _c7_p[2].replace('Panel 3', 'Panel 1', 1) + _c7_p[3].replace('Panel 4', 'Panel 2', 1)),
]
SIMPLE_NOTE['7c'] = 'SIMPLIFIED version 4: image 1 = panels 1-4 (one-shot frames), image 2 = panels 5-6 (ownership; my attach vs enemy weapon tell), image 3 = panels 7-8 (enemy attack tell vs zone; material warning vs zone = pair 24). Judge all three images together as one sheet.'

# ----------------------------------------------------------------------------------------------------------------- prompt assembly
def vname(sid, part, ver):
    return f'{sid}_{part}_v{ver}'

def ref_block(keys):
    lines = ['--- THE ATTACHED PICTURES ---']
    for i, k in enumerate(keys, 1):
        lines.append(f'Picture {i} is {REF_NAMES[k]}')
    return '\n'.join(lines)

def parts_for(sid, ver):
    return SIMPLE[sid] if (ver >= 4 and sid in SIMPLE) else SHEETS[sid]['parts']

def gen_prompt(sid, part, ver, prev=None):
    s = SHEETS[sid]
    keys, body = next((p[1], p[2]) for p in parts_for(sid, ver) if p[0] == part)
    chunks = [base.HEAD, ref_block(keys)]
    if prev and ver >= 4 and sid in SIMPLE:
        fix = OUT / f'{sid}_fix{ver - 1}.txt'
        fixtext = fix.read_text(encoding='utf-8') if fix.exists() else ''
        chunks.append('KNOWN PROBLEMS OF THE EARLIER, MORE CROWDED VERSION (avoid all of them in this simpler picture):\n' + fixtext)
    elif prev:
        n = len(keys) + 1
        fix = (OUT / f'{sid}_fix{ver - 1}.txt')
        fixtext = fix.read_text(encoding='utf-8') if fix.exists() else ''
        chunks.append(f"Picture {n} is the PREVIOUS version of this exact picture. Keep its layout, camera, ground, enemy designs and every panel that already works; change ONLY what the list below says, and keep the NOT-IN-THE-PICTURE rules.\nFIX LIST:\n{fixtext}")
    chunks += [PALETTE, OWNERSHIP, BODY_CUES, STYLE, body, BAN, base.TAIL % ((OUT / f'{vname(sid, part, ver)}.png').as_posix())]
    return '\n\n'.join(chunks)

def part_refs(sid, part, ver):
    keys = next(p[1] for p in parts_for(sid, ver) if p[0] == part)
    refs = [REF_KEYS[k] for k in keys]
    if ver > 1 and not (ver >= 4 and sid in SIMPLE):
        prev = OUT / f'{vname(sid, part, ver - 1)}.png'
        refs.append(prev)
    return refs

# ----------------------------------------------------------------------------------------------------------------- codex runs
def run_codex(args, prompt_bytes, timeout=1500):
    cmd = ['cmd', '/c', 'codex', 'exec', '--skip-git-repo-check'] + args
    return subprocess.run(cmd, input=prompt_bytes, capture_output=True, timeout=timeout)

def recover(log_text, started):
    cands = []
    for p in re.findall(r'[A-Za-z]:[\\/][^\s"\']*generated_images[\\/][^\s"\']*?\.png', log_text):
        cands.append(Path(p))
    for rel in re.findall(r'images[\\/]([0-9a-f-]{20,}[\\/]exec-[0-9a-f-]+\.png)', log_text):
        cands.append(GEN_HOME / rel.replace('\\', '/'))
    cands = [f for f in cands if f.exists() and f.stat().st_mtime >= started - 2]
    if cands:
        return max(cands, key=lambda f: f.stat().st_mtime)
    return None
def gen_part(sid, part, ver):
    name = vname(sid, part, ver)
    png = OUT / f'{name}.png'
    prompt = gen_prompt(sid, part, ver, prev=(ver > 1))
    (OUT / f'{name}.prompt.txt').write_text(prompt, encoding='utf-8')
    if png.exists():
        png.unlink()
    args = ['-s', 'workspace-write', '-C', str(OUT)]
    for r in part_refs(sid, part, ver):
        args += ['-i', str(r)]
    with GATE:
        for attempt in range(1, 4):
            t0 = time.time()
            try:
                r = run_codex(args, prompt.encode('utf-8'))
                text = (r.stdout or b'').decode('utf-8', 'replace') + '\n' + (r.stderr or b'').decode('utf-8', 'replace')
            except subprocess.TimeoutExpired:
                text = 'TIMEOUT'
            (OUT / f'{name}.log.txt').write_text(f'attempt {attempt}\n' + text, encoding='utf-8')
            if not png.exists() and 'IMAGE_TOOL_UNAVAILABLE' not in text:
                got = recover(text, t0)
                if got is not None and got.stat().st_mtime >= t0 - 2:
                    shutil.copyfile(got, png)
            if png.exists():
                print('OK', name, f'{time.time() - t0:.0f}s', flush=True)
                return True
            print('RETRY', name, attempt, text[-200:].replace('\n', ' '), flush=True)
    print('FAIL', name, flush=True)
    return False

# ----------------------------------------------------------------------------------------------------------------- judge
def plan_excerpt(ranges):
    lines = PLAN.read_text(encoding='utf-8').splitlines()
    out = []
    for a, b in ranges:
        out.append('\n'.join(lines[a - 1:b]))
    return '\n\n'.join(out)

JUDGE_HEAD = """너는 오행부(五行符) 술식 VFX 컨셉 시안의 엄격한 채점자다. 첨부 그림(들)만 보고 판단한다. 파일을 수정하지 말고, 다른 파일도 읽지 말고, 아래 계획 발췌와 그림만 근거로 채점해라. 칭찬하지 말고 감점 근거를 구체적으로 대라.

[채점표 — 정지 보조 채점(시안용), 합 100]
- 틀·동사 형태 30 (장별 하한 29)
- 몸·소유 구별 25 (하한 24)
- 먹 룩 20 (하한 19)  [수묵 담채·무광·발광 0·땅 대비·두 명도층이 한 획의 마른 가장자리와 안료 침착으로 공존]
- 극치·반응 구조 15 (하한 14)
- 구성·가림 위험 10 (하한 9)
합격 = 합계 ≥ 97 이고 모든 항목이 하한 이상이고 즉시 실패 사유가 없음.
즉시 실패(점수와 무관): 소유 역전 · 가짜 범위 · 내 면 채움 · 불벽 · 원판 결계. 추가 즉시 실패 확인: 빛나는 테 · 종이 스티커 · 분리된 이중 외곽(C-2) · 발광/번쩍임/가산광 · 글자/문자/HUD/라벨 · 적이 70 % 미만으로 보임 · 장식 구조가 판정 범위를 가짜로 만듦.

[판정 범위 D308-150] 정밀 기하(착점 위치 · 치수(m) · 분기 위치 · 공극/테 px · 정확한 색차 ΔE)는 엔진 정지 렌더 몫이므로 감점하지 않는다. 형태 문법(어떤 모양인지) · 소유 문법(마른 테 빈 획 vs 젖은 채운 면) · 먹 인상 · 혼동 쌍 구별 · 가림만 본다. 즉시 실패 항목은 그대로 적용한다. 극치·반응 항목(④)이 해당 장에 극치/반응 칸이 없으면 '해당 없음'으로 감점하지 말고, 각 칸이 요구하는 구조 요소가 완결되었는지만 본다.

[이 장의 적용 규칙]
- 이 장이 시험하지 않는 항목은 "계약 위반이 보이지 않으면 만점"으로 채점한다(없다는 이유로 감점 금지). 그러나 이 장이 시험하는 항목에서 계약 위반이나 불명확함이 보이면 반드시 감점한다.
- 정지 시안은 형태 · 소유 문법 · 먹 인상만 증명한다. 투영 · 픽셀 · 점유 · 가시율 · 움직임 · 종료 · 정확한 ΔE00는 이 단계에서 채점하지 않는다(엔진/영상 단계). 단, 그림에서 눈으로 명백히 틀린 것(가림, 색 구별 불가 등)은 감점한다.
- 이미지에 글자가 없는 것은 규칙이다. 칸 위치는 아래 "칸 지도"로 안다.
"""

JUDGE_TAIL = """
[출력 형식 — 반드시 이 형태로, 마지막에 둔다]
SCORES: 틀동사=NN/30 몸소유=NN/25 먹룩=NN/20 극치반응=NN/15 구성가림=NN/10
TOTAL=NN
AUTOFAIL: none 또는 해당 사유 목록(칸 번호 포함)
FLOORS: PASS 또는 FAIL(미달 항목)
VERDICT: PASS 또는 FAIL
PANEL_NOTES: 칸마다 한두 줄(잘된 점 / 틀린 점)
FIXES: 번호 매긴 구체적 수정 목록(어느 칸의 무엇을 어떻게 — 다음 재작도에서 그대로 프롬프트에 넣을 수 있게 영어로 간결히, 이미 통과한 칸은 "keep"으로 명시)
"""

def judge_prompt(sid, ver):
    s = SHEETS[sid]
    maps = []
    for p in parts_for(sid, ver):
        maps.append(f'[이미지 {p[0]}] 생성 사양(칸 지도 겸용):\n' + p[2])
    parts = [JUDGE_HEAD,
             f"[장] {sid} — {s['title']}",
             f"[이 장의 고유 정지 판정] {s['special']}",
             f"[적용 주석] {s['na']}",
             ('[레이아웃 주석] ' + (SIMPLE_NOTE[sid] if (ver >= 4 and sid in SIMPLE) else s['extra'])) if (s['extra'] and s['extra'] != 'REACT') or (ver >= 4 and sid in SIMPLE) else '',
             '[칸 지도: 첨부된 이미지 순서 = 아래 이미지 순서]\n' + '\n\n'.join(maps),
             '[계획 발췌(정본)]\n' + plan_excerpt(s['plan_lines']),
             '[공통 계약]\n' + PALETTE + '\n' + OWNERSHIP + '\n' + BODY_CUES,
             JUDGE_TAIL]
    return '\n\n'.join(x for x in parts if x)

def write_fix(sid, ver, text):
    m = re.search(r'FIXES:\s*(.*)', text, re.S)
    if m:
        (OUT / f'{sid}_fix{ver}.txt').write_text(m.group(1).strip(), encoding='utf-8')

def cycle(sid, ver):
    ok = all((OUT / f'{vname(sid, p[0], ver)}.png').exists() or gen_part(sid, p[0], ver) for p in parts_for(sid, ver))
    jf = OUT / f'{sid}_v{ver}.judge.md'
    if ok and not (jf.exists() and 'TOTAL=' in jf.read_text(encoding='utf-8')):
        judge(sid, ver)

def all_versions(sid):
    vs = []
    for f in OUT.glob(f'{sid}_v*.judge.md'):
        m = re.match(rf'{re.escape(sid)}_v(\d+)\.judge\.md$', f.name)
        if m and 'TOTAL=' in f.read_text(encoding='utf-8'):
            vs.append(int(m.group(1)))
    return sorted(vs)

def rejudge_best(sid):
    vs = all_versions(sid)
    if not vs:
        return
    best = max(vs, key=lambda v: (verdict(sid, v)[0], v))
    cand = sorted({best, vs[-1]})
    for v in cand:
        judge(sid, v, tag='_d150')

def latest_judged(sid):
    best = 0
    for f in OUT.glob(f'{sid}_v*.judge.md'):
        m = re.match(rf'{re.escape(sid)}_v(\d+)\.judge\.md$', f.name)
        if m and 'TOTAL=' in f.read_text(encoding='utf-8'):
            best = max(best, int(m.group(1)))
    return best

def verdict(sid, ver):
    t = (OUT / f'{sid}_v{ver}.judge.md').read_text(encoding='utf-8')
    m = re.search(r'TOTAL\s*=\s*(\d+(?:\.\d+)?)', t)
    v = re.search(r'VERDICT:\s*(PASS|FAIL)', t)
    return (float(m.group(1)) if m else 0, v.group(1) if v else 'FAIL')

def auto(sid, maxver=3):
    ver = latest_judged(sid)
    if ver == 0:
        cycle(sid, 1)
        ver = latest_judged(sid)
    while ver and verdict(sid, ver)[1] != 'PASS' and ver < maxver:
        cycle(sid, ver + 1)
        nv = latest_judged(sid)
        if nv <= ver:
            break
        ver = nv
    print('AUTO-END', sid, ver, verdict(sid, ver) if ver else None, flush=True)

def judge(sid, ver, tag=''):
    imgs = [OUT / f'{vname(sid, p[0], ver)}.png' for p in parts_for(sid, ver)]
    missing = [i for i in imgs if not i.exists()]
    if missing:
        print('NOIMG', sid, missing)
        return None
    prompt = judge_prompt(sid, ver)
    pf = OUT / f'{sid}_v{ver}.judge_prompt{tag}.txt'
    pf.write_text(prompt, encoding='utf-8')
    last = OUT / f'{sid}_v{ver}.judge{tag}.md'
    args = ['-s', 'read-only', '-C', str(REPO), '-o', str(last)]
    for i in imgs:
        args += ['-i', str(i)]
    with GATE:
        for attempt in range(1, 4):
            r = run_codex(args, prompt.encode('utf-8'), timeout=1500)
            text = last.read_text(encoding='utf-8') if last.exists() else ''
            if 'TOTAL=' in text:
                break
            print('JRETRY', sid, attempt, ((r.stderr or b'').decode('utf-8', 'replace'))[-200:].replace('\n', ' '), flush=True)
    if not tag:
        write_fix(sid, ver, text)
    m = re.search(r'TOTAL\s*=\s*(\d+(?:\.\d+)?)', text)
    v = re.search(r'VERDICT:\s*(PASS|FAIL)', text)
    af = re.search(r'AUTOFAIL:\s*(.*)', text)
    sc = re.search(r'SCORES:\s*(.*)', text)
    print('JUDGED', sid, ver, 'TOTAL', m.group(1) if m else '?', v.group(1) if v else '?', '| autofail:', af.group(1)[:80] if af else '?', '|', sc.group(1) if sc else '', flush=True)
    return text

# ----------------------------------------------------------------------------------------------------------------- main
def arglist(flag):
    if flag not in sys.argv:
        return []
    out = []
    for a in sys.argv[sys.argv.index(flag) + 1:]:
        if a.startswith('--'):
            break
        out += [x for x in re.split(r'[,\s]+', a) if x]
    return out

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ver = int(arglist('--ver')[0]) if arglist('--ver') else 1
    if '--list' in sys.argv:
        for sid, s in SHEETS.items():
            print(sid, s['title'], [p[0] for p in s['parts']])
        return
    if '--prompts' in sys.argv:
        for sid, s in SHEETS.items():
            for p in s['parts']:
                (OUT / f'{vname(sid, p[0], ver)}.prompt.txt').write_text(gen_prompt(sid, p[0], ver, prev=(ver > 1)), encoding='utf-8')
            (OUT / f'{sid}_v{ver}.judge_prompt.txt').write_text(judge_prompt(sid, ver), encoding='utf-8')
            print('PROMPT', sid)
        return
    g = arglist('--gen')
    if g:
        jobs = [(sid, p[0]) for sid in g for p in parts_for(sid, ver)]
        with ThreadPoolExecutor(max_workers=3) as ex:
            list(ex.map(lambda j: gen_part(j[0], j[1], ver), jobs))
    cy = arglist('--cycle')
    if cy:
        with ThreadPoolExecutor(max_workers=6) as ex:
            list(ex.map(lambda x: cycle(x.split(':')[0], int(x.split(':')[1]) if ':' in x else ver), cy))
    if '--fixes' in sys.argv:
        for sid in arglist('--fixes'):
            f = OUT / f'{sid}_v{ver}.judge.md'
            if f.exists():
                write_fix(sid, ver, f.read_text(encoding='utf-8'))
    au = arglist('--auto')
    if au:
        with ThreadPoolExecutor(max_workers=6) as ex:
            list(ex.map(auto, au))
    rj = arglist('--rejudge')
    if rj:
        with ThreadPoolExecutor(max_workers=6) as ex:
            list(ex.map(rejudge_best, rj))
    j = arglist('--judge')
    if j:
        with ThreadPoolExecutor(max_workers=3) as ex:
            list(ex.map(lambda sid: judge(sid, ver), j))


if __name__ == '__main__':
    main()








