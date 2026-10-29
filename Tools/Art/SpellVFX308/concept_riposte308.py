#!/usr/bin/env python3
"""#308 D308-147 follow-up: 앞잡 (riposte / finisher) look v2 concepts. User 2026-10-23: "확실한 건 앞잡이든 오의든 vfx 손보고
발동 방식도 체크는 한번 해봐야해". Made the same way as concept_reactions308.py (codex exec, built-in image generation).
Keeps the user's own directions for the finisher (SPEC-PLAYER-FEEL-300 F: several glyph magic circles open in front of and above
the player, projectiles pour out of them, wisp-like wobbling paths) and moves them into the ink language of the spell VFX overhaul
(D308-80 glyph circle on the swing, D308-83 ink deploy, ART-INK glow cap).

  python Tools/Art/SpellVFX308/concept_riposte308.py --run        # all
  python Tools/Art/SpellVFX308/concept_riposte308.py --run rp_a   # only these

Writes Art/SpellVFX308/Concepts/riposte/<id>.prompt.txt (+ .png). Concepts only (D308-28)."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_reactions308 as base

OUT = base.REPO / 'Art/SpellVFX308/Concepts/riposte'
OLD = OUT / 'ref_old_riposte_sheet.png'   # contact sheet of the 2026-09-28 first version (32 black glyphs, red flash)

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder) and the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character with the great calligraphy brush. Keep that character and that brush.
Picture 3 is a contact sheet of the FIRST version of this finisher (a cloud of small black printed-looking Hangul glyphs thrown at the enemy, then a red flash). It looked flat, like stickers, with no impact - it is what we are improving, do not copy its look."""

STYLE = base.STYLE
COMMON = """--- WHAT THIS MOVE IS ---
A finisher used only while a staggered enemy is open (kneeling, guard broken). One tap and the caster raises the brush: several ink-drawn magic circles bloom in an arc in front of and above the caster, each with one Korean consonant brushed at its centre and a ring of brush-stroke patterns around it; from every circle a stream of small ink-fire wisps pours out, wobbling and looping like dokkaebi fire, then converges and slams into the enemy. Everything is INK: circles are wet brush rings that spread and dry, wisps are dense black-ink orbs with only a thin muted-colour rim (wood green-black, fire vermilion edge, earth ochre, metal grey, water indigo - one colour per circle), never bright light. Impacts are ink splashes bursting outward plus at most one very brief dark-red flash on the final hit."""

PICTURES = {
    'rp_a_board': ([base.SHOULDER, base.BRUSH, OLD], COMMON + """
--- THE PICTURE: readability board, three moments ---
LAYOUT: ONE wide strip of THREE equal panels, the SAME shoulder camera, the same red-earth clearing, a kneeling staggered enemy (gaunt soldier, guard broken, matte) 8 m ahead, thin paper-coloured gaps.
Panel 1 (0.3 s): the caster's brush raised; 5 of the circles already open in an arc 2-5 m up and ahead, each a wet black brush ring 0.6-1.8 m across, different sizes, consonant at the centre brushed thick, ink still dripping from the ring bottoms; the others are just starting as a single stroke.
Panel 2 (1.0 s): all circles open; dozens of small ink-fire wisps stream from each ring on wobbling looping paths with short smoky ink tails, converging toward the enemy's chest.
Panel 3 (1.6 s): the wisps slam into the enemy: a dense radial ink splash bursting outward on the enemy's body and the ground behind, the enemy recoiling, the circles above smearing and dissolving like wet ink running down paper.
Nothing glows except one tiny dark-red flash at the centre of panel 3. No sparks, no white light."""),
    'rp_b_first_person': ([base.SHOULDER, base.BRUSH, OLD], COMMON + """
--- THE PICTURE: first-person, the peak moment ---
ONE single full frame. First-person view: the caster's right hand and the great brush raised at the bottom-right of the frame, the brush tip still trailing a wet stroke. Above and ahead, the arc of ink circles fills the upper half of the frame (12 circles, varied sizes, nearest one 1 m away and large, farthest small), each with a different brushed consonant. Streams of ink-fire wisps pour from them toward a kneeling staggered enemy 8 m ahead in the lower centre; the enemy stays at least 70 % visible. The ground below darkens with drips falling from the circles. Matte ink, no glow, no bloom."""),
}


