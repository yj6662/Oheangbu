#!/usr/bin/env python3
"""A frame batch at a glance (2026-10-30, the user's answer: "틀 하나(6자)마다 영상 보내고 계속"): up to 6 glyphs' lab shots as one 3 x 2 grid mp4
(640 x 360 cells, the glyph and its score labelled) and a still sheet of the same grid at the bite.
  python Tools/Art/SpellVFX308/restart/g308_grid.py <out name> <글자> [<글자> ...]
Shots: Art/SpellVFX308/Restart/shots/<글자>_A/f_*.jpg (60 fps). Scores: Tools/Art/SpellVFX308/spell_catalog_status.json. Out: Art/SpellVFX308/Restart/compare/<name>.mp4 · .jpg"""
import json, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
SHOTS = REPO / 'Art/SpellVFX308/Restart/shots'
OUT = REPO / 'Art/SpellVFX308/Restart/compare'
TMP = OUT / '_grid'
FF = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
FONT = ImageFont.truetype(r'C:\Windows\Fonts\malgunbd.ttf', 26)
CW, CH = 640, 360


def main():
    name, letters = sys.argv[1], sys.argv[2:11]
    led = json.loads((REPO / 'Tools/Art/SpellVFX308/spell_catalog_status.json').read_text(encoding='utf-8'))
    # a cell may name its variant ("고_F", the ㅗ membrane test): that shot folder, and no ledger score (the ledger scores variant A)
    frames = [sorted((SHOTS / (g if '_' in g else f'{g}_A')).glob('f_*.jpg')) for g in letters]
    n = max(len(f) for f in frames)
    TMP.mkdir(parents=True, exist_ok=True)
    for old in TMP.glob('g_*.jpg'):
        old.unlink()
    cols = 3; rows = (len(letters) + 2) // 3
    for k in range(n):
        sheet = Image.new('RGB', (CW * cols, CH * rows), (20, 18, 16))
        for i, (g, fs) in enumerate(zip(letters, frames)):
            if not fs:
                continue
            im = Image.open(fs[min(k, len(fs) - 1)]).convert('RGB').resize((CW, CH))
            d = ImageDraw.Draw(im)
            sc = None if '_' in g else led.get(g, {}).get('score')
            label = g + (f'  {sc}' if sc is not None else '')
            d.rectangle((0, 0, 14 + int(d.textlength(label, font=FONT)), 38), fill=(0, 0, 0)); d.text((7, 3), label, font=FONT, fill=(255, 220, 0))
            sheet.paste(im, ((i % cols) * CW, (i // cols) * CH))
        sheet.save(TMP / f'g_{k:05d}.jpg', quality=85)
        if k == int(n * 0.25):
            sheet.save(OUT / f'{name}.jpg', quality=86)
    subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-y', '-framerate', '60', '-i', str(TMP / 'g_%05d.jpg'), '-c:v', 'libx264', '-crf', '25',
                    '-preset', 'medium', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(OUT / f'{name}.mp4')], check=True)
    print(OUT / f'{name}.mp4')


if __name__ == '__main__':
    main()
