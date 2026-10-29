#!/usr/bin/env python3
"""#308 D308-142 (debate 29) concepts: arcanepunk Joseon (마석) / steampunk Goryeo lineage (기관술). Made the same way as
Tools/Art/SpellVFX308/concept_reactions308.py (codex exec, built-in image generation, game stills attached).
Process (user 2026-10-23: "현강 유구 · 3막 중계소 · 숨통 시안을 Codex 그림으로 뽑고 토론으로 판정해"): concepts -> debate -> verdict -> build.

  python Tools/Art/Punk308/concept_punk308.py            # writes the prompts
  python Tools/Art/Punk308/concept_punk308.py --run      # also runs codex exec for each (parallel)
  python Tools/Art/Punk308/concept_punk308.py --run p_relay   # only these

Writes Art/Punk308/Concepts/<id>.prompt.txt (+ .png with --run). Concepts only, never textures (D308-28)."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'SpellVFX308'))
import concept_reactions308 as base   # shared head, look and ban

OUT = base.REPO / 'Art/Punk308/Concepts'
REGION = base.REPO / 'Art/Presentation297/Stills/fin_region_hyeongang.png'                   # 현강 tone: cold grey-blue hills, misty lake
STEAM = base.REPO / 'Art/World/Temple308/Unity/Temple308K_Sun_Won_close_Flywheel.png'        # the temple steam kit already built (D308-43/46)

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a game screenshot of the 현강 region: use it for the ink-wash tone - cold grey-blue hills, pale mist, a still grey-green lake. Do not copy the user interface.
Picture 2 is the steam machinery the game already built for Buddhist temples: a riveted bronze-copper boiler drum, a copper pipe with a hand valve, an ornate cast flywheel with a gear rim, aged green patina. Use it ONLY for the material language of the temple machines (bronze, copper, patina, rivets) - never put it where the picture says there is no steam."""

STYLE = """--- THE LOOK ---
A game concept paint-over: desaturated, cold, like ink wash over a 3D scene, matching picture 1. Joseon things are white porcelain, onggi earthenware, yugi brass, black lacquer; old Goryeo things are bronze with a dull celadon-green patina (청자 비색 dulled in shadow) and dark wood. Water and mist are soft ink washes, wood and broken edges are dry brush. First-person, eye height about 1.6 m unless the picture says otherwise."""