# round 2 (Docs/Research/debate32/c1_codex_answer.md): readable consonants, black rings, matte beads, enemy >= 70 % visible,
# first person with clear upper-screen gaps, bursts instead of necklaces
FIX2 = """--- SECOND VERSION FIXES (round 1 failed these) ---
- Each ring holds ONE clearly readable Korean consonant, written large and plain with a brush, exactly one of: ㄱ, ㄴ, ㅁ, ㅅ, ㅇ (use all five across the rings). No abstract scribbles, no other letters.
- Rings stay predominantly BLACK wet ink; the element colour (wood green, fire vermilion, earth ochre, metal grey, water indigo) appears only as a thin muted edge on the ring and on the bead rims - never as a wide coloured band.
- Beads are fully MATTE black ink, like dried ink drops: no highlights, no reflections, no glossy bands, no glow; only a thin muted coloured rim.
- Paths come in staggered bursts with irregular wobble and small loops, thin broken dry-brush trails - never evenly spaced like a string of beads or a necklace.
- The enemy stays at least 70 % visible at every moment: splashes burst outward AROUND the body, not covering it.
- Only one brief dark-crimson flash, on the final hit."""

PICTURES.update({
    'rp_a2_board': (PICTURES['rp_a_board'][0], PICTURES['rp_a_board'][1] + "\n" + FIX2),
    'rp_b2_first_person': (PICTURES['rp_b_first_person'][0], COMMON + """
--- THE PICTURE: first-person, the peak moment (second version) ---
ONE single full frame. First-person view: the caster's right hand and the great brush raised at the bottom-right, the brush tip trailing a wet stroke. The 12 ink rings stand 4-9 m AHEAD of the camera, smaller (each covering at most 1/12 of the frame width), spread in a loose arc with wide clear gaps of sky and scene between them; none touches the frame edge, none is cut off. Bursts of matte ink beads pour from them toward a kneeling staggered enemy 8 m ahead in the lower centre. The upper half of the frame must still show plenty of sky and scene through the gaps.
""" + FIX2),
})


# round 3 (Docs/Research/debate32/c2_codex_answer.md: a2 82, b2 77): matte irregular masses, staggered clusters with gaps,
# directional dry-brush impacts instead of a radial liquid burst; picture 3 is now the second-version first-person frame
REF3 = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character with the great calligraphy brush. Keep that character and that brush.
Picture 3 is the SECOND version of this finisher: keep its readable consonants, its black rings with thin muted colour edges and its layout, and fix only what the list below says."""
FIX3 = """--- THIRD VERSION FIXES (version 2 failed these) ---
- Projectiles are irregular MATTE ink masses - torn, lumpy, absorbent like ink soaked into paper. Absolutely no round polished spheres, no specular dots, no shiny surfaces.
- Projectiles fly in staggered small clusters of 2-5 with large empty gaps between clusters; most of each path is EMPTY. No continuous chain from ring to enemy.
- Each impact is a short DIRECTIONAL dry-brush strike: split bristle marks and broken streaks that continue the projectile's flight direction, flicked across and past the body. No radial liquid explosion, no puddles, no ink pool on the ground.
- The enemy's silhouette stays at least 70 % visible: head, torso, arms and legs readable.
- Rings: asymmetric hand-brushed rings of varying thickness with a dry broken gap where the brush lifted, consonant crisp at the centre."""

PICTURES.update({
    'rp_b3_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_b2_first_person.png'], COMMON + """
