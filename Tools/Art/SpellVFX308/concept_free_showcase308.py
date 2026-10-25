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


def prompt(vid):
    spell, var = VARIANTS[vid]
    return '\n\n'.join([HEAD, REF, STYLE, '--- THE PICTURE ---\n' + SPELLS[spell] + '\n' + var, TAIL % (OUT / f'{vid}.png')])


PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(vid, started):
    dst = OUT / f'{vid}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{vid}.log.txt'
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
    OUT.mkdir(parents=True, exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith('--')]
    vids = [v for v in VARIANTS if not only or v in only]
    for v in vids:
        (OUT / f'{v}.prompt.txt').write_text(prompt(v), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', vids)
        return
    pending = vids
    for attempt in range(1, 3):
        procs = []
        for v in pending:
            cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT), '-i', str(GAME), '-i', str(BRUSH), '-i', str(BURST)]
            log = open(OUT / f'{v}.log.txt', 'w', encoding='utf-8')
            procs.append((v, time.time(), subprocess.Popen(cmd, stdin=open(OUT / f'{v}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
            time.sleep(2)
        failed = []
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
