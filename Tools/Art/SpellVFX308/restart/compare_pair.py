"""D308-153: before | after of one variant (e.g. 우 A r3 | r4) plus the concept still.
  python Tools/Art/SpellVFX308/restart/compare_pair.py <before shot folder> <after shot folder> <out name> [<before label> <after label>]
Folders are under Art/SpellVFX308/Restart/shots; writes Art/SpellVFX308/Restart/compare/<out name>.mp4 (1x then 0.5x) and .jpg."""
import glob, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart'
FF = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
CONCEPT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Vol2Vfx\free_showcase\batch2\concepts\u_a.png'
b, a, name = sys.argv[1], sys.argv[2], sys.argv[3]
lb, la = (sys.argv[4], sys.argv[5]) if len(sys.argv) > 5 else ('이전', '이번')
fb = sorted(glob.glob(os.path.join(ROOT, 'shots', b, 'f_*.jpg'))); fa = sorted(glob.glob(os.path.join(ROOT, 'shots', a, 'f_*.jpg')))
n = min(len(fb), len(fa)); W, H = 640, 360
font = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 26)
out = os.path.join(ROOT, 'compare'); os.makedirs(out, exist_ok=True)
sheet = Image.new('RGB', (W * 3 + 16, (H + 40) * 3), (18, 18, 18)); d = ImageDraw.Draw(sheet)
d.text((10, 6), '시안', font=font, fill=(255, 220, 0)); c = Image.open(CONCEPT).convert('RGB'); sheet.paste(c.resize((W, int(c.height * W / c.width))), (W + 8, 40))
for r, (lab, fs) in enumerate(((lb, fb), (la, fa))):
    y = (H + 40) * (r + 1) + 40
    d.text((10, y - 34), lab, font=font, fill=(255, 220, 0))
    for i, m in enumerate((0.15, 0.3, 0.5)):
        sheet.paste(Image.open(fs[int(n * m)]).convert('RGB').resize((W, H)), (i * (W + 8), y))
sheet.save(os.path.join(out, name + '.jpg'), quality=88)
tmp = os.path.join(out, '_tmp'); os.makedirs(tmp, exist_ok=True)
for f in glob.glob(os.path.join(tmp, '*.jpg')): os.remove(f)
for i in range(n):
    row = Image.new('RGB', (W * 2, H + 40)); dd = ImageDraw.Draw(row)
    for k, (lab, fs) in enumerate(((lb, fb), (la, fa))):
        row.paste(Image.open(fs[i]).convert('RGB').resize((W, H)), (k * W, 40)); dd.text((k * W + 10, 6), lab, font=font, fill=(255, 220, 0))
    row.save(os.path.join(tmp, 'r_%05d.jpg' % i), quality=90)
parts = []
for fr, tag in ((60, '_1x'), (30, '_05x')):
    p = os.path.join(out, tag + '.mp4'); parts.append(p)
    subprocess.run([FF, '-y', '-loglevel', 'error', '-framerate', str(fr), '-i', os.path.join(tmp, 'r_%05d.jpg'), '-vf', 'format=yuv420p', '-r', '30', '-c:v', 'libx264', '-crf', '21', p], check=True)
lst = os.path.join(out, '_list.txt'); open(lst, 'w').write(''.join("file '%s'\n" % p.replace('\\', '/') for p in parts))
final = os.path.join(out, name + '.mp4')
subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c', 'copy', final], check=True)
for p in parts + [lst]: os.remove(p)
print(final, '%.1f MB' % (os.path.getsize(final) / 1e6))