--- THE PICTURE: first-person, the peak moment (third version) ---
ONE single full frame, same camera and layout as picture 3: the caster's right hand and great brush at the bottom-right; 12 small ink rings far ahead along the UPPER and SIDE edges of the frame, the centre of the frame clear; a kneeling staggered enemy 8 m ahead in the lower centre.
""" + FIX3, REF3),
    'rp_c3_impact': ([base.SHOULDER, base.BRUSH, OUT / 'rp_b2_first_person.png'], COMMON + """
--- THE PICTURE: the impact, close third-person (third version) ---
ONE single full frame. Third-person shoulder camera, 4 m from a kneeling staggered enemy (gaunt soldier, guard broken, matte). Three or four clusters of matte ink masses arrive from the upper left and upper right at the same moment; each one turns into a short directional dry-brush strike across the enemy's body - split bristle streaks, broken flicks, a few torn ink flecks continuing past him - like a calligrapher slashing strokes through the air. The final hit at the chest: one tiny dark-crimson flash, smaller than a hand. The rings are visible small and far at the top edge, already dissolving.
""" + FIX3, REF3),
})


# round 4 (Docs/Research/debate32/c3_codex_answer.md: set 87, b3 88, c3 85): no thin tails linking the masses,
# irregular rings, impact marks only along the incoming direction (no star around the chest)
REF4 = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character with the great calligraphy brush. Keep that character and that brush.
Picture 3 is the THIRD version of this exact picture: keep its camera, layout, matte torn ink masses and readable consonants, and fix only what the list below says."""
FIX4 = """--- FOURTH VERSION FIXES (version 3 failed these) ---
- NO thin tails or threads between the ink masses: each small cluster of 2 masses has its own very short dry-brush tail, then fully EMPTY air until the next cluster. Nothing connects one cluster to the next or to the ring.
- Rings are NOT neat circles: each is a single hand-brushed loop with uneven thickness, slightly oval and tilted differently, one end overlapping and one dry broken gap where the brush lifted.
- Impact marks run ONLY along each projectile's incoming direction - short parallel dry-brush slashes continuing through and past the body. No star-shaped splatter, nothing radiating outward from the chest in all directions.
- The enemy's head, torso, both arms and legs stay clearly readable; ink marks cross the body only as thin slashes."""

PICTURES.update({
    'rp_b4_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_b3_first_person.png'],
                           PICTURES['rp_b3_first_person'][1].split('--- THIRD VERSION')[0] + FIX4, REF4),
    'rp_c4_impact': ([base.SHOULDER, base.BRUSH, OUT / 'rp_c3_impact.png'],
                     PICTURES['rp_c3_impact'][1].split('--- THIRD VERSION')[0] + FIX4, REF4),
})


# round 5 (Docs/Research/debate32/c4_codex_answer.md: b4 86 bug-like masses, c4 88 star at the chest)
REF5 = REF4.replace('THIRD version', 'FOURTH version').replace('matte torn ink masses and ', '')
FIX5 = """--- FIFTH VERSION FIXES (version 4 failed these) ---
- Projectiles must NOT look like insects, beetles, ticks or bullets: no closed oval bodies, no spikes or legs sticking out to the sides. Each projectile is ONE asymmetric swept brush mark - a short calligraphy stroke flicked in the flight direction: blunt wet head, thinning dry tail with split bristle lines and gaps where the paper shows through. Every mark has a different shape and length.
- Keep the version-4 spacing: pairs of marks with fully empty air between pairs, no connecting threads.
- Each ring is ONE single brush stroke drawn round in one motion: starts thick and wet, thins out, the end overshoots past the start, slightly oval and tilted differently per ring; no dashed or broken-up outlines, no neat circles.
- At the enemy, each arriving mark continues as 1-2 short parallel dry slashes along its own flight direction only. At the chest: one tiny dark-crimson fleck, no lines radiating from it."""

PICTURES.update({
    'rp_b5_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_b4_first_person.png'],
                           PICTURES['rp_b3_first_person'][1].split('--- THIRD VERSION')[0] + FIX5, REF5),
    'rp_c5_impact': ([base.SHOULDER, base.BRUSH, OUT / 'rp_c4_impact.png'],
                     PICTURES['rp_c3_impact'][1].split('--- THIRD VERSION')[0] + FIX5, REF5),
})


