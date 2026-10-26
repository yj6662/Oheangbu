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


def prompt_b1(vid):
    spell, var = B1_VARIANTS[vid]
    tmpl, text = B1_SPELLS[spell]
    return '\n\n'.join([HEAD, B1_REF[tmpl] + '\n' + B1_REF_TAIL, STYLE, '--- THE PICTURE ---\n' + text + '\n' + var, TAIL % (B1_OUT / f'{vid}.png')])


def prompt(vid):
    if vid in B1_VARIANTS:
        return prompt_b1(vid)
    spell, var = VARIANTS[vid]
    return '\n\n'.join([HEAD, REF, STYLE, '--- THE PICTURE ---\n' + SPELLS[spell] + '\n' + var, TAIL % (OUT / f'{vid}.png')])


PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def out_of(vid):
    return B1_OUT if vid in B1_VARIANTS else OUT


def game_of(vid):
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
    pool = B1_VARIANTS if batch == 'b1' else VARIANTS
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
