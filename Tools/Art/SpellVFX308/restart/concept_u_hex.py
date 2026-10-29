#!/usr/bin/env python3
"""D308-153 우 concept, route H (2026-10-29, the user: "단순한 문양형태로 만드는 건? 반투명 홀로그램 육각형 막에 문양이 박혀있는 모습."):
a translucent hexagon-cell membrane round the caster with KCISA patterns set into the cells. Two shapes: hx_1 dome, hx_2 ring wall.
Codex built-in image generation (gpt-6-astra) on the real game frame + the KCISA water-pattern sheet. Concept TEST (D308-28).
  python Tools/Art/SpellVFX308/restart/concept_u_hex.py          # prompts only
  python Tools/Art/SpellVFX308/restart/concept_u_hex.py --run    # codex exec per variant (parallel)
Out: Art/SpellVFX308/Restart/kcisa/concepts/hx_*.png"""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OUT = REPO / 'Art/SpellVFX308/Restart/kcisa/concepts'
GEN = Path.home() / '.codex' / 'generated_images'
GAME = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/batch2/r0/frames/R0_bridge_Wood_red/f_00000.jpg'
PAT = REPO / 'Art/SpellVFX308/Restart/kcisa/ref_water_patterns.png'

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image (landscape 16:9). If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw it with code. Do not download anything. Do not read, change, move or delete any other file.
2. Picture 1 is a frame of our game (Korean ink-wash look, third-person camera behind the caster, a robed Taoist with a big brush; red-brown ground, cream paper sky). Keep that camera, that caster and that ground. Picture 2 is a sheet of traditional Korean water patterns (KCISA public-domain motifs: seigaiha-like wave scales, curling wave lines).
3. THE SPELL "우" (rain / water ward): """
LOOK = """
THE LOOK: SIMPLE. A thin, translucent membrane made of hexagonal cells (honeycomb), like a hologram shield, but in our palette: deep indigo / blue-grey glass, cream-white cell edges as fine brush-ink lines, a soft pale glow on the edges only (no neon cyan, no sci-fi HUD text). Each hex cell has ONE water pattern from picture 2 set into it as a faint cream line drawing (seigaiha scales, a curling wave) - pattern inside the cells, not on top. The caster and the ground stay clearly visible through the membrane. A few cells near the ground still forming (outlines only), a few flickering brighter. No realistic water, no splashes, no particles clouds, no text, no UI."""
VARIANTS = {
    'hx_1': "a low DOME of hex cells over the caster (about 3 m radius, reaching about 2.5 m high), its rim sitting on the ground in a circle.",
    'hx_2': "a RING WALL of hex cells standing round the caster (about 3 m radius, about 2 m high, open at the top), slightly curving inward at the top edge like a short cylinder.",
}
TAIL = """
4. Save the image unchanged (PNG) as: %s
5. Print the saved file path and stop."""
PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(v):
    dst = OUT / f'{v}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = OUT / f'{v}.log.txt'
    cand = [GEN / m.group(1) / m.group(2) for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace'))] if log.exists() else []
    cand = [p for p in cand if p.exists()]
    if cand:
        dst.write_bytes(cand[-1].read_bytes()); return True
    return False


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for v, what in VARIANTS.items():
        (OUT / f'{v}.prompt.txt').write_text(HEAD + what + LOOK + TAIL % (OUT / f'{v}.png'), encoding='utf-8')
    if '--run' not in sys.argv:
        print('prompts written', list(VARIANTS)); return
    procs = []
    for v in VARIANTS:
        if recover(v):
            continue
        cmd = ['codex', 'exec', '-m', 'gpt-6-astra', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(OUT), '-i', str(GAME), '-i', str(PAT)]
        log = open(OUT / f'{v}.log.txt', 'w', encoding='utf-8')
        procs.append((v, subprocess.Popen(cmd, stdin=open(OUT / f'{v}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
        time.sleep(2)
    for v, p in procs:
        try: p.wait(timeout=1500)
        except subprocess.TimeoutExpired: p.kill()
        print('DONE', v, p.returncode, recover(v), flush=True)


if __name__ == '__main__':
    main()
