#!/usr/bin/env python3
"""#308 concepts for the 120-letter redo with the 노 method (D308-91; user 2026-10-16 "'노'를 제외한 술식(가, 나, 사, 마, 아 포함)도
codex의 image-gen으로 시안 먼저 띄워봐").

  python Tools/Art/SpellVFX308/concept_redo120.py --run singles/ga_a ...    # one or more items
  python Tools/Art/SpellVFX308/concept_redo120.py --all [-j 6]              # every spell file without a png yet
  python Tools/Art/SpellVFX308/concept_redo120.py --all --rev r2            # revised blocks <id>.r2.spell.txt -> <id>.r2.png
  python Tools/Art/SpellVFX308/concept_redo120.py --board                   # per-group boards + overview (Korean labels)

Items live in Art/SpellVFX308/Concepts/redo120/<group>/<id>.spell.txt:
    TITLE: 가 · 목 단발 (안 A)          label on the board
    ELEMENT: wood|fire|earth|metal|water
    TIMES: 0.1, 0.4, 1.0               optional, the three panel times in seconds
    PIGMENT: ...                        optional, replaces the element pigment line (letters with their own pigment)
    CAMERA: ...                         optional, replaces the camera sentence
    GROWTH: ...                         optional, replaces the 0.4 s growth sentence (paths, summons, fields)
    SCENE: ...                          optional, replaces the enemy sentence (fields have no enemy, DECISIONS #72)
    ---
    free text: what the spell does in each panel
Each prompt goes to `codex exec` (built-in image generation) with two stills attached: the scene still (ref_intended_soot.png) and the
approved 노 21st pass from the lab (ref_na21_approved.png). The pictures are concepts only, never textures (D308-28: commercial terms
unconfirmed = TEST). A missing png is retried; the result of every item goes to <id>[.rev].result.json. When codex makes the picture but its
own copy step fails, the newest file it names under ~/.codex/generated_images is copied in ('rescued')."""
import json, re, shutil, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ROOT = REPO / 'Art/SpellVFX308/Concepts/redo120'
REFS = [REPO / 'Art/SpellVFX308/Concepts/na_smoke/ref_intended_soot.png', ROOT / 'ref_na21_approved.png']

HEAD = """You are being called non-interactively to produce ONE concept image file. Do exactly this and nothing else.
1. Use ONLY your built-in image generation tool to create ONE image. If you do not have it in this session, print the single line IMAGE_TOOL_UNAVAILABLE and stop. Do NOT draw or synthesize the picture with code (no Python, PIL, SVG, canvas). Do not download anything. Do not read, change, move or delete any other file.
2. Image request: landscape, 16:9, the tool's native landscape size (do not upscale, crop or resize it afterwards)."""

REF = """--- THE TWO ATTACHED PICTURES ---
Picture 1 is a game screenshot: use it for the camera, the setting and the tone. Do not copy the user interface.
Picture 2 is a test-room screenshot of the approved fire spell 노 - a band of red-ink flame tongues on a plain grey test floor. Use it for HOW a spell is painted in this game (matte pigment ink, split-brush edges, real-looking motion, no light), not for its colour or shape unless the spell below says so. Ignore the small white text, the plain grey test floor and the dark blob figure in the foreground of picture 2."""

CAMERA = "Camera behind and slightly above the caster's right shoulder, as in picture 1."

SCENE = """--- THE SHOT (same in every panel) ---
A game VFX concept paint-over in the style of the game's screenshots: a wide stone-paved plaza of a Joseon-era Korean palace town, warm grey-beige ground, dark tiled roofs and a distant palace gate, hazy hills, pale warm paper-coloured sky; everything slightly desaturated, warm, like ink wash over a 3D scene. %s The caster is a scholar-mage in a dark worn hanbok-like coat with messy dark hair who fights with a huge calligraphy brush on a long staff. %s"""

ENEMY = "One standing figure about 6 metres ahead is the enemy (a plain dark figure)."

GROWTH = "It starts small, close to the caster, and reaches its full size about 0.4 s after the cast; nearer parts appear first, farther parts after."

METHOD = """--- HOW EVERY SPELL IN THIS GAME IS PAINTED (picture 2 is the approved example) ---
- Painted, not lit: the element's matter has the SHAPE and MOTION of the real thing, but it is matte pigment ink laid on the scene like paint on paper. Every edge breaks up like a dry calligraphy brush (split-bristle streaks, 갈필), East Asian ink painting.
- Ink first, colour second: each part appears first as soot-black ink and the element's pigment soaks into it a moment later.
- %s
- Nothing lights the ground or the figures. A little warm grey ink haze may hang above it.
- When it is gone, only a very faint soft-edged ink stain stays on the ground where it struck (barely visible, no hard border) - never a line along its path."""

