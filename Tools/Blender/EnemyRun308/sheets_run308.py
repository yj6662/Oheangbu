#!/usr/bin/env python3
"""#308 enemy run: sheets of the built run next to the repaired walk played at the speed it would need, from the posed vertices
enemyrun308.py dumped (run/work/<id>_run.npz). Plain Python: the numpy z-buffer rasteriser of Tools/Blender/EnemyRig308/sheets308.py
(imported, not copied) + PIL. NO GPU, no Blender, no Unity.

  python Tools/resource_guard.py --wait
  python Tools/Blender/EnemyRun308/sheets_run308.py [ids...] [--tag <name>]      default ids: agwi changgui
Writes Art/Characters308/DokkaebiVerify/run/<id>_run_front|side|back.jpg (+ _half.jpg); with --tag also a copy under run/iterations/<tag>/.
Both rows share one clock: neighbouring tiles are the same fraction of a second apart, so the walk row shows how fast the walk has
to step to carry the run's speed. Side view: the short ticks under the ground line are fixed points of the ground (they scroll back
at the travel speed) - a planted foot stays on its tick from tile to tile.
These are Blender-side poses in flat colours (body grey, own-left arm red, own-right arm blue): no texture, no game light, no Unity
humanoid retarget [O Blender / I game]."""
import sys, json, shutil
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent; sys.path.insert(0, str(HERE.parent / 'EnemyRig308'))
import sheets308 as S

ROOT = HERE.parents[2]
RUN = ROOT / 'Art/Characters308/DokkaebiVerify/run'; WORK = RUN / 'work'
KR = S.KR; TILE = (250, 340); TICK = 0.5


def ticks(img, view, height, travel):
    """Ground-fixed marks every TICK metres, scrolled by the distance travelled (side view only)."""
    if view != 'side': return img
    w, h = img.size; scale = h / (height * 1.30); gy = int(round(h / 2 + height * .5 * scale)); d = ImageDraw.Draw(img)
    y = (travel % TICK) - 4 * TICK
    while y < 4 * TICK:
        x = int(round(y * scale + w / 2))
        if 2 <= x < w - 2: d.line([(x, gy + 2), (x, gy + 9)], fill=(60, 60, 60), width=2)
        y += TICK
    return img


def save(img, name, tag):
    img.save(RUN / name, quality=90); half = img.resize((img.width // 2, img.height // 2), Image.LANCZOS); half.save(RUN / name.replace('.jpg', '_half.jpg'), quality=88)
    if tag:
        d = RUN / 'iterations' / tag; d.mkdir(parents=True, exist_ok=True); half.save(d / name.replace('.jpg', '_half.jpg'), quality=85)
    print('wrote', name, img.size, flush=True)


def run(mid, tag):
    j = json.loads((RUN / ('%s_run.json' % mid)).read_text(encoding='utf-8')); z = np.load(WORK / ('%s_run.npz' % mid))
    H = float(z['height']); tris = z['tris']; tt = S.TINT[z['labels'][tris][:, 0]]; secs = z['tile_seconds']; v = float(z['clip_speed']); rate = float(z['walk_rate'])
    r = j['run']; w = j['walk_at_run_speed']; key = '%.2f' % v
    rows_def = [('walk', '걷기 수리본을\n%.1f m/s에 맞춰 틀 때\n%.2f배속 · 분당 %.0f걸음' % (v, rate, w['by_actor_speed'][key]['steps_per_minute'])),
                ('run', '달리기 run308\n1.00배속 · 분당 %.0f걸음\n체공 %d프레임 / %d' % (r['steps_per_minute_at_clip_rate'], r['flight_frames_by_height'], r['cycle_frames']))]
    top = ['%.2f s' % t for t in secs]
    for view in ('front', 'side', 'back'):
        rows = [[ticks(S.raster(z[k][i].astype(np.float64), tris, tt, view, H, TILE), view, H, v * float(secs[i])) for i in range(len(secs))] for k, _ in rows_def]
        title = '%s 달리기 — %s  |  숙임 %.0f° → %.0f°, 출렁임 %.1f → %.1f cm, 내딛는 발목 올림 %.0f → %.0f cm, 무릎 최대 %.0f → %.0f°  [O] Blender, 게임 화면 아님' % (
            KR[mid], S.VIEW_KR[view], w['lean_deg'][1], r['lean_deg'][1], w['bounce_cm'], r['bounce_cm'], max(w['ankle_lift_cm'].values()), max(r['feet'][s]['ankle_lift_cm'] for s in ('Left', 'Right')),
            max(w['knee'][s][1] for s in ('Left', 'Right')), max(r['feet'][s]['knee_max_deg'] for s in ('Left', 'Right')))
        save(S.grid(rows, top, [l for _, l in rows_def], title, lw=170), '%s_run_%s.jpg' % (mid, view), tag)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace'); a = sys.argv[1:]; tag = a[a.index('--tag') + 1] if '--tag' in a else None
    ids = [x for i, x in enumerate(a) if not x.startswith('--') and (i == 0 or a[i - 1] != '--tag')]
    for mid in (ids or ['agwi', 'changgui']): run(mid, tag)