# round 6 (Docs/Research/debate32/c5_codex_answer.md: b5 89 kept, c5 82 crossing burst at the chest): impact only
REF6 = REF5.replace('FOURTH version', 'FIFTH version')
FIX6 = """--- SIXTH VERSION FIXES (version 5 failed these) ---
- Keep the flying brush marks exactly as in picture 3 (swept, asymmetric, split dry tails, pairs with empty air between).
- At the enemy's chest there is NOTHING but one small dark-crimson brush dab, about the size of a palm, elongated along the flight direction of the marks arriving from the upper right. No black spikes, no crossing strokes, no lines radiating from the chest, no new strokes sprouting from the body.
- Marks that already passed the body continue as one or two thin dry flecks on the ground BEHIND him, along their own flight direction.
- Rings: each one a single irregular brush loop with a visible thick wet start, thinning, and a dry overshooting end - no concentric double outlines."""

PICTURES['rp_c6_impact'] = ([base.SHOULDER, base.BRUSH, OUT / 'rp_c5_impact.png'],
                            PICTURES['rp_c3_impact'][1].split('--- THIRD VERSION')[0] + FIX6, REF6)


# free directions (user 10-24: "자음이 보이는 형태가 생각보다 별로야. 아예 자유주제로 너가 한번 만들어볼래?") - the
# glyph-ring structure is dropped; only the finisher's job (one tap, a staggered enemy, ink, no glow) is kept
REF_FREE = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera (third person, behind and slightly above the caster's right shoulder), the palace plaza and the ink-wash tone. Do not copy the user interface.
Picture 2 is the player character with the great calligraphy brush. Keep that character and that brush."""
FREE = """--- WHAT THIS MOVE IS ---
A finisher in an ink-wash action game: the enemy 6 m ahead is staggered, kneeling with its guard broken. The caster answers with ONE decisive move of the great brush. It must read instantly as "the end of this fight", powered only by INK - wet ink, dry brush, paper. Matte, no light of any kind. No letters, no magic circles, no glyphs. The enemy (gaunt soldier, matte armour) stays clearly readable."""
PICTURES.update({
    'rp_free_stroke': ([base.SHOULDER, base.BRUSH], FREE + """
--- DIRECTION: ONE STROKE (일필휘지) ---
ONE single full frame, the instant after the move. The caster has swept the great brush in one huge diagonal from upper left to lower right; that single stroke now hangs in the air across the whole frame like a giant calligraphy stroke on invisible paper, 12 m long, passing straight THROUGH the kneeling enemy: a fat wet black head at the upper left, bristle grooves running along its length, splitting into a dry broken tail full of paper-white gaps at the lower right. Where the stroke crosses the enemy, his body is cleanly cut by it and ink soaks into him along the stroke line. The rest of the world is drained toward pale paper tones for this instant, so the stroke is the darkest thing on screen. A few heavy ink drops fall from the stroke's underside.""", REF_FREE),
    'rp_free_seal': ([base.SHOULDER, base.BRUSH], FREE + """
--- DIRECTION: THE SEAL (낙관) ---
ONE single full frame. Finishing a painting by stamping the artist's seal: the caster has swung the brush down like a gavel, and a huge square seal-stamp imprint made of deep cinnabar-red and black ink, 3 m wide, has slammed onto the ground and the enemy from above, flattening the dust. The seal is a carved square block print with uneven pressure - some areas dense pigment, some patchy where the stone did not touch - and its border is cracked and chipped like an old stone seal. NO characters inside the seal: its interior is an abstract carved pattern of cut lines and blank channels. The enemy is crushed to one knee inside the imprint, still fully readable. Ink spreads outward from the seal's edges into the cracks of the ground like ink into paper fibres. The cinnabar is pigment, not light.""", REF_FREE),
    'rp_free_wave': ([base.SHOULDER, base.BRUSH], FREE + """
--- DIRECTION: THE PAINTED WAVE (먹 파도) ---
ONE single full frame. The caster has flicked the brush upward and a towering wave painted in the style of a Korean folk painting of waves (minhwa) rises from the ground behind the enemy and curls over him, 7 m tall: layered ink-wash bands, the crest drawn with crisp dry-brush claw-like curls, the inside graded from deep black to pale grey wash, paper texture visible through the thin layers. The wave is about to crash; its first black fingers already touch the enemy's shoulders. The enemy is in the lower centre, kneeling, still clearly readable in front of the dark wave. No water reflections, no foam highlights - it is painted ink, not real water.""", REF_FREE),
})


