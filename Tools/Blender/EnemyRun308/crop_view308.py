#!/usr/bin/env python3
"""#308 enemy run: cut a run sheet into readable pieces for review (work files only, under run/work/crops).
  python Tools/Blender/EnemyRun308/crop_view308.py <id> <view> [row] [parts]     row: 0 walk, 1 run, -1 both (default 1); parts default 2"""
import sys
from pathlib import Path
from PIL import Image

RUN = Path(__file__).resolve().parents[3] / 'Art/Characters308/DokkaebiVerify/run'
mid, view = sys.argv[1], sys.argv[2]; row = int(sys.argv[3]) if len(sys.argv) > 3 else 1; parts = int(sys.argv[4]) if len(sys.argv) > 4 else 2
img = Image.open(RUN / ('%s_run_%s.jpg' % (mid, view))); w, h = img.size
top, tile_h, lw = 60, 344, 170
y0, y1 = (top, h) if row < 0 else (top + row * tile_h, top + (row + 1) * tile_h)
out = RUN / 'work' / 'crops'; out.mkdir(parents=True, exist_ok=True)
step = (w - lw) / parts
for i in range(parts):
    box = (int(lw + i * step), y0, int(lw + (i + 1) * step), y1); p = out / ('%s_%s_r%d_p%d.jpg' % (mid, view, row, i)); img.crop(box).save(p, quality=90); print(p)
