#!/usr/bin/env python3
"""D308-153 variant I (2026-10-29 overnight, the user: "… recraft, gpt image gen 등 다양한 방법으로 시도해보고"): isolated painted wave elements in the
concept uks_1's own hand, made with Codex's built-in image generation (GPT image), on flat chroma green, then keyed to RGBA.

  python Tools/Art/SpellVFX308/restart/u_sprites_gpt.py            # prompts only
  python Tools/Art/SpellVFX308/restart/u_sprites_gpt.py --run      # codex exec per element (parallel) + key
  python Tools/Art/SpellVFX308/restart/u_sprites_gpt.py --key      # key the raw images again

Out: Art/SpellVFX308/Restart/sprites/I/raw/<el>.png (green) -> sprites/I/<el>.png (RGBA). Concept TEST (D308-28)."""
import re, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OUT = REPO / 'Art/SpellVFX308/Restart/sprites/I'
RAW = OUT / 'raw'
GEN = Path.home() / '.codex' / 'generated_images'
CONCEPT = REPO / 'Art/SpellVFX308/Restart/kcisa/concepts/uks_1.png'
STRIP = REPO / 'Art/SpellVFX308/Restart/ai_video/u_kling_1_strip.jpg'

HEAD = """You are being called non-interactively to produce ONE image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw it with code. Do not download anything. Do not read, change, move or delete any other file.
2. Picture 1 is a game spell concept: a ring of painted Korean/Japanese woodblock-style waves around a caster. Picture 2 shows the same waves animated. Paint ONE ISOLATED ELEMENT of those waves in EXACTLY that painting style: deep indigo and blue-grey water, thin white 청해파 (seigaiha) scale lines on the wave bodies, cream/off-white outlines and foam, black ink accents, slightly textured painted paper feel. No caster, no bear, no ground, no sky, no shadow on the ground.
3. BACKGROUND: perfectly flat pure chroma green (#00FF00) everywhere around the element, no gradient, no texture, no shadow, no green spill on the element, and the element must not contain any green. Leave a margin of green on all sides; the element must not touch the image edges.
4. No text, letters, signatures or watermarks."""

ELEMENTS = {
    'mound': ("landscape 3:2", "A WIDE CLUSTER of rounded wave mounds seen from the side: 5 to 7 overlapping rounded swells of indigo water, each covered in concentric white seigaiha scale arcs with a cream outline, the front ones bigger, forming a low wavy band - like the front row of waves at the bottom of picture 1."),
    'crest': ("portrait 2:3", "ONE TALL BREAKING WAVE seen from the side: an indigo wave body with white seigaiha scale lines rising up and its crest hooking over sideways into a big curl, with cream foam claws/fingers at the curl's lip, a few cream foam droplets, like the big wave crashing over the bear at the right of picture 1."),
    'claw': ("square 1:1", "ONE SMALL FOAM CURL: a single curling wave tip / foam claw, cream and white foam fingers around a small spiral curl of indigo water, like the hooked tips of the wave crests in picture 1."),
    'ink': ("landscape 3:1 (very wide)", "ONE LONG BLACK INK BRUSH STROKE sweeping horizontally, wet head on the left trailing into dry-brush streaks on the right, slightly curved, like the black ink strokes sweeping around the ring in picture 1."),
    'splash': ("square 1:1", "A SMALL BURST of water splash: cream/white foam droplets and a few indigo water drops with seigaiha-style curls, flying outward, painted in the same woodblock style."),
}


def prompt(el):
    shape, what = ELEMENTS[el]
    return HEAD + "\n5. Image shape: " + shape + ". The element: " + what + "\n6. Save the image unchanged (PNG) as: %s\n7. Print the saved file path and stop." % (RAW / f'{el}.png')


PNG_RE = re.compile(r'generated_images[\\/]+([0-9a-f-]+)[\\/]+([^\\/\'"\s]+?\.png)', re.I)


def recover(el):
    dst = RAW / f'{el}.png'
    if dst.exists() and dst.stat().st_size > 10000:
        return True
    log = RAW / f'{el}.log.txt'
    cand = [GEN / m.group(1) / m.group(2) for m in PNG_RE.finditer(log.read_text(encoding='utf-8', errors='replace'))] if log.exists() else []
    cand = [p for p in cand if p.exists()]
    if cand:
        dst.write_bytes(cand[-1].read_bytes()); return True
    return False


def key(el):
    import numpy as np
    from PIL import Image, ImageFilter
    src = RAW / f'{el}.png'
    if not src.exists():
        return False
    im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    green = g - np.maximum(r, b)                       # >0 where green dominates
    a = 1 - np.clip((green - 0.12) / 0.25, 0, 1)       # soft key
    am = Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    # despill: clamp green to max(r, b) where the key is partial
    g2 = np.minimum(g, np.maximum(r, b) + 0.02)
    rgb = np.stack([r, g2, b], -1)
    out = Image.fromarray((rgb * 255).astype(np.uint8)).convert('RGBA'); out.putalpha(am)
    bbox = am.point(lambda v: 255 if v > 8 else 0).getbbox()
    if bbox:
        out = out.crop(bbox)
    out.save(OUT / f'{el}.png')
    return True


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    els = [e for e in ELEMENTS if not [a for a in sys.argv[1:] if not a.startswith('--')] or e in sys.argv]
    for e in els:
        (RAW / f'{e}.prompt.txt').write_text(prompt(e), encoding='utf-8')
    if '--run' in sys.argv:
        procs = []
        for e in els:
            if recover(e):
                continue
            cmd = ['codex', 'exec', '-m', 'gpt-6-astra', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(RAW), '-i', str(CONCEPT), '-i', str(STRIP)]
            log = open(RAW / f'{e}.log.txt', 'w', encoding='utf-8')
            procs.append((e, subprocess.Popen(cmd, stdin=open(RAW / f'{e}.prompt.txt', 'rb'), stdout=log, stderr=subprocess.STDOUT, shell=True)))
            time.sleep(2)
        for e, p in procs:
            try: p.wait(timeout=1500)
            except subprocess.TimeoutExpired: p.kill()
            print('DONE', e, p.returncode, recover(e), flush=True)
    if '--run' in sys.argv or '--key' in sys.argv:
        for e in els:
            print('key', e, key(e))
    else:
        print('prompts written', els)


if __name__ == '__main__':
    main()