# one stroke, round 2 (user 10-24: "앞잡은 ① 한 획(일필휘지) 방향으로 가자. 끝에 먹색 낙관을 작게 찍는 것까지 넣어서 2차 시안을
# 만들어 줘."): matte drops, the stroke's life in three moments, a small ink-black seal at the stroke's end
REF_STROKE = REF_FREE + """
Picture 3 is the FIRST version of this exact move: keep its idea (one giant brush stroke hanging in the air and passing through the enemy, wet head, bristle grooves, dry split tail), its colours and its scale, and change only what the text below asks."""
STROKE = FREE.replace('No letters, no magic circles, no glyphs.', 'No letters, no magic circles, no glyphs (the only exception is the small seal described below, whose face is an abstract carved pattern, not a character).') + """
--- THE MOVE: ONE STROKE, SIGNED (일필휘지 + 낙관) ---
The caster sweeps the great brush once, from upper left to lower right. The swing leaves ONE giant calligraphy stroke hanging in the air on invisible paper, passing straight THROUGH the kneeling enemy: a fat wet black head, bristle grooves along its length, a dry split tail full of paper-white gaps. Like a painter signing a finished work, the move ends with a SMALL seal stamp pressed at the very END of the stroke's tail: a square ink-BLACK seal impression about 40 cm wide, uneven pressure, patchy where the stone did not touch, chipped border, its face an abstract carved pattern of cut lines (no readable character). The seal is black ink like the stroke - not red, not glowing.
MATERIAL: every drop and splatter is MATTE ink, like ink soaked into paper - no round glossy beads, no specular highlights, no shine anywhere."""
PICTURES.update({
    'rp_stroke2_board': ([base.SHOULDER, base.BRUSH, OUT / 'rp_free_stroke.png'], STROKE + """
--- THE PICTURE: readability board, three moments ---
LAYOUT: ONE wide strip of THREE equal panels, the SAME shoulder camera and the same palace plaza, the kneeling enemy 6 m ahead, thin paper-coloured gaps between panels.
Panel 1 (0.00-0.15 s, the swing): the caster mid-swing, the brush tip laying down the stroke's wet head at the upper left; the stroke is half drawn, its leading edge racing toward the enemy; the plaza around starts draining toward pale paper tones.
Panel 2 (0.2 s, the cut): the full stroke hangs across the frame and through the enemy's body; along the stroke line the enemy is soaked dark; the world is at its palest so the stroke is the darkest thing on screen; a few heavy matte drops fall from the stroke's underside.
Panel 3 (0.6 s, the seal): the caster's brush is raised and turned like pressing a seal; the small black seal is stamped at the stroke's tail end; the stroke above is already drying - cracking into dry fibres and flaking away from its head - while the enemy collapses forward. Colour is returning to the plaza.""", REF_STROKE),
    'rp_stroke2_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_free_stroke.png'], STROKE + """
--- THE PICTURE: first-person, the cut (0.2 s) ---
ONE single full frame. First-person view: the caster's right hand and the great brush at the bottom-right, just finished the sweep, bristles splayed and dripping. The giant stroke runs diagonally from the upper left of the frame, through the kneeling enemy 6 m ahead in the centre, to the lower right, where the small black seal is being pressed at its tail end. The stroke is thick but the enemy's head, shoulders and arms are still clearly readable on both sides of it. The world around is drained toward pale paper tones.""", REF_STROKE),
})


