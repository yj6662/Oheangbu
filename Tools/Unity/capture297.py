"""#297 batch captures through the editor queue (finish297 view:). Same eye/target set before and after a pass.

python Tools/Unity/capture297.py <set> <stage>     e.g.  lighting before | lighting after | cave after
Sets: 'lighting' = the 21 #296 review cameras + early clusters; 'cave' = relocated mine + mouth + reveal.
Images land in Art/World/Compact/Rebuild/Finish297/Views/<stage>/<name>.png; a side-by-side sheet is written
by `python Tools/Unity/capture297.py sheet <set>` once both stages exist.
"""
import json, sys, shutil
from pathlib import Path
from playtest_polish import call

ROOT = Path(__file__).resolve().parents[2]
F297 = ROOT / 'Art/World/Compact/Rebuild/Finish297'
A296 = ROOT / 'Art/World/Compact/Rebuild/Architecture296/cameras.json'
EXTRA = [('inn-approach', (3130, 140, 2300), (3070, 130, 2335)), ('village-eye', (2790, 218.8, 2110), (2765, 219, 2145)),
         ('capital-center', (2080, 110, 2880), (2000, 68, 2960)), ('cave-gallery', (3551, 126.2, 1778), (3515, 126.4, 1800))]


def views(name):
    if name == 'lighting':
        v = json.loads(A296.read_text(encoding='utf-8-sig'))['Views']
        out = [(f"{i:02d}-{x['Id']}", (x['Eye']['x'], x['Eye']['y'], x['Eye']['z']), (x['Target']['x'], x['Target']['y'], x['Target']['z'])) for i, x in enumerate(v)]
        return out + [(n, e, t) for n, e, t in EXTRA]
    if name == 'cave':
        rel = json.loads((F297 / 'Cave/relocation.json').read_text(encoding='utf-8'))
        mx, my, mz = rel['mouth']; ox, oz = rel['outward']; sx, sy, sz = rel['start']
        return [('cave-start', (sx, sy + 1.7, sz), (sx + oz * 12, sy + 1.5, sz - ox * 12)),
                ('mouth-inside', (mx - ox * 22, my + 1.7, mz - oz * 22), (mx + ox * 10, my + 1.6, mz + oz * 10)),
                ('mouth-outside', (mx + ox * 16 + oz * 5, my + 3.0, mz + oz * 16 - ox * 5), (mx, my + 2.5, mz)),
                ('mouth-high', (mx + ox * 30, my + 22, mz + oz * 30), (mx, my + 3, mz)),
                ('reveal-overlook', (mx + ox * 4, my + 1.75, mz + oz * 4), (3070, 131, 2337)),
                ('reveal-ridge', (3300, 181, 2228), (3070, 131, 2337))]
    raise SystemExit('unknown set ' + name)


def fmt(v): return ','.join(f'{c:.2f}' for c in v)


if __name__ == '__main__':
    if sys.argv[1] == 'sheet':
        from PIL import Image, ImageDraw
        name = sys.argv[2]; rows = []; sa, sb = (sys.argv[3], sys.argv[4]) if len(sys.argv) > 4 else ('before', 'after')
        for n, _, _ in views(name):
            a, b = F297 / 'Views' / sa / f'{n}.png', F297 / 'Views' / sb / f'{n}.png'
            if a.exists() and b.exists(): rows.append((n, a, b))
        W, H = 640, 360; sheet = Image.new('RGB', (W * 2, (H + 18) * len(rows)), 'white'); d = ImageDraw.Draw(sheet)
        for i, (n, a, b) in enumerate(rows):
            for k, p in enumerate((a, b)):
                sheet.paste(Image.open(p).convert('RGB').resize((W, H)), (k * W, i * (H + 18) + 18))
            d.text((4, i * (H + 18) + 3), f'{n}   {sa} | {sb}', fill='black')
        out = F297 / f'compare-{name}-{sb}.jpg'; sheet.save(out, quality=85); print(out); sys.exit(0)
    name, stage = sys.argv[1], sys.argv[2]
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None
    dst = F297 / 'Views' / stage; dst.mkdir(parents=True, exist_ok=True)
    for n, e, t in views(name):
        if only and n not in only: continue
        call('Oheangbu.EditorTools.WorldMacro.CompactFinish297', 'Run', f'view:{n}:{fmt(e)}:{fmt(t)}', 300)
        src = F297 / 'Views' / f'{n}.png'
        if src.exists(): shutil.move(str(src), str(dst / f'{n}.png'))
    print('captured', len(views(name)), 'views ->', dst)