PICTURES = {
    'p_ruin': ([REGION], """--- THE PICTURE: 현강 - a drowned temple workshop of old Goryeo, before steam ---
ONE single full frame, landscape. Low first-person view standing at knee-deep water's edge. Wide, shallow, still water and mist fill most of the frame; half-sunken palace roofs, a royal tomb mound and a broken stone bridge lie on the SAME water surface further away. In the middle ground, a half-drowned temple courtyard: a cracked stone terrace edge, a few tilted wooden pillars, an eave still standing over a narrow dry ledge.
In that courtyard, three things only:
1. A large bronze BELL MOULD (the outer casting mould of a temple bell, split open, its rounded shoulder and band of relief decoration clearly visible), half in the water, dull green patina.
2. A broken CRUCIBLE (thick clay-and-stone melting pot), cracked, tipped on its side.
3. Behind them, a stopped wooden WATERWHEEL connected by a wooden shaft to a box BELLOWS - the shaft snapped, the support frame collapsed into the water, so it obviously cannot simply be repaired.
Far away and small on a misty ridge, a faint pagoda silhouette may show. NOTHING with steam: no boiler, no copper pipes, no exposed gears, no chimney, no gauges. No floating lit lanterns (lanterns may float unlit). No text, no inscriptions."""),
    'p_relay': ([REGION, STEAM], """--- THE PICTURE: act 3 - a hidden cargo relay outside the capital, seen from the road ---
ONE single full frame, landscape. First-person from a dirt cart road at dusk-grey light, looking 15-20 m off the road into a small fenced yard behind a low earthen wall with a broken gate - an outskirts cargo relay of the capital (Joseon tiled-roof storehouse, stacked crates and straw-tied bundles, an ox-cart without the ox). It is visible from the road but you would have to step off the road to enter.
In the yard, two things stand APART on separate stands, not connected by any pipe or cable:
1. A SMALL TRANSPORT ENGINE on a low wheeled carrying frame: one riveted bronze vessel, a short copper pipe ending in a nozzle that can swing between two stops left and right, an exposed swing-arm and pivot near knee height, two fan-shaped scuffed wear marks on the ground in front of the two nozzle positions. Rough, smaller and cruder than a temple machine (picture 2 is the material only). A thin short puff of grey-white vapour just leaving the nozzle, darker at the base, thinning out quickly - never bright.
2. A JOSEON MASEOK LAMP pried off an official building: a white porcelain and black-lacquer lamp housing with a brass frame, its broken official mounting bracket still attached, lashed with rope to a separate wooden post. Its light is OFF (no glow at all). The lamp housing shows a dark smudge where a stroke of ink once clung.
No people, no enemies in view. No temple roofs, no pagoda, no Buddhist symbols here. No text."""),
    'p_sumtong': ([STEAM], """--- THE PICTURE: the 숨통 (the steam vent of an engine) - a readability board ---
LAYOUT: ONE wide strip of THREE equal panels side by side, the SAME first-person camera 4 m from the same small engine (a riveted bronze vessel on a low frame, a short copper pipe ending in a swing nozzle, an exposed swing-arm and pivot near knee height, two stop pins left and right), the same dull stone-and-earth ground, thin paper-coloured gaps.
Panel 1 (idle): nozzle resting at the LEFT stop, nothing coming out, the two fan-shaped wear marks on the ground in front of the left and right stop positions.
Panel 2 (wind-up, 0.8 s before the blast): the nozzle twitches and its collar lifts, a pressure flap on the vessel rattles open a little, a thin wisp of vapour leaks at the joint - the warning is shown only by moving PARTS, nothing lights up.
Panel 3 (blast, 1.2 s): a straight, narrow cone of vapour shoots from the nozzle along the left wear mark, about 4 m long, drawn as soft ink-wash that is DARKER and denser at the nozzle and thins and pales to transparent at the tip, cool grey (blue at least as strong as red), never white, never brighter than the ground. Ground beneath the cone looks slightly damp.
Same everything else in all three panels. No glow anywhere, no gauges lit, no text."""),
    'p_lamp': ([REGION], """--- THE PICTURE: act 1 - a Joseon maseok lamp hung out over a cliff edge ---
ONE single full frame, landscape. First-person on a rocky forest ledge (cold green-grey woodland, mist below). An official post at the cliff edge - a short timber gantry with a Joseon tiled cap and a brass bracket - holds a MASEOK LAMP hanging out over the drop on an iron arm: white porcelain body, black-lacquer frame, a soft warm paper-window glow inside (the ONLY light in the picture, dim, like a paper lantern, no rays).
Clinging to the outside of the lamp's lower half is an ENEMY's ink: a flat, filled, wet-looking dark green-black smear without an outline (an enemy spell residue), visibly sitting on the lamp, not on the ground. Below the lamp there is NO ground - only mist and a far valley - so it is obvious nothing could have been put there except on the lamp.
At the cliff edge, a little behind the lamp, a faint dark disc of the same green-black wash lies on the ground right under where the lamp's reach ends (the area the hung residue reaches).
A gaunt enemy ink-caster (ragged Joseon-era robe, rope sash, one hand raised) stands 10 m away on the ledge, having just thrown it. No text."""),
}

# round 2 (debate 29 concept verdict 1, Docs/Research/debate29/c1_verdict.md): every picture read as desaturated photo-real PBR
INK = """--- INK-WASH FINISH (the most important rule, round 1 failed it) ---
Paint it as a Korean ink-wash game paint-over, NOT a photograph and NOT a glossy 3D render: large soft wash planes, bleeding edges, dry broken brush strokes on near edges, faint paper grain, simple flat shading like picture 1. Far things lose their outline in the wash. No photographic micro-detail, no specular highlights, no wet glossy reflections, no lens effects. Mist, water, sky and vapour are never brighter than the stone and earth in the same picture. Copper and bronze are a dull, low-saturation green patina, never orange."""

