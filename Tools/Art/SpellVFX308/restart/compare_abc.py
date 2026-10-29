"""D308-153: the 우 comparison - concept u_a | A (Blender Cycles cards) | B (concept cards) | C (live in-engine), same scene, same camera.
  python Tools/Art/SpellVFX308/restart/compare_abc.py [letter=우]
Reads Art/SpellVFX308/Restart/shots/<letter>_<A|B|C>/f_*.jpg; writes Art/SpellVFX308/Restart/compare/<letter>_abc.mp4 (1x, then 0.5x) and <letter>_abc.jpg
(three moments of each variant under the concept)."""
import glob, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

LETTER = sys.argv[1] if len(sys.argv) > 1 else '우'
ROOT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart'
SHOTS = os.path.join(ROOT, 'shots')
OUT = os.path.join(ROOT, 'compare'); os.makedirs(OUT, exist_ok=True)
FF = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
CONCEPT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Vol2Vfx\free_showcase\batch2\concepts\u_a.png'
NAMES = {'A': 'A Blender 렌더', 'B': 'B 시안 그림', 'C': 'C 엔진 실시간'}
font = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 26)
small = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 20)

frames = {v: sorted(glob.glob(os.path.join(SHOTS, LETTER + '_' + v, 'f_*.jpg'))) for v in 'ABC'}
have = [v for v in 'ABC' if frames[v]]
if not have: sys.exit('no shots under ' + SHOTS)
n = min(len(frames[v]) for v in have)
W, H = 640, 360

# ---- still: concept on top, three moments per variant ----
moms = (0.15, 0.3, 0.5)
sheet = Image.new('RGB', (W * 3 + 16, 40 + H + 40 + (H + 40) * len(have)), (18, 18, 18)); d = ImageDraw.Draw(sheet)
d.text((10, 6), '시안 u_a', font=font, fill=(255, 220, 0))
c = Image.open(CONCEPT).convert('RGB'); c = c.resize((W, int(c.height * W / c.width)))
sheet.paste(c, (W + 8, 40))
y = 40 + H + 40
for v in have:
    d.text((10, y - 34), NAMES[v], font=font, fill=(255, 220, 0))
    for i, m in enumerate(moms):
        im = Image.open(frames[v][int(n * m)]).convert('RGB').resize((W, H)); sheet.paste(im, (i * (W + 8), y))
        d.text((i * (W + 8) + 8, y + 6), '%.2f s' % (int(n * m) / 60.0), font=small, fill=(255, 255, 255))
    y += H + 40
sheet.save(os.path.join(OUT, LETTER + '_abc.jpg'), quality=88)

# ---- video: the variants side by side at 1x, then at 0.5x ----
tmp = os.path.join(OUT, '_tmp'); os.makedirs(tmp, exist_ok=True)
for f in glob.glob(os.path.join(tmp, '*.jpg')): os.remove(f)
for i in range(n):
    row = Image.new('RGB', (W * len(have), H + 40), (0, 0, 0)); dd = ImageDraw.Draw(row)
    for k, v in enumerate(have):
        row.paste(Image.open(frames[v][i]).convert('RGB').resize((W, H)), (k * W, 40))
        dd.text((k * W + 10, 6), NAMES[v], font=font, fill=(255, 220, 0))
    row.save(os.path.join(tmp, 'r_%05d.jpg' % i), quality=90)
side, slow = os.path.join(OUT, '_1x.mp4'), os.path.join(OUT, '_05x.mp4')
for fr, out in ((60, side), (30, slow)):
    subprocess.run([FF, '-y', '-loglevel', 'error', '-framerate', str(fr), '-i', os.path.join(tmp, 'r_%05d.jpg'), '-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p',
                    '-r', '30', '-c:v', 'libx264', '-crf', '21', out], check=True)
lst = os.path.join(OUT, '_list.txt')
open(lst, 'w').write("file '%s'\nfile '%s'\n" % (side.replace('\\', '/'), slow.replace('\\', '/')))
final = os.path.join(OUT, LETTER + '_abc.mp4')
subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c', 'copy', final], check=True)
for p in (side, slow, lst): os.remove(p)
print(final, '%.1f MB' % (os.path.getsize(final) / 1e6), 'variants', have, 'frames', n)
