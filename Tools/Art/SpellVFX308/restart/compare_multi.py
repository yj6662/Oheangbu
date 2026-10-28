"""D308-153 overnight comparison (2026-10-29): several variants of one glyph side by side - a still sheet (concept on top, each variant at three moments)
and an mp4 grid (1x then 0.5x).
  python Tools/Art/SpellVFX308/restart/compare_multi.py <out name> <shot folder>=<label> [...] [--concept <png>] [--video <mp4 shown as the first cell>]
Shot folders are under Art/SpellVFX308/Restart/shots (60 fps jpg frames)."""
import glob, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart'
FF = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
argv = sys.argv[1:]
concept = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart\kcisa\concepts\uks_1.png'
video = None
if '--concept' in argv: i = argv.index('--concept'); concept = argv[i + 1]; del argv[i:i + 2]
if '--video' in argv: i = argv.index('--video'); video = argv[i + 1]; del argv[i:i + 2]
name, pairs = argv[0], [a.split('=', 1) for a in argv[1:]]
W, H = 640, 360
font = ImageFont.truetype(r'C:\Windows\Fonts\malgunbd.ttf', 26)
out = os.path.join(ROOT, 'compare'); os.makedirs(out, exist_ok=True)
frames = {lab: sorted(glob.glob(os.path.join(ROOT, 'shots', d, 'f_*.jpg'))) for d, lab in pairs}
frames = {k: v for k, v in frames.items() if v}


def label(im, text):
    d = ImageDraw.Draw(im); d.rectangle((0, 0, 14 + int(d.textlength(text, font=font)), 38), fill=(0, 0, 0)); d.text((7, 3), text, font=font, fill=(255, 220, 0))


# still sheet: concept + one row per variant at 15 / 30 / 50 %
rows = list(frames.items())
sheet = Image.new('RGB', (W * 3 + 16, (H + 8) * (len(rows) + 1)), (18, 18, 18))
c = Image.open(concept).convert('RGB').resize((W, H)); label(c, '시안'); sheet.paste(c, (W + 8, 0))
for r, (lab, fs) in enumerate(rows):
    for i, m in enumerate((0.15, 0.3, 0.5)):
        im = Image.open(fs[int(len(fs) * m)]).convert('RGB').resize((W, H))
        if i == 0: label(im, lab)
        sheet.paste(im, (i * (W + 8), (r + 1) * (H + 8)))
sheet.save(os.path.join(out, name + '.jpg'), quality=86)

# mp4 grid: 3 columns, the concept video (optional) first
cells = ([('AI 영상(원본)', None)] if video else []) + rows
cols = 3; nrow = (len(cells) + cols - 1) // cols
n = min(len(fs) for _, fs in rows)
vid_frames = []
if video:
    tmpv = os.path.join(out, '_v'); os.makedirs(tmpv, exist_ok=True)
    for f in glob.glob(os.path.join(tmpv, '*.jpg')): os.remove(f)
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', video, '-vf', 'fps=60,scale=%d:%d' % (W, H), os.path.join(tmpv, 'v_%05d.jpg')], check=True)
    vid_frames = sorted(glob.glob(os.path.join(tmpv, 'v_*.jpg')))
tmp = os.path.join(out, '_grid'); os.makedirs(tmp, exist_ok=True)
for f in glob.glob(os.path.join(tmp, '*.jpg')): os.remove(f)
for i in range(n):
    g = Image.new('RGB', (W * cols, H * nrow), (18, 18, 18))
    for k, (lab, fs) in enumerate(cells):
        if fs is None:
            if not vid_frames: continue
            im = Image.open(vid_frames[min(i, len(vid_frames) - 1)]).convert('RGB')
        else:
            im = Image.open(fs[i]).convert('RGB').resize((W, H))
        label(im, lab); g.paste(im, ((k % cols) * W, (k // cols) * H))
    g.save(os.path.join(tmp, 'g_%05d.jpg' % i), quality=88)
parts = []
for fr, tag in ((60, '_1x'), (30, '_05x')):
    p = os.path.join(out, tag + '.mp4'); parts.append(p)
    subprocess.run([FF, '-y', '-loglevel', 'error', '-framerate', str(fr), '-i', os.path.join(tmp, 'g_%05d.jpg'), '-vf', 'format=yuv420p', '-r', '30', '-c:v', 'libx264', '-crf', '22', p], check=True)
lst = os.path.join(out, '_list.txt'); open(lst, 'w').write(''.join("file '%s'\n" % p.replace('\\', '/') for p in parts))
final = os.path.join(out, name + '.mp4')
subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c', 'copy', final], check=True)
for p in parts + [lst]: os.remove(p)
print(final, '%.1f MB' % (os.path.getsize(final) / 1e6), 'variants', list(frames), 'frames', n)
