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