ENGINE2 = ("a SMALL, crude transport engine: ONE squat bronze vessel whose rounded shoulder curves like the shoulder of a temple bell "
           "(but it is not a bell, plain, no relief, no monster face), ONE short straight copper pipe ending in a collar nozzle on a visible pivot that can swing "
           "between two stop pins left and right, an exposed swing-arm near knee height, a pressure flap on the vessel; NO flywheel, NO long pipework; "
           "its wheels have been taken off and its low carrying frame rests bolted on timber blocks so it cannot move")

PICTURES.update({
    'p_ruin2': ([REGION], """--- THE PICTURE: 현강 - a drowned temple workshop of old Goryeo, before steam (second version) ---
ONE single full frame, landscape. Low first-person view at knee-deep water's edge. Wide, shallow, still water and mist fill most of the frame; half-sunken palace roofs, the stone base of a royal tomb mound and the piers of a broken stone bridge all stand in the SAME water at the SAME water level further away (the tomb is not on a dry hill).
In the middle ground, a half-drowned temple courtyard with a cracked stone terrace edge; any eave or pillars in the foreground are small, soft and dark like a dry-brush silhouette, not the focus.
Three things only:
1. A large split clay CASTING MOULD for a temple bell: thick rammed clay and earth in visible layered seams, hollow inside, the inner face showing the faint negative impression of decoration, only a rough bronze-stained lip where metal once ran. NOT a finished bell: no metal skin, no studs, no raised lotus band.
2. A broken thick clay-and-stone CRUCIBLE tipped on its side.
3. The wreck of a wooden WATERWHEEL and box BELLOWS: most spokes missing, the rim tilted and sunk, the shaft snapped, the support frame and floor boards collapsed into the water, the bellows box smashed open - plainly beyond repair.
Far away and small on a misty ridge, a faint pagoda silhouette may show. NOTHING with steam: no boiler, no copper pipes, no exposed gears, no chimney, no gauges. Lanterns, if any, float unlit. No text, no inscriptions."""),
    'p_relay2': ([REGION, STEAM], """--- THE PICTURE: act 3 - a hidden cargo relay outside the capital, seen from the road (second version) ---
ONE single full frame, landscape. First-person from a dirt cart road in grey overcast light, looking 15-20 m off the road through a broken gate in a low earthen wall into a small yard - an outskirts cargo relay of the capital (plain Joseon tiled-roof storehouse with no decoration, stacked crates, straw-tied bundles, an ox-cart without the ox). Visible from the road; entering means stepping off it.
In the yard, two things stand APART on separate supports, not connected by any pipe, rope or cable:
1. """ + ENGINE2 + """. In front of it, two separate fan-shaped patches of scuffed bare earth, DARKER than the ground around, with no sharp edges or symbols, one under each nozzle stop. A short, sparse puff of grey vapour leaves the nozzle, as dark as the ground or darker, turning transparent within a metre.
2. A Joseon MASEOK LAMP pried off an official building: an opaque white porcelain body in a black-lacquer frame with small paper-window panels, a snapped iron official bracket stub and empty nail holes on its back plate, lashed with rope to a separate wooden post. Its light is OFF. A dark ink smudge stains the porcelain.
No people. No temple roofs, no pagoda, no Buddhist symbols. No text."""),
    'p_sumtong2': ([REGION, STEAM], """--- THE PICTURE: the 숨통 (the steam vent of an engine) - a readability board (second version) ---
LAYOUT: ONE wide strip of THREE equal panels side by side with thin paper-coloured gaps. Camera, engine position, background and ground are EXACTLY identical in all three; only the moving parts and the vapour change.
The setting is the yard of a plain outskirts cargo relay (earthen wall, crates, grey overcast), first-person 4 m from """ + ENGINE2 + """. On the ground in front, two irregular scuffed patches of bare earth with exposed small stones, slightly darker and drier than the surrounding ground - NOT shapes, NOT wedges, NOT arrows, never paler than the ground.
Panel 1 (idle): the nozzle rests against the LEFT stop pin. No vapour.
Panel 2 (wind-up, 0.8 s before): NO vapour at all. The swing-arm has visibly rotated about 15 degrees off its stop pin, the nozzle collar is lifted, the pressure flap on the vessel hangs open - readable from the silhouette of the parts alone.
Panel 3 (venting): a straight, narrow cone of cool grey vapour about 4 m long streams out of the nozzle over the left patch, darkest and densest at the nozzle mouth, thinning to transparent at the tip; blue at least as strong as red; never white, never brighter than the ground; no lingering opaque cloud.
No glow anywhere, no text."""),
    'p_lamp2': ([REGION], """--- THE PICTURE: act 1 - a Joseon maseok lamp hung out over a cliff edge (second version) ---
ONE single full frame, landscape. First-person on a rocky forest ledge, cold green-grey woodland, mist below; sparse dark lichen only, sky and mist not brighter than the rock.
An official post at the cliff edge - a short timber gantry with a small Joseon tiled cap and a brass bracket - holds a MASEOK LAMP hanging out over the drop on an iron arm. The lamp is an OPAQUE WHITE PORCELAIN body set in a BLACK-LACQUER frame with small paper-window panels: no glass, no transparency, no metal cage. Behind the paper only a faint, dim warmth - it does not light up anything around it, no halo, no rays.
On the OUTSIDE of the lamp's lower porcelain clings an enemy's ink: a flat, filled, wet-looking dark green-black patch with soft bleeding edges and NO outline, two or three thin runs of ink trickling down the porcelain and dripping off into the empty air below. Directly below the lamp there is NO ground - only mist and a far valley. Where the drop line passes the cliff lip, a few stray ink drops have spattered the rock edge; there is NO separate disc or pool on the ground.
A gaunt enemy ink-caster stands 10 m away on the ledge, one hand still raised: a tattered Joseon-era robe with a rope sash, a growth of dull crystal on one shoulder, ribs showing through the torn cloth, uneven proportions, a twisted joint, an iron shackle on one wrist; nothing on him glows. No text."""),
})


