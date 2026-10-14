"""#308 6th pass: the swing ink in PLAY (steps sw_j) - one sheet from the held-Play shots.

  python Tools/Art/SpellVFX308/review_swingplay308.py [run label, default sw_j]

Rows: sweep 나 / arc 노 / slam 곰 / plant 거 / draw 걱. Columns: three casts, shot at a later moment each (see the steps
file for the delays). Each cell is the middle of the 2560x1440 shot (the player and what is in front of them).
Output: Art/SpellVFX308/Review/swingplay_<label>.png
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
SHOTS = ROOT / 'Art/Playtest306/Checks'


def main():
    label = sys.argv[1] if len(sys.argv) > 1 else 'sw_j'
    states = ['sweep', 'arc', 'slam', 'plant', 'draw']
    sheet = Image.new('RGB', (640 * 3, 400 * len(states)), (20, 20, 20))
    missing = []
    for y, state in enumerate(states):
        for x in range(3):
            path = SHOTS / f'{label}_{state}_{x}_2560x1440.png'
            if not path.exists():
                missing.append(path.name)
                continue
            im = Image.open(path).convert('RGB')
            w, h = im.size
            sheet.paste(im.crop((int(w * .2), int(h * .22), int(w * .8), int(h * .97))).resize((640, 400)), (x * 640, y * 400))
    out = ROOT / f'Art/SpellVFX308/Review/swingplay_{label}.png'
    sheet.save(out)
    print(out)
    if missing:
        print('missing:', ', '.join(missing))


if __name__ == '__main__':
    main()