# one stroke, round 3 (user 10-24: "낙관은 없애고 진행"): no seal; matte drops; the stroke must read as drawn by the swing
REF_STROKE3 = REF_FREE + """
Picture 3 is the SECOND version of this move (three moments): keep its camera, its timing, the stroke's shape and scale, and change only what the text below asks."""
STROKE3 = FREE + """
--- THE MOVE: ONE STROKE (일필휘지) ---
The caster sweeps the great brush once, from upper left to lower right. The swing leaves ONE giant calligraphy stroke hanging in the air on invisible paper, passing straight THROUGH the kneeling enemy: a fat wet black head where the swing began, bristle grooves running along its length, a dry split tail full of paper-white gaps where the swing ended. The stroke is the only effect - no seal, no stamp, no symbols, no extra marks.
MATERIAL: the stroke and every drop are MATTE ink soaked into paper. Drops are small irregular blots and short dry flecks, never round glossy beads; no specular highlights, no sheen, no blue or metallic tint anywhere in the ink.
THE WORLD: for the instant of the cut, the plaza, sky and buildings drain toward pale warm paper tones with low contrast, so the black stroke and the enemy are the strongest values on screen."""
PICTURES.update({
    'rp_stroke3_board': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke2_board.png'], STROKE3 + """
--- THE PICTURE: readability board, three moments ---
LAYOUT: ONE wide strip of THREE equal panels, the SAME shoulder camera and the same palace plaza, the kneeling enemy 6 m ahead, thin paper-coloured gaps between panels.
Panel 1 (0.00-0.15 s, the swing): the caster mid-swing; the brush tip is laying down the stroke - the wet head already hangs at the upper left and the stroke's leading edge is racing toward the enemy, its body visibly growing out of the brush's path.
Panel 2 (0.2 s, the cut): the full stroke hangs across the frame and through the enemy's body; along the stroke line the enemy is soaked dark; the world is at its palest.
Panel 3 (0.6 s, the aftermath): the stroke is drying - cracking into dry fibres and flaking away from its head like old ink on paper, only a faint grey ghost of it left; the enemy collapses forward, a dark ink line across the body where the stroke passed; colour returns to the plaza.""", REF_STROKE3),
    'rp_stroke3_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke2_board.png'], STROKE3 + """
--- THE PICTURE: first-person, the cut (0.2 s) ---
ONE single full frame. First-person view: the caster's right hand and the great brush at the bottom-RIGHT corner, the arm having just finished the sweep, bristles splayed and dripping. The giant stroke starts at the upper LEFT of the frame (its fat wet head) and runs diagonally down through the kneeling enemy 6 m ahead in the centre, its dry split tail ending in the air a short way past him on the lower right - it does NOT touch the hand or the brush. The enemy's head, shoulders and both arms are clearly readable on both sides of the stroke. The world around is drained toward pale paper tones.""", REF_STROKE3),
})


# one stroke, round 4 (Docs/Research/debate32/s3_codex_answer.md: board 84, first person 70 - stray brush head):
# one brush only, a real dry break-up in the last moment, a narrower stroke through the body, the colour drain made visible
REF_STROKE4 = REF_STROKE3.replace('SECOND version', 'THIRD version')
FIX_S4 = """
--- FOURTH VERSION FIXES ---
- Exactly ONE brush in the picture: the one in the caster's hand. No second brush head, no floating bristles anywhere in the stroke.
- Where the stroke crosses the enemy's body it narrows and concentrates (like the brush pressing down hard), then thins quickly into a dry broken tail past him. His head, torso and knees stay readable.
- No round drops at all - only small irregular blots and dry flecks."""
PICTURES.update({
    'rp_stroke4_board': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke3_board.png'], STROKE3 + """
--- THE PICTURE: readability board, three moments ---
LAYOUT: ONE wide strip of THREE equal panels, the SAME shoulder camera and the same palace plaza, the kneeling enemy 6 m ahead, thin paper-coloured gaps between panels.
Panel 1 (0.00-0.15 s, the swing): the plaza still in its NORMAL colours (red pillars, warm stone); the caster mid-swing, the brush tip laying down the stroke - wet head at the upper left, its leading edge racing toward the enemy, visibly growing out of the brush's path.
Panel 2 (0.2 s, the cut): the colours have DRAINED - the whole plaza is pale low-contrast paper tone, red pillars faded to grey-beige; the full stroke hangs through the enemy's body; along the stroke line the enemy is soaked dark.
Panel 3 (0.6 s, the drying): colour is coming back; the stroke is still there in its full shape but DRY - pale grey-black, its body split by long bristle gaps and irregular cracks, its thin parts breaking into short dry fragments falling away, like old ink cracking off paper; the enemy collapses forward with a dark ink line across the body.""" + FIX_S4, REF_STROKE4),
    'rp_stroke4_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke3_board.png'], STROKE3 + """
--- THE PICTURE: first-person, the cut (0.2 s) ---
ONE single full frame. First-person view: the caster's right hand holds the great brush at the bottom-right; the arm has just swept from upper left to lower right, the brush tip now low at the right, bristles splayed, still trailing the last thin dry wisp of the stroke from its tip. The giant stroke: its fat wet head at the upper LEFT of the frame, running diagonally down through the kneeling enemy 6 m ahead in the centre, thinning into a dry broken tail that fades out in the air before reaching the brush. The enemy's head, shoulders and both arms are clearly readable on both sides of the stroke. The world around is drained toward pale paper tones.""" + FIX_S4, REF_STROKE4),
})


# one stroke, round 5 (Docs/Research/debate32/s4_codex_answer.md: board 87, first person 86): local fixes only
REF_STROKE5 = REF_STROKE3.replace('SECOND version of this move (three moments)', 'FOURTH version of this exact picture').replace('keep its camera, its timing, the stroke\'s shape and scale', 'keep EVERYTHING - camera, layout, colours, the caster, the enemy, the stroke\'s shape')
FIX_S5 = """
--- FIFTH VERSION: LOCAL FIXES ONLY ---
- The light lines inside the stroke are GAPS between bristle marks where the background shows through - never light lying on top of the ink. No golden, tan or shiny streaks on the ink; the ink is matte black with only darker and lighter (thinner) ink."""
PICTURES.update({
    'rp_stroke5_board': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke4_board.png'], STROKE3 + FIX_S4 + FIX_S5 + """
- Panel 3 ONLY: the stroke keeps its full shape from panel 2 but is DRY and pale grey-black; irregular cracks open inside its body like dried mud or old lacquer, and its edges break into short FLAT dry flakes that drop straight down. No thin threads, no web of strings, no new splashes flying in other directions.
Keep the three panels, their timing and the colour change exactly as in picture 3 (normal colours, then drained, then returning).""", REF_STROKE5),
    'rp_stroke5_first_person': ([base.SHOULDER, base.BRUSH, OUT / 'rp_stroke4_first_person.png'], STROKE3 + FIX_S4 + FIX_S5 + """
- Where the stroke crosses the enemy's face and chest, the band is NARROWER (about the width of his forearm), so his helmet, face and chest stay readable.
- The big wet head at the upper left is one continuous part of the stroke, not a separate shape that looks like bristles or a brush.
Keep the single frame, the first-person hand and brush at the bottom-right and everything else exactly as in picture 3.""", REF_STROKE5),
})


def prompt(pid):
    refs, body, *ref = PICTURES[pid]
    return '\n\n'.join([base.HEAD, ref[0] if ref else REF, STYLE, body, base.BAN, base.TAIL % (OUT / f'{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'{pid}.png').exists())


if __name__ == '__main__':
    main()
