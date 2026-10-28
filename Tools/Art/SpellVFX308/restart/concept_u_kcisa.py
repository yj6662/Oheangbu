#!/usr/bin/env python3
"""D308-153 side study (2026-10-29, the user: "실제 물과 같은 효과가 아닌, kisca 문양 에셋을 이용한 술식일 경우의 시안도 한번 뽑아줘"):
concepts of 우 (water ring) drawn with the KCISA traditional pattern pack (Assets/KoreanTraditionalPattern_Effect) instead of realistic water.

  python Tools/Art/SpellVFX308/restart/concept_u_kcisa.py            # writes the prompts
  python Tools/Art/SpellVFX308/restart/concept_u_kcisa.py --run      # codex exec per variant (parallel), recovers from ~/.codex/generated_images

Pictures: the real R0 game frame, the caster/brush, the chosen water concept u_a (composition only), and the pattern sheet
Art/SpellVFX308/Restart/kcisa/ref_water_patterns.png (patterns 204 92 226 247 / 58 232 198 155).
Out: Art/SpellVFX308/Restart/kcisa/concepts/<id>.png. Concepts only (D308-28: commercial terms unconfirmed = TEST)."""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OUT = REPO / 'Art/SpellVFX308/Restart/kcisa/concepts'
GEN = Path.home() / '.codex' / 'generated_images'
GAME = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/r0/frames/R0_bridge_Wood_red/f_00000.jpg'
BRUSH = REPO / 'Art/SpellVFX308/Concepts/weapon_forms/ref_brush.png'
UA = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/concepts/u_a.png'
PAT = REPO / 'Art/SpellVFX308/Restart/kcisa/ref_water_patterns.png'

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE ATTACHED PICTURES ---
Picture 1 is a REAL frame of the game at its real gameplay camera: third-person, low, right behind the caster's back (the caster stands at the bottom centre, back to us, holding the great brush), a flat dark red-brown earth ground (the region 적로), a pale paper-beige misty sky, and one real enemy (a dark matted bear with crusted crystal growths on its back) about 2 metres ahead and a little to the right - exactly on the ring that the spell strikes. Paint the concept ON THIS VIEW: keep this camera height, angle and framing (you may raise the camera a little so the ring around the caster reads), the same ground, sky and bear. Ignore any small coloured blob in picture 1 (capture artefacts). The scene keeps its muted Korean ink-wash painted look.
Picture 2 is the player character: a scholar-mage in a worn grey-brown hanbok-like coat, messy dark curly hair, holding a great calligraphy brush (black lacquered staff about 1.25 m, pale bone ferrule, black bristle head, small red tassel). Keep that character and that brush.
Picture 3 is the director's chosen concept for this spell in a REALISTIC style: a ring of breaking water around the caster. Keep its COMPOSITION, scale and timing (a ring about 2 m around the caster, waist-to-head high at the front, the bear on the ring hit and drenched), but NOT its realistic water.
Picture 4 is a sheet of traditional Korean decorative patterns (line-art ornaments from a Korean cultural pattern archive): overlapping wave scales (청해파) in a crescent, a cloud-scroll roundel, a three-armed whirl, a swirl roundel with teardrop petals, a ring of curling waves, a cloud scroll (운문), a fish over wave scales, and a cloud. These patterns are the MATERIAL of the spell."""

STYLE = """--- THE LOOK ---
This spell is NOT realistic water. It is water made of TRADITIONAL KOREAN PATTERN: the shapes, lines and motifs of picture 4 become the substance of the spell - fine clean line-art ornaments (white and pale blue-green lines, deep indigo-blue fills, touches of black ink) that build up into three-dimensional forms with depth, overlapping layers and motion. It must still read at a glance as WATER rising in a CIRCLE around the caster and crashing on the bear. Glow is allowed on the pattern lines (a soft cool light), but the world stays a muted Korean ink-wash painting. Elegant, ceremonial, crafted, like a living decorative screen or a temple painting come alive - not a cartoon, not a cheap hologram.
NOT in the picture: no readable text, letters, numbers, labels, Chinese characters or calligraphy, no arrows, UI, HUD, frame, signature or watermark. Not a flat decal on the ground only - the patterns must rise into the air with volume."""

SPELL = """--- THE PICTURE ---
THE SPELL 우 (WATER ring): ONE instant strike on a hollow circular band about 2 metres around the caster (a ring, not a filled disc - the caster stands in the empty middle, untouched); the bear standing on the band is struck and DRENCHED (soaked for 4 seconds). It must read at a glance as a CIRCLE around the caster (the near half behind the camera may be cut off, the front half and the sides must show). Show THE INSTANT OF THE STRIKE."""

VARIANTS = {
    'uk_a': "VARIANT A - pattern wave ring: the ring is a circular breaking WAVE built entirely of the wave-scale pattern (청해파): hundreds of nested arc-scales stacked into a rolling wall that rises out of the ground all around the caster and curls over at its crest into the curling-wave scrolls of picture 4, the crests breaking into small swirl roundels and cloud-scroll spray; the line-art scales are white and pale blue on deep indigo, a few black ink strokes sweep through it; the bear on the ring is swallowed by the curling pattern wave, pattern droplets and loose arc-scales burst off it.",
    'uk_b': "VARIANT B - pattern whirl ring: on the ground around the caster a large circular ornament of the patterns flashes open (a ring of curling waves with wave-scales between, like picture 4's roundels, 4 m wide), and from that circle a whirling wall of pattern rises - three-armed whirls, swirl roundels and cloud scrolls spinning around the caster like a vortex of carved ornament, layered in depth, white and pale blue-green line art on translucent indigo; where it meets the bear the pattern bursts into a crown of wave-scale spray and cloud scrolls and the bear is drenched.",
}

TAIL = """3. Save the image unchanged (PNG) as: %s
4. Print the saved file path and stop."""

PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def prompt(v):
    return '\n\n'.join([HEAD, REF, STYLE, SPELL + '\n' + VARIANTS[v], TAIL % (OUT / f'{v}.png')])


def recover(v):
    dst = OUT / f'{v}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{v}.log.txt'
    cand = [GEN / m.group(1) / m.group(2) for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace'))] if log.exists() else []
    cand = [p for p in cand if p.exists()]
    if cand:      # only a path this variant's own log printed (never the newest-file guess)
        dst.write_bytes(cand[-1].read_bytes()); print('RECOVERED', v, cand[-1]); return True
    return False


def main():
    vids = [v for v in VARIANTS if not [a for a in sys.argv[1:] if not a.startswith('--')] or v in sys.argv]
    OUT.mkdir(parents=True, exist_ok=True)
    for v in vids:
        (OUT / f'{v}.prompt.txt').write_text(prompt(v), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', vids); return
    procs = []
    for v in vids:
        if recover(v): continue
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT), '-i', str(GAME), '-i', str(BRUSH), '-i', str(UA), '-i', str(PAT)]
        log = open(OUT / f'{v}.log.txt', 'w', encoding='utf-8')
        procs.append((v, subprocess.Popen(cmd, stdin=open(OUT / f'{v}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
        time.sleep(2)
    for v, p in procs:
        try: p.wait(timeout=1500)
        except subprocess.TimeoutExpired: p.kill()
        print('DONE', v, p.returncode, recover(v), flush=True)


if __name__ == '__main__':
    main()
