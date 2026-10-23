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