PIGMENT = {
    'wood': "Colour: Wood (목) - living green mineral pigment #426E58, darker #2C4A3B in dense parts, swallowed by soot-black ink #2A2622 at the edges; matte.",
    'fire': "Colour: Fire (화) - vermilion / cinnabar red pigment #BE543B, deep red-brown #6E2A1F in dense parts, swallowed by soot-black ink #2A2622 at the edges, warm grey-brown soot #3E362C to #867C6B above.",
    'earth': "Colour: Earth (토) - ochre / raw-umber earth pigment #977343, dark umber #5E4728 in dense parts, swallowed by soot-black ink #2A2622 at the edges; dry and dusty, matte.",
    'metal': "Colour: Metal (금) - pale cool grey pigment #A6B0AD, darker grey #747B79 in dense parts, like ground tin or lead-white paint, swallowed by soot-black ink #2A2622 at the edges; matte - NOT shiny chrome, no specular glints, no bright white.",
    'water': "Colour: Water (수) - deep indigo blue-grey pigment #2C465A, thinning to a pale grey-blue wash where thin, swallowed by soot-black ink #2A2622 at the edges; matte - never bright cyan, never glassy.",
}

STRIP = """--- LAYOUT ---
The image is ONE wide strip of three equal panels side by side, left to right, separated by thin plain paper-coloured gaps, all with the identical camera: panel 1 = %s s after the cast, panel 2 = %s s, panel 3 = %s s. The same spell develops across the three panels."""

BAN = """--- NOT IN THE PICTURE ---
No glow, no bloom, no light cast on the ground or the figures, no bright white or yellow core, no emissive anything, no sparks, no light rays, no lens flare, no neon or saturated cyan / magenta. No lasting line, trail or road mark drawn along the ground. No text, numbers, labels, arrows, readable calligraphy characters, seal stamp, signature, watermark or frame. No HUD, no health bars, no user interface."""

TAIL = """3. Save the image unchanged (PNG) as: %s
4. As your final message print exactly four lines: the absolute path of the saved file; its pixel size WxH; the generation time in seconds; the tool / model name. Print nothing else."""


def parse(path):
    head, _, body = path.read_text(encoding='utf-8-sig').partition('\n---\n')
    meta = {}
    for line in head.splitlines():
        if ':' in line:
            k, v = line.split(':', 1)
            meta[k.strip().upper()] = v.strip()
    if not body.strip():
        raise ValueError('%s: no body after ---' % path)
    if meta.get('ELEMENT') not in PIGMENT:
        raise ValueError('%s: ELEMENT must be one of %s' % (path, '/'.join(PIGMENT)))
    return meta, body.strip()


def stem(item, rev):
    return item + ('.' + rev if rev else '')


def build(item, rev):
    group, iid = item.split('/')
    spell = ROOT / group / (stem(iid, rev) + '.spell.txt')
    meta, body = parse(spell)
    times = [t.strip() for t in meta.get('TIMES', '0.1, 0.4, 1.0').split(',')]
    if len(times) != 3:
        raise ValueError('%s: TIMES needs three values' % spell)
    png = ROOT / group / (stem(iid, rev) + '.png')
    parts = [HEAD, REF, SCENE % (meta.get('CAMERA', CAMERA), meta.get('SCENE', ENEMY)), METHOD % meta.get('GROWTH', GROWTH), meta.get('PIGMENT') or PIGMENT[meta['ELEMENT']],
             STRIP % tuple(times), '--- THE SPELL ---\n' + body, BAN, TAIL % png.as_posix()]
    prompt = ROOT / group / (stem(iid, rev) + '.prompt.txt')
    prompt.write_text('\n\n'.join(parts), encoding='utf-8')
    return prompt, png


def kill_tree(p):
    subprocess.run(['taskkill', '/T', '/F', '/PID', str(p.pid)], capture_output=True)


GENERATED = re.compile(r'[A-Za-z]:[\\/][^\s"\']*?\.codex[\\/]+generated_images[\\/]+[^\s"\']+?\.png')


def rescue(text, png, t0):
    """codex sometimes makes the picture but cannot run its own copy command; take the newest picture it names."""
    found = []
    for m in GENERATED.findall(text):
        p = Path(m.replace('\\\\', '\\'))
        if p.exists() and p.stat().st_mtime >= t0 - 5 and p.stat().st_size > 10000:
            found.append(p)
    if not found:
        return None
    src = max(found, key=lambda p: p.stat().st_mtime)
    shutil.copyfile(src, png)
    return src


