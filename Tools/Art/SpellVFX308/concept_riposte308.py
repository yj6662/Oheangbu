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