# round 3 (c2_verdict.md): one engine for relay and vent board, never a cannon; vapour near ground value; values keyed to p_ruin2
RUIN2 = OUT / 'p_ruin2.png'
RELAY3 = OUT / 'p_relay3.png'
ENGINE3 = ("a SMALL, crude transport engine: ONE upright bronze vessel shaped like a squat dome whose rounded shoulder curves like the shoulder of a temple bell "
           "(plain, no relief, no face), dull low-saturation green patina; from its side ONE thin short copper pipe ending in a FLARED trumpet-like swivel collar "
           "on a visible pivot that swings between two stop pins left and right; an exposed swing-arm near knee height; a small pressure flap on the dome. "
           "It must NOT look like a cannon or a gun: no thick barrel, no breech block, no muzzle, no horizontal cylinder. No flywheel, no long pipework. "
           "No wheels: its low carrying frame rests bolted on timber blocks")
VAPOUR = ("Any vapour is cool grey and translucent, within about 10 percent of the ground's brightness - never black soot or gunpowder smoke, never a white or bright cloud")
WEAR = ("two wide fan-shaped patches of scraped bare earth on the ground in front of the left and right nozzle stops, only slightly darker than the ground around, "
        "plain dirt colour, soft irregular edges - no ash, no black centre, no purple tint, no closed shapes, no arrows")
TONE3 = "\nThe LAST attached picture (an earlier accepted concept, the drowned workshop) sets the brightness and value structure: match its mid-grey key, its soft ink-wash planes and how dark mist and water sit against the stone."