def run_one(item, rev, tries, timeout):
    group, iid = item.split('/')
    out = ROOT / group
    prompt, png = build(item, rev)
    res = {'item': item, 'rev': rev, 'ok': False, 'tries': 0, 'secs': [], 'note': ''}
    for k in range(1, tries + 1):
        res['tries'] = k
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'workspace-write', '-C', str(out)]
        for r in REFS:
            cmd += ['-i', str(r)]
        logp = out / ('%s.log%d.txt' % (stem(iid, rev), k))
        t0 = time.time()
        with open(logp, 'w', encoding='utf-8') as log, open(prompt, 'rb') as inp:
            p = subprocess.Popen(cmd, stdin=inp, stdout=log, stderr=subprocess.STDOUT, shell=True)
            try:
                p.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                kill_tree(p)
                res['note'] = 'timeout try %d' % k
        res['secs'].append(round(time.time() - t0))
        text = logp.read_text(encoding='utf-8', errors='replace')
        if not (png.exists() and png.stat().st_size > 10000):
            src = rescue(text, png, t0)
            if src:
                res['rescued_from'] = src.as_posix()
        if not png.exists() and any(l.strip() == 'IMAGE_TOOL_UNAVAILABLE' for l in text.splitlines()[-5:]):
            res['note'] = 'IMAGE_TOOL_UNAVAILABLE try %d' % k
            continue  # mostly network drops (10054); the next try often works
        if png.exists() and png.stat().st_size > 10000:
            try:
                from PIL import Image
                with Image.open(png) as im:
                    res['size'] = '%dx%d' % im.size
                res['ok'] = True
                break
            except Exception as e:  # broken file: drop it and retry
                res['note'] = 'bad png: %s' % e
                png.unlink()
    (out / (stem(iid, rev) + '.result.json')).write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding='utf-8')
    print('DONE', json.dumps(res, ensure_ascii=False), flush=True)
    return res


def pending(rev):
    items = []
    for spell in sorted(ROOT.glob('*/*.spell.txt')):
        name = spell.name[:-len('.spell.txt')]
        if rev:
            if not name.endswith('.' + rev):
                continue
            iid = name[:-len(rev) - 1]
        else:
            if '.' in name:
                continue
            iid = name
        if not (spell.parent / (name + '.png')).exists():
            items.append(spell.parent.name + '/' + iid)
    return items


def arg(flag, default):
    return sys.argv[sys.argv.index(flag) + 1] if flag in sys.argv else default


def board():
    from PIL import Image, ImageDraw, ImageFont
    font = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 34)
    small = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 26)
    W, LABEL = 1400, 56
    thumbs = []
    for gdir in sorted(p for p in ROOT.iterdir() if p.is_dir()):
        rows = []
        for spell in sorted(gdir.glob('*.spell.txt')):
            name = spell.name[:-len('.spell.txt')]
            if '.' in name:
                continue
            meta, _ = parse(spell)
            rev = sorted(gdir.glob(name + '.r*.png'))
            png = rev[-1] if rev else gdir / (name + '.png')
            if not png.exists():
                continue
            title = meta.get('TITLE', name) + ('  (' + png.stem.split('.')[-1] + ')' if rev else '')
            im = Image.open(png).convert('RGB')
            im = im.resize((W, round(im.height * W / im.width)), Image.LANCZOS)
            rows.append((title, im))
            thumbs.append((gdir.name, title, im))
        if not rows:
            continue
        H = sum(LABEL + im.height for _, im in rows)
        sheet = Image.new('RGB', (W, H), (236, 230, 216))
        d = ImageDraw.Draw(sheet)
        y = 0
        for title, im in rows:
            d.text((12, y + 8), title, font=font, fill=(40, 34, 30))
            sheet.paste(im, (0, y + LABEL))
            y += LABEL + im.height
        sheet.save(gdir / ('board_%s.jpg' % gdir.name), quality=88)
        print('BOARD', gdir.name, len(rows))
    if thumbs:
        cols, tw = 2, 1000
        th = round(thumbs[0][2].height * tw / W)
        rows = (len(thumbs) + cols - 1) // cols
        ov = Image.new('RGB', (cols * tw, rows * (th + 40)), (236, 230, 216))
        d = ImageDraw.Draw(ov)
        for i, (g, title, im) in enumerate(thumbs):
            x, y = (i % cols) * tw, (i // cols) * (th + 40)
            d.text((8, y + 4), title, font=small, fill=(40, 34, 30))
            ov.paste(im.resize((tw - 6, th)), (x + 3, y + 40))
        ov.save(ROOT / 'overview_redo120.jpg', quality=85)
        print('OVERVIEW', len(thumbs))


def main():
    if '--board' in sys.argv:
        board()
        return
    rev = arg('--rev', '')
    tries, timeout, jobs = int(arg('--tries', '3')), int(arg('--timeout', '420')), int(arg('-j', '6'))
    if '--all' in sys.argv:
        items = pending(rev)
    else:
        i = sys.argv.index('--run')
        items = [a for a in sys.argv[i + 1:] if not a.startswith('-') and '/' in a]
    print('ITEMS', len(items), items, flush=True)
    with ThreadPoolExecutor(max_workers=jobs) as ex:
        results = list(ex.map(lambda it: run_one(it, rev, tries, timeout), items))
    bad = [r['item'] for r in results if not r['ok']]
    print('SUMMARY ok %d / %d, failed: %s' % (len(results) - len(bad), len(results), bad), flush=True)


if __name__ == '__main__':
    main()
