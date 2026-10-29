#!/usr/bin/env python3
"""#308 D308-127 follow-up concepts: the weapon forms' swing afterimage in the element's colour (user 2026-10-22: "비교그림에서
속성이 잘 드러나지 않아. 휘두를 때 해당 속성 오방색의 잔상이 잘 보이도록 해줘"; process: concepts first, debate on them, then
decide the implementation). Colours = ArtAudio Bible 오행 강조색 번안 [LOCKED]: 목 청록(생죽색) · 화 주홍 · 토 황토 · 금 은백 ·
수 현색(깊은 청묵); white and black as such are avoided on paper and ink.

  python Tools/Art/SpellVFX308/concept_weapon_trails308.py --run [ids]

Writes Art/SpellVFX308/Concepts/weapon_trails/wt_<id>.prompt.txt and, with --run, the pictures through `codex exec` (built-in
image generation). Concepts only, never textures (D308-28)."""
import shutil, subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from concept_weapon_forms308 import HEAD, REF, BAN, TAIL, SHOULDER, BRUSH   # the same frame as debate 11

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Concepts/weapon_trails'
HEADS = OUT / 'ref_heads_unity.png'   # Art/Characters308/WeaponHeads308/v1/unity/lineup_three_quarter.png - the built heads

REF_HEADS = """Picture 3 is the five weapon heads as built in the game (left to right after the plain brush: wood spear, fire two-handed sword (temporary), earth axe, metal two-handed sword, water flail). They are matte black hardened ink on the black staff; the element shows only as a thin stain on the edges, and the user says the element does NOT read. Keep these weapon shapes."""

STYLE = """--- THE LOOK ---
A game concept paint-over in the style of picture 1: a Joseon-era Korean palace town, stone-paved plaza, dark tiled roofs, hazy hills, pale warm paper-coloured sky; everything warm grey-beige and slightly desaturated, like ink wash over a 3D scene. All magic is INK and mineral PIGMENT soaked into paper - never light. The weapons are matte hardened black ink. The swing leaves an AFTERIMAGE painted in the element's traditional Korean colour (obangsaek as this game translates it): WOOD bamboo-green / blue-green #3E8C6A, FIRE vermilion #C8452F, EARTH ochre yellow #C08A2E, METAL silver-white #D8D6CC laid over a thin dark ink core so it reads on the pale ground, WATER deep blue-black #1F3550 with a lighter indigo edge #3C5F86. The colour must be strong enough to name the element at a glance, but it is pigment (matte, opaque or watery), never glowing."""

PICTURES = {
    'a_trails': ([SHOULDER, BRUSH, HEADS], """--- THE PICTURE: each weapon mid-swing, its afterimage in the element colour ---
Shoulder camera as in picture 1 (the caster about 3 m in front of the camera, a bandit 3-4 m beyond). LAYOUT: ONE row of FIVE equal panels, thin paper-coloured gaps, same plaza, the moment just after the strike passes the enemy.
Panel 1 WOOD SPEAR, a straight forward thrust: a long straight band of bamboo-green pigment stretched along the thrust line from the caster's hands to just past the spear point, thick near the point and splitting into dry-brush streaks toward the hands.
Panel 2 FIRE (temporary two-handed sword): a diagonal sweep arc of vermilion pigment behind the blade.
Panel 3 EARTH AXE, an overhead chop: a short heavy vertical arc of ochre pigment, wide and blunt, ending in a few heavy ochre-and-ink drops near the ground.
Panel 4 METAL TWO-HANDED SWORD, a horizontal sweep: a wide flat arc of silver-white pigment over a thin dark ink core, crisp sharp edge on the outside of the arc.
Panel 5 WATER FLAIL, a wide whipping sweep: a looping curved trail of deep blue-black ink with an indigo edge following the swinging rod, with small blue-black flecks flung outward.
Every afterimage is a single brush stroke shape (like a calligraphy stroke laid in the air along the weapon's path), widest just behind the weapon head and thinning and breaking into dry-brush streaks at the start of the swing; it covers at most a third of the panel and never hides the enemy's head."""),
    'b_variants': ([SHOULDER, BRUSH, HEADS], """--- THE PICTURE: three ways to paint the same swing afterimage (one weapon: the metal two-handed sword, horizontal sweep) ---
Shoulder camera as in picture 1. LAYOUT: ONE row of THREE equal panels, thin paper-coloured gaps, identical camera and pose.
Panel A RIBBON: one continuous flat pigment ribbon along the blade's arc, solid near the blade, fading by dry-brush splitting at the tail.
Panel B INK + PIGMENT RIM: the arc is a black ink stroke, with the element colour (here silver-white) only as a broad soaked rim on its outer edge.
Panel C SPLIT AFTERIMAGES: three or four faint copies of the blade's silhouette left along the arc, each painted in the element colour and getting fainter toward the start, like ghosted brush prints.
Then add a SECOND row with the same three panels for the WATER flail (deep blue-black with indigo edge) to show how each way reads for the darkest colour."""),
    'c_distance': ([SHOULDER, BRUSH, HEADS], """--- THE PICTURE: do the five colours still name the element when small? ---
LAYOUT: TWO rows of FIVE equal panels, thin paper-coloured gaps. Each column is one weapon and its swing afterimage in its colour (wood spear thrust bamboo-green · fire sword sweep vermilion · earth axe chop ochre · metal sword sweep silver-white on a thin ink core · water flail whip deep blue-black with indigo edge).
TOP row: shoulder camera, the caster about 3 m away (as in picture 1).
BOTTOM row: the same swings seen from about 15 m to the side (the caster small, about a tenth of the panel height) - a test that the colours still read apart when small. Same plaza, same light."""),
}


def prompt(pid):
    refs, body = PICTURES[pid]
    return '\n\n'.join([HEAD, REF + '\n' + REF_HEADS, STYLE, body, BAN, TAIL % (OUT / f'wt_{pid}.png').as_posix()])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    src = REPO / 'Art/Characters308/WeaponHeads308/v1/unity/lineup_three_quarter.png'
    if src.exists(): shutil.copyfile(src, HEADS)
    for ref in (SHOULDER, BRUSH):
        if not ref.exists(): sys.exit('missing reference ' + str(ref))
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    pids = [p for p in PICTURES if not only or p in only]
    for pid in pids:
        (OUT / f'wt_{pid}.prompt.txt').write_text(prompt(pid), encoding='utf-8')
        print('PROMPT', pid)
    if '--run' not in sys.argv:
        return
    procs = []
    for pid in pids:
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT)]
        for r in PICTURES[pid][0]:
            cmd += ['-i', str(r)]
        log = open(OUT / f'wt_{pid}.log.txt', 'w', encoding='utf-8')
        procs.append((pid, subprocess.Popen(cmd, stdin=open(OUT / f'wt_{pid}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
    for pid, p in procs:
        print('DONE', pid, p.wait(), (OUT / f'wt_{pid}.png').exists())


if __name__ == '__main__':
    main()