PICTURES.update({
    'p_relay3': ([REGION, STEAM, RUIN2], """--- THE PICTURE: act 3 - a hidden cargo relay outside the capital, seen from the road (third version) ---
ONE single full frame, landscape. First-person from a dirt cart road in grey overcast light, looking 15-20 m off the road through a broken gate in a low earthen wall into a small yard - an outskirts cargo relay (plain Joseon tiled-roof storehouse, stacked crates, straw-tied bundles, an empty ox-cart). Beyond: low hills and a river; NO multi-storey pavilions, no temple-like building clusters on the horizon.
In the yard, two things stand APART on separate supports, not connected by any pipe, rope or cable:
1. """ + ENGINE3 + """. On the ground: """ + WEAR + """. A thin, short wisp leaves the collar. """ + VAPOUR + """.
2. A Joseon MASEOK LAMP pried off an official building: opaque white porcelain body in a black-lacquer WOODEN frame, small paper-window panels that look dark and unlit, a snapped iron bracket stub with a clean break and empty nail holes on its back plate, lashed with rope to a separate wooden post, a dark ink smudge on the porcelain. The porcelain is a soft grey-white, not the brightest thing in the picture.
No people. No Buddhist symbols. No text.""" + TONE3),
    'p_sumtong3': ([REGION, RELAY3, RUIN2], """--- THE PICTURE: the 숨통 (the steam vent of the engine) - a readability board (third version) ---
Picture 2 shows the exact engine to draw: the same dome vessel, the same thin pipe with the flared swivel collar, the same frame on timber blocks. Draw THAT engine, not a new one.
LAYOUT: ONE wide strip of THREE equal panels with thin paper-coloured gaps. Camera, engine, crates, stones, wall and ground are EXACTLY identical in all three - copy panel 1 into panels 2 and 3 and change only the moving parts and the vapour.
Setting: the yard of the plain outskirts cargo relay (earthen wall, a few crates), first-person 4 m from """ + ENGINE3 + """. On the ground: """ + WEAR + """.
Panel 1 (idle): the collar rests against the LEFT stop pin, pointing over the left patch. No vapour.
Panel 2 (wind-up): NO vapour at all. The swing-arm has clearly rotated about 15 degrees off its stop pin, the flared collar is lifted, the pressure flap on the dome hangs open - three changes readable from silhouette alone.
Panel 3 (venting): from the collar, still pointing LEFT, a straight narrow cone of vapour (half-angle under 10 degrees) about 4 m long runs over the left patch, slightly denser near the collar and fading to fully transparent at the tip. """ + VAPOUR + """.
No glow anywhere, no text.""" + TONE3),
    'p_lamp3': ([REGION, RUIN2], """--- THE PICTURE: act 1 - a Joseon maseok lamp hung out over a cliff edge (third version) ---
ONE single full frame, landscape. First-person on a rocky forest ledge, cold green-grey woodland, mist below. Lichen sparse and as dark as the rock; sky and mist no brighter than the rock.
An official Joseon post at the cliff edge: two square timber posts under a small Joseon tiled cap with a STRAIGHT ridge and LEVEL eaves (definitely not a Japanese torii, no upswept curved beam, no shrine look), from which ONE plain straight iron arm reaches out over the drop. From it hangs a MASEOK LAMP: an opaque white porcelain body (soft grey-white, not glaring) in a black-lacquer wooden frame, topped by a simple LOW hipped lacquer cap (no pagoda roof, no hexagonal tower), with only SMALL paper-window panels showing a very faint dim warmth that lights nothing around it.
On the OUTSIDE of the lamp's lower porcelain clings an enemy's ink: a flat, filled, wet-looking dark green-black patch with soft bleeding edges and no outline, two thin runs trickling down the porcelain, and just two or three drops falling into the empty air below. Directly below the lamp there is NO ground - only mist and a far valley. Only a few stray ink spots on the cliff lip; no disc or pool on the ground.
A gaunt enemy ink-caster stands 10 m away on the ledge, one hand still raised: tattered Joseon-era robe with a rope sash, a dull crystal growth on one shoulder, ribs showing through torn cloth, uneven proportions, a twisted joint, an iron shackle on one wrist; nothing glows. No text.""" + TONE3),
})


def prompt(pid):
    refs, body = PICTURES[pid]
    ref = '\n'.join(REF.splitlines()[:2] if len(refs) == 1 and refs[0] == REGION else
                    ([REF.splitlines()[0], REF.splitlines()[2].replace('Picture 2', 'Picture 1')] if refs == [STEAM] else REF.splitlines()))
    if pid in ('p_sumtong3', 'p_lamp3'):   # picture 2 / the last picture are explained in the body
        ref = '\n'.join(REF.splitlines()[:2])
    style = [STYLE, INK] if pid[-1] in '23' else [STYLE]
    return '\n\n'.join([base.HEAD, ref, *style, body, base.BAN, base.TAIL % (OUT / f'{pid}.png').as_posix()])


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
